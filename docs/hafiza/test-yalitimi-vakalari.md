# Test Yalitimi — Vaka Defteri

- **🚨 BEKLEME KOŞULU, İDDİANIN OKUDUĞUNDAN DAR OLMAMALI** (2026-09-14, Faz 166
  kapanış koşumu). İki `LiveVoice` testi tam koşumda düştü, tek başına 1000+
  kez geçti ve ikisinin de sebebi aynıydı: `WaitForAsync` iddianın okuduğundan
  **daha azını** bekliyordu.
  - `LiveVoiceTests:111` — `AppendsOf(...).Count > 0` bekliyordu, ama iddia
    birleştirilmiş metnin `"Look up order 442"` İÇERMESİNİ arıyordu. Cevap
    birkaç append'e yayılır ve **ilki yankılanan prompt'tur**; yük altında
    bekleme transkript yoldayken dönüyordu.
  - `LiveVoiceLifecycleTests:47` — yalnız ÇIKTI transkriptini bekliyordu, ama
    iki iddia vardı: girdi **ve** çıktı. Bekleme girdi yazılmadan dönebiliyordu.
  Kural: `WaitForAsync(...)` içindeki ifade, altındaki her `Should*`'un okuduğu
  şeyi **kapsamalıdır**. Sayı beklemek (`Count > 0`) yalnız iddia da sayıya
  bakıyorsa doğrudur. Tarama: bir dosyadaki her `WaitForAsync`'i altındaki
  iddialarla yan yana oku — bu ikisi o taramayla bulundu, üçüncü bir vaka
  (`LiveVoiceTests:308`) eşleşiyordu ve dokunulmadı.

> Bir testin TEK BASINA gecip TAM kosumda dustugu OLCULMUS vakalar. Ayirt etme
> KURALI ve aktif tuzaklar [`test-yalitimi.md`](test-yalitimi.md)'dedir — once
> orayi oku; buraya yalnizca "bu testi/desen daha once gorduk mu?" diye
> bakilir, bastan sona okunmaz: `grep -n '<TestAdi>' docs/hafiza/test-yalitimi-vakalari.md`.
>
> 2026-09-12'de ayrildi: `test-yalitimi.md` 16.259/16.000 B'ye ulasmisti ve
> asan sey bu bolumdu — her yeni vaka onu buyutur, ayirt etme kurali ise
> sabittir. Emsal: `test-kosum-tuzaklari.md` Faz 101'de ayni sekilde ayrildi.

## Vakalar

