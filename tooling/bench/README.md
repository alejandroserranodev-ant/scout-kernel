# Token/latency benchmark: Arm A (no Scout) vs Arm B (with Scout)

Answers one question: for a given task, does asking Scout cost fewer tokens, fewer tool calls,
and less time than the naive alternative (grep the repo, then read what matched), at the same
or better accuracy? If not, per the top-level plan, work stops and this is reported rather than
built further on top of.

## Running it

```
node tooling/bench/bench.mjs --manifest tooling/bench/corpora/synthetic/manifest.json \
  --scout-command "dotnet <path-to-scout.dll>"
```

Add `--report out.json` to also write the full per-task data. `--scout-command` (or
`SCOUT_COMMAND`, or a git-ignored local config's `scoutCommand` field) is required and never
versioned, same convention as `tooling/eval/`.

## What each arm measures

- **Arm A** — a deterministic simulation (`engine.mjs`, `simulateArmA`) of the cheapest naive
  strategy: derive search terms from the task's own words, grep every source file in the
  repository for them (one call), then read every file that matched, in full (one call per
  file, split into ranged reads past a configurable size — see `tooling/bench/config.json`).
  This is a stand-in for an agent without Scout, not a recorded trace.
- **Arm B** — the real `scout query` CLI, one call. The token cost is the literal length of the
  JSON it returns, because that is exactly what would enter an agent's context.

Both arms report calls, approximate tokens (`chars / charsPerToken`, configurable), and whether
the task's `expectedFiles` were found.

## Corpora

- `corpora/synthetic/` — a made-up "orders" system (2 projects, 6 files), versioned, with 21
  declared tasks across `concrete`/`vague`/`cross-cutting` types. Safe for CI.
- `local/` — git-ignored; point a manifest here at a real, larger project. See `local/README.md`
  for why this matters more than it sounds: **the synthetic corpus is currently too small to
  show Scout's benefit** (see `reports/synthetic-baseline-2026-11-09.txt`) — a 6-file repository
  can't exercise the "reading the whole thing is expensive" regime Scout exists for.
- `manual-measurement-guide.md` + `measure-transcript.mjs` — a protocol and a script for
  measuring 5 real tasks in an actual Claude Code session (with and without Scout), reading the
  real token counts Claude Code itself records. This is the measurement that actually validates
  or refutes what the simulated arms can only estimate.

## Decision rule

`tooling/bench/config.json`'s `minimumTokenSavingsRate` (proposed: `0.3`) is the threshold the
top-level plan's Phase 1 decision rule checks against, at equal-or-better hit rate. `bench.mjs`
prints and (with `--report`) records a `PASS`/`STOP_AND_REPORT` verdict either way, but per the
plan this rule is only *enforced* as a stop condition after Phase 3 (once `scout brief`/`scout
read` exist) — before that, Scout's only per-task capability is `scout query`, so a result here
is informational, not yet a reason to halt the rest of the plan.
