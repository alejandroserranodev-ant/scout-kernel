// Generic token/latency benchmark engine: Arm A (no Scout — a Grep-then-Read agent) against
// Arm B (with Scout — one `scout query` call). Knows nothing about any specific repository;
// everything it touches comes from a corpus manifest and the shared tooling/bench/config.json.
//
// Arm A is a deterministic simulation of the cheapest *naive* strategy, not a recorded human
// or LLM trace: derive search terms from the task's own words, "grep" every source file in the
// repository for them (one call), then "read" every file that matched in full (one call per
// file, split into ranged reads once a file crosses the configured chunk threshold — chunking
// does not reduce the characters read, only raises the call count, same as a real agent reading
// a large file in pieces).
//
// Arm B runs the real `scout query` CLI and measures the raw JSON text it returns — that text
// is exactly what would enter an agent's context, so no simulation is needed on this side.

import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { runScoutQuery } from '../lib/scout-cli.mjs';

export function listSourceFiles(rootDir, config) {
  const files = [];

  function walk(currentDir) {
    for (const entry of readdirSync(currentDir, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        if (!config.excludedDirectoryNames.includes(entry.name)) {
          walk(path.join(currentDir, entry.name));
        }
        continue;
      }
      if (config.sourceFileExtensions.includes(path.extname(entry.name))) {
        const absolutePath = path.join(currentDir, entry.name);
        const content = readFileSync(absolutePath, 'utf8');
        files.push({
          relativePath: path.relative(rootDir, absolutePath).split(path.sep).join('/'),
          absolutePath,
          content,
          contentLower: content.toLowerCase(),
          sizeChars: content.length,
        });
      }
    }
  }

  walk(rootDir);
  return files;
}

export function deriveSearchTerms(query, stopwords) {
  const stopwordSet = new Set(stopwords);
  return [
    ...new Set(
      query
        .toLowerCase()
        .split(/[^a-z0-9]+/)
        .filter((term) => term.length > 1 && !stopwordSet.has(term)),
    ),
  ];
}

function countReadCalls(sizeChars, chunkThreshold, chunkSize) {
  return sizeChars <= chunkThreshold ? 1 : Math.ceil(sizeChars / chunkSize);
}

export function simulateArmA(repositoryFiles, task, config) {
  const terms = deriveSearchTerms(task.query, config.stopwords);
  const matched = repositoryFiles.filter((file) => terms.some((term) => file.contentLower.includes(term)));

  const grepResultChars = matched.length * config.grepResultCharsPerMatch;
  let readChars = 0;
  let readCalls = 0;
  for (const file of matched) {
    readChars += file.sizeChars;
    readCalls += countReadCalls(file.sizeChars, config.armAChunkThresholdChars, config.armAChunkSizeChars);
  }

  const matchedPaths = matched.map((file) => file.relativePath);
  const hit = task.expectedFiles.every((expected) => matchedPaths.includes(expected));

  return {
    calls: 1 + readCalls,
    chars: grepResultChars + readChars,
    hit,
    matchedFiles: matchedPaths,
    searchTerms: terms,
  };
}

export function simulateArmB(command, kernelPath, task, config) {
  const { raw, parsed, elapsedMs } = runScoutQuery(command, kernelPath, task.query, config.resultLimit, task.kind);
  const returnedFiles = parsed.Hits.map((hit) => hit.RelativePath);
  const hit = task.expectedFiles.every((expected) => returnedFiles.includes(expected));

  return {
    calls: 1,
    chars: raw.length,
    hit,
    returnedFiles,
    elapsedMs,
  };
}

export function tokensApprox(chars, config) {
  return chars / config.charsPerToken;
}

export function runBenchmark(command, manifest, repositoryRoot, kernelPath, config) {
  const repositoryFiles = listSourceFiles(repositoryRoot, config);

  return manifest.tasks.map((task) => {
    const armA = simulateArmA(repositoryFiles, task, config);
    const armB = simulateArmB(command, kernelPath, task, config);
    return {
      id: task.id,
      type: task.type,
      query: task.query,
      armA: { ...armA, tokensApprox: tokensApprox(armA.chars, config) },
      armB: { ...armB, tokensApprox: tokensApprox(armB.chars, config) },
    };
  });
}

export function summarize(results, config) {
  const totalArmATokens = results.reduce((sum, r) => sum + r.armA.tokensApprox, 0);
  const totalArmBTokens = results.reduce((sum, r) => sum + r.armB.tokensApprox, 0);
  const armAHits = results.filter((r) => r.armA.hit).length;
  const armBHits = results.filter((r) => r.armB.hit).length;
  const savingsRate = totalArmATokens === 0 ? 0 : 1 - totalArmBTokens / totalArmATokens;
  const averageArmBLatencyMs = results.reduce((sum, r) => sum + r.armB.elapsedMs, 0) / results.length;

  const meetsThreshold = savingsRate >= config.minimumTokenSavingsRate;
  const equalOrBetterAccuracy = armBHits >= armAHits;

  return {
    taskCount: results.length,
    totalArmATokens,
    totalArmBTokens,
    armAHitRate: armAHits / results.length,
    armBHitRate: armBHits / results.length,
    savingsRate,
    averageArmBLatencyMs,
    decision: {
      minimumTokenSavingsRate: config.minimumTokenSavingsRate,
      meetsThreshold,
      equalOrBetterAccuracy,
      verdict: meetsThreshold && equalOrBetterAccuracy ? 'PASS' : 'STOP_AND_REPORT',
    },
  };
}
