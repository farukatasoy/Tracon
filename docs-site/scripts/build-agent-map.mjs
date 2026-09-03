// Generates the capability map that a coding agent reads.
//
// One hand-written source (src/content/docs/capabilities.md) produces three
// artifacts, so the map can never disagree with the documented capability set:
//
//   src/AgentPrism.Core/buildTransitive/AgentPrism.AgentMap.md  shipped in the nupkg
//   docs-site/public/llms.txt                                   same map + a page index
//   docs-site/public/llms-full.txt                              every hand-written page
//
// They are three sizes for three questions: which capability exists, which page
// explains it, and what that page says. The first two are budgeted — one is read
// at the start of every session from disk, the other is fetched over the network
// on purpose — and each has its own ceiling below. The generated API and HTTP
// references are deliberately absent from all three: the compiler and XML
// documentation already cover that surface, and ~1.7M tokens would burn the
// budget for nothing.
//
// Run:    node scripts/build-agent-map.mjs
// Verify: node scripts/build-agent-map.mjs --check   (also run by check-content.mjs)

import { createHash } from 'node:crypto';
import { readFileSync, readdirSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { basename, dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { siteUrl } from '../site.config.mjs';

const here = resolve(fileURLToPath(new URL('.', import.meta.url)));
const siteRoot = resolve(here, '..');
const docsRoot = join(siteRoot, 'src/content/docs');
const repositoryRoot = resolve(siteRoot, '..');

/**
 * The agent map is read at the start of every session in a consumer repository,
 * so it is budgeted rather than left to grow. 10 KiB is roughly 2500 tokens:
 * cheap enough to always read, and far from the ~1.7M tokens of the generated
 * references. The complete map measures ~8.4 KB today, so the budget leaves
 * room for roughly twenty more capabilities. Overflow is not truncated —
 * generation fails, and the maintainer decides what moves to llms-full.txt.
 */
export const agentMapBudgetBytes = 10240;

/**
 * llms.txt is the same map plus one line per hand-written page, and it is
 * budgeted separately because it is read differently: an agent fetches it over
 * the network, deliberately, when it wants to know which page answers its
 * question — while the shipped map is read at the start of every session from
 * disk. The page index measures ~8.0 KB on its own, so the two cannot share one
 * ceiling without either starving the map or lifting its limit.
 *
 * Raised from 20 KiB once, measured: 51 pages reached 20666 bytes and the
 * release-notes page - which every published package's metadata links to, so it
 * cannot be dropped - did not fit in the last 3 bytes. 24 KiB is ~6000 tokens
 * for an index an agent fetches on purpose, and it leaves room again. Trimming
 * unrelated pages' descriptions to fund one new page was the alternative; those
 * descriptions are also the site's search snippets, so shrinking them to make
 * an accounting figure work would have paid for the page with the reader's
 * text.
 */
export const llmsBudgetBytes = 24576;

/** The byte ceiling of each budgeted artifact. */
export const budgets = { agentMap: agentMapBudgetBytes, llms: llmsBudgetBytes };

export const outputs = {
  agentMap: join(repositoryRoot, 'src/AgentPrism.Core/buildTransitive/AgentPrism.AgentMap.md'),
  llms: join(siteRoot, 'public/llms.txt'),
  llmsFull: join(siteRoot, 'public/llms-full.txt'),
};

/** Hand-written pages, in reading order. The generated references are excluded. */
const fullTextOrder = ['getting-started', 'concepts', 'guides', 'reference'];
const fullTextRootPages = ['capabilities.md', 'packages.md', 'troubleshooting.md', 'ui.md', 'http-api.md'];

/** Builds all three artifacts. Returns their exact bytes, keyed like {@link outputs}. */
export function build() {
  const capabilities = parseCapabilities();
  const packages = readPackages();
  const pages = handWrittenPages().map(readPage);
  const llmsFull = renderFullText(pages);

  // The map warns how large the full text is, so the size is measured rather
  // than typed: a number written by hand goes stale silently, and this one only
  // exists to stop an agent from fetching 400 KB it does not need. It is rounded
  // hard on purpose - the revision below is a hash of this text, and a figure
  // that moved with every prose edit would report every consumer's map as stale.
  const body = renderMap(capabilities, packages, roundedKilobytes(llmsFull));
  const revision = createHash('sha256').update(body).digest('hex').slice(0, 8);

  const agentMap = `${marker(revision)}\n${body}`;

  // The site copy is the same map plus the page index: one revision, one set of
  // facts, and the one thing only a networked reader can act on - a link per
  // page. The map itself names both site artifacts, so a reader who only has the
  // shipped copy learns they exist too.
  const llms = `${agentMap}${renderIndex(pages)}`;

  return { agentMap, llms, llmsFull };
}

/** The size of an artifact, coarse enough to stay put across ordinary edits. */
function roundedKilobytes(text) {
  return Math.round(Buffer.byteLength(text) / 100000) * 100;
}

/** The single line APG0401 reads. Both artifacts carry the same revision. */
export function marker(revision) {
  return `<!-- AgentPrism agent map · revision: ${revision} · generated by docs-site/scripts/build-agent-map.mjs -->`;
}

/** Reads the revision out of a generated file, or null when the file is not one. */
export function readRevision(text) {
  return /^<!-- AgentPrism agent map · revision: ([0-9a-f]{8}) ·/m.exec(text)?.[1] ?? null;
}

function parseCapabilities() {
  const path = join(docsRoot, 'capabilities.md');
  const text = readFileSync(path, 'utf8');
  const body = text.replace(/^---\n[\s\S]*?\n---\n/, '');

  if (body.trim().length === 0) {
    throw new Error(`${path} is empty. The agent map cannot be generated from it.`);
  }

  const lead = firstSentence(body.split('\n\n').find((block) => block.trim() && !block.startsWith('#')) ?? '');
  const sections = [];
  let current = null;

  for (const line of body.split('\n')) {
    const heading = /^## (.+)$/.exec(line);

    if (heading) {
      current = { title: heading[1].trim(), rows: [], rule: null, prose: [] };
      sections.push(current);
      continue;
    }

    if (!current) continue;

    if (line.startsWith('|')) {
      const cells = splitRow(line);

      if (cells === null) continue;

      if (current.columns === undefined) {
        current.columns = resolveColumns(cells);
        continue;
      }

      const row = renderRow(current.columns, cells);
      if (row) current.rows.push(row);
      continue;
    }

    if (line.trim() && !line.startsWith('```') && !line.startsWith('#')) {
      current.prose.push(line.trim());
    }
  }

  for (const section of sections) {
    // The paragraph that follows a capability table states that section's rule;
    // its first sentence is the part an agent must not violate. A section with a
    // table but no such paragraph would ship a rule-less block, so the generator
    // refuses rather than producing a map that is quietly incomplete.
    section.rule = section.rows.length > 0 ? firstSentence(section.prose.join(' ')) : null;

    if (section.rows.length > 0 && !section.rule) {
      throw new Error(
        `capabilities.md: section '${section.title}' has a capability table but no rule paragraph. ` +
          'Add one sentence stating the rule an agent must not violate.',
      );
    }
  }

  if (sections.every((section) => section.rows.length === 0)) {
    throw new Error(`${path} has no capability table. The agent map would be empty.`);
  }

  return { lead, sections: sections.filter((section) => section.rows.length > 0) };
}

/** Splits a table line into cells; returns null for the separator line. */
function splitRow(line) {
  const cells = line.replace(/^\|/, '').replace(/\|$/, '').split('|').map((cell) => cell.trim());

  return cells.every((cell) => /^:?-{2,}:?$/.test(cell)) ? null : cells;
}

/**
 * Picks the columns to keep, from the header row. Column shapes differ per
 * section, so the choice is made by header name rather than by position: the
 * "Enable or define it" column sits at index 2 in one table and index 1 in the
 * rest, and reading the wrong one produced entries like "Model binding: top_p".
 */
function resolveColumns(header) {
  const entry = header.findIndex(
    (title, index) => index > 0 && /registration|enable|surface|definition|choice|where|output/i.test(title),
  );

  // The provider table is the one keyed by package rather than by capability.
  // Its useful line is the registration call, qualified by package and by the
  // provider names an AgentDefinition can bind to.
  if (/^`?Package`?$/.test(header[0])) {
    return {
      kind: 'provider',
      entry: entry > 0 ? entry : 1,
      names: header.findIndex((title) => /provider names/i.test(title)),
    };
  }

  return { kind: 'capability', entry: entry > 0 ? entry : 1 };
}

function renderRow(columns, cells) {
  const name = plain(cells[0]);

  if (!name) return null;

  const entryCell = cells[columns.entry] ?? '';
  const entry = codesOf(entryCell).slice(0, 2).join(', ') || shorten(plain(entryCell), 52);

  if (columns.kind === 'provider') {
    // "Chosen by the implementation" is prose, not a bindable provider name;
    // only a cell that names actual providers in code spans is carried over.
    const names = codesOf(cells[columns.names] ?? '');

    return { name: entry, entry: names.length > 0 ? `${name}, binds ${names.join(', ')}` : name };
  }

  return { name, entry };
}

function codesOf(cell) {
  return [...cell.matchAll(/`([^`]+)`/g)].map((match) => match[1]);
}

function readPackages() {
  const sourceRoot = join(repositoryRoot, 'src');

  return readdirSync(sourceRoot, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && entry.name.startsWith('AgentPrism'))
    .map((entry) => join(sourceRoot, entry.name, `${entry.name}.csproj`))
    .filter((path) => existsSync(path))
    .map((path) => {
      const project = readFileSync(path, 'utf8');

      // A project that is not packed is not a capability the consumer can add.
      if (/<IsPackable>\s*false\s*<\/IsPackable>/.test(project)) {
        return null;
      }

      const description = /<Description>([\s\S]*?)<\/Description>/.exec(project)?.[1] ?? '';
      const job = shorten(
        firstSentence(description.replace(/\s+/g, ' ').trim())
          .replace(/\.$/, '')
          .replace(/\s+for AgentPrism\b/, '')
          .replace(/^AgentPrism(?:UI)?[ -]*/, ''),
        44,
      );

      return { name: packageName(path), job };
    })
    .filter((entry) => entry !== null)
    .sort((left, right) => left.name.localeCompare(right.name, 'en'));
}

/** Extracts a package ID with the path rules of the current operating system. */
export function packageName(projectPath, basenamePath = basename) {
  return basenamePath(projectPath).replace(/\.csproj$/, '');
}

function renderMap({ lead, sections }, packages, fullTextKilobytes) {
  const lines = [];

  lines.push('# AgentPrism');
  lines.push('');
  lines.push(lead);
  lines.push('');
  lines.push('Read this list before you write agent, run, tool, or evaluation code by hand:');
  lines.push('the capability is very likely already here. AgentPrism uses Microsoft Agent');
  lines.push('Framework types directly, so MAF documentation applies unchanged.');
  lines.push('');
  lines.push('## Wiring');
  lines.push('');
  lines.push('```csharp');
  lines.push('builder.Services.AddAgentPrism();  // required: catalog, runs, in-memory stores');
  lines.push('app.MapAgentPrism();               // optional: management HTTP API and console');
  lines.push('```');
  lines.push('');
  lines.push('Everything else is opt-in and named below.');
  lines.push('');
  lines.push('## Packages');
  lines.push('');

  for (const entry of packages) {
    lines.push(`- ${entry.name}: ${entry.job}`);
  }

  lines.push('');
  lines.push('## Capabilities');

  for (const section of sections) {
    lines.push('');
    lines.push(`### ${section.title}`);
    lines.push('');

    for (const row of section.rows) {
      lines.push(row.name === row.entry ? `- ${row.name}` : `- ${row.name}: ${row.entry}`);
    }

    if (section.rule) {
      lines.push(`- Rule: ${section.rule}`);
    }
  }

  lines.push('');
  lines.push('## Where to look');
  lines.push('');
  lines.push('- Exact local paths for the version you have: AgentPrism.LocalReference.md, beside each project that references AgentPrism');
  lines.push(`- Capability map, with the boundary of each capability: ${siteUrl}capabilities/`);
  lines.push(`- Guides, concepts, and configuration reference: ${siteUrl}`);
  lines.push(`- Which page answers what, one line per page: ${siteUrl}llms.txt`);
  lines.push(
    `- Full text of every hand-written page (about ${fullTextKilobytes} KB - prefer one page above): ${siteUrl}llms-full.txt`,
  );
  lines.push(`- HTTP API reference: ${siteUrl}http-api/`);
  lines.push(`- .NET API reference: ${siteUrl}api/`);
  lines.push('');

  return `${lines.join('\n')}\n`;
}

/**
 * Every hand-written page, in one deterministic order. The index and the full
 * text walk the SAME list on purpose: a page that appears in one and not the
 * other would be a page an agent is told about and cannot read, or can read and
 * is never told about.
 */
function handWrittenPages() {
  const files = [];

  for (const page of fullTextRootPages) {
    const path = join(docsRoot, page);
    if (existsSync(path)) files.push(path);
  }

  for (const directory of fullTextOrder) {
    const path = join(docsRoot, directory);
    if (existsSync(path)) files.push(...collect(path).filter((file) => file.endsWith('.md') || file.endsWith('.mdx')));
  }

  return files.sort((left, right) => left.localeCompare(right, 'en'));
}

/**
 * One page: what it is called, what it answers, and where it is published.
 *
 * A missing title or description FAILS generation rather than producing a
 * shorter index. A silently skipped page is the same defect as an empty map: the
 * artifact still looks complete, and the page it omits is the one nobody finds.
 */
function readPage(file) {
  const text = readFileSync(file, 'utf8');
  const frontmatter = /^---\n([\s\S]*?)\n---\n/.exec(text)?.[1] ?? '';

  const title = field(frontmatter, 'title');
  const description = field(frontmatter, 'description');

  if (!title || !description) {
    throw new Error(
      `${relative(siteRoot, file)}: the page index needs both 'title' and 'description' in the frontmatter; ` +
        `this page has ${title ? 'no description' : description ? 'no title' : 'neither'}. ` +
        'Add the missing field - a page cannot be listed without it.',
    );
  }

  // An explicit slug wins, exactly as Starlight resolves it; without one the
  // route comes from the file path, with index.md standing for its directory.
  const slug =
    field(frontmatter, 'slug') ??
    relative(docsRoot, file)
      .replaceAll('\\', '/')
      .replace(/\.mdx?$/, '')
      .replace(/(^|\/)index$/, '');

  return { title, description, slug, body: text.replace(/^---\n[\s\S]*?\n---\n/, '').trim() };
}

/** One frontmatter field, unquoted, or null when the page does not declare it. */
function field(frontmatter, name) {
  const value = new RegExp(`^${name}:\\s*(.+)$`, 'm').exec(frontmatter)?.[1].trim();

  return value ? value.replace(/^['"]|['"]$/g, '') : null;
}

/**
 * The layer between the map and the full text: the map names a capability, this
 * names the page that explains it, and only then does a whole page get read. It
 * lives in llms.txt alone - the shipped map points at it by address, because a
 * consumer's copy of these links would go stale with the site rather than with
 * the package.
 */
function renderIndex(pages) {
  const lines = [
    '',
    '## Which page answers what',
    '',
    'One line per hand-written page. Read the page that matches your question',
    'rather than the concatenated full text, which is far larger and answers the',
    'same question with everything else attached.',
    '',
  ];

  for (const page of pages) {
    lines.push(`- [${page.title}](${siteUrl}${page.slug}${page.slug ? '/' : ''}) — ${page.description}`);
  }

  return `${lines.join('\n')}\n`;
}

function renderFullText(pages) {
  const parts = [
    '# AgentPrism — full documentation',
    '',
    'Every hand-written page of the AgentPrism documentation, concatenated. The',
    'generated API and HTTP references are not included; use the compiler, the XML',
    `documentation, and ${siteUrl}http-api/ for those.`,
    '',
  ];

  for (const page of pages) {
    parts.push('---', '', `# ${page.title}`, '', page.body, '');
  }

  return `${parts.join('\n')}\n`;
}

function collect(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? collect(path) : [path];
  });
}

/** Strips inline Markdown so a table cell reads as plain prose. */
function plain(cell) {
  return cell
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/[`*]/g, '')
    .trim();
}

function firstSentence(text) {
  const cleaned = plain(text.replace(/\s+/g, ' ').trim());
  const match = /^(.+?[.!?])(?:\s|$)/.exec(cleaned);

  return (match?.[1] ?? cleaned).trim();
}

function shorten(text, limit) {
  if (text.length <= limit) return text;
  // Cut at the last word boundary that fits: 'a non-networked model provide…'
  // reads as a defect, not an abbreviation.
  const clipped = text.slice(0, limit - 1);
  const boundary = clipped.lastIndexOf(' ');
  const kept = (boundary > limit / 2 ? clipped.slice(0, boundary) : clipped).trimEnd();
  return `${kept.replace(/[,;:]$/, '')}…`;
}

const isCheck = process.argv.includes('--check');
const isMain = process.argv[1] && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url));

if (isMain) {
  const built = build();
  const problems = verifyBudget(built);

  if (isCheck) {
    for (const [key, path] of Object.entries(outputs)) {
      const actual = existsSync(path) ? readFileSync(path, 'utf8') : null;

      if (actual !== built[key]) {
        problems.push(`${path} differs from its source. Run: node docs-site/scripts/build-agent-map.mjs`);
      }
    }

    if (problems.length > 0) {
      for (const problem of problems) console.error(`  ${problem}`);
      process.exit(1);
    }

    console.log('Agent map: up to date and within budget.');
  } else {
    if (problems.length > 0) {
      for (const problem of problems) console.error(`  ${problem}`);
      process.exit(1);
    }

    for (const [key, path] of Object.entries(outputs)) {
      mkdirSync(dirname(path), { recursive: true });
      writeFileSync(path, built[key]);
      console.log(`${path}: ${Buffer.byteLength(built[key])} bytes`);
    }
  }
}

/** The budget is enforced at generation time, not only in review. */
export function verifyBudget(built) {
  const problems = [];

  for (const [key, budget] of Object.entries(budgets)) {
    const size = Buffer.byteLength(built[key]);

    if (size > budget) {
      problems.push(`${outputs[key]} is ${size} bytes; the budget is ${budget}.`);
    }
  }

  return problems;
}
