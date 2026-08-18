# Faz 63 — Argüman Düzeyinde Onay Politikası

> **Durum:** 📋 Planlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-61**
> **Önkoşul:** [Faz 6](06-GOZLEMLENEBILIRLIK.md) — onay kuralı tablosu ve değerlendirici oradan gelir · [Faz 48](48-GUARDRAILS.md) — tool **argümanı** denetimini bilerek kapsam dışı bıraktı; bu faz o boşluğun sahibidir · [Faz 55](55-ASENKRON-ONAY-KUTUSU.md) — asenkron onay kutusu bu kuralların tüketicisidir
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Sql.Shared`, `AgentPrism.PostgreSql`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (PostgreSQL + SQL Server + SQLite). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor** — `ToolApprovalRule`'a bir alan, iki yeni tip, bir kayıt uzantısı. `PublicAPI.Shipped.txt` bugün **boş**; ekleme **bugün bedava**
> **Site etkisi:** `concepts/governance.md`, `concepts/tools.md`
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-012\|K-089\|K-178\|K-218\|K-368\|K-370" docs/KARARLAR.md
   ```
   **K-012** (tool'lar yalnız kodda — koşul dili bu sınırın altındadır),
   **K-089** (denetim kaydı mutasyondan **önce** yazılır),
   **K-178** (migration numaraları sağlayıcı başına bağımsızdır),
   **K-218** (tool bağımlılığı kurulum anında alınır),
   **K-368** (onay kararından sonra **yeni** çalıştırma açılır),
   **K-370** (onay kararı `AuditRecorder` ile değil doğrudan `IAuditLog` ile yazılır)
3. [`55-ASENKRON-ONAY-KUTUSU.md`](55-ASENKRON-ONAY-KUTUSU.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/55-ASENKRON-ONAY-KUTUSU.md
   ```
   Bu faz onay **kararının** verilme yolunu değil, onayın **gerekip
   gerekmediğinin** hesaplanmasını değiştirir.
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/postgresql.md`](hafiza/postgresql.md) (`jsonb` sütun ve benzersizlik
   indeksi tuzağı), [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md)
   (üç sağlayıcıda aynı şema), [`hafiza/frontend.md`](hafiza/frontend.md)
   (kural düzenleme ekranı)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) bölüm 3 (K2'nin tanımı ve iki istisnası)

---

## Amaç

Onay kuralları bugün **tool düzeyindedir**. `refund_order` ya **hep** onay
ister ya **hiç** istemez. Gerçek ihtiyaç ise koşulludur: "100 TL altı otomatik
geçsin, üstü insana sorulsun".

Bu kabalık **onay yorgunluğu** üretir. Kullanıcı her şeyi onaylamayı öğrenir ve
onay akışı — Faz 6'dan Faz 55'e kadar kurulan bütün makine — değerini
kaybeder.

- **F-61** — Onay kuralına argüman koşulu: bildirimsel karşılaştırma **ve**
  kodda kayıtlı politika.

Karar (kullanıcı, 2026-08-18): **ikisi birden.** Basit eşik ve liste
karşılaştırmaları veriden gelir ve arayüzden yönetilir; karmaşık kural koddan
gelir ve tam güce sahiptir.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`ToolApprovalRule.cs:20-47`](../src/AgentPrism.Abstractions/Approvals/ToolApprovalRule.cs) | Alanlar: `Id`, `TenantId`, `AgentName`, `ToolName`, `ArgumentsHash`, `CreatedBy`, `CreatedAt`. Koşul alanı **yok** |
| [`ToolApprovalRule.cs:41`](../src/AgentPrism.Abstractions/Approvals/ToolApprovalRule.cs) | `ArgumentsHash` **birebir aynı argüman** demektir. Bir koşul değil, bir parmak izidir |
| [`ToolApprovalRuleEvaluator.cs:104-111`](../src/AgentPrism.Core/Approvals/ToolApprovalRuleEvaluator.cs) | Değerlendirmenin **tek** yeri. `ArgumentsHash` `null` ise kural **her** çağrıyı otomatik onaylar; doluysa yalnız hash eşleşmesinde |
| [`ToolRegistry.cs:33-38`](../src/AgentPrism.Core/Tools/ToolRegistry.cs) | Onay sarmalaması **tek kapıdır** ve `ToolRegistry` içindedir. Yeni bir kapı açmaya gerek yoktur |
| [`ToolApprovalResolver.cs:179-180`](../src/AgentPrism.AspNetCore/Internal/ToolApprovalResolver.cs) | "Bu argümanları hatırla" kararı kuralı burada üretir — koşullu kural da buradan doğacaktır |
| [`GovernanceEndpoints.cs:558`](../src/AgentPrism.AspNetCore/Endpoints/GovernanceEndpoints.cs) | `GET /api/approvals/rules` ve kardeşleri vardır; kural yönetimi yüzeyi hazırdır |
| [`0002_observability.sql:76-90`](../src/AgentPrism.PostgreSql/Migrations/0002_observability.sql) | Tablo `tool_approval_rules`. 🚨 Benzersizlik indeksi `COALESCE(arguments_hash, '')` kullanıyor — koşul eklenince **anahtar da genişlemelidir**, yoksa aynı kapsam için sınırsız kural üretilir |
| [`0004_skill_scripts.sql:28-33`](../src/AgentPrism.PostgreSql/Migrations/0004_skill_scripts.sql) | Aynı ders orada yazılı: PostgreSQL'de `NULL` `NULL`'a eşit değildir; `COALESCE` ifade indeksi şarttır |
| [`48-GUARDRAILS.md`](48-GUARDRAILS.md) devir notu | İçerik denetimi **model sınırındadır**; tool argümanı denetimi bilerek kapsam dışı bırakıldı |
| `ToolApprovalRule` biçimi | 🚨 Aday listesi "`record` olduğu için **ek kurucu ister**" diyordu. **Ölçüldü: yanlış.** Tip konumsal (`positional`) değildir, `required init` özellikleri kullanır; yeni bir `init` özelliği eklemek ek kurucu **istemez** |

