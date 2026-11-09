#!/usr/bin/env node
// Generic recall@N harness for a compiled Scout kernel.
//
// Given a config naming a kernel and a golden set of {query, expectedFiles} cases, this runs
// each query through the real `scout query` CLI and reports whether the expected files came
// back in the top N results. It knows nothing about any specific repository: every path comes
// from the config and the golden set, both supplied by the caller.
//
// Usage:
//   node eval.mjs --config <path-to-eval.config.json> [--scout-command "dotnet path/to/scout.dll"] [--limit N]
//
// The command used to invoke the `scout` CLI is machine-specific (a Debug build today, a
// published binary tomorrow), so it is never read from a versioned config: pass it with
// --scout-command, or set the SCOUT_COMMAND environment variable, or put it in a git-ignored
// local config (see tooling/eval/local/README.md).

import { readFileSync } from 'node:fs';
import path from 'node:path';
import { resolveScoutCommand as resolveCommand, runScoutQuery } from '../lib/scout-cli.mjs';

const DEFAULT_RESULT_LIMIT = 8;

function fail(message) {
  console.error(`error: ${message}`);
  process.exit(1);
}

function parseArgs(argv) {
  const args = { configPath: null, limit: null, scoutCommand: null };
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (token === '--config') {
      args.configPath = argv[++i];
    } else if (token === '--limit') {
      args.limit = Number(argv[++i]);
    } else if (token === '--scout-command') {
      args.scoutCommand = argv[++i];
    } else {
      fail(`unrecognized argument '${token}'`);
    }
  }
  if (!args.configPath) {
    fail('--config <path-to-eval.config.json> is required');
  }
  return args;
}

function loadJson(absolutePath) {
  return JSON.parse(readFileSync(absolutePath, 'utf8'));
}

function resolveFromConfig(configDir, maybeRelative) {
  return path.isAbsolute(maybeRelative) ? maybeRelative : path.resolve(configDir, maybeRelative);
}

function resolveScoutCommand(cliValue, config) {
  const command = resolveCommand(cliValue, config.scoutCommand);
  if (!command) {
    fail(
      'no scout command: pass --scout-command "dotnet path/to/scout.dll", set SCOUT_COMMAND, ' +
        'or add "scoutCommand" to a git-ignored local config.',
    );
  }
  return command;
}

function evaluateCase(command, kernelPath, limit, testCase) {
  const { parsed: response, elapsedMs } = runScoutQuery(command, kernelPath, testCase.query, limit, testCase.kind);
  const returnedPaths = response.Hits.map((hit) => hit.RelativePath);
  const returnedSet = new Set(returnedPaths);
  const expected = testCase.expectedFiles;
  const hitCount = expected.filter((file) => returnedSet.has(file)).length;
  return {
    query: testCase.query,
    expected,
    returned: returnedPaths,
    hitCount,
    expectedCount: expected.length,
    elapsedMs,
  };
}

function printReport(results, limit) {
  for (const result of results) {
    const mark = result.hitCount === result.expectedCount ? 'OK  ' : 'MISS';
    console.log(
      `${mark} recall@${limit} ${result.hitCount}/${result.expectedCount}  ${Math.round(result.elapsedMs)}ms  "${result.query}"`,
    );
    if (result.hitCount !== result.expectedCount) {
      const missing = result.expected.filter((file) => !result.returned.includes(file));
      console.log(`     missing: ${missing.join(', ')}`);
    }
  }

  const totalExpected = results.reduce((sum, result) => sum + result.expectedCount, 0);
  const totalHit = results.reduce((sum, result) => sum + result.hitCount, 0);
  const strictHits = results.filter((result) => result.hitCount === result.expectedCount).length;
  const averageLatencyMs = results.reduce((sum, result) => sum + result.elapsedMs, 0) / results.length;

  console.log('');
  console.log(`Cases fully recalled: ${strictHits}/${results.length}`);
  console.log(
    `Files recalled:       ${totalHit}/${totalExpected} (${((totalHit / totalExpected) * 100).toFixed(1)}%)`,
  );
  console.log(`Average latency:      ${averageLatencyMs.toFixed(1)}ms`);

  return strictHits / results.length;
}

function main() {
  const { configPath: configArg, limit: limitArg, scoutCommand: scoutCommandArg } = parseArgs(process.argv.slice(2));
  const configPath = path.resolve(process.cwd(), configArg);
  const configDir = path.dirname(configPath);
  const config = loadJson(configPath);

  if (!config.kernelPath) {
    fail(`config at '${configPath}' is missing "kernelPath"`);
  }
  if (!config.goldenSetPath) {
    fail(`config at '${configPath}' is missing "goldenSetPath"`);
  }

  const command = resolveScoutCommand(scoutCommandArg, config);
  const kernelPath = resolveFromConfig(configDir, config.kernelPath);
  const goldenSetPath = resolveFromConfig(configDir, config.goldenSetPath);
  const limit = limitArg ?? config.limit ?? DEFAULT_RESULT_LIMIT;
  const goldenSet = loadJson(goldenSetPath);

  if (!Array.isArray(goldenSet) || goldenSet.length === 0) {
    fail(`golden set at '${goldenSetPath}' must be a non-empty array of cases`);
  }

  const results = goldenSet.map((testCase) => evaluateCase(command, kernelPath, limit, testCase));
  const fullRecallRate = printReport(results, limit);

  if (config.minimumFullRecallRate !== undefined && fullRecallRate < config.minimumFullRecallRate) {
    console.error(
      `\nFAIL: full-recall rate ${fullRecallRate.toFixed(3)} is below the configured minimum ${config.minimumFullRecallRate}`,
    );
    process.exit(1);
  }
}

main();
