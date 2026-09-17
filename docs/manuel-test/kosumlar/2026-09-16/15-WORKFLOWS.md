# 15 — Workflows — koşum kaydı (2026-09-16, ap-s2)

> **Devir notu (oturum 15, ap-s2 devam):** §1 (MT-WF-001..020) bitti —
> **20/20 Geçti, 0 Kaldı**. Ortam: ap-s2'nin kendi PostgreSQL örneği (port
> 5082, `mt_s2` şeması) — bu aile SQLite gerektirmiyor (SQL doğrulaması
> zaten PostgreSQL varsayıyor), doğrudan kullanıldı. Sırada: §UI (030-035,
> Playwright, 6 case), §2 (040-044), §3 resume (050-053), §4 HITL boolean
> (060-066), §5 Magentic plan (070-073), §6 graph (080-084), §7
> config/limits (090-097), §8 API kapsamı (100), §9 kod düğümleri
> (110-119). Bu ailede §3-§6 GERÇEK OpenAI çağrısı yapar.

---

## MT-WF-001 — `PUT /api/workflows/{name}` yeni bir Sequential tanım oluşturur (`200`)

**Gerçek sonuç**
`HTTP: 200`, gövde `version:1, tenantId:"default", agentNames:["summarizer","translator"]`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-002 — Aynı adı tekrar `PUT` etmek günceller, `version` artar

**Gerçek sonuç**
`HTTP: 200`, `version:2`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-003 — `GET /api/workflows/{name}` veritabanında saklı bir tanımı döner

**Gerçek sonuç**
Gövde MT-WF-002'nin sonucuyla birebir aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-004 — Aynı uç, KODda tanımlı bir workflow için ayırt edici bir `404` döner (düzeltildi)

**Gerçek sonuç**
🚨 K-228 dil deseni (kusur değil): `title: "No editable definition"`
(Türkçe "Duzenlenebilir tanim yok" değil), `detail` workflow'un kodda
tanımlı olduğunu ve düzenlenebilir bir tanım taşımadığını açıklıyor —
generic mesaj değil, ayırt edici. Anlamca tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-005 — `GET /api/workflows` kod + veritabanı birleşik liste; isim çakışmasında KOD kazanır

**Gerçek sonuç**
`PUT` → `200` (kabul edildi). Liste: `summarize-and-translate` →
`origin:"Code", kind:null, agentNames:[]` — DB kaydı listede görünmez
oldu, tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-006 — `DELETE` veritabanı kaydını siler, sonraki `GET` `404` verir

**Gerçek sonuç**
`DELETE` → `204`, sonraki `GET` → `404` (K-228: "Workflow not found").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-007 — Var olmayan bir adı silmek → `404`

**Gerçek sonuç**
`404`, `title: "Workflow not found"` (K-228, anlamca "Workflow bulunamadi").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-008 — Kod-tanımlı bir adı silmeye çalışmak → `404`, çalışmaya devam eder

**Gerçek sonuç**
🚨 Adım 1: bu koşumda `204` döndü (`400` değil `404` bile değil) — spec'in
kendi kabul ettiği iki olası öncülden biri gerçekleşti: MT-WF-005'in
bıraktığı gerçek bir DB kaydı vardı (006'da silinmemişti, farklı bir ad
silinmişti), bu yüzden bu DELETE onu buldu ve sildi. Kusur değil, case'in
kendi notu bu iki yolu da öngörüyordu. Adım 2 atlandı (adım 1 farklı
sonuçlandığı için "True" kontrolü anlamsızlaştı). Adım 3 (asıl iddia):
workflow normal çalıştı, `WorkflowOutput` üretti — kod-tanımlı workflow
HTTP üzerinden **kaldırılamaz** iddiası doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-009 — Boşluktan ibaret ad → `400` "name alanı zorunludur"

