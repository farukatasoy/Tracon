# Test Yalitimi ve Tam Kosum Kirilganligi

> Bir test TEK BASINA gecip TAM kosumda dustugunde buraya bak. Test
> **yazimi** tuzaklari [`test-altyapisi.md`](test-altyapisi.md), kosumun
> ASILMASI/eksik kalmasi [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md).
>
> **Bu sinif YEDI kez kayda gecti:** asagidaki alti vaka (sinif Faz 103'te
> acildi; Faz 82 · 130 · 134 · 141 ayri testlerle tekrarladi) ve 2026-09-04
> surec denetiminin taban kosumu. Her seferinde ayirt etme ELLE yapildi.
> `python3 scripts/kapi.py kapanis` artik dusen testi otomatik izole tekrar
> kosar ve hukmu basar (`isolate_failed_tests`) — cikis kodunu DEGISTIRMEZ,
> yalnizca bir sonraki adimi soyler. Ayrim: 2026-09-04 surec denetimi
> ([kesif](../kesif/2026-09-04-surec-denetimi.md)).

## Vakalar

- **🚨 Tam `dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1`
  koşumu ara sıra flaky kırılır — izole koşumda hep geçer (Faz 103).** Beş ayrı
  koşumda beş FARKLI test kırıldı: `ImageAttachmentWriterTests` (port çakışması,
  "Address already in use"), `SqliteDialectTests.Polymorphic_JSON_round_trips_intact`
  (`ON CONFLICT` unique constraint hatası), `ModelHealthSingletonTests.Health_check_runs_on_only_one_instance` (**bu vaka 2026-09-09'da
  ÜRÜN KUSURU çıktı — aşağıya bak; sınıfa yanlış yazılmıştı**),
  `OnlineEvalJobHandlerTests.Judge_timeout_cuts_off_the_wait_when_the_judge_ignores_cancellation`
  (20ms iç timeout'a karşı 1s dış test sınırı — thread-pool starvation altında
  50x marj bile tükeniyor), `AgentPrism.Ui.E2ETests.UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript`
  (Faz 130: `GetByTestId("voice-transcript")` 30s Playwright timeout'una takıldı,
  izole koşumda 2.5s'de geçti), `AgentPrism.Ui.E2ETests.UiTests.Runs_screen_lists_only_roots_by_default`
  (Faz 141: aynı 30s `WaitForAsync` deseni bir kez kırıldı, izole 4/4 ve tüm
  proje 58/58 geçti). Altısı da kendi projesinde tek başına 100% geçti.
  Şüphe: binlerce testin aynı anda paylaştığı port/dosya/thread-pool kaynağı —
  hiçbiri fazın kendi değişikliğiyle ilgili değildi. Bir kapı koşumunda bunlardan
  biri kırmızı çıkarsa önce İZOLE tekrar et; yalnız izole de kırmızıysa gerçek
  regresyondur.
- **F-122 kapandı (2026-08-26): E2E liste testi ağ yanıtını beklemiyordu.**
  `UiTests.Runs_button_on_session_page_navigates_to_filtered_list`, yönlendirme
  sonrası yalnız başlığı bekledi. Ardından `tbody tr` sayısını tek seferde
  okudu. `/api/runs` yanıtına `RouteAsync` ile 750 ms gecikme eklenince test
  her koşumda 1 yerine 0 görerek düştü. Düzeltme satır sayısını Playwright
  `ToHaveCountAsync` ile 10 saniyeye kadar bekler. Gecikme regression baskısı
  olarak testte kaldı; düzeltilmiş test aynı gecikmeyle 5/5 geçti. Bu kök neden
  yukarıdaki dört .NET kırılganını veya F-130/F-137/F-139'u açıklamaz.
- **Canary background-service testleri süre değil sonuç bekler (onarım
  2026-08-26).** Tam koşumda
  `Gradual_ramp_advances_weight_by_one_step_and_keeps_existing_assignments`
  25 yerine başlangıç değeri 5'i gördü. Test service'i başlattı, 200 ms uyudu ve
  sonucu okudu. Beklemeyi 1 ms'ye indirmek aynı hatayı deterministik üretti.
  `CanaryEvaluationServiceTests` sınıfındaki beş sabit uyku tarandı: pozitif
  yollar store/audit/lease sonucunu en çok 5 saniye poll eder; disabled yol
  tamamlanan service task'ını kullanır. Sayaçlar `Interlocked`/`Volatile` oldu.
  İki-instance testi non-holder'ı önce durdurur; böylece ardışık iki lease
  sahibini eşzamanlı sahiplik sanmaz. Sınıf 10 ardışık koşumda 50/50 geçti.
  Sonraki tam koşum aynı sınıfı `RunReconciliationTests` içinde de gösterdi:
  heartbeat yazılmadan orphan claim koştu. 1 ms mutation aynı hatayı deterministik
  üretti. Sınıftaki reconciliation, continuation, singleton ve heartbeat
  senaryoları da sonuç bekler; sayaçlar thread-safe'tir. Sınıf 10 turda 70/70
  geçti.
- **🚨 `MeterListener` process-wide'dır; süzgeç `Meter` INSTANCE'ına bağlanır,
  ismine değil** (Faz 134'te gerçekten çöktü: izole 1/1 yeşil, yeni eşzamanlı
  bir testle 3/3 kırık). Her host `new Meter(AgentPrismDiagnostics.MeterName)`
  kurar — AYNI isim, FARKLI instance. **Kural:** teste kendi `IMeterFactory`'sini
  ver, `ReferenceEquals(instrument.Meter, meter)` ile süz —
  `Fakes/MetricTestHelpers.cs` (birim), `Infrastructure/TestMeterIsolation.cs`
  (fonksiyonel; host'a `AddSingleton<IMeterFactory>`). Etiketle süzmek YETMEZ —
  gerekçe kapının XML'inde. Sınıf taraması (2026-09-02) dört vaka buldu, dördü
  de düzeltildi. Kapı: `MeterListenerIsolationTests`.
- **🚨 `ModelContextProtocol` istemcisi `server/discover` probesi 5 saniye
  aşılırsa SESSİZCE eski `initialize` handshake'ine düşer ve `2025-11-25`
  negotiate eder — Tasks eklentisi bunu reddeder** (F-190, faz dışı kusur
  giderme, 2026-09-04). Tam paket koşumu CPU baskısı altında bu 5 saniyeyi
  arada bir aşıyordu (üç `McpTask*` testi düşüyordu, izole hep geçiyordu; K-656
  ile YÜZEYSEL benzer ama SINIFI farklı — paylaşılan process durumu değil, kısa
  bir üretim-varsayımlı zaman aşımı). `ModelContextProtocol.Core`'un kendi
  `McpClientOptions.DiscoverProbeTimeout` XML'i bunu zaten belgeliyor:
  varsayılan 5 sn "gerçek ağ eşleri için" kasıtlı kısa, "yüksek gecikmeli
  ortamlar için artırın" diyor — in-memory `TestServer` + onlarca paralel host
  tam olarak o ortamdır. **Çözüm**: test istemcisinde
  `DiscoverProbeTimeout = TimeSpan.FromSeconds(30)` (varsayılan
  `InitializationTimeout` 60 sn'nin altında kalır) —
  `Infrastructure/McpTaskTestClient.cs`. Mekanizma
  `configureApp`'ten geçirilen bir middleware'in İLK isteği 6 sn geciktirmesiyle
  deterministik kırmızıya çevrildi (`McpTasksEndpointTests.Discover_probe_negotiates_2026_07_28_even_when_the_first_response_is_slow`),
  gerçek CI çekişmesini beklemeden. Sınıf taraması: bu SDK istemcisini kuran
  TEK yer bu dosyaydı — üretim tarafında (`McpOAuthAuthorizationCoordinator`)
  `clientOptions: null` kasıtlı, çünkü o gerçek ağ eşlerine bağlanıyor ve SDK
  varsayılanı orada doğru.

- **🚨 F-180 "✅ KAPANDI" işaretlendi, sınıf kapanmadı — YEDİNCİ vaka
  (2026-09-04, süreç denetimi).** `UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript`
  taban koşumda yine düştü (`Timeout 30000ms exceeded ... waiting for
  GetByTestId("voice-transcript")`), izole koşumda **1/1, 2,5 sn**. K-660'ın
  düzeltmesi (ses gelmeden `commit` ve boş transcript yollarına `idle` sunucu
  çerçevesi) HEAD'de ve doğrudur — kapattığı iki ürün yolu gerçekti. Ama kaydı
  AÇAN repro "yük altındaki tam paket koşumu"ydu ve kapanış onu tekrar
  koşmadı; hedefli `VoiceConversationTests`'in yeşili sınıfı kapatmadı.
  **Ders:** bir kaydı açan repro ne ise, kapanış onunla kanıtlanır. Hedefli bir
  testin yeşili, yük altında görülen bir semptomu kapatmaz.
  Ölçüm: [`../kesif/2026-09-04-surec-denetimi.md`](../kesif/2026-09-04-surec-denetimi.md) § A7.

## Kusur mu, kirilgan test mi — ayirmadan rapor etme

> Faz 156'da `test-kosum-tuzaklari.md`'den taşındı: konu bu dosyanın "tek
> başına geçip TAM koşumda düşen test" temasıyla örtüşüyordu.

- **🚨 Bir test tam pakette duser, tek basina gecerse "bayat" demeden ONCE
  paketi BIR KEZ DAHA kostur.** 2026-08-08'de yasandi: PostgreSQL paketinde
  `EvalStoreContract.AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir` dustu
  (`SqlEvalStore.AddCaseAsync` — "5 denemede sira numarasi atanamadi"), tek
  basina gecti, ikinci tam kosumda **870/870** yesil geldi. Yuk altinda
  kirilgan bir test; degisiklikle ilgisi yoktu. Aday listesine **F-102** olarak
  yazildi — sessiz birakilmadi.
  **Ikinci vaka (2026-08-24, Faz 95 kapanisi):** `AgentPrism.Workflows.UnitTests.WorkflowHumanInTheLoopTests.A_rejection_response_also_flows_through_execution`
  tam kosumda dustu (`Sequence contains no matching element`), tek basina VE
  proje tek basina (102/102) gecti. Faz 95 `src/AgentPrism.Workflows`'a hic
  dokunmadi — nedensellik dislaniyor. Ucuncu tam kosum 20/20 proje yesil
  donuncu dogrulandi. F-102'nin sinifi `Ui.E2ETests`'e ozgu degil, tam
  cozum kosumunda HERHANGI bir projede gorulebilir.
- **🚨 `Templates.Tests` GLOBAL `~/.templateengine/packages.json` dosyasina
  yazar — paralel kosumda birbirini kilitler** (2026-08-16, Faz 58). Belirti:
  `Most_minimal_combination_compiles_with_zero_warnings` `ExitCode 70` ile
  duser, mesaj: *"Failed to retrieve template packages from provider 'Global
  Settings'. Details: The process cannot access the file
  '/Users/<kullanici>/.templateengine/packages.json' because it is being used
  by another process"* + `Sequence contains no matching element`. Bu bir URUN
  KUSURU DEGILDIR: `dotnet new` sablon deposu kullanici genelindedir, test
  basina yalitilmaz. **Ayirt etme**: tek basina kostur —
  `dotnet test tests/AgentPrism.Package.Tests -c Release --no-build`; 10/10
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
  **Ucuncu vaka (2026-08-23, Faz 90 kapanisi): AYNI test** (`Eval_suite_is_created_case_added_and_run_passes`). Uc adim yine kosuldu: tam kosumda 4956/4957 (bu tek test dustu), izolasyonda 1/1 (7,7 sn), ikinci tam kosum cikis kodu 0. **Kapanış (2026-08-26):** kök yarış bulundu: ilk `GET /cases` pending iken kullanıcı `Add case`e basabiliyor, sonra gelen boş yanıt yeni satırı siliyordu. UI ilk yükleme bitene kadar `Add case` ve `Save cases`i kapatır; frontend regression testi önce kırmızı, düzeltmeden sonra yeşildir. F-130 açık kayıt değildir.
- **🚨 "Yük altında kırılgan" hükmü, testin GEÇME sebebi bir zamanlama eşiğinin ALTINDA kalmaksa YANLIŞTIR** (2026-09-09, K-743). `ModelHealthSingletonTests.Health_check_runs_on_only_one_instance` bu dosyada Faz 103'ten beri kırılgan
  listesindeydi. Değildi: `LeaseDuration=1 sn` ile `SingletonGuard`'ın yenileme aralığı kiranın süresine EŞİTTİ, kira t≈1 sn'de
  sahibi çalışırken düşüyor ve ikinci örnek devralıyordu. Test yalnız 500 ms'lik penceresi o ana ULAŞMADIĞI için geçiyordu; CI'da
  yük altında pencere taşınca (test süresi 1,24 sn) gerçek kusur ortaya çıktı. **Ayırt etme yöntemi**: testi tekrar koşmak yetmez —
  testin geçmesini sağlayan zamanlama varsayımını bul ve onu BÜYÜT. Pencere 1500 ms yapılınca 3/3 kırmızı, aynı sayılarla
  (`providerA=16, providerB=9`): deterministik, yani kırılgan değil. Düzeltme ürün tarafındaydı (validator kısa kirayı reddediyor);
  test artık saatten bağımsız (kira 5 dk, süresi testin içinde dolamaz). Gerçek zamanlı pencere kullanan bir test yazarken kural:
  **pencere uzarsa iddia hâlâ doğru kalmalı**; yalnız pencere kısa olduğu için doğruysa o test bir kusuru saklıyordur.
  Sınıf taraması aynı kurgudan üç tane daha buldu — `McpDiscoverySingletonTests` (birebir aynı: 1 sn kira + 500 ms pencere + XOR),
  `CanaryEvaluationServiceTests`, `RunReconciliationTests` — dördü de 5 dk'lık kiraya geçti. Bir XOR iddiası kurulurken sorulacak soru:
  **bu iddiayı yanlışlayabilecek bir zamanlayıcı var mı, ve testin süresi ona ulaşabilir mi?**
- **Kaynak nedenselliğini ölçme yolu değişmedi.** Bir tam koşum kırılması faz
  değişikliğinden şüphe ettiriyorsa `git stash push -u` → tabanı derle → tam
  koşum → `git stash pop` uygula. Worktree kullanma; extension sample'ları yerel
  NuGet feed'ini ister ve `artifacts/package/release` worktree'de yoktur.

## 🚨 `HttpListener`'ı iki kez kapatmak BAŞKA testin portunu çalar

`Stop()` ve `Close()` ikisi de uç nokta yöneticisinin prefix-kaldırma yolundan
geçer ve o yol **çıkarken portu bağlayabilir** — gözlenen yığın
`Close → RemoveListener → RemovePrefixInternal → GetEPListener` ile
`Address already in use` verdi. İkisini birden çağırmak, o anda boş port
arayan her testle yarışır.

Kusurun okunması zordur: hata **`Dispose` içinde**, testin kendi iddiaları
geçtikten sonra düşer ve **tek başına koşan test hep yeşildir**. Kırılganlık
gibi görünür; değildir. Ölçüm: tam projede 2/2 düştü, izole 2/2 geçti,
düzeltmeden sonra tam projede 3/3 geçti.

Kural: bir dinleyiciyi **tam olarak bir kez** yık. Sınıf taraması (2026-08-31):
`HttpListener` ve "port 0'ı rezerve et, bırak, sonra bağlan" deseni depoda
**tek dosyadadır** (`ImageAttachmentWriterTests`); başka vaka yok.

## 🚨 Bir global sahte `TimeProvider` kaydı arka plan servisini de dondurur (Faz 128)

`AgentPrismTestHost.StartAsync`'te `services.AddSingleton<TimeProvider>(fakeClock)`
yalnız test edilen KODU değil, **host'un kendi arka plan servislerini** de
etkiler — `JobWorkerBackgroundService`'in poll/kira-yenileme döngüsü AYNI
`TimeProvider`'ı okur. Elle ilerleyen bir sahte saat (`ManualTimeProvider`,
yalnız `Advance()` çağrılınca ilerler) o döngüyü **sonsuza kadar dondurur** —
ölçüldü: `UseScheduling` ile kuyruğa alınan bir `run` 30 saniye boyunca
`Queued`'dan hiç çıkmadı, test zaman aşımına uğradı. Senkron (job worker'sız)
bir senaryo sahte saatle sorunsuz çalışır; kuyruklu/arka-plan bir senaryoyu
test ederken ya **gerçek saat + kısa gerçek süre** kullan (örnek: kısa bir
`MaxDuration` + `Task.Delay` ile gerçekten geciken bir tool), ya da yalnız
SENKRON yolu sahte saatle test et. Vaka: `RunDeadlineTests.Deadline_is_enforced_on_the_queued_durable_run_path_too`.

- **Process testinde bekleme MUTLAK SÜRE değil KOŞUL olmalıdır** (Faz 157). Hazırlık için process'in stdout'a yazdığı bir satır (`HARNESS-READY`), devralma için veritabanının KENDİ `lease_until` değeri beklenir; `Task.Delay(sabit)` yüklü bir ajanda kırılgandır. `ManagedProcess.DisposeAsync` her yolda ağacı öldürür — düşen bir test öksüz worker bırakmaz.
