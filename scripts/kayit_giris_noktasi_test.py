#!/usr/bin/env python3
"""Kayıt giriş noktası kuralının (K-509) testleri."""
from __future__ import annotations

import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import kayit_giris_noktasi as kural  # noqa: E402

# Tek fixture: iki betik de bu modülü kullandığı için kuralın eşliği burada
# kanıtlanır. Her satırın beklenen sonucu yanındadır.
SATIRLAR = [
    ("Tracon.ITraconBuilder.Services.get -> Microsoft.Extensions.DependencyInjection.IServiceCollection!", "Services"),
    ("static Tracon.TraconBuilderExtensions.Configure(this Tracon.ITraconBuilder! builder, System.Action<Tracon.TraconOptions!>! configure) -> Tracon.ITraconBuilder!", "Configure"),
    ("static Tracon.TraconBuilderExtensions.RequireCustomBinding<T>(this Tracon.ITraconBuilder! builder) -> Tracon.ITraconBuilder!", "RequireCustomBinding"),
    ("static Tracon.TraconBuilderExtensions.RequireProductionProfile(this Tracon.ITraconBuilder! builder, System.Action<Tracon.TraconProductionProfileOptions!>? configure = null) -> Tracon.ITraconBuilder!", "RequireProductionProfile"),
    ("static Tracon.PostgreSqlBuilderExtensions.UsePostgreSql(this Tracon.ITraconBuilder! builder) -> Tracon.ITraconBuilder!", "UsePostgreSql"),
    ("static Tracon.TraconServiceCollectionExtensions.AddTracon(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services) -> Tracon.ITraconBuilder!", "AddTracon"),
    ("~static Tracon.TraconEndpointRouteBuilderExtensions.MapTracon(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder! endpoints) -> void", "MapTracon"),
    # Önek dışı ad, builder dışı alıcıda giriş noktası DEĞİLDİR.
    ("static Tracon.ServiceCollectionHelpers.Configure(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services) -> void", None),
    # Kayıt tipi olmayan alıcı.
    ("static Tracon.StringExtensions.AddSuffix(this string! value) -> string!", None),
    # Uzantı olmayan statik metot.
    ("static Tracon.Helper.AddNumbers(int a, int b) -> int", None),
    ("Tracon.TraconBuilderExtensions", None),
]


class KuralTestleri(unittest.TestCase):
    def test_her_fixture_satiri_beklenen_sonucu_verir(self):
        for satir, beklenen in SATIRLAR:
            with self.subTest(satir=satir):
                self.assertEqual(kural.kayit_giris_noktasi(satir), beklenen)

    def test_builder_alicisinda_ad_onekten_bagimsizdir(self):
        self.assertTrue(kural.kayit_uzantisi_mi("Configure", "Tracon.ITraconBuilder"))
        self.assertFalse(kural.kayit_uzantisi_mi(
            "Configure", "Microsoft.Extensions.DependencyInjection.IServiceCollection"))


if __name__ == "__main__":
    unittest.main()
