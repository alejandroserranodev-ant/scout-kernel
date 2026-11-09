# Local benchmark manifests (git-ignored)

This whole directory is listed in the root `.gitignore` (`tooling/bench/local/`, except this
file). A manifest for a real project encodes knowledge of that project's layout and likely
belongs to someone else's codebase, so it never leaves this machine.

Layout, mirroring `tooling/bench/corpora/synthetic/`:

```
tooling/bench/local/
  <project-name>/
    manifest.json   # { "corpusName": "...", "repositoryPath": "...", "kernelPath": "...", "tasks": [...] }
```

`repositoryPath` and `kernelPath` can be absolute, or point outside this repository entirely
(e.g. at `C:\Juanca\vheltrix` and `C:\Juanca\vheltrix-current.scout`).

## Why this matters more than the synthetic corpus

The versioned synthetic "orders" corpus (`tooling/bench/corpora/synthetic/`) is 6 files. Measured
on it (2026-11-09), Scout currently comes out **more expensive**, not cheaper: a `scout query`
response lists up to `resultLimit` cards with a full set of fields each, and on a repository this
small that JSON is bigger than just reading the one small file a naive Grep-then-Read agent would
have found anyway. See the root README's Phase 1 report for the numbers.

That is an honest result, not a flaw in the harness: Scout's value proposition is specifically
that reading fewer, smaller things beats reading whole files *once files and repositories get
large enough that a full read is itself expensive*. A 6-file toy repository can't exercise that
regime. Before trusting the decision rule (`minimumTokenSavingsRate` in `tooling/bench/config.json`),
run `bench.mjs` against a manifest here pointing at a real, larger repository — the kind of size
Scout is actually meant for — and also run the manual Claude Code comparison described in
`../manual-measurement-guide.md`.
