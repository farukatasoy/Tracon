# Faz 127 — Tool Kayıt Yüzeyi: Tek Kompozisyon, Argüman Kapısı ve Kapsamlı Tool

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-08-31-tuketici-raporu-faz-adaylari.md](kesif/2026-08-31-tuketici-raporu-faz-adaylari.md) · **T-4**, **T-3**
> **Önkoşul:** [Faz 69](arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) (sarmalayıcı zinciri ve sırası) ve [Faz 89](arsiv/fazlar/89-TOOL-CIKTISI-BOYUT-SINIRI.md) (`TruncatingAIFunction`) — ikisi de arşivde
> **Paketler:** `AgentPrism.Abstractions` (yeni arayüz), `AgentPrism.Core` (`Tools/`), `AgentPrism.Mcp` (`Internal/McpTenantTools.cs`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — bir arayüz (`IToolArgumentsValidator`), bir sonuç tipi (`ToolArgumentsValidationResult`), iki builder metodu (`AddScopedTool` aşırı yüklemeleri) ve **bir sarmalayıcı tip** (`ValidatingAIFunction`, `AuthorizingAIFunction`/`TimeoutAIFunction`/`TruncatingAIFunction` ile aynı public-wrapper deseninde — kapanışta eklendi, denetim bulgusu). Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/write-your-own-tool.md`, `concepts/tools.md`, `reference/extension-points.md` · sevk edilen: yeni arayüzün XML `<example>`'ı, `AgentPrism.AgentMap.md` yetenek satırı
> **Manuel test alanı:** `docs/manuel-test/18-MCP-VE-A2A.md` (MCP tarafı) ve `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` (argüman kapısı)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula.

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**:
   ```bash
   grep -n "K-218\|K-347\|K-368\|K-483" docs/KARARLAR.md
   ```
   **K-218** (tool bağımlılıkları kurulum anında alınır; `AIFunctionArguments.Services` boştur — bu fazın kapatacağı tuzak) · **K-347** (`ToolMethodScanner` örnek metotları tarama anında reddeder — 🚨 bu faz o kararı **açmaz**) · **K-368** (onay kararı YENİ bir run'dır) · **K-483** (elle tekrarlanan ifade sessiz kusur sınıfı üretir — bu fazın birinci işi)
3. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (K-218'in dört vakası) ·
   [`hafiza/mcp-a2a-sunucu.md`](hafiza/mcp-a2a-sunucu.md) (MCP tool kataloğu ve kiracı yalıtımı)
4. Gerektiğinde: [`MAF-GENISLEME-NOKTALARI.md`](MAF-GENISLEME-NOKTALARI.md) — `AIFunction` sarmalama

---

## Amaç

Bu faz tool kayıt yüzeyindeki üç şeyi birlikte ele alır, çünkü üçü de **aynı
altyapıya** — sarmalayıcı zincirine ve `AgentPrismBuilder`'a — dokunur.

Birincisi bir yapısal borçtur ve ölçümde çıktı: sarmalayıcı zinciri **iki
yerde elle yazılı** ve kodun kendi yorumu bunu itiraf ediyor. İkincisi
tüketicinin istediği argüman doğrulama halkasıdır; o halka, tek kompozisyon
noktası olmadan **iki yere birden** eklenmek zorunda kalır — yani K-483'ün
sınıfını üçüncü kez üretir. Üçüncüsü, K-218'in dört kez bedel ödettiği tuzağı
API ile kapatan `AddScopedTool`'dur.

- **T-3** — Tool argümanları için değiştirilebilir bir yerel doğrulama halkası.
- **T-4** — Çağrı başına DI kapsamı açan tool kaydı.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`ToolRegistry.cs:110-135`](../src/AgentPrism.Core/Tools/ToolRegistry.cs) | Sarmalayıcı zinciri burada elle kuruluyor: `Truncating → Approval → Timeout → Authorizing` |
| [`McpTenantTools.cs:70-90`](../src/AgentPrism.Mcp/Internal/McpTenantTools.cs) | 🚨 **Aynı zincir ikinci kez elle kuruluyor.** Kodun kendi yorumu: *"MCP tools do not go through that registry, so the wrapping is repeated on this path"* |
| [`ToolRegistry.cs:110-135`](../src/AgentPrism.Core/Tools/ToolRegistry.cs) · [`McpTenantTools.cs:70-90`](../src/AgentPrism.Mcp/Internal/McpTenantTools.cs) | Zincirde bir **doğrulama halkası yok**; argüman kontrolü yalnız bağlamanın yan etkisidir |
| [`AgentPrismGeneratedToolArguments.cs:33-60`](../src/AgentPrism.Abstractions/Tools/AgentPrismGeneratedToolArguments.cs) | `GetRequired`/`GetOptional` **ada göre** okur; şemada olmayan fazladan alanları görmez (`additionalProperties:false` şemada yazar, yerel uygulanmaz) |
| [`AgentPrismBuilder.cs:28-83`](../src/AgentPrism.Core/AgentPrismBuilder.cs) | Her tool kaydı `Services.AddSingleton` — kapsamlı bağımlılık için hiçbir yol yok |
| [`ToolMethodScanner.cs:95-103`](../src/AgentPrism.Core/Tools/ToolMethodScanner.cs) | Örnek metot **tarama anında** reddediliyor; reddin metni MAF'ın boş sağlayıcısını gerekçe gösteriyor (K-347) |
| [`guides/write-your-own-tool.md:44-67`](../docs-site/src/content/docs/guides/write-your-own-tool.md) | Kapsamlı iş için reçete **var**, ergonomi yok: sekiz satırlık `IServiceScopeFactory` kalıbı |
| [`McpToolRegistry.cs:24-77`](../src/AgentPrism.Mcp/McpToolRegistry.cs) | MCP kayıt defteri kod defterini **sarmalıyor**; MCP tool'ları `ToolRegistry`'nin gövdesinden hiç geçmiyor |

> Kanıtlar 2026-08-31 tarihinde `8105c00` üzerinde doğrulandı.

---

## 127.1 — Önce tek kompozisyon noktası

Bu, fazın **ilk** işidir ve diğer ikisinin ön koşuludur.

Zincir bugün iki yerde yaşıyor. Bir halka eklemek iki yeri değiştirmek
demektir; birini unutmak MCP tool'larının o halkayı görmemesi demektir ve
bunu hiçbir test yakalamaz — çünkü iki yol ayrı test edilir.

**MEMORY.md'nin dersi tam olarak budur:** elle tekrarlanan bir ifadeye terim
eklemek sessiz bir kusur **sınıfı** üretir; Faz 68'de yedi yerde yazılı bir
toplama ifadesine üçüncü terim eklenince yalnız SQL düzeltildi ve 4241 test
kaçırdı.

```mermaid
flowchart LR
    subgraph Bugün
        A[ToolRegistry] -->|elle| C1[Truncating → Approval → Timeout → Authorizing]
        B[McpTenantTools] -->|elle| C2[Truncating → Approval → Timeout → Authorizing]
    end
    subgraph Hedef
        D[ToolRegistry] --> E[ToolWrapperChain.Compose]
        F[McpTenantTools] --> E
    end
```

`ToolWrapperChain.Compose` `internal`'dır ve `AgentPrism.Core` içinde yaşar;
`AgentPrism.Mcp` ona zaten `InternalsVisibleTo` ile erişiyor
(`AssemblyInfo.cs:8` bu erişimi adıyla anlatıyor).

**Kapı:** iki çağrı yerinin aynı zinciri kurduğunu bir test sabitler. Sarmalayıcı
tiplerinin sırasını dışarıdan okuyan bir test yazılır; halka eklendiğinde her
iki yolda da görünmezse kırmızıdır.

🚨 Tek fark korunur: MCP tarafı `ToolEffect.Read`'i `External`'a yükseltiyor
(uzak sunucu kendi etkisini beyan etmez). Bu **kompozisyonun** değil, çağrı
yerinin kararıdır ve orada kalır.

## 127.2 — `IToolArgumentsValidator` — argüman kapısı

Bugün argüman kontrolü bir **kapı değil, bağlamanın yan etkisidir**. Tip
uyuşmazlığı, eksik `required` ve geçersiz `enum` bağlama sırasında fırlar; ama
fazladan alanlar sessizce yok sayılır, ve elle yazılmış `AIFunction`'lar ile
MCP'den gelen tool'lar bu davranışı **hiç paylaşmaz**.

```csharp
// AgentPrism.Abstractions
public interface IToolArgumentsValidator
{
    ValueTask<ToolArgumentsValidationResult> ValidateAsync(
        ToolDescriptor tool,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken = default);
}

public sealed record ToolArgumentsValidationResult
{
    public static ToolArgumentsValidationResult Valid { get; }
    public static ToolArgumentsValidationResult Invalid(string reason);

    public bool IsValid { get; }
    /// <summary>The reason shown to the model. Never carries argument values.</summary>
    public string? Reason { get; }
}
```

**Varsayılan no-op'tur** (K1 — sıfır sürpriz) ve `TryAddSingleton` ile
kaydedilir (K4 — tüketicinin kaydı kazanır). AgentPrism yerleşik bir JSON
Schema doğrulayıcısı **sevk etmez**: bu, `guides/structured-output.md`'nin
yanıt tarafındaki duruşuyla tutarlıdır — doğrulama tüketicinin güven
sınırındadır.

### Halkanın yeri

```
Authorizing (en dış) → Validating → Timeout → ApprovalRequired → Truncating → gerçek fonksiyon
```

Gerekçe, Faz 69'un kendi gerekçesinin devamıdır: yetkilendirme her şeyden
önce koşar — çağıranın hiç yapamayacağı bir çağrıyı doğrulamak ters olurdu.
Doğrulama, timeout ve onaydan **önce** koşar: bozuk bir çağrı için ne zaman
bütçesi harcanmalı, ne de bir insandan onay istenmelidir.

### Reddedilen çağrının sonucu

Reddedilen çağrı modele **güvenli** bir sonuç döner ve `ToolFailed` olayına
yazılır. Metin `ToolFailureText`'in bugünkü kuralına uyar: argüman **değerleri**
asla metne girmez — bir run olayı kalıcıdır ve argüman bir `secret` taşıyabilir.

### Karşı görüş, ölçüldü

Aday metni "tüketici kendi `AIFunction`'ını `AddTool`'dan önce sarmalayabilir,
yani seam kompozisyonla zaten var" diyordu. Ölçüm bunu **yarı doğru** buldu:
kod tool'ları için doğru, **MCP tool'ları için yanlış** — onlar
`McpToolCatalog`'tan gelir ve tüketicinin sarmalayacağı bir yer yoktur.
Fazın kazancı budur ve 127.1 olmadan bu kazanç elde edilemez.

## 127.3 — `AddScopedTool` — çağrı başına kapsam

Kurumsal tüketicinin **standart** hâli budur: repository, `DbContext`, current
user. Rapor 23 handler'ın hepsinin bu kalıba ihtiyaç duyduğunu ölçtü. Bugün
her biri sekiz satırlık bir `IServiceScopeFactory` kalıbı ister ve yanlış
yazımı **çalışma anında** patlar.

### Tasarım: boş sağlayıcıyı doldurmak

K-218'in tuzağı şudur: MAF, `AIFunctionArguments.Services` olarak boş bir
sağlayıcı geçirir. `AddScopedTool` o boşluğu doldurur:

```csharp
// AgentPrism.Core — internal
internal sealed class ScopedAIFunction(AIFunction inner, IServiceScopeFactory scopes)
    : DelegatingAIFunction(inner)
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        arguments.Services = scope.ServiceProvider;
        return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
    }
}
```

Böylece K-218'in *"çözme"* yasağı, bu yolla kaydedilmiş tool'lar için bir
**garantiye** döner: `AIFunctionArguments.Services` artık gerçek ve kapsamlıdır,
ve tool tamamlanınca kapsam kapanır.

🚨 **`AIFunctionArguments.Services`'in yazılabilir olduğu doğrulanmadı —
`maf-api-kesfi` ile ölçülmeli.** Yazılamıyorsa tasarım değişir: o durumda
kapsam sağlayıcısı `AdditionalProperties` üzerinden veya sarmalanan
delegate'e parametre olarak taşınır. Bu fazın **ilk** işi bu ölçümdür.

### Public imza

```csharp
// AgentPrism.Abstractions — IAgentPrismBuilder
IAgentPrismBuilder AddScopedTool(AIFunction tool);
IAgentPrismBuilder AddScopedTool(AIFunction tool, Action<ToolRegistrationOptions> configure);
```

Şekli `AddTool` ile **birebir** aynıdır; tek fark bir kelimedir ve o kelimenin
anlamı yazılıdır: *her çağrı kendi DI kapsamında koşar.*

### Aday metnindeki `AddScopedTool<THandler>()` neden reddedildi

Aday `AddScopedTool<THandler>()` diyordu: handler tipini çöz, tek
`[AgentPrismTool]` örnek metodunu bul, çağır. Bu **yansıma** gerektirir ve
`AgentPrism.Core` AOT uyumlu kalmak zorundadır. Ayrıca K-347'yi
(`ToolMethodScanner` örnek metotları reddeder) yeniden açardı.

**K-347 açılmıyor.** Tarayıcı örnek metotları reddetmeye devam eder; reddin
metni yalnız yeni API'yi **adıyla** gösterecek şekilde genişletilir — bugün
tüketiciye "yapamazsın" diyor, yarın "şunu kullan" diyecek.

---

## Planlanan Public API

```csharp
// AgentPrism.Abstractions
public interface IToolArgumentsValidator { /* 127.2 */ }
public sealed record ToolArgumentsValidationResult { /* 127.2 */ }

