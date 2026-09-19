import { checkCapacityStamp } from './check-capacity-stamp.mjs';
import { checkConsoleScreens } from './check-console-screens.mjs';
// Product-documentation invariants that are cheap enough to run on every build.
//
// 🚨 This script needs the GENERATED pages on disk. `reference/changelog.md` is
// gitignored and written by `npm run generate`, which npm runs as `prebuild`,
// so on a fresh checkout it does not exist until `astro build` has run once.
// `npm run check` used to call this first, and all three of its findings were
// the same absence: an llms.txt built without the changelog page cannot match
// the committed one, and the 404 exemption list names a page that is not
// there. `check` therefore builds FIRST now. The cost is that a content error
// surfaces after the build rather than before it; the alternative, generating
// twice, pays for docfx twice on every run.
//
// This gate is intentionally about facts and structure, not subjective prose style.
// It catches the defect classes that previously shipped: unusable install commands,
// stale template choices, internal development notes in the public API reference,
// compiler-generated record noise, synchronization copies, and missing capability
// guides.

import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { basename, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build as buildAgentMap, outputs as agentMapOutputs, verifyBudget } from './build-agent-map.mjs';
import { formerHosts, repositoryIsPublic, repositoryUrl, site, siteUrl } from '../site.config.mjs';
import { hasInternalHistory } from './internal-history.mjs';
import { portableRelative as relative } from './path-utils.mjs';
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

/**
 * Every shipped install command names a version.
 *
 * Tracon has no stable release, so `dotnet add package Tracon` alone ends in
 * NU1103 and `dotnet tool install -g Tracon.Cli` in "package not found". The
 * command a reader copies is the first thing they run, and it has to work.
 *
 * This runs over the site pages AND the packaged README files, because the
 * README is what nuget.org renders on the package page -- for a first-time
 * reader that page comes before the site. One definition, both surfaces: the
 * gate used to cover the site only, and 14 commands across 13 package READMEs
 * were shipping without a flag.
 *
 * @param {string} label file label used in the error message
 * @param {string} text file contents
 */
function checkPreviewInstallCommands(label, text) {
  for (const [lineIndex, line] of text.split('\n').entries()) {
    const trimmed = line.trim();

    if (
      /^dotnet add package Tracon(?:\.[A-Za-z0-9.]+)?(?:\s|$)/.test(trimmed) &&
      !trimmed.includes('--prerelease') &&
      !trimmed.includes('--version')
    ) {
      errors.push(`${label}:${lineIndex + 1}: the package is preview; add --prerelease or --version`);
    }

    if (
      /^dotnet tool (?:install|update)\b.*\bTracon\.Cli\b/.test(trimmed) &&
      !trimmed.includes('--prerelease') &&
      !trimmed.includes('--version')
    ) {
      errors.push(`${label}:${lineIndex + 1}: Tracon.Cli is preview; add --prerelease or --version`);
    }

    if (/^dotnet new install Tracon\.Templates(?:\s|$)/.test(trimmed)) {
      errors.push(`${label}:${lineIndex + 1}: preview templates require an explicit @version`);
    }
  }
}

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

// SECURITY.md and the site's security policy page are two audiences for one policy:
// GitHub reads the root file for its Security tab, a consumer reads the site. Two
// copies drift, and a reporting address or a response window that drifts is worse
// than none - the reporter follows the stale one. Only the facts a reporter acts on
// are pinned here; the prose is free to differ, because the two audiences differ.
const securityPolicyFacts = [
  ['reporting address', /hfarukatasoy@gmail\.com/],
  ['acknowledgement window', /\b72\s+hours\b/],
  ['assessment window', /\bseven\s+days\b/],
];
const rootSecurity = readFileSync(join(repositoryRoot, 'SECURITY.md'), 'utf8');
const sitePolicy = readFileSync(join(docsRoot, 'reference/security-policy.md'), 'utf8');

for (const [label, pattern] of securityPolicyFacts) {
  if (!pattern.test(rootSecurity)) {
    errors.push(`SECURITY.md no longer states its ${label}; reference/security-policy.md still does`);
  }
  if (!pattern.test(sitePolicy)) {
    errors.push(`reference/security-policy.md no longer states its ${label}; SECURITY.md still does`);
  }
}

