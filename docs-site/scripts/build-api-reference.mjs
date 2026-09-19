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
import { internalHistoryMarker } from './internal-history.mjs';

import { base } from '../site.config.mjs';

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
 * prepends the base itself — including it there would double the prefix.
 *
 * They hold the same value while `base` is '/', which is what hides the mistake:
 * swap them today and nothing breaks, then a move to a sub-path base breaks every
 * sidebar entry at once. Keep them apart. The link checker catches the doubling.
 */
const apiBase = `${base}api`;
const sidebarBase = '/api';

main();

function main() {
  if (!skipDocfx) {
    runDocfx();
  }

  if (!existsSync(metadataDirectory)) {
    throw new Error(
      `DocFX produced no output at ${metadataDirectory}. Build the solution first ` +
        '(dotnet build Tracon.slnx -c Release), then run this script again.',
    );
  }

  const files = readdirSync(metadataDirectory).filter((name) => name.endsWith('.md'));

  if (files.length === 0) {
    throw new Error(`No markdown files under ${metadataDirectory}.`);
  }

  // A page's uid is its file name, so the type set is known before any page is
  // read. `readPage` needs it: a summary renders references as plain text, and
  // telling a type from a member is the difference between "an IRunJudge" and
  // "an Tracon.IRunJudge" (see `plainText`).
  const uids = new Set(files.map((name) => name.replace(/\.md$/, '')));
  const pages = files.map((name) => readPage(name, uids));
  const anchorsByUid = new Map(pages.map((page) => [page.uid, page.anchors]));
  const uidsByDisplayName = buildDisplayNameIndex(pages);
  const externalSlugs = harvestExternalSlugs(pages);

  rmSync(outputDirectory, { recursive: true, force: true });
  mkdirSync(outputDirectory, { recursive: true });

  let unresolved = 0;

  for (const page of pages) {
    const { body, unresolvedCount } = transform(
      page,
      uids,
      anchorsByUid,
      uidsByDisplayName,
      externalSlugs,
    );
    unresolved += unresolvedCount;
    writeFileSync(join(outputDirectory, `${page.uid}.md`), body);
  }

  writeFileSync(join(outputDirectory, 'index.md'), buildIndex(pages));

  for (const [assembly, items] of groupByAssembly(pages)) {
    writeFileSync(
      join(outputDirectory, `package-${slugify(assembly)}.md`),
      buildPackageIndex(assembly, items),
    );
  }

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

  // docfx metadata is incremental: a type that stops being public leaves its
  // old .md file behind under api-md instead of removing it (measured, Phase
  // 96 - a package's public surface shrank but the stale page for the
  // now-internal type kept surviving into src/content/docs/api). Wiping the
  // directory first forces every run to reflect only the CURRENT surface.
  rmSync(metadataDirectory, { recursive: true, force: true });

  execFileSync('dotnet', ['docfx', 'metadata', 'docfx.json'], {
    cwd: docfxDirectory,
    stdio: 'inherit',
  });
}

