# Paketleme ve Tuketiciye Teslim Tuzaklari

> `dotnet pack`, `.nuspec`, `buildTransitive/`, sablon paketi ve tuketicinin
> agacina yazilan uretilmis dosyalar.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken
> okunur. Sinir: **nasil derlenir/analiz edilir** sorusu
> [`build-ve-analyzer.md`](build-ve-analyzer.md)'dedir; **nasil paketlenir ve
> tuketiciye nasil ulasir** sorusu buradadir. Analyzer YAZIMI ucuncu bir
> dosyadadir: [`analyzer-yazimi.md`](analyzer-yazimi.md). NSwag ile uretilen
> istemcinin (`Tracon.Client`/`@tracon/client`) kendi tuzaklari
> dorduncu bir dosyadadir: [`nswag-istemci-uretimi.md`](nswag-istemci-uretimi.md).
> MinVer surumleme ve repo disi tuketiciyi yerel feed'e baglama BESINCI bir
> dosyadadir: [`yayin-ve-surumleme.md`](yayin-ve-surumleme.md) (Faz 156)
> (2026-09-03, Faz 136 denetimi — dosya bütçesini asti, ayrildi).

## `IncludeBuildOutput=false` paketleri (Templates, meta) — pack tuzaklari

> Faz 37'de `Tracon.Templates` (dotnet new sablonu) paketlenirken kesfedildi.
> Meta paket (`Tracon.csproj`) de `IncludeBuildOutput=false` oldugu icin
> ayni sinifta risk tasir. K-262/K-263/K-264.
>
> Bu bolumun uzun tanilama anlatilari (nasil izole edildi, hangi olcum yapildi)
> [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md)'ye tasindi
> (2026-08-15, Faz 58.0). Asagida yalnizca **kural** durur.

- **🚨 Bos sembol paketi `NU5017` ile ANA paketi de basarisiz gosterir**
  (Faz 37): `IncludeBuildOutput=false` olan projede `.pdb` yoktur, `.snupkg`
  bos kalir ve pack reddeder. Hata mesaji hangi paketten geldigini SOYLEMEZ.
  Cozum: `<IncludeSymbols>false</IncludeSymbols>`.
- **🚨 `TargetFrameworks` (cogul) miras kalirsa `dotnet pack` sessizce
  capraz-hedefler** (Faz 37): tekil `TargetFramework` yazmak `dotnet build`i
  tek TFM'e indirger ama pack orkestrasyonu COGUL degeri okumaya devam eder.
  Cozum: `<TargetFrameworks></TargetFrameworks>` ile bosalt (derlenmeyen paket)
  veya `<TargetFrameworks>net10.0</TargetFrameworks>` ile tek degere sabitle —
  **tekil ozelligi kullanma**. `Tracon.Testing` bu ikinci hali kullanir:
  `Microsoft.AspNetCore.TestHost` surumu barindirma framework'uyle BIREBIR
  eslenir, tek surum coklu TFM'i desteklemez (`NU1202`, Faz 39).
- **`<None Include Pack="true" PackagePath="...">` ile keyfi dosya paketleme**
  (Faz 37): resmi desen budur. `PackagePath`'i acikca
  `content/%(RecursiveDir)%(Filename)%(Extension)` ile yaz ve
  `ContentTargetFolders`'i HIC kullanma — ikisi birlikte `content/content/...`
  cift onegi uretir.
- **🚨 `dotnet pack <cozum>` cozumdeki HER projeyi (test projeleri dahil)
  restore+build eder** (Faz 39): paketlenemeyen projede Pack no-op'tur ama Build
  ONA BAGIMLI oldugu icin yine calisir. Olculdu: `TemplateFixture` bu yuzden
  ~1 saat suruyordu. Cozum: `Tracon.src.slnf` cozum filtresi (yalniz `src/`);
  warm pack ~30 sn.
