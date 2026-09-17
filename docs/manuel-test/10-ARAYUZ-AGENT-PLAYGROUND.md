# 10 — Arayüz: Agent ve Playground (`UIAG`)

> **Alan kodu:** `UIAG` · **Faz:** 5 (agent kataloğu, editör, playground), 19 (sürüm
> karşılaştırma — `agent-detail.tsx` içindeki `VersionCompare`), 142 (onay
> kartında varlık adı sunumu ve katlanır ham argümanlar)
> **Kaynak:** `src/Tracon.UI/frontend/src/screens/agents.tsx` (katalog) ·
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
> Sunucu tarafı davranış: `src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs`
> (özellikle `AgentRunStream.ExecuteStreamingAsync`'in `run`/`update`/`done`/
> `error` çerçeveleri), `Endpoints/AttachmentEndpoints.cs`,
> `Core/Attachments/AttachmentTypeGuard.cs`, `Core/Approvals/
> ToolApprovalRuleEvaluator.cs`, `AspNetCore/Internal/ToolApprovalResolver.cs`.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/10-ARAYUZ-AGENT-PLAYGROUND.md`](kosumlar/2026-08-13/10-ARAYUZ-AGENT-PLAYGROUND.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

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
2. Örnek uygulama çalışır (`cd samples/Tracon.Api && dotnet run`),
   `http://localhost:5080/tracon/` açılır, `manuel-test-token-2026` ile giriş
   yapılmıştır (bkz. [`09-ARAYUZ-GENEL.md`](09-ARAYUZ-GENEL.md) `MT-UI-001`/`002`).
