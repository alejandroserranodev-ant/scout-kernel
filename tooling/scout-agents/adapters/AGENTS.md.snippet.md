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