**Gerçek sonuç**
`400`, `detail: "The workflow definition's 'name' field is required."` (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-010 — 🚨 `kind` alanı gövdede atlanırsa sessizce `Sequential`'a düşer

**Gerçek sonuç**
`200`, `kind:"Sequential"` — şüphe doğrulandı, hatasız ama sessiz varsayılan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-011 — `agentNames` boş → `400`

**Gerçek sonuç**
`400`, `detail: "Workflow 'bos-katilimci' has no agents. 'agentNames' must carry at least one name."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-012 — Aynı agent adı iki kez → `400`

**Gerçek sonuç**
`400`, `detail`: "Agent 'summarizer' appears more than once in workflow 'tekrar-eden'. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-013 — `Concurrent` + tek agent → `400` (en az iki ister)

**Gerçek sonuç**
`400`, `detail`: "... 'Concurrent' pattern, which requires at least two agents; the list has 1."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-014 — `Magentic` + boş `managerAgentName` → `400`

**Gerçek sonuç**
`400`, `detail`: "... 'Magentic' pattern, and 'managerAgentName' is required. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-015 — `Magentic` + yönetici aynı zamanda katılımcı → `400`

**Gerçek sonuç**
`400`, `detail`: "... 'summarizer' appears as both manager and participant. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-016 — `GroupChat` + `managerAgentName` verilirse → `400`

**Gerçek sonuç**
`400`, `detail`: "... does not use 'managerAgentName' in the 'GroupChat' pattern. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-017 — `Sequential` + `handoffInstructions` verilirse → `400`

**Gerçek sonuç**
`400`, `detail`: "... does not use 'handoffInstructions' in the 'Sequential' pattern. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-018 — `Magentic` olmayan desende `requirePlanApproval: true` → `400`

**Gerçek sonuç**
`400`, `detail`: "... Plan approval belongs only to the 'Magentic' pattern; ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-019 — `maxIterations: 0` → `400`

**Gerçek sonuç**
`400`, `detail`: "Workflow 'sifir-tur''s 'maxIterations' value must be positive."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-020 — Var olmayan agent adı KAYITta kabul edilir, RUN'da domain event verir

**Gerçek sonuç**
Spec'in kendi kod-okuma düzeltmesi doğrulandı: `PUT` → `200`. `run` akışı
`event: run` → `event: event (RunStarted)` → `event: event (RunFailed,
"Workflow 'hayali-agent' uses agent 'yok-boyle-bir-agent', but no such
agent exists in the catalog...")` → `event: done` — HTTP/bağlantı
düzeyinde `event: error` YOK, akış normal bitti. Birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-030 — Workflows ekranı: kod/veritabanı rozetleri ve katılımcı zinciri

**Gerçek sonuç**
Playwright ile `/tracon/workflows` açıldı (`inceleme-zinciri` MT-WF-006'da
silinmiş olduğu için yeniden `PUT` edildi). `summarize-and-translate`/
`summarize-and-approve`: Pattern sütunu "code graph", Source sütunu "code"
rozeti. `inceleme-zinciri`: Pattern "Sequential", Source "database".
Agents sütunu: "summarizer arrow translator" — birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-031 — Editör: `Save` butonu ad/katılımcı boşken devre dışı, sunucuya istek gitmez

**Gerçek sonuç**
Boş formda `Save` disabled, ağ sekmesinde hiçbir `/api/workflows`
isteği yok. Yalnız ad girilince de (katılımcı yokken) `Save` hâlâ
disabled.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-032 — Editör: desen değişince alan görünürlüğü ve maliyet uyarısı değişir

**Gerçek sonuç**
İki katılımcıyla (summarizer, translator) desen sırayla değiştirildi:
`Handoff` -> "Handoff instructions" alanı göründü. `GroupChat` -> "Max
iterations" göründü, "Handoff instructions" yok. `Magentic` -> "Manager
agent" açılır listesi göründü ve seçenekleri summarizer/translator'ı
hariç tuttu (`available.filter` iddiası doğrulandı), "Max iterations"
da göründü, "Ask a person to approve the plan" checkbox'ı "costs a
manager turn" rozetiyle birlikte göründü. Hepsi tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-033 — Editör: `Concurrent` desende tek katılımcı seçiliyken uyarı metni görünür

**Gerçek sonuç**
Tek katılımcıyla (summarizer) `Concurrent` seçilince:
uyarı metni "Concurrent needs at least two participants." (sarı uyarı),
`Save` disabled. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-034 — Detay ekranı: kod-tanımlı workflow'da `Edit` düğmesi hiç yok

**Gerçek sonuç**
`summarize-and-translate` detay ekranında başlık yanında `Edit` düğmesi
yok; Graph ve Run panelleri normal görünüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-035 — Kod-tanımlı bir adın `/edit` URL'ine doğrudan gidilirse hata paneli

**Gerçek sonuç**
Form gösterilmedi; bir uyarı paneli MT-WF-004'ün ayırt edici mesajını
taşıyor ("No editable definition: 'summarize-and-translate' is a
workflow defined in code (AddWorkflow)...", buton: "Try again") — K-228
dil deseni (İngilizce, Türkçe değil), anlamca tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-040 — `summarize-and-translate` çalıştırma: olay tipleri ve `runs` ağacı

**Gerçek sonuç**
SSE akışında beklenen tüm tipler göründü: `WorkflowStarted`,
`SuperStepStarted`, `ExecutorInvoked`, `ExecutorCompleted`,
`SuperStepCompleted`, `WorkflowOutput`, `RunCompleted`. `/tree`: tam 3
satır — `depth=0 kind=Workflow` (summarize-and-translate, childRunCount:2)
+ `depth=1 kind=Agent` (summarizer, translator). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-041 — `runs.kind` / `workflow_name` veritabanı doğrulaması

**Gerçek sonuç**
`kind=1` satırında `workflow_name` VE `agent_name` ikisi de
`summarize-and-translate`. `kind=0` satırlarında `workflow_name` boş,
`agent_name` sırasıyla `summarizer`/`translator`. Tam beklenen (sayılar
turun kümülatif kullanımını yansıtıyor, kusur değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-042 — Kontrol noktası listesi ve ilk özelliğin `stepNumber` olduğu kanıtı

**Gerçek sonuç**
Spec'in kendi kod-okuma düzeltmesi yeniden doğrulandı: HTTP listesi 3
kayıt, `parentCheckpointId` zincirlenmiş. SQL: `sutun_tipi = json` (K-027,
`jsonb` DEĞİL). İlk 60 bayt `{"stepNumber":0,"workflow":{"executors":
{"summarizer_771ef71...` ile başlıyor — `$type` İLK özellik DEĞİL,
`stepNumber`. `$type` işaretçisi payload'ın DERİNİNDE gerçekten var
(`LIKE '%$type%'` → `true`). Spec'in düzeltmesiyle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-043 — Kontrol noktalarının `run_id` ve `session_id` ile filtrelenebilirliği

**Gerçek sonuç**
İkinci bir çalıştırma farklı `sessionId` (`ikinci-oturum`) ile yapıldı. SQL:
iki ayrı `session_id`, her biri kendi `run_id`'sine bağlı 3'er kontrol
noktası — sızıntı yok. `GET .../checkpoints` yalnız ilgili run'ın 3
noktasını döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-044 — Aynı workflow iki kez çalıştırılınca executor kimlikleri SABİT kalır (K-127 kanıtı)

**Gerçek sonuç**
🚨 K-228 (kusur değil): case'in grep deseni Türkçe `ozetleyici_` bekliyordu,
gerçek kimlik İngilizce `summarizer_<hash>`. İki ayrı çalıştırmada (farklı
`sessionId`) birebir aynı kimlik: `summarizer_771ef71a7f6739f38d3e79585
f2f494c` — tam beklenen, `WorkflowAgentIdentity.Compute` çalıştırmadan
bağımsız.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-050 — `inceleme-zinciri` tanımla ve çalıştır, ağaç 3 satır

**Gerçek sonuç**
`FIX-WF-01` (yeniden `PUT` edildi, MT-WF-006'da silinmişti). `/tree`: 3
satır — Workflow satırının `agentName` alanı `"inceleme-zinciri"` (workflow
adı), 2 Agent alt satırı. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-051 — Varsayılan `resume`: `checkpointId` verilmezse SON kontrol noktası kullanılır

**Gerçek sonuç**
TAMAMLANMIŞ bir çalıştırma boş gövdeyle sürdürüldü: `event: run` **YENİ**
bir `runId` bildirdi, akış `WorkflowOutput` üretti. Tam beklenen —
`resume` tamamlanmış bir çalıştırmayı bile kabul ediyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-052 — Belirli bir `checkpointId` ile erken bir noktadan `resume`

**Gerçek sonuç**
İLK kontrol noktasından (`parentCheckpointId: null`) sürdürüldü: akış
başarıyla başladı, tamamlandı (`event: done`), 56 `MessageDelta` + 13
`ExecutorInvoked` olayı gözlendi — `summarizer`'nin yeniden çalıştığı
doğrulandı. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-053 — Tanım güncellendikten sonra ESKİ bir kontrol noktasından `resume` → uyumsuzluk hatası

**Gerçek sonuç**
Spec'in kendi kod-okuma düzeltmesi yeniden doğrulandı: `agentNames`'e
`router` eklenip `PUT` edildikten sonra eski `runId`/`checkpointId` ile
`resume` denendi. `event: run` → `event: event (RunFailed, "Workflow
'inceleme-zinciri' cannot be resumed from this checkpoint: the graph's
structure differs from when the checkpoint was written...")` →
`event: done`. Birebir beklenen (K-228: İngilizce).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-060 — Çalıştırma `AwaitingInput` ile kapanır, `Boolean` form kartı

**Gerçek sonuç**
🚨 İki küçük doküman notu (kusur değil): (1) K-228 — `portId:
"publish-approval"` (Türkçe "yayin-onayi" değil), `prompt` "Should this
summary be published?" ile başlıyor (Türkçe değil). (2) Sıralama iddiası
hafif yanıltıcı: `WorkflowRequest`'ten SONRA `RunAwaitingInput`'tan ÖNCE
iki ara altyapı olayı var (`ExecutorCompleted`, `SuperStepCompleted`) —
"hemen öncesinde" tam bitişik değil ama akışta yalnız BİR
`WorkflowRequest` var ve son olay `RunAwaitingInput`, ardından `done` —
asıl iddia doğru. `form:"Boolean"`, `requestType:"System.String"`,
`responseType:"System.Boolean"` — tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-061 — `GET /requests` yalnız akış kapandıktan sonra çağrılır (arayüz kuralı)

**Gerçek sonuç**
Arayüzden çalıştırıldı. "Streaming sırasında panel gizli" anı (adım 2),
gerçek API çağrısının çok hızlı tamamlanması yüzünden **güvenilir
yakalanamadı** (dürüstçe not düşülüyor — bir test aracı sınırlaması, ürün
davranışı hakkında değil). Adım 3 (akış bitince panel görünür, "Yes"/"No"
düğmeleri) **doğrulandı** — panel "Waiting on you" başlığıyla, doğru
prompt metniyle ve iki düğmeyle göründü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-062 — `respond` onayla → yeni `runId`, çıktı BİREBİR sabit metin

**Gerçek sonuç**
Arayüzden "Yes" tıklandı (aynı zamanda MT-WF-061'in devamı). Yeni bir
`runId` (`01a0afb2-da7f-...`) üretildi, `WorkflowOutput.text` **birebir**
`"Summary published."` (K-228: "Ozet yayinlandi."nin İngilizcesi, sabit
kod metni). `GET /api/runs/{yeni-runId}` → `status: "Completed"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-063 — `respond` reddet → çıktı BİREBİR sabit metin

**Gerçek sonuç**
Yeni bir çalıştırma + `approved:false` ile `respond`: `WorkflowOutput.text`
**birebir** `"Publication canceled; summary kept in the archive."` (K-228:
"Yayin iptal edildi; ozet arsivde birakildi."nin İngilizcesi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-064 — Yanlış `requestId` ile `respond` → SSE `error`

**Gerçek sonuç**
🚨 Bu case MT-WF-020/053'ten farklı olarak GERÇEKTEN `event: error`
üretti (RunFailed domain event değil): `{"type":"TraconException",
"message":"There is no pending request with id 'uydurma-istek-kimligi'
on run '...'. Refresh the request list with GET /api/workflows/runs/
{runId}/requests."}` — K-228 (İngilizce), anlamca tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-065 — `AwaitingInput` OLMAYAN bir çalıştırmaya `respond` → SSE `error`

**Gerçek sonuç**
Tamamlanmış bir çalıştırmaya (MT-WF-040'ın run'ı) `respond` denendi:
`event: error`, `message: "Run '...' is not awaiting human input (status:
Completed). Only a run in 'AwaitingInput' status can be responded to."`
(K-228). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-066 — Başka kiracının `AwaitingInput` çalıştırmasına `/requests` → `404`

**Gerçek sonuç**
🚨🚨 **Önemli yöntem notu (ürün kusuru DEĞİL, dosya 02/03'ün zaten belgelediği
tuzağın bu ailede tekrarı):** İlk denemede ap-s2'nin ANA uygulaması (port
5082) çok kiracılık bayraklarıyla (`Tracon:Tenancy:Enabled` VE
`AllowHeaderResolution`) başlatılmamıştı — `X-Tracon-Tenant` başlığı
**sessizce yok sayıldı** ve `kiraci-beta` başlığıyla yapılan istek
`kiraci-alfa`'nın isteğini **`200` ile sızdırdı** (kiracı izolasyonu YOK
gibi göründü). Bu bir ürün kusuru DEĞİL — yalnızca test ortamının bu iki
bayrağı hiç açmamış olmasıydı. Ana uygulama iki bayrakla yeniden
başlatıldı (PostgreSQL `mt_s2` şeması, mevcut veriler korunarak — restart
sonrası tüm önceki workflow tanımları doğrulandı: hâlâ erişilebilir).
Doğru kurulumla tekrar denendi: `kiraci-beta` başlığıyla `kiraci-alfa`'nın
run'ına erişim → **`404`**, `title: "Run not found"` (K-228) — "yetkisiz"
bile demiyor, varlığı sızdırmıyor. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-070 — `plan-onayli` tanımla ve çalıştır → `AwaitingInput`, form `PlanReview`

**Gerçek sonuç**
`PUT` → `200`, `requirePlanApproval: true`. `run` → `RunAwaitingInput` olayı
göründü. `GET .../requests`: `form:"PlanReview"`,
`requestType:"Microsoft.Agents.AI.Workflows.MagenticPlanReviewRequest"`,
`prompt` planın kendisiyle dolu (boş değil). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-071 — Planı onayla → yönetici bitirir, katılımcı agent çalışır

**Gerçek sonuç**
Yeni bir `runId` açıldı, `ExecutorInvoked(translator_8217e31...)` ve
`WorkflowOutput` (boş olmayan, gerçek çeviri metni: "Tracon is a family
of NuGet packages built on the Microsoft Agent Framework.") göründü. Tam
beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-072 — Metinsiz ret (`text` boş) → hata

**Gerçek sonuç**
🚨 **İlk kez ölçüldü (case'in kendi notu, kural 1.1 istisnası, kusur
değil):** Case "SSE `event: error`" bekliyordu (spekülasyon, önceden
ölçülmemişti). Gerçek mekanizma MT-WF-020/053 ile aynı desen: **RunFailed
domain event**, ham `event: error` DEĞİL. Mesaj: "The plan was rejected
but no revision text was given. The 'text' field is required so the
manager agent knows what to rebuild the plan against." — net ve
bilgilendirici. Davranışsal iddia (düzeltme metni olmadan reddin hata
vermesi) doğrulandı, yalnız mekanizma netleştirildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-073 — Düzeltme metniyle ret → yönetici YENİDEN planlar, ikinci bir `AwaitingInput`

**Gerçek sonuç**
Düzeltme metniyle ret sonrası: yeni `runId`, akış yine `RunAwaitingInput`
ile kapandı. `GET .../requests`: YENİ bir `requestId` (eskisinden farklı),
yeni `prompt` (yöneticinin gözden geçirilmiş planı, "Root cause: I
overcomplicated the task..." ile başlıyor). Tam beklenen — yönetici ikinci
kez çalıştı (maliyet notu doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
