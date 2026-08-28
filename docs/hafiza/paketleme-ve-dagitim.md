# Paketleme ve Tuketiciye Teslim Tuzaklari

> `dotnet pack`, `.nuspec`, `buildTransitive/`, sablon paketi ve tuketicinin
> agacina yazilan uretilmis dosyalar.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken
> okunur. Sinir: **nasil derlenir/analiz edilir** sorusu
> [`build-ve-analyzer.md`](build-ve-analyzer.md)'dedir; **nasil paketlenir ve
> tuketiciye nasil ulasir** sorusu buradadir. Analyzer YAZIMI ucuncu bir
> dosyadadir: [`analyzer-yazimi.md`](analyzer-yazimi.md).

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

## NSwag ile uretilen istemci (Faz 83)

> `AgentPrism.Client`, `docs/openapi/agentprism.json`'dan `dotnet nswag run` ile
> uretilir. Uc script (`scripts/nswag-*.py`) uretim ONCESI/SONRASI donusum
> yapar; komut sirasi paketin kendi README'sinde.

- **🚨 `[JsonSourceGenerationOptions(Converters = [...])]` PROPERTY UZERINDEN
  ulasilan enum tipleri icin ETKISIZ** (olculdu): global listeye kayitli bir
  `JsonStringEnumConverter<T>` yalniz KOK (`[JsonSerializable]`) tip olarak
  islenirse calisir; bir DTO'nun ozelligi olarak REACHABLE olan enum icin
  kaynak ureteci sessizce varsayilan SAYISAL `EnumConverter<T>`'a duser — tel
  degeri ("Running") `JsonException` firlatir. Tanı: `resolver.GetTypeInfo(t,
  options).Converter` turunu dogrudan sorgula. Cozum global liste degil, HER
  `enum` bildiriminin USTUNE TIP DUZEYINDE `[JsonConverter(typeof(
  JsonStringEnumConverter<T>))]` yazmak (K-571) — NSwag'in zaten property
  duzeyinde uyguladigi (polimorfik `$type` ayrimcilari icin) desenin aynisi.