public partial interface IAgentPrismBuilder
{
    IAgentPrismBuilder AddScopedTool(AIFunction tool);
    IAgentPrismBuilder AddScopedTool(AIFunction tool, Action<ToolRegistrationOptions> configure);
}
```

**Sözleşme değişikliği:** `IAgentPrismBuilder`'a metot eklemek Faz 7'den sonra
kırıcıdır (arayüzü uygulayan tüketici kırılır). Bugün eklemek **bedavadır**;
`PublicAPI.Shipped.txt` boştur (K-603).

### HTTP `endpoint`'leri

Yok. Doğrulama sunucu içi bir halkadır; uç yüzeyi değişmez.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Tools/
├── IToolArgumentsValidator.cs           (yeni)
└── ToolArgumentsValidationResult.cs     (yeni)

src/AgentPrism.Core/Tools/
├── ToolWrapperChain.cs                  (yeni — TEK kompozisyon noktası)
├── ValidatingAIFunction.cs              (yeni)
├── ScopedAIFunction.cs                  (yeni)
├── NoOpToolArgumentsValidator.cs        (yeni — varsayılan)
├── ToolRegistry.cs                      (değişir — zincir Compose'a taşınır)
└── ToolMethodScanner.cs                 (değişir — ret metni AddScopedTool'u anar)

src/AgentPrism.Core/AgentPrismBuilder.cs (değişir — AddScopedTool)
src/AgentPrism.Mcp/Internal/McpTenantTools.cs   (değişir — zincir Compose'a taşınır)

tests/AgentPrism.Core.UnitTests/Tools/
├── ToolWrapperChainTests.cs             (yeni — iki yolun aynı zinciri kurduğu)
├── ToolArgumentsValidatorTests.cs       (yeni)
└── ScopedToolTests.cs                   (yeni)

tests/AgentPrism.AspNetCore.FunctionalTests/ScopedToolLifetimeTests.cs   (yeni)

docs-site/src/content/docs/guides/write-your-own-tool.md    (değişir)
docs-site/src/content/docs/reference/extension-points.md    (değişir)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Doğrulama halkası kod tool'una eklenip MCP tool'una eklenmiyor | Birim | `ToolWrapperChainTests` — iki yol aynı sarmalayıcı sırasını üretmeli |
| Halka sırası kayıyor (doğrulama onaydan sonra koşuyor) | Birim | `ToolWrapperChainTests` |
| Reddedilen çağrının metni **argüman değeri** sızdırıyor | Birim | `ToolArgumentsValidatorTests` — 🚨 güvenlik ekseni |
| Reddedilen çağrı `ToolFailed` olayına yazılmıyor | Fonksiyonel | `ToolArgumentsValidatorTests` |
| Doğrulayıcı **fırlatırsa** run çöküyor (kapalı kalması gerekirken) | Birim | `ToolArgumentsValidatorTests` — fırlatan doğrulayıcı çağrıyı **reddetmeli**, run'ı düşürmemeli |
| Varsayılan no-op olmadığı için var olan kurulum kırılıyor | Sözleşme | mevcut tool sözleşme testleri regresyon oracle'ı |
| Kapsam tool tamamlanmadan kapanıyor (kullanım sonrası dispose) | **Fonksiyonel** | `ScopedToolLifetimeTests` — sınır: DI |
| Kapsam hiç kapanmıyor (sızıntı) | Fonksiyonel | `ScopedToolLifetimeTests` — `IDisposable` sayacı |
| İki eşzamanlı tool çağrısı **aynı** kapsamı paylaşıyor | Fonksiyonel | `ScopedToolLifetimeTests` — `AllowConcurrentToolCalls` açık |
| Kapsamlı tool başka kiracının kapsamını görüyor | **Sözleşme** (`TenantIsolationContract`) | dört koşumda birden |
| İptal kapsam kapanmadan yayılıyor | Fonksiyonel | `ScopedToolLifetimeTests` |
| AOT'ta `ScopedAIFunction` yansımaya düşüyor | Kapı | mevcut AOT kapısı |
| Boş/aşırı argüman (`{}`, 1 MB JSON) doğrulayıcıyı çökertiyor | Birim | `ToolArgumentsValidatorTests` |

Beş soru ve cevapları tablonun içindedir: **iptal**, **eşzamanlılık**,
**boş/aşırı girdi**, **başka kiracı**, **alt sistem hatası** (doğrulayıcının
fırlatması) — beşi de ayrı satırdır.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Fazladan alan reddeden bir `IToolArgumentsValidator` kayıtlı | Modele şemada olmayan bir alan gönderten bir tool çağrısı yaptır | Çağrı reddedilir; model güvenli metin görür; `ToolFailed` olayı yazılır; argüman değeri hiçbir yere yazılmaz |
| 2 | Aynı doğrulayıcı, bir **MCP** tool'u | Aynı senaryo | Aynı davranış — kapının MCP tool'unu da gördüğü budur |
| 3 | Hiçbir doğrulayıcı kayıtlı değil | Normal tool çağrısı | Davranış Faz 126 ile birebir aynı |
| 4 | `AddScopedTool` ile kaydedilmiş, `DbContext` çözen bir tool | İki tool çağrısı içeren bir run | Her çağrı kendi kapsamını alır; ikisi de tamamlanınca kapsamlar kapanır |
| 5 | Örnek metoda `[AgentPrismTool]` konmuş bir tip | `AddToolsFrom<T>()` | Ret mesajı `AddScopedTool`'u **adıyla** gösterir |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `AIFunctionArguments.Services` yazılabilir mi? | Ölçüm sorusu | `maf-api-kesfi` ile **ilk iş** ölç. Yazılamıyorsa 127.3'ün tasarımı değişir; kapsam yaşam döngüsü kararı değişmez |
| 2 | Doğrulayıcı **fırlatırsa** ne olur? | A: Çağrı reddedilir (fail-closed) · B: Run düşer | **A** — `IToolAuthorizationHandler`'ın bugünkü deseniyle tutarlı: fırlatan handler çağrıyı reddeder, run'ı düşürmez |
| 3 | Doğrulayıcı tool başına devre dışı bırakılabilmeli mi (`ToolRegistrationOptions.SkipArgumentValidation`)? | A: Hayır · B: Evet | **A** — kapının delinebilir olması kapıyı bitirir. İhtiyaç ölçülürse ayrı kalem |
| 4 | `ToolWrapperChain` `AgentPrism.Core` içinde mi, `Abstractions` içinde mi? | A: Core (`internal`) · B: Abstractions | **A** — sarmalayıcı tipleri Core'da; `Abstractions`'a taşımak paket yönünü ters çevirir (`DependencyDirectionTests`) |

---

## Bitiş Ölçütleri (DoD)

- [x] Sarmalayıcı zinciri **tek** yerde kuruluyor; `ToolRegistry` ve `McpTenantTools` aynı fonksiyonu çağırıyor — `ToolWrapperChainTests` iki yolun aynı sırayı ürettiğini kanıtlıyor
- [x] Kayıtlı bir `IToolArgumentsValidator` hem kod tool'unu hem **MCP tool'unu** görüyor — manuel case 1 ve 2 (`MT-GUARD-080`, `MT-MCP-068`; otomatik karşılıkları koştu, gerçek sağlayıcı anahtarıyla henüz elle koşulmadı — bkz. Plandan Sapmalar)
- [x] Doğrulayıcı kayıtlı değilken davranış değişmiyor; mevcut tool testleri değişmeden geçiyor
- [x] Reddedilen çağrının metni argüman **değeri** taşımıyor
- [x] Fırlatan doğrulayıcı çağrıyı reddediyor, run'ı düşürmüyor
- [x] `AddScopedTool` ile kaydedilmiş tool her çağrıda taze kapsam alıyor; kapsam tool tamamlanınca kapanıyor — `ScopedToolLifetimeTests`
- [x] Eşzamanlı iki tool çağrısı ayrı kapsamlar alıyor
- [x] `ToolMethodScanner` ret metni `AddScopedTool`'u adıyla gösteriyor; K-347 **açılmadı** (örnek metot hâlâ reddediliyor)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. Plandan Sapmalar (bu ortamda sağlayıcı anahtarı yok)
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/18-MCP-VE-A2A.md` ve `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` içine eklendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# İki yolun aynı zinciri kurduğu
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests --filter-method "*ToolWrapperChain*"

