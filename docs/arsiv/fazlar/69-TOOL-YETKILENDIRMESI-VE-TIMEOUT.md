# Faz 69 — Tool Yetkilendirmesi ve Yürütme Timeout'u

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-113**, **F-114**
> **Önkoşul:** [Faz 6](06-GOZLEMLENEBILIRLIK.md) — tool onayı ve `ApprovalRequiredAIFunction` sarmalaması · [Faz 9](09-YONETISIM-VE-DENETIM-IZI.md) — rol politikaları ve denetim izi
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Generators`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (`tool_invocations` tablosuna yetki kararı ve timeout alanı). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor** — `ToolDescriptor` ve tool attribute'u alan alır, iki yeni arayüz gelir. `PublicAPI.Shipped.txt` bugün **boş** — şimdi bedava
> **Site etkisi:** `concepts/tools.md`, `concepts/governance.md`, `getting-started/tools.md`, `getting-started/security.md`
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-218\|K-368\|K-367\|K-178" docs/KARARLAR.md
   ```
   **K-218** (🚨 tool'un gördüğü servis sağlayıcı **boştur**), **K-368**
   (onay kararından sonra **yeni** bir `run` açılır), **K-367** (onay yüzeyi
   denetimi istek bazlı filtreye taşındı), **K-178** (migration numaraları).
3. [`06-GOZLEMLENEBILIRLIK.md`](06-GOZLEMLENEBILIRLIK.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/06-GOZLEMLENEBILIRLIK.md
   ```
   Onay sarmalamasının hangi katmanda durduğunu devralıyorsun.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/maf-api.md`](../../hafiza/maf-api.md) (`AIFunction`, `DelegatingAIFunction`) ·
   [`hafiza/cekirdek-calistirma.md`](../../hafiza/cekirdek-calistirma.md) (tool çağrı turu, K-218)

---

## Amaç

AgentPrism bugün bir tool'un çağrılmasına **insan** kapısı koyabiliyor (onay),
ama **izin** kapısı koyamıyor. "Bu kullanıcı bu tool'u hiç çağırabilir mi"
sorusu sözleşmede yoktur. Aynı şekilde bir tool'un yürütmesi süresizdir. Bu faz
ikisini birlikte kapatır: ikisi de aynı `ToolDescriptor` kaydına yazılır ve aynı
sarmalama noktasında uygulanır.

- **F-113** — tool başına gerekli izin adı, etki sınıfı ve değiştirilebilir bir
  yetkilendirme kancası.
- **F-114** — tool başına yürütme timeout'u.

🚨 **Onay ile izin farklı şeylerdir.** Onay "bu sefer olur mu" der ve insana
sorar. İzin "bu kullanıcı bunu hiç yapabilir mi" der ve politikaya sorar. İkisi
birbirinin yerine geçmez; bu faz ikincisini ekler, birincisini korur.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`ToolDescriptor.cs:7-50`](../../../src/AgentPrism.Abstractions/Tools/ToolDescriptor.cs) | Altı alan: `Name`, `Description`, `JsonSchema`, `RequiresApproval`, `Source`, `RunsOnClient`. İzin, etki ve timeout **yok** |
| `grep -rn "IToolAuthoriz\|ToolAuthorization" src --include="*.cs" \| wc -l` → **0** | Yetkilendirme kavramı kod tabanında yok |
| [`AgentPrismToolRegistration.cs:45-51`](../../../src/AgentPrism.Abstractions/Tools/AgentPrismToolRegistration.cs) | Kayıt üç şey taşır: `Function`, `RequiresApproval`, `Source` |
| [`AgentPrismOptions.cs:71`](../../../src/AgentPrism.Core/AgentPrismOptions.cs) | `McpTimeout` — yalnız MCP bağlantısı |
| [`AgentPrismOptions.cs:210`](../../../src/AgentPrism.Core/AgentPrismOptions.cs) | Skill script timeout'u — yalnız script yolu |
| [`ToolRegistry.cs:35-52`](../../../src/AgentPrism.Core/Tools/ToolRegistry.cs) | Sarmalama **registry'de** yapılır ve kod yorumu bunu açıkça gerekçelendirir: *"the only place that enforces the rule"* |

> Kanıtlar 2026-08-18 tarihinde doğrulandı.

**Ekosistem (2026-08-18):** LiteLLM tool başına izin/ret kuralı (*tool
permission guardrail*) ve MCP tool'ları için anahtar/takım/organizasyon bazlı
izin yönetimi sunuyor; Portkey MCP Gateway takım seviyesinde tool izni veriyor.
.NET tarafında karşılığı yok.

---

## 69.1 — Sarmalama noktası zaten doğru yerde

`ToolRegistry` onay sarmalamasını neden kendi içinde yaptığını kodda
gerekçelendirmiş: *"an agent can only refer to a registered tool" kuralını
zorlayan tek yer burasıdır; onayı burada uygulamak başka bir kod yolunun bunu
atlamasını engeller.*

Aynı gerekçe izin ve timeout için de geçerlidir. Üç sarmalayıcı zincir olur:

