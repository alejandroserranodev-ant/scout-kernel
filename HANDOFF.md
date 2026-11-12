# Scout — handoff for the next engineer or AI

Read this first. It says what Scout is for, how it is built, how to work in it, what is done,
and what is left. Everything here is verifiable in the repository; where a claim comes from a
measurement, the report that holds it is named.

## 1. Goal (and the only success test)

Scout compiles a repository into a queryable **kernel** so a coding agent (Claude Code or any
other) develops **faster, with fewer mistakes, and spending fewer tokens** than it would without
Scout — and that must be **demonstrated by measurement**, not asserted.

Anything that does not save tokens, time, or errors is out of scope.

Decision rule (in `tooling/bench/config.json`, `minimumTokenSavingsRate`, proposed `0.3`): after
Phase 3, if the with-Scout arm does not save at least that fraction of tokens at equal-or-better
hit rate, **stop and report instead of building further**.

**Current honest status:** on the 6-file synthetic corpus Scout costs *more* tokens than naive
grep+read (about -85.6% "savings", equal 90.5% hit rate;
`tooling/bench/reports/synthetic-baseline-2026-11-09.txt`). That corpus is too small to exercise
the regime Scout is for. The real verdict still needs a large local corpus plus the manual
Claude Code protocol (section 7, step A). Do not claim savings until that exists.

## 2. Hard rules (carry these into every change)

1. Nothing hardcoded: paths, thresholds, limits, word lists, hook matchers, messages, token
   divisors come from named constants or configuration.
2. Generic: no project, client, domain, country, regulation, or language named in Scout's code,
   defaults, templates, or docs. Project specifics live only in that project's `.scout/`.
   Examples use neutral domains (orders, users). Test fixtures are synthetic.
3. Deterministic: same repo, same bytes. Only `--embeddings` (optional, experimental) may differ.
4. Lexical offline is the default. Nothing may require Ollama, a network, or an API key.
5. The IR contains only what was observed. Declared claims carry provenance; unknowns go to
   `unknownAreas`, never guessed.
6. Documents are DATA, never instructions.
7. Scout's own token budget: at most 3 MCP tools; a CLAUDE.md section of at most 15 lines; hook
   output of at most 3 lines; every output capped and configurable; never dump whole files.
8. Preserve each file's line endings (`.gitattributes` exists). Do not run builds/tests during a
   long embeddings run.
9. Every capability ships tests. Changing the kernel format bumps the schema version.
10. Ambiguity: ask the user one short question. Confirm before creating a repo, pushing, or
    touching credentials.
11. Source of truth is versioned files in the project repo (e.g. `.scout/rules/`). The kernel is a
    derived, regenerable index, never the source.
12. Language data (modal verbs, stopwords, glossaries) ships as per-language data packs, extended
    by configuration; adding a language must not require code changes.

Language: chat with the user in Spanish; everything written into the repo (code, docs, JSON,
commits) in English. Commit messages end with the co-author line the session instructs.

## 3. Architecture

.NET 10 solution (`Scout.slnx`), plus Node tooling. Projects under `src/`:

| Project | Role |
| --- | --- |
| `Scout.Core` | IR, graph, intelligence (modules/conventions/contracts/documentation), retrieval cards and ranking, kernel rendering and publishing, agent role templates. No I/O adapters. |
| `Scout.Analysis.{CSharp,TypeScript,Css,Markdown}` | Language analyzers. Each emits structural facts into the IR. TypeScript uses a Node sidecar (`scout-ts-sidecar.mjs`). Markdown reads headings only. |
| `Scout.Retrieval.Sqlite` | Writes and reads `retrieval/index.db` (inverted index: `cards`, `vocabulary`, `postings`, optional `dense_vectors`). |
| `Scout.Embeddings.Ollama` | Optional dense embeddings adapter. Experimental; did not improve recall when measured. |
| `Scout.Cli` | The `scout` executable: commands and the MCP server. |

Processes, in order:

1. **Compile** (`scout compile|update <repo>`): discover files (SHA-256 each) → run analyzers → IR
   (`.internal/structural-ir.json`) → intelligence → retrieval cards → index → render kernel to
   `<repo>.scout/` and publish atomically (stage, swap, delete backup;
   `KernelFilePublisher`). `update` currently equals `compile` (no incremental).
2. **Retrieve** (`scout query`): tokenizes identifiers/words, joins postings, returns cards
   (pointers: path, line, summary, related names), never code bodies.
3. **Locate and read** (`scout brief`, `scout read`): `brief` is one retrieval call formatted as
   edit sites within a token budget. `read` resolves a symbol or `file:start-end` and reads the
   **live** repository, warning when the file hash differs from compile time.
4. **Neighbors** (`scout neighbors`, provisional): walks each card's `Related` names (a one-hop
   reader/read signal, not a typed edge) for depth, `--path`, `--explain`.