# Kapsam yaşam döngüsü (sınır: DI)
./artifacts/bin/AgentPrism.AspNetCore.FunctionalTests/release/AgentPrism.AspNetCore.FunctionalTests --filter-method "*ScopedToolLifetime*"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| `AIFunctionArguments.Services` yazılamaz; tasarımın yarısı düşer | Açık Soru 1 fazın **ilk** işidir; alternatif tasarım orada yazılı |
| Kompozisyon birleştirilirken MCP'nin `Read → External` yükseltmesi kaybolur | Farkın çağrı yerinde kaldığı 127.1'de yazılı; `ToolWrapperChainTests` ikisini ayrı doğrular |
| Yeni halka her tool çağrısına ek gecikme koyar | Varsayılan no-op'tur ve kayıt yoksa halka **hiç eklenmez** — `ContentGuard`'ın bugünkü deseni |
| Kapsamlı tool'un kapsamı akışlı yolda erken kapanır | `ScopedToolLifetimeTests` akışlı uçta da koşar |
| `IAgentPrismBuilder`'a metot eklemek tüketici uygulamalarını kırar | Bugün ucuz (K-603); plan bunu yazıyor. Faz 7'den sonra aynı ekleme bir sürüm kararıdır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

- **`docs-site/src/content/docs/reference/extension-points.md` yok.** Fazın "Tüketici
  yüzeyi" satırı bu dosyayı anıyordu; repo'da böyle bir dosya hiç yoktu (muhtemelen
  aday metninden kalma yanlış bir yol). Onun yerine gerçekten var olan ve tool
  kaydını anlatan üç sayfa güncellendi: `getting-started/tools.md`,
  `concepts/tools.md`, `guides/write-your-own-tool.md`.