- **🚨 Tam `dotnet test Tracon.slnx -c Release --no-build -maxcpucount:1`
  koşumu ara sıra flaky kırılır — izole koşumda hep geçer (Faz 103).** Beş ayrı
  koşumda beş FARKLI test kırıldı: `ImageAttachmentWriterTests` (port çakışması,
  "Address already in use"), `SqliteDialectTests.Polymorphic_JSON_round_trips_intact`
  (`ON CONFLICT` unique constraint hatası), `ModelHealthSingletonTests.Health_check_runs_on_only_one_instance` (**bu vaka 2026-09-09'da
  ÜRÜN KUSURU çıktı — aşağıya bak; sınıfa yanlış yazılmıştı**),
  `OnlineEvalJobHandlerTests.Judge_timeout_cuts_off_the_wait_when_the_judge_ignores_cancellation`
  (20ms iç timeout'a karşı 1s dış test sınırı — thread-pool starvation altında
  50x marj bile tükeniyor), `Tracon.Ui.E2ETests.UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript`
  (Faz 130: `GetByTestId("voice-transcript")` 30s Playwright timeout'una takıldı,
  izole koşumda 2.5s'de geçti), `Tracon.Ui.E2ETests.UiTests.Runs_screen_lists_only_roots_by_default`
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
  bir testle 3/3 kırık). Her host `new Meter(TraconDiagnostics.MeterName)`
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
- **SEKIZINCI vaka (2026-09-16, Faz 177 kapanisi) — ayni test, ama bu sefer
  UC tam kosumda UC FARKLI test dustu.** Kapanis kapisi iki kez kirmizi dondu:
  (1) `ObjectToolAotPackageTests` (`IOException ... Tracon.Abstractions.pdb ...
  being used by another process`), (2) `SqlServer.ConversationBranchTests` +
  `SqlServer.RunScoreStatisticsTests` + `UiTests.Schedule_is_created_...`,
  (3) ayri kosulan `Ui.E2ETests` projesinde
  `Playground_voice_mode_opens_microphone_and_shows_transcript` (60 sn timeout).
  Ikinci `Ui.E2ETests` kosumu **79/79 yesil** (ses testi dahil);
  `Tracon.Package.Tests` izole **53/53**; `SqlServer.IntegrationTests` tam
  **806/806**.
  🚨 **Uc kanittan BIRI tutmadi ve kod yolu okundu** (kural: `test-yalitimi.md`
  Faz 161). Faz `InMemoryVoiceSessionStore`'a iki iptal kontrolu eklemisti, yani
  "degisiklik o yuzeye dokunmuyor" kaniti YOKTU. Okuma sonucu temiz cikti:
  `LiveVoiceSessionHost` yazmayi `catch (Exception)` ile sarar ve loglar;
  `VoiceConversationDriver.WriteRecordAsync` store'a `CancellationToken.None`
  gecer — guard tetiklenemez. Test ayrica `voice-transcript`'i **akistan**
  bekler, store'dan degil. Ders: dokunulmus bir yuzeyde "izole gecti" savunma
  degildir; cagri yolundaki token'in **gercek degeri** okunur.

- **`ObjectToolAotPackageTests` — ayni desenin Native AOT `publish` hali**
  (2026-09-12, F-219/F-220 kusur turu kapanisi).
  `…publishes_under_Native_AOT_without_a_trim_warning_and_runs` ayni degisiklik
  kumesiyle uc tam kosumda: GECTI · DUSTU · GECTI (son kosum 21/21 proje,
  7040 test, 0 dusen; `Tracon.Package.Tests` 53/53). Izole kosum da gecti. AOT
  `publish` tam bir native derlemedir ve `-maxcpucount:1` altinda bile
  Docker/Playwright ile ayni CPU/RAM butcesini paylasir; urun kusuru degildir.

- **🚨 KAPI KOŞARKEN İKİNCİ BİR AĞIR KOMUT KOŞMA — 83 test bu yüzden düştü**
  (2026-09-19, manuel tur kapanışı). `dotnet test Tracon.slnx -maxcpucount:1`
  sürerken aynı makinede `python3 -m unittest discover -s scripts` başlatıldı;
  o paket içinde `dotnet pack` ve bir **yayın provası** koşan testler var
  (çalışma ağacını geçici değiştirir, feed'e paket basar). Sonuç:
  `SqlServer.IntegrationTests` **80** düştü (hepsi aynı belirti —
  `Migration '0037_run_score_message_key' could not be applied: Execution
  Timeout Expired`) ve `Ui.E2ETests` **3** düştü (Playwright `GotoAsync` /
  `ToBeVisible` timeout'u). Ölçüm ayrıştırdı: aynı iki proje **tek başına**
  koşunca `822/822` ve `81/81` geçti, ve süre `6 dk 59 sn → 1 dk 41 sn`
  düştü — dört kat. ∴ kusur kodda değil, **eşzamanlı kaynak çekişmesindeydi**.
  Belirti aldatıcıdır: 80 düşen testin hepsi tek bir migration adını gösterir
  ve gerçek bir migration kusuru gibi okunur. Kural: dört kapı **yalnız
  başına** koşar; başka bir `dotnet` ya da container işi paralel çalışıyorsa
  ölçümün kanıt değeri yoktur.
