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
import { build as buildAgentMap, outputs as agentMapOutputs, verifyBudget } from './build-agent-map.mjs';
import { hasInternalHistory } from './internal-history.mjs';

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

// The agent map is generated from capabilities.md and COMMITTED: `dotnet pack`
// reads it, so the Node chain must not be part of `dotnet build`. That trade
// needs this gate - a map that drifts from its source ships a stale capability
// list to every consumer.
const agentMap = buildAgentMap();

errors.push(...verifyBudget(agentMap));

for (const [key, path] of Object.entries(agentMapOutputs)) {
  const published = existsSync(path) ? readFileSync(path, 'utf8') : null;

  if (published !== agentMap[key]) {
    errors.push(
      `${relative(repositoryRoot, path)} does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs`,
    );
  }
}

// ---------------------------------------------------------------------------
// Surfaces the product owns that the prose has to keep up with. Each of these
// shipped a gap that no test noticed: a console screen the guide never named, a
// telemetry attribute nobody could group by, an option nobody could find, and a
// hand-typed operation count that drifted by seventeen.
// ---------------------------------------------------------------------------

const consoleLocales = readFileSync(
  join(sourceRoot, 'AgentPrism.UI/frontend/src/locales/en.ts'),
  'utf8',
);
const uiGuide = readFileSync(join(docsRoot, 'ui.md'), 'utf8');
const screenshotRoot = join(siteRoot, 'public/screenshots');

for (const [, key, label] of consoleLocales.matchAll(/'nav\.([A-Za-z]+)':\s*'([^']+)'/g)) {
  if (key === 'primary') continue;

  // A heading, not a passing mention: 'Jobs' appears in a cross-link on a page that
  // never describes the Jobs screen, which is exactly the gap this gate closes.
  if (!new RegExp(`^#{2,3} .*\\b${label}\\b`, 'im').test(uiGuide)) {
    errors.push(`ui.md has no section describing the '${label}' console screen`);
  }

  if (!existsSync(join(screenshotRoot, `${key}.png`))) {
    errors.push(
      `public/screenshots/${key}.png is missing; regenerate with ` +
        'AGENTPRISM_UI_SCREENSHOTS=1 dotnet test tests/AgentPrism.Ui.E2ETests -c Release',
    );
  }
}

const manualProse = manualContent.map((file) => readFileSync(file, 'utf8')).join('\n');

const diagnostics = readFileSync(
  join(sourceRoot, 'AgentPrism.Core/Diagnostics/AgentPrismDiagnostics.cs'),
  'utf8',
);

const telemetryNames = [...diagnostics.matchAll(/public const string \w+ = "([^"]+)"/g)]
  .map(([, value]) => value)
  // The meter and activity-source name is just "AgentPrism"; it is documented as
  // prose, not as an identifier, and matching it would accept any page.
  .filter((value) => value !== 'AgentPrism');

for (const name of telemetryNames) {
  // Whole name: 'agentprism.tenant.id' is a prefix of 'agentprism.tenant.identifier',
  // so a substring test would accept a renamed attribute as documented.
  if (!new RegExp(`${name.replace(/\./g, '\\.')}(?![\\w.])`).test(manualProse)) {
    errors.push(`Telemetry name '${name}' appears on no hand-written page`);
  }
}

// A page documents an option when it names the property AND the type or section it
// belongs to. Measured: a bare word match accepted four options that no page
// describes — 'Enabled' matched an unrelated sentence, 'Kind' matched a table of
// workflow kinds.
for (const file of collect(sourceRoot).filter((entry) => entry.endsWith('Options.cs'))) {
  if (['obj', 'bin'].some((segment) => file.includes(`/${segment}/`))) continue;

  const text = readFileSync(file, 'utf8');
  const owners = [...text.matchAll(/public sealed class (\w+)/g)].map(([, name]) => name);
  const sections = [...text.matchAll(/SectionName = "([^"]+)"/g)].map(([, name]) => name);
  const context = [...owners, ...sections];

  const pagesNamingOwner = manualContent
    .map((entry) => readFileSync(entry, 'utf8'))
    .filter((page) => context.some((name) => page.includes(name)));

  for (const [, property] of text.matchAll(/public\s+[\w<>?,[\]. ]+?\s+(\w+)\s*\{\s*get/g)) {
    const named = new RegExp(`\\b${property}\\b`);

    if (!pagesNamingOwner.some((page) => named.test(page))) {
      errors.push(
        `${relative(repositoryRoot, file)}: option '${property}' appears on no page that ` +
          `also names ${context.join(' or ')}`,
      );
    }
  }
}

const httpApiPage = readFileSync(join(docsRoot, 'http-api.md'), 'utf8');
const declared = /(\d+) operations across (\d+) paths/.exec(httpApiPage);

if (!declared) {
  errors.push('http-api.md no longer states its operation and path counts');
} else if (
  Number(declared[1]) !== operationCount ||
  Number(declared[2]) !== Object.keys(openApi.paths ?? {}).length
) {
  errors.push(
    `http-api.md claims ${declared[1]} operations across ${declared[2]} paths; ` +
      `the document has ${operationCount} across ${Object.keys(openApi.paths ?? {}).length}`,
  );
}

// The packaged copy, which AgentPrism.AspNetCore ships under buildTransitive/.
// The site copy below is sanitised; this one reaches the consumer unedited.
const packagedOpenApi = join(repositoryRoot, 'docs/openapi/agentprism.json');

if (existsSync(packagedOpenApi)) {
  const text = readFileSync(packagedOpenApi, 'utf8');

  if (hasInternalHistory(text)) {
    errors.push('docs/openapi/agentprism.json: internal development history is packaged into AgentPrism.AspNetCore');
  }

  // Two shapes: a nullable member ("string? X.Y") and a plain one ("string X.Y").
  // Measured: fixing only the first left seventeen of the second behind.
  const signatureLeak =
    /\b(?:[A-Za-z_][A-Za-z0-9_.<>]*|[A-Za-z_][A-Za-z0-9_]*\[\])\?\s+(?=[A-Z][A-Za-z0-9_.]+\b)|\b(?:int|long|bool|string|double|decimal|float|TimeSpan|DateTimeOffset|Guid|JsonElement|Uri)(?:\[\])?\s+[A-Z][A-Za-z0-9_]*\.[A-Z][A-Za-z0-9_]*\b/;

  if (signatureLeak.test(text)) {
    errors.push(
      'docs/openapi/agentprism.json: a <see cref> rendered as a full CLR signature; ' +
        'use <c>MemberName</c> in the contract documentation instead',
    );
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

  const expectedAnonymous = ['AgentPrismAcceptInboundTrigger', 'AgentPrismMcpOAuthCallback', 'AgentPrismMeta'];
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


function isSynchronizationCopy(file) {
  return / \d+\.(?:md|mdx|json)$/.test(basename(file));
}