- **`samples/AgentPrism.Api` ile gerçek sağlayıcı çağrısı yapılmadı.** Bu ortamda
  bir OpenAI/Anthropic/vb. API anahtarı yok. Onun yerine gerçek boru hattı
  (`FunctionInvokingChatClient` tool döngüsü, gerçek `AgentPrismTestHost`)
  `ToolGovernanceEndpointTests.Rejected_arguments_complete_the_run_and_are_recorded_as_ToolFailed`
  ve `ScopedToolLifetimeTests`'te sahte bir model sağlayıcısıyla (`FakeModelProvider`)
  koştu — MAF'ın gerçek fonksiyon çağırma döngüsünden geçer, yalnız model kararı
  sahtedir. `MT-GUARD-080/081`, `MT-MCP-068`, `MT-CORE-107/108` bu yüzden 👤
  (elle, gerçek anahtarla) koşulmadı işaretiyle kaldı.
- **İki ek manuel case, plandaki iki dosyanın dışında.** Plan yalnız
  `18-MCP-VE-A2A.md` ve `22-GUARDRAIL-VE-YAPISAL-CIKTI.md`'yi anıyordu (argüman
  kapısı için). `AddScopedTool`'un yaşam döngüsü (manuel case 4) ve
  `ToolMethodScanner`'ın ret metni (manuel case 5) argüman doğrulamasından ayrı,
  genel bir tool-kaydı konusu olduğu için `02-CEKIRDEK-VE-KATALOG.md`'ye
  (`CORE`, zaten `Tools/` kapsıyor) `MT-CORE-107`/`MT-CORE-108` olarak eklendi.
