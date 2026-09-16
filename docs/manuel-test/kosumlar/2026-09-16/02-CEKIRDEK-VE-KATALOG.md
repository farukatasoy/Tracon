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

## MT-CORE-030 — Kod kaynaklı agent veritabanı tanımını yener

**Gerçek sonuç**
```
1) GET /api/agents -> support: origin="Code" (AD olarak, sayi degil)
                      displayName="Support Assistant"

2) POST /api/agents {"name":"support",...} -> HTTP 409
   "title": "Agent name in use"
   "detail": "'support' is an agent defined in code and cannot be changed from
              the management API. Code wins name conflicts, so a definition
              written with the same name would never resolve."

3) GET /api/agents -> support TEK kayit · origin="Code"
                      displayName hala "Support Assistant" (SAHTE Destek DEGIL)
```

**Doğrulama sorgusu** (`mt_s1`): `SELECT name, version FROM agent_definitions
WHERE name='support'` → **0 satır**.

Spec iki sonucu da kabul ediyordu ("ya reddedilir ya kaydedilir ama katalogda
görünmez"); gerçekleşen **daha güçlü** olanı: kayıt hiç oluşmadı, yani veritabanında
asla çözülmeyecek ölü bir satır birikmiyor. Güvenlik özelliği ayakta — arayüzden
gelen tanım kod tanımını ezemiyor ve 409 gerekçesini kullanıcıya açıklıyor.

⚠️ **Spec'e düzeltme** (skill §1.1): doğrulama sorgusu `display_name` sütununu
okuyordu, öyle bir sütun **yok**. Şema:
`id · tenant_id · name · version · definition (jsonb) · created_at · updated_at`
— görünen ad `definition` içindedir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-031 — Katalog ada göre sıralı döner

**Gerçek sonuç**
```
['cached-support', 'claude-support', 'claude-thinking', 'gemini-strict-filter',
 'gemini-support', 'manuel-a', 'manuel-b', 'openrouter-support', 'order-summary',
 'researcher', 'router', 'summarizer', 'support', 'translator']
SIRALI
```
(`manuel-a`/`manuel-b` `MT-CORE-007`'den kalan kayıtlardır.)

📋 **İkinci beklenti bu veriyle ölçülemez:** "sıralama ordinal'dir: büyük
harfler küçük harflerden önce gelir". Katalogdaki 14 adın **hepsi küçük harf**;
ordinal ile harf-duyarsız sıralama aynı sonucu veriyor. Ayrımı görmek büyük
harfle başlayan bir agent adı ister. Bu bir kusur değil, kapsam sınırı —
`MT-CORE-033`+ veri yazan case'lerden sonra tekrar bakılabilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-032 — Bulunmayan agent `null` döner, istisna atmaz

**Gerçek sonuç**
```
GET  /api/agents/hic-boyle-bir-agent-yok      -> 404
RUN  /api/agents/hic-boyle-bir-agent-yok/run  -> 404
sunucu logu "Unhandled exception" eslesmesi   -> 0
SELECT count(*) FROM mt_s1.runs
  WHERE agent_name='hic-boyle-bir-agent-yok'  -> 0
```

Dört beklentinin dördü de tuttu. Bulunmayan bir agent'ı **çalıştırmayı**
denemek bile `run` kaydı üretmiyor — yani 404 yolu kayıt katmanına hiç
dokunmuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-033 — Sürüm artışı derlenmiş agent önbelleğini geçersiz kılar

**Gerçek sonuç**
```
POST manuel-surum ("BIRINCI SURUM TALIMATI.")  -> 201
run #1                                          -> 200 (SSE, echo yanit verdi)
PUT  manuel-surum ("IKINCI SURUM TALIMATI.")    -> 200
GET  manuel-surum -> descriptor.version = 2
                     definition.instructions = "IKINCI SURUM TALIMATI."
run #2 (uygulama YENIDEN BASLATILMADI)          -> 200
```

**Doğrulama sorgusu** — kanıt burada:
```
  agent_name  | agent_version | status |          started_at
--------------+---------------+--------+-------------------------------
 manuel-surum |             1 |      1 | 2026-09-16 15:48:24.403532+00
 manuel-surum |             2 |      1 | 2026-09-16 15:48:24.700167+00
```

Üç beklentinin üçü de tuttu. İkinci `run` **sürüm 2**'ye yazıldı, yani derlenmiş
agent önbelleği sürüm artışında geçersiz kılındı — kullanıcı eski talimatla
çalışan bir agent görmedi.

📋 **Sonraki oturumlar için yanıt şekli:** `GET /api/agents/{ad}` iki kat
taşıyor — `descriptor` (özet + `version` + `origin`) ve `definition` (tam tanım
+ `instructions`). Spec'in `grep -E "version|instructions"` komutu çalışır ama
alanlar **üst seviyede değildir**; JSON'dan okurken yol
`descriptor.version` / `definition.instructions`'tır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-034 — Kod kaynaklı agent'ta sürümlü çözümleme reddedilir

**Gerçek sonuç**
```
GET /api/agents/support/versions      -> HTTP 404
  { "title": "Agent not found",
    "detail": "There is no agent named 'support'." }        🚨 YANLIS IFADE

GET /api/agents/manuel-surum/versions -> HTTP 200 · IKI surum
  version 2 | IKINCI SURUM TALIMATI.
  version 1 | BIRINCI SURUM TALIMATI.

GET /api/agents/support               -> HTTP 200   (yani agent VAR)
```

**Tutan:** `manuel-surum` için iki sürüm listelendi · hiçbir istekte 500 yok ·
`support` için sürümlü çözümleme reddedildi (4xx).

🚨 **Tutmayan — `HATA-S1-009`.** Case'in gerekçesi "bu **açıkça** söylenmelidir"
diyor; 404 gövdesi gerekçeyi söylemiyor, üstelik **yanlış** bir şey söylüyor:
`support` vardır (`GET /api/agents/support` → 200 ve katalogda görünür), yalnız
sürüm geçmişi yoktur. Kullanıcı agent'ı ekranda görürken "böyle bir agent yok"
cevabı alıyor.

Spec'in operasyonel testi ("gövde `kod kaynagi` taşır **veya** HTTP 4xx") lafzen
sağlanıyor; ama case'in adı ve gerekçesi açık bir sebep istiyor. Bu ikiliği
kaldırmak için `Beklenen sonuç` netleştirildi (skill §1.1) ve case `Kaldı`
işaretlendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### HATA-S1-009 — Kod kaynaklı agent'ın sürüm listesi "böyle bir agent yok" diyor

| | |
|---|---|
| **Önem** | Orta |
| **Bulunduğu case** | MT-CORE-034 |
| **Sınıf** | Tanı kalitesi · kod/DB kaynak ayrımı |

**Repro**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/support/versions" -H "$APB"
# -> 404 "There is no agent named 'support'."
curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/agents/support" -H "$APB"
# -> 200        (ayni agent, ayni an)
```

**Kök neden** — `src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs:664-672`:

```csharp
if (await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
{
    return NotFound(name);
}
```

Denetim `IAgentDefinitionStore`'a, yani **veritabanı** deposuna bakıyor. Kod
kaynaklı agent orada yoktur; dolayısıyla "yok" sonucuna varılıyor. Oysa
"veritabanı deposunda yok" ile "böyle bir agent yok" aynı şey değildir.

**Aynı durumda doğru davranan uç var** — `MT-CORE-030`, `POST /api/agents`:

```
409 "Agent name in use"
"'support' is an agent defined in code and cannot be changed from the
 management API. Code wins name conflicts, ..."
```

Yani proje bu ayrımı nasıl anlatacağını **biliyor**; `versions` ucu o bilgiyi
kullanmıyor.

**Önerilen davranış (kapanışta karar):** `support/versions` ya 200 + boş liste
(sürüm geçmişi yok) ya da 409/404 + kod kaynaklı olduğunu söyleyen bir gövde
dönmeli. Hangisi seçilirse seçilsin, mesaj var olan bir agent için "yok"
dememeli.

**Sınıf taraması gerekir (kapanışta):** `IAgentDefinitionStore.GetAsync ... is
null → NotFound` deseni. `AgentEndpoints.cs`'te `rollback` ve
`versions/{a}/diff/{b}` uçları aynı şekilde kuruluyor gibi görünüyor; kod
kaynaklı agent'la çağrıldıklarında aynı yanıltıcı metni üretme ihtimalleri
**kapanışta ölçülmeli**.

**Etki:** orta — veri kaybı yok, güvenlik sorunu yok; bedeli tüketicinin
teşhis süresi ve dokümana duyduğu güven.

---

## MT-CORE-035 — Tanım hiçbir zaman kimlik bilgisi taşımaz

**Gerçek sonuç**
```
POST manuel-secret (metadata.not = "sk-MANUEL-TEST-SAHTE-ANAHTAR-0000") -> 201

ModelBinding alanlari (GET ile okundu):
  allowConcurrentToolCalls · fallbacks · maxOutputTokens · model · provider ·
  providerSettings · reasoningEffort · responseCache · responseFormat ·
  temperature · topP
  -> API anahtari alani YOK ✅ sozlesme buna izin vermiyor

metadata geri okundu: {"not": "sk-MANUEL-TEST-SAHTE-ANAHTAR-0000"} ✅ beklenen
```

**Doğrulama sorgusu** (`mt_s1`, 4 tanım):
```
definition::text ILIKE '%apikey%' OR '%secret%' OR '%"key"%'
  OR '%connectionstring%'                       -> 0 satir ✅
definition::text LIKE '%sk-%'                   -> 1 satir: manuel-secret
                                                   (tuketicinin KENDI metadata'si)
```

Üç beklentinin üçü de tuttu. K-059 ayakta: kayıtta Tracon'in **kendi** yazdığı
hiçbir sağlayıcı anahtarı yok. Tüketicinin serbest `metadata` alanına kendi
koyduğu metin aynen duruyor — Tracon serbest metadata'yı denetlemiyor, ki
beklenen de bu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-040 — Tool listesi ad, açıklama, şema ve kaynak taşır

**Gerçek sonuç**
```
GET /api/tools -> 10 tool (spec UC bekliyordu)

cancel_order            onay=True   effect=Destructive  izin=orders.cancel
estimate_shipping_cost  onay=False
get_order_status        onay=False
get_slow_report         onay=False
list_recent_orders      onay=False
list_voices             onay=False
mark_preview_ready      onay=False
read_shopping_cart      onay=False
speak                   onay=False
transcribe              onay=False

SIRALI ✅ · her tool'un description'i dolu ✅ · her tool'un jsonSchema'si dolu
ve parametrelerini tanimliyor ✅
```

**Tutan:** sıralama · `cancel_order` tek onay isteyen · açıklamalar ve şemalar
dolu.

⚠️ **İki beklenti bayat, ikisi de doküman kusuru (skill §1.1, düzeltildi):**

1. **"Üç tool görünür" → 10.** Örnek uygulama Faz 52'den beri büyümüş.
   `00-INDEKS.md` §3.2 de yalnız **4** tool listeliyor; o tablo da bayat
   (bu turda düzeltilmedi, dosya 02'nin kapsamı değil — açık kaleme yazıldı).
2. **"`source` alanı `generated`'dır" → `null`, ve bu DOĞRU.**
   `src/Tracon.Abstractions/Tools/ToolDescriptor.cs:30-38`:

   > *"The tool's source. **null** for tools defined in code; the server name
   > for tools coming from a remote MCP server."*

   `"generated"` değeri kod tabanında **hiç geçmiyor**. Alan kod/uzak-MCP
   ayrımı için var ve arayüzde ayrı bir rozet olarak gösteriliyor. Kodda
   tanımlı 10 tool'un onunda da `null` olması beklenen davranıştır.

**Yan gözlem — case'in yazıldığı zamandan beri eklenen alanlar:**
`effect` · `requiredPermission` · `runsOnClient` · `safeToRepeat` ·
`timeout` · `maxOutputBytes`. `cancel_order` için `effect=Destructive` ve
`requiredPermission=orders.cancel` dolu; bunlar bu dosyanın değil ilgili
ailelerin konusu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-041 — Aynı adda iki tool açılışta hata verir

**Gerçek sonuç**
```
beklenen istisna: More than one tool is registered with name 'ayni_ad'.
                  Tool names must be unique.
```
`🚨 istisna ATILMADI` satırı **görünmedi**. "Son kayıt kazanır" davranışı yok;
belirsizlik açılışta patlıyor.

⚠️ **Spec'e düzeltme:** beklenen metin Türkçe yazılmıştı
(`'ayni_ad' adinda birden cok tool kaydedilmis`). Sevk edilen metin K-228'den
beri İngilizce; bugünkü metne göre güncellendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-042 — Onay gerektiren tool sarmalanır ama şeması değişmez

**Gerçek sonuç**
```
cancel_order     | onay: True  | aciklama: "Cancels an order."
   sema: {"type":"object","properties":{"orderId":{"description":"The order
          number.","type":"string"}},"required":["orderId"],
          "additionalProperties":false}

get_order_status | onay: False | aciklama: "Returns the shipping status of an order."
   sema: {"type":"object","properties":{"orderId":{"description":"The order
          number.","type":"string"}},"required":["orderId"],
          "additionalProperties":false}
```

Dört beklentinin dördü de tuttu:

| Beklenti | Gözlenen |
|---|---|
| `cancel_order` onay=true, `get_order_status` onay=false | ✅ |
| ikisinin de `description`'ı boş değil | ✅ |
| ikisinin de `jsonSchema`'sı `orderId` içeriyor | ✅ |
| `cancel_order` adı `ApprovalRequired` gibi önek/sonek taşımıyor | ✅ ad tam olarak `cancel_order` |

İki şema **birebir aynı yapıda** — sarmalayıcı ad, açıklama ve şemanın hiçbirine
dokunmamış. Model tool'u tanıyabilir; onay sarmalaması yalnız çalıştırma
tarafında.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-043 — Tanım yalnız kayıtlı tool'a işaret edebilir

**Gerçek sonuç**
```
POST /api/agents  -> HTTP 400 · "Definition invalid"
  "Agent 'manuel-hayali-tool-kayit' refers to tool 'hayali_tool',
   but it is not registered in this code."

POST /api/agents/manuel-hayali-tool-kayit/run -> HTTP 404
  "There is no agent named 'manuel-hayali-tool-kayit'."   (hic olusmadi)
```

**Seçilen davranış kaydedildi:** iki seçenekten **birincisi** — kayıt 4xx ile
reddediliyor. Agent hiç oluşmadığı için çalıştırma da 404.

Üç beklentinin üçü de tuttu: `hayali_tool` hiç çağrılmadı · hiçbir adımda 500
yok · hata metni eksik tool adını taşıyor. Güvenlik sınırı (K2) hem doğrulama
(`MT-CORE-002`) hem **kayıt** yolunda ayrı ayrı kapalı — bu case'in varlık
sebebi bu ikinci yolun ayrı kod olması.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Ortam değişimi — gerçek sağlayıcıya geçiş (MT-CORE-044'ten itibaren)

`MT-CORE-044`'ün ön koşulu `Tracon:Providers:OpenAI:ApiKey`'in **tanımlı**
olmasını şart koşuyor: `cost_usd`, `tool_invocations` ve gerçek tool çağrısı
ancak gerçek bir model çağrısıyla ölçülebilir.

Ölçüm (kalan 76 case): **23'ü** açıkça gerçek `run` istiyor, **48'i** karışık
ya da dolaylı olarak agent çalıştırıyor, yalnız **3'ü** (`073 · 087 · 088`)
yalnız `echo` istiyor. `MT-CORE-043` ikisini de kullanıyordu ve `echo` modunda
kapandı.

**Kullanıcı kararı (2026-09-16):** gerçek çağrılarla koşulur, en ucuz modelle
(örnek uygulamanın varsayılanı). `echo` isteyen üç case sırası geldiğinde ortam
geçici olarak geri alınır.

∴ `Tracon__Providers__OpenAI__ApiKey` yeniden export edildi; `echo` sağlayıcısı
bu andan itibaren **kayıtlı değildir** (`Program.cs:277-280`).

---

## MT-CORE-044 — Tool gerçekten çağrılır ve sonucu kayda geçer

**Gerçek sonuç** (gerçek OpenAI çağrısı · model `gpt-5.4-mini`)
```
POST /api/agents/support/run  {"message":"ORD-1001 siparisim nerede?",
                               "sessionId":"musteri-42"}
-> SSE akisi, finishReason "stop", "ORD-1001" yanitta 2 kez gecti ✅

 agent_name | status | model_id     | input_tokens | output_tokens | tool_cagrisi
------------+--------+--------------+--------------+---------------+--------------
 support    |      1 | gpt-5.4-mini |          402 |            25 |            1
```

`status = 1` = `RunStatus.Completed` (`src/Tracon.Abstractions/Runs/RunStatus.cs:22`).

**Tutan (dört beklentinin üçü):** yanıt `ORD-1001` taşıyor · `get_order_status`
**tam bir kez** çağrıldı · `runs.status` tamamlandı · `tool_invocations`'ta o
tool için tam bir satır var.

🚨 **Tutmayan: maliyet.** Spec `runs.cost_usd`'nin dolu ve pozitif olmasını
bekliyor; alan **boş**. İki ayrı sebep var ve ikisi de kayda geçti:

1. ⚠️ **`cost_usd` diye bir sütun yok** (doküman kusuru, düzeltildi). Gerçek
   şema: `input_cost · output_cost · cost_currency · pricing_source`.
2. 🚨 **Fiyat verisi hiçbir modelde yok** → `HATA-S1-010`. `pricing_source = 2`
   dönüyor; enum'a göre bu "fiyat hiçbir kaynakta tanımlı değil, bu yüzden
   maliyet alanları `null` — **sıfır değil**, sıfır yalan olurdu"
   (`PricingSource.cs:19-21`). Yani **mekanizma doğru davranıyor**; eksik olan
   veridir.

`Doğrulama sorgusu` gerçek sütun adlarına göre düzeltildi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

## MT-CORE-045 — Tool gerekmeyen istek tool çağırmaz

**Gerçek sonuç**
```
POST /api/agents/support/run {"message":"Merhaba","sessionId":"musteri-99"}
-> finishReason "stop", run tamamlandi

 status | input_tokens | output_tokens | input_cost | pricing_source | tool_sayisi
--------+--------------+---------------+------------+----------------+-------------
      1 |          343 |            13 |            |              2 |           0   <- "Merhaba"
      1 |          402 |            25 |            |              2 |           1   <- "ORD-1001..."
```

**Case'in asıl iddiası TUTTU:** selam isteği için `tool_invocations` satır
sayısı **0**; bir önceki sipariş isteği için **1**. Her istekte tool çağıran
bir kurulum değil — gereksiz maliyet üretilmiyor. `run` tamamlandı.

🚨 **`runs.cost_usd` yine boş** — aynı kök neden, `HATA-S1-010`. Bu case'in
kendi davranışsal iddiası doğru olduğu için bulgu 044'e bağlandı; case maliyet
beklentisi yüzünden `Kaldı`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### HATA-S1-010 — Hiçbir modelde fiyat verisi yok; her `run` maliyetsiz kaydediliyor

| | |
|---|---|
| **Önem** | Orta (yayın öncesi karar gerektirir) |
| **Bulunduğu case** | MT-CORE-044 · MT-CORE-045 |
| **Sınıf** | Katalog verisi · maliyet takibi |

**Repro**
```bash
curl -s "$APU/api/models" -H "$APB"   # her modelde:
#   "inputCostPerMillionTokens": null
#   "outputCostPerMillionTokens": null
#   "cachedInputCostPerMillionTokens": null
```

**Ölçüm — sistemik, tek bir modele özgü değil:**

| Sağlayıcı | Model sayısı | Fiyat taşıyan |
|---|---|---|
| anthropic | 3 | 0 |
| google | 3 | 0 |
| openai | 3 | 0 |
| openai-responses | 3 | 0 |
| openrouter | 1 | 0 |
| **toplam** | **13** | **0** |

∴ Örnek uygulamada **her** `run` `input_cost`/`output_cost` `null` ve
`pricing_source = NotDefined` ile kaydediliyor.

**Mekanizma doğru, veri eksik.** `PricingSource.cs:19-21` sıfır yazmayı açıkça
reddediyor ("zero would be a lie") ve `NotDefined` ile durumu dürüstçe
bildiriyor. Yani bu bir hesaplama kusuru değil; fiyat kaynağının boş olması.

**Karar gerektiren (kapanış / `nuget-danismani`):** maliyet takibi tüketiciye
dönük bir yetenek olarak duyuruluyor mu? Duyuruluyorsa ya katalog fiyatlarla
gelmeli ya da `Tracon:Pricing` yapılandırmasının **zorunlu** olduğu
dokümantasyonda ve örnek uygulamada açıkça görünmeli. Bugün sessizce boş
geçiyor: tüketici `cost` sütunlarını görüyor, doldurmuyor ve sebebini ancak
`pricing_source` enum'unu okuyarak anlıyor.

**Etki:** orta — veri kaybı yok, yanlış sayı **üretilmiyor** (en önemlisi bu);
bedeli, maliyet raporlamasının kutudan çıkmamasıdır.

---

## MT-CORE-050 — Oturum yoksa oluşturulur, varsa yüklenir

**Gerçek sonuç**
```
1) run "Benim adim Faruk." (sessionId manuel-oturum-01) -> tamam
2) GET /api/sessions -> manuel-oturum-01 VAR
3) run "Adim neydi?" (ayni sessionId) -> "Adınız Faruk."     ✅ gecmis yuklendi
4) GET /api/sessions/manuel-oturum-01 -> 4 mesaj, kronolojik:
     user      | Benim adim Faruk.
     assistant | Merhaba Faruk! Size nasıl yardımcı olabilirim?
     user      | Adim neydi?
     assistant | Adınız Faruk.
```

Dört beklentinin dördü de tuttu.

🚨 **Ölçüm tuzağı — sonraki oturumlar için:** `/run` yanıtı **SSE akışıdır** ve
metin token token gelir. Ham `grep "Faruk"` **boş döner**, çünkü kelime
`" Far"` + `"uk"` diye bölünür. Bu bir kusur değil, akışın doğasıdır. Parçaları
birleştiren yardımcı `<scratch>/sse.py` olarak yazıldı ve bu dosyanın kalan
tüm akış case'lerinde kullanıldı.

⚠️ **Doğrulama sorgusu çalışmıyor, düzeltildi (skill §1.1):** spec
`conversation_items ci JOIN sessions s ON s.id = ci.session_id` ve
`s.external_id` kullanıyor; **üçü de yok**. Gerçek şema:

| Tablo | Gerçek |
|---|---|
| `sessions` | `id` (text, dış kimliğin **kendisi**), `external_id` sütunu yok |
| `conversation_items` | `conversation_id`'ye bağlı, `session_id` sütunu yok |
| `conversations` | `session_id` sütunu **yok** |

🚨 **Daha önemlisi: oturum geçmişi SQL'den okunamaz — şifreli.**
`sessions.state` bir zarf taşıyor: `{"$apEnc":…, "kid":…, "n":…, "c":…}`.
Örnek uygulama `AddContentProtection()` çağırıyor
(`samples/Tracon.Api/Program.cs:227`) ve AES-256 anahtarı `user-secrets`'tan
geliyor (`appsettings.json:31-36`). Yani at-rest koruması **çalışıyor** ve bu
case'in doğrulaması API üzerinden yapılmalıdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-051 — Oturum silinir ve geçmiş gider

**Gerçek sonuç**
```
DELETE /api/sessions/manuel-oturum-01 -> 204  ✅ 2xx
GET    /api/sessions/manuel-oturum-01 -> 404  ✅
run "Adim neydi?" (ayni kimlik) ->
  "Adınızı bilmiyorum. Eğer isterseniz bana söyleyebilirsiniz; sonra size
   adınızla hitap ederim."                    ✅ "Faruk" YOK — gecmis gercekten gitti
GET /api/sessions/manuel-oturum-01 -> 200, 2 mesaj  ✅ yeni oturum olustu
```
Dört beklentinin dördü de tuttu. Silme gerçek: model artık adı bilmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-052 — Var olmayan oturumun silinmesi hata vermez

**Gerçek sonuç**
```
1. deneme: 404
2. deneme: 404
```
**Seçilen kod kaydedildi: 404** (204 değil). İki deneme **aynı** kodu döndürdü,
ikinci deneme farklı davranmadı, 500 yok. Silme idempotent.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-053 — İki oturum birbirini görmez

**Gerçek sonuç**
```
musteri-42'ye: "Gizli kodum MAVI-42."
musteri-99'a : "Gizli kodum neydi?"
  -> "Bunu göremem. Gizli kodunuzu öğrenmek için hesabınızın güvenlik/şifre
      sıfırlama adımlarını kullanın. ..."

MAVI-42 sizdi mi: HAYIR ✅
mesaj sayilari: musteri-42 -> 6 · musteri-99 -> 4   (ayri kumeler ✅)
```
İki beklenti de tuttu. `FIX-SESSION-01` ve `FIX-SESSION-02` ayrı kişiler olarak
davranıyor; sızma yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-054 — Aynı oturuma eşzamanlı iki çalıştırma

**Gerçek sonuç**

🚨 **İlk bakışta kayıp güncelleme gibi görünüyor, DEĞİL.** Oturumda 4 yerine 2
mesaj kalıyor ve oturum `version` 1'de duruyor — ama kaybeden istek **sessizce**
düşmüyor, SSE akışında açık bir hata olayı alıyor:

```
event: error
data: {"type":"TraconSessionConflictException",
       "message":"Another request also opened session 'manuel-yaris' at the
                  same time and saved it before us. Retry again shortly."}
```

**Dört tekrarda dördü de aynı** (tekrarlanabilirlik ölçüldü):

| Oturum | mesaj | version | çakışma hatası alan |
|---|---|---|---|
| `manuel-yaris` | 2 | 1 | 1. istek |
| `manuel-yaris-2` | 2 | 1 | 2. istek |
| `manuel-yaris-3` | 2 | 1 | 2. istek |
| `manuel-yaris-4` | 2 | 1 | 1. istek |

Her seferinde **tam bir** istek kazanıyor, **tam bir** istek açık çakışma
hatası alıyor. Hangisinin kazandığı yarışa bağlı, ki beklenen budur.

∴ Spec'in üçüncü maddesi gerçekleşti: *"Bir çakışma denetimi varsa isteklerden
biri açık bir çakışma hatası döner; bu da **kabul edilebilir**. Sessiz kayıp
kabul edilemez."* Sessiz kayıp **yok**; optimistic concurrency çalışıyor ve
mesaj çağırana ne yapacağını (`Retry again shortly`) söylüyor. Hiçbir istek
500 dönmedi.

🚨 **Yan bulgu — `HATA-S1-011`:** çakışmayla biten `run` veritabanında
**`Completed`** olarak kaydediliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-011 — Çakışmayla düşen `run`, kayıtta `Completed` görünüyor

| | |
|---|---|
| **Önem** | Orta |
| **Bulunduğu case** | MT-CORE-054 |
| **Sınıf** | Gözlemlenebilirlik · `run` durum doğruluğu |

**Repro** — aynı `sessionId` ile iki eşzamanlı `run`, sonra:
```sql
SELECT session_id, status, error_type, error_message
FROM runs WHERE session_id LIKE 'manuel-yaris%';
```

**Gözlenen** (8 satır, 4 çakışmalı koşumun tamamı):
```
 session_id     | status | error_type | error_message
----------------+--------+------------+---------------
 manuel-yaris   |      1 |            |
 manuel-yaris   |      1 |            |      <- bu run CAKISMAYLA dustu
 ...
```

İkisi de `status = 1` (`RunStatus.Completed`), `error_type` ve `error_message`
**boş**. Oysa isteklerden biri çağırana `TraconSessionConflictException`
döndürdü ve oturumuna hiçbir şey yazamadı.

**Neden önemli:** `runs` tablosu kontrol düzleminin kendi kaydıdır. Operatör
tabloya baktığında iki başarılı koşum görüyor; çakışmanın izi **yalnız**
o an akışı dinleyen istemcide kalıyor. `RunStatus.Failed` (2) tam da bunun
için var. Oturum sahibi "mesajım kayboldu" dediğinde kayıtta hiçbir kanıt yok.

`AGENTS.md`: *"Gözlemlenebilirlik işlevselliği bozmaz"* — burada tersi
geçerli: işlev doğru, **gözlemlenebilirlik** eksik.

**Sınıf taraması gerekir (kapanışta):** akış başladıktan **sonra** atılan her
istisna. Akış açıldıktan sonra `run` kaydı erkenden `Completed`'a çekiliyorsa
aynı sınıfın başka örnekleri olabilir (sağlayıcı kesintisi, guard engellemesi,
iptal). Ölçüm noktası: `run` kaydının `status` yazıldığı an ile SSE `error`
olayının üretildiği an.

**Etki:** orta — veri kaybı yok (kaybeden taraf zaten yazamadı, çağıran
bilgilendirildi); bedeli olay sonrası teşhisin imkânsızlaşması.

---

## MT-CORE-060 — Geçersiz `MaxPayloadLength` açılışı durdurur

**Gerçek sonuç**
```
1048576: KABUL
1048577: RED - TraconRunRecordingOptions.MaxPayloadLength must be between
                0 and 1048576. Actual value: 1048577.
     -1: RED - ... Actual value: -1.
```
Dört beklentinin dördü de tuttu: sınırın **tam üstü** geçerli, bir üstü ve
negatif reddedildi, red mesajı **gelen değeri** yazıyor (`Actual value`).

⚠️ **Spec'e düzeltme:** beklenen metin Türkçe alıntılanmıştı
(`0 ile 1048576 arasinda olmalidir`); sevk edilen metin İngilizce (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-061 — Script çalıştırma onaysız açılamaz

**Gerçek sonuç**
```
onay=False: RED - TraconSkillScriptOptions.Enabled requires
  PlatformIsolationAcknowledged to be true. Tracon does not provide
  operating-system isolation. Network access, file-system isolation, CPU and
  memory quotas, and privilege dropping are the host environment's
  responsibility. Enable script execution only in a container, under an
  unprivileged user, and with restricted network access.
onay=True : KABUL
varsayilan: KABUL      (Enabled'a hic dokunulmadan — dogrulama hatasi yok)
```
Üç beklentinin üçü de tuttu. Mesaj yalnız alan adını vermiyor, **sınırın ne
olduğunu** ve hangi koşullarda açılabileceğini de anlatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-062 — Agent grafiği sınırlarının varsayılanı vardır

**Gerçek sonuç**
```
MaxDepth      : 3          ✅
MaxTotalTokens: 200000     ✅
MaxTotalRuns  : 25         ✅
sifirla butce token siniri: SINIRSIZ   ✅
```
Dört beklentinin dördü de birebir tuttu. Sınırsız bırakılan bir kurulumun ilk
yanlış tanımı faturayla öğrenmesi engellenmiş.

⚠️ **Spec'e düzeltme:** `CreateBudget()` artık parametresiz değil —
`CreateBudget(TimeProvider)`. Script `error CS7036` ile derlenmiyordu;
`TimeProvider.System` verildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-063 — Hassas veri varsayılan olarak kaydedilmez

**Gerçek sonuç**
```
RecordSensitiveData      : False   ✅
EnableQuotaUsageGauge    : False   ✅
SuccessSampleRatio       : 0,1     ✅ (tr-TR ondalik ayraci — deger 0.1)
AlwaysPersistFailures    : True    ✅
Health.BackgroundInterval: YOK     ✅
```
Beş beklentinin beşi de tuttu. İstem ve yanıt metinleri span'lere varsayılan
olarak yazılmıyor; boşta duran bir kurulum sağlayıcıya düzenli istek atmıyor.
Başarısızlıklar her zaman kalıcılaştırılıyor — örnekleme yalnız başarıya
uygulanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-064 — Yapılandırmadan gelen negatif fiyat reddedilir

**Gerçek sonuç**
```
beklenen red: TraconPricingOptions: price for 'echo:echo-1' cannot be negative.
```
Üç beklentinin üçü de tuttu: çıktı `beklenen red:` ile başladı · mesaj
sağlayıcı **ve** modeli (`echo:echo-1`) adlandırıyor ·
`🚨 negatif fiyat KABUL EDILDI` görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-065 — `Pricing` altındaki rezerve anahtarlar sağlayıcı sayılmaz

**Gerçek sonuç**

⚠️ **Spec'in script'i iki yerde yanlıştı; ikisi de düzeltildi.**

**1) Bölüm `AddTracon`'a verilmiyordu.** Elle (AOT) bağlayıcı
`AddTracon(services, IConfiguration section)` içinden koşuyor
(`TraconServiceCollectionExtensions.cs:64 → Bind → BindPricing:198`).
Spec `AddTracon()`'u **argümansız** çağırıp ayrıca
`services.Configure<TraconOptions>(section)` yapıyordu; o yol yansıma tabanlı
binder'ı kullanır ve `Providers` sözlüğünü **dolduramaz**:

```
A) spec'teki hali : Currency=USD · Saglayici sayisi=0 · Ses=elevenlabs
B) AddTracon(section): Saglayici sayisi=1 · Saglayicilar=echo
```

**2) Fiyat anahtarları C# özellik adıyla yazılmıştı.** Yapılandırmada geçerli
adlar `Input` · `Output` · `CachedInput`'tur
(`TraconServiceCollectionExtensions.Binding.Models.cs:51-53`), C# özellik adı
`InputCostPerMillionTokens` **değil**.

Doğru script ile üç beklentinin üçü de tuttu:
```
Currency          : USD          ✅
Saglayici sayisi  : 1            ✅
Saglayicilar      : echo         ✅ Currency ve Voice listede YOK
Ses saglayicilari : elevenlabs   ✅ Voice kendi sozlugune bagli
echo/echo-1 Input : 0,25         ✅ deger gercekten baglandi
```

💡 **Kodun kendisi bu case'i adıyla anıyor** (`Binding.Models.cs:62-68`):
yanlış anahtar adıyla yazılan bir fiyat kaydı **sessizce düşürülmüyor**, boş
bir `ModelPriceOverride` olarak sözlüğe giriyor ve doğrulayıcı açılışta
reddediyor. Yanlış anahtarla denendiğinde tam olarak bu gözlendi (aşağıda) —
K-034'ün "sessizce yok sayma" yasağı çalışıyor.

🚨 **O red mesajı bir kusur ortaya çıkardı → `HATA-S1-012`.**

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-012 — Sevk edilen üç hata mesajında yarım kalmış Türkçe çeviri var

| | |
|---|---|
| **Önem** | Orta (K-228 dil sınırı ihlali · tüketiciye görünür) |
| **Bulunduğu case** | MT-CORE-065 |
| **Sınıf** | Dil sınırı · `SourceLanguageTests` kör noktası |

**Repro** — `Tracon:Pricing` altına anahtar adı yanlış bir fiyat yaz:
```json
{ "Tracon": { "Pricing": { "echo": { "echo-1": { "InputCostPerMillionTokens": 0.25 } } } } }
```
Açılışta:
```
OptionsValidationException: TraconPricingOptions: 'echo:echo-1'
  ne 'Input' ne 'Output' contains neither value. Check the key name.
                  ^^^^^^^^^^^^^^^^^^^^ Turkce "ne ... ne ..." yapisi
```

Cümle iki dilin ortasında kalmış: Türkçe *"ne X ne Y"* kalıbı İngilizce
*"contains neither value"* ile birleştirilmiş. Tüketici bozuk İngilizce
okuyor.

**Kaynak — `src/Tracon.Core/TraconOptionsValidator.cs`, iki satır:**

| Satır | Metin |
|---|---|
| 292 | `'{providerName}:{modelName}' ne 'Input' ne 'Output' ` |
| 312 | `'Voice:{providerName}:{modelName}' ne '{PerMillionCharacters}' ne '{PerMinute}' ` |

(`:342` aynı bloktaki üçüncü mesajdır ve **doğru** İngilizce yazılmış — düzeltme
sırasında atlanan iki satır bunlar.)

🚨 **Kapı bunu neden yakalamadı:** `SourceLanguageTests` iki kaynakla tarıyor —
Türkçe harfler (`çğıöşü`) ve Türkçe kelime listesi. `ne` ikisine de girmiyor:
Türkçe harf taşımıyor ve kelime listesi **"every two-letter word"**'ü
*bilinçli olarak* dışlıyor (`SourceLanguageTests.cs:138-141`), çünkü iki
harfli Türkçe kelimeler İngilizce kelimelerle ve tanımlayıcılarla çakışıyor.
`TraconOptionsValidator.cs` taban çizgisinde (`source-language-baseline.txt`)
**yok** — yani bu borç kayıtlı değil, kapıdan **kaçmış**.

**Kapanışta yapılacak:** iki satır düzeltilir (ör. `neither 'Input' nor
'Output' is set`). Kapının kör noktası **ayrı** bir karardır: iki harfli
kelimeleri listeye almak yanlış pozitif üretir; alternatif, `ne X ne Y` gibi
Türkçe **kalıpları** (tek kelimeleri değil) arayan bir desen eklemektir.

**Sınıf taraması gerekir:** aynı kalıbın başka örnekleri. `grep -rn " ne '" src/`
bugün **2** satır buluyor, ikisi de bu dosyada. Diğer iki-harfli Türkçe
kelimeler (`ve`, `bu`, `da`, `de`, `o`, `mi`) ayrıca taranmalı.

**Etki:** orta — işlev doğru, doğrulama doğru reddediyor; bedeli paketin
profesyonel görünümü ve K-228'in ihlali.

---

## MT-CORE-070 — Bellek içi depolar veritabanı olmadan çalışır

**Gerçek sonuç**
```
dotnet run -c Release -> cikis 0
host ayakta
ulasilan istek sayisi (baslangic): 0

log taramasi: "connection refused|could not connect|Npgsql|socket" -> 0 eslesme
```
Dört beklentinin dördü de tuttu: `TraconTestHost` bağlantı dizesi olmadan
ayağa kalktı, veritabanı denemesi yok, ağ isteği yok, süreç sıfırla bitti.
`FakeModelProvider` hiç çağrılmadı (`Requests.Count = 0`) — host açılışta
modele gitmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-071 — `FakeModelProvider` kuyruğu bir kez tüketilir

**Gerçek sonuç**
```
kuyruk kuruldu
istek sayisi: 0
```
Üç beklentinin üçü de tuttu: `RespondsWith("BIRINCI","IKINCI")` ve
`EchoesUserMessage()` **zincirlendi**, kurulum hata vermedi, istisna atılmadı.

📋 **Kapsam notu:** bu script kuyruğu yalnız **kuruyor**, tüketmiyor
(`Requests.Count = 0`). "Kuyruk tükendikten sonra yankıya düşer" iddiası bir
`run` gerektirir ve bu case'in script'i onu yapmıyor; iddianın ölçülen kısmı
kurulum ve zincirlenebilirliktir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-072 — Kimlikler zaman sıralı UUIDv7'dir

**Gerçek sonuç**
```
sirali: True                    ✅
damga farki (sn): 0,0           ✅ 1 sn'den kucuk
beklenen red: The identifier is not a UUID version 7 value. (Parameter 'id')
🚨 v4 KABUL EDILDI satiri: gorunmedi   ✅
```
Dört beklentinin dördü de tuttu. Beş kimlik ordinal sıralamada artan —
veritabanı indeksi için önemli olan özellik.

⚠️ **Spec'e düzeltme:** beklenen metin Türkçe alıntılanmıştı
(`surum 7 degeri degil`); sevk edilen metin İngilizce (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-073 — Enum'lar JSON'da ad olarak yazılır

**Gerçek sonuç**
```
"origin":"Code"
"origin":"Database"        ✅ ikisi de tirnak icinde, AD olarak
"severity":"Error"         ✅ ad olarak

sayi olarak enum taramasi:
  grep -oE '"(origin|severity|status|effect)":[0-9]+'  -> HIC ESLESME YOK ✅
```
Üç beklentinin üçü de tuttu.

**Sapma:** case `provider:"echo"` kullanıyor; oturum o sırada gerçek sağlayıcı
modundaydı ve `echo` kayıtlı değildi, bu yüzden doğrulama isteği
`provider:"openai"` ile yapıldı. Ölçülen şey (enum'un ad olarak yazılması)
sağlayıcıdan bağımsızdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-074 — Uygulama yeniden başlatıldığında kod agent'ları geri gelir

**Gerçek sonuç**
```
                       oncesi   sonrasi
toplam agent            18        18      ✅ ayni
origin=Code             14        14      ✅ ayni
origin=Database          4         4      ✅
manuel-surum version     2         2      ✅

acilis logu: "applied N migration" satiri YOK (sema zaten guncel) ✅
             "Now listening" 2 kez — host tam acildi
```
Dört beklentinin dördü de tuttu. Kod agent'ları derleme zamanından geliyor,
veritabanı agent'ları sürümleriyle birlikte kalıcı; migration'lar ikinci
açılışta yeniden uygulanmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Kültür bloğu (075–080) — ortak kurulum

`kod-agent` diye bir agent **yok** (404). `MT-CORE-075` için kültür sözlüğü boş
bir kod agent'ı olarak `support` kullanıldı.

`MT-CORE-076`'nın agent'ı oluşturuldu (`manuel-kultur`, 201):
```json
{ "instructions": "Answer briefly and ENTIRELY IN ENGLISH.",
  "instructionsByCulture": { "tr": "Kisa cevap ver ve TAMAMEN TURKCE yaz." },
  "model": { "provider": "openai", "model": "gpt-5.4-mini" } }
```

---

## MT-CORE-075 — Kültür sözlüğü boş agent'ta `culture` verilse de davranış değişmez

**Gerçek sonuç**
```
POST support/run {"message":"merhaba","culture":"tr"} -> HTTP 200
  "Merhaba! Size nasıl yardımcı olabilirim?"
```
`culture` alanı hiç gönderilmemiş gibi davrandı: hata yok, farklı davranış yok,
`Instructions` kullanıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-076 — Eşleşen kültür kendi talimatını seçer

**Gerçek sonuç**
```
culture:"tr" -> HTTP 200
  "İyiyim, teşekkürler. Sen nasılsın?"
```
👁 **Gözle kontrol:** yanıt açıkça **Türkçe** ve **kısa** — `tr` talimatına
uyuyor. Varsayılan İngilizce talimat devreye girmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-077 — Bölge alt etiketi ebeveynine düşer: `tr-TR` → `tr`

**Gerçek sonuç**
```
culture:"tr-TR" -> HTTP 200
  "İyiyim, teşekkürler. Sen nasılsın?"
```
`MT-CORE-076` ile **aynı** davranış: `tr-TR` sözlükte yok, `tr` anahtarına
düştü. Yanıt Türkçe.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-078 — Eşleşmeyen kültür varsayılana düşer, hata VERMEZ

**Gerçek sonuç**
```
culture:"de" -> HTTP 200
  "I’m doing well, thank you. How are you?"
```
`de` sözlükte yok ve istek **reddedilmedi**; yanıt varsayılan İngilizce
talimata göre üretildi. K1 (sessiz geri düşüş, patlama değil) ayakta.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-079 — `Accept-Language` başlığı talimatı DEĞİŞTİRMEZ

**Gerçek sonuç**
```
-H "Accept-Language: tr", govdede culture YOK -> HTTP 200
  "I’m good, thanks. How are you?"
```
Başlık **yok sayıldı**; yanıt varsayılan İngilizce talimata göre üretildi.
K-232'nin çizgisi korunuyor: sunucu içeriği ambient bir tarayıcı başlığından
beslenmiyor.

📋 **Koşum notu (ürün değil, ölçüm):** ilk denemede HTTP 400 göründü. Sebep
koşum tarafındaki shell yardımcı fonksiyonunun ek başlığı bölmesiydi
(`-H Accept-Language:` + `tr` iki ayrı argüman oluyordu). Düz `curl` ile
tekrarlandığında 200 döndü. Ürün kusuru **değildir**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-080 — Arka arkaya farklı kültürlerle `run` — önbellek yanlış dili TUTMAZ

**Gerçek sonuç**
```
1) culture:"tr"     -> "İyiyim, teşekkürler. Sen nasılsın?"      TR
2) culture YOK      -> "I’m good, thanks. How are you?"          EN
3) culture:"tr"     -> "İyiyim, teşekkür ederim. Sen nasılsın?"  TR
```
İki beklenti de tuttu ve **ters yön de ölçüldü** (spec yalnız tr→en istiyordu;
en→tr de denendi). `CompiledAgentCache` anahtarına kültür gerçekten eklenmiş:
hiçbir yönde sızma yok. Anahtar kültürsüz olsaydı 2. çağrı Türkçe, 3. çağrı
İngilizce kalırdı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-081 — 👤 Agent editöründe dil sekmesi; sürüm diff'i iki dili de gösterir

**Gerçek sonuç** (Playwright · `http://localhost:5081/tracon`)

**1) Panel** — `/agents/manuel-kultur/edit`, "Instructions" altında
"Instructions by culture":
```
[textbox placeholder="tr" değer="tr"]  [Remove culture]
[textbox: "Kisa cevap ver ve TAMAMEN TURKCE yaz."]
[Add culture]
yardim metni: "A culture tag such as \"en\" or \"tr\". A run whose requested
               culture matches none of these uses the instructions above."
```
Her kültür satırı için **bir dil kodu alanı ve bir metin alanı** var ✅

