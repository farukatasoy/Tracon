#!/usr/bin/env python3
"""Tests for the release-to-release gate of the non-.NET surfaces.

Pure logic: OpenAPI documents as dictionaries, and a temp git repository for the
name sets and the exit code. One red fixture and one green fixture per rule of
the HTTP row; the rule table in `contract_breaks.py` and the table in
`reference/versioning.md` are compared by hand in the phase audit.
"""
from __future__ import annotations

import contextlib
import copy
import importlib.util
import io
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import unittest
import unittest.mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("contract_breaks", ROOT / "scripts" / "contract_breaks.py")
sk = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = sk
spec.loader.exec_module(sk)


# -----------------------------------------------------------------------------
# OpenAPI fixtures
# -----------------------------------------------------------------------------


def _document() -> dict:
    """A small document in the shape ASP.NET Core writes (OpenAPI 3.1)."""
    return {
        "openapi": "3.1.1",
        "info": {"title": "Tracon", "version": "1.0.0"},
        "paths": {
            "/api/runs/{runId}": {
                "get": {
                    "tags": ["Runs"],
                    "summary": "Reads a run.",
                    "operationId": "TraconGetRun",
                    "parameters": [
                        {"name": "runId", "in": "path", "required": True, "schema": {"type": "string", "format": "uuid"}},
                    ],
                    "responses": {
                        "200": {
                            "description": "OK",
                            "content": {"application/json": {"schema": {"$ref": "#/components/schemas/RunRecord"}}},
                        },
                        "404": {"description": "Not Found"},
                    },
                },
            },
            "/api/runs": {
                "get": {
                    "operationId": "TraconListRuns",
                    "parameters": [{"name": "agent", "in": "query", "schema": {"type": "string"}}],
                    "responses": {
                        "200": {
                            "description": "OK",
                            "content": {
                                "application/json": {
                                    "schema": {"type": "array", "items": {"$ref": "#/components/schemas/RunRecord"}},
                                },
                            },
                        },
                    },
                },
                "post": {
                    "operationId": "TraconStartRun",
                    "requestBody": {
                        "required": True,
                        "content": {"application/json": {"schema": {"$ref": "#/components/schemas/StartRunRequest"}}},
                    },
                    "responses": {"201": {"description": "Created"}},
                },
            },
        },
        "components": {
            "schemas": {
                "RunRecord": {
                    "type": "object",
                    "required": ["id", "status"],
                    "properties": {
                        "id": {"type": "string", "format": "uuid"},
                        "status": {"$ref": "#/components/schemas/RunStatus"},
                        "agentName": {"type": "string"},
                        "note": {"oneOf": [{"type": "null"}, {"type": "string"}]},
                        "parent": {"oneOf": [{"type": "null"}, {"$ref": "#/components/schemas/RunRecord"}]},
                    },
                },
                "RunStatus": {"type": "string", "enum": ["Running", "Completed", "Failed"]},
                "StartRunRequest": {
                    "type": "object",
                    "required": ["agentName"],
                    "properties": {
                        "agentName": {"type": "string"},
                        "message": {"type": "string"},
                        "mode": {"$ref": "#/components/schemas/StartMode"},
                    },
                },
                "StartMode": {"type": "string", "enum": ["Sync", "Async"]},
            },
        },
    }


def _compare(mutate) -> tuple[list, list]:
    old = _document()
    new = copy.deepcopy(old)
    mutate(new)
    return sk.compare_openapi(old, new)


def _run_record(document: dict) -> dict:
    return document["components"]["schemas"]["RunRecord"]


def _start_request(document: dict) -> dict:
    return document["components"]["schemas"]["StartRunRequest"]


def _rules(changes: list) -> list[str]:
    return [change.rule for change in changes]