```mermaid
flowchart LR
    R["ToolRegistry"] --> A["AuthorizingAIFunction<br/>izin"]
    A --> T["TimeoutAIFunction<br/>sure"]
    T --> P["ApprovalRequiredAIFunction<br/>onay"]
    P --> F["Gercek AIFunction"]
```

**Sıra rastgele değildir ve plan bunu sabitler:**

| Sıra | Katman | Neden burada |
|---|---|---|
| 1 | **İzin** | En dışta. İzin yoksa onay istemenin anlamı yok — kullanıcıya yapamayacağı bir işi onaylatmak yanlış |
| 2 | **Timeout** | Onayın **dışında**. İnsanın onay bekleme süresi tool'un yürütme süresi değildir |
| 3 | **Onay** | Var olan davranış korunur |

🚨 Timeout'un onayın dışında durması bu fazın en kolay kaçırılacak kararıdır.
Ters sırada, bir gece bekleyen onay isteği timeout'a düşer.

### 🚨 K-218 — kanca bağımlılığını kurulum anında al

MAF, `AIFunctionArguments.Services` olarak **`EmptyServiceProvider`** geçirir.
Yani sarmalayıcı, yetkilendirme kancasını çağrı anında DI'dan **çözemez**.
Bağımlılık `ToolRegistry` kurulurken kurucuya verilir. Bu tuzak bu repoda
K-218 olarak kayıtlıdır ve izole bir konsol probunda **çalışıyor görünmüştü** —
entegre davranışı yalnız gerçek bir `run` kanıtlar.

---

## 69.2 — Etki sınıfı ve izin adı

```csharp
public enum ToolEffect { Read, Write, Destructive, External }
```

| Değer | Anlamı | Arayüzde |
|---|---|---|
| `Read` | Yan etkisi yok | Nötr |
| `Write` | Kalıcı veri değiştirir | Sarı |
| `Destructive` | Geri alınamaz | 🔴 Kırmızı |
| `External` | Veri süreç dışına çıkar | Turuncu |

Etki sınıfı **bilgidir, kapı değildir** — kapıyı izin ve onay kurar. Değeri
üçtür: arayüzde okunur, denetim kaydına yazılır, ve
[Faz 63](63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md)'ün kalıcı onay kuralları için
doğal bir kapsam anahtarı olur.

`RequiredPermission` **opak bir dizedir**. AgentPrism onu çözmez, yorumlamaz,
saklamaz — yalnız kancaya verir.

### Kanca sözleşmesi

```csharp
public interface IToolAuthorizationHandler
{
    ValueTask<ToolAuthorizationResult> AuthorizeAsync(
        ToolAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
```

**K1 gereği varsayılan uygulama her şeye izin verir.** `AddAgentPrism()` tek
başına bugünkü gibi çalışır. **K4 gereği `TryAdd` ile kaydedilir**; tüketici
kendi uygulamasını önce kaydederse onunki kazanır.

### Ret modele nasıl bildirilir

🚨 Bu, fazın ikinci kritik kararıdır. Bir tool reddedildiğinde `run` **düşmez**.
Model tool sonucu olarak **açık bir ret metni** alır ve turuna devam eder.

Gerekçe: yetkisiz bir çağrı bir hata değil, bir **sınırdır**. `run`'ı
düşürmek, modelin izinli bir alternatifi denemesini engeller ve kullanıcıya
altyapı hatası gibi görünür. Ret metni İngilizcedir ve modele veri olarak gider;
K-232 kapsamındadır.

### İzin, tool'un modele gösterilmesini de etkiler mi?

**Hayır — bu fazda değil.** Şema derleme anında oluşur, izin ise çalışma anında
kullanıcıya bağlıdır; ikisini birleştirmek derlenmiş agent önbelleğini
(`CompiledAgentCache`) kullanıcı başına ayırmayı gerektirir. Bu ölçülmemiş bir
maliyettir ve **kapsam dışıdır**. Açık Soru 3'e yazıldı.

---

## 69.3 — Timeout

Her tool kaydı bir timeout taşır. Varsayılan `AgentPrismOptions` üzerinden
gelir; tool kaydı kendi değerini verebilir.

**Üç kural:**

1. **Timeout iptalden ayrı bir hata sınıfıdır.** `RunErrorClass`'a ayrı bir
   değer girer, yoksa `DefaultRunErrorClassifier` kullanıcı iptaliyle karıştırır.
2. **Timeout circuit breaker sayacına girmez.** İptal ve guard blokları gibi
   davranır — sağlayıcı sağlığının göstergesi değildir.
3. **Timeout `run`'ı düşürmez.** İzin reddi gibi, modele bir tool hatası olarak
   döner ve tur devam eder.

Süre aşımı `ToolFailed` olayı üretir ve `tool_invocations` kaydına yazılır.

🚨 **İptal etmek ile durdurmak aynı şey değildir.** `CancellationToken`
işbirlikçidir; token'ı hiç okumayan bir tool gövdesi çalışmaya devam eder.
Timeout o gövdeyi **öldüremez**. Plan bunu vaat etmez: timeout `run`'ın
beklemesini keser, tool sürecini değil. Doküman bunu açıkça yazmalıdır, yoksa
tüketici yanlış bir güvence varsayar.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public enum ToolEffect { Read = 0, Write = 1, Destructive = 2, External = 3 }

