#!/usr/bin/env python3
"""Tests for the CHANGELOG.md Keep a Changelog section parser."""
from __future__ import annotations

import importlib.util
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("changelog", ROOT / "scripts" / "changelog.py")
changelog = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = changelog
spec.loader.exec_module(changelog)


class ChangelogTests(unittest.TestCase):
    def test_a_section_body_is_read(self):
        text = (
            "# Changelog\n\n"
            "## [Unreleased]\n\n"
            "## [1.0.0-preview.1] - 2026-08-28\n\n"
            "### Added\n- first feature\n\n"
            "## [0.9.0] - 2026-08-01\n\n"
            "### Added\n- old feature\n"
        )

        body = changelog.section_body(text, "1.0.0-preview.1")

        self.assertIn("first feature", body)
        self.assertNotIn("old feature", body)

    def test_a_section_at_the_end_of_the_file_is_read(self):
        text = "# Changelog\n\n## [1.0.0-preview.1] - 2026-08-28\n\n### Added\n- only feature\n"

        body = changelog.section_body(text, "1.0.0-preview.1")

        self.assertIn("only feature", body)

    def test_a_missing_version_returns_none(self):
        text = "# Changelog\n\n## [1.0.0-preview.1] - 2026-08-28\n\n### Added\n- feature\n"

        self.assertIsNone(changelog.section_body(text, "1.0.0-preview.2"))

    def test_an_empty_file_has_no_section(self):
        self.assertIsNone(changelog.section_body("", "1.0.0-preview.1"))
        self.assertFalse(changelog.has_section("", "1.0.0-preview.1"))

    def test_has_section_is_false_for_a_heading_with_an_empty_body(self):
        text = "# Changelog\n\n## [1.0.0-preview.1] - 2026-08-28\n\n## [0.9.0] - 2026-08-01\n\n### Added\n- x\n"

        self.assertFalse(changelog.has_section(text, "1.0.0-preview.1"))

    def test_the_first_section_wins_when_a_version_appears_twice(self):
        text = (
            "## [1.0.0-preview.1] - 2026-08-28\n\n### Added\n- first\n\n"
            "## [1.0.0-preview.1] - 2026-08-29\n\n### Added\n- second\n"
        )

        body = changelog.section_body(text, "1.0.0-preview.1")

        self.assertIn("first", body)
        self.assertNotIn("second", body)

    def test_a_heading_without_a_date_matches_too(self):
        text = "## [Unreleased]\n\n### Added\n- ongoing\n"

        self.assertTrue(changelog.has_section(text, "Unreleased"))

    # latest_released_version: a release is the first dated section.

    def test_the_latest_release_is_the_first_dated_section(self):
        text = (
            "# Changelog\n\n"
            "## [Unreleased]\n\n### Added\n- ongoing\n\n"
            "## [1.0.0-preview.2] - 2026-09-01\n\n"
            "## [1.0.0-preview.1] - 2026-08-28\n"
        )

        self.assertEqual(changelog.latest_released_version(text), "1.0.0-preview.2")

    def test_an_undated_version_section_is_not_a_release(self):
        text = "## [Unreleased]\n\n## [1.0.0-preview.3]\n\n## [1.0.0-preview.2] - 2026-09-01\n"

        self.assertEqual(changelog.latest_released_version(text), "1.0.0-preview.2")

    def test_there_is_no_latest_release_without_a_dated_section(self):
        self.assertIsNone(changelog.latest_released_version("# Changelog\n\n## [Unreleased]\n"))

    # released_versions: the breaking-change baseline picks only a tag from this set; a skipped version number has no heading.

    def test_released_versions_are_only_the_dated_headings(self):
        text = (
            "## [Unreleased]\n\n"
            "## [1.0.0-preview.4]\n\n"
            "## [1.0.0-preview.3] - 2026-09-24\n\n"
            "### Added\n- `## [1.0.0-preview.9] - 2026-01-01` is not a heading\n\n"
            "## [1.0.0-preview.1] - 2026-09-20\n"
        )

        self.assertEqual(changelog.released_versions(text), {"1.0.0-preview.3", "1.0.0-preview.1"})

    # release_notes: the version section is written when the TAG is cut and the rehearsal runs before that.

    def test_the_version_section_wins_when_it_exists(self):
        text = (
            "## [Unreleased]\n\n### Added\n- ongoing\n\n"
            "## [1.0.0-preview.1] - 2026-08-28\n\n### Added\n- shipped\n"
        )

        body, heading = changelog.release_notes(text, "1.0.0-preview.1")

        self.assertIn("shipped", body)
        self.assertNotIn("ongoing", body)
        self.assertEqual(heading, "1.0.0-preview.1")

    def test_unreleased_is_read_when_the_version_section_is_missing(self):
        text = "# Changelog\n\n## [Unreleased]\n\n### Added\n- ongoing\n"

        body, heading = changelog.release_notes(text, "1.0.0-preview.1")

        self.assertIn("ongoing", body)
        self.assertEqual(heading, "Unreleased")

    def test_an_empty_unreleased_section_blocks_a_release_without_notes(self):
        text = "# Changelog\n\n## [Unreleased]\n\n## [0.9.0] - 2026-08-01\n\n### Added\n- old\n"

        body, heading = changelog.release_notes(text, "1.0.0-preview.1")

        self.assertIsNone(body)
        self.assertIsNone(heading)

    def test_no_section_at_all_counts_as_no_notes(self):
        body, heading = changelog.release_notes("# Changelog\n", "1.0.0-preview.1")

        self.assertIsNone(body)
        self.assertIsNone(heading)

    def test_notes_that_fit_enter_the_github_body_unchanged(self):
        notes = "Summary.\n\n### Fixed\n- one fix"

        self.assertEqual(changelog.github_release_body(notes, "1.0.0", limit=1_000), notes)

    def test_long_notes_keep_whole_groups_and_name_the_rest(self):
        notes = (
            "Summary.\n\n"
            "### Security\n" + "- s\n" * 20 +
            "### Fixed\n" + "- f\n" * 20 +
            "### Added\n" + "- a\n" * 400 +
            "### Changed\n- c\n"
        )

        body = changelog.github_release_body(notes, "1.0.0-preview.9", limit=600)

        self.assertLessEqual(len(body), 600)
        self.assertIn("### Security", body)
        self.assertIn("### Fixed", body)
        self.assertNotIn("### Added", body)
        # Order is kept: the small group after the one that did not fit is
        # left out too, and both are named.
        self.assertNotIn("### Changed", body)
        self.assertIn("Not shown here: Added, Changed.", body)
        self.assertIn("https://tracon.dev/reference/changelog/#v1.0.0-preview.9", body)

    def test_text_before_the_first_group_that_does_not_fit_alone_is_cut_at_a_line(self):
        notes = "".join(f"line {i}\n" for i in range(500)) + "### Fixed\n- f\n"

        body = changelog.github_release_body(notes, "1.0.0", limit=400)

        self.assertLessEqual(len(body), 400)
        head = body.split("\n\n---", 1)[0]
        self.assertTrue(notes.startswith(head + "\n"))
        self.assertIn("Not shown here: the rest of the notes.", body)

    def test_every_release_note_of_the_repository_fits_the_github_body(self):
        """Every section the repository has cut fits once it is cut down."""
        path = pathlib.Path(__file__).resolve().parent.parent / "CHANGELOG.md"
        text = path.read_text(encoding="utf-8")
        versions = [
            match.group("version")
            for line in text.splitlines()
            if (match := changelog.SECTION_HEADER_PATTERN.match(line))
            and match.group("version") != changelog.UNRELEASED
        ]

        for version in versions:
            body = changelog.github_release_body(changelog.section_body(text, version) or "", version)
            self.assertLessEqual(len(body), changelog.GITHUB_RELEASE_BODY_LIMIT, version)

    def test_the_changelog_of_the_repository_passes_the_rehearsal(self):
        """Whatever state the repository's own file is in, the rehearsal finds notes.

        The assertion follows the file instead of freezing one moment of it.
        An earlier version demanded the heading be `Unreleased`, which was true
        only until the first version was cut -- it went red on cut day and said
        nothing about what had broken. Both states are legitimate: before a cut
        the notes come from Unreleased, after one they come from the version
        section, and in both the body has to be there.
        """
        path = pathlib.Path(__file__).resolve().parent.parent / "CHANGELOG.md"
        cut = [
            match.group("version")
            for line in path.read_text(encoding="utf-8").splitlines()
            if (match := changelog.SECTION_HEADER_PATTERN.match(line))
            and match.group("version") != changelog.UNRELEASED
        ]

        target = cut[0] if cut else "1.0.0-preview.1"
        body, heading = changelog.read_release_notes(path, target)

        self.assertIsNotNone(body)
        self.assertEqual(heading, target if cut else changelog.UNRELEASED)


if __name__ == "__main__":
    unittest.main()
