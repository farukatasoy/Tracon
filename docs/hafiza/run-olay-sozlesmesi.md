# Run Olay ve Terminal Durum Sozlesmesi Tuzaklari

> `RunEventType`/`RunStatus` enum uyeligi, terminal durum siralamasi,
> `JobRecord.Payload` ve olay payload sozlesmesi. RunRecording zinciri,
> `AsyncLocal`, kaynak sahipligi icin: [`cekirdek-calistirma.md`](cekirdek-calistirma.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 156'da `cekirdek-calistirma.md`'den ayrildi: dosya %3 bosluga dusmustu,
> enum/terminal-durum sozlesme temasi calistirma mekanigi temasindan ayrisiyordu.

- **🚨 `JobRecord.Payload` atanmazsa `/api/jobs` TUM listeyi 500 ile dondurur** (Faz 21): `JsonElement` bir struct'tir; atanmazsa `default` olur (`ValueKind = Undefined`) ve serilestirme cokertir — etki tek isle sinirli degil, **liste ucunun tamami** cokar. 1231 test yakalamadi. **Kural**: yeni bir `JobRecord` ureten her kod yolu `Payload` atamalidir. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `RunEventType.MessageCompleted` YALNIZ akissiz yolda yazilir** (Faz 39): akisli yol yalniz `MessageDelta` uretir — ikisini birden TOPLAMA, akissiz yolda mukerrer sayar. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Yeni ARA `RunStatus` üç yeri kırar** (2026-08-07, Faz 46, K-304): `StartRunAsync` aynı `RunId`'de 2. çağrıda PK çakışır (→UPSERT); SSE/iptal defteri yalnız `Running` bilir.
- **🚨 Plandaki "yeni enum degeri N" iddiasi kod okunmadan guvenilmez** (2026-08-19, Faz 70, K-492): plan `ReasoningDelta`'yi 22 diyordu, `ModelFallbackUsed` Faz 62'den beri zaten 22'ydi — gercek bos deger 23. **Faz 151 AYNI tuzagi ayni dosyada tekrarladi**: plan `LoopIterationCompleted`'i 30 diyordu, 30 Faz 144'ten beri `ChildRunTimedOut`'tu (K-703). Uc kez tekrarladi — `RunEventType.cs`'i OKUMADAN sayi yazma. "Son deger" turunden bir sayi asla varsayilmaz, `RunEventType.cs` okunur.
- **🚨 Olay dokümanı, payload'ında OLMAYAN alan vaat edebilir** (2026-08-31): iki vaka; XML **pakete girer**. Ölçüm, kapı ve kapının sınırı: `RunEventPayloadContractTests`.

- **🚨 Bir run'ın birden çok YAZARI vardır; sıra numarası tek başına sıra değildir** (2026-10-11, F-257, K-879): workflow pompası ile MAF yürütücüsü (çocuk agent kök writer'a `ChildRunStarted` yazar), eşzamanlı tool çağrıları, çocuk run'lar aynı writer'a yazar. Numara `Interlocked` ile alınıp store ve sink ayrı adımlarda yazılınca 6, 5'ten önce ulaştı — sink "sequence order" sözü yalandı, tam çözüm koşumunda kırılgan test olarak göründü. `RunEventWriter` artık numara + store + sink'i tek kapı altında yapar; sink içinden aynı writer'a append kendini SONSUZA DEK beklerdi: sink çağrısı süresince bir `AsyncLocal` işaret bunu `InvalidOperationException`'a çevirir (sink devre dışı kalır); işaret yalnız sink varken yazılır. Deterministik düşen test zamanlamaya değil `IsCompleted`'a bakar: ilk append store'da tutulur, ikinci append tamamlanmamış olmalıdır. 🚨 Sekiz append'i store/sink içinde bir BARİYERDE tutup "hepsi gelince bırak" diyen iki eski test kapı altında sonsuza dek asıldı — tam koşum 4 sa 50 dk takıldı, çünkü düzeltmeden sonra yalnız ilgili sınıf koşulmuştu. Eşzamanlılık semantiğini değiştiren her düzeltmeden sonra bariyerli testleri tara (`grep -rn TaskCompletionSource tests/`) ve tam koşumu MTP `--timeout` ile koş.

