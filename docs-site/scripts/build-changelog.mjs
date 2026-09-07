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

import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(here, '../..');
const changelogPath = join(repositoryRoot, 'CHANGELOG.md');
const outputPath = join(here, '../src/content/docs/reference/changelog.md');

main();

function main() {
  if (!existsSync(changelogPath)) {
    throw new Error(`${changelogPath} not found. The release notes page has no source.`);
  }

  const releases = parseReleases(readFileSync(changelogPath, 'utf8'));

  mkdirSync(dirname(outputPath), { recursive: true });

  // No released version is a legitimate state, not a broken changelog: nothing
  // has been pushed to NuGet or npm yet. This used to throw, which is why the
  // changelog kept a version section for a release that never happened - the
  // build demanded one. The page now says so plainly instead of naming a
  // version a reader cannot install.
  writeFileSync(outputPath, releases.length === 0 ? renderUnreleased() : render(releases));
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
      // The body's own `### Added` headings drop one level: the version becomes
      // an `###` under `## Releases`, and `## Read next` stays the last `##` the
      // closing-section gate looks for.
      body: section
        .slice(heading.length)
        .replace(/^### /gm, '#### ')
        .trim(),
    });
  }

  return releases;
}

function renderUnreleased() {
  return `---
title: Release notes
description: AgentPrism has not been released yet; this page will carry the entry for each published version.
---

**AgentPrism has not been released yet.** No version has been published to
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

function render(releases) {
  const entries = releases
    .map(
      (release) =>
        `<a id="v${release.version}"></a>\n\n` +
        `### ${release.version} — ${release.date}\n\n${release.body}\n`,
    )
    .join('\n');

  return `---
title: Release notes
description: What changed in every published AgentPrism version, with the entry each package's release-notes link points to.
---

Every AgentPrism package is cut from one version line, so a release below covers
all 20 NuGet packages and the npm client at once. Each package's release-notes
metadata links straight to the entry for the version you installed.

Read [Versions and upgrades](/reference/versioning/) before you move between
previews: it carries the pinning rules and the upgrade sequence this page's
entries assume.

## Releases

${entries}
## Read next

- [Versions and upgrades](/reference/versioning/) — how to pin the family and upgrade it without drift
- [Compatibility matrices](/reference/compatibility/) — the framework, runtime, and protocol versions each release supports
- [Choosing packages](/packages/) — which packages you actually take a version of
`;
}