public sealed record ToolDescriptor
{
    public ToolEffect Effect { get; init; }          // varsayılan Read
    public string? RequiredPermission { get; init; }
    public TimeSpan? Timeout { get; init; }
}

public sealed record ToolAuthorizationRequest
{
    public required string ToolName { get; init; }
    public required ToolEffect Effect { get; init; }
    public string? RequiredPermission { get; init; }
    public required string TenantId { get; init; }
    public string? UserId { get; init; }             // Faz 68 varsa dolar
    public required Guid RunId { get; init; }
    public required string AgentName { get; init; }
}

public sealed record ToolAuthorizationResult
{
    public required bool IsAllowed { get; init; }
    /// <summary>Shown to the model as the tool result. English (K-232).</summary>
    public string? Reason { get; init; }

    public static ToolAuthorizationResult Allow();
    public static ToolAuthorizationResult Deny(string reason);
}

public interface IToolAuthorizationHandler
{
    ValueTask<ToolAuthorizationResult> AuthorizeAsync(
        ToolAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
```

Tool attribute'u (`AgentPrismToolAttribute`) aynı üç alanı alır ve
`AgentPrism.Generators` üretilen kayda taşır.

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/tools` | Reader | Yanıta `effect`, `requiredPermission`, `timeout` eklenir |

Yeni uç yok.

### Arayüz payı

Tool kataloğuna etki rozeti ve izin sütunu. Yeni bağımlılık yok. Bugünkü bundle
146.104 B brotli; fazın payı kapanışta ölçülür. Yeni metin `en.ts` **ve**
`tr.ts` içine girer (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Tools/
├── ToolDescriptor.cs                   (değişir)
├── AgentPrismToolAttribute.cs          (değişir)
├── AgentPrismToolRegistration.cs       (değişir)
├── ToolEffect.cs                       (YENİ)
├── IToolAuthorizationHandler.cs        (YENİ)
└── ToolAuthorizationTypes.cs           (YENİ)

src/AgentPrism.Core/Tools/
├── ToolRegistry.cs                     (değişir — üç katmanlı sarmalama)
├── AuthorizingAIFunction.cs            (YENİ — DelegatingAIFunction)
├── TimeoutAIFunction.cs                (YENİ — DelegatingAIFunction)
├── AllowAllToolAuthorizationHandler.cs (YENİ — varsayılan)
└── ToolMethodScanner.cs                (değişir)

src/AgentPrism.Generators/             (değişir — üç yeni alan)
src/AgentPrism.Abstractions/Runs/RunErrorClass.cs (değişir — ToolTimeout)
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Migrations/NNNN_tool_governance.sql (YENİ ×3)
src/AgentPrism.AspNetCore/             (sözleşme alanları)
src/AgentPrism.UI/                     (etki rozeti + locales)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Kanca `EmptyServiceProvider`'dan çözülmeye çalışılır (K-218) | Fonksiyonel (gerçek `run`) | `ToolAuthorizationWiringTests` |
| Sarmalama sırası bozulur → onay beklerken timeout | Fonksiyonel | `ToolWrapperOrderTests` |
| Reddedilen tool `run`'ı düşürür | Fonksiyonel | `ToolDenialContinuesRunTests` |
| Ret metni modele hiç ulaşmaz | Fonksiyonel | aynı sınıf |
| Timeout iptalle aynı hata sınıfına düşer | Birim | `RunErrorClassifierTests` |
| Timeout circuit breaker'ı açar | Birim | `CircuitBreakerCountingTests` |
| Token okumayan tool timeout'ta asılı kalır | Fonksiyonel | `ToolTimeoutNonCooperativeTests` — **belgelenmiş sınır** |
| İstemci tarafı tool onay/izin sarmalamasını kırar | Fonksiyonel | `ClientToolGovernanceTests` |
| MCP kaynaklı tool'da etki sınıfı tanımsız kalır | Birim | `McpToolDescriptorTests` — varsayılan `External` |
| Kaynak üreteci yeni alanları taşımaz | Birim (üreteç anlık görüntüsü) | `GeneratorSnapshotTests` |
| Başka kiracının izin kararı sızar | Sözleşme | `TenantIsolationContract` |
| Kanca hata fırlatırsa `run` düşer | Fonksiyonel | `ToolAuthorizationFailureTests` — **ret sayılır**, açık davranış |

**Beş soru:** iptal — timeout ile kullanıcı iptali ayrı sınıflar · eşzamanlılık —
aynı tool'un paralel çağrıları · boş/aşırı girdi — `RequiredPermission` boş
dizeyse **kayıt hatası** · başka kiracı — sözleşme testi · alt sistem hatası —
kanca patlarsa ret sayılır ve loglanır.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Kanca kayıtlı değil | Herhangi bir tool'u çağıran `run` | Bugünkü davranış birebir; hiçbir şey değişmez |
| 2 | Her şeyi reddeden bir kanca | Aynı `run` | Model ret metnini alır, tura devam eder, `run` **başarılı** biter |
| 3 | Aynı ortam | `GET /api/runs/{id}` olay akışı | `ToolFailed` yerine yetki reddi ayırt edilebilir |
| 4 | 1 sn timeout'lu, 5 sn uyuyan tool | `run` | ~1 sn'de tool hatası; `run` devam eder; hata sınıfı `ToolTimeout` |
| 5 | Aynı tool, arka arkaya 6 kez | `run`'lar | Circuit breaker **açılmaz** |
| 6 | `Destructive` + `RequiresApproval` tool | `run` → onay iste → 2 dk bekle → onayla | Onay süresi timeout'a **düşmez**; onaydan sonra tool çalışır |
| 7 | Arayüz | Tool kataloğunu aç | 👤 `Destructive` kırmızı, `External` turuncu; izin adı görünür |
| 8 | Token okumayan tool + 1 sn timeout | `run` | `run` 1 sn'de devam eder; tool gövdesinin arkada bittiği loglanır. **Belgelenmiş sınır** |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | [Faz 63](63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md) önce mi gelmeli? | A: 63 önce · B: 69 önce | **B** — 69 `ToolDescriptor`'a alan ekler, 63 `ToolApprovalRule`'a. 69 önce giderse 63 etki sınıfını kapsam anahtarı olarak kullanabilir; tersi durumda 63 bunu sonradan eklemek zorunda kalır |
| 2 | Kanca hata fırlatırsa? | A: ret sayılır · B: `run` düşer | **A** — açık kapı (fail-open) bir güvenlik kapısında kabul edilemez. Kapalı kapı (fail-closed) doğrudur ve loglanır |
| 3 | İzin, tool'un modele gösterilmesini de kısıtlasın mı? | A: hayır, yalnız yürütme · B: evet, şemadan da düşsün | **A** bu fazda — B, `CompiledAgentCache`'i kullanıcı başına ayırmayı gerektirir; maliyeti ölçülmedi. Ayrı bir aday kalemi olabilir |
| 4 | MCP kaynaklı tool'un varsayılan etkisi? | A: `External` · B: `Unknown` | **A** — tanım uzak bir sunucudadır ve değişebilir; en temkinli sınıf doğrudur |
| 5 | Timeout varsayılanı kaç? | A: 30 sn (skill script ile aynı) · B: sınırsız | **A** — B bugünkü davranıştır ve fazın amacını boşa çıkarır. Değer **ölçülmedi**; ilk koşumdan sonra gözden geçirilmeli |

---

## Bitiş Ölçütleri (DoD)

- [x] Kanca kayıtlı değilken hiçbir davranış değişmez (test kanıtıyla) —
      `AllowAllToolAuthorizationHandler` `TryAddSingleton`; 565/565 mevcut
      `AspNetCore.FunctionalTests` değişmeden geçti
- [x] Reddedilen tool çağrısı `run`'ı düşürmez; ret metni modele ulaşır —
      `ToolGovernanceEndpointTests.Denied_call_completes_the_run_and_marks_the_record_denied`,
      gerçek `FunctionInvokingChatClient` boru hattı üzerinden
- [x] Kanca `EmptyServiceProvider` üzerinden çözülmeye **çalışılmaz** —
      gerçek bir `run` ile kanıtlandı (K-218): üç yeni `ToolGovernanceEndpointTests`
      testi gerçek `FakeModelProvider` + `UseFunctionInvocation()` boru hattından
      geçiyor; ilk taslak yalnız birim testiyle "kanıtlanmıştı" — bağımsız
      denetim bunu 🔴 bulgu olarak işaretledi, gerçek `run` testleriyle kapatıldı
- [x] 1 sn timeout'lu bir tool ~1 sn'de kesilir; hata sınıfı `ToolTimeout` —
      `TimeoutAIFunctionTests` (gerçek `Stopwatch`) + `ToolGovernanceEndpointTests.Timed_out_call_...`
      (gerçek `run`, 300 ms timeout, 30 sn'lik gövde, `run` 10 sn altında tamamlanıyor)
- [x] Timeout circuit breaker sayacını **artırmaz** —
      `ToolGovernanceEndpointTests.Repeated_tool_timeouts_never_open_the_providers_circuit_breaker`
      (6 ardışık zaman aşımı, eşik 5, devre açılmıyor)
- [x] Onay bekleme süresi timeout'a düşmez (Case 6) —
      `ToolGovernanceEndpointTests.Approval_wait_is_not_bounded_by_the_tools_own_timeout`
      (200 ms timeout'lu tool, 1 sn bekleme, sonra onay — `run` `Completed`)
- [x] `GET /api/tools` yeni üç alanı döner — `docs/openapi/agentprism.json`
      yeniden üretildi (`effect`, `requiredPermission`, `timeout`, `ToolEffect` şeması)
- [x] Kaynak üreteci üç yeni alanı taşır —
      `GeneratedOutputTests.Effect_permission_and_timeout_carry_into_the_generated_registration`,
      `Undeclared_effect_permission_and_timeout_generate_defaults`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `build`/`test`/`pack`/`format`
      hepsi temiz (arayüz dahil)
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı —
      **yapılamadı**: bu oturumun sandbox'ında model sağlayıcı API anahtarı/ağ
      erişimi yok. Yerine geçen kanıt: yukarıdaki `ToolGovernanceEndpointTests`
      GERÇEK bir `FunctionInvokingChatClient` boru hattından (`FakeModelProvider`,
      `ModelProviderRegistry`'nin her ham istemciyi `UseFunctionInvocation()` ile
      sardığı NOKTADAN) geçiyor — K-218'in "izole prob yeterli değil" dersini
      karşılıyor, ancak gerçek bir OpenAI/Anthropic/vb. çağrısı DEĞİL. `demo`
      tool `get_slow_report` (`samples/AgentPrism.Api/OrderTools.cs`) bir sonraki
      oturumda gerçek anahtarla koşulmaya hazır
- [x] `secret` taraması boş döndü — yeni eklenen hiçbir dosyada `secret` yok
- [x] Manuel kabul case'leri
      [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)
      içine eklendi (MT-SEC-120..127). Her case'in senaryosu — ret/timeout/devre
      kesici/onay+timeout — `ToolGovernanceEndpointTests`'te gerçek bir `run`
      üzerinden AYRICA otomatik test edildi; ancak case'lerin kendisi
      `samples/AgentPrism.Api`'ye karşı gerçek model anahtarıyla curl ile
      **koşulmadı** (sandbox kısıtı) — bu iki doğrulama birbirinin YERİNE
      geçmez, ikinci koşum sonraki oturuma devredildi
- [x] `faz-denetim` koşuldu; taze bağlamlı bağımsız denetçi 2 🔴 + 3 🟡 bulgu
      buldu — ikisi de 🔴 (gerçek-`run` kanıtı eksikliği, `RunErrorClass.ToolTimeout`
      arayüz sözleşmesinden eksik) kapatıldı, 🟡'lardan ikisi (üreteç snapshot testi,
      devre kesici regresyon testi) test eklenerek kapatıldı, biri (attribute
      `TimeoutSeconds` tip farkı) aşağıda dokümante edildi — **🔴 bulgu kalmadı**
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz —
      `concepts/tools.md` (yeni "Authorization and timeout" bölümü),
      `concepts/governance.md` (yeni "Tool authorization" alt bölümü),
      `getting-started/security.md` (checklist satırı), `ui.md` (rozet notu).
      **Ekran görüntüsü güncellenmedi** — `AGENTPRISM_UI_SCREENSHOTS=1` E2E
      koşumu bu oturumda yapılmadı, sonraki oturuma devredildi
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — bkz. aşağıda

### Doğrulama komutları

```bash
# Yeni alanlar sözleşmede mi
curl -s http://localhost:5081/agentprism/api/tools | jq '.[0]'

# Timeout gerçekten kesiyor mu — sure olculur
time curl -s -X POST http://localhost:5081/agentprism/api/agents/slow/run \
  -H 'content-type: application/json' -d '{"message":"call the slow tool"}'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 K-218 tuzağı tekrar eder — izole prob çalışır, gerçek `run` çalışmaz | Kanca kurulum anında kurucuya verilir; DoD gerçek `run` kanıtı ister |
| Sarmalama sırası ters kurulur → onay timeout'a düşer | `ToolWrapperOrderTests` ve Manuel Case 6 |
| Timeout "tool'u durdurur" sanılır | Doküman ve XML yorumu işbirlikçi iptali açıkça yazar; Case 8 sınırı belgeler |
| Faz 63 ile aynı yere iki kez dokunulur | Açık Soru 1 karara bağlanır; 69 önce gitmelidir |
| Ret metni kullanıcıya İngilizce görünür | Metin **modele** gider, kullanıcıya değil. Arayüz kendi metnini gösterir (K-232) |
| `ToolDescriptor` Faz 7'den sonra elden geçer | İki kalem tek fazda; ikinci bir alan ekleme turu olmaz |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **MCP tool'ları için sarmalama `ToolRegistry`'de değil, `McpTenantTools.Create`'de
   TEKRARLANDI.** Plan yalnız `ToolRegistry`'yi ("sarmalama noktası zaten doğru
   yerde", 69.1) konu alıyordu; kod okunduğunda MCP tool'larının `ToolRegistry`'den
   HİÇ geçmediği, kendi ayrı onay-sarmalama kopyasına sahip olduğu görüldü (Faz 6'dan
   beri var olan, kod yorumuyla gerekçelendirilmiş bir desen). Aynı üç katman
   (Authorizing → Timeout → ApprovalRequired) `McpTenantTools.cs`'e de elle taşındı;
   `AuthorizingAIFunction`/`TimeoutAIFunction` bu yüzden `internal` değil **public**
   (paketler arası `InternalsVisibleTo` yerine mevcut kopyalama deseni izlendi — K-487).
   Bu, planın "hata modları" tablosundaki `McpToolDescriptorTests` satırının neden
   var olduğunu da açıklıyor: MCP tool'unun effect'i planın öngördüğünden daha
   fazla kod gerektirdi.

2. **`ApprovalRequiredAIFunction`'ı ek katmanlarla sarmak MAF'ın onay kısa devresini
   KIRABİLİRDİ — ölçülerek kontrol edildi (K-490).** Plan bu riski hiç tartışmıyordu;
   `ApprovalRequiredAIFunction.InvokeCoreAsync`'in doğrudan çağrıldığında defer
   ETMEDİĞİ (gerçek gövdeyi çalıştırdığı) bir birim testiyle ölçüldü. Kısa devrenin
   `AITool.GetService<T>()` pipeline'ı üzerinden çalıştığı bulundu; `AuthorizingAIFunction`/
   `TimeoutAIFunction` bu yüzden `GetService`'i EZMİYOR (miras alınan `DelegatingAIFunction`
   davranışına bilerek güveniliyor) — bu satır kodda YOKSA sarmalama sessizce onayı
   bozardı. `ToolRegistryWrapperOrderTests.The_approval_wrapper_stays_discoverable_through_the_outer_layers`
   bunu korur.

3. **`AgentPrismToolAttribute.TimeoutSeconds` `int`, planın taslak `ToolDescriptor.Timeout`
   (`TimeSpan?`) imzasından farklı tip taşıyor.** Bir attribute parametresi
   derleme-zamanı sabiti olmalı ve `TimeSpan` olamaz; `TimeoutSeconds` (0 = "belirtilmedi,
   varsayılanı kullan") seçildi, `ToolRegistry`/`ToolMethodScanner` bunu `TimeSpan.FromSeconds(n)`'e
   çeviriyor. Bağımsız denetimin 🟡 bulgusu — kod doğru, plan bu ayrımı yazmamıştı.

4. **Gerçek `samples/AgentPrism.Api` çalıştırması ve `docs-site` ekran görüntüsü
   yapılamadı.** Bu oturumun sandbox'ında model sağlayıcı API anahtarı ve dış ağ
   erişimi yok. Yerine: `ToolGovernanceEndpointTests` (gerçek `FunctionInvokingChatClient`
   boru hattı, `FakeModelProvider` ile) ve üç SQL sağlayıcısının gerçek veritabanlarına
   (Docker Postgres/SQL Server, gerçek SQLite dosyası) karşı koşan bütünleşme testleri
   kanıt taşıyor. Sonraki oturumun `samples/AgentPrism.Api`'yi gerçek bir anahtarla
   çalıştırıp DoD'nin ilgili satırını ve `docs-site` ekran görüntüsünü
   (`AGENTPRISM_UI_SCREENSHOTS=1`) tamamlaması gerekiyor.

5. **Alan hafızası dosyaları (`docs/hafiza/maf-api.md`, `cekirdek-calistirma.md`)
   bütçeyi aştı; ikiye bölme yerine yalnız TRIM edildi.** `scripts/dokuman-bakim.py`
   ikisini de "AŞTI — ikiye böl" diye işaretledi; bu fazın yeni notları (K-487/K-488/K-490)
   en özet haline indirilerek her ikisi de bütçenin altına çekildi (16150/16000,
   15950/16000). Gerçek bir bölme (konuya göre ikinci bir dosya) yapılmadı — dosyaların
   ÖNCEDEN de bütçeye yakın olduğu ölçüldü, bu yüzden köklü bir bölme ayrı bir kapsam
   kararı gerektirir. Sonraki fazın bu iki dosyaya dokunması gerekirse önce bölünmeli.

## Bu Fazda Verilen Kararlar

K-487, K-488, K-489, K-490, K-491 — bkz. [`docs/KARARLAR.md`](../../KARARLAR.md), bölüm 2
(K-486'nın hemen altı). Özet:

- **K-487** — sarmalama sırası (Authorizing → Timeout → ApprovalRequired → gerçek
  fonksiyon) ve MCP yolunda aynı mantığın tekrarlanması.
- **K-488** — yetki reddi istisna fırlatmaz, normal sonuç döner; kanca hatası fail-closed.
- **K-489** — `RunErrorClass.ToolTimeout` (13), `tool_timeout` stabil kimliği.
- **K-490** — `ApprovalRequiredAIFunction`'ın kısa devresi `GetService<T>()` üzerinden
  çalışır (ölçüldü); yeni sarmalayıcılar bu metodu ezmemeli.
- **K-491** — varsayılan tool timeout'u 30 sn, ölçülmedi, ilk gerçek koşumdan sonra
  gözden geçirilecek (Açık Soru 5'in kararı).

## Gerçekleşen Public API

Taslaktan sapan veya ekleyen kısımlar aşağıda; taslakla birebir aynı kalan üyeler
(`ToolAuthorizationRequest`/`Result`, `IToolAuthorizationHandler`) tekrar yazılmadı.

```csharp
// AgentPrism.Abstractions

public enum ToolEffect { Read = 0, Write = 1, Destructive = 2, External = 3 }
// [JsonConverter(typeof(JsonStringEnumConverter<ToolEffect>))] — planda yoktu,
// diğer arayüz-yüzü enum'larla (RunEventType, RunErrorClass) tutarlılık için eklendi.

public enum RunErrorClass { /* ...mevcut 13 değer..., */ ToolTimeout = 13 }

public sealed class AgentPrismToolTimeoutException : AgentPrismException
{
    public const string ToolTimeoutErrorType = "tool_timeout";
    public string? ToolName { get; init; }
    public TimeSpan? Timeout { get; init; }
    public override string ErrorType => ToolTimeoutErrorType;
}

public sealed record ToolDescriptor
{
    // ...mevcut alanlar...
    public ToolEffect Effect { get; init; }
    public string? RequiredPermission { get; init; }
    public TimeSpan? Timeout { get; init; }
}

public sealed class AgentPrismToolRegistration
{
    public AgentPrismToolRegistration(
        AIFunctionDeclaration function,
        bool requiresApproval = false,
        string? source = null,
        ToolEffect effect = ToolEffect.Read,
        string? requiredPermission = null,
        TimeSpan? timeout = null);
    // Effect/RequiredPermission/Timeout salt-okunur property olarak da eklendi.
}

public sealed class AgentPrismToolAttribute : Attribute
{
    // ...mevcut alanlar...
    public ToolEffect Effect { get; init; }
    public string? RequiredPermission { get; init; }
    public int TimeoutSeconds { get; init; }  // planın TimeSpan? taslağından SAPMA — bkz. Sapma #3
}

public sealed record ToolInvocationRecord
{
    // ...mevcut alanlar...
    public bool AuthorizationDenied { get; init; }
    public bool TimedOut { get; init; }
}

// AgentPrism.Core — planda YOKTU, sarmalama sırası tartışmasından doğdu (public,
// K-487'nin MCP-tekrarı gerekçesiyle; bkz. Sapma #1)

public sealed class AuthorizingAIFunction : DelegatingAIFunction
{
    public AuthorizingAIFunction(
        AIFunction innerFunction,
        IToolAuthorizationHandler handler,
        ToolEffect effect,
        string? requiredPermission,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> logger);
}

public sealed class TimeoutAIFunction : DelegatingAIFunction
{
    public TimeoutAIFunction(AIFunction innerFunction, TimeSpan timeout, ILogger<TimeoutAIFunction> logger);
}

public sealed class AllowAllToolAuthorizationHandler : IToolAuthorizationHandler { }

public sealed class ToolRegistry : IToolRegistry
{
    // İmza büyüdü — planda tek parametreliydi (registrations).
    public ToolRegistry(
        IEnumerable<AgentPrismToolRegistration> registrations,
        IToolAuthorizationHandler authorizationHandler,
        IOptionsMonitor<AgentPrismOptions> optionsMonitor,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> authorizingLogger,
        ILogger<TimeoutAIFunction> timeoutLogger);
}

public sealed class AgentPrismToolOptions  // planda yoktu, AgentPrismOptions.Tools
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

`GET /api/tools` HTTP sözleşmesi taslakla birebir aynı — yeni uç yok, `ToolDescriptor`'ın
üç yeni alanı otomatik yansıyor (`docs/openapi/agentprism.json`).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
├── Tools/ToolDescriptor.cs                    (değişti)
├── Tools/AgentPrismToolAttribute.cs           (değişti)
├── Tools/AgentPrismToolRegistration.cs        (değişti)
├── Tools/ToolEffect.cs                        (YENİ)
├── Tools/ToolAuthorizationTypes.cs            (YENİ — IToolAuthorizationHandler + 2 record)
├── AgentPrismException.cs                     (değişti — AgentPrismToolTimeoutException)
└── Runs/RunErrorClass.cs, ToolInvocationRecord.cs (değişti)

src/AgentPrism.Core/
├── Tools/ToolRegistry.cs                      (değişti — üç katmanlı sarmalama)
├── Tools/ToolMethodScanner.cs                 (değişti)
├── Tools/AuthorizingAIFunction.cs             (YENİ)
├── Tools/TimeoutAIFunction.cs                 (YENİ)
├── Tools/AllowAllToolAuthorizationHandler.cs  (YENİ)
├── Recording/ToolAuthorizationAccumulator.cs  (YENİ)
├── Recording/AgentPrismRunContext.cs, RunRecordingAgent.cs, ToolInvocationTracker.cs (değişti)
├── Runs/DefaultRunErrorClassifier.cs          (değişti)
├── AgentPrismOptions.cs, AgentPrismOptionsValidator.cs, AgentPrismServiceCollectionExtensions.cs (değişti)

src/AgentPrism.Generators/
├── ToolCandidate.cs, SourceWriter.cs          (değişti)

src/AgentPrism.Mcp/                            (planda YOKTU — Sapma #1)
├── Internal/McpTenantTools.cs                 (değişti — aynı üç katman)
├── Internal/McpToolCatalog.cs                 (değişti — yeni bağımlılıklar)
├── AgentPrismMcpBuilderExtensions.cs           (değişti)

src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Migrations/
├── 0035_tool_governance.sql (PostgreSql) · 0022_tool_governance.sql (SqlServer, Sqlite)
src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs (değişti — yazma + fixed-index okuma)
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Internal/*Queries.cs (değişti)

src/AgentPrism.UI/frontend/src/
├── screens/tools.tsx, lib/types.ts, locales/{en,tr}.ts (değişti)

samples/AgentPrism.Api/OrderTools.cs           (değişti — get_slow_report demo tool)

tests/ — 10 yeni dosya (AuthorizingAIFunctionTests, TimeoutAIFunctionTests,
ToolRegistryWrapperOrderTests, ToolAuthorizationAccumulatorTests,
ToolInvocationTrackerGovernanceTests, McpTenantToolsTests,
ToolGovernanceEndpointTests — dördü Core/Mcp birim, biri AspNetCore fonksiyonel),
6 değişen dosya (mevcut `ToolRegistry`/`McpToolCatalog` kurucu çağrıları, DoD'nin
`RunErrorClassifierTests`/`GeneratedOutputTests` genişlemeleri).
```

## Denetim Bulguları

`faz-denetim` (taze bağlamlı bağımsız denetçi, ayrı bir worktree'de) iki 🔴 ve üç 🟡
bulgu üretti:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | DoD'nin "gerçek `run`" gerektiren maddeleri yalnız `AuthorizingAIFunction`/`TimeoutAIFunction`'ı doğrudan kurup `InvokeAsync` çağıran birim testleriyle "kanıtlanmıştı" — K-218'in tam olarak uyardığı hata | **Düzeltildi.** `ToolGovernanceEndpointTests.cs` (4 test) gerçek `FunctionInvokingChatClient` boru hattından (`FakeModelProvider` + `ModelProviderRegistry`'nin `UseFunctionInvocation()` sarması) geçen `run`'larla ret/timeout/devre-kesici/onay+timeout senaryolarını kanıtlıyor |
| 2 | 🔴 | `RunErrorClass.ToolTimeout` arayüz sözleşmesine (`types.ts`, `en.ts`, `tr.ts`) yansıtılmamıştı — üretimde gerçekleşince gösterge panelinde sessizce boş satır üretirdi | **Düzeltildi.** `RunErrorClass` union'ına `'ToolTimeout'` eklendi, iki dilde `dashboard.errorClass.ToolTimeout` çevirisi yazıldı |
| 3 | 🟡 | `GeneratorSnapshotTests` (Effect/RequiredPermission/Timeout üreteçten geçiyor mu) hiç yazılmamıştı | **Düzeltildi.** `GeneratedOutputTests`'e iki test eklendi |
| 4 | 🟡 | `CircuitBreakerCountingTests` (timeout devre kesiciyi artırmaz) hiç yazılmamıştı | **Düzeltildi.** `ToolGovernanceEndpointTests.Repeated_tool_timeouts_never_open_the_providers_circuit_breaker` |
| 5 | 🟡 | `AgentPrismToolAttribute.TimeoutSeconds` (`int`) planın taslak `TimeSpan?` imzasından farklı | **Dokümante edildi**, bkz. Plandan Sapmalar #3 — bulgu değil, gerekçeli bir tasarım zorunluluğu (attribute parametresi derleme-zamanı sabiti olmalı) |

Düzeltmelerden sonra dört doğrulama kapısı yeniden koşuldu (build/test/pack/format,
arayüz dahil) — hepsi temiz. `AgentPrism.SqlServer.IntegrationTests`'te tüm paket
takımı PARALEL koşulduğunda görülen bir `deadlock` (hata 1205, migration `0017`)
**bu fazla ilgisizdir** — Faz 63'ün KARARLAR.md'de zaten kayıtlı, tekrarlanan bir
test-altyapısı sıkışmasıdır (izole koşumda 557/557 temiz).

## Sonraki Faza Devir Notu

1. **`samples/AgentPrism.Api` gerçek anahtarla koşulmadı.** `get_slow_report` demo
   tool'u (1 sn timeout, 5 sn uyuyan, token almayan gövde) ve `cancel_order`'ın
   `Effect`/`RequiredPermission` alanları hazır — bir sonraki oturum gerçek bir
   OpenAI anahtarıyla `dotnet run` edip DoD'nin ilgili satırını gerçek çıktıyla
   doldurabilir.

2. **`docs-site` ekran görüntüsü güncellenmedi.** `screenshots/tools.png` hâlâ
   effect/permission/timeout rozetlerinden ÖNCEKİ hali gösteriyor.
   `AGENTPRISM_UI_SCREENSHOTS=1` ile E2E koşumu bunu üretir.

3. **`docs/hafiza/maf-api.md` ve `cekirdek-calistirma.md` bütçenin sınırında**
   (15950/16000, 15991/16000 — bkz. Plandan Sapmalar #5). Bu iki dosyaya dokunan
   bir sonraki faz muhtemelen bütçeyi tekrar aşacak; gerçek bir bölme (konuya göre
   ikinci dosya) o zaman ele alınmalı, tekrar tekrar TRIM edilmemeli.

4. **MCP tool sarmalaması ile kod-tanımlı tool sarmalaması artık İKİ AYRI kod
   yolu** (`ToolRegistry.cs`, `McpTenantTools.cs`) — aynı mantığı taşıyorlar ama
   fiziksel olarak ayrılar. Üçüncü bir tool kaynağı (örn. bir eklenti sistemi)
   eklenirse aynı üç katman ORAYA da elle taşınmalı; K-487'ye bak.

5. **`AgentPrismOptions.Tools.DefaultTimeout` (30 sn) ölçülmedi** (K-491). İlk
   gerçek üretim trafiğinden sonra gözden geçirilmesi öneriliyor.
