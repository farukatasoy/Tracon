# Build, Paketleme ve Analyzer Tuzaklari

> MSBuild, NuGet, AOT, .editorconfig, Meziantou/Roslyn tanilari.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **`Enum.TryParse<T>` / `Enum.IsDefined` / `Enum.GetNames<T>()` AOT temiz** (2026-08-02): `ReasoningEffort` çevrimi bunlarla yazıldı, hiçbir `IL2026`/`IL3050` çıkmadı.
- **AOT üç yerde ödün istedi** (2026-08-02): `ValidateDataAnnotations()` → elle validator; `optionsBuilder.Bind()` → elle bağlama; tool argümanı serileştirme → elle biçimlendirme. Faz 2'de `jsonb` için `JsonSerializerContext` gerekecek.
- **`Guid.CreateVersion7()` net9+** (2026-08-02): `net8.0` da hedeflediğimiz için `AgentPrismId.NewId()` yazıldı (RFC 9562). Birincil anahtarlarda `Guid.NewGuid()` **kullanma** — index parçalanır.
- **`dotnet pack` kodsuz uyarı üretir** (2026-08-01): `IsPackable=false` olan projeler için NuGet **kodsuz** bir uyarı verir; `NoWarn` ile susturulamaz. Çözüm `<WarnOnPackingNonPackableProject>false</WarnOnPackingNonPackableProject>`. Kaynak: `NuGet.Build.Tasks.Pack.targets` satır 204. `Directory.Build.props` içinde boş `<Target Name="Pack" />` tanımlamak **çalışmaz** — props SDK hedeflerinden önce yüklenir.
- **`CentralPackageTransitivePinningEnabled` kütüphanede zararlı** (2026-08-01): geçişli bağımlılıkları üretilen `.nuspec` içine **doğrudan** bağımlılık olarak yazar. Ölçüldü: `AgentPrism.PostgreSql` 13 → 2 doğrudan bağımlılık.
- **Trim/AOT analyzer'ları kök seviyede açılamaz** (2026-08-01): `app.MapGet(pattern, delegate)` `IL2026` + `IL3050` üretir. Analyzer'lar `src/` katmanında, paket bazlı kapatılabilir olmalı.
- **`.editorconfig` isimlendirme kurallarında sıra önemli** (2026-08-01): ilk eşleşen kural kazanır. `const` ve `static readonly` kuralları genel private alan kuralından **önce** gelmelidir.
- **`MA0004` `await using` ifadelerini de kapsar** (2026-08-02): kütüphane kodunda hata seviyesinde. Kalıp: `var x = ...;` sonra `await using (x.ConfigureAwait(false)) { ... }`. Doğrudan `await using var x = ....ConfigureAwait(false)` yazmak değişkenin tipini `ConfiguredAsyncDisposable` yapar ve kullanılamaz hale getirir.
- **Ham interpolasyonlu dizede `{{` kaçış değildir** (2026-08-02): tek `$` ile açılan ham dizede `{` her zaman interpolasyon başlatır; `'{}'::jsonb` yazmak CS9006 verir. Çözüm: sütunu INSERT listesinden çıkarıp şema varsayılanına bırak, ya da iki `$` ile aç.
- **`Convert.ToHexStringLower` net9+** (2026-08-02): `net8.0` da hedeflendiği için `Convert.ToHexString` kullanılır. Migration checksum'ları bu yüzden büyük harf onaltılıktır.
- **Statik sınıf tür argümanı olamaz** (2026-08-02): `AddToolsFrom<OrderTools>()` `CS0718` verir çünkü tool sınıfları genelde `static class`. Bu yüzden `AddToolsFrom(Type)` aşırı yüklemesi var.
- **`record` ayar sınıfı `secret` sızdırır** (2026-08-02): derleyicinin ürettiği `ToString` tüm özellikleri yazar. Ayar sınıfları `class` olmalı; `SecretLeakTests` bunu tip üzerinden denetler (`GetMethod("ToString").DeclaringType == typeof(object)`).
- **🚨 `IsAotCompatible` `src/Directory.Build.props` içinde türetilemez** (2026-08-02): o dosya csproj gövdesinden **önce** yüklenir; csproj'da yazan `AgentPrismAotCompatible=false` görülmez ve bayrak geri alınamaz biçimde `true` kalır. Türetme `Directory.Build.targets` içindedir. Aynı tuzak csproj'a bakan her türetilmiş özellik için geçerli.
- **🚨 MSBuild hedef `Condition`'ı `DependsOnTargets`'tan ÖNCE değerlendirilir** (2026-08-02): bağımlılık zinciri hedeflerin kendi üzerinde kurulursa (`A` → `B` → `C`) ve `B`'nin koşulu `C`'nin ürettiği bir özelliğe bakıyorsa, `C` **hiç çalışmaz**. Zinciri en dıştaki hedefin `DependsOnTargets` listesinde sırayla kur. Yaşandı: Node algılama hedefi hiç koşmadı, arayüz sessizce derlenmedi.
- **🚨 `Sdk="..."` niteliğiyle yüklenen SDK hedefleri projenin EN SONUNA gelir** (2026-08-02): csproj gövdesindeki `<Import Project="...targets" />` daha önce yüklenir ve içindeki `BeforeTargets="AssignTargetPaths"` *"does not exist in the project, and will be ignored"* diye **sessizce** atılır. SDK hedeflerine kanca atan bir `.targets` için açık `<Import Project="Sdk.props|Sdk.targets" Sdk="Microsoft.NET.Sdk" />` biçimini kullan. Karar K-051.
- **🚨 Çok hedefli projede iç derlemeler paralel koşar** (2026-08-02): tek bir çıktı dizinine yazan bir dış araç (Vite, `emptyOutDir`) üç kez aynı anda çalışır ve `ENOENT ... unlink` verir. Böyle adımlar `BeforeTargets="DispatchToInnerBuilds"` ile dış derlemeye alınır. Karar K-050.
- **`MA0009` kaynak üretilmiş `[GeneratedRegex]`'i de yakalar** (2026-08-02, Faz 8): "regex DoS" analizi timeout kontrolü sağlanamayan her regex'i işaretler. **Düzeltme (2026-08-07, Faz 48): `GeneratedRegexAttribute`'ün timeout aşırı yüklemesi VARDIR** — `matchTimeoutMilliseconds: 1000` yazılır ve analyzer susar. Repoda on üç kullanım bu biçimdedir; yeni desen yazarken timeout **atlanmaz**. Basit sabit desenler (ör. `^[a-z0-9][a-z0-9-]{0,31}$`) için regex'ten tamamen vazgeçip elle karakter döngüsü yazmak yine daha az koddur.
- **AOT analyzer'ı jenerik tip argümanını da denetler** (2026-08-07, Faz 48): `ServiceDescriptor.Singleton<TService, TImplementation>()`'a kendi jenerik parametrenizi geçirmek `IL2091` verir; parametre `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]` ile işaretlenmelidir. `AddContentGuard<TGuard>` bunun örneğidir. Bu tanı derlemeyi kırar — AOT analyzer'ının `Core` üzerinde gerçekten canlı olduğunun kanıtı.
- **`System.Threading.Lock` net9+** (2026-08-02): `src/` net8.0 da hedefler; orada `lock` nesnesi olarak listenin kendisi kullanılır — ayrı bir `object` alanı `MA0158` tetikler. Test projeleri net10.0'dır ve `Lock` kullanabilir.

