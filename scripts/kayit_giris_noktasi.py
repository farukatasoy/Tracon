"""Kayıt giriş noktası kuralı (K-509) — iki betiğin ortak yüklemi.

Tüketicinin bir yeteneği açtığı public üye bir "kayıt giriş noktası"dır:

- alıcısı `Tracon.ITraconBuilder` olan HER uzantı metodu, adından bağımsız
  (`Configure`, `RequireCustomBinding`, `RequireProductionProfile` önek taşımaz
  ama zincirin yalnız kayıt için vardır — Faz 189);
- alıcısı diğer kayıt tiplerinden biri olan `Add`/`Use`/`Map` önekli uzantı;
- builder arayüzünün kendi üyesi (`Tracon.ITraconBuilder.Services`).

C# kopyası `tests/Tracon.Core.UnitTests/Architecture/CapabilityEntryPoints.cs`
içindedir ve bu modülü import edemez; eşliği iki taraftaki "üç ad" testleri
tutar. Kullanan betikler: `manuel-test-tazelik.py`, `public-yuzey-envanteri.py`.
"""
from __future__ import annotations

import re

BUILDER_ALICISI = "Tracon.ITraconBuilder"

KAYIT_ALICILARI = frozenset({
    BUILDER_ALICISI,
    "Microsoft.Extensions.DependencyInjection.IServiceCollection",
    "Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder",
    "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
    "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
})

KAYIT_ONEKI = re.compile(r"^(?:Add|Use|Map)")

BUILDER_UYESI = re.compile(r"^Tracon\.ITraconBuilder\.(?P<ad>[A-Za-z0-9_]+)")

# Unshipped satırındaki bir uzantı metodu. Baştaki değiştiriciler (`static`,
# `~` nullable-oblivious işareti) atlanır; ad ve alıcı yakalanır.
UZANTI_METODU = re.compile(
    r"^~?(?:[a-z]+ )*[A-Za-z0-9_.<>]+?\.(?P<ad>[A-Za-z_][A-Za-z0-9_]*)"
    r"(?:<[^(]*>)?\(this (?P<alici>[A-Za-z0-9_.]+)")


def kayit_uzantisi_mi(ad: str, alici: str) -> bool:
    """`alici` tipine yazılmış `ad` uzantısı bir kayıt giriş noktası mı?"""
    if alici == BUILDER_ALICISI:
        return True
    return alici in KAYIT_ALICILARI and KAYIT_ONEKI.match(ad) is not None


def kayit_giris_noktasi(satir: str) -> str | None:
    """Bir `PublicAPI.*.txt` satırı kayıt giriş noktasıysa üyenin adı, değilse `None`."""
    uye = BUILDER_UYESI.match(satir)
    if uye:
        return uye.group("ad")
    uzanti = UZANTI_METODU.match(satir)
    if uzanti and kayit_uzantisi_mi(uzanti.group("ad"), uzanti.group("alici")):
        return uzanti.group("ad")
    return None
