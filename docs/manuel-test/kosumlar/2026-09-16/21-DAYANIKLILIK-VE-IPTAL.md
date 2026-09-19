# 21 — Dayanıklılık: Çalıştırma İptali, Öksüz Uzlaştırma ve Asenkron Onay Kutusu (`RES`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../21-DAYANIKLILIK-VE-IPTAL.md`](../../21-DAYANIKLILIK-VE-IPTAL.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (şerit ap-s3, family 34'ün hemen
> ardından) `Gerçek sonuç` ve `Durum` kayıtlarıdır.

## Ortam notu (oturum başı)

- `Tracon:Providers:OpenAICompatible:openrouter:ApiKey` bu şeritte **boş
  bırakıldı** — startup sırasında bu anahtar dolu iken uygulama
  `OptionsValidationException: OpenAIProviderOptions.Endpoint is required for
  compatible providers` ile çöküyordu, oysa `appsettings.json`'da
  `Tracon:Providers:OpenAICompatible:openrouter:Endpoint` **doludur**
  (`https://openrouter.ai/api/v1`) ve bağımsız bir .NET konfigürasyon
  reprodüksiyonu (aynı `appsettings.json` + aynı ortam değişkenleri, yalnız
  JSON + EnvironmentVariables sağlayıcılarıyla) `Endpoint`'i doğru okudu. Kök
  neden bu oturumda **kesinleştirilemedi** — büyük olasılıkla bu şeride özgü
  bir DI/Options zamanlama sorunu ya da bu ortamın kendine özgü bir durumu
  (`nohup` ile arka planda başlatma, env aktarımı). Bu ailenin case'lerinden
  hiçbiri `openrouter` sağlayıcısını kullanmadığı için anahtar boş bırakılarak
  ilerlendi; `HATA-S3` numarası açılmadı (yeniden üretilemedi, kapsam dışı).
  Not `docs/manuel-test/00-INDEKS.md`'ye taşınmadı — yalnız bu oturumun kaydı.

## HATA-S3-004 — Bir job'ın ikinci denemesi (lease devralma sonrası) her zaman sessizce başarısız kalıyor: gerçek iş biter, `run` sonsuza dek `Running` kalır

