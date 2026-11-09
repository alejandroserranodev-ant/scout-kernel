namespace Scout.Core.Agents;

/// <summary>
/// Static, repository-agnostic files for a retrieval query skill that enables direct
/// queries against a Scout kernel's retrieval index. Scout-agents can invoke this
/// programmatically for efficient, token-optimized agent role execution.
/// </summary>
internal static class RetrievalQuerySkillArtifacts
{
    /// <summary>Path, relative to the kernel root, where every file below is published.</summary>
    internal const string RelativeDirectory = "skills/scout-query";

    internal const string RetrievalEngine = """
        // retrieval-engine.mjs — shared query logic for scout-query and scout-agents.
        // Reads the kernel's retrieval index (retrieval/index.db) with Node's built-in SQLite
        // (node:sqlite, Node 22.13+), so there is nothing to install. It ranks the way `scout query`
        // does: a TF-IDF dot product over the index's postings, nudged by each card's importance. A kernel built
        // with --embeddings also carries semantic vectors: when the model that made them answers on this machine,
        // the two rankings are fused; when it does not, the answer is lexical and says so. Nothing is required.

        import { DatabaseSync } from 'node:sqlite';

        const IMPORTANCE_INFLUENCE = 0.25;
        const MINIMUM_SIMILARITY = 0.01;
        const FUSION_OFFSET = 20;
        const FUSION_DEPTH = 50;

        // The names a card lists beside itself, without the qualified duplicate of its own name.
        function neighbours(hit) {
          return (hit.related ?? []).filter((name) => !name.includes('::'));
        }

        // Compact result format: ~30% smaller than verbose JSON. A line and the neighbours are added only when
        // the card has them: they are what lets a hit be followed to the next file without opening this one.
        export function compactResult(hit) {
          const out = { n: hit.name, k: hit.kind, p: hit.path, d: hit.detail, s: hit.score };
          if (hit.line) out.l = hit.line;
          const near = neighbours(hit);
          if (near.length > 0) out.r = near.slice(0, 6);
          return out;
        }

        // Verbose result format (CLI default)
        export function verboseResult(hit) {
          return {
            name: hit.name, kind: hit.kind, path: hit.path, line: hit.line ?? null, detail: hit.detail,
            summary: hit.summary, related: neighbours(hit), score: hit.score
          };
        }

        function parseJson(text, fallback) {
          try {
            return JSON.parse(text);
          } catch {
            return fallback;
          }
        }

        const CARD_COLUMNS = 'c.id, c.kind, c.name, c.relative_path AS path, c.detail, c.summary, c.facets, c.related, c.importance';

        function toHit(row, similarity) {
          return {
            id: row.id,
            name: row.name,
            kind: row.kind,
            path: row.path,
            detail: row.detail,
            summary: row.summary,
            line: parseJson(row.facets, []).find((facet) => facet.name === 'line')?.value ?? null,
            related: parseJson(row.related, []),
            importance: row.importance,
            similarity
          };
        }

        function byScoreThenId(a, b) {
          return b.score - a.score || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
        }

        // Accented letters fold to their base letter so "activacion" meets "Activación".
        function fold(text) {
          return text.normalize('NFD').replace(/\p{M}/gu, '');
        }

        // Splits identifiers the way the index was built: whole run, plus its camelCase and digit parts.
        export function tokenize(text) {
          const terms = [];
          const add = (candidate) => {
            if (candidate.length >= 2) terms.push(candidate.toLowerCase());
          };
          for (const run of fold(text ?? '').match(/[A-Za-z0-9]+/g) ?? []) {
            const parts = splitCasing(run);
            if (parts.length > 1) add(run);
            parts.forEach(add);
          }
          return terms;
        }

        function splitCasing(run) {
          const isLower = (c) => c >= 'a' && c <= 'z';
          const isUpper = (c) => c >= 'A' && c <= 'Z';
          const isDigit = (c) => c >= '0' && c <= '9';
          const parts = [];
          let start = 0;
          for (let i = 1; i < run.length; i++) {
            const previous = run[i - 1];
            const current = run[i];
            const boundary =
              (isLower(previous) && isUpper(current)) ||
              (isUpper(previous) && isUpper(current) && i + 1 < run.length && isLower(run[i + 1])) ||
              ((isLower(previous) || isUpper(previous)) && isDigit(current)) ||
              (isDigit(previous) && (isLower(current) || isUpper(current)));
            if (boundary) {
              parts.push(run.slice(start, i));
              start = i;
            }
          }
          parts.push(run.slice(start));
          return parts;
        }

        // The query as a unit vector over the index's vocabulary; terms the index never saw carry no weight.
        function queryVector(db, text) {
          const counts = new Map();
          for (const term of tokenize(text)) counts.set(term, (counts.get(term) ?? 0) + 1);

          const lookup = db.prepare('SELECT dimension, idf FROM vocabulary WHERE term = ?');
          const weights = [];
          for (const [term, count] of counts) {
            const row = lookup.get(term);
            if (row) weights.push([row.dimension, (1 + Math.log(count)) * row.idf]);
          }

          const length = Math.sqrt(weights.reduce((sum, [, weight]) => sum + weight * weight, 0));
          return length === 0 ? [] : weights.map(([dimension, weight]) => [dimension, weight / length]);
        }

        function search(db, text, limit, kind) {
          const vector = queryVector(db, text);
          if (vector.length === 0) return [];

          const terms = vector.map(() => '(?, ?)').join(', ');
          const rows = db.prepare(`
            WITH q(dimension, weight) AS (VALUES ${terms})
            SELECT ${CARD_COLUMNS},
                   SUM(p.weight * q.weight) AS similarity
            FROM postings p
            JOIN q ON q.dimension = p.dimension
            JOIN cards c ON c.rowid = p.card_rowid
            WHERE (? IS NULL OR c.kind = ?)
            GROUP BY p.card_rowid
          `).all(...vector.flat(), kind, kind);

          return rows
            .filter((row) => row.similarity >= MINIMUM_SIMILARITY)
            .map((row) => ({
              ...toHit(row, row.similarity),
              score: Number((row.similarity * (1 + IMPORTANCE_INFLUENCE * row.importance)).toFixed(3))
            }))
            .sort(byScoreThenId)
            .slice(0, limit);
        }

        function readMetadata(db) {
          const metadata = new Map();
          for (const row of db.prepare('SELECT key, value FROM index_metadata').all()) metadata.set(row.key, row.value);
          return metadata;
        }

        // OLLAMA_HOST is often a bare "host:port", sometimes "0.0.0.0", which says where a server listens.
        function ollamaEndpoint() {
          const host = (process.env.OLLAMA_HOST ?? '').trim();
          if (!host) return 'http://localhost:11434';
          const withScheme = host.includes('://') ? host : `http://${host}`;
          return withScheme.replace('//0.0.0.0', '//localhost').replace(/\/+$/, '');
        }

        // Embeds the query with the model that made the index's vectors, or throws with what is missing.
        async function embedQuery(modelId, text) {
          if (!modelId.startsWith('ollama:')) {
            throw new Error(`this index carries '${modelId}' semantic vectors, which this skill cannot embed a query for`);
          }
          const model = modelId.slice('ollama:'.length);
          let response;
          try {
            response = await fetch(`${ollamaEndpoint()}/api/embed`, {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ model, input: [text] }),
              signal: AbortSignal.timeout(60000)
            });
          } catch (e) {
            throw new Error(`Ollama is not reachable at ${ollamaEndpoint()} (${e.message})`);
          }
          if (response.status === 404) throw new Error(`Ollama does not have the model '${model}' (ollama pull ${model})`);
          if (!response.ok) throw new Error(`Ollama answered ${response.status} while embedding with '${model}'`);

          const vector = (await response.json()).embeddings?.[0];
          if (!Array.isArray(vector)) throw new Error('Ollama returned no vector');
          const length = Math.sqrt(vector.reduce((sum, value) => sum + value * value, 0)) || 1;
          return Float32Array.from(vector, (value) => value / length);
        }

        // A stored vector is a float32 scale followed by one signed byte per component.
        function semanticHits(db, kind, query) {
          const rows = db.prepare(`
            SELECT d.card_rowid AS card, d.vector AS vector
            FROM dense_vectors d JOIN cards c ON c.rowid = d.card_rowid
            WHERE (? IS NULL OR c.kind = ?)
          `).all(kind, kind);

          const scored = [];
          for (const row of rows) {
            const blob = row.vector;
            if (blob.byteLength !== 4 + query.length) {
              throw new Error('the vectors in this index do not have the dimension of the query model');
            }
            const view = new DataView(blob.buffer, blob.byteOffset, blob.byteLength);
            const scale = view.getFloat32(0, true);
            let dot = 0;
            for (let i = 0; i < query.length; i++) dot += view.getInt8(4 + i) * query[i];
            scored.push({ card: Number(row.card), similarity: dot * scale });
          }

          const best = scored.sort((a, b) => b.similarity - a.similarity || a.card - b.card).slice(0, FUSION_DEPTH);
          const byRowid = db.prepare(`SELECT ${CARD_COLUMNS} FROM cards c WHERE c.rowid = ?`);
          return best.map((entry) => toHit(byRowid.get(entry.card), entry.similarity));
        }

        // Rank fusion, the same as the compiler's: a card both rankings like rises above one only one liked.
        function fuse(lexical, semantic, limit) {
          const fused = new Map();
          lexical.slice(0, FUSION_DEPTH).forEach((hit, rank) => {
            fused.set(hit.id, { hit, fusion: 1 / (FUSION_OFFSET + rank + 1) });
          });
          semantic.slice(0, FUSION_DEPTH).forEach((hit, rank) => {
            const share = 1 / (FUSION_OFFSET + rank + 1);
            const existing = fused.get(hit.id);
            if (existing) existing.fusion += share;
            else fused.set(hit.id, { hit, fusion: share });
          });
          const best = 2 / (FUSION_OFFSET + 1);
          return [...fused.values()]
            .map(({ hit, fusion }) => ({
              ...hit,
              score: Number(((fusion / best) * (1 + IMPORTANCE_INFLUENCE * hit.importance)).toFixed(3))
            }))
            .sort(byScoreThenId)
            .slice(0, limit);
        }

        // Answers one query for each kind. The query is embedded once, whatever the number of kinds, because
        // embedding is the slow step and the kinds only differ in which cards they read.
        async function answer(db, text, kinds, limit, lexicalOnly) {
          const denseModelId = readMetadata(db).get('denseModelId') ?? null;
          let queryVector = null;
          let notice = null;

          if (denseModelId && lexicalOnly) {
            notice = `semantic vectors from '${denseModelId}' were not used (lexical answer requested)`;
          } else if (denseModelId) {
            try {
              queryVector = await embedQuery(denseModelId, text);
            } catch (e) {
              notice = `${e.message}. Only the wording of the query was matched`;
            }
          }

          const groups = kinds.map((kind) => {
            if (queryVector === null) return { kind, hits: search(db, text, limit, kind), mode: 'lexical' };
            const lexical = search(db, text, FUSION_DEPTH, kind);
            return { kind, hits: fuse(lexical, semanticHits(db, kind, queryVector), limit), mode: 'hybrid' };
          });
          return { groups, notice };
        }

        function open(dbPath) {
          return new DatabaseSync(dbPath, { readOnly: true });
        }

        export async function queryIndex(dbPath, searchTerm, options = {}) {
          const { limit = 10, kind = null, compact = false, lexicalOnly = false } = options;

          if (!searchTerm || searchTerm.trim().length === 0) {
            return { query: searchTerm, error: 'Search term cannot be empty', hits: [] };
          }

          let db;
          try {
            db = open(dbPath);
          } catch (e) {
            return { query: searchTerm, error: `Database error: ${e.message}`, hits: [] };
          }

          try {
            const { groups, notice } = await answer(db, searchTerm, [kind], limit, lexicalOnly);
            const { hits, mode } = groups[0];
            const format = compact ? compactResult : verboseResult;
            return {
              query: searchTerm,
              kind: kind || 'all',
              hits: hits.map(format),
              count: hits.length,
              truncated: hits.length === limit,
              mode,
              notice
            };
          } finally {
            db.close();
          }
        }

        // One question asked of several kinds at once, embedding it a single time.
        export async function queryKinds(dbPath, searchTerm, kinds, options = {}) {
          const { limit = 5, lexicalOnly = false } = options;
          if (!searchTerm || searchTerm.trim().length === 0) {
            return { query: searchTerm, error: 'Search term cannot be empty', groups: [] };
          }

          let db;
          try {
            db = open(dbPath);
          } catch (e) {
            return { query: searchTerm, error: `Database error: ${e.message}`, groups: [] };
          }

          try {
            const { groups, notice } = await answer(db, searchTerm, kinds, limit, lexicalOnly);
            return {
              query: searchTerm,
              groups: groups.map((group) => ({ kind: group.kind, mode: group.mode, hits: group.hits.map(compactResult) })),
              notice
            };
          } finally {
            db.close();
          }
        }

        // Batch query for agent use: execute multiple queries in one DB open
        export function batchQuery(dbPath, queries, options = {}) {
          const { compact = true } = options;
          const db = open(dbPath);
          try {
            const format = compact ? compactResult : verboseResult;
            return queries.map((q) => {
              const hits = search(db, q.query, q.limit || 10, q.kind || null);
              return { query: q.query, kind: q.kind || 'all', hits: hits.map(format), count: hits.length };
            });
          } finally {
            db.close();
          }
        }
        """;

