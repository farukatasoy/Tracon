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

- **🚨 Chromium'un sahte ses cihazi HIC SUSMAZ** (2026-08-05, Faz 29): `--use-fake-device-for-media-stream` surekli bir ton uretir. Sessizlik tespitine (VAD) dayanan bir E2E testi bu yuzden hicbir zaman tetiklenmez ve 30 sn'de zaman asimina ugrar — olculdu. Cozum bir test hilesi degil, urunun kendi ihtiyaciydi: elle kapatma dugmesi (bas-konus / gurultulu ortam) eklendi ve test onu tiklar. Mikrofon ayrica GUVENLI BAGLAM ister; `http://127.0.0.1:<port>` Chromium'da guvenilir sayilir, uzak bir HTTP adresi sayilmaz.
- **Sahte model saglayicisi artik `AgentPrism.Testing.FakeModelProvider`'dir — yeni bir test projesi kendi `IModelProvider` taklidini YAZMAZ** (2026-08-06, Faz 39): Bes ayri dosyaya kopyalanmis (`EchoModelProvider` ×2, `ScriptedModelProvider`, `RoutingModelProvider`, `Fakes/FakeModelProvider`) 523 satir birlestirildi. Her modelin KENDI sirali yanit kuyrugu vardir (`ForModel(id, cfg => cfg.CallsTool(...).RespondsWith(...))`); kuyruk BIR KEZ tuketilir, tukendikten sonra `EchoesUserMessage()`/`EchoesLastToolResult()` fallback'i devreye girer — mesaj gecmisi taranarak "hangi tool zaten cagrildi" ASLA cikarilmaz (eski Routing/ScriptedModelProvider'in yaptigi gibi). Ayni saglayicinin FARKLI modelleri (ornek: bir yonlendirici + devrettigi alt agent) BAGIMSIZ kuyruk ister — ayni model id'sini paylasmak testler arasi durum sizdirir. `AgentPrismTestHost` (paket) ile FunctionalTests'in KENDI ic `AgentPrismTestHost`'u (TestServer tabanli, `Infrastructure/` altinda) AYNI ada sahiptir — ayni dosyada ikisi de `using` edilirse `CS0104` (belirsiz referans) verir; `using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;` tipi takma adla almak `using AgentPrism.Testing;` yerine cakismayi onler. Karar K-269.
- **`RunAssertions.ShouldHaveOutputContaining` akisli/akissiz ayrimina dikkat etmeli** (2026-08-06, Faz 39): bkz. `docs/hafiza/cekirdek-calistirma.md` — `MessageCompleted` yalniz akissiz `agent.RunAsync()` yolunda vardir, HTTP `/run` (SSE) yalniz `MessageDelta` uretir. Bu, depo ICI testlerin hicbirinde yakalanmadi (hepsi ya akissiz cagirdi ya da bu iddiayi hic kullanmadi) — yalniz depo DISINDAN paketlenmis nupkg'i kullanan gercek bir tuketici senaryosu yakaladi. **Ders**: yeni bir test paketi yayimlamadan once GERCEKTEN paketlenmis halini disaridan (ayri bir scratch projede, `NuGet.config` ile yerel beslemeye isaret ederek) dene — `ProjectReference` ile calisan bir ic test asla bu sinifta bir bosluk gormez.
- **Depo sozlesmeleri artik `TenantIsolationContract<TStore>`'tan turer** (2026-08-07, Faz 41): taban sinif hem ortak yasam dongusu tesisatini (`Store`, `CreateStoreAsync`, `InitializeAsync`/`DisposeAsync`, `OnDisposeAsync`) hem bes kiraci yalitimi testini tasir. Yeni bir sozlesme yazarken **kosum sinifi eklemek gerekmez**: var olan dort kosum (bellek ici + uc SQL) yalitim testlerini kendiliginden alir. Dort kanca yazilir: `SeedAsync`, `ExistsAsync`, `CountAsync` (zorunlu) ve `TryDeleteAsync` (silme sunmayan depoda `null` doner).
- **🚨 Kiraciyi PARAMETRE olarak almayan depolar icin iki depo ornegi KURULAMAZ** (2026-08-07, Faz 41, K-282): bellek ici depolar durumu ornek icinde tasir; iki ornek ayni arka uca bakmaz. Cozum `MutableTenantContext`: tek depo ornegi, cagrilar arasinda degisen kiraci. Sozlesme kancasinin ilk satiri `AmbientTenant.TenantId = tenantId;` olur.
- **🚨 Bellek ici depoyu kuran testte kiraci baglamini da ver** (2026-08-07, Faz 41): `new InMemoryRunStore()` varsayilan olarak `"default"` kiracisina baglanir. `RunRecordingAgent`'i baska bir `ITenantContext` ile kurup depoyu parametresiz olusturursan `QueryRunsAsync` BOS doner ve hata "test yanlis kurulmus" gibi degil "kayit yazilmamis" gibi gorunur. Faz 41'de 23 test bu sekilde kirildi; duzeltme `new InMemoryRunStore(tenantContext: ...)`.


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
- **🚨 Tam `dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1`
  koşumu ara sıra flaky kırılır — izole koşumda hep geçer (Faz 103).** Dört ayrı
  koşumda dört FARKLI test kırıldı: `ImageAttachmentWriterTests` (port çakışması,
  "Address already in use"), `SqliteDialectTests.Polymorphic_JSON_round_trips_intact`
  (`ON CONFLICT` unique constraint hatası), `ModelHealthSingletonTests.Health_check_runs_on_only_one_instance`,
  `OnlineEvalJobHandlerTests.Judge_timeout_cuts_off_the_wait_when_the_judge_ignores_cancellation`
  (20ms iç timeout'a karşı 1s dış test sınırı — thread-pool starvation altında
  50x marj bile tükeniyor). Dördü de kendi projesinde tek başına 100% geçti.
  Şüphe: binlerce testin aynı anda paylaştığı port/dosya/thread-pool kaynağı —
  hiçbiri fazın kendi değişikliğiyle ilgili değildi. Bir kapı koşumunda bunlardan
  biri kırmızı çıkarsa önce İZOLE tekrar et; yalnız izole de kırmızıysa gerçek
  regresyondur.
