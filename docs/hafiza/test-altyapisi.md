# Test Altyapisi Tuzaklari

> xunit.v3/MTP, Shouldly, Testcontainers, Playwright.
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

- **🚨 Yonlendirilmis bir alt surecte MSBuild DUGUM YENIDEN KULLANIMI `WaitForExitAsync`'i ~15 DAKIKA bloke eder** (2026-08-07, Faz 47): `dotnet test AgentPrism.slnx` hicbir test kosmadan on dakikalarca asili kaldi. Kok sebep `AgentPrism.Templates.Tests` fikstürüdür: `ProcessRunner` `dotnet pack`/`build`'i `RedirectStandardOutput`/`Error` ile calistirir; `dotnet pack` MSBuild isci dugumlerini `nodeReuse:true` ile baslatir ve o dugumler komut bittikten sonra da yasar (varsayilan ~15 dk). Dugumler ebeveynin yonlendirilmis boru taniticilarini MIRAS ALIR, boru hicbir zaman EOF gormez ve .NET'in `Process.WaitForExitAsync` cagrisi cikis kodunu degil **asenkron okuyucularin bitmesini** de bekledigi icin alt surec saniyeler once cikmis olsa bile bloke kalir. **Belirti**: `ps` ciktisinda tek bir `dotnet pack` sureci yoktur, yalnizca oksuz (`ppid = 1`) `MSBuild.dll … /nodeReuse:true` dugumleri durur; dugumler `pkill` ile oldurulunce fikstür ANINDA devam eder (olculdu). **Cozum**: `ProcessRunner` her alt surece `MSBUILDDISABLENODEREUSE=1` verir. Komut satiri anahtari (`-nodeReuse:false`) yetmez — `dotnet new` gibi MSBuild'i DOLAYLI cagiran komutlar onu tasiyamaz. Olcum: 8 dk+ (asili) → **18,5 sn**. **Kural**: MSBuild cagiran her alt sureci yonlendirirken bu degisken verilir.

## 🚨 `dotnet test --filter` SESSIZCE YUTULUR (Faz 77)

- **`--filter` MTP'de YOKTUR ve hata da vermez — tum paketi kosar.** 2026-08-20'de
  olculdu: `dotnet test tests/AgentPrism.Core.UnitTests -c Release --no-build
  --filter CapabilityExampleTests` **1004 testin tamamini** kosar ve yesil doner.
  Daralttigini sanirsin; kosum suresi seni yanilmaz cunku paket zaten hizlidir.
  Tehlike yesil bir yanlistir: bir kapiyi "kostum" diye isaretlersin ama aslinda
  hangi testin gectigini bilmezsin.
  **Dogru bicim derlenmis ikiliyi DOGRUDAN cagirmaktir:**
  `./artifacts/bin/<Proje>/release/<Proje> --filter-class "*Ad*" "*Ad2*"`
  (birden cok desen bosluk ile ayrilir). Secenekler: `--filter-class`,
  `--filter-method`, `--filter-namespace`, `--filter-uid` ve `--filter-not-*`.
  Olcum: 1004 test → **15 test / ~2 sn**.
  🚨 **Bayat komut dokumanlarda duruyor:** `docs/73`, `docs/74` ve `docs/75`
  `dotnet test --filter <Ad>` yazar. Oradan kopyalama; uc dosya da kapanmis
  kayittir ve geriye donuk duzeltilmez.

## Kusur mu, kirilgan test mi — ayirmadan rapor etme

- **🚨 Bir test tam pakette duser, tek basina gecerse "bayat" demeden ONCE
  paketi BIR KEZ DAHA kostur.** 2026-08-08'de yasandi: PostgreSQL paketinde
  `EvalStoreContract.AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir` dustu
  (`SqlEvalStore.AddCaseAsync` — "5 denemede sira numarasi atanamadi"), tek
  basina gecti, ikinci tam kosumda **870/870** yesil geldi. Yuk altinda
  kirilgan bir test; degisiklikle ilgisi yoktu. Aday listesine **F-102** olarak
  yazildi — sessiz birakilmadi.
