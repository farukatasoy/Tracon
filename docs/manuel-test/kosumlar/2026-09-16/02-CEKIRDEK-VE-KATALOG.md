# 02 — Çekirdek ve Katalog (`CORE`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../02-CEKIRDEK-VE-KATALOG.md`](../../02-CEKIRDEK-VE-KATALOG.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz A zinciri, tek şerit) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 97 (MT-CORE-001..129) |
| **Port** | 5081 (spec 5080 yazar — şerit sapması) |
| **Şema** | `mt_s1` (spec `tracon` yazar — şerit sapması, skill §1.3) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): sağlayıcı anahtarları şeridin
kendi **ortam değişkenine** okunur. Depo makine genelinde tektir.

**Açılış ölçümü (reset sonrası):**

```
DROP SCHEMA mt_s1 CASCADE -> yeniden kuruldu
Tracon applied 51 migration(s). Schema: mt_s1.
mt_s1.__migrations satir sayisi: 51
GET /api/agents -> 14 agent (azure-support YOK — kimlik yok, beklenen)
/health -> 200 (Degraded: "No model provider has been confirmed healthy yet")
```

⚠️ **`00-INDEKS.md` §4 bayat: "33 migration" diyor, bugün 51.** Bu dosyanın
case'lerini etkilemiyor; kalıcılık ailesinin (`03-KALICILIK-POSTGRESQL.md`)
konusu. Oraya geçen oturum doğrulamalı.

---

## Devir notu

*(oturum kapanışında yazılır)*

---

## Ortam sapması — `echo` sağlayıcısı ve OpenAI anahtarı

Oturum 1 açılışta anahtarların **hepsini** export etti ve `MT-CORE-001` düştü:

```
"No model provider named 'echo' is registered.
 Registered providers: anthropic, google, openai, openai-responses, openrouter."
```

Kök neden koddadır, kusur değildir — `samples/Tracon.Api/Program.cs:277-280`:

```csharp
else { tracon.AddModelProvider(new EchoModelProvider()); }
```

`echo` **yalnız** OpenAI anahtarı yokken kayıtlanır ("zero surprises" kuralı,
`Program.cs:7-9`). Bu dosyadaki 23 case `echo` istiyor, yalnız `MT-CORE-004`
gerçek sağlayıcı istiyor (o da ağa çıkmadan, yerel `Validate`).

∴ **Şerit yapılandırması:** `Tracon__Providers__OpenAI__ApiKey=""` bırakıldı.
Kayıtlı sağlayıcılar: `anthropic, echo, google, openrouter`.

**Bedeli:** katalog 14 yerine **12** agent çözüyor — `knowledge-assistant`
(OpenAI embedding ister) ve `voice-assistant` (ElevenLabs ister) düşüyor.
`azure-support` zaten yok. Bu iki agent'a dokunan case çıkarsa ortam bekleyen
olarak işaretlenir.

---

## MT-CORE-001 — Geçerli tanım hatasız doğrulanır

**Gerçek sonuç**
```json
{ "valid": true, "inconclusive": false, "messages": [] }
```
`GET /api/agents` → 12 agent, `manuel-destek` **yok**. Doğrulama hiçbir şey
kaydetmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-002 — `unknown_tool`: kayıtlı olmayan tool adı

**Gerçek sonuç**
```json
{ "valid": false, "inconclusive": false, "messages": [
  { "severity": "Error", "code": "unknown_tool", "path": "toolNames[1]",
    "message": "Agent 'manuel-hayali-tool' refers to tool 'hayali_tool', but it is not registered in this code." },
  { "severity": "Error", "code": "unknown_tool", "path": "toolNames[2]",
    "message": "Agent 'manuel-hayali-tool' refers to tool 'bir_baska_hayali', but it is not registered in this code." }
]}
```

Beş beklentinin beşi de tuttu: **iki** kayıt (ilk hatada durulmadı) ·
`severity` **ad olarak** (`"Error"`, sayı değil) · `path` sorunlu öğeyi
indeksle gösteriyor · `get_order_status` için mesaj **yok**.

Güvenlik sınırı ayakta: mesaj "not registered **in this code**" diyor — tool
kodunun arayüzden yazılamayacağını kullanıcıya da anlatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-003 — `unknown_model`: kayıtlı olmayan sağlayıcı

**Gerçek sonuç**
```
1) provider "olmayan-saglayici" -> valid:false
   code=unknown_model · path=model.provider
   "No model provider named 'olmayan-saglayici' is registered.
    Registered providers: anthropic, echo, google, openrouter."

2) provider "echo" + model "katalogda-olmayan-model" -> valid:true · messages:[]
```

