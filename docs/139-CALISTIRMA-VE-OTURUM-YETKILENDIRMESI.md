# Faz 139 — Çalıştırma ve Oturum Yetkilendirmesi

> **Durum:** ✅ Tamamlandı (2026-09-03)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-185** (tüketici turu 3, A1 + A8)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — yeni kontrat + `AgentRunScope`'a iki alan. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (yalnız başlık), yani shipped giriş **sıfır**: bugün eklemek bedava, GA'dan sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `guides/embedding.md` (genişleme noktası listesi), `concepts/sessions.md`, `concepts/runs.md`, `capabilities.md` · sevk edilen: XML `<example>`, `src/AgentPrism.Abstractions/README.md`
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-103\|K-283\|K-162" docs/KARARLAR.md
   ```
   **K-103** (onay kontrolü run kuyruğa taşınınca handler'dan düşer),
   **K-283** (bir davranışı düzeltmek ona dayanan çağıranı sessizce değiştirir —
   oturum deposu kiracıyla sınırlanınca ses ucunun reddi etkisiz kaldı),
   **K-162** (devam eden run kota aşımında kesilmez)
3. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/http-uc-tuzaklari.md`](hafiza/http-uc-tuzaklari.md) (uç filtresi ve
   gövde bağlama sırası) · [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md)
   (singleton/scoped sınırı — kontrat singleton olmak zorundadır)
4. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](MIMARI-GUVENLIK.md) — kiracı ve rol bölümü

---

## Amaç

Bugün bir kiracıdaki her `Operator`, aynı kiracıdaki **başka bir kullanıcının**
konuşmasını okuyabiliyor, ona yazabiliyor ve silebiliyor. AgentPrism sahipliği
kiracı düzeyinde çiziyor; kiracı **içindeki** kullanıcıyı hiçbir yerde
ayırmıyor. Bu faz sahipliği AgentPrism'e **öğretmez** — tüketiciye **sorar**.

- **F-185** — Run başlatmayı ve session erişimini yetkilendiren, varsayılan
  kapalı, fail-closed bir genişleme noktası; ve çağıran kimliğinin
  `AgentRunScope` üzerinden tool gövdesine ulaşması.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentEndpoints.cs:193-233`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs) | Run yolunda dört kapı var (`DrainGate` · `RunAttributionGate` · `QuotaGate` · `PreflightGate`). **Hiçbiri** çağıranı session sahipliğiyle karşılaştırmıyor |
| [`AgentEndpoints.cs:825`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs) | `sessionId` **istek gövdesinden** geliyor ve doğrudan `GetOrCreateSessionAsync`'e veriliyor |
| [`SessionEndpoints.cs:22,52,65,79`](../src/AgentPrism.AspNetCore/Endpoints/SessionEndpoints.cs) | Dört session ucu yalnız rol (`Reader`/`Operator`) ve API key scope taşıyor |
| [`AgentSessionManager.cs:365`](../src/AgentPrism.Core/Sessions/AgentSessionManager.cs) | Liste sorgusu yalnız `_tenantContext.TenantId` ile daralıyor |
| [`AgentPrismDiagnosticsCollector.cs:221-245`](../src/AgentPrism.Core/Diagnostics/AgentPrismDiagnosticsCollector.cs) | Sevk edilen **beş** genişleme noktası sayılıyor; run başlatmayı veya session okumayı yetkilendiren **yok** |
| [`AgentPrismRunContext.cs:52`](../src/AgentPrism.Core/Recording/AgentPrismRunContext.cs) | `AgentRunScope` `UserId` ve `Labels` taşımıyor |
| [`RunRecord.cs:47,57`](../src/AgentPrism.Abstractions/Runs/RunRecord.cs) | Aynı iki değer **zaten toplanıyor** ve `runs` satırına yazılıyor — bir adım ötede düşürülüyor |
| [`RunAttributionGate.cs:19`](../src/AgentPrism.AspNetCore/RateLimiting/RunAttributionGate.cs) | 🚨 *"the endpoints that start runs do not share one"* — run başlatan uçlar **ortak filtre paylaşmıyor**; kapı her uca **tek tek** eklenmelidir |

> Kanıtlar 2026-09-03 tarihinde doğrulandı.

### Neden bu kalem AgentPrism'e ait

Gerekçe kütüphanenin kendi ifadesidir. `IRunAttributionContext` `userId` için
şunu yazıyor: *"A `userId` field on `POST /api/agents/{name}/run` would let any
client write spend against another user's name, so the body is not a source of
attribution at all."* Aynı muhakeme `sessionId` için de geçerlidir ve sonucu
daha ağırdır: yanlış atıf bir **kayıt** hatasıdır, yanlış session bir
**sızıntıdır**.

---

## 139.1 — Kapı nereye girer

🚨 **En kritik yapısal iddia budur ve ölçülerek doğrulandı.** Run başlatan
uçlar ortak bir filtre paylaşmaz; her biri kapılarını **kendi gövdesinde**
açıkça çağırır. Bir uç atlanırsa kapı bir bypass'a döner ve yanlış bir güvenlik
hissi üretir — bu, kapının hiç olmamasından kötüdür.

Kullanıcı kararı (2026-09-03): **dört yüzeyin dördü de kapsanır.**

```mermaid
flowchart TD
    A["POST /api/agents/{name}/run<br/>AgentEndpoints.cs:169"] --> G
    B["POST /api/workflows/.../run<br/>WorkflowEndpoints.cs:397"] --> G
    C["Inbound trigger<br/>TriggerEndpoints.cs:301"] --> G
    D["OpenAI uyumlu yüzey<br/>OpenAIResponsesEndpoints.cs"] --> G
    G["RunAuthorizationGate<br/>(QuotaGate ile aynı çağrı şekli)"] --> H
    H{"IRunAuthorizationHandler<br/>kayıtlı mı?"}
    H -- "hayır" --> I["İzin ver — bugünkü davranış"]
    H -- "evet" --> J["AuthorizeRunAsync"]
    J -- "Allow" --> K["Run başlar"]
    J -- "Deny" --> L["403"]
    J -- "throw" --> L