- **NJsonSchema, `additionalProperties` anahtari YOK sayilan HER semaya
  otomatik bir `[JsonExtensionData]` yakalama ozelligi (`AdditionalProperties`)
  ekler** — semanin KENDI, ayni adli bir alani varsa `CS0102` verir. Uretim
  ONCESI dokuman kopyasinda her semaya `additionalProperties: false` yazmak
  hem catismayi giderir hem gereksiz yakalamayi kaldirir (K-570); STJ zaten
  bilinmeyen alani sessizce atlar, kapatma davranisi DEGISTIRMEZ.
  `properties` tasiyan ama zaten `additionalProperties` yazan bir semaya
  DOKUNMA — anahtar SIBLING'dir, `properties` ICINDEKI ayni adli bir ALAN
  (property) degildir.
  - **NSwag `.g.cs` dosyasindaki bir 🚨/`K-NNN`/`docs/` referansi ASLINDA
    KAYNAKTAN gelir**: sunucunun `.WithDescription(...)` metni oldugu gibi
    OpenAPI `description` alanina, oradan XML doc yorumuna kopyalanir.
    `ShippedDocumentationSelfContainmentTests` bunu `.cs` dosyasinda YAKALAR
    ama commit'li `docs/openapi/agentprism.json`'da YAKALAMAZ (emoji orada
    `🚨` olarak JSON-escape'lidir, ham UTF-8 degildir) — bir
    onceki fazdan miras kalan boyle bir ihlal, istemci ILK KEZ uretildiginde
    ortaya cikar. Duzeltme KAYNAK `.WithDescription(...)` metnindedir, uretilen
    dosyada degil (o zaten yeniden uretilir).
- **🚨 Linked-source (K-176) bir tipi UC saglayiciyi BIRLIKTE referanslayan
  bir tuketici derlemesinde `CS0433` (belirsiz referans) verir** (Faz 83,
  K-568): `MigrationRunner` her SQL saglayici paketine AYRI derlenir (K-247'nin
  ayni tuzagi, burada "sayim" degil "unqualified referans" baglaminda);
  `agentprism` CLI'si `--provider`'a gore calisma aninda secim yaptigi icin
  UCUNU DE ayni derlemede referans eder. `extern alias` uc komut sinifini
  neredeyse birebir uc kez tekrar etmeyi gerektirirdi. Cozum: paylasilan
  arayuzu `AgentPrism.Abstractions`'a tasimak (`IMigrationApplier`,
  `ISqlPersistenceDiagnostics`'in yaninda) — arayuz TEK derlemede tanimli
  oldugu icin uc saglayici referans edildiginde bile AYNI tip kalir.
- **`UseBaseUrl: false` (nswag.json) + `HttpClient.BaseAddress`**: istemci
  URL'leri BAGIL (`"api/agents"`, onek/sonek yok) uretilsin diye. Belge
  `servers` alanindan gelen sabit bir `_baseUrl` alani (varsayilan `nswag.json`
  ayariyla) her cagriya ONEK olarak eklenir ve `AddAgentPrismClient`'in
  `BaseAddress`'ini GORMEZDEN GELIR — `UseBaseUrl:false` bu alani TAMAMEN
  kaldirir, `HttpClient.BaseAddress` (trailing `/` ile) tek kaynak olur.

- **🚨 Tel uzerinde gorunen bir `enum`'u degistirmek DORT uretilmis yuzeyi birden
  tazelemeyi ister; ucunu yapip birini atlamak `tsc`'yi kirmizi birakir**
  (2026-08-26, K-627, olculdu). Sira: (1) `AGENTPRISM_OPENAPI_REFRESH=1 dotnet test
  tests/AgentPrism.AspNetCore.FunctionalTests -c Release --filter
  FullyQualifiedName~OpenApiSnapshotTests` → `docs/openapi/agentprism.json`;
  (2) `packages/agentprism-client` icinde `npm run generate` → `src/schema.ts`;
  (3) `dotnet tool restore && python3 scripts/nswag-prepare-document.py ... &&
  dotnet nswag run nswag.json && python3 scripts/nswag-postprocess-client.py ...
  && python3 scripts/generate-client-json-context.py ...` → `AgentPrismApiClient.g.cs`;
  (4) **`packages/agentprism-client` icinde `npm run build`**. Dorduncu adim
  kolayca unutulur: `src/AgentPrism.UI/frontend` tiplerini `@agentprism/client`'tan
  alir ve o import **`dist/`'i** cozer, `src/`'i degil. `dist/` gitignore'dur, yani
  `git status` temiz gorunur ve yalniz frontend `tsc` sikayet eder — sozluk anahtari
  `t(\`dashboard.errorClass.${entry.class}\`)` gibi sema tipinden TUREYEN her yerde
  hata bayat `dist`'i degil sanki sozlugu isaret eder.

- **🚨 Kendi `[JsonConverter]`'i olan bir deger tipi (System.Text.Json.JsonElement,
  Microsoft.Extensions.AI.ChatRole) ASP.NET Core OpenApi ureticisinde BOS `{}`
  sema uretir; NSwag bu semayi TIPIN KISA ADIYLA ("JsonElement", "ChatRole")
  gercek bir POCO sinifina cevirir ve bu sinif AYNI ad-alaninda GERCEK tipi
  GOLGELER** (2026-08-26, olculdu, Faz 115 sirasinda kesfedildi — eval CLI
  komutu ilk kez `EvalCaseResult.Scores`'u gercek veriyle deserialize etti).
  `anyType: "object"` yalnizca NSwag'in INLINE ettigi semalara uygulanir;
  `inlineNamedAny: false` NAMED bir semayi INLINE ETMEZ, kendi sinifini uretir.
  Sonuc: 16 `JsonElement`-tipli alanin TUMU (`EvalCaseResult.Scores`,
  `EvalSuite.Checks`, `JobTriggerRequest.Payload`, ...) bos-`[JsonExtensionData]`
  sinifina karsi deserialize ediliyordu — tel uzerindeki deger bir JSON NESNESI
  DEGILSE (ör. `Scores` bir dizi) her cagri `JsonException` firlatiyordu, HICBIR
  test bunu yakalamamisti (`AgentPrismTestHost`'un in-memory `TestServer`'i
  `AgentPrismApiClient` degil dogrudan `HttpClient` kullaniyor). Ayrica
  `System.Text.Json.JsonElement` bir STRUCT oldugu icin gercek tipe gecince
  NJsonSchema'nin ROOT response null-check'i (`if (objectResponse_.Object ==
  null)`) `CS0019` verir — bu da ayrica silinmeli. Cozum
  `scripts/nswag-postprocess-client.py`'daki `COLLIDING_ANY_TYPES` tablosu:
  bogus sinifi siler, her referansi GERCEK tipe (`System.Text.Json.JsonElement`)
  ya da — `AgentPrism.Client`'in bilerek referans ETMEDIGI bir paketin tipiyse
  (`Microsoft.Extensions.AI.ChatRole`) — o tipin GERCEK tel bicimine (`string`,
  kendi converter'i zaten oyle serialize ediyor) nitelendirir. Yeni bir
  cakisma tespiti: `python3 -c "import json; s=json.load(open('docs/openapi/agentprism.json'))['components']['schemas']; print([k for k,v in s.items() if v=={}])"`
  — cikan her ad `COLLIDING_ANY_TYPES`'a eklenir. `scripts/nswag_postprocess_client_test.py`
  regresyonu kapatir.

- **🚨 `$(Version)` iceren bir MSBuild ozelligi duz bir `<PropertyGroup>`'ta
  HER ZAMAN bos okunur** (2026-08-28, olculdu: `PackageReleaseNotes` ureten
  URL her paket icin `.../blob/v/CHANGELOG.md` cikti). Sebep `IsAotCompatible`
  tuzaginin AYNI SINIFI, farkli ekseni: `<Project>`'in DOGRUDAN cocugu olan
  her `<PropertyGroup>` **evaluation phase**'de, TUM target'lardan ONCE
  degerlendirilir; MinVer `$(Version)`'i kendi TARGET'inde (**execution
  phase**) hesaplar. Cozum: ozelligi `BeforeTargets="GenerateNuspec"` bir
  `<Target>`'in ICINDEKI `<PropertyGroup>`'a tasi — o zaman `$(Version)` zaten
  dolu. Kanit: `src/Directory.Build.props`.

- **🚨 Packed-consumer sample projelerini `AgentPrism.slnx`'e ekleme.** Bu
  projeler `artifacts/package/release` local feed'inden exact paket tuketir;
  temiz CI runner'inda feed `pack` oncesi yoktur ve solution restore `NU1301`
  ile kirilir. Sample'lari `scripts/release_extension_samples.py` dogrudan
  `.csproj` ile kosar. `release_extension_samples_test.py` bu siniri zorlar.
