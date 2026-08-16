// Turns DocFX's markdown output into Starlight content pages.
//
// Why a transform step exists at all:
//
//   DocFX writes real markdown for structured sections (signature, parameters,
//   returns) but leaves every `<see cref="..."/>` that appears in PROSE as a raw
//   `<xref href="..."></xref>` element. Measured on this repository: 1351 of them
//   across 374 of 590 pages. Rendered as-is they are invisible — the reader loses
//   the link AND the type name.
//
//   The alternative was to let DocFX build its own HTML site under /api/. That
//   works, but it is a second site with a second theme and a second search index.
//   Resolving the references ourselves keeps one site, one theme, and one Pagefind
//   index over everything.
//
// The one rule this script will not break: it never invents a URL. An internal uid
// is linked only when the page exists; an external one only when DocFX itself linked
// that same type somewhere in this build. Anything else is rendered as inline code,
// which loses a link but never produces a broken one.

import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(here, '../..');
const docfxDirectory = join(repositoryRoot, 'docfx');
const metadataDirectory = join(docfxDirectory, 'api-md');
const outputDirectory = join(here, '../src/content/docs/api');
const sidebarFile = join(here, '../src/generated/api-sidebar.json');

const skipDocfx = process.argv.includes('--skip-docfx');

/**
 * Two different prefixes, and mixing them up is the classic Starlight mistake.
 *
 * `apiBase` goes into MARKDOWN links, which Astro copies through untouched, so it
 * carries the site base. `sidebarBase` goes into the sidebar config, where Starlight
 * prepends the base itself — including it there produces `/AgentPrism/AgentPrism/...`.
 * The link checker catches it; this comment is here so it is not reintroduced.
 */
const apiBase = '/AgentPrism/api';
const sidebarBase = '/api';

main();

function main() {
  if (!skipDocfx) {
    runDocfx();
  }

  if (!existsSync(metadataDirectory)) {
    throw new Error(
      `DocFX produced no output at ${metadataDirectory}. Build the solution first ` +
        '(dotnet build AgentPrism.slnx -c Release), then run this script again.',
    );
  }

  const files = readdirSync(metadataDirectory).filter((name) => name.endsWith('.md'));

  if (files.length === 0) {
    throw new Error(`No markdown files under ${metadataDirectory}.`);
  }

  const pages = files.map((name) => readPage(name));
  const uids = new Set(pages.map((page) => page.uid));
  const externalSlugs = harvestExternalSlugs(pages);

  rmSync(outputDirectory, { recursive: true, force: true });
  mkdirSync(outputDirectory, { recursive: true });

  let unresolved = 0;

  for (const page of pages) {
    const { body, unresolvedCount } = transform(page, uids, externalSlugs);
    unresolved += unresolvedCount;
    writeFileSync(join(outputDirectory, `${page.uid}.md`), body);
  }

  writeFileSync(join(outputDirectory, 'index.md'), buildIndex(pages));

  mkdirSync(dirname(sidebarFile), { recursive: true });
  writeFileSync(sidebarFile, `${JSON.stringify(buildSidebar(pages), null, 2)}\n`);

  const types = pages.filter((page) => page.kind !== 'Namespace').length;

  console.log(
    `API reference: ${types} types across ${new Set(pages.flatMap((p) => p.assemblies)).size} ` +
      `assemblies; ${unresolved} cross-reference(s) rendered as code because no target exists.`,
  );
}

function runDocfx() {
  console.log('Running docfx metadata (assembly + XML mode, no MSBuild)…');

  execFileSync('dotnet', ['docfx', 'metadata', 'docfx.json'], {
    cwd: docfxDirectory,
    stdio: 'inherit',
  });
}

