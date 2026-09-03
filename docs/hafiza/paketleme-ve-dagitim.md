# Paketleme ve Tuketiciye Teslim Tuzaklari

> `dotnet pack`, `.nuspec`, `buildTransitive/`, sablon paketi ve tuketicinin
> agacina yazilan uretilmis dosyalar.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken
> okunur. Sinir: **nasil derlenir/analiz edilir** sorusu
> [`build-ve-analyzer.md`](build-ve-analyzer.md)'dedir; **nasil paketlenir ve
> tuketiciye nasil ulasir** sorusu buradadir. Analyzer YAZIMI ucuncu bir
> dosyadadir: [`analyzer-yazimi.md`](analyzer-yazimi.md). NSwag ile uretilen
> istemcinin (`AgentPrism.Client`/`@agentprism/client`) kendi tuzaklari
> dorduncu bir dosyadadir: [`nswag-istemci-uretimi.md`](nswag-istemci-uretimi.md)
> (2026-09-03, Faz 136 denetimi — dosya bütçesini asti, ayrildi).

## `IncludeBuildOutput=false` paketleri (Templates, meta) — pack tuzaklari

> Faz 37'de `AgentPrism.Templates` (dotnet new sablonu) paketlenirken kesfedildi.
> Meta paket (`AgentPrism.csproj`) de `IncludeBuildOutput=false` oldugu icin
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
  **tekil ozelligi kullanma**. `AgentPrism.Testing` bu ikinci hali kullanir:
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
  ~1 saat suruyordu. Cozum: `AgentPrism.src.slnf` cozum filtresi (yalniz `src/`);
  warm pack ~30 sn.
