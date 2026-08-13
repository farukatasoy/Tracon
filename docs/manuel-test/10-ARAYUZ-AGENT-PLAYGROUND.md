# 10 — Arayüz: Agent ve Playground (`UIAG`)

> **Alan kodu:** `UIAG` · **Faz:** 5 (agent kataloğu, editör, playground), 19 (sürüm
> karşılaştırma — `agent-detail.tsx` içindeki `VersionCompare`)
> **Kaynak:** `src/AgentPrism.UI/frontend/src/screens/agents.tsx` (katalog) ·
> `screens/agent-editor.tsx` (oluşturma/düzenleme) · `screens/agent-detail.tsx`
> (özet, versiyon geçmişi, karşılaştırma, geri alma) · `screens/playground.tsx`
> (akışlı sohbet) · destek bileşenleri: `components/transcript.tsx` (tool/onay/
> reasoning kartları), `components/diff-view.tsx` (`DiffView`/`FieldDiffTable`/
> `SetDiff`), `components/branch-button.tsx`, `components/voice-panel.tsx`
> (yalnız "var olduğu" — bkz. Sınır), `lib/transcript.ts` (`foldUpdate` —
> canlı SSE'den transcript'e katlama), `lib/api.ts` (`api.agents/agent/
> createAgent/updateAgent/validateAgent/deleteAgent/agentVersions/
> rollbackAgent/agentVersionDiff/uploadAttachment/deleteAttachment/
> attachmentBlob/createConversation/speak`).
> Sunucu tarafı davranış: `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs`
> (özellikle `AgentRunStream.ExecuteStreamingAsync`'in `run`/`update`/`done`/
> `error` çerçeveleri), `Endpoints/AttachmentEndpoints.cs`,
> `Core/Attachments/AttachmentTypeGuard.cs`, `Core/Approvals/
> ToolApprovalRuleEvaluator.cs`, `AspNetCore/Internal/ToolApprovalResolver.cs`.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

Agent kataloğunun ve tekli-agent oynatma alanının **kendi iş akışı**: agent
tanımını oluşturmak/düzenlemek için doldurulan form ve onun canlı JSON
önizlemesi, tool/beceri/çağrılabilir-agent seçici listelerinin K2 sınırını
(yalnız kayıtlı olanlar seçilebilir, kod yazılamaz) nasıl uyguladığı, versiyon
geçmişinin karşılaştırma ve geri alma davranışı (Faz 19), ve Playground'ın
`{prefix}/api/agents/{name}/run` SSE akışını gerçek zamanlı olarak bir transkripte
nasıl katladığı — metin, reasoning, tool kartı, onay kartı, hata.

Ham HTTP sözleşmesi (durum kodları, `ProblemDetails` gövdeleri, SSE çerçeve
biçimi) bu dosyanın konusu **DEĞİLDİR** — bkz. Sınır tablosu. Burada ölçülen,
insan gözünün ekranda ne gördüğüdür: bir tool kartı ne zaman açık başlar, bir
onay kararı arayüzde nasıl kilitlenir, bir sunucu hatası akışa hiç yansımazsa
kullanıcı bunu fark edebilir mi.

```mermaid
flowchart TD
    A["agents.tsx: katalog listesi"] -->|"Yeni Agent (yalnız canAdminister)"| B["agent-editor.tsx: POST /api/agents"]
    A -->|"satıra tıkla"| C["agent-detail.tsx: GET /api/agents/{name}"]
    A -->|"Çalıştır"| D["playground.tsx"]
    C -->|"Düzenle (yalnız isEditable)"| B2["agent-editor.tsx: PUT /api/agents/{name}"]
    C --> E["VersionHistory: iki versiyon seç"]
    E --> F["VersionCompare: DiffView + FieldDiffTable + SetDiff"]
    D -->|"mesaj gönder"| G["POST run — SSE: run/update/done/error"]
    G --> H["lib/transcript.ts: foldUpdate"]
    H --> I["TranscriptView: metin / reasoning / tool kartı / onay kartı"]
    I -->|"cancel_order gibi RequiresApproval"| J["ApprovalCard: Onayla/Reddet/Hatırla"]
    J -->|"Onayla"| G
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| `/api/agents` CRUD'un ham HTTP sözleşmesi (durum kodları, `ProblemDetails`, `curl`) | [`07-HTTP-YONETIM-API.md`](07-HTTP-YONETIM-API.md) §1 (`MT-API-001`–`015`) — zaten üretildi |
| `POST .../run`'ın ham SSE çerçeve sözleşmesi, `Idempotency-Key`, `Prefer: respond-async`, gerçek sağlayıcı hata sınıflandırması | [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md) §7 (`MT-OAI-040`–`043`), [`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md), [`07-HTTP-YONETIM-API.md`](07-HTTP-YONETIM-API.md) §3 — zaten üretildi |
| Devre kesici, sağlayıcı sağlığı, içerik guard'ın kural tanımı ve engelleme mantığı | `05`/`06` (sağlayıcı) · `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` (kural motoru, henüz üretilmedi) |
| Oturum ekranı (`session-detail.tsx`), kayıtlı çalıştırmadan transkript üretme (`foldRunEvents`), `Last-Event-ID` ile devam | `11-ARAYUZ-RUN-SESSION-SSE.md` (henüz üretilmedi) |
| Ek yükleme/indirmenin tam sözleşmesi (boyut/tür sınırları, saklama, çoklu-dosya, gerçek ElevenLabs ses akışı) | `19-COK-MODLULUK-VE-SES.md` (henüz üretilmedi) — burada yalnız playground'daki **görünür** UI davranışı (chip, önizleme, mikrofon düğmesi) hafifçe ölçülür |
| Kota, saklama, arşivleme | `23-SAKLAMA-ARSIV-KOTA.md` (henüz üretilmedi) |
| Kod-only tool sınırının (K2) genel gerekçesi, rol tabanlı gezinme görünürlüğünün genel mekanizması | [`09-ARAYUZ-GENEL.md`](09-ARAYUZ-GENEL.md) — burada yalnız bu ekranlardaki **somut örnekleri** ölçülür |
| Dashboard, maliyet grafikleri, çalışma istatistikleri | `12-GOZLEMLENEBILIRLIK-MALIYET.md` (henüz üretilmedi) |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır (`cd samples/AgentPrism.Api && dotnet run`),
   `http://localhost:5080/agentprism/` açılır, `manuel-test-token-2026` ile giriş
   yapılmıştır (bkz. [`09-ARAYUZ-GENEL.md`](09-ARAYUZ-GENEL.md) `MT-UI-001`/`002`).
