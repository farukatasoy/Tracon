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

**DOSYA 02 KAPANDI.** `MT-CORE-001..129` — **97 case: 92 ☑ Geçti · 4 ☑ Kaldı ·
1 ☐ Beklemede.** Dört oturumluk iş tek turda koşuldu.

- **Sonraki aile:** zincirin 3.'sü `03-KALICILIK-POSTGRESQL.md` (50 case).
- **Bozuk ön koşul:** yok.

### Bu dosyanın bulguları

| Bulgu | Önem | Kısaca |
|---|---|---|
| `HATA-S1-008` | Düşük | Bilinmeyen `compaction.strategy` reddediliyor ama mesaj reddedilen değeri de geçerli listeyi de söylemiyor (güçlü tiplenmiş enum, JSON okuyucusunda düşüyor) |
| `HATA-S1-009` | Orta | Kod kaynaklı agent'ın `versions` ucu "böyle bir agent yok" diyor; agent var. `IAgentDefinitionStore`'da yokluk, her yerde yokluk sanılıyor |
| `HATA-S1-010` | Orta | Katalogdaki **13 modelin hiçbirinde fiyat yok** → her `run` maliyetsiz kaydediliyor. Mekanizma dürüst (`pricing_source=NotDefined`), eksik olan veri |
| `HATA-S1-011` | Orta | Çakışmayla düşen `run` kayıtta `Completed` görünüyor; çakışmanın izi yalnız akışta kalıyor |
| `HATA-S1-012` | Orta | Sevk edilen iki hata mesajında yarım kalmış Türkçe (`ne 'Input' ne 'Output' contains neither value`). `SourceLanguageTests` iki harfli kelimeleri bilinçli dışladığı için kaçmış |
| `HATA-S1-013` | Düşük | `Custom` kaynaklı agent'a "bu agent kodda tanımlı" deniyor; yönlendirme de yanlış |
| `HATA-S1-014` | Düşük | Analyzer'ın `TRC0007` metni `AddScopedTool`'u anmıyor; runtime metni anıyor. Pratikte görülen analyzer'ınki |

`HATA-S1-009` ve `HATA-S1-013` **aynı sınıftır** ("veritabanında yok" →
"kodda"); kapanışta birlikte değerlendirilmeli.

### Ortam — sonraki oturumun bilmesi gerekenler

🚨 **`echo` sağlayıcısı ile gerçek sağlayıcı aynı anda olamaz.**
`samples/Tracon.Api/Program.cs:277-280` `EchoModelProvider`'ı **yalnız** OpenAI
anahtarı yokken kaydeder. Bu dosyanın 4 case'i `echo`, 73'ü gerçek model
istedi; ortam ikisi arasında bilinçli olarak değiştirildi (kullanıcı kararı,
2026-09-16: gerçek çağrılarla koşulur).

🚨 **`/run` yanıtı SSE'dir ve metin token token gelir.** Ham `grep "Kelime"`
boş dönebilir (`" Far"` + `"uk"`). Birleştirici: `<scratch>/sse.py`.

🚨 **Kod donmuş turda `samples/` değiştirilemez.** Sekiz case bunu istiyordu
(`087 · 088 · 089 · 090 · 091 · 092 · 093 · 094` ve `129`). Hepsi repo
**dışında** kurulan bir tüketici host'uyla koşuldu: `~/tracon-manuel/ohost`
(port 5085), yayınlanmış paketler + örnek kaynak dosyalarının kopyası.
`git status` repo'da boş kaldı.

**Bırakılan aparat** (tur sonunda silinecek): `~/tracon-manuel/ohost` ·
`~/tracon-manuel/skillsiz` · `<scratch>/sse.py`. Çalışan süreç
bırakılmadı.

⚠️ **`00-INDEKS.md` §4 bayat:** "33 migration" diyor, bugün **51**. §3.2 tool
tablosu **4** tool listeliyor, bugün **10**. İkisi de bu dosyanın kapsamı
değil; açık kaleme yazıldı.

### Spec düzeltmeleri (skill §1.1, hepsi doküman kusuru)

| Case | Ne düzeltildi |
|---|---|
| 006 | SSRF kapısı: `Tracon__Egress__AllowPrivateNetworkTargets=true` ön koşulu + kayıt temizliği uyarısı |
| 022 | `Summarize` → `Summarization`; `ContextWindow` değeri katalogdan **türetilir** |
| 023 · 024 | `ResolveAsync` üç parametre alır; Türkçe mesaj alıntısı → İngilizce; 024'e sağlayıcı kaydı ön koşulu |
| 030 | `display_name` sütunu yok — görünen ad `definition` jsonb'sinde |
| 040 · 041 | Tool sayısı sabit değil; `source` kodda tanımlı tool'da `null`'dır (`"generated"` diye bir değer yok) |
| 044 · 045 | `cost_usd` sütunu yok (`input_cost`/`output_cost`); fiyat verisi olmadan maliyet `null` |
| 050 · 051 · 053 · 054 | Oturum geçmişi SQL'den doğrulanamaz — şema farklı **ve** `state` şifreli; doğrulama API'den |
| 060 · 072 · 041 | Türkçe mesaj alıntıları → bugünkü İngilizce metinler (K-228) |
| 062 | `CreateBudget()` artık `TimeProvider` alıyor |
| 065 | Bölüm `AddTracon`'a verilmeli; fiyat anahtarları `Input`/`Output` |
| 082 | `parameters` öğesi `kind` zorunlu alanını taşır |
| 084 · 085 | Talimat metni yalnız trace'ten görülebilir (span'e yazılır, `run_events`'e değil) |
| 087 · 089 | `isEditable` detay ucunda; alan adları `modelId`/`modelProvider` |
| 090 | Var olmayan dizin kaynağı **bozmaz** (`yield break`); bozmak için hatalı JSON gerekir |
| 092 · 093 | `json-file` önceliği 101; aynı tipi iki kez kaydetmek çakışma üretmez (`TryAddEnumerable`) |
| 099 | İki yüzey **farklı** metin taşır; aranacak şey sızıntının olmamasıdır |
| 087-094 · 129 | Donmuş `samples/` yerine repo dışı tüketici host'u tarifi |

---


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