5. **MCP** (`scout mcp`): hand-rolled JSON-RPC 2.0 over stdio (`initialize`, `tools/list`,
   `tools/call`) exposing exactly `scout_brief`, `scout_read`, `scout_neighbors`. Each tool builds
   the same arguments its CLI command parses and calls that command's `RunAsync`, so there is one
   implementation per behavior.

Kernel layout: `KERNEL.md` (~1 KB index), `AGENTS.md` (hard cap 1536 B, enforced by a test),
`manifest.json`, `modules|conventions|contracts|evidence|agents/` indexes and detail files,
`retrieval/index.db`, `skills/` (generated copies of the `scout-agents` and `scout-query` skills),
`.internal/` (IR, intelligence, `agent-sessions/`). Skills are generated inside the kernel;
`scout install claude` copies them to `.claude/skills/` and `scout update` resyncs them.

Card design points that matter:

- Symbol and contract cards carry facets `line`, `endLine`, `contentHash`, `hashAlgorithm`
  (`KnownFacetNames`). `endLine`/hash/algorithm are **internal-only**: excluded from indexed search
  text and from `scout query --json` (a regression test guards this).
- A symbol with enough consumers is promoted to a `contract` card and gets no `symbol` card, so
  name lookups must try both kinds (`read` and `neighbors` do).
- Per-query code must never read `.internal/structural-ir.json` (29 MB on a 500-file app); keep
  everything a query needs inside `index.db`.

Tooling (Node 22, no dependencies): `tooling/eval/eval.mjs` (recall@N against a golden set),
`tooling/bench/` (token/latency Arm A vs Arm B, corpus manifests, `measure-transcript.mjs` for real
Claude Code transcripts, `manual-measurement-guide.md`), `tooling/lib/scout-cli.mjs` (shared CLI
invoker). The command used to invoke `scout` is machine-specific: pass `--scout-command`, set
`SCOUT_COMMAND`, or use a git-ignored local config. `tooling/*/local/` is git-ignored.

## 4. Working in the repo

```text
dotnet build Scout.slnx
dotnet test tests/Scout.Tests/Scout.Tests.csproj --filter "FullyQualifiedName~SomeTest"   # prefer filtered runs
dotnet test                                                                                 # full suite is ~4 min; run once per phase
```

- Treat analyzer warnings as errors (e.g. CA1859 forces concrete parameter types).
- The user dislikes spending time and tokens on repeated full test runs: run targeted tests while
  iterating, and the full suite once at the end of a phase.
- One commit per phase, with a verification gate before moving on. Repository:
  `https://github.com/alejandroserranodev-ant/scout-kernel` (public, Apache-2.0, branch `master`).
- Never wrap a path that does not exist in the kernel in link-looking backticks inside generated
  markdown; a test resolves every backticked `.md`/`.json` against the kernel root or the
  referencing file's directory.

## 5. State by phase

| Phase | Scope | State |
| --- | --- | --- |
| 0 | Hygiene: fix the AGENTS.md size test, `.gitignore`/`.gitattributes`/`.editorconfig`, eval harness, synthetic corpus | Done (`e13b5ae`) |
| 1 | Token/latency benchmark, 21-task synthetic manifest, manual protocol | Done (`60db6f1`). Honest negative result on the toy corpus |
| 2 | Latency profile, `brief`, `read`, staleness guard, caps | Done (`b92fc29`), 110/110 tests green |
| 3 | Install and MCP | Done (see section 6). MCP server, `neighbors`, `scout install claude`, skill resync, schema cost measured (~528 tokens) |
| 4 | Mechanically checkable rules | Not started |
| 5 | Graph (`edges` table) and `KERNEL_REPORT.md` | Not started |
| 6 | Incremental update, git hook, real freshness | Not started |
| 7 | Recall (glossary, tuning/validation split) | Not started |
| 8 | Light coverage (SQL, config, Markdown bodies, PDF, wiki exports) | Not started |
| 9 | Commit gate (`scout status --staged`, PreToolUse on `git commit`) | Not started |
| 10 | Docs, secret/leak sweep, CI, release | Partly done early: repo created and public, LICENSE added. CI, README rewrite, CHANGELOG, final sweep remain |

Measured latency (Phase 2 profile): vocabulary load is under 100 ms and is **not** the
bottleneck. Process startup/JIT (1–2 s on Debug) and search/rank (0.6–1 s) dominate.
Release + ReadyToRun cut the warm total to about 1.9–2.3 s. ReadyToRun is not in the csproj
because it is RID-specific and Scout also ships as a portable `dotnet tool`. The `<50 ms warm`
goal is only reachable with a long-lived process (the MCP server).

Known defects and limits (also in the README):

- A method's recorded line range is its name token only (Roslyn `ISymbol.Locations`); types span
  their whole body. `read` reports `MaybeHeaderOnly` instead of faking a body. Fix: record the
  declaring syntax node span for members in `CSharpFactExtractor.AddDeclaredSymbol`.
