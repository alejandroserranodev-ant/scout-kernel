namespace Scout.Core.Agents;

/// <summary>
/// Static, repository-agnostic files for a vendor-neutral orchestration skill that drives the role
/// pipeline published in <c>agents/index.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every kernel gets the identical bytes here. Nothing in this class is derived from analysis of the
/// target repository — the runner only ever reads <c>agents/index.json</c> and <c>manifest.json</c> at
/// the call site, at the time each command runs. That is what makes it safe to render unconditionally
/// alongside the other kernel artifacts without breaking the rule that kernel rendering performs no new
/// analysis: there is no fact here that could be wrong for a particular repository, because none of it
/// is about a particular repository.
/// </para>
/// <para>
/// <see cref="Kernel.KernelRenderer"/> is the only caller. It writes these under
/// <see cref="RelativeDirectory"/> inside the kernel output directory — never into the repository being
/// analyzed, consistent with Scout never writing anywhere but its own kernel.
/// </para>
/// </remarks>
internal static class OrchestrationSkillArtifacts
{
    /// <summary>Path, relative to the kernel root, where every file below is published.</summary>
    internal const string RelativeDirectory = "skills/scout-agents";

    internal const string SkillMarkdown = """
        ---
        name: scout-agents
        description: Run the Architect/Developer/Reviewer/QA role pipeline published in this Scout kernel's agents/index.json, using the bundled scout-agents CLI to track whose turn it is and hand off between roles (including bounded subsessions). Use when the user asks to run Scout's agents, follow the architect/developer/reviewer/qa flow, or orchestrate a change through a Scout kernel.
        ---

        # scout-agents

        Drives the role pipeline a Scout kernel publishes (`agents/index.json`): Architect ->
        Developer -> Reviewer -> QA, with loop-backs and bounded subsessions. The pipeline
        itself lives outside any AI vendor's format: `bin/scout-agents.mjs` is a
        dependency-free Node script that only reads/writes plain JSON files inside the
        kernel's `.internal/agent-sessions/` folder. This SKILL.md is the thin, Claude-Code
        specific adapter over it — see `adapters/` for the same protocol written for
        Codex, Copilot and Cursor.

        This whole `skills/scout-agents/` folder is generated inside the kernel
        (`<repo>.scout/skills/scout-agents/`), not inside your repository — Scout never
        writes to the repository it analyzes. To use it as an actual Claude Code skill,
        copy this folder to `.claude/skills/scout-agents/` at your repository root. Every
        command below already assumes that destination path; copying it verbatim (same
        folder name, same nesting) means nothing here needs to be edited afterward.

        Nothing here calls an LLM API or spawns another vendor's CLI. "Running a role" means:
        you (this session, or a forked subagent) read what the CLI prints, do the work by
        hand, then call the CLI again to record the handoff. The CLI is a session-state
        tracker, not an autonomous engine — it will not skip a bad handoff, but it also
        will not stop you from doing sloppy work between two correct commands.

        ## Locate the kernel

        ```
        node .claude/skills/scout-agents/bin/scout-agents.mjs resolve-kernel --from <repo-root>
        ```

        If it reports more than one kernel (a stale one alongside a fresh one, commonly a
        `*-legacy.scout` next to a `*-current.scout`), ask the user which to use, or pass
        `--kernel <path>` explicitly on every command below instead of relying on
        auto-detection.

        ## Start a task

        ```
        node .claude/skills/scout-agents/bin/scout-agents.mjs start --kernel "<kernel>" --task "<what the user asked for>"
        ```

        Prints the session id and the first role's full status (mandate, files to read,
        retrieval commands to run, obligations, possible handoffs). From here on, everything
        you do is scoped to whatever role is currently on top of the session's stack.

        ## Play a role

        1. Run `status --kernel "<kernel>" --session <id>` if you need to re-read the current
           role (e.g. after a compaction, or a fresh subagent picking up the session).
        2. Open only the files under `reads`.
        3. Run every command under `retrieval`, replacing the placeholder with the actual
           thing you're looking for — this is a real `scout query` invocation against the
           kernel's retrieval index. Open source only for what a query result names.
        4. Follow `obligations` literally. They cite real numbers from this repo's kernel
           (contract counts, module counts) — treat a mismatch between what you observe in
           code and what an obligation asserts as a reason to stop and flag it, not to
           silently trust either side.
        5. Decide the handoff: match the situation against each entry under `handoffs` and
           its `condition`.

        Documentation cards (`--kind documentation`, and a documentation index when a role reads one)
        are stated intent from the project's own documents. Only their titles were read: open the section
        before relying on it, cite it as documentation, and when it disagrees with an observed contract or
        convention trust the observation and flag the disagreement.

        Scout does not compare documents with code, so each role does it, and only when the task depends on a
        document: read the section body, compare it with the code it describes, and report it as confirmed, a
        discrepancy (what it says, what the code shows, and where each is) or not checkable. Skip documents the
        task does not depend on. A role's obligations carry the same rule when the kernel has documentation.

        ## Hand off

        - **`sequential`** — the role's job is done, control moves on and does not return:

          ```
          node .claude/skills/scout-agents/bin/scout-agents.mjs advance --kernel "<kernel>" --session <id> --to <roleId> --note "<one line: what was decided>"
          ```

          Keep working in this same conversation, now as the new current role (go back to
          "Play a role" above).

        - **`subsession`** — the current role needs another role's judgment on one question,
          without giving up ownership of the task:

          ```
          node .claude/skills/scout-agents/bin/scout-agents.mjs open-subsession --kernel "<kernel>" --session <id> --to <roleId> --note "<the question>"
          ```

          Then spawn a fork to actually answer as that role:

          > Agent({ subagent_type: "fork", description: "<roleId> subsession", prompt: "Act as
          > the '<roleId>' role in scout-agents session <id> (kernel: <kernel>). Run `status`
          > for that session, answer: <the question>, then run `return --kernel \"<kernel>\"
          > --session <id> --result \"<your answer>\"` before reporting back." })

          The fork calling `return` is what pops the stack — do not call `return` yourself
          from the parent side. When the fork reports back, continue as the original role
          with its answer in hand (the session file already reflects the pop; `status` will
          confirm you're back on top).

        ## Finish

        When a role has no handoff left that applies (typically QA with no failing case):

        ```
        node .claude/skills/scout-agents/bin/scout-agents.mjs finish --kernel "<kernel>" --session <id> --note "<summary>"
        ```

        ## Useful side commands

        - `list-roles --kernel "<kernel>"` — dump all four role mandates without starting a session.
        - `sessions --kernel "<kernel>"` — see what's in flight or already closed.
        - `log --kernel "<kernel>" --session <id>` — full handoff history for one session.

        Every command accepts `--json` for machine-parseable output.

        ## Portability

        `bin/scout-agents.mjs` has no Claude-specific code in it — it is the same file
        referenced by `adapters/AGENTS.md.snippet.md` (Codex), `adapters/copilot-instructions.snippet.md`
        (GitHub Copilot) and `adapters/cursor-rules.snippet.mdc` (Cursor). Moving this whole
        `scout-agents/` folder into another project's `.claude/skills/` (or copying just
        `bin/scout-agents.mjs` next to one of the other adapter files) is enough to reuse it
        elsewhere; nothing here is tied to this repository or to this kernel.

        The other three adapters describe the same protocol in each tool's own convention.
        They were written from each tool's publicly documented instruction-file format, not
        verified inside that tool — Codex, Copilot and Cursor were not available to test
        against when this was written. Confirm the conventions still match before trusting
        one blindly (file location, whether it's auto-loaded, any size limits).
        """;