**Doküman ile kod ÇELİŞMİYOR.** Case bu karşılaştırmayı açıkça istiyordu:
`README.md` katalog için "bir doğrulama listesi değildir" diyor ve kod tam
olarak öyle davranıyor — sağlayıcı kayıtlı olmak zorunda, model adı değil.
Uygulama hiçbir istekte çökmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-004 — `invalid_setting`: tanınmayan sağlayıcı ayarı

**Gerçek sonuç**
```
1) "anthropic.boyle.bir.ayar.yok": true -> valid:false
   code=invalid_setting · path=model.providerSettings
   "ModelBinding.ProviderSettings is invalid: these keys are not recognized:
    anthropic.boyle.bir.ayar.yok.
    Supported keys: anthropic.promptCaching, anthropic.thinking.budgetTokens."

2) "ANTHROPIC.MAX_TOKENS": 100 -> valid:false · ayni kod, ayni liste
```

Üç beklentinin üçü de tuttu: ayar **sessizce yok sayılmıyor**, mesaj
desteklenen anahtarları **listeliyor**, kod `invalid_setting`.

**Adım 2 için ek ölçüm** (spec "farklı harf büyüklüğü" diyor ama desteklenen mi
desteklenmeyen mi anahtar üzerinde olduğunu söylemiyor — desteklenen anahtarla
da ölçüldü):

| Anahtar | Sonuç |
|---|---|
| `anthropic.promptCaching` | valid |
| `Anthropic.PromptCaching` | valid |
| `ANTHROPIC.PROMPTCACHING` | valid |

∴ Eşleştirme **harf büyüklüğüne duyarsız**: tanınan anahtar her yazımda kabul
ediliyor, tanınmayan her yazımda reddediliyor. Tutarlı ve bağışlayıcı taraf
doğru yerde.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-005 — `unknown_skill`: bağlı olmayan skill adı

**Gerçek sonuç**
```json
{ "valid": false, "inconclusive": false, "messages": [
  { "severity": "Error", "code": "unknown_skill", "path": "skillNames[0]",
    "message": "Agent 'manuel-yok-skill' refers to skill 'olmayan-skill', but the skill was not found." }]}
```
İki beklenti de tuttu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-006 — `Inconclusive`: erişilemeyen MCP sunucusu `Valid`'i düşürmez

**Gerçek sonuç**

⚠️ **Ön koşul ilk denemede tutmadı — spec bayat.** `PUT /api/mcp-servers/olu-mcp`
**400** döndü:

```
"title": "Address not allowed",
"detail": "The target resolves to a private network address (127.0.0.1);
           set 'Tracon:Egress:AllowPrivateNetworkTargets' to true to allow it."
```

Bu bir kusur değil, **SSRF kapısı** — case 127.0.0.1 kullanıyor ve kapı o
adresi varsayılanda engelliyor. Uygulama `Tracon__Egress__AllowPrivateNetworkTargets=true`
ile yeniden başlatıldı (kapının kendi mesajının söylediği ayar); kayıt 200 döndü.
`Ön koşul` bu ayarı taşıyacak şekilde düzeltildi (skill §1.1 istisnası).

Sonra:
```json
{ "valid": true, "inconclusive": true, "messages": [
  { "severity": "Warning", "code": "mcp_unreachable", "path": null,
    "message": "A fresh list could not be fetched from MCP servers for missing
                tool names (timeout or connection failure). The result is inconclusive." }]}
SURE: 0.16 sn   ·  /health sonrasi 200
```

Dört beklentinin dördü de tuttu: 8 sn'nin **çok** altında · `inconclusive:true`
· `mcp_unreachable` + `Warning` · uygulama ayakta. **`valid` düşmedi** —
belirsizlik ile geçersizlik ayrı tutuluyor, case'in asıl iddiası bu.

📋 **Not (kapsam sınırı):** kapalı bir port **anında** `connection refused`
veriyor, yani 5 sn'lik zaman aşımı bu case'te hiç çalışmadı. Zaman aşımı yolunu
ölçmek **yavaş** (asılı kalan) bir sunucu ister; bu senaryo `18-MCP-VE-A2A.md`
kapsamındadır.

