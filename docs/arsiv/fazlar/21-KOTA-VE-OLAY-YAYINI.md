# Faz 21 — Hız Sınırı, Kota ve Olay Yayını

> **Durum:** ✅ Tamamlandı (2026-08-03)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-18**, **F-19**
> **Önkoşul:** [Faz 20](20-MALIYET-VE-GOSTERGE-PANELI.md) (para cinsi kota için) · [Faz 17](17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) (webhook teslimi kuyruğu kullanır)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok (21.1 ölçüldü — `System.Threading.RateLimiting` paylaşılan çerçevede) · **Migration:** 0012

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/21-KOTA-VE-OLAY-YAYINI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

İki ayrı iş, tek fazda: ikisi de "Tracon'i dış dünyayla sözleşmeye bağlar".

## Bu Fazda Alınan Kararlar

Tam gerekçeler `docs/KARARLAR.md` içindedir; buradan yalnızca indeks:

| Karar | Özet |
|-------|------|
| **K-158** | Hız sınırı ve kota ayrı mekanizmalardır — biri bellekte, biri veritabanında. Yeni paket yok. |
| **K-159** | Kota yaklaşıktır; eşzamanlılıkta küçük aşım kabul edilir ve dokümante edilir. |
| **K-160** | Teslim Faz 17'nin kuyruğunu kullanır; `IJobStore` geri adımlı beklemeyle genişletildi. |
| **K-161** | Webhook yükü yalnızca özet taşır; mesaj içeriği hiçbir zaman girmez. |
| **K-162** | Kota aşımında devam eden çalıştırma kesilmez. |
| **K-163** | İmza zaman damgasını içerir — yeniden oynatmaya karşı. |
| **K-164** | SSRF koruması `WebhookHttpClient`'ın içine gömülüdür; `IHttpClientFactory` kullanılmaz. |
| **K-165** | Hız sınırının varsayılanı kapalıdır; varsayılan kota da yoktur. |
| **K-166** | 🚨 `JobRecord.Payload` atanmazsa `/api/jobs` tüm listeyi 500 ile döndürür. |
| **K-167** | 🚨 `AllowInsecureHttp` loopback *adresini* de açar, yalnız şemayı değil. |

Ayrıca **K-059** (sır veritabanında durmaz) bu fazda birebir uygulandı:
`webhook_subscriptions` tablosunda sır alanı yoktur.

---

## Açık Sorular — Kapandı

Dördü de kullanıcı kararıyla, önerilen yönde kapandı.

| # | Soru | Karar | Defter |
|---|------|-------|--------|
| 1 | Kota aşımında devam eden çalıştırma kesilsin mi? | **Kesilmez**; yalnız yeni çalıştırma `429` alır | K-162 |
| 2 | Hız sınırı varsayılan değeri olsun mu? | **Varsayılan yok** (`Enabled = false`) | K-165 |
| 3 | Webhook yükünde tam içerik olsun mu? | **Yalnız özet**; içerik isteyen `/api/runs/{id}` çağırır | K-161 |
| 4 | `AllowInsecureHttp` olsun mu? | **Evet**, yalnız loopback — ve loopback *adresini* de açar | K-167 |

Ayrıca planda olmayan, uygulama sırasında çıkan bir soru kullanıcıya soruldu:
**webhook teslimi hangi yeniden deneme mekanizmasını kullansın?** Doküman kendi
içinde çelişiyordu. Karar: `IJobStore` geri adımlı beklemeyle genişletildi (K-160).

---

## Plandan Sapmalar

Üç sapma oldu; üçü de gerekçesiyle karar defterine yazıldı.

### 1. `webhook_deliveries` bir kuyruk değil, geçmiş tablosu (K-160)

