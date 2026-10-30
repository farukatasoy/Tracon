// Turns the repository's root CHANGELOG.md into the site's release-notes page.
//
// Why generated and not written twice:
//
//   Every package's `PackageReleaseNotes` metadata has to resolve to something a
//   consumer can open. The repository is private, so a GitHub blob URL 404s for
//   everyone outside it, and this site is the only public surface that can carry
//   the notes. Keeping a second, hand-maintained copy of the changelog here would
//   drift from the root file the release gate actually reads - so the root file
//   stays the single source and this script publishes it.
//
//   Each version gets an explicit `v<version>` anchor because the package metadata
//   links straight to one release: the auto-generated heading slug would drop the
//   dots from `1.0.0-preview.1` and change again whenever the date changes.
//
// Why one page per release:
//
//   The single page grew with every release and crossed its weight ceiling at the
//   1.0.0-preview.6 cut. A reader lands on one version's anchor, so the notes are
//   split: `/reference/changelog/` is the index and keeps every `v<version>` anchor
//   with the date, a summary and a link, and each release body lives on its own page
//   under `/reference/releases/<version>/`. An anchor that package metadata or a
//   GitHub release body already links to still lands on that version's entry.
//   The release pages stay out of the sidebar: the index is the way in.

import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { site } from '../site.config.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(here, '../..');
const changelogPath = join(repositoryRoot, 'CHANGELOG.md');
const outputPath = join(here, '../src/content/docs/reference/changelog.md');
const releasesDirectory = join(here, '../src/content/docs/reference/releases');

main();

function main() {
  if (!existsSync(changelogPath)) {
    throw new Error(`${changelogPath} not found. The release notes page has no source.`);
  }

  const releases = parseReleases(readFileSync(changelogPath, 'utf8'));

  mkdirSync(dirname(outputPath), { recursive: true });

  // A release removed from CHANGELOG.md must not keep a page of its own.
  rmSync(releasesDirectory, { recursive: true, force: true });

  // No released version is a legitimate state, not a broken changelog: nothing
  // has been pushed to NuGet or npm yet. This used to throw, which is why the
  // changelog kept a version section for a release that never happened - the
  // build demanded one. The page now says so plainly instead of naming a
  // version a reader cannot install.
  if (releases.length === 0) {
    writeFileSync(outputPath, renderUnreleased());
    return;
  }

  mkdirSync(releasesDirectory, { recursive: true });

  for (const release of releases) {
    writeFileSync(join(releasesDirectory, `${release.version}.md`), renderRelease(release));
  }

  writeFileSync(outputPath, renderIndex(releases));
}

/**
 * Reads the `## [version] - date` sections. `[Unreleased]` is deliberately
 * dropped: this page describes what you can install, and an unreleased heading
 * on a public page reads as a promise.
 */