**Temizlik:** `DELETE /api/mcp-servers/olu-mcp` → 204. Silinmeseydi sonraki tüm
doğrulamalar `inconclusive` dönerdi; silindikten sonra `unknown_tool` davranışı
geri geldi (doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-007 — Kendi kendini çağıran agent reddedilir

**Gerçek sonuç**
```
1) validate, dogrudan dongu -> valid:false
   code = "cycle"   (spec "gercek deger kosumda yazilir" diyordu — ÖLÇÜLDÜ: cycle)
   path = callableAgentNames
   "'manuel-dongu' cannot call itself. An agent calling itself is infinite
    recursion and generates cost until the depth counter runs out."

2) POST /api/agents (ayni tanim) -> HTTP 400 · "Call graph invalid"

3) dolayli dongu: POST manuel-a -> 201 · POST manuel-b(->a) -> 201
   PUT manuel-a(->b) -> HTTP 400
   "There is a cycle in the call graph: manuel-a -> manuel-b -> manuel-a.
    A cyclic graph causes the run to continue until it hits the depth limit."
```

**Doğrulama sorgusu** (`mt_s1` şeması, şerit sapması):
```
 name     | version
----------+---------
 manuel-a |       1
 manuel-b |       1
```
`manuel-dongu` **hiç kaydedilmedi**; `manuel-a` reddedilen güncellemeden sonra
**versiyon 1'de kaldı** — yarım yazma yok.

Dört beklentinin dördü de tuttu. Dolaylı döngü mesajı **yolun tamamını**
(`a -> b -> a`) veriyor; tek bir ad yerine zinciri göstermek teşhisi ucuzlatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-008 — Doğrulama hiçbir zaman istisna sızdırmaz

**Gerçek sonuç**
```
HTTP: 200                                    ✅ dogrulama basarisizligi HTTP hatasi degil
{ "valid": false, "inconclusive": false,
  "messages": [{ "severity": "Error", "code": "compilation_error", "path": null,
    "message": "Agent 'manuel-derleme-hatasi' selected the JsonSchema output
                mode but did not supply Schema." }]}

sunucu logu (istek sirasinda eklenen satirlar):
  "Unhandled exception" eslesmesi: 0        ✅
```

Beş beklentinin beşi de tuttu. Derleyici istisnası `compilation_error`
mesajına çevrilmiş, 500'e dönüşmemiş; mesaj eksik alanı (`Schema`) **adıyla**
söylüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-009 — Boş ve aşırı uzun alanlar

**Gerçek sonuç**
```
1) "name": ""        -> HTTP 400 · title "Agent name empty" · "'name' is required."
2) name alani yok    -> HTTP 400 · title "Invalid request body"
   "JSON deserialization for type 'Tracon.AgentDefinitionRequest' was missing
    required properties including: 'name'."
3) 50.000 karakter instructions -> HTTP 200 · {"valid":true,...} · aninda dondu
```

**Seçilen biçim kaydedildi:** ikisi de **400**, `valid=false` değil. Tutarlı.
İki mesaj farklı katmandan geliyor (biri boş değer, öbürü eksik alan) ve ikisi
de eksiği **adıyla** söylüyor. Uzun `instructions` için uzunluk sınırı yok;
sunucu çökmedi, zaman aşımına uğramadı. Hiçbir adımda 500 yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-020 — Tanınmayan `reasoningEffort` değeri reddedilir

**Gerçek sonuç**
```
1) "cok-yuksek" -> valid:false · code=compilation_error
   "Agent 'manuel-akil''s reasoning effort value is not recognized:
    'cok-yuksek'. Valid values: None, Low, Medium, High, ExtraHigh."
2) "hIgH"       -> valid:true · messages:[]
```
İki beklenti de tuttu. Geçerli değer listesi spec'in yazdığıyla **birebir**
aynı; reddedilen değer mesajda aynen taşınıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-021 — `responseFormat` kombinasyonları

**Gerçek sonuç**
```
m1 (semasiz JsonSchema) -> valid:false
   "Agent 'm1' selected the JsonSchema output mode but did not supply Schema."
m2 (sema dizi)          -> valid:false
   "Agent 'm2''s Schema field must be a JSON object."
m3 (Text + sema)        -> valid:false
   "Agent 'm3' selected the 'Text' output mode but also supplied Schema.
    The schema is only used in JsonSchema mode."
m4 (gecerli JsonSchema) -> valid:false
   "Agent 'm4''s model ('echo/echo-1') does not support structured output."
```

Üç geçersiz kombinasyon **üç ayrı** mesaj üretti — beklenen buydu.

**`echo` sağlayıcısının desteği kaydedildi** (case bunu istiyordu): `echo/echo-1`
**yapısal çıktı desteklemiyor.** `m4` için spec iki sonucu da kabul ediyor;
gerçekleşen ikincisi. Mesaj modeli `sağlayıcı/model` biçiminde adlandırıyor,
yani kullanıcı hangi bağlamanın yetersiz olduğunu görüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-022 — Sıkıştırma ayarları tetikleyicisiz olamaz

**Gerçek sonuç**

⚠️ **Spec'in girdisi iki yerde yanlıştı; davranış doğru ve spec'in
varsaydığından daha iyi.**

```
c1 '"strategy":"Summarize"'        -> HTTP 400 deserialization hatasi
c2 '"strategy":"BoyleBirSeyYok"'   -> HTTP 400 deserialization hatasi
c3 ContextWindow, maxContextWindowTokens YOK -> valid:TRUE   (spec hata bekliyordu)
```

**1) `Summarize` diye bir strateji yok.** Enum
(`src/Tracon.Abstractions/Agents/CompactionStrategyKind.cs`):
`None · SlidingWindow · Truncation · ToolResult · Summarization · ContextWindow`.
Doğru adla tekrar koşuldu:

```
c1b '"strategy":"Summarization"' (tetikleyicisiz) -> valid:false
  "Agent 'c1b' selected the 'Summarization' compaction strategy but gave no
   trigger (at least one of TriggerTokens/TriggerMessages/TriggerTurns is required)."
```
∴ **c1 beklentisi tutuyor** — yalnız spec'teki strateji adı yanlışmış.

**2) `c3` katalogdan TÜRETİYOR, bu bilinçli.**
`AgentDefinitionCompiler.Compaction.cs:145-168` bunu belgeliyor: değer
verilmezse `ModelDescriptor.ContextWindowTokens`'tan türetilir, çünkü
"kullanıcının elle kopyalayacağı değer zaten model bağlamasında duruyor".
Derleme **yalnız iki kaynağın da boş olduğu** durumda düşer. Kanıtlandı:

```
c3b: model "katalogda-yok" -> valid:false
  "Agent 'c3b' selected the ContextWindow compaction strategy but did not
   supply MaxContextWindowTokens, and its model ('echo/katalogda-yok') has no
   context window size in the catalog either. Set MaxContextWindowTokens explicitly."
```
∴ `c3` beklentisi **bayat**; mesaj ortaya çıktığında spec'in istediği alanı
adlandırıyor, üstelik **ikinci** kaynağı da söylüyor.

`Beklenen sonuç` ve girdiler bu iki bulguya göre düzeltildi (skill §1.1).

🚨 **c2 GERÇEK BİR BULGU — `HATA-S1-008`.** Bilinmeyen strateji adı reddediliyor
ama mesaj **reddedilen değeri taşımıyor**:

```
"The JSON value could not be converted to Tracon.CompactionStrategyKind.
 Path: $.compaction.strategy | LineNumber: 0 | BytePositionInLine: 99."
```

`BoyleBirSeyYok` metinde **geçmiyor**; geçerli değerler de listelenmiyor.
Karşılaştır: `MT-CORE-020` (`reasoningEffort`) aynı sınıf hatada hem reddedilen
değeri hem geçerli listeyi veriyor. Aradaki fark, enum'un `CompactionStrategyKind`
olarak **güçlü tiplenmiş** olması: istek Tracon'un doğrulayıcısına hiç
ulaşmıyor, ASP.NET Core'un JSON okuyucusunda düşüyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### HATA-S1-008 — Sıkıştırma stratejisi adı yanlışsa hata reddedilen değeri de geçerli listeyi de söylemiyor

| | |
|---|---|
| **Önem** | Düşük |
| **Bulunduğu case** | MT-CORE-022 (c2) |
| **Sınıf** | Tanı kalitesi · enum deserialization |

**Repro**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"c2","model":{"provider":"echo","model":"echo-1"},
       "compaction":{"strategy":"BoyleBirSeyYok","triggerTokens":1000}}'
```

**Gözlenen:** HTTP 400 · `"The JSON value could not be converted to
Tracon.CompactionStrategyKind. Path: $.compaction.strategy | LineNumber: 0 |
BytePositionInLine: 99."`

**Beklenen sınıf davranışı:** `reasoningEffort` ile aynı — reddedilen değeri
aynen taşı ve geçerli değerleri listele.

**Kök neden:** `strategy` alanı `CompactionStrategyKind` enum'una güçlü
tiplenmiş; `System.Text.Json` dönüşümü Tracon'un doğrulama katmanından **önce**
yapıyor. `reasoningEffort` ise string olarak alınıp Tracon içinde çözümlendiği
için iyi mesaj üretebiliyor.