**2) Satır eklendi ve kaydedildi:** `de` / `Kurz antworten.` →
"Save new version" → agent detayına dönüldü, rozet **`db · v2`**.
Tanım JSON'u: `"instructionsByCulture": { "de": "Kurz antworten.",
"tr": "Kisa cevap ver ve TAMAMEN TURKCE yaz." }` ✅ yeni sürüm oluştu

**3) Sürüm karşılaştırma** — v1 ve v2 seçildi:
```
Instructions        1 | 1 | Answer briefly and ENTIRELY IN ENGLISH.   (degismedi)
Instructions (de)   1 | + | Kurz antworten.                           ← EKLENEN
Instructions (tr)   1 | 1 | ...                                       (degismedi)
```
`de` için **ayrı bir "Instructions (de)" bölümü** belirdi ve eklenen metin
`+` ile vurgulandı ✅

**4) Dil değişimi (EN → TR)** — başlıktaki `en` düğmesi:

| İngilizce | Türkçe |
|---|---|
| Summary | Özet |
| Instructions | Talimatlar |
| **Instructions (de)** | **Talimatlar (de)** |
| Version history | Sürüm geçmişi |
| Definition / Copy | Tanım / Kopyala |
| Edit / Delete / Roll back | Düzenle / Sil / Geri al |
| "Comparing v1 → v2" | "v1 → v2 karşılaştırılıyor." |
| tooltip: "Stored definition, version 2." | "Saklanan tanım, sürüm 2." |

Panel etiketleri, tablo başlıkları **ve tooltip'ler** çevrildi ✅

🚨 **Doğru olan ayrım:** agent'ın **içeriği** çevrilmedi —
`"Answer briefly and ENTIRELY IN ENGLISH."` her iki dilde de aynen duruyor.
K-228'in çizgisi tam burada: çeviri konsol yüzeyine ait, kullanıcının verisine
değil.

**Konsol:** her sayfada `browser_console_messages` kontrol edildi. Tek tekrar
eden hata `HATA-S1-004`'ün CSP ihlali (oturum 2'de kaydedilmişti) —
**bağımsız olarak doğrulandı**:
```
Executing inline script violates the following Content Security Policy
directive 'script-src 'self''. ... The action has been blocked.
@ http://localhost:5081/tracon:41
```
📋 **Yeni bilgi:** bu hata **ölümcül değil** — konsol tümüyle çalışıyor
(token girişi, gezinme, düzenleme, kaydetme, diff, dil değişimi hepsi çalıştı).
`HATA-S1-004`'ün etkisi bu turda ilk kez sınırlandırılmış oldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Parametre bloğu (082–086) — ortak kurulum

`PARAM_AGENT` = `manuel-param` (201):
```json
{ "name": "manuel-param",
  "instructions": "Musteri: {{musteri}}. Kisa cevap ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "parameters": [ { "name": "musteri", "kind": "Text", "required": true } ] }
```

⚠️ **Spec'te eksik alan:** `parameters` öğesi `kind` **zorunlu** alanını
taşımalıdır (`AgentParameter.cs:28`, `AgentParameterKind`: `Text · Number ·
Boolean`). `kind` olmadan istek `400 "missing required properties including:
'kind'"` döner. Ön koşul düzeltildi.

---

## MT-CORE-082 — Zorunlu parametre eksikken `run` başlamaz; ad hatada geçer

**Gerçek sonuç**
```
POST .../manuel-param/run       -> HTTP 400
POST .../manuel-param/estimate  -> HTTP 400

Iki govde de BIREBIR ayni bicimde:
{ "title": "Invalid run parameters",
  "detail": "Missing required parameter 'musteri'.",
  "missingParameters": ["musteri"],
  "unknownParameters": [],
  "tooLongParameters": [] }
```
Beklentilerin hepsi tuttu: iki uç da 400 · `missingParameters` `"musteri"`
içeriyor · gövde biçimi **aynı**, yani tek bir doğrulayıcı
(`AgentParameterValidator`) ikisini de besliyor. Üç dizi alanı da her yanıtta
mevcut — tüketici hepsini tek şekilde okuyabiliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-083 — Fazladan parametre sessizce yutulmaz

**Gerçek sonuç**
```
parameters: {"musteri":"Acme","musteriii":"oops"} -> HTTP 400
  "detail": "Unknown parameter 'musteriii'.",
  "unknownParameters": ["musteriii"]
```
Koşu başlamadı, model çağrılmadı. Yazım hatası taşıyan bir parametre adı
üretimde sessizce kaybolmuyor — `musteri` doğru olsa bile istek tümden
reddediliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-084 — Değer JSON yapısını bozmaz

**Gerçek sonuç**
```
agent: manuel-json · instructions: "Ornek: {\"customer\": \"{{musteri}}\"}. ..."
run parameters: {"musteri": "a\"b\\c"}   (tirnak + ters bolu)
  -> HTTP 200 · yanit "Selam!"  · saglayici ayristirma hatasi YOK
(iki kez kosuldu, ikisi de basarili)
```

**Case'in asıl iddiası TUTTU:** tırnak ve ters bölü taşıyan bir değer, JSON
örneği içeren bir talimata gömüldüğünde sağlayıcı isteği bozulmadı. Kaçış
çalışmasaydı istek hatalı JSON olur ve koşu düşerdi.

📋 **İkinci adım bu ortamda doğrulanamadı.** "Kayıtlı girdi metninde `a"b\c`
kaçırılmış biçimde görünür" için talimat metninin bir yerde okunabilir olması
gerekiyor; bugün **hiçbir yerde yok**:

| Kaynak | Sonuç |
|---|---|
| `GET /api/runs/{id}` | talimat alanı taşımıyor |
| `mt_s1.run_events.payload` | 5 olayın **beşinde de** `NULL` |
| aynısı `Tracon__Observability__RecordSensitiveData=true` ile | yine `NULL` |
| trace | OTLP collector yapılandırılmamış |

`MT-CORE-063` zaten istem/yanıt metinlerinin **span'lere** yazıldığını söylüyor;
`run_events` onları hiç taşımıyor. Yani bu adım bir trace collector ister.
`Beklenen sonuç`'a bu ön koşul eklendi (skill §1.1).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-085 — Paylaşılan talimat bloğu prepend edilir; bloğa referans veren blok reddedilir

**Gerçek sonuç**
```
1) house-rules olusturuldu (201)
2) shared-e2e olusturuldu (201) · sharedInstructionsName: "house-rules"
   run -> HTTP 200, agent fatura sorusunu yanitladi
   => derleme BASARILI (cozumleme derlemeyi tetikler)
