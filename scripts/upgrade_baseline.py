#!/usr/bin/env python3
"""Yükseltme kapısının taban manifestini son yayının etiketinden yazar.

Kapı (`tests/Shared/Upgrade/`), son yayınlanmış sürümün veritabanını kurar ve bugünkü
migration'larla yükseltir. Taban manifesti o yayının her migration dosyasını iki sayıyla
tanımlar: ham sağlama toplamı (veritabanındaki `__migrations` satırı bunu taşır) ve
yorumsuz, boşluksuz deyim özeti (yorum düzeltmesi bunu değiştirmez, deyim değişikliği
değiştirir). Manifest depoda dosyadır; kapı ağa ve git'e ihtiyaç duymaz.

Taban her yayında ilerler. Kesim commit'inde etiket henüz yoktur; migration dosyaları
etiketin işaret edeceği commit'te (`HEAD`) okunur:

    python3 scripts/upgrade_baseline.py --write --package-version 1.0.0-rc.1 --source HEAD

Yayından sonra aynı manifest etiketten yeniden üretilebilir (`--source` verilmezse `v<sürüm>`
etiketi okunur; `--package-version` verilmezse son yayın `breaking_changes.resolve_baseline` ile çözülür).
`gate.py scan`, manifestin sürümü CHANGELOG'daki son yayından farklıysa kırmızıdır.

Deyim özetinin algoritması `tests/Shared/Upgrade/UpgradeBaseline.cs` içindeki
`StatementDigest` ile birebir aynıdır; biri değişirse öteki de değişir (kapı ilk
koşuda her dosyada kırmızı olur, sessiz kalmaz).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MANIFEST = pathlib.PurePath("tests", "Shared", "Upgrade", "UpgradeBaseline.json")

TAG_PREFIX = "v"

PROVIDER_DIRECTORIES = {
    "sqlite": "src/Tracon.Sqlite/Migrations",
    "postgresql": "src/Tracon.PostgreSql/Migrations",
    "sqlserver": "src/Tracon.SqlServer/Migrations",
}

# Opsiyonel migration kümeleri (`views`, `knowledge`): çekirdek kümeden ayrı dizinlerde yaşar, ledger'da kendi
# `set_name`'iyle yazılır. Kapı, yayının opsiyonel kümeleri açık veritabanını da yükseltir.
OPTIONAL_DIRECTORIES = {
    "sqlite": {"views": "src/Tracon.Sqlite/MigrationsViews"},
    "postgresql": {
        "views": "src/Tracon.PostgreSql/MigrationsViews",
        "knowledge": "src/Tracon.PostgreSql/MigrationsKnowledge",
    },
    "sqlserver": {"views": "src/Tracon.SqlServer/MigrationsViews"},
}

_BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.DOTALL)
_LINE_COMMENT = re.compile(r"--[^\n]*")
_WHITESPACE = re.compile(r"[ \t\r\n\f\v]+")
_FILE_NAME = re.compile(r"^(?P<id>\d{4})_(?P<name>.+)\.sql$")


def read_text(data: bytes) -> str:
    """Okur gibi çöz: UTF-8, başındaki BOM atılır (gömülü kaynağı okuyan `StreamReader` de atar)."""
    return data.decode("utf-8-sig")


def raw_checksum(text: str) -> str:
    """`MigrationDescriptor.ComputeChecksum` ile aynı: satır sonu LF'ye çevrilir, SHA-256, büyük harf onaltılık."""
    return hashlib.sha256(text.replace("\r\n", "\n").encode("utf-8")).hexdigest().upper()


def statement_digest(text: str) -> str:
    """Yorumları ve boşluk farkını silip deyimlerin SHA-256 özetini verir (küçük harf onaltılık)."""
    text = text.replace("\r\n", "\n")
    text = _BLOCK_COMMENT.sub(" ", text)
    text = _LINE_COMMENT.sub(" ", text)
    text = _WHITESPACE.sub(" ", text).strip()
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def describe(file_name: str, data: bytes) -> dict[str, object]:
    """Bir migration dosyasının manifest girdisi."""
    match = _FILE_NAME.match(file_name)
    if match is None:
        raise ValueError(f"the migration file name does not match the pattern: {file_name}")
    text = read_text(data)
    return {
        "id": int(match.group("id")),
        "name": file_name[: -len(".sql")],
        "checksum": raw_checksum(text),
        "statements": statement_digest(text),
    }


def _git(root: pathlib.Path, *args: str, binary: bool = False) -> bytes | str:
    result = subprocess.run(["git", *args], cwd=root, capture_output=True, check=False)
    if result.returncode:
        raise RuntimeError(f"git {' '.join(args)}: {result.stderr.decode('utf-8', 'replace').strip()}")
    return result.stdout if binary else result.stdout.decode("utf-8")


