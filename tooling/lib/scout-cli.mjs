// Shared helper for invoking the real `scout` CLI as a subprocess from Node tooling
// (tooling/eval and tooling/bench both need exactly this). Generic: knows nothing about any
// specific repository, kernel, or query.

import { spawnSync } from 'node:child_process';

export function resolveScoutCommand(cliValue, configValue) {
  const raw = cliValue ?? process.env.SCOUT_COMMAND ?? configValue;
  if (!raw) {
    return null;
  }
  return Array.isArray(raw) ? raw : raw.split(' ').filter((part) => part.length > 0);
}

// Runs `scout query` and returns both the parsed hits and the raw stdout text, because the raw
// text length is itself a measurement (it is what would enter an agent's context).
export function runScoutQuery(command, kernelPath, query, limit, kind) {
  const args = [...command.slice(1), 'query', query, '--kernel', kernelPath, '--limit', String(limit), '--json'];
  if (kind) {
    args.push('--kind', kind);
  }
  const startedAt = performance.now();
  const result = spawnSync(command[0], args, { encoding: 'utf8' });
  const elapsedMs = performance.now() - startedAt;
  if (result.status !== 0) {
    throw new Error(`scout query failed (exit ${result.status}): ${result.stderr || result.stdout}`);
  }
  return { raw: result.stdout, parsed: JSON.parse(result.stdout), elapsedMs };
}
