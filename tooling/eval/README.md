# Retrieval recall harness

`eval.mjs` measures whether `scout query` actually returns the files a task needs — recall@N
against a golden set of `{query, expectedFiles}` cases, run against a real compiled kernel
through the real CLI (so latency numbers are real too, not simulated).

It is generic: every path comes from a config file and a golden set, both supplied by the
caller. Nothing in the script names a specific repository, project, or language.

## Running it against the synthetic corpus (versioned, safe for CI)

```
dotnet <path-to-scout.dll> compile tooling/eval/corpora/synthetic/repo --output tooling/eval/corpora/synthetic/repo.scout
node tooling/eval/eval.mjs --config tooling/eval/corpora/synthetic/eval.config.json --scout-command "dotnet <path-to-scout.dll>"
```

`repo.scout` is generated, not committed (it matches the root `*.scout/` ignore rule).

The synthetic corpus under `corpora/synthetic/repo/` is a small made-up "orders" system (a
C# service/repository pair, a billing calculator, and a few TypeScript/React files) created
only to have something safe to measure recall against in CI. It is not based on, and does not
reference, any real project.

## Running it against a real project (local machine only)

A real project's golden set usually encodes knowledge of that project's bug reports and file
layout, which is not something to publish. Point `--config` at a config file under
`tooling/eval/local/` instead — that whole directory is git-ignored (see `local/README.md`).

## Config shape

```json
{
  "kernelPath": "repo.scout",
  "goldenSetPath": "golden.json",
  "limit": 5,
  "minimumFullRecallRate": 1.0
}
```

`kernelPath` and `goldenSetPath` are resolved relative to the config file. `scoutCommand` (how
to invoke the CLI — e.g. `["dotnet", "path/to/scout.dll"]`) is deliberately **not** part of a
versioned config, because the right value is specific to this machine's build. Supply it with
`--scout-command`, the `SCOUT_COMMAND` environment variable, or a `scoutCommand` field in a
git-ignored local config. `minimumFullRecallRate` is optional; when set, the harness exits
non-zero if the fraction of fully-recalled cases falls below it (useful as a CI gate once a
real benchmark threshold has been agreed — see `tooling/bench/` for the token/latency side of
that decision).

## Golden set shape

```json
[
  { "query": "create a new order", "expectedFiles": ["src/Orders/OrderService.cs"] }
]
```

`kind` is optional (`contract|module|symbol|documentation|text|file`), matching `scout query
--kind`.