class HttpKirilmaKurallari(unittest.TestCase):
    """Every rule in the 'only in a major' column is red."""

    def assert_break(self, mutate, expected_rule: str, ident: str | None = None) -> None:
        breaks, _ = _compare(mutate)
        self.assertTrue(
            any(expected_rule in rule for rule in _rules(breaks)),
            f"expected rule '{expected_rule}' not found: {_rules(breaks)}")
        if ident is not None:
            self.assertTrue(any(ident in (change.ident, change.alt) for change in breaks), f"ident '{ident}' is missing")

    def test_uc_silinirse_kirmizidir(self):
        self.assert_break(lambda d: d["paths"].pop("/api/runs"), "endpoint or method removed", "/api/runs")

    def test_metot_silinirse_kirmizidir(self):
        self.assert_break(lambda d: d["paths"]["/api/runs"].pop("post"), "endpoint or method removed")

    def test_yanit_alani_silinirse_kirmizidir(self):
        self.assert_break(lambda d: _run_record(d)["properties"].pop("agentName"), "response field removed", "agentName")

    def test_yanit_alani_tur_degistirirse_kirmizidir(self):
        self.assert_break(
            lambda d: _run_record(d)["properties"]["agentName"].update(type="integer"), "type changed")

    def test_istekte_yeni_zorunlu_alan_kirmizidir(self):
        def mutate(d):
            _start_request(d)["properties"]["tenant"] = {"type": "string"}
            _start_request(d)["required"].append("tenant")

        self.assert_break(mutate, "new required field in the request", "tenant")

    def test_opsiyonel_istek_alani_zorunlu_olursa_kirmizidir(self):
        self.assert_break(lambda d: _start_request(d)["required"].append("message"), "optional request field became required")

    def test_istek_enum_degeri_silinirse_kirmizidir(self):
        self.assert_break(
            lambda d: d["components"]["schemas"]["StartMode"]["enum"].remove("Async"), "request enum value removed")

    def test_istek_alani_silinirse_kirmizidir(self):
        self.assert_break(lambda d: _start_request(d)["properties"].pop("message"), "request field removed")

    def test_basari_durum_kodu_degisirse_kirmizidir(self):
        def mutate(d):
            responses = d["paths"]["/api/runs"]["post"]["responses"]
            responses["200"] = responses.pop("201")

        self.assert_break(mutate, "success status code removed: 201")

    def test_zorunlu_yanit_alani_opsiyonel_olursa_kirmizidir(self):
        self.assert_break(lambda d: _run_record(d)["required"].remove("status"), "required response field became optional")

    def test_istekte_yeni_zorunlu_parametre_kirmizidir(self):
        def mutate(d):
            d["paths"]["/api/runs"]["get"]["parameters"].append(
                {"name": "tenant", "in": "query", "required": True, "schema": {"type": "string"}})

        self.assert_break(mutate, "new required parameter in the request")

    def test_opsiyonel_parametre_zorunlu_olursa_kirmizidir(self):
        self.assert_break(
            lambda d: d["paths"]["/api/runs"]["get"]["parameters"][0].update(required=True),
            "optional parameter became required")

    def test_parametre_silinirse_kirmizidir(self):
        self.assert_break(lambda d: d["paths"]["/api/runs"]["get"]["parameters"].clear(), "request parameter removed")

    def test_operation_id_degisirse_kirmizidir(self):
        """NSwag names the generated client method from it."""
        self.assert_break(
            lambda d: d["paths"]["/api/runs"]["get"].update(operationId="TraconListRunsV2"), "operationId changed")

    def test_yanit_null_olabilir_hale_gelirse_kirmizidir(self):
        self.assert_break(
            lambda d: _run_record(d)["properties"]["agentName"].update(type=["string", "null"]),
            "response field can now be null")

    def test_istek_alani_null_olamaz_hale_gelirse_kirmizidir(self):
        def mutate(d):
            d["components"]["schemas"]["StartRunRequest"]["properties"]["message"] = {"type": "string"}

        old = _document()
        old["components"]["schemas"]["StartRunRequest"]["properties"]["message"] = {"type": ["string", "null"]}
        new = copy.deepcopy(old)
        mutate(new)
        breaks, _ = sk.compare_openapi(old, new)
        self.assertIn("request field can no longer be null", _rules(breaks))

    def test_sema_baska_semaya_baglanirsa_kirmizidir(self):
        """A schema name becomes a client type name."""
        def mutate(d):
            d["components"]["schemas"]["RunStatus2"] = d["components"]["schemas"]["RunStatus"]
            _run_record(d)["properties"]["status"] = {"$ref": "#/components/schemas/RunStatus2"}

        self.assert_break(mutate, "schema now points to another schema")

    def test_ortam_turu_kalkarsa_kirmizidir(self):
        def mutate(d):
            content = d["paths"]["/api/runs/{runId}"]["get"]["responses"]["200"]["content"]
            content["text/plain"] = content.pop("application/json")

        self.assert_break(mutate, "media type removed")

    def test_zorunlu_istek_govdesi_eklenirse_kirmizidir(self):
        def mutate(d):
            d["paths"]["/api/runs"]["get"]["requestBody"] = {
                "required": True,
                "content": {"application/json": {"schema": {"type": "object"}}},
            }

        self.assert_break(mutate, "new required body in the request")

    def test_istek_govdesi_zorunlu_olursa_kirmizidir(self):
        def mutate(d):
            d["paths"]["/api/runs"]["post"]["requestBody"]["required"] = False

        old = _document()
        old["paths"]["/api/runs"]["post"]["requestBody"]["required"] = False
        new = copy.deepcopy(old)
        new["paths"]["/api/runs"]["post"]["requestBody"]["required"] = True
        breaks, _ = sk.compare_openapi(old, new)
        self.assertIn("optional request body became required", _rules(breaks))


    def test_yanit_enum_degeri_silinirse_kirmizidir(self):
        """The generated .NET client loses a member; Tracon.Client is outside PublicAPI tracking."""
        self.assert_break(
            lambda d: d["components"]["schemas"]["RunStatus"]["enum"].remove("Failed"), "response enum value removed")


def _two_users_of_owner() -> dict:
    """`Owner` is used by two request bodies, once per property; nullability is a property of the PLACE."""
    document = _document()
    schemas = document["components"]["schemas"]
    schemas["Owner"] = {"type": "object", "properties": {"name": {"type": "string"}}}
    for name in ("First", "Second"):
        schemas[name] = {"type": "object", "properties": {"owner": {"oneOf": [{"type": "null"}, {"$ref": "#/components/schemas/Owner"}]}}}
        document["paths"][f"/api/{name.lower()}"] = {
            "post": {
                "operationId": f"Post{name}",
                "requestBody": {"content": {"application/json": {"schema": {"$ref": f"#/components/schemas/{name}"}}}},
                "responses": {"200": {"description": "OK", "content": {"application/json": {"schema": {"$ref": f"#/components/schemas/{name}"}}}}},
            },
        }
    return document