Plan hem "Faz 17'nin kuyruğunu kullan, ikinci bir yeniden deneme mekanizması
yazma" diyordu hem de `webhook_deliveries` tablosuna `next_attempt_at` koyuyordu
— bunlar birbirini dışlar. Ayrıca Faz 17'nin kuyruğu geri adımlı bekleme
yapamıyordu: `ReleaseForRetryAsync` gecikme almıyordu ve `MaxAttempts` genel bir
ayardı (3), webhook ise 5 deneme istiyordu.

**Yapılan:** `IJobStore.ReleaseForRetryAsync`'e opsiyonel `retryAfter`,
`JobRecord`'a iş başına `MaxAttempts` eklendi. Gecikme `jobs.scheduled_for`'u
ileri taşır ve kiralama sorgusu zaten `scheduled_for <= now` süzer. Tek kuyruk,
tek kiralama, tek işçi kaldı. `next_attempt_at` sütunu **hiç eklenmedi**.

### 2. SSRF koruması istemcinin içine gömüldü (K-164)

Plan "DNS çözümlenir, çözülen IP'ye doğrudan bağlanılır" diyordu ama bunun
*nerede* yapılacağını söylemiyordu. Önce doğrulayıp sonra
`HttpClient.SendAsync(url)` çağırmak bir TOCTOU açığı bırakır: `HttpClient` adı
yeniden çözer ve saldırgan iki çözümleme arasında yanıtı değiştirebilir.

**Yapılan:** Denetim `SocketsHttpHandler.ConnectCallback` içindedir —
doğrulanan adres, soketin bağlandığı adresin ta kendisidir. `IHttpClientFactory`
kullanılmadı: `Microsoft.Extensions.Http` bağımlılığı eklenirdi (K-007) ve
tüketici fabrikayı yeniden yapılandırarak korumayı kaldırabilirdi.

### 3. `AllowInsecureHttp` loopback adresini de açar (K-167)

Plan "yalnız loopback hedefleri için" diyordu; ilk yazımda yalnızca *şema*
açıldı, adres denetimi loopback'i özel ağ sayıp reddetti. Sonuç: yerel bir
dinleyiciye teslim **imkânsızdı** — teslim `Dropped` oldu. Canlı sınamada
yakalandı.

**Yapılan:** `WebhookUrlValidator.IsAllowedTarget` — `AllowInsecureHttp` açıkken
**yalnızca loopback** kabul edilir; `10/8`, `192.168/16` ve `169.254.169.254`
dâhil başka hiçbir özel aralık açılmaz.

---

## Bitiş Ölçütleri (DoD)

Tüm satırlar örnek uygulama **gerçekten çalıştırılarak** doğrulandı
(`samples/Tracon.Api`, gerçek OpenAI çağrısı, yerel webhook dinleyicisi).

- [x] **Kiracı kotası aşıldığında `429` ve anlaşılır `ProblemDetails` dönüyor**

  ```
  $ curl -X PUT .../api/quotas -d '{"agentName":null,"period":"Daily","maxRuns":1}'
  $ curl -X POST .../api/agents/ozetleyici/run -d '{"message":"..."}'   → HTTP 200
  $ curl -X POST .../api/agents/ozetleyici/run -d '{"message":"..."}'   → HTTP 429

  Retry-After: 20558
  {
    "title": "Kota asildi",
    "status": 429,
    "detail": "kiraci geneli icin gunluk calistirma kotasi asildi (1/1).
               Sayac 2026-08-04T00:00:00.0000000+00:00 tarihinde sifirlanir.",
    "quotaMetric": "Runs", "quotaPeriod": "Daily",
    "quotaLimit": 1, "quotaUsed": 1,
    "quotaResetsAt": "2026-08-04T00:00:00.0000000+00:00"
  }
  ```

- [x] **Kullanım sayaçları doğru artıyor; dönem sınırında sıfırlanıyor** — bir
  gerçek çalıştırma dört satır üretti (agent × kiracı geneli, günlük × aylık),
  her biri `runs: 1, tokens: 68`. Dönem dönüşü:
  `QuotaEnforcerTests.Donem_donunce_sayac_sifirlanir`.