**Gerçek sonuç — yeniden koşum 2026-09-19 (Aile L kapanışı sonrası)**

Canlı host (`artifacts/bin/Tracon.Api/release`, `--urls :5087`,
`ASPNETCORE_ENVIRONMENT=Development`, OpenAI anahtarı komut satırından boşaltıldı
ki `EchoModelProvider` kaydolsun — case'in kendi girdisi `echo/echo-1`).
Dört alt kontrolün dördü de:

```
c1b '"strategy":"Summarization"' (tetikleyicisiz)
  -> valid:false · "Agent 'c1b' selected the 'Summarization' compaction strategy
                    but gave no trigger (at least one of TriggerTokens/
                    TriggerMessages/TriggerTurns is required)."
c2  '"strategy":"BoyleBirSeyYok"'
  -> HTTP 400 · "'BoyleBirSeyYok' is not a valid value. The valid values are:
                 None, SlidingWindow, Truncation, ToolResult, Summarization,
                 ContextWindow, Pipeline. Path: $.compaction.strategy"
c3  ContextWindow, maxContextWindowTokens YOK -> valid:true   (katalogdan türetildi)
c3b ContextWindow, model "katalogda-yok"      -> valid:false · iki kaynağı da söylüyor
```

`c2` artık reddedilen değeri **ve** geçerli listeyi taşıyor; `BytePositionInLine`
gibi tüketiciye hiçbir şey anlatmayan ayrıntı gitti, yararlı olan tek parça
(`$.compaction.strategy`) kaldı. Case'in `Kaldı` sebebi buydu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**✅ KAPANDI 2026-09-18 (Aile L).** 👤 Karar: **tek converter, bütün enum
alanları** (K-821). Mesaj artık şu:

```
'BoyleBirSeyYok' is not a valid value. The valid values are: None,
SlidingWindow, Truncation, ToolResult, Summarization, ContextWindow,
Pipeline. Path: $.compaction.strategy
```

🚨 **İlk tasarım ÖLÇÜMLE reddedildi.** Converter'ı enum tiplerinin kendisine
`[JsonConverter]` ile takmak akla yatkındı ve **yayınlanan sözleşmeyi
bozuyordu**: `JsonSchemaExporter` yalnız framework'ün kendi enum converter'ını
tanır, bu yüzden `docs/openapi/tracon.json` **42 enum'un `enum` listesini birden
kaybetti** (287 satır silindi; `CompactionStrategyKind` şeması yalnız
`description` olarak kaldı) ve üretilen her istemci union'larını yitirirdi. Bu
ancak belgeyi tazeleyip `git diff`'e bakınca görüldü — derleme ve testler
yeşildi.

Çözüm, aynı sonucu sözleşmeye hiç dokunmadan verir: converter **istek gövdesi
options'ına** takılır. Bir options converter'ı tip düzeyindeki attribute'u
geçer (çözüm sırası: property attribute → options listesi → tip attribute'u),
yani gövdeler için kazanır, şema üretiminde hiç görünmez, ve `Tracon.Abstractions`
public yüzeyi büyümez.

🚨 **İkinci katman: `AgentEndpoints` gövdeyi KENDİ okuyordu.** Converter
takıldıktan sonra test hâlâ kırmızı kaldı. Sebep: `BindAgentDefinitionRequestAsync`
`RequestBodyBinding`'in **elle yazılmış ikinci bir kopyasıydı** ve
`ReadFromJsonAsync`'i **hiç options vermeden** çağırıyordu. Üç agent tanımı ucu
bu yüzden paylaşılan okuyucunun eklediği her şeyi sessizce kaçırıyordu —
yalnız bu converter'ı değil, `RespectNullableAnnotations`'ı da. İki okuyucunun
başlığı ve durum kodu **aynı** olduğu için kopya fark edilmeden durabilmişti.
Artık delege ediyor.

🚨 **Üçüncü katman: yolu System.Text.Json eklemiyor.** STJ `JsonException.Path`
alanını bir converter'ın attığı istisna için de doldurur, ama **mesaja** yalnız
kendi ürettiği istisnalarda ekler. Daha iyi bir cümle yazan converter bu yüzden
stok mesajın tek yararlı parçasını kaybediyordu. `RequestBodyBinding.Describe`
yolu bir kez, her gövde hatası için ekliyor.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ üç fonksiyonel testin ikisi düzeltmeden önce kırmızı |
| Sınıf taraması | ☑ `grep -rn "ReadFromJsonAsync" src/` → gövdeyi elle okuyan başka **iki** yer var (OpenAI uyumluluk uçları) ve ikisi de `JsonElement` okuyor, yani enum bağlama yok. Kaydın saydığı iki alan yerine **tüm** enum alanları kapsandı |
| Testler | 3 fonksiyonel test; ikisi ayrı sözleşme/uç, biri geçerli değerin hâlâ kabul edildiğini kilitliyor |

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

**Gerçek sonuç — yeniden koşum 2026-09-19 (Aile L kapanışı sonrası)**

Canlı host (`:5087`). `manuel-surum` bu veritabanında yoktu; case'in kendi
adımlarıyla yeniden kuruldu (`POST` → 201, `PUT` → 200) ve koşum sonunda
silindi (`DELETE` → 204).

```
GET /api/agents/support/versions      -> HTTP 404
  { "title": "Agent has no version history",
    "detail": "'support' is defined in code. A code definition is not stored by
               Tracon, so it has no version history; its history is the
               application's source history." }

GET /api/agents/support               -> HTTP 200   (agent VAR; iki cevap artik celiskisiz)

GET /api/agents/manuel-surum/versions -> HTTP 200 · IKI surum
  version 2 | IKINCI SURUM TALIMATI.
  version 1 | BIRINCI SURUM TALIMATI.

GET /api/agents/support/versions/1/diff/2 -> HTTP 404 · AYNI gerekce
  (sinif taramasinin bulduğu ikinci yuzey; onceden "Agent 'support' has no
   version 1." diyordu, yani agent'in burada surumlendigini iddia ediyordu)

GET /api/agents/hicbir-yerde-olmayan/versions -> HTTP 404
  { "title": "Agent not found", "detail": "There is no agent named
    'hicbir-yerde-olmayan'." }   (ters yon korundu)
```

Case'in gerekçesinin istediği şey — var olan bir agent için "yok" dememek ve
sebebi açıkça söylemek — sağlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**✅ KAPANDI 2026-09-18 (Aile L).** 👤 Karar: **durum kodu `404` kalır, gerekçe
doğruyu söyler** (K-820). Sürüm geçmişi kaynağı gerçekten yoktur; değişen tek
şey çağıranın üzerine iş yaptığı cümledir. `409` reddedildi — o kod bir
**değişiklik** çakışması içindir ve bu bir `GET`'tir. `200 + boş liste` de
reddedildi: "hiç sürüm yok" ile "burada sürümlenmiyor" ayrımını siler.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ altı fonksiyonel testin beşi düzeltmeden önce kırmızı |
| Sınıf taraması | ☑ kaydın şüphesi **yarı** doğru çıktı. `rollback` **zaten** doğruydu — `GuardCodeAgentAsync`'ten geçiyor. `versions/{a}/diff/{b}` ise kaydın öngörmediği bir **ikinci** biçimde yanlıştı: agent varlığını hiç kontrol etmiyor, doğrudan `"Agent 'x' has no version N."` diyordu — yani "bu agent burada sürümleniyor, yalnız o numara yok" iddiası; iki yarısı da yanlış |
| Düzeltme | Ortak `NoVersionHistoryAsync`: `Code` · `Custom` · bilinmeyen ad için üç ayrı cevap, tek yerde. `diff` artık varlık kontrolünü **iki sürüm okumasından önce** yapıyor |
| Testler | 6 fonksiyonel test; ikisi ters yönü kilitliyor (bilinmeyen ad **hâlâ** "There is no agent named 'x'." alır) |
| Tüketici yüzeyi | İki ucun OpenAPI `description` metni; yayınlanan belge tazelendi |

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

**Yeniden koşum — 2026-09-18 (Aile G kapanışı) · ☑ GEÇTİ**

Gerçek OpenAI `gpt-5.4-mini`, gerçek PostgreSQL (`mt_g` şeması). Token sayıları
**turdakiyle birebir aynı** çıktı (402 / 25), yani ölçülen tek fark fiyattır:

```
 agent_name | status | model_id     | input_tokens | output_tokens | input_cost   | output_cost  | cur | pricing_source
 support    |      1 | gpt-5.4-mini |          402 |            25 | 0.0001005000 | 0.0000500000 | USD | 0
```

`pricing_source` **2 (`Unknown`) → 0 (`Catalog`)**. Hesap elle doğrulandı:
402 × 0,25 / 1e6 = 0,0001005 ✓ · 25 × 2,00 / 1e6 = 0,00005 ✓. Dört beklentinin
dördü de karşılandı; `tool_invocations` satır sayısı yine **1**.

Örnek uygulamanın katalogu artık fiyat taşıyor (K-809) ve mekanizma
değişmedi — eksik olan yalnız veriydi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Yeniden koşum — 2026-09-18 (Aile G kapanışı) · ☑ GEÇTİ**

Aynı oturum, aynı kurulum. Token sayıları yine **turdakiyle birebir** (343 / 13):

```
 agent_name | input_tokens | output_tokens | input_cost   | output_cost  | pricing_source | tool_calls
 support    |          343 |            13 | 0.0000857500 | 0.0000260000 |              0 |          0
```

343 × 0,25 / 1e6 = 0,00008575 ✓ · 13 × 2,00 / 1e6 = 0,000026 ✓. Case'in asıl
iddiası (selam isteği tool çağırmaz) yine tuttu: **0** tool çağrısı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**✅ KAPANDI 2026-09-18 (Aile G).** Kaydın teşhisi doğruydu ve iki yarısı da
kapatıldı (K-808, K-809):

1. **Mekanizma artık susmuyor.** `UnpricedModelWarningService` açılışta her
   fiyatsız `provider/model` çiftini `Warning` ile adlandırır ve
   `GET /api/diagnostics` aynı listeyi `pricing` altında bildirir. İki okuyucu
   **tek** `UnpricedModels.Find` metodunu çağırır — iki kopya kaçınılmaz olarak
   kayar ve raporu uyarıyı doğrulamak için açan operatör farklı bir liste
   görürdü.
2. **Örnek uygulama artık dolu.** 13 modelin 13'ü fiyat taşıyor; rakamlar
   `//Models` yorumunda **açıkça ÖRNEK** olarak işaretlendi (bakımı yapılan bir
   fiyat listesi değil — K-032). Paket içine gömülü fiyat tablosu yine yok.

Canlı ölçüm (gerçek kurulum, `mt_g` şeması):

```
GET /api/models            -> 13 model, 13'ü fiyatlı  (turda: 13 / 0)
GET /api/diagnostics       -> "pricing": { "pricedModels": 13,
                                           "unpricedModels": [], "currency": "USD" }
acilis kaydi (echo saglayicisi acikken):
  warn: Tracon.UnpricedModelWarningService[1]
        1 catalog model(s) carry no price ... Unpriced: echo/echo-1
```

Veri kaybı olmadığı kayıtta zaten doğruydu: `POST /api/stats/recalculate-costs`
fiyat sonradan girilince geçmişi geri hesaplar. `HATA-S1-025`'ten (tool
harcamasının **kalıcı** kaybı) bu ailedeki ikizini ayıran şey budur.

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

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile B)

