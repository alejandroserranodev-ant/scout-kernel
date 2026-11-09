#!/usr/bin/env node
// scout-agents — a tool-agnostic session tracker for the role graph a Scout kernel
// publishes in agents/index.json. It runs on plain Node.js: no .NET, no network,
// no dependency on any specific AI CLI. Any agent that can run a shell command
// (Claude Code, Codex, Copilot, Cursor, a human) can drive it.
//
// It does not call an LLM and it does not run the roles. It only answers two
// questions on request: "whose turn is it, and what are they bound to do", and
// "record that control moved, and remember it after this process exits".
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

function roleView(kernelDir, index, session, roleId) {
  const item = findRole(index, roleId);
  const detail = loadRoleDetail(kernelDir, index, roleId);
  return {
    id: item.id,
    role: item.role,
    mandate: detail.mandate,
    reads: (detail.reads || []).map((r) => path.join(kernelDir, r)),
    retrieval: retrievalHints(kernelDir, detail),
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
  if (view.reads.length) {
    lines.push('reads (open only these, and only what retrieval names beyond them):');
    for (const r of view.reads) lines.push(`  - ${r}`);
  }
  if (view.retrieval.length) {
    lines.push('retrieval (run before opening source):');
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

function cmdStart(args) {
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
  printView(roleView(kernelDir, index, session, entryRoleId), args);
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

function cmdStatus(args) {
  const { kernelDir, session, index } = loadSessionContext(args);
  printView(roleView(kernelDir, index, session, currentRole(session)), args);
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

function cmdAdvance(args) {
  const { kernelDir, sessionFile, session, index } = loadSessionContext(args);
  const to = typeof args.to === 'string' ? args.to : fail('--to <roleId> is required.');
  const handoff = requireHandoff(kernelDir, index, session, to, 'sequential');
  const from = currentRole(session);
  session.stack[session.stack.length - 1] = to;
  session.history.push({ at: nowIso(), event: 'advance', from, to, mode: 'sequential', condition: handoff.condition, note: args.note || '' });
  saveSession(sessionFile, session);
  printView(roleView(kernelDir, index, session, to), args);
}

function cmdOpenSubsession(args) {
  const { kernelDir, sessionFile, session, index } = loadSessionContext(args);
  const to = typeof args.to === 'string' ? args.to : fail('--to <roleId> is required.');
  const handoff = requireHandoff(kernelDir, index, session, to, 'subsession');
  const from = currentRole(session);
  session.stack.push(to);
  session.history.push({ at: nowIso(), event: 'subsession-open', from, to, condition: handoff.condition, note: args.note || '' });
  saveSession(sessionFile, session);
  printView(roleView(kernelDir, index, session, to), args);
}

function cmdReturn(args) {
  const { kernelDir, sessionFile, session, index } = loadSessionContext(args);
  if (session.stack.length < 2) {
    fail(`Nothing to return to: '${currentRole(session)}' is the top-level role in this session.`);
  }
  const from = session.stack.pop();
  const to = currentRole(session);
  session.history.push({ at: nowIso(), event: 'subsession-return', from, to, note: args.note || '', result: args.result || '' });
  saveSession(sessionFile, session);
  process.stdout.write(`returned from '${from}' to '${to}'.\n\n`);
  printView(roleView(kernelDir, index, session, to), args);
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

main();