```

Kapı `QuotaGate` ile **aynı çağrı şeklini** taşır: bir `internal static` yardımcı,
uç filtresi değil. Gerekçe `QuotaGate.cs:12-18`'de yazılı ve burada da geçerli:
OpenAI uyumlu uçta agent adı rota değerinde değil gövdenin `model` alanındadır;
gövdeyi okuyan bir filtre isteği iki kez ayrıştırır.

**Sıra:** `DrainGate` → `RunAttributionGate` → **`RunAuthorizationGate`** →
`QuotaGate` → `PreflightGate`.

Gerekçe: yetkilendirme atıftan **sonra** gelir (kapı `UserId`'yi atıftan alır)
ve kotadan **önce** gelir — yetkisiz bir çağrının kiracının kotasını tüketmesi
yanlıştır.

## 139.2 — Session uçlarının kapısı

Dört session ucu (`SessionEndpoints.cs:22,52,65,79`) `AuthorizeSessionAsync`
çağırır. Red yanıtları:

| Erişim | Red yanıtı | Gerekçe |
|---|---|---|
| `Read` · `Delete` · `Branch` | **`404`** | "Görmemesi gereken kaynak `404`, `403` değil" kuralı. `403`, kaynağın **var olduğunu** sızdırır |
| `List` | **`403`** | Liste bir kaynak değil bir işlemdir; varlığı sızdıracak bir kimlik yok |
| `Start` (run) | **`403`** | Agent adı zaten katalogda görünür; gizlenecek bir varlık yok |

🚨 **`List` filtrelenmez, reddedilir.** Tüketici bunu açıkça kabul etti:
*"Filtreleme isteyen tüketici listeyi zaten kendi tarafında tutuyor."*
Filtreleme sayfalama sözleşmesini bozar — `skip`/`take` sunucu tarafında
uygulanır ve süzülmüş bir sayfa eksik döner.

## 139.3 — Sözleşme

Kullanıcı kararı (2026-09-03): **iki metot, kapalı enum.**
`IToolAuthorizationHandler` ile birebir simetriktir; tüketici o deseni zaten
tanıyor ve fail-closed davranışı aynıdır.

## 139.4 — Çağıran kimliğinin scope'a taşınması (A8)

`AgentRunScope` `UserId` ve `Labels` alır. Yeni kavram gelmez: değer
`IRunAttributionContext`'ten **zaten** okunuyor ve `runs` satırına **zaten**
yazılıyor (`RunRecord.cs:47,57`). AgentPrism'in duruşu korunur — opak string,
çözülmez, doğrulanmaz, yorumlanmaz.

🚨 **`RunAttributionReader.Read` üzerinden okunur, arayüze doğrudan
dokunulmaz.** Sebep dosyanın kendi yorumunda yazılı: tüketici implementasyonu
kendi kimlik hattına uzanır ve orada `throw` edebilir; gözlemlenebilirlik
işlevselliği bozmaz.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public interface IRunAuthorizationHandler
{
    ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request, CancellationToken cancellationToken = default);

    ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request, CancellationToken cancellationToken = default);
}

public sealed record RunAuthorizationRequest
{
    public required string TenantId { get; init; }
    public required string AgentName { get; init; }
    public string? SessionId { get; init; }
    public string? UserId { get; init; }
    public required RunAccess Access { get; init; }
}

public sealed record SessionAuthorizationRequest
{
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public string? UserId { get; init; }
    public required SessionAccess Access { get; init; }
}

public enum RunAccess { Start = 0 }
public enum SessionAccess { Read = 0, List = 1, Delete = 2, Branch = 3 }

public sealed record RunAuthorizationResult
{
    public required bool IsAllowed { get; init; }
    public string? Reason { get; init; }

    public static RunAuthorizationResult Allow();
    public static RunAuthorizationResult Deny(string reason);
}

// AgentPrism.Core — AgentRunScope'a eklenen alanlar
public sealed record AgentRunScope
{
    public string? UserId { get; init; }
    public IReadOnlyDictionary<string, string>? Labels { get; init; }
}
```