/** Reads one DocFX page and pulls out the facts the site needs. */
function readPage(fileName, uids) {
  const uid = fileName.replace(/\.md$/, '');
  const raw = readFileSync(join(metadataDirectory, fileName), 'utf8');

  // `# <a id="Tracon_IAgentCatalog"></a> Interface IAgentCatalog`
  const heading = /^# <a id="[^"]*"><\/a> (\w+) (.+)$/m.exec(raw);
  const kind = heading?.[1] ?? 'Type';
  const name = (heading?.[2] ?? uid).trim();

  // `Assembly: Tracon.Abstractions.dll  ` — a type compiled into more than one
  // package (the shared SQL sources) lists them all on one line.
  const assemblyLine = /^Assembly: (.+)$/m.exec(raw);
  const assemblies = (assemblyLine?.[1] ?? '')
    .split(',')
    .map((entry) => entry.trim().replace(/\.dll$/, ''))
    .filter(Boolean);

  const anchors = new Set([...raw.matchAll(/<a id="([^"]+)"><\/a>/g)].map((match) => match[1]));
  const fallbackSummary = kind === 'Namespace'
    ? `Public types in the ${name} namespace.`
    : `${kind} ${name} in the ${assemblies[0] ?? 'Tracon'} package.`;
  const extractedSummary = sanitizeInternalHistory(extractSummary(raw, fallbackSummary, uids)).trim();
  const summary = extractedSummary && !extractedSummary.startsWith('#')
    ? extractedSummary
    : fallbackSummary;

  return { uid, fileName, raw, kind, name, assemblies, anchors, summary };
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

function transform(page, uids, anchorsByUid, uidsByDisplayName, externalSlugs) {
  let unresolvedCount = 0;
  let body = page.raw;

  // 1. The heading moves into frontmatter; Starlight renders the title itself.
  body = body.replace(/^# <a id="[^"]*"><\/a> .+$/m, '');

  // 2. Prose cross-references. This is the reason the script exists.
  //
  // 🚨 A `<see cref="X">text</see>` (one WITH inner text) does not reach here as
  // an element with children: DocFX folds the text into the href as a
  // `?text=` query. Without stripping it the reader saw the query string
  // itself -- `Threading.CancellationToken?text=cooperative` -- in the middle
  // of a sentence. The author's own words are the better label, so they become
  // the link text.
  body = body.replace(/<xref href="([^"]+)"[^>]*>\s*<\/xref>/g, (_match, rawUid) => {
    const { uid, text } = splitReferenceText(decodeUid(rawUid));
    const link = resolveReference(uid, uids, anchorsByUid, externalSlugs);

    if (link === null) {
      unresolvedCount += 1;
      return `<code>${text ?? displayName(uid)}</code>`;
    }

    return text === null ? link : relabel(link, text);
  });

  // 3. Page-to-page links: DocFX writes `Foo.md`, Starlight serves `/api/foo/`.
  //
  // The fragment arm has to tolerate DocFX's escaping. In an `Extension Methods`
  // list it writes the whole tail escaped — `Foo.md\#Tracon\_Bar\_Baz` — and a
  // pattern demanding a bare `#` left those 640 links pointing at a `.md` path that
  // is not published. They resolved relative to the page, so they were 404s that
  // check-links could not see: it treats anything not starting with `/` as external.
  // A generic type escapes its arity the same way (`Contract\-1.md`), so the name arm
  // steps over an escape too — but never over `\#`, or it would swallow the fragment.
  body = body.replace(/\]\((Tracon(?:[^)\s#\\]|\\[^#])*)\.md(?:\\?#((?:[^)\s\\]|\\.)*))?\)/g, (_match, target, anchor) => {
    const uid = target.replaceAll('\\', '');
    const fragment = anchor ? `#${anchor.replaceAll('\\', '')}` : '';

    return uids.has(uid) ? `](${apiBase}/${uid.toLowerCase()}/${fragment})` : `](${apiBase}/)`;
  });

  // DocFX sometimes chooses a pinned GitHub source link for a public Tracon
  // type even when that type has a page in this reference. Keep readers inside
  // the reference; genuine "View source" links are not matched by the name index.
  body = body.replace(
    /\[([^\]]+)\]\(https:\/\/github\.com\/farukatasoy\/Tracon\/blob\/[^)]+\/src\/[^)]+\.cs\)/g,
    (match, label) => {
      const display = label.replaceAll('\\', '').replace(/<.*$/, '').trim();
      const target = uidsByDisplayName.get(display);
      if (target) {
        return `[${label}](${apiBase}/${target.toLowerCase()}/)`;
      }

      const memberSeparator = display.lastIndexOf('.');
      if (memberSeparator > 0) {
        const owner = uidsByDisplayName.get(display.slice(0, memberSeparator));
        if (owner) {
          const memberUid = `${owner}${display.slice(memberSeparator)}`;
          return resolveReference(memberUid, uids, anchorsByUid, new Set()) ?? match;
        }
      }

      return resolveReference(display, uids, anchorsByUid, new Set()) ?? match;
    },
  );

  // Shared SQL source files are compiled into more than one assembly. DocFX can
  // repeat the same member block once per assembly; publish each member once.
  body = dedupeMemberSections(body);

  // 4. DocFX exposes the compiler-only clone member of every record. It is not a
  // useful callable surface and makes hundreds of reference pages look unfinished.
  body = removeCompilerGeneratedClone(body);

  // 5. XML documentation uses HTML block elements. Markdown inside a raw HTML block
  // is not parsed, so links written by step 3 used to appear as literal brackets in
  // the published site. Convert the small, known XML-doc subset into Markdown.
  body = normalizeDocumentationHtml(body);

  // 6. Namespace/Assembly become one quiet, explicitly scoped provenance line.
  // Do this after the XML-to-Markdown pass so its anchor and code markup stay HTML.
  body = body.replace(
    /^Namespace: (.+?)\s*\nAssembly: (.+?)\s*$/m,
    (_match, namespaceText, assemblyText) =>
      `<div class="api-provenance">Namespace ${namespaceLink(namespaceText)} · Assembly <code>${escapeHtml(assemblyText.trim())}</code></div>\n`,
  );

  // 7. The XML files also carry development-history pointers that are useful in the
  // repository but meaningless to a NuGet consumer. Keep the reasoning, remove the
  // phase/decision/file identifiers from the public site.
  body = sanitizeInternalHistory(body);
  body = normalizeSharedProviderDocumentation(page.uid, body);

  // 8. DocFX opens a type page with `#### Inheritance` and a namespace page with
  // `### Classes`, both directly under the `#` removed in step 1. Against the h1
  // Starlight renders, those skip one or two levels. The two cases are not the same
  // kind of thing, so they are not repaired the same way: the `####` run is type
  // metadata and becomes a name/value list, while a namespace page's `###` really is
  // a section and is promoted. Last on purpose — step 5 rewrites `<a>` back to
  // markdown, which would undo the anchors written here.
  body = renderTypeRelations(body);
  body = promoteLeadingHeadings(body);

  const frontmatter = [
    '---',
    `title: ${quote(page.name)}`,
    `description: ${quote(`${page.name}: ${sanitizeInternalHistory(page.summary)}`)}`,
    `slug: api/${page.uid.toLowerCase()}`,
    'editUrl: false',
    'lastUpdated: false',
    '---',
    '',
  ].join('\n');

  return { body: frontmatter + body.trimStart(), unresolvedCount };
}

