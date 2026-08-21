# Analyzer ve Kaynak Ureteci Yazimi

> Roslyn `IIncrementalGenerator` ve `DiagnosticAnalyzer` yazarken cikan tuzaklar.
> Bir analyzer'i TUKETMEK (RS00xx, `NoWarn`, paketleme) `build-ve-analyzer.md`'dedir.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

## Kaynak üreteci (Faz 52)

- **`netstandard2.0`'da `record`/`init` için `IsExternalInit` yok** (2026-08-08): net5.0+ BCL'si taşır. Çözüm: `namespace System.Runtime.CompilerServices { internal static class IsExternalInit; }` polyfill'i — derleyici yalnız VARLIĞA bakar.
- **🚨 `SymbolDisplayFormat.FullyQualifiedFormat`, `UseSpecialTypes` yüzünden ilkel tipleri anahtar kelimeyle yazar** (`int`), `global::System.Int32` DEĞİL (2026-08-08): tip adına göre `switch` yapan kod (dönüştürücü seçimi) sessizce hiç eşleşmez; yalnız GERÇEK bir `int`/`bool` parametreli tool ile ortaya çıktı, `string`-only birim testleri yakalamadı. Çözüm: `UseSpecialTypes` bayrağı çıkarılmış özel format.
- **🚨 `OutputItemType=Analyzer`, çok-sıçramalı `ProjectReference` zincirinde YAYILMAZ**: `A → B → Generator` ise `A` üreteci YÜKLEMEZ — yalnız `A` PAKET (`PackageReference`) tükettiğinde NuGet'in `analyzers/dotnet/cs` yayılımı çalışır. Aynı çözümdeki bir örnek/tüketici (ProjectReference) Analyzer referansını AYRICA almalı; gerçek dış (paket) tüketici etkilenmez.
- **Çok-hedefli (net8/9/10) projede `@(Analyzer)` yalnız İÇ derlemede doludur**: `dotnet pack`'in dış derlemesinde `BeforeTargets="_GetPackageFiles"` BOŞ döner. Çözüm `TargetsForTfmSpecificContentInPackage` + `TfmSpecificPackageFile`; TFM-bağımsız dosyayı üç kez eklemek `NU5118` verir, `Condition="'$(TargetFramework)'=='netX.0'"` ile tek TFM'e sabitlenir.
- **`namespace X;` dosya başına TEK ad alanıyla sınırlıdır** (`CS8954`): iki ad alanı gereken üretilmiş dosyada blok biçimi (`namespace X { }`) kullanılır.
- **3. taraf soyut/sanal üye override ederken NRT imzasını TAHMİN ETME** (`Microsoft.Extensions.AI.AITool.Description`): reflection dökümü nullable ek açıklamasını GÖSTERMEZ; derleyici `CS8764` ile gerçek imzayı söyler. `NullableAttribute` YOKLUĞU genelde NON-nullable demektir.

## Tanı yazımı (Faz 73)