**Kök neden ölçüldü.** `AgentEndpoints` `SaveSessionAsync`'i `agent.RunAsync`'ten
**sonra** çağırıyor, yani `CompleteAsync(Completed)` çoktan yazılmış oluyor.
Run gerçekten tamamlandı: model çağrıldı, token harcandı, yanıt üretildi.
Düşen şey run'ın **oturum yazımı**.

**Kullanıcı kararı 👤 (2026-09-18):** durum `Completed` **kalır**, çakışma yeni
bir olay olarak kaydedilir. `Failed` demek run'ın maliyetini ve ürettiği yanıtı
da başarısız gösterirdi, ve hata sınıflandırma/uyarı hatları bunu gerçek bir
kesinti sanabilirdi.

**Düzeltme.** `RunEventType.SessionWriteConflicted = 32` eklendi (32, çünkü
`run_events.type` bir `smallint` sütunu ve var olan bir üyenin sayısal değeri
asla kaydırılamaz). Her iki yol da — akışlı ve akışsız — `SaveSessionAsync`'i
`SaveSessionRecordingConflictAsync` üzerinden çağırıyor; çakışmada olay run'a
yazılır, `Text` oturum kimliğini taşır, sonra istisna olduğu gibi devam eder ve
çağıran yine `409` alır.