**Sınıf taraması gerekir (kapanışta):** aynı desendeki her enum alanı. İlk
bakışta `responseFormat.kind`, `compaction.strategy` ve OpenAPI'den türeyen
diğer enum'lar aynı sınıfta. `BytePositionInLine` gibi bir ayrıntı tüketiciye
hiçbir şey anlatmıyor; alan adı (`$.compaction.strategy`) tek yararlı kısım.

**Etki:** düşük — istek yine reddediliyor, sessiz kabul yok. Bedeli teşhis
süresi: kullanıcı geçerli adı dokümanda aramak zorunda.

---

## MT-CORE-023 — Skill kataloğu kayıtlı değilken skill isteyen tanım

**Gerçek sonuç**
```
dotnet run -c Release -> cikis 0
beklenen istisna: Agent 'skill-isteyen' refers to skill 'olmayan-skill',
                  but the skill was not found.
AgentName: skill-isteyen
```

Dört beklentinin dördü de tuttu: çıktı `beklenen istisna:` ile başlıyor ·
mesaj eksik skill'i adıyla söylüyor · `AgentName` doğru · `🚨 istisna ATILMADI`
**görünmedi**. 2026-08-15'in düzeltilmiş öncülü (erişilebilir dal, tanımın var
olmayan skill'e işaret ettiği daldır) bugün de geçerli.

⚠️ **Spec'e iki düzeltme** (skill §1.1 istisnası):

1. **Script derlenmiyordu.** `catalog.ResolveAsync("skill-isteyen")` →
   `error CS1501: No overload for method 'ResolveAsync' takes 1 arguments`.
   Gerçek imza `IAgentCatalog.cs:32`:
   `ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)`.
   Çağrı `ResolveAsync("skill-isteyen", null, CancellationToken.None)` yapıldı.
2. **Beklenen mesaj metni Türkçe yazılmıştı** ("...isaret ediyor ancak skill
   bulunamadi."). Sevk edilen metin K-228'den beri **İngilizce**. Bugünkü
   metne göre güncellendi.

**Sapma:** proje `<scratch>` yerine `~/tracon-manuel/skillsiz` altında kuruldu
(spec böyle diyor); yerel feed oturum 2'den hazır (`0.0.0-preview.0.789`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-024 — Derleme hatası hangi agent'ta olduğunu söyler

**Gerçek sonuç**

⚠️ **İlk koşumda case'in ölçmek istediği dala VARILAMADI.** Spec'in script'i
hiçbir model sağlayıcısı kaydetmiyor; derleme tool denetiminden **önce**
sağlayıcıda düşüyor:

```
Agent 'tool-eksik' could not be compiled: No model provider named 'echo' is
registered. Registered providers: no provider is registered. For OpenAI, call
`builder.AddTracon().UseOpenAI(apiKey)`.
```

(`echo` yalnız örnek uygulamanın kendi `EchoModelProvider`'ıdır — yalın bir
tüketici projesinde yoktur.) `Tracon.Testing` eklenip
`.AddModelProvider(new FakeModelProvider("echo"))` ile sağlayıcı kaydedildi;
`Beklenen sonuç` bu ön koşulu taşıyacak şekilde düzeltildi (skill §1.1).

Sonra, **dört beklentinin dördü de tuttu:**

```
Agent 'tool-eksik' references the following tools, but they are not registered
in code: hayali_tool. Registered tools: Var. Tools are defined only in code;
register them with `builder.AddTracon().AddTool(...)`.
```

| Beklenti | Gözlenen |
|---|---|
| agent adını taşır | `Agent 'tool-eksik'` ✅ |
| eksik tool adını taşır | `hayali_tool` ✅ |
| kayıtlı tool'ları listeler, `Var` görünür | `Registered tools: Var` ✅ |
| `builder.AddTracon().AddTool(...)` yönlendirmesi | aynen var ✅ |

🚨 **2026-08-15'in düzeltmesi bugün de doğrulandı:** kayıtlı ad `Var`
(**metot adı**), `var_olan_tool` (öznitelik adı) **değil**. `.AddTool(delegate)`
`[TraconTool]` özniteliğini hiç okumuyor. Yirmi agent'lı bir kurulumda mesaj
hangi agent, hangi tool ve hangi alternatifler olduğunu tek satırda veriyor.

**Yan gözlem:** derleme `warning TRC0009` üretti (`x` parametresinin
açıklaması yok) — `MT-PKG-040`/`049`'da ölçülen aynı analyzer, tüketici
projesinde de çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

