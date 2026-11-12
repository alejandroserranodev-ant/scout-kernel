// Measures what Scout's MCP tool schemas add to an agent session: the size of the `tools/list`
// answer, converted to approximate tokens with the same divisor the benchmark uses.
//
//   node tooling/bench/schema-cost.mjs --scout-command "dotnet <path-to-scout.dll>" [--budget <tokens>]

import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolveScoutCommand } from '../lib/scout-cli.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const config = JSON.parse(readFileSync(join(here, 'config.json'), 'utf8'));

const args = process.argv.slice(2);
const option = (name) => {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : undefined;
};

const command = resolveScoutCommand(option('--scout-command'), undefined);
if (!command) {
  console.error('error: pass --scout-command or set SCOUT_COMMAND.');
  process.exit(2);
}
const budgetTokens = option('--budget') === undefined ? null : Number(option('--budget'));

const request = JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list' }) + '\n';
const run = spawnSync(command[0], [...command.slice(1), 'mcp'], { input: request, encoding: 'utf8' });
if (run.status !== 0) {
  console.error(`error: scout mcp failed (exit ${run.status}): ${run.stderr}`);
  process.exit(1);
}

const tools = JSON.parse(run.stdout.split('\n').find((line) => line.trim().length > 0)).result.tools;
const rows = tools.map((tool) => {
  const chars = JSON.stringify(tool).length;
  return { name: tool.name, chars, tokens: Math.ceil(chars / config.charsPerToken) };
});
const totalChars = rows.reduce((sum, row) => sum + row.chars, 0);
const totalTokens = Math.ceil(totalChars / config.charsPerToken);

for (const row of rows) {
  console.log(`${row.name.padEnd(18)} ${String(row.chars).padStart(6)} chars  ~${row.tokens} tokens`);
}
console.log(`${'TOTAL'.padEnd(18)} ${String(totalChars).padStart(6)} chars  ~${totalTokens} tokens (${config.charsPerToken} chars/token)`);
if (budgetTokens !== null) {
  const verdict = totalTokens <= budgetTokens ? 'within' : 'OVER';
  console.log(`budget: ${budgetTokens} tokens -> ${verdict}`);
  process.exit(totalTokens <= budgetTokens ? 0 : 1);
}