3) house-rules'a sharedInstructionsName:"shared-e2e" yazilmaya calisildi
   -> HTTP 400 "Definition invalid"
   "Agent 'house-rules' references shared instructions 'shared-e2e', which
    itself references 'house-rules'. A shared instructions block cannot
    reference another block."
```

Adım 3 birebir tuttu ve mesaj **zincirin tamamını** gösteriyor — tek atlama
kuralı adıyla anlatılıyor.

📋 **Adım 2'nin metin iddiası doğrulanamadı** (`MT-CORE-084` ile aynı sebep):
birleşik talimatın `"Her zaman kaynağını belirt.\n\nFatura sorularını
yanıtla."` biçiminde olduğu ancak trace'ten görülebilir. API `definition`'da
iki alan **ayrı** duruyor (`instructions` + `sharedInstructionsName`),
`factoryInstructions` `null`. Derlemenin başarılı olduğu koşumla kanıtlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-086 — Aşırı uzun parametre değeri koşuyu düşürür

**Gerçek sonuç**
```
musteri = 5000 karakter -> HTTP 400
  "detail": "Parameter 'musteri' exceeds the maximum value length.",
  "tooLongParameters": ["musteri"]
```
Beklenen `detail` metni ve `tooLongParameters` dizisi **birebir** tuttu.
Koşu başlamadı, model çağrılmadı. 2026-08-22'nin canlı doğrulaması bugün de
geçerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Özel agent kaynağı bloğu (087–089) — ortak kurulum

