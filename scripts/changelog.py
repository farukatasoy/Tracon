#!/usr/bin/env python3
"""Parses CHANGELOG.md's Keep a Changelog version sections.

Shared by `kapi.py` (the release-notes gate) and the `github-release` CI job
(the release body) so the two never grow two different parsers for the same
`## [<version>] - <date>` heading (docs/arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md, 123.3).
"""
from __future__ import annotations

import pathlib
import re

SECTION_HEADER_PATTERN = re.compile(r"^## \[(?P<version>[^\]]+)\](?:\s*-\s*.+)?\s*$")


def section_body(changelog_text: str, version: str) -> str | None:
    """The body of the `## [<version>]` section: everything after that
    heading line up to (not including) the next `## ` heading, or the end of
    the file. Returns None when no heading names `version`.

    Only the heading line is parsed; group names (Added/Changed/...) are not
    interpreted, so Keep a Changelog's free-form body text never trips this.
    """
    lines = changelog_text.splitlines()

    start = None
    for index, line in enumerate(lines):
        match = SECTION_HEADER_PATTERN.match(line)
        if match and match.group("version") == version:
            start = index + 1
            break

    if start is None:
        return None

    end = start
    while end < len(lines) and not lines[end].startswith("## "):
        end += 1

    return "\n".join(lines[start:end]).strip()


def has_section(changelog_text: str, version: str) -> bool:
    """True when CHANGELOG.md carries a non-empty `## [<version>]` section."""
    body = section_body(changelog_text, version)
    return bool(body)


UNRELEASED = "Unreleased"


def release_notes(changelog_text: str, version: str) -> tuple[str | None, str | None]:
    """The notes a release of `version` would ship, and the heading they came from.

    `## [<version>]` wins when it exists. When it does not, `## [Unreleased]`
    is used instead: a version section is written at TAG time, by renaming the
    Unreleased heading to the version and the date it shipped on, so before
    that moment the notes for the next release are the ones under Unreleased
    (K-825). Returns (None, None) when neither section carries a body - a
    release with no notes is what the gate exists to stop.
    """
    body = section_body(changelog_text, version)
    if body:
        return body, version

    body = section_body(changelog_text, UNRELEASED)
    if body:
        return body, UNRELEASED

    return None, None


def read_section(changelog_path: pathlib.Path, version: str) -> str | None:
    """Convenience wrapper: reads the file, then extracts the section."""
    return section_body(changelog_path.read_text(encoding="utf-8"), version)


def read_release_notes(changelog_path: pathlib.Path, version: str) -> tuple[str | None, str | None]:
    """Convenience wrapper around :func:`release_notes`."""
    return release_notes(changelog_path.read_text(encoding="utf-8"), version)