<!-- MEMORY.md'de ozeti var; tam metin burada korunur -->
- **`dotnet format`, `dotnet build`'den fazlasını yakalar** (2026-08-02): `EnableConfigurationBindingGenerator=true` ile build temiz geçti ama format `IL2026`/`IL3050` gösterdi — kaynak üreteci format'ın analyzer geçişinde devreye girmiyor. **Dört kapıyı da çalıştır**; sadece build'e güvenme.
- **🚨 Faz 11 `dotnet format` calistirmadan kapanmis** (2026-08-02, Faz 12): `main` uzerinde `dotnet build` 276 `IDE0055` hatasi veriyordu (uc bosluk girinti, `ISkillScriptGrantStore.cs` ve `SkillScriptGrant.cs` + turevleri). Faz kapanisinda dort kapinin da gercekten calistirildigini dogrulayin; `dotnet build` tek basina yesil gorunmuyordu bile.
- **Bash komutlarında `cd` kalıcıdır** (2026-08-01): bir komutta `cd artifacts/...` yapıldıysa sonraki komut oradan başlar. `rm -rf artifacts && dotnet build AgentPrism.slnx` sessizce yanlış dizinde çalıştı ve eski paketler doğru sanıldı. Doğrulama komutlarında mutlak yol kullan.

