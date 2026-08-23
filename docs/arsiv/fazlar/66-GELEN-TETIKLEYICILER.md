# Faz 66 — Gelen Tetikleyiciler

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-65**
> **Önkoşul:** [Faz 17](17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) — iş kuyruğu · [Faz 21](21-KOTA-VE-OLAY-YAYINI.md) — `WebhookSigner` ters yönde kullanılır · [Faz 43](43-IDEMPOTENCY-KEY.md) — tekrar koruması oradan gelir · [Faz 46](46-DAYANIKLI-CALISTIRMA.md) — `JobKind.AgentRun` tetikleyicinin hedefidir · [Faz 53](53-KIRACI-API-ANAHTARLARI.md) — kapsam modeli
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Sql.Shared`, `AgentPrism.PostgreSql`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (yeni `inbound_triggers` tablosu). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor** — bir kayıt tipi, bir depo arayüzü, uç ailesi. `PublicAPI.Shipped.txt` bugün **boş**; ekleme **bugün bedava**
> **Site etkisi:** `guides/background-work.md`, `concepts/runs.md`, yeni `guides/inbound-triggers.md`
> **Manuel test alanı:** [`docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-059\|K-138\|K-178\|K-382\|K-394\|K-395" docs/KARARLAR.md
   ```
   **K-059** (imza `secret`'ı veritabanına yazılmaz — yalnız **adı**),
   **K-138** (zamanlamada çift tetikleme benzersiz kısıtla kapatıldı — aynı
   ders burada tekrarlanır), **K-178** (migration numaraları bağımsız),
   **K-382** (kiracı listede yoksa sessizce varsayılana **düşmez**),
   **K-394** (workflow çalıştırmaları kota kapısından geçer — tetikleyici de
   geçmelidir), **K-395** (kimlik doğrulaması olmayan gruplarda başlık nötr
   davranışı)
3. [`21-KOTA-VE-OLAY-YAYINI.md`](21-KOTA-VE-OLAY-YAYINI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/21-KOTA-VE-OLAY-YAYINI.md
   ```
   Giden webhook'un imza sözleşmesi devralınır; bu faz onu **ters yönde**
   kullanır.
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/aspnetcore-di.md`](../../hafiza/aspnetcore-di.md) (kimlik doğrulamasız uç
   grubu ve filtre sırası), [`hafiza/postgresql.md`](../../hafiza/postgresql.md)
   (benzersizlik indeksi), [`hafiza/frontend.md`](../../hafiza/frontend.md)
   (tetikleyici ekranı)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](../../MIMARI-GUVENLIK.md) · [`MIMARI.md`](../../MIMARI.md) — çalıştırma yolu bölümü

---

## Amaç

Faz 21 **giden** webhook'u verdi: AgentPrism dış dünyaya olay yollar. Tersi
yoktur. Bir Slack mesajı, bir e-posta, bir kuyruk olayı bir çalıştırma
başlatamaz. Agent yalnız **sorulunca** konuşur; olaya tepki veremez.

- **F-65** — İmzalı gelen uç, olay → agent eşlemesi ve Faz 17'nin kuyruğuna
  düşürme.

Karar (kullanıcı, 2026-08-18): **yeni `inbound_triggers` tablosu.**
Tetikleyici çalışma anında tanımlanır ve arayüzden yönetilir; `job_schedules`
emsalini izler.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -rln "Trigger" src/AgentPrism.AspNetCore/` | Yalnız zamanlama sözleşmelerinde geçiyor. **Gelen tetikleyici ucu yok** |
| [`WebhookSigner.cs:68`](../../../src/AgentPrism.Core/Webhooks/WebhookSigner.cs) | `Verify(body, timestamp, secret, signature)` **hazır** ve sabit zamanlı karşılaştırma kullanıyor. Ters yön için yeni kriptografi yazılmaz |
| [`JobKind.cs:49`](../../../src/AgentPrism.Abstractions/Scheduling/JobKind.cs) | `AgentRun = 5` vardır (Faz 46). Tetikleyicinin hedefi budur; yeni bir iş tipi gerekmez |
| [`JobKind.cs:18`](../../../src/AgentPrism.Abstractions/Scheduling/JobKind.cs) | `Workflow = 1` — tetikleyici workflow'u da hedefleyebilir |
| [`IIdempotencyStore.cs:22`](../../../src/AgentPrism.Abstractions/Idempotency/IIdempotencyStore.cs) | Tekrar koruması için depo **hazır** (Faz 43) |
| [`0008_scheduling.sql:31-46`](../../../src/AgentPrism.PostgreSql/Migrations/0008_scheduling.sql) | `job_schedules` şeması bu tablonun emsalidir: kiracı, ad, hedef, `enabled`, benzersiz `(tenant_id, name)` |
| K-138 | Çift tetikleme zamanlamada **benzersiz kısıtla** kapatıldı; aynı ders bu fazda tekrar edilir |