// The threat model's boundary table restates the set of boundaries from the
// authorization guide, for a reader who never opens that page. A phase that adds or
// removes a boundary there and forgets the model leaves a stale promise standing -
// the direction that matters more, because it claims a protection that no longer
// exists. Same pattern as securityPolicyFacts above, applied to a table's first
// column instead of a fixed set of facts.
function boundaryNames(markdown, heading) {
  const headingIndex = markdown.indexOf(heading);
  if (headingIndex === -1) return null;
  const afterHeading = markdown.slice(headingIndex + heading.length);
  const nextHeading = afterHeading.search(/\n#{1,6} /);
  const section = nextHeading === -1 ? afterHeading : afterHeading.slice(0, nextHeading);
  const rows = section.split('\n').filter((line) => line.trim().startsWith('|'));
  // rows[0] is the header row, rows[1] the `---` separator; data starts at rows[2].
  return rows.slice(2).map((row) => row.split('|')[1].trim());
}

const authGuide = readFileSync(join(docsRoot, 'getting-started/security.md'), 'utf8');
const threatModel = readFileSync(join(docsRoot, 'reference/threat-model.md'), 'utf8');
const authGuideBoundaries = boundaryNames(authGuide, '## The boundaries Tracon enforces');
const threatModelBoundaries = boundaryNames(threatModel, '## Boundary mapping');

if (!authGuideBoundaries) {
  errors.push('getting-started/security.md: "The boundaries Tracon enforces" table not found');
} else if (!threatModelBoundaries) {
  errors.push('reference/threat-model.md: "Boundary mapping" table not found');
} else {
  const authSet = new Set(authGuideBoundaries);
  const modelSet = new Set(threatModelBoundaries);
  for (const boundary of authGuideBoundaries) {
    if (!modelSet.has(boundary)) {
      errors.push(`reference/threat-model.md is missing the "${boundary}" boundary that getting-started/security.md lists`);
    }
  }
  for (const boundary of threatModelBoundaries) {
    if (!authSet.has(boundary)) {
      errors.push(`reference/threat-model.md lists a "${boundary}" boundary that getting-started/security.md no longer does`);
    }
  }
}

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
    entry.name.startsWith('Tracon') &&
    entry.name !== 'Tracon.Generators' &&
    existsSync(join(sourceRoot, entry.name, `${entry.name}.csproj`)),
).length;
const openApi = JSON.parse(readFileSync(join(repositoryRoot, 'docs/openapi/tracon.json'), 'utf8'));
const operationCount = Object.values(openApi.paths ?? {}).reduce(
  (total, item) =>
    total + ['get', 'post', 'put', 'patch', 'delete'].filter((method) => item[method]).length,
  0,
);
const uiRoutes = readFileSync(join(sourceRoot, 'Tracon.UI/frontend/src/app.tsx'), 'utf8');

// A "screen" is what the reader sees, so this counts the Screen COMPONENTS the
// route table renders, not the modules they are imported from. Two modules
// (skills, triggers) export a list screen and an editor screen each, so the
// module count is 28 where the component count is 30 - and the pages have
// always said 30. Counting modules made this gate measure something no page
// claims, which is a gate that cannot fail for the reason it exists.
const screenCount = new Set([...uiRoutes.matchAll(/<([A-Z]\w*Screen)\b/g)].map((match) => match[1])).size;

// Domain tags, excluding the umbrella tag every operation also carries.
const tagCount = new Set(
  Object.values(openApi.paths ?? {}).flatMap((item) =>
    Object.entries(item)
      .filter(([method]) => ['get', 'post', 'put', 'patch', 'delete'].includes(method))
      .flatMap(([, operation]) => operation.tags ?? []),
  ),
).size - 1;

for (const [value, label] of [
  [packageCount, 'modular packages'],
  [operationCount, 'generated HTTP operations'],
  [screenCount, 'embedded console screens'],
]) {
  if (!landingPage.includes(`<strong>${value}</strong> ${label}`)) {
    errors.push(`Landing-page metric drift: expected ${value} ${label}`);
  }
}

// The same fact lives in four places and only one was gated: the landing page said
// 168 while README said 162 in one row and 165 in two others. Any number written
// directly before the word "operations" is the OpenAPI total, so the gate reads
// them all rather than naming line numbers that move.
//
// 🚨 The rule's edge: a future sentence about a SUBSET ("12 streaming operations")
// would read as drift here. Write such a sentence without a number, or teach this
// pattern to skip it - do not widen the total.
const readme = readFileSync(join(repositoryRoot, 'README.md'), 'utf8');

for (const [, stated] of readme.matchAll(/\b(\d+) operations\b/g)) {
  if (Number(stated) !== operationCount) {
    errors.push(`README.md says ${stated} operations, expected ${operationCount}`);
  }
}

// The install commands the reader meets first: the repository README and the
// package README that nuget.org renders on each package page.
checkPreviewInstallCommands('README.md', readme);

for (const entry of readdirSync(sourceRoot, { withFileTypes: true })) {
  if (!entry.isDirectory()) {
    continue;
  }

  const packagedReadme = join(sourceRoot, entry.name, 'README.md');

  if (existsSync(packagedReadme)) {
    checkPreviewInstallCommands(
      `src/${entry.name}/README.md`,
      readFileSync(packagedReadme, 'utf8'));
  }
}