    internal const string AgentRetrievalExecutor = """
        // agent-retrieval-executor.mjs — executes automatic queries for agent roles efficiently
        // Uses compact results to minimize token usage

        import { existsSync } from 'node:fs';
        import path from 'node:path';
        import { batchQuery, queryKinds } from './retrieval-engine.mjs';

        export function executeAutoQueries(kernelDir, autoQueries) {
          if (!autoQueries || autoQueries.length === 0) {
            return {};
          }

          const dbPath = path.join(kernelDir, 'retrieval', 'index.db');
          if (!existsSync(dbPath)) {
            return {}; // Retrieval not available
          }

          try {
            const results = batchQuery(dbPath, autoQueries, { compact: true });
            const byQuery = {};
            results.forEach(r => {
              byQuery[r.query] = r;
            });
            return byQuery;
          } catch (e) {
            console.error(`Warning: Auto-query execution failed: ${e.message}`);
            return {};
          }
        }

        // The first thing a role needs is where to start, and the task is phrased in the words the project shows:
        // a label it quotes, the behaviour it wants changed, the field it names. So the task itself is the query,
        // asked of the wording, the files and the symbols at once. This is a lead to open, not a verdict.
        export async function locateTask(kernelDir, task) {
          if (!task || task.trim().length === 0) {
            return null;
          }

          const dbPath = path.join(kernelDir, 'retrieval', 'index.db');
          if (!existsSync(dbPath)) {
            return null;
          }

          try {
            const result = await queryKinds(dbPath, task, ['text', 'file', 'symbol'], { limit: 5 });
            return result.error ? null : result;
          } catch (e) {
            console.error(`Warning: locating the task failed: ${e.message}`);
            return null;
          }
        }

        export function formatLocated(located) {
          const labels = { text: 'wording that matches', file: 'files that match', symbol: 'symbols that match' };
          const lines = [];
          for (const group of located.groups) {
            if (group.hits.length === 0) continue;
            lines.push(`${labels[group.kind] ?? group.kind} [${group.mode}]:`);
            for (const hit of group.hits) {
              const where = `${hit.p}${hit.l ? `:${hit.l}` : ''}`;
              const near = hit.r && hit.r.length > 0 ? `  (${hit.k === 'text' ? 'in' : 'near'} ${hit.r.slice(0, 4).join(', ')})` : '';
              lines.push(`  - ${hit.n}  ->  ${where}${near}`);
            }
          }
          if (located.notice) {
            lines.push(`notice: ${located.notice}`);
          }
          return lines.join('\n');
        }

        export function formatAutoQueryResults(results) {
          const lines = [];
          for (const [query, result] of Object.entries(results)) {
            lines.push(`auto-query: "${query}" (${result.count} hit${result.count !== 1 ? 's' : ''})`);
            if (result.hits && result.hits.length > 0) {
              result.hits.slice(0, 5).forEach(hit => {
                lines.push(`   - ${hit.n} [${hit.k}] (score: ${hit.s})`);
                // A documentation hit is a place to read, so it points at the document rather than at the shared index.
                lines.push(`     -> ${hit.k === 'documentation' ? hit.p : (hit.d ?? hit.p)}`);
              });
              if (result.hits.length > 5) {
                lines.push(`   ... and ${result.hits.length - 5} more`);
              }
            }
            lines.push('');
          }
          return lines.join('\n');
        }
        """;