3. `Tracon:Providers:OpenAI:ApiKey` tanımlıdır — bu dosyanın çoğu case'i
   gerçek bir OpenAI çağrısı yapar (`support` agent'ı, model `gpt-5.4-mini`).
   Gerçek para harcanır; her case'in "Adımlar" bölümü tam olarak kaç çalıştırma
   gerektiğini yazar.
4. Kalıcılık: `Tracon:PostgreSql:ConnectionString` tanımlıdır (agent
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
3. `researcher` satırını incele (harness rozeti için).
4. Tool sayısı hücresinin üzerine gel (`title` tooltip).

**Beklenen sonuç**
- `support` satırında `code` rozeti görünür (`OriginBadge`, `agent.origin === 'Code'`);
  üzerine gelince `agents.origin.code` metni kaynak adını (`sourceName`) taşır.
- `researcher` satırında ayrıca sarı `harness` rozeti görünür
  (`agent.usesHarness === true`).
- Tool sütununda sayı (`toolNames.length`) görünür; üzerine gelince tam tool
  adları virgülle ayrılmış tooltip'te belirir.
- Satırın en sağındaki "Çalıştır" düğmesi `playground/{ad}` rotasına gider.

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
  `Reader`/`Operator` rolündeki herhangi bir `Tracon:Ui:AuthToken` dışı
  erişim yolu (API anahtarı üstünden yalnızca REST) kullanılabilir; arayüz
  bugün TEK bir bearer token'ı destekler ve o token her zaman tam rol taşır —
  bu durumda case ⏭ **ATLA** işaretlenip gerekçe "13'ün API-anahtarı fixture'ı
  bekleniyor" olarak yazılır.

**Adımlar**
1. `canAdminister = false` olan bir erişimle `/tracon/agents` aç.

**Beklenen sonuç**
- `PageHeader`'ın `actions` alanında "Yeni Agent" düğmesi HİÇ render edilmez
  (`meta.roles.canAdminister && (<Link.../>)`— koşul `false` olduğunda ağaçtan
  tamamen düşer, yalnız `disabled` olmaz).
- Liste normal şekilde okunur (`Reader` rolü `GET /api/agents`'ı geçirir).

### MT-UIAG-003 — Boş formda Doğrula/Kaydet devre dışıdır; zorunlu alanlar dolunca etkinleşir

Sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `/tracon/agents/new` açık, form tamamen boş.

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

---

### MT-UIAG-004 — `FIX-AGENT-02` ile minimal agent oluşturma; canlı JSON önizlemesi gönderilen gövdeyle birebir eşleşir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `/tracon/agents/new` açık.

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
- `/tracon/agents/new` açık.

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
1. `/tracon/agents/new` aç, `Ad`: `manuel-bos` yaz.
2. `Sağlayıcı`/`Model` doldur, "Oluştur"a tıkla.

**Beklenen sonuç**
- Form kapanmaz; sayfanın üstünde kırmızı bir `ErrorNote` görünür.
- Mesaj sunucunun `ProblemDetails.detail`'ini **birebir** taşır: `"A
  definition named 'manuel-bos' already exists. Use PUT to update it."`
  (2026-09-16 turunda düzeltildi: metin İngilizce — `AgentEndpoints.cs:543`
  — K-228 gereği runtime metni İngilizce'dir; spec'in Türkçe metni bayattı.)
- Formdaki veri kaybolmaz — kullanıcı adı değiştirip yeniden deneyebilir.

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
- `/tracon/agents/new` açık.

**Adımlar**
1. `Ad`: `support`. `Sağlayıcı`/`Model` doldur, "Oluştur"a tıkla.

**Beklenen sonuç**
- `ErrorNote`, `MT-UIAG-006`'dan **farklı** bir gerekçe metni taşır:
  `"'support' is an agent defined in code and cannot be changed from the
  management API. Code wins name conflicts, so a definition written with
  the same name would never resolve."` (2026-09-16 turunda düzeltildi: metin
  İngilizce — `AgentEndpoints.cs:538-540` — K-228, aynı bayatlık MT-UIAG-006'da
  da bulundu.)
- Katalogda ikinci bir `support` satırı **oluşmaz**.

---

### MT-UIAG-008 — "Doğrula" kaydetmeden `AgentValidationReport`'u gösterir, hiçbir kayıt oluşmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 34 |
| **İlgili karar** | — |

**Ön koşul**
- `/tracon/agents/new` açık.

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
- `/tracon/agents/new` açık; `Ad`/`Sağlayıcı`/`Model` geçerli değerlerle
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

---

### MT-UIAG-010 — Harness açılınca ek alanlar görünür, kapatılınca gövdede `harness: null` gider

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 10 |
| **İlgili karar** | — |

**Ön koşul**
- `/tracon/agents/new` açık, zorunlu alanlar dolu.

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
- `/tracon/agents/new` açık.

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
1. `/tracon/agents/new` aç, "Beceriler" panelinde art arda 10 checkbox işaretle.
2. 11. beceriyi işaretlemeyi dene.

**Beklenen sonuç**
- Adım 2: 11. checkbox `disabled` — `!checked && limitReached`
  (`form.skillNames.length >= 10`) koşulu true olur; tıklama hiçbir etki yapmaz.
- Halihazırda işaretli 10 becerinin checkbox'ları hâlâ TIKLANABİLİR (işareti
  kaldırmak serbesttir — `disabled` yalnız YENİ seçimi engeller).
- `enabled: false` olan bir beceri her koşulda `disabled`'dır (sayaçtan
  bağımsız).

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
- Adım 3: kayıt `400` ile reddedilir; `ErrorNote` başlığı `"Call graph
  invalid"` metnini taşır (2026-09-16 turunda düzeltildi: metin İngilizce,
  K-228 — spec'in Türkçe başlığı bayattı), ayrıntı `AgentCallGraph.Validate`'in
  ürettiği çevrim açıklamasıdır. Form kapanmaz, veri kaybolmaz.

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
  Kaydet"e basıldığında `409` hatası görünür: `"Code-defined agent cannot
  be modified: 'support' is defined in code. Code definitions are
  validated at compile time and cannot be changed from the management
  API; update the application code to change it."` (2026-09-16 turunda
  düzeltildi: metin İngilizce, K-228 — spec'in Türkçe metni bayattı) —
  arayüz bu güvenliği yalnızca DÜĞMEYİ GİZLEYEREK sağlar, URL seviyesinde
  bir engel yoktur; gerçek sınır sunucudadır.

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
- `definition`'ın null olup olmadığı kod agent'ının NASIL kaydedildiğine
  bağlıdır (2026-09-16 turunda düzeltildi — `AgentEndpoints.cs`'in kendi
  `WithDescription`'ı bunu açıkça ayırıyor, spec'in eski "her kod agent'ının
  definition'ı null'dır" varsayımı BAYAT): `AddAgent(new AgentDefinition
  {...})` ile (deklaratif) kayıtlı bir agent'ın `definition`'ı DOLU gelir
  (`instructions` dahil tam nesne) — bu yüzden "Tanım" paneli RENDER EDİLİR
  ve `noDefinitionForCode` notu GÖRÜNMEZ. Yalnız `AddAgent(name, factory)`
  ile (fabrika) kayıtlı bir agent'ın `definition`'ı `null` gelir — o zaman
  panel gizlenir ve not görünür. `samples/Tracon.Api/Program.cs`'teki
  **her** agent (`support` dahil, 15 kayıt) deklaratif — bu ortamda fabrika
  stili tek bir örnek YOK, yani `null`-definition/`noDefinitionForCode`
  dalı bu ortamda ampirik olarak hiç tetiklenemiyor (ölçüldü: `GET
  /api/agents/support` `definition` alanı dolu, `factoryInstructions: null`
  döndü). Bu bir Tracon kusuru değil, fixture kapsamı boşluğu — açık kalem
  olarak `00-INDEKS.md`'ye yazılmalı.
- Sayfanın altında "Sürümler" bölümü hiç YOKTUR (`isEditable` koşulu
  `VersionHistory`'yi de kapsar) — kod agent'ının versiyon geçmişi olmaz.

---

### MT-UIAG-019 — Sil bir doğrulama adımı ister; iptal edilirse hiçbir şey olmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 175 |
| **İlgili karar** | K-785, K-786 |

🚨 **Faz 175'te yeniden yazıldı.** Bu case tarayıcının kendi `window.confirm`
kutusunu bekliyordu; K-785 onu konsoldan tamamen kaldırdı. Adım silinmedi,
konsolun kendi `ConfirmDialog`'una taşındı — agent silmek §175.3 ölçütünün (a)
dalını geçer (sürüm geçmişi `ON DELETE CASCADE` ile gider).

**Ön koşul**
- `manuel-bos` (`MT-UIAG-004`) var — bu agent bundan sonra artık gerekmiyor,
  bu case'te silinecek.

**Adımlar**
1. `agents/manuel-bos` aç, "Sil" düğmesine **klavyeyle odaklan** (basma).
2. "Sil"e bas, sonra `Esc`'e bas.
3. "Sil"e tekrar bas, `Tab` ile gez, `İptal`e bas.
4. "Sil"e tekrar bas ve `Onayla`ya bas.

**Beklenen sonuç**
- Adım 1: tooltip görünür ve tanımın **ve sürüm geçmişinin** gideceğini söyler.
- Adım 2: konsolun kendi dialogu açılır (tarayıcı kutusu **değil**); başlık agent
  adını gömer, gövde aynı etki cümlesini taşır, açılış odağı `İptal`dedir.
  `Esc` hiçbir istek göndermeden kapatır; agent hâlâ vardır.
- Adım 3: `Tab` dialog içinde döner, dışarı çıkmaz. `İptal` sonrası odak "Sil"
  düğmesine geri döner.
- Adım 4: `agents` listesine yönlenilir; `manuel-bos` artık listede yok.

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
`src/Tracon.UI/frontend/src/components/diff-view.tsx:6-9`'un kendi XML
yorumu "Line-by-line diff of two texts" der; `diffLines()` satırı BÜTÜN
olarak `added`/`removed` işaretler, satır İÇİNDE hangi alt-dizinin
değiştiğini ayırt etmez. Tasarım kasıtlı (K-149'un da referans verdiği
"bütün olarak okunur/yazılır" deseni); doküman düzeltildi, koda göre.
`İlgili karar: K-045` başlığı da hatalı — K-045 yönlendirme (routing)
kararıdır, diff bileşeniyle ilgisi yok; muhtemelen kopyala-yapıştır hatası.

**Doğrulama sorgusu**
```bash
curl -s "http://localhost:5080/tracon/api/agents/manuel-destek/versions/1/diff/2" \
  -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool
```

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

---

### MT-UIAG-024 — "Geri Al" tek tık kalır; Sil doğrulama ister — asimetri BİLİNÇLİDİR

🚨 **Faz 175'te yeniden yazıldı.** Bu case 2026-08-09'da açıklanamayan bir
asimetriyi kaydediyordu. Faz 175 ikisini de §175.3 ölçütüne bağladı (K-786) ve
asimetri artık bir karardır, bir kaza değil:

- **Sil** ölçütün (a) dalını geçer — tanımla birlikte `agent_definition_versions`
  satırlarının tamamı `ON DELETE CASCADE` ile gider ve arayüzden geri getirilemez
  → `ConfirmDialog` (`MT-UIAG-019`).
- **Geri Al** ikisini de geçmez — eski sürüm geçmişte kalır, işlem tekrar geri
  alınabilir → yalnız katman 1: `agentDetail.rollbackEffect` tooltip'i hangi
  sürümün canlı olacağını karar anında söyler (Faz 164).

Onay yorgunluğu gerçek bir maliyettir: her şeyi doğrulatmak hiçbirini
doğrulatmamakla aynı yere çıkar. Bu case o kararın koşumda teyididir.

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
2. Tıklamadan HEMEN sonra ekranı gözlemle: bir doğrulama dialogu çıkıyor mu?
   (Tıklamadan önce düğmeye odaklanıp tooltip'i de oku.)
3. İşlem bitince "Sürümler" tablosuna bak.

**Beklenen sonuç**
- Adım 2: HİÇBİR doğrulama dialogu çıkmaz — düğme `busy` durumuna geçer ve
  istek hemen gider. Bu bilinçlidir (Faz 175, K-786): geri alma §175.3
  ölçütünü geçmez. Ama tek tık **sessiz** değildir — odakta
  `agentDetail.rollbackEffect` tooltip'i hangi sürümün canlı olacağını söyler.
- Adım 3: yeni bir `v4` satırı belirir, içeriği `v1`'in içeriğiyle AYNIDIR
  (yeni sürüm olarak yazılır, `v1`'e geri SARILMAZ — sürüm sayacı artmaya
  devam eder).
- `v4` satırında `Güncel` rozeti; `v1`'in kendi satırında hâlâ "Geri Al"
  düğmesi görünür (kendine geri dönüş engellenmez, yalnız güncel sürüme geri
  dönüş engellenir).

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
- Yanıt metin metin akar; `ap-stream-caret` sınıfı son metin bloğunda
  DOM'a eklenir ama görsel karşılığı YOKTUR — bkz. `HATA-S1-028` (2026-09-16
  turunda bulundu): CSS yalnız `.tracon-stream-caret::after` tanımlıyor,
  bileşen `ap-stream-caret` uyguluyor, ikisi hiç eşleşmiyor.
- **Hiçbir** `data-testid="tool-card"` öğesi belirmez.
- Tur `done` olunca "Seslendir" (`playground.speak`, spec'in eski "Konuştur"
  metni bayattı — 2026-09-16 turunda düzeltildi) düğmesi görünür
  (`spokenText.length > 0`).

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
  anında `useState(item.state !== 'ok')` → `true`), `"sürüyor"` rozeti
  (2026-09-16 turunda düzeltildi: spec'in `"Çalışıyor"` metni bayattı —
  gerçek i18n anahtarı `runs.status.running` küçük harfle `"sürüyor"`).
- Adım 3: kart HÂLÂ açık — bileşen aynı `key={item.id}` ile yeniden render
  edildiği için `useState`'in başlangıç değeri BİR DAHA hesaplanmaz; rozet
  `"bitti"`'ye döner (spec'in `"Tamamlandı"` metni de bayat —
  `transcript.done` i18n anahtarı), "Argümanlar" ve "Sonuç" bölümleri dolu görünür
  (`get_order_status` çağrısının argümanı `ORD-1001` içerir).
- Adım 4: kullanıcı elle açıp kapatabilir — bu davranış yalnız İLK render'da
  otomatiktir.

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
- `data-testid="approval-card"` görünür: `cancel_order` mono yazı tipiyle
  görünür. Spec'in "hiçbir `IToolApprovalPresenter` kayıtlı değilse
  gösterilecek varlık adı yoktur" varsayımı bu ortam için BAYAT (2026-09-16
  turunda düzeltildi): `samples/Tracon.Api/Program.cs:146`
  `OrderApprovalPresenter`'ı HER ZAMAN kayıtlı tutuyor (kod donuk, kaldırılamaz)
  — bu yüzden başlıkta `"Order ORD-1001"` varlık adı da görünür. "Presenter
  kayıtlı değil" dalı bu ortamda ampirik olarak hiç sınanamıyor; o dal zaten
  `MT-UIAG-053`'ün konusu (Faz 142, "presenter varken varlık adı gösterir").
  `tools.approvalRequired` rozeti (`"onay gerekli"`) doğru görünür.
- "Argümanlar" satırı KATLI durur (Faz 142): `data-testid=
  "approval-toggle-arguments"` düğmesine tıklamadan argüman içeriği
  görünmez. Tıklandığında `orderId: "ORD-1001"` içeren bölüm açılır.
- Onay kartından SONRA hiçbir metin bloğu gelmez — MAF çalıştırmayı burada
  DURDURUR (istek bekleyen bir onaya rağmen tamamlanmış sayılır).
- Tur durumu `done` olur (`event: done` çerçevesi gelir), `failed` DEĞİL.
- Onayla/Reddet düğmeleri görünür ve tıklanabilir — `onDecide` yalnız
  `turn.status === 'done'` iken geçirilir, bu koşul artık sağlanmıştır.

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
  call invocation rejected.") olarak sentezliyor; Tracon'in transcript
  render'ı HER `FunctionResultContent`'i (kaynağı ister gerçek tool
  çalıştırması ister red-stub'u olsun) bir tool kartına çeviriyor. Doğru
  beklenti: yeni turda `cancel_order` İKİNCİ bir kartla (rozet `bitti`/`ok`
  — `failed` DEĞİL, çünkü MAF açısından "tamamlanmış" bir çağrı) belirir,
  ama `Sonuç` alanı gerçek `cancel_order` tool gövdesinin (`OrderTools.
  CancelOrder`) ÜRETTİĞİ bir metin DEĞİL, sabit red mesajıdır — asıl tool
  kodu HİÇ çalışmaz (bu kısım orijinal iddiayla tutarlı kalıyor).

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
FROM tracon.tool_approval_rules
WHERE agent_name = 'support' AND tool_name = 'cancel_order';
```
Beklenen: tek satır, `arguments_hash IS NULL` (argüman bazlı sınırlama yok).

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
1. Üstteki agent seçiciden `researcher`'yi seç.
2. Tarayıcı adres çubuğunu kontrol et.

**Beklenen sonuç**
- Adres `playground/researcher`'ye değişir (`navigate` çağrısı).
- Sohbet paneli sıfırlanır (`reset()` seçim değişmeden ÖNCE çağrılır — eski
  agent'ın turları yeni agent'ın ekranında sızmaz).
- Devam eden bir akış varken agent değiştirilirse `reset()` içindeki
  `abort.current?.abort()` bağlantıyı de kesip sonra sıfırlar.

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

### MT-UIAG-041 — Var olmayan agent adıyla akış hiç başlamadan ÜSTTE ve tur içinde hata gösterir

Negatif senaryo — akış hiç başlamadan gelen `ProblemDetails` yolu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `http://localhost:5080/tracon/#/playground/manuel-yok-boyle-agent`
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

---

### MT-UIAG-042 — `FIX-PROMPT-05` guard engeli → yalnız tur içi hata; ÜST hata kutusu YOK

Negatif senaryo — akış İÇİNDE gelen `event: error` yolu.
`TraconContentBlockedException`, `TraconException`'dan türediği için
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
  `TraconContentBlockedException: ...` ile başlar (engellenen metnin
  KENDİSİ mesajda YOKTUR — yalnız guard/kural bilgisi).
- `GET /api/runs/{runId}` (Run bağlantısından) çağrılırsa `runs.error_type =
  'content_blocked'` görünür (bu alanın derinliği `22-GUARDRAIL-VE-YAPISAL-
  CIKTI.md`'nin konusudur; burada yalnız var olduğu doğrulanır).

---

### MT-UIAG-043 — Sağlayıcı hatası artık SSE `error` çerçevesi üretir; tur SESSİZCE "tamamlandı" görünmez (K-296, düzeltildi)

Negatif senaryo — **düzeltilmiş kusur (2026-08-10)**. `05-SAGLAYICI-OPENAI.md`
`MT-OAI-043` ve `06-SAGLAYICI-DIGER.md` `MT-PROV-036`/`042`, gerçek bir
sağlayıcı istisnasının (`ClientResultException`, `AnthropicApiException`)
`AgentRunStream.ExecuteStreamingAsync`'in o zamanki dar `catch` bloğuna
uymadığını ve bağlantının `error` çerçevesi ÜRETMEDEN kapandığını ölçmüştü.
Bu case, o boşluğun **arayüzde nasıl göründüğünü** ölçmek için yazılmıştı:
`@tracon/client`'ın `readSse`'si akış sonunu (`reader.read()`'in `done: true`
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
curl -s "http://localhost:5080/tracon/api/runs/<runId>" \
  -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool
```

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

---

### MT-UIAG-048 — 20 MB sınırını aşan dosya "Ek çok büyük" hatası verir

Negatif/sınır senaryosu — varsayılan `TraconAttachmentOptions.MaxBytes = 20
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

### MT-UIAG-049 — Mikrofon düğmesi konuşma panelini açar/kapar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 28, 29 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon:Voice:ApiKey` (ElevenLabs) tanımlı.
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

---

### MT-UIAG-050 — Tamamlanan turda "Seslendir" ses oynatıcı + maliyet notu ekler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon:Voice:ApiKey` tanımlı.
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
- `dotnet user-secrets remove "Tracon:Voice:ApiKey"`, uygulama yeniden
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
- Case bitince `Tracon:Voice:ApiKey`'i TEKRAR ayarla, uygulamayı yeniden
  başlat — sonraki dosyaların koşumu bu anahtara ihtiyaç duyabilir.

### MT-UIAG-052 — Dar ekranda (375px) agent editor ve playground yatay taşma yapmaz

Sınır durumu. `09-ARAYUZ-GENEL.md`'deki `MT-UI-043` genel ekranları kapsar;
Faz 109 bu iki ekranı çok sayıda alt modüle böldüğü için ikisi burada ayrıca
ölçülür.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 109 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. DevTools → Device toolbar → genişliği 375px yap.
2. `agents/new` (kimlik, model, tool/skill/callable-agent, harness, context,
   önizleme section'larının tamamını aşağı kaydırarak) gez.
3. `playground/support`'u aç; bir mesaj gönder, bir dosya ekle.

**Beklenen sonuç**
- Sayfanın kendisi yatay kaymaz. Önizleme paneli (agent editor) ve ek
  çipleri/parametre alanları (playground) dar genişlikte sarar, taşmaz.
- Gönder/Onayla/Reddet düğmeleri hâlâ dokunulabilir boyuttadır.

---

### MT-UIAG-053 — Kayıtlı bir `IToolApprovalPresenter` varken onay kartı başlıkta varlık adını gösterir (Faz 142)

`MT-UIAG-028`'in aksine burada `samples/Tracon.Api`'nin kendi
`OrderApprovalPresenter`'ı devrede — sunucu SSE akışına ayrı bir
`event: approvals` çerçevesi ekler, kart onu `requestId` ile eşleştirip
kendini günceller.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 142 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` açık, yeni sohbet.

**Adımlar**
1. `ORD-1001 siparisimi iptal et` gönder.
2. Akış bitene kadar bekle.

**Beklenen sonuç**
- Kartın başlığında `Order ORD-1001` görünür (kalın), altında ince/mono
  yazıyla `cancel_order` durur — `MT-UIAG-028`'deki gibi yalnız tool adı
  DEĞİL.
- Altında `Cancel order ORD-1001 for Priya Shah.` mesajı görünür.
- "Argümanlar" satırı yine KATLI başlar; açılınca `orderId: "ORD-1001"`
  görünür.

---

### MT-UIAG-054 — Konsolda açıklamayı düzenlemek, editörün kontrolü OLMAYAN alanları düşürmez (B01)

Regresyon. `PUT /api/agents/{name}` tam değiştirmedir: form neyi taşımıyorsa o
alan korunmaz, SİLİNİR. Ölçülen kayıp `parameters`, `sharedInstructionsName`,
`model.providerSettings`, `model.responseCache`, `model.allowConcurrentToolCalls`
ve yalnız vector search açık olan `memory` bloğuydu — hiçbirinin editörde
kontrolü yok, hepsi artık `PreservedFields` ile taşınır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-725 |

**Ön koşul**
- HTTP ile bir definition yaz: `parameters` (bir kalem),
  `sharedInstructionsName`, `model.allowConcurrentToolCalls: true` ve
  `model.providerSettings` dolu olsun.

**Adımlar**
1. Konsolda o agent'ı aç, **yalnız açıklamayı** değiştir, kaydet.
2. `GET {prefix}/api/agents/{name}` ile kaydı oku.

**Beklenen sonuç**
- `description` yeni değeri taşır.
- `parameters`, `sharedInstructionsName`, `model.providerSettings` ve
  `model.allowConcurrentToolCalls` **değişmemiştir** — hiçbiri boşalmaz,
  `null` olmaz.
- Editörde bu alanlar için bir kontrol GÖRÜNMEZ; korunmaları düzenlenebilir
  olmalarını gerektirmez.

---

### MT-UIAG-055 — Yalnız vector search açık olan `memory` bloğu kaydetmede kaybolmaz (B01)

Regresyon. `memoryHasAnything` yalnız file/todo/text search bakıyordu; yalnız
`enableVectorSearch` açıksa istek `memory: null` gidiyordu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-725 |

**Ön koşul**
- `IVectorSearchStore` kayıtlı (PostgreSQL) bir ortam.
- Bir definition'da yalnız `memory.enableVectorSearch: true` ve
  `memory.vectorCollection` dolu; diğer memory bayrakları kapalı.

**Adımlar**
1. Konsolda agent'ı aç, adı/açıklamayı değiştirmeden kaydet.
2. Kaydı `GET` ile oku.

**Beklenen sonuç**
- `memory` **null değildir**; `enableVectorSearch` hâlâ `true`,
  `vectorCollection` korunmuştur.

---

### MT-UIAG-056 — Boş bir kiracıda her liste ekranı ilkini nasıl oluşturacağını söyler (Faz 165)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 165 |
| **İlgili karar** | — |

**Ön koşul**
- Hiç agent, skill, workflow, trigger, deney, eval seti ve MCP server bulunmayan
  temiz bir kiracı. Rol: administrator.

**Adımlar**
1. Sırayla `agents` · `skills` · `workflows` · `triggers` · `experiments` ·
   `evals` · `mcp` ekranlarını aç.
2. Sonra `approvals` · `audit` ekranlarını ve `settings`'teki kota ile script
   izni panellerini aç.

**Beklenen sonuç**
- Birinci gruptaki her boş durum **ne olmadığını** ve **ilkinin nasıl
  oluşturulacağını** söyler; birincil aksiyonun metni başlıktaki düğmeyi
  tekrarlamaz ("İlk agent'ı oluştur", "İlk server'ı ekle" gibi).
- İkinci grupta **sahte bir aksiyon yoktur**: boş olmak istenen durumdur (bekleyen
  onay yok, kayıt yok, tanımlı kota yok, verilmiş script izni yok). Metin bunu
  söyler, "ilkini oluştur" demez. 👤

---

### MT-UIAG-057 — Bir düğmenin açıklaması hem işaretçiyle hem klavyeyle görünür (Faz 165)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 165 · 164 |
| **İlgili karar** | — |

**Ön koşul**
- En az iki sürümü olan, veritabanında saklanan bir agent. Rol: administrator.

**Adımlar**
1. `agents/<ad>` aç, sürüm geçmişindeki eski bir satırın "Geri al" düğmesinin
   üzerine gel.
2. İşaretçiyi uzaklaştır, aynı düğmeye `Tab` ile odaklan.
3. `Esc` bas.
4. `models` ekranında bir sağlık badge'ine hem `Tab` ile odaklan hem üstüne gel.
5. `runs` ekranında "Ağaç token" sütun başlığına `Tab` ile odaklan.

**Beklenen sonuç**
- Açıklama **iki yolda da** görünür ve geri almanın **sonucunu** söyler: hangi
  sürüm canlı olacak, mevcut sürümün geçmişte kalacağı, bundan sonraki run'ların
  hangi sürümü kullanacağı. 👤
- `Esc` açıklamayı kapatır, altındaki katmanı kapatmaz.
- Badge ve sütun başlığı klavyeyle **ulaşılabilir**; açıklamaları görünür.
- Hiçbir açıklama için işaretçiyi bekletmek gerekmez ve hiçbiri yalnız
  işaretçiyle görünmez. 👤

---

### MT-UIAG-058 — Yükleme hatası boş bir form göstermez (Faz 165)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 165 |
| **İlgili karar** | — |

Regresyon sınıfı. `isPending` istek düşer düşmez `false` olur; hata dalı
yoksa editör "boş yüklenmiş bir kayıt" gibi görünür ve o hâlden kaydetmek
gerçek tanımı siler.

**Ön koşul**
- Saklanan bir skill, bir trigger ve bir workflow. Rol: administrator.
- Tarayıcı geliştirici araçlarından ilgili `GET` ucunu 500 döndürecek şekilde
  kesin (veya sunucuyu o an durdurun).

**Adımlar**
1. `skills/<ad>/edit` aç.
2. `triggers/<ad>/edit` aç.
3. `workflows/<ad>/edit` aç.
4. Her birinde "tekrar dene"ye bas, kesme kaldırılmış hâlde.

**Beklenen sonuç**
- Üçü de **hata** gösterir, boş form göstermez; sunucunun kendi metni çevrilmeden
  görünür.
- "Kaydet" düğmesi bu hâlde erişilebilir değildir (form hiç render edilmez).
- Tekrar deneme isteği yeniler ve form gerçek değerlerle dolar. 👤