**Kayıt:** `TryAddSingleton<IRunAuthorizationHandler, AllowAllRunAuthorizationHandler>()`.
K1 (sıfır sürpriz) ve K4 (her nokta değiştirilebilir) gereği: kaydedilmemiş bir
kurulumda **hiçbir şey değişmez**, tüketicinin kaydı kazanır.

🚨 **Singleton zorunludur.** `IRunAttributionContext` ile aynı gerekçe:
singleton servisler buna bağımlanır, scoped kayıt captive dependency olur.
İstek başına durum `IHttpContextAccessor` ile çözülür. Sözleşme bunu XML
dokümanında **açıkça** yazar.

### HTTP `endpoint`'leri

Yeni uç **yok**. Var olan uçların davranışı değişir:

| Metot | Yol | Rol | Yeni davranış |
|---|---|---|---|
| `POST` | `/api/agents/{name}/run` | Operator | Handler reddederse `403` |
| `POST` | `/api/workflows/{name}/run` | Operator | Handler reddederse `403` |
| `POST` | `/v1/responses` (OpenAI uyumlu) | Operator | Handler reddederse `403` |
| `GET` | `/api/sessions` | Reader | Handler reddederse `403` |
| `GET` | `/api/sessions/{sessionId}` | Reader | Handler reddederse `404` |
| `DELETE` | `/api/sessions/{sessionId}` | Operator | Handler reddederse `404` |
| `POST` | `/api/sessions/{sessionId}/branch` | Operator | Handler reddederse `404` |

### Arayüz payı

Yok. Konsol `Operator` olarak koşar ve varsayılan handler her şeye izin verir.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
└── Runs/
    └── RunAuthorizationTypes.cs          (yeni — kontrat + istek/sonuç tipleri)

src/AgentPrism.Core/
├── Runs/
│   └── AllowAllRunAuthorizationHandler.cs (yeni — varsayılan, her şeye izin)
├── Recording/
│   └── AgentPrismRunContext.cs           (AgentRunScope: UserId + Labels)
├── Recording/
│   └── RunRecordingAgent.Lifecycle.cs    (scope kurulurken iki alan doldurulur)
└── Diagnostics/
    └── AgentPrismDiagnosticsCollector.cs  (altıncı genişleme noktası)

src/AgentPrism.AspNetCore/
├── RateLimiting/
│   └── RunAuthorizationGate.cs           (yeni — QuotaGate ile aynı şekil)
├── Endpoints/AgentEndpoints.cs           (kapı çağrısı)
├── Endpoints/WorkflowEndpoints.cs        (kapı çağrısı)
├── Endpoints/TriggerEndpoints.cs         (kapı çağrısı)
├── Endpoints/SessionEndpoints.cs         (dört uç)
└── OpenAICompat/OpenAIResponsesEndpoints.cs (kapı çağrısı)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| 🚨 Bir run başlatan uç kapıyı **çağırmayı unutur** (bypass) | Fonksiyonel | `RunAuthorizationCoverageTests` — dört yüzeyin **dördü** ayrı ayrı |
| Handler `throw` ederse çağrı **izin verilir** (fail-open) | Fonksiyonel | `RunAuthorizationTests` |
| Handler kaydedilmemişken davranış değişir | Fonksiyonel | `RunAuthorizationTests` |
| Reddedilen session okuması `403` döner (`404` yerine) ve varlığı sızdırır | Fonksiyonel | `SecurityTests` |
| Reddedilen run kotayı **tüketir** (kapı sırası yanlış) | Fonksiyonel | `RunAuthorizationOrderTests` |
| Handler `UserId` alamaz (atıf kapısından **sonra** çalışmıyor) | Fonksiyonel | `RunAuthorizationTests` |
| Başka kiracının session'ı kapıya `TenantId` olmadan gider | Sözleşme | `TenantIsolationContract` |
| Kuyruğa alınan run'da kapı worker tarafında **düşer** (K-103 sınıfı) | Fonksiyonel | `QueuedRunAuthorizationTests` |
| `AgentRunScope.UserId` akışlı yolda `null` kalır | Fonksiyonel | `RunScopeAttributionTests` |
| Handler iptal edilirse run asılı kalır | Fonksiyonel | `RunAuthorizationTests` |
| Eşzamanlı iki run aynı handler örneğinde yarışır | Fonksiyonel | `RunAuthorizationConcurrencyTests` |
| Handler `null` `Reason` ile `Deny` döner | Birim | `RunAuthorizationResultTests` |

