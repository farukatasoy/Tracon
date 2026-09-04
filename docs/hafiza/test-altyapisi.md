# Test Altyapisi Tuzaklari

> xunit.v3/MTP, Shouldly, Testcontainers, Playwright.
>
> Paketi KOSARKEN cikan tuzaklar (`dotnet test`, MSBuild, paralellik) AYRI
> dosyadadir: [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.
>
> 2026-08-01…03 arasindaki on bes madde (xunit.v3/VSTest ayrimi, Testcontainers
> 4.13 ctor'u, Shouldly/Meziantou cakismalari, erken Playwright locator tuzaklari)
> butce yuzunden [`../arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md)'ye
> tasindi (Faz 76). Kurallar hala gecerli — bir tuzak ararken oraya da grep at.

## Circir testi ve uretilen dosya okuma (Faz 74)

- **🚨 Statik ozellik baslaticilari BEYAN SIRASINDA kosar** (2026-08-19, olculdu): `CapabilityEntryPoints`'te `RepositoryRoot` toplayicilardan SONRA beyan edilmisti; toplayicilar kostugunda deger hala `null`'di ve `TypeInitializationException` verdi. Bir toplayicinin ihtiyac duydugu deger ya EN BASTA beyan edilir ya da `Lazy<T>` ile ertelenir — `Lazy` alani da consumers'tan **once** beyan edilmelidir, cunku alan baslaticisi da sirayla kosar.
- **🚨 Derlenmis XML dokumanini okuyan kapi, derlenmemis cozumde SESSIZCE YESIL gecer**: XML yoksa "hic kapsanmayan uye yok" sonucu cikar. Kapi once "her giris noktasi icin bir dokumanli uye bulundu mu?" diye sormali ve bulunamayanlari "once cozumu derle" mesajiyla dusurmelidir.
- **Test derlemesinin kendi dizini yapilandirmayi soyler**: `UseArtifactsOutput` altinda `AppContext.BaseDirectory` son segmenti `release` veya `debug`'dir. Paket XML'i `artifacts/bin/<Paket>/<yapilandirma>_net10.0/<Paket>.xml` altindadir; **dizin adi ile dosya adi eslesmelidir**, yoksa bagimliligin baska bir paketin ciktisina kopyalanan XML'i ikinci kez okunur.
- **Ornek metnini denetleyen iddia yabanci uyeler icin ACIK bir liste ister**: `Add*`/`Use*`/`Map*` cagrilarinin AgentPrism'e ait olup olmadigi ad sekline bakarak ayirt edilemez (`AddSingleton`, `AddHealthChecks`, `MapHealthChecks` Microsoft'undur). Liste yalnizca **Microsoft** uyelerini tasir; oraya bir AgentPrism uyesi eklemek incelemede tam olarak yanlis iddia olarak gorunur.
- **Kaynak dili kapisi test dosyalarini da tarar**: yol adinda Turkce harf kullanan bir test (`"Sipariş Servisi"`) `SourceLanguageTests`'i kizartir. Unicode yolu denemek icin Turkce olmayan bir harf sec (`Café`, `Órder`).

## Kume karsilastirmasi (Faz 78)

- **🚨 `HashSet.ShouldBe(...)` SIRALI esitlik denetler.** MSBuild proje sirasini
  garanti etmez; iki projeli bir cozumde `["First", "Second"]` bekleyen bir
  kume karsilastirmasi `["Second", "First"]` gordu ve dustu. Cozum
  `Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()` +
  liste karsilastirmasi. **Ikinci tuzak:** `ShouldContain("dize")` bir
  `HashSet<string>` uzerinde LINQ `Contains`'e baglanir ve `MA0002` ile
  **DERLEMEYI KIRAR** — karsilastirici ister; predicate asiri yuklemesi
  (`ShouldContain(x => ...)`) veya siralanmis liste kullan.
- **🚨 Bir derleme hatasi test kosumunu SESSIZCE eskitir.** `dotnet build … |
  grep error` ile hatayi gorup yine de test binary'sini kosmak **onceki**
  surumu olcer ve yesil gorunur. Faz 78'de bu iki kez oldu (mutasyon denetimi ve
  `MA0002`). Kosumdan once derlemenin gercekten yesil oldugunu dogrula.
- **🚨 Tam cozum `dotnet test`i art arda Docker tabanli paket (`SqlServer`,
  `PostgreSql`) kosarsa `Testcontainers` teardown'i yarisa girer** (Faz 81
  kapanisi). Belirti: her test `Passed` VE `Test Assembly Cleanup Failure` ile
  ikiletir (`Passed: N, Failed: N, Total: 2N`) — gercek assertion asla
  KIRMIZI degildir; `PostgresFixture.DisposeAsync()`'in konteyner silme
  cagrisi `TaskCanceledException` alir, cunku bir onceki paketin (`SqlServer`)
  KENDI teardown'i Docker daemon'ini hala mesgul ediyordur. Izole kosumda
  (`dotnet test tests/<Paket>`) her zaman temiz. Ayirt etme aynidir: izole
  kostur, gecerse kaynak cekismesi.

## Sevk edilen xunit.v3 test taban sınıfları: `xunit.v3` DEĞİL `xunit.v3.extensibility.core` (Faz 98)

Bir paket `[Fact]`/`[Theory]` taşıyan ABSTRACT taban sınıflar sevk ediyorsa
(kendisi çalıştırılabilir bir test PROJESİ değil, tüketicinin test projesinin
referans vereceği bir KÜTÜPHANE) `xunit.v3` paketi YANLIŞ seçimdir: onun
`buildTransitive` özellikleri `<OutputType>Exe</OutputType>` dayatır (MTP'nin
giriş noktası) ve kütüphane derlemesini kırar. `xunit.v3.extensibility.core`
AYNI `xunit.v3.core.dll`'i, bu zorunluluk olmadan verir — paketin kendi
açıklaması "test yazarları xunit.v3'ü kullanmalı" der, ama bu paket bir test
yazarı değil, test yazarının projesinin referans vereceği kütüphanedir.
`Shouldly` normal PackageReference kalır (private değil) — tüketicinin
kalıtılan `[Fact]` gövdeleri Shouldly çağırır. Ayrıca: bu paket `src/`
altındaysa `tests/Directory.Build.props`'un `<Using Include="Xunit"/>` /
`<Using Include="Shouldly"/>` satırlarını MİRAS ALMAZ — kendi csproj'unda
tekrarlanmalı — ve `.editorconfig`'in `[tests/**/*.cs]` bölümü (CA1707 alt
çizgi serbestliği gibi) de kapsamaz; proje yoluna özel yeni bir bölüm gerekir.

## `Barrier` ile eszamanlilik testi: senkron govde sessizce SIRALI calisir (Faz 81)

- **🚨 `AllowConcurrentInvocation = true` + senkron (`Func<string>`) bir tool
  govdesi = SESSIZCE sirali calisma.** Olculdu: `FunctionInvocationProcessor.ProcessFunctionCallsAsync`
  (MEAI) esiklemeyi `Task.WhenAll(...)` ile yapar, ama bu cagri ONCE LINQ
  `select`'i MATERYALIZE eder — govde `await` ETMEDEN (senkron) bloklayan bir
  cagriya (`Barrier.SignalAndWait`) girerse, `Task.WhenAll` ikinci govdeyi
  BASLATAMAZ: birinci govde donene kadar ikinci cagri hic KURULMAZ. Uc govde x
  5 sn zaman asimi = 15 sn — sessizce, hatasiz, ama HICBIR zaman gercekten
  cakismadan. **Cozum**: govde `Task.Run(() => { barrier.SignalAndWait(...); return sonuc; })`
  ile sarilir (senkron blokaji GERCEK bir arka plan thread'ine tasir) veya
  gercekten `async`/`await Task.Yield()` iceren bir govde yazilir. Barrier
  boyutu da DENIED/atlanan cagrilari SAYMAZ — bir cagri yetkilendirme
  reddiyle govdesine hic girmiyorsa `Barrier` katilimci sayisi o cagriyi
  DISLAR, aksi halde kalan govdeler suresiz bekler (zaman asimina kadar).

## Sevk edilen sozlesme paketi (Faz 98 · 99)


- **🚨 `ContractCoverage` gibi bir "hepsini bul" reflection kapisi, YENI bir
  sozlesme ailesi eklenince TUM mevcut tuketicileri kirar.** Faz 98'in
  `ContractTypes()`'i derlemedeki adi `Contract` ile biten her public abstract
  tipi donduruyordu; Faz 99 sagalayici ailesini ekleyince dort depolama kapsam
  testi (bellek ici + uc SQL) birden kirmiziya dondu — kendilerine ait olmayan
  sozlesmeleri turetmedikleri icin. Ders: boyle bir kapi **ilk gunden** bir
  aile/kapsam parametresi almalidir, ve kapsamsiz asiri yukleme BIRAKILMAMALIDIR
  (birakmak tuzagi birakmaktir). Eslesme uretmeyen bir kapsam `ArgumentException`
  atmalidir — aksi halde yazim hatasi "hicbir sozlesme yok, demek ki hepsi
  kapsanmis" diyen yesil bir kapi uretir (K-610).
- **🚨 `xunit.v3.extensibility.core` `Assert` TASIMAZ.** `Assert.Skip` /
  `Assert.SkipWhen` `xunit.v3.assert` paketindedir ve o paket sevk edilen
  sozlesme paketinin cozulmus grafiginde YOKTUR (olculdu, `project.assets.json`).
  Kosullu atlama icin xunit v3'un `[Fact(SkipUnless = nameof(X))]` alternatifi
  de ise yaramaz: `X` **public static** olmali, dolayisiyla ornek duzeyinde
  sanal bir uyeyi (turetilmis sinifin `override` ettigi bir ozelligi) OKUYAMAZ.
  Cozum atlamak degil, **ayri bir opt-in sozlesme sinifi** yazmaktir; turetmek
  niyet beyanidir ve sessizce gecen senaryo kalmaz (K-611).
- **Bir sozlesme senaryosunun disi var mi — ihlali KASTEN uretip kirmiziyi gor.**
  Faz 99'da ham-istemci kurali icin bu yapildi: ihlal once **derlenmedi bile**
  (yalniz `AgentPrism.Abstractions`'a bagli bir saglayici `AsBuilder()`'a
  erisemez — o tip `Microsoft.Extensions.AI`'dedir). Bu kendi basina bir
  bulgudur: en olasi hatayi yapmak yapisal olarak zordur. Paket bilerek
  eklendiginde senaryo uc turetilmis sinifta birden dustu.
- **Yeni bir `samples/*.Tests` projesi `tests/**` gevsemelerini ALMAZ.**
  `.editorconfig`'in `[tests/**/*.cs]` bolumu path'e bakar; `samples/` altindaki
  bir test projesi CA1707 (snake_case test adi) ve xUnit1051'e takilir.
  `[samples/*.Tests/**/*.cs]` glob'u **eslesmedi** (denendi); bastirmayi projenin
  kendi `<NoWarn>`'una gerekcesiyle yazmak hem calisiyor hem de yanindaki ornek
  UYGULAMALARI gevsetmiyor.
- **🚨 `ImplementationFactory.Method.Name`/`DeclaringType` derleyici-üretimi
  closure adı, PARTIAL CLASS genelinde numaralanır — dosya değil, hatta metot
  bile değil** (2026-08-26, Faz 105): `TryAddSingleton(static provider => ...)`
  kayıtlarını bir DI kayıt anlık görüntü testinde ayırt etmek için lambda'nın
  `Method.Name`'ini (`<RegisterCoreInfrastructure>b__48_0` gibi) kullanmak
  cazip görünür. Ölçüldü: sınıfa TAMAMEN ilgisiz bir private metot
  (`BindCoreFields`) eklemek bile sonraki HER closure'ın numarasını kaydırdı
  (`b__48_0` → `b__49_0`) — gerçek bir DI davranış değişikliği olmadan test
  kırıldı. Aynı `ServiceType`+`Lifetime` çiftini paylaşan birden fazla factory'yi
  ayırt etmek gerekiyorsa (`ServiceRegistrationSnapshotTests`'in vazgeçtiği
  kullanım), bunun yerine ya `ServiceProvider` kurup gerçek `.GetType()`'ı
  resolve et ya da farkı basitçe kabul edip testin XML dokümanına yaz — kırılgan
  bir ayrım, kapattığı boşluktan daha pahalıdır.
- **🚨 Satır-bazlı bir mimari cırcır taraması, ÇOK SATIRLI bir yapıyı sessizce
  yanlış kapsar — çökmez, olduğundan FAZLA eşleşir** (2026-08-27, Faz 119):
  `RawExceptionTextSiteTests`'in blok-sonu bulucusu "`catch` satırından sonraki
  ilk satır `{` mi" varsayıyordu. `WorkflowNodeRetry.cs`'deki çok satırlı
  `catch (Exception exception) when (...)` şeklinde açılış `{` üç satır sonra
  geliyordu; bulucu onu bulamayınca "fallback: dosya sonuna kadar" moduna
  düşüyor ve kategorik olarak ilgisiz bir metodun `.Message` satırını da aynı
  "catch bloğu" sanıyordu — sahte pozitif, ama SESSİZ (test hâlâ çalışıyor,
  yalnız yanlış siteyi raporluyor). Bir line-based tarayıcı yazarken "imzanın
  tamamı tek satırda" varsayımını asla yapma; gerçek repo kod stilinde çok
  koşullu `when`/`if` neredeyse her zaman çok satırlıdır. Tarayıcının kendi
  regresyon testleri de tek-satır örnek kullandığı için bunu yakalamadı —
  gerçek `src/` ağacında REFRESH koşup çıkan siteleri tek tek okumak asıl
  yakalayan adımdı.
- **🚨 Yeni bir XML `<example>`'a illüstratif bir tip adı eklemek `AgentPrism.
  Generators.UnitTests`'i kırar, hedef paketin kendi test projesini DEĞİL**
  (Faz 122): `tests/AgentPrism.Generators.UnitTests/Examples/ExampleCompilationTests`
  her `<example>` bloğunu gerçekten DERLER; `ExamplePrelude.cs` "sen yazmış
  gibi davran" tipleri (`GitAgentSource`, `ResponseQualityJudge`, vb.) stub
  olarak tanımlar. Yeni bir örnek yeni bir illüstratif ad kullanıyorsa
  (`AuditingAgentDecorator` gibi) o ada `ExamplePrelude.cs`'e bir stub eklenmeli
  — eklenmezse `CS0246` yalnız TAM çözüm koşumunda (`dotnet test AgentPrism.slnx`
  veya `kapi.py kapanis`) görünür, `Core.UnitTests` gibi hedef paketin kendi
  testini koşmak bunu YAKALAMAZ.
- **`FakeModelProvider.EchoesUserMessage()` ham mesajı DEĞİL, `"Echo: {mesaj}"`
  önekli metni döner** (2026-09-01, Faz 131). Geçerli JSON test etmek isteyen
  bir test bu yüzden echo modunu kullanamaz — `"Echo: {...}"` sözdizimsel olarak
  geçersiz JSON'dur. `RespondsWith(sabitMetin)` ile sabit bir yanıt kuyruklamak
  gerekir (`FakeModelProvider("...").RespondsWith(...)`).

## Yalitim ve tam kosum kirilganligi

Tek basina gecip tam kosumda dusen test AYRI dosyadadir:
[`test-yalitimi.md`](test-yalitimi.md) (2026-09-04'te butce ve konu icin ayrildi).