## AOT uyumluluk tablosunun gerekceleri

> `MIMARI.md` bolum 9'daki tablonun uzun gerekceleri buraya TASINDI (2026-08-05,
> Faz 30): sicak yol butcesi asilmisti ve bu satirlar ancak AOT'a dokunurken
> gerekir.

- **`AgentPrism.Core` — Evet.** Yansimaya dayanan tek yol `AddToolsFrom` ve
  `AddTool(Delegate)`; ikisi de `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`
  ile isaretlidir. Uyari **bastirilmaz**, cagirana iletilir.
- **`AgentPrism.SqlServer` — Hayir (vaat ertelendi).** Olculdu: sifir IL2/IL3
  uyarisi. Canli bir sorgu AOT altinda dogrulanmadigi icin vaat verilmedi (K-181).
- **`AgentPrism.Sqlite` — Hayir (olculmedi).** Faz 24 kapanisinda olcum YAPILMADI;
  `SQLitePCLRaw` yerel kutuphane tasir (K-196).
- **`AgentPrism.OpenAI` — Evet.** Faz 3'te olculdu: `IsAotCompatible=true` ile
  sifir uyari. `OPENAI001` / `MAAI001` **deneysel API** tanilaridir, AOT tanisi
  degildir — karistirilmamalidir.
- **`AgentPrism.AspNetCore` — Hayir.** Minimal API delege yonlendirmesi reflection
  kullanir. Bayrak `Directory.Build.targets` icinde turetilir: `src/Directory.Build.props`
  csproj'dan **once** yuklendigi icin orada turetmek tuketicinin `false` tercihini
  yok sayardi (K-006).

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

## PublicApiAnalyzers (RS00xx, Faz 60)

- **🚨 `dotnet format analyzers --diagnostics RS0016` tek koşumda bitmez —
  çözüm boyunca iteratif çalıştır** (2026-08-16): her koşum yalnız bir sonraki
  projenin (bağımlılık sırasına yakın) diagnostiklerini çözer. 17 paketlik bir
  çözümde yakınsamak için 13 koşum gerekti. `Formatted N of M files.` çıktısı
  N < M iken bile "bitti" görünebilir — asıl kanıt yeniden `dotnet build`
  çalıştırıp kalan `RS0016` sayısına bakmaktır.