class HttpNullKaymasiTestleri(unittest.TestCase):
    """The audit's red 2: the second use of a named schema must not hide a nullability change."""

    def _compare(self, mutate):
        old = _two_users_of_owner()
        new = copy.deepcopy(old)
        mutate(new)
        return sk.compare_openapi(old, new)

    def test_ikinci_kullanim_yerinde_istek_alani_null_olamaz_hale_gelirse_kirmizidir(self):
        def mutate(d):
            d["components"]["schemas"]["Second"]["properties"]["owner"] = {"$ref": "#/components/schemas/Owner"}

        breaks, _ = self._compare(mutate)
        self.assertIn("request field can no longer be null", _rules(breaks))
        self.assertTrue(any(change.alt == "owner" for change in breaks), [c.ident for c in breaks])

    def test_ikinci_kullanim_yerinde_yanit_alani_null_olabilir_hale_gelirse_kirmizidir(self):
        def mutate(d):
            d["components"]["schemas"]["Second"]["properties"]["owner"] = {"oneOf": [{"type": "null"}, {"$ref": "#/components/schemas/Owner"}]}

        old = _two_users_of_owner()
        old["components"]["schemas"]["Second"]["properties"]["owner"] = {"$ref": "#/components/schemas/Owner"}
        new = copy.deepcopy(old)
        mutate(new)
        breaks, _ = sk.compare_openapi(old, new)
        self.assertIn("response field can now be null", _rules(breaks))

    def test_degismeyen_ortak_sema_temizdir(self):
        breaks, additions = self._compare(lambda d: None)
        self.assertEqual(([], []), (breaks, additions))

    def test_gercek_belgede_istegin_ulastigi_her_null_olabilen_ref_yeri_kirmizi_olabilir(self):
        """Every `oneOf[null, $ref]` or `anyOf[null, $ref]` property a REQUEST reaches in the real document: making
        any ONE of them non-null is red. The audit found three that were not (named schemas used a second time).
        The published document spells a free-form member `anyOf` and every other nullable reference `oneOf`."""
        document = json.loads((ROOT / "docs" / "openapi" / "tracon.json").read_text(encoding="utf-8"))
        reachable = self._request_reachable(document)
        places = [
            (schema_name, property_name, keyword)
            for schema_name, schema in document["components"]["schemas"].items()
            if schema_name in reachable
            for property_name, prop in (schema.get("properties") or {}).items()
            for keyword in ("oneOf", "anyOf")
            if isinstance(prop.get(keyword), list)
            and any(m.get("type") == "null" for m in prop[keyword])
            and any("$ref" in m for m in prop[keyword])
        ]
        self.assertGreater(len(places), 15, "no nullable $ref place reachable from a request was found")

        missed = []
        for schema_name, property_name, keyword in places:
            new = copy.deepcopy(document)
            properties = new["components"]["schemas"][schema_name]["properties"]
            target = next(m for m in properties[property_name][keyword] if "$ref" in m)
            properties[property_name] = {"$ref": target["$ref"]}
            breaks, _ = sk.compare_openapi(document, new)
            if not any(change.rule == "request field can no longer be null" and change.alt == property_name for change in breaks):
                missed.append(f"{schema_name}.{property_name}")

        self.assertEqual([], missed, "a null shift a request reaches that is not red")

    @staticmethod
    def _request_reachable(document: dict) -> set[str]:
        """The schema names a request body or a parameter reaches, directly or through `$ref`."""
        schemas = document["components"]["schemas"]
        pending: list[object] = []
        for item in document["paths"].values():
            for operation in item.values():
                if not isinstance(operation, dict):
                    continue
                pending.append(operation.get("requestBody"))
                pending.append(operation.get("parameters"))
        seen: set[str] = set()
        while pending:
            node = pending.pop()
            if isinstance(node, dict):
                reference = node.get("$ref")
                if isinstance(reference, str) and reference.startswith("#/components/schemas/"):
                    name = reference.rsplit("/", 1)[-1]
                    if name not in seen:
                        seen.add(name)
                        pending.append(schemas.get(name))
                pending.extend(node.values())
            elif isinstance(node, list):
                pending.extend(node)
        return seen


class HttpBelgeDuzeyiTestleri(unittest.TestCase):
    def test_guvenlik_gereksinimi_degisirse_kirmizidir(self):
        old = _document()
        old["security"] = [{"bearer": []}]
        new = copy.deepcopy(old)
        new["security"] = [{"apiKey": []}]
        breaks, _ = sk.compare_openapi(old, new)
        self.assertIn("document-level security requirement changed", _rules(breaks))

    def test_guvenlik_gereksinimi_silinirse_kirmizidir(self):
        old = _document()
        old["security"] = [{"bearer": []}]
        new = copy.deepcopy(old)
        new.pop("security")
        breaks, _ = sk.compare_openapi(old, new)
        self.assertIn("document-level security requirement changed", _rules(breaks))

    def test_sunucu_adresi_ve_belge_surumu_degisirse_kirilma_degildir(self):
        old = _document()
        old["servers"] = [{"url": "http://localhost:5081"}]
        new = copy.deepcopy(old)
        new["servers"] = [{"url": "https://agents.example.com"}]
        new["openapi"] = "3.1.2"
        new["info"]["title"] = "Other"
        self.assertEqual(([], []), sk.compare_openapi(old, new))

    def test_taninmayan_components_anahtari_kirmizidir(self):
        breaks, _ = _compare(lambda d: d["components"].update(responses={"X": {"description": "x"}}))
        self.assertTrue(any("components.responses" in change.subject for change in breaks), _rules(breaks))

    def test_istek_govdesi_ref_ile_yazilirsa_cozulur(self):
        old = _document()
        body = old["paths"]["/api/runs"]["post"]["requestBody"]
        old["components"]["requestBodies"] = {"Start": body}
        old["paths"]["/api/runs"]["post"]["requestBody"] = {"$ref": "#/components/requestBodies/Start"}
        new = copy.deepcopy(old)
        new["components"]["schemas"]["StartRunRequest"]["required"].append("message")
        breaks, _ = sk.compare_openapi(old, new)
        self.assertIn("optional request field became required", _rules(breaks))