- **🚨 `Templates.Tests` GLOBAL `~/.templateengine/packages.json` dosyasina
  yazar — paralel kosumda birbirini kilitler** (2026-08-16, Faz 58). Belirti:
  `Most_minimal_combination_compiles_with_zero_warnings` `ExitCode 70` ile
  duser, mesaj: *"Failed to retrieve template packages from provider 'Global
  Settings'. Details: The process cannot access the file
  '/Users/<kullanici>/.templateengine/packages.json' because it is being used
  by another process"* + `Sequence contains no matching element`. Bu bir URUN
  KUSURU DEGILDIR: `dotnet new` sablon deposu kullanici genelindedir, test
  basina yalitilmaz. **Ayirt etme**: tek basina kostur —
  `dotnet test tests/AgentPrism.Templates.Tests -c Release --no-build`; 10/10
  gecerse kilit cakismasidir. Ayni kosumda iki kez ust uste duserse gercek
  kusurdur. Faz 58'de tam paket bir kez 3695/3695 yesil, ikinci kez bu tek
  testte dustu, izole kosumda 10/10 gecti.
- **🚨 `AgentPrism.Ui.E2ETests` tam kosarken (55 test) zamanlama yarisi altinda
  kirilgan tekil testler cikabilir — F-102'nin Playwright hali** (2026-08-19,
  Faz 65 kapanisi). `Runs_button_on_session_page_navigates_to_filtered_list`
  tam kosumda 3 denemeden 2'sinde dustu (`tbody tr` sayisi dugme etiketiyle
  eslesmeden okundu), izolasyonda 3/3 gecti. Kok sebep tarayici/`Docker`
  kaynak cekismesi, urun kusuru degil. **Ayirt etme**: ayni desen — tek basina
  kostur, gecerse yuk altinda kirilganlik, ikinci tam kosumda da duserse
  gercek kusur. Aday: **F-122**.
  **Ikinci vaka (2026-08-20, Faz 77): `Eval_suite_is_created_case_added_and_run_passes`** —
  ayni desen, bu kez `fill` sirasinda *"element was detached from the DOM"* (`UiTests.cs:1113`).
  Uc adim da kosuldu: tam kosumda dustu, izolasyonda 1/1, ikinci tam kosumda 56/56 gecti.
  Aday: **F-130**. Iki vaka ayni sinif -- `Ui.E2ETests` tam kosumda kaynak cekismesine acik.

- **🚨 `dotnet test ... | grep ... | head -N` KOSUMU ERKEN KESER.** `head` N
  satiri alinca boruyu kapatir, `dotnet test` SIGPIPE alir ve kalan test
  projeleri **hic kosmaz**; kabuk yine de `exit 0` doner ve kosum basarili
  GORUNUR. 2026-08-08'de yasandi: 16 projeden yalniz 9'u kostu. Tam paketi
  **dosyaya yaz**, sonra dosyayi filtrele.