Olay yazımı **best effort**'tur ve `409`'un yerine geçmez: gözlemlenebilirlik
işlevselliği bozmaz. Olayı alamayan bir store çağıranın yanıtını değiştirmez,
yalnız bir uyarı loglanır.

**Sınıf taraması.** Kayıt "akış başladıktan sonra atılan her istisna" taramasını
istiyordu. Aynı oturumda `HATA-S4-003` ile birlikte kapandı: o kusur run
satırının yazılmasından **önceki** pencereyi, bu kusur **sonraki** pencereyi
kapsıyor. Aradaki pencere (`MoveNextAsync` döngüsü) zaten `Failed` yazıyordu ve
ölçümle doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**✅ KAPANDI 2026-09-18 (Aile K).** Üç satır düzeltildi ve kapının kör noktası
kapatıldı.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ iki yeni test düzeltmeden önce kırmızı; ölçülen metin: `TraconPricingOptions: 'echo:echo-1' ne 'Input' ne 'Output' contains neither value.` |
| Düzeltme | Üç satır `Images` dalının **zaten doğru** olan cümlesine hizalandı: `'{anahtar}' contains neither 'X' nor 'Y'. Check the key name.` Çifte olumsuzlama da kalktı |
| Kapı | `SourceLanguageTests` iki harfli kelimeleri artık toptan atlamıyor; `ne · ya · ki · mi · mu · da · ve` listeye girdi |
| Sınıf taraması | ☑ taranan ağacın tamamında 16 iki-harfli aday ölçüldü; listeye giren yedisinin **sıfır** çakışması var. Çakışan üçü dışarıda kaldı: `de` ve `en` BCP-47 etiketi olarak literal geçiyor, `bu` çevrilmiş arayüz metnini doğrulayan E2E testinde |
| Taban çizgisi | **boş kaldı** — yeni kural repoda başka hiçbir satır bulmuyor |

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

**✅ KAPANDI 2026-09-18 (Aile L).** Bilgi kutusu artık `descriptor.origin`'i
okuyor; `Custom` için yeni bir metin (`agentDetail.sourceNotice`, en + tr)
kaynağı **adıyla** söylüyor ve doğru eylemi gösteriyor. Sayfa bu bilgiyi zaten
rozette gösteriyordu; eksik olan kutunun onu kullanmasıydı.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ iki vitest testi düzeltmeden önce kırmızı |
| Sınıf taraması | ☑ `grep -rn "isEditable" src/Tracon.UI/frontend/src` → beş kullanım; kalan dördü düğme görünürlüğüdür ve `Code`/`Custom` ayrımı onlar için anlamsızdır (ikisi de düzenlenemez). Rozet (`OriginBadge`) ayrımı zaten yapıyordu |
| Testler | `agent-detail.test.tsx`; ikinci test kod kaynaklı agent'ın metnini kilitliyor |

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

## MT-CORE-100 — BYOK desteklemeyen provider'da tenant credential fail-closed'tır

**Gerçek sonuç** (`ohost` · yalnız `IModelProvider` uygulayan `ByokSuzProvider`)

Sağlayıcının `CreateChatClient`'ı bir sayaç artırıp **istisna atıyor**
("Bu noktaya GELINMEMELIYDI") — böylece çağrılıp çağrılmadığı kesin ölçülüyor.

```
PUT /api/tenants/default/providers/byoksuz
  {"apiKeyConfigurationName":"Tracon:ProviderKeys:byoksuz"} -> 200
  {"providerName":"byoksuz","resolved":true,...}     <- kimlik GERCEKTEN cozuldu
Tracon__ProviderKeys__byoksuz = "SAHTE-KIRACI-ANAHTARI-9999"

POST /api/agents/byoksuz-agent/run -> HTTP 400
  "detail": "Agent 'byoksuz-agent' could not be compiled:
             The model provider does not support tenant credentials.
             Provider: 'byoksuz'."
```

**Üç beklentinin üçü de tuttu:**

| Beklenti | Ölçüm |
|---|---|
| Provider/setup client çağrısı **0** | `"GELINMEMELIYDI"` SSE'de 0, logda 0 ✅ |
| Stable hata, sessiz fallback yok | `provider_credential_unsupported` mesajı döndü; global anahtara **düşmedi** ✅ |
| Credential değeri hiçbir yerde yok | `SAHTE-KIRACI-ANAHTARI-9999`: SSE'de 0, logda 0 ✅ |

**Kök mekanizma** (`src/Tracon.Core/Models/ModelProviderRegistry.cs:396-401`):
```csharp
chatClient = credential switch
{
    null => provider.CreateChatClient(binding),
    _ when provider is ITenantCredentialModelProvider p => p.CreateChatClient(binding, credential),
    _ => throw ProviderInvocationException.CredentialUnsupported(binding.Provider),
};
```
Üçüncü dal sağlayıcıya **hiç dokunmadan** atıyor — "0 çağrı" iddiasının
yapısal karşılığı budur.

📋 `provider_credential_unsupported` dizgisi iç `ErrorType`'tır
(`ProviderFailureNormalizer.cs:11`); HTTP gövdesi onun **mesajını** taşır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-101 — `AddRunJudge` üç overload'ı AgentSource deseniyle aynı idempotency'i taşır

**Gerçek sonuç**
```
1) AddRunJudge<SabitJudge>() iki kez  -> kayit sayisi: 1          ✅ tek singleton
2) AddRunJudge(instance "birinci")
   + AddRunJudge(factory  "ikinci")   -> kayit sayisi: 2
                                          adlar: birinci, ikinci  ✅ ikisi de korundu
3) AddRunJudge(instance "ayniad")
   + AddRunJudge(factory  "AYNIAD")   -> TraconException:
   "More than one run judge named 'ayniad' has been registered."  ✅ case-insensitive
```
Üç beklentinin üçü de tuttu. Desen `AddAgentSource` ile birebir aynı
(`MT-CORE-093`: generic aşırı yükleme `TryAddEnumerable` ile tipi
tekilleştirir; instance/factory ayrı örnekleri korur; ad çakışması hata verir).