class HttpTanimaKurallari(unittest.TestCase):
    """A change the rules do not name is red: the gate fails closed."""

    def test_taninmayan_sema_anahtari_kirmizidir(self):
        breaks, _ = _compare(lambda d: _run_record(d)["properties"]["agentName"].update(minLength=3))
        self.assertTrue(any("unrecognized change: minLength" in rule for rule in _rules(breaks)), _rules(breaks))

    def test_taninmayan_islem_anahtari_kirmizidir(self):
        breaks, _ = _compare(lambda d: d["paths"]["/api/runs"]["get"].update(security=[{"bearer": []}]))
        self.assertTrue(any("unrecognized change: security" in rule for rule in _rules(breaks)), _rules(breaks))

    def test_taninmayan_belge_anahtari_kirmizidir(self):
        breaks, _ = _compare(lambda d: d.update(webhooks={}))
        self.assertTrue(any("unrecognized change" in rule for rule in _rules(breaks)), _rules(breaks))

    def test_desen_degisirse_kirmizidir(self):
        breaks, _ = _compare(lambda d: _run_record(d)["properties"]["agentName"].update(pattern="^a+$"))
        self.assertIn("pattern changed", _rules(breaks))

    def test_bilinmeyen_basari_yaniti_anahtari_kirmizidir(self):
        def mutate(d):
            d["paths"]["/api/runs"]["post"]["responses"]["201"]["headers"] = {"Location": {"schema": {"type": "string"}}}

        breaks, _ = _compare(mutate)
        self.assertTrue(any("unrecognized change: response 201 headers" in rule for rule in _rules(breaks)), _rules(breaks))


class HttpEklemeKurallari(unittest.TestCase):
    """Every rule in the 'free in a minor' column is green - and an addition, which a patch may not make."""

    def assert_green(self, mutate, expected_addition: str | None = None) -> None:
        breaks, additions = _compare(mutate)
        self.assertEqual([], breaks, f"a break was assumed: {_rules(breaks)}")
        if expected_addition is not None:
            self.assertTrue(
                any(expected_addition in rule for rule in _rules(additions)),
                f"expected addition '{expected_addition}' not found: {_rules(additions)}")

    def test_yeni_uc_ekleme_sayilir(self):
        def mutate(d):
            d["paths"]["/api/agents"] = {"get": {"operationId": "TraconListAgents", "responses": {"200": {"description": "OK"}}}}

        self.assert_green(mutate, "new endpoint")

    def test_yanita_yeni_alan_ekleme_sayilir(self):
        self.assert_green(
            lambda d: _run_record(d)["properties"].update(cost={"type": "number"}), "new field in the response")

    def test_istekte_yeni_opsiyonel_alan_ekleme_sayilir(self):
        self.assert_green(
            lambda d: _start_request(d)["properties"].update(label={"type": "string"}), "new optional field in the request")

    def test_istekte_yeni_opsiyonel_parametre_ekleme_sayilir(self):
        def mutate(d):
            d["paths"]["/api/runs"]["get"]["parameters"].append({"name": "skip", "in": "query", "schema": {"type": "integer"}})

        self.assert_green(mutate, "new optional parameter in the request")

    def test_yanit_enum_degeri_ekleme_sayilir(self):
        self.assert_green(
            lambda d: d["components"]["schemas"]["RunStatus"]["enum"].append("AwaitingInput"), "response enum value added")

    def test_istek_enum_degeri_ekleme_sayilir(self):
        self.assert_green(
            lambda d: d["components"]["schemas"]["StartMode"]["enum"].append("Queued"), "request enum value added")

    def test_yeni_hata_yaniti_kirilma_degildir(self):
        self.assert_green(lambda d: d["paths"]["/api/runs"]["get"]["responses"].update({"429": {"description": "Too Many"}}))

    def test_hata_yaniti_kalkarsa_kirilma_degildir(self):
        self.assert_green(lambda d: d["paths"]["/api/runs/{runId}"]["get"]["responses"].pop("404"))

    def test_zorunlu_istek_alani_opsiyonel_olursa_kirilma_degildir(self):
        self.assert_green(lambda d: _start_request(d)["required"].remove("agentName"))

    def test_opsiyonel_yanit_alani_zorunlu_olursa_kirilma_degildir(self):
        self.assert_green(lambda d: _run_record(d)["required"].append("agentName"))

    def test_belge_metni_degisirse_kirilma_degildir(self):
        def mutate(d):
            d["info"]["version"] = "1.2.0"
            d["paths"]["/api/runs"]["get"]["summary"] = "Lists runs."
            d["paths"]["/api/runs"]["get"]["tags"] = ["Runs", "Other"]
            d["paths"]["/api/runs"]["get"]["deprecated"] = True
            _run_record(d)["properties"]["agentName"]["description"] = "The agent."
            _run_record(d)["properties"]["agentName"]["examples"] = ["support"]

        self.assert_green(mutate)

    def test_yol_degiskeni_yeniden_adlandirilirsa_ayni_uctur(self):
        def mutate(d):
            item = d["paths"].pop("/api/runs/{runId}")
            operation = item["get"]
            operation["parameters"][0]["name"] = "id"
            d["paths"]["/api/runs/{id}"] = item

        self.assert_green(mutate)

    def test_ayni_belge_karsilastirilirsa_bos_doner(self):
        breaks, additions = sk.compare_openapi(_document(), _document())
        self.assertEqual(([], []), (breaks, additions))

    def test_kendine_donen_sema_biter(self):
        """`RunRecord.parent` is a RunRecord: the walk must stop."""
        breaks, additions = sk.compare_openapi(_document(), _document())
        self.assertEqual(0, len(breaks) + len(additions))

    def test_gercek_belge_kendisiyle_karsilastirilirsa_bos_doner(self):
        document = json.loads((ROOT / "docs" / "openapi" / "tracon.json").read_text(encoding="utf-8"))
        breaks, additions = sk.compare_openapi(document, document)
        self.assertEqual(([], []), (breaks, additions))
        self.assertGreater(len(document["paths"]), 100, "the real document was not read")


