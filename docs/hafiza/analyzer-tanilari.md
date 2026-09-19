# Analyzer/Lint Tani Kodlariyla Karsilasma

> RS0016/RS0026/RS0027 (`PublicApiAnalyzers`), MA0009, MA0004, CA1305, CA1875,
> CA1873 tani kodlarinin somut karsilasma vakalari. MSBuild/AOT/paketleme
> mekanigi icin: [`build-ve-analyzer.md`](build-ve-analyzer.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 156'da `build-ve-analyzer.md`'den ayrildi: dosya %1 bosluga dusmustu,
> analyzer TANI karsilasmasi MSBuild/AOT mekanigi temasindan ayrisiyordu.

- **`MA0004` `await using` ifadelerini de kapsar** (2026-08-02): kütüphane kodunda hata seviyesinde. Kalıp: `var x = ...;` sonra `await using (x.ConfigureAwait(false)) { ... }`. Doğrudan `await using var x = ....ConfigureAwait(false)` yazmak değişkenin tipini `ConfiguredAsyncDisposable` yapar ve kullanılamaz hale getirir.
- **`MA0009` kaynak üretilmiş `[GeneratedRegex]`'i de yakalar** (2026-08-02, Faz 8): "regex DoS" analizi timeout kontrolü sağlanamayan her regex'i işaretler. **Düzeltme (2026-08-07, Faz 48): `GeneratedRegexAttribute`'ün timeout aşırı yüklemesi VARDIR** — `matchTimeoutMilliseconds: 1000` yazılır ve analyzer susar. Repoda on üç kullanım bu biçimdedir; yeni desen yazarken timeout **atlanmaz**. Basit sabit desenler (ör. `^[a-z0-9][a-z0-9-]{0,31}$`) için regex'ten tamamen vazgeçip elle karakter döngüsü yazmak yine daha az koddur.

- **🚨 `.editorconfig` ile tanı susturmak HER AĞAÇTA ÇALIŞMAZ; `NoWarn` çalışır**
  (2026-09-14, Faz 166). `bench/capacity/` ağacı (kendi `Directory.Build.props`'u
  olan, repo mirasından kesilmiş bir alt ağaç) için `dotnet_diagnostic.CA1707.severity = none`
  **hiçbir yerleşimde** etki etmedi: dosya `bench/capacity/`'de, projenin kendi
  dizininde, `[*.cs]` ve `[**.cs]` bölüm desenleriyle, `root = true` ile ve
  onsuz — altı kombinasyonun altısında da CA1707 hata olarak kaldı. Aynı
  `.editorconfig` ÇIPLAK bir test projesinde (aynı SDK, aynı `AnalysisMode`)
  çalışıyor, yani mekanizma bu ağaca özgü bir şeyle etkileşiyor ve
  **saptanamadı**. csproj'daki `<NoWarn>` tek koşumda sustudu.
  **Ölçüm tuzağı:** ikinci bir `dotnet build` aynı ağaçta **0 bulgu** gösterir —
  derleme bayat değildir, yalnız yeniden derlenmez. `--no-incremental` de
  yetmez; bu yüzden her deneme **taze bir kopyada** koşulmalıdır, yoksa
  "düzeldi" sanılır. Kural: repo kökünden miras almayan bir alt ağaçta tanı
  gevşetmesi `NoWarn`'a yazılır ve gerekçesi csproj'un yanında durur.

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

## CA13xx/CA18xx tani vakalari

- **🚨 `CA1873`, logging argumani olarak verilen property erisimini de pahali
  sayabilir** (2026-09-19, Windows ve Ubuntu CI): `_request.SessionId`,
  `result.Observed`, `evaluator.GetType().FullName` ve
  `timeout.TotalSeconds` gibi erisimler on cagrida build'i kirdi. Mesaji
  susturma. Cagriyi ayni seviyenin `logger.IsEnabled(LogLevel.X)` guard'i
  icine al; nullable logger icin `logger?.IsEnabled(...) is true` kullan.
  Ayni tani her target framework icin tekrarlandigi icin 10 vaka 30 hata
  gorunur.
- **`CA1875` için `Regex.Matches(...).Count` kullanma.** Yalnız eşleşme sayısı
  gerekiyorsa `Regex.Count(...)` kullan; `MatchCollection` üretme. Analyzer
  sürümü veya işletim sistemi farkı nedeniyle yerel incremental build tanıyı
  göstermese bile temiz CI build'i gösterebilir.
- **🚨 CA1305 format belirtecli bir INTERPOLASYONU GORMEZ** (2026-09-07, Faz 153,
  K-720): format belirtecli bir interpolasyon `string.Format`'a degil
  `DefaultInterpolatedStringHandler`'a derlenir. Olculdu: CA1305 `warning`'e
  cekildiginde uc gercek ihlal dururken **sifir** bulgu verdi. Hangi belirtecin
  riskli oldugu da olculdu (tr-TR): `0.0`, `0.000000` ve `P0` RISKLI; **`F0` ve
  `0` DEGIL** — hicbir kulturde basamak gruplamazlar, yani `{seconds:F0}` gormek
  tek basina bir bulgu DEGILDIR. Kural: surecten cikan (kalici, HTTP govdesi,
  tool sonucu, CLI satiri) ve ondalik ya da yuzde tasiyan her metin
  `string.Create(CultureInfo.InvariantCulture, $"...")` ile kurulur. Guard
  davranissaldir (`InvariantShippedTextTests`), analyzer kurali yoktur. Ayrinti:
  [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
