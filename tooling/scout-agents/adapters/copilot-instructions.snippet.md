# Adapter: GitHub Copilot (.github/copilot-instructions.md)

GitHub Copilot Chat reads `.github/copilot-instructions.md` as repo-wide custom
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
