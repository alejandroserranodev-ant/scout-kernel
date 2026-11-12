#!/usr/bin/env node
// CLI entrypoint for the Arm A (no Scout) vs Arm B (with Scout) token/latency benchmark.
//
// Usage:
//   node bench.mjs --manifest <path-to-corpus-manifest.json> [--config <path>] [--scout-command "..."] [--report <path.json>]
//
// --config defaults to tooling/bench/config.json (the versioned, project-agnostic thresholds).
// --scout-command works exactly like tooling/eval/eval.mjs's flag of the same name: it is
// machine-specific and never belongs in a versioned manifest (pass it, set SCOUT_COMMAND, or
// put it in a git-ignored tooling/bench/local/*.json config).

import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolveScoutCommand } from '../lib/scout-cli.mjs';
import { runBenchmark, summarize } from './engine.mjs';

const BENCH_ROOT = path.dirname(fileURLToPath(import.meta.url));
const DEFAULT_CONFIG_PATH = path.join(BENCH_ROOT, 'config.json');

function fail(message) {
  console.error(`error: ${message}`);
  process.exit(1);
}

function parseArgs(argv) {
  const args = { manifestPath: null, configPath: DEFAULT_CONFIG_PATH, scoutCommand: null, reportPath: null };
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (token === '--manifest') {
      args.manifestPath = argv[++i];
    } else if (token === '--config') {
      args.configPath = argv[++i];
    } else if (token === '--scout-command') {
      args.scoutCommand = argv[++i];
    } else if (token === '--report') {
      args.reportPath = argv[++i];
    } else {
      fail(`unrecognized argument '${token}'`);
    }
  }
  if (!args.manifestPath) {
    fail('--manifest <path-to-corpus-manifest.json> is required');
  }
  return args;
}

function loadJson(absolutePath) {
  return JSON.parse(readFileSync(absolutePath, 'utf8'));
}

function resolveFromDir(baseDir, maybeRelative) {
  return path.isAbsolute(maybeRelative) ? maybeRelative : path.resolve(baseDir, maybeRelative);
}

function printTaskTable(results) {
  for (const result of results) {
    const armAMark = result.armA.hit ? 'hit ' : 'MISS';
    const armBMark = result.armB.hit ? 'hit ' : 'MISS';
    console.log(
      `${result.id} [${result.type}] "${result.query}"\n` +
        `  Arm A (no Scout): ${armAMark}  ${result.armA.calls} call(s)  ~${Math.round(result.armA.tokensApprox)} tok\n` +
        `  Arm B (Scout):    ${armBMark}  ${result.armB.calls} call(s)  ~${Math.round(result.armB.tokensApprox)} tok  ${Math.round(result.armB.elapsedMs)}ms`,
    );
  }
}

function printSummary(summary) {
  console.log('');
  console.log(`Tasks:                 ${summary.taskCount}`);
  console.log(`Arm A hit rate:         ${(summary.armAHitRate * 100).toFixed(1)}%`);
  console.log(`Arm B hit rate:         ${(summary.armBHitRate * 100).toFixed(1)}%`);
  console.log(`Arm A mean file recall: ${(summary.armAMeanRecall * 100).toFixed(1)}%`);
  console.log(`Arm B mean file recall: ${(summary.armBMeanRecall * 100).toFixed(1)}%`);
  console.log(`Arm A tokens (approx):  ${Math.round(summary.totalArmATokens)}`);
  console.log(`Arm B tokens (approx):  ${Math.round(summary.totalArmBTokens)}`);
  console.log(`Ranked-grep baseline (reads the top result-limit files, equal budget):`);
  console.log(`  hit rate ${(summary.armARankedHitRate * 100).toFixed(1)}%  mean file recall ${(summary.armARankedMeanRecall * 100).toFixed(1)}%  tokens ${Math.round(summary.totalArmARankedTokens)} (Scout: ${Math.round(summary.totalArmBTokens)})`);
  console.log(`Token savings rate:     ${(summary.savingsRate * 100).toFixed(1)}%`);
  console.log(`Arm B avg latency:      ${summary.averageArmBLatencyMs.toFixed(1)}ms`);
  console.log('');
  console.log(
    `Decision rule (minimum ${(summary.decision.minimumTokenSavingsRate * 100).toFixed(0)}% savings at equal-or-better accuracy): ` +
      `${summary.decision.verdict}`,
  );
  if (summary.decision.verdict !== 'PASS') {
    console.log(
      '  (This rule is enforced after Phase 3, once scout brief/read exist; at this stage it is informational.)',
    );
  }
}

function main() {
  const { manifestPath: manifestArg, configPath, scoutCommand: scoutCommandArg, reportPath } = parseArgs(
    process.argv.slice(2),
  );

  const manifestPath = path.resolve(process.cwd(), manifestArg);
  const manifestDir = path.dirname(manifestPath);
  const manifest = loadJson(manifestPath);
  const config = loadJson(configPath);

  const command = resolveScoutCommand(scoutCommandArg, config.scoutCommand);
  if (!command) {
    fail(
      'no scout command: pass --scout-command "dotnet path/to/scout.dll", set SCOUT_COMMAND, ' +
        'or add "scoutCommand" to a git-ignored local config.',
    );
  }

  const minimumTasks = manifest.minimumTaskCount ?? config.minimumTaskCount;
  if (!Array.isArray(manifest.tasks) || manifest.tasks.length < minimumTasks) {
    fail(`manifest at '${manifestPath}' must declare at least ${minimumTasks} tasks (has ${manifest.tasks?.length ?? 0})`);
  }

  const repositoryRoot = resolveFromDir(manifestDir, manifest.repositoryPath);
  const kernelPath = resolveFromDir(manifestDir, manifest.kernelPath);

  const results = runBenchmark(command, manifest, repositoryRoot, kernelPath, config);
  const summary = summarize(results, config);

  console.log(`Corpus: ${manifest.corpusName} (${results.length} tasks)\n`);
  printTaskTable(results);
  printSummary(summary);

  if (reportPath) {
    const report = { corpusName: manifest.corpusName, generatedAt: new Date().toISOString(), config, results, summary };
    writeFileSync(path.resolve(process.cwd(), reportPath), JSON.stringify(report, null, 2));
    console.log(`\nWrote ${reportPath}`);
  }

  if (summary.decision.verdict !== 'PASS') {
    process.exitCode = 1;
  }
}

main();