// compatibility.md's own package table must track packageCount independently of
// the landing page - Phase 96 dropped 98 types from the surface but left this
// table's heading and row count at a stale "17", two packages short, until this
// check existed (docs/arsiv/fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md, 97.5).
const compatibility = readFileSync(join(docsRoot, 'reference/compatibility.md'), 'utf8');
const packagesHeading = compatibility.match(/^## The (\d+) packages$/m);
const packagesSection = compatibility.split(/^## /m).find((section) => /^The \d+ packages\b/.test(section)) ?? '';
const packageRowCount = [...packagesSection.matchAll(/^\| `Tracon[^`]*` \|/gm)].length;

if (!packagesHeading || Number(packagesHeading[1]) !== packageCount) {
  errors.push(
    `compatibility.md heading says "${packagesHeading?.[1] ?? '(missing)'}" packages, expected ${packageCount}`,
  );
}
if (packageRowCount !== packageCount) {
  errors.push(`compatibility.md package table has ${packageRowCount} row(s), expected ${packageCount}`);
}

// The framework claim is read BEFORE a consumer can compile anything: someone on
// .NET 8 LTS decides from this table whether Tracon is usable at all. One default
// matrix lives in src/Directory.Build.props and a few projects deviate from it, so
// the page restates a fact it cannot see change. It drifted: getting-started said
// "the testing and template packages require .NET 10" for weeks after
// Tracon.Testing rejoined the runtime matrix, and an outside reviewer had to
// reconcile two pages to work out which one was true. Bind the table to the
// csproj files instead, in both directions - a narrowed package with a stale
// `net8/9/10` cell is the direction that costs more, because it promises a
// framework that no longer builds.
const defaultFrameworks = readFileSync(join(sourceRoot, 'Directory.Build.props'), 'utf8')
  .match(/<TargetFrameworks>([^<]+)<\/TargetFrameworks>/)?.[1];

if (!defaultFrameworks) {
  errors.push('src/Directory.Build.props: the default <TargetFrameworks> element was not found');
} else {
  // How the table writes a framework set: `net8.0;net9.0;net10.0` reads `net8/9/10`,
  // and a single framework keeps its own spelling (`net10`, `netstandard2.0`).
  const shorthand = (frameworks) => {
    const parts = frameworks.split(';').map((value) => value.trim()).filter(Boolean);
    if (parts.length === 1) return parts[0].replace(/^net(\d+)\.0$/, 'net$1');
    return `net${parts.map((part) => part.replace(/^net/, '').replace(/\.0$/, '')).join('/')}`;
  };

  // A project deviates only by setting the SINGULAR element; every other project
  // inherits the default. Tracon.Generators ships inside Tracon.Core and owns no
  // row here - the "Target frameworks" table above covers it as the embedded
  // source generator, which is why packageCount excludes it too.
  const deviating = new Map();
  for (const entry of readdirSync(sourceRoot, { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith('Tracon')) continue;
    const projectFile = join(sourceRoot, entry.name, `${entry.name}.csproj`);
    if (!existsSync(projectFile)) continue;
    const single = readFileSync(projectFile, 'utf8').match(/<TargetFramework>([^<]+)<\/TargetFramework>/)?.[1];
    if (single) deviating.set(entry.name, single.trim());
  }

  const seen = new Set();
  for (const row of packagesSection.matchAll(/^\| `(Tracon[^`]*)` \|[^|]*\|([^|]*)\|/gm)) {
    const [, packageName, frameworksCell] = row;
    seen.add(packageName);
    if (!existsSync(join(sourceRoot, packageName, `${packageName}.csproj`))) continue;

    const expected = shorthand(deviating.get(packageName) ?? defaultFrameworks);
    // The cell may add a qualifier ("net10 output", "net8/9/10 dependency groups"),
    // but it must OPEN with the set the project actually builds. The boundary stops
    // `net10` from passing as the opening of `net10.0-windows`.
    const opening = new RegExp(`^${expected.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?![\\w.])`);
    if (!opening.test(frameworksCell.trim())) {
      errors.push(
        `compatibility.md: \`${packageName}\` builds ${deviating.get(packageName) ?? defaultFrameworks}, ` +
        `so its Frameworks cell should open with "${expected}", not "${frameworksCell.trim()}"`,
      );
    }
  }

  for (const packageName of deviating.keys()) {
    if (packageName !== 'Tracon.Generators' && !seen.has(packageName)) {
      errors.push(
        `compatibility.md: \`${packageName}\` targets ${deviating.get(packageName)} instead of the default ` +
        `matrix and has no row in the package table`,
      );
    }
  }

  // The summary table above the package table states the same fact for a reader who
  // never scrolls. It carries full framework spellings, not the shorthand.
  const frameworkSummary = compatibility.split(/^## /m).find((section) => section.startsWith('Target frameworks')) ?? '';
  const testingRow = frameworkSummary.match(/^\| `Tracon\.Testing` \|([^|]*)\|/m)?.[1] ?? '';
  const testingFrameworks = deviating.get('Tracon.Testing') ?? defaultFrameworks;

  // Both directions: an omitted framework hides support, and a listed one the
  // project no longer builds promises a package that will not restore.
  const testingBuilt = new Set(testingFrameworks.split(';').map((value) => value.trim()).filter(Boolean));
  const testingClaimed = new Set(testingRow.match(/net(?:standard)?\d+\.\d+/g) ?? []);

  for (const framework of testingBuilt) {
    if (!testingClaimed.has(framework)) {
      errors.push(
        `compatibility.md "Target frameworks": the \`Tracon.Testing\` row omits ${framework}, which the project builds`,
      );
    }
  }
  for (const framework of testingClaimed) {
    if (!testingBuilt.has(framework)) {
      errors.push(
        `compatibility.md "Target frameworks": the \`Tracon.Testing\` row claims ${framework}, which the project does not build`,
      );
    }
  }
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

// Everything a person typed, 404 included. The structural rules below exempt 404
// because it is a system route with no sidebar entry and no llms index line - but
// that exemption used to cover the whole gate, so the page also escaped the rules
// that have nothing to do with structure: the decision-number leak and this
// phase's voice rules. An exemption is per rule, not per page.
const handWrittenContent = allContent.filter(
  (file) => !generatedRoots.some((directory) => file.startsWith(directory)),
);

const manualContent = handWrittenContent.filter(
  // 404 is a system route, not an article: it is never in the sidebar or llms index.
  (file) => basename(file) !== '404.md',
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

  checkPreviewInstallCommands(label, text);

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


    if (/github\.com\/farukatasoy\/Tracon\/blob\/[^)]+\/src\/[^)]+\.cs/.test(text)) {
      errors.push(`${label}: internal Tracon type links to source instead of the local reference`);
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

errors.push(...checkConsoleScreens(sourceRoot, docsRoot, siteRoot));

// A published capacity number is a shipped claim, and phase 166's audit found
// five of them wrong in the step between the measurement file and the page.
errors.push(...checkCapacityStamp(docsRoot, join(repositoryRoot, 'bench/capacity/measurements')));

const manualProse = manualContent.map((file) => readFileSync(file, 'utf8')).join('\n');

const diagnostics = readFileSync(
  join(sourceRoot, 'Tracon.Core/Diagnostics/TraconDiagnostics.cs'),
  'utf8',
);

const telemetryNames = [...diagnostics.matchAll(/public const string \w+ = "([^"]+)"/g)]
  .map(([, value]) => value)
  // The meter and activity-source name is just "Tracon"; it is documented as
  // prose, not as an identifier, and matching it would accept any page.
  .filter((value) => value !== 'Tracon');

// A gate that collects its own expectations can only fail while it collects
// something. The screenshot gate returned zero for an entire phase and stayed
// green; these two read source the same way, so they carry the same guard.
// `public const string` becoming `static readonly` is all it would take.
if (telemetryNames.length === 0) {
  errors.push(
    'No telemetry names found in TraconDiagnostics.cs; refusing to validate zero names. ' +
      'Check the `public const string` shape the collector above matches.',
  );
}

for (const name of telemetryNames) {
  // Whole name: 'tracon.tenant.id' is a prefix of 'tracon.tenant.identifier',
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

// 🚨 Every manual page that states a countable fact, not just the one page that
// happens to be checked below. Three pages carried stale operation counts (143,
// 143, 162 against a real 165) and one a stale tag count (19 against 23) while
// this gate stayed green, because the gate only knew about the landing page and
// http-api.md. A drifted number is a wrong answer to a reader's question, so
// the rule is that a page may state a count or omit it, but may not state a
// stale one.
//
// Release notes and the changelog are deliberately exempt: their numbers
// describe a dated snapshot, and rewriting those to today's value would destroy
// the record rather than fix it.
// A count, then up to five plain words, then the noun - so "143 operations" and
// "143 management and OpenAI-compatible operations" are both caught. The words
// may not themselves contain a digit, which keeps the match from jumping across
// an unrelated number earlier in the sentence.
const qualifier = String.raw`(?:[A-Za-z][\w-]*[\s]+){0,5}`;

const countedClaims = [
  { pattern: new RegExp(String.raw`(\d+)[\s-]${qualifier}operations\b`, 'g'), expected: operationCount, label: 'HTTP operations' },
  { pattern: new RegExp(String.raw`(\d+)-operation\b`, 'g'), expected: operationCount, label: 'HTTP operations' },
  { pattern: new RegExp(String.raw`grouped under (\d+) tags\b`, 'g'), expected: tagCount, label: 'domain tags' },
  { pattern: new RegExp(String.raw`(\d+)[\s-]${qualifier}screens\b`, 'g'), expected: screenCount, label: 'console screens' },
];

for (const file of manualContent) {
  const slug = relative(docsRoot, file);

  if (slug.startsWith('release') || slug.includes('changelog')) {
    continue;
  }

  // Line breaks are not meaningful to a prose claim; a wrapped sentence must
  // read the same to this gate as an unwrapped one.
  const page = readFileSync(file, 'utf8').replace(/\s+/g, ' ');

  for (const { pattern, expected, label } of countedClaims) {
    for (const [match, value] of page.matchAll(pattern)) {
      if (Number(value) !== expected) {
        errors.push(
          `${slug}: says "${match.trim()}" but there are ${expected} ${label}. ` +
            'Update the number, or drop it if the page does not need to count.',
        );
      }
    }
  }
}

// 🚨 Marked BEHAVIOR claims (F-171, phase 158). A count can be recomputed from
// the code; a sentence like "off by default" cannot - it can only be
// re-measured. This gate runs BEFORE the solution is built (a clean checkout
// has no compiled assembly to reflect on - see the ERR_MODULE_NOT_FOUND trap
// this file already works around above), so it does only the part that needs
// no build: it finds every marked claim and checks that it still names a real
// property or a real scope. The claim's actual VALUE - the real default, the
// real allow/deny behavior - is proved by DocumentedPolicyTests.cs, which runs
// after the build, inside `dotnet test`. Two mechanisms, not one gate, because
// an option default and an endpoint policy are measured by two different means
// (158.1): a reflected object versus a real request.
const optionsMembers = new Map();

for (const file of collect(sourceRoot).filter((entry) => entry.endsWith('Options.cs'))) {
  if (['obj', 'bin'].some((segment) => file.includes(`/${segment}/`))) continue;

  const text = readFileSync(file, 'utf8');

  for (const [, owner] of text.matchAll(/public sealed class (\w+)/g)) {
    optionsMembers.set(
      owner,
      new Set([...text.matchAll(/public\s+[\w<>?,[\]. ]+?\s+(\w+)\s*\{\s*get/g)].map(([, name]) => name)),
    );
  }
}

const validApiKeyScopes = new Set(
  [...readFileSync(join(sourceRoot, 'Tracon.Abstractions/Security/ApiKeyScope.cs'), 'utf8').matchAll(
    /^\s{4}(\w+)\s*=\s*\d+,?\s*$/gm,
  )].map(([, name]) => name),
);

// Same guard, same reason: the pattern pins both the indentation and the
// explicit `= <number>`, so a reformat or an implicit enum value would empty
// this set and every scope claim below would pass unchecked.
if (validApiKeyScopes.size === 0) {
  errors.push(
    'No API key scopes found in ApiKeyScope.cs; refusing to validate zero scopes. ' +
      'Check the indentation and explicit values the collector above matches.',
  );
}

// A marker sits right next to the sentence it measures, as an HTML comment -
// invisible on the rendered page, readable from the source both by a human
// editing the sentence and by this scan (Open Question 1). The value after
// the property is a fixed string today because every claim in this first
// round is a bool (158.3); DocumentedPolicyTests.cs is the one that actually
// evaluates it against the real code.
//
// 🚨 That alone is not enough (denetim, 158, finding 1): DocumentedPolicyTests
// reads the marker's value, never the READER'S sentence, so a page could say
// "off by default" while its marker still claims `false` after the real
// default flipped to `true` and BOTH gates would stay green - the marker and
// the prose would have drifted apart from each other, silently, which is the
// exact failure this phase exists to prevent. So every marked sentence must
// also state its value as a literal, backtick-quoted token immediately before
// the marker (only closing punctuation allowed between them), and this gate
// checks that literal against the marker's own value - closing the triangle:
// prose == marker (here) and marker == real code (DocumentedPolicyTests.cs).
const adjacentLiteralPattern = /`([^`]*)`[)\].,;:\s]*$/;
const claimPattern = /<!--\s*claim:(option|policy)\s+(.+?)\s*-->/g;
let markedClaims = 0;

for (const file of manualContent) {
  const label = relative(docsRoot, file);
  const text = readFileSync(file, 'utf8');

  for (const match of text.matchAll(claimPattern)) {
    const [whole, kind, payload] = match;
    markedClaims += 1;

    const before = adjacentLiteralPattern.exec(text.slice(0, match.index));

    if (kind === 'option') {
      const optionMatch = /^([A-Za-z0-9_]+)\.([A-Za-z0-9_]+)=(\S+)$/.exec(payload);

      if (!optionMatch) {
        errors.push(`${label}: malformed option claim '${whole}'`);
        continue;
      }

      const [, type, property, value] = optionMatch;
      const properties = optionsMembers.get(type);

      if (!properties) {
        errors.push(`${label}: claim names '${type}', which is not a public sealed Options type`);
      } else if (!properties.has(property)) {
        errors.push(`${label}: claim names '${type}.${property}', which is not one of its properties`);
      }

      if (!before || before[1].toLowerCase() !== value.toLowerCase()) {
        errors.push(
          `${label}: claim says ${type}.${property}=${value}, but the sentence right before the marker ` +
            `does not state that value as a backtick-quoted literal (found: ${before ? `'${before[1]}'` : 'none'})`,
        );
      }
    } else {
      const policyMatch = /^([A-Z]+)\s+(\S+)\s+scope=(\S+)$/.exec(payload);

      if (!policyMatch) {
        errors.push(`${label}: malformed policy claim '${whole}'`);
        continue;
      }

      const [, , , scope] = policyMatch;

      if (!validApiKeyScopes.has(scope)) {
        errors.push(`${label}: claim names scope '${scope}', which is not a member of ApiKeyScope`);
      }

      if (!before || before[1] !== scope) {
        errors.push(
          `${label}: claim says scope=${scope}, but the sentence right before the marker does not state ` +
            `that scope as a backtick-quoted literal (found: ${before ? `'${before[1]}'` : 'none'})`,
        );
      }
    }
  }
}

// Reported, not enforced (158.2): forcing every "by default" sentence under a
// marker on day one would turn 147 lines red at once instead of the nine this
// round measured (158.3), and a gate nobody can make green gets silenced. The
// two counts are printed at the bottom of this file on every run, so the gap
// stays visible without blocking on it.
let defaultPhraseMentions = 0;

for (const file of manualContent) {
  const slug = relative(docsRoot, file);

  if (slug.startsWith('release') || slug.includes('changelog')) continue;

  defaultPhraseMentions += [
    ...readFileSync(file, 'utf8').matchAll(/\bdefaults? to\b|\bby default\b/gi),
  ].length;
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

// The packaged copy, which Tracon.AspNetCore ships under buildTransitive/.
// The site copy below is sanitised; this one reaches the consumer unedited.
const packagedOpenApi = join(repositoryRoot, 'docs/openapi/tracon.json');

if (existsSync(packagedOpenApi)) {
  const text = readFileSync(packagedOpenApi, 'utf8');

  if (hasInternalHistory(text)) {
    errors.push('docs/openapi/tracon.json: internal development history is packaged into Tracon.AspNetCore');
  }

  // Two shapes: a nullable member ("string? X.Y") and a plain one ("string X.Y").
  // Measured: fixing only the first left seventeen of the second behind.
  const signatureLeak =
    /\b(?:[A-Za-z_][A-Za-z0-9_.<>]*|[A-Za-z_][A-Za-z0-9_]*\[\])\?\s+(?=[A-Z][A-Za-z0-9_.]+\b)|\b(?:int|long|bool|string|double|decimal|float|TimeSpan|DateTimeOffset|Guid|JsonElement|Uri)(?:\[\])?\s+[A-Z][A-Za-z0-9_]*\.[A-Z][A-Za-z0-9_]*\b/;

  if (signatureLeak.test(text)) {
    errors.push(
      'docs/openapi/tracon.json: a <see cref> rendered as a full CLR signature; ' +
        'use <c>MemberName</c> in the contract documentation instead',
    );
  }
}

const publishedOpenApi = join(siteRoot, 'public/openapi/tracon.json');
if (existsSync(publishedOpenApi)) {
  const text = readFileSync(publishedOpenApi, 'utf8');
  if (hasInternalHistory(text)) {
    errors.push('public/openapi/tracon.json: internal development history leaked into OpenAPI');
  }
  if (/\brationale\b|\b(?:int|long|decimal|double|string|float\[\])\?\s+[A-Z][A-Za-z0-9_.]+/i.test(text)) {
    errors.push('public/openapi/tracon.json: malformed source prose leaked into OpenAPI');
  }
  if (/\/api\/keys\b/.test(text)) {
    errors.push('public/openapi/tracon.json: API key routes use /api/api-keys');
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
      if (!operation['x-tracon-role']) {
        errors.push(`public OpenAPI operation ${operation.operationId} has no role metadata`);
      }
      if (
        !operation['x-tracon-api-key-scope'] &&
        operation.operationId !== 'TraconCurrentTenant'
      ) {
        errors.push(`public OpenAPI operation ${operation.operationId} has no API-key scope metadata`);
      }
    }
  }

  const expectedAnonymous = ['TraconAcceptInboundTrigger', 'TraconMcpOAuthCallback', 'TraconMeta'];
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
  ['reference/changelog.md', 'a dated list of released versions, with no flow between entries'],
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

  for (const [, name, value] of body.matchAll(/(--tracon-[\w-]+):\s*([^;]+);/g)) {
    const colour = /^#[0-9a-fA-F]{3,8}$/.test(value.trim()) ? value.trim() : null;
    if (!colour) continue;
    for (const target of targets) themes[target].set(name, colour);
  }
}

// A colour declared anywhere else — inside a component rule, a media query — would
// never reach the maps above and so would never be measured. Measured: adding
// `.card { --tracon-sneaky: #ff0000; }` left the gate green. The token set is closed, so
// the declaration site is part of the contract.
for (const [, selectors, body] of styleSheet.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
  const list = selectors.split(',').map((entry) => entry.trim());
  if (list.includes(':root') || list.includes(":root[data-theme='light']")) continue;

  for (const [, name, value] of body.matchAll(/(--tracon-[\w-]+):\s*([^;]+);/g)) {
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
const DECORATIVE = new Map([['--tracon-border', 'a hairline: spacing and surface carry the same boundary']]);

const CONTRAST_PAIRS = [
  ...['success', 'warning', 'danger'].flatMap((name) => [
    [`--tracon-${name}`, '--tracon-surface', 4.5],
    [`--tracon-${name}`, '--tracon-surface-raised', 4.5],
  ]),
  ['--tracon-text', '--tracon-surface', 4.5],
  ['--tracon-text', '--tracon-surface-raised', 4.5],
  ['--tracon-text-strong', '--tracon-surface', 4.5],
  ['--tracon-text-strong', '--tracon-surface-raised', 4.5],
  ['--tracon-text-muted', '--tracon-surface', 4.5],
  ['--tracon-text-muted', '--tracon-surface-raised', 4.5],
  ['--tracon-accent', '--tracon-surface', 4.5],
  ['--tracon-accent', '--tracon-surface-raised', 4.5],
  ['--tracon-accent', '--tracon-accent-quiet', 4.5],
  ['--tracon-code', '--tracon-surface-sunken', 4.5],
  ['--tracon-code', '--tracon-surface-raised', 4.5],
  ['--tracon-diagram-ink', '--tracon-diagram-plate', 4.5],
  ['--tracon-diagram-ink', '--tracon-diagram-node', 4.5],
  ['--tracon-diagram-ink', '--tracon-diagram-cluster', 4.5],
  // Non-text boundaries: WCAG 1.4.11 asks for 3:1, not 4.5:1.
  ['--tracon-border-strong', '--tracon-surface', 3],
  ['--tracon-border-strong', '--tracon-surface-raised', 3],
  ['--tracon-diagram-line', '--tracon-diagram-plate', 3],
  ['--tracon-diagram-line', '--tracon-diagram-node', 3],
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
//     hand-written pages too: a reader cannot resolve a decision number. 404 is a
//     page a reader reaches, so it answers to this rule like any other.
for (const file of handWrittenContent) {
  const text = readFileSync(file, 'utf8');

  if (hasInternalHistory(text)) {
    errors.push(`${relative(docsRoot, file)}: internal development history leaked into a public page`);
  }

  // A per-package framework REQUIREMENT is owned by the compatibility matrix, which
  // is bound to the csproj files above. A page that restates it in prose is a copy
  // nothing can check, and that copy is what went stale: "the testing and template
  // packages require .NET 10" outlived the change that made it false. Name the
  // framework a package builds, or link the matrix - do not write a requirement.
  if (!file.endsWith(join('reference', 'compatibility.md'))) {
    const restated = text.match(/[^.\n]*\brequires?\b[^.\n]*\.NET\s*\d+[^.\n]*/i);
    if (restated) {
      errors.push(
        `${relative(docsRoot, file)}: restates a framework requirement the compatibility matrix owns ` +
        `- "${restated[0].trim()}"`,
      );
    }
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
  join(sourceRoot, 'Tracon.Generators', 'DocumentationLinks.cs'),
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
  // packages/tracon-client/README.md ships to npm the same way a NuGet
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

// ---------------------------------------------------------------------------
// Sizes of generated files, quoted in prose. The capability map, the page index
// and the full text all grow with the documentation itself, so a number typed
// once drifts without anyone touching the sentence: measured here, the page
// still said "about 400 KB" for a file that had reached 713 KB, and "under
// 10 KB" for a map of 10.3 KB. A reader sizes a context window with these, so
// the rule is the one this file already applies to counts - state it accurately
// or leave it out. The band is wide because the prose says "about": it catches
// a number that has drifted, not one that rounded.
// ---------------------------------------------------------------------------
const sizeTolerance = 0.15;

const quotedSizes = [
  {
    page: 'guides/coding-agents.md',
    path: join(repositoryRoot, 'src', 'Tracon.Core', 'buildTransitive', 'Tracon.AgentMap.md'),
    pattern: /repository root\*\*, about (\d+(?:\.\d+)?) KB/,
    label: 'the capability map',
  },
  {
    page: 'guides/coding-agents.md',
    path: join(siteRoot, 'public', 'llms.txt'),
    pattern: /title, address, and subject\. About (\d+(?:\.\d+)?) KB/,
    label: 'llms.txt',
  },
  {
    page: 'guides/coding-agents.md',
    path: join(siteRoot, 'public', 'llms-full.txt'),
    pattern: /page concatenated, about (\d+(?:\.\d+)?) KB/,
    label: 'llms-full.txt',
  },
];

for (const { page, path, pattern, label } of quotedSizes) {
  // Line breaks are not meaningful to a prose claim, the same way they are not
  // to the counted claims above.
  const text = readFileSync(join(docsRoot, page), 'utf8').replace(/\s+/g, ' ');
  const match = pattern.exec(text);

  if (!match) {
    errors.push(
      `${page}: no longer states the size of ${label}. Restore the sentence, or ` +
        'drop its entry from quotedSizes in this gate if the page should not size it.',
    );
    continue;
  }

  const actual = statSync(path).size / 1000;
  const claimed = Number(match[1]);

  if (Math.abs(actual - claimed) / actual > sizeTolerance) {
    errors.push(
      `${page}: says ${label} is about ${claimed} KB, but it is ${actual.toFixed(1)} KB. ` +
        'Update the number, or drop it if the page does not need to size the file.',
    );
  }
}

// ---------------------------------------------------------------------------
// The development journal's alarm voice, on a page a consumer reads. Two other
// surfaces already refuse it: build-api-reference.mjs rewrites 🚨 and ⚠️ into
// "**Important:**" on its way out, and the package's own XML answers to
// ShippedDocumentationSelfContainmentTests. A page typed straight into
// src/content/docs/ passes through neither, so the marker reached the reader
// unstripped in three paragraphs across two guides while both of those gates
// stayed green. The warning itself is worth keeping; only the shouting is not.
// ---------------------------------------------------------------------------
const alarmVoice = /🚨|⚠️/u;

// The article the product name no longer takes. Renaming the product left "an
// Tracon agent" behind on thirteen hand-written pages, because the article was
// correct for the name it replaced. A find-and-replace cannot see it: the wrong
// word is the one the rename never touched. It reads as a typo on every page it
// survives on, so the rule is cheap to state and worth keeping after the
// clean-up. Whitespace is flattened first - the article often ends a line and
// the name starts the next - and inline code or a link may sit between them.
// No word boundary after the name on purpose: the article is just as wrong in
// front of a type that starts with it. "throws an TraconException" hid from a
// `Tracon\b` pattern through four clean-up passes, in XML that ships to every
// consumer's IntelliSense.
const wrongArticle = /\b[Aa]n\s+(?:`|\[)?Tracon/;

// The article check runs over EVERY page, generated ones included. Sixty of the
// occurrences were not typed by anyone: the reference generator rendered a type
// reference in its member form, so XML that correctly reads "an IRunJudge"
// shipped as "an Tracon.IRunJudge". That is a generator regression no gate over
// hand-written pages can see. The alarm-emoji rule stays on the manual set,
// because the generator rewrites the marker on its way out by design.
for (const file of allContent) {
  const label = relative(docsRoot, file);
  const text = readFileSync(file, 'utf8');
  const isHandWritten = handWrittenContent.includes(file);

  for (const [index, line] of isHandWritten ? text.split('\n').entries() : []) {
    if (alarmVoice.test(line)) {
      errors.push(
        `${label}:${index + 1}: alarm emoji in shipped prose — "${line.trim().slice(0, 60)}". ` +
          'Keep the warning, drop the marker: lead with bold text, an aside, or ' +
          '"**Important:**" the way the API-reference generator rewrites it.',
      );
    }
  }

  const flattened = text.replace(/\s+/g, ' ');
  const article = wrongArticle.exec(flattened);

  if (article) {
    errors.push(
      `${label}: "${article[0]}" — the product name takes "a", not "an". ` +
        `Context: "${flattened.slice(Math.max(0, article.index - 40), article.index + 40).trim()}".`,
    );
  }
}

// ---------------------------------------------------------------------------
// The brand mark ships as three byte-identical committed copies, because neither
// the console build nor `dotnet pack` may depend on the site. `build-package-icon.mjs`
// writes all three, but it runs by hand - it is in neither `prebuild` nor `check` -
// so nothing noticed if one copy drifted. The agent map solved the same trade the
// same way: commit the derivative, then gate the drift.
// ---------------------------------------------------------------------------
const markSource = join(repositoryRoot, 'assets', 'tracon-mark.svg');
const markCopies = [
  join(siteRoot, 'public', 'favicon.svg'),
  join(sourceRoot, 'Tracon.UI', 'frontend', 'src', 'assets', 'tracon-mark.svg'),
];

if (!existsSync(markSource)) {
  errors.push('assets/tracon-mark.svg is missing; it is the source every other copy is cut from.');
} else {
  const original = readFileSync(markSource);

  for (const copy of markCopies) {
    if (!existsSync(copy)) {
      errors.push(
        `${relative(repositoryRoot, copy)} is missing; run: node docs-site/scripts/build-package-icon.mjs`,
      );
    } else if (!readFileSync(copy).equals(original)) {
      errors.push(
        `${relative(repositoryRoot, copy)} differs from assets/tracon-mark.svg; ` +
          'run: node docs-site/scripts/build-package-icon.mjs',
      );
    }
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
console.log(
  `Behavior claims: ${markedClaims} marked and verified by DocumentedPolicyTests.cs; ` +
    `${defaultPhraseMentions} sentence(s) across manual pages match "by default" or "defaults to" ` +
    '(marked or not - see 158.2).',
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
