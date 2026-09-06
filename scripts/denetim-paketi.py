#!/usr/bin/env python3
"""Produce a mechanical evidence package for ``faz-denetim``.

The report narrows the auditor's search. It is deliberately advisory: a
regex-based candidate is never promoted to a failing gate, and a clean report
never replaces reading the diff.
"""
from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
# 🚨 `Should\w*`, ciplak `Should` DEGIL. Bu depo Shouldly kullanir ve her
# iddia `ShouldBe`/`ShouldContain`/`ShouldNotBeNull` seklindedir; `\bShould\b`
# bunlarin HICBIRINI eslestirmez ("Should"dan sonra kelime karakteri gelir,
# sinir olusmaz). Olculdu (2026-09-06): `tests/` agacinda 7488 Shouldly
# cagrisi, 6 `Assert.` cagrisi var -- yani tarayici pratikte HER yeni testi
# aday sayiyordu. Cikis kodunu kirmadigi icin gurultu olarak yasadi; kirmizi
# kalan bir dedektor insanlari onu gormezden gelmeye egitir.
ASSERTION = re.compile(r"\b(?:Assert|Should\w*|Verify|Received|HaveDiagnostic|Throws|DoesNotThrow|Equal|True|False|Contains|NotNull)\b")
TEST_ATTRIBUTE = re.compile(r"\[[^\]]*(?:Fact|Theory|Test|TestCase)[^\]]*\]")
TEST_METHOD = re.compile(
    r"^\+\s*(?:public|internal|private|protected)\s+(?:static\s+)?(?:async\s+)?"
    r"(?:void|Task(?:<[^>]+>)?|ValueTask(?:<[^>]+>)?)\s+(\w+)\s*\(",
    re.M,
)


def git(*args: str) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)
    except OSError:
        return subprocess.CompletedProcess(args, returncode=1, stdout="", stderr="git not found")


def _is_worktree(target: str) -> bool:
    return target.upper() in {"WORKTREE", "WORKING-TREE", "WORKING_TREE"}


def _untracked_paths() -> list[str] | None:
    result = git("status", "--porcelain", "--untracked-files=all")
    if result.returncode != 0:
        return None
    return sorted(
        line[3:] for line in result.stdout.splitlines()
        if line.startswith("?? ") and line[3:])


def _worktree_diff(base: str) -> str | None:
    result = git("diff", "--no-ext-diff", "--unified=80", base)
    if result.returncode != 0:
        return None

    untracked = _untracked_paths()
    if untracked is None:
        return None

    chunks = [result.stdout]
    for path in untracked:
        added = git("diff", "--no-ext-diff", "--unified=80", "--no-index", "/dev/null", path)
        if added.returncode not in {0, 1}:
            return None
        chunks.append(added.stdout)
    return "\n".join(chunk for chunk in chunks if chunk)


def diff_range(base: str, target: str) -> str | None:
    if _is_worktree(target):
        return _worktree_diff(base)
    result = git("diff", "--no-ext-diff", "--unified=80", f"{base}..{target}")
    return result.stdout if result.returncode == 0 else None


def changed_files(base: str, target: str) -> list[str] | None:
    if _is_worktree(target):
        result = git("diff", "--no-ext-diff", "--name-only", base)
        untracked = _untracked_paths()
        if result.returncode != 0 or untracked is None:
            return None
        return sorted(set(result.stdout.splitlines()).union(untracked))
    result = git("diff", "--no-ext-diff", "--name-only", f"{base}..{target}")
    return result.stdout.splitlines() if result.returncode == 0 else None


def stat_range(base: str, target: str) -> list[str] | None:
    if _is_worktree(target):
        result = git("diff", "--no-ext-diff", "--stat", base)
        untracked = _untracked_paths()
        if result.returncode != 0 or untracked is None:
            return None
        return result.stdout.splitlines() + [f" {path} (untracked)" for path in untracked]
    result = git("diff", "--no-ext-diff", "--stat", f"{base}..{target}")
    return result.stdout.splitlines() if result.returncode == 0 else None


def _added_lines(diff: str) -> list[str]:
    return [line[1:] for line in diff.splitlines() if line.startswith("+") and not line.startswith("+++")]