- **🚨 Analyzer'ı ZORUNLU kılan `PackageReference` koşulsuzsa, o projede
  eksik `PublicAPI.txt` `dotnet format`'ı `System.NotSupportedException:
  Adding additional documents is not supported` ile ÇÖKERTİR** (2026-08-16):
  `MSBuildWorkspace` eksik bir `AdditionalDocument`'i solution-çapında toplu
  düzeltme sırasında EKLEYEMİYOR. Çözüm: dosya olmayan projeleri toplu koşum
  ÖNCESİNDE ya boş `PublicAPI.{Shipped,Unshipped}.txt` ekleyerek ya da
  analyzer referansını MSBuild özelliğiyle (`Condition`) o projede kapatarak
  devre dışı bırak.
- **RS0026 ("aynı ada sahip aşırı yükleme, opsiyonel parametre taşıyor")
  RS0027 ile birlikte okunmalı** ("bu opsiyonel parametreyi taşıyan aşırı
  yükleme, aynı isimdeki TÜM aşırı yüklemeler arasında EN ÇOK parametreye
  sahip olmalı"): opsiyonel parametreyi KISA aşırı yüklemede bırakıp UZUN
  aşırı yüklemeden kaldırmak RS0026'yı KAPATIR ama RS0027'yi AÇAR. Doğru yön
  tam tersi — opsiyonel her zaman en uzun aşırı yüklemede kalır, kısa
  olan(lar) ya tamamen opsiyonelsiz ya ayrı isimli (statik fabrika) olur.
  Constructor çiftlerinde (isim değiştirilemez) çözüm `private` ctor + `public
  static FromXyz(...)` fabrika metodudur — varsayılanlar fabrikada kalır,
  ctor artık public API'de görünmez.

## Baglantili kaynak ve XML dokumani (K-352)

- **🚨 Ayni kaynak N derlemeye baglanirsa N `.xml` AYNI `<member>` kimligini
  tasir** (`Sql.Shared` → 620 ortak), `AddOpenApi()` **500** verir. Yalniz
  `ProjectReference` tuketicisi etkilenir, paket tuketicisi degil.
- MSBuild item'i olcerken hedefe `DependsOnTargets` ver; `-t:` bagimliyi
  kosmaz, cikti bos gelir.
- **🚨 Tek `$` işaretli raw interpolated string'de `{{` KAÇIŞ DEĞİLDİR** (2026-08-19, Faz 68): `$"""..."""` içinde tek `{` bir interpolasyon deliği açar; SQL'e literal süslü parantez yazmak (`ISNULL(labels, N'{}')`, `COALESCE(labels, '{}')`) `CS9006`/`CS1733` verir. `$$"""` + `{{` ile çözmek yerine deseni değiştir: `labels IS NOT NULL AND EXISTS (...)` guard'ı hem brace istemez hem NULL davranışını AÇIK yazar. `OPENJSON`/`json_each`'in NULL girdideki davranışına güvenmemek de ayrıca doğrudur.

## Tüketiciye giden MSBuild (Faz 73)

> Analyzer YAZIMI ayrı bir alan dosyasındadır:
> [`analyzer-yazimi.md`](analyzer-yazimi.md).

- **🚨 `<None Update=...>` çapraz-hedefli projede SESSİZCE hiçbir şey yapmaz** (2026-08-19): SDK'nın varsayılan `None` glob'u yalnız **iç** (TFM'e özgü) derlemelerde uygulanır; `dotnet pack` paket dosyalarını **dış** çapraz-hedefleme derlemesinde toplar. Ölçüldü: dışarıda 2, içeride 7 `None` öğesi. `Update` eşleşecek bir öğe bulamaz, dosya pakete girmez ve **hiçbir uyarı çıkmaz**. Doğrusu `<None Remove="dizin/**" />` + `<None Include=... Pack="true" PackagePath="..." />` — `Remove` iç derlemelerdeki glob kopyasını düşürür, `Include` her iki derlemede de görünür. `README.md`'nin `Include` ile paketlenmesinin sebebi de budur. Doğrulama tek komuttur: `unzip -l <nupkg>`.
- **Git kökünü MSBuild'de bulmak**: `$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '.git/config'))` normal klonu bulur; worktree ve submodule'de `.git` bir DOSYA olduğu için ikinci bir deneme (`'.git'`) gerekir. İkisi de boşsa proje dizinine düş — tüketicinin build'ini kırma.
- **Tüketicinin ağacına dosya yazan `Copy` `ContinueOnError` taşımalıdır**: salt-okunur depo kökü (yaygın CI mount'u) aksi hâlde `MSB3021` ile build'i düşürür. Kolaylık amaçlı bir dosya, tüketicinin derlemesini kıramaz.