/** Reads one DocFX page and pulls out the facts the site needs. */
function readPage(fileName) {
  const uid = fileName.replace(/\.md$/, '');
  const raw = readFileSync(join(metadataDirectory, fileName), 'utf8');

  // `# <a id="AgentPrism_IAgentCatalog"></a> Interface IAgentCatalog`
  const heading = /^# <a id="[^"]*"><\/a> (\w+) (.+)$/m.exec(raw);
  const kind = heading?.[1] ?? 'Type';
  const name = (heading?.[2] ?? uid).trim();

  // `Assembly: AgentPrism.Abstractions.dll  ` — a type compiled into more than one
  // package (the shared SQL sources) lists them all on one line.
  const assemblyLine = /^Assembly: (.+)$/m.exec(raw);
  const assemblies = (assemblyLine?.[1] ?? '')
    .split(',')
    .map((entry) => entry.trim().replace(/\.dll$/, ''))
    .filter(Boolean);

  return { uid, fileName, raw, kind, name, assemblies };
}

/**
 * Collects the learn.microsoft.com slugs DocFX itself linked in this build.
 *
 * This is the whole external link budget on purpose. DocFX links a BCL type it knows
 * and leaves everything else as plain text; mirroring that decision keeps a page
 * internally consistent and guarantees no invented URL.
 */
function harvestExternalSlugs(pages) {
  const slugs = new Set();

  for (const page of pages) {
    for (const match of page.raw.matchAll(/https:\/\/learn\.microsoft\.com\/dotnet\/api\/([a-z0-9._\\-]+)/g)) {
      slugs.add(match[1].replaceAll('\\', ''));
    }
  }

  return slugs;
}

function transform(page, uids, externalSlugs) {
  let unresolvedCount = 0;
  let body = page.raw;

  // 1. The heading moves into frontmatter; Starlight renders the title itself.
  body = body.replace(/^# <a id="[^"]*"><\/a> .+$/m, '');

  // 2. Namespace/Assembly become one quiet line instead of two loose ones.
  //    Written as markdown, NOT wrapped in a <p>: markdown inside a raw HTML block
  //    is not processed, and the namespace link would render as literal brackets.
  body = body.replace(
    /^Namespace: (.+?)\s*\nAssembly: (.+?)\s*$/m,
    (_match, namespaceText, assemblyText) =>
      `*Namespace ${namespaceText} · Assembly \`${assemblyText.trim()}\`*\n`,
  );

  // 3. Prose cross-references. This is the reason the script exists.
  body = body.replace(/<xref href="([^"]+)"[^>]*>\s*<\/xref>/g, (_match, rawUid) => {
    const link = resolveReference(decodeUid(rawUid), uids, externalSlugs);

    if (link === null) {
      unresolvedCount += 1;
      return `<code>${displayName(decodeUid(rawUid))}</code>`;
    }

    return link;
  });

  // 4. Page-to-page links: DocFX writes `Foo.md`, Starlight serves `/api/foo/`.
  body = body.replace(/\]\((AgentPrism[^)\s#]*)\.md(#[^)\s]*)?\)/g, (_match, uid, anchor) =>
    uids.has(uid) ? `](${apiBase}/${uid.toLowerCase()}/${anchor ?? ''})` : `](${apiBase}/)`,
  );

  const frontmatter = [
    '---',
    `title: ${quote(page.name)}`,
    `description: ${quote(`${page.kind} ${page.name} in ${page.assemblies[0] ?? 'AgentPrism'}.`)}`,
    `slug: api/${page.uid.toLowerCase()}`,
    'editUrl: false',
    'lastUpdated: false',
    '---',
    '',
  ].join('\n');

  return { body: frontmatter + body.trimStart(), unresolvedCount };
}

/** DocFX percent-encodes uids that carry a signature. */
function decodeUid(uid) {
  try {
    return decodeURIComponent(uid.replaceAll('%60', '`'));
  } catch {
    return uid;
  }
}

/**
 * Turns a uid into a markdown link, or null when nothing can be linked honestly.
 *
 * Internal uids fall back through their declaring types, so a member reference lands
 * on its type's page at the member's anchor.
 */