**Sapma:** `RunJudgeSet` `internal` olduğu için çakışma denetimi reflection ile
tetiklendi (`Activator.CreateInstance`). Üretim yolunda aynı kurucu host
açılışında koşar.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-102 — `JudgeTimeout` token'ı yok sayan judge için gerçek wait cutoff'tur

**Gerçek sonuç**

`TembelJudge` `Task.Delay(8 sn)` yapıyor ve cancellation token'ı **bilinçli
olarak** iletmiyor. `Tracon__OnlineEvaluation__JudgeTimeout = 00:00:02`.

```
POST /api/runs/{id}/judge -> HTTP 502 · 2,06 sn
  { "title": "Manual scoring failed", "detail": "tembel (judge_timeout)" }
```

🚨 **Kanıt süredir:** judge gövdesi 8 sn sürüyor ve token'ı dinlemiyor; çağrı
yine de **2,06 sn**'de döndü. Yani `JudgeTimeout` judge'ın işbirliğine bağlı
değil, çağıran tarafta **gerçek bir bekleme kesintisi**. Hata metni judge'ı
adıyla ve `judge_timeout` koduyla veriyor.

**Gövde bittikten sonra (12 sn beklendi):**
```
run kaydinda scores/judgments  -> None       ✅ skor YAZILMADI
UnobservedTaskException/Unhandled -> 0        ✅ uretilmedi
```
İki beklentinin ikisi de tuttu: geç biten gövde skoru geri yazmıyor ve arkada
kalan Task sessizce yutulmuyor da, patlamıyor da.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-103 — `AgentDefinitionCompiler` ayrıştırmasından sonra `support` agent aynı şekilde çalışır

**Gerçek sonuç** (gerçek OpenAI · PostgreSQL `ap-pg` · şema `mt_s1`)
```
SSE olaylari: 1 run · 7 update · 1 done          ✅ update (metin+usage) + done
yanit: "Merhaba! 👋"

GET /api/runs?agentName=support&limit=1:
  id     : 01a0ab24-0170-7d24-a153-cbb764e97a7b
  status : Completed                              ✅
  usage  : inputTokens 351 · outputTokens 8 · totalTokens 359   ✅ gercek usage
```
İki beklentinin ikisi de tuttu. 2026-08-26 koşumunun sonucu (234/40 token)
bugün 351/8 ile tekrarlandı — sayılar farklı, **yapı** aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-104 — Culture taşıyan tanım en yakın culture talimatını çözer

**Gerçek sonuç:** aşağıdaki ortak koşumun kapsamındadır
(`AgentDefinitionCompilerPathTests`, 14/14). Davranışsal karşılığı bu turda
**canlı** olarak da ölçüldü: `MT-CORE-076` (`tr`) ve `MT-CORE-077` (`tr-TR`)
Türkçe talimatı seçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-105 — Shared instructions sync yolda açık hata verir, async yolda çözülür

**Gerçek sonuç** (ikisi de otomatik teste atıfta bulunuyor; testler koşuldu)
```
dotnet build tests/Tracon.Core.UnitTests -c Release -> 0
Tracon.Core.UnitTests --filter-class "*AgentDefinitionCompilerPathTests" "*SharedInstructionsTests"
  -> cikis 0 · total: 14 · succeeded: 14 · failed: 0 · skipped: 0
```

Atıfta bulunulan dört testin dördü de kaynakta **var**:

| Test | Dosya |
|---|---|
| `Sync_full_overload_resolves_the_requested_culture` | `Compilation/AgentDefinitionCompilerPathTests.cs` |
| `Async_CompileAsync_resolves_the_requested_culture` | aynı |
| `Synchronous_Compile_refuses_a_definition_that_references_a_block` | `Compilation/SharedInstructionsTests.cs` |
| `Blocks_text_is_prepended_to_the_agents_own_instructions` | aynı |