def read_tag(root: pathlib.Path, tag: str) -> dict[str, list[dict[str, object]]]:
    """Etiketteki her sağlayıcının migration dosyaları."""
    providers: dict[str, list[dict[str, object]]] = {}
    for provider, directory in PROVIDER_DIRECTORIES.items():
        names = sorted(
            line.rsplit("/", 1)[-1]
            for line in str(_git(root, "ls-tree", "--name-only", tag, directory + "/")).splitlines()
            if line.endswith(".sql"))
        if not names:
            raise RuntimeError(f"tag {tag} holds no migration under {directory}")
        providers[provider] = [
            describe(name, bytes(_git(root, "show", f"{tag}:{directory}/{name}", binary=True))) for name in names
        ]
    return providers


def read_optional_tag(root: pathlib.Path, tag: str) -> dict[str, dict[str, list[dict[str, object]]]]:
    """Etiketteki her sağlayıcının opsiyonel kümelerinin migration dosyaları. Yayında olmayan küme yazılmaz."""
    providers: dict[str, dict[str, list[dict[str, object]]]] = {}
    for provider, sets in OPTIONAL_DIRECTORIES.items():
        found: dict[str, list[dict[str, object]]] = {}
        for set_name, directory in sets.items():
            names = sorted(
                line.rsplit("/", 1)[-1]
                for line in str(_git(root, "ls-tree", "--name-only", tag, directory + "/")).splitlines()
                if line.endswith(".sql"))
            if names:
                found[set_name] = [
                    describe(name, bytes(_git(root, "show", f"{tag}:{directory}/{name}", binary=True))) for name in names
                ]
        providers[provider] = found
    return providers


def build_manifest(
        root: pathlib.Path, version: str, source: str | None, previous: dict[str, object] | None) -> dict[str, object]:
    """Yeni manifest. Aynı sürümün muafiyet listesi korunur; sürüm değişince boşalır.

    `source` migration dosyalarının okunacağı git başvurusudur (varsayılan: `v<sürüm>` etiketi).
    """
    tag = f"{TAG_PREFIX}{version}"
    kept = previous.get("exemptions", []) if previous and previous.get("version") == version else []
    return {
        "version": version,
        "tag": tag,
        "providers": read_tag(root, source or tag),
        "optionalSets": read_optional_tag(root, source or tag),
        "exemptions": kept,
    }


def differing_from_head(root: pathlib.Path, manifest: dict[str, object]) -> list[str]:
    """Etiketten bugüne deyimi değişmiş migration dosyaları (kapı bunları muafiyet listesinde bekler)."""
    changed: list[str] = []
    providers = manifest["providers"]
    assert isinstance(providers, dict)
    for provider, entries in providers.items():
        directory = root / PROVIDER_DIRECTORIES[provider]
        for entry in entries:
            path = directory / f"{entry['name']}.sql"
            if not path.is_file():
                changed.append(f"{provider}/{entry['name']} (deleted)")
            elif statement_digest(read_text(path.read_bytes())) != entry["statements"]:
                changed.append(f"{provider}/{entry['name']}")
    optional = manifest.get("optionalSets", {})
    assert isinstance(optional, dict)
    for provider, sets in optional.items():
        for set_name, entries in sets.items():
            directory = root / OPTIONAL_DIRECTORIES[provider][set_name]
            for entry in entries:
                path = directory / f"{entry['name']}.sql"
                if not path.is_file():
                    changed.append(f"{provider}/{set_name}/{entry['name']} (deleted)")
                elif statement_digest(read_text(path.read_bytes())) != entry["statements"]:
                    changed.append(f"{provider}/{set_name}/{entry['name']}")
    return changed


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Writes the baseline manifest of the upgrade gate from the tag of the last release.\n\n"
            "Before the tag exists, read the migration files from the commit the tag will point to:\n\n"
            "    python3 scripts/upgrade_baseline.py --write --package-version 1.0.0-rc.1 --source HEAD"),
        formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--write", action="store_true", help="rewrite the manifest from the tag")
    parser.add_argument(
        "--package-version", help="version of the baseline (default: the last release, `breaking_changes.resolve_baseline`)")
    parser.add_argument(
        "--source", help="git reference to read the migration files from (default: the `v<version>` tag)")
    args = parser.parse_args(argv)
    if not args.write:
        parser.error("choose what to do: --write")

    version = args.package_version
    if version is None:
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes

        version = breaking_changes.resolve_baseline(ROOT)

    path = ROOT / MANIFEST
    previous = json.loads(path.read_text(encoding="utf-8")) if path.is_file() else None
    manifest = build_manifest(ROOT, version, args.source, previous)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    counts = ", ".join(f"{provider} {len(entries)}" for provider, entries in manifest["providers"].items())
    optional = ", ".join(
        f"{provider}/{set_name} {len(entries)}"
        for provider, sets in manifest["optionalSets"].items()
        for set_name, entries in sets.items())
    print(
        f"Baseline: {manifest['tag']} ({counts} migrations; optional sets: {optional or 'none'}, "
        f"source {args.source or manifest['tag']}) -> {MANIFEST}")

    changed = differing_from_head(ROOT, manifest)
    if changed:
        print("Migration files whose statements changed between the tag and now (the gate needs each one as an exemption, with a reason):")
        for name in changed:
            print(f"  {name}")
    else:
        print("No migration file changed its statements between the tag and now.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