function resolveReference(uid, uids, externalSlugs) {
  const bare = uid.replace(/\(.*$/, '');

  if (uids.has(bare)) {
    // A type reads as its own name. Using the member form here would render the
    // namespace as if it were a declaring type: `AgentPrism.IRunStore`.
    return `[${shortName(uid)}](${apiBase}/${bare.toLowerCase()}/)`;
  }

  const segments = bare.split('.');

  for (let index = segments.length - 1; index > 0; index -= 1) {
    const owner = segments.slice(0, index).join('.');

    if (uids.has(owner)) {
      const anchor = bare.replaceAll('.', '_').replaceAll('`', '-');
      return `[${displayName(uid)}](${apiBase}/${owner.toLowerCase()}/#${anchor})`;
    }
  }

  const slug = bare.toLowerCase().replaceAll('`', '-');

  if (externalSlugs.has(slug)) {
    return `[${shortName(uid)}](https://learn.microsoft.com/dotnet/api/${slug})`;
  }

  for (let index = segments.length - 1; index > 0; index -= 1) {
    const ownerSlug = segments.slice(0, index).join('.').toLowerCase().replaceAll('`', '-');

    if (externalSlugs.has(ownerSlug)) {
      return `[${displayName(uid)}](https://learn.microsoft.com/dotnet/api/${ownerSlug})`;
    }
  }

  return null;
}

/** The last segment: `AgentPrism.IRunStore` reads as `IRunStore`. */
function shortName(uid) {
  const bare = uid.replace(/\(.*$/, '').replace(/`\d+/g, '');
  const text = bare.split('.').pop();

  return uid.includes('(') ? `${text}()` : text;
}

/** A member carries its declaring type: `ModelBinding.Provider`. */
function displayName(uid) {
  const bare = uid.replace(/\(.*$/, '').replace(/`\d+/g, '');
  const tail = bare.split('.').slice(-2);
  const text = tail.length === 2 ? tail.join('.') : tail[0];

  return uid.includes('(') ? `${text}()` : text;
}

function buildIndex(pages) {
  const byAssembly = groupByAssembly(pages);

  const rows = [...byAssembly.entries()]
    .sort((left, right) => right[1].length - left[1].length)
    .map(([assembly, items]) => `| \`${assembly}\` | ${items.length} |`)
    .join('\n');

  const types = pages.filter((page) => page.kind !== 'Namespace').length;

  return `---
title: API reference
description: Every public type in the AgentPrism packages, generated from the compiled assemblies and their XML documentation.
slug: api
tableOfContents: false
editUrl: false
lastUpdated: false
---

${types} public types across ${byAssembly.size} packages. These pages are generated
from the compiled assemblies and the XML documentation that ships inside each
\`.nupkg\`, so what you read here is exactly what your IDE shows you.

Use the sidebar to browse by package, or the search box — the reference is indexed
along with the rest of the site.

| Package | Public types |
|---|---|
${rows}

\`AgentPrism\` and \`AgentPrism.Templates\` are absent on purpose: the first is a meta
package that only carries references, and the second ships a \`dotnet new\` template
rather than an API.

## Where to start

- [\`IAgentCatalog\`](${apiBase}/agentprism.iagentcatalog/) — resolving an agent, the entry point to a run
- [\`IRunStore\`](${apiBase}/agentprism.irunstore/) — where every run is recorded
- [\`IAgentPrismBuilder\`](${apiBase}/agentprism.iagentprismbuilder/) — the registration chain
- [\`AgentDefinition\`](${apiBase}/agentprism.agentdefinition/) — what an agent is, as data
`;
}

function groupByAssembly(pages) {
  const groups = new Map();

  for (const page of pages) {
    if (page.kind === 'Namespace') {
      continue;
    }

    const assembly = page.assemblies[0] ?? 'AgentPrism';
    groups.set(assembly, [...(groups.get(assembly) ?? []), page]);
  }

  return groups;
}

/** One collapsed group per package, then one entry per type. */
function buildSidebar(pages) {
  const byAssembly = groupByAssembly(pages);

  return [...byAssembly.entries()]
    .sort((left, right) => right[1].length - left[1].length)
    .map(([assembly, items]) => ({
      label: assembly,
      collapsed: true,
      items: items
        .sort((left, right) => left.name.localeCompare(right.name))
        .map((page) => ({ label: page.name, link: `${sidebarBase}/${page.uid.toLowerCase()}/` })),
    }));
}

function quote(text) {
  return `"${text.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}"`;
}
