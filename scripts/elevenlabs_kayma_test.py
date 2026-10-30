#!/usr/bin/env python3
"""`elevenlabs-kayma.py` testleri.

Betik canli belgeye karsi agdan koşar ve CI'da kosmaz; mantigi (`$ref` cozumu, birlesim
duzlestirme, alan yolu ve tip karsilastirmasi) ise yalnizca elle bir negatif kontrolle
(manuel kabul adimi) sinaniyordu. Burada kucuk bir OpenAPI parcasi uzerinde sinanir.
"""
from __future__ import annotations

import copy
import importlib.util
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("elevenlabs_kayma", ROOT / "scripts" / "elevenlabs-kayma.py")
kayma = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kayma
spec.loader.exec_module(kayma)


BELGE = {
    "openapi": "3.1.0",
    "paths": {
        "/v1/speak": {
            "parameters": [{"name": "output_format", "in": "query"}],
            "post": {
                "requestBody": {"content": {"application/json": {"schema": {"$ref": "#/components/schemas/Body"}}}},
                "responses": {"200": {"content": {"application/json": {"schema": {"$ref": "#/components/schemas/Reply"}}}}},
            },
        },
        "/v1/listen": {
            "post": {
                "parameters": [{"$ref": "#/components/parameters/Page"}],
                "requestBody": {"content": {"multipart/form-data": {"schema": {"properties": {"file": {"type": "string"}}}}}},
                "responses": {"200": {"content": {"application/json": {"schema": {"$ref": "#/components/schemas/Reply"}}}}},
            },
        },
    },
    "components": {
        "parameters": {"Page": {"name": "page_token", "in": "query"}},
        "schemas": {
            "Body": {
                "type": "object",
                "properties": {
                    "text": {"type": "string"},
                    "seed": {"anyOf": [{"type": "integer"}, {"type": "null"}]},
                    "ids": {"type": "array", "items": {"type": "string"}},
                    "settings": {"$ref": "#/components/schemas/Settings"},
                },
            },
            "Settings": {"type": "object", "properties": {"speed": {"type": "number"}, "boost": {"type": ["boolean", "null"]}}},
            "Reply": {
                "type": "object",
                "properties": {
                    "items": {"type": "array", "items": {"$ref": "#/components/schemas/Item"}},
                    "total": {"type": "integer"},
                    "mystery": {},
                },
            },
            "Item": {"properties": {"id": {"type": "string"}}},
        },
    },
}


def beyan(**degisiklik):
    """Belgeyle uyumlu bir beyan; `degisiklik` ilk operasyonun alanlarini ezer."""
    speak = {
        "method": "POST",
        "path": "/v1/speak",
        "query": ["output_format"],
        "request": ["text", "seed", "ids[]", "settings", "settings.speed", "settings.boost"],
        "response": ["items[]", "items[].id", "total", "mystery"],
        "types": {
            "request": {
                "text": "string", "seed": "integer", "ids[]": "array",
                "settings": "object", "settings.speed": "number", "settings.boost": "boolean",
            },
            "response": {"items[]": "array", "items[].id": "string", "total": "integer", "mystery": "string"},
        },
    }
    speak.update(copy.deepcopy(degisiklik))
    return {"operations": [speak]}


class AlanYoluTestleri(unittest.TestCase):
    def test_uyumlu_beyan_sorunsuzdur(self):
        self.assertEqual(kayma.check(BELGE, beyan()), [])

    def test_olmayan_operasyon_raporlanir(self):
        sorunlar = kayma.check(BELGE, beyan(path="/v1/yok"))
        self.assertEqual(sorunlar, ["POST /v1/yok: the operation is gone"])

    def test_kaybolan_sorgu_parametresi_raporlanir(self):
        sorunlar = kayma.check(BELGE, beyan(query=["output_format", "gone"]))
        self.assertEqual(sorunlar, ["POST /v1/speak: query parameter 'gone' is gone"])

    def test_operasyon_duzeyindeki_ve_ref_ile_gelen_sorgu_parametresi_bulunur(self):
        listen = {"method": "POST", "path": "/v1/listen", "query": ["page_token"], "request": ["file"], "response": ["total"]}
        self.assertEqual(kayma.check(BELGE, {"operations": [listen]}), [])

    def test_kaybolan_alan_raporlanir_ic_ice_olan_dahil(self):
        sorunlar = kayma.check(BELGE, beyan(request=["text", "settings.gone", "items[].gone"], types={}))
        self.assertEqual(sorunlar, [
            "POST /v1/speak: request field 'settings.gone' is gone",
            "POST /v1/speak: request field 'items[].gone' is gone",
        ])

    def test_dizi_ogesinin_alani_birlesim_ve_ref_icinden_bulunur(self):
        liste = kayma.alternatives(BELGE, BELGE["components"]["schemas"]["Reply"]["properties"]["items"])
        self.assertTrue(any(secenek.get("type") == "array" for secenek in liste))
        self.assertTrue(kayma.has_path(BELGE, BELGE["components"]["schemas"]["Reply"], ["items[]", "id"]))
        self.assertFalse(kayma.has_path(BELGE, BELGE["components"]["schemas"]["Reply"], ["items", "id"]))

    def test_govdesi_olmayan_taraf_alan_beklerse_raporlanir(self):
        belge = copy.deepcopy(BELGE)
        del belge["paths"]["/v1/speak"]["post"]["requestBody"]
        sorunlar = kayma.check(belge, beyan(types={}))
        self.assertEqual(sorunlar, ["POST /v1/speak: has no request body schema but Tracon relies on 6 field(s)"])

    def test_ref_dongusu_sonsuza_gitmez(self):
        belge = {"a": {"$ref": "#/a"}}
        with self.assertRaises(ValueError):
            kayma.resolve(belge, belge["a"])