- [x] **Hız sınırı yapılandırıldığında çalışıyor, kapalıyken davranış değişmiyor**

  ```
  Tracon__RateLimit__Enabled=true PermitLimit=3 Window=00:05:00
  istek 1 → 200 · istek 2 → 200 · istek 3 → 429 (Retry-After: 300) · …
  ```

  Kapalıyken (varsayılan) 50 ardışık istek hiç `429` almadı.

- [x] **Webhook gerçek bir uca teslim ediliyor; imza doğrulanabiliyor** —
  bağımsız bir Python dinleyicisi HMAC'i kendi hesapladı:

  ```json
  { "event": "run.completed",
    "signature_received": "sha256=65ece24c1832e024ed6c490efd148407cd8e555d7fa3daff6edfc87f90ab1456",
    "signature_expected": "sha256=65ece24c1832e024ed6c490efd148407cd8e555d7fa3daff6edfc87f90ab1456",
    "signature_valid": true,
    "body": { "event": "run.completed", "deliveryId": "019fc8df-5681-…",
              "run": { "runId": "019fc8df-4fd5-…", "agentName": "ozetleyici",
                       "modelId": "gpt-5.4-mini", "status": "Completed",
                       "durationMs": 1665, "inputTokens": 38, "outputTokens": 36 } } }
  ```

  Teslim kaydı: `status=Delivered code=204 attempt=1`. Yükte **mesaj içeriği yok**.

- [x] **Başarısız teslim yeniden deneniyor; eşikten sonra abonelik kapanıyor** —
  merdiven Faz 17'nin kuyruğu üzerinden uygulanır; canlı çıktıda iş
  `maxAttempts=5` taşıyor. Otomatik kapatma:
  `WebhookStoreContract.Basarisizlik_sayaci_artar_ve_esikte_abonelik_kapanir`.

- [x] **Özel ağ adresine webhook gönderilemiyor**

  ```
  https://169.254.169.254/latest/meta-data/  → status=Dropped
     error=Hedef ozel bir ag adresine (169.254.169.254) cozumleniyor; AllowPrivateNetworkTargets kapali.
  https://10.0.0.5/hook                      → status=Dropped
     error=Hedef ozel bir ag adresine (10.0.0.5) cozumleniyor; AllowPrivateNetworkTargets kapali.
  ```

  İkisi de `AllowInsecureHttp=true` **açıkken** reddedildi — loopback izni başka
  aralıkları açmıyor (K-167).

- [x] **Webhook yanıtında ve kaydında hiçbir sır yok** — yanıt yalnızca
  `"secretConfigurationKey": "Tracon:Webhooks:Secrets:yerel"` (anahtarın
  **adı**) döndü. Fazladan gönderilen `secret`/`signingSecret` alanları
  bağlanmadı: `WebhookEndpointTests.Yanit_ve_kayit_hicbir_sir_tasimaz`.

- [x] **Dört doğrulama kapısı sıfır uyarı; sır taraması boş** — `dotnet format`
  iki `IMPORTS` hatası yakaladı (`dotnet build` yakalamamıştı), düzeltildi.

---

## Riskler — Kapanış Değerlendirmesi

| Risk | Önlem | Durum |
|------|-------|-------|
| **SSRF** | Şema + IP aralığı + yönlendirme + zaman aşımı; denetim bağlantı geri çağrısında | ✅ Canlı doğrulandı; 30 birim testi |
| Kota aşımı yanlış hesaplanır | Sözleşme testleri; "yaklaşık" olduğu yazılır | ✅ K-159 dokümante |
| Webhook teslimi ana yolu yavaşlatır | Teslim kuyruğa yazılır, istek içinde gönderilmez | ✅ Yayın iki satır yazıp döner |
| Yeni paket bağımlılığı | Önce ölçüm (21.1) | ✅ Yeni paket yok |
| Kota kullanıcıyı beklenmedik durdurur | Varsayılan kota yok; %80 eşiğinde uyarı olayı | ✅ K-165 |

---

