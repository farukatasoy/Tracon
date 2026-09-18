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