🚨 **Spec'in ön koşulu kod donmasını çiğniyor:** `samples/Tracon.Api/Program.cs`'e
geçici kayıt eklemeyi istiyor. Bu turda `samples/` **donuktur** (kural 1).

**Çözüm — repo'ya dokunmadan kendi tüketici host'um kuruldu**
(`~/tracon-manuel/ohost`, port **5085**). İzlek A'nın yaptığı şeyin aynısı:

- `Tracon.AspNetCore` · `Tracon.UI` · `Tracon.Testing` (yerel feed,
  `0.0.0-preview.0.789`)
- `samples/Tracon.Samples.CustomAgentSource`'un **üç `.cs` dosyası kopyalandı**
  (paketlenmiş bir sürümü yok; örnek kod zaten kopyalanmak için var)
- `agents/greeter.json` = spec'in verdiği içerik
- `.AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage())` —
  `echo` yalın bir host'ta yok, sağlayıcı kaydı şart
- `app.MapTracon("/tracon", o => o.AuthToken = "...")` — 🚨 `AuthToken`
  yapılandırmadan **otomatik okunmaz**, host'un kendisi verir
  (`TraconEndpointOptions.cs:65`); örnek uygulama bunu `Program.cs:944`'te
  elle yapıyor

`git status` repo'da boş kaldı; donma bozulmadı.

