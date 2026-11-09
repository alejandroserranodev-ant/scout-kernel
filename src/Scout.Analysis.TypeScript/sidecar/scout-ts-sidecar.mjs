#!/usr/bin/env node
// Scout TypeScript IR sidecar.
//
// Reads a JSON request on stdin and writes a deterministic structural IR payload on stdout.
// The sidecar never invents facts: anything the TypeScript checker cannot resolve is omitted and,
// when that limits analysis, reported as a diagnostic instead of guessed.
//
// Request:  { "rootPath": "<absolute>", "files": ["src/a.ts", ...] }
// Response: { status, typeScriptVersion, projects, sourceFiles, symbols, relationships, diagnostics }

import { createRequire } from 'node:module';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

const SYMBOL_KIND = { type: 'type', callable: 'callable', value: 'value', textLiteral: 'text-literal' };
const VISIBILITY = { public: 'public', protected: 'protected', private: 'private', unknown: 'unknown' };

// Text literals are the wording a project shows to people: JSX text, JSX attribute values and the strings a
// declaration carries. They are reported so that a question phrased in the product's own words can reach the
// declaration that holds them. The bounds below keep this from turning into an index of source bodies.
const TEXT_NATIVE_KIND = { jsxText: 'jsx-text', jsxAttribute: 'jsx-attribute', string: 'string' };
const TEXT_MIN_LENGTH = 4;
const TEXT_MAX_LENGTH = 200;
const TEXT_LIMIT_PER_FILE = 300;

// Attributes whose value is wiring, not wording.
const NON_TEXT_ATTRIBUTES = new Set([
  'className', 'class', 'style', 'key', 'ref', 'id', 'href', 'src', 'srcSet', 'htmlFor', 'for', 'rel', 'target',
  'role', 'type'
]);

