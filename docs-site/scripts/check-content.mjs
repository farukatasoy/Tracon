// Product-documentation invariants that are cheap enough to run on every build.
//
// This gate is intentionally about facts and structure, not subjective prose style.
// It catches the defect classes that previously shipped: unusable install commands,
// stale template choices, internal development notes in the public API reference,
// compiler-generated record noise, synchronization copies, and missing capability
// guides.

import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { basename, extname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build as buildAgentMap, outputs as agentMapOutputs, verifyBudget } from './build-agent-map.mjs';
import { formerHosts, repositoryIsPublic, repositoryUrl, site, siteUrl } from '../site.config.mjs';
import { hasInternalHistory } from './internal-history.mjs';
import { sidebar, sectionImages } from '../src/sidebar.mjs';

/** Every slug the sidebar reaches, at any depth. */
const sidebarSlugs = new Set(
  (function collectSlugs(entries) {
    return entries.flatMap((entry) =>
      entry.items ? collectSlugs(entry.items) : entry.slug ? [entry.slug] : [],
    );
  })(sidebar),
);

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

// compatibility.md's own package table must track packageCount independently of
// the landing page - Phase 96 dropped 98 types from the surface but left this
// table's heading and row count at a stale "17", two packages short, until this
// check existed (docs/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md, 97.5).
const compatibility = readFileSync(join(docsRoot, 'reference/compatibility.md'), 'utf8');
const packagesHeading = compatibility.match(/^## The (\d+) packages$/m);
const packagesSection = compatibility.split(/^## /m).find((section) => /^The \d+ packages\b/.test(section)) ?? '';
const packageRowCount = [...packagesSection.matchAll(/^\| `AgentPrism[^`]*` \|/gm)].length;

if (!packagesHeading || Number(packagesHeading[1]) !== packageCount) {
  errors.push(
    `compatibility.md heading says "${packagesHeading?.[1] ?? '(missing)'}" packages, expected ${packageCount}`,
  );
}
if (packageRowCount !== packageCount) {
  errors.push(`compatibility.md package table has ${packageRowCount} row(s), expected ${packageCount}`);
}

for (const page of requiredManualPages) {
  const slug = page.replace(/\.(?:md|mdx)$/, '').replace(/\/index$/, '');
  if (!sidebarSlugs.has(slug)) {
    errors.push(`Capability page is not reachable from the sidebar: ${page}`);
  }
}

const allContent = collect(docsRoot).filter((file) => ['.md', '.mdx'].includes(extname(file)));
// The separator matters: without it `http-api.md`, which is hand-written, is read
// as living under the generated `http-api/` directory and skips every check below.
const generatedRoots = [join(docsRoot, 'api', sep), join(docsRoot, 'http-api', sep)];

const manualContent = allContent.filter(
  (file) => !generatedRoots.some((directory) => file.startsWith(directory)),
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

  if (label !== 'index.mdx' && !sidebarSlugs.has(slug)) {
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

// Comparing generated against committed cannot catch a generator that renders
// the wrong thing into both - both sides agree and both are wrong. These two
// checks state the outcome instead, and each names a defect that shipped: the
// llms-full.txt address used to be appended to the SITE copy alone, which hid
// the full text from the only reader who cannot browse to it, and an index
// built off its own traversal could silently omit a page.
for (const key of ['agentMap', 'llms']) {
  for (const artifact of ['llms.txt', 'llms-full.txt']) {
    if (!agentMap[key].includes(`${siteUrl}${artifact}`)) {
      errors.push(`${relative(repositoryRoot, agentMapOutputs[key])} never names ${siteUrl}${artifact}`);
    }
  }
}

// The page index, counted from this file's OWN list of pages. The landing page
// is the one exclusion: a splash screen answers no question, and the full text
// leaves it out for the same reason.
const indexed = new Set(
  [...agentMap.llms.matchAll(/^- \[[^\]]*]\((\S+)\)/gm)].map(([, url]) => url),
);

for (const file of manualContent) {
  if (file === join(docsRoot, 'index.mdx')) continue;

  const declared = /^slug:\s*(.+)$/m.exec(/^---\n([\s\S]*?)\n---/.exec(readFileSync(file, 'utf8'))?.[1] ?? '');

  const slug =
    declared?.[1].trim().replace(/^['"]|['"]$/g, '') ??
    relative(docsRoot, file).replaceAll(sep, '/').replace(/\.mdx?$/, '').replace(/(^|\/)index$/, '');

  if (!indexed.delete(`${siteUrl}${slug}${slug ? '/' : ''}`)) {
    errors.push(`llms.txt has no index line for ${relative(docsRoot, file)}`);
  }
}

for (const url of indexed) {
  errors.push(`llms.txt indexes ${url}, which is not a hand-written page`);
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

// ---------------------------------------------------------------------------
// Presentation contract. Content correctness is checked above; these four claims
// are about the shape a reader meets on every page, and each one shipped broken:
// three different names for the closing section, twenty-one pages with nothing to
// look at, a palette nobody had measured, and one link preview for the whole site.
// ---------------------------------------------------------------------------

// 6. Every hand-written page ends with the same closing section, and that section
//    is a decision aid rather than a link dump.
const CLOSING_EXEMPT = new Map([
  [
    'index.mdx',
    'the splash page\'s last section is a four-way "Choose your path" card grid, which ' +
      'IS the closing; three bullets under it would repeat it',
  ],
]);

for (const file of manualContent) {
  const label = relative(docsRoot, file);
  const text = readFileSync(file, 'utf8');
  const headings = [...text.matchAll(/^## (.+)$/gm)].map(([, name]) => name.trim());

  for (const stale of ['Related', 'Next', 'Related reference', 'See also']) {
    if (headings.includes(stale)) {
      errors.push(`${label}: closing section is named '${stale}'; every page uses '## Read next'`);
    }
  }

  const reason = CLOSING_EXEMPT.get(label);

  if (reason) {
    if (headings.includes('Read next')) {
      errors.push(`${label}: exempt from the closing contract but has one; drop the exemption`);
    }
    continue;
  }

  if (headings.at(-1) !== 'Read next') {
    errors.push(
      `${label}: does not end with '## Read next' (last section: ${headings.at(-1) ?? 'none'})`,
    );
    continue;
  }

  const closing = text.slice(text.lastIndexOf('\n## Read next'));
  const links = (closing.match(/^- /gm) ?? []).length;

  if (links === 0) {
    errors.push(`${label}: '## Read next' has no links`);
  } else if (links > 3) {
    errors.push(`${label}: '## Read next' offers ${links} links; a reader can decide between at most 3`);
  }
}

// 7. A page that explains a flow, a decision, or a layer shows one. A page that
//    lists things does not have to, but it has to say so here.
const DIAGRAM_THRESHOLD = 6500;
const DIAGRAM_EXEMPT = new Map([
  ['troubleshooting.md', 'a symptom catalogue read by search; its navigation is the symptom index'],
  ['reference/configuration.md', 'a table of keys, defaults, and the package that reads each section'],
  ['reference/compatibility.md', 'support matrices, which are already tables'],
  ['reference/glossary.md', 'alphabetical definitions with no flow between them'],
  ['packages.md', 'a decision table: one row per package'],
]);

for (const file of manualContent) {
  const label = relative(docsRoot, file);
  const text = readFileSync(file, 'utf8');
  const size = Buffer.byteLength(text);
  const shows = /```mermaid|<img\s|!\[/.test(text);
  const reason = DIAGRAM_EXEMPT.get(label);

  if (reason && shows) {
    errors.push(`${label}: listed as a table page but now shows a figure; drop the exemption`);
  }

  if (reason || shows || size <= DIAGRAM_THRESHOLD) continue;

  errors.push(
    `${label}: ${size} bytes of narrative with no diagram or image. Add one, or add it to ` +
      'DIAGRAM_EXEMPT in this file with the reason it is a table page.',
  );
}

for (const label of [...CLOSING_EXEMPT.keys(), ...DIAGRAM_EXEMPT.keys()]) {
  if (!existsSync(join(docsRoot, label))) {
    errors.push(`Exemption list names a page that no longer exists: ${label}`);
  }
}

// 8. Every colour pair the reader actually sees clears WCAG AA, in BOTH themes.
//    site.css is the only place a colour is declared, which is what makes this
//    measurable without a browser.
const styleSheet = readFileSync(join(siteRoot, 'src/styles/site.css'), 'utf8').replace(
  /\/\*[\s\S]*?\*\//g,
  '',
);

const themes = { dark: new Map(), light: new Map() };

for (const [, selectors, body] of styleSheet.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
  const list = selectors.split(',').map((entry) => entry.trim());
  const targets = [];
  if (list.includes(':root')) targets.push('dark');
  if (list.includes(":root[data-theme='light']")) targets.push('light');
  // A block that names both selectors declares one value for both themes.
  if (list.includes(':root') && list.includes(":root[data-theme='light']")) {
    targets.length = 0;
    targets.push('dark', 'light');
  }
  if (targets.length === 0) continue;

  for (const [, name, value] of body.matchAll(/(--ap-[\w-]+):\s*([^;]+);/g)) {
    const colour = /^#[0-9a-fA-F]{3,8}$/.test(value.trim()) ? value.trim() : null;
    if (!colour) continue;
    for (const target of targets) themes[target].set(name, colour);
  }
}

// A colour declared anywhere else — inside a component rule, a media query — would
// never reach the maps above and so would never be measured. Measured: adding
// `.card { --ap-sneaky: #ff0000; }` left the gate green. The token set is closed, so
// the declaration site is part of the contract.
for (const [, selectors, body] of styleSheet.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
  const list = selectors.split(',').map((entry) => entry.trim());
  if (list.includes(':root') || list.includes(":root[data-theme='light']")) continue;

  for (const [, name, value] of body.matchAll(/(--ap-[\w-]+):\s*([^;]+);/g)) {
    if (/^#[0-9a-fA-F]{3,8}$/.test(value.trim())) {
      errors.push(
        `site.css: ${name} is declared on '${selectors.trim()}'. Colour tokens belong in ` +
          'the :root blocks, where both themes and the contrast gate can see them.',
      );
    }
  }
}

// A hairline separates; it does not carry information, and the boundary it draws is
// carried by spacing and by the raised surface behind it as well. It is the only
// colour allowed to sit below the UI threshold, and it says so here.
const DECORATIVE = new Map([['--ap-border', 'a hairline: spacing and surface carry the same boundary']]);

const CONTRAST_PAIRS = [
  ['--ap-text', '--ap-surface', 4.5],
  ['--ap-text', '--ap-surface-raised', 4.5],
  ['--ap-text-strong', '--ap-surface', 4.5],
  ['--ap-text-strong', '--ap-surface-raised', 4.5],
  ['--ap-text-muted', '--ap-surface', 4.5],
  ['--ap-text-muted', '--ap-surface-raised', 4.5],
  ['--ap-accent', '--ap-surface', 4.5],
  ['--ap-accent', '--ap-surface-raised', 4.5],
  ['--ap-accent', '--ap-accent-quiet', 4.5],
  ['--ap-code', '--ap-surface-sunken', 4.5],
  ['--ap-code', '--ap-surface-raised', 4.5],
  ['--ap-diagram-ink', '--ap-diagram-plate', 4.5],
  ['--ap-diagram-ink', '--ap-diagram-node', 4.5],
  ['--ap-diagram-ink', '--ap-diagram-cluster', 4.5],
  // Non-text boundaries: WCAG 1.4.11 asks for 3:1, not 4.5:1.
  ['--ap-border-strong', '--ap-surface', 3],
  ['--ap-border-strong', '--ap-surface-raised', 3],
  ['--ap-diagram-line', '--ap-diagram-plate', 3],
  ['--ap-diagram-line', '--ap-diagram-node', 3],
];

const paired = new Set(CONTRAST_PAIRS.flatMap(([a, b]) => [a, b]));
// Reported on success, not only on failure: the phase records the measured floor,
// and a number nobody prints is a number nobody notices drifting.
const floors = {
  4.5: { ratio: Number.POSITIVE_INFINITY, pair: '' },
  3: { ratio: Number.POSITIVE_INFINITY, pair: '' },
};

for (const [theme, tokens] of Object.entries(themes)) {
  for (const name of tokens.keys()) {
    if (!paired.has(name) && !DECORATIVE.has(name)) {
      errors.push(
        `site.css: ${name} is a colour with no contrast pair. Add it to CONTRAST_PAIRS ` +
          'or to DECORATIVE with the reason it carries no information.',
      );
    }
  }

  for (const [foreground, background, minimum] of CONTRAST_PAIRS) {
    const front = tokens.get(foreground);
    const back = tokens.get(background);

    // Never skip: an unresolved token is a token that has no value in this theme,
    // which is exactly the surface that goes unreadable.
    if (!front || !back) {
      errors.push(`site.css: ${!front ? foreground : background} has no value in the ${theme} theme`);
      continue;
    }

    const ratio = contrast(front, back);
    const floor = floors[minimum];

    if (ratio < floor.ratio) {
      floor.ratio = ratio;
      floor.pair = `${foreground} on ${background}, ${theme}`;
    }

    if (ratio < minimum) {
      errors.push(
        `site.css: ${foreground} on ${background} is ${ratio.toFixed(2)}:1 in the ${theme} ` +
          `theme; ${minimum}:1 required`,
      );
    }
  }
}

// A token nobody reads is a token nobody maintains. astro.config.mjs reads the
// diagram tokens by name, so both files count as usage.
const astroConfigText = readFileSync(join(siteRoot, 'astro.config.mjs'), 'utf8');

for (const name of themes.dark.keys()) {
  const usedInCss = new RegExp(`var\\(${name}[,)]`).test(styleSheet);
  const usedInConfig = astroConfigText.includes(`token('${name.slice(2)}')`);

  if (!usedInCss && !usedInConfig) {
    errors.push(`site.css: ${name} is declared but nothing reads it`);
  }
}

// 9. Every sidebar section advertises its own link preview, and the file exists.
for (const section of sidebar) {
  const image = sectionImages[section.label];

  if (!image) {
    errors.push(`Sidebar section '${section.label}' has no link-preview image in src/sidebar.mjs`);
    continue;
  }

  const asset = join(siteRoot, 'public/social', `${image}.png`);

  if (!existsSync(asset)) {
    errors.push(
      `Sidebar section '${section.label}' points at social/${image}.png, which is not on disk; ` +
        'regenerate with: node scripts/build-social-images.mjs',
    );
  }
}

for (const label of Object.keys(sectionImages)) {
  if (!sidebar.some((section) => section.label === label)) {
    errors.push(`src/sidebar.mjs maps '${label}', which is no longer a sidebar section`);
  }
}

// 10. The rule the packaged documentation already follows applies to the site's own
//     hand-written pages too: a reader cannot resolve a decision number.
for (const file of manualContent) {
  const text = readFileSync(file, 'utf8');

  if (hasInternalHistory(text)) {
    errors.push(`${relative(docsRoot, file)}: internal development history leaked into a public page`);
  }
}

// 11. The published address is declared once, and no dead address survives a move.
//
//     Two failure modes, and they need different rules. The first is a seventh copy of
//     the address being born in a file that could have imported it - the site was
//     carrying seven, spelled inconsistently, with nothing checking that they agreed.
//     The second is a copy that CANNOT be derived: a package README is plain markdown
//     that ships to NuGet, and an analyzer help link is a compiled constant. Those are
//     edited by hand on every move, and a missed one looks exactly like a working link
//     until a reader clicks it.
const siteHost = new URL(site).host;

const declaresTheAddress = [
  join(siteRoot, 'site.config.mjs'),
  join(sourceRoot, 'AgentPrism.Generators', 'DocumentationLinks.cs'),
];

const derivedFromConfig = [
  join(siteRoot, 'astro.config.mjs'),
  ...collectSources(join(siteRoot, 'scripts')),
  ...collectSources(join(siteRoot, 'src')),
  // The serving stack: its Traefik rule names the host, and `.env` supplies it.
  ...collectSources(join(siteRoot, 'deploy')),
  ...collectSources(sourceRoot).filter((file) => extname(file) === '.cs'),
].filter((file) => !declaresTheAddress.includes(file));

for (const file of derivedFromConfig) {
  if (readFileSync(file, 'utf8').includes(siteHost)) {
    errors.push(
      `${relative(repositoryRoot, file)}: spells out '${siteHost}'. ` +
        'Import it from docs-site/site.config.mjs (C#: DocumentationLinks) instead.',
    );
  }
}

// Hand-edited copies, checked for the opposite thing: not that they name the address,
// but that they do not still name one this site has left behind.
const packagesRoot = join(repositoryRoot, 'packages');
const handWritten = [
  ...derivedFromConfig,
  ...collectSources(sourceRoot).filter((file) => basename(file) === 'README.md'),
  // packages/agentprism-client/README.md ships to npm the same way a NuGet
  // README does (Phase 84) — same blind spot, same fix.
  ...(existsSync(packagesRoot) ? collectSources(packagesRoot) : []).filter(
    (file) => basename(file) === 'README.md',
  ),
  join(repositoryRoot, 'README.md'),
];

for (const file of handWritten) {
  const text = readFileSync(file, 'utf8');

  for (const host of formerHosts) {
    if (text.includes(host)) {
      errors.push(
        `${relative(repositoryRoot, file)}: still points at '${host}', which no longer ` +
          `serves this site. The site is published at ${siteUrl}.`,
      );
    }
  }

  // The same defect wearing different clothes: a private repository is a 404 to every
  // reader of a published page or a NuGet listing.
  if (!repositoryIsPublic && text.includes(repositoryUrl)) {
    errors.push(
      `${relative(repositoryRoot, file)}: links to ${repositoryUrl}, which is private ` +
        'and answers 404 to a reader. Link to the documentation site instead, or set ' +
        'repositoryIsPublic in docs-site/site.config.mjs once the repository is open.',
    );
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

console.log(
  `Content: ${manualContent.length} manual pages and ${allContent.length} total pages passed.`,
);
console.log(
  `Contrast floor: text ${floors[4.5].ratio.toFixed(2)}:1 (${floors[4.5].pair}); ` +
    `non-text ${floors[3].ratio.toFixed(2)}:1 (${floors[3].pair}).`,
);

function collect(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? collect(path) : [path];
  });
}

/**
 * Like {@link collect}, but stops at directories nobody in this repository writes by
 * hand: dependencies, build output, and generated pages. Pruning rather than filtering
 * afterwards matters - `collect` over docs-site/ walks tens of thousands of
 * node_modules entries before anything gets a chance to discard them.
 */
function collectSources(directory) {
  const pruned = new Set(['node_modules', 'dist', 'bin', 'obj', '.astro', 'generated']);

  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);

    if (!entry.isDirectory()) {
      return [path];
    }

    return pruned.has(entry.name) || generatedRoots.includes(`${path}${sep}`)
      ? []
      : collectSources(path);
  });
}


function isSynchronizationCopy(file) {
  return / \d+\.(?:md|mdx|json)$/.test(basename(file));
}


/** WCAG 2.x relative luminance, from a #rgb or #rrggbb value. */
function luminance(colour) {
  const text = colour.replace('#', '');
  const full = text.length === 3 ? [...text].map((digit) => digit + digit).join('') : text;
  const [red, green, blue] = [0, 2, 4]
    .map((offset) => parseInt(full.slice(offset, offset + 2), 16) / 255)
    .map((channel) => (channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4));

  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

function contrast(foreground, background) {
  const [lighter, darker] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (lighter + 0.05) / (darker + 0.05);
}