    internal const string RunnerScript = """
        #!/usr/bin/env node
        // scout-agents — a tool-agnostic session tracker for the role graph a Scout kernel
        // publishes in agents/index.json. Enhanced with automatic retrieval queries for
        // token efficiency. Runs on plain Node.js: no .NET, no network, no dependency on
        // any specific AI CLI. Any agent that can run a shell command can drive it.
        //
        // It does not call an LLM and it does not run the roles. It only answers two
        // questions on request: "whose turn is it, and what are they bound to do", and
        // "record that control moved, and remember it after this process exits".
        //
        // Automatic queries are executed before role execution for token efficiency.
        //
        // Commands:
        //   start          --kernel <dir> [--task "<text>"] [--session <id>]
        //   status         --session <id> [--kernel <dir>] [--json]
        //   list-roles     --kernel <dir> [--json]
        //   sessions       --kernel <dir> [--json]
        //   advance        --session <id> --to <roleId> [--kernel <dir>] [--note "<text>"]
        //   open-subsession --session <id> --to <roleId> [--kernel <dir>] [--note "<text>"]
        //   return         --session <id> [--kernel <dir>] [--note "<text>"] [--result "<text>"]
        //   finish         --session <id> [--kernel <dir>] [--note "<text>"]
        //   log            --session <id> [--kernel <dir>] [--json]
        //   resolve-kernel [--from <dir>]

        import { existsSync, readFileSync, writeFileSync, mkdirSync, readdirSync, statSync } from 'node:fs';
        import path from 'node:path';
        import crypto from 'node:crypto';
        import { executeAutoQueries, formatAutoQueryResults, locateTask, formatLocated } from './lib/agent-retrieval-executor.mjs';

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

        function readJson(filePath) {
          return JSON.parse(readFileSync(filePath, 'utf8'));
        }

        function writeJson(filePath, value) {
          mkdirSync(path.dirname(filePath), { recursive: true });
          writeFileSync(filePath, JSON.stringify(value, null, 2) + '\n', 'utf8');
        }

        // --- kernel discovery -------------------------------------------------

        function looksLikeKernel(dir) {
          return existsSync(path.join(dir, 'manifest.json')) && existsSync(path.join(dir, 'agents', 'index.json'));
        }

        function findScoutDirsIn(dir) {
          if (!existsSync(dir)) return [];
          return readdirSync(dir, { withFileTypes: true })
            .filter((e) => e.isDirectory() && e.name.endsWith('.scout'))
            .map((e) => path.join(dir, e.name))
            .filter(looksLikeKernel);
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

        // --- agents/index.json access ------------------------------------------

        function loadAgentsIndex(kernelDir) {
          const p = path.join(kernelDir, 'agents', 'index.json');
          if (!existsSync(p)) fail(`'${kernelDir}' has no agents/index.json. This kernel did not publish role templates.`);
          return readJson(p);
        }

        function findRole(index, roleId) {
          const item = index.items.find((i) => i.id === roleId);
          if (!item) fail(`Unknown role '${roleId}'. Known roles: ${index.items.map((i) => i.id).join(', ')}`);
          return item;
        }

        function loadRoleDetail(kernelDir, index, roleId) {
          const item = findRole(index, roleId);
          const detailPath = path.join(kernelDir, item.detail);
          return readJson(detailPath);
        }

        // --- sessions ------------------------------------------------------------

        function sessionsDir(kernelDir) {
          return path.join(kernelDir, '.internal', 'agent-sessions');
        }

        function sessionPathFor(kernelDir, idOrPath) {
          if (idOrPath.endsWith('.json') && existsSync(idOrPath)) return path.resolve(idOrPath);
          return path.join(sessionsDir(kernelDir), `${idOrPath}.json`);
        }

        function kernelFromSessionPath(sessionFile) {
          // <kernel>/.internal/agent-sessions/<id>.json
          return path.dirname(path.dirname(path.dirname(sessionFile)));
        }

        function loadSession(sessionFile) {
          if (!existsSync(sessionFile)) fail(`No session at '${sessionFile}'.`);
          return readJson(sessionFile);
        }

        function saveSession(sessionFile, session) {
          writeJson(sessionFile, session);
        }

        function nowIso() {
          return new Date().toISOString();
        }

        function currentRole(session) {
          return session.stack[session.stack.length - 1];
        }

        // --- rendering -------------------------------------------------------------

        function retrievalHints(kernelDir, detail) {
          const kinds = detail.retrievalKinds || [];
          const limit = detail.retrievalLimit || 8;
          return kinds.map((kind) => ({
            kind,
            limit,
            command: `scout query "<describe what you need for this step>" --kernel "${kernelDir}" --kind ${kind} --limit ${limit} --json`,
          }));
        }

        // The roles that decide where to work are the ones told where the task's words point.
        const LOCATING_ROLES = new Set(['architect', 'developer']);

        async function roleView(kernelDir, index, session, roleId) {
          const item = findRole(index, roleId);
          const detail = loadRoleDetail(kernelDir, index, roleId);
          const located = session && LOCATING_ROLES.has(roleId) ? await locateTask(kernelDir, session.task) : null;

          // Execute automatic queries for token efficiency
          const autoQueryResults = detail.autoQueries && detail.autoQueries.length > 0
            ? executeAutoQueries(kernelDir, detail.autoQueries)
            : {};

          return {
            id: item.id,
            role: item.role,
            mandate: detail.mandate,
            reads: (detail.reads || []).map((r) => path.join(kernelDir, r)),
            retrieval: retrievalHints(kernelDir, detail),
            autoQueryResults: autoQueryResults,
            located: located,
            obligations: detail.obligations || [],
            handoffs: detail.handoffs || [],
            stack: session ? [...session.stack] : undefined,
            task: session ? session.task : undefined,
          };
        }

        function printView(view, opts) {
          if (opts.json) {
            process.stdout.write(JSON.stringify(view, null, 2) + '\n');
            return;
          }
          const lines = [];
          lines.push(`role: ${view.role} (${view.id})`);
          if (view.stack) lines.push(`stack: ${view.stack.join(' -> ')}`);
          if (view.task) lines.push(`task: ${view.task}`);
          lines.push(`mandate: ${view.mandate}`);

          if (view.located) {
            const located = formatLocated(view.located);
            if (located.length > 0) {
              lines.push("where the task's own words point (start here, and open these before searching):");
              lines.push(located.split('\n').map((l) => '  ' + l).join('\n'));
            }
          }

          // Show automatic query results (token-efficient)
          if (view.autoQueryResults && Object.keys(view.autoQueryResults).length > 0) {
            lines.push('📌 automatic retrieval results (token-optimized):');
            const results = formatAutoQueryResults(view.autoQueryResults);
            lines.push(results.split('\n').map(l => '  ' + l).join('\n'));
          }

          if (view.reads.length) {
            lines.push('reads (open only these, and only what retrieval names beyond them):');
            for (const r of view.reads) lines.push(`  - ${r}`);
          }
          if (view.retrieval.length) {
            lines.push('manual retrieval (if auto-queries did not cover your needs):');
            for (const r of view.retrieval) lines.push(`  - [${r.kind}, limit ${r.limit}] ${r.command}`);
          }
          if (view.obligations.length) {
            lines.push('obligations:');
            for (const o of view.obligations) lines.push(`  - ${o}`);
          }
          if (view.handoffs.length) {
            lines.push('handoffs:');
            for (const h of view.handoffs) {
              lines.push(`  - ${h.mode} -> ${h.targetRoleId}  (when: ${h.condition})`);
            }
          } else {
            lines.push('handoffs: none (terminal role for this path; consider `finish`).');
          }
          process.stdout.write(lines.join('\n') + '\n');
        }

        // --- commands -----------------------------------------------------------

        function cmdResolveKernel(args) {
          const kernel = resolveKernel(args.kernel, args.from);
          if (args.json) process.stdout.write(JSON.stringify({ kernel }, null, 2) + '\n');
          else process.stdout.write(`${kernel}\n`);
        }

        function cmdListRoles(args) {
          const kernelDir = resolveKernel(args.kernel, process.cwd());
          const index = loadAgentsIndex(kernelDir);
          if (args.json) {
            process.stdout.write(JSON.stringify(index, null, 2) + '\n');
            return;
          }
          process.stdout.write(`${index.count} role(s), ${index.ordering}\n`);
          for (const item of index.items) {
            process.stdout.write(`  - ${item.id} (${item.role}): ${item.mandate}\n`);
          }
        }

        async function cmdStart(args) {
          const kernelDir = resolveKernel(args.kernel, process.cwd());
          const index = loadAgentsIndex(kernelDir);
          const entryRoleId = index.items[0].id; // agents/index.json documents this as run order
          const id = typeof args.session === 'string' ? args.session : crypto.randomBytes(4).toString('hex');
          const sessionFile = sessionPathFor(kernelDir, id);
          if (existsSync(sessionFile)) fail(`Session '${id}' already exists at '${sessionFile}'.`);

          const session = {
            id,
            kernel: kernelDir,
            task: typeof args.task === 'string' ? args.task : '',
            createdAt: nowIso(),
            stack: [entryRoleId],
            done: false,
            history: [{ at: nowIso(), event: 'start', role: entryRoleId }],
          };
          saveSession(sessionFile, session);

          process.stdout.write(`session ${id} started at ${sessionFile}\n\n`);
          printView(await roleView(kernelDir, index, session, entryRoleId), args);
        }

        function loadSessionContext(args) {
          const sessionArg = typeof args.session === 'string' ? args.session : fail('--session is required.');
          const kernelDir = args.kernel
            ? resolveKernel(args.kernel, process.cwd())
            : sessionArg.endsWith('.json')
              ? kernelFromSessionPath(path.resolve(sessionArg))
              : resolveKernel(undefined, process.cwd());
          const sessionFile = sessionPathFor(kernelDir, sessionArg);
          const session = loadSession(sessionFile);
          const index = loadAgentsIndex(kernelDir);
          return { kernelDir, sessionFile, session, index };
        }

        async function cmdStatus(args) {
          const { kernelDir, session, index } = loadSessionContext(args);
          printView(await roleView(kernelDir, index, session, currentRole(session)), args);
        }

        function cmdSessions(args) {
          const kernelDir = resolveKernel(args.kernel, process.cwd());
          const dir = sessionsDir(kernelDir);
          const files = existsSync(dir) ? readdirSync(dir).filter((f) => f.endsWith('.json')) : [];
          const summaries = files.map((f) => {
            const s = readJson(path.join(dir, f));
            return { id: s.id, task: s.task, stack: s.stack, done: s.done, createdAt: s.createdAt };
          });
          if (args.json) {
            process.stdout.write(JSON.stringify(summaries, null, 2) + '\n');
            return;
          }
          if (summaries.length === 0) {
            process.stdout.write('no sessions yet.\n');
            return;
          }
          for (const s of summaries) {
            process.stdout.write(`  - ${s.id}  [${s.done ? 'done' : 'open'}]  ${s.stack.join(' -> ')}  ${s.task}\n`);
          }
        }

        function requireHandoff(kernelDir, index, session, targetRoleId, requiredMode) {
          const roleId = currentRole(session);
          const detail = loadRoleDetail(kernelDir, index, roleId);
          const handoff = (detail.handoffs || []).find((h) => h.targetRoleId === targetRoleId);
          if (!handoff) {
            const options = (detail.handoffs || []).map((h) => `${h.targetRoleId} (${h.mode})`).join(', ') || 'none';
            fail(`'${roleId}' has no handoff to '${targetRoleId}'. Options: ${options}`);
          }
          if (handoff.mode !== requiredMode) {
            fail(`'${roleId}' -> '${targetRoleId}' is a '${handoff.mode}' handoff. Use the matching command, not this one.`);
          }
          return handoff;
        }

        async function cmdAdvance(args) {
          const { kernelDir, sessionFile, session, index } = loadSessionContext(args);
          const to = typeof args.to === 'string' ? args.to : fail('--to <roleId> is required.');
          const handoff = requireHandoff(kernelDir, index, session, to, 'sequential');
          const from = currentRole(session);
          session.stack[session.stack.length - 1] = to;
          session.history.push({ at: nowIso(), event: 'advance', from, to, mode: 'sequential', condition: handoff.condition, note: args.note || '' });
          saveSession(sessionFile, session);
          printView(await roleView(kernelDir, index, session, to), args);
        }

        async function cmdOpenSubsession(args) {
          const { kernelDir, sessionFile, session, index } = loadSessionContext(args);
          const to = typeof args.to === 'string' ? args.to : fail('--to <roleId> is required.');
          const handoff = requireHandoff(kernelDir, index, session, to, 'subsession');
          const from = currentRole(session);
          session.stack.push(to);
          session.history.push({ at: nowIso(), event: 'subsession-open', from, to, condition: handoff.condition, note: args.note || '' });
          saveSession(sessionFile, session);
          printView(await roleView(kernelDir, index, session, to), args);
        }

        async function cmdReturn(args) {
          const { kernelDir, sessionFile, session, index } = loadSessionContext(args);
          if (session.stack.length < 2) {
            fail(`Nothing to return to: '${currentRole(session)}' is the top-level role in this session.`);
          }
          const from = session.stack.pop();
          const to = currentRole(session);
          session.history.push({ at: nowIso(), event: 'subsession-return', from, to, note: args.note || '', result: args.result || '' });
          saveSession(sessionFile, session);
          process.stdout.write(`returned from '${from}' to '${to}'.\n\n`);
          printView(await roleView(kernelDir, index, session, to), args);
        }

        function cmdFinish(args) {
          const { session, sessionFile } = loadSessionContext(args);
          session.done = true;
          session.history.push({ at: nowIso(), event: 'finish', role: currentRole(session), note: args.note || '' });
          saveSession(sessionFile, session);
          process.stdout.write(`session ${session.id} marked done at role '${currentRole(session)}'.\n`);
        }

        function cmdLog(args) {
          const { session } = loadSessionContext(args);
          if (args.json) {
            process.stdout.write(JSON.stringify(session.history, null, 2) + '\n');
            return;
          }
          for (const h of session.history) {
            const bits = Object.entries(h)
              .filter(([k]) => k !== 'at')
              .map(([k, v]) => `${k}=${v}`)
              .join(' ');
            process.stdout.write(`${h.at}  ${bits}\n`);
          }
        }

        function main() {
          const args = parseArgs(process.argv.slice(2));
          const command = args._[0];
          switch (command) {
            case 'resolve-kernel': return cmdResolveKernel(args);
            case 'list-roles': return cmdListRoles(args);
            case 'start': return cmdStart(args);
            case 'status': return cmdStatus(args);
            case 'sessions': return cmdSessions(args);
            case 'advance': return cmdAdvance(args);
            case 'open-subsession': return cmdOpenSubsession(args);
            case 'return': return cmdReturn(args);
            case 'finish': return cmdFinish(args);
            case 'log': return cmdLog(args);
            default:
              process.stderr.write(
                'usage: scout-agents <resolve-kernel|list-roles|start|status|sessions|advance|open-subsession|return|finish|log> [options]\n'
              );
              process.exit(command ? 1 : 0);
          }
        }

        Promise.resolve(main()).catch((e) => {
          process.stderr.write(`error: ${e.message}\n`);
          process.exit(1);
        });
        """;

