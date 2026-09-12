# Faz 46 — Dayanıklı Çalıştırma (`202 Accepted`)

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-68** (F-39 bu kalemin içinde yaşar)
> **Önkoşul:** [Faz 43](43-IDEMPOTENCY-KEY.md) — yan etkili tool'un iki kez koşmasına karşı tek savunma. Faz 17'nin iş kuyruğu **hazır**
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** **Yok** — yeni tablo yoktur, `JobKind` ve `RunStatus` yalnız **sona** değer ekler
> **Public API:** büyüyor — bir `JobKind` üyesi, bir `RunStatus` üyesi, bir ayar sınıfı, bir yanıt sözleşmesi. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/46-DAYANIKLI-CALISTIRMA.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün bir çalıştırma HTTP isteğinin ömrüne bağlıdır. İstemci bağlantıyı bırakırsa veya süreç yeniden başlarsa iş kaybolur. Saatler süren bir araştırma agent'ı bu yüzden yazılamaz: tarayıcı sekmesi kapanınca çalıştırma da kapanır. Bu faz, çalıştırmayı istekten **ayırır**.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 `Prefer: respond-async` ile gönderilen istek `202`, `Location` ve
      `Preference-Applied: respond-async` döner
- [x] 🚨 `202`'den **hemen sonra** `GET /api/runs/{runId}` `Queued` döner — `404` **değil**
- [x] İstemci bağlantıyı kapatsa bile çalıştırma **tamamlanır**; `runs` satırı
      `Completed` olur
- [x] `runs.id`, `Location` başlığındaki kimliğe **eşittir**
- [x] `202`'den sonra `/events`'e bağlanan istemci **başlangıç olaylarını görür**
- [x] Başlık **gönderilmeyen** istekte bugünkü SSE davranışı değişmemiştir
- [x] `Prefer: respond-async` + `Idempotency-Key` → **tek** iş, tek `runs` satırı
      (`IdempotencyFilter` gövdedeki `stream` alanına bakar; `/run`'ın gövdesi
      hiç taşımaz, bu yüzden bu uçta akışlı dal zaten hiç tetiklenmez)
- [x] Akışlı istek + `Idempotency-Key` → Faz 43'ün `400`'ü OpenAI uyumlu
      uçlarda (`/v1/responses`, `/v1/chat/completions`) korunur — bu uçlar
      `Prefer: respond-async`'i **tanımaz** (Açık Soru 3 = A), bu yüzden mesaj
      kasıtlı olarak değiştirilmedi (bkz. Plandan Sapmalar madde 8)
