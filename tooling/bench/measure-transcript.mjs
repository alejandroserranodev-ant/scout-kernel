#!/usr/bin/env node
// Sums the real token usage recorded in a Claude Code session transcript (the .jsonl file
// Claude Code itself writes under its project directory, one line per event). Generic: this is
// the host tool's own log format, not anything specific to a project or to Scout.
//
// Usage:
//   node measure-transcript.mjs --transcript <path-to-session>.jsonl
//
// Prints the total input/output/cache tokens across every assistant turn in the file. Point it
// at a transcript recorded for a single task (see manual-measurement-guide.md) to get that
// task's real token cost.

import { readFileSync } from 'node:fs';

function fail(message) {
  console.error(`error: ${message}`);
  process.exit(1);
}

function parseArgs(argv) {
  const args = { transcriptPath: null };
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (token === '--transcript') {
      args.transcriptPath = argv[++i];
    } else {
      fail(`unrecognized argument '${token}'`);
    }
  }
  if (!args.transcriptPath) {
    fail('--transcript <path-to-session>.jsonl is required');
  }
  return args;
}

function sumUsage(transcriptPath) {
  const lines = readFileSync(transcriptPath, 'utf8').split('\n').filter((line) => line.length > 0);
  const totals = { inputTokens: 0, cacheCreationTokens: 0, cacheReadTokens: 0, outputTokens: 0 };
  let assistantTurns = 0;

  for (const line of lines) {
    const event = JSON.parse(line);
    const usage = event.message?.usage;
    if (!usage) {
      continue;
    }
    assistantTurns += 1;
    totals.inputTokens += usage.input_tokens ?? 0;
    totals.cacheCreationTokens += usage.cache_creation_input_tokens ?? 0;
    totals.cacheReadTokens += usage.cache_read_input_tokens ?? 0;
    totals.outputTokens += usage.output_tokens ?? 0;
  }

  return { assistantTurns, totals };
}

function main() {
  const { transcriptPath } = parseArgs(process.argv.slice(2));
  const { assistantTurns, totals } = sumUsage(transcriptPath);
  const contextTokens = totals.inputTokens + totals.cacheCreationTokens + totals.cacheReadTokens;

  console.log(`Transcript:        ${transcriptPath}`);
  console.log(`Assistant turns:   ${assistantTurns}`);
  console.log(`Input tokens:      ${totals.inputTokens}`);
  console.log(`Cache creation:    ${totals.cacheCreationTokens}`);
  console.log(`Cache read:        ${totals.cacheReadTokens}`);
  console.log(`Context tokens:    ${contextTokens}  (input + cache creation + cache read)`);
  console.log(`Output tokens:     ${totals.outputTokens}`);
  console.log(`Total tokens:      ${contextTokens + totals.outputTokens}`);
}

main();