- **🚨 Tüketici testleri GLOBAL NuGet önbelleğine takılır — değişiklik görünmez olur** (2026-08-19, Faz 73): MinVer sürümü git yüksekliğinden türediği için iki commit arasındaki her `dotnet pack` **aynı** sürüm dizesini üretir (`0.0.0-preview.0.271`). NuGet bir sürümü global paket klasörüne BİR KEZ açar ve sonra hep onu kullanır; yeniden paketlenen `.nupkg` hiç açılmaz. Belirti: kodda yaptığın değişiklik `TemplateFixture` tabanlı testlerde **hiç görünmez** ve teşhis yanlış yere gider (Faz 73'te bir analyzer değişikliği üç koşum boyunca yok sanıldı). Çözüm fixture'a girdi: `TemplateFixture.ClearGlobalPackageCache` paketlenen sürümün `~/.nuget/packages/agentprism*/<sürüm>` dizinlerini siler. **Depo dışında elle bir tüketici denerken aynı dizini sen de sil.**

## Circir testi ve uretilen dosya okuma (Faz 74)

- **🚨 Statik ozellik baslaticilari BEYAN SIRASINDA kosar** (2026-08-19, olculdu): `CapabilityEntryPoints`'te `RepositoryRoot` toplayicilardan SONRA beyan edilmisti; toplayicilar kostugunda deger hala `null`'di ve `TypeInitializationException` verdi. Bir toplayicinin ihtiyac duydugu deger ya EN BASTA beyan edilir ya da `Lazy<T>` ile ertelenir — `Lazy` alani da consumers'tan **once** beyan edilmelidir, cunku alan baslaticisi da sirayla kosar.
- **🚨 Derlenmis XML dokumanini okuyan kapi, derlenmemis cozumde SESSIZCE YESIL gecer**: XML yoksa "hic kapsanmayan uye yok" sonucu cikar. Kapi once "her giris noktasi icin bir dokumanli uye bulundu mu?" diye sormali ve bulunamayanlari "once cozumu derle" mesajiyla dusurmelidir.
- **Test derlemesinin kendi dizini yapilandirmayi soyler**: `UseArtifactsOutput` altinda `AppContext.BaseDirectory` son segmenti `release` veya `debug`'dir. Paket XML'i `artifacts/bin/<Paket>/<yapilandirma>_net10.0/<Paket>.xml` altindadir; **dizin adi ile dosya adi eslesmelidir**, yoksa bagimliligin baska bir paketin ciktisina kopyalanan XML'i ikinci kez okunur.
- **Ornek metnini denetleyen iddia yabanci uyeler icin ACIK bir liste ister**: `Add*`/`Use*`/`Map*` cagrilarinin AgentPrism'e ait olup olmadigi ad sekline bakarak ayirt edilemez (`AddSingleton`, `AddHealthChecks`, `MapHealthChecks` Microsoft'undur). Liste yalnizca **Microsoft** uyelerini tasir; oraya bir AgentPrism uyesi eklemek incelemede tam olarak yanlis iddia olarak gorunur.
- **Kaynak dili kapisi test dosyalarini da tarar**: yol adinda Turkce harf kullanan bir test (`"Sipariş Servisi"`) `SourceLanguageTests`'i kizartir. Unicode yolu denemek icin Turkce olmayan bir harf sec (`Café`, `Órder`).

## 🚨 Paralel test SINIFLARI migration deadlock'u uretir (Faz 76)

Olculdu: tam surunun dort kosumunun **ikisinde** SQL Server entegrasyon testleri
20–32 test dusurdu — hepsi `SqlServerSchemaFixture.InitializeAsync` icinde
`0017_approval_conditions` deadlock'u. Hata bir migration adi soyler, yarisi degil.

Sebep tasarimdadir: her sozlesme testi SINIFI kendi semasini kurar
(`NewSchemaName()`) ve tum migration setini uygular; xunit siniflari paralel
kosturur. Desen uc saglayicida da vardi, yalniz SQL Server'in DDL kilitleri
cekismeyi gorunur yapiyor. Cozum: uc `*TestContext.CreateAsync` yolunda
`SemaphoreSlim(1, 1)` — sema kurulumu olculen sey degildir. **Kasitli**
eszamanlilik testleri etkilenmez; onlar `Migrations.ApplyAsync()`'i dogrudan
cagirir.

- **Izole yesil, tam kosum kirmizi** ise once paralellige ve paylasilan
  altyapiya bak (tek proje kosumu 558/558 geciyordu).
- **"failed: 0" ama toplam sayi dusmusse kosum eksiktir.**
  `grep -cE "Test run summary:"` ile proje sayisini da say — beklenen **16**.