- [x] `Enabled = false` iken başlık taşıyan istek `501` alır
- [x] `MaxAttempts = 1` iken kira dolan iş **yeniden denenmez**
- [x] Kota dolu iken kuyruğa alma `429` alır ve iş açılmaz
- [x] `Queued` durumdaki çalıştırma iptal edilebilir
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (gerçek OpenAI çağrısı,
      `support` agent'ı — örnek uygulamada `asistan` adında bir agent yok),
      çıktı bu belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve buraya yazıldı
      (**157,1 KB gzip / 250 KB**, Faz 46 katkısı yalnızca bir `Badge` durumu
      ve iki filtre seçeneği — ayrı ölçülemeyecek kadar küçük)

### Doğrulama komutları (gerçek çalıştırma, 2026-08-07)

`samples/Tracon.Api` gerçek bir OpenAI anahtarıyla (`user-secrets`) çalıştırıldı.
Örnekte `asistan` adında bir agent yok; `support` kullanıldı.

```bash
# 1) Kuyruga al
RESP=$(curl -s -D - -o /tmp/accepted-body.json -X POST \
  http://localhost:5081/tracon/api/agents/support/run \
  -H "content-type: application/json" \
  -H "Prefer: respond-async" \
  -d '{"message":"ORD-2 siparişim nerede?"}')
echo "$RESP" | grep -i "^HTTP/\|^location:\|^preference-applied:"
```
```
HTTP/1.1 202 Accepted
Location: /tracon/api/runs/019fdbc2-cad4-78cf-8292-f5b2a09c36d0
Preference-Applied: respond-async
```
Gövde: `{"runId":"019fdbc2-cad4-78cf-8292-f5b2a09c36d0","jobId":"019fdbc2-cad4-78cf-8292-f5b2a09c36d0", ...}`
— **`jobId` == `runId`**, bilinçli bir tasarım kararı (bkz. Plandan Sapmalar madde 4).

```bash
# 2) HEMEN sorgula — Queued gelmeli, 404 GELMEMELI
curl -s "http://localhost:5081/tracon/api/runs/$RUN_ID" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```
```
Queued
```
1 saniye sonra (gerçek OpenAI çağrısı + `get_order_status` tool'u dahil):
```
Completed
```

```bash
# 3) Olay akisina bagla — baslangic olaylari da gelmeli (K-014 replay)
curl -sN "http://localhost:5081/tracon/api/runs/$RUN_ID/events"
```
```
id: 0
event: run.started
data: {...,"text":"ORD-2 siparişim nerede?",...}
id: 1
event: tool.invoking
data: {...,"toolName":"get_order_status","payload":"orderId=ORD-2",...}
id: 2
event: tool.invoked
data: {...,"payload":"ORD-2 numarali siparis kargoya verildi. Tahmini teslim: 2 gun.",...}
id: 3 / 4
event: message.delta / message.completed
data: {...,"text":"ORD-2 siparişiniz kargoya verilmiş. Tahmini teslimat: 2 gün.",...}
id: 5
event: run.completed
```

```bash
# 4) Kuyruktaki calistirma iptal edilir mi
RESP=$(curl -s -D - -o /tmp/accepted3.json -X POST .../agents/support/run \
  -H "Prefer: respond-async" -d '{"message":"ORD-3 siparişim nerede?"}')
RUN_ID=$(python3 -c "import json;print(json.load(open('/tmp/accepted3.json'))['runId'])")
curl -s -D - -X POST "http://localhost:5081/tracon/api/runs/$RUN_ID/cancel"
```
```
HTTP/1.1 202 Accepted
{"id":"...","status":"Canceled","completedAt":"2026-08-07T10:27:26.909458+00:00", ...}
```
İşçi bu işi hiç almadı (`Pending` iken iptal edildi); `runs` satırı doğrudan
`Canceled`'e kapatıldı, orphan **oluşmadı**.

```bash
# 5) Baslik yokken davranis degismedi mi
curl -s -D - -X POST .../agents/support/run -d '{"message":"ORD-1 siparişim nerede?"}'
```
```
HTTP/1.1 200 OK
Content-Type: text/event-stream
```

Sunucu günlüğünde (`Trace` seviyesi) hiçbir uyarı/hata satırı yok.

---

## Plandan Sapmalar

Plan, "runs satırı kuyruğa alma anında yazılır" (46.3) derken bunun somut nasıl
yapılacağını (hangi API, hangi çakışma davranışı) açık bırakmıştı. Uygulama
sırasında bu boşluk gerçek bir tasarım kararı gerektirdi ve sekiz noktada
plandan sapıldı:

1. 🚨 **`RunStartInfo` yeni bir `Status` alanı kazandı (varsayılan `Running`)
   ve üç SQL sağlayıcısının `InsertRun` deyimi UPSERT'e çevrildi** —
   plan bunu öngörmemişti (dosya listesi `IRunStore`/SQL katmanına hiç
   değinmiyordu). Gerekçe: kuyruğa alma anında yazılan `Queued` satırı ile
   işçinin gerçek çalıştırma başlarken yazdığı satır **aynı `RunId`'yi**
   taşır. Düz bir `INSERT` ikinci çağrıda birincil anahtar çakışması
   üretirdi (`MaxAttempts > 1` yapılandırıldığında bu senaryo gerçekten
   oluşur). `ON CONFLICT (id) DO UPDATE` (Postgres/SQLite) ve
   `UPDATE ... WITH (UPDLOCK, SERIALIZABLE)` + `IF @@ROWCOUNT = 0 INSERT`
   (SqlServer, `UpsertConversation` ile aynı desen) seçildi. `InMemoryRunStore`
   zaten bir sözlük ataması olduğu için doğal olarak upsert davranışı
   gösteriyordu; yalnız olay/araç-çağrısı günlüklerinin ikinci çağrıda
   sıfırlanmaması için küçük bir `isNew` denetimi eklendi.
2. **`TraconAsyncRunOptions` plandaki `Tracon.AspNetCore/` yerine
   `Tracon.Core/Scheduling/`de yaşıyor.** Ölçüldü: `TraconIdempotencyOptions`,
   `TraconRateLimitOptions`, `TraconQuotaOptions` gibi tüm benzer
   ayar sınıfları Core'da yaşar ve `TraconServiceCollectionExtensions.AddTracon`
   içinde merkezi olarak bağlanır (`BindAsyncRun`, aynı `BindIdempotency`
   deseni). Plan bu yerleşik kuralı bilmiyordu.
3. **`AgentRunJobPayload` public record'u yazılmadı; yük ham `JsonElement`
   olarak (`{ runId, message, sessionId }`) kurulur ve elle ayrıştırılır.**
   Ölçüldü: `EvalEndpoints.BuildRunPayload`/`EvalJobHandler.ParsePayload` ve
   `WebhookDeliveryJobHandler` aynı deseni zaten kullanıyor — hiçbiri
   Abstractions'ta bir payload tipi veya `JsonSerializerContext` girdisi
   açmıyor. Yeni bir public tip + kaynak üreteci kaydı eklemek bu yerleşik
   deseni gereksiz yere kırardı.
4. 🚨 **`JobRecord.Id` == `RunRecord.Id`.** Plan ikisini ayrı kimlik olarak
   tasarlamıştı (`AcceptedRunResponse.JobId` "teşhis için" ayrı bir alan).
   Birleştirme bilinçli bir karardır: `GET /api/runs/{runId}` bu sayede
   **hiçbir** iş kuyruğu farkındalığı olmadan çalışır — gerçek `runs` satırı
   `Queued` olarak zaten mevcuttur. `JobId` alanı yine de yanıtta durur
   (teşhis) ama değeri `RunId` ile aynıdır.
5. **Ekler (`AttachmentIds`) ve onay kararları (`Approvals`) kuyruğa alınan
   çalıştırmalarda desteklenmiyor; istek `400` alır.** Gerekçe: ek referansı
   `UriContent` için bir HTTP yol öneki (`prefix`) gerektirir ve bu değer
   yalnızca `MapTracon(prefix, ...)` çağrısı anında bilinir — DI kayıt
   anında (`AddTracon()`) değil. `AgentRunJobHandler` bir singleton
   `IJobHandler`dır ve kayıt anında `prefix`'i alamaz. Onay kararları da
   canlı bir istemci bağlantısı varsayar. İkisi de gelecekte ayrı bir aday
   kalemi olabilir; bu fazda kapsam dışı bırakıldı.
6. 🚨 **`RunEventStream` (`RunEndpoints.cs`) `Queued` durumunu da bekleyecek
   şekilde değiştirildi.** Ölçüldü: orijinal döngü `snapshot.Status != Running`
   olunca akışı kapatıyordu — kuyruktaki bir çalıştırmaya `202`'den hemen
   sonra bağlanan bir istemci, işçi hiç başlamadan akışın kapandığını
   görürdü. DoD'nin "başlangıç olaylarını görür" maddesi bu düzeltme
   olmadan sağlanamazdı. Plan bu dosyayı hiç listelemiyordu.
7. 🚨 **`RunEndpoints.CancelRunAsync`, `RunStatus.Queued` için ayrı bir dal
   kazandı.** Ölçüldü: `IRunCancellationRegistry` yalnız CANLI (işçi
   tarafından gerçekten çalıştırılan) bir yürütmeyi bilir; kuyruktaki bir
   iş için kayıt yoktur. Yeni dal `IJobStore.CancelAsync` ile işi
   kuyruktan iptal eder ve `IRunStore.CompleteRunAsync` ile `runs` satırını
   doğrudan `Canceled`'e kapatır (işçi bu satırı asla kapatmayacağı için).
   DoD'nin "Queued durumdaki çalıştırma iptal edilebilir" maddesi bu
   olmadan karşılanamazdı.
8. **OpenAI uyumlu uçların (`/v1/responses`, `/v1/chat/completions`) paylaştığı
   `IdempotencyFilter`'ın "akışlı istekte 400" mesajı `Prefer: respond-async`
   önermeyecek şekilde DEĞİŞTİRİLMEDİ** — plan bunu önermişti (46.5.1: "mesaj
   artık bir yol gösterir"). Gerekçe: bu öneri yalnız `/api/agents/{name}/run`
   için doğrudur; OpenAI uyumlu uçlar `Prefer` başlığını hiç tanımıyor (Açık
   Soru 3 = A). Aynı mesajı üç ucun paylaştığı tek bir filtrede değiştirmek,
   desteklemediği bir özelliği önerecek şekilde yanıltıcı olurdu.

## Bu Fazda Verilen Kararlar

- **K-304 — Kuyruğa alınan bir çalıştırmanın `runs` satırı, işçinin gerçek
  yürütme satırıyla AYNI birincil anahtarı paylaşır; `IRunStore.StartRunAsync`
  bu yüzden bir UPSERT'tir** *(kullanıcı kararı yok, ölçülmüş teknik zorunluluk)*.
  `RunStartInfo.Status` alanı (varsayılan `Running`) eklendi; üç SQL
  sağlayıcısının `InsertRun` deyimi `ON CONFLICT`/`UPDLOCK` ile upsert'e
  çevrildi. Gerekçe: Faz 46'nın 46.3 bölümü yer tutucu bir `Queued` satırı
  istiyordu ama `StartRunAsync`'in var olan sözleşmesi (düz `INSERT`,
  `Status` her zaman `Running`) aynı kimlikle ikinci çağrıda birincil anahtar
  çakışması üretirdi. Alternatif (job deposunu okuyarak sentetik bir `Queued`
  yanıtı üretmek) hem `GET /api/runs/{id}` hem `/events` uçlarını iş
  kuyruğuna bağımlı kılardı ve kira dolup iş yeniden başladığında istemcinin
  `Location`'ının kalıcı olarak anlamsızlaşmasına yol açardı — ölçülen risk
  daha büyüktü.
- **K-305 — Kuyruğa alınan bir çalıştırmada `Job.Id` ile `Run.Id` bilinçli
  olarak AYNI değeri taşır** *(kullanıcı kararı yok)*. Gerekçe: `GET
  /api/runs/{runId}` ucunun iş kuyruğu farkındalığı olmadan (yalnız `IRunStore`
  okuyarak) doğru cevap verebilmesi için tek yol budur.
- **K-306 — `MaxAttempts = 1` varsayılanı korunur** *(kullanıcı kararı,
  Açık Soru önerisi kabul edildi)*. Gerekçe: "dayanıklı" kelimesi "iş asla
  kaybolmaz" değil "iş sessizce kaybolmaz" demektir; sessizce iki kez koşan
  bir tool hiç koşmayandan kötüdür. Yükseltmek tüketicinin bilinçli tercihidir.
- **K-307 — `TraconAsyncRunOptions.Enabled` varsayılanı `true`'dur**
  *(kullanıcı kararı, Açık Soru 1 önerisi kabul edildi)*. Faz 43'ün
  `TraconIdempotencyOptions.Enabled` kararıyla aynı K1 okuması: başlık
  taşımayan bir istek için hiçbir şey değişmez, kapalı gelseydi başlığı
  gönderen bir istemci korunduğunu sanıp korunmazdı.

## Sonraki Faza Devir Notu

1. 🚨 **F-36 (öksüz çalıştırma uzlaştırması) kapsamı daraldı ama hâlâ
   gerekli.** `StartRunAsync`'in UPSERT olması, `MaxAttempts > 1`
   yapılandırıldığında bir yeniden denemenin AYNI satırı kendiliğinden
   `Running`'e geri getirmesini sağlıyor — bu senaryoda orphan **oluşmuyor**.
   Ama **varsayılan** yolda (`MaxAttempts = 1`, işçi süreci `agent.RunAsync`
   ortasında çökerse) satır `Running`'de sonsuza dek kalır. F-36 hâlâ gerekli,
   ama artık "her çöküş orphan üretir" değil, "yalnız yeniden denemesiz
   çöküş orphan üretir" — aday listesi bu daralmayı yansıtacak şekilde
   güncellenmeli.
2. **Okuma B'nin yolu açık kaldı.** `202` + `Location` sözleşmesi kontrol
   noktası eklendiğinde değişmez. Ölçülen gerçek: MAF kancayı yalnız
   `Microsoft.Agents.AI.Workflows` içinde veriyor
   (`ICheckpointStore<T>`, `CheckpointInfo`, `CheckpointableRunBase`).
   Tracon'in `workflow_checkpoints` tablosu (`parent_id` dahil) taklit
   edilecek desendir.
3. **F-69 (asenkron onay kutusu) bu fazdan sonra doğaldır** — ve bu faz onu
   somutlaştırdı: kuyruğa alınan çalıştırmalarda `Approvals` **bilerek** `400`
   ile reddedildi (bkz. Plandan Sapmalar madde 5). Kuyrukta koşan bir
   çalıştırma onay isterse bugün kimse cevap veremez.
4. **Ekler de aynı nedenle kuyruğa alınan çalıştırmalarda desteklenmiyor**
   (Plandan Sapmalar madde 5) — `AttachmentUriReference` bir HTTP yol
   önekine ihtiyaç duyar ve bu değer yalnız `MapTracon` çağrısı anında
   bilinir, `AgentRunJobHandler`'ın DI kayıt anında değil. Aday listesine
   eklenmesi gereken yeni bir kalem: "kuyruğa alınan çalıştırmalarda ek
   desteği" — prefix'i job payload'ına gömmek veya `IOptions<TraconEndpointOptions>`
   benzeri bir mekanizmayla işçiye ulaştırmak gerekir.
5. **OpenAI uyumlu uçların asenkron sözleşmesi** yeni bir aday kalemidir
   (`background: true` + `response.id`); bu fazın başlığı oraya taşınmadı.
6. **`RunStoreContract`'a eklenen UPSERT testi artık dört sağlayıcının
   (InMemory, Postgres, SQLite, SqlServer) tümünde geçerli bir sözleşmedir.**
   Yeni bir `IRunStore` uygulaması yazan biri bu testi otomatik miras alır.