> Kanıtlar 2026-08-18 tarihinde doğrulandı.

---

## 63.1 — İki kaynak, tek karar

Değerlendirici tek bir soruya cevap verir: **bu çağrı insan onayı ister mi?**

```mermaid
flowchart TD
    A["Tool cagrisi"] --> B{"Tool RequiresApproval mi?"}
    B -->|"hayir"| C["Onay ISTENMEZ"]
    B -->|"evet"| D{"Kodda politika kayitli mi?"}
    D -->|"evet"| E["Politika calisir"]
    E --> F{"Sonuc"}
    F -->|"Required"| G["Onay ISTENIR"]
    F -->|"NotRequired"| C
    F -->|"Undecided"| H["Veri kurallari"]
    D -->|"hayir"| H
    H --> I{"Kosullari eslesen kural var mi?"}
    I -->|"evet"| C
    I -->|"hayir"| G
```

🚨 **Kod politikası veriden önce gelir.** Kod bir güvenlik sınırıdır; veri
arayüzden değiştirilebilir. Bir kurum "bu tool asla otomatik geçmesin" demek
isterse bunu kodda söyleyebilmelidir ve veri onu **ezememelidir**.

🚨 **Varsayılan hep "onay istenir"dir.** Eşleşme yoksa, koşul çözülemezse veya
tip uyuşmazsa sonuç **onay istenir** olur. Kapalı düşmek (`fail-closed`) bu
fazın en önemli davranış kuralıdır.

## 63.2 — Bildirimsel koşul: karşılaştırma, ifade değil

🚨 **K2 gevşemez.** Arayüzden **ifade** yazılamaz. Yalnız üçlü bir yapı
saklanır: **alan yolu · operatör · değer**.

| Operatör | Anlamı | Kabul edilen tip |
|---|---|---|
| `Equals` · `NotEquals` | Eşitlik | metin, sayı, mantıksal |
| `GreaterThan` · `GreaterThanOrEqual` | Büyüklük | yalnız sayı |
| `LessThan` · `LessThanOrEqual` | Küçüklük | yalnız sayı |
| `In` · `NotIn` | Liste üyeliği | metin, sayı |

Bir kuralın **bütün** koşulları eşleşmelidir (`AND`). `OR` **yoktur**; iki
kural yazılır. Bu, kural dilinin bir motora dönüşmesini engeller.

**Yol biçimi:** JSON argüman nesnesinde noktalı yol (`order.amount`). Dizi
indeksi **yoktur**. Yol çözülemezse koşul eşleşmez ve sonuç onay ister.