---

## MT-CORE-087 — Özel `IAgentSource` ajanı `Custom` origin ile listelenir

**Gerçek sonuç**

**Adım 1 — API:**
```
GET /api/agents        -> greeter | origin: Custom | sourceName: json-file
GET /api/agents/greeter -> isEditable: false
                           descriptor.origin: Custom
                           descriptor.sourceName: json-file
```
Üç beklenti de tuttu.

⚠️ **Spec'e düzeltme:** `isEditable` **liste** ucunda yoktur, **detay** ucunda
vardır. Liste öğesinin alanları: `name · origin · sourceName · model ·
version · displayName · description · toolNames · skillNames ·
callableAgentNames · usesHarness · updatedAt`. Case'in `grep -A3 '"name":
"greeter"'` komutu `isEditable`'ı hiçbir zaman göremez.

**Adım 2 — konsol** (`http://localhost:5085/tracon/agents/greeter`):
```
rozet     : "json-file"  · tooltip: "Provided by source \"json-file\"."   ✅
Edit baglantisi: YOK (manuel-kultur'da vardi)                              ✅
salt-okunur gorunum                                                        ✅
```
İki beklenti de tuttu.

🚨 **Ama açıklama metni yanlış → `HATA-S1-013`:** sayfadaki bilgi kutusu
*"This agent is declared in code..."* diyor. `greeter` **kodda tanımlı
değildir**; bir JSON dosyasından gelir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-013 — `Custom` kaynaklı agent'a "bu agent kodda tanımlı" deniyor