/** DocFX percent-encodes uids that carry a signature. */
/**
 * Splits a uid from the display text DocFX folded into it.
 *
 * `<see cref="X">text</see>` arrives as `X?text=the%20words`; a plain
 * `<see cref="X"/>` arrives as `X` and yields a null text.
 *
 * @param {string} uid the decoded href
 * @returns {{uid: string, text: string | null}} the uid and its label
 */
function splitReferenceText(uid) {
  const separator = uid.indexOf('?text=');

  if (separator < 0) {
    return { uid, text: null };
  }

  const raw = uid.slice(separator + '?text='.length);

  let text;

  try {
    text = decodeURIComponent(raw.replaceAll('+', ' '));
  } catch {
    text = raw;
  }

  return { uid: uid.slice(0, separator), text: text.length > 0 ? text : null };
}

/**
 * Replaces a markdown link's label, keeping its target.
 *
 * @param {string} link a `[label](target)` string
 * @param {string} text the label to use instead
 * @returns {string} the relabelled link
 */
function relabel(link, text) {
  const match = /^\[(?:[^\]]*)\]\((.*)\)$/s.exec(link);

  return match === null ? link : `[${text}](${match[1]})`;
}

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
function resolveReference(uid, uids, anchorsByUid, externalSlugs) {
  const bare = uid.replace(/\(.*$/, '');

  if (uids.has(bare)) {
    // A type reads as its own name. Using the member form here would render the
    // namespace as if it were a declaring type: `Tracon.IRunStore`.
    return `[${shortName(uid)}](${apiBase}/${bare.toLowerCase()}/)`;
  }

  const segments = bare.split('.');

  for (let index = segments.length - 1; index > 0; index -= 1) {
    const owner = segments.slice(0, index).join('.');

    if (uids.has(owner)) {
      const desired = bare.replaceAll('.', '_').replaceAll('`', '-');
      const anchors = anchorsByUid.get(owner) ?? new Set();
      const exact = anchors.has(desired) ? desired : null;
      const overloads = [...anchors].filter((anchor) => anchor.startsWith(`${desired}_`));
      const anchor = exact ?? (overloads.length === 1 ? overloads[0] : null);
      const fragment = anchor ? `#${anchor}` : '';
      return `[${displayName(uid)}](${apiBase}/${owner.toLowerCase()}/${fragment})`;
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

function buildDisplayNameIndex(pages) {
  const candidates = new Map();

  for (const page of pages) {
    const values = [page.name, shortName(page.uid)];
    for (const value of values) {
      candidates.set(value, [...(candidates.get(value) ?? []), page.uid]);
    }
  }

  return new Map(
    [...candidates.entries()]
      .filter(([, values]) => new Set(values).size === 1)
      .map(([name, values]) => [name, values[0]]),
  );
}

/** The last segment: `Tracon.IRunStore` reads as `IRunStore`. */
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
    .map(
      ([assembly, items]) =>
        `| [\`${assembly}\`](${apiBase}/package-${slugify(assembly)}/) | ${items.length} |`,
    )
    .join('\n');

  const types = pages.filter((page) => page.kind !== 'Namespace').length;

  return `---
title: API reference
description: Every public type in the Tracon packages, generated from the compiled assemblies and their XML documentation.
slug: api
tableOfContents: false
editUrl: false
lastUpdated: false
---

${types} public types across the ${byAssembly.size} packages that ship a library API of
their own. These pages are generated from the compiled assemblies and the XML
documentation that ships inside each
\`.nupkg\`. The generator removes compiler-only members and repository history,
then verifies every internal link for a reference that reads cleanly on the web.

Use the sidebar to browse by package, or the search box — the reference is indexed
along with the rest of the site.

| Package | Public types |
|---|---|
${rows}

\`Tracon\` and \`Tracon.Templates\` are absent on purpose: the first is a meta
package that only carries references, and the second ships a \`dotnet new\` template
rather than an API.

## Where to start

- [\`IAgentCatalog\`](${apiBase}/tracon.iagentcatalog/) — resolving an agent, the entry point to a run
- [\`IRunStore\`](${apiBase}/tracon.irunstore/) — where every run is recorded
- [\`ITraconBuilder\`](${apiBase}/tracon.itraconbuilder/) — the registration chain
- [\`AgentDefinition\`](${apiBase}/tracon.agentdefinition/) — what an agent is, as data
`;
}

function groupByAssembly(pages) {
  const groups = new Map();

  for (const page of pages) {
    if (page.kind === 'Namespace') {
      continue;
    }

    const assemblies = page.assemblies.length > 0 ? page.assemblies : ['Tracon'];
    for (const assembly of assemblies) {
      groups.set(assembly, [...(groups.get(assembly) ?? []), page]);
    }
  }

  return groups;
}

function buildPackageIndex(assembly, items) {
  const rows = [...items]
    .sort((left, right) => left.name.localeCompare(right.name))
    .map(
      (page) =>
        `| [\`${page.name}\`](${apiBase}/${page.uid.toLowerCase()}/) | ${page.kind} | ${escapeTable(sanitizeInternalHistory(page.summary))} |`,
    )
    .join('\n');

  return `---
title: ${quote(assembly)}
description: ${quote(`Public types shipped by ${assembly}, generated from the compiled assembly and its XML documentation.`)}
slug: api/package-${slugify(assembly)}
tableOfContents: false
editUrl: false
lastUpdated: false
---

${items.length} public types. Select a type for signatures, defaults, remarks,
exceptions, and links to related contracts.

| Type | Kind | Purpose |
|---|---|---|
${rows}
`;
}

/** Keep the global navigation small; each package page is the type browser. */
function buildSidebar(pages) {
  const byAssembly = groupByAssembly(pages);

  return [...byAssembly.entries()]
    .sort((left, right) => right[1].length - left[1].length)
    .map(([assembly]) => ({
      label: assembly,
      link: `${sidebarBase}/package-${slugify(assembly)}/`,
    }));
}

/**
 * Turns the `####` run that opens a type page into a name/value list.
 *
 * `Inheritance`, `Implements` and `Inherited Members` are facts about the type, not
 * sections of prose: the same handful of labels repeats across 600+ pages, and as
 * headings they both skipped two levels and claimed the same weight as `Remarks`.
 * A `<dl>` says name/value, which is what they are. The links inside are kept —
 * they carry the reference's internal graph, so they are rewritten to anchors here
 * rather than dropped.
 */
function renderTypeRelations(body) {
  // `#{1,3}` cannot match a `####` line: the level is followed by a `#`, not a space.
  const metadata = /^####[ \t]+\S.*$/m.exec(body);
  const section = /^#{1,3}[ \t]+\S.*$/m.exec(body);

  if (!metadata || (section && section.index < metadata.index)) {
    return body;
  }

  const rest = body.slice(metadata.index);
  const next = /\n#{1,3}[ \t]+\S/.exec(rest);
  const block = next ? rest.slice(0, next.index + 1) : rest;
  const rows = [];

  // `(?![\s\S])` rather than `$`: under `m` the latter would match at every line end
  // and every row would capture an empty value.
  for (const entry of block.matchAll(/^####[ \t]+(.+?)[ \t]*\n([\s\S]*?)(?=\n####[ \t]|(?![\s\S]))/gm)) {
    const value = markdownInlineToHtml(entry[2].trim());

    if (value) {
      rows.push(`<dt>${escapeHtml(entry[1].trim())}</dt><dd>${value}</dd>`);
    }
  }

  if (rows.length === 0) {
    return body;
  }

  // The blank line is load-bearing: an HTML block runs to the next one, so without it
  // the `## Constructors` that follows is swallowed as text instead of parsed.
  const list = `<dl class="api-relations">${rows.join('')}</dl>\n\n`;
  return body.slice(0, metadata.index) + list + body.slice(metadata.index + block.length);
}

/**
 * Renders the inline markdown DocFX writes in those rows as HTML.
 *
 * Deliberately narrow: links and inline code are the whole vocabulary of a relation
 * row. Everything outside a link is escaped, so a generic argument like
 * `IEquatable<AgentDefinition>` cannot open a tag.
 */
function markdownInlineToHtml(value) {
  const unescape = (text) => text.replace(/\\([\\`*_{}[\]()<>#+\-.!|])/g, '$1');
  const inline = (text) => escapeHtml(unescape(text)).replace(/`([^`]+)`/g, '<code>$1</code>');
  let html = '';
  let index = 0;

  // DocFX escapes the parentheses of an overload inside the URL too
  // (`…equals\(system-object\)`), so the href arm has to step over `\)` as well.
  for (const link of value.matchAll(/\[((?:[^\]\\]|\\.)*)\]\(((?:[^)\s\\]|\\.)+)\)/g)) {
    html += inline(value.slice(index, link.index));
    html += `<a href="${escapeHtml(unescape(link[2]))}">${inline(link[1])}</a>`;
    index = link.index + link[0].length;
  }

  return (html + inline(value.slice(index))).replace(/\s*\n\s*/g, ' ').trim();
}

/**
 * Lifts every heading above a page's first `##` up to `##`.
 *
 * Only that leading run moves. From the first `##` onwards DocFX already nests
 * `##` → `###` → `####` correctly — measured across all 765 generated pages, zero
 * skips occur below it — so promoting further would flatten a correct tree instead
 * of repairing a broken one. Fenced blocks are skipped: a `#` there is code.
 */
function promoteLeadingHeadings(body) {
  const lines = body.split('\n');
  let fenced = false;

  for (let index = 0; index < lines.length; index += 1) {
    if (/^\s*(?:```|~~~)/.test(lines[index])) {
      fenced = !fenced;
      continue;
    }

    if (fenced) {
      continue;
    }

    const heading = /^(#{2,6})(\s+.*)$/.exec(lines[index]);

    if (!heading) {
      continue;
    }

    if (heading[1] === '##') {
      break;
    }

    lines[index] = `##${heading[2]}`;
  }

  return lines.join('\n');
}

function removeCompilerGeneratedClone(body) {
  const lines = body.split('\n');
  const output = [];
  let skipping = false;

  for (const line of lines) {
    if (/^### .*<Clone\\?>\$\\?\(\\?\)/.test(line)) {
      skipping = true;
      continue;
    }

    if (skipping && /^#{2,3} /.test(line)) {
      skipping = false;
    }

    if (!skipping) {
      output.push(line);
    }
  }

  return output.join('\n');
}

function dedupeMemberSections(body) {
  const lines = body.split('\n');
  const output = [];
  const seen = new Set();
  let skipping = false;

  for (const line of lines) {
    const member = /^### <a id="([^"]+)"><\/a>/.exec(line);
    if (member) {
      skipping = seen.has(member[1]);
      seen.add(member[1]);
      if (skipping) {
        continue;
      }
    } else if (skipping && /^#{2,3} /.test(line)) {
      skipping = false;
    }

    if (!skipping) {
      output.push(line);
    }
  }

  return output.join('\n');
}

function normalizeDocumentationHtml(body) {
  let normalized = body;

  normalized = normalized.replace(
    /<example>\s*<pre><code(?: class="lang-([^"]+)")?>([\s\S]*?)<\/code><\/pre>\s*<\/example>/g,
    (_match, language, code) => `\n\n\`\`\`${language ?? 'text'}\n${decodeHtml(code).trim()}\n\`\`\`\n\n`,
  );
  normalized = normalized.replace(
    /<pre><code(?: class="lang-([^"]+)")?>([\s\S]*?)<\/code><\/pre>/g,
    (_match, language, code) => `\n\n\`\`\`${language ?? 'text'}\n${decodeHtml(code).trim()}\n\`\`\`\n\n`,
  );

  normalized = normalized.replace(/<table><tbody>([\s\S]*?)<\/tbody><\/table>/g, (_match, rows) => {
    const entries = [...rows.matchAll(/<tr><td[^>]*>([\s\S]*?)<\/td><td[^>]*>([\s\S]*?)<\/td><\/tr>/g)];
    if (entries.length === 0) {
      return '';
    }

    const markdownRows = entries
      .map((entry) => `| ${inlineMarkdown(entry[1])} | ${inlineMarkdown(entry[2])} |`)
      .join('\n');
    return `\n\n| Value | Fields and meaning |\n|---|---|\n${markdownRows}\n\n`;
  });

  normalized = normalized.replace(/<a href="([^"]+)">([\s\S]*?)<\/a>/g, '[$2]($1)');
  normalized = normalized.replace(/<code(?: class="[^"]+")?>([\s\S]*?)<\/code>/g, (_match, code) => {
    const text = decodeHtml(code).trim();
    const fence = text.includes('`') ? '``' : '`';
    return `${fence}${text}${fence}`;
  });
  normalized = normalized
    .replace(/<strong>([\s\S]*?)<\/strong>/g, '**$1**')
    .replace(/<em>([\s\S]*?)<\/em>/g, '*$1*')
    .replace(/<li>([\s\S]*?)<\/li>/g, (_match, item) => `\n- ${inlineMarkdown(item)}`)
    .replace(/<\/?(?:ol|ul)>/g, '\n')
    .replace(/<br\s*\/?>/g, '\n')
    .replace(/<\/?p>/g, '\n')
    .replace(/<\/?example>/g, '\n')
    .replace(/\n{3,}/g, '\n\n');

  return normalized;
}

function inlineMarkdown(value) {
  return value
    .replace(/\s+/g, ' ')
    .replace(/<strong>(.*?)<\/strong>/g, '**$1**')
    .replace(/<em>(.*?)<\/em>/g, '*$1*')
    .replace(/<code(?: class="[^"]+")?>(.*?)<\/code>/g, '`$1`')
    .replace(/<a href="([^"]+)">(.*?)<\/a>/g, '[$2]($1)')
    .replaceAll('|', '\\|')
    .trim();
}

// The source documentation is kept self-contained by
// ShippedDocumentationSelfContainmentTests, so nothing here strips journal prose any
// more. A reference that reaches this point is a defect in src/, and repairing it
// here is what used to leave half-sentences behind. What remains turns docfx output
// into readable Markdown.
function sanitizeInternalHistory(body) {
  return body.split(/(```[\s\S]*?```)/g).map((part, index) => {
    if (index % 2 === 1) {
      return part;
    }

    const marker = internalHistoryMarker(part);

    if (marker) {
      throw new Error(
        `Internal development history reached the API reference: "${marker.text}" in ` +
          `"...${part.slice(Math.max(0, marker.index - 60), marker.index + 60).replace(/\n/g, ' ')}...". ` +
          'Fix the XML documentation in src/ rather than filtering it here.',
      );
    }

    return part
      .replace(/\bRationale:\s*/gi, '')
      .replace(/\brationale\b/gi, 'reason')
      .replace(/🚨|⚠️/gu, '**Important:**')
      .replace(/^## Remarks's cost model/gm, "## Remarks\n\nTracon's cost model")
      .replace(/^(#{2,4} Remarks)[.:]\s*(.+)$/gm, '$1\n\n$2')
      .replace(/\*\*(?:NO|THE SAME)\*\*/g, (value) => `**${value.slice(2, -2).toLowerCase()}**`)
      .replace(/\b(?:NOT SILENTLY OVERWRITE|NOT SUPPORTED|NEVER CHANGES AGAIN)\b/g, (value) => value.toLowerCase())
      .replace(/\b(?:NEW|EMPTY|FIRST|EVERY|OWN|ALREADY|GENUINELY|DELIBERATELY|WHICH|THEIR|ITS|SEPARATE|SUMMED|SCORE|REGISTRATION-TIME|NOT|NO|OLDEST|AS IS|RAW)\b/g, (value) => value.toLowerCase())
      // Not before an ellipsis: "POST .../trigger" is a path, not a sentence end.
      .replace(/(\S)\s+([,.;:])(?!\.)/g, '$1$2')
      .replace(/\(\s*\)/g, '');
  }).join('');
}



function normalizeSharedProviderDocumentation(uid, body) {
  if (uid !== 'Tracon.MigrationRunner') {
    return body;
  }

  const remarks = `## Remarks

The runner uses one coordination scope per configured database namespace:

| Provider | Namespace | Migration lock |
|---|---|---|
| PostgreSQL | Schema | \`pg_advisory_lock\` on one connection |
| SQL Server | Schema | \`sp_getapplock\` on one connection |
| SQLite | Table prefix | Sidecar file lock next to the database |

It creates the namespace and \`__migrations\` ledger when needed, verifies the
SHA-256 checksum of every applied file, and applies each pending migration in its
own transaction. A checksum mismatch fails instead of running against an unknown
schema state.

Some migrations create database-wide objects. PostgreSQL's pgvector extension is
one example. Concurrent first-time migration of different schemas can race on that
shared object; the guarded operation is retried safely.
`;

  return body.replace(/## Remarks[\s\S]*?(?=\n## Properties)/, `${remarks.trimEnd()}\n`);
}

function extractSummary(raw, fallback, uids) {
  const withoutHeader = raw
    .replace(/^# .*$/m, '')
    .replace(/^Namespace:.*$/m, '')
    .replace(/^Assembly:.*$/m, '')
    .trimStart();
  const paragraph = withoutHeader.split(/\n\s*\n/).find((part) => !part.startsWith('```'));
  const text = plainText(paragraph ?? fallback, uids);
  if (text.length <= 165) {
    return text || fallback;
  }

  const shortened = text.slice(0, 162).replace(/\s+\S*$/, '');
  return `${shortened}.`;
}

// `uids` is optional: a caller with no type set falls back to the member form,
// which is what every reference was rendered as before. When the set is supplied,
// a reference to a TYPE reads as its own name — the same rule `resolveReference`
// applies to a link. Without it the namespace renders as if it were a declaring
// type and the surrounding sentence breaks with it: the XML says "an
// <see cref="IRunJudge"/>", correct for the name a reader sees, and the member
// form turned that into "an Tracon.IRunJudge" on 60 generated pages.
function plainText(value, uids) {
  return decodeHtml(value)
    .replace(/<xref href="([^"]+)"[^>]*><\/xref>/g, (_match, uid) =>
      uids?.has(uid.replace(/\(.*$/, '')) ? shortName(uid) : displayName(uid))
    .replace(/<[^>]+>/g, '')
    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
    .replace(/[`*_\\]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

function decodeHtml(value) {
  return value
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&quot;', '"')
    .replaceAll('&#39;', "'")
    .replaceAll('&amp;', '&');
}

function slugify(value) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

function escapeTable(value) {
  return value.replaceAll('|', '\\|').replace(/\s+/g, ' ').trim();
}

function escapeHtml(value) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function namespaceLink(value) {
  const match = /^\[([^\]]+)]\((\/Tracon\/api\/[^)#]+\/)\)$/.exec(value.trim());

  if (!match) {
    return escapeHtml(value.replace(/^\[([^\]]+)]\([^)]+\)$/, '$1'));
  }

  return `<a href="${escapeHtml(match[2])}">${escapeHtml(match[1])}</a>`;
}

function quote(text) {
  return `"${text.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}"`;
}