    internal const string SkillMarkdown = """
        ---
        name: scout-query
        description: Query a Scout kernel's retrieval index directly for fast, targeted lookups. Use when you need to search for symbols, modules, contracts, or documentation sections by keyword without running the full role pipeline. This is a faster alternative to scout-agents for one-off questions. Scout-agents automatically uses this for efficient agent role execution.
        ---

        # scout-query

        Direct search against a Scout kernel's retrieval index (`retrieval/index.db`). Use this for quick lookups
        without orchestrating the full Architect/Developer/Reviewer/QA pipeline. The CLI is a thin wrapper around
        SQLite queries optimized for code discovery. It needs Node 22.13 or newer (built-in `node:sqlite`) and
        installs nothing.

        This whole `skills/scout-query/` folder is generated inside the kernel
        (`<repo>.scout/skills/scout-query/`), not inside your repository — Scout never
        writes to the repository it analyzes. To use it as an actual Claude Code skill,
        copy this folder to `.claude/skills/scout-query/` at your repository root.

        Nothing here calls an LLM API. It only queries the retrieval index and returns results.

        ## Quick query

        ```
        node .claude/skills/scout-query/bin/scout-query.mjs "<search term>" --kernel "<kernel-path>"
        ```

        Returns up to 10 matching cards (symbols, contracts, modules, documentation sections, wording, files) from the
        retrieval index.

        ## Finding the place to work from the task's own words

        - `--kind text` finds a piece of wording the project shows (a button label, an error message) and names the
          component that holds it, with its line. Each card is one place: the same wording on three screens is three cards.
        - `--kind file` finds the file that declares, reads or shows what the words describe.
        - Wording that comes from data, not source, is in no text card. Query the field's name with `--kind symbol`:
          its `r` list names the declarations that read it.

        ## Options

        - `--kernel "<path>"` - Path to the Scout kernel (auto-detected if near cwd)
        - `--limit <n>` - Maximum results (default: 10)
        - `--kind <type>` - Filter by card kind: `symbol`, `module`, `contract`, `documentation`, `text` or `file`
        - `--lexical` - Answer from the wording of the query only, even when the kernel carries semantic vectors
        - `--compact` - Return compact JSON (~30% smaller, for programmatic use)
        - `--json` - Output as JSON (verbose format)

        ## Examples

        ```
        # Find all references to "admin"
        node .claude/skills/scout-query/bin/scout-query.mjs "admin" --kernel vheltrix-current.scout

        # Find contracts only, get up to 20 results
        node .claude/skills/scout-query/bin/scout-query.mjs "auth" --kind contract --limit 20

        # Find where the project's own documents speak about a topic
        node .claude/skills/scout-query/bin/scout-query.mjs "deploy" --kind documentation --kernel vheltrix-current.scout

        # Get compact JSON output for scripting
        node .claude/skills/scout-query/bin/scout-query.mjs "database" --compact --kernel vheltrix-current.scout
        ```

        ## Documentation cards

        A card of kind `documentation` is a heading of one of the project's documents (Markdown, for now).
        Only the heading was read, never the section body, so a hit tells you *where* the project speaks about
        a topic and nothing about what it says. Treat it as stated intent: open the section before relying on
        it, cite it as documentation, and where it disagrees with an observed contract or convention, trust
        the observation and report the disagreement. Matching ignores accents (`activacion` finds `Activación`).

        Scout does not compare documents with code, so do it only when the task depends on a document: if you
        would follow a section or cite it as fact, read its body, compare it with the code it describes, and
        report it as confirmed, a discrepancy (what it says, what the code shows, and where each is) or not
        checkable. A convention or concern it states that no observed convention covers applies to this task as
        a documented, unverified rule. Skip documents the task does not depend on.

        ## Compact Format (--compact flag)

        When used programmatically, compact format saves ~30% tokens:

        ```json
        {
          "query": "admin",
          "hits": [
            { "n": "Admin", "k": "shared-type", "p": "packages/domain/src/platform/admins.ts", "d": "contracts/xxx.json", "s": 0.95 }
          ]
        }
        ```

        Key: `n`=name, `k`=kind, `p`=path, `d`=detail, `s`=score, `l`=line, `r`=neighbours (what reads it, or what it
        reads). `l` and `r` appear only when the card has them.

        ## Semantic search

        A kernel built with `scout compile --embeddings ollama` also carries semantic vectors, so "whatsapp link"
        can find `waLink`. When the model that made them answers on this machine, results are fused (`mode:
        "hybrid"`); otherwise the answer is lexical and `notice` says what was missing. Nothing is required: a machine
        without Ollama gets the lexical answer.

        ## Locate the kernel

        Auto-detection searches for `*.scout` directories near your working directory:

        ```
        node .claude/skills/scout-query/bin/scout-query.mjs resolve-kernel
        ```

        If multiple kernels exist and you want to pick one explicitly:

        ```
        node .claude/skills/scout-query/bin/scout-query.mjs "<search>" --kernel /path/to/repo-current.scout
        ```

        ## How retrieval works

        The index ranks results by TF-IDF relevance over a vocabulary extracted from symbols, types,
        and declarations. Each card includes:

        - `path` (or `p`) — where in the repository the symbol lives
        - `detail` (or `d`) — path to a `.json` file with the full card details
        - `kind` (or `k`) — what type of artifact (symbol, contract, module, etc.)
        - `name` (or `n`) — the symbol or feature name
        - `score` (or `s`) — relevance score (higher is better match)

        Open the detail file only for cards that match your needs; the kernel narrows scope
        before requiring a full source code read.

        ## Internal use by scout-agents

        Scout-agents automatically uses `scout-query` to run efficient retrieval lookups for each agent role,
        without manual intervention. Results are embedded in role instructions for token efficiency.
        """;

