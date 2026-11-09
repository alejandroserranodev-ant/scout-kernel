# Local eval configs (git-ignored)

This whole directory is listed in the root `.gitignore` (`tooling/eval/local/`). Put a real
project's eval config and golden set here when you want to measure recall against it — nothing
you add under this directory ever gets committed.

Suggested layout, mirroring `tooling/eval/corpora/synthetic/`:

```
tooling/eval/local/
  <project-name>/
    eval.config.json   # { "kernelPath": "...", "goldenSetPath": "golden.json", "scoutCommand": [...] }
    golden.json         # [{ "query": "...", "expectedFiles": ["..."] }, ...]
```

`kernelPath` can point anywhere on disk (absolute path, or relative to this config file) — it
does not need to live inside the Scout repository.

## Status (2026-11-09)

The original 13-case golden set measured against `vheltrix-current.scout` (10/13 recall,
documented in project memory) lived only in a previous session's scratchpad and was lost when
that scratchpad was cleaned up before this repository existed. It is not reconstructed here:
redoing it honestly means re-deriving cases from real bug reports, not guessing plausible
queries after the fact. Rebuild it here, in this git-ignored folder, the next time there is a
concrete need to measure recall against a real project.
