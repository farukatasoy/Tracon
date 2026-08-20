# Konu 7 — Bağımlılık ve Tedarik Zinciri

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`Directory.Packages.props`, her `src/**/*.csproj`, `frontend/package.json`.

## Bilinen tasarım

4 bilinçli CVE-pin: `Microsoft.Bcl.Memory` (GHSA-73j8-2gch-69rq),
`SQLitePCLRaw.lib.e_sqlite3` (GHSA-2m69-gcr7-jv3q), `Microsoft.OpenApi`
(GHSA-v5pm-xwqc-g5wc), `SSH.NET` (CVE-2026-48798, test bağımlılığı).
`Microsoft.Agents.AI.Hosting*` preview/alpha paketleri yalnız
`AgentPrism.AspNetCore`'a hapsedilmiştir (K-008).

## Ara

- 4 CVE-pin'in HÂLÂ gerekli olup olmadığını — üst sürüm çıkmış mı, çıktıysa
  pin kaldırılabilir mi (yalnız tespit et, güncelleme yapma).
- Preview/alpha MAF paketlerinin başka bir pakete (K-008'i ihlal ederek)
  sızıp sızmadığını.
- `frontend/package.json`'daki bağımlılıklarda bilinen kritik/yüksek CVE
  olup olmadığını (npm advisory veritabanı bilgin varsa, yoksa yalnız
  şüpheli/eski sürümleri işaretle).