    internal const string RunnerScript = """
        #!/usr/bin/env node
        // scout-query.mjs — query a Scout kernel's retrieval index for fast lookups.
        // Uses shared retrieval-engine.mjs for token efficiency.

        import { existsSync, readdirSync } from 'node:fs';
        import path from 'node:path';
        import { queryIndex } from './lib/retrieval-engine.mjs';

        function parseArgs(argv) {
          const out = { _: [] };
          for (let i = 0; i < argv.length; i++) {
            const a = argv[i];
            if (a.startsWith('--')) {
              const key = a.slice(2);
              const next = argv[i + 1];
              if (next === undefined || next.startsWith('--')) {
                out[key] = true;
              } else {
                out[key] = next;
                i++;
              }
            } else {
              out._.push(a);
            }
          }
          return out;
        }

        function fail(message) {
          process.stderr.write(`error: ${message}\n`);
          process.exit(1);
        }

        function looksLikeKernel(dir) {
          return existsSync(path.join(dir, 'manifest.json')) &&
                 existsSync(path.join(dir, 'agents', 'index.json'));
        }

        function findScoutDirsIn(dir) {
          if (!existsSync(dir)) return [];
          try {
            return readdirSync(dir, { withFileTypes: true })
              .filter((e) => e.isDirectory() && e.name.endsWith('.scout'))
              .map((e) => path.join(dir, e.name))
              .filter(looksLikeKernel);
          } catch {
            return [];
          }
        }

        function resolveKernel(explicit, fromDir) {
          if (explicit) {
            const abs = path.resolve(explicit);
            if (!looksLikeKernel(abs)) {
              fail(`'${abs}' does not look like a Scout kernel (missing manifest.json or agents/index.json).`);
            }
            return abs;
          }

          const start = path.resolve(fromDir || process.cwd());
          const candidates = new Set();

          if (looksLikeKernel(start)) candidates.add(start);
          for (const d of findScoutDirsIn(start)) candidates.add(d);

          let dir = start;
          for (let depth = 0; depth < 4; depth++) {
            const parent = path.dirname(dir);
            if (parent === dir) break;
            for (const d of findScoutDirsIn(parent)) candidates.add(d);
            dir = parent;
          }

          const list = [...candidates];
          if (list.length === 1) return list[0];
          if (list.length === 0) {
            fail(
              `No Scout kernel found near '${start}'. Pass --kernel <path-to-*.scout>, or run ` +
              `'scout compile'/'scout update' first.`
            );
          }
          fail(`Multiple Scout kernels found near '${start}':\n  ${list.join('\n  ')}\nPass --kernel to pick one.`);
        }

        function cmdResolveKernel(args) {
          const kernel = resolveKernel(args.kernel, args.from);
          if (args.json) process.stdout.write(JSON.stringify({ kernel }, null, 2) + '\n');
          else process.stdout.write(`${kernel}\n`);
        }

        async function cmdQuery(searchTerm, args) {
          const kernelDir = resolveKernel(args.kernel, process.cwd());
          const dbPath = path.join(kernelDir, 'retrieval', 'index.db');

          if (!existsSync(dbPath)) {
            fail(`No retrieval index found at '${dbPath}'. This kernel was not generated with retrieval enabled.`);
          }

          const limit = parseInt(args.limit || '10', 10);
          const result = await queryIndex(dbPath, searchTerm, {
            limit,
            kind: args.kind || null,
            compact: args.compact === true,
            lexicalOnly: args.lexical === true
          });

          if (result.notice) {
            process.stderr.write(`notice: ${result.notice}\n`);
          }

          if (result.error) {
            fail(result.error);
          }

          if (args.json || args.compact) {
            process.stdout.write(JSON.stringify(result, null, 2) + '\n');
          } else {
            if (result.hits.length === 0) {
              process.stdout.write(`No results for "${searchTerm}"\n`);
              return;
            }

            process.stdout.write(`Found ${result.hits.length} result(s):\n\n`);
            for (const r of result.hits) {
              process.stdout.write(`• ${r.name} [${r.kind}] (score: ${r.score})\n`);
              process.stdout.write(`  path: ${r.path}${r.line ? `:${r.line}` : ''}\n`);
              if (r.related.length > 0) process.stdout.write(`  near: ${r.related.slice(0, 8).join(', ')}\n`);
              process.stdout.write(`  detail: ${r.detail}\n\n`);
            }
          }
        }

        function main() {
          const args = parseArgs(process.argv.slice(2));
          const command = args._[0];

          if (command === 'resolve-kernel') {
            return cmdResolveKernel(args);
          }

          if (!command) {
            process.stderr.write('usage: scout-query <search-term> [--kernel <path>] [--limit <n>] [--kind <type>] [--lexical] [--compact] [--json]\n');
            process.stderr.write('       scout-query resolve-kernel [--from <dir>]\n');
            process.exit(1);
          }

          cmdQuery(command, args).catch(e => fail(e.message));
        }

        main();
        """;
}