- **🚨 `wwwroot` + damga birlikte silinip SOLUTION derlenirse arayuz derlemesi
  YINE yarisir** (Faz 48; K-050'nin kapatmadigi bosluk). Belirti:
  `ENOENT: ... unlink '.../wwwroot/assets/index-*.js'`. **Cozum: damga
  silindikten sonra ONCE tek basina
  `dotnet build src/AgentPrism.UI/AgentPrism.UI.csproj -c Release` calistir,
  sonra solution'i derle.** Damga guncelken yaris hic olusmaz — normal artimli
  derlemede gorulmez, yalniz kopya dosya temizliginden sonra gorulur.

## Tüketiciye giden MSBuild (Faz 73)

> Analyzer YAZIMI ayrı bir alan dosyasındadır:
> [`analyzer-yazimi.md`](analyzer-yazimi.md).

- **🚨 `<None Update=...>` çapraz-hedefli projede SESSİZCE hiçbir şey yapmaz** (2026-08-19): SDK'nın varsayılan `None` glob'u yalnız **iç** (TFM'e özgü) derlemelerde uygulanır; `dotnet pack` paket dosyalarını **dış** çapraz-hedefleme derlemesinde toplar. Ölçüldü: dışarıda 2, içeride 7 `None` öğesi. `Update` eşleşecek bir öğe bulamaz, dosya pakete girmez ve **hiçbir uyarı çıkmaz**. Doğrusu `<None Remove="dizin/**" />` + `<None Include=... Pack="true" PackagePath="..." />` — `Remove` iç derlemelerdeki glob kopyasını düşürür, `Include` her iki derlemede de görünür. `README.md`'nin `Include` ile paketlenmesinin sebebi de budur. Doğrulama tek komuttur: `unzip -l <nupkg>`.
- **Git kökünü MSBuild'de bulmak**: `$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '.git/config'))` normal klonu bulur; worktree ve submodule'de `.git` bir DOSYA olduğu için ikinci bir deneme (`'.git'`) gerekir. İkisi de boşsa proje dizinine düş — tüketicinin build'ini kırma.
- **Tüketicinin ağacına dosya yazan `Copy` `ContinueOnError` taşımalıdır**: salt-okunur depo kökü (yaygın CI mount'u) aksi hâlde `MSB3021` ile build'i düşürür. Kolaylık amaçlı bir dosya, tüketicinin derlemesini kıramaz.

## Tuketiciye yazilan uretilmis dosya (Faz 74)

- **`%(ReferencePath.NuGetPackageId)` paket referansini `ProjectReference`'tan ayirir** (2026-08-19, olculdu): paket referansinda `NuGetPackageId` + `NuGetPackageVersion` doludur, `ProjectReference`'ta **bostur**. Kardes XML dokumani `%(RootDir)%(Directory)%(Filename).xml` ile bulunur — `ChangeExtension` cagirmaya gerek yok. Bu depo kendi paketlerini `ProjectReference` ile kullandigi icin ayni hedef burada **hicbir sey yazmaz**; tuketiciye giden bir target'i bu depoda dogrulayamazsin, `AgentPrism.Package.Tests` gerekir.
- **🚨 MSBuild `Include` degerinin BASINDAKI bosluk kirpilir** (2026-08-19, olculdu): `Include="    grep ..."` dosyaya girintisiz yazilir, yani girintili markdown kod blogu **uretilemez**. Cozum: uc ters tirnakli citli blok. Sondaki `%0A` bir satirdan sonra bos satir uretir ve korunur.
- **Ogenin donusumu (`@(X->'...')`) `;` uzerinden BOLUNMEZ, ama OZELLIK enterpolasyonu boler** (2026-08-19, olculdu): donusum kaynak oge basina tam bir cikti ogesi uretir, icinde noktali virgul olsa bile (`a;b.dll` tek oge kaldi) — `$([MSBuild]::Escape(...))` gereksizdir. Buna karsilik `Include="... $(Prop) ..."` icindeki `;` satiri IKIYE boler; olculdu: `->Distinct()` iki farkli paket surumunu `;` ile birlestirince `Installed version:` satiri ikiye ayrildi. **Donusum ifadesi icinde string fonksiyonu YAZILAMAZ** — donusumun ayraci da tek tirnaktir ve ifadeyi erken kapatir; hesaplanmis degeri oge ustverisinden (`%(NuGetPackageId)`) al.
- **`buildTransitive/` META PAKET uzerinden de akar** (2026-08-19, olculdu): uretilen `.nuspec` bagimliliklari `exclude="Build,Analyzers"` tasisa bile hem `AgentPrism.Core.targets` hem `AgentPrism.AspNetCore.targets` tuketiciye ulasti. `build/` bu sicramada durur; `buildTransitive/` durmaz.
- **NuGet yalniz `buildTransitive/<PackageId>.props` ve `.targets` dosyasini KENDILIGINDEN import eder.** Baska adla paketlenen bir `.targets` sorunsuz paketlenir ve **hicbir uyari vermeden** hic yuklenmez. Iki paketin `.targets`'i arasinda import sirasi onemsizdir — birinin yazdigi ozelligi digeri **target govdesinde** (calisma aninda) okuyorsa.
- **XML dokuman kimliginde metot jenerigi CIFT ters tirnak tasir**: `AddContentGuard``1`, `AddWorkflowFunction``2`. Tip jenerigi tek ters tirnaktir. Naif ad eslestirmesi 39 giris noktasinin dordunu sessizce atladi (olculdu, Faz 74).
- **`docs/openapi/agentprism.json` `AgentPrism.AspNetCore` paketine KAYNAGINDAN girer**: `<None Include="../../docs/openapi/agentprism.json" Pack="true" PackagePath="buildTransitive/" />`. Olculen maliyet: 1 034 455 → 1 100 931 bayt (**+%6,4**). Kopya uretilmedigi icin sapma yuzeyi yok; belgeyi calisan host'a `OpenApiSnapshotTests` bagliyor.
- **🚨 Tuketicinin agacina yazilan uretilmis dosya PROJE basina yazilir, depo koküne degil** (2026-08-20, Faz 74 denetim bulgusu 3, olculdu): bir cozumdeki iki proje FARKLI paket kumesi referanslar (`src/Web` meta paket, `src/Worker` yalniz `AgentPrism.Core`) ve tek paylasilan dosya iki cevabi birden tasiyamaz — son derlenen proje kazanir, Web HTTP belgesini KAYBEDER, icerik her derlemede degisir. Birlestirme (merge) COZMEZ: projeler PARALEL derlenir ve okuma-yazma yarisir. `$(MSBuildProjectDirectory)` yapisal olarak dogrudur. `AGENTS.md` istisnadir cunku HIC ezilmez ve icerigi projeye gore degismez.
- **Proje dizinine yazan bir hedefi "salt-okunur dizin" ile test etme**: proje dizini `bin/`/`obj/` icin zaten yazilabilir olmali; salt-okunur yapilinca DERLEMENIN KENDISI `MSB3021` ile kirilir ve test urunu degil kendini olcer. Tasinabilir bicim: dosyanin yerine bir **dizin** koy. Gercek CI sekli (`UseArtifactsOutput` ile cikti baska yere, kaynak agaci salt-okunur) elle dogrulanir: `warning MSB3491`, `exit=0`.

## MinVer surumu calisma agacinin durumunu GORMEZ (Faz 136)

- **🚨 MinVer surumu yalniz git YUKSEKLIGINDEN turetir** (`git rev-list --count
  --first-parent <commit>`); calisma agacinin KIRLI olup olmadigi HIC girdi
  degildir. Commit'siz bir degisiklik yapip `dotnet pack` calistirmak AYNI
  surumu (ayni yukseklik) ama FARKLI SHA-256 tasiyan bir artifact uretir - iki
  farkli icerik AYNI `<id, version>` ciftini adlandirir. Olculdu (2026-09-03):
  `src/Directory.Build.props`'a commit'siz bir satir eklemek `.nuspec`'te
  YALNIZ `<projectUrl>`'i degistirdi, `<repository commit="...">` AYNI kaldi.
  Cozum `Directory.Build.targets`'teki `AgentPrismValidateCleanWorkingTree`
  hedefi (`BeforeTargets="GenerateNuspec"`, aynen `AgentPrismValidatePackageReadme`
  gibi - yalniz `pack` yolunda kosar, `build`/`test`'i KIRMAZ): `git status
  --porcelain` bos degilse `AGENTPRISM0004` ile durur. **Untracked dosya da
  kirli sayilir** - SDK'nin varsayilan `Compile` glob'u `**/*.cs` oldugu icin
  takip edilmeyen bir `.cs` dosyasi PAKETE GIREBILIR. Override
  (`AgentPrismAllowDirtyPack=true`) surumu OTOMATIK turetmez - `dirty` tasiyan
  ACIK bir `MinVerVersionOverride` ister (`0.0.0-dirty.<ad>`, her zaman temiz
  surumun ALTINDA sıralanır) ve CI'da (`CI=true` veya
  `ContinuousIntegrationBuild=true`) HIC calismaz.
- **🚨 Bu kapı, iterasyon için commit isteyen çağıranları da yakalar** (Faz
  136, bağımsız denetim 🔴#1). `kapi.py kapanis`'in kendi pack adımı ve
  `AgentPrism.Package.Tests`'in `TemplateFixture`/`ReleaseArtifactFixture`'ı
  gerçek `dotnet pack "AgentPrism.src.slnf"` çalıştırır - bunlar paketleme
  SÖZLEŞMESİNİ (README, icon, K-008) doğrular, bir yayın adayı üretmez, ama
  repo commit'i yalnız kullanıcı isteyince atılır. **Çözüm:**
  `AgentPrismSkipCleanWorkingTreeCheck=true` - kapıyı TAMAMEN atlar, yalnız bu
  üç iç araç noktasının kendi `dotnet pack` çağrısına eklenmiştir (K-661). Yeni
  bir çağıran noktasına eklemek (insanın DOĞRUDAN kullanması dahil) bu kararı
  ihlal eder. `PackCleanlinessGateTests` (aynı test projesinde) gerçek kapıyı
  KASITLI olarak dirtiler - `RepositoryTreeGate` koleksiyonu onu
  `ReleaseArtifactTests`'ten SIRALI tutar, aksi halde paralel çalışan iki
  gerçek `dotnet pack` birbirinin kirlilik durumunu görür.
- **🚨 NuGet'in KENDİSİ `.nupkg`'i iki ayrı `dotnet pack` koşumunda AYNI
  ÜRETMEZ** (Faz 136, ölçüldü: aynı commit, aynı `MinVerVersionOverride`, art
  arda iki koşum → 20/20 paket FARKLI ham SHA-256). `.nupkg`/`.snupkg` bir OPC
  (Open Packaging Conventions) zip'idir; NuGet.Packaging kendi core-properties
  parçasını HER koşumda RASTGELE (`Guid.NewGuid()`) bir dosya adıyla yazar
  (`package/services/metadata/core-properties/<32 hex>.psmdcp`) ve
  `_rels/.rels` o adı taşır - `lib/`, `.nuspec` ve geri kalan HER giriş
  birebir aynı kalsa bile. Ham dosya SHA-256'sını karşılaştırmak "aynı sürümün
  ikinci koşumu" senaryosunu HER ZAMAN sahte bir "farklı artifact" çakışmasına
  çevirirdi - tam da no-op iddiasının tersini. Çözüm `_content_fingerprint`:
  bu iki rastgele-adlı girişi HARİÇ TUTUP geri kalan girişleri (ad + bayt)
  hash'ler; manifest'in yayınlanan `sha256` alanı DEĞİŞMEDİ (hâlâ ham dosya
  hash'i - gerçekte yayınlanana eşleşen budur).
- **`scripts/kapi.py yayin` artık staging dizinine paketler, sonra promote
  eder** (`artifacts/package/staging/run-<rastgele>/`, `.gitignore`'daki
  `artifacts/` altında - bir sonraki koşumun kendi "erken ret" git denetimini
  kirletmez). `_clean_stale_packages`'ın sessiz silmesi KALDIRILDI: aynı
  `<id, sürüm>` çifti `release_dir`'de FARKLI bir içerik parmak iziyle zaten
  varsa hiçbir dosya promote edilmez (`_promote_staged_packages`, hepsi ya da
  hiçbiri), aynı parmak iziyle deterministik no-op'tur. Sonuç: tekrarlanan
  yerel `--surum` koşumları artık `release_dir`'i ESKİ sürümlerden OTOMATİK
  temizlemez - bu bilinçlidir (silme davranışı kaldırıldı, eklenmedi); gerekiyorsa elle
  `rm -rf artifacts/package/release`.