🚨 **Birim testi bu fazı kanıtlayamaz.** Davranış dört sınır geçiyor: DI ·
HTTP · kiracı · akış. Kapsama testi (`RunAuthorizationCoverageTests`) fazın
**en önemli** testidir — planın yapısal iddiası tam olarak orada ölçülür.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Handler kaydedilmemiş | `support` agent'ına run at | Bugünkü davranış; hiçbir fark yok |
| 2 | `UserId = "a"` dışındaki her şeyi reddeden handler | `a` kimliğiyle run at | `200`, run çalışır |
| 3 | Aynı handler | `b` kimliğiyle run at | `403`, `runs` satırı **açılmaz** |
| 4 | Aynı handler | `b` ile `a`'nın `sessionId`'siyle run at | `403` |
| 5 | Aynı handler | `b` ile `GET /api/sessions/{a-oturumu}` | **`404`** (`403` değil) |
| 6 | Aynı handler | `b` ile `GET /api/sessions` | `403` |
| 7 | `throw` eden handler | Run at | `403` — fail-closed |
| 8 | Aynı handler | `POST /v1/responses` (OpenAI uyumlu) | `403` — bypass yok |
| 9 | Aynı handler | Workflow run + inbound trigger | İkisi de `403` |
| 10 | Kimliği loglayan tool | `a` ile run at | Tool gövdesi `UserId = "a"` görür |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kapı `IAgentPrismDiagnosticsCollector`'a altıncı genişleme noktası olarak girsin mi? | A: Girsin · B: Girmesin | **A** — beş noktanın hepsi orada; kapıyı dışarıda bırakmak envanteri yalanlar |
| 2 | `SessionAccess`'e ileride `Write` eklenirse mevcut handler'lar ne görür? | A: Kapalı enum, yeni değer additive · B: Şimdiden `Write` ekle | **A** — bugün `Write` diye bir işlem yok; olmayan bir erişimi ilan etmek yanlış |
| 3 | Voice oturumları (`VoiceEndpoints`) kapsansın mı? | A: Bu fazda · B: Ayrı faz | **B** — ses oturumu ayrı bir depo ve ömür taşır; K-283 vakası tam olarak orada yaşandı, ölçülmeden girilmez |

---

## Bitiş Ölçütleri (DoD)

- [x] Handler kaydedilmemiş kurulumda **hiçbir** davranış değişmez (case 1 kanıt) — `Nothing_changes_when_no_handler_is_registered`, canlı doğrulama aşağıda
- [x] Dört run başlatan yüzeyin **dördü** de kapıdan geçer; `RunAuthorizationEndpointTests` dördünü ayrı ayrı kanıtlar (`Handler_denies_a_different_user_and_no_run_row_opens`, `Workflow_run_is_covered`, `Inbound_trigger_is_covered`, `OpenAI_compatible_endpoint_is_covered`)
- [x] `throw` eden handler çağrıyı **reddeder** (fail-closed) — `Throwing_handler_denies_the_run_fail_closed`
- [x] Reddedilen session okuması `404`, reddedilen liste `403` döner — `Denied_session_read_returns_404_not_403`, `Denied_session_list_returns_403`, `Denied_session_delete_returns_404`, `Denied_session_branch_returns_404`
- [x] Reddedilen run `runs` satırı **açmaz** ve kota **tüketmez** — `Handler_denies_a_different_user_and_no_run_row_opens`, `Denied_run_does_not_consume_the_quota`
- [x] `AgentRunScope.UserId` tool gövdesinde görünür; akışlı yolda da dolu — `Allowed_user_id_reaches_the_tool_via_scope` (varsayılan akışlı/SSE yolu üzerinden)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-SEC-140..150, tamamı otomatik ve yeşil
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları — gerçek çıktı (2026-09-03, `samples/AgentPrism.Api`, handler kayıtlı DEĞİL)

```bash
$ curl -s "$APU/api/diagnostics" -H "$APB" | python3 -c "..."
6
ITenantContext SingleTenantContext True
IRunAttributionContext DemoRunAttributionContext False
IToolAuthorizationHandler AllowAllToolAuthorizationHandler True
IRunAuthorizationHandler AllowAllRunAuthorizationHandler True
IRunEventSink (none) True
IAttachmentStorage (database) True

$ curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" \
     -H "$APB" -H 'content-type: application/json' -d '{"message":"What is your return policy in one sentence?"}'
# ... SSE akışı, gerçek OpenAI yanıtı ("You can return most unused items within 30 days...") ...
HTTP: 200

$ curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions/does-not-exist-xyz" -H "$APB"
{"type":"...","title":"Session not found","status":404,"detail":"There is no session with id 'does-not-exist-xyz'."}
HTTP: 404
```