**Tip kuralı:** karşılaştırma yalnız aynı JSON tipinde çalışır. `amount` metin
gelirse ve kural sayı bekliyorsa eşleşme **olmaz**. Sessiz dönüştürme yapılmaz;
sessiz dönüştürme "100" ile 100'ü karıştırır ve bir güvenlik kuralını deler.

## 63.3 — Kodda kayıtlı politika

```csharp
builder.AddToolApprovalPolicy("refund_order", context =>
    context.GetNumber("amount") is { } amount && amount <= 100
        ? ToolApprovalDecision.NotRequired
        : ToolApprovalDecision.Required);
```

Politika **kurulum anında** kaydedilir. 🚨 K-218 birebir geçerlidir:
politikanın bağımlılıkları kurulum anında alınır, çalışma anında servis
sağlayıcıdan çözülmez — tool'un gördüğü servis sağlayıcı **boştur**.

Politika üç değer döndürebilir: `Required`, `NotRequired`, `Undecided`.
`Undecided` kararı veri kurallarına devreder. Bir istisna atarsa sonuç
**`Required`** olur ve hata loglanır; politika hatası bir tool'u sessizce
serbest bırakmamalıdır.

## 63.4 — Benzersizlik anahtarı ve migration

Koşul listesi `argument_conditions` adında bir `jsonb`/`text` sütununda yaşar.

🚨 **Benzersizlik indeksi genişlemek zorundadır.** Bugünkü anahtar
`(tenant_id, COALESCE(agent_name,''), tool_name, COALESCE(arguments_hash,''))`.
Koşullar buna girmezse aynı kapsam için **sınırsız** koşullu kural yazılabilir
ve hangisinin kazandığı belirsizleşir. Çözüm: koşulların **kanonik** biçiminden
türetilen bir `conditions_hash` sütunu anahtara eklenir.

Kanonik biçim uygulama anında sabitlenir: alanlar sıralanır, boşluk normalize
edilir. Aynı koşul kümesi her zaman aynı hash'i üretmelidir.

Üç migration seti gerekir. SQLite'ta `jsonb` yoktur; sütun `text` olur ve
sağlayıcı farkı [`hafiza/sqlite.md`](hafiza/sqlite.md) kuralına göre yazılır.

## 63.5 — Arayüz

Kural düzenleme ekranı koşul satırları kazanır: alan yolu, operatör listesi,
değer. Serbest metin **ifade kutusu yoktur** — operatör bir açılır listedir.
Bu, K2 sınırının arayüzdeki görünümüdür.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public enum ToolArgumentOperator
{
    Equals = 0,
    NotEquals = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5,
    In = 6,
    NotIn = 7,
}

/// <summary>One comparison against a tool call argument. Comparisons only; no expressions.</summary>
public sealed record ToolArgumentCondition
{
    /// <summary>Dotted path into the argument object, for example <c>order.amount</c>.</summary>
    public required string Path { get; init; }

    public required ToolArgumentOperator Operator { get; init; }

    /// <summary>The value compared against. For In/NotIn a JSON array.</summary>
    public required JsonElement Value { get; init; }
}

public sealed record ToolApprovalRule
{
    // ... mevcut alanlar

    /// <summary>All conditions must match for the rule to apply. Empty means the rule matches every call.</summary>
    public IReadOnlyList<ToolArgumentCondition> ArgumentConditions { get; init; } = [];
}

public enum ToolApprovalDecision
{
    Undecided = 0,
    NotRequired = 1,
    Required = 2,
}

/// <summary>The call being judged. Read-only.</summary>
public sealed class ToolApprovalContext
{
    public required string TenantId { get; init; }
    public required string ToolName { get; init; }
    public string? AgentName { get; init; }
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }

    public double? GetNumber(string path);
    public string? GetString(string path);
}