class TipTestleri(unittest.TestCase):
    def test_tip_degisimi_adi_ayni_olsa_da_raporlanir(self):
        belge = copy.deepcopy(BELGE)
        belge["components"]["schemas"]["Body"]["properties"]["text"] = {"type": "integer"}
        sorunlar = kayma.check(belge, beyan())
        self.assertEqual(sorunlar, ["POST /v1/speak: request field 'text' is integer but Tracon declares string"])

    def test_null_ile_birlesen_tip_tipi_bozmaz(self):
        # `seed`: anyOf [integer, null] ve `settings.boost`: ["boolean", "null"].
        self.assertEqual(kayma.find_types(BELGE, BELGE["components"]["schemas"]["Body"], ["seed"]), {"integer"})
        self.assertEqual(
            kayma.find_types(BELGE, BELGE["components"]["schemas"]["Body"], ["settings", "boost"]), {"boolean"})

    def test_tam_sayi_beyani_number_alana_uyar_number_beyani_integer_alana_uymaz(self):
        belge = copy.deepcopy(BELGE)
        belge["components"]["schemas"]["Body"]["properties"]["seed"] = {"type": "number"}
        self.assertEqual(kayma.check(belge, beyan()), [])

        belge["components"]["schemas"]["Settings"]["properties"]["speed"] = {"type": "integer"}
        self.assertEqual(
            kayma.check(belge, beyan()),
            ["POST /v1/speak: request field 'settings.speed' is integer but Tracon declares number"])

    def test_dizi_ve_nesne_tipleri_ayristirilir(self):
        belge = copy.deepcopy(BELGE)
        belge["components"]["schemas"]["Body"]["properties"]["ids"] = {"type": "string"}
        belge["components"]["schemas"]["Body"]["properties"]["settings"] = {"type": "array", "items": {"type": "string"}}
        sorunlar = kayma.check(belge, beyan(request=["ids[]", "settings"], types={"request": {"ids[]": "array", "settings": "object"}}))
        self.assertEqual(sorunlar, [
            "POST /v1/speak: request field 'ids[]' is string but Tracon declares array",
            "POST /v1/speak: request field 'settings' is array but Tracon declares object",
        ])

    def test_belgenin_tip_vermedigi_alan_her_beyana_uyar(self):
        # `mystery: {}`: sema tipsiz. Karsilastirilacak bir sey yok; alan varsa yeter.
        self.assertEqual(kayma.find_types(BELGE, BELGE["components"]["schemas"]["Reply"], ["mystery"]), set())
        self.assertTrue(kayma.type_matches("string", set()))

    def test_tipi_beyan_edilmeyen_alan_yalniz_varlik_icin_denetlenir(self):
        # Multipart form alanlari tip beyan etmez (tel uzerinde hepsi metindir).
        listen = {"method": "POST", "path": "/v1/listen", "query": [], "request": ["file"], "response": [], "types": {"request": {}, "response": {}}}
        self.assertEqual(kayma.check(BELGE, {"operations": [listen]}), [])

    def test_bildirim_dosyasindaki_her_json_alani_tip_tasir(self):
        # Gercek beyan: forma ozgu istek (speech-to-text) disinda her alanin tipi yazili.
        import json
        beyan_dosyasi = json.loads(kayma.FIXTURE.read_text(encoding="utf-8"))
        for operasyon in beyan_dosyasi["operations"]:
            etiket = f"{operasyon['method']} {operasyon['path']}"
            for taraf in ("request", "response"):
                tipler = operasyon["types"][taraf]
                if operasyon["path"] == "/v1/speech-to-text" and taraf == "request":
                    self.assertEqual(tipler, {}, f"{etiket}: bir form tip beyan etmez")
                    continue
                self.assertEqual(sorted(tipler), sorted(operasyon[taraf]), f"{etiket}: {taraf}")
                self.assertTrue(set(tipler.values()) <= {"string", "integer", "number", "boolean", "array", "object"})


if __name__ == "__main__":
    unittest.main()