- **🚨 `AgentPrism.Core` KENDİ analyzer'ını KENDİ ÜZERİNDE koşturur** (2026-08-19): Core, `AgentPrism.Generators`'ı `OutputItemType=Analyzer` ile referanslar, yani üreteçle birlikte oradaki her `DiagnosticAnalyzer` Core'un kendi derlemesinde çalışır. Faz 73'te yeni bir tanının ilk hâli `dotnet pack`'i kırdı: kural `RunRecordingAgent` ve `ReplayMismatchGuard`'ı işaretledi. Yeni bir `APG` tanısı yazarken Core'un kendi kodunu da tarayacağını hesaba kat — bu bedava bir dogfood turudur. Diğer paketler etkilenmez; analyzer referansı çok sıçramalı `ProjectReference` zincirinde yayılmaz.
- **🚨 `DiagnosticSeverity.Info` tanısı `dotnet build` çıktısına DÜŞMEZ** (2026-08-19, ölçüldü): `-v:normal` ve `-t:Rebuild` ile de görünmez. Yalnız IDE'de ve `.editorconfig` ile seviye yükseltilirse çıkar. Bir agent'ın veya CI günlüğünün okumasını istediğin tanı `Warning` olmalıdır; bedelini `NoWarn`'a çeviren bir MSBuild özelliğiyle sınırla (K-506).
- **🚨 Bir tanının önerdiği DÜZELTMENİN uygulanabilir olduğunu ölç, yalnız tetiğini değil** (2026-08-21, Faz 78 denetim 🔴): `APG0402` doğru koşulda ötüyor, mesajı doğru, testleri geçiyordu — ve hiçbir özellik açmamış tüketiciye **var olmayan** bir dosyayı adlandırmasını söylüyordu. Sorulacak soru: *bu tanının dediğini harfiyen yapan tüketici ne elde eder?* Her yapılandırmada sor, yalnız mutlu yolda değil.
- **Tüketicinin MSBuild özelliğini analyzer'a `CompilerVisibleProperty` taşır** (2026-08-21): `<CompilerVisibleProperty Include="X" />` → `context.Options.AnalyzerConfigOptionsProvider.GlobalOptions["build_property.X"]`. 🚨 `AdditionalFiles` opt-in'e bağlı DEĞİLDİR (`AgentPrism.Core.targets`); bir tanı "tüketici bunu açtı mı" bilgisine ihtiyaç duyuyorsa onu **ayrıca** almalıdır. Birim testinde `AnalyzerOptions`'ın ikinci parametresi bir `AnalyzerConfigOptionsProvider` ister — `AnalyzerTestHelper.RunWithPropertiesAsync` bunu kurar.
- **Analyzer diskten dosya OKUYAMAZ** (`RS1035`): tüketicinin bir dosyasına bakan tanı, dosyayı `buildTransitive` hedefinden `<AdditionalFiles>` ile alır. `AdditionalFiles` öğeleri **evaluation** anında toplandığı için, `.targets` dosyasının projenin sonunda import edilmesi sorun değildir — ama aynı build içinde ÜRETİLEN bir dosya o listeye giremez.

## `<example>` derleme kapısı (Faz 79)

- **🚨 XML `<example>` bloğunun "yalnız iki yer tutucu yeter" iddiasını ölçmeden kabul etme.** Plan `app`/`agentPrism`'in yettiğini söylüyordu; küçük bir tarama `builder` (`IHostApplicationBuilder`) adının 45 bloğun 36'sında bağlanmamış geçtiğini gösterdi. Bir sarmalama (prelüd/harness) yazmadan önce **her** serbest tanımlayıcıyı say, sonra hangilerinin 2+ blokta tekrarlandığını (kalıcı placeholder) ve hangilerinin tek bloğa özgü olduğunu (örneği kendi kendine yeterli yap) ayır.
- **`<example>` her zaman C# taşımaz.** `<code language="json">` bir yapılandırma parçasıdır (`appsettings.json` fragmanı), Roslyn'e verilemez — bare bir JSON property'dir (`"X": {...}`), `{`+içerik+`}` sarılıp `JsonDocument.Parse` ile doğrulanır. Blok çıkarıcı `<code>` etiketinin `language` özniteliğini okumalı; okumazsa o bloklar sessizce atlanır (görüldü: 49 etiketten 4'ü kayboldu, bağımsız sayım çapraz kontrolü yakaladı).
- **🚨 Statik bir sınıf generic tip argümanı olamaz (`CS0718`) — iki ayrı `<example>` birbirini bu şekilde kırabilir.** Bir tool konteyner sınıfını `internal static class` gösteren örnek, `AddToolsFrom<T>()` gibi generic bir çağıranın örneğiyle BİRLİKTE kopyalanınca derlenmez. Tool metodu `static` kalmalı (APG0007), konteyner sınıf kalmamalı.
