# Ek — Yeni paket eklendiyse

> `faz-tamamlama` Adım 1'den açılır. Tetik: faz yeni bir `src/<Paket>/`
> projesi ekledi.

`dotnet pack` çıktısını say: paket sayısı beklenenle uyuşmalıdır. Yeni paket
ayrıca şunları ister — atlanırsa build veya test kırar:

- `src/<Paket>/README.md` (NuGet sayfasında görünür; `DependencyDirectionTests` zorlar)
- `Tracon.slnx` içine `<Project Path=... />`
- Meta pakete (`src/Tracon/Tracon.csproj`) `ProjectReference`
- `DependencyDirectionTests.AllowedReferences` içine bir satır
- Library paketiyse csproj'a `<TraconPackageFirstRelease>true</TraconPackageFirstRelease>`:
  nuget.org'da tabanı yoktur ve yayın provası onu restore edemez. **İlk yayından
  sonra kaldır** — paket son `v*` etiketinde varsa `kapi.py yayin` bayrağı bayat
  sayar ve kırmızı verir (Faz 187)

**Kapı bu listeyi yakalar, ama kapanışın SONUNDA** (Faz 236: yeni `Tracon.AGUI` paketi tam `kapanis` koşumunda dokuz teste
çarptı ve ilk koşum 11 dakika sonra düştü). Paketi eklediğin gün şunları da yap:

- `src/Tracon.Core/Diagnostics/PackageFamilyAlignment.cs` içindeki aile listesine assembly adını ekle (karışık sürüm
  grafiği başlangıçta yakalansın); test: `PackageFamilyAlignmentTests`
- `TRACON_PUBLIC_SURFACE_REFRESH=1 ./artifacts/bin/Tracon.Core.UnitTests/release_net10.0/Tracon.Core.UnitTests --filter-class "*PublicSurfaceBaselineTests*"`
  ile `public-surface-baseline.txt`'e satır yaz (önce `dotnet build tests/Tracon.Core.UnitTests`; `kapi.py test` derlemez)
- Pakette `<example>` taşıyan bir giriş noktası varsa `tests/Tracon.Generators.UnitTests/*.csproj`'a `ProjectReference`
  (`ExampleCompilationTests` her bloğu o projenin referans kümesine karşı derler)
- `tests/Tracon.Package.Tests/PackageBaselineWiringTests.cs` kitaplık paketi sayısı ve açıklamadaki paket sayısı
- `scripts/breaking_changes_test.py` `NEW_SINCE_LAST_RELEASE`: bayrak yalnız yayınlanmamış pakette durur; ilk yayından sonra
  hem csproj'dan hem bu listeden çıkar
- `scripts/kapi.py`: `TEST_PROJECTS` haritası (paket → test projesi) ve lisans matrisi
- Paket bir mevcut paketin iç tur kurulumunu paylaşıyorsa (`InternalsVisibleTo`), paylaşılan çağrıyı kapı sayan
  `RunAuthorizationCoverageTests` listelerine **çağıranı** ekle
- Site: `index.mdx`, `packages.md`, `reference/compatibility.md`, `reference/versioning.md` ve kök `README.md` paket sayısı;
  kenar çubuğuna sayfa girerse **her sayfaya ~10 B** gzip biner ve `check-weight` en ağır sayfada (K-756) taşabilir —
  tavanı yükseltme, sayfayı kısalt ya da böl