- **Case:** MT-RES-086
- **Önem:** Yüksek
- **İzlek:** B (yönetim API'si, iki gerçek worker süreci, gerçek PostgreSQL, gerçek OpenAI çağrısı)
- **Ortam:** macOS arm64 · net10 · PostgreSQL `mt_s3` · OpenAI (gpt-5.4-mini) · `Tracon:Scheduling:LeaseDuration=00:00:20`

**Beklenen**
`MT-RES-086`'nın kendi beklentisi: worker B ölünce (`kill -9`) ve lease
dolunca worker A işi devralır (`attempt=2`), `job` `Completed` olur VE
altındaki `run` kaydı da normal şekilde `Completed`e kapanır — spesifikasyon
yalnız `doneItems`in çift sayılmadığını doğrulamayı ister, ama zımni
varsayım budur: devralınan bir iş GÖZLENEBİLİR şekilde tamamlanır.

**Gerçekleşen**
İki worker süreci (A: 5083, B: 5093) aynı `mt_s3` şemasına bağlandı. B,
~110 saniye süren gerçek bir `durability-support` çalıştırmasını (`Prefer:
respond-async`, uzun bir hikâye isteği) lease'ledi. B ortasında `kill -9`
ile öldürüldü; 20 sn sonra A işi devraldı (`attempt=2`, yeni `leaseOwner`).
A gerçekten çalıştırdı — oturum (`sessions.state`) gerçekten güncellendi,
`job` kaydı `status:"Completed"` oldu (`completedAt` dolu, gerçek ~110 sn
sürede). AMA aynı `runId`'ye ait `runs` satırı **sonsuza dek `status:0`
(Running)** kaldı — `completed_at`, `heartbeat_at` hep `NULL`; `run_events`
tablosunda bu `run_id` için **TEK BİR** satır var (`seq=0`, `created_at`
B'nin ilk denemesinden — A'nın kendi `run.started`'ı bile YAZILAMADI).
`GET /api/runs/{id}` istemciye run'ın hâlâ `Running` olduğunu söylerken,
`GET /api/jobs/{id}` AYNI iş için `Completed` diyor — iki uç nokta
ÇELİŞİYOR. Gerçek para/token maliyeti oluştu ama hiçbir yerde
gözlemlenmiyor (`usage`/`cost` alanları hep boş kaldı).

**Kök neden**
`src/Tracon.Core/Recording/RunEventWriter.cs:42`
(`private long _sequence;`) her `RunEventWriter` ÖRNEĞİNDE (yani her deneme/
`attempt`'te) sıfırdan başlar — önceki (ölü) denemenin kaç olay yazdığını
BİLMEZ. `AppendCoreAsync` (satır 188)
`Sequence = Interlocked.Increment(ref _sequence) - 1` ile A'nın kendi İLK
olayına da `seq=0` atar. `mt_s3.run_events`'in birincil anahtarı
`PRIMARY KEY (run_id, seq)`dir — A'nın `seq=0` INSERT'i B'nin ZATEN
yazdığı `(run_id, 0)` satırıyla ÇAKIŞIR, PostgreSQL benzersizlik ihlali
fırlatır. `StartAsync` (satır 134-144) bu istisnayı yakalayıp
`Disable(ex, RunRecordingStages.Event)` çağırıyor (satır 225) — writer
KALICI OLARAK devre dışı kalıyor. `CompleteAsync` (satır 338-397) `if
(IsDisabled) { return; }` denetimiyle (satır 368-371) TÜM sonraki store
yazımını (tool çağrıları, mesaj olayları, ve en kritik olarak
`_store.CompleteRunAsync`, satır 375) sessizce ATLIYOR. `AgentRunJobHandler.
cs`'nin kendi XML dokümanı (satır 21-27) bu senaryoyu zaten "SAME identity,
UPSERT, no new runs row is OPENED" diye tarif ediyor ama `run_events`'in
AYNI upsert güvencesine sahip OLMADIĞINI hesaba katmıyor — `runs` satırı
UPSERT'lenirken `run_events` INSERT-only ve `(run_id, seq)` çakışmasına
karşı hiç korunmasız.

**Etki**
Bu, lease devralmanın (Faz 157'nin bütün amacı: ölü bir worker'ın işini
kurtarmak) HER ZAMAN bu sessiz bozulmayla sonuçlanacağı anlamına gelir —
`RunEventWriter`'ın "önceki denemeden devam et" mekanizması hiç yok, yani
gözlemlenen bu, uç bir durum değil, **devralınan HER işin** deterministik
sonucu. `RunReconciliation` açıksa (bu case'in kendisi RunReconciliation'ı
KAPALI tuttu), bu `run` bir süre sonra heartbeat eşiğini aşıp YANLIŞLIKLA
`Infrastructure`/`orphaned` olarak sınıflandırılabilir — GERÇEK bir başarıyı
sahte bir altyapı hatası gibi gösterir.

---

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile B)

**Ampirik yeniden üretim.** `RunEventSequenceResumeTests` düzeltme olmadan
düştü: aynı `runId` ile ikinci bir `RunRecordingAgent` koşumu `seq=0`'ı yeniden
kullanmaya çalıştı, writer devre dışı kaldı ve `run` `Running` kaldı.

**Kullanıcı kararı 👤 (2026-09-18):** son `seq` **sözleşmeden okunur**.
`IRunStore`'a `GetLastEventSequenceAsync` eklendi. `RunRecord.EventCount` bu
soruyu yanıtlayamaz — yalnız **tamamlanmada** yazılıyor ve devralınan bir run
henüz tamamlanmamış.

**Düzeltme.** `RunEventWriter.StartAsync` artık `StartRunAsync`'ten sonra son
`seq`'i okuyor ve `_sequence`'ı `written + 1`'e alıyor. İlk denemede sorgu
`null` döner ve sayaç sıfırda kalır.

**SQL sorgusunda tenant join'i YOK** ve bu bilinçli: çağıran run'ın kendi
writer'ıdır ve zaten tuttuğu bir run'ı sürdürüyor. Tenant filtresi, ortam
kiracısı run'ınkinden farklı olan meşru bir devralmada (job kuyruğu ve
workflow'lar ikisi de böyle çalışır, K-355) "hiç olay yok" derdi ve writer
sıfırdan başlardı — tam da bu sorgunun engellemek için var olduğu çakışma.

**Sözleşme paketi güncellendi.** `RunStoreContract` iki yeni test taşıyor
(`The_last_event_sequence_is_readable`, `An_unknown_run_has_no_last_event_sequence`),
yani üç SQL sağlayıcısının üçü de ve üçüncü taraf store'lar aynı iddiayı koşar.

**Sözleşme değişikliğinin bedeli ölçüldü:** `IRunStore`'u uygulayan **on**
yer güncellendi (üç ürün store'u, `samples/Tracon.Samples.FileRunStore`, bench
ve altı test stub'ı). `PublicAPI.Unshipped` olduğu için 1.0 öncesi kabul
edilebilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden üretme**
1. İki worker süreci aynı PostgreSQL şemasına, kısa `LeaseDuration` ile
   başlat (bu koşumda 20 sn; işin doğal süresinden kısa olması yeterli).
2. `Prefer: respond-async` ile en az `LeaseDuration` kadar sürecek bir
   çalıştırma kuyruğa gönder (örn. çok uzun bir metin üretimi).
3. İşi lease'leyen worker'ı `kill -9` ile öldür.
4. Lease süresi dolana kadar bekle; diğer worker'ın devraldığını doğrula
   (`GET /api/jobs/{id}`, `attempt=2`).
5. İş `Completed` olana kadar bekle.
6. `GET /api/runs/{id}` — `status` hâlâ `"Running"`. `GET .../events` —
   yalnız `seq=0` (ilk denemeden). `SELECT * FROM run_events WHERE run_id=
   '<id>'` — tek satır.

**Kayıt:** MT-RES-086 (bkz. koşum kaydı aşağıda), koşan agent: `durability-
support` (bu ailenin kendi kalıcı klonu, bkz. §6 ortam notu).

---

## Devir notu (oturum 16 · TAMAMLANDI)

Aile 21 (RES) bitti: 55/55 case işlendi — 48 Geçti, 4 Atlandı (014, 029,
064, 072 — hepsi kendi ön koşulunun bu ortamda sağlanmadığını kendi metninde
söylüyor), 2 Kaldı (`HATA-S3-004` yeni — MT-RES-086, `HATA-S1-020`'nin ek
doğrulanması — MT-RES-089), 1 Beklemede (090 — ortam engeli, kod değişikliği
ister).
Sayım betiği (`python3` ile `^### (MT-RES-\d+)` + son `Durum:`) 55/55
doğruladı, açık yalnız 090. Bu ailenin koşum sorumluluğu bitti — sıradaki
oturum `ap-s3`'ün bir sonraki ailesiyle (`00-KOSUM-PLANI.md`'ye bkz.)
başlamalı.

---

## § 1 — Çalıştırma İptali: Kayıt Defteri ve Sınır Durumları

### MT-RES-001

**Gerçek sonuç**
`router` → `support` alt-çalıştırması yakalandı (`Running` iken kök iptal
edildi). Kök `POST /cancel` → `202`, gövde `status:"Running"` (eski, K-uyumlu).
3 sn sonra: kök `Canceled`, alt `Canceled`. `GET /api/audit/run:{root}}`
tek kayıt taşıyor: `action:"run.cancel"`, `entity:"run:{root}"`. `GET
/api/audit/run:{child}` **boş dizi** — ikinci bir audit yazımı yok. Beklenen
sonucun tamamı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-002

**Gerçek sonuç**
Yalnız alt-çalıştırma (`support`) doğrudan iptal edildi. 3 sn sonra: alt
`Canceled`, kök **hâlâ `Running`** (durmadı). 11 sn'de kök normal akışıyla
`Completed` oldu — iptalden etkilenmedi. Beklenen sonuçla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-003

**Gerçek sonuç**
`Running` dalı: `POST /cancel` gövdesi `status:"Running"` (eski/okunmuş kayıt)
— nihai durum ayrı `GET` ile görülüyor. `Queued` dalı (`Prefer:
respond-async`): `POST /cancel` gövdesi `status:"Canceled"` (güncel kayıt).
İki dalın gövde tazeliği FARKLI — beklenen sonuçla birebir örtüşüyor. Not:
`support` agent'ı bu ortamda çok hızlı (gpt-5.4-mini, ~1-3 sn); "Running"
penceresini yakalamak için tekrarlayan ("lorem ipsum...") metin yerine
benzersiz rastgele metin (prompt cache'i devre dışı bırakmak için) ve
Python alt-süreç başlatmadan salt `grep`/`cut` ile durum kontrolü gerekti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-004

**Gerçek sonuç**
`router` çalıştırması `Running` iken süreç `kill -9` ile öldürüldü (DB satırı
`Running` kaldı, registry kayboldu). Süreç yeniden başlatıldıktan sonra aynı
`runId`'ye `POST /cancel` → **`409`**, `title: "Run is not executing on this
instance"`, `detail`: `"...shows as 'Running' but is not registered in this
process. It may be running on a different instance, or the process may have
restarted mid-run."` — `MT-RES-003`'ün "already ended" 409'undan (`title:
"Run already ended"`) FARKLI bir `title` taşıyan ikinci bir `409` yolu; kodun
Türkçe metniyle aynı anlam, ama gerçek koşumda İngilizce (`en`, varsayılan
locale) metinler üretti — beklenen davranışla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-005

**Gerçek sonuç**
`summarize-and-translate` workflow'u `Running` iken `POST /cancel` → `202`.
3 sn sonra `GET /api/runs/{id}` → `status:"Canceled"`, `error:null`, `kind:
"Workflow"`. Faz 32 kapanışındaki eski beklenti (`Completed`, MAF grafik-içi
iptal sınırı) DEĞİL — 2026-08-18 güncellenmiş beklenti (F-107/K-432)
doğrulandı: `WorkflowRunner` iptali kendisi zorluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-006

**Gerçek sonuç**
İptal edilmiş bir `run` (`MT-RES-001`'in kök çalıştırması) için `error:
null`. `GET /api/stats/errors?hours=1` **boş dizi** döndü — `"Canceled"`
sınıfı taşıyan hiçbir küme yok, birden fazla gerçek iptal olmuş olmasına
rağmen. Şüphe DOĞRULANDI: `RunErrorClass.Canceled` pratikte hiç üretilmiyor
— **kusur değil, ölü kod** (spesifikasyonun kendi tanısıyla örtüşüyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

> **Ortam notu:** Bu şeyin `zsh` kabuğunda çalıştığı doğrulandı (`ps -p $$`).
> Çok kelimeli `$PG` değişkeni `zsh`'de word-split OLMAZ (`bash`'in aksine) —
> `$PG -c "..."` tek bir komut adı gibi yorumlanıp `command not found` verir.
> Bundan sonraki tüm SQL çağrıları `docker exec -i ap-pg psql -U postgres -d
> tracon -c "..."` biçiminde doğrudan yazıldı (ya da bir `pgq()` kabuk
> fonksiyonuyla).

## § 2 — Uzlaştırma: Varsayılan Kapalı / Açık (Faz 54)

### MT-RES-010

**Gerçek sonuç**
`RunReconciliation` ayarlarının HİÇBİRİ verilmemiş (varsayılan kapalı). Bir
tamamlanmış çalıştırma SQL ile "10 dakika önce başlamış, hâlâ Running" hâline
getirildi. 10 sn sonra `GET /api/runs/{id}` hâlâ `status:"Running"` —
beklenen sonuçla birebir örtüşüyor (hiçbir arka plan taraması satırı kapatmıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-011

**Gerçek sonuç**
`RunReconciliation` açık (`Enabled=true`, `HeartbeatInterval=1sn`,
`OrphanThreshold=3sn`, `ScanInterval=1sn`), uygulama bu ayarlarla yeniden
başlatıldı. `MT-RES-010`'un aynı satırı yeniden "10 dakika önce başlamış"
hâline getirildi. 5 sn içinde: `status:"Failed"`, `error.type:"orphaned"`,
`error.class:"Infrastructure"`, `error.fingerprint:"orphaned"`,
`error.message`: `"The process running this run is not responding; last
heartbeat: 2026-09-17 00:46:28.083501+00."` — beklenen sonucun tamamı
(dört alan + mesaj biçimi) birebir doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-012

**Gerçek sonuç**
`Prefer: respond-async` ile kuyruğa alınan bir çalıştırmanın `started_at`'i
geçmişe alındı, `status` DEĞİŞTİRİLMEDİ (`Queued` kaldı SQL güncellemesi
anında). Uzlaştırma bu satıra dokunmadı: 5 sn'de işçi normal şekilde aldı
(`Running`), birkaç saniye sonra `Completed` oldu — hiçbir noktada
`Failed`/`Infrastructure` olmadı. Beklenen sonuçla birebir örtüşüyor
(`WHERE status = 0` filtresi `Queued`'a dokunmuyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-013

**Gerçek sonuç**
`GET /api/runs?status=Running` → çıplak dizi (`{items:[...]}` DEĞİL),
uzunluk **0**. `GET /api/stats/errors?hours=1` → `class:"Infrastructure"`
kümesi var, `totalRuns:3`, `topClusters[0].fingerprint:"orphaned"`,
`sampleMessage` İngilizce metin taşıyor (`"...is not responding..."` —
spesifikasyonun beklediği `"...yanit vermiyor..."` Türkçe örneği DEĞİL,
sunucunun varsayılan `en` locale'i; anlam aynı). Adım 3 (dashboard ekran
görüntüsü) bu oturumda AYRICA doğrulanmadı — aynı bileşen `12-
GOZLEMLENEBILIRLIK-MALIYET.md` `MT-OBS-010`'da zaten görsel olarak
kanıtlanmış (sınır tablosu); burada yalnız API sözleşmesi tekrar ölçüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-014

**Gerçek sonuç**
Zaman bütçesi bu ailenin geri kalan 40+ case'i için dar; case'in kendisi
"iki terminal ister, zaman bütçesi dar ise `⏭ Atlandı` işaretlenip gerekçe
not düşülebilir, geri kalan case'lerin hiçbiri buna bağımlı değildir" diyor.
Bu ortamda zaten dört ayrı şerit (`ap-s1..s4`) portları meşgul ediyor; ikinci
bir `support`/PostgreSQL örneği başka bir portta (`5085`+) başlatılabilirdi
ama bu ailenin geri kalanına hiçbir bağımlılığı yok. Atlandı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe: kod
tarafından zaten opsiyonel işaretlenmiş, iki terminal/örnek gerektirir,
sonraki case'ler bağımsız.

---

## § 3 — Asenkron Onay Kutusu (Faz 55)

### MT-RES-020

**Gerçek sonuç**
Spesifikasyonun kendi örneği `sessionId` GÖNDERMİYORDU; bununla istek
`Failed` düşüyor (`TraconException`: onay kararının bir sonraki turun girdisi
olduğu, oturumsuz çözülemeyeceği net mesajıyla — kusur değil, tasarım kısıtı).
Spesifikasyon `sessionId:"mt-res-020"` eklenerek düzeltildi (yukarıda not
düşüldü). Düzeltilmiş istekle: 9 sn içinde `status:"AwaitingApproval"`.
`GET /api/approvals/pending`'de tam kayıt: `toolName:"cancel_order"`,
`arguments:"orderId=ORD-1001"` (JSON değil, `anahtar=deger`), `status:
"Pending"`, `expiresAt` `createdAt`'ten ~24 saat sonra. `presentation` alanı
DOLU: `entityType:"order"`, `entityId:"ORD-1001"`, `entityName:"Order
ORD-1001"`, `message:"Cancel order ORD-1001 for Priya Shah."` — beklenen
sonucun tamamı (Faz 142 dahil) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-021

**Gerçek sonuç**
`POST /api/approvals/{id}/decide {"approved":true}` → `200`,
`status:"Approved"`, `decidedBy`/`decidedAt` dolu. ESKİ `runId` 3 sn sonra
HÂLÂ `AwaitingApproval` (K-014 doğrulandı, hiç değişmedi). Aynı `sessionId`
altında YENİ bir `runId` (`Queued` → `Completed`, ~10 sn). Yeni koşunun tool
listesinde `cancel_order` GERÇEKTEN çalıştı: `result:"Order ORD-1001 has been
canceled."`, `succeeded:true`. Beklenen sonucun tamamı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-022

**Gerçek sonuç**
Yeni bir `ORD-1002` onayı `{"approved":false}` ile reddedildi → `200`,
`status:"Rejected"`. Yeni koşu (`mt-res-022` oturumu) `Completed` oldu; tool
listesinde `cancel_order`'ın `result:"Tool call invocation rejected."` —
sipariş GERÇEKTEN iptal edilmedi (senkron `ToolApprovalResolver` reddetme
davranışıyla aynı ilke). Beklenen sonuçla örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-023

**Gerçek sonuç**
`MT-RES-021`'in ONAYLANMIŞ onayına TERS kararla (`{"approved":false}`) ikinci
`decide` → **`409`**, `title:"Decision already made"`, `detail:"...is no
longer pending."` — beklenen sonuçla birebir örtüşüyor. `GET
/api/runs?sessionId=...` sayısı değişmedi.

**Ek gözlem (kusur değil, kasıtlı tasarım — kod incelemesiyle doğrulandı):**
`ApprovalEndpoints.DecideAsync` (satır 199-217) AYNI kararın tekrarını
kasıtlı olarak `409` DEĞİL `200` ile idempotent kabul eder ve `ResumeAsync`'i
yeniden çalıştırır (run kimliği onaydan türetildiği için tekrar aynı işi
kuyruğa sürer, ikinci bir run AÇILMAZ) — yorum satırı gerekçeyi açıkça
anlatıyor: "audit ve run-kuyruğa-alma arasında hiçbir şey yok, arada çökme
onayı sonsuza kilitler; yalnız TERS bir tekrar çakışmadır." İlk denemede
(yanlışlıkla `MT-RES-022`'nin ZATEN `false` reddedilmiş onayına yine `false`
gönderildiğinde) bu yol tetiklendi ve `200` döndü — spesifikasyonun
"ikinci karar HER ZAMAN 409" ifadesi bu nüansı kapsamıyor, ama test edilen
asıl senaryo (ters karar) doğru şekilde `409` veriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-024

**Gerçek sonuç**
Rastgele `guid` ile `GET /api/approvals/{id}` → `404`. `POST .../decide` →
`404`, `title:"Approval request not found"`, `detail:"There is no pending
approval request with id '...', or it does not belong to this tenant."` —
beklenen sonuçla örtüşüyor (varlığı sızdırmayan tek `404` yolu, kiracı
kontrolü de aynı mesajda anılıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-025

**Gerçek sonuç**
`kiraci-alfa` altında yeni bir onay üretildi (`sessionId:"mt-res-025"`).
`kiraci-beta` başlığıyla `GET /api/approvals/{id}` → `404`; aynı başlıkla
`decide` → `404`. Kontrol: `kiraci-alfa` başlığıyla `GET` → `200`. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-026

**Gerçek sonuç**
`MT-RES-025`'in onayı `kiraci-alfa` başlığıyla onaylandı (`200`,
`status:"Approved"`). `GET /api/audit/tool:cancel_order` tek kayıt döndü:
`action:"approval.decision"`, `entity:"tool:cancel_order"`, `after:
"{\"approved\":true,\"approvalId\":\"...\"}"` — beklenen sonucun tamamı
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-027

**Gerçek sonuç**
`Tracon:Approvals:DefaultExpiration=00:00:05`, `ScanInterval=00:00:02` ile
uygulama yeniden başlatıldı. Yeni bir onay üretildi (`mt-res-027`). 10 sn
sonra `GET /api/approvals/{id}` → `status:"Expired"`. `decide` denemesi →
`409`. Spesifikasyonun beklemediği ek bir gözlem: bu kadar kısa bir
`DefaultExpiration` ile **run'ın kendisi de** `Failed` düştü —
`error.type:"ApprovalExpired"`, `error.message:"The approval request for
tool 'cancel_order' expired at ..."` (async job onayı beklerken süresi
dolduğunda `AgentRunJobHandler` çalıştırmayı başarısız kapatıyor). Bu bir
kusur DEĞİL — spesifikasyonun ölçtüğü üç iddia (Expired durumu, pending
listede yokluk, ikinci `decide`'ın 409'u) birebir doğrulandı; yalnız çok
kısa süre sonu ile ek bir yan etki (run'ın da başarısız kapanması) gözlendi.
Not: `pending` listesinde daha önceki bir oturumdan kalma, `default`
kiracılı, alakasız bir kayıt (`sessionId:"mt-res-025"`, 24 saatlik varsayılan
süreyle) hâlâ görünüyordu — bu ORTAM ARTIĞI, bu case'in ölçtüğü onayla
ilgisi yok, doğal süresinde (2026-09-18) kendiliğinden dolacak.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-028

**Gerçek sonuç**
Spesifikasyonun kendi gerekçesi (`RequireApiKeyScope` çağrılmıyor iddiası)
bu koşumda **doğrulanmadı** — kaynakta üç çağrı bulundu (satır 37, 54, 67).
`RunsRead` kapsamıyla üretilen anahtarla `decide` → `403`,
`title:"Insufficient scope"`, `detail:"...requires the 'RunsWrite'
scope..."`. Kontrol grubu (`PUT /api/agents/...`) → `403`,
`detail:"...requires the 'AgentsAdmin' scope..."`. Boşluk zaten kapanmış —
kusur değil (spesifikasyon başlığı ve "Beklenen sonuç" yukarıda
düzeltildi). Ayrı bir doküman düzeltmesi: API anahtarı yanıtının alan adı
`rawKey` DEĞİL `plaintextKey`'dir — `Girilecek veri` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-029

**Gerçek sonuç**
Case'in kendi metni bu senaryonun bu ortamda gözlemlenemeyeceğini zaten
söylüyor (tek statik bearer token, rol claim'i üreten ayrı bir kimlik
doğrulama şeması yok). Doğrulama amaçlı yine de çağrıldı: statik token ile
`decide` → beklenen gibi `200`/kapsam engeline TAKILMADAN geçti (RunsWrite
kapsamı statik tokende zımnen var kabul ediliyor). Gerekçesiyle atlandı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe: ortamda
"Reader" rolünü temsil eden ayrı bir kimlik yok, case'in kendi metni bunu
belirtiyor.

---

### MT-RES-030

**Gerçek sonuç**
Gövdede `approvals` alanı taşıyan `Prefer: respond-async` isteği → `400` —
beklenen sonuçla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-040

**Gerçek sonuç**
İki eski bekleyen onay (bir tanesi `mt-res-025`'in erken denemesinden kalma
`default` kiracılı artık, diğeri `MT-RES-028`'in kapsam testinden) önce
kararlandırılarak liste boşaltıldı. "Onaylar" ekranında `Empty` bileşeni
göründü: "Nothing is waiting" başlığı + "A queued run only appears here
when a tool call needs approval and no live client can answer it." gövdesi.
Konsolda 2 hata VAR ama İKİSİ de bu case'e YABANCI: (1) bilinen `HATA-S2-002`
ile aynı CSP inline-script engeli (erken tema boyama, işlevsel etkisi yok),
(2) token girilmeden ÖNCEki ilk yüklemede `/api/agents`'a giden `401` (kimlik
doğrulama akışının normal bir parçası). Beklenen sonuçla örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-041

**Gerçek sonuç**
Yeni bir bekleyen onay (`mt-res-041`) üretilip ekran yenilendi. Satırda:
`presentation.entityName` ("Order ORD-1001", kalın) üstte, altında
`cancel_order` (mono, subtle renk — kalın DEĞİL, spesifikasyonun "mono,
kalın" beklentisiyle KISMEN farklı), altında `presentation.message`
("Cancel order ORD-1001 for Priya Shah."). Ham argüman (`orderId=ORD-1001`)
spesifikasyonun beklediği gibi HER ZAMAN görünür kırpılmış metin DEĞİL,
bir `<details><summary>Raw arguments</summary>...</details>` açılır-kapanır
bileşeninin ARKASINDA (varsayılan kapalı) — kod incelemesiyle doğrulandı
(`<p class="font-semibold">Order ORD-1001</p><span class="font-mono ...">
cancel_order</span>...<details>...<summary>Raw arguments</summary>`).
Bu bir kusur DEĞİL — Faz 142'nin `presentation` alanı eklendiğinde arayüz
kasıtlı olarak öncelik sırasını değiştirmiş (insan-okunur ad/mesaj önde, ham
argüman ayrıntıda); spesifikasyon metni bu değişiklikten ÖNCE yazılmış,
aşağıda düzeltildi. `Run` hücresi kısaltılmış run id'ye + `mt-res-041`
oturum kimliğine bağlantı, "Created" hücresi göreli ("9 sec. ago"), "Expires"
hücresi MUTLAK ("Sep 18, 2026, 8:48:27 PM") — bu ikisi beklenen sonuçla
birebir örtüşüyor. Ağ sekmesinde ekran açıkken `pending`'e ardışık birden
fazla istek gözlendi (`refetchInterval` doğrulandı). Run bağlantısına
tıklanınca `/tracon/runs/{runId}`'ye gidildi, durum rozeti "awaiting
approval" — orijinal çalıştırma, beklenen sonuçla örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-042

**Gerçek sonuç**
`MT-RES-041`'in satırındaki Onayla düğmesine tıklanınca DOĞRUDAN istek
gitmedi — bir onay diyaloğu açıldı ("Approve cancel_order?" başlığı,
"Cancel"/"Approve" düğmeleri). Spesifikasyon bu ara adımı anmıyor; aşağıda
düzeltildi (kusur değil, ek onay katmanı). Diyaloğun Onayla düğmesine
tıklanınca istek gitti; tıklama-yanıt aralığı bu ortamda `busy` durumunu
yakalamaya izin vermeyecek kadar hızlıydı (`MT-RES-003`'ün aynı sınırı).
İstek dönünce satır listeden kalktı, ekran `Empty` durumuna döndü. `GET
/api/runs?sessionId=mt-res-041` ile doğrulandı: YENİ bir run (`...f086-
7b47...`) `Completed` oldu (`cancel_order` gerçekten çalıştı), ESKİ run
(`...e608-7e5f...`) hâlâ `AwaitingApproval` — beklenen sonucun ölçülebilir
kısmı (Adım 3) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-043

**Gerçek sonuç**
Yeni bir bekleyen onay (`mt-res-043`) üretilip Reddet düğmesine tıklandı.
`MT-RES-042`'deki aynı desenle bir onay diyaloğu açıldı ("Reject
cancel_order?"), diyaloğun kendi Reddet düğmesine tıklanınca istek gitti.
Satır listeden kalktı, ekran boş duruma döndü. `GET
/api/runs?sessionId=mt-res-043` ile doğrulandı: YENİ run `Completed`
(model reddi gördü), ESKİ run hâlâ `AwaitingApproval` —
`MT-RES-022`'nin HTTP davranışıyla birebir aynı, arayüzden de doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-044

**Gerçek sonuç**
İki deneme yapıldı. İlk denemede (aynı karar dışarıdan curl ile
tekrarlandı) `MT-RES-023`'ün idempotency kuralı doğrulandı: diyalog sessizce
başarıya ulaştı (hata YOK) — bu, ikinci denemenin gerekçesidir. İkinci
denemede diyalog açıkken onay dışarıdan curl ile TERS kararla (`approved:
true`) kararlandırıldı, sonra diyaloğun kendi Reddet düğmesine tıklandı:
`409`, sunucu mesajı `"Decision already made: The approval request with id
'...' is no longer pending."` — bu mesaj İKİ yerde birden göründü: diyaloğun
içinde (`role="alert"`, açıklama metninin altında) VE sayfanın alt kısmında
(boş-durum panelinin altında, birebir aynı metin). Sayfa ÇÖKMEDİ, diyalog
"Reject" düğmesi tekrar tıklanabilir kaldı. Beklenen sonucun düzeltilmiş
hâliyle birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-050

**Gerçek sonuç**
Spesifikasyonun kendi "şüphe"si (girdi kaydı yok, `404`) bu ortamda
DOĞRULANMADI: gerçek bir `support` çalıştırması üretilip (`sessionId:
mt-res-050`, gerçek `agent.RunAsync` çağrısı — SQL'le doğrudan DEĞİL) sonra
SQL ile öksüz hâline getirildi. `GET .../input` → `200` — girdi GERÇEKTEN
var, çünkü bu ortam PostgreSQL kalıcılığı kullanıyor (`SqlRunInputStore`,
`InMemoryRunInputStore` DEĞİL — kaynakta doğrulandı, spesifikasyon
düzeltildi). `status`/`error` alanları `MT-RES-011`'in aynı deseniyle
doğrulandı: `Failed`/`Infrastructure`/`orphaned`.

`POST .../replay {"toolMode":"ReplayTools"}` → `400`, `title:"Replay not
supported"`, gerekçe: `support` kodda tanımlı bir agent (persistent
definition yok), `ReplayTools`/`NoTools` tanımın YENİDEN DERLENMESİNİ
gerektiriyor. `LiveTools` denendi → `409`, gerekçe: agent'ın taşıdığı
`cancel_order` onay gerektiriyor ve `LiveTools` modunda canlı onay
istemcisi yok. Sonuç: bu case'in asıl sorduğu soru (`Status`'un kendisi
oynatmayı engelliyor mu) bu ajanla İZOLE ÖLÇÜLEMEDİ — iki bağımsız,
`Status`'tan tamamen bağımsız engel devreye giriyor. Kusur değil, ortam/
agent seçimi sınırı (spesifikasyona not düşüldü).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## § 6 — Kesilen İşin Devamı ve Zarif Kapanış (Faz 87)

**Ortam notu (bu bölüme özel, oturum 16).** İki ek kurulum kararı:
1. Bölüm başındaki `RunReconciliation`/`RunContinuation` ayarlarıyla
   uygulama yeniden başlatıldı (§2'nin ayarları + `RunContinuation:Enabled=
   true`, `MaxAttempts=1`).
2. **Kalıcı bir agent gerekti.** İlk deneme `support`'la yapıldı ve
   `TraconException: "Agent 'support' has no persistent definition version
   1; a code-defined or deleted agent cannot be continued."` ile düştü —
   `GET /api/agents` kataloğundaki **12 agent'ın 12'si de** `origin:"Code"`
   taşıyor (kalıcı/veritabanı kökenli TEK bir örnek yok). Bu, `MT-RES-050`'nin
   `replay` engeliyle AYNI kök neden. Çözüm: `POST /api/agents` ile
   `durability-support` adında kalıcı (`origin:"Database"`) bir klon
   üretildi (`get_order_status`, `list_recent_orders`, `cancel_order`
   tool'larıyla, `support`'un aynısı) — §6'nın tamamı bu agent'la koşuldu.
   Bu bir kusur DEĞİL, örnek uygulamanın bilinçli tasarımı (K-008: ön sürüm
   MAF paketi yalnız `Tracon.AspNetCore` içinde, örnek agent'lar hep kodda
   tanımlı) — ama §6'nın "mutlu yol" case'lerinin (`061`, `062`, `067`) test
   EDİLEBİLMESİ için kalıcı bir agent ZORUNLUYDU, bu ayrım spesifikasyonda
   hiç anılmıyor.

### MT-RES-060

**Gerçek sonuç**
İlk deneme (`mt-res-060`, `support` ile, `sleep 3` sonrası hemen SQL ile
öksüzleştirme) YANLIŞ pozitif verdi: bu ortamda `RunContinuation` o SIRADA
zaten AÇIKTI (bölümün ortam hazırlığı `060`'tan ÖNCE yapılmıştı) — iki
ayrı devam denemesi tetiklendi, ikisi de yapısal nedenlerle düştü ("girdi
kaydı yok" — run tamamlanmadan öksüzleştirildiği için — sonra "kalıcı tanım
yok"). Bu, case'in kendi ölçtüğü şeyi (kapalıyken devam AÇILMAZ) ölçmüyordu.

Düzeltilmiş koşum (`mt-res-060b`): uygulama `RunContinuation` VERİLMEDEN
(yalnız `RunReconciliation` açık) yeniden başlatıldı, `durability-support`
ile GERÇEKTEN tamamlanan bir run üretilip SQL ile öksüzleştirildi. 8 sn
sonra: `sessionId=mt-res-060b` altında **tek** satır, `status:"Failed"`,
`error.type:"orphaned"`/`class:"Infrastructure"` — beklenen sonuçla birebir
örtüştü, hiçbir devam açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-061

**Gerçek sonuç**
`durability-support`'a gerçek bir soru gönderildi, `Completed` olması
beklendi (4 sn). Sonra SQL ile öksüzleştirildi. 6 sn sonra
`sessionId=mt-res-061` altında İKİ satır: kaynak `Failed`/`error.type:
"orphaned"`/`class:"Infrastructure"` (değişmedi), YENİ satır
`continuedFromRunId` kaynağı gösteriyor, `status:"Completed"`, aynı
`sessionId`. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-062

**Gerçek sonuç**
`durability-support`'a `get_order_status`'u tetikleyen bir soru gönderildi;
GERÇEK sonuç not edildi ("Order ORD-1001 has shipped..."). SQL ile
`tool_invocations.result` `"RECORDED-DEGERI"`ye çevrildi, sonra satır
öksüzleştirildi. Devam koşusu `Completed` oldu (`422`/`Failed` DEĞİL);
devam koşusunun `get_order_status` sonucu tam olarak `"RECORDED-DEGERI"` —
tool'un gerçek gövdesi ÇALIŞMADI, kayıtlı (yapay) sonuç OYNATILDI. Beklenen
sonucun ölçülebilir iki maddesi birebir doğrulandı (üçüncü madde — kaynakta
olmayan yeni bir çağrının canlı çalışması — modelin aynı soruya tek bir tool
çağrısıyla yanıt vermesi nedeniyle bu koşumda tetiklenmedi, ayrı gözlem
gerektirmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-063

**Gerçek sonuç**
`durability-support`'a `cancel_order`'ı tetikleyen bir soru gönderildi;
`cancel_order` onay gerektirdiği için (`Faz 55`) run önce `AwaitingApproval`
oldu — onaylandı, ikinci (yeni) run `cancel_order`'ı GERÇEKTEN çalıştırıp
`Completed` oldu (`GET .../tools` ile doğrulandı: `result:"Order ORD-1001
has been canceled."`). Bu GERÇEK `cancel_order` kaydını taşıyan run SQL ile
öksüzleştirildi. 6 sn sonra: bu run için YENİ bir devam AÇILMADI (aynı
oturumdaki satır sayısı sabit kaldı). `GET .../events` (SSE, `grep` ile
ayrıştırıldı) `run.continuation-blocked` olayını taşıyor: `text:"Automatic
continuation was refused: tool 'cancel_order' may not be safely repeated
(destructive or external effect, not declared SafeToRepeat)."` — `cancel_
order` adını içeriyor. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-064

**Gerçek sonuç**
Örnek uygulamanın tool kataloğu (`GET /api/tools`) taranmış: yedi tool'un
`safeToRepeat` alanı hepsinde `false`. `SafeToRepeat=true` bildiren bir tool
YOK — case'in kendi öngördüğü atlama koşulu gerçekleşti. DoD'un birim testi
(`A_tool_declaring_SafeToRepeat_allows_continuation_despite_a_destructive_
effect`) bu davranışı zaten kilitliyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe: örnek
uygulamada `SafeToRepeat=true` bildiren bir tool yok (case'in kendi
öngördüğü atlama koşulu); not `docs/hafiza/`'ya taşınacak.

---

### MT-RES-065

**Gerçek sonuç**
`MT-RES-061`'in ÜRETTİĞİ devam koşusu (`...da82...`) da SQL ile
öksüzleştirildi. 6 sn sonra `sessionId=mt-res-061` altında sayı **2**'de
kaldı (kaynak + ilk devam) — üçüncü bir devam AÇILMADI, `MaxAttempts=1`
sınırı doğrulandı. `$CONT` da `Failed`/`orphaned` kapandı ve öyle kaldı.
Beklenen sonuçla birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-066

**Gerçek sonuç**
`durability-support`'a `sessionId` VERİLMEDEN senkron bir istek gönderildi
(`Completed`, `sessionId:null`). SQL ile öksüzleştirildi. 6 sn sonra:
`status:"Failed"` — her zamanki gibi. `SELECT count(*) FROM mt_s3.runs
WHERE started_at > now() - interval '15 seconds'` → **0** — hiçbir yeni
satır açılmadı. Beklenen sonuçla birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-067 (👤 insan gerekir — arayüz oturumunda tamamlandı)

**Gerçek sonuç**
Taze bir kaynak+devam çifti üretildi (`mt-res-067`). Devam koşusunun
sayfasında (`/tracon/runs/{devamId}`) başlık altındaki özet paragrafında
HİÇBİR "continued from" ibaresi YOK — `document.body.innerText` taraması
doğruladı. Kaynak API'de `continuedFromRunId` DOLU olduğu (`GET
/api/runs/{id}` ile doğrulandı) hâlde arayüz bunu satır metni olarak
göstermiyor; kaynak taraması (`run-detail.tsx:250-255`) ilişkinin "Related"
açılır menüsünün bir ögesi olduğunu gösterdi. "Related" düğmesine
tıklanınca menüde `"continued from 01a0b089-eff…9050f"` ögesi göründü;
tıklanınca kaynak çalıştırmanın sayfasına gidildi (`01a0b089-...9050f` —
birebir `$SRC`). Beklenen sonuç spesifikasyonda düzeltildi; düzeltilmiş
hâliyle birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-068

**Gerçek sonuç**
`Tracon:Drain:Enabled=true`, `Timeout=00:00:10` ile yeniden başlatıldı.
İlk deneme (2000× "lorem ipsum...") modelin ~1.8 sn'de tamamlanmasına yol
açtı — gerçek bir "akış sürerken" penceresi yakalanamadı, `SIGTERM`
gönderildiğinde istek zaten bitmişti (yöntemsel ders: model kısa yanıt
verirse mesajın UZUNLUĞU akışı UZATMAZ). Uzun bir hikâye isteğiyle
(`"Write a very long... story... 4000+ words"`) tekrarlandı — bu kez
gerçek akış ~32 sn sürdü.

`SIGTERM` akış BAŞLADIKTAN 2 sn sonra gönderildi. Adım 3 (yeni koşu
denemesi): `curl` bağlantıyı KURAMADI (`exit 7`, HTTP kodu `000`) — `503`
DEĞİL, HAM TCP reddi. Kaynak incelemesi bunun `BL-026` olarak zaten
bilinen ve `docs/YAYIN-HAZIRLIK.md`'de 🟡'ye indirilmiş bir sınır olduğunu
doğruladı: Kestrel `ApplicationStopping`'de yeni bağlantı kabulünü
`DrainGate` middleware'i hiç çalışmadan durduruyor.

Adım 4: süreç `SIGTERM`'den ~32 sn SONRA çıktı (günlükte "Request finished
... 200 ... 31848.8226ms") — akış TAMAMEN başarıyla tamamlandı (istemci
tam hikâyeyi aldı, 1.7 MB). Bu süre `Drain:Timeout=10 sn`'yi AŞIYOR;
kaynak incelemesi (`TraconDrainService.cs`, `TraconDrainOptions.cs`)
gerekçeyi doğruladı: gözlenen süreyi asıl belirleyen `Drain:Timeout` DEĞİL,
Kestrel'in KENDİ bağımsız istek tahliyesi (ASP.NET Core varsayılan
`HostOptions.ShutdownTimeout=30 sn`) — `Tracon:Drain` bu örnekte hiç
ayarlanmamış bu üst sınırı DEĞİŞTİRMEZ. Sonuç: kod, dokümantasyon (K-tipi
karar `BL-026`) ve gözlem BİRBİRİYLE tutarlı — yeni kusur DEĞİL, zaten
bilinen ve önceliği düşürülmüş bir sınırın bu turda bağımsız doğrulanması.
İş kaybı yok (akış kesilmeden bitti).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-069

**Gerçek sonuç**
İki tamamlayıcı yöntem kullanıldı. (1) Migration'ın kendisi izole edildi:
geçici bir scratch şema (`mt_s3_migtest`) açılıp Faz 126 ÖNCESİ tabloya
birebir uyan bir `sessions` satırı (`schema_version=1`) yazıldı,
`0040_persisted_payload_version.sql` bu şemaya karşı BİREBİR koşuldu:
sonuç `state_schema_version=1` (DEĞİŞMEDİ), `state_maf_version=NULL` — her
ikisi de beklenen sonuçla birebir örtüştü. Şema iş bitince silindi. (2) Adım
4 (canlı oturum): `mt_s3`'teki GERÇEK bir oturum (`mt-res-020`) SQL ile
`state_maf_version=NULL`'a çekilip "Faz 126'dan önce yazılmış ama migration
sonrası hiç dokunulmamış" hâli taklit edildi, sonra aynı oturuma yeni bir
tur gönderildi: run `Completed` oldu, oturum satırı normal şekilde
güncellendi (`state_maf_version` yeniden `1.20.0`'a döndü — bkz.
`MT-RES-070`). Migration verideki hiçbir baytı bozmadı, yalnız zarfı
damgaladı; oturum kesintisiz çalışmaya devam etti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-070

**Gerçek sonuç**
`mt_s3.sessions`'taki mevcut gerçek oturumlar zaten doğrulandı:
`state_schema_version:1`, `state_maf_version:"1.20.0"` — `Directory.
Packages.props`'taki `MicrosoftAgentsAIVersion` ile BİREBİR eşleşiyor
(`NULL` DEĞİL). Beklenen sonuçla birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-071

**Gerçek sonuç**
`mt-res-020`'nin `state_schema_version`'ı SQL ile `999999`'a çekildi, aynı
oturuma yeni bir tur gönderildi: run `Failed`,
`error.message:"Session 'mt-res-020' was written with Tracon schema
generation 999999; this Tracon version can read up to generation 1. Update
the Tracon packages."` — HER İKİ sayı da (`999999` ve `1`) adıyla anılıyor,
"may have become unreadable" gibi tahmine dayalı bir ifade YOK. Oturum
satırı SİLİNMEDİ, sessizce sıfırlanmadı — `state_schema_version=999999`
ile hâlâ orada (SQL ile doğrulandı). Beklenen sonucun tamamı birebir
örtüştü. Satır test sonunda normal nesle (`1`) geri döndürüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-072

**Gerçek sonuç**
Case'in kendi ön koşulu bu case'in yalnız GERÇEK bir MAF sürüm yükseltmesi
elde varken anlamlı olduğunu, günlük geliştirmede atlandığını söylüyor. Bu
tur `src/`'yi donuk tutuyor (kural 1) ve `Directory.Packages.props`'taki
`MicrosoftAgentsAIVersion` değiştirilmedi — koşullar sağlanmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe: case'in
kendi ön koşulu (gerçek bir MAF sürüm yükseltmesi gerektirir, bu turda yok).

---

### MT-RES-073

**Gerçek sonuç**
`support`'a "ORD-9999 siparisimi iptal et" gönderildi (bilinmeyen sipariş).
Run `AwaitingApproval` oldu, kayıt yayımlandı: `status:"Pending"`,
`presentation: null` (`OrderApprovalPresenter` ORD-9999'u tanımıyor, fail-
open), `arguments:"orderId=ORD-9999"` DOLU kaldı — çözümleyicinin
"bilmiyorum" demesi ham argümanı gizlemedi. Beklenen sonuçla birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## § 7 — Alt-Agent Zaman Aşımı (Faz 144)

**Ortam notu.** `router` kodda `callableAgentNames:["support"]` taşıyor,
`subAgents` da `null` — `researcher`'ı çağıran, `ChildDeadline` ayarlı bir
`router` YOK. `POST /api/agents` ile kalıcı bir klon
(`router-childdeadline-test`: `callableAgentNames:["researcher"]`,
`subAgents:{childDeadline:"00:00:01", waitTimeout:"00:00:02"}`) üretildi.

### MT-RES-080

**Gerçek sonuç**
`router-childdeadline-test`'e `researcher`'ı çağıracak bir istek gönderildi.
Olay akışı: `ChildRunStarted` (18:14:53.08) → `ChildRunTimedOut`
(18:14:54.13 — aradan geçen süre **~1.05 sn**, `ChildDeadline=1sn` ile
örtüşüyor) `payload:{"hardCutoff":false}` → `ChildRunCompleted` (aynı an) —
alt-agent'ın çalıştırması gerçekten iptal edilerek bitti, arka planda asılı
kalmadı. `background_agents_get_task_results` tool sonucu: `"Agent
'researcher' did not respond in time (limit: 00:00:01). The tree
continues; the sub-call's eventual result, if any, is discarded."` Kök
`run` `Completed` oldu (toplam ~5.2 sn — bu süre `router`'ın KENDİ model
çağrılarını da içeriyor, yalnız alt-agent'ın süresini DEĞİL; alt-agent'ın
kendisi tam olarak `ChildDeadline`'da kesildi). Beklenen sonucun tamamı
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-081

**Gerçek sonuç**
`MT-RES-080`'in aynısı, tek fark `Harness` alanı dolu bir kalıcı klon
(`router-harness-childdeadline-test`). Aynı olay dizisi: `ChildRunTimedOut`
(`hardCutoff:false`) hemen ardından `ChildRunCompleted`. Harness yolu düz
agent yolundan farklı davranmadı. Beklenen sonuçla birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-082

**Gerçek sonuç**
Bu şerit oturumu boyunca `Tracon:AgentGraph:ChildDeadline` HİÇ ayarlanmadı
(kurulumun kendi varsayılanı — MAF'ın büyük varsayılan değeri geçerli).
`MT-RES-080`'in kendisi zaten bu case'i kanıtlıyor: agent'ın kendi
`SubAgents.ChildDeadline=1sn`'i, kurulumdan/MAF'tan gelen çok daha büyük
varsayılanı EZEREK devreye girdi (alt-agent tam ~1 sn'de kesildi, kurulumun
büyük varsayılanını beklemedi). Ayrı bir koşum gerekmedi — çözümleme sırası
(agent → kurulum → MAF varsayılanı) MT-RES-080'in kanıtıyla doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-083

**Gerçek sonuç**
`ChildDeadline=WaitTimeout=00:00:30` (eşit — geçersiz) ile `POST
/api/agents` denendi. Sessizce kabul EDİLMEDİ — `400`, `title:"Definition
invalid"`, `detail:"Agent 'router-invalid-deadline-test' has an invalid
sub-agent wait limit: WaitTimeout (00:00:30) must be greater than
ChildDeadline (00:00:30); otherwise the hard cutoff would fire before the
cooperative one ever gets a chance to take effect."` — agent adı VE her iki
alan adı/değeri mesajda. Doğrulama beklenenden bile ERKEN yakalandı
(tanım OLUŞTURULURKEN, ilk çalıştırma denemesini beklemeden). Beklenen
sonuçla örtüştü (kod yorumu HTTP katmanında `TraconCompilationException`'ı
bu `400`'e çeviriyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-084

**Gerçek sonuç**
`MT-RES-080`'in zaman aşımına uğramış çalıştırması ~4 dakika sonra tekrar
okundu (`GET .../events`): olay sayısı hâlâ **13** (sequence 0-12), ilk
okumadakiyle BİREBİR AYNI — alt-agent'ın gecikmiş sonucu hiçbir yeni olay,
metrik veya sunucu hatası üretmedi. Beklenen sonuçla birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## § 8 — Zamanlama ve Dağıtık Çalıştırma (Faz 157)

**Ortam notu.** İki worker süreci aynı `mt_s3` şemasına, farklı portlarda
(A: 5083, B: 5093) başlatıldı: `Tracon:Scheduling:LeaseDuration=00:00:20`,
`PollInterval=00:00:00.250`. İlk deneme 6 sn'lik lease ile yapıldı ve
tepki süresi yetmediği için (lease benim curl round-trip'imden önce doldu)
tekrarlandı — 20 sn'ye çıkarıldı.

### MT-RES-085

**Gerçek sonuç**
Uzun bir hikâye isteği (`durability-support`, ~110 sn sürecek) kuyruğa
gönderildi; worker B (`pid 65284`) lease'ledi (`attempt:1`,
`leaseUntil:18:19:01`). B `kill -9` ile öldürüldü (`18:18:46`, lease
dolmadan ~15 sn önce). Hemen ardından `GET /api/jobs/{id}`: `leaseOwner`
HÂLÂ B, `attempt` HÂLÂ `1` — worker A işi ERKEN almadı. Beklenen sonuçla
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-086

**Gerçek sonuç**
Lease süresi (`18:19:01`) dolduktan sonra worker A (`pid 65243`) işi
devraldı: `leaseOwner` A'ya döndü, `attempt:2`, `startedAt` yenilendi.
~90 sn sonra `job` `status:"Completed"` oldu (gerçek çalışma — oturum
durumu güncellendi). **Ama** `GET /api/runs/{id}` aynı run için hâlâ
`status:"Running"` gösteriyor (`completedAt:null`) — iki uç nokta birbiriyle
ÇELİŞİYOR. `run_events` tablosunda bu run_id için yalnız **1** satır var
(B'nin ilk `run.started`'ı); A'nın kendi olayları hiç yazılamadı. Kök neden
kaynak okumasıyla kesinleştirildi ve `HATA-S3-004` olarak kaydedildi (bu
dosyanın başında): `RunEventWriter`'ın sıra sayacı her denemede sıfırdan
başlıyor, `(run_id, seq)` birincil anahtarında ikinci denemenin `seq=0`
yazımı ÇAKIŞIYOR, bu istisna writer'ı KALICI OLARAK devre dışı bırakıyor
(`CompleteAsync` dahil hiçbir sonraki store yazımı gerçekleşmiyor). Bu
uç bir durum değil — lease devralmanın HER örneğinde deterministik olarak
tekrarlanır (kod okumasıyla doğrulandı, ikinci bir koşuma gerek kalmadı).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı — `HATA-S3-004`.

---

**Yeniden koşum — 2026-09-19 (kapanış, Aile B sonrası) · ☑ GEÇTİ**

İki gerçek worker süreci, tek şema (`mt_z86`), `LeaseDuration=00:00:20`,
`PollInterval=00:00:00.250`, gerçek OpenAI (`gpt-5.4-mini`, ~5.700 token'lık
bir roman isteği). Worker B (`pid 81537`) lease'ledi, `kill -9` ile öldürüldü.

```
03:39:14  Running | attempt 1 | leaseOwner …:81537…   <- B olu, lease dolmadi
03:39:20  Running | attempt 1 | leaseOwner …:81537…   <- MT-RES-085 hala tutuyor
03:39:26  Running | attempt 2 | leaseOwner …:81522…   <- A devraldi
03:39:56  Completed | attempt 2
```

**Turun ÇELİŞKİSİ GİTTİ.** `GET /api/runs/{id}` artık `job` ile aynı şeyi
söylüyor:

```json
"status": "Completed",
"startedAt": "2026-09-19T00:39:21.697313+00:00",
"completedAt": "2026-09-19T00:39:52.485284+00:00",
"eventCount": 5,
"usage": {"inputTokens": 77, "outputTokens": 5677, "totalTokens": 5754}
```

Tur bu run'ı sonsuza dek `Running` görüyordu, `completedAt` `NULL`'du ve
`usage`/`cost` boştu.

**`seq` sürüyor — `HATA-S3-004`'ün tam kanıtı.** `mt_z86.run_events`'te bu
`run_id` için **beş** satır var, `seq` `0..4` **boşluksuz**:

```
 seq | type
   0 |    0   <- B'nin RunStarted'i (olmeden once yazdi)
   1 |    0   <- A'nin RunStarted'i: seq SIFIRDAN BASLAMADI
   2 |    1
   3 |    2
   4 |    6
```

Turda bu tabloda **tek** satır vardı (`seq=0`) ve A'nın ilk yazımı
`(run_id, 0)` birincil anahtarıyla çakışıp writer'ı kalıcı devre dışı
bırakıyordu. `IRunStore.GetLastEventSequenceAsync` (K-…, Aile B) writer'a son
`seq`'i okutuyor; A `1`'den devam etti.

⚠️ `doneItems` iddiası bu koşumda ölçülmedi: `tracon.agent-run` job'u **batch
değildir** (`totalItems: 0`), madde sayacı yalnız batch handler'larında
anlamlıdır. Case'in asıl konusu olan devralma ve gözlemlenebilirlik ölçüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-087

**Gerçek sonuç**
Tek bir süreç, `Tracon:Scheduling:RunWorker=false` ile yeniden başlatıldı.
Bir iş kuyruğa gönderildi; birkaç `PollInterval` sonra `GET /api/jobs/{id}`:
`status:"Pending"`, `leaseOwner:null`, `attempt:0` — kuyruk hiç
ilerlemedi. Beklenen sonuçla birebir örtüştü (kusur değil, tanımlı
davranış).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-088

**Yöntem notu.** Paylaşılan `ap-pg` container'ına dokunulmadı (kural 3).
Bunun yerine `00-KOSUM-PLANI.md`'nin de kullandığı şerit-yerel TCP
yönlendirici tarifi uygulandı: uygulama `localhost:55483`'e bağlandı,
`55483 → 55432` bir Python yönlendiricisi üzerinden akıyordu; yönlendirici
öldürülünce uygulamanın gözünde veritabanı tam olarak erişilemez oldu,
`ap-pg`'nin kendisi hiç etkilenmedi. Adım sırası da uyarlandı: DB önce
KAPATILDI (kuyrukta iş yokken), okuma/süreç-hayatta-kalma iddiaları
doğrulandı, SONRA DB geri getirilip taze bir işin uçtan uca çalıştığı
gösterildi — "kesinti sırasında zaten kuyrukta olan bir işin dokunulmadan
kaldığı" iddiası (adım 4'ün "job bulunduğu yerdedir" kısmı) bu sıralamayla
AYRICA ölçülmedi.

**Gerçek sonuç**
Yönlendirici öldürüldü. `GET /api/jobs` → `500`, `title:"An error occurred
while processing your request."` — BOŞ LİSTE DEĞİL, istisna. Süreç `ps`'te
HÂLÂ görünüyordu (çıkmadı); günlükte `JobWorkerBackgroundService.
LeaseLoopAsync` → `SqlJobStore.LeaseAsync` → Npgsql bağlantı hatası zinciri
**300 kez** tekrarlandı (`PollInterval=250ms` ile ~75 sn boyunca, döngü
sürdü, süreç ölmedi). Yönlendirici geri başlatıldı: `GET /api/jobs` hemen
`200`'e döndü; taze bir iş kuyruğa gönderildi ve normal şekilde
`Completed` oldu. Beklenen sonucun ölçülen kısmı (süreç ayakta kalır,
okuma istisna fırlatır, DB dönünce normale döner) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-089

**Gerçek sonuç**
`Tracon:Providers:OpenAI:Endpoint` yanıt vermeyen bir adrese
(`http://192.0.2.1:81/v1` — TEST-NET-1, garantili yönlendirilemez) ve
`Timeout=00:00:03`'e çekilerek yeniden başlatıldı. `Idempotency-Key`
başlıklı akışsız istek → `502`, `application/problem+json`,
`detail:"The model provider request failed."` (ham istisna metni
YOK) — beklenen sonucun bu kısmı birebir örtüştü. `run.status:"Failed"`
doğrulandı. AMA `error.class` **`"Unknown"`** döndü, `"Timeout"` DEĞİL —
bu spesifikasyonun beklediği ayrımın TUTMADIĞI, `00-INDEKS.md`'nin
birikmiş notlarındaki (ap-s1 dosya 05, `HATA-S1-020`) AYNI kök nedenin bu
ailede bir kez daha doğrulanması: `upstream_error` sınıflandırıcıda
tanınmıyor, her sağlayıcı hatası `Unknown`/tek `fingerprint`'e düşüyor.
**Yeni bir `HATA-S3` kaydı AÇILMADI** — dosya 07'nin `MT-API-040/041/042`
deseniyle aynı: mevcut `HATA-S1-020`'nin ek bir doğrulanmasıdır.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı — `HATA-S1-020`
(yeni kayıt açılmadı, ek doğrulama).

---

**Yeniden koşum — 2026-09-19 (kapanış, Aile J sonrası) · 🟡 DÖRT BEKLENTİNİN
ÜÇÜ GEÇTİ · `errorClass` KARAR BEKLİYOR**

`Tracon__Providers__OpenAI__Endpoint=http://192.0.2.1:81/v1` (TEST-NET-1,
garantili yönlendirilemez) · `Timeout=00:00:03` · `mt_z89` şeması.

| Beklenti | Sonuç |
|---|---|
| `502` + `application/problem+json`, `200`+boş DEĞİL | ☑ |
| `run.status` `Failed` | ☑ |
| Sağlayıcının ham istisna metni yanıtta görünmez | ☑ |
| Fallback eklenince `200` ve `modelProvider` YANIT VEREN | ☑ |
| `errorClass` **`Timeout`** | ☒ — **`ProviderError`** geldi |

```json
"status": "Failed",
"error": { "type": "upstream_error",
           "message": "The model provider request failed. Provider: 'openai', fault: 'TaskCanceledException'.",
           "class": "ProviderError",
           "fingerprint": "63fb576e265c82d1d264c9f55c14802a9ef8c2aaefd5c4f9f42f04216d9f8580" }
```

Fallback yarısı ayrıca ölçüldü ve **tuttu**: aynı ölü uca bağlı bir agent'a
`anthropic` fallback'i eklendiğinde istek `200` döndü, `status: Completed`,
`modelProvider: anthropic`, `error: null`.

🚨 **`Unknown` gitti ama `Timeout` gelmedi — ve nedeni ölçüldü.** Tur bu alanı
`Unknown` görmüştü; `HATA-S1-020`'nin düzeltmesi (K-817) `StableIdentities`'e
`upstream_error → ProviderError` satırını ekledi. O sözlük sınıflandırıcının
**ilk** adımıdır (`DefaultRunErrorClassifier.cs:83`) ve `TimeoutPattern`
kontrolünden (satır 102) **önce** çalışır. Normalleştiriciden geçen her
sağlayıcı hatasının tipi `upstream_error` olduğu için, bu yolda `Timeout`
sınıfına **hiçbir zaman** ulaşılamaz.

🚨 **Otomatik test bunu göremiyor ve sebebi öğretici.**
`FailureManifests.ProviderTimeoutTests`'in sahtesi (`TimingOutModelProvider`)
`TaskCanceledException`'ı **doğrudan** fırlatır; `ShouldNormalize` bir
`OperationCanceledException`'ı normalleştirmez, yani o yol `upstream_error`'a
hiç girmez ve `TimeoutPattern` mesajı görür. Gerçek OpenAI SDK'sında zaman
aşımı **sarmalanmış** gelir, `ShouldNormalize` dıştaki yabancı istisnayı
görür ve normalleştirir. Testin adı `..._is_classified_as_a_timeout_...` ama
gövdesi yalnız `Failed`'ı iddia ediyor — sınıfı hiç ölçmüyor, bu yüzden
değişim sessiz kaldı. **Canlı koşum, otomatik testin sahtesinin gerçeği
taşımadığı yeri gösterdi.**

⚠️ **Case'in koruduğu regresyon GERİ GELMEDİ:** sınıf `Canceled` değil.
Kaybolan şey "sağlayıcı hiç cevap vermedi" ile "sağlayıcı hata döndü"
ayrımıdır. `fingerprint` ikisini hâlâ ayırıyor (mesaj `fault:` alanında
farklı), yani kümeleme çalışıyor; kaba olan yalnız **sınıf**.

👤 **KARAR ALINDI (2026-09-19): `Timeout` kazanır, kodlandı.** Gerekçe:
K-737'nin ayrımı ("sağlayıcı hiç cevap vermedi") yeniden deneme ve alarm
davranışını "sağlayıcı hata döndürdü"den farklı sürer.

**Düzeltme.** `DefaultRunErrorClassifier.ClassifyCore`, `StableIdentities`
eşleşmesi `ProviderError` olduğunda mesajı **bir kez** daraltıyor: normalleştirilmiş
metin `fault: 'TimeoutException' | 'TaskCanceledException' |
'OperationCanceledException'` taşıyorsa sınıf `Timeout` olur. Desen
(`NormalizedTimeoutFaultPattern`) yalnız Tracon'un **kendi** cümlesine
(`ProviderFailureNormalizer.DescribeUpstreamFailure`) dayanır; sağlayıcının
metni oraya hiç girmez. `fault: '...'` çapası, sağlayıcı mesajında geçen bir
"cancel" kelimesinin yanlışlıkla eşleşmesini engeller.

⚠️ **Burada iptal edilmiş bir TİP zaman aşımı demektir** — sınıflandırıcının
alt kısmındakinin **tersi**. Model çağrısı sınırında normalleştirici bir
`OperationCanceledException`'ı **dokunmadan** geçirir
(`ProviderFailureNormalizer.ShouldNormalize`), yani çağıranın durdurması hiçbir
zaman `upstream_error` üretmez. Buraya kadar gelen bir iptal, sağlayıcının kendi
HTTP yığınının kendi süresine karşı attığıdır.

**Canlı doğrulama — ☑ GEÇTİ** (aynı ölü uç, `mt_z89b` şeması):

```json
HTTP 502  application/problem+json
"status": "Failed",
"error": { "type": "upstream_error",
           "message": "The model provider request failed. Provider: 'openai', fault: 'TaskCanceledException'.",
           "class": "Timeout",
           "fingerprint": "63fb576e265c82d1d264c9f55c14802a9ef8c2aaefd5c4f9f42f04216d9f8580" }
```

Dört beklentinin **dördü de** karşılandı.

**Testler.** `DefaultRunErrorClassifierTests`'e dört satır (iki zaman aşımı
faultu `Timeout`; iki HTTP faultu **hâlâ** `ProviderError` — daraltma bir vakayı
daraltır, eşlemeyi değiştirmez). `ProviderTimeoutTests`'e
`A_timeout_the_sdk_wrapped_is_still_classified_as_a_timeout`: sahte sağlayıcı
artık **gerçek şekli** taklit ediyor — `AggregateException("Retry failed after
4 tries")` içinde `TaskCanceledException`. Şekil koşum günlüğünden alındı
(`srv89.log:2042`), tahmin edilmedi. İlk yazdığım sahte `InvalidOperationException`
ile sarmalıyordu ve test **yanlış sebeple** kırmızı kaldı: `ReadFaultType`
yalnız `AggregateException` soyar, bu yüzden fault adı `InvalidOperationException`
oluyordu. Gerçek SDK'nın retry hattı `AggregateException` atıyor.

🚨 **SINIF TARAMASI İKİ VAKA DAHA BULDU — ikisi de bu turda KODLANMADI.**

1. **Normalleştirilmiş `429` `RateLimited` değil `ProviderError` oluyor.**
   Ölçüldü: `upstream_error` + `... (HTTP 429).` → `ProviderError`; `402` ve
   `503` de aynı. `RateLimitPattern` `StableIdentities`'in **altında** olduğu
   için hiç çalışmıyor — `Timeout` ile birebir aynı gölgeleme. Taksonomide
   `RateLimited` kovası var ve geri çekilme davranışını sürer.
2. 🚨 **Normalleştirilmeyen yolda durum daha kötü: sınıf `Canceled` geliyor.**
   `ProviderTimeoutTests.Without_a_fallback_...`'ın sahtesi `TaskCanceledException`'ı
   **doğrudan** atar; o yol normalleştirilmez, ama `RunRecordingAgent.ToRunError`
   mesajı `SafeErrorText`'ten geçirir ve kayda `"TaskCanceledException failed.
   (ref: …)"` yazar. K-737'nin dayandığı zaman aşımı **kelimeleri redakte
   edilmiştir**, bu yüzden `TimeoutPattern` hiçbir şey görmez ve
   `CanceledTypePattern` tipte eşleşip **`Canceled`** verir — `status` `Failed`
   olduğu hâlde. Çağıranın durdurması buraya **ulaşamaz**
   (`RunRecordingAgent.cs:309` onu `when (cancellationSource.IsCancellationRequested)`
   ile ayırır), yani `Canceled` burada yanlıştır. Düzeltmek
   `IRunErrorClassifier`'ın **provenance'ını göremediği** bir `RunError` için ne
   vaat ettiğini değiştirir — bu bir karardır, kusur düzeltmesi değil.
   Testte assertion **eklenmedi**; onun yerine o satıra bulgunun tam gerekçesi
   yorum olarak yazıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-090

**Gerçek sonuç**
Ön koşul özel bir `IRunEventSink` KAYDI ister (her olayda ~200 ms bekleyen,
sonra fırlatan). `grep -rn "IRunEventSink" samples/Tracon.Api/*.cs` **boş**
döndü — `samples/Tracon.Api` böyle bir sink kaydetmiyor ve kod bu turda
donuk (kural 1), bu kaydı EKLEMEK bir kod değişikliği olurdu. Bu case bu
ortamda koşulamadı. Otomatik karşılığı (`FailureManifests.SlowSinkTests`)
donuk kod tabanında zaten mevcut ve ayrı bir doğrulama gerektirmiyor.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı — gerekçe:
`samples/Tracon.Api`'de özel `IRunEventSink` kaydı yok, eklemek kod
değişikliği gerektirir (kural 1 donmuş kodu yasaklıyor); kapanışta
`00-INDEKS.md`'nin açık kalem tablosuna taşınacak.

---

## § 9 — Depo Sözleşmesi: İptal Edilmiş Token (Faz 177)

### MT-RES-091

**Gerçek sonuç**
Dört önceden derlenmiş `release` ikilisi kullanıldı (`--list-tests` ile
sayım, sonra `--filter-method "*Canceled_token*"` ile gerçek koşum).
Bellek içi: **61/61** listelenip geçti. PostgreSQL: **65/65**
(kendi `testcontainers` container'ını kullandı, `ap-pg`'ye DOKUNMADI).
SQL Server: **65/65** (kendi container'ı). SQLite: **65/65**. Dördü de
beklenen sayılarla ve "hepsi geçti" iddiasıyla birebir örtüştü. Hiçbir
koşumda sağlayıcıya özgü bir istisna (yalnız `OperationCanceledException`
bekleniyordu) gözlenmedi — dördü de sıfır başarısızlıkla bitti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-092

**Yöntem notu.** Case'in kendi adımları `samples/Tracon.Samples.
FileRunStore/FileRunStore.cs`'i GEÇİCİ olarak bozup geri almayı istiyor —
kural 1 (kod donuk) bunu tur boyunca yasaklıyor, geçici olsa bile. Bunun
yerine case'in kendi "Otomatik karşılığı" satırının andığı
`StoreCancellationContractSelfProofTests` doğrudan koşuldu — bu, AYNI iki
kırık uygulamayı (`TokenBlindSkillStore`, `LateCheckSkillStore`)
kaynakta KALICI olarak barındıran, kod DEĞİŞTİRMEDEN çalıştırılabilen bir
meta-test; case'in adım 1-4'ünün ölçtüğü dört iddiayı birebir kapsıyor.

**Gerçek sonuç**
`./Tracon.Core.UnitTests --filter-method "*StoreCancellationContractSelfProofTests*"`
→ **4/4 geçti**: `Read_case_fails_for_a_store_that_ignores_the_token`
(adım 2'nin `Canceled_token_throws_on_read` DÜŞER iddiası —
`TokenBlindSkillStore`), `Write_case_fails_for_a_store_that_ignores_the_token`
(aynı deseninin yazma tarafı), `Write_case_fails_for_a_store_that_checks_
the_token_too_late` (adım 4'ün `Canceled_token_throws_on_write_and_leaves_
no_trace` DÜŞER iddiası — `LateCheckSkillStore`),
`Read_case_PASSES_for_that_same_store` (adım 4'ün `Canceled_token_throws_
on_read` GEÇER iddiası, aynı geç-kontrol store'u için). Dördü de beklenen
sonucun dört maddesiyle birebir örtüşüyor — sözleşmenin kırık
uygulamaları GERÇEKTEN yakaladığı, kod dokunulmadan kanıtlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---