    internal const string CodexAdapter = """
        # Adapter: Codex (AGENTS.md)

        Codex CLI reads `AGENTS.md` at the repo root as standing instructions. Add a section
        like this one to that file (create it if the repo has none) to make Codex drive the
        same scout-agents pipeline the Claude Code skill uses. This is a template written
        from Codex's documented `AGENTS.md` convention — it has not been run inside Codex.

        ---

        ## Scout agent pipeline

        This repo has a Scout kernel with a role pipeline (Architect/Developer/Reviewer/QA)
        in `agents/index.json`. When asked to run it, or to work through it for a task:

        1. Find the kernel: `node .claude/skills/scout-agents/bin/scout-agents.mjs resolve-kernel --from .`
           If more than one is reported, ask which to use instead of guessing.
        2. Start: `node .claude/skills/scout-agents/bin/scout-agents.mjs start --kernel "<kernel>" --task "<task>"`
        3. Read the printed role status: open only the files under `reads`, run every
           command under `retrieval` (a real `scout query` call) before opening any source
           file, and follow `obligations` literally.
        4. Decide the handoff by matching the situation to a `condition` under `handoffs`:
           - `sequential`: `node .claude/skills/scout-agents/bin/scout-agents.mjs advance --kernel "<kernel>" --session <id> --to <roleId> --note "<what was decided>"`,
             then continue as the new role.
           - `subsession`: this CLI has no native "spawn a bounded sub-agent that returns"
             primitive the way Claude Code's fork does. Simulate it explicitly instead of
             silently switching roles:
             a. `node .claude/skills/scout-agents/bin/scout-agents.mjs open-subsession --kernel "<kernel>" --session <id> --to <roleId> --note "<the question>"`
             b. Answer the question *as that role*, reading only what its `status` says to
                read — do not carry over the calling role's assumptions.
             c. `node .claude/skills/scout-agents/bin/scout-agents.mjs return --kernel "<kernel>" --session <id> --result "<answer>"`
             d. Resume the original role with that answer.
        5. When a role has no applicable handoff left: `node .claude/skills/scout-agents/bin/scout-agents.mjs finish --kernel "<kernel>" --session <id> --note "<summary>"`.

        Every command accepts `--json`. `node .claude/skills/scout-agents/bin/scout-agents.mjs log --kernel "<kernel>" --session <id>` shows the full handoff history if the session's state is unclear.
        """;