> Kanıtlar 2026-08-18 tarihinde doğrulandı.

---

## 66.1 — Akış

```mermaid
sequenceDiagram
    participant D as Dis sistem
    participant E as POST /api/triggers/{tenant}/{ad}
    participant S as inbound_triggers
    participant Q as Is kuyrugu
    participant A as Agent
    D->>E: imzali govde + zaman damgasi
    E->>S: tetikleyici bul - etkin mi
    E->>E: imza dogrula - WebhookSigner.Verify
    E->>E: zaman penceresi ve tekrar denetimi
    E->>Q: JobKind.AgentRun kuyruga
    E-->>D: 202 Accepted + runId
    Q->>A: calistirma kuyruktan kosar
```

Uç **her zaman kuyruğa** düşürür ve `202` döner. Senkron çalıştırma
**yoktur**: dış sistem yanıtı beklemez ve uzun bir model çağrısı bir webhook
zaman aşımına takılır. Faz 46'nın `202 Accepted` + `Location` sözleşmesi birebir
kullanılır.

## 66.2 — Kimlik: imza, bearer token değil

Gelen uç **kimlik doğrulaması olmadan açılamaz**. Ama dış sistem bir yönetim
token'ı taşıyamaz; taşırsa o token dış sistemde durur.

Kimlik **imzadır**:

- Her tetikleyicinin kendi imza `secret`'ı vardır.
- 🚨 **K-059:** kayıtta yalnız `secret`'ın **yapılandırma anahtarı adı** durur.
  Değer `IConfiguration`'dan çözülür. Faz 65'in önek kısıtı burada da
  uygulanır.
- İmza `WebhookSigner.Verify` ile doğrulanır — sabit zamanlı karşılaştırma
  hazırdır.
- Zaman damgası bir **pencere** içinde olmalıdır (varsayılan beş dakika);
  pencere dışındaki istek reddedilir. Bu tekrar saldırısına karşı ilk
  savunmadır.
- İkinci savunma `IIdempotencyStore`'dur: aynı imza ikinci kez kabul edilmez.

🚨 **Kiracı yoldan gelir ama doğrulanır.** K-382 dersi birebir geçerlidir:
bilinmeyen bir kiracı sessizce varsayılana **düşmez**, `404` alır. Yanıt
"tetikleyici yok" ile "imza yanlış" arasında **fark göstermez** — ayrım bir
numaralandırma yüzeyidir.

## 66.3 — Olay gövdesi agent'a nasıl geçer

🚨 **K2 gevşemez: şablon dili yoktur.** Gelen JSON'dan mesaj üretmek için
yalnız iki seçenek vardır:

| Mod | Davranış |
|---|---|
| `WholeBody` (varsayılan) | Gövdenin tamamı JSON metni olarak mesaj olur |
| `Path` | Noktalı bir yol (`event.text`) ile tek bir alan seçilir |

Yol biçimi Faz 63'ün koşul yolu ile **aynıdır**; iki yerde iki farklı yol dili
olmaz. Yol çözülemezse çalıştırma başlatılmaz ve `400` döner.

Dönüşüm kodu yazmak isteyen tüketici bunu kendi tarafında yapar ve gövdeyi
hazır gönderir. Sunucu tarafında ifade çalıştırmak K2'nin ihlalidir.

## 66.4 — Kota, kiracı ve denetim

- Tetikleyici çalıştırması **kota kapısından geçer** (K-394 emsali). Dış bir
  olay kotayı bypass edemez; aksi hâlde kota anlamsızdır.
- Kuyruğa düşen iş kiracı kapsamında koşar (`AmbientTenantScope`, Faz 17
  deseni).
- Tetikleyici tanımının her yazımı denetim izine girer (K-089: mutasyondan
  **önce**).
- Kabul edilen her olay bir `run` kaydı üretir; kaynağı (`trigger:<ad>`)
  görünür olmalıdır.

## 66.5 — Hız sınırı

Gelen uç internete açıktır. Tetikleyici başına bir hız sınırı **zorunludur**;
sınırsız bir uç kuyruğu doldurur ve kotayı tüketir.

🚨 Sınır **bellekte** yaşar (K-158). Dağıtık sınır bu repoda bilerek alınmadı
ve bu faz o kararı yeniden açmaz. Çok örnekli dağıtımda sınır örnek başına
uygulanır; belge bunu açıkça yazar.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public enum InboundTriggerTargetKind
{
    Agent = 0,
    Workflow = 1,
}