Bu üç çağrı, handler kayıtlı OLMADIĞI (K1 varsayılan) davranışın gerçek bir
sunucuda bozulmadığını kanıtlar. Reddeden bir handler'ın 403/404 ürettiği
davranış — `samples/AgentPrism.Api`'ye geçici kod eklemek yerine —
`RunAuthorizationEndpointTests`'in 18 testinde gerçek bir `TestServer`
üzerinden (Kestrel'in kendisi değil ama aynı `RequestDelegate` boru hattı)
kanıtlanmıştır; bu skill'in Adım 2 notu ("birim testleri geçmesi yetmez")
model çağrısı içeren gerçek entegrasyon davranışına ("yapılandırma okunmuyor",
`AsyncLocal` kaybı gibi) işaret eder — bu fazın kapısı ise saf HTTP
yönlendirme/red mantığıdır ve `TestServer` bunu Kestrel'le birebir aynı
kod yolundan (`RequestDelegateFactory`) çalıştırır.

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Bir run başlatan uç atlanır ve kapı bypass'a döner | Fonksiyonel kanıt: `RunAuthorizationEndpointTests` dört yüzeyi ayrı ayrı, reddeden bir handler'la kilitler. İkinci, daha ucuz katman: `RunAuthorizationCoverageTests` (Core.UnitTests/Architecture) dört bilinen dosyanın kaynak metnini `RunAuthorizationGate.CheckRunAsync(` çağrısı için tarar — host açmadan, saniyeler içinde. Yeni bir BEŞİNCİ run ucu eklenirse bu tarama onu göremez (hangi dosyanın "run başlatan" olduğu mekanik bir karar değildir); yakaladığı şey bilinen dört çağrı yerinden birinin bir refactor'da SESSİZCE silinmesidir |
| Kapı sırası yanlış olur; yetkisiz çağrı kota tüketir | `RunAuthorizationOrderTests` sırayı ölçer |
| Handler scoped kaydedilir; captive dependency oluşur | XML dokümanı singleton zorunluluğunu yazar; `AgentPrismDiagnosticsCollector` yanlış ömrü rapor eder |
| `UserId` akışlı yolda kaybolur (`AsyncLocal` tuzağı) | 🚨 `MEMORY.md`: yazım çağırana akmaz. Scope, run'ı başlatan metodun **kendi gövdesinde** kurulur; `RunScopeAttributionTests` akışlı yolu ayrıca ölçer |
| Kuyruğa alınan run'da kapı düşer (K-103 sınıfı) | `QueuedRunAuthorizationTests`. Bu tam olarak K-103'ün yaşandığı sınıftır |
| Public yüzey büyür ve GA'da kırıcı olur | Shipped giriş bugün **sıfır** (ölçüldü); pencere açık |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **`SessionAuthorizationRequest.SessionId` planın taslağında `required string`
   idi, gerçekte `string?`.** `List` erişiminin tekil bir session kimliği yok;
   `required` bırakılsaydı çağıran boş dizge gibi bir sentinel uydurmak zorunda
   kalırdı — K1'in "sıfır sürpriz" ruhuna aykırı bir gizli sözleşme. `Access ==
   List` iken `null`, diğer üç erişimde her zaman dolu.
2. **`RunAuthorizationGate.CheckRunAsync`/`CheckSessionAsync` planın ima ettiği
   gibi `IResult?` değil, `ProblemHttpResult?` döner.** Ölçüldü:
   `Microsoft.AspNetCore.Http.Results.Problem(...)` derleme-anı `IResult`
   döndürür, `TypedResults.Problem(...)` ise `ProblemHttpResult`. Session
   uçlarının üçü (`GetSessionAsync`, `DeleteSessionAsync`, `BranchSessionAsync`)
   zaten `Results<T, ProblemHttpResult>` tipinde imzalar taşıyordu; gate `IResult`
   dönseydi bu union'lara **implicit olarak dönüşemezdi**. Bkz.
   `docs/hafiza/http-uc-tuzaklari.md`.
3. **`GET /api/sessions` ucunun imzası `Task<IResult>`'a gevşetilip sonra geri
   `Task<Results<Ok<IReadOnlyList<SessionRecord>>, ProblemHttpResult>>>`'a
   döndürüldü.** İlk gevşetme `OpenApiSnapshotTests` refresh'inde 200 yanıtının
   OpenAPI çıktısından SESSİZCE düştüğünü, yerine yalnız 403'ün kaldığını
   gösterdi — somut dönüş tipi olmayınca üretici yalnız açık
   `.ProducesProblem(...)` çağrısını görüyor. Aynı hafıza notu.
4. **Plandaki test sınıfı isimleri (`RunAuthorizationCoverageTests`,
   `RunAuthorizationOrderTests`, `QueuedRunAuthorizationTests`,
   `RunScopeAttributionTests`, `RunAuthorizationConcurrencyTests`) ayrı
   dosyalara açılmadı; tek bir `RunAuthorizationEndpointTests` (18 test) altında
   toplandı.** Her biri plandaki senaryoyu birebir kanıtlıyor (bkz. "Testler"),
   yalnız dosya sınırı farklı — tekrarlanan host-kurulum kodunu (`StartAsync`
   yardımcı metodu) tek dosyada paylaşmak, aynı senaryoyu beş küçük dosyaya
   bölmekten daha az tekrar üretti.
5. **`TenantIsolationContract` (plandaki sözleşme testi tablosu) ayrı bir
   `Contracts/` sınıfı olarak açılmadı.** Bu fazda kalıcı bir session-sahiplik
   veri modeli yok — kanıtlanacak şey yalnız "kapı kendi `TenantId` değeri
   uydurmuyor, `ITenantContext`'ten okuyor" — bu, `Handler_receives_the_ambient_tenant`
   fonksiyonel testiyle tam olarak kanıtlanıyor; SQL/bellek içi ayrımı taşıyan
   bir sözleşme testi burada fazladan soyutlama olurdu.
6. **`AgentPrismDiagnosticsCollector`'ın dokümanı "beş" yerine "altı" genişleme
   noktasından söz edecek şekilde güncellendi** (`AgentPrismDiagnosticsReport.cs`,
   `ExtensionPointDiagnostic.cs`) — plan bunu açıkça yazmıyordu ama Açık Soru
   1'in "A" cevabının doğal sonucu.

## Bu Fazda Verilen Kararlar

- **K-670** — `IRunAuthorizationHandler` dört run başlatan yüzeyin dördünü de
  kapsar; kapı `QuotaGate` ile aynı çağrı şeklini taşır (elle çağrı, ortak
  `IEndpointFilter` değil). Gerekçe ve sıra (`DrainGate → RunAttributionGate →
  RunAuthorizationGate → QuotaGate → PreflightGate`): `docs/KARARLAR.md`.
- **K-671** — Reddedilen session `List` erişimi `403` döner (asla filtrelenmez);
  reddedilen `Read`/`Delete`/`Branch` `404` döner ve gövdesi gerçekten var
  olmayan bir session'la birebir aynıdır. Gerekçe: `docs/KARARLAR.md`.

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions/Runs/RunAuthorizationTypes.cs
public interface IRunAuthorizationHandler
{
    ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request, CancellationToken cancellationToken = default);

    ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request, CancellationToken cancellationToken = default);
}

public sealed record RunAuthorizationRequest
{
    public required string TenantId { get; init; }
    public required string AgentName { get; init; }
    public string? SessionId { get; init; }
    public string? UserId { get; init; }
    public required RunAccess Access { get; init; }
}

public sealed record SessionAuthorizationRequest
{
    public required string TenantId { get; init; }
    public string? SessionId { get; init; }          // null yalnız SessionAccess.List için — planla fark
    public string? UserId { get; init; }
    public required SessionAccess Access { get; init; }
}

public enum RunAccess { Start = 0 }
public enum SessionAccess { Read = 0, List = 1, Delete = 2, Branch = 3 }

public sealed record RunAuthorizationResult
{
    public required bool IsAllowed { get; init; }
    public string? Reason { get; init; }

    public static RunAuthorizationResult Allow();
    public static RunAuthorizationResult Deny(string reason);   // reason boş/whitespace ise ArgumentException
}

// AgentPrism.Core/Recording/AgentPrismRunContext.cs — AgentRunScope'a eklenen alanlar
public sealed record AgentRunScope
{
    public string? UserId { get; init; }
    public IReadOnlyDictionary<string, string>? Labels { get; init; }
    // ... plandaki gibi
}
```

**Kayıt:** `TryAddSingleton<IRunAuthorizationHandler, AllowAllRunAuthorizationHandler>()`
— plandakiyle birebir aynı.

**Diagnostics:** `AgentPrismDiagnosticsCollector`'ın kurucusuna
`IRunAuthorizationHandler runAuthorizationHandler` eklendi (public
constructor — `dotnet pack` sonrası `PublicAPI.Shipped.txt`'e girecek);
`ExtensionPoints` artık 6 satır döndürür.

### HTTP davranışı (yeni uç yok, var olanların davranışı değişti)

Plandaki tablo birebir gerçekleşti — bkz. faz dokümanının "HTTP `endpoint`'leri"
bölümü. Tek ek: `POST /v1/responses` ve `POST /api/triggers/{tenantId}/{name}`
uçlarının `.ProducesProblem`/`.Produces` zincirlerine `403` eklendi (OpenAPI
snapshot'ı bunu şimdi taşıyor).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
├── Runs/RunAuthorizationTypes.cs                  (yeni)
└── Diagnostics/AgentPrismDiagnosticsReport.cs      (yorum: "beş" → "altı")
└── Diagnostics/ExtensionPointDiagnostic.cs         (yorum: "beş" → "altı")

src/AgentPrism.Core/
├── Runs/AllowAllRunAuthorizationHandler.cs         (yeni)
├── Recording/AgentPrismRunContext.cs               (AgentRunScope.UserId/Labels)
├── Recording/RunRecordingAgent.Lifecycle.cs         (scope doldurma)
├── AgentPrismServiceCollectionExtensions.Registration.Core.cs  (DI kaydı ×2)
└── Diagnostics/AgentPrismDiagnosticsCollector.cs    (6. genişleme noktası)

src/AgentPrism.AspNetCore/
├── RateLimiting/RunAuthorizationGate.cs             (yeni)
├── Endpoints/AgentEndpoints.cs                      (kapı çağrısı + doküman)
├── Endpoints/WorkflowEndpoints.cs                   (kapı çağrısı + doküman)
├── Endpoints/TriggerEndpoints.cs                    (kapı çağrısı + doküman)
├── Endpoints/SessionEndpoints.cs                    (dört uç + doküman)
└── OpenAICompat/OpenAIResponsesEndpoints.cs         (kapı çağrısı + doküman)

tests/
├── AgentPrism.AspNetCore.FunctionalTests/RunAuthorizationEndpointTests.cs  (yeni, 18 test)
├── AgentPrism.AspNetCore.FunctionalTests/DiagnosticsEndpointTests.cs       (5→6 nokta)
├── AgentPrism.Core.UnitTests/Runs/RunAuthorizationResultTests.cs          (yeni, 4 test)
├── AgentPrism.Core.UnitTests/Architecture/RunAuthorizationCoverageTests.cs (yeni, 2 test — denetim 🟡 #1'e karşılık)
├── AgentPrism.Core.UnitTests/Diagnostics/DiagnosticsCollectorTests.cs      (5→6 nokta)
├── AgentPrism.Core.UnitTests/Configuration/ServiceRegistrationSnapshotTests.cs (yeni satır)
├── AgentPrism.Core.UnitTests/Architecture/public-surface-baseline.txt      (364→370, refresh)
└── AgentPrism.Core.UnitTests/Architecture/seam-contract-baseline.txt       (değişmedi — bkz. Testler)

docs/
├── openapi/agentprism.json                          (refresh)
├── manuel-test/13-KIRACI-VE-GUVENLIK.md              (MT-SEC-140..150)
├── manuel-test/00-INDEKS.md                          (satır 13 güncellendi)
├── KARARLAR.md                                        (K-670, K-671)
└── hafiza/http-uc-tuzaklari.md                        (yeni tuzak notu)
```

## Testler

| Sınıf | Dosya | Kanıtladığı |
|---|---|---|
| `RunAuthorizationEndpointTests` (18 test) | `tests/AgentPrism.AspNetCore.FunctionalTests/RunAuthorizationEndpointTests.cs` | Kayıtsız handler'da sıfır değişiklik · diagnostics 6. nokta · izin/red kullanıcı bazlı · session bazlı red · fail-closed `throw` · kota sırası · kuyruğa alınan run · session List/Read/Delete/Branch red şekilleri · dört yüzeyin dördü (agent/workflow/trigger/OpenAI) · ambient tenant · `AgentRunScope.UserId` tool'a ulaşır · eşzamanlı çağrılar |
| `RunAuthorizationResultTests` (4 test) | `tests/AgentPrism.Core.UnitTests/Runs/RunAuthorizationResultTests.cs` | `Allow()`/`Deny(reason)` fabrikaları, boş/whitespace `reason` `ArgumentException` |
| `RunAuthorizationCoverageTests` (2 test) | `tests/AgentPrism.Core.UnitTests/Architecture/RunAuthorizationCoverageTests.cs` | Bağımsız denetimin 🟡 bulgusuna karşılık eklendi: dört bilinen run-başlatan dosyanın kaynak metninde `RunAuthorizationGate.CheckRunAsync(` çağrısının hâlâ durduğunu — host açmadan — doğrular; regex çağrının gerçek iki satıra bölünmüş biçimini (K-642 sınıfı tuzak) tolere eder |
| `DiagnosticsCollectorTests`, `DiagnosticsEndpointTests` (güncellendi) | Core.UnitTests, AspNetCore.FunctionalTests | 6. genişleme noktasının varsayılan ve host-bağlı hâli |

Toplam: Core.UnitTests 2298→2304, AspNetCore.FunctionalTests 747→765. İkisi de
`dotnet test` ile tam koşuldu, 0 kırmızı.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent, 2026-09-03) çalışma ağacının
tamamını (kod + testler + `docs-site` + karar defteri) inceledi.

**🔴 bulgu:** Yok.

**🟡 bulgu (1, kapandı):** Riskler tablosunun vaat ettiği ikinci savunma
katmanı — "yeni bir run ucu eklenirse test onu görmez, bu yüzden testin
kendisi `QuotaGate` çağrı yerlerini `grep` ile sayan bir iddia taşır" —
kod yazılırken atlanmıştı; yalnız fonksiyonel `RunAuthorizationEndpointTests`
vardı. Kapatma: `RunAuthorizationCoverageTests` (Core.UnitTests/Architecture,
2 test) eklendi — dört bilinen run-başlatan dosyanın kaynak metnini
`RunAuthorizationGate.CheckRunAsync(` çağrısı için tarar. İlk yazımda
`Contains` düz dizge araması kullanıldı ve **kendi K-642 sınıfı tuzağına
düştü**: gerçek çağrı `RunAuthorizationGate` ile `.CheckRunAsync(` arasında
satır kırıyor (`if (await RunAuthorizationGate\n        .CheckRunAsync(...)`),
düz arama hiçbir zaman eşleşmiyordu — test dört dosyayı da "eksik" diye
işaretledi. Regex'e (`RunAuthorizationGate\s*\.\s*CheckRunAsync\s*\(`) geçilip
doğrulandı.

**Temiz çıkan başlıklar** (denetçinin raporundan): DoD kanıtları koda karşı
doğrulandı (3.1); test tiyatrosu yok, testler gerçek `IRunStore`/`IJobStore`/
`QuotaEnforcer` durumunu okuyor (3.2); doğru test seviyesi — DI/HTTP/kiracı/akış
sınırı gerçek `TestServer` üzerinden geçiyor (3.3); iptal/eşzamanlılık/boş
girdi/başka kiracı/alt sistem hatası beş sorusu kapsandı — `throw` fail-closed,
kiracı ambient okunuyor (3.4); imza-gövde takibi doğru — `attribution.UserId`/
`Labels` senkron gövdede okunup scope'a yazılıyor, `RunAttributionReader.Read`
üzerinden (3.5); yeni public API planla birebir, küçük sapmalar (yukarıdaki
"Plandan Sapmalar") gerekçeli (3.6); İngilizce, XML doküman tam, `secret` yok,
her `await`te `ConfigureAwait(false)` var (3.7); OpenAPI `403`/`404` eklendi,
`docs-site` dört sayfa güncel ve tutarlı, manuel test MT-SEC-140..150 eklendi (3.8).

## Sonraki Faza Devir Notu

Faz 140 ("İçerik Guard'ının Kaynağı") bu fazın dokunduğu boru hattına
(`ContentGuardPipeline`, model çağrı zinciri) dokunuyor ama **konu olarak
bağımsızdır** — bu fazın `IRunAuthorizationHandler`/`RunAuthorizationGate`'iyle
hiçbir çakışması yok, kendi "Bu Faza Başlarken" bölümü zaten tam.

Bu fazdan devreden, sonraki fazları etkileyebilecek tek gerçek bilgi:

- 🚨 **Yeni bir run başlatan HTTP yüzeyi eklenirse** (örn. bir MCP/A2A dışa
  açılan run tetikleyicisi, Faz 66'nın "gelen tetikleyiciler"inin bir
  benzeri), o yüzey `RunAuthorizationGate.CheckRunAsync(...)`'ı **kendi
  gövdesinde**, atıf kontrolünden sonra ve kota kontrolünden önce çağırmalı
  VE `RunAuthorizationCoverageTests.ExpectedRunStartingFiles`'a eklenmelidir
  — aksi hâlde kapı sessizce bypass edilir ve hiçbir test bunu yakalamaz.
- `IRunAuthorizationHandler` ve `IToolAuthorizationHandler` artık simetrik
  iki kardeş sözleşmedir (ikisi de fail-closed, ikisi de `TryAdd` ile
  kaydedilir, ikisi de tek metotlu değil — biri iki metotlu). Yeni bir
  yetkilendirme sınıfı gerekirse önce bu ikisinin desenine bakılmalı.
- Voice oturumları (`VoiceEndpoints`) bu fazın KAPSAMI DIŞINDA bırakıldı
  (Açık Soru 3, seçenek B) — ayrı bir depo ve ömür taşıdıkları için. Ses
  oturumu erişimine bir gün sahiplik eklenmek istenirse bu, ayrı bir faz
  olarak K-283'ün ölçtüğü vakayla aynı sınıfa dikkatle yaklaşmalıdır.