## Sonraki Faza Devir Notu

### Devralınan sözleşmeler

Tam liste yukarıda **Gerçekleşen Public API** bölümündedir. En çok etkilenen üç
nokta:

1. **`IJobStore.ReleaseForRetryAsync` imzası değişti** (K-160). Bu arayüzü
   uygulayan yeni bir depo yazan faz dördüncü parametreyi
   (`TimeSpan? retryAfter`) uygulamalıdır: gecikme `ScheduledFor`'a yazılır,
   ayrı bir sütun yoktur.
2. **`JobRecord.MaxAttempts`** iş başına deneme sınırıdır; `null` genel ayarı
   kullanır.
3. **`IWebhookPublisher`** enjekte edilebilir bir genişleme noktasıdır. Yeni bir
   olay eklemek isteyen faz `WebhookEvents`'e sabit ekler, `WebhookEventPayload`'a
   bir özet kaydı ekler ve `PublishAsync` çağırır.

### 🚨 Henüz yayılmayan olaylar — bilinçli kapsam sınırı

`WebhookEvents` dokuz olay adı tanımlar; bu fazda **üçü** gerçekten yayılıyor:
`run.completed`, `run.failed`, `quota.threshold`. Kalanların sözleşmesi hazırdır
ama yayın noktası ilgili fazın kodundadır ve **bu fazda bağlanmadı**:

| Olay | Nereye bağlanmalı |
|------|-------------------|
| `approval.pending` | Faz 6 — `ToolApprovalAgentDecorator` bekleyen çağrıyı yazdığı yer |
| `workflow.request.pending` | Faz 16 — `WorkflowRunner` `ExternalRequest` ürettiği yer |
| `job.completed` · `job.failed` | Faz 17 — `JobWorkerBackgroundService.ExecuteJobAsync` sonlandırma dalları |
| `eval.completed` | Faz 18 — `EvalJobHandler` koşuyu bitirdiği yer |

Her olayı bağlamak beş ayrı fazın çalıştırma yoluna dokunmayı gerektirirdi.
Bağlamak tek satırlık bir `PublishAsync` çağrısıdır. `WebhookEndpoints` bu adları
**zaten kabul eder**, bu yüzden bir abone bugün abone olabilir ve olay
bağlandığında kendiliğinden almaya başlar.

### Bilinen tuzaklar

- 🚨 **Yeni bir `JobRecord` üreten her kod yolu `Payload` atamalıdır** (K-166).
  Atanmazsa `/api/jobs` **tüm** listeyi 500 ile döndürür; 1231 test yakalamadı.
- 🚨 **SSRF kararı tek yerdedir**: `WebhookUrlValidator.IsAllowedTarget`. Hem
  kaydetme hem bağlantı yolu oradan geçer; kuralı başka yere kopyalamayın.
- 🚨 **`(@p IS NULL OR col = @p)` deseninde parametre açıkça tiplenmelidir**,
  yoksa PostgreSQL `42P08` verir ve hata yalnızca çalışma anında görünür.
- **Migration tablo sayısı testi bilerek sabittir**: 0012 ile 32 → 36.
- **Kota ve olay yayını yalnız kök çalıştırmada işler** (`scope.Depth == 0`).

### Faz 25 (saklama) için

`webhook_deliveries` ve `quota_usage` tabloları temizlemeye muhtaçtır. Teslim
kayıtları her olayda bir satır üretir ve hızla büyür. `quota_usage` geçmiş
dönemleri sonsuza dek tutar — yalnız geçerli dönem sorgulanır, eskiler ölü
veridir.

### Sıradaki faz

[`docs/arsiv/IKINCI-FAZ-YOL-HARITASI.md`](../IKINCI-FAZ-YOL-HARITASI.md) sırasındadır. Bu
fazdan devralınan altyapı: `IWebhookPublisher` (olay yaymak için),
`QuotaEnforcer` (tüketim saymak için) ve geri adımlı bekleme taşıyan iş kuyruğu.