// A run of letters with optional surrounding punctuation: a word as prose writes it.
const PROSE_WORD = /^[¿¡("'“]*\p{L}+['’]?\p{L}*[.,;:!?…)"'”]*$/u;
// A single capitalised word reads as a label ("Calcular"); a lower-case or shouting one is an identifier or a constant.
const LABEL_WORD = /^[¿¡("'“]*\p{Lu}\p{Ll}+[.,;:!?…)"'”]*$/u;

// Marks a relationship endpoint whose declaration belongs to another analyzer. The declaring analyzer
// derives the same identifier from the same logical key; see Scout.Core.Analysis.SharedSymbolKey.
const SHARED_PREFIX = 'shared::';

function readStdin() {
  return new Promise((resolve, reject) => {
    let buffer = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', (chunk) => { buffer += chunk; });
    process.stdin.on('end', () => resolve(buffer));
    process.stdin.on('error', reject);
  });
}

// The repository's own compiler is preferred so Scout observes the code through the exact
// TypeScript version that the repository itself compiles with.
function loadTypeScript(rootPath) {
  const repositoryRequire = createRequire(path.join(rootPath, 'scout-sidecar-resolution.cjs'));
  try {
    return repositoryRequire(repositoryRequire.resolve('typescript', {
      paths: [rootPath, path.join(rootPath, 'node_modules')]
    }));
  } catch {
    try {
      return createRequire(import.meta.url)('typescript');
    } catch {
      return null;
    }
  }
}

function toPosix(value) {
  return value.split(path.sep).join('/');
}

function normalizeText(raw) {
  const collapsed = raw.replace(/\s+/gu, ' ').trim();
  return collapsed.length > TEXT_MAX_LENGTH ? `${collapsed.slice(0, TEXT_MAX_LENGTH - 1)}…` : collapsed;
}

// Decides whether a string is wording rather than wiring. Wording is longer than three characters, holds a
// letter, and either is a single capitalised word or is mostly prose words. Class lists, paths, URLs,
// identifiers and constants fail one of these without any list of names to maintain.
function isProse(text) {
  if (text.length < TEXT_MIN_LENGTH || !/\p{L}/u.test(text)) {
    return false;
  }
  const words = text.split(' ');
  if (words.length === 1) {
    return LABEL_WORD.test(text);
  }
  const proseWords = words.filter((word) => PROSE_WORD.test(word)).length;
  return proseWords >= 2 && proseWords * 2 > words.length;
}

// The language is read from the extension the file actually carries. A .jsx file is JavaScript even
// though the TypeScript compiler parses it, and reporting it as TypeScript would state a fact the
// repository never declared.
function languageOf(relativeFile) {
  const extension = path.posix.extname(relativeFile);
  switch (extension) {
    case '.ts':
    case '.mts':
    case '.cts':
      return 'typescript';
    case '.tsx':
      return 'typescriptreact';
    case '.jsx':
      return 'javascriptreact';
    default:
      return 'javascript';
  }
}

// Only these two extensions may contain JSX. Every other extension parses a `<` as a comparison.
function supportsJsx(relativeFile) {
  const extension = path.posix.extname(relativeFile);
  return extension === '.tsx' || extension === '.jsx';
}

// A project speaks TypeScript when it declares a TypeScript compilation boundary, and JavaScript when
// the only manifest it owns is a package manifest.
function projectLanguageOf(projectPath) {
  return path.posix.basename(projectPath) === 'tsconfig.json' ? 'typescript' : 'javascript';
}

// A project is an observed manifest, never an inferred grouping. When a directory declares both a
// TypeScript and a package manifest, the TypeScript one wins because it defines the compilation boundary.
function resolveProjectPaths(rootPath, relativeFiles) {
  const directories = new Set();
  for (const relativeFile of relativeFiles) {
    let directory = path.posix.dirname(relativeFile);
    for (;;) {
      directories.add(directory);
      if (directory === '.') {
        break;
      }
      const parent = path.posix.dirname(directory);
      directory = parent === directory ? '.' : parent;
    }
  }

  const manifests = [];
  for (const directory of directories) {
    for (const manifest of ['tsconfig.json', 'package.json']) {
      const candidate = directory === '.' ? manifest : `${directory}/${manifest}`;
      if (existsSync(path.join(rootPath, candidate))) {
        manifests.push(candidate);
        break;
      }
    }
  }
  return manifests.sort();
}

function directoryPrefixOf(projectPath) {
  const directory = path.posix.dirname(projectPath);
  return directory === '.' ? '' : `${directory}/`;
}

function findOwnerProject(projectPaths, relativePath) {
  let owner = null;
  let ownerPrefixLength = -1;
  for (const projectPath of projectPaths) {
    const prefix = directoryPrefixOf(projectPath);
    if (relativePath.startsWith(prefix) && prefix.length > ownerPrefixLength) {
      owner = projectPath;
      ownerPrefixLength = prefix.length;
    }
  }
  return owner;
}

function readProjectName(rootPath, projectPath) {
  if (projectPath.endsWith('package.json')) {
    try {
      const manifest = JSON.parse(readFileSync(path.join(rootPath, projectPath), 'utf8'));
      if (typeof manifest.name === 'string' && manifest.name.trim().length > 0) {
        return manifest.name.trim();
      }
    } catch {
      // A malformed manifest is not a structural fact; fall back to the directory name.
    }
  }
  const directory = path.posix.dirname(projectPath);
  return directory === '.' ? path.basename(rootPath) : path.posix.basename(directory);
}

function failure(code, message) {
  return {
    status: 'failed',
    typeScriptVersion: null,
    projects: [],
    sourceFiles: [],
    symbols: [],
    relationships: [],
    diagnostics: [{ severity: 'error', code, message }]
  };
}

export function analyze(request) {
  const rootPath = request.rootPath;
  const analyzedFiles = [...request.files].map(toPosix).sort();
  const ts = loadTypeScript(rootPath);
  if (ts === null) {
    return failure(
      'TS_COMPILER_UNAVAILABLE',
      'The TypeScript compiler could not be resolved from the repository or from the Scout sidecar. ' +
      'Run "npm install typescript" in the analyzed repository to enable TypeScript analysis.');
  }

  const diagnostics = [];
  const program = ts.createProgram(
    analyzedFiles.map((relativeFile) => path.join(rootPath, relativeFile)),
    {
      allowJs: true,
      jsx: ts.JsxEmit.Preserve,
      target: ts.ScriptTarget.Latest,
      module: ts.ModuleKind.ESNext,
      moduleResolution: ts.ModuleResolutionKind.Bundler,
      skipLibCheck: true,
      noEmit: true
    });
  const checker = program.getTypeChecker();

  const projectPaths = resolveProjectPaths(rootPath, analyzedFiles);
  const symbolsByKey = new Map();
  const keysByDeclaredSymbol = new Map();
  const emittedRelationships = new Set();
  const relationships = [];

  function addRelationship(kind, sourceKind, sourceKey, targetKind, targetKey) {
    if (sourceKind === targetKind && sourceKey === targetKey) {
      return;
    }
    const identity = `${kind}|${sourceKind}|${sourceKey}|${targetKind}|${targetKey}`;
    if (emittedRelationships.has(identity)) {
      return;
    }
    emittedRelationships.add(identity);
    relationships.push({ kind, sourceKind, sourceKey, targetKind, targetKey });
  }

  function declarationName(node) {
    return node.name !== undefined && ts.isIdentifier(node.name) ? node.name : undefined;
  }

  function isStyleClassAttribute(attribute) {
    const name = attribute.name;
    if (name === undefined || !ts.isIdentifier(name)) {
      return false;
    }
    return name.text === 'className' || name.text === 'class';
  }

  // Only literal class lists are observable. A computed value depends on runtime state that no static
  // analysis can resolve, so it is left unreported instead of guessed.
  function readStyleClassNames(initializer) {
    if (initializer === undefined) {
      return [];
    }
    let literal = initializer;
    if (ts.isJsxExpression(literal)) {
      if (literal.expression === undefined) {
        return [];
      }
      literal = literal.expression;
    }
    const text = ts.isStringLiteral(literal)
      ? literal.text
      : ts.isNoSubstitutionTemplateLiteral(literal)
        ? literal.text
        : undefined;
    return text === undefined ? [] : text.split(/\s+/u).filter((name) => name.length > 0);
  }

  // Where a string sits decides whether it is wording. Module specifiers, property keys, literal types,
  // directives and tagged templates are syntax or wiring even when their content reads like prose.
  function isTextPosition(node) {
    const parent = node.parent;
    if (parent === undefined) {
      return false;
    }
    if (ts.isImportDeclaration(parent) || ts.isExportDeclaration(parent) || ts.isExternalModuleReference(parent) ||
        ts.isLiteralTypeNode(parent) || ts.isTaggedTemplateExpression(parent) || parent.name === node) {
      return false;
    }
    if (ts.isElementAccessExpression(parent) && parent.argumentExpression === node) {
      return false;
    }
    if (ts.isExpressionStatement(parent)) {
      return false;
    }
    const attribute = enclosingJsxAttribute(node);
    if (attribute === undefined) {
      return true;
    }
    const attributeName = ts.isIdentifier(attribute.name) ? attribute.name.text : '';
    return !NON_TEXT_ATTRIBUTES.has(attributeName) && !attributeName.startsWith('data-');
  }

  // The attribute a value belongs to, whether written as attr="..." or attr={"..."}.
  function enclosingJsxAttribute(node) {
    const parent = node.parent;
    if (ts.isJsxAttribute(parent)) {
      return parent;
    }
    return ts.isJsxExpression(parent) && ts.isJsxAttribute(parent.parent) ? parent.parent : undefined;
  }

  function textNativeKind(node) {
    return enclosingJsxAttribute(node) === undefined ? TEXT_NATIVE_KIND.string : TEXT_NATIVE_KIND.jsxAttribute;
  }

  // Reads the wording a node carries, or null when the node is not wording. Positions are absolute offsets;
  // JSX text spans its surrounding whitespace, so its range is narrowed to the words themselves.
  function readText(node) {
    let raw;
    let nativeKind;
    let start = null;
    let end = null;

    if (ts.isJsxText(node)) {
      if (node.containsOnlyTriviaWhiteSpaces) {
        return null;
      }
      raw = node.text;
      nativeKind = TEXT_NATIVE_KIND.jsxText;
      start = node.pos + (raw.length - raw.trimStart().length);
      end = node.end - (raw.length - raw.trimEnd().length);
    } else if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
      if (!isTextPosition(node)) {
        return null;
      }
      raw = node.text;
      nativeKind = textNativeKind(node);
    } else if (ts.isTemplateExpression(node)) {
      if (!isTextPosition(node)) {
        return null;
      }
      // Only the fixed parts are observable; what a substitution holds depends on runtime state.
      raw = [node.head.text, ...node.templateSpans.map((span) => span.literal.text)].join(' ');
      nativeKind = textNativeKind(node);
    } else {
      return null;
    }

    const text = normalizeText(raw);
    return isProse(text) ? { text, nativeKind, start, end } : null;
  }

  // `model.version` names the member being read. The checker resolves it to the declared property, so the
  // reading declaration is observed to depend on that field and not only on the type that holds it. A name in
  // a declaration position (`name` of a property or function) is still never a use.
  function isMemberAccess(identifier) {
    return ts.isPropertyAccessExpression(identifier.parent) && identifier.parent.name === identifier;
  }

  function resolveAlias(symbol) {
    return symbol !== undefined && (symbol.flags & ts.SymbolFlags.Alias) !== 0
      ? checker.getAliasedSymbol(symbol)
      : symbol;
  }

  function registerDeclaration(node, key) {
    const symbol = checker.getSymbolAtLocation(declarationName(node) ?? node);
    if (symbol === undefined) {
      return;
    }
    keysByDeclaredSymbol.set(symbol, key);
    const aliased = resolveAlias(symbol);
    if (aliased !== undefined && aliased !== symbol) {
      keysByDeclaredSymbol.set(aliased, key);
    }
  }

  function resolveKey(node) {
    const symbol = resolveAlias(checker.getSymbolAtLocation(node));
    if (symbol === undefined) {
      return null;
    }
    const direct = keysByDeclaredSymbol.get(symbol);
    if (direct !== undefined) {
      return direct;
    }
    for (const declaration of symbol.declarations ?? []) {
      const declaredSymbol = checker.getSymbolAtLocation(declarationName(declaration) ?? declaration);
      const declaredKey = declaredSymbol === undefined ? undefined : keysByDeclaredSymbol.get(declaredSymbol);
      if (declaredKey !== undefined) {
        return declaredKey;
      }
    }
    return null;
  }

  function locationOf(sourceFile, node, relativeFile) {
    const start = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
    const end = sourceFile.getLineAndCharacterOfPosition(node.getEnd());
    return {
      relativePath: relativeFile,
      startLine: start.line + 1,
      startColumn: start.character + 1,
      endLine: end.line + 1,
      endColumn: end.character + 1
    };
  }

  function locationBetween(sourceFile, startOffset, endOffset, relativeFile) {
    const start = sourceFile.getLineAndCharacterOfPosition(startOffset);
    const end = sourceFile.getLineAndCharacterOfPosition(endOffset);
    return {
      relativePath: relativeFile,
      startLine: start.line + 1,
      startColumn: start.character + 1,
      endLine: end.line + 1,
      endColumn: end.character + 1
    };
  }

  function containsJsx(node) {
    let found = false;
    const walk = (current) => {
      if (found) {
        return;
      }
      if (ts.isJsxElement(current) || ts.isJsxSelfClosingElement(current) || ts.isJsxFragment(current)) {
        found = true;
        return;
      }
      ts.forEachChild(current, walk);
    };
    walk(node);
    return found;
  }

  // React is observed, never assumed: the file must be able to hold JSX, the declaration must be named
  // like a component, and the body must actually contain JSX. The language id is not the right question
  // here, because both .tsx and .jsx carry JSX.
  function isReactComponent(node, name, relativeFile) {
    return supportsJsx(relativeFile) &&
      name.length > 0 &&
      name[0] === name[0].toUpperCase() &&
      containsJsx(node);
  }

  function classify(node, name, relativeFile) {
    if (ts.isInterfaceDeclaration(node)) {
      return { symbolKind: SYMBOL_KIND.type, nativeKind: 'interface' };
    }
    if (ts.isClassDeclaration(node)) {
      return { symbolKind: SYMBOL_KIND.type, nativeKind: 'class' };
    }
    if (ts.isTypeAliasDeclaration(node)) {
      return { symbolKind: SYMBOL_KIND.type, nativeKind: 'type-alias' };
    }
    if (ts.isEnumDeclaration(node)) {
      return { symbolKind: SYMBOL_KIND.type, nativeKind: 'enum' };
    }
    if (ts.isFunctionDeclaration(node) || ts.isFunctionExpression(node) || ts.isArrowFunction(node)) {
      return {
        symbolKind: SYMBOL_KIND.callable,
        nativeKind: isReactComponent(node, name, relativeFile) ? 'react-component' : 'function'
      };
    }
    if (ts.isConstructorDeclaration(node) || ts.isMethodDeclaration(node) || ts.isMethodSignature(node)) {
      return { symbolKind: SYMBOL_KIND.callable, nativeKind: 'method' };
    }
    if (ts.isPropertyDeclaration(node) || ts.isPropertySignature(node)) {
      return { symbolKind: SYMBOL_KIND.value, nativeKind: 'property' };
    }
    if (ts.isVariableDeclaration(node)) {
      return { symbolKind: SYMBOL_KIND.value, nativeKind: 'variable' };
    }
    return null;
  }

  function memberVisibility(node) {
    const modifiers = ts.getCombinedModifierFlags(node);
    if ((modifiers & ts.ModifierFlags.Private) !== 0 || node.name?.kind === ts.SyntaxKind.PrivateIdentifier) {
      return VISIBILITY.private;
    }
    return (modifiers & ts.ModifierFlags.Protected) !== 0 ? VISIBILITY.protected : VISIBILITY.public;
  }

  function addSymbol(sourceFile, relativeFile, node, name, classification, visibility, containerKey) {
    const key = containerKey === null ? `${relativeFile}::${name}` : `${containerKey}.${name}`;
    const existing = symbolsByKey.get(key);
    if (existing !== undefined) {
      return existing;
    }
    const record = {
      key,
      name,
      qualifiedName: key,
      symbolKind: classification.symbolKind,
      nativeKind: classification.nativeKind,
      visibility,
      languageId: languageOf(relativeFile),
      location: locationOf(sourceFile, node, relativeFile)
    };
    symbolsByKey.set(key, record);
    registerDeclaration(node, key);
    return record;
  }

  for (const projectPath of projectPaths) {
    const ancestors = projectPaths.filter((candidate) =>
      candidate !== projectPath && directoryPrefixOf(projectPath).startsWith(directoryPrefixOf(candidate)));
    const parent = findOwnerProject(ancestors, directoryPrefixOf(projectPath));
    if (parent !== null) {
      addRelationship('project-depends-on', 'project', projectPath, 'project', parent);
    }
  }

  const declaredNodesByFile = new Map();

  for (const relativeFile of analyzedFiles) {
    const owner = findOwnerProject(projectPaths, relativeFile);
    if (owner !== null) {
      addRelationship('contains', 'project', owner, 'source-file', relativeFile);
    }

    const sourceFile = program.getSourceFile(path.join(rootPath, relativeFile));
    if (sourceFile === undefined) {
      diagnostics.push({
        severity: 'warning',
        code: 'TS_SOURCE_FILE_UNAVAILABLE',
        message: `The TypeScript program did not include '${relativeFile}'.`
      });
      continue;
    }

    const declared = [];
    declaredNodesByFile.set(relativeFile, declared);

    const declareMembers = (owningNode, owningKey) => {
      for (const member of owningNode.members ?? []) {
        const memberName = declarationName(member)?.text
          ?? (ts.isConstructorDeclaration(member) ? 'constructor' : undefined);
        if (memberName === undefined) {
          continue;
        }
        const classification = classify(member, memberName, relativeFile);
        if (classification === null) {
          continue;
        }
        const record = addSymbol(
          sourceFile,
          relativeFile,
          member,
          memberName,
          classification,
          memberVisibility(member),
          owningKey);
        addRelationship('defines', 'symbol', owningKey, 'symbol', record.key);
        declared.push({ node: member, key: record.key });
      }
    };

    for (const statement of sourceFile.statements) {
      // Module-scoped declarations are only reachable from other files when they are exported.
      const isExported = (ts.getCombinedModifierFlags(statement) & ts.ModifierFlags.Export) !== 0;
      const visibility = isExported ? VISIBILITY.public : VISIBILITY.private;

      if (ts.isVariableStatement(statement)) {
        for (const declaration of statement.declarationList.declarations) {
          const variableName = declarationName(declaration)?.text;
          if (variableName === undefined) {
            continue;
          }
          const initializer = declaration.initializer;
          const shapeNode = initializer !== undefined &&
            (ts.isArrowFunction(initializer) || ts.isFunctionExpression(initializer))
            ? initializer
            : declaration;
          const classification = classify(shapeNode, variableName, relativeFile);
          if (classification === null) {
            continue;
          }
          const record = addSymbol(
            sourceFile,
            relativeFile,
            declaration,
            variableName,
            classification,
            visibility,
            null);
          addRelationship('defines', 'source-file', relativeFile, 'symbol', record.key);
          declared.push({ node: declaration, key: record.key });
        }
        continue;
      }

      const statementName = declarationName(statement)?.text;
      if (statementName === undefined) {
        continue;
      }
      const classification = classify(statement, statementName, relativeFile);
      if (classification === null) {
        continue;
      }
      const record = addSymbol(
        sourceFile,
        relativeFile,
        statement,
        statementName,
        classification,
        visibility,
        null);
      addRelationship('defines', 'source-file', relativeFile, 'symbol', record.key);
      declared.push({ node: statement, key: record.key });

      if (ts.isClassDeclaration(statement) || ts.isInterfaceDeclaration(statement)) {
        declareMembers(statement, record.key);
      }
    }
  }

  // Wording observed in each file, keyed by where it sits. A nested declaration is walked after the one that
  // encloses it, so the innermost declaration is the one left owning the text.
  const textsByFile = new Map();

  // Second pass: every declaration is now known, so heritage and usage can be resolved precisely.
  for (const [relativeFile, declared] of declaredNodesByFile) {
    const sourceFile = program.getSourceFile(path.join(rootPath, relativeFile));
    if (sourceFile === undefined) {
      continue;
    }

    const texts = new Map();
    textsByFile.set(relativeFile, texts);

    for (const { node, key } of declared) {
      for (const clause of node.heritageClauses ?? []) {
        const kind = clause.token === ts.SyntaxKind.ImplementsKeyword ? 'implements' : 'inherits';
        for (const typeExpression of clause.types) {
          const target = resolveKey(ts.isPropertyAccessExpression(typeExpression.expression)
            ? typeExpression.expression.name
            : typeExpression.expression);
          if (target !== null) {
            addRelationship(kind, 'symbol', key, 'symbol', target);
          }
        }
      }

      const consumed = new Set();
      const walk = (current) => {
        const text = readText(current);
        if (text !== null) {
          const location = locationBetween(
            sourceFile,
            text.start ?? current.getStart(sourceFile),
            text.end ?? current.getEnd(),
            relativeFile);
          texts.set(`${location.startLine}:${location.startColumn}`, { ...text, location, containerKey: key });
        }

        if (ts.isCallExpression(current)) {
          const callee = ts.isPropertyAccessExpression(current.expression)
            ? current.expression.name
            : current.expression;
          consumed.add(callee);
          const target = resolveKey(callee);
          if (target !== null) {
            const targetSymbol = symbolsByKey.get(target);
            addRelationship(
              targetSymbol?.symbolKind === SYMBOL_KIND.callable ? 'calls' : 'references',
              'symbol',
              key,
              'symbol',
              target);
          }
        } else if (ts.isJsxOpeningElement(current) || ts.isJsxSelfClosingElement(current)) {
          const tag = ts.isPropertyAccessExpression(current.tagName) ? current.tagName.name : current.tagName;
          consumed.add(tag);
          const target = resolveKey(tag);
          if (target !== null) {
            addRelationship('renders', 'symbol', key, 'symbol', target);
          }
        } else if (ts.isTypeReferenceNode(current)) {
          const typeName = ts.isQualifiedName(current.typeName) ? current.typeName.right : current.typeName;
          consumed.add(typeName);
          const target = resolveKey(typeName);
          if (target !== null) {
            addRelationship('references', 'symbol', key, 'symbol', target);
          }
        } else if (ts.isJsxAttribute(current) && isStyleClassAttribute(current)) {
          for (const className of readStyleClassNames(current.initializer)) {
            addRelationship('references', 'symbol', key, 'symbol', `${SHARED_PREFIX}css.class::${className}`);
          }
        } else if (ts.isIdentifier(current) && !consumed.has(current) && (current.parent?.name !== current || isMemberAccess(current))) {
          const target = resolveKey(current);
          if (target !== null) {
            addRelationship('references', 'symbol', key, 'symbol', target);
          }
        }
        ts.forEachChild(current, walk);
      };
      walk(node);
    }
  }

  for (const [relativeFile, texts] of textsByFile) {
    const ordered = [...texts.values()].sort((left, right) =>
      left.location.startLine - right.location.startLine || left.location.startColumn - right.location.startColumn);
    if (ordered.length > TEXT_LIMIT_PER_FILE) {
      diagnostics.push({
        severity: 'warning',
        code: 'TS_TEXT_LITERAL_LIMIT',
        message: `'${relativeFile}' holds ${ordered.length} text literals; only the first ${TEXT_LIMIT_PER_FILE} were reported.`
      });
    }

    for (const text of ordered.slice(0, TEXT_LIMIT_PER_FILE)) {
      const key = `${relativeFile}#text@${text.location.startLine}:${text.location.startColumn}`;
      symbolsByKey.set(key, {
        key,
        name: text.text,
        qualifiedName: key,
        symbolKind: SYMBOL_KIND.textLiteral,
        nativeKind: text.nativeKind,
        visibility: VISIBILITY.unknown,
        languageId: languageOf(relativeFile),
        location: text.location
      });
      addRelationship('contains', 'symbol', text.containerKey, 'symbol', key);
    }
  }

  return {
    status: diagnostics.length === 0 ? 'succeeded' : 'partial',
    typeScriptVersion: ts.version,
    projects: projectPaths.map((projectPath) => ({
      relativePath: projectPath,
      name: readProjectName(rootPath, projectPath),
      languageId: projectLanguageOf(projectPath)
    })),
    sourceFiles: analyzedFiles.map((relativeFile) => ({
      relativePath: relativeFile,
      languageId: languageOf(relativeFile)
    })),
    symbols: [...symbolsByKey.values()].sort((left, right) => (left.key < right.key ? -1 : left.key > right.key ? 1 : 0)),
    relationships: relationships.sort((left, right) => {
      const leftIdentity = `${left.kind}|${left.sourceKey}|${left.targetKey}`;
      const rightIdentity = `${right.kind}|${right.sourceKey}|${right.targetKey}`;
      return leftIdentity < rightIdentity ? -1 : leftIdentity > rightIdentity ? 1 : 0;
    }),
    diagnostics
  };
}

async function main() {
  try {
    process.stdout.write(JSON.stringify(analyze(JSON.parse(await readStdin()))));
  } catch (error) {
    process.stdout.write(JSON.stringify(
      failure('TS_SIDECAR_FAILURE', error instanceof Error ? error.message : String(error))));
  }
}

await main();