# -----------------------------------------------------------------------------
# Name sets and the gate, over a temp git repository
# -----------------------------------------------------------------------------


def _git(repo: pathlib.Path, *args: str) -> str:
    environment = {
        **os.environ,
        "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@example.invalid",
        "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@example.invalid",
        "GIT_CONFIG_GLOBAL": os.devnull, "GIT_CONFIG_NOSYSTEM": "1",
    }
    return subprocess.run(["git", *args], cwd=repo, env=environment, check=True, capture_output=True, text=True).stdout


SNAPSHOTS = {
    "docs/contracts/sse-events.txt": "# header\nrun-events run.started\nrun-events run.completed\nagent-stream done\n",
    "docs/contracts/telemetry.txt": "# header\ninstrument tracon.runs Counter<long> unit={run} attributes=tracon.agent.name\nattribute tracon.agent.name\n",
    "docs/contracts/configuration.txt": "# header\nTracon:Quotas:Enabled\nTracon:Observability:IncludeAgentVersionTag\n",
    "docs/contracts/error-codes.txt": "# header\ntool_timeout\ncontent_blocked\n",
    "docs/contracts/validation-codes.txt": "# header\nunknown_tool\nunknown_model\n",
    "docs/contracts/cli.txt": "# header\ncommand eval\noption eval --timeout\n",
}


def _write(repo: pathlib.Path, relative: str, text: str) -> None:
    path = repo / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def _repo_with_baseline(directory: str, *, tag: str = "v1.0.0", snapshots: bool = True) -> pathlib.Path:
    repo = pathlib.Path(directory) / "repo"
    repo.mkdir()
    _git(repo, "init", "-q", "-b", "main")
    _write(repo, "docs/openapi/tracon.json", json.dumps(_document(), indent=2))
    if snapshots:
        for relative, text in SNAPSHOTS.items():
            _write(repo, relative, text)
    _write(repo, "CHANGELOG.md", "# Changelog\n\n## [Unreleased]\n\n")
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "-m", "baseline")
    _git(repo, "tag", tag)
    return repo


def _quiet(call):
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        result = call()
    return result, output.getvalue()


def _check(repo: pathlib.Path, *, baseline: str = "1.0.0", version: str = "1.1.0", notes: str = "") -> tuple[int, str]:
    _write(repo, "CHANGELOG.md", f"# Changelog\n\n## [Unreleased]\n\n{notes}\n")
    return _quiet(lambda: sk.check(repo=repo, baseline=baseline, version=version, changelog=repo / "CHANGELOG.md"))