## 🚨 Terminal durum, ona bağlı kayıtlardan ÖNCE görünür olmamalı (K-541)

`RunRecordingAgent` çalıştırmayı `agent.RunAsync`'in **içinde** kapatır. Çağıranın
o çalıştırmaya bağlı kayıtları (onay satırı, oturum) çağrı **döndükten** sonra
yazması, kendini "bekliyorum" ilan etmiş ama beklediği şey henüz listelenemeyen bir
`run` üretir. F-133 buydu ve kaydı "kırılgan test" diyordu — **değildi**: sıra
sabitti, pencere onay isteyen her kuyruk çalıştırmasında açıktı; kırılgan olan
yalnız tüketicinin oraya bakıp bakmadığıydı.

Sıra sabittir: **oturum → onay satırı → durum.** Ortadakini atlarsan karar
`ApprovalResume` işini kuyruklar ve o iş oturumu bulamaz.

Tamamlamayı çağırana devretme. `CompleteAsync` yalnız durum yazmaz: bitmemiş
tool'ları boşaltır, compaction usage'ını birleştirir, fallback atfını çözer,
maliyeti ve metriği yazar. Doğru dikiş, durum yazılmadan hemen önce koşan bir
kancadır — `TraconRunOptions.BeforePendingApprovalIsPublished`. Kanca akışlı
yolda da koşar; bir genişleme noktası yola göre sessizce farklı davranmamalıdır.

- **🚨 Uretilemeyen bir `enum` uyesi sessiz bir YANLIS BEYANDIR ve tum sevk
  edilen yuzeye yayilir** (2026-08-26, K-627, olculdu). `RunErrorClass.BudgetExceeded`
  "tree veya context budget asildi" diye ilan ediliyordu; hicbir kod yolu onu
  uretemiyordu — `DefaultRunErrorClassifier` hicbir exception'i ona eslemiyor ve
  tree budget tukendiginde `ChildAgentInvoker` **bilerek** exception atmiyor,
  modele metin donduruyor (run basarili biter). Uye yine de OpenAPI belgesine,
  TypeScript semasina, generated istemciye ve **iki dil dosyasina** ulasmisti.
  Ders: bir hata sinifi/durum uyesi eklerken "kim uretiyor" sorusunu kodla
  yanitla — `grep -rn "RunErrorClass.<Uye>" src/` sifir donuyorsa uye yanlistir.
  Kaldirirken sayisal degeri **bosalt, yeniden numaralandirma**: kayitli run'lar
  ve eski istemciler eski anlami tasir. Kapi: `RunErrorClassContractTests`.