def test_theater_candidates(diff: str) -> list[str]:
    """Find newly added attributed test methods with no assertion vocabulary."""
    lines = diff.splitlines()
    candidates: list[str] = []
    for index, line in enumerate(lines):
        if not line.startswith("+") or line.startswith("+++"):
            continue
        if not TEST_METHOD.search(line):
            continue
        window = "\n".join(lines[max(0, index - 5):index])
        if not TEST_ATTRIBUTE.search(window):
            continue
        method = TEST_METHOD.search(line).group(1)
        body: list[str] = []
        for following in lines[index + 1:]:
            if following.startswith("+    [") or following.startswith("+    public "):
                break
            if following.startswith("+") and not following.startswith("+++"):
                body.append(following[1:])
        if not ASSERTION.search("\n".join(body)):
            candidates.append(method)
    return sorted(set(candidates))


def signature_body_candidates(diff: str) -> list[str]:
    """Return conservative candidates for known changed-input bug shapes.

    This intentionally does not treat every capitalized word in a Markdown
    diff as a member. New fingerprints belong here with a focused replay test.
    """
    added = "\n".join(_added_lines(diff))
    candidates: list[str] = []

    # These are intentionally broad candidates. The historical replay is a
    # regression probe; the auditor must inspect the hunk before calling one a
    # defect. The labels make that contract explicit.
    if re.search(r"CompleteAsync[\s\S]{0,120}\bcost\b", added):
        candidates.append("RunEventWriter.cs: CompleteAsync(cost) → RunRecord.Cost")
    if "cached_input_cost" in added or "CachedInputCost" in added:
        candidates.append("cost totals: CachedInputCost/cached_input_cost → every total")

    return sorted(set(candidates))


def http_metadata_candidates(diff: str) -> list[str]:
    candidates: list[str] = []
    for line in _added_lines(diff):
        if re.search(r"\bMap(?:Get|Post|Put|Patch|Delete)\s*\(", line) and ".WithTags" not in line:
            candidates.append(f"HTTP endpoint metadata candidate: {line.strip()}")
    return candidates


def report(base: str, target: str) -> int:
    diff = diff_range(base, target)
    files = changed_files(base, target)
    stats = stat_range(base, target)
    if diff is None or files is None or stats is None:
        print(f"❌ Git diff çözülemedi: {base}..{target}")
        return 2

    print(f"Denetim kanıt paketi: {base}..{target}")
    print("\nGenel diff özeti")
    print("\n".join(stats[-1:]) if stats else "(değişiklik yok)")
    print(f"Değişen dosya: {len(files)}")

    public_api = [path for path in files if pathlib.PurePosixPath(path).name.startswith("PublicAPI.")]
    print(f"\nPublic API delta'sı: {len(public_api)} dosya")
    for path in public_api:
        print(f"  {path}")

    test_files = [
        path for path in files
        if (path.startswith("tests/") or path.startswith("scripts/"))
        and path.endswith(('.cs', '.ts', '.py'))
    ]
    print(f"\nYeni/değişen test dosyaları: {len(test_files)}")
    for path in test_files:
        print(f"  {path}")

    theater = test_theater_candidates(diff)
    print(f"\nİddiası olmayan test metotları: {'yok' if not theater else f'{len(theater)} aday'}")
    for method in theater:
        print(f"  ADAY — {method} (assertion vocabulary bulunamadı; denetçi doğrular)")

    signatures = signature_body_candidates(diff)
    print(f"\nİmza-gövde kayması adayları: {'yok' if not signatures else f'{len(signatures)} aday'}")
    for candidate in signatures:
        print(f"  ADAY — {candidate}")

    http = http_metadata_candidates(diff)
    print(f"\nHTTP üstveri adayları: {'yok' if not http else f'{len(http)} aday'}")
    for candidate in http:
        print(f"  ADAY — {candidate}")

    changed_phase_lines = [line for line in _added_lines(diff) if re.search(r"- \[[ xX]\]", line)]
    print(f"\nFaz DoD satırı delta'sı: {len(changed_phase_lines)}")
    for line in changed_phase_lines[:20]:
        print(f"  {line.strip()}")

    print("\n⚠️ Bu rapor başlangıç noktasıdır; adaylar çıkış kodunu kırmaz ve ham diff yine okunmalıdır.")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--taban", required=True, help="karşılaştırmanın başlangıç commit'i")
    parser.add_argument(
        "--hedef", default="WORKTREE", help="hedef commit'i veya WORKTREE (commit öncesi çalışma ağacı)")
    args = parser.parse_args(argv)
    return report(args.taban, args.hedef)


if __name__ == "__main__":
    sys.exit(main())