class AdKumesiTestleri(unittest.TestCase):
    def test_degismeyen_yuzey_temizdir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            results = sk.surface_results(repo, "1.0.0")

            self.assertEqual(7, len(results))
            self.assertTrue(all(not r.breaks and not r.additions and r.note is None for r in results))

    def test_silinen_satir_kirilmadir_eklenen_satir_degildir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/sse-events.txt", "# header\nrun-events run.started\nagent-stream done\nagent-stream extra\n")

            sse = next(r for r in sk.surface_results(repo, "1.0.0") if r.surface == "SSE")

            self.assertEqual(["run-events run.completed"], [c.subject for c in sse.breaks])
            self.assertEqual("run.completed", sse.breaks[0].ident)
            self.assertEqual(["agent-stream extra"], [c.subject for c in sse.additions])

    def test_degisen_satir_kirilmadir(self):
        """A unit changed on an instrument is the old line gone."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/telemetry.txt", "# header\ninstrument tracon.runs Counter<long> unit=1 attributes=tracon.agent.name\nattribute tracon.agent.name\n")

            telemetry = next(r for r in sk.surface_results(repo, "1.0.0") if r.surface == "telemetry")

            self.assertEqual(1, len(telemetry.breaks))
            self.assertEqual("tracon.runs", telemetry.breaks[0].ident)

    def test_cli_secenegi_ad_olarak_anilir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/cli.txt", "# header\ncommand eval\n")

            cli = next(r for r in sk.surface_results(repo, "1.0.0") if r.surface == "CLI")

            self.assertEqual("--timeout", cli.breaks[0].ident)

    def test_taban_etiketinde_dosya_yoksa_preview_hattinda_not_yazar_hata_vermez(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0-preview.5", snapshots=False)
            results = sk.surface_results(repo, "1.0.0-preview.5")

            self.assertEqual([None], sorted({r.note for r in results if r.surface == HTTP}))
            names = [r for r in results if r.surface != HTTP]
            self.assertTrue(all(r.note is not None and "no baseline" in r.note for r in names))
            self.assertTrue(all(not r.breaks for r in names))

    def test_donmus_tabanda_dosya_yoksa_hatadir(self):
        """A frozen baseline was cut after the files existed: a missing one is a hole."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0", snapshots=False)

            with self.assertRaises(sk.SurfaceError):
                sk.surface_results(repo, "1.0.0")

    def test_taban_etiketi_yoksa_kirmizidir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)

            with self.assertRaises(sk.bc.BaselineError):
                sk.surface_results(repo, "9.9.9")

            code, output = _check(repo, baseline="9.9.9", version="9.9.10")
            self.assertEqual(1, code)
            self.assertIn("baseline tag", output)


    def test_dogrulama_kodu_silinirse_kirilmadir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/validation-codes.txt", "# header\nunknown_model\n")

            surface = next(r for r in sk.surface_results(repo, "1.0.0") if r.surface == "validation code")

            self.assertEqual(["unknown_tool"], [c.ident for c in surface.breaks])

    def test_taban_dosyasi_okunamazsa_kirmizidir_ilk_kayit_sayilmaz(self):
        """A missing file is a first record; a file that exists and cannot be read is a hole."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            real = sk.bc._git

            def broken_show(root, *args):
                if args and args[0] == "show" and args[1].endswith("sse-events.txt"):
                    return subprocess.CompletedProcess(args, 128, "", "fatal: unable to read blob")
                return real(root, *args)

            with unittest.mock.patch.object(sk.bc, "_git", broken_show):
                code, output = _check(repo)

            self.assertEqual(1, code, output)
            self.assertIn("cannot read", output)

    def test_taban_agaci_sorgulanamazsa_kirmizidir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            real = sk.bc._git

            def broken_tree(root, *args):
                if args and args[0] == "ls-tree" and "cli.txt" in args[-1]:
                    return subprocess.CompletedProcess(args, 128, "", "fatal: not a tree object")
                return real(root, *args)

            with unittest.mock.patch.object(sk.bc, "_git", broken_tree):
                code, output = _check(repo)

            self.assertEqual(1, code, output)
            self.assertIn("cannot query", output)


HTTP = "HTTP"


class KapiTestleri(unittest.TestCase):
    """`check`: repo + CHANGELOG -> exit code, as gate.py calls it."""

    def test_temiz_yuzey_yesildir_ve_hat_satirini_yazar(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            code, output = _check(repo)

            self.assertEqual(0, code, output)
            self.assertIn("Line: frozen 1.x", output)
            self.assertIn("HTTP: 0 breaks", output)
            self.assertIn("CLI: 0 breaks", output)

    def test_donmus_hatta_notta_adi_gecen_kirilma_yine_kirmizidir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/error-codes.txt", "# header\ncontent_blocked\n")

            code, output = _check(repo, notes="- `tool_timeout` is gone.")

            self.assertEqual(1, code, output)
            self.assertIn("on a frozen line", output)
            self.assertIn("error code: 1 break", output)

    def test_manuel_case_3_donmus_hatta_iki_kirilma_kirmizidir(self):
        """A gone SSE name and a gone response field, baseline 1.0.0, release 1.1.0."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/sse-events.txt", "# header\nrun-events run.started\nagent-stream done\n")
            document = _document()
            _run_record(document)["properties"].pop("agentName")
            _write(repo, "docs/openapi/tracon.json", json.dumps(document, indent=2))

            code, output = _check(repo, baseline="1.0.0", version="1.1.0")

            self.assertEqual(1, code, output)
            self.assertIn("Line: frozen 1.x", output)
            self.assertIn("HTTP: 1 break", output)
            self.assertIn("SSE: 1 break", output)

    def test_preview_hattinda_notta_adi_gecen_kirilma_yesildir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0-preview.5")
            _write(repo, "docs/contracts/error-codes.txt", "# header\ncontent_blocked\n")

            code, output = _check(
                repo, baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="- `tool_timeout` is gone.")

            self.assertEqual(0, code, output)
            self.assertIn("error code: 1 break (named in the notes)", output)

    def test_preview_hattinda_notta_adi_gecmeyen_kirilma_kirmizidir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0-preview.5")
            _write(repo, "docs/contracts/error-codes.txt", "# header\ncontent_blocked\n")

            code, output = _check(
                repo, baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="- `content_blocked` still works.")

            self.assertEqual(1, code, output)
            self.assertIn("The release notes do not name the break", output)
            self.assertIn("tool_timeout", output)

    def test_preview_hattinda_not_yoksa_kirmizidir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0-preview.5")
            _write(repo, "docs/contracts/error-codes.txt", "# header\ncontent_blocked\n")
            _write(repo, "CHANGELOG.md", "# Changelog\n")

            code, output = _quiet(lambda: sk.check(
                repo=repo, baseline="1.0.0-preview.5", version="1.0.0-preview.6", changelog=repo / "CHANGELOG.md"))

            self.assertEqual(1, code, output)
            self.assertIn("no release notes for", output)

    def test_http_kirilmasi_alan_adiyla_anilabilir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0-preview.5")
            document = _document()
            _run_record(document)["properties"].pop("agentName")
            _write(repo, "docs/openapi/tracon.json", json.dumps(document, indent=2))

            code, output = _check(
                repo, baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="- `RunRecord.AgentName` is gone from the run.")

            self.assertEqual(0, code, output)
            self.assertIn("HTTP: 1 break (named in the notes)", output)

    def test_yama_surumunde_eklenen_yuzey_kirmizidir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/error-codes.txt", "# header\ntool_timeout\ncontent_blocked\nnew_code\n")

            code, output = _check(repo, baseline="1.0.0", version="1.0.1")

            self.assertEqual(1, code, output)
            self.assertIn("Line: frozen 1.x patch", output)
            self.assertIn("A patch release cannot grow", output)

    def test_yama_surumunde_http_ekleme_kirmizidir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            document = _document()
            _run_record(document)["properties"]["cost"] = {"type": "number"}
            _write(repo, "docs/openapi/tracon.json", json.dumps(document, indent=2))

            code, output = _check(repo, baseline="1.0.0", version="1.0.1")

            self.assertEqual(1, code, output)
            self.assertIn("HTTP", output)

    def test_minor_surumde_ekleme_yesildir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/error-codes.txt", "# header\ntool_timeout\ncontent_blocked\nnew_code\n")
            document = _document()
            _run_record(document)["properties"]["cost"] = {"type": "number"}
            _write(repo, "docs/openapi/tracon.json", json.dumps(document, indent=2))

            code, output = _check(repo, baseline="1.0.0", version="1.1.0")

            self.assertEqual(0, code, output)

    def test_donmus_tabanda_surumsuz_hat_belirlenemez(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            code, output = _quiet(lambda: sk.check(
                repo=repo, baseline="1.0.0", version=sk.bc.UNRELEASED, changelog=repo / "CHANGELOG.md"))

            self.assertEqual(1, code)
            self.assertIn("Cannot decide the release line", output)

    def test_cli_komut_satiri_manuel_case_3_icin_calisir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/sse-events.txt", "# header\nrun-events run.started\nagent-stream done\n")

            code, output = _quiet(lambda: sk.main(["--repository", str(repo), "--base", "1.0.0", "--package-version", "1.1.0"]))

            self.assertEqual(1, code)
            self.assertIn("Line: frozen 1.x", output)
            self.assertIn("SSE: 1 break", output)


# -----------------------------------------------------------------------------
# Tags from before the move: the snapshots sit under `docs/sozlesme/` there
# -----------------------------------------------------------------------------

LEGACY_PATHS = {
    "docs/contracts/sse-events.txt": "docs/sozlesme/sse-olaylari.txt",
    "docs/contracts/telemetry.txt": "docs/sozlesme/telemetri.txt",
    "docs/contracts/configuration.txt": "docs/sozlesme/yapilandirma.txt",
    "docs/contracts/error-codes.txt": "docs/sozlesme/hata-kodlari.txt",
    "docs/contracts/validation-codes.txt": "docs/sozlesme/dogrulama-kodlari.txt",
    "docs/contracts/cli.txt": "docs/sozlesme/cli.txt",
}


def _repo_with_old_paths(directory: str, *, tag: str = "v1.0.0-preview.8") -> pathlib.Path:
    """The move itself: the tag holds the snapshots under `docs/sozlesme/`, HEAD holds them under `docs/contracts/`."""
    repo = pathlib.Path(directory) / "repo"
    repo.mkdir()
    _git(repo, "init", "-q", "-b", "main")
    _write(repo, "docs/openapi/tracon.json", json.dumps(_document(), indent=2))
    for current, legacy in LEGACY_PATHS.items():
        _write(repo, legacy, SNAPSHOTS[current])
    _write(repo, "CHANGELOG.md", "# Changelog\n\n## [Unreleased]\n\n")
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "-m", "baseline")
    _git(repo, "tag", tag)

    for legacy in LEGACY_PATHS.values():
        (repo / legacy).unlink()
    for current in LEGACY_PATHS:
        _write(repo, current, SNAPSHOTS[current])
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "-m", "move the snapshots")
    return repo