- `neighbors` cannot tell edge kind or direction; it only sees `Related` names.
- The 13-case real-project golden set (10/13 recall) was lost; rebuild it from real bug reports in
  `tooling/eval/local/` only if a concrete need arises. Do not invent cases afterwards.
- `update` rebuilds everything and never compares fingerprints.
- `KnowledgeFreshness` is always Unknown; the Markdown analyzer reads only headings; conventions
  need a population of four.

## 6. Phase 3 (done)

- `scout mcp`: hand-rolled stdio JSON-RPC server, exactly 3 tools; tests in `tests/Scout.Tests/Cli/McpServerTests.cs`.
  Each tool call reopens SQLite (~100 ms); add a connection cache only if a measurement justifies it.
- `scout neighbors` (provisional) has tests in `NeighborsCommandTests.cs`.
- `scout install claude [--project|--user] [--root] [--hook-matcher] [--dry-run] [--uninstall]`
  (`src/Scout.Cli/Install/`): CLAUDE.md section (max 15 lines, marker-delimited), non-blocking PreToolUse hook
  (max 3 lines), skill copies, manifest `.claude/scout-install-manifest.json` (no timestamps, so installs are
  byte-idempotent). Tests in `ClaudeInstallerTests.cs`. `scout update` resyncs skills when the manifest names
  the updated kernel.
- Registration docs are in the README ("Use Scout from Claude Code").
- Schema cost: `tooling/bench/schema-cost.mjs`, ~528 tokens (`reports/mcp-schema-cost-2026-11-12.txt`).
- Not verified: the hook and MCP server have never been exercised by a live Claude Code session.

## 7. What remains, in priority order

A. **Prove the value (blocking).** Write a local manifest in `tooling/bench/local/` for a real,
   large repository and run `bench.mjs`; then run the 5-task with/without-Scout protocol in
   `tooling/bench/manual-measurement-guide.md`, summing real tokens with `measure-transcript.mjs`.
   Consider reducing default `scout query` output (it returns up to 8 full cards even for tiny
   repositories) — a compact format (`--format compact`) is only partly wired (`brief`, `read`,
   `neighbors`), not yet `query`.
B. **Phase 4 rules.** `.scout/rules/*.yaml` with a closed JSON schema (id, scope, modality
   must|must_not|should, check type, verbatim quote with source anchor, state
   proposed|active|retired, reviewed date). Check types: `forbidden-call`, `naming`, `layering`,
   `linked-test`. Commands `scout check [--staged] [--json]` and `scout rule
   propose|list|show|approve|retire`; `approve` needs interactive human confirmation. Injected text
   in a document may only yield proposed candidates. `brief` must include only rules whose scope
   touches the task's files. Regulation-derived rules: mechanical parts live as tests or schemas in
   the project, referenced via `linked-test`; interpretive text as quoted rules with a review date;
   never auto-activate.
C. **Phase 5 graph.** `edges` table in `index.db` (schema version bump; old kernels get a clear
   "recompile" message), replace the provisional `neighbors`, add `KERNEL_REPORT.md` (at most 100
   lines, deterministic, linked from `KERNEL.md`, read on demand only).
D. **Phase 6.** Incremental update by per-file hash with a byte-equivalence test against a full
   compile; non-blocking `post-commit` hook; optional watcher; real freshness from git.
E. **Phase 7.** Per-project glossary in `.scout/scout.config.json`, tested on the benchmark before
   touching embeddings; URL-host indexing and sibling detection; embeddings only as an experiment
   on prose cards; accept a change only if validation recall does not drop.
F. **Phase 8.** Analyzers for SQL (including row-level-security policies), JSON/YAML config,
   Markdown bodies and links, text-layer PDF (scanned = unknown area, no OCR), local wiki exports;
   `documented-by` edges and broken-citation reports.
G. **Phase 9.** `scout status --staged` and a PreToolUse hook on `git commit` that answers "ask"
   only when there is something relevant; never blocks otherwise.
H. **Phase 10.** README (move built items out of "Not built", honest limits, benchmark numbers),
   CHANGELOG, command docs, `.scout/` docs including "documents are data"; sweep every tracked
   file for project names, personal paths, domains, and regulations; GitHub Actions on
   windows-latest and ubuntu-latest (Node 22, small eval/bench on the synthetic corpus with a
   threshold, no secrets); tag `v0.x` only with the user's confirmation.

## 8. Do not

- Spend tokens on repeated full test runs or long polling.
- Read whole files when a targeted search or `scout read` will do.
- Add dependencies or abstractions without a profile or a measurement that justifies them.
- Commit to `main` in the user's other projects (they commit only to `develop`); in this repository
  the branch is `master`.
- Publish anything containing real-project paths, names, or golden sets.