function parseReleases(text) {
  const withoutLinkDefinitions = text
    .split('\n')
    .filter((line) => !/^\[[^\]]+\]:\s*https?:/.test(line))
    .join('\n');

  const sections = withoutLinkDefinitions.split(/^## /m).slice(1);
  const releases = [];

  for (const section of sections) {
    const heading = section.slice(0, section.indexOf('\n'));
    const match = /^\[(?<version>[^\]]+)\](?:\s*-\s*(?<date>\d{4}-\d{2}-\d{2}))?$/.exec(
      heading.trim(),
    );

    if (!match || match.groups.version === 'Unreleased') {
      continue;
    }

    if (!match.groups.date) {
      throw new Error(
        `CHANGELOG.md: version ${match.groups.version} carries no release date. ` +
          'A published entry states the day it shipped.',
      );
    }

    releases.push({
      version: match.groups.version,
      date: match.groups.date,
      // The body's own `### Added` headings rise one level: on the release page
      // they are the `##` sections, and `## Read next` stays the last `##` the
      // closing-section gate looks for.
      body: onThisSite(
        section
          .slice(heading.length)
          .replace(/^#### /gm, '### ')
          .replace(/^### /gm, '## ')
          .trim(),
      ),
    });
  }

  return releases;
}

/**
 * Turns this site's own absolute links into site-relative ones.
 *
 * The root CHANGELOG.md is read in three places and only one of them is this
 * site: the GitHub release body and a package's release-notes link both live
 * off-site, where `/reference/licensing/` would resolve against the wrong
 * host. So the root file writes the address in full, and the page drops it on
 * the way in - which is also what `check-content.mjs` enforces, since a page
 * under src/ that spells the host out survives a domain move as a broken link.
 */
function onThisSite(body) {
  // An autolink is handled first and separately: `<https://host/x/>` cannot
  // just lose its host, because `</x/>` is not an autolink any more - Markdown
  // wants an absolute URI inside the angle brackets and a reader would be left
  // with literal text or a stray tag.
  const autolink = new RegExp(`<${site.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}([^>]*)>`, 'g');

  return body.replace(autolink, (_, path) => `[${path}](${path})`).split(site).join('');
}

function renderUnreleased() {
  return `---
title: Release notes
description: Tracon has not been released yet; this page will carry the entry for each published version.
---

**Tracon has not been released yet.** No version has been published to
NuGet or npm, and there is no release tag, so there is nothing here to install
or pin. Development happens on \`main\`.

The repository's \`CHANGELOG.md\` tracks what has landed there. When the first
version ships, this page will carry its entry, and each package's release-notes
metadata will link straight to it.

## Read next

- [Versions and upgrades](/reference/versioning/) — the pinning and upgrade rules the first release will follow
- [Compatibility matrices](/reference/compatibility/) — the framework, runtime, and protocol versions supported
- [Choosing packages](/packages/) — which packages you would take a version of
`;
}

/**
 * What the index says about a release: its opening paragraph when the entry has one, otherwise
 * the sections it carries and how many entries each holds.
 */
function summarize(release) {
  const lead = release.body.split(/^## /m)[0].trim();

  if (lead) {
    return lead;
  }

  const sections = release.body
    .split(/^## /m)
    .slice(1)
    .map((section) => {
      const name = section.slice(0, section.indexOf('\n')).trim();
      const entries = (section.match(/^- /gm) ?? []).length;

      return `${name} (${entries})`;
    });

  return `This release carries ${sections.join(', ')}.`;
}

function renderRelease(release) {
  return `---
title: Tracon ${release.version} release notes
description: What changed in Tracon ${release.version}, released on ${release.date}, grouped as the changelog groups it.
slug: reference/releases/${release.version}
---

Released on ${release.date}. This entry covers every NuGet package of the family and the npm client
at version ${release.version}. [Release notes](/reference/changelog/) lists every version.

${release.body}

## Read next

- [Release notes](/reference/changelog/) — every published version, newest first
- [Versions and upgrades](/reference/versioning/) — how to pin the family and upgrade it without drift
`;
}

function renderIndex(releases) {
  const entries = releases
    .map(
      (release) =>
        `<a id="v${release.version}"></a>\n\n` +
        `### ${release.version} — ${release.date}\n\n${summarize(release)}\n\n` +
        `[Read the ${release.version} entry](/reference/releases/${release.version}/)\n`,
    )
    .join('\n');

  return `---
title: Release notes
description: What changed in every published Tracon version, with the entry each package's release-notes link points to.
---

Every Tracon package is cut from one version line, so a release below covers
every NuGet package of the family and the npm client at once. Each package's release-notes
metadata links straight to the entry for the version you installed; the entry itself is
the page this list links to.

Read [Versions and upgrades](/reference/versioning/) before you move between
previews: it carries the pinning rules and the upgrade sequence these entries
assume.

## Releases

${entries}
## Read next

- [Versions and upgrades](/reference/versioning/) — how to pin the family and upgrade it without drift
- [Compatibility matrices](/reference/compatibility/) — the framework, runtime, and protocol versions each release supports
- [Choosing packages](/packages/) — which packages you actually take a version of
`;
}
