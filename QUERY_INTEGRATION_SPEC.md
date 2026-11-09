# Scout Query Integration Specification

**Goal:** Enable `scout-agents` roles to programmatically invoke `scout-query` for efficient retrieval without manual intervention or token waste.

## Design Principles

1. **Independence** — `scout-query.mjs` remains a standalone CLI tool
2. **Efficiency** — Query results are compact JSON, optimized for token usage
3. **Programmatic** — `scout-agents.mjs` can invoke `scout-query` as a subprocess or module
4. **No duplication** — Single shared query engine, dual interfaces (CLI + internal)

## Specification

### 1. Query Result Format (Compact)

```json
{
  "query": "admin contracts",
  "hits": [
    {
      "n": "Admin",
      "k": "shared-type",
      "p": "packages/domain/src/platform/admins.ts",
      "d": "contracts/contract-xxx.json",
      "s": 0.95
    }
  ],
  "count": 101,
  "truncated": false
}
```

**Token savings:**
- `n` instead of `name` (-4 chars per hit)
- `k` instead of `kind` (-4 chars per hit)
- `p` instead of `path` (-4 chars per hit)
- `d` instead of `detail` (-7 chars per hit)
- `s` instead of `score` (-4 chars per hit)

For 10 hits: **~230 tokens saved** vs verbose JSON.

### 2. Scout-Agents Integration

#### Option A: Subprocess (Recommended)
```javascript
// In scout-agents.mjs
async function queryRetrieval(kernelDir, searchTerm, options = {}) {
  const result = await execFile('node', [
    './bin/scout-query.mjs',
    searchTerm,
    '--kernel', kernelDir,
    '--limit', options.limit || 8,
    '--json'
  ]);
  return JSON.parse(result.stdout);
}
```

**Pros:**
- `scout-query.mjs` stays 100% independent
- No shared Node modules
- Clear separation of concerns

**Cons:**
- Subprocess overhead (~50ms)

#### Option B: Shared Module (Faster)
Extract query logic to `lib/retrieval-engine.mjs`:
```javascript
// bin/scout-query.mjs
import { queryIndex } from '../lib/retrieval-engine.mjs';

// bin/scout-agents.mjs  
import { queryIndex } from '../lib/retrieval-engine.mjs';
```

**Pros:**
- No subprocess overhead
- Shared code, single source of truth

**Cons:**
- Light coupling between tools

### 3. Agent Retrieval Command Format

Current (Manual):
```json
{
  "kind": "contract",
  "limit": 8,
  "command": "scout query \"<describe what you need>\" --kernel \"...\" --json"
}
```

New (Automatic):
```json
{
  "kind": "contract",
  "limit": 8,
  "query": "<describes what to search for>",
  "auto": true
}
```

Agent role detail file (`agents/architect.json`):
```json
{
  "retrieval": [
    {
      "query": "contracts related to authentication",
      "kind": "contract",
      "limit": 5,
      "auto": true
    },
    {
      "query": "modules that depend on auth",
      "kind": "module",
      "limit": 3,
      "auto": true
    }
  ]
}
```

### 4. Scout-Agents Role Execution Flow

```
1. Load role detail
2. FOR EACH retrieval entry where auto=true:
   a. Call queryRetrieval(kernelDir, entry.query, entry)
   b. Cache result: results[entry.query] = compactHits
3. Build role view with embedded results
4. Print view with inline results (not just suggestions)
```

Example output to user:
```
role: architect
mandate: Identify stability boundaries and module structure

retrieval results:
  [contract, limit=5] "contracts related to authentication"
    - Admin (score: 0.95) @ packages/domain/src/platform/admins.ts
    - NewAdmin (score: 0.92) @ packages/domain/src/platform/admins.ts
    - deleteAuthUserById (score: 0.88) @ apps/web/src/features/admin/admins/auth-accounts.ts

  [module, limit=3] "modules that depend on auth"
    - admin/admins (5 consumers)
    - admin/applications (3 consumers)
    - admin/leads (2 consumers)

obligations:
  - Verify contract count matches manifest (expect 403 total)
  - Check if any contract has >10 consumers (high impact)
```

### 5. Error Handling

If retrieval fails:
```json
{
  "query": "broken syntax $$",
  "error": "Query failed: database locked",
  "fallback": "Retrieval unavailable. Read manifest.json and contracts/index.json manually."
}
```

Agent continues with fallback instructions.

### 6. Token Budget

Per role execution (architect example):
- Without auto queries: ~800 tokens (suggestions + manual work)
- With auto queries (compact): ~600 tokens (inline results)
- **Savings: ~200 tokens/role × 4 roles = ~800 tokens/session**

## Implementation Phases

### Phase 1: Compact Query Module
- Extract query logic to `lib/retrieval-engine.mjs`
- Define compact result schema
- Add `--compact` flag to scout-query CLI

### Phase 2: Scout-Agents Integration  
- Add `queryRetrieval()` function to scout-agents.mjs
- Parse `auto: true` entries in role details
- Cache results, embed in role view
- Fallback to manual instructions on error

### Phase 3: Agent Detail Generation
- Update `AgentTemplateCompiler.cs` to generate `auto: true` queries
- Define standard queries per role (architect searches for contracts, etc.)

## Testing

```bash
# Test compact output
node bin/scout-query.mjs "admin" --kernel vheltrix-current.scout --compact

# Test scout-agents integration (manual)
node bin/scout-agents.mjs start --kernel vheltrix-current.scout --task "review auth contracts"
# Should print inline query results
```

## Backward Compatibility

- Old agent details still work (auto=false or missing = manual instructions)
- CLI stays identical (add --compact as optional)
- No breaking changes to kernel format