- **🚨 `RunEventWriter.CompleteAsync`'in kapanış olayı switch'i yalnız
  `Completed`/`Failed`/`AwaitingInput`'u eşliyordu; `RunStatus.AwaitingApproval`
  default kola düşüp HER onay-bekleyen kökü `RunFailed` + "The run was
  canceled." metniyle yayımlıyordu** (2026-09-04, Faz 142). Kusur
  `RunRecordingAgentOutcomeMatrixTests`'in kendi yorumunda BİLEREK PIN'lenmişti
  ("bu quirk'u iki yoldan biri düzeltmeden diğerini unutursa burada görünür")
  — testi yeşil tutmak asıl hatayı gizliyordu, yalnız yorumu okuyan biri fark
  ederdi. Yeni bir terminal `RunStatus` değeri eklerken switch'in HER dalını
  say: `_ => RunFailed` gibi bir varsayılan kol, adı "iptal" olan bir metni
  ALAKASIZ bir duruma yapıştırabilir. Kapı:
  `RunRecordingAgentOutcomeMatrixTests.AwaitingApproval_run_reaches_the_same_outcome_on_both_paths`
  artık gerçek `RunEventType.RunAwaitingInput`'u ve `Payload`'ı ölçüyor. Tool
  onay sarmalayıcısının yerleşimiyle ilgili ilişkili not (MEAI'nin
  `ApprovalRequiredAIFunction`'ı `GetService` ile bulması):
  [`tool-onay-ve-yetkilendirme.md`](tool-onay-ve-yetkilendirme.md).
- **🚨 Parmak izi normalleştirmesinde her sayı gürültü DEĞİLDİR**
  (2026-09-18, `HATA-S1-020` kapanışında ölçüldu). `ErrorFingerprint`'in
  `NumberPattern`'ı (`\d+`) her sayı dizisini `{n}` yapıyordu; ayrı edici bir
  mesaj eklendikten SONRA bile `HTTP 404` ile `HTTP 500` aynı küme anahtarına
  düşüyordu. Sayı temizliği bir **id**'nin tek hatayı yüzlerce kümeye
  bölmesini engellemek içindir (ölçülmüş: 2000 oluşum → 1368 küme); bir **durum
  kodu** bunun tersini yapar, iki hatayı ayırır. `(?<!\bHTTP\s)` lookbehind'ı
  yalnız onu korur. **Kural:** bir normalleştirme kuralı eklerken "bu değer
  oluşum başına mı değişiyor, yoksa HATAYI mi tanımlıyor?" diye sor — ilkine
  gürültü, ikincisine kimlik denir. K-818.
- **Bir düzeltmenin testi hâlâ kırmızıysa kusur BİR DEĞİL İKİ katmandadır**
  (aynı vaka). Mesaja ayrı edici eklendi, test yine düştü; ikinci katman
  (`ErrorFingerprint`'in kendi normalleştirmesi) ancak o kırmızı sayesinde
  görüldü. Kusur kaydı da, kapanış analizi de bu ikinci katmanı öngörmemişti.

- **🚨 `StableIdentities` sınıflandırıcının İLK adımıdır ve altındaki her
  deseni GÖLGELER** (2026-09-19, `MT-RES-089` canlı ölçümü, K-831 · K-832).
  `upstream_error → ProviderError` eşlemesi eklendiğinde (K-816) normalleştirilen
  **her** sağlayıcı hatası o satırda durur; `TimeoutPattern` ve
  `RateLimitPattern` o trafiği hiç görmez. Ölçülen sonuç: gerçek bir sağlayıcı
  zaman aşımı `Timeout` değil `ProviderError`, gerçek bir `429` `RateLimited`
  değil `ProviderError`. **Kural:** sözlüğe geniş bir kimlik eklerken "bu kimlik
  altında taksonominin DAHA ÖZGÜL bir kovası var mı?" diye sor; varsa daraltmayı
  aynı anda yaz. Zaman aşımı daraltması kodlandı (K-831), `429` bilerek açık
  bırakıldı (K-832).
- **🚨 Sınıflandırıcı, kendisine ulaşmadan REDAKTE EDİLMİŞ bir metni okuyamaz**
  (aynı vaka). `RunRecordingAgent.ToRunError` yabancı bir istisnanın mesajını
  `SafeErrorText`'ten geçirir ve kayda `"<TipAdi> failed. (ref: …)"` yazar —
  K-737'nin "iptal mi zaman aşımı mı" ayrımını taşıyan **kelimeler orada yok**.
  ∴ mesaja bakan her desen normalleştirilmeyen yolda kördür ve
  `CanceledTypePattern` tipte eşleşip `Canceled` verir. **Kural:** bir desen
  `RunError.Message`'a bakıyorsa, o mesajın o noktaya hangi hâlde geldiğini
  ÖLÇ — `SafeErrorText` ve `ProviderFailureNormalizer` ikisi de metni değiştirir.
- **🚨 Sahtenin ŞEKLİ gerçeği taşımıyorsa test yeşil yalan söyler**
  (aynı vaka). `ProviderTimeoutTests`'in sahtesi `TaskCanceledException`'ı
  **doğrudan** atıyordu; `ShouldNormalize` her `OperationCanceledException`'ı
  geçirdiği için o yol hiç normalleştirilmiyor ve üretimdeki yolu hiç
  koşmuyordu. Gerçek SDK zaman aşımını `AggregateException("Retry failed after
  N tries")` içinde sarmalar (şekil koşum günlüğünden alındı). Testin **adı**
  `..._is_classified_as_a_timeout_...` olduğu hâlde gövdesi yalnız `Failed`'ı
  iddia ediyordu, ∴ sınıf sessizce değişebildi. **Kural:** bir sağlayıcı
  hatasını taklit eden sahte, istisnayı gerçek SDK'nın sardığı gibi sarmalı;
  ve testin adı neyi iddia ediyorsa gövdesi onu ölçmeli.
- **🚨 Bir `run` async iterator ile akıyorsa terminal durumu YALNIZ `finally`
  garanti eder** (2026-09-20, A-32). `WorkflowRunner.RunGuardedAsync` `run`'ı
  `writer.StartAsync` ile acar ve `CompleteAsync` ile kapatirdi; ikisi arasinda
  `try/finally` **yoktu**. Async iterator gövdesi yalnizca **biri enumere
  ederken** ilerler: tuketici `await foreach`'ten `break` ederse ya da bir
  istisna bir `yield return`'den gecerek cikarsa, enumerator dispose edilir ve
  gövde **sadece `finally` bloklarini** kosmak icin devam eder. `finally` yoksa
  satir `Running`'de acik kalir. `RunReconciliationService` yetimi sonunda
  kapatir ama **`Failed`** olarak ⇒ kullanicinin iptal ettigi `run`, dakikalar
  sonra, hic olmamis bir basarisizlik olarak raporlanir.
- **Ayni ders agent yolunda ZATEN yaziliydi; workflow yolu onu almadi.**
  `RunRecordingAgent`'in akis yolu bu `finally`'yi iki kusurla (HATA-S4-012,
  HATA-S4-003) sertlestirmis ve yorumuna yazmisti: *"this is an early
  DisposeAsync() by the consumer. It is the LAST chance to write the terminal
  status."* Sonradan eklenen workflow yolu ayni yuzeydi ama desen tasinmadi.
  **Kural:** `run` acan her yuzey — bugun iki tane: `RunRecordingAgent` ve
  `WorkflowRunner` — ayni terminal-durum garantisini tasimak zorundadir; yeni
  bir tane eklenirse once bu iki gövdeye bakilir.
- **Zamanlamaya dayali test kusuru GIZLER.** Bu kusuru once
  `Cancellation_requested_inside_a_function_node_still_records_Canceled` yakaladi:
  yerelde **40/40 gecti**, CI'da windows-latest'te iki kez dustu. Yaris testi
  "kirilgan" diye damgalanmaya acikti. Terk yolu (`break`) ayni deligi
  **deterministik** olarak gosterir (218 ms) — bir yarisi kovalamak yerine ayni
  kusura zamanlamasiz bir yoldan ulasmak, siniflandirmayi tartisma disi birakir.
- **🚨 Önce `Queued` yer tutucu satır, sonra job yazan yol enqueue düşerse satırı
  kapatmalıdır** (2026-10-13, F-432). Ajan kuyruk ucu ve trigger'ın ajan hedefi
  satırı açıp `EnqueueAsync` hatasında bırakıyordu: satır sonsuza kadar `Queued`
  kaldı, trigger tekrarı yeni run kimliği açtığı için hiçbir işçi onu almadı.
  Workflow kuyruk ucu aynı sınıfı elle kapatmıştı; üç yol artık
  `QueuedRunPlaceholder.EnqueueAsync`'ten geçer (`Failed`, güvenli hata metni,
  kapatma da düşerse log). İstisna bilinçlidir: onay devamının run kimliği
  onaydan türetilir, kararı tekrarlamak eksik job'u yazar. Emsal test:
  `RefusingJobStore` (`Infrastructure/`).
- **🚨 Oturumsuz run onay isterse `Failed` kapanır — kural her oturumsuz yolda
  ayrı ayrı uygulanmalıdır** (2026-10-13, F-389). Onay sonraki turun girdisidir ve
  oturum geçmişinden cevaplanır; oturumsuz run `AwaitingApproval` kalır, posta
  kutusu satırı yazılmaz, sonsuza kadar bekler. Kural `AgentRunJobHandler`'daydı;
  batch öğesi ve eval vakası yoktu, batch öğeyi `Completed` raporladı. İkisi artık
  `SessionlessApprovalRun`'dan geçer (öğe/vaka hatası + run `Failed`). Açık sınıf
  üyesi: `/v1/chat/completions` oturumsuz onayda `200` + boş içerik döner (F-448).
  Kanıt fonksiyonel testtir: iş durumu ile run satırı iki ayrı yerde yazılır.
- **🚨 Yeni ayrılmış `CustomType` canlı ajan akışına kendiliğinden girmez** (2026-10-14, Faz 207, K-991): `RunNoticeMirror.IsMirrored` kapalı bir listedir ve depo yalnız kota seçeneği açıksa ya da bir `update` kesilme sebebi taşıdıysa okunur. Yeni bildirim listeye ve kapıya birlikte girer; yalnız listeye eklenen tip kapı açılmayan run'da hiç okunmaz. Liste değişikliği tel sözleşmesidir (uç açıklaması + dört üretilmiş yapıt).
- **"Bildirim yok" iddiası yalnız bildirimi üretebilen yapılandırmada bir şey kanıtlar** (Faz 207 denetimi 🟡1): yapılandırılmış agent kesilmede istisna atar, olay yazmaz; "ara tool turu çerçeve üretmez" iddiası orada hiç kırmızı olamazdı. Düz metin ikizi mutasyonla (dedektör "ilk kesilme sebebi kalır") kırmızıya döndü.
- **🚨 Run'ın KENDİ token'ıyla olay yazmak, durduğu an istisna fırlatır** (2026-10-15, F-550): `WorkflowRunner.PumpAsync` olayı `writer.AppendAsync(draft, linked.Token)` ile yazıyordu. Süre sınırı olay okunduktan sonra ve yazılmadan önce dolunca (MAF kanalı olayı, iptal gözlenmeden önce verir) `_appendGate.WaitAsync` `TaskCanceledException` attı ve akıştan çıktı; kayıt `Canceled` kapandı ama akış kapanış olayı vermedi. CI etiket koşumunda bir kez, yerelde yük altında 200'de 26. Düzeltme adım sınırıyla aynı kural: iptali yakala, `CancelAsync`, `FromCancellation`. Deterministik test kurulamadı (sıra MAF'tadır); bekçi `WorkflowDefinitionRunTimeoutTests`'in yük altında koşumudur.
- **🚨 `switch` vakasının `when` koşulu başarısızsa içerik `default`'a düşer** (2026-10-20, Faz 223). `WriteContentsAsync`'in `default`'u "tanınmayanı `tracon.content` yaz"dır; `case TextContent when RecordMessageDeltas` kapalıyken her metni oraya düşürürdü. Seçeneği vaka GÖVDESİNDE denetle. Kapı: `ContentEventClassificationTests`.
- **🚨 Ayrılmış `Custom` tipin yük muafiyeti AÇIK LİSTEDİR** (aynı faz): `RunEventWriter.KeepsPayloadWithPayloadsOff` yalnız `quota.threshold` ve `output.truncated`'ı sayar; `tracon.content` ve `tracon.workflow.event` `RecordToolPayloads` kapalıyken `null` yazar. `RunEventPayloadSuppressionTests` yalnız `RunEventType.X` sayar, tip adlarını sayamaz: listeyi değiştirirsen `RunEventType` önsözünü elle düzelt.
- **🚨 Writer bir olayda devre dışı kalınca run `Running`'de kalır ve `GET /api/runs/{id}/events` BİTMEZ** (aynı faz, store hatası testi 10 dk asıldı): hata testi olayları `IRunStore.ReadEventsAsync`'ten okur (`NodeUsageStoreFailureTests`).
- **Yük JSON'unda serbest metin içeriden kesilir** (aynı faz): yazıcı yükü şekline bakmadan keser. `ContentEventPayload.CapText` hata metnini ve workflow metin verisini 2048 karakterde keser.