- **`capabilities.md`'de üç ÖNCEKİ satır da kısaltıldı.** İki yeni satır
  (`Scoped tools`, `Argument validation`) eklenince üretilen `llms.txt` sabit
  20 480 bayt bütçesini aştı (agent map yalnız tablonun `Ad` ve `Kayıt/kaynak`
  hücrelerini okur, "Ne zorlanıyor" hücresini HİÇ okumaz — ölçüldü). Bütçeye
  dönmek için `Delegate tools`, `Tool output size limit` ve `Client-side tools`
  satırlarının **kod açıklığı** hücreleri kısaltıldı; anlamları ve tam açıklamaları
  sayfanın kendi metninde değişmeden durur. İçerik hatası değil, bütçe disiplini.
- **`ValidatingAIFunction` reddi, `AuthorizingAIFunction`'ın deseninin TERSİDİR.**
  Plan yalnız "reddedilen çağrı `ToolFailed` olayına yazılır" diyordu; somut
  mekanizma plandan çıkarıldı: `AuthorizingAIFunction` bir reddi normal başarılı
  bir sonuç olarak DÖNER (fırlatmaz), `ValidatingAIFunction` ise
  `AgentPrismException` FIRLATIR — MAF'ın kendi exception→`FunctionResultContent`
  dönüşümü bunu `ToolFailed` yapar (`TimeoutAIFunction`'ın zaten kullandığı aynı
  mekanizma, `ToolGovernanceEndpointTests`'in mevcut timeout testiyle önceden
  kanıtlanmıştı). İki halka aynı "fail-closed" ilkesini paylaşır ama farklı
  kayıt sınıfı üretir — bilinçli, plan onaylı bir asimetri (DoD, "Reddedilen
  çağrının sonucu").
- **`ValidatingAIFunction` `public`.** Plandaki "Planlanan Public API" bölümü
  yalnız arayüzü, sonuç tipini ve `AddScopedTool`'u sayıyordu; sarmalayıcı
  tipin kendisi listede yoktu. Kod tarafında sorun değil —
  `AuthorizingAIFunction`/`TimeoutAIFunction`/`TruncatingAIFunction` zaten aynı
  desende `public`'tir (tüketici `AITool.GetService<T>()` ile katmanı bulabilsin
  diye) ve `PublicAPI.Unshipped.txt` doğru güncellenmişti — yalnız plan bunu
  öngörmemişti (bağımsız denetim bulgusu, 🟡#1). Bu belge şimdi düzeltiliyor.

## Bu Fazda Verilen Kararlar

Yok. Plandaki dört Açık Soru, plan dokümanının kendi "Öneri" sütunundaki A
seçenekleriyle (tek kompozisyon noktası `Core`'da kalır, fırlatan doğrulayıcı
reddeder, tool-başına muafiyet yok, `AIFunctionArguments.Services` yazılabilir
— `maf-api-kesfi` ile ölçüldü) kullanıcıya yeniden sorulmadan kapandı; hiçbiri
public API/compatibility contract, güvenlik/kiracı sınırı veya kalıcı veri
kararı düzeyinde değildi, dolayısıyla yeni bir `K-*` kaydı açılmadı.

## Gerçekleşen Public API

Plandakiyle birebir. `AgentPrism.Abstractions/PublicAPI.Unshipped.txt`:

```
AgentPrism.IToolArgumentsValidator
AgentPrism.IToolArgumentsValidator.ValidateAsync(AgentPrism.ToolDescriptor! tool, Microsoft.Extensions.AI.AIFunctionArguments! arguments, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<AgentPrism.ToolArgumentsValidationResult!>
AgentPrism.ToolArgumentsValidationResult (+ record üyeleri: <Clone>$, Equals, GetHashCode, ToString, ==, !=, IsValid, Reason, static Valid, static Invalid(string))
```

`AgentPrism.Core/PublicAPI.Unshipped.txt`:

```
AgentPrism.IAgentPrismBuilder.AddScopedTool(Microsoft.Extensions.AI.AIFunction! tool) -> AgentPrism.IAgentPrismBuilder!
AgentPrism.IAgentPrismBuilder.AddScopedTool(Microsoft.Extensions.AI.AIFunction! tool, System.Action<AgentPrism.ToolRegistrationOptions!>! configure) -> AgentPrism.IAgentPrismBuilder!
AgentPrism.ValidatingAIFunction
AgentPrism.ValidatingAIFunction.ValidatingAIFunction(Microsoft.Extensions.AI.AIFunction! innerFunction, AgentPrism.ToolDescriptor! descriptor, AgentPrism.IToolArgumentsValidator! validator, Microsoft.Extensions.Logging.ILogger<AgentPrism.ValidatingAIFunction!>! logger) -> void
```

`ToolWrapperChain`, `ScopedAIFunction`, `NoOpToolArgumentsValidator` planlandığı
gibi `internal` kaldı — public yüzeye girmedi.

## Dosya Listesi (gerçekleşen)

Plandaki listeye ek olarak, imza değişikliğinin dokunduğu çağrı yerleri de
değişti (plan bunları saymıyordu, ama K-483/imza-gövde kuralı gereği zorunluydu):

```
src/AgentPrism.Core/IAgentPrismBuilder.cs                              (değişir — AddScopedTool arayüz üyesi, plan dosyasında yoktu)
src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.Registration.Core.cs  (değişir — NoOpToolArgumentsValidator.Instance TryAddSingleton)
src/AgentPrism.Mcp/Internal/McpToolCatalog.cs                          (değişir — validator alanı + McpTenantTools.Create çağrısına iletim)

tests/AgentPrism.Core.UnitTests/Tools/ToolRegistrationTests.cs         (değişir — ToolRegistry ctor imzası + AddScopedTool ret metni testi)
tests/AgentPrism.Core.UnitTests/Approvals/ToolApprovalTests.cs         (değişir — ToolRegistry ctor imzası)
tests/AgentPrism.Core.UnitTests/Fakes/TestData.cs                      (değişir — TestData.Registry ctor imzası)
tests/AgentPrism.Core.UnitTests/Configuration/ServiceRegistrationSnapshotTests.cs (değişir — yeni IToolArgumentsValidator kaydı satırı)
tests/AgentPrism.Core.UnitTests/Architecture/public-surface-baseline.txt (değişir — +2 Abstractions, +1 Core, otomatik yenilendi)
tests/AgentPrism.Mcp.UnitTests/McpTenantToolsTests.cs                  (değişir — McpTenantTools.Create imzası)
tests/AgentPrism.Mcp.UnitTests/McpDiscoverySingletonTests.cs           (değişir — McpToolCatalog ctor imzası)
tests/AgentPrism.Mcp.UnitTests/McpToolCatalogReachabilityTests.cs      (değişir — McpToolCatalog ctor imzası)
tests/AgentPrism.AspNetCore.FunctionalTests/ToolGovernanceEndpointTests.cs (değişir — yeni fonksiyonel test eklendi)

docs-site/src/content/docs/capabilities.md                             (değişir — 2 yeni satır + 3 bütçe kısaltması, plan farklı sayfa anıyordu)
docs-site/src/content/docs/concepts/tools.md                           (değişir — plandaki gibi)
docs-site/src/content/docs/getting-started/tools.md                    (değişir — plan dosyasında yoktu)
docs-site/src/content/docs/guides/write-your-own-tool.md               (değişir — plandaki gibi)
docs-site/public/llms.txt, llms-full.txt, src/AgentPrism.Core/buildTransitive/AgentPrism.AgentMap.md (üretilir — build-agent-map.mjs)

docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md                             (değişir — MT-CORE-107/108, plan dosyasında yoktu)
docs/manuel-test/00-INDEKS.md                                          (değişir — 3 satırın faz/case sayısı güncellendi)
```

## Denetim Bulguları

Bağımsız denetçi (taze bağlamlı ayrı bir agent) 2026-09-01'de koştu. Üç 🔴, iki
🟡 bulgu; hepsi bu faz içinde kapandı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `docs-site`'ın `npm run check:content` kapısı kırmızıydı: `write-your-own-tool.md` yeni bölümlerle `DIAGRAM_THRESHOLD` (6500 bayt) üstüne çıktı, diyagramsızdı. | **Düzeltildi.** Sayfaya "Call pipeline order" mermaid akış şeması eklendi (Authorizing → Validating → Timeout → ApprovalRequired → Truncating). `npm run check` dördü de yeşil. |
| 2 | 🔴 | `SourceLanguageTests` kırmızıydı: `ToolRegistrationTests.cs:231`'deki yorumda `Faz 127` ifadesi Türkçe kelime listesine takılıyordu. | **Düzeltildi** — denetim başlamadan önce, uygulama oturumunda zaten fark edilip giderilmişti (denetçi çalışma ağacının erken bir anını yakaladı); bağımsız yeniden koşum bunu doğruladı. |
| 3 | 🔴 | DoD satırı "`samples/AgentPrism.Api` ile gerçek `run` yapıldı" kanıtsızdı; faz dokümanının kapanış bölümleri denetim anında hâlâ boştu. | **Gerekçelendi** — bu ortamda hiçbir sağlayıcı API anahtarı yok (ölçüldü: `env` taraması boş döndü). Gerçek boru hattı (`FunctionInvokingChatClient` döngüsü, gerçek `AgentPrismTestHost`) `ToolGovernanceEndpointTests`/`ScopedToolLifetimeTests`'te sahte model sağlayıcısıyla koştu; ilgili beş manuel case (`MT-GUARD-080/081`, `MT-MCP-068`, `MT-CORE-107/108`) 👤 (gerçek anahtarla elle koşulacak) olarak açıkça işaretlendi — bkz. Plandan Sapmalar ve Sonraki Faza Devir Notu. |
| 4 | 🟡 | `ValidatingAIFunction` `public` sevk ediliyor ama plandaki "Planlanan Public API" özeti bunu saymıyordu. | **Gerekçelendi ve belgelendi** — bkz. Plandan Sapmalar; kod doğru (`AuthorizingAIFunction`/`TimeoutAIFunction`/`TruncatingAIFunction` ile aynı desen), yalnız plan metni eksikti. |
| 5 | 🟡 | Fazın "Tüketici yüzeyi" satırı var olmayan `docs-site/.../reference/extension-points.md`'yi anıyordu. | **Gerekçelendi** — bkz. Plandan Sapmalar; gerçek tüketici içeriği doğru sayfalara (zaten) eklenmişti, yalnız plan yanlış bir yola işaret ediyordu. |

**Temiz çıkan başlıklar:** 3.2 (test tiyatrosu), 3.3 (yanlış test seviyesi),
3.4 (kapsanmayan hata yolları), 3.5 (imza-gövde kayması), 3.7 (repo kuralları,
dil sınırı hariç — o da #2 ile kapandı).

🔴 bulgular kapandıktan sonra dört kapı yeniden koşuldu (`dotnet build` sıfır
uyarı, ilgili test projeleri yeşil, `npm run check` dördü yeşil).

## Sonraki Faza Devir Notu

- Tool sarmalayıcı zinciri artık **tek** kompozisyon noktasından
  (`ToolWrapperChain.Compose`) geçiyor. Yeni bir halka eklerken tek yer
  yeterli — `ToolRegistry`/`McpTenantTools`'a ayrı ayrı dokunmaya gerek yok.
- `IToolArgumentsValidator` şu an sadece **tüketicinin** yazacağı bir arayüz;
  AgentPrism kendi JSON Schema doğrulayıcısını sevk etmiyor (bilinçli tercih,
  §127.2). İleride bir gömülü doğrulayıcı istenirse bu, yeni bir fazdır.
  `Tools/` kapsamında dokunulmayan `docs/manuel-test/22-...`'nin §9'u yalnız
  reddetme yolunu kanıtlıyor — kabul eden bir gerçek doğrulayıcıyla koşum
  henüz yok.
- `samples/AgentPrism.Api`'ye gerçek anahtarla erişimi olan bir sonraki oturum,
  `MT-GUARD-080/081`, `MT-MCP-068`, `MT-CORE-107/108`'i 👤 olarak kapatmalı —
  bu faz onları yalnız otomatik testlerle (gerçek boru hattı, sahte model)
  kanıtladı.
