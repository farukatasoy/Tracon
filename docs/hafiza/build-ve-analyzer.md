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

## Roslyn kaynak üreteci projesi (Faz 52)

- **`netstandard2.0`'da `record`/`init` için `IsExternalInit` yok** (2026-08-08): net5.0+ BCL'si taşır. Çözüm: `namespace System.Runtime.CompilerServices { internal static class IsExternalInit; }` polyfill'i — derleyici yalnız VARLIĞA bakar.
- **🚨 `SymbolDisplayFormat.FullyQualifiedFormat`, `UseSpecialTypes` yüzünden ilkel tipleri anahtar kelimeyle yazar** (`int`), `global::System.Int32` DEĞİL (2026-08-08): tip adına göre `switch` yapan kod (dönüştürücü seçimi) sessizce hiç eşleşmez; yalnız GERÇEK bir `int`/`bool` parametreli tool ile ortaya çıktı, `string`-only birim testleri yakalamadı. Çözüm: `UseSpecialTypes` bayrağı çıkarılmış özel format.
- **🚨 `OutputItemType=Analyzer`, çok-sıçramalı `ProjectReference` zincirinde YAYILMAZ**: `A → B → Generator` ise `A` üreteci YÜKLEMEZ — yalnız `A` PAKET (`PackageReference`) tükettiğinde NuGet'in `analyzers/dotnet/cs` yayılımı çalışır. Aynı çözümdeki bir örnek/tüketici (ProjectReference) Analyzer referansını AYRICA almalı; gerçek dış (paket) tüketici etkilenmez.
- **Çok-hedefli (net8/9/10) projede `@(Analyzer)` yalnız İÇ derlemede doludur**: `dotnet pack`'in dış derlemesinde `BeforeTargets="_GetPackageFiles"` BOŞ döner. Çözüm `TargetsForTfmSpecificContentInPackage` + `TfmSpecificPackageFile`; TFM-bağımsız dosyayı üç kez eklemek `NU5118` verir, `Condition="'$(TargetFramework)'=='netX.0'"` ile tek TFM'e sabitlenir.
- **`namespace X;` dosya başına TEK ad alanıyla sınırlıdır** (`CS8954`): iki ad alanı gereken üretilmiş dosyada blok biçimi (`namespace X { }`) kullanılır.
- **3. taraf soyut/sanal üye override ederken NRT imzasını TAHMİN ETME** (`Microsoft.Extensions.AI.AITool.Description`): reflection dökümü nullable ek açıklamasını GÖSTERMEZ; derleyici `CS8764` ile gerçek imzayı söyler. `NullableAttribute` YOKLUĞU genelde NON-nullable demektir.


## Baglantili kaynak ve XML dokumani (K-352)

- **🚨 Ayni kaynak N derlemeye baglanirsa N `.xml` AYNI `<member>` kimligini
  tasir** (`Sql.Shared` → 620 ortak), `AddOpenApi()` **500** verir. Yalniz
  `ProjectReference` tuketicisi etkilenir, paket tuketicisi degil.
- MSBuild item'i olcerken hedefe `DependsOnTargets` ver; `-t:` bagimliyi
  kosmaz, cikti bos gelir.
