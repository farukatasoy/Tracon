// Product-documentation invariants that are cheap enough to run on every build.
//
// This gate is intentionally about facts and structure, not subjective prose style.
// It catches the defect classes that previously shipped: unusable install commands,
// stale template choices, internal development notes in the public API reference,
// compiler-generated record noise, synchronization copies, and missing capability
// guides.

import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { basename, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = resolve(fileURLToPath(new URL('.', import.meta.url)));
const docsRoot = resolve(here, '../src/content/docs');
const siteRoot = resolve(here, '..');
const repositoryRoot = resolve(siteRoot, '..');
const errors = [];

const requiredManualPages = [
  'capabilities.md',
  'guides/background-work.md',
  'guides/context-and-memory.md',
  'guides/external-agents.md',
  'guides/knowledge.md',
  'guides/model-providers.md',
  'guides/multimodal.md',
  'guides/observability.md',
  'guides/openai-api.md',
  'guides/production.md',
  'guides/reliability.md',
  'guides/structured-output.md',
  'guides/testing.md',
  'guides/voice.md',
  'reference/compatibility.md',
  'reference/configuration.md',
  'reference/glossary.md',
  'reference/versioning.md',
  'troubleshooting.md',
];

const requiredCapabilityEvidence = [
  'Structured output',
  'Harness mode',
  'Context compaction',
  'Working memory',
  'UseOpenAICompatible',
  'UseAnthropic',
  'UseGoogle',
  'UseAzureOpenAI',
  'AddGeneratedTools',
  'UseSkillScripts',
  'McpResourceUris',
  'Knowledge search',
  'Run reconciliation',
  'Idempotency-Key',
  'UseVoiceConversation',
  'Durable checkpoints',
  'UseScheduling',
  'Online evaluation',
  'Canary rollback',
  'Multi-tenancy',
  'Retention and archive',
  'OpenTelemetry traces',
  'Provider health',
  'UseMcpServer',
  'UseA2A',
  'FakeModelProvider',
];

for (const page of requiredManualPages) {
  if (!existsSync(join(docsRoot, page))) {
    errors.push(`Missing capability page: ${page}`);
  }
}

const astroConfig = readFileSync(join(siteRoot, 'astro.config.mjs'), 'utf8');
const landingPage = readFileSync(join(docsRoot, 'index.mdx'), 'utf8');
const sourceRoot = join(repositoryRoot, 'src');
const packageCount = readdirSync(sourceRoot, { withFileTypes: true }).filter(
  (entry) =>
    entry.isDirectory() &&
    entry.name.startsWith('AgentPrism') &&
    entry.name !== 'AgentPrism.Generators' &&
    existsSync(join(sourceRoot, entry.name, `${entry.name}.csproj`)),
).length;
const openApi = JSON.parse(readFileSync(join(repositoryRoot, 'docs/openapi/agentprism.json'), 'utf8'));
const operationCount = Object.values(openApi.paths ?? {}).reduce(
  (total, item) =>
    total + ['get', 'post', 'put', 'patch', 'delete'].filter((method) => item[method]).length,
  0,
);
const uiRoutes = readFileSync(join(sourceRoot, 'AgentPrism.UI/frontend/src/app.tsx'), 'utf8');
const screenCount = new Set(
  [...uiRoutes.matchAll(/from '\.\/screens\/([^']+)'/g)].map((match) => match[1]),
).size;

for (const [value, label] of [
  [packageCount, 'NuGet packages'],
  [operationCount, 'generated HTTP operations'],
  [screenCount, 'embedded console screens'],
]) {
  if (!landingPage.includes(`<strong>${value}</strong> ${label}`)) {
    errors.push(`Landing-page metric drift: expected ${value} ${label}`);
  }
}

for (const page of requiredManualPages) {
  const slug = page.replace(/\.(?:md|mdx)$/, '').replace(/\/index$/, '');
  if (!astroConfig.includes(`slug: '${slug}'`)) {
    errors.push(`Capability page is not reachable from the sidebar: ${page}`);
  }
}

const allContent = collect(docsRoot).filter((file) => ['.md', '.mdx'].includes(extname(file)));
const manualContent = allContent.filter(
  (file) => !file.startsWith(join(docsRoot, 'api')) && !file.startsWith(join(docsRoot, 'http-api')),
);

const capabilityText = readFileSync(join(docsRoot, 'capabilities.md'), 'utf8');
for (const evidence of requiredCapabilityEvidence) {
  if (!capabilityText.includes(evidence)) {
    errors.push(`Capability map lost required evidence: ${evidence}`);
  }
}

const manualSlugs = new Map();

for (const file of allContent) {
  const name = basename(file);

  if (/ \d+\.(?:md|mdx)$/.test(name)) {
    errors.push(`Synchronization copy: ${relative(docsRoot, file)}`);
  }
}

for (const file of collect(resolve(siteRoot, 'src'))) {
  if (/ \d+\.[^/]+$/.test(basename(file))) {
    errors.push(`Synchronization copy under src: ${relative(siteRoot, file)}`);
  }
}

for (const file of manualContent) {
  const text = readFileSync(file, 'utf8');
  const label = relative(docsRoot, file);
  const frontmatter = /^---\n([\s\S]*?)\n---/.exec(text)?.[1];

  if (!frontmatter) {
    errors.push(`${label}: missing frontmatter`);
    continue;
  }

  const explicitSlug = /^slug:\s*(.+)$/m.exec(frontmatter)?.[1]?.replace(/^['"]|['"]$/g, '');
  const derivedSlug = label.replace(/\.(?:md|mdx)$/, '').replace(/\/index$/, '');
  const slug = explicitSlug ?? derivedSlug;

  if (label !== 'index.mdx' && !astroConfig.includes(`slug: '${slug}'`)) {
    errors.push(`${label}: manual page is not reachable from the sidebar`);
  }

  if (manualSlugs.has(slug)) {
    errors.push(`${label}: duplicate manual slug '${slug}' also used by ${manualSlugs.get(slug)}`);
  } else {
    manualSlugs.set(slug, label);
  }

  if (!/^title:\s*.+$/m.test(frontmatter)) {
    errors.push(`${label}: missing title`);
  }

  const description = /^description:\s*(.+)$/m.exec(frontmatter)?.[1]?.replace(/^['"]|['"]$/g, '');

  if (!description) {
    errors.push(`${label}: missing description`);
  } else if (description.length < 70 || description.length > 180) {
    errors.push(`${label}: description length is ${description.length}; expected 70..180 characters`);
  }

  for (const [lineIndex, line] of text.split('\n').entries()) {
    const trimmed = line.trim();

    if (
      /^dotnet add package AgentPrism(?:\s|$)/.test(trimmed) &&
      !trimmed.includes('--prerelease') &&
      !trimmed.includes('--version')
    ) {
      errors.push(`${label}:${lineIndex + 1}: AgentPrism is preview; add --prerelease or --version`);
    }

    if (/^dotnet new install AgentPrism\.Templates(?:\s|$)/.test(trimmed)) {
      errors.push(`${label}:${lineIndex + 1}: preview templates require an explicit @version`);
    }
  }

  if (/--persistence\s+none\b/.test(text)) {
    errors.push(`${label}: template persistence choice is memory, not none`);
  }

  if (/\/api\/keys(?:\b|`)/.test(text)) {
    errors.push(`${label}: API key routes use /api/api-keys`);
  }

  const proseWithoutCode = text
    .replace(/```[\s\S]*?```/g, '')
    .replace(/`[^`\n]*`/g, '');

  for (const image of proseWithoutCode.matchAll(/<img\s[\s\S]*?>/g)) {
    for (const attribute of ['alt', 'width', 'height', 'loading', 'decoding']) {
      if (!new RegExp(`\\b${attribute}=`).test(image[0])) {
        errors.push(`${label}: HTML image is missing ${attribute}`);
      }
    }
  }

  for (const diagram of text.matchAll(/```mermaid\n([\s\S]*?)```/g)) {
    if (!/^\s*accTitle:\s*\S.+$/m.test(diagram[1])) {
      errors.push(`${label}: Mermaid diagram is missing an accessible title`);
    }
    if (!/^\s*accDescr(?::|\s*\{)\s*\S.+$/m.test(diagram[1])) {
      errors.push(`${label}: Mermaid diagram is missing an accessible description`);
    }
  }
}

const apiRoot = join(docsRoot, 'api');

if (existsSync(apiRoot)) {
  for (const file of collect(apiRoot).filter(
    (entry) => entry.endsWith('.md') && !isSynchronizationCopy(entry),
  )) {
    const text = readFileSync(file, 'utf8');
    const label = relative(docsRoot, file);

    if (/<Clone\\?>\$\(\)/.test(text)) {
      errors.push(`${label}: compiler-generated <Clone>$() member leaked into the public reference`);
    }

    if (/^description:\s*""\s*$/m.test(text)) {
      errors.push(`${label}: generated reference description is empty`);
    }

    if (/<\/?p>/.test(text)) {
      errors.push(`${label}: raw paragraph HTML prevents nested Markdown from rendering`);
    }

    if (/\brationale\b|🚨|⚠️|^#{2,4} Remarks[.:]|^#{2,4} Remarks\n\n(?=#{2,4} )|\bSee\s+for\b/im.test(text)) {
      errors.push(`${label}: internal or malformed reference prose leaked into the public page`);
    }


    if (/github\.com\/farukatasoy\/AgentPrism\/blob\/[^)]+\/src\/[^)]+\.cs/.test(text)) {
      errors.push(`${label}: internal AgentPrism type links to source instead of the local reference`);
    }

    if (
      /\b(?:phase|faz)\s+\d+\b|\b(?:K|F)-\d{2,3}\b|\bK[1-4]\b|\bsection\s+\d+(?:\.\d+)?\b|\b(?:HATA|MT)-[A-Z0-9-]+\b|(?:<code>|`)?docs\/[^\s`<),]+/i.test(
        text,
      )
    ) {
      errors.push(`${label}: internal development history leaked into the public reference`);
    }
  }
}

for (const directory of [join(docsRoot, 'http-api')]) {
  if (!existsSync(directory)) continue;
  for (const file of collect(directory).filter(
    (entry) => entry.endsWith('.md') && !isSynchronizationCopy(entry),
  )) {
    const text = readFileSync(file, 'utf8');
    if (hasInternalHistory(text)) {
      errors.push(`${relative(docsRoot, file)}: internal development history leaked into HTTP reference`);
    }
    if (/\brationale\b|\b(?:int|long|decimal|double|string|float\[\])\?\s+[A-Z][A-Za-z0-9_.]+/i.test(text)) {
      errors.push(`${relative(docsRoot, file)}: malformed source prose leaked into HTTP reference`);
    }
  }
}

const publishedOpenApi = join(siteRoot, 'public/openapi/agentprism.json');
if (existsSync(publishedOpenApi)) {
  const text = readFileSync(publishedOpenApi, 'utf8');
  if (hasInternalHistory(text)) {
    errors.push('public/openapi/agentprism.json: internal development history leaked into OpenAPI');
  }
  if (/\brationale\b|\b(?:int|long|decimal|double|string|float\[\])\?\s+[A-Z][A-Za-z0-9_.]+/i.test(text)) {
    errors.push('public/openapi/agentprism.json: malformed source prose leaked into OpenAPI');
  }
  if (/\/api\/keys\b/.test(text)) {
    errors.push('public/openapi/agentprism.json: API key routes use /api/api-keys');
  }

  const document = JSON.parse(text);
  const anonymousOperations = [];
  for (const item of Object.values(document.paths ?? {})) {
    for (const method of ['get', 'post', 'put', 'patch', 'delete']) {
      const operation = item[method];
      if (!operation) continue;
      if (operation.security?.length === 0) {
        anonymousOperations.push(operation.operationId);
        continue;
      }
      if (!operation['x-agentprism-role']) {
        errors.push(`public OpenAPI operation ${operation.operationId} has no role metadata`);
      }
      if (
        !operation['x-agentprism-api-key-scope'] &&
        operation.operationId !== 'AgentPrismCurrentTenant'
      ) {
        errors.push(`public OpenAPI operation ${operation.operationId} has no API-key scope metadata`);
      }
    }
  }

  const expectedAnonymous = ['AgentPrismMcpOAuthCallback', 'AgentPrismMeta'];
  if (anonymousOperations.sort().join(',') !== expectedAnonymous.join(',')) {
    errors.push(`public OpenAPI anonymous-operation drift: ${anonymousOperations.join(', ')}`);
  }
}

if (errors.length > 0) {
  console.error(`Content check failed with ${errors.length} issue(s):`);
  for (const error of errors.slice(0, 80)) {
    console.error(`  ${error}`);
  }
  if (errors.length > 80) {
    console.error(`  ... and ${errors.length - 80} more`);
  }
  process.exit(1);
}

console.log(`Content: ${manualContent.length} manual pages and ${allContent.length} total pages passed.`);

function collect(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? collect(path) : [path];
  });
}

function hasInternalHistory(text) {
  return /\b(?:phase|faz)\s+\d+\b|\b(?:K|F)-\d{2,3}\b|\bK[1-4]\b|\bsection\s+\d+(?:\.\d+)?\b|\b(?:HATA|MT)-[A-Z0-9-]+\b|(?:<code>|`)?docs\/[^\s`<),]+/i.test(
    text,
  );
}

function isSynchronizationCopy(file) {
  return / \d+\.(?:md|mdx|json)$/.test(basename(file));
}