public enum InboundTriggerPayloadMode
{
    WholeBody = 0,
    Path = 1,
}

public sealed record InboundTrigger
{
    public required Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required InboundTriggerTargetKind TargetKind { get; init; }
    public required string TargetName { get; init; }

    /// <summary>The configuration KEY NAME of the signing secret. Never the value itself.</summary>
    public required string SigningSecretConfigurationName { get; init; }

    public InboundTriggerPayloadMode PayloadMode { get; init; } = InboundTriggerPayloadMode.WholeBody;

    /// <summary>Dotted path used when PayloadMode is Path.</summary>
    public string? PayloadPath { get; init; }

    public bool Enabled { get; init; } = true;
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public interface IInboundTriggerStore
{
    ValueTask<InboundTrigger?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<InboundTrigger>> ListAsync(string tenantId, CancellationToken cancellationToken = default);
    ValueTask UpsertAsync(InboundTrigger trigger, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

public sealed class AgentPrismInboundTriggerOptions
{
    /// <summary>How far the request timestamp may drift. Default five minutes.</summary>
    public TimeSpan TimestampTolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum accepted body size in bytes. Default 256 KB.</summary>
    public int MaxBodyBytes { get; set; } = 256 * 1024;

    /// <summary>Maximum accepted requests per trigger per minute. Default 60.</summary>
    public int MaxRequestsPerMinute { get; set; } = 60;

    /// <summary>Only configuration keys under this prefix can hold a signing secret.</summary>
    public string AllowedConfigurationPrefix { get; set; } = "AgentPrism:TriggerSecrets:";
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol · kapsam | Ne yapar |
|---|---|---|---|
| `POST` | `/api/triggers/{tenantId}/{name}` | **imza** — bearer token yok | Olayı kabul eder, kuyruğa düşürür, `202` + `Location` döner |
| `GET` | `/api/triggers` | Admin · `PlatformRead` | Kiracının tetikleyicilerini listeler |
| `PUT` | `/api/triggers/{name}` | Admin · `PlatformAdmin` | Tetikleyici tanımlar veya günceller |
| `DELETE` | `/api/triggers/{name}` | Admin · `PlatformAdmin` | Siler |

🚨 Kabul ucu kimlik doğrulamasız gruptadır; K-395 dersine göre filtre sırası
uygulama anında **ölçülmelidir**.

### Arayüz payı

Tetikleyici listesi ve düzenleme ekranı eklenir; `secret` **değeri** girilmez,
yalnız yapılandırma adı. Bugünkü kullanım **165,4 KB gzip / 250 KB**; artış
uygulama anında ölçülüp yazılır. Yeni sözlük anahtarları `en.ts` **ve** `tr.ts`
(K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Triggers/
├── InboundTrigger.cs                  (yeni)
├── IInboundTriggerStore.cs            (yeni)
├── InboundTriggerTargetKind.cs        (yeni)
└── InboundTriggerPayloadMode.cs       (yeni)

src/AgentPrism.Core/Triggers/
├── InboundTriggerDispatcher.cs        (yeni - imza, pencere, tekrar, kuyruk)
├── InboundTriggerRateLimiter.cs       (yeni - bellekte, K-158)
└── InboundTriggerPayloadReader.cs     (yeni - WholeBody ve Path)

src/AgentPrism.Core/Storage/
└── InMemoryInboundTriggerStore.cs     (yeni)

src/AgentPrism.Sql.Shared/Stores/
└── SqlInboundTriggerStore.cs          (yeni)

src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Migrations/
└── NNNN_inbound_triggers.sql          (uc set - numara uygulama aninda)

src/AgentPrism.AspNetCore/Endpoints/
└── TriggerEndpoints.cs                (yeni - kabul + yonetim)

src/AgentPrism.UI/frontend/src/
├── screens/                           (tetikleyici listesi ve duzenleme)
└── locales/{en,tr}.ts                 (yeni anahtarlar - K-228)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| 🚨 İmzasız istek kabul edilir | Fonksiyonel | `TriggerEndpointTests` → `401` |
| 🚨 Yanlış imza kabul edilir | Fonksiyonel | `TriggerEndpointTests` → `401` |
| 🚨 Eski bir istek tekrar oynatılır | Fonksiyonel | `TriggerReplayTests` — pencere dışı `401`, pencere içi tekrar `409` |
| Bilinmeyen kiracı varsayılana düşer | Fonksiyonel | `TriggerEndpointTests` → `404` (K-382) |
| Yanıt "tetikleyici yok" ile "imza yanlış"ı ayırır | Fonksiyonel | `TriggerEndpointTests` — aynı gövde ve kod |
| Devre dışı tetikleyici çalışır | Fonksiyonel | `TriggerEndpointTests` |
| Tetikleyici kotayı bypass eder | Fonksiyonel | `TriggerQuotaTests` (K-394 emsali) |
| Aşırı büyük gövde kabul edilir | Fonksiyonel | `TriggerEndpointTests` → `413` |
| Hız sınırı yok, kuyruk dolar | Fonksiyonel | `TriggerRateLimitTests` → `429` |
| `Path` modunda yol çözülemez | Fonksiyonel | `TriggerPayloadTests` → `400`, çalıştırma **başlamaz** |
| Bozuk JSON gövdesi | Fonksiyonel | `TriggerPayloadTests` → `400` |
| Boş gövde | Fonksiyonel | `TriggerPayloadTests` |
| İmza `secret`'ının **değeri** veritabanına yazılır | Fonksiyonel + tarama | `TriggerStoreTests` + `secret` taraması |
| Önek dışındaki bir yapılandırma adı kabul edilir | Fonksiyonel | `TriggerEndpointTests` → `400` |
| Başka kiracının tetikleyicisi görünür veya çalışır | Sözleşme (`TenantIsolationContract`) | dört koşumda birden |
| Aynı ada iki tetikleyici yazılır | Sözleşme | benzersiz kısıt → `409` (K-138 dersi) |
| Eş zamanlı iki özdeş olay iki çalıştırma açar | Fonksiyonel | `TriggerReplayTests` |
| Kuyruk yazımı başarısız olur ama `202` döner | Fonksiyonel | `TriggerDispatchFailureTests` |
| Kuyruktaki iş iptal edilir | Fonksiyonel | `TriggerDispatchTests` |
| Tetikleyici yazımı denetim izine yazılmaz | Fonksiyonel | `TriggerAuditTests` (K-089) |
| Üç sağlayıcıda tablo davranışı ayrışır | Sözleşme | `InboundTriggerContract` |
| Sözlük anahtarı eksik | Derleme | `tsc --noEmit` (K-228) |
| Tetikleyici arayüzden tanımlanıp listelenir | E2E (Playwright) | `UiTests` |

Sözleşme testi `tests/Shared/Contracts/` altına — hem bellek içi hem üç SQL
sağlayıcısı üzerinde koşar.

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md)
> içine eklenecek case'lerin taslağı.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Tetikleyici tanımlı, `secret` `user-secrets`'ta | Doğru imzayla `POST` | `202` + `Location`; çalıştırma kuyruktan koşar |
| 2 | 1'in ardından | `Location` adresini oku | Çalıştırma tamamlanır, kaynağı tetikleyicidir |
| 3 | — | İmzasız `POST` | `401` |
| 4 | — | Gövdeyi bir bayt değiştirip aynı imzayla gönder | `401` |
| 5 | — | On dakika eski zaman damgasıyla gönder | `401` |
| 6 | 1'in ardından | Aynı isteği tekrar gönder | `409`; ikinci çalıştırma **açılmaz** |
| 7 | Bilinmeyen kiracı | `POST` | `404`; varsayılan kiracıya düşmez |
| 8 | Tetikleyici devre dışı | `POST` | Reddedilir; çalıştırma açılmaz |
| 9 | Kota dolu | `POST` | `429`; kota bypass edilmez |
| 10 | `Path` modu, yol yok | `POST` | `400`; çalıştırma başlamaz |
| 11 | — | Veritabanını `secret` için tara | İmza `secret`'ı **yok**, yalnız ad var |
| 12 | — | 100 istek arka arkaya gönder | Hız sınırı `429` üretir |

---

## Açık Sorular (kapanışta verilen kararlar)

| # | Soru | Verilen karar |
|---|---|---|
| 1 | Kiracı yolda mı görünsün, imzadan mı türetilsin? | **A — yolda** (`/api/triggers/{tenantId}/{name}`). Ölçüm: bir kiracı slug'ının dışarı verilmesi, K-382'nin "tam eşleşme arar, varsayılana düşmez" korumasıyla zaten kapanıyor; opak anahtar (B) ayrı bir kayıt alanı ve yönetim yükü ekler, karşılığında kazandırdığı gizlilik K-4xx'in "aynı 401" birleşmesiyle zaten sağlanıyor |
| 2 | İmza başlıkları hangi adları taşır? | **A** — `WebhookSigner.TimestampHeader`/`SignatureHeader` birebir yeniden kullanıldı |
| 3 | Tetikleyici workflow'u da hedefleyebilmeli mi? | **A** — `InboundTriggerTargetKind.Workflow` uygulandı |
| 4 | Reddedilen istekler kaydedilsin mi? | **A** — yalnız metrik/log; ayrı tablo açılmadı |
| 5 | Tekrar koruması neyi anahtar alır? | **A** — imza; `IIdempotencyStore` rezervasyonu asla tamamlanmaz (bkz. K-4xx) |

---

## Bitiş Ölçütleri (DoD)

- [x] Doğru imzalı istek `202` + `Location` döner ve çalıştırma kuyruktan koşar — `samples/AgentPrism.Api`'de doğrulandı: `202`, `Location: /agentprism/api/runs/{runId}`, run birkaç saniyede `Completed`
- [x] İmzasız, yanlış imzalı ve pencere dışı istek `401` döner — `TriggerEndpointTests` + `InboundTriggerDispatcherTests`
- [x] Aynı istek ikinci kez `409` döner; ikinci çalıştırma açılmaz — `A_replayed_request_is_rejected_the_second_time`
- [x] Bilinmeyen kiracı `401` alır (**K-472**, plandaki `404` değil), varsayılana **düşmez** — `An_unknown_tenant_does_not_fall_back_to_the_default_tenant`
- [x] Yanıt "tetikleyici yok" ile "imza yanlış" arasında fark **göstermez** — `An_unknown_trigger_name_and_a_wrong_signature_return_the_identical_response` iki gövdeyi bayt bayt karşılaştırır
- [x] Tetikleyici çalıştırması kota kapısından geçer — `A_full_quota_rejects_the_trigger_with_429_and_does_not_bypass_it`
- [x] Hız sınırı ve gövde boyutu sınırı çalışır (`429` / `413`) — `The_trigger_rate_limit_rejects_requests_beyond_the_per_minute_cap`, `A_body_larger_than_the_configured_limit_is_rejected`
- [x] İmza `secret`'ının değeri hiçbir yerde saklanmaz; yalnız yapılandırma adı durur — `pg_dump` taraması 0 eşleşme (aşağıda)
- [x] Önek dışındaki bir yapılandırma adı `400` ile reddedilir — `Name_outside_the_allowed_prefix_is_rejected`
- [x] Başka kiracının tetikleyicisi ne görünür ne çalışır (sözleşme testi, dört koşum) — `InboundTriggerStoreContract`, InMemory + 3 SQL sağlayıcısı
- [x] Tetikleyici yazımı denetim izine mutasyondan **önce** yazılır — `Save_writes_an_audit_trail_entry_before_the_definition_is_readable`; kodda `ApprovalEndpoints`/K-370 ile birebir aynı desen
- [x] Dört doğrulama kapısı sıfır uyarı verir — build/test/pack/format hepsi temiz
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıdaki "Doğrulama komutları" bölümü gerçek çıktıyla güncellendi
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` içine eklendi (MT-JOB-091..102); otomatikleştirilebilenler (091-101) `samples/AgentPrism.Api`'ye karşı koşuldu, 102 (arayüz) 👤 insan gerekir
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi (`guides/background-work.md`, `guides/inbound-triggers.md`, `concepts/runs.md`); `npm run build` + `check-links.mjs` temiz — 970 sayfa, 122628 iç bağlantı, kırık yok
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — 165,4 KB → **171,3 KB** gzip / 250 KB (+5,9 KB)

### Doğrulama komutları

Gerçek koşum çıktısı (2026-08-19, `samples/AgentPrism.Api`, gerçek PostgreSQL + gerçek `support` agent):

```bash
dotnet user-secrets set "AgentPrism:TriggerSecrets:Slack" "whsec_manual_test_66" \
  --project samples/AgentPrism.Api

curl -s -X PUT http://localhost:5000/agentprism/api/triggers/slack \
  -H 'Authorization: Bearer manuel-test-token-2026' -H 'Content-Type: application/json' \
  -d '{"targetKind":"agent","targetName":"support","signingSecretConfigurationName":"AgentPrism:TriggerSecrets:Slack","payloadMode":"path","payloadPath":"event.text"}'
# -> {"name":"slack",...,"resolved":true,...}

# imzali istek (X-AgentPrism-Timestamp/-Signature hesabi WebhookSigner.Sign ile)
curl -s -i -X POST http://localhost:5000/agentprism/api/triggers/default/slack \
  -H 'Content-Type: application/json' -H "X-AgentPrism-Timestamp: $TS" -H "X-AgentPrism-Signature: $SIG" \
  --data-binary @body.json
# -> HTTP/1.1 202 Accepted
#    Location: /agentprism/api/runs/01a01890-1652-7183-9d50-6efd430644a3
#    {"runId":"01a01890-...","jobId":"01a01890-...", "location":"...","eventsLocation":".../events"}

curl -s http://localhost:5000/agentprism/api/runs/01a01890-1652-7183-9d50-6efd430644a3 -H "$AUTH"
# -> "status":"Completed", modelId gpt-5.4-mini, usage.totalTokens 246

# ayni imza tekrar -> 409; imzasiz -> 401; bilinmeyen kiraci -> 401; bilinmeyen isim -> 401
# path cozulmezse -> 400 detail: "The payload path 'event.text' did not resolve..."

pg_dump -U postgres -d agentprism --schema=agentprism | grep -c "whsec_manual_test_66"
# -> 0
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Gelen uç internete açıktır | İmza zorunlu; zaman penceresi; tekrar koruması; hız sınırı; gövde boyutu sınırı |
| 🚨 Tekrar saldırısı çalıştırma çoğaltır | `IIdempotencyStore` (Faz 43) ve zaman penceresi; test iki katmanı da koşar |
| 🚨 `secret` veritabanına yazılır | K-059: yalnız ad; önek kısıtı; `secret` taraması (`pg_dump` ile 0 eşleşme) |
| Kiracı/tetikleyici adı numaralandırılır | **K-472**: bilinmeyen kiracı, bilinmeyen/devre dışı ad ve her imza hatası TEK jenerik `401`'e birleşir — bağımsız denetimde bulunup düzeltildi (ilk uygulama 404/401 ayırıyordu) |
| Kimliksiz akış hız sınırını tüketir | **K-473**: imza doğrulaması hız sınırından ÖNCE çalışır; yalnız kanıtlanmış istek bütçe harcar |
| Kota bypass edilir | Tetikleyici çalıştırması aynı `QuotaGate`'ten geçer (K-394); `A_full_quota_rejects_the_trigger_with_429_and_does_not_bypass_it` ile kanıtlandı |
| Çift tetikleme | Benzersiz kısıt (K-138 dersi) ve tekrar koruması |
| Şablon dili sızar | Yalnız `WholeBody` ve `Path`; yol dili Faz 63 ile **aynı** |
| Hız sınırı çok örnekte örnek başına uygulanır | K-158 kararı korunur; belge sınırın kapsamını açıkça yazar |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **404 → 401 birleşmesi (K-472).** İlk uygulama planın taslak public API'sindeki
   `NotFound`/`Unauthorized` ayrımını birebir kodladı (bilinmeyen kiracı/isim/devre
   dışı → `404`; imza hatası → `401`). Bağımsız denetim bunun 66.2'nin "aynı gövde
   ve kod" kuralını (ve manuel case 7'nin `404` beklentisiyle o kuralın kendi
   içindeki çelişkiyi) ihlal ettiğini, ve bunun gerçek bir numaralandırma açığı
   olduğunu gösterdi. Kapanışta TÜM bu durumlar TEK bir jenerik `401`'e birleştirildi;
   manuel case 96/97 buna göre güncellendi.
2. **İmza/hız sınırı sırası (K-473).** Plan sıralamayı açıkça belirtmiyordu; ilk
   uygulama hız sınırını imzadan ÖNCE kontrol etti ("her istek sayılır" sezgisiyle).
   Bağımsız denetim bunun kimliksiz bir saldırganın gerçek gönderenin bütçesini
   tüketmesine izin verdiğini gösterdi; sıra ters çevrildi.
3. **Hedef varlığı doğrulanmıyor (K-474, gerekçelendi — sapma değil).** Plan bunu
   açıkça belirtmiyordu; `SchedulingEndpoints`/Faz 17 emsaliyle tutarlı olacak
   şekilde kabul anında agent/workflow varlığı kontrol EDİLMEDİ — hata işleyici
   seviyesinde (`run` → `Failed`) yakalanır. Bağımsız denetimde sorgulandı, emsal
   ile doğrulanıp onaylandı.
4. **Rate limiter `System.Threading.RateLimiting` yerine elle yazıldı.** Plan
   Faz 21'in `AgentPrismRateLimitFilter`'ıyla aynı altyapıyı ima ediyordu, ama o
   tip ASP.NET Core paylaşılan çerçevesinden gelir ve `AgentPrism.Core` (düz sınıf
   kütüphanesi, web bağımlılığı yok) onu göremez. `InboundTriggerRateLimiter`
   bağımsız, sabit pencereli bir sayaçla yazıldı (K1: dispatcher'ın bir web
   çerçevesine bağımlı olmaması).

## Bu Fazda Verilen Kararlar

- **K-472** — "tetikleyici yok" ile "imza yanlış" TEK `401`'e birleşir (bkz. `docs/KARARLAR.md`).
- **K-473** — imza doğrulaması hız sınırından önce çalışır (bkz. `docs/KARARLAR.md`).
- **K-474** — hedef agent/workflow varlığı kabul anında doğrulanmaz (bkz. `docs/KARARLAR.md`).

## Gerçekleşen Public API

Plandaki taslaktan farklar: `IInboundTriggerStore.UpsertAsync` dönüş tipi
`ValueTask<InboundTrigger>` (taslakta belirtilmemişti, `IJobScheduleStore.SaveAsync`
emsaliyle tutarlı). `AgentPrismInboundTriggerOptions` planla birebir aynı.
Ayrıca planda olmayan, `AgentPrism.Core`'da public olarak eklenen orkestrasyon
tipleri (QuotaEnforcer/AgentSessionManager emsaliyle tutarlı, K1 gerekçesi
`InboundTriggerDispatcher`'ın XML dokümanında yazılı):

```csharp
// AgentPrism.Core
public enum InboundTriggerOutcome { Valid, Unauthorized, RateLimited, Replayed, InvalidPayload }
public sealed record InboundTriggerValidationResult { Outcome, ErrorDetail, Validated }
public sealed record InboundTriggerValidatedRequest { Trigger, Message, IdempotencyKey }
public sealed record InboundTriggerDispatchResult { RunId, JobId }
public sealed class InboundTriggerDispatcher { ValidateAsync(...), EnqueueAsync(...), ReleaseAsync(...) }
public sealed class InboundTriggerSecretResolver { ValidatePrefix(...), Resolve(...) }
public sealed class InboundTriggerRateLimiter { TryAcquire(Guid) }
public sealed class InMemoryInboundTriggerStore : IInboundTriggerStore

// AgentPrism.AspNetCore
public sealed record InboundTriggerResponse { ... Resolved, ... }
public sealed record InboundTriggerSaveRequest { ... }
public sealed record InboundTriggerAcceptedResponse { RunId, JobId, Location, EventsLocation }
```

Tam liste `PublicAPI.Unshipped.txt` (üç projede) içindedir.

## Dosya Listesi (gerçekleşen)

Planlanan listeyle birebir eşleşir, artı test/doküman dosyaları:

```
src/AgentPrism.Abstractions/Triggers/{InboundTrigger,IInboundTriggerStore,InboundTriggerTargetKind,InboundTriggerPayloadMode}.cs
src/AgentPrism.Abstractions/Options/AgentPrismInboundTriggerOptions.cs
src/AgentPrism.Core/Triggers/{InboundTriggerDispatcher,InboundTriggerSecretResolver,InboundTriggerRateLimiter,InboundTriggerPayloadReader}.cs
src/AgentPrism.Core/Storage/InMemoryInboundTriggerStore.cs
src/AgentPrism.Sql.Shared/Stores/SqlInboundTriggerStore.cs
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Migrations/{0033,0020,0020}_inbound_triggers.sql
src/AgentPrism.AspNetCore/Endpoints/TriggerEndpoints.cs
src/AgentPrism.AspNetCore/Contracts/TriggerContracts.cs
src/AgentPrism.UI/frontend/src/screens/triggers.tsx

tests/AgentPrism.Core.UnitTests/Triggers/{InboundTriggerDispatcherTests,InboundTriggerSecretResolverTests,InboundTriggerPayloadReaderTests}.cs
tests/AgentPrism.AspNetCore.FunctionalTests/TriggerEndpointTests.cs
tests/Shared/Contracts/InboundTriggerStoreContract.cs (+ InMemory/Postgres/SqlServer/Sqlite kayıtları)
tests/AgentPrism.Ui.E2ETests/UiTests.cs (Trigger_created_from_UI_is_listed eklendi)

docs-site/src/content/docs/guides/inbound-triggers.md (yeni)
docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md (MT-JOB-091..102 eklendi)
```

Ayrıca dokunulan paylaşılan dosyalar: `AgentPrismServiceCollectionExtensions.cs`
(DI), `AgentPrismEndpointRouteBuilderExtensions.cs` (uç kaydı, üçüncü kimlik
doğrulamasız grup), `SqlQueriesBase.cs` + üç sağlayıcının `*Queries.cs`'i,
üç sağlayıcının `TestContext.cs`'i, `TenantCoverageTests.cs`,
`ApiKeyScopeCoverageTests.cs`, `AuditContentPolicyTests.cs`, migration sayısı
testleri (üç sağlayıcı), frontend `app.tsx`/`layout.tsx`/`icons.tsx`/`api.ts`/
`types.ts`/`locales/{en,tr}.ts`/`i18n.test.ts`, `docs-site/astro.config.mjs`,
`docs-site/scripts/{build-http-api.mjs,check-content.mjs}`.

## Denetim Bulguları

`faz-denetim` bir kez koşuldu (2026-08-19, taze bağlamlı bağımsız agent).

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | "Tetikleyici yok" ile "imza yanlış" farklı kodlarla (`404`/`401`) dönüyordu; 66.2'nin "aynı gövde/kod" kuralını ihlal ediyordu; iddia eden test aslında karşılaştırmıyordu (test tiyatrosu) | 🔴 | **Düzeltildi** — tek `Unauthorized=401`'e birleştirildi (K-472); test artık iki gövdeyi bayt bayt karşılaştırıyor |
| 2 | Hız sınırı imzadan önce kontrol ediliyordu; kimliksiz bir istek akını gerçek gönderenin bütçesini tüketebiliyordu | 🔴 | **Düzeltildi** — sıra ters çevrildi (K-473); yeni test `An_unsigned_flood_does_not_consume_a_legitimate_senders_rate_limit_budget` |
| 3 | `SqlInboundTriggerStore`'un çalışma anı `exception.Message`'ı Türkçe idi (dil sınırı ihlali) | 🔴 | **Düzeltildi** — İngilizce'ye çevrildi |
| 4 | "Kota kapısından geçer" DoD satırı hiçbir testle doğrulanmamıştı | 🟡 | **Düzeltildi** — `A_full_quota_rejects_the_trigger_with_429_and_does_not_bypass_it` eklendi |
| 5 | `EnqueueAsync` başarısız olursa `ValidateAsync`'in açtığı idempotency rezervasyonu asla serbest bırakılmıyordu; retry sonsuza dek `409` alırdı | 🟡 | **Düzeltildi** — `AcceptAsync` artık `EnqueueAsync`'i try/catch'e alıp hata durumunda `ReleaseAsync` çağırıyor |

**Ek doğrulanan noktalar (soruldu, kusur çıkmadı):** K-089/K-370 audit-önce
deseni birebir doğru uygulanmış; `EnqueueAsync`'in hedef varlığını doğrulamaması
kasıtlı ve `SchedulingEndpoints` emsaliyle tutarlı (K-474); migration/tablo
sayısı testleri üç sağlayıcıda da doğru; `PublicAPI.Unshipped.txt` üç dosyada
da gerçek yüzeyle eşleşiyor.

Düzeltmelerden sonra dört doğrulama kapısı yeniden koşuldu — hepsi temiz.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IInboundTriggerStore`/`InboundTrigger` (Abstractions), `InboundTriggerDispatcher`
  (Core, host-agnostic — `ValidateAsync` → `EnqueueAsync`/`ReleaseAsync` iki
  aşamalı akışı, HTTP katmanının arasına kota kontrolü sokabilmesi için).
- `POST /api/triggers/{tenantId}/{name}` kimlik doğrulamasız üçüncü uç grubu
  (`requireBearerToken:false, requireLoopback:false`, `AuthorizationPolicy`
  BİLEREK uygulanmaz) — internete açık, imzayla korunan bir uca ihtiyaç duyan
  gelecekteki her faz bu deseni tekrar kullanabilir.

**Bilinen tuzaklar (🚨):**
- 🚨 Bu fazdan sonra eklenecek her yeni "kimlik doğrulamasız" uç,
  `docs-site/scripts/build-http-api.mjs`'in `readEndpointAuthorization`
  fonksiyonundaki `anonymous` kontrolüne VE `check-content.mjs`'in
  `expectedAnonymous` listesine eklenmelidir — yoksa `npm run generate`/`npm run
  check:content` kırılır (`AgentPrismAcceptInboundTrigger` örneği).
- 🚨 `tests/AgentPrism.AspNetCore.FunctionalTests/ApiKeyScopeCoverageTests.cs`'in
  `ExemptRoutePatterns`'ı da aynı şekilde her yeni kimlik doğrulamasız/kapsamsız
  uç için güncellenmelidir.
- 🚨 `AgentPrism.Core`'da `System.Threading.RateLimiting` KULLANILAMAZ (ASP.NET
  Core paylaşılan çerçevesinden gelir); host-agnostic bir sınırlayıcı gerekiyorsa
  elle yazılmalıdır (`InboundTriggerRateLimiter` örneği).
- 🚨 Bir kaynağın "yok" durumuyla "yetkisiz" durumunu birleştirmek isteyen her
  yeni uç için: plan taslağı ile manuel case tablosunun aynı HTTP kodunu
  iddia ettiğinden EMİN OL — K-472'nin çelişkisi ikisinin ayrı yazılmasından
  doğdu.

**Yarım kalan işler:** Yok — DoD'nin tamamı kapalı, 🔴/🟡 denetim bulgusu kalmadı.

**Sıradaki faz:** `docs/YOL-HARITASI.md`'de üretilir; bu faz kapanınca yeniden
üretilmelidir (`python3 scripts/dokuman-bakim.py`).