def _outcome(repo: pathlib.Path, baseline: str) -> list[tuple]:
    return [
        (result.surface, [change.subject for change in result.breaks], [change.subject for change in result.additions], result.note)
        for result in sk.surface_results(repo, baseline)
    ]


class LegacyPathTests(unittest.TestCase):
    """A tag up to `v1.0.0-preview.8` holds the old paths; the gate must still compare against it."""

    def test_a_tag_with_the_old_paths_is_compared_and_not_reported_missing(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_old_paths(directory)

            results = sk.surface_results(repo, "1.0.0-preview.8")

            self.assertEqual(7, len(results))
            self.assertTrue(all(r.note is None and not r.breaks and not r.additions for r in results))

    def test_the_old_paths_give_the_same_delta_as_the_current_paths(self):
        with tempfile.TemporaryDirectory() as old_directory, tempfile.TemporaryDirectory() as new_directory:
            old = _repo_with_old_paths(old_directory)
            new = _repo_with_baseline(new_directory, tag="v1.0.0-preview.8")
            for repo in (old, new):
                _write(repo, "docs/contracts/error-codes.txt", "# header\ncontent_blocked\nnew_code\n")
                _write(repo, "docs/contracts/cli.txt", "# header\ncommand eval\n")

            from_old_tag = _outcome(old, "1.0.0-preview.8")
            from_current_tag = _outcome(new, "1.0.0-preview.8")

            self.assertEqual(from_current_tag, from_old_tag)
            error_codes = next(entry for entry in from_old_tag if entry[0] == "error code")
            self.assertEqual(1, len(error_codes[1]))
            self.assertEqual(1, len(error_codes[2]))
            self.assertEqual(1, len(next(entry for entry in from_old_tag if entry[0] == "CLI")[1]))

    def test_the_gate_reads_a_tag_with_the_old_paths_to_a_green_result(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_old_paths(directory)

            code, output = _check(repo, baseline="1.0.0-preview.8", version="1.0.0-preview.9")

            self.assertEqual(0, code, output)
            self.assertNotIn("no baseline", output)

    def test_the_current_path_wins_when_the_tag_holds_both(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0-preview.8")
            _write(repo, "docs/sozlesme/hata-kodlari.txt", "# header\nstale_code\n")
            _git(repo, "add", "-A")
            _git(repo, "commit", "-q", "-m", "an old copy that must stay unread")
            _git(repo, "tag", "-f", "v1.0.0-preview.8")

            error_codes = next(r for r in sk.surface_results(repo, "1.0.0-preview.8") if r.surface == "error code")

            self.assertEqual([], error_codes.breaks)
            self.assertEqual([], error_codes.additions)
            self.assertIsNone(error_codes.note)

    def test_the_note_names_both_paths_when_the_tag_holds_neither(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0-preview.5", snapshots=False)

            error_codes = next(r for r in sk.surface_results(repo, "1.0.0-preview.5") if r.surface == "error code")

            self.assertIn("no baseline", error_codes.note)
            self.assertIn("docs/contracts/error-codes.txt", error_codes.note)
            self.assertIn("docs/sozlesme/hata-kodlari.txt", error_codes.note)

    def test_a_frozen_baseline_with_neither_path_is_an_error_that_names_both(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0", snapshots=False)

            with self.assertRaises(sk.SurfaceError) as raised:
                sk.surface_results(repo, "1.0.0")

            self.assertIn("docs/contracts/sse-events.txt", str(raised.exception))
            self.assertIn("docs/sozlesme/sse-olaylari.txt", str(raised.exception))

    def test_every_surface_maps_to_one_current_and_one_old_path(self):
        self.assertEqual(
            sorted(LEGACY_PATHS.items()),
            sorted((surface.paths[0], surface.paths[1]) for surface in sk.NAME_SURFACES))


class OutputLanguageTests(unittest.TestCase):
    """Everything the gate prints is English: a contributor of the public repository reads it."""

    TURKISH_LETTERS = re.compile("[çğıöşüÇĞİÖŞÜ]")

    def assert_english(self, output: str) -> None:
        self.assertIsNone(self.TURKISH_LETTERS.search(output), output)

    def test_the_labels_of_the_name_sets_are_english(self):
        self.assertEqual(
            ["SSE", "telemetry", "configuration", "error code", "validation code", "CLI"],
            [surface.label for surface in sk.NAME_SURFACES])

    def test_a_red_frozen_run_prints_english_only(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/sse-events.txt", "# header\nrun-events run.started\nagent-stream done\n")
            document = _document()
            _run_record(document)["properties"].pop("agentName")
            _write(repo, "docs/openapi/tracon.json", json.dumps(document, indent=2))

            code, output = _check(repo, baseline="1.0.0", version="1.1.0")

            self.assertEqual(1, code, output)
            self.assertIn("line removed or changed", output)
            self.assert_english(output)

    def test_a_green_preview_run_with_no_baseline_notes_prints_english_only(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory, tag="v1.0.0-preview.5", snapshots=False)

            code, output = _check(repo, baseline="1.0.0-preview.5", version="1.0.0-preview.6")

            self.assertEqual(0, code, output)
            self.assertIn("Line: preview", output)
            self.assertIn("no baseline", output)
            self.assert_english(output)

    def test_a_red_patch_run_prints_english_only(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_baseline(directory)
            _write(repo, "docs/contracts/error-codes.txt", "# header\ntool_timeout\ncontent_blocked\nnew_code\n")

            code, output = _check(repo, baseline="1.0.0", version="1.0.1")

            self.assertEqual(1, code, output)
            self.assertIn("new line", output)
            self.assert_english(output)

    def test_the_help_text_is_english(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output), self.assertRaises(SystemExit):
            sk.main(["--help"])

        self.assertIn("--repository", output.getvalue())
        self.assertIn("--package-version", output.getvalue())
        self.assert_english(output.getvalue())

    def test_the_old_flag_names_are_gone(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as raised:
            sk.main(["--depo", ".", "--" "taban", "1.0.0"])

        self.assertEqual(2, raised.exception.code)


if __name__ == "__main__":
    unittest.main()