- **🚨 `wwwroot` + damga birlikte silinip SOLUTION derlenirse arayuz derlemesi
  YINE yarisir** (Faz 48; K-050'nin kapatmadigi bosluk). Belirti:
  `ENOENT: ... unlink '.../wwwroot/assets/index-*.js'`. **Cozum: damga
  silindikten sonra ONCE tek basina
  `dotnet build src/Tracon.UI/Tracon.UI.csproj -c Release` calistir,
  sonra solution'i derle.** Damga guncelken yaris hic olusmaz — normal artimli
  derlemede gorulmez, yalniz kopya dosya temizliginden sonra gorulur.

## Tüketiciye giden MSBuild (Faz 73)

> Analyzer YAZIMI ayrı bir alan dosyasındadır:
> [`analyzer-yazimi.md`](analyzer-yazimi.md).

- **🚨 `<None Update=...>` çapraz-hedefli projede SESSİZCE hiçbir şey yapmaz** (2026-08-19): SDK'nın varsayılan `None` glob'u yalnız **iç** (TFM'e özgü) derlemelerde uygulanır; `dotnet pack` paket dosyalarını **dış** çapraz-hedefleme derlemesinde toplar. Ölçüldü: dışarıda 2, içeride 7 `None` öğesi. `Update` eşleşecek bir öğe bulamaz, dosya pakete girmez ve **hiçbir uyarı çıkmaz**. Doğrusu `<None Remove="dizin/**" />` + `<None Include=... Pack="true" PackagePath="..." />` — `Remove` iç derlemelerdeki glob kopyasını düşürür, `Include` her iki derlemede de görünür. `README.md`'nin `Include` ile paketlenmesinin sebebi de budur. Doğrulama tek komuttur: `unzip -l <nupkg>`.
- **Git kökünü MSBuild'de bulmak**: `$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '.git/config'))` normal klonu bulur; worktree ve submodule'de `.git` bir DOSYA olduğu için ikinci bir deneme (`'.git'`) gerekir. İkisi de boşsa proje dizinine düş — tüketicinin build'ini kırma.
- **Tüketicinin ağacına dosya yazan `Copy` `ContinueOnError` taşımalıdır**: salt-okunur depo kökü (yaygın CI mount'u) aksi hâlde `MSB3021` ile build'i düşürür. Kolaylık amaçlı bir dosya, tüketicinin derlemesini kıramaz.

- **🚨 `ProjectReference` nuspec'e ALT SINIR olarak paketlenir** (Faz 185, K-858): `version="x"` = `>= x`; tek paketi yükselten tüketicinin karışık grafı sıfır uyarıyla restore olur. Aralığı besleyen public özellik yoktur — SDK `_GetProjectReferenceVersions` hedefinde SDK-private `_ProjectReferencesWithVersions` öğesinin `ProjectVersion` metadata'sını okur (`NuGet.Build.Tasks.Pack.targets`). `TraconPinSiblingDependencies` (`BeforeTargets="GenerateNuspec"`; `DependsOnTargets` önce koşar, öğe doludur) metadata'yı `[x]` yapar; `PackTask` `[x]` yazar (`[x, x]` değil), `exclude` ve üç TFM grubu korunur. Hedef içinde öğe metadata'sı `<Öğe Condition="...%(Öğe.M)..."><M>...</M></Öğe>` ile güncellenir. Öğe adı değişirse hedef sessizce hiçbir şey yapmaz; yalnız `ReleaseArtifactTests.EverySiblingDependencyIsExactAndMatchesOwnVersion` yakalar.
- **MSBuild koşul ayrıştırıcısı property fonksiyonunun tırnaklı argümanındaki `(`'i parantez sayar** (Faz 185): `StartsWith('(')` → `MSB4109 ... Did you forget the closing parenthesis?`. Argümandan parantezi çıkar.

## Tuketiciye yazilan uretilmis dosya (Faz 74)

- **`%(ReferencePath.NuGetPackageId)` paket referansini `ProjectReference`'tan ayirir** (2026-08-19, olculdu): paket referansinda `NuGetPackageId` + `NuGetPackageVersion` doludur, `ProjectReference`'ta **bostur**. Kardes XML dokumani `%(RootDir)%(Directory)%(Filename).xml` ile bulunur — `ChangeExtension` cagirmaya gerek yok. Bu depo kendi paketlerini `ProjectReference` ile kullandigi icin ayni hedef burada **hicbir sey yazmaz**; tuketiciye giden bir target'i bu depoda dogrulayamazsin, `Tracon.Package.Tests` gerekir.
- **🚨 MSBuild `Include` degerinin BASINDAKI bosluk kirpilir** (2026-08-19, olculdu): `Include="    grep ..."` dosyaya girintisiz yazilir, yani girintili markdown kod blogu **uretilemez**. Cozum: uc ters tirnakli citli blok. Sondaki `%0A` bir satirdan sonra bos satir uretir ve korunur.
- **Ogenin donusumu (`@(X->'...')`) `;` uzerinden BOLUNMEZ, ama OZELLIK enterpolasyonu boler** (2026-08-19, olculdu): donusum kaynak oge basina tam bir cikti ogesi uretir, icinde noktali virgul olsa bile (`a;b.dll` tek oge kaldi) — `$([MSBuild]::Escape(...))` gereksizdir. Buna karsilik `Include="... $(Prop) ..."` icindeki `;` satiri IKIYE boler; olculdu: `->Distinct()` iki farkli paket surumunu `;` ile birlestirince `Installed version:` satiri ikiye ayrildi. **Donusum ifadesi icinde string fonksiyonu YAZILAMAZ** — donusumun ayraci da tek tirnaktir ve ifadeyi erken kapatir; hesaplanmis degeri oge ustverisinden (`%(NuGetPackageId)`) al.
- **`buildTransitive/` META PAKET uzerinden de akar** (2026-08-19, olculdu): uretilen `.nuspec` bagimliliklari `exclude="Build,Analyzers"` tasisa bile hem `Tracon.Core.targets` hem `Tracon.AspNetCore.targets` tuketiciye ulasti. `build/` bu sicramada durur; `buildTransitive/` durmaz.
- **NuGet yalniz `buildTransitive/<PackageId>.props` ve `.targets` dosyasini KENDILIGINDEN import eder.** Baska adla paketlenen bir `.targets` sorunsuz paketlenir ve **hicbir uyari vermeden** hic yuklenmez. Iki paketin `.targets`'i arasinda import sirasi onemsizdir — birinin yazdigi ozelligi digeri **target govdesinde** (calisma aninda) okuyorsa.
- **XML dokuman kimliginde metot jenerigi CIFT ters tirnak tasir**: `AddContentGuard``1`, `AddWorkflowFunction``2`. Tip jenerigi tek ters tirnaktir. Naif ad eslestirmesi 39 giris noktasinin dordunu sessizce atladi (olculdu, Faz 74).
- **`docs/openapi/tracon.json` `Tracon.AspNetCore` paketine KAYNAGINDAN girer**: `<None Include="../../docs/openapi/tracon.json" Pack="true" PackagePath="buildTransitive/" />`. Olculen maliyet: 1 034 455 → 1 100 931 bayt (**+%6,4**). Kopya uretilmedigi icin sapma yuzeyi yok; belgeyi calisan host'a `OpenApiSnapshotTests` bagliyor.
- **🚨 Tuketicinin agacina yazilan uretilmis dosya PROJE basina yazilir, depo koküne degil** (2026-08-20, Faz 74 denetim bulgusu 3, olculdu): bir cozumdeki iki proje FARKLI paket kumesi referanslar (`src/Web` meta paket, `src/Worker` yalniz `Tracon.Core`) ve tek paylasilan dosya iki cevabi birden tasiyamaz — son derlenen proje kazanir, Web HTTP belgesini KAYBEDER, icerik her derlemede degisir. Birlestirme (merge) COZMEZ: projeler PARALEL derlenir ve okuma-yazma yarisir. `$(MSBuildProjectDirectory)` yapisal olarak dogrudur. `AGENTS.md` istisnadir cunku HIC ezilmez ve icerigi projeye gore degismez.
- **Proje dizinine yazan bir hedefi "salt-okunur dizin" ile test etme**: proje dizini `bin/`/`obj/` icin zaten yazilabilir olmali; salt-okunur yapilinca DERLEMENIN KENDISI `MSB3021` ile kirilir ve test urunu degil kendini olcer. Tasinabilir bicim: dosyanin yerine bir **dizin** koy. Gercek CI sekli (`UseArtifactsOutput` ile cikti baska yere, kaynak agaci salt-okunur) elle dogrulanir: `warning MSB3491`, `exit=0`.

## CLI komut ekleme (Faz 156)

- **Bir CLI komutu eklerken beş yüzey birlikte değişir** (2026-09-08): `Program.cs`
  yönlendirmesi, `Program.PrintHelp` metni, `src/Tracon.Cli/README.md` (pakete
  `Include` ile girer), `Tracon.Cli.csproj`'un `<Description>`'ı (nuget.org'da
  görünen metin) ve `docs-site/guides/cli.md`. Beşinden birini atlamak derlemeyi
  kırmaz; yalnız `MT-CLI-011` ("kaç komut listeleniyor") ile yakalanır.
- **`SqlProviderSelector.BuildProvider` `AddTracon()` + `Use*` koşar, yani
  content protector NO-OP'tur** (2026-09-08): CLI hiçbir zaman
  `AddContentProtection(...)` çağırmaz. Veritabanındaki şifreli sütun bu yüzden
  CLI'ya **zarfıyla** gelir. Bir CLI komutu korumalı bir sütunu okuyacaksa bunu
  hesaba katmalıdır — `ProtectedValue.Read` sessizce ham zarfı döndürür ve zarf
  geçerli JSON'dur (`$apEnc`). Vaka: K-735.


## Lisans dosyası paketleme (Faz 160)

🚨 **`Directory.Build.props` içindeki bir `ItemGroup`, hiçbir `csproj` gövdesini
görmez.** MSBuild değerlendirme sırası dosya sırasıdır ve `Directory.Build.props`
`csproj`'un **başında** import edilir. Yani:

```xml
<!-- src/Directory.Build.props -->
<None Include="$(RepositoryRootPath)$(PackageLicenseFile)" Pack="true" ... />
```

satırındaki `$(PackageLicenseFile)`, o an props içinde ne ise odur. Bir `csproj`
sonradan `<PackageLicenseFile>LICENSE-MIT.md</PackageLicenseFile>` yazarsa
**`.nuspec` doğru, paketlenen dosya yanlış** olur — ikisi sessizce ayrışır ve
`unzip -l` dışında hiçbir şey bunu gösterir.

Çözüm: paket bazlı farkı `MSBuildProjectName` ile **props içinde**, `ItemGroup`'un
üstünde çöz. `src/Directory.Build.targets` açmak cazip görünür ama köktekini <!-- yol:ornek -->
sessizce devre dışı bırakır (MSBuild yalnızca EN YAKIN dosyayı import eder).

**`requireLicenseAcceptance` `false` iken `.nuspec`'e hiç yazılmaz.** NuGet yalnız
`true` değerini yazar. Bir kapı bu elementi "false" metniyle aramamalı; yokluğunu
kontrol etmeli. Ölçüldü: `Tracon.Abstractions` (MIT) nuspec'inde element yok,
`Tracon.Core` (PolyForm) nuspec'inde `true` var.

**PolyForm OSI onaylı değildir**, bu yüzden `<PackageLicenseExpression>` kabul
etmez — NuGet orada yalnız kendi izin listesini alır. `<PackageLicenseFile>`
kullanılır, ve bunun yan faydası lisansın bir URL'ye bağlı olmamasıdır (K-659).

🚨 **Lisans iddiası taşıyan yüzey yalnız `.nuspec` değildir.** Sevk edilen README
de lisans söyler ve nuget.org ile npmjs.com **onu render eder**. Faz 160'ın
denetimi tam olarak burada bir 🔴 buldu: `packages/tracon-client/README.md`,
`package.json` PolyForm'a geçtikten sonra `License: MIT` demeye devam etti. Bir
lisans değişikliğinde taranacak küme `src/*/README.md` **ve** `packages/*/README.md`
ve `package.json` ve `.nuspec`'tir; biri unutulursa tüketici çelişkiyi görür ve
kendisine daha geniş hak veren metne inanır. Kapı: `MT-PKG-122`.

Lisans dosyaları her pakette sevk edildiği için `SourceLanguageTests` ve
`ShippedDocumentationSelfContainmentTests` kapsamındadır. Matris üç yerde
yazılıdır — `src/Directory.Build.props`, `scripts/kapi.py` (`PACKAGE_LICENSES`) ve
npm `package.json` — üçünü `PackageLicenseTests` birbirine kilitler; npm beklentisi
sabit dizge değil, `Tracon.Client`'ın tablodaki lisansından **türetilir**.

## Kararlı sürüm pack'i ve ön sürüm bağımlılığı (`NU5104`, 2026-09-23)

- **🚨 Kararlı bir sürüm `Tracon.AspNetCore`'da beş `NU5104` hatasıyla durur.**
  Ölçüm: `dotnet pack src/Tracon.AspNetCore -p:MinVerVersionOverride=1.0.0` →
  çıkış 1; `error NU5104: Warning As Error: A stable release of a package should
  not have a prerelease dependency` beş kez: `A2A.AspNetCore`,
  `Microsoft.Agents.AI.Hosting`, `.Hosting.A2A`, `.Hosting.AspNetCore`
  (preview) ve `.Hosting.OpenAI` (alpha). `TreatWarningsAsErrors` uyarıyı
  hataya çevirir. Bu K-602/K-008'in bilinen GA koşuludur: önce `Hosting*` ve
  `A2A.AspNetCore` GA olmalıdır. Kilit kasıtlıdır; `NoWarn` ile açma.
- **`NU5104` yalnız DOĞRUDAN bağımlılığa bakar.** Aynı override ile
  `Tracon.UI`, `Tracon.Testing` ve `Tracon` (meta) çıkış 0 verir. Üçünün
  nuspec'lerinde doğrudan ön sürüm bağımlılığı yoktur; ön sürüm paketler
  `Tracon.AspNetCore 1.0.0` üzerinden geçişli gelir. Yani bir paketin temiz
  pack'i grafiğinde ön sürüm olmadığını kanıtlamaz. Kararlı hattı `Tracon.AspNetCore`'un pack'i
  durdurur, çünkü tek sürüm hattı (K-602) 20 paketi birlikte keser.