    internal const string CopilotAdapter = """
        # Adapter: GitHub Copilot (.github/copilot-instructions.md)

        GitHub Copilot Chat reads .github/copilot-instructions.md as repo-wide custom
        instructions. Add a section like this one to that file (create it if the repo has
        none) so Copilot drives the same scout-agents pipeline the Claude Code skill uses.
        This is a template written from Copilot's documented custom-instructions
        convention — it has not been run inside Copilot.

        ---

        ## Scout agent pipeline

        This repo has a Scout kernel publishing an Architect/Developer/Reviewer/QA role
        pipeline in `agents/index.json`. To run it for a task:

        1. Locate the kernel:
           `node .claude/skills/scout-agents/bin/scout-agents.mjs resolve-kernel --from .`
           If it reports more than one candidate, ask the user which one before continuing.
        2. Start a session:
           `node .claude/skills/scout-agents/bin/scout-agents.mjs start --kernel "<kernel>" --task "<task>"`
        3. For the role now current: open only the files listed under `reads`, run each
           command under `retrieval` (a real `scout query` call against this kernel) before
           opening any source file, and follow every line under `obligations` as a hard
           constraint, not a suggestion.
        4. Pick the handoff whose `condition` matches what happened:
           - **sequential** — the role's work is done:
             `node .claude/skills/scout-agents/bin/scout-agents.mjs advance --kernel "<kernel>" --session <id> --to <roleId> --note "<decision>"`,
             then keep going as the new current role.
           - **subsession** — a bounded question for another role, without giving up the
             task. Copilot has no documented mechanism for a nested sub-agent that returns
             control, so simulate it explicitly:
             `open-subsession --kernel "<kernel>" --session <id> --to <roleId> --note "<question>"`,
             answer strictly as that role using only its own `reads`/`retrieval`, then
             `return --kernel "<kernel>" --session <id> --result "<answer>"` and resume the
             original role with that answer in hand.
        5. When the current role has no handoff that fits (typically QA with nothing
           failing): `node .claude/skills/scout-agents/bin/scout-agents.mjs finish --kernel "<kernel>" --session <id> --note "<summary>"`.

        Add `--json` to any command for structured output; use
        `node .claude/skills/scout-agents/bin/scout-agents.mjs log --kernel "<kernel>" --session <id>`
        to recover the full handoff history if a conversation loses track of where it left off.
        """;

