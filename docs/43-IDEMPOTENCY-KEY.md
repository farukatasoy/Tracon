# Faz 43 — `Idempotency-Key` Desteği

> **Durum:** 📋 Planlandı (2026-08-06)
> **Kaynak:** [UCUNCU-FAZ-ADAYLARI.md](UCUNCU-FAZ-ADAYLARI.md) · **F-37**
> **Önkoşul:** Yok. Ama [Faz 25](25-VERI-SAKLAMA-VE-ARSIVLEME.md)'in saklama hedef kayıt defteri **kullanılır**
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`
> **Yeni paket:** Yok · **Migration:** **gerekli** — bir tablo, üç set, numaralar uygulama anında alınır (K-178)
> **Public API:** büyüyor — bir arayüz, bir ayar, bir kayıt tipi. Faz 7'den önce ucuz

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-059\|K-158\|K-178\|K-198\|K-232" docs/KARARLAR.md
   ```
   **K-158** (hız sınırı ve kota **ayrı** mekanizmalardır — idempotency
   üçüncü bir mekanizmadır ve ikisiyle karıştırılmamalıdır), **K-198**
   (saklama SQL'i tek tabloyla üretilir — yeni hedef aynı yerden çıkar),
   **K-232** (sunucu yanıtları çevrilmez), **K-178** (migration numaraları
   sağlayıcı başına bağımsız), **K-059** (`secret` veritabanına yazılmaz —
   saklanan yanıt gövdesi için [43.5](#435--saklanan-yanıt-ve-secret-riski)).
3. [`25-VERI-SAKLAMA-VE-ARSIVLEME.md`](25-VERI-SAKLAMA-VE-ARSIVLEME.md) — yalnız
   hedef kayıt defteri bölümü ve devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/25-VERI-SAKLAMA-VE-ARSIVLEME.md
   ```
   Saklanan yanıtların bir saklama hedefi olması gerekir; sözleşme oradan
   devralınır.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) (**ana kaynak** —
   `IEndpointFilter` zinciri, filtre sırası),
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) (üç diyalekt,
   benzersizlik kısıtı ve çakışma davranışı)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — HTTP yüzeyi ve güvenlik bölümü

---

## Amaç

Bir istemci ağ hatası aldığında isteği yeniden gönderir. AgentPrism bugün bunu
**ikinci bir çalıştırma** olarak görür: agent ikinci kez koşar, tool'lar ikinci
kez yan etki üretir ve model faturası ikinci kez yazılır.

Bu faz, standart `Idempotency-Key` başlığını destekler: aynı anahtarla gelen
ikinci istek **yeniden çalıştırmaz**, ilk yanıtı döndürür.

- **F-37** — `Idempotency-Key` başlığı, anahtar + kiracı benzersizliği,
  saklanan yanıtın tekrar döndürülmesi ve gövde parmak izi denetimi.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -rni "idempoten" src/ --include="*.cs"` | **Yedi sonuç, hepsi iç kavram.** [`IJobStore.cs:15`](../src/AgentPrism.Abstractions/Scheduling/IJobStore.cs), üç SQL sorgu dosyası, [`InMemoryJobStore.cs:359`](../src/AgentPrism.Core/Scheduling/InMemoryJobStore.cs), [`TraceSpanIdentity.cs:18`](../src/AgentPrism.Core/Diagnostics/TraceSpanIdentity.cs). **HTTP yüzeyinde hiçbir şey yok** |
| [`AgentEndpoints.cs:75`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs) | `POST /api/agents/{name}/run` — korumasız |
| [`OpenAIResponsesEndpoints.cs`](../src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs) | `POST /v1/responses` — korumasız |
| [`OpenAIChatCompletionsEndpoints.cs`](../src/AgentPrism.AspNetCore/OpenAICompat/OpenAIChatCompletionsEndpoints.cs) | `POST /v1/chat/completions` — korumasız |
| [`AgentEndpoints.cs:90`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs) | `QuotaGate` çalıştırma başlamadan **kotayı tüketir**. Yeniden deneme kotayı ikinci kez tüketir |

> Kanıtlar 2026-08-06 tarihinde doğrulandı.

**Aday listesinin iddiası doğrulandı ve daraltıldı:** `grep` gerçekten yalnız iş
kuyruğunun iç yorumlarını buluyor. Ama iş kuyruğunun idempotency'si HTTP
yüzeyinin sorununu **çözmez** — o, kiralanan bir işin iki kez işlenmemesini
sağlar; istemcinin iki kez istek göndermesini değil.

---

## 43.1 — Zincirdeki yer

Filtre sırası bu tasarımın en önemli kararıdır ve bugünkü zincir onu belirliyor:

```mermaid
flowchart TD
    A["HTTP istegi"] --> B["ASP.NET Core authorization<br/>RequireAuthorization"]
    B --> C["AgentPrismEndpointFilter<br/>loopback + bearer token"]
    C --> D["AgentPrismRateLimitFilter<br/>saniye olcegi"]
    D --> E["IdempotencyFilter<br/>YENI"]
    E -->|"anahtar bulundu"| F["saklanan yaniti dondur<br/>agent HIC kosmaz"]
    E -->|"anahtar yok"| G["QuotaGate<br/>AgentEndpoints icinde"]
    G --> H["agent kosar"]
    H --> I["yanit saklanir"]

    classDef yeni fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    class E,F,I yeni
```

🚨 **Idempotency, `QuotaGate`'ten ÖNCE gelmelidir.** Bugün `QuotaGate`
[`AgentEndpoints.cs:90`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs)
içinde, handler'ın gövdesinde çağrılıyor. Tekrarlanan bir istek kotayı ikinci
kez tüketirse fazın amacı yarım kalır: çalıştırma tekrarlanmaz ama **fatura
tekrarlanır**.

Bu, filtrenin handler'dan önce çalışmasıyla sağlanır — `IEndpointFilter`
zincirindeki yeri bunu doğal olarak verir.

🚨 **Idempotency, hız sınırından SONRA gelmelidir.** Aksi hâlde saklanmış bir
yanıtı sonsuz kez isteyen bir istemci hız sınırını atlar.

## 43.2 — Dört durum

Anahtar + kiracı çifti için dört durum vardır ve dördü de farklı yanıt verir.

```mermaid
stateDiagram-v2
    [*] --> Yok: Idempotency-Key geldi
    Yok --> Isleniyor: kayit olusturulur
    Isleniyor --> Tamam: yanit saklanir
    Isleniyor --> Basarisiz: hata saklanmaz, kayit SILINIR

    Tamam --> Tamam: ayni govde ile tekrar -> saklanan yanit
    Isleniyor --> Catisma: ayni anahtar es zamanli -> 409
    Tamam --> Uyusmazlik: FARKLI govde -> 422
```

| Durum | Yanıt | Gerekçe |
|---|---|---|
| Anahtar **yok** | Normal işlem; kayıt `Isleniyor` olarak açılır | İlk istek |
| Anahtar var, **tamamlanmış**, gövde parmak izi **aynı** | Saklanan yanıt, orijinal durum kodu ile | Fazın amacı |
| Anahtar var, **işleniyor** | `409 Conflict` | İlk istek hâlâ koşuyor; ikinciyi beklemek bağlantıyı tutar |
| Anahtar var, gövde parmak izi **farklı** | `422 Unprocessable Content` | 🚨 İstemci hatası. Aynı anahtarı farklı bir istek için kullanmak, sessizce yanlış yanıt döndürmekten **çok daha az** zararlıdır |

🚨 **Başarısız çalıştırma saklanmaz ve kayıt silinir.** Gerekçe: idempotency'nin
amacı yeniden denemeyi **güvenli** kılmaktır. Bir hatayı saklamak, istemcinin
geçici bir hatadan sonra hiç yeniden deneyememesi demektir — mekanizmanın
kendi amacını yok eder.

> Bu, Stripe'ın desenidir ve bilinçli olarak izlenir. Yeniden icat edilmez.

## 43.3 — Gövde parmak izi

Parmak izi, isteğin **ham gövdesinin** SHA-256 özetidir. Üç kural:

| Kural | Gerekçe |
|---|---|
| Ham baytlar özetlenir, ayrıştırılmış nesne değil | JSON alan sırası değişse bile istemci aynı isteği gönderdiğini düşünür; ham özet bunu ayırır ve **daha güvenlidir** |
| Özet saklanır, gövde **saklanmaz** | Gövde `secret` taşıyabilir; özet taşımaz |
| Yol ve HTTP metodu da özete girer | Aynı anahtarın iki farklı uçta kullanılması bir hatadır |

## 43.4 — Akışlı yanıt bu fazın kapsamı dışındadır

`POST /api/agents/{name}/run` **akışlı** (SSE) yanıt üretebilir. Saklanmış bir
SSE akışını yeniden oynatmak teknik olarak mümkündür ama üç sorun doğurur:
gövde büyüktür, zamanlama bilgisi kaybolur ve saklama maliyeti öngörülemez.

**Karar: akışlı bir istek `Idempotency-Key` taşırsa `400 Bad Request` döner** ve
mesaj sebebi açıkça yazar.

Sessizce yok saymak kabul edilemez: istemci korunduğunu sanır, korunmaz. Bu,
K-034 ve K-208'in üç kez tekrarladığı kuralın aynısıdır — **sessizce yok
sayılan bir ayar, kullanıcının beklediği davranışı almamasına yol açar.**

> Bu sınır kalıcı değildir. Aday listesindeki **F-68** (dayanıklı çalıştırma)
> Okuma A ile `202 Accepted` + `Location: /api/runs/{id}` sözleşmesini getirir.
> Akışlı idempotency'nin doğru evi orasıdır: anahtar `run`'ı tekilleştirir,
> istemci akışı `run` kimliğinden okur. Aday listesi zaten "F-37, F-68 ile
> birlikte planlanmalıdır" diyor.

## 43.5 — Saklanan yanıt ve `secret` riski

Saklanan yanıt gövdesi bir agent çıktısıdır ve **hassas içerik taşıyabilir**.
İki koruma uygulanır:

1. **Saklama süresi zorunludur.** Kayıt sonsuza dek yaşamaz. Faz 25'in hedef
   kayıt defterine yeni bir hedef (`idempotency_keys`) eklenir ve varsayılan
   bir yaş sınırı taşır.
2. **`secret` filtresi uygulanmaz — çünkü uygulanamaz.** `AuditSecretFilter`
   denetim kaydını temizler; bir agent yanıtının içindeki neyin `secret`
   olduğunu bilmenin yolu yoktur. Bunun yerine kural şudur: saklanan yanıt,
   **istemciye zaten gönderilmiş** olan yanıtın aynısıdır. Yeni bir bilgi
   açığa çıkmaz; yalnız ömrü uzar. Ömür sınırı bu yüzden zorunludur.

> K-059 ihlal edilmez: bir **yapılandırma `secret`'ı** veritabanına yazılmaz.
> Saklanan şey bir kullanıcı yanıtıdır ve `conversation_items` zaten aynı
> içeriği saklıyor (K-107).

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions/Idempotency/IIdempotencyStore.cs

/// <summary>Idempotency kayitlarinin deposu.</summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Anahtari <c>Isleniyor</c> olarak ayirmayi dener. Anahtar zaten varsa
    /// mevcut kayit dondurulur ve yeni kayit ACILMAZ.
    /// </summary>
    ValueTask<IdempotencyReservation> ReserveAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Tamamlanan yaniti kaydeder.</summary>
    ValueTask CompleteAsync(
        string tenantId,
        string key,
        IdempotencyResponse response,
        CancellationToken cancellationToken = default);

    /// <summary>Basarisiz istek sonrasi kaydi siler; yeniden deneme serbest kalir.</summary>
    ValueTask ReleaseAsync(
        string tenantId,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>Bir idempotency ayirma istegi.</summary>
public sealed record IdempotencyRequest
{
    public required string TenantId { get; init; }
    public required string Key { get; init; }

    /// <summary>Metot + yol + ham govde ozeti.</summary>
    public required string Fingerprint { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Ayirma sonucu.</summary>
public sealed record IdempotencyReservation
{
    public required IdempotencyState State { get; init; }

    /// <summary>Yalnizca <see cref="IdempotencyState.Completed"/> icin dolar.</summary>
    public IdempotencyResponse? Response { get; init; }
}

public enum IdempotencyState
{
    /// <summary>Anahtar yeni ayrildi; istek normal islenir.</summary>
    Reserved = 0,

    /// <summary>Ayni anahtar hala isleniyor. Yanit: 409.</summary>
    InProgress = 1,

    /// <summary>Tamamlanmis ve govde ayni. Saklanan yanit dondurulur.</summary>
    Completed = 2,

    /// <summary>Tamamlanmis ama govde FARKLI. Yanit: 422.</summary>
    FingerprintMismatch = 3,
}

/// <summary>Saklanan yanit.</summary>
public sealed record IdempotencyResponse
{
    public required int StatusCode { get; init; }
    public required string ContentType { get; init; }
    public required string Body { get; init; }
    public Guid? RunId { get; init; }
}
```

```csharp
// AgentPrism.AspNetCore
public sealed class AgentPrismIdempotencyOptions
{
    /// <summary>
    /// Idempotency destegi acik mi. Aciksa bile <c>Idempotency-Key</c>
    /// basligi TASIMAYAN istek hicbir ek maliyet odemez.
    /// </summary>
    public bool Enabled { get; set; } = true;   // Acik Soru 1

    /// <summary>Anahtarin gecerli kalacagi sure.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Anahtarin en fazla uzunlugu.</summary>
    public int MaxKeyLength { get; set; } = 255;
}
```

### Yeni tablo

```sql
CREATE TABLE IF NOT EXISTS {schema}.idempotency_keys (
    tenant_id    text        NOT NULL,
    key          text        NOT NULL,
    fingerprint  text        NOT NULL,
    state        smallint    NOT NULL,     -- 0=Reserved 2=Completed
    status_code  integer,
    content_type text,
    body         text,
    run_id       uuid,
    created_at   timestamptz NOT NULL,
    completed_at timestamptz,
    CONSTRAINT idempotency_keys_pkey PRIMARY KEY (tenant_id, key)
);

CREATE INDEX IF NOT EXISTS idempotency_keys_created_idx
    ON {schema}.idempotency_keys (tenant_id, created_at DESC);
```

🚨 **Ayırma atomik olmalıdır.** `ReserveAsync` iki eş zamanlı isteği ayırt
edebilmelidir. PostgreSQL'de `INSERT … ON CONFLICT DO NOTHING RETURNING`,
SQL Server'da `MERGE` veya `INSERT` + yakalanan benzersizlik hatası, SQLite'ta
`INSERT OR IGNORE`. Üç diyalekt üç teknik kullanır; `SqlQuotaStore`'un
`ON CONFLICT DO UPDATE` deseni örnek alınır.

### HTTP `endpoint`'leri

Yeni uç **yok**. Var olan uçlar bir başlık tanır:

| Metot | Yol | Başlık | Davranış |
|---|---|---|---|
| `POST` | `/api/agents/{name}/run` | `Idempotency-Key` | Akışsız istekte tekilleştirir; akışlı istekte `400` |
| `POST` | `/v1/responses` | `Idempotency-Key` | Aynı |
| `POST` | `/v1/chat/completions` | `Idempotency-Key` | Aynı |

Yanıt başlıkları:

| Başlık | Ne zaman |
|---|---|
| `Idempotency-Replayed: true` | Saklanan yanıt döndürüldüğünde |

### Arayüz payı

**Yok.** Arayüze dokunulmaz.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Idempotency/
├── IIdempotencyStore.cs              (YENI)
├── IdempotencyTypes.cs               (YENI — request, reservation, response, state)

src/AgentPrism.Core/Idempotency/
└── InMemoryIdempotencyStore.cs       (YENI — K-018: birinci sinif)

src/AgentPrism.Core/Retention/
└── RetentionTargets.cs               (idempotency_keys hedefi eklenir)

src/AgentPrism.AspNetCore/
├── Idempotency/IdempotencyFilter.cs  (YENI — IEndpointFilter)
├── AgentPrismIdempotencyOptions.cs   (YENI)
└── AgentPrismEndpointRouteBuilderExtensions.cs   (filtre zincirine eklenir)

src/AgentPrism.Sql.Shared/Stores/
└── SqlIdempotencyStore.cs            (YENI)

src/AgentPrism.PostgreSql/Migrations/NNNN_idempotency.sql   (YENI)
src/AgentPrism.SqlServer/Migrations/NNNN_idempotency.sql    (YENI)
src/AgentPrism.Sqlite/Migrations/NNNN_idempotency.sql       (YENI)

src/AgentPrism.PostgreSql/Internal/PostgresQueries.cs   (atomik ayirma)
src/AgentPrism.SqlServer/Internal/SqlServerQueries.cs   (ayni)
src/AgentPrism.Sqlite/Internal/SqliteQueries.cs         (ayni)
```

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `IdempotencyStoreContract` | `tests/Shared/Contracts/` altında; bellek içi + üç SQL sağlayıcısında aynı sonuç |
| `IdempotencyReplayTests` | Aynı anahtar + aynı gövde → agent **ikinci kez koşmaz**; yanıt ve durum kodu aynı; `Idempotency-Replayed: true` |
| `IdempotencyFingerprintTests` | Aynı anahtar + **farklı** gövde → `422`; saklanan yanıt döndürülmez |
| `IdempotencyConcurrencyTests` | 🚨 İki eş zamanlı istek, aynı anahtar → biri koşar, diğeri `409`. Ayırma atomikliği |
| `IdempotencyFailureTests` | 🚨 Başarısız çalıştırmadan sonra aynı anahtarla yeniden deneme **çalışır** — kayıt silinmiştir |
| `IdempotencyQuotaTests` | 🚨 Tekrarlanan istek kotayı **ikinci kez tüketmez** |
| `IdempotencyRateLimitTests` | Tekrarlanan istek hız sınırına **tabidir** |
| `IdempotencyStreamingTests` | Akışlı istek + anahtar → `400`, mesaj sebebi yazar |
| `IdempotencyTenantTests` | Aynı anahtar iki kiracıda **bağımsız** yaşar |
| `IdempotencyRetentionTests` | `idempotency_keys` saklama hedefi olarak tanınır ve temizlenir |
| `IdempotencyDisabledTests` | `Enabled = false` iken başlık taşıyan istek — Açık Soru 2'nin kararı doğrulanır |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> sorular plan yazılmadan **önce** sorulur.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | K1 "varsayılan kapalı" der. Bu özellik kapalı mı gelsin? | A: **açık**; başlık yokken maliyet sıfır · B: kapalı | **A ve bu bilinçli bir K1 yorumudur.** K1'in amacı sürpriz üretmemektir. Başlık göndermeyen istemci için hiçbir şey değişmez — sorgu bile atılmaz. Kapalı gelirse, başlık gönderen istemci **korunduğunu sanır ve korunmaz**; asıl sürpriz budur. Karar `KARARLAR.md`'ye gerekçesiyle yazılmalıdır |
| 2 | `Enabled = false` iken başlık taşıyan isteğe ne olur? | A: `501 Not Implemented` · B: sessizce yok sayılır | **A.** B, kullanıcının korunduğunu sanmasına yol açar — K-034'ün kuralı. Yanıt, özelliğin kapalı olduğunu açıkça yazar |
| 3 | Akışlı istek + anahtar gerçekten `400` mü dönmeli? | A: evet · B: anahtar yok sayılır · C: akış saklanır | **A.** B sessiz yanlıştır. C bu fazın kapsamını iki katına çıkarır ve doğru evi F-68'dir ([43.4](#434--akışlı-yanıt-bu-fazın-kapsamı-dışındadır)) |
| 4 | Saklama süresi varsayılanı ne olmalı? | A: 24 saat · B: **ölçülmeli** | **A** bir taslaktır; Stripe'ın deseni budur. Ama saklanan gövdelerin **hacmi ölçülmelidir** — bir ölçüm yapılmadan üretim önerisi yazılmaz |
| 5 | `409` mu `425 Too Early` mi? | A: `409 Conflict` · B: `425` | **A.** `425` TLS erken veri içindir; `409` bu durumun yerleşik karşılığıdır ve Stripe da onu kullanır |
| 6 | Anahtar kotaya mı hız sınırına mı tabi? | A: hız sınırına evet, kotaya hayır | **A.** [43.1](#431--zincirdeki-yer)'in sıralaması bunu zaten veriyor; test iki yönü de doğrular |

---

## Bitiş Ölçütleri (DoD)

- [ ] 🚨 Aynı `Idempotency-Key` ile gönderilen ikinci istek **agent'ı yeniden
      çalıştırmaz**; `runs` tablosunda **tek** satır oluşur
- [ ] Tekrarlanan istek kotayı **ikinci kez tüketmez** (`/api/quotas` ile
      doğrulanır)
- [ ] Aynı anahtar + farklı gövde `422` döner
- [ ] İki eş zamanlı istek: biri koşar, diğeri `409` alır
- [ ] Başarısız çalıştırmadan sonra aynı anahtarla yeniden deneme **çalışır**
- [ ] Akışlı istek + anahtar `400` döner ve sebebi yazar
- [ ] Aynı anahtar iki kiracıda bağımsız yaşar
- [ ] `idempotency_keys` bir saklama hedefidir; `GET /api/retention/idempotency_keys`
      yanıt verir
- [ ] Sözleşme testleri bellek içi + üç SQL sağlayıcısında geçer
- [ ] Migration üç sette de uygulandı (K-178)
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı
- [ ] `secret` taraması boş döndü

### Doğrulama komutları

```bash
KEY=$(uuidgen)

# Ilk istek
curl -s -X POST http://localhost:5081/agentprism/api/agents/asistan/run \
  -H "content-type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"messages":[{"role":"user","text":"merhaba"}]}' -D - | head -20

# Ikinci istek — AYNI govde. Idempotency-Replayed: true gelmeli
curl -s -X POST http://localhost:5081/agentprism/api/agents/asistan/run \
  -H "content-type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"messages":[{"role":"user","text":"merhaba"}]}' -D - | head -20

# runs tablosunda TEK satir olmali
psql "$AGENTPRISM_CONN" -c \
  "SELECT count(*) FROM agentprism.runs WHERE agent_name='asistan';"

# Farkli govde — 422 gelmeli
curl -s -X POST http://localhost:5081/agentprism/api/agents/asistan/run \
  -H "content-type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"messages":[{"role":"user","text":"BASKA"}]}' -i | head -5

# Akisli istek + anahtar — 400 gelmeli
curl -s -X POST "http://localhost:5081/agentprism/api/agents/asistan/run?stream=true" \
  -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"messages":[{"role":"user","text":"merhaba"}]}' -i | head -5

# Saklama hedefi taniniyor mu
curl -s http://localhost:5081/agentprism/api/retention/idempotency_keys | jq
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Ayırma atomik değilse iki eş zamanlı istek de koşar | Üç diyalektte atomik `INSERT` deseni; `IdempotencyConcurrencyTests` bunu doğrular |
| Tekrarlanan istek kotayı ikinci kez tüketir | Filtre `QuotaGate`'ten **önce** çalışır; ayrı bir test bunu doğrular |
| Saklanan yanıt hacmi öngörülemez şekilde büyür | Saklama hedefi zorunludur; hacim **ölçülür** (Açık Soru 4). [Faz 36](36-SAKLAMA-HACIM-SINIRI.md)'nın `MaxRows`'u da uygulanabilir |
| 🚨 Başarısız istek saklanırsa istemci hiç yeniden deneyemez | Başarısızlıkta kayıt **silinir**; ayrı bir test bunu doğrular |
| Akışlı yol sessizce korumasız kalır | Akışlı istek + anahtar `400` döner; sessiz yok sayma reddedildi |
| Anahtar kiracılar arasında sızar | Birincil anahtar `(tenant_id, key)`; Faz 41'in yalıtım sözleşmesi bu depoyu da kapsar |
| İstemci çok uzun bir anahtar gönderir | `MaxKeyLength` ile sınırlanır; aşımda `400` |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.
> **Not:** Açık Soru 1'in kararı (K1'in "varsayılan kapalı" kuralının bu fazda
> nasıl yorumlandığı) **mutlaka** kayda geçmelidir — sonraki fazlar bu yorumu
> emsal alacaktır.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
>
> **Not:** Aday listesindeki **F-68** (dayanıklı çalıştırma) bu fazla birlikte
> planlanmalıdır. İki devir bilgisi zorunludur:
> 1. **Akışlı idempotency** bu fazda `400` ile kapatıldı; F-68'in
>    `202 Accepted` + `Location` sözleşmesi onun doğru evidir.
> 2. F-68 Okuma A "süreç düşerse iş **baştan** çalışır" diyor. Bu fazın
>    `IIdempotencyStore`'u o yeniden çalışmanın yan etkili tool'ları ikinci kez
>    tetiklemesini **engellemez** — anahtar HTTP yüzeyindedir, iş kuyruğunda
>    değil. Devir notu bu sınırı açıkça yazmalıdır.