`MT-CORE-104`'ün davranışsal karşılığı bu turda **canlı** olarak da ölçüldü
(`MT-CORE-076`/`077`: `tr` ve `tr-TR` Türkçe talimatı seçti).
`MT-CORE-105`'in async yolu `MT-CORE-085`'te canlı koştu (`shared-e2e`
derlendi ve çalıştı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Otomatik karşılık koşumları (106–129 bloğunun tabanı)

Bu bloğun case'lerinin çoğu spec'te **adı verilmiş** otomatik testlere atıfta
bulunuyor. Dört suite koşuldu; hepsi `--no-build`'den **önce** derlendi
(skill §6 tuzağı):

| Suite | Sonuç | Kapsadığı case'ler |
|---|---|---|
| `Tracon.Generators.UnitTests` | **291/291** · 0 atlanan | 109 · 110 · 111 · 113 · 114 · 115 · 116 · 117 · 118 · 119 · 120 |
| `Tracon.AspNetCore.FunctionalTests` (`*HarnessLoopTests` · `*ScopedToolLifetimeTests`) | **13/13** · 0 atlanan | 107 · 123 · 124 · 125 · 127 · 128 |
| `Tracon.Core.UnitTests` (`*LoopEvaluatorRegistryTests` · `*InMemoryRunStore*` · `*ScopedToolTests`) | **142/142** · 0 atlanan | 106 · 107 · 126 |
| `Tracon.Package.Tests` | **2/2** (ayrı ayrı) | 112 · 122 |

Spec'in adıyla andığı test metotlarının kaynakta varlığı tek tek doğrulandı
(beşi `Tracon.Generators.UnitTests` içinde, dördü `Tracon.Core.UnitTests`
içinde) — "geçti" demek için testin **var olduğunu** da görmek gerekir.

---

## MT-CORE-106 — `InMemoryRunStore` ayrıştırması sonrası ağaç toplamları ve kiracı yalıtımı doğru kalır

**Gerçek sonuç** (`samples/Tracon.Embedded`, port **5086** — şerit sapması;
spec 5082 der, o port şerit `ap-s2`'nindir)
```
GET /tracon/api/diagnostics -> "persistenceProvider": "InMemory"   ✅ on kosul

POST /jobs acme  · POST /jobs globex

acme:   run sayisi 1 | status Completed
        usage     : in 16 · out 16 · total 32
        treeUsage : in 16 · out 16 · total 32        ✅ ikisi de dolu
globex: run sayisi 1 | status Completed
        -> acme'nin listesinde globex YOK, tersi de                 ✅ kiraci suzgeci

GET /tracon/api/stats (X-Host-Tenant: acme):
  totalRuns: 1 · completedRuns: 1 · failedRuns: 0
  byAgent : [{ agentName: "assistant", totalRuns: 1, totalTokens: 32 }]
  byModel : [{ modelId: "echo-1", ... }]                            ✅ kirilimlar dogru
```
Üç beklentinin üçü de tuttu. Beş dosyaya bölünme davranışı bozmamış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-107 — `AddScopedTool` ile kaydedilmiş bir tool her çağrıda taze DI kapsamı alır

**Gerçek sonuç**
Spec iki yol sunuyor: örnek uygulamaya tool eklemek (**`samples/` donuk**,
yapılamaz) **ya da** `ScopedToolLifetimeTests`'i elle izlemek. İkincisi
koşuldu:
```
Tracon.AspNetCore.FunctionalTests --filter-class "*ScopedToolLifetimeTests"
  (HarnessLoopTests ile birlikte) -> 13/13 · 0 atlanan
Tracon.Core.UnitTests --filter-class "*ScopedToolTests" -> 142/142 icinde
```
Fonksiyonel test gerçek bir host üzerinden **art arda ve eşzamanlı** çağrıların
ayrı kapsam aldığını ve kapsamın çağrı bitince kapandığını kanıtlıyor; birim
testi sarmalayıcının kendi mekaniğini ayrıca kapsıyor. K-218 kısıtı kapalı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-108 — Örnek metot tool taraması, `AddScopedTool`'u adıyla önerir

**Gerçek sonuç**

**Case'in kendi iddiası TUTTU** — runtime tarayıcı yolunda:
```
beklenen red: Method 'OrnekMetotTool.Calistir' is an instance method and
cannot be a tool. MAF supplies an empty provider as
AIFunctionArguments.Services (K-218). Make the method `static`, create the
target during registration and use `AddTool(AIFunctionFactory.Create(...))`,
or use `AddScopedTool(...)` if the dependency must be resolved per call.

AddScopedTool aniliyor mu: True   ✅
```
Başlangıç `TraconException` ile durdu ve metin `AddScopedTool`'u **adıyla ve
ne zaman kullanılacağıyla** anıyor (`ToolMethodScanner.cs:101-105`).

🚨 **Ama ikinci bir ret yolu var ve o güncellenmemiş → `HATA-S1-014`.**

📋 **Koşum notu — skill §6'nın tuzağına düşülüp çıkıldı:** ilk denemede
derleme `error TRC0007` ile kırıldı, ama `dotnet run --no-build` **eski
ikiliyi** koşup bir önceki case'in çıktısını bastı. Çıktı bir an doğru
görünüyordu. Derleme logu okunarak yakalandı; `--no-build` öncesi derleme
sonucunun okunması bu yüzden zorunlu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-014 — Analyzer'ın `TRC0007` metni `AddScopedTool`'u anmıyor; runtime metni anıyor

| | |
|---|---|
| **Önem** | Düşük |
| **Bulunduğu case** | MT-CORE-108 |
| **Sınıf** | Tanı kalitesi · iki yüzeyin ayrışması |

Aynı kusur için **iki** ret metni var ve Faz 127 yalnız birini güncellemiş:

| Yüzey | Kaynak | `AddScopedTool` anılıyor mu |
|---|---|---|
| Runtime tarayıcı (`TraconException`) | `src/Tracon.Core/Tools/ToolMethodScanner.cs:101-105` | ✅ evet |
| Analyzer (`error TRC0007`) | `src/Tracon.Generators/ToolDiagnostics.cs:74` | ❌ **hayır** |

Analyzer metni yalnız iki yol öneriyor: *"Make the method 'static', or
instantiate the tool at setup time and register it with
'AddTool(AIFunctionFactory.Create(...))'."*

🚨 **Pratikte görülen metin analyzer'ınkidir.** Analyzer **derleme zamanında**
hata verir, yani runtime tarayıcıya sıra **hiç gelmez** — bu turda runtime
yolunu ölçmek için `TRC0007`'yi `NoWarn` ile bastırmak gerekti. Yani
geliştiricinin gördüğü tek metin, güncellenmemiş olanıdır.

`grep -rc "AddScopedTool" src/Tracon.Generators/ToolDiagnostics.cs` → **0**.

**Kapanışta:** `ToolDiagnostics.cs:74`'e üçüncü seçenek eklenir. Ölçüt basit —
iki metin aynı seçenek kümesini saymalı.

**Etki:** düşük — kusur her iki yolda da yakalanıyor; bedeli, kalıcı bir
bağımlılığı olan tüketicinin doğru API'yi (kendisi için var olan `AddScopedTool`)
bulamaması.

**✅ KAPANDI 2026-09-18 (Aile L).** Analyzer metni üçüncü seçeneği kazandı.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ `TRC0007` testi genişletildi ve düzeltmeden önce kırmızı |
| Sınıf taraması | ☑ kayıt **iki** yüzey sayıyordu; ölçüm **dört** buldu. `AddScopedTool`'u anmayan diğer ikisi: `ToolMethodScanner`'ın kendi XML dokümanı (sevk edilen metin) ve `docs-site/troubleshooting.md`'nin `TRC0007` bölümü — ikincisi `AddScopedTool`'un yaptığı işi **tarif ediyor** ama adını vermiyordu, yani okuyan kişi API'yi yine bulamıyordu. İkisi de düzeltildi; siteye çalışan bir örnek eklendi |
| Diğer ikizler | `TRC0004` ve `TRC0005`'in çalışma-anı karşılıkları da karşılaştırıldı: `TRC0004` birebir aynı, `TRC0005`'in iki metni **bilerek** farklı (biri `AddGeneratedTools()` çağrısını, diğeri `AddToolsFrom(type)` çağrısını anlatır) — kayma değil |
| Testler | `DiagnosticTests`; üç seçeneğin üçü de adıyla zorlanıyor |

---

## MT-CORE-109 — `[Range]` üretilen şemaya `minimum`/`maximum` yazar

> Aşağıdaki ölçüm **109 · 110 · 111 · 113 · 114** için ortaktır.

**Gerçek sonuç**
```
Tracon.Generators.UnitTests -> 291/291 · failed 0 · skipped 0
```
Spec'in adıyla andığı test kaynakta doğrulandı:
`ToolSchemaConstraintTests.A_Range_attribute_on_an_integer_parameter_produces_minimum_and_maximum`
(`tests/Tracon.Generators.UnitTests/ToolSchemaConstraintTests.cs`).

Bu beş case (`[Range]` → `minimum`/`maximum` · `[MinLength]` → `minLength`
vs `minItems` · uyumsuz kısıt → `TRC0010` derlemeyi kırmadan · `tr-TR`
yerelinde ondalık ayırıcı `.` kalır · iki derlemede bit düzeyinde aynı şema)
üreteç suite'inin kapsamındadır ve suite tamamen yeşildir.

📋 `tr-TR` ondalık ayracı konusu bu turda **bağımsız olarak da** görüldü:
`MT-CORE-063`'te `SuccessSampleRatio` konsola `0,1` olarak yazıldı (konsol
çıktısı, kültüre duyarlı `ToString`), ama üretilen **şemada** ayracın `.`
kalması ayrı bir yoldur ve `ToolSchemaConstraintTests` onu kapsar.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-110 — `[MinLength]` `string`'de `minLength`, dizide `minItems` yazar

**Gerçek sonuç:** aşağıdaki **Üreteç şema kısıtları** ortak koşumunun kapsamındadır —
`Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-111 — Uyumsuz kısıt `TRC0010` üretir, derleme başarılı kalır

**Gerçek sonuç:** aşağıdaki **Üreteç şema kısıtları** ortak koşumunun kapsamındadır —
`Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-113 — `tr-TR` yerelinde ondalık ayırıcı `.` kalır

**Gerçek sonuç:** aşağıdaki **Üreteç şema kısıtları** ortak koşumunun kapsamındadır —
`Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-114 — Aynı girdi iki derlemede bit düzeyinde aynı şema üretir

**Gerçek sonuç:** aşağıdaki **Üreteç şema kısıtları** ortak koşumunun kapsamındadır —
`Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---


---

## MT-CORE-112 — Kısıtlı bir tool ile gerçek `run` başarıyla biter

**Gerçek sonuç**
```
Tracon.Package.Tests --filter-method "*Constrained*"
  -> cikis 0 · total 1 · succeeded 1 · failed 0 · skipped 0 · 57,7 sn
```
57 saniye, testin gerçekten **paketleyip** yerel feed'e koyup dış bir tüketici
projesi kurduğunun işareti — `samples/Tracon.Api`'nin `ProjectReference` ile
kanıtlayamayacağı şey budur (K-166 emsali). `analyzers/dotnet/cs/` içindeki
DLL paket sınırını geçiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-115 — Nesne parametresi nested JSON Schema düğümü üretir

> Aşağıdaki ölçüm **115 · 116 · 117 · 118 · 119 · 120** için ortaktır.

**Gerçek sonuç** — `Tracon.Generators.UnitTests` 291/291 içinde. Adı verilen
test metotları kaynakta doğrulandı:

| Case | Test |
|---|---|
| 115 | `ToolSchemaObjectTests.An_object_parameter_produces_a_nested_object_schema_node` |
| 116 | `ToolSchemaObjectTests.An_object_array_parameter_produces_an_array_of_object_schema_nodes` |
| 117 | `ToolSchemaObjectTests.A_Range_attribute_on_an_object_member_produces_minimum_and_maximum_on_the_member_node` |
| 119 | `ToolObjectGraphTests.A_graph_four_levels_deep_produces_TRC0012_and_blocks_generation` |

118 (`TRC0011`) ve 120 (cycle → `TRC0012`, generator asılmaz) aynı iki
sınıfın kapsamındadır. Suite 291 testin tamamını **asılmadan** bitirdi, ki
120'nin "generator asılmaz" iddiasının pratik karşılığı da budur.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-116 — Nesne dizisi parametresi `items` düğümünde nesne üretir

**Gerçek sonuç:** aşağıdaki **Nesne şeması ve grafik sınırları** ortak koşumunun
kapsamındadır — `Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-117 — Nesne üyesindeki `[Range]` üye düğümüne `minimum`/`maximum` yazar

**Gerçek sonuç:** aşağıdaki **Nesne şeması ve grafik sınırları** ortak koşumunun
kapsamındadır — `Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-118 — Context'te bildirilmeyen nesne tipi `TRC0011` ile derlemeyi durdurur

**Gerçek sonuç:** aşağıdaki **Nesne şeması ve grafik sınırları** ortak koşumunun
kapsamındadır — `Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-119 — 3'ü aşan nesne derinliği `TRC0012` ile derlemeyi durdurur

**Gerçek sonuç:** aşağıdaki **Nesne şeması ve grafik sınırları** ortak koşumunun
kapsamındadır — `Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-120 — Bir cycle `TRC0012` üretir, generator asılmaz

**Gerçek sonuç:** aşağıdaki **Nesne şeması ve grafik sınırları** ortak koşumunun
kapsamındadır — `Tracon.Generators.UnitTests` **291/291**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---


---

## MT-CORE-121 — Nesne parametreli bir tool gerçek `run`'da modelden nesne argümanı alır

**Gerçek sonuç** (gerçek OpenAI · `support` · `gpt-5.4-mini`)

`support`'un tool listesi ön koşulu **zaten** sağlıyor:
`['get_order_status', 'list_recent_orders', 'cancel_order',
'read_shopping_cart', 'estimate_shipping_cost', 'mark_preview_ready']`

```
istek: "I need a shipping estimate for an order going to 42 Rose Ave,
        Springfield, postal code 62704."

functionCall   | {"name": "estimate_shipping_cost",
                  "arguments": {"address": {"Street": "42 Rose Ave",
                                            "City": "Springfield",
                                            "PostalCode": "62704"}}}
functionResult | {"result": "Estimated shipping to Springfield, 62704:
                             $12.50 (3-5 business days)."}
```

🚨 **Model düz `string` değil, gerçek bir NESNE argümanı gönderdi** — üç alanı
da ayrı ayrı doldurdu. 2026-09-02 koşumunun sonucu bugün **birebir**
tekrarlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-122 — Nesne parametreli tool paketlenmiş dış tüketicide `PublishAot` altında çalışır

**Gerçek sonuç**
```
Tracon.Package.Tests --filter-method "*An_object_parameter_tool_publishes_under_Native_AOT*"
  -> cikis 0 · total 1 · succeeded 1 · failed 0 · skipped 0 · 17,7 sn
```
Test dış tüketici projesini `PackageReference` + `PublishAot=true` ile
yayımlıyor ve trim uyarısı (`IL2026`/`IL3050`) olsaydı düşerdi. 2026-09-02'nin
`osx-arm64` / 31 sn koşumu bugün 17,7 sn'de tekrarlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-123 — `harness.loop` verilmeyen agent bugünkü davranışını korur

> Aşağıdaki ölçüm **123 · 124 · 125 · 126 · 127 · 128** için ortaktır.

**Gerçek sonuç**
```
Tracon.AspNetCore.FunctionalTests --filter-class "*HarnessLoopTests" ...
  -> 13/13 · failed 0 · skipped 0
Tracon.Core.UnitTests --filter-class "*LoopEvaluatorRegistryTests" ...
  -> 142/142 · failed 0 · skipped 0
```
Spec'in adıyla andığı testler:

| Case | Test |
|---|---|
| 123 | `HarnessLoopTests.A_harness_without_loop_settings_writes_no_iteration_event` |
| 124 | `HarnessLoopTests.A_marker_criterion_loops_until_the_marker_and_records_every_iteration` |
| 125 | `HarnessLoopTests.An_unreachable_criterion_stops_at_the_Tracon_ceiling_instead_of_running_on` |
| 126 | `LoopEvaluatorRegistryTests` (yedi ret vakası) |
| 128 | `HarnessLoopTests.An_exhausted_tree_budget_stops_the_loop_rather_than_letting_it_open_another_iteration` |

Bu case'ler `harness.loop` yapılandırması taşıyan agent'lar gerektiriyor;
örnek uygulamada öyle bir agent **yok** ve `samples/` bu turda **donuk**.
Fonksiyonel suite gerçek bir host üzerinden aynı yolları koşuyor ve yeşil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-124 — `completionMarker` ölçütü marker gelene kadar döner

**Gerçek sonuç:** aşağıdaki **Harness döngüsü** ortak koşumunun kapsamındadır —
fonksiyonel suite **13/13**, çekirdek suite **142/142**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-125 — Ulaşılamayan bir ölçüt Tracon'in kendi tavanında durur

**Gerçek sonuç:** aşağıdaki **Harness döngüsü** ortak koşumunun kapsamındadır —
fonksiyonel suite **13/13**, çekirdek suite **142/142**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-126 — Bilinmeyen bir ölçüt `kind`'i kayıtta `400` ile reddedilir

**Gerçek sonuç:** aşağıdaki **Harness döngüsü** ortak koşumunun kapsamındadır —
fonksiyonel suite **13/13**, çekirdek suite **142/142**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-127 — Döngü iterasyonları kayda geçer

**Gerçek sonuç:** aşağıdaki **Harness döngüsü** ortak koşumunun kapsamındadır —
fonksiyonel suite **13/13**, çekirdek suite **142/142**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-128 — Bütçe tavanı dolunca döngü yeni iterasyon açmaz

**Gerçek sonuç:** aşağıdaki **Harness döngüsü** ortak koşumunun kapsamındadır —
fonksiyonel suite **13/13**, çekirdek suite **142/142**, 0 atlanan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---


---

## MT-CORE-129 — `AddAgentDecorator()` tüketicinin kendi decorator'ını sıraya sokar

**Gerçek sonuç**

🚨 **Spec'in ön koşulu `samples/Tracon.Api/Program.cs`'i değiştirmeyi istiyor —
donuk.** Bunun yerine `ohost` tüketici host'una `Order => 5` taşıyan bir
`TuketiciDecorator` kaydedildi (`AddAgentDecorator<T>()`). Decorator sardığı
agent'ın **tipini** kaydediyor; sıralamanın kanıtı o tip.

```
GET /decorator-kayit ->
[ "kod-agent <- OpenTelemetryAgent",
  "greeter  <- OpenTelemetryAgent" ]
```

**1) Her agent'a uygulanıyor** ✅ — `kod-agent` (kod kaynağı) **ve** `greeter`
(özel `json-file` kaynağı) ikisi de sarıldı. Özel kaynaktan gelen bir agent
bile atlanmadı.