    internal const string CursorAdapter = """
        ---
        description: Scout agent pipeline (Architect/Developer/Reviewer/QA) via scout-agents
        alwaysApply: false
        ---

        # Adapter: Cursor (.cursor/rules/*.mdc)

        Cursor loads `.cursor/rules/*.mdc` files as project rules. Save this file under
        `.cursor/rules/` (adjust the path in the commands below if `scout-agents/` is not at
        `.claude/skills/scout-agents/` in this project) to make Cursor drive the same
        scout-agents pipeline the Claude Code skill uses. This is a template written from
        Cursor's documented rules convention — it has not been run inside Cursor.

        When asked to run the Scout agent pipeline for a task:

        1. Locate the kernel: `node .claude/skills/scout-agents/bin/scout-agents.mjs resolve-kernel --from .`
           More than one candidate reported -> ask which one instead of guessing.
        2. Start: `node .claude/skills/scout-agents/bin/scout-agents.mjs start --kernel "<kernel>" --task "<task>"`.
        3. As the current role: open only its `reads` files, run its `retrieval` commands
           (real `scout query` calls) before opening any source file, and treat every
           `obligations` entry as a hard constraint.
        4. Match the situation to a `handoffs` condition:
           - **sequential**: `advance --kernel "<kernel>" --session <id> --to <roleId> --note "<decision>"`, then continue as the new role.
           - **subsession**: Cursor has no documented nested-agent-that-returns primitive, so
             simulate it: `open-subsession --kernel "<kernel>" --session <id> --to <roleId> --note "<question>"`,
             answer strictly as that role, then `return --kernel "<kernel>" --session <id> --result "<answer>"`
             and resume the original role.
        5. No handoff applies (typically QA, nothing failing): `finish --kernel "<kernel>" --session <id> --note "<summary>"`.

        Use `--json` on any command for structured output; `log --kernel "<kernel>" --session <id>` recovers full handoff history.
        """;
}
