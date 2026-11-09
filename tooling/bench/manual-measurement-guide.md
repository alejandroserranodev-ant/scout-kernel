# Measuring 5 real Claude Code tasks, with and without Scout

The simulated Arm A/B comparison in `bench.mjs` is a mechanical stand-in for "a naive
Grep-then-Read agent" versus "one `scout query` call." It is useful for fast, deterministic,
CI-safe comparisons, but it is not an actual agent. This protocol measures the real thing: an
actual Claude Code session, doing an actual task, with the real token accounting Claude Code
itself records.

## Protocol

Pick 5 real tasks against a real project (bug fixes, small features, "where does X happen" —
whatever you'd normally ask Claude Code to do). For each task, run it **twice**, each time as
its own fresh session (`/clear` or a new terminal — not a continuation, so the two runs don't
share cached context):

1. **Without Scout** — no `.scout` kernel available, or the Scout MCP/skill not installed, so
   Claude Code falls back to Grep/Read/Glob the way it would on any other repository.
2. **With Scout** — the project's kernel compiled and (once Phase 3 lands) installed, so Claude
   Code can use it.

Let each run finish the task, then find its transcript file:

```
~/.claude/projects/<project-slug>/<session-id>.jsonl
```

(On this machine that's `C:\Users\tentu\.claude\projects\<project-slug>\<session-id>.jsonl` —
the most recently modified file in that directory right after you finish a session is almost
always the one you just ran.)

Measure it:

```
node tooling/bench/measure-transcript.mjs --transcript "<path-to-session>.jsonl"
```

## Recording results

There's no fixed schema enforced here — paste the "Context tokens" and "Total tokens" lines
`measure-transcript.mjs` prints for each of the 10 runs (5 tasks × 2 arms) into a table, next to
whether each run actually produced a correct fix/change. Keep that table in your own notes or in
a git-ignored file under `tooling/bench/local/` (see `local/README.md`) — it is about to name a
real project's tasks, which is exactly the kind of thing that stays off this machine's git
history.

## Reading the result

This manual comparison is the one that actually validates (or refutes) what the simulated
benchmark can only estimate. If the real sessions show a token-savings rate below
`minimumTokenSavingsRate` in `tooling/bench/config.json` at equal-or-better task success, that
is the signal the top-level plan's decision rule is watching for.