**2) Sıralama kanıtlandı** ✅ — `Order 5` decorator'ına gelen agent zaten
`OpenTelemetryAgent`. Yani `Order 10` (telemetri) **daha önce** sarmış; benim
`Order 5` çıktım da sonra `Order 0` (kayıt) tarafından sarılacak. Belgelenen
kural birebir: *"düşük `Order` DIŞTA sarar"* —

```
RunRecording (0)  →  TUKETICI (5)  →  OpenTelemetry (10)  →  ToolApproval (20)  →  StructuredResponse (30)
   en distaki                                                                          en icteki
```
Yani tüketicinin logladığı iş `run` kaydının **içinde**, telemetri span'inin
**dışında** görünür — spec'in tarif ettiği yer.

**3) Yerleşik decorator'lar kaybolmadı** ✅ — telemetri sarmalayıcısı gözle
görünür durumda; `run`'lar tamamlandı ve kaydedildi.

📋 **Ölçülmeyen:** "aynı davranış üç kayıt biçiminin üçünde de aynıdır" —
yalnız `AddAgentDecorator<T>()` biçimi koşuldu. Örnek alan ve `IServiceProvider`
alan aşırı yüklemeler ölçülmedi; `MT-CORE-101`/`MT-CORE-093` aynı desenin
(TryAddEnumerable + instance/factory) tutarlı olduğunu gösteriyor ama bu case
için doğrudan kanıt değildir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