| | |
|---|---|
| **Önem** | Düşük |
| **Bulunduğu case** | MT-CORE-087 (adım 2) |
| **Sınıf** | Arayüz metni · kod/Custom kaynak ayrımı |

**Gözlenen:** `greeter` (origin `Custom`, source `json-file`) detay sayfasında:

> *"This agent is declared in code. A code definition is validated at compile
> time and cannot be changed from the console — **edit the application source
> instead**."*

**Kök neden** — `src/Tracon.UI/frontend/src/screens/agent-detail.tsx:139`:
```tsx
{!isEditable && (
  <div ...>{t('agentDetail.codeNotice')}</div>
)}
```
Koşul yalnız `!isEditable`'a bakıyor; `origin` **hiç okunmuyor**. Sözlükte de
tek metin var (`locales/en/agents.ts:51`) — `Custom` kaynak için karşılığı yok.

**Neden önemli:** yönlendirme de yanlış. Kullanıcı "uygulama kaynağını düzenle"
diyor; doğru eylem `agents/greeter.json` dosyasını düzenlemektir. Sayfa
gerekli bilgiyi **zaten gösteriyor** (rozet `json-file`, tooltip "Provided by
source"), yalnız bilgi kutusu onu kullanmıyor.

**`HATA-S1-009` ile aynı sınıf:** "veritabanında yok" → "kodda" varsayımı.
Kapanışta ikisi birlikte değerlendirilmeli.

---

## MT-CORE-088 — `Custom` kaynağa ait ada `PUT`/`POST` çakışması `409` döner

**Gerçek sonuç**
```
PUT  /api/agents/greeter -> HTTP 409 · title "Custom-source agent cannot be modified"
POST /api/agents         -> HTTP 409 · title "Agent name in use"

ikisinin de detail'i AYNI:
 "'greeter' belongs to the 'json-file' agent source and cannot be changed
  from the management API."
```
İki beklenti de tuttu: ikisi de 409 · `detail` **kaynağın adını** taşıyor ·
404 değil, doğru çakışma anlatılıyor. Başlıklar iki uç için ayrı ayrı
anlamlandırılmış, gerekçe ortak.

📋 **Karşıtlık:** `MT-CORE-034`'te (`HATA-S1-009`) aynı proje, var olan bir
agent için "böyle bir agent yok" diyordu. Burada doğrusu yapılıyor — yani
desen biliniyor, `versions` ucunda uygulanmamış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-089 — `Custom` kaynaktaki agent gerçek bir `run` tamamlar

**Gerçek sonuç**
```
POST /api/agents/greeter/run {"message":"selam"} -> yanit "Echo: selam"

run kaydi:
  status       : Completed
  modelId      : "echo-1"     ✅ bos degil
  modelProvider: "echo"       ✅ bos degil
  agentVersion : 0
```
Koşu tamamlandı ve kayıt **doğru atıf** taşıyor: kaynağın kendi `ListAsync`'inden
gelen dondurulmuş descriptor kullanılmış. Uydurma `Origin=Code` / `Model=null`
**yok**.

⚠️ **Spec'e düzeltme:** alan adları `modelId` ve `modelProvider`'dır
(`model`/`provider` değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-090 — Bozuk bir kaynak varken `GET /api/agents` sağlıklı kalır

**Gerçek sonuç**

🚨 **Spec'in "bozuk kaynak" tarifi bu örnek için YANLIŞ.** Case var olmayan bir
dizin öneriyor ("`ListAsync`'i her çağrıda istisna fırlatsın diye"), ama
`JsonFileAgentSource.ReadAllAsync` (`:142-145`) bunu **açıkça** ele alıyor:

```csharp
if (!Directory.Exists(_directory)) { yield break; }
```

Var olmayan dizin **boş** kaynak üretir, bozuk kaynak değil. Ölçüldü: log
tertemiz, hiçbir hata yok.

**Gerçekten bozmak için** dizine ayrıştırılamayan bir JSON kondu
(`agents/aaa-broken.json` = `{ bu gecerli JSON degil`); ad sıralamada
`greeter.json`'dan **önce** gelsin diye `aaa-` öneki verildi.

Sonra, host'a bir de kod agent'ı eklenerek (yerleşik kaynağın görünürlüğünü
kanıtlamak için) ölçüldü:

```
GET /api/agents -> HTTP 200                                  ✅ fail-open
agent sayisi: 1 | [('kod-agent', 'Code')]                    ✅ yerlesik kaynak GORUNUYOR
greeter (bozuk kaynak) listede YOK                           ✅

sunucu logu:
  fail: Tracon.CompositeAgentCatalog[0]
        Agent source 'json-file' failed during list.          ✅ kaynak ADIYLA
```

Üç beklentinin üçü de tuttu. Bozuk bir uzantı bütün katalogu düşürmüyor;
hata yutulmuyor da — loga kaynağın adıyla yazılıyor.

`Ön koşul` gerçek bozma yöntemine göre düzeltildi (skill §1.1).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-091 — Bozuk kaynağa ait adla `run` denemesi ham hata metnini sızdırmaz

**Gerçek sonuç**
```
POST /api/agents/greeter/run -> HTTP 400
{ "title": "Agent compilation failed",
  "detail": "Agent source 'json-file' failed during resolve (agent_source_failed)." }
```

Beklenen `detail` metni **birebir** tuttu. Gövdede **yok**: dosya sistemi hata
metni · dizin yolu · JSON ayrıştırma ayrıntısı · .NET exception stack'i ·
dosya adı. Kaynak adı ve makine okunur bir kod (`agent_source_failed`)
dışında hiçbir iç ayrıntı sızmıyor.

**`MT-CORE-090` ile birlikte okunduğunda:** liste **fail-open** (bozuk kaynak
atlanır, katalog ayakta), çözümleme **fail-closed** (isim bilinen bir kaynağa
aitse hata döner, sessizce "yok" denmez). İkisi doğru yönde.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-092 — `GET /api/diagnostics` kayıtlı kaynakları önceliğe göre listeler

**Gerçek sonuç**
```json
"agentSources": [
  { "name": "code",      "priority": 0,   "implementation": "Tracon.CodeAgentSource" },
  { "name": "database",  "priority": 100, "implementation": "Tracon.DefinitionStoreAgentSource" },
  { "name": "json-file", "priority": 101, "implementation": "Tracon.Samples.CustomAgentSource.JsonFileAgentSource" }
]
```
Üç girdi, **doğru öncelik sırasıyla** — case'in asıl iddiası bu ve tuttu.
`implementation` alanı da dolu, yani hangi tipin hangi adı sağladığı görünüyor.

⚠️ **Spec'te sayı yanlış:** `json-file` önceliği **101**'dir, 200 değil.
Örnek kaynağın kendi beyanı: `Priority => AgentSourcePriority.Database + 1`
(`JsonFileAgentSource.cs:91`). Düzeltildi.

**Sapma:** `EnableDiagnosticsEndpoint = true` host'un `MapTracon` seçeneğinden
verildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-093 — Aynı `Name`'e sahip iki kaynak host'u başlatmaz

**Gerçek sonuç**

📋 **İlk deneme yanlış kurulumdu:** `AddAgentSource<JsonFileAgentSource>()` **iki
kez** çağrıldı ve host sorunsuz açıldı (HTTP 200). Bu bir kusur değil —
`TraconBuilder.cs:142` `TryAddEnumerable` kullanıyor ve aynı **tipi**
tekilleştiriyor. Yani iki kayıt tek kaynağa iniyor, ad çakışması hiç oluşmuyor.

Gerçek koşul için aynı adı döndüren **ikinci bir tip** yazıldı
(`IkinciJsonFileSource`, `Name => "json-file"`):

```
host acilmadi: curl -> HTTP 000 (baglanti reddedildi)          ✅

Unhandled exception. Tracon.TraconAgentSourceException:
  Agent sources 'Tracon.Samples.CustomAgentSource.JsonFileAgentSource' and
  'IkinciJsonFileSource' share the name 'json-file'.            ✅
```

İki beklenti de tuttu: uygulama **başlamadı** · mesaj **iki kaynağın da
tipini** ve paylaşılan adı taşıyor. Hata açılışta, ilk istekte değil.

`Ön koşul`a "iki farklı TİP gerekir" notu eklendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-094 — Yavaş bir kaynak başlangıcı geciktirmez

**Gerçek sonuç**

`ListAsync`'inde `Task.Delay(3 sn)` olan bir `YavasSource` kaydedildi.

```
host baslangici -> ilk saglikli yanit :  1,51 sn     ✅ GECIKMEDI
ilk GET /api/agents cagrisi           :  3,15 sn     <- 3 sn'lik gecikme BURADA
```

🚨 **Kanıt tam olarak bu iki sayının farkıdır.** Startup doğrulaması
`ListAsync`'i çağırsaydı açılış ≥3 sn sürerdi; 1,51 sn sürdü (tipik soğuk
başlangıç). Gecikme **ilk listeleme isteğinde**, yani talep anında ödendi.
∴ Açılışta yalnız `Name`/`Priority` okunuyor, I/O yapılmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-095 — Kiracıya duyarlı özel kaynak

**Gerçek sonuç**
Koşulmadı. Spec'in kendisi bunu **👤 insan gerekir** olarak işaretliyor: bu
depoda kiracıya duyarlı bir örnek `IAgentSource` **yok**, dolayısıyla kiracı
A/B listelerinin ayrıştığını gösterecek bir kurulum kurulamıyor.

Bu bir kusur değil, **kapsam boşluğu**dur. Otomatik karşılığı mevcut ve
kapsıyor: `TenantAwareAgentSourceContract`
(`tests/Tracon.Core.UnitTests/Catalog/BuiltInAgentSourceContractTests.cs`,
`DefinitionStoreTenantAwareAgentSourceContractTests`).

Bu turda yazılan `ohost` kurulumuna kiracıya duyarlı bir kaynak **eklenebilirdi**,
ama o zaman case kendi yazdığım bir kaynağı ölçerdi, ürünü değil — sözleşme
testinin zaten yaptığı şeyi tekrarlardı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-096 — `AddAgentSource` kayıt yüzeyi: generic ve factory aşırı yüklemeleri tek instance üretir

**Gerçek sonuç**
```
dotnet build samples/Tracon.Samples.CustomAgentSource.Tests -c Release -> 0
dotnet test  ... --no-build -> cikis 0

  total: 15 · succeeded: 15 · failed: 0 · skipped: 0     ✅ 15/15

TryAddEnumerable sayimi (sample kodunun 6 dosyasi):  hepsinde 0   ✅
```
İki beklenti de tuttu. `--no-build` **öncesinde derleme koşuldu** (skill §6'nın
uyarısı: kırık build'de `--no-build` eski ikiliyi koşar ve yanlış yeşil verir).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-097 — Complex tool sonucu canonical JSON ve output limiti taşır

**Gerçek sonuç**
```
dotnet test samples/Tracon.Samples.CustomTool.Tests -c Release --no-build -> 0
  total: 18 · succeeded: 18 · failed: 0 · skipped: 0
```
`MaxOutputBytes = 768` kaynakta doğrulandı
(`samples/Tracon.Samples.CustomTool/OrderPreviewTools.cs:13`), `preview_order`
tool'u `OrderPreviewJsonContext` ile kayıtlı (`:11-14`).

Alan değerlerinin modele gerçekten JSON olarak geçtiğini gösteren test:
`A_generated_complex_results_field_value_is_seen_by_the_content_guard` —
guard **alan değerini** görebiliyorsa modele giden metin CLR tip adı değil,
serileştirilmiş içeriktir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-098 — Doğrulanmamış tool registry startup'ta reddedilir

**Gerçek sonuç** (`ohost`'a kendi `IToolRegistry` uygulamam kaydedildi)
```
1) Varsayilan kurulum -> host ACILMADI (curl HTTP 000)
   Unhandled exception. Tracon.TraconException: IToolRegistry was replaced.
   This removes the Authorizing, Timeout, ApprovalRequired, and Truncating
   wrappers. Use AddTool APIs, or explicitly set
   Tools.AllowUnverifiedToolRegistry to true.

2) Tracon__Tools__AllowUnverifiedToolRegistry=true -> host ACILDI
   GET /api/agents -> 200
   warn: Tracon.ToolRegistrationValidationService[0]
         An unverified IToolRegistry is active. Authorizing, Timeout,
         ApprovalRequired, and Truncating wrappers are not guaranteed.
```

İki beklenti de tuttu. Mesaj **hangi dört sarmalayıcının kaybolduğunu**
adıyla sayıyor ve çıkış yolunu veriyor; opt-in yolu sessiz değil, her açılışta
uyarı yazıyor. Fail-closed varsayılan + adlandırılmış opt-in deseni
(`MT-CORE-061` ile aynı sınıf).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-099 — Tool hatası foreign exception ayrıntısını sızdırmaz

**Gerçek sonuç**

`ohost`'a bağlantı dizesi taşıyan yabancı bir istisna atan tool eklendi:
```csharp
throw new SahteVeritabaniException(
  "Host=gizli-sunucu.local;Port=5432;Database=musteri;Username=admin;Password=COK-GIZLI-PAROLA");
```
Model sağlayıcısı `CallsTool("Patla", ...)` ile tool'u gerçekten çağırttı.

**🚨 Sızıntı taraması — iki yüzeyde de temiz:**
```
SSE akisi        : "COK-GIZLI|gizli-sunucu|Password=" -> 0 eslesme ✅
run olaylari     : gizli dizge var mi -> False                      ✅
```

**İki yüzeydeki metinler farklı, ikisi de güvenli:**

| Yüzey | Metin |
|---|---|
| `GET /api/runs/{id}/tools` → `error` | **`Tool failed with SahteVeritabaniException.`** |
| SSE `functionResult.result` | `Error: Function failed.` |

Kayıt yüzeyi spec'in beklediği metni **birebir** veriyor
(`src/Tracon.Core/Tools/ToolFailureText.cs:10`:
`$"Tool failed with {exception.GetType().Name}."`, çağıranı
`ToolInvocationTracker.cs:130`).

⚠️ **Spec'e düzeltme:** "İki yüzeyde de **yalnız** `Tool failed with
<ExceptionType>.` görünür" ifadesi akış yüzeyi için doğru değil. SSE'deki
`functionResult` metni Microsoft.Extensions.AI'ın kendi genel metnidir
(`Error: Function failed.`) ve Tracon'un `ToolFailureText`'inden geçmez.
**Güvenlik iddiası ikisinde de tutuyor** — ne bağlantı dizesi ne özgün mesaj
görünüyor; yalnız metin ikisinde farklı. `Beklenen sonuç` buna göre
netleştirildi.

`tool_invocations` satırı ayrıca `succeeded: false`, `authorizationDenied:
false`, `timedOut: false` taşıyor — başarısızlığın **türü** de kayıtta ayrık.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