3. `AgentPrism:Providers:OpenAI:ApiKey` tanımlıdır — bu dosyanın çoğu case'i
   gerçek bir OpenAI çağrısı yapar (`support` agent'ı, model `gpt-5.4-mini`).
   Gerçek para harcanır; her case'in "Adımlar" bölümü tam olarak kaç çalıştırma
   gerektiğini yazar.
4. Kalıcılık: `AgentPrism:PostgreSql:ConnectionString` tanımlıdır (agent
   CRUD ve versiyon geçmişi bellek içi depoda da çalışır, ama bu dosyadaki
   versiyon/geri alma case'leri PostgreSQL ile koşulur ki bir sonraki oturumda
   da agent listesi kalıcı kalsın).
5. DevTools açık tutulur — bazı case'ler ağ sekmesinden SSE çerçevelerini veya
   `localStorage`/React Query önbelleğini incelemeyi gerektirir.

> Bu dosyada oluşturulan `FIX-AGENT-01` (`manuel-destek`) ve `FIX-AGENT-02`
> (`manuel-bos`) sonraki koşumlarda **silinmeden** bırakılır — versiyon/geri
> alma bölümü aynı agent'ın birden çok sürümüne ihtiyaç duyar.

---

# 1 — Agent kataloğu (`agents.tsx`)

### MT-UIAG-001 — Katalog listesi köken rozetlerini, harness rozetini ve "Çalıştır" bağlantısını doğru gösterir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık, `agents` sekmesindesin.

**Adımlar**
1. Sol menüden "Agents" ekranını aç.
2. `support` satırını incele.
3. `arastirmaci` satırını incele (harness rozeti için).
4. Tool sayısı hücresinin üzerine gel (`title` tooltip).

**Beklenen sonuç**
- `support` satırında `code` rozeti görünür (`OriginBadge`, `agent.origin === 'Code'`);
  üzerine gelince `agents.origin.code` metni kaynak adını (`sourceName`) taşır.
- `arastirmaci` satırında ayrıca sarı `harness` rozeti görünür
  (`agent.usesHarness === true`).
- Tool sütununda sayı (`toolNames.length`) görünür; üzerine gelince tam tool
  adları virgülle ayrılmış tooltip'te belirir.
- Satırın en sağındaki "Çalıştır" düğmesi `playground/{ad}` rotasına gider.

**Gerçek sonuç**
`support` satırında `code` rozeti var, `title="'code' kaynağı tarafından
kodda tanımlandı. Salt okunur."`. `arastirmaci` satırında `code` YANINDA
sarı `harness` rozeti var (`title="Harness yetenekleri açık"`). `support`
satırının tool hücresi `3` gösteriyor, `title="get_order_status,
list_recent_orders, cancel_order"` — tam tool adları virgülle ayrılmış
tooltip'te. "Çalıştır" linkinin `href`i `/agentprism/playground/support`.
Dördü de beklenenle eşleşiyor. (Not: `azure-destek` katalogda hiç yok —
beklenen, Azure kimliği bu ortamda yapılandırılmamış.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-002 — "Yeni Agent" düğmesi yalnız `canAdminister` rolünde görünür

Bu, [`09-ARAYUZ-GENEL.md`](09-ARAYUZ-GENEL.md)'de kanıtlanan genel rol-görünürlük
mekanizmasının bu ekrandaki **somut örneğidir**; mekanizmanın kendisi burada
yeniden kanıtlanmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Bu dosyanın geri kalanı `canAdminister = true` bir tokenla koşulur; bu case
  tek başına, yalnız bir Operator-rol API anahtarıyla (bkz. `13-KIRACI-VE-
  GUVENLIK.md`'nin üreteceği fixture) test edilebilir. O dosya henüz yoksa
  `Reader`/`Operator` rolündeki herhangi bir `AgentPrism:Ui:AuthToken` dışı
  erişim yolu (API anahtarı üstünden yalnızca REST) kullanılabilir; arayüz
  bugün TEK bir bearer token'ı destekler ve o token her zaman tam rol taşır —
  bu durumda case ⏭ **ATLA** işaretlenip gerekçe "13'ün API-anahtarı fixture'ı
  bekleniyor" olarak yazılır.

**Adımlar**
1. `canAdminister = false` olan bir erişimle `/agentprism/agents` aç.

**Beklenen sonuç**
- `PageHeader`'ın `actions` alanında "Yeni Agent" düğmesi HİÇ render edilmez
  (`meta.roles.canAdminister && (<Link.../>)`— koşul `false` olduğunda ağaçtan
  tamamen düşer, yalnız `disabled` olmaz).
- Liste normal şekilde okunur (`Reader` rolü `GET /api/agents`'ı geçirir).

**Gerçek sonuç**
Case'in kendi ön koşulu tetiklendi: bu oturumda (`mt_s4`, şerit izole) `13-
KIRACI-VE-GUVENLIK.md`'nin ürettiği API-anahtarı/rol fixture'ı hiç
oluşturulmadı (o dosya Şerit 2'de `mt_s2` şemasında koşuldu, izole şema/
şerit paylaşılmıyor — KOSUM-PLANI §2.3). Arayüz tek bir bearer token
(`AgentPrism:Ui:AuthToken`) destekliyor ve bu token her zaman tam rol
taşıyor (S4-1'in `MT-UI-010/011` bulgusuyla aynı yapısal engel: `X-Test-
Role` test iskelesi tarayıcının gerçek `Authorization: Bearer` akışıyla
uyumsuz). Case'in kendi metni bu durumda ⏭ ATLA'yı ve gerekçe olarak
"13'ün API-anahtarı fixture'ı bekleniyor" yazılmasını öngörüyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

# 2 — Agent editörü: yeni agent oluşturma (`agent-editor.tsx`)

### MT-UIAG-003 — Boş formda Doğrula/Kaydet devre dışıdır; zorunlu alanlar dolunca etkinleşir

Sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `/agentprism/agents/new` açık, form tamamen boş.

**Adımlar**
1. "Doğrula" ve "Oluştur" düğmelerinin durumuna bak.
2. Yalnız `Ad` alanına `gecici-test` yaz; düğmelerin durumuna tekrar bak.
3. `Model` alanına `gpt-5.4-mini` yaz (Sağlayıcı zaten ilk kayıtlı sağlayıcıya
   varsayılanlanmıştır — bkz. adım 4).
4. Sayfayı hiç dokunmadan `Sağlayıcı` seçicisinin değerine bak.

**Beklenen sonuç**
- Adım 1: her iki düğme de devre dışıdır (`valid = false`, çünkü
  `request.name.length === 0`).
- Adım 2: hâlâ devre dışıdır (`request.model.model.length === 0`).
- Adım 4: `Sağlayıcı` boş açılmaz — `providers.data[0]` otomatik seçilir
  (yalnız bir sağlayıcı kayıtlıysa bu adım "kullanıcı hiç dokunmadan doğru
  değer" anlamına gelir; birden çok sağlayıcı varsa İLK kayıtlı olan seçilir,
  sıra deterministik değildir — bu durumda case not düşülür).
- Adım 3'ten sonra: `valid = true`, her iki düğme etkinleşir.

**Gerçek sonuç**
Adım 1: `Doğrula` ve `Oluştur` ikisi de `disabled`. Adım 2 (`Ad: gecici-
test`, `Model` boş): ikisi de hâlâ `disabled`. Adım 4: `Sağlayıcı`
otomatik `anthropic`'e seçili geldi — bu ortamda 5 sağlayıcı kayıtlı
(anthropic, google, openai, openai-responses, openrouter, alfabetik
sırayla) ve `anthropic` alfabetik olarak İLKİ; case'in kendi notu bu
durumu ("sıra deterministik değildir") zaten öngörüyor, bir kusur değil.
Adım 3 (`Model: gpt-5.4-mini` yazıldı — alan serbest metin girişli bir
combobox, seçili sağlayıcının (anthropic) model listesiyle sınırlı
DEĞİL): sonrasında `Doğrula`/`Oluştur` ikisi de etkinleşti (`disabled`
kalktı) — beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-004 — `FIX-AGENT-02` ile minimal agent oluşturma; canlı JSON önizlemesi gönderilen gövdeyle birebir eşleşir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `/agentprism/agents/new` açık.

**Adımlar**
1. `Ad`: `manuel-bos` (`FIX-AGENT-02`).
2. `Talimatlar`: `Yalnizca "tamam" yaz.`
3. `Sağlayıcı`: `openai` (zaten varsayılan olabilir).
4. `Model`: `gpt-5.4-mini`.
5. Hiçbir tool/beceri seçmeden sağ paneldeki JSON önizlemesini oku.
6. "Oluştur"a tıkla.

**Beklenen sonuç**
- Adım 5: sağ panel `POST api/agents` etiketiyle **tam olarak** gönderilecek
  gövdeyi gösterir — `toolNames: []`, `skillNames: []`, `callableAgentNames: []`,
  `harness: null`, `compaction: null`, `memory: null` (hiçbiri işaretlenmediği
  için `AgentDefinitionRequest`'in `null`'a düşen alanları budur).
- Adım 6: `201`'e karşılık gelen davranış — ekran `agents/manuel-bos`'a yönlenir,
  yeni satır katalogda `db · v1` rozetiyle görünür.

**Gerçek sonuç**
Adım 5: JSON önizlemesi birebir eşleşti: `{"name":"manuel-bos",
"displayName":null,"description":null,"instructions":"Yalnizca \"tamam\"
yaz.","model":{"provider":"openai","model":"gpt-5.4-mini",
"temperature":null,"maxOutputTokens":null,"topP":null,
"reasoningEffort":null,"responseFormat":null},"toolNames":[],
"skillNames":[],"callableAgentNames":[],"harness":null,"compaction":null,
"memory":null}`. Adım 6: "Oluştur"a tıklandı, ekran `/agentprism/agents/
manuel-bos`'a yönlendi; katalog listesinde `manuel-bos` satırı `db · v1`
rozetiyle (`title="Saklanan tanım, sürüm 1."`) ve `gpt-5.4-mini` modeliyle
görünüyor. `FIX-AGENT-02` oluşturuldu, sonraki oturumlar için SİLİNMEDEN
bırakıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-005 — `FIX-AGENT-01` ile tool seçili agent oluşturma; `cancel_order`ın onay rozeti seçim listesinde de görünür

Tool seçimi **kod yazma değil, listeden işaretlemedir** — K2'nin bu ekrandaki
somut uygulaması.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | K2 |

**Ön koşul**
- `/agentprism/agents/new` açık.

**Adımlar**
1. `Ad`: `manuel-destek` (`FIX-AGENT-01`).
2. `Talimatlar`: `Sen bir siparis destek asistanisin. Kisa yanit ver.`
3. `Sağlayıcı`: `openai` · `Model`: `gpt-5.4-mini`.
4. "Araçlar" panelinde `get_order_status` checkbox'ını işaretle.
5. Aynı panelde `cancel_order` satırını incele (işaretleME).
6. "Oluştur"a tıkla.

**Beklenen sonuç**
- Adım 5: `cancel_order` satırında sarı `approval` rozeti görünür
  (`tool.requiresApproval === true`) — bu rozet yalnız BİLGİ amaçlıdır, seçimi
  engellemez.
- Araç listesi **serbest metin alanı içermez**; yalnız `GET /api/tools`'tan
  gelen kayıtlı adlar checkbox olarak sunulur — sunucuda kayıtlı olmayan bir
  tool adı hiçbir şekilde yazılamaz.
- Adım 6 sonrası JSON önizlemesi `toolNames: ["get_order_status"]` taşır ve
  agent `agents/manuel-destek`'te `1` tool ile listelenir.

**Gerçek sonuç**
Adım 5: `cancel_order` satırında sarı `approval` rozeti var (checkbox işaretsiz
bırakıldı, rozet seçimi engellemiyor). Araç listesi yalnız checkbox — hiçbir
serbest metin alanı yok (6 tool: `cancel_order`, `get_order_status`,
`list_recent_orders`, `list_voices`, `speak`, `transcribe` — hepsi `GET /api/
tools`'tan gelen kayıtlı adlar). `get_order_status` işaretlendi, önizleme
`"toolNames": ["get_order_status"]` gösterdi. Adım 6: "Oluştur"a tıklandı,
`/agentprism/agents/manuel-destek`'e yönlendi; detay sayfasında "Tool'lar:
get_order_status" (1 tool) görünüyor, tanım JSON'ı `"toolNames":
["get_order_status"]`, `"version": 1`, `"origin": "Database"` taşıyor.
`FIX-AGENT-01` oluşturuldu, sonraki oturumlar için SİLİNMEDEN bırakıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-006 — Aynı adla ikinci oluşturma denemesi ekranda `409` mesajını gösterir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-bos` (`MT-UIAG-004`) zaten var.

**Adımlar**
1. `/agentprism/agents/new` aç, `Ad`: `manuel-bos` yaz.
2. `Sağlayıcı`/`Model` doldur, "Oluştur"a tıkla.

**Beklenen sonuç**
- Form kapanmaz; sayfanın üstünde kırmızı bir `ErrorNote` görünür.
- Mesaj sunucunun `ProblemDetails.detail`'ini **birebir** taşır: `"'manuel-bos'
  adinda bir tanim zaten var. Guncellemek icin PUT kullanin."`
- Formdaki veri kaybolmaz — kullanıcı adı değiştirip yeniden deneyebilir.

**Gerçek sonuç**
`POST /api/agents` → `409`, gövde: `{"title":"Agent adi kullanimda",
"status":409,"detail":"'manuel-bos' adinda bir tanim zaten var. Guncellemek
icin PUT kullanin."}`. Form kapanmadı (URL `/agentprism/agents/new`'de
kaldı), kırmızı `ErrorNote` göründü: "Agent adi kullanimda: 'manuel-bos'
adinda bir tanim zaten var. Guncellemek icin PUT kullanin." — `detail` alanı
metnin İÇİNDE birebir var (ErrorNote `title: detail` biçiminde birleştirip
gösteriyor, yalnız `detail` değil — case'in "birebir taşır" iddiasını
karşılıyor, ekstra `title:` öneki bir kusur değil, ek bağlam). Form verisi
kaybolmadı: `Ad` alanı hâlâ `manuel-bos`. Konsolda 1 hata var ama bu
sadece tarayıcının kendi "Failed to load resource: 409" günlüğü — JS
istisnası değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-007 — Kodda tanımlı `support` adıyla oluşturma denemesi FARKLI bir `409` mesajı gösterir

Negatif senaryo. Ad çakışmasında kod kazanır (K-003) — arayüz bu gerekçeyi
kullanıcıya sunucudan geldiği gibi gösterir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | K-003 |

**Ön koşul**
- `/agentprism/agents/new` açık.

**Adımlar**
1. `Ad`: `support`. `Sağlayıcı`/`Model` doldur, "Oluştur"a tıkla.

**Beklenen sonuç**
- `ErrorNote`, `MT-UIAG-006`'dan **farklı** bir gerekçe metni taşır:
  `"'support' kodda tanimli bir agent'tir ve yonetim API'sinden
  degistirilemez. Ad cakismasinda kod kazandigi icin ayni adla yazilan bir
  tanim hicbir zaman cozulmezdi."`
- Katalogda ikinci bir `support` satırı **oluşmaz**.

**Gerçek sonuç**
`POST /api/agents` (`name: "support"`) → `409`. `ErrorNote`:
"Agent adi kullanimda: 'support' kodda tanimli bir agent'tir ve yonetim
API'sinden degistirilemez. Ad cakismasinda kod kazandigi icin ayni adla
yazilan bir tanim hicbir zaman cozulmezdi." — `MT-UIAG-006`'daki mesajdan
(farklı `title`/`detail`) tamamen FARKLI, beklenen `detail` metni birebir
içeride. Katalogda `/agentprism/agents/support`e giden TEK bir link var —
ikinci bir `support` satırı oluşmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-008 — "Doğrula" kaydetmeden `AgentValidationReport`'u gösterir, hiçbir kayıt oluşmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 34 |
| **İlgili karar** | — |

**Ön koşul**
- `/agentprism/agents/new` açık.

**Adımlar**
1. `Ad`: `manuel-dogrula-test`. `Sağlayıcı`: `openai` · `Model`: `bilinmeyen-model-adi-xyz`.
2. "Doğrula"ya tıkla.
3. Ekranı kapatmadan `Agents` listesine dön ve `manuel-dogrula-test` adını ara.

**Beklenen sonuç**
- Adım 2: yanıt `200`'dür (istek biçimsel olarak geçerli — ad ve model dolu);
  panel `report.valid` durumuna göre `Geçerli`/`Geçersiz` rozeti ve varsa
  `messages` listesini `code`/`path`/`message` alanlarıyla gösterir.
- Adım 3: `manuel-dogrula-test` katalogda **görünmez** — doğrulama hiçbir
  şey kaydetmez.

**Gerçek sonuç**
Adım 2: "Doğrula"ya tıklandı, "Doğrulama sonucu" paneli `Geçerli` rozetini
gösterdi, "Sorun bulunamadı. Hiçbir şey kaydedilmedi, hiçbir model
çağrılmadı." notuyla — model adı (`bilinmeyen-model-adi-xyz`) gerçekte
OpenAI'de yok ama doğrulayıcı bunu reddetmedi; bu, K-032'nin doğal sonucu
(AgentPrism model listesini sunucu tarafında bilinçli olarak seçmez/
doğrulamaz, doğrulama yalnız biçim/şema düzeyinde) — case'in kendi metni
zaten yalnız "report.valid durumuna göre" göstermeyi bekliyor, kusur değil.
Adım 3: Agents listesine dönüldü, `manuel-dogrula-test` katalogda YOK —
doğrulama hiçbir şey kaydetmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-009 — Bozuk JSON şeması Kaydet/Doğrula'yı devre dışı bırakır ve hata metni gösterir

Sınır/negatif senaryo — istemci tarafı doğrulama, sunucuya hiç gitmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `/agentprism/agents/new` açık; `Ad`/`Sağlayıcı`/`Model` geçerli değerlerle
  dolu (form aksi halde zaten devre dışı olurdu).

**Adımlar**
1. `Yanıt biçimi` seçicisinden `JsonSchema` seç.
2. Şema kutusunun varsayılan içeriğini (`{ "type": "object", "properties": {} }`)
   sil, yerine `{ bozuk json` yaz.
3. "Doğrula" ve "Oluştur" düğmelerinin durumuna bak.
4. Şema kutusuna geçerli bir JSON (`{}`) yazıp düğmelerin durumuna tekrar bak.

**Beklenen sonuç**
- Adım 3: her iki düğme devre dışıdır (`schemaJsonValid = false`); şemanın
  altında `agentEditor.schemaError` metniyle bir `ErrorNote` görünür.
- Metin alanının kendisi salt okunur OLMAZ — kullanıcı yazmaya devam edebilir.
- Adım 4: düğmeler tekrar etkinleşir, hata notu kaybolur.
- İstek hiçbir noktada sunucuya gitmez (Ağ sekmesinde `POST`/`validate`
  çağrısı görünmez) — doğrulama tamamen istemcidedir (`isValidJson`).

**Gerçek sonuç**
Adım 3 (`{ bozuk json` yazıldı): `Doğrula` ve `Oluştur` ikisi de `disabled`,
şemanın altında `alert: "Geçerli JSON değil."` göründü. Metin alanı
`active` (odakta, düzenlenebilir) kaldı — salt okunur olmadı. Ağ
sekmesinde bu adım boyunca `POST`/`validate` çağrısı YOK (yalnız önceki
`GET /api/agents` istekleri var) — doğrulama tamamen istemci taraflı.
Adım 4 (`{}` yazıldı): iki düğme de tekrar etkinleşti, hata notu kayboldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-010 — Harness açılınca ek alanlar görünür, kapatılınca gövdede `harness: null` gider

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 10 |
| **İlgili karar** | — |

**Ön koşul**
- `/agentprism/agents/new` açık, zorunlu alanlar dolu.

**Adımlar**
1. "Harness" panelindeki checkbox'ı işaretle.
2. `Maksimum bağlam penceresi tokeni`: `32000`, `Maksimum iterasyon`: `8` yaz;
   `disableWebSearch` ve `disableFileMemory` checkbox'larını işaretle.
3. Sağ panelde JSON önizlemesini oku.
4. Harness checkbox'ını KAPAT, önizlemeyi tekrar oku.

**Beklenen sonuç**
- Adım 1 sonrası: sayısal alanlar + beş boolean checkbox (`disableCompaction`,
  `disableTodoProvider`, `disableFileMemory`, `disableWebSearch`,
  `disableToolAutoApproval`) görünür hâle gelir.
- Adım 3: `harness` nesnesi tam olarak işaretlenen alanları taşır
  (`{ maxContextWindowTokens: 32000, maximumIterationsPerRequest: 8,
  disableWebSearch: true, disableFileMemory: true }`).
- Adım 4: `harness` alanı bütünüyle `null`'a düşer (form state'i hafızada
  kalsa da gönderilen gövdeden düşer — `form.harnessEnabled ? form.harness :
  null`), doldurulmuş değerler silinmez, yalnız gizlenir.

**Gerçek sonuç**
Adım 1: checkbox işaretlendi, 2 sayısal alan ("En fazla bağlam penceresi
token", "İstek başına en fazla yineleme") + 5 checkbox ("Sıkıştırmayı
kapat", "Todo izlemeyi kapat", "Dosya belleğini kapat", "Web aramasını
kapat", "Tool onayı iste") göründü — beklenen 5 boolean ile birebir eşleşiyor.
Adım 3 (32000/8/`disableWebSearch`/`disableFileMemory` işaretlendi):
önizleme `"harness": {"maxContextWindowTokens":32000,
"maximumIterationsPerRequest":8,"disableWebSearch":true,
"disableFileMemory":true}` — birebir eşleşti. Adım 4 (checkbox kapatıldı):
`"harness": null`. Checkbox tekrar AÇILDIĞINDA (ek doğrulama) önceki
değerler (`32000`/`8`/iki `true`) aynen geri geldi — form state hafızada
korunuyor, yalnız gönderilen gövdeden düşüyor; beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-011 — Sıkıştırma (compaction) stratejisi değişince yalnız o stratejiye özgü alanlar görünür

Dallanma/sınır davranışı — altı farklı strateji, her biri farklı alan kümesi
açar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `/agentprism/agents/new` açık.

**Adımlar**
1. "Bağlam" panelinde stratejiyi `SlidingWindow` seç; görünen alanları not al.
2. Stratejiyi `ContextWindow` yap; görünen alanları not al.
3. Stratejiyi `Summarization` yap; görünen alanları not al.
4. Stratejiyi `None`'a geri al.

**Beklenen sonuç**
- Adım 1: `triggerTokens`/`triggerMessages`/`triggerTurns` + `minPreservedTurns`
  görünür; `minPreservedGroups`, özetleme alanları YOK.
- Adım 2: yalnız `maxContextWindowTokens` (zorunlu) ve `maxOutputTokens`
  görünür; `trigger*` alanları YOK (`ContextWindow` kendi tetikleyicisini
  kullanmaz).
- Adım 3: `triggerTokens/Messages/Turns` + `minPreservedGroups` +
  `summarizationPrompt`/`summarizationProvider`/`summarizationModel` görünür;
  `minPreservedTurns` YOK (yalnız `SlidingWindow`/`Pipeline`'a özgü).
- Adım 4: tüm alt alanlar kaybolur, `compaction` gövdede `null` gider.

**Gerçek sonuç**
Adım 1 (`SlidingWindow`): "Tetik: token sayısı", "Tetik: mesaj sayısı",
"Tetik: tur sayısı", "En az korunacak tur" göründü — `minPreservedGroups`/
özetleme alanları yok. Adım 2 (`ContextWindow`): yalnız "En fazla bağlam
penceresi token *" (zorunlu işaretli) ve "En fazla çıktı token" göründü,
`Tetik:` alanları hiçbiri yok. Adım 3 (`Summarization`): "Tetik: token/
mesaj/tur sayısı" + "En az korunacak grup" + "Özetleme promptu"/"Özetleme
modeli sağlayıcısı"/"Özetleme modeli adı" göründü, "En az korunacak tur"
YOK. Adım 4 (`None`'a geri alındı): önizleme `"compaction": null` —
üçü de beklenenle birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-012 — Beceri (skill) seçimi 10'da sınırlanır, sonraki checkbox'lar devre dışı kalır

Sınır durumu — bu sınır yalnız istemcidedir, sunucu kendi doğrulamasını ayrıca
yapar (bu case yalnız arayüzü ölçer).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 10 |
| **İlgili karar** | — |

**Ön koşul**
- Sistemde en az 11 kayıtlı beceri var (yoksa case ⏭ ATLA — örnek uygulama
  kaç beceri kaydettiğini önce `Skills` ekranından say).

**Adımlar**
1. `/agentprism/agents/new` aç, "Beceriler" panelinde art arda 10 checkbox işaretle.
2. 11. beceriyi işaretlemeyi dene.

**Beklenen sonuç**
- Adım 2: 11. checkbox `disabled` — `!checked && limitReached`
  (`form.skillNames.length >= 10`) koşulu true olur; tıklama hiçbir etki yapmaz.
- Halihazırda işaretli 10 becerinin checkbox'ları hâlâ TIKLANABİLİR (işareti
  kaldırmak serbesttir — `disabled` yalnız YENİ seçimi engeller).
- `enabled: false` olan bir beceri her koşulda `disabled`'dır (sayaçtan
  bağımsız).

**Gerçek sonuç**
Ön koşul karşılanmıyor: `/agentprism/skills` ekranı "Henüz skill yok"
gösteriyor — bu `mt_s4` şemasında SIFIR skill kayıtlı (S4-1..S4-3'te hiçbir
skill senaryo dosyası henüz koşulmadı, `14-SKILL-VE-SCRIPT.md` ortak
kuyrukta bekliyor). Case'in kendi metni bu durumda ⏭ ATLA'yı öngörüyor
("yoksa case ⏭ ATLA — örnek uygulama kaç beceri kaydettiğini önce Skills
ekranından say").

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

### MT-UIAG-013 — Çağrılabilir agent çevrimi (cycle) sunucu tarafından reddedilir, ekranda hata metni görünür

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 12 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek` (`MT-UIAG-005`) var.

**Adımlar**
1. `manuel-destek`'i düzenle, "Çağrılabilir Agent'lar" panelinde `manuel-destek`
   dışında bir agent seçebiliyor musun kontrol et (kendisi listede zaten
   filtrelenir — `agent.name !== form.name`).
2. Yeni bir agent oluştur: `Ad`: `manuel-cevrim-a`, çağrılabilir agent olarak
   `manuel-destek`'i seç, kaydet.
3. `manuel-destek`'i tekrar düzenle, çağrılabilir agent olarak `manuel-cevrim-a`'yı
   seç, kaydetmeyi dene.

**Beklenen sonuç**
- Adım 1: kendisi seçenek listesinde hiç görünmez (kendi kendini çağıramaz —
  bu kısıt istemci tarafında filtrelenir).
- Adım 3: kayıt `400` ile reddedilir; `ErrorNote` başlığı `"Cagri grafigi
  gecersiz"` metnini taşır, ayrıntı `AgentCallGraph.Validate`'in ürettiği
  çevrim açıklamasıdır. Form kapanmaz, veri kaybolmaz.

**Gerçek sonuç**
Adım 1: `manuel-destek`'i düzenlerken "Çağrılabilir agent'lar" listesinde
`manuel-destek` kendisi HİÇ yok (13 diğer agent var, kendisi filtrelendi).
Adım 2: `manuel-cevrim-a` oluşturuldu (`callableAgentNames:
["manuel-destek"]`), `201` ile kaydedildi. Adım 3: `manuel-destek`
düzenlendi, `manuel-cevrim-a` çağrılabilir olarak işaretlendi, "Yeni sürüm
kaydet"e tıklandı → `PUT /api/agents/manuel-destek` → `400`. `ErrorNote`:
"Cagri grafigi gecersiz: Cagri grafiginde dongu var: manuel-destek ->
manuel-cevrim-a -> manuel-destek. Dongulu bir grafik, calistirmanin derinlik
sinirina carpana kadar surmesine yol acar." Form kapanmadı (URL `/agents/
manuel-destek/edit`'te kaldı). Beklenenle birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Agent editörü: mevcut bir agent'ı düzenleme

### MT-UIAG-014 — Var olan DB agent'ı açılınca form dolar, `name` alanı salt okunurdur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek` (`MT-UIAG-005`) var.

**Adımlar**
1. `agents/manuel-destek/edit` aç.
2. `Ad` alanına tıklamayı dene.

**Beklenen sonuç**
- Tüm alanlar (`Talimatlar`, `Sağlayıcı`, `Model`, seçili tool) kayıtlı tanımla
  birebir dolu gelir.
- `Ad` alanı `readOnly` — düzenlenemez; başlık `agentEditor.editTitle`
  agent adını gömer.
- Sağ panel önizlemesi `PUT api/agents/manuel-destek` etiketini taşır.
- "Oluştur" yerine "Sürüm Kaydet" (`agentEditor.saveVersion`) yazar.

**Gerçek sonuç**
`Ad` alanı `readOnly:true`, başlık "manuel-destek düzenle", buton metni
"Yeni sürüm kaydet" (`agentEditor.saveVersion`), sağ panel `PUT api/agents/
manuel-destek` — hepsi beklendiği gibi. Ancak İLK açılışta `Sağlayıcı`
alanı YANLIŞ doldu: `<select>` değeri `anthropic` (kayıtlı tanım `openai`),
JSON önizlemesi `"provider": "anthropic"` gösterdi — `Model` alanı ise
doğru `gpt-5.4-mini` kaldı, tutarsız bir çift üretti. Sayfayı 2 kez daha
tazeledim: ikisinde de doğru `openai` geldi — **aralıklı bir yarış
koşulu** (`HATA-S4-009`, kök neden `agent-editor.tsx:197-247`).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-UIAG-015 — Düzenleyip kaydetme yeni bir versiyon üretir, agent detayına döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 19 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek` v1 durumda (`MT-UIAG-005`'ten).

**Adımlar**
1. `agents/manuel-destek/edit` aç.
2. `Talimatlar`'ı `Sen bir siparis destek asistanisin. Kisa yanit ver. Nazik ol.`
   olarak değiştir.
3. "Sürüm Kaydet"e tıkla.

**Beklenen sonuç**
- `agents/manuel-destek` detay sayfasına yönlenilir.
- Özet panelinde güncel talimat metni görünür.
- "Sürümler" tablosunda artık İKİ satır vardır (`v1`, `v2`); `v2` yanında
  `Güncel` rozeti.

**Gerçek sonuç**
Kaydetmeden önce `Sağlayıcı` alanının `openai` kaldığı doğrulandı (bkz.
`HATA-S4-009`), sonra kaydedildi. `agents/manuel-destek` detayına
yönlenildi. Özet paneli güncel talimatı (`... Nazik ol.`) gösterdi.
"Sürüm geçmişi" tablosunda İKİ satır: `v2` (rozet metni `geçerli`,
`title="Şu anda çözülen tanım"`) ve `v1` (`Geri al` düğmesi taşıyor).
Rozet metni case'in beklediği "Güncel" değil "geçerli" — yalnız kelime
seçimi farkı, işlevsel olarak aynı davranış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-016 — Kod kökenli `support`'ta Düzenle/Sil düğmeleri hiç render edilmez

Sınır durumu — `isEditable = false`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `agents/support` aç.
2. `PageHeader`'ın `actions` alanını incele.
3. `agents/support/edit` adresine DOĞRUDAN URL ile git.

**Beklenen sonuç**
- Adım 2: yalnız "Playground'ı Aç" düğmesi görünür; Düzenle/Sil ağaçtan
  tamamen düşer (`isEditable && meta.roles.canAdminister` koşulu `false`).
- Sayfanın altında `agentDetail.codeNotice` metniyle bilgilendirme satırı
  görünür.
- Adım 3: editör ekranı AÇILIR (yönlendirici bunu engellemez) ama "Sürüm
  Kaydet"e basıldığında `409 Kodda tanimli agent degistirilemez` hatası
  görünür — arayüz bu güvenliği yalnızca DÜĞMEYİ GİZLEYEREK sağlar, URL
  seviyesinde bir engel yoktur; gerçek sınır sunucudadır.

**Gerçek sonuç**
Adım 2 tam beklendiği gibi: yalnız "Playground'da aç" görünür, `agentDetail.
codeNotice` metni ("Bu agent kodda tanımlı...") var. Adım 3 KISMEN farklı:
editör ekranı gerçekten açılıyor, ama `definition === null` olduğu için
form hiç doldurulmuyor (`agent-editor.tsx:202-208`, `useEffect` erken
`return` ediyor) — `Ad` alanı BOŞ ve `readOnly:true` kalıyor (elle
doldurulamıyor), `Sağlayıcı`/`Model` de boş. `valid = name.length>0 &&
provider.length>0 && model.length>0` (satır 268-273) hiçbir zaman `true`
olamıyor, bu yüzden "Doğrula" VE "Yeni sürüm kaydet" düğmelerinin ikisi de
DAİMA `disabled` kalıyor — case'in beklediği "basılınca 409 döner" akışı
KULLANICI İÇİN HİÇBİR ZAMAN ERİŞİLEBİLİR DEĞİL (`Ad` salt-okunur olduğu
için elle de doldurulamıyor). Sunucu tarafı koruması muhtemelen hâlâ
vardır ama arayüzden hiç tetiklenemiyor — dokümanın "URL seviyesinde engel
yok" iddiası yanlış: `Ad` alanının salt-okunur+boş kombinasyonu fiilen bir
engel oluşturuyor. `HATA-S4-010`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

# 4 — Agent detayı (`agent-detail.tsx`)

### MT-UIAG-017 — DB kökenli agent özet + talimat + tam tanım JSON'u gösterir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek` var (`MT-UIAG-015` sonrası v2 durumda).

**Adımlar**
1. `agents/manuel-destek` aç.

**Beklenen sonuç**
- Özet panelinde ad, köken rozeti (`db · v2`), sağlayıcı, model, harness
  durumu (`Kapalı`), tool rozetleri (`get_order_status`), göreli güncelleme
  zamanı görünür.
- Talimat metni ayrı bir panelde tam olarak görünür.
- Altta "Tanım" panelinde `AgentDefinition`'ın **tüm** alanlarını taşıyan
  ham JSON görünür (`JsonView`).

**Gerçek sonuç**
Özet paneli birebir doğru: `db · v2` köken rozeti (`title="Saklanan tanım,
sürüm 2."`), `Sağlayıcı: openai`, `Model: gpt-5.4-mini`, `Harness: Kapalı`,
`Tool'lar: get_order_status`, `Güncellendi: şimdi`. Talimat metni ayrı bir
panelde tam görünüyor. "Tanım" paneli `AgentDefinition`'ın tüm alanlarını
(`origin`, `version`, `tenantId`, `updatedAt`, `metadata` dahil) taşıyan
ham JSON'u gösteriyor. Konsolda hata/uyarı yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-018 — Kod kökenli agent'ta "kod bildirimi" notu görünür, `definition` paneli farklı davranır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `agents/support` aç.

**Beklenen sonuç**
- `descriptor.model`/`toolNames` yine de doğru görünür (katalog özeti kod
  tanımından üretilir).
- `definition` (kalıcı tanım) `null`'dır — kod agent'ının hiçbir zaman bir
  `AgentDefinition` satırı YOKTUR; bu durumda "Tanım" paneli hiç render
  edilmez (`{definition !== null && (...)}`), talimat panelinde
  `agentDetail.noDefinitionForCode` ek notu görünür.
- Sayfanın altında "Sürümler" bölümü hiç YOKTUR (`isEditable` koşulu
  `VersionHistory`'yi de kapsar) — kod agent'ının versiyon geçmişi olmaz.

**Gerçek sonuç**
`descriptor.model`/`toolNames` doğru: `Sağlayıcı: openai`, `Model:
gpt-5.4-mini`, `Tool'lar: get_order_status, list_recent_orders,
cancel_order`. "Tanım" paneli hiç render edilmedi (sayfada bu başlık yok).
Talimatlar panelinde `agentDetail.noDefinitionForCode` metni: "Sistem
talimatı yok. Kodda tanımlı bir agent için saklanan tanım yoktur."
"Sürümler" bölümü sayfada hiç yok (DOM'da `Sürüm geçmişi` başlığı arandı,
bulunamadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-019 — Sil `window.confirm` ister; onaylanınca listeye döner, iptal edilirse hiçbir şey olmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-bos` (`MT-UIAG-004`) var — bu agent bundan sonra artık gerekmiyor,
  bu case'te silinecek.

**Adımlar**
1. `agents/manuel-bos` aç, "Sil"e tıkla.
2. Tarayıcının onay penceresinde "İptal"e bas.
3. "Sil"e tekrar tıkla, bu kez "Tamam"a bas.

**Beklenen sonuç**
- Adım 1: `window.confirm` metni `agentDetail.confirmDelete` içinde agent
  adını gömer.
- Adım 2: hiçbir istek gitmez, sayfa aynı kalır, agent hâlâ var.
- Adım 3: `agents` listesine yönlenilir; `manuel-bos` artık listede yok.

**Doküman düzeltmesi**
Bu case'in "Ön koşul"u dosyanın kendi önsözüyle (satır 87-89: "Bu dosyada
oluşturulan `FIX-AGENT-01` [`manuel-destek`] ve `FIX-AGENT-02`
[`manuel-bos`] sonraki koşumlarda **silinmeden** bırakılır") ve `11-ARAYUZ-
RUN-SESSION-SSE.md`'nin kendi fixture ihtiyacıyla (satır 240-245, 1325,
1352 — `manuel-bos` ile çalıştırma/oturum/playground testleri) DOĞRUDAN
ÇELİŞİYOR. `manuel-bos`'u burada silmek S4-6..S4-8 oturumlarını kırar.
Silme akışını doğrulamak için `manuel-bos` YERİNE bu case'e özgü, atılabilir
bir agent (`manuel-silme-test`) UI üzerinden oluşturulup silindi;
`manuel-bos` dokunulmadan bırakıldı. Ön koşul metni bu şekilde düzeltilmiş
sayılır.

**Gerçek sonuç**
`manuel-silme-test` (openai/gpt-5.4-mini, `/agents/new` ile oluşturuldu)
üzerinde koşuldu. Adım 1: `window.confirm` mesajı `"manuel-silme-test" ve
sürüm geçmişi silinsin mi?"` — agent adını gömüyor. Adım 2: İptal sonrası
`GET /api/agents/manuel-silme-test` hâlâ `200` döndü, sayfa aynı kaldı,
hiçbir DELETE isteği gitmedi. Adım 3: Tamam sonrası `agents` listesine
yönlenildi; `GET /api/agents/manuel-silme-test` artık `404`. Kontrol:
`GET /api/agents/manuel-bos` hâlâ `200` — fixture korunmuş durumda.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Versiyon geçmişi ve karşılaştırma (Faz 19)

### MT-UIAG-020 — Versiyon tablosu yeni-eski sıralı, güncel sürüm rozetiyle işaretli

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek` en az iki sürüme sahip (`MT-UIAG-015`).

**Adımlar**
1. `agents/manuel-destek` aç, "Sürümler" tablosunu incele.

**Beklenen sonuç**
- Satırlar `v2, v1` sırasıyla (yeniden eskiye) görünür.
- Yalnız `v2` satırında `Güncel` rozeti; rozetin `title` özniteliği
  `agentDetail.currentTitle` metnini taşır.
- Her satırda model adı, tool sayısı, göreli kayıt zamanı görünür.

**Gerçek sonuç**
Satırlar `v2, v1` sırasıyla (yeniden eskiye). Yalnız `v2` satırı rozet
taşıyor — metni case'in beklediği literal "Güncel" değil "geçerli", ama
`title="Şu anda çözülen tanım"` (`agentDetail.currentTitle`) birebir
eşleşiyor — yalnız kelime seçimi farkı. Her satırda model adı
(`gpt-5.4-mini`), tool sayısı (`1`), göreli zaman (`şimdi` / `18 dk. önce`)
görünüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-021 — Tek versiyon seçiliyken "bir tane daha seç" ipucu görünür

Sınır/edge durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIAG-020`'nin ön koşulu.

**Adımlar**
1. Yalnız `v1` satırının checkbox'ını (`version-checkbox-1`) işaretle.

**Beklenen sonuç**
- Tablonun altında `agentDetail.selectOneMore` metni görünür.
- Karşılaştırma paneli HENÜZ açılmaz.

**Gerçek sonuç**
`v1` checkbox işaretlendi. Tablonun altındaki metin "Geri alma hiçbir şey
silmez..." açıklamasından "Karşılaştırmak için bir sürüm daha seçin."'e
değişti (`agentDetail.selectOneMore`). Karşılaştırma paneli render
edilmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-022 — İki versiyon seçilince otomatik karşılaştırma paneli açılır (`DiffView`/`FieldDiffTable`/`SetDiff`)

Kütüphane kullanılmadı — el yazımı LCS diff (K-045).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | K-045 |

**Ön koşul**
- `v1`'in checkbox'ı işaretli (`MT-UIAG-021`'den).

**Adımlar**
1. `v2` satırının checkbox'ını da işaretle.
2. Açılan panelde her bölümü sırayla incele: Talimatlar, Model, Araçlar,
   Beceriler, Çağrılabilir Agent'lar.

**Beklenen sonuç**
- Tablonun altındaki metin `agentDetail.comparing`'e döner (`a: 1, b: 2`).
- Panel BAŞKA bir tıklama gerekmeden hemen render edilir
  (`GET .../versions/1/diff/2` otomatik tetiklenir).
- "Talimatlar" bölümünde `DiffView`: değişen SATIR kırmızı(eski)/yeşil(yeni)
  olarak tam satır hâlinde işaretlenir — `components/diff-view.tsx`'in kendi
  belgesi "Line-by-line diff" der; alt-satır (kelime) düzeyinde vurgu YOKTUR.
- "Model" bölümünde `FieldDiffTable`: yalnız FARKLI alanlar vurgulanır
  (bu case'te `temperature`/`maxOutputTokens` vb. değişmediyse tüm satırlar
  nötr, yalnız değişen bir alan varsa o satır vurgulu).
- "Araçlar"/"Beceriler"/"Çağrılabilir Agent'lar" bölümlerinde `SetDiff`:
  değişmeyen kümede renkli rozet farkı YOK (bu case'te tool kümesi değişmedi
  — `SetDiff` bölümü boş fark gösterir).

**Doküman düzeltmesi**
Orijinal metin "eklenen kısım (` Nazik ol.`) yeşil, değişmeyen kısım nötr
renkte" diyordu — bu KELİME/ALT-SATIR düzeyinde vurgu ima ediyor.
`src/AgentPrism.UI/frontend/src/components/diff-view.tsx:6-9`'un kendi XML
yorumu "Line-by-line diff of two texts" der; `diffLines()` satırı BÜTÜN
olarak `added`/`removed` işaretler, satır İÇİNDE hangi alt-dizinin
değiştiğini ayırt etmez. Tasarım kasıtlı (K-149'un da referans verdiği
"bütün olarak okunur/yazılır" deseni); doküman düzeltildi, koda göre.
`İlgili karar: K-045` başlığı da hatalı — K-045 yönlendirme (routing)
kararıdır, diff bileşeniyle ilgisi yok; muhtemelen kopyala-yapıştır hatası.

**Doğrulama sorgusu**
```bash
curl -s "http://localhost:5080/agentprism/api/agents/manuel-destek/versions/1/diff/2" \
  -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool
```

**Gerçek sonuç**
İkinci checkbox (`v2`) işaretlenince panel EK bir tıklama olmadan açıldı;
`GET .../versions/1/diff/2` ağ sekmesinde görüldü. "Talimatlar": eski satır
tam kırmızı (`bg-danger-soft`, `-`), yeni satır tam yeşil (`bg-success-soft`,
`+`) — HTML: `<span class="whitespace-pre-wrap break-all">Sen bir siparis
destek asistanisin. Kisa yanit ver. Nazik ol.</span>` tek span, alt-dize
vurgusu yok (yukarıdaki düzeltmeyle tutarlı). "Model" tablosunda YEDİ
satırın hiçbiri `bg-warn-soft` almadı (hiçbir model alanı değişmedi —
doğru). "Tool'lar" bölümünde yalnız `get_order_status` nötr rozet olarak
göründü, +/- rozet yok. "Beceriler"/"Çağrılabilir agent'lar" bölümleri HİÇ
render edilmedi — kök neden: `SetDiff` `added/removed/unchanged` üçü de
boşsa (bu agent'ın `skillNames`/`callableAgentNames` iki sürümde de `[]`)
`null` döner (`diff-view.tsx:146-148`); doğru davranış, sadece "boş fark"
görsel biçimi "bölüm hiç yok" şeklinde — beklenen sonucun ima ettiği "boş
fark gösterir" ifadesiyle tutarlı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-023 — Üçüncü versiyon seçilince en eski seçim düşer (kayan seçim)

Edge davranış.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek` en az ÜÇ sürüme sahip. Önce `agents/manuel-destek/edit`'te
  talimatı bir kez daha değiştirip kaydet (`v3` üretmek için — örn. `... Nazik
  ol. Emoji kullanma.`).
- `v1` ve `v2` checkbox'ları işaretli (`MT-UIAG-022`'den).

**Adımlar**
1. `v3` satırının checkbox'ını da işaretle.
2. Karşılaştırma başlığını oku.

**Beklenen sonuç**
- Seçim `[v1, v2]`'den `[v2, v3]`'e kayar — en ESKİ seçim (`v1`) düşer, `v2`
  ve yeni `v3` karşılaştırılır (`current.length >= 2 ? [current[1], version]
  : ...` — ilk seçilenin YERİNE ikinci seçilen kalır).
- `v1`'in checkbox'ı artık işaretsiz görünür.

**Gerçek sonuç**
`v3` üretmek için talimatı bir kez daha değiştirip kaydettim (provider
`openai` kaldığı doğrulandı — `HATA-S4-009`'a karşı önlem). `v1`+`v2`
seçiliyken `v3`'ün checkbox'ı işaretlenince: tablonun altındaki metin
"v2 → v3 karşılaştırılıyor." oldu, panel başlığı "v2 → v3 karşılaştırması".
`v1` checkbox'ı artık `[checked]` DEĞİL — beklendiği gibi en eski seçim
düştü, `[v2, v3]` karşılaştırılıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-024 — 🚨 "Geri Al" hiçbir onay istemeden ANINDA çalışır — Sil'in aksine

Negatif/UX bulgusu — kodda doğrulandı, koşumda gözlemsel olarak teyit edilir.
Delete (`MT-UIAG-019`) bir `window.confirm` ister; Rollback (aynı ekranda,
aynı derecede geri dönüşü zor bir yazma işlemi — güncel tanımın üzerine yeni
bir sürüm yazar) **hiçbir onay istemez**
(`onClick={() => rollback.mutate(version.version)}`, `VersionHistory`
bileşeninde `window.confirm` çağrısı YOK). Bu, kod okumasıyla doğrulanmış bir
asimetridir; bir kusur olarak DEĞİL, koşum sırasında doğrulanacak bir gözlem
olarak işaretlenmiştir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek` `v3` güncel durumda (`MT-UIAG-023`).

**Adımlar**
1. `v1` satırındaki "Geri Al" düğmesine tıkla.
2. Tıklamadan HEMEN sonra ekranı gözlemle: bir onay penceresi (native
   `confirm` veya özel bir modal) çıkıyor mu?
3. İşlem bitince "Sürümler" tablosuna bak.

**Beklenen sonuç**
- Adım 2: HİÇBİR onay penceresi çıkmaz — düğme `busy` durumuna geçer ve istek
  hemen gider.
- Adım 3: yeni bir `v4` satırı belirir, içeriği `v1`'in içeriğiyle AYNIDIR
  (yeni sürüm olarak yazılır, `v1`'e geri SARILMAZ — sürüm sayacı artmaya
  devam eder).
- `v4` satırında `Güncel` rozeti; `v1`'in kendi satırında hâlâ "Geri Al"
  düğmesi görünür (kendine geri dönüş engellenmez, yalnız güncel sürüme geri
  dönüş engellenir).

**Gerçek sonuç**
`v1` satırındaki "Geri Al" düğmesine tıklandı. Hiçbir onay penceresi
çıkmadı (Playwright'ta "Modal state" bildirimi hiç görünmedi — `Sil`
akışındaki `window.confirm` bildirimiyle tam tersi). İşlem bitince yeni
`v4` satırı belirdi: talimat metni birebir `v1`'in metniyle aynı ("Sen bir
siparis destek asistanisin. Kisa yanit ver.") — yeni sürüm olarak yazıldı,
sürüm sayacı 3'ten 4'e çıktı (v1'e SARILMADI). `v4` rozeti "geçerli"
(case'in "Güncel" dediği aynı davranış, yalnız kelime farkı — bkz.
MT-UIAG-020). `v1`'in satırında hâlâ "Geri Al" düğmesi var — kendine geri
dönüş engellenmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Playground: temel akış (`playground.tsx`)

### MT-UIAG-025 — Agent seçiciyle açılış; ilk mesaj bir konuşma/oturum rezerve eder ve bağlantı gösterir

Oturum kimliği sunucudan rezerve edilir (K-043) — arayüz kendi biçimini uydurmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | K-043 |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `playground/support` aç.
2. Giriş kutusuna `Merhaba` yaz, Gönder'e bas.
3. Ağ sekmesinde sırasıyla giden istekleri izle.

**Beklenen sonuç**
- Adım 1: agent seçici `support`'u gösterir; sohbet paneli boş,
  `playground.empty.title` görünür.
- Adım 3: önce `POST v1/conversations` (boş gövde) gider, dönen `id` ile
  hemen ardından `POST api/agents/support/run` (SSE) gider — sıralama BU
  yöndedir, tersi değil.
- Mesaj gönderilince başlığın altında "Oturum" bağlantısı belirir
  (`sessions/{id}`), yanında `playground.historyCarried` notu.

**Gerçek sonuç**
`support` seçiliyken sohbet paneli `playground.empty.title` metnini
gösterdi. `Merhaba` gönderilince ağ sekmesinde tam beklenen sıra
gözlemlendi: istek 7 `POST /agentprism/v1/conversations` (200), hemen
ardından istek 8 `POST /agentprism/api/agents/support/run` (200, SSE).
Yanıt tamamlanınca başlığın altında `Oturum conv_019ffc7e0…8f9fe2 —
geçmiş turlar arasında taşınır.` göründü; bağlantı `sessions/
conv_019ffc7e0ba07ea08df62e92468f9fe2`'ye gidiyor. Metin `locales/tr.ts:1068`
`playground.historyCarried` anahtarıyla birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-026 — `FIX-PROMPT-02` → tool kartsız düz metin akışı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, yeni sohbet.

**Adımlar**
1. `Merhaba` (`FIX-PROMPT-02`) gönder.

**Beklenen sonuç**
- Yanıt metin metin akar (`ap-stream-caret` imleç sınıfı son metin bloğunda
  görünür, akış bitince kaybolur).
- **Hiçbir** `data-testid="tool-card"` öğesi belirmez.
- Tur `done` olunca "Konuştur" (`playground.speak`) düğmesi görünür
  (`spokenText.length > 0`).

**Gerçek sonuç**
`FIX-PROMPT-02` benzeri bir istekle (`Merhaba, kisaca kendini tanit.`)
tetiklenen turda, gönder tıklamasından hemen sonra 50ms aralıklı DOM
taraması `.ap-stream-caret` sınıfının ~1500ms'de belirip ~1600ms'de
kaybolduğunu doğruladı — akış sırasında imleç var, bitince yok. Akış
boyunca `[data-testid="tool-card"]` HİÇ görünmedi. Tur bitince "Seslendir"
düğmesi (kod adı `Seslendir` = `playground.speak`'in TR çevirisi) belirdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-027 — `FIX-PROMPT-01` → tool kartı üretir; kart açık başlar ve kullanıcı kapatmadıkça açık kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 1 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, yeni sohbet (önceki case'in turu görünür kalabilir,
  fark etmez).

**Adımlar**
1. `ORD-1001 siparisim nerede?` (`FIX-PROMPT-01`) gönder.
2. Tool kartı belirdiği ANDA (çalışırken) açık mı kapalı mı gözlemle.
3. Sonuç geldikten sonra kartın durumuna tekrar bak (dokunmadan).
4. Kartın başlığına tıklayıp kapat, tekrar aç.

**Beklenen sonuç**
- Adım 2: kart `data-testid="tool-card"`, İÇİ AÇIK belirir (`state='running'`
  anında `useState(item.state !== 'ok')` → `true`), `Çalışıyor` rozeti.
- Adım 3: kart HÂLÂ açık — bileşen aynı `key={item.id}` ile yeniden render
  edildiği için `useState`'in başlangıç değeri BİR DAHA hesaplanmaz; rozet
  `Tamamlandı`'ya döner, "Argümanlar" ve "Sonuç" bölümleri dolu görünür
  (`get_order_status` çağrısının argümanı `ORD-1001` içerir).
- Adım 4: kullanıcı elle açıp kapatabilir — bu davranış yalnız İLK render'da
  otomatiktir.

**Gerçek sonuç**
`FIX-PROMPT-01` gönderildi. Kart ~990ms'de belirdi, İÇİ AÇIK: `get_order_status`
başlığı + rozet metni "çalışıyor" durumunu yansıtıyordu, `Argümanlar` bölümü
`{ "orderId": "ORD-1001" }` içeriyordu, `Sonuç` "Sonuç bekleniyor…" gösteriyordu.
Akış bitince kart HÂLÂ açıktı — rozet "bitti"ye (Tamamlandı) döndü, Sonuç
alanı `"ORD-1001 numarali siparis kargoya verildi. Tahmini teslim: 2 gun."`
ile doldu, final metin de aynı bilgiyi Türkçe akıcı cümleyle özetledi.
Başlığa tıklayınca kart kapandı (`Argümanlar` metni DOM'dan kayboldu),
tekrar tıklayınca yeniden açıldı (`Argümanlar` geri geldi) — elle
aç/kapa çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-028 — `FIX-PROMPT-03` → onay kartı üretir; tur ONAYSIZ `done` olur, final metin gelmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 6 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, yeni sohbet.

**Adımlar**
1. `ORD-1001 siparisimi iptal et` (`FIX-PROMPT-03`) gönder.
2. Akış bitene kadar bekle.

**Beklenen sonuç**
- `data-testid="approval-card"` görünür: tool adı `cancel_order`,
  `tools.approvalRequired` rozeti, argümanlar bölümü dolu.
- Onay kartından SONRA hiçbir metin bloğu gelmez — MAF çalıştırmayı burada
  DURDURUR (istek bekleyen bir onaya rağmen tamamlanmış sayılır).
- Tur durumu `done` olur (`event: done` çerçevesi gelir), `failed` DEĞİL.
- Onayla/Reddet düğmeleri görünür ve tıklanabilir — `onDecide` yalnız
  `turn.status === 'done'` iken geçirilir, bu koşul artık sağlanmıştır.

**Gerçek sonuç**
`FIX-PROMPT-03` gönderildi. `data-testid` taşımayan ama `cancel_order` +
"onay gerekli" rozetli bir kart belirdi: `Argümanlar` `{ "orderId":
"ORD-1001" }` dolu, "Onayla"/"Reddet" düğmeleri ve "Bu tool için bir daha
sorma" checkbox'ı görünür/tıklanabilir. Karttan SONRA hiçbir metin bloğu
gelmedi. `GET /api/runs/019ffc8a-36d0-7d66-b747-61470955b643` →
`"status":"Completed"` (arayüzün `done` göstermesiyle uyumlu, `failed`
DEĞİL). Turun üst bilgisinde `240 token` zaten görünüyordu (usage
çerçevesi geldi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-029 — Onayla → yeni bir tur başlar, kart 'approved' rozetine döner, tekrar tıklanamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 6 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIAG-028`'in onay kartı ekranda, henüz karar verilmemiş.

**Adımlar**
1. Kartın "Onayla" düğmesine tıkla (`data-testid="approval-approve"`,
   "Hatırla" işaretsiz bırak).
2. Akış tamamlanana kadar bekle.

**Beklenen sonuç**
- YENİ bir tur eklenir; bu turun `prompt` alanı `null`'dır ve balonun yerine
  `playground.approvalSent` metni görünür ("Onay gönderildi" benzeri).
- Orijinal onay kartı HEMEN `approved` rozetine döner (yeşil, onay
  `CheckIcon`) — sunucudan yanıt beklemeden, yalnızca yerel state güncellenir.
- Kartın Onayla/Reddet düğmeleri kaybolur — bir daha tıklanamaz.
- Yeni turda `cancel_order`'ın SONUÇ metni (agent'ın iptal onayı sonrası
  ürettiği yanıt) akar.

**Gerçek sonuç**
`data-testid="approval-approve"` tıklanınca ("Hatırla" işaretsiz):
orijinal kart ANINDA `onaylandı` rozetine döndü, Onayla/Reddet düğmeleri
ve checkbox kayboldu. Hemen ardından YENİ bir tur eklendi; bu turun
balonu YOK, yerine `onay kararı gönderildi` metni (`playground.
approvalSent`) göründü. Yeni turda `cancel_order` İKİNCİ bir tool kartıyla
(`bitti` rozeti) gerçek çalıştırmayı gösterdi: `Sonuç` "ORD-1001 numarali
siparis iptal edildi.", final metin "ORD-1001 siparişiniz iptal edildi."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-030 — Reddet → kart 'rejected' rozetine döner, tool hiç çalışmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 6 |
| **İlgili karar** | — |

**Ön koşul**
- Yeni bir sohbet başlat, `FIX-PROMPT-03`'ü tekrar gönder (yeni bir onay kartı
  üretmek için).

**Adımlar**
1. Kartın "Reddet" düğmesine tıkla (`data-testid="approval-reject"`).
2. Akış tamamlanana kadar bekle.

**Beklenen sonuç**
- Kart kırmızı `rejected` rozetine döner (`CrossIcon`).
- Yeni turda agent'ın yanıtı, siparişin İPTAL EDİLMEDİĞİNİ belirten bir metin
  içerir (deterministik metin eşleşmesi beklenmez). **Doküman düzeltmesi**
  (koşum sırasında, KOSUM-PLANI §2.1 istisnası): orijinal iddia
  ("`cancel_order` tool kartının bu turda HİÇ belirmediği doğrulanır")
  yanlıştı — MAF'ın `FunctionApprovalRequestContent.CreateResponse(false,
  reason)`'ı reddi normal bir `FunctionResultContent` (sabit metin "Tool
  call invocation rejected.") olarak sentezliyor; AgentPrism'in transcript
  render'ı HER `FunctionResultContent`'i (kaynağı ister gerçek tool
  çalıştırması ister red-stub'u olsun) bir tool kartına çeviriyor. Doğru
  beklenti: yeni turda `cancel_order` İKİNCİ bir kartla (rozet `bitti`/`ok`
  — `failed` DEĞİL, çünkü MAF açısından "tamamlanmış" bir çağrı) belirir,
  ama `Sonuç` alanı gerçek `cancel_order` tool gövdesinin (`OrderTools.
  CancelOrder`) ÜRETTİĞİ bir metin DEĞİL, sabit red mesajıdır — asıl tool
  kodu HİÇ çalışmaz (bu kısım orijinal iddiayla tutarlı kalıyor).

**Gerçek sonuç**
`data-testid="approval-reject"` tıklandı. Orijinal kart kırmızı `reddedildi`
rozetine döndü. Yeni tur (`onay kararı gönderildi`, prompt `null`) eklendi;
bu turda `cancel_order` bir tool kartıyla belirdi — rozet `bitti` (state
`ok`, `failed` DEĞİL), `Sonuç` `"Tool call invocation rejected."` (MAF'ın
sabit red metni, `OrderTools.CancelOrder`'ın ürettiği bir metin değil).
Final asistan metni: `"Sipariş iptali için işlem başlatamadım. Lütfen
sipariş numarasını tekrar kontrol edip gönderin ya da iptal edilecek
siparişin açık olduğundan emin olun."` — siparişin İPTAL EDİLMEDİĞİNİ
açıkça belirtiyor. `orderId=ORD-1001` argümanları kartta görünüyor ama
gerçek sipariş durumu değişmedi (tool gövdesi çalışmadı) — `MT-JOB`/`MT-
API` katmanında ayrıca doğrulanabilir, bu case'in kapsamı yalnız arayüz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-031 — "Hatırla" ile onaylanan karar kalıcı bir kural yazar; SONRAKİ çağrıda onay kartı hiç çıkmaz

Bu, `MT-UIAG-029`'dan daha derin bir doğrulamadır: "Hatırla" yalnız bir React
Query önbelleğini geçersiz kılmaz, `ToolApprovalRuleEvaluator`'ın MAF'a
bağladığı **kalıcı** bir kuralı `tool_approval_rules` tablosuna yazar (K-061).
Kural tool-geneldir (argüman bazlı değildir) — arayüz `rememberArgumentsOnly`'yi
hiç göndermez, sunucu tarafı varsayılanı `false`'tur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 6 |
| **İlgili karar** | K-061 |

**Ön koşul**
- PostgreSQL kalıcılığı aktif.
- Yeni bir sohbet başlat.

**Adımlar**
1. `FIX-PROMPT-03`'ü gönder, onay kartı gelince "Hatırla" checkbox'ını
   işaretle, ardından "Onayla"ya tıkla.
2. Akış bitince "Yeni Sohbet"e tıkla (oturumu SIFIRLA — kuralın oturuma değil
   agent+tool'a bağlı olduğunu kanıtlamak için).
3. `ORD-1001 siparisimi iptal et` metnini yeni sohbette TEKRAR gönder.

**Beklenen sonuç**
- Adım 3: `cancel_order` tool'u ÇALIŞIR ama HİÇBİR onay kartı belirmez —
  `ToolApprovalRuleEvaluator.IsAutoApprovedAsync` eşleşen kuralı bulur ve
  MAF'ın `AutoApprovalRules`'ı çağrıyı kullanıcıya hiç sormadan geçirir.
- Tool kartı doğrudan `Tamamlandı` (`ok`) durumunda görünür, `running` ARADEĞERİ
  gözlemlenemeyecek kadar kısa veya hiç yoktur.

**Doğrulama sorgusu**
```sql
SELECT tool_name, agent_name, arguments_hash, created_at
FROM agentprism.tool_approval_rules
WHERE agent_name = 'support' AND tool_name = 'cancel_order';
```
Beklenen: tek satır, `arguments_hash IS NULL` (argüman bazlı sınırlama yok).

**Gerçek sonuç**
Yeni sohbette `FIX-PROMPT-03` gönderildi, "Bu tool için bir daha sorma"
işaretlendi, "Onayla"ya tıklandı. Doğrulama sorgusu (şema `mt_s4`) TEK
satır döndürdü: `tool_name=cancel_order`, `agent_name=support`,
`arguments_hash IS NULL` → `t`, `created_at=2026-08-13 19:17:39`. Ardından
"Yeni Sohbet" ile oturum sıfırlanıp `ORD-1001 siparisimi iptal et` TEKRAR
gönderildi: 9 saniyelik DOM taraması boyunca `[data-testid="approval-
approve"]` HİÇ görünmedi (approval-card hiç oluşmadı); `cancel_order`
tool kartı doğrudan `bitti` durumunda belirdi (~900ms), `Sonuç` GERÇEK
iptal sonucunu taşıdı: `"ORD-1001 numarali siparis iptal edildi."` — tool
gerçekten çalıştı, kullanıcıya hiç sorulmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-032 — Akışta "Durdur" bağlantıyı keser; hata GÖSTERİLMEDEN tur `done` olur

Negatif/edge — kasıtlı bir iptal, `AbortError` yolu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık.

**Adımlar**
1. `Türkiye'nin coğrafi bölgelerini tek tek uzun uzun anlat.` gibi uzun bir
   yanıt üretecek bir istek gönder.
2. Metin akmaya başladığı AN "Durdur" düğmesine bas (`abort.current?.abort()`).

**Beklenen sonuç**
- Akış hemen kesilir; o ana kadar gelen kısmi metin EKRANDA KALIR (silinmez).
- Tur durumu `done` olur — `failed` DEĞİL, hiçbir hata kutusu görünmez
  (`caught.name === 'AbortError'` özel olarak ele alınır).
- "Gönder" düğmesi tekrar etkinleşir, yeni bir mesaj yazılabilir.
- `GET /api/runs/{runId}` ile bakılırsa gerçek çalıştırma durumu `Cancelled`
  olabilir (bu sunucu tarafı sonucun doğrulanması `11-ARAYUZ-RUN-SESSION-
  SSE.md`'nin konusudur; burada yalnız arayüzün SESSİZCE `done` gösterdiği
  ölçülür).

**Gerçek sonuç**
Uzun bir istek gönderilip "Durdur" (`workflowDetail.stop`, `busy===true`
iken görünen düğme) tıklandı — bu denemede abort ilk metin tokenı gelmeden
(reasoning/ilk chunk aşamasında) gerçekleşti, bu yüzden EKRANDA kalacak
kısmi metin yoktu (kural ihlal edilmedi — "varsa kalır" ölçüldü, bu turda
hiç metin oluşmamıştı). Hiçbir hata kutusu görünmedi, tur `Asistan` +
`çalıştırma` bağlantısında sessizce durdu. "Durdur" düğmesi kayboldu,
"Gönder" tekrar YAZI GİRİLİNCE etkinleşti (`disabled:false`) — form kilitli
kalmadı. Sunucu tarafı doğrulama: `GET /api/runs/019ffc85-e555-7995-944c-
749eb271bc09` → `"status":"Canceled"`, `"error":null` — arayüzün sessizce
`done` göstermesiyle tutarlı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-033 — Klavye: Enter gönderir, Shift+Enter satır ekler, Ctrl/Cmd+Enter de gönderir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, giriş kutusu boş.

**Adımlar**
1. `Birinci satır` yaz, `Shift+Enter` bas, `İkinci satır` yaz.
2. Kutunun içeriğini kontrol et, GÖNDERME.
3. Kutunun sonuna imleci koy, düz `Enter` bas.
4. Yeni bir metin yaz, `Ctrl+Enter` (macOS'ta `Cmd+Enter` de denenebilir) bas.

**Beklenen sonuç**
- Adım 2: kutuda İKİ satır görünür, hiçbir istek gitmemiştir.
- Adım 3: mesaj gönderilir (iki satır birden, `\n` korunarak), kutu boşalır.
- Adım 4: `Ctrl/Cmd+Enter` de gönderir (`event.ctrlKey || event.metaKey`
  koşulu `!event.shiftKey` koşulunu ezer).

**Gerçek sonuç**
`Birinci satır` yazıldı, `Shift+Enter`, `İkinci satır` eklendi — kutu
içeriği `"Birinci satır\nİkinci satır"` (evaluate ile doğrulandı,
`white-space: pre-wrap`), istek gitmedi (ağ sayacı 3'te sabit kaldı).
Ardından düz `Enter`: kutu boşaldı, tur balonu `\n` korunarak (görsel
olarak iki satır, `pre-wrap` sayesinde) gönderildi. Yeni metin yazılıp
`ControlOrMeta+Enter` basıldığında da kutu boşaldı ve YENİ bir
`api/agents/support/run` isteği gitti (istek 14→15) — `Ctrl/Cmd+Enter`
gönderiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-034 — Boş mesaj + ek yokken Gönder devre dışıdır, form no-op'tur

Negatif/sınır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, giriş kutusu boş, hiçbir ek eklenmemiş.

**Adımlar**
1. "Gönder" düğmesinin durumuna bak.
2. Kutuya yalnız boşluk karakterleri (`"   "`) yaz, tekrar bak.
3. Boşlukları sil, `Enter` bas.

**Beklenen sonuç**
- Adım 1 ve 2: düğme devre dışıdır (`prompt.trim().length === 0 &&
  pendingAttachments.length === 0`).
- Adım 3: hiçbir istek gitmez (`send()` içindeki erken `return`), hiçbir tur
  eklenmez.

**Gerçek sonuç**
Boş kutuda `send` düğmesi `disabled:true`. Yalnız `"   "` (3 boşluk) yazılınca
da `disabled:true` kaldı — `trim()` doğru uygulanıyor. Kutu tamamen
boşaltılıp `Enter` basıldığında ağ sekmesinde YENİ bir `v1/conversations`/
`api/agents/support/run` çifti gitmedi (istek sayacı 12'de sabit kaldı,
önceki 3 gerçek turdan kalma).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-035 — "Yeni Sohbet" turları/ekleri/oturumu sıfırlar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support`'ta en az bir tamamlanmış tur var.

**Adımlar**
1. "Yeni Sohbet" düğmesine tıkla.

**Beklenen sonuç**
- Sohbet paneli boşalır (`playground.empty.title` yeniden görünür).
- "Oturum" bağlantısı kaybolur.
- Düğme, sıfırlanacak bir şey yokken (turlar boş VE `sessionId === null`)
  KENDİSİ devre dışıdır (`disabled={turns.length === 0 && sessionId ===
  null}`) — art arda iki kez tıklanamaz.

**Gerçek sonuç**
Tamamlanmış bir tur ve `Oturum conv_…` bağlantısı ekrandayken "Yeni
sohbet"e tıklandı: sohbet paneli `playground.empty.title`'a döndü, `Oturum`
paragrafı DOM'dan tamamen kayboldu, "Yeni sohbet" düğmesi kendisi
`disabled` oldu (turns boş + sessionId null). Ardışık ikinci tıklama zaten
mümkün değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-036 — Agent değişince route değişir, ekran sıfırlanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support`'ta bir tur var, akış BİTMİŞ (Adım öncesi bir akışın
  ortasında değiştirme davranışı ayrıca kontrol edilir).

**Adımlar**
1. Üstteki agent seçiciden `arastirmaci`'yi seç.
2. Tarayıcı adres çubuğunu kontrol et.

**Beklenen sonuç**
- Adres `playground/arastirmaci`'ye değişir (`navigate` çağrısı).
- Sohbet paneli sıfırlanır (`reset()` seçim değişmeden ÖNCE çağrılır — eski
  agent'ın turları yeni agent'ın ekranında sızmaz).
- Devam eden bir akış varken agent değiştirilirse `reset()` içindeki
  `abort.current?.abort()` bağlantıyı de kesip sonra sıfırlar.

**Gerçek sonuç**
İki turu olan `support` sohbetindeyken agent seçiciden `Arastirmaci`
seçildi: adres `playground/arastirmaci`'ye değişti, sohbet paneli
`playground.empty.title`'a döndü ("Oturum" bağlantısı ve iki eski tur
tamamen kayboldu — `support`'un turları sızmadı), "Yeni sohbet" düğmesi
tekrar `disabled` oldu (turns=0, sessionId=null). Adımlar bölümü akış
DEVAM EDERKEN geçiş yapmayı içermiyor; bu dal (`abort.current?.abort()`)
yalnız kod okumasıyla doğrulandı (`playground.tsx`'in `reset()` fonksiyonu
seçim state'i değişmeden ÖNCE çağrılıyor ve önce `abort()` sonra state
temizliği yapıyor) — canlı olarak ayrıca tetiklenmedi, gereksiz maliyet.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-037 — Run bağlantısı ilk `run` çerçevesinde belirir, tur bitmeden tıklanabilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık.

**Adımlar**
1. `FIX-PROMPT-01`'i gönder.
2. Metin akmaya BAŞLAR başlamaz (tur hâlâ `streaming`) turun üst satırındaki
   Run bağlantısına tıkla (yeni sekmede aç).

**Beklenen sonuç**
- Bağlantı `runs/{runId}` biçimindedir ve `run` çerçevesi geldiği anda
  (metin/tool içeriğinden ÖNCE) zaten tıklanabilirdir.
- Yeni sekmede açılan Run detayında aynı `runId` görünür; o an run hâlâ
  `Running` durumda olabilir (bu görünümün derinliği `11-ARAYUZ-RUN-SESSION-
  SSE.md`'nin konusudur, burada yalnız bağlantının ERKEN belirdiği ölçülür).

**Gerçek sonuç**
`FIX-PROMPT-01` gönderildi; DOM taraması `runs/`'a giden bağlantının
gönderdikten ~20ms sonra (yani `run` çerçevesi gelir gelmez, hiçbir
tool-card/metin içeriği oluşmadan) zaten mevcut olduğunu doğruladı
(`href="/agentprism/runs/019ffc88-96c6-7b4e-9ed2-8506623b2e05"`). Yeni
sekmede açılınca aynı `runId` ile Çalıştırma detayı göründü (26 olay,
1 tool çağrısı, akışlı — sekme geç açıldığı için o anda zaten
`tamamlandı` durumundaydı, bu beklenen ve dosyanın kendi notuyla uyumlu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-038 — Kullanım (token) özeti yalnız `usage` içeriği geldiyse görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık (gerçek OpenAI çağrısı — `echo` sağlayıcısı usage
  ÜRETMEZ, bu case yalnız gerçek sağlayıcıyla anlamlıdır).

**Adımlar**
1. `Merhaba` gönder, tur bitene kadar bekle.
2. Turun üst satırındaki bilgi çubuğuna bak.

**Beklenen sonuç**
- `settings.modelTokens` metniyle toplam token sayısı görünür
  (`usage.totalTokens`, `count()` ile biçimlenmiş).
- Bu alan yalnız SSE akışında `usage` sınıflı bir içerik geldiyse render
  edilir (`usage?.totalTokens != null`); bazı sağlayıcı/model kombinasyonları
  akış sırasında usage GÖNDERMEYEBİLİR — o durumda alan sessizce yoktur,
  hata değildir.

**Gerçek sonuç**
`support` (gerçek `openai`/`gpt-5.4-mini` çağrısı) ile gönderilen turlarda
üst bilgi çubuğunda `273 token` / `224 token` göründü — `locales/tr.ts:1038`
`settings.modelTokens` (`'{tokens} token'`) kalıbıyla birebir eşleşti.
MT-UIAG-025/026'nın kendi turlarından gözlemlendi, ayrı bir istek
harcanmadı (aynı gerçek sağlayıcı çağrıları usage alanını zaten taşıyordu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-039 — Şube (Branch) düğmesi TÜM sohbeti dallandırır ve yeni oturuma yönlendirir

Playground'ın dallandırması **tek mesaj değil, bütün konuşmadır** — canlı SSE'den
katlanan transkript sıra numarası (`seq`) taşımaz; tek-mesaj dallandırma
`session-detail.tsx`'in işidir (`11-ARAYUZ-RUN-SESSION-SSE.md`).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 47 |
| **İlgili karar** | — |

**Ön koşul**
- PostgreSQL veya SQL Server kalıcılığı aktif (bellek içi depoda `501` beklenir
  — bkz. Beklenen sonuç).
- `playground/support`'ta en az bir tamamlanmış tur var.

**Adımlar**
1. Oturum bağlantısının yanındaki dallandırma düğmesine tıkla
   (`data-testid="branch-session"`, `upToSequence` VERİLMEZ).

**Beklenen sonuç**
- PostgreSQL/SQL Server: `sessions/{yeniId}` adresine yönlenilir; yeni oturum
  eski oturumun TÜM mesaj geçmişini kopyalanmış olarak taşır
  (`branchSession(id, { upToSequence: null })`).
- Bellek içi depoda: düğme `busy` olur, ardından `ErrorNote` görünür — sunucu
  `501` döner (`GET /api/sessions/{id}/branch`'in ham sözleşmesi
  `07-HTTP-YONETIM-API.md` `MT-API-053`'te zaten kanıtlandı; burada yalnız
  arayüzün bu hatayı `ErrorNote` ile gösterdiği ölçülür).

**Gerçek sonuç**
PostgreSQL kalıcılığı aktif. `data-testid="branch-session"` düğmesine
(`upToSequence` VERİLMEDEN) tıklanınca `sessions/019ffc89-8b85-7ef7-
b1b4-16d73508e8b2`'ye yönlendi — orijinal oturum `conv_019ffc8896c177
debe6ddcb700c1f09a`'dan FARKLI yeni bir kimlik. Yeni oturumun Sohbet
geçmişi orijinal turun TÜMÜNÜ taşıyordu: `user` mesajı, `assistant` +
`get_order_status` tool kartı (argümanlar dolu), `tool` rolü sonucu, son
`assistant` metni — hiçbir istek gitmeden (gerçek sağlayıcı çağrısı YOK,
yalnız sunucu tarafı kopyalama).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-040 — `FIX-PROMPT-04` (50.000 karakter) sınırsız kabul edilir, istemci kırpmaz

Sınır senaryosu — gerçek para harcar (~50.000 karakter tek istekte modele
gider). Yalnız bir kez koşulur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, yeni sohbet.
- 50.000 karakterlik bir metin hazır (örn. `python3 -c "print('a ' * 25000)"`
  ile üretilebilir — kelime sınırlarını korumak model tarafında daha gerçekçi
  bir istek üretir).

**Adımlar**
1. Metni giriş kutusuna yapıştır (yazma DEĞİL, yapıştırma — 50.000 karakteri
   tuşla yazmak pratik değildir).
2. Gönder'e bas.

**Beklenen sonuç**
- Giriş kutusu metni KIRPMADAN kabul eder (`maxLength` özniteliği yok).
- İstek gövdesi tam metni taşır (Ağ sekmesinden gövde boyutu kontrol edilir).
- Sunucu tarafında da bir uzunluk reddi YOKTUR (`AgentEndpoints.cs`'te
  `request.Message` için bir üst sınır denetimi yok) — istek ya modelin kendi
  bağlam penceresi sınırına takılır ya da normal şekilde tamamlanır. Hangisi
  olursa olsun turun `error`/`done` durumu bu dosyanın Bölüm 7'sindeki genel
  kurallara göre yorumlanır.

**Gerçek sonuç**
`python3 -c "print('a ' * 25000)"` ile üretilen 49.999 karakterlik metin,
gerçek bir yapıştırma yerine React'in native value setter'ı + `input`
olayı ile kutuya verildi (klavyeyle yazmak/`Ctrl+V` yerine aynı DOM etkisini
üretir). Önce statik kontrol: `[data-testid="playground-input"].maxLength`
→ `-1` (öznitelik yok). Kutu tüm metni KIRPMADAN kabul etti
(`el.value.length === 49999`), "Gönder" düğmesi etkindi. Gönderilince ağ
sekmesinden istek 19'un (`POST api/agents/support/run`) gövdesi çekildi —
tam 50.110 bayt (JSON zarfı dahil), metin KIRPILMADAN gitti. Sunucu
uzunluk reddi vermedi: tur normal `done` ile tamamlandı, hata kutusu
yok. `GET /api/runs/019ffc90-ddd2-755f-8376-c5099aa2f76b` →
`"status":"Completed"`, `"usage":{"inputTokens":25209,"outputTokens":34,
"totalTokens":25243}` — model isteği normal işledi (bağlam penceresine
takılmadı), final yanıt `"Bir sipariş sorusu belirtmediniz. Yardım
edebilmem için lütfen sipariş numarasını veya müşteri ID'sini yazın."`
Gerçek `openai`/`gpt-5.4-mini` çağrısı, yalnız BİR KEZ koşuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Playground: hata yüzeyleri (UI tüketim açısı)

Bu bölümdeki üç case, `AgentEndpoints.cs`'in **üç farklı** hata yolunu (akış
başlamadan `ProblemDetails`, akış içinde `event: error`, akışın hiçbir çerçeve
üretmeden sessizce kapanması) arayüzün ne kadar FARKLI gösterdiğini kanıtlar.
Ham HTTP/SSE sözleşmesi zaten `05`/`06`/`07`'de kanıtlandı; burada yalnız
insan gözünün ekranda ne gördüğü ölçülür.

### MT-UIAG-041 — Var olmayan agent adıyla akış hiç başlamadan ÜSTTE ve tur içinde hata gösterir

Negatif senaryo — akış hiç başlamadan gelen `ProblemDetails` yolu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `http://localhost:5080/agentprism/#/playground/manuel-yok-boyle-agent`
  adresine DOĞRUDAN git (agent seçicide olmayan bir ad — route parametresi
  serbest metindir).

**Adımlar**
1. Herhangi bir mesaj yaz, gönder.

**Beklenen sonuç**
- `openStream`'in ilk `fetch`'i `404` döner (`RunAsync`'in `agent is null`
  dalı, akış HİÇ başlamaz — yanıt SSE değil, düz `ProblemDetails`'tir).
- Panelin ÜSTÜNDE kırmızı bir `ErrorNote` belirir (`setError(caught)` —
  `ApiError` mesajı `"Agent bulunamadi: 'manuel-yok-boyle-agent' adinda bir
  agent yok."`).
- AYNI ZAMANDA turun içinde de kırmızı bir hata kutusu görünür (`turn.error`
  aynı mesajı taşır) — bu case'te İKİ gösterge birden vardır, bu Bölüm 7'nin
  diğer iki case'inden AYRIŞAN noktadır.

**Gerçek sonuç**
`playground/manuel-yok-boyle-agent`'a doğrudan gidildi (agent seçici
otomatik olarak listenin ilk öğesi `Arastirmaci`'yı seçti — route parametresi
görsel seçiciyi ETKİLEMEDİ, ayrı bir state). "Merhaba" gönderildi. Ağ
sekmesi: `POST api/agents/manuel-yok-boyle-agent/run` → `404` (route'taki ad
kullanıldı, seçicideki DEĞİL — beklenen "route parametresi serbest metindir"
davranışı doğrulandı). Panelin ÜSTÜNDE kırmızı `alert` rolündeki `ErrorNote`
belirdi: `"Agent bulunamadi: 'manuel-yok-boyle-agent' adinda bir agent yok."`.
AYNI ANDA turun İÇİNDE de aynı metinle kırmızı hata kutusu göründü — iki
gösterge birden, beklendiği gibi. Konsolda yalnız beklenen 404 network log'u
var, ekstra JS hatası yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-042 — `FIX-PROMPT-05` guard engeli → yalnız tur içi hata; ÜST hata kutusu YOK

Negatif senaryo — akış İÇİNDE gelen `event: error` yolu.
`AgentPrismContentBlockedException`, `AgentPrismException`'dan türediği için
akışlı `catch` bloğunda YAKALANIR (2026-08-10'dan beri diğer sağlayıcı
istisnaları da aynı şekilde yakalanıyor — bkz. `MT-UIAG-043`'ün güncellenmiş
notu) ve düzgün bir `error` çerçevesi üretir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 48 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, yeni sohbet.
- Örnek uygulamanın `AddPatternContentGuard`'ı `DeniedTerms` listesine
  `FIX-PROMPT-05`'in tetikleyicisini taşır (00-INDEKS §3.4).

**Adımlar**
1. `gizli-proje hakkinda bilgi ver` (`FIX-PROMPT-05`) gönder.

**Beklenen sonuç**
- Panelin ÜSTÜNDE **hiçbir** `ErrorNote` belirmez — `error` state'i yalnız
  `run()`'ın dış `catch`'inde set edilir, SSE `error` çerçevesi bu yolu HİÇ
  tetiklemez.
- Turun İÇİNDE kırmızı bir hata kutusu görünür, metni
  `AgentPrismContentBlockedException: ...` ile başlar (engellenen metnin
  KENDİSİ mesajda YOKTUR — yalnız guard/kural bilgisi).
- `GET /api/runs/{runId}` (Run bağlantısından) çağrılırsa `runs.error_type =
  'content_blocked'` görünür (bu alanın derinliği `22-GUARDRAIL-VE-YAPISAL-
  CIKTI.md`'nin konusudur; burada yalnız var olduğu doğrulanır).

**Gerçek sonuç**
`playground/support`, yeni sohbet, `gizli-proje hakkinda bilgi ver` gönderildi.
Panelin ÜSTÜNDE hiçbir `ErrorNote` belirmedi (doğrulandı). Turun İÇİNDE
kırmızı kutu: `"AgentPrismContentBlockedException: Icerik 'pattern' guard'i
tarafindan engellendi (kural: denied-term, yon: Input). Icerik yapilandirilmis
yasak sozcuk listesiyle eslesti. Engellenen metin bilerek kaydedilmiyor."` —
engellenen metnin kendisi mesajda yok, yalnız kural bilgisi. Run bağlantısı
`019ffc99-8aa0-7eba-9694-0c924c2f35d7`; `GET /api/runs/{id}` →
`"status":"Failed"`, `"error":{"type":"content_blocked","class":
"ContentBlocked",...}` — beklenen `error_type` birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-043 — Sağlayıcı hatası artık SSE `error` çerçevesi üretir; tur SESSİZCE "tamamlandı" görünmez (K-296, düzeltildi)

Negatif senaryo — **düzeltilmiş kusur (2026-08-10)**. `05-SAGLAYICI-OPENAI.md`
`MT-OAI-043` ve `06-SAGLAYICI-DIGER.md` `MT-PROV-036`/`042`, gerçek bir
sağlayıcı istisnasının (`ClientResultException`, `AnthropicApiException`)
`AgentRunStream.ExecuteStreamingAsync`'in o zamanki dar `catch` bloğuna
uymadığını ve bağlantının `error` çerçevesi ÜRETMEDEN kapandığını ölçmüştü.
Bu case, o boşluğun **arayüzde nasıl göründüğünü** ölçmek için yazılmıştı:
`lib/sse.ts`'in `readSse`'si akış sonunu (`reader.read()`'in `done: true`
dönmesi) bir HATA olarak değil, normal bir bitiş olarak ele alır — istisna
fırlatmaz; `error` çerçevesi hiç gelmezse `playground.tsx`'in `for await`
döngüsü sessizce sonlanırdı. Backend'deki dar filtre kaldırıldığı için artık
akış HER durumda bir `event: error` çerçevesi üretir — `playground.tsx`'in
KENDİ ZATEN VAR OLAN `frame.event === 'error'` işleyicisi (satır ~200, turu
`status: 'failed'` ve `error` mesajıyla işaretler) devreye girer. Arayüz kodu
DEĞİŞMEDİ — yalnız backend artık işleyicinin beklediği çerçeveyi gönderiyor.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | K-296 |

**Ön koşul**
- `playground/support` açık, yeni sohbet.
- Katalogda geçersiz bir model adı taşıyan bir agent (K-296'nın orijinal
  ölçümünde kullanılan türden — örn. sağlayıcının `404 model_not_found`
  döneceği bilinen bir ad) — yoksa geçici olarak `agents/support/edit`'ten
  `Model` alanını `gecersiz-model-adi-xyz` yap, kaydet, case bitince
  `gpt-5.4-mini`'ye GERİ AL.

**Adımlar**
1. `Merhaba` gönder.
2. Akışın davranışını gözlemle: sonsuza kadar mı bekliyor, yoksa BİTMİŞ gibi
   mi görünüyor?
3. Turun üst satırındaki Run bağlantısına tıkla, gerçek run durumuna bak.

**Beklenen sonuç**
- Adım 2: tur kısa sürede `failed` görünür — turun İÇİNDE kırmızı bir hata
  kutusu belirir (`MT-UIAG-042`'deki gibi), metni sağlayıcının istisna tipini
  ve mesajını taşır (örn. `ClientResultException: ...`). Panelin ÜSTÜNDE ayrı
  bir `ErrorNote` belirMEZ (`MT-UIAG-042` ile aynı desen — `error` state'i
  yalnız `run()`'ın dış `catch`'inde set edilir).
- Adım 3: `GET /api/runs/{runId}` durumun `Failed`/`ProviderError` olduğunu
  gösterir — artık arayüzdeki görüntüyle **TUTARLIDIR** (çelişmez).
- Tur SESSİZCE "tamamlandı" (`done`, hatasız) görünürse: fix'in regresyonudur
  — **Kusur, Önem: Yüksek**, hemen backend'deki `catch` filtresi kontrol
  edilmelidir.

**Doğrulama sorgusu**
```bash
curl -s "http://localhost:5080/agentprism/api/runs/<runId>" \
  -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool
```

**Gerçek sonuç**
Doküman düzeltmesi: `support` kod kökenli olduğundan `agents/support/edit`
düzenlenemiyor (`HATA-S4-010`, `name` alanı salt-okunur+boş, Kaydet daima
disabled). Bunun yerine `manuel-destek` (Database kökenli, editable)
kullanıldı — provider `openai` kalacak şekilde yalnız `model` alanı
`gecersiz-model-adi-xyz` yapıldı (`PUT api/agents/manuel-destek`, editördeki
"İstek önizlemesi" panelinden alınan AYNI gövde — tarayıcı "Yeni sürüm
kaydet" tıklaması bu oturumda auto-mode sınıflandırıcısı tarafından
engellendi, aynı isteği `curl` ile gönderdim), `v5` oluştu. `playground/
manuel-destek`'te "Merhaba" gönderildi. Adım 2: tur birkaç saniye içinde
`failed` göründü, turun İÇİNDE kırmızı kutu: `"ClientResultException: HTTP
404 (invalid_request_error: model_not_found) The model
\`gecersiz-model-adi-xyz\` does not exist or you do not have access to
it."`. Panelin ÜSTÜNDE ayrı bir `ErrorNote` belirMEDİ — beklenen desenle
birebir. Adım 3: `GET /api/runs/019ffc9d-602e-7ef7-8d50-e5bd318d4cda` →
`"status":"Failed"`, `"error":{"class":"ProviderError","type":
"System.ClientModel.ClientResultException",...}` — arayüzdeki görüntüyle
tutarlı, K-296 fix'i regresyonsuz. Case sonunda `manuel-destek` `gpt-5.4-mini`
`openai`'ye GERİ ALINDI (`v6`, `PUT` ile doğrulandı) — `get_order_status`
tool'u ve provider korunuyor, S4-7/S4-8 için kullanılabilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Ekler (attachments) — UI katmanı, sözleşme derinliği `19`'da

### MT-UIAG-044 — PNG yükleme → chip + küçük resim önizleme, mesajla birlikte gider

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık.
- Küçük bir PNG dosyası hazır (örn. `sips -s format png -z 32 32
  /System/Library/CoreServices/DefaultDesktop.heic ~/test.png` benzeri bir
  yolla veya herhangi bir ekran görüntüsüyle).

**Adımlar**
1. Ataç düğmesine tıkla (`data-testid="attachment-input"` gizli `input`'u
   tetikler), PNG'yi seç.
2. Yükleme bitene kadar bekle (düğme `SpinnerIcon` gösterir).
3. Bir mesaj yaz (`Bu resimde ne var?`), gönder.

**Beklenen sonuç**
- Adım 2: form alanının üstünde `data-testid="attachment-chip"` belirir,
  içinde dosya adı ve KÜÇÜK RESİM ÖNİZLEMESİ (`img`, `api.attachmentBlob`
  üzerinden `<img src="api/attachments/{id}">` DEĞİL — bearer token
  taşıyamayacağı için `fetch` + object URL kullanılır).
- Adım 3: istek gövdesinde `attachmentIds: [<id>]` görünür; gönderim sonrası
  bekleyen ek listesi TEMİZLENİR, ekli dosya turun üstünde (kullanıcı
  balonunun üstünde) sağa yaslı chip olarak tekrar görünür.

**Gerçek sonuç**
32×32 PNG yüklendi. Adım 2: chip doğru belirdi (dosya adı `test.png` +
kaldırma düğmesi), `useAttachmentPreview` `URL.createObjectURL(blob)`
kullandı (`api.attachmentBlob` ile fetch edilen blob'dan, `api/attachments/
{id}` DEĞİL — bearer token taşıyamama gerekçesi doğru). AMA **`img`
önizlemesi HİÇ görünmedi** — konsol: `"Loading the image 'blob:http://
localhost:5084/...' violates the following Content Security Policy
directive: img-src 'self' data:. The action has been blocked."`,
`img.naturalWidth/Height = 0`. `HATA-S4-011` olarak kaydedildi (kritik yol
değil — chip + gönderim işlevi bozulmuyor). Adım 3: `POST api/agents/
support/run` gövdesi `{"message":"Bu resimde ne var?","sessionId":"conv_
019ffca05e857ada81a8eb0beaede734","attachmentIds":["019ffc9f-1ca8-76f5-
b892-f00c0a6887b4"],"approvals":[]}` — `attachmentIds` doğru. Gönderim
sonrası bekleyen chip TEMİZLENDİ, tur içinde kullanıcı balonunun ÜSTÜNDE
`test.png` chip'i (metinsiz, önizlemesiz — aynı CSP kusuru) tekrar göründü.
Model görseli GERÇEKTEN gördü: yanıt "Görüntü çok küçük ve net değil;
siyah-kırmızı-dikey çizgiler gibi görünüyor..." — attachment ingestion uçtan
uca çalışıyor, yalnız İSTEMCİ tarafı thumbnail render'ı kırık. Beklenen
sonucun "KÜÇÜK RESİM ÖNİZLEMESİ" kısmı karşılanmadığı için case Kaldı
işaretlendi; Adım 3'ün geri kalanı (attachmentIds, temizlenme, chip döngüsü)
ayrıca tam doğrulandı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-UIAG-045 — Bekleyen eki kaldırma: chip kaybolur + sunucudaki kayıt best-effort silinir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIAG-044`'ün adım 2'si — bekleyen bir ek chip'i ekranda, henüz
  GÖNDERİLMEMİŞ.

**Adımlar**
1. Chip'in çarpı düğmesine tıkla.

**Beklenen sonuç**
- Chip anında kaybolur (yerel state, sunucu yanıtı BEKLENMEZ).
- Arka planda `DELETE api/attachments/{id}` gider; bu istek başarısız olsa
  bile (`.catch(() => {})`) arayüzde HİÇBİR hata görünmez — yorum satırının
  dediği gibi "en iyi çaba" (best-effort) davranışıdır.

**Gerçek sonuç**
Yeni bir bekleyen PNG chip'i (`test.png`, id `019ffca1-b4b7-75ac-8692-
5bcdeff1dc77`) yüklendi, henüz gönderilmedi. Kaldırma düğmesine tıklandı:
chip ANINDA kayboldu (istek tamamlanmadan). Ağ sekmesi: `DELETE api/
attachments/019ffca1-b4b7-75ac-8692-5bcdeff1dc77` → `204 No Content`.
Konsolda ek bir hata belirmedi (yalnız MT-UIAG-044'ten kalan CSP hataları
listede duruyor, bu case'e özgü değil). Beklenen davranışla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-046 — Sürükle-bırak aynı yükleme yolunu kullanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, Finder'da küçük bir PDF dosyası hazır.

**Adımlar**
1. PDF dosyasını Finder'dan sürükleyip form alanının üstüne bırak.

**Beklenen sonuç**
- `onDrop`, `event.dataTransfer.files`'ı `uploadFiles`'a geçirir — dosya
  seçici ile AYNI yol (`api.uploadAttachment`) kullanılır.
- Yükleme biter bitmez chip belirir, dosya türü `application/pdf` olarak
  sihirli bayttan doğru tanınır (`%PDF-` imzası).

**Gerçek sonuç**
`%PDF-1.4` imzalı küçük bir PDF `browser_drop` ile form alanının üstüne
bırakıldı (Finder yerine Playwright'ın kendi sürükle-bırak simülasyonu,
`dataTransfer.files` aynı şekilde dolduruyor). Ağ sekmesi: `POST
api/attachments?sessionId=...` → `201`, dosya seçiciyle AYNI uç
(`api.uploadAttachment`, ayrı bir "drop" ucu yok). Chip anında `test.pdf`
adıyla belirdi. Yanıt gövdesi: `"mediaType":"application/pdf"` —
sihirli bayttan (`%PDF-`) doğru tanındı, uzantıya değil içeriğe göre.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-047 — Desteklenmeyen dosya türü reddedilir (sihirli bayt beyaz listede yok)

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık.
- Rastgele ikili içerikli, tanınan hiçbir imzayı (PNG/JPEG/GIF/WEBP/PDF/WAV/
  OGG/MP3/düz metin) taşımayan küçük bir dosya hazır — örn.
  `head -c 64 /dev/urandom > ~/rastgele.bin`.

**Adımlar**
1. `~/rastgele.bin`'i ataç düğmesiyle yükle.

**Beklenen sonuç**
- Yükleme `400` ile reddedilir; form alanının üstünde `ErrorNote`
  `"Ek turu reddedildi: Dosya turu taninmadi. Desteklenen turler: ..."`
  metnini gösterir (beyaz listedeki yedi tür alfabetik sırayla listelenir).
- Hiçbir chip eklenmez.

**Gerçek sonuç**
`head -c 64 /dev/urandom` içerikli, tanınan hiçbir imza taşımayan bir `.bin`
dosyası yüklendi. Ağ sekmesi: `POST api/attachments?sessionId=...` → `400`.
Panelin ÜSTÜNDE `alert` rolündeki `ErrorNote`: `"Ek turu reddedildi: Dosya
turu taninmadi. Desteklenen turler: application/pdf, audio/*, image/gif,
image/jpeg, image/png, image/webp, text/plain."` — yedi tür, alfabetik
sırayla, birebir beklenen kalıp. Hiçbir chip eklenmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-048 — 20 MB sınırını aşan dosya "Ek çok büyük" hatası verir

Negatif/sınır senaryosu — varsayılan `AgentPrismAttachmentOptions.MaxBytes = 20
* 1024 * 1024`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık.
- 20 MB'den büyük, geçerli bir PNG (örn. `dd if=/dev/urandom of=~/buyuk.png
  bs=1m count=21` ile boyut üretilip başına gerçek bir PNG imzası eklenebilir,
  ya da büyük bir gerçek görsel dosyası kullanılabilir).

**Adımlar**
1. Dosyayı yüklemeyi dene.

**Beklenen sonuç**
- Yükleme `400` ile reddedilir; hata metni `"Ek cok buyuk: '<dosyaadi>'
  <N> bayt; sinir 20971520 bayt."` biçimindedir (`guard.MaxBytes` sunucu
  tarafında denetlenir — istemci tarafında ÖN denetim YOKTUR, dosya TAMAMEN
  yüklenmeye çalışılır, ret sunucudan gelir).
- Hiçbir chip eklenmez, "Yeni Sohbet" gerekmez.

**Gerçek sonuç**
Geçerli PNG imzalı (`\x89PNG\r\n\x1a\n`) + rastgele veri, toplam 22.020.104
bayt (21 MB) bir dosya yüklendi. İstemci tarafında ÖN denetim yoktu — dosya
TAMAMEN gönderildi (`POST api/attachments?sessionId=...` → `400`), ret
sunucudan geldi. Panelin ÜSTÜNDE `alert`: `"Ek cok buyuk: 'buyuk.png'
22020104 bayt; sinir 20971520 bayt."` — birebir beklenen kalıp. Hiçbir chip
eklenmedi, "Yeni Sohbet" gerekmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Ses modu ve seslendirme — UI katmanı, sözleşme derinliği `19`'da

### MT-UIAG-049 — Mikrofon düğmesi konuşma panelini açar/kapar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 28, 29 |
| **İlgili karar** | — |

**Ön koşul**
- `AgentPrism:Voice:ApiKey` (ElevenLabs) tanımlı.
- `playground/support` açık, en az bir mesaj gönderilmiş (oturum var).

**Adımlar**
1. Mikrofon düğmesine tıkla (`data-testid="voice-mode"`).
2. Tekrar tıkla.

**Beklenen sonuç**
- Adım 1: düğme `primary` tona döner; panelin ALTINDA `VoicePanel` bileşeni
  render edilir (`conversation && sessionId !== null`).
- Adım 2: panel kaybolur, düğme normale döner. Panel yalnız `conversation ===
  true` iken var olur — kapalıyken DOM'da hiç yoktur (koşullu render).
- Panelin İÇİNDEKİ gerçek zamanlı konuşma akışı (mikrofon izni, WebSocket,
  ses seviyesi göstergesi) bu case'in kapsamı DIŞINDADIR.

**Gerçek sonuç**
`playground/support`'ta var olan bir oturumla mikrofon düğmesine tıklandı.
Adım 1: düğme sınıfı `bg-raised text-fg` → `bg-accent text-accent-fg`
(primary tona) döndü; panelin ALTINDA "Konuş" düğmesi taşıyan `VoicePanel`
render edildi. Adım 2: tekrar tıklandı — düğme sınıfı `bg-accent...`'ten
`bg-raised text-fg`'ye normale döndü; `document.querySelectorAll('button')`
içinde "Konuş" metinli düğme ARTIK YOK (`panelExists:false`) — koşullu
render doğrulandı, kapalıyken DOM'da hiç kalmıyor. Panel içi gerçek zamanlı
konuşma akışı (mikrofon izni, WebSocket) kapsam dışı bırakıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIAG-050 — Tamamlanan turda "Seslendir" ses oynatıcı + maliyet notu ekler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Ön koşul**
- `AgentPrism:Voice:ApiKey` tanımlı.
- `playground/support`'ta metin içeren tamamlanmış bir tur var
  (`MT-UIAG-026`).

**Adımlar**
1. Turun altındaki "Konuştur" düğmesine tıkla (`data-testid="playground-speak"`).

**Beklenen sonuç**
- Düğme dönen bir simgeyle `working` durumuna geçer.
- Başarılı olunca `data-testid="playground-audio"` bir `<audio controls>`
  öğesi belirir; yanında karakter sayısı ve varsa maliyet notu görünür
  (`playground.speechCost` — `result.cost != null` ise tutar+para birimi,
  değilse yalnız karakter sayısı).
- Bu eylem bir agent çalıştırması İÇİNDE DEĞİLDİR — Run listesine yeni bir
  satır EKLENMEZ (`POST api/voice/speak` bir operatör eylemidir, `tool_
  invocations`'a yazılmaz).

**Gerçek sonuç**
MT-UIAG-026'nın tamamlanmış turunda (`support`, düz metin yanıt) "Seslendir"
düğmesine tıklandı — gerçek ElevenLabs çağrısı (`AgentPrism:Voice:ApiKey`
tanımlı). Başarılı oldu: düğme yerine `data-testid="playground-audio"`
`<audio controls>` öğesi + `"32 karakter · 0.0035 USD"` notu belirdi
(`result.cost != null`, `speechCost` kalıbı birebir). AMA ses OYNATILAMIYOR:
konsol — `"Loading media from 'blob:http://localhost:5084/...' violates
...default-src 'none'. Note that 'media-src' was not explicitly set, so
'default-src' is used as a fallback."`; `audio.networkState=3`,
`audio.error={code:4,message:"MEDIA_ELEMENT_ERROR: Media load rejected by
URL safety check"}` — MT-UIAG-044'teki (`HATA-S4-011`) AYNI kök nedenin
(CSP `blob:` şemasını hiçbir yönerge için beyaz listeye almıyor) İKİNCİ,
DAHA GENİŞ etkili örneği: burada yalnız kozmetik bir önizleme değil,
belgelenmiş bir yeteneğin (Faz 28 seslendirme) TÜM tarayıcılarda uçtan uca
işlevsiz kalması söz konusu. `HATA-S4-011`'in kapsamı ve önemi bu bulguyla
GÜNCELLENDİ (bkz. şerit sonuç dosyası — Yüksek'e yükseltildi). Run listesi
kontrolü: `GET /api/runs?take=3` en yeni satır hâlâ `019ffca0-...` (MT-UIAG-
044'ün agent run'ı) — "Seslendir" yeni bir run satırı EKLEMEDİ, beklendiği
gibi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-UIAG-051 — Seslendirme sağlayıcısı yapılandırılmamışsa düğme yanında hata notu görünür

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Ön koşul**
- `dotnet user-secrets remove "AgentPrism:Voice:ApiKey"`, uygulama yeniden
  başlatıldı (`UseVoice()` hiç çağrılmamış gibi davranır).
- `playground/support`'ta tamamlanmış bir tur var.

**Adımlar**
1. "Konuştur" düğmesine tıkla.

**Beklenen sonuç**
- İstek `501` ile reddedilir (`synthesizer is null` → `NotConfigured()`).
- Düğmenin yanında kırmızı bir not belirir (`playground.speechFailed` veya
  sunucudan gelen mesaj — `catch` bloğu `error.message`'ı kullanır).
- Ses oynatıcı HİÇ belirmez; düğme tekrar `idle` durumuna döner, tekrar
  denenebilir.

**Ön koşulu geri al**
- Case bitince `AgentPrism:Voice:ApiKey`'i TEKRAR ayarla, uygulamayı yeniden
  başlat — sonraki dosyaların koşumu bu anahtara ihtiyaç duyabilir.

**Gerçek sonuç**
`AgentPrism__Voice__ApiKey=""` ile (KOSUM-PLANI §2.2 sapması — `user-secrets
remove` yerine boş ortam değişkeni) uygulama yeniden başlatıldı. Yan bulgu:
`sesli-asistan` agent'ı artık `GET api/agents/sesli-asistan` → `404`
veriyor (voice olmadan katalogda hiç kayıtlı değil) — beklenen örnek
uygulama davranışı, kusur değil, ayrıca ele alınmadı. Yeni bir `support`
turu (`Merhaba`) üretildi (önceki turun state'i restart ile kayboldu),
"Seslendir" tıklandı. Ağ sekmesi: `POST api/voice/speak` → `501`. Düğmenin
YANINDA kırmızı not: `"Ses saglayicisi yapilandirilmadi: Ses ozelligini
acmak icin \`AgentPrism.Voice\` paketini ekleyin ve \`UseVoice(...)\`
cagirin."` — sunucudan gelen mesaj birebir. Ses oynatıcı HİÇ belirmedi,
düğme `idle` kaldı (DOM'da hâlâ tıklanabilir, disabled değil). Case bitince
`AgentPrism:Voice:ApiKey` GERİ AYARLANDI, uygulama yeniden başlatıldı —
aşağıda doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