// AgentPrism.Core — kayit, K4: TryAdd ile
public static class AgentPrismToolApprovalPolicyExtensions
{
    public static IAgentPrismBuilder AddToolApprovalPolicy(
        this IAgentPrismBuilder builder,
        string toolName,
        Func<ToolApprovalContext, ToolApprovalDecision> policy);
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol · kapsam | Ne yapar |
|---|---|---|---|
| `POST` | `/api/approvals/rules` | Admin · `SecurityAdmin` | **Mevcut uç.** Gövde `argumentConditions` alanı kazanır |
| `GET` | `/api/approvals/rules` | Admin · `SecurityAdmin` | **Mevcut uç.** Yanıt koşulları taşır |

Yeni uç **yoktur**.

### Arayüz payı

Kural ekranına koşul satırı bileşeni eklenir. Bugünkü kullanım **165,4 KB
gzip / 250 KB**; artış uygulama anında ölçülüp bu belgeye yazılır. Yeni sözlük
anahtarları `en.ts` **ve** `tr.ts` içine girer (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Approvals/
├── ToolApprovalRule.cs              (ArgumentConditions alani)
├── ToolArgumentCondition.cs         (yeni)
├── ToolArgumentOperator.cs          (yeni)
├── ToolApprovalDecision.cs          (yeni)
└── ToolApprovalContext.cs           (yeni)

src/AgentPrism.Core/Approvals/
├── ToolApprovalRuleEvaluator.cs     (kosul degerlendirmesi + politika sirasi)
├── ToolArgumentConditionMatcher.cs  (yeni - tek karsilastirma noktasi)
└── ToolApprovalPolicyRegistry.cs    (yeni - kodda kayitli politikalar)

src/AgentPrism.Sql.Shared/Stores/
└── SqlApprovalAndMcpStores.cs       (yeni sutun + conditions_hash)

src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Migrations/
└── NNNN_approval_conditions.sql     (uc set - numara uygulama aninda)

src/AgentPrism.UI/frontend/src/
├── screens/                         (kural ekrani kosul satirlari)
└── locales/{en,tr}.ts               (yeni anahtarlar - K-228)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Koşulsuz eski kural artık eşleşmez (geriye dönük kırılma) | Sözleşme | `ToolApprovalRuleContract` — dört koşumda |
| Koşul eşleşmediğinde çağrı **otomatik geçer** | Birim | `ToolArgumentConditionMatcherTests` — kapalı düşme |
| Yol çözülemez (`order.amount` yok) | Birim | `ToolArgumentConditionMatcherTests` → onay istenir |
| Tip uyuşmaz (`"100"` metni ile sayı kuralı) | Birim | `ToolArgumentConditionMatcherTests` → eşleşme **yok** |
| Sayısal karşılaştırma metin üzerinde çalışır | Birim | `ToolArgumentConditionMatcherTests` |
| `In` listesi boş gelir | Birim | `ToolArgumentConditionMatcherTests` |
| Aşırı derin veya çok uzun yol | Birim | `ToolArgumentConditionMatcherTests` — sınır ve red |
| Kod politikası istisna atar | Birim | `ToolApprovalPolicyRegistryTests` → `Required` ve log |
| Kod politikası veri kuralını ezemiyor | Fonksiyonel | `ToolApprovalPolicyTests` |
| İki kural aynı kapsamda çakışır | Sözleşme | benzersizlik indeksi ihlali → `409` |
| Başka kiracının kuralı bu kiracıyı etkiler | Sözleşme (`TenantIsolationContract`) | dört koşumda birden |
| Koşullu kural üç sağlayıcıda farklı seri hâle gelir | Sözleşme | `ToolApprovalRuleContract` |
| Eşzamanlı iki kural yazımı aynı `conditions_hash` üretir | Fonksiyonel | `ToolApprovalRuleConcurrencyTests` |
| Onay kararı denetim izine yazılmaz | Fonksiyonel | `ApprovalAuditTests` (K-370 emsali) |
| Arayüzden serbest ifade yazılabilir | Birim | `ToolApprovalContractTests` — böyle bir alan **yok** |
| Sözlük anahtarı eksik | Derleme | `tsc --noEmit` (K-228) |
| Koşullu kural arayüzden kaydedilip geri okunur | E2E (Playwright) | `UiTests` |

Sözleşme testi `tests/Shared/Contracts/` altına — hem bellek içi hem üç SQL
sağlayıcısı üzerinde koşar.

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](manuel-test/13-KIRACI-VE-GUVENLIK.md)
> içine eklenecek case'lerin taslağı.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Koşulsuz eski kural kayıtlı | Tool'u çağırt | Bugünkü davranış birebir korunur |
| 2 | `amount <= 100` koşullu kural | 50 ile çağırt | Onay **istenmez**, tool çalışır |
| 3 | Aynı kural | 500 ile çağırt | Onay **istenir** |
| 4 | Aynı kural | `amount` hiç gönderilmez | Onay **istenir** |
| 5 | Aynı kural | `amount` `"50"` metni olarak gelir | Onay **istenir** (tip uyuşmaz) |
| 6 | Kodda `Required` dönen politika + otomatik geçiren veri kuralı | Çağırt | Onay **istenir** — kod kazanır |
| 7 | — | Arayüzden koşul ekle, kaydet, geri oku | Koşul olduğu gibi görünür |
| 8 | — | Aynı kapsam ve aynı koşulla ikinci kural yaz | `409` |
| 9 | — | Denetim izini aç | Kural yazımı ve onay kararı kayıtlı |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `ArgumentsHash` ile `ArgumentConditions` aynı kuralda birlikte olabilir mi? | A: Hayır, biri seçilir · B: Evet, ikisi de `AND` | **A.** İkisi aynı işin iki biçimidir; birlikte olması "birebir aynı argüman **ve** koşul" gibi anlamsız bir kural üretir. Doğrulama `400` ile reddeder |
| 2 | Koşul sayısı sınırlanacak mı? | A: Evet, kural başına sabit üst sınır · B: Hayır | **A.** Sınırsız koşul bir değerlendirme maliyetidir ve arayüzü de bozar. Sayı uygulama anında seçilir ve belgeye yazılır |
| 3 | `In` listesinin uzunluğu sınırlanacak mı? | A: Evet · B: Hayır | **A.** Aynı gerekçe |
| 4 | Kod politikası kiracı görebilmeli mi? | A: Evet, `ToolApprovalContext.TenantId` taşır · B: Hayır | **A.** Çok kiracılı bir tüketici politikayı kiracıya göre yazmak ister; kiracıyı gizlemek onu ambient okumaya iter ve bu daha risklidir |
| 5 | Koşullar denetim izine yazılırken maskelenmeli mi? | A: Evet, `secret` filtresinden geçer · B: Hayır | **A.** Koşul değeri müşteri verisi taşıyabilir; denetim kaydı `AuditRecorder`'ın `secret` filtresini zaten kullanıyor |

---

## Bitiş Ölçütleri (DoD)

- [ ] Koşulsuz eski kurallar **birebir** eskisi gibi çalışır (sözleşme testi dört koşumda)
- [ ] `amount <= 100` koşullu kural 50'de otomatik geçer, 500'de onay ister
- [ ] Yol bulunamazsa, tip uyuşmazsa veya koşul çözülemezse sonuç **onay istenir**
- [ ] Kodda kayıtlı politika veri kuralını **ezer**; istisna atarsa `Required` döner
- [ ] Aynı kapsam ve aynı koşul kümesiyle ikinci kural `409` ile reddedilir
- [ ] Üç sağlayıcıda da migration uygulanır ve sözleşme testleri geçer
- [ ] Arayüzde serbest ifade kutusu **yoktur**; operatör açılır listedir
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`concepts/governance.md`, `concepts/tools.md`); `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# Kosullu kural yaz
curl -s -X POST http://localhost:5081/agentprism/api/approvals/rules \
  -H 'Content-Type: application/json' \
  -d '{"toolName":"refund_order","argumentConditions":[{"path":"amount","operator":"lessThanOrEqual","value":100}]}'

# Esik altinda otomatik gecer
curl -s -X POST http://localhost:5081/agentprism/api/agents/demo/run \
  -H 'Content-Type: application/json' \
  -d '{"message":"50 TL iade et","sessionId":"s1"}'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Koşul dili bir kural motoruna dönüşür | `OR` yok, ifade yok, dizi indeksi yok, koşul sayısı sınırlı. Operatör listesi bu belgede kapalıdır |
| 🚨 Sessiz tip dönüştürme bir güvenlik kuralını deler | Aynı JSON tipi şartı; dönüştürme yapılmaz; testle kapatılır |
| Kapalı düşme unutulur ve eşleşmeyen koşul otomatik geçer | Varsayılan `Required`; bu davranış beş ayrı testte kanıtlanır |
| Benzersizlik indeksi genişletilmez, kural çoğalır | `conditions_hash` anahtara girer; `COALESCE` dersi (0004) tekrarlanır |
| Kod politikası çalışma anında servis çözmeye çalışır | 🚨 K-218 plana yazıldı; politika kurulum anında kaydedilir |
| Var olan kurallar migration'da bozulur | Yeni sütun `NULL` kabul eder; boş koşul listesi "her çağrı" demektir — eski anlam korunur |

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

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
