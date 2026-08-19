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
- **Analyzer diskten dosya OKUYAMAZ** (`RS1035`): tüketicinin bir dosyasına bakan tanı, dosyayı `buildTransitive` hedefinden `<AdditionalFiles>` ile alır. `AdditionalFiles` öğeleri **evaluation** anında toplandığı için, `.targets` dosyasının projenin sonunda import edilmesi sorun değildir — ama aynı build içinde ÜRETİLEN bir dosya o listeye giremez.
