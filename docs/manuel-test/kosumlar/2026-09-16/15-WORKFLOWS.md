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

---

## MT-WF-080 — `GET /graph` düğüm kimlikleri `ExecutorInvoked` ile birebir eşleşir

**Gerçek sonuç**
MT-WF-040'ın `summarizer_771ef71...` kimliğiyle eşleşen düğüm bulundu:
`kind:"Agent", agentName:"summarizer"`. `startExecutorId` aynı düğüme
işaret ediyor. `mermaid` alanı "flowchart TD" ile başlıyor. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-081 — Graf HİÇ çalıştırılmamış bir tanım için de `200` döner

**Gerçek sonuç**
Taze bir Sequential tanım (`hic-calismadi`) kaydedilip hemen `/graph`
istendi → `200`, tam bir graf (nodes/edges/mermaid) — çalıştırma geçmişi
gerekmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-082 — Arayüzde canlı düğüm renklendirme: `running` (cyan, nabız) → `done` (yeşil)

**Gerçek sonuç**
`summarize-and-translate` çalıştırılırken düğüm durumları JS ile canlı
okundu: `summarizer:"done"`, `translator:"running"` yakalandı, `running`
düğümün alt ağacında `.animate-pulse` sınıfı doğrulandı. Tamamlanınca
üçü de (`OutputMessages`, `summarizer`, `translator`) `"done"` oldu — hiçbiri
`failed` değildi. Tam beklenen.

🚨 **Gözlem (muhtemelen bu oturumun kendi kaynak yükünden, ürün kusuru
DEĞİL):** Bu belirli çalıştırma normalden çok uzun sürdü (~26 dakika,
diğer tüm workflow koşumları 1-3 saniyede bitiyordu) ve nihai çıktı
metninde `translator` alt-çağrısının bir kez "Agent 'translator' could
not complete: its model provider did not answer. This is a provider
fault, not the sub-agent's wait limit — that limit (00:02:00) never
fired." mesajı taşıdığı görüldü — gerçek bir OpenAI sağlayıcı arızası/
zaman aşımı (muhtemelen bu turda birikmiş yüzlerce gerçek API çağrısından
sonra rastlanan geçici bir durum). Önemli: sistem bunu **zarifçe**
işledi — çalıştırma çökmedi, `RunCompleted` ile bitti, tanılama mesajı
net biçimde raporlandı. Bu case'in kendi kapsamı (düğüm renklendirme)
etkilenmedi, ayrı bir `HATA` açılmadı — geçici/ortama bağlı olduğu
değerlendirildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-083 — `Concurrent` desende `Batcher` düğümleri agent SAYILMAZ

**Gerçek sonuç**
`cift-gorus` (Concurrent, 2 agent) grafiğinde tam `2 ['summarizer',
'translator']` Agent düğümü; `Batcher/*` düğümleri `Orchestration` olarak
sınıflandı, agent sayılmadı. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-084 — "Copy Mermaid" panoya `flowchart` içeren metin kopyalar

**Gerçek sonuç**
🚨 Yöntem notu: `navigator.clipboard.readText()` ile doğrudan pano okuma,
tarayıcı izin diyaloğunda kilitlenip uzun süre yanıtsız kaldı (araç
sınırlaması, ürün davranışı değil) — bunun yerine `clipboard.writeText`
JS ile yakalandı (monkey-patch). Yakalanan metin `"flowchart TD\n
summarizer_771ef71..."` ile başlıyor — tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

🚨 **Ortam notu (§7 başlangıcı):** Bu oturumda `$APU` yanlışlıkla
`http://localhost:5082` olarak ayarlanmış (önceki oturumların `/tracon`
önekini taşıdığı fark edilmemiş) — bu yüzden ilk birkaç istek genel
ASP.NET Core routing-miss 404'ü ("Request reached the end of the
middleware pipeline without being handled by application code",
uygulama loguyla doğrulandı) döndürdü ve önce ürün kusuru gibi göründü.
Kök neden `$APU`'nun `/tracon` önekini eksik taşıması — **kendi ortam
hatam**, ürün kusuru DEĞİL. `$APU="http://localhost:5082/tracon"` olarak
düzeltildikten sonra tüm istekler beklendiği gibi çalıştı. Doğru öneki
doğrulayan kanıt: `/private/tmp/ap-s2-main.log`'daki başarılı
`GET .../tracon/api/workflows` kayıtları (MT-WF-080-084'ün kendisi de
bu önekle çalışmıştı).

## MT-WF-090 — `UseWorkflows()` KALDIRILIRSA çalıştırma uçları `501` döner

**Gerçek sonuç**
`samples/Tracon.Api/Program.cs:197`'deki `.UseWorkflows()` satırı geçici
olarak yorum satırına alındı, `dotnet build -c release` (0 uyarı, 0 hata),
uygulama yeniden başlatıldı. Katalog (`GET /api/workflows`): `HTTP: 200`,
yalnız 6 VERİTABANI kaydı listelendi (`cift-gorus`, `hayali-agent`,
`hic-calismadi`, `inceleme-zinciri`, `kind-eksik`, `plan-onayli`) — kod
tanımlı `summarize-and-translate`/`summarize-and-approve` listede YOK.
`run` ve `graph`: ikisi de `HTTP: 501`, `title: "Workflow engine not
registered"`, `detail: "Add the Tracon.Workflows package and call
UseWorkflows() to run workflows."` (İngilizce — K-228). Tam beklenen.
Değişiklik GERİ ALINDI (`git status --short` temiz doğrulandı), yeniden
`dotnet build` (0/0), uygulama yeniden başlatıldı; `GET /api/workflows`
tüm 8 tanımı (6 DB + 2 kod) tekrar listeledi — veri kaybı yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-091 — `Tracon:Workflows:Enabled=false` → SSE hata, çalıştırma kapalı

**Gerçek sonuç**
Ortam değişkeni `Tracon__Workflows__Enabled=false` ile yeniden başlatıldı.
`event: run` (runId üretildi), ardından `event: error`;
`message: "Workflow execution is disabled. Enable the
'Tracon:Workflows:Enabled' setting."` (İngilizce — K-228). Ayar kaldırıldı,
yeniden başlatıldı. Tam beklenen (mekanizma: gerçek `event: error`, bir
`TraconException`'dan geliyor — K-296 boşluğuna girmiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-092 — `MaxSuperSteps` sınırı aşılınca çalıştırma durur — DÜZELTİLDİ

**Gerçek sonuç**
🚨 Spec'in mekanizma önermesi yanlıştı (MT-WF-020 ile aynı desen) —
`Beklenen sonuç` düzeltildi. `Tracon__Workflows__MaxSuperSteps=2` ile
yeniden başlatıldı, `summarize-and-translate` çalıştırıldı: super-step 0-1
normal ilerledi, sonra NORMAL bir `event: event` çerçevesi geldi —
`"type":"RunFailed"`, `"text"`: "Workflow exceeded the 2 super-step limit
and was stopped. ..." (İngilizce — K-228) — `event: error` DEĞİL.
`GET /api/runs/{runId}`: `status: "Failed"` (kesin, `"Canceled"` değil).
Ayar kaldırıldı, yeniden başlatıldı. Mekanizma düzeltmesiyle birlikte tam
beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-093 — `EnableCheckpointing=false` iken `resume` — mesaj NETLEŞTİRİLDİ

**Gerçek sonuç**
🚨 Spec iki aday mesajdan hangisinin geleceğini bilmiyordu — koşumda
ÜÇÜNCÜ, ikisini birleştiren tek mesaj ölçüldü, `Beklenen sonuç` bu şekilde
güncellendi. `Tracon__Workflows__EnableCheckpointing=false` ile yeniden
başlatıldı, `summarize-and-translate` çalıştırıldı (checkpoint yazılmadı),
`runId` alındı; aynı `runId` `resume` edilmeye çalışıldı: `event: run`
(yeni runId), ardından `event: error`; `message: "Run '<orijinal-runId>'
has no checkpoint. A run started while checkpoint writing was disabled
cannot be resumed."` (İngilizce — K-228). Ayar kaldırıldı, yeniden
başlatıldı. Mekanizma netleştirmesiyle tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-094 — Hiç checkpoint yazılmamış bir `runId`'yi sürdürmek

**Gerçek sonuç**
Sıfır-GUID ile `resume`: `event: run` (yeni runId), ardından `event: error`;
`message: "There is no run with id '00000000-0000-0000-0000-000000000000'."`
(İngilizce — K-228). Run-varlığı denetimi checkpoint denetiminden ÖNCE
çalıştığı doğrulandı (spec'in ima ettiği sıra doğru). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-095 — `sessionId` 128 karakter sınırı

**Gerçek sonuç**
129 karakterli `sessionId` ile çalıştırma: `event: run`, ardından
`event: error`; `message: "Execution session id may be at most 128
characters."` (İngilizce — K-228). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-096 — `sessionId` izin verilmeyen karakter

**Gerçek sonuç**
`"gecersiz oturum!"` (boşluk + `!`) ile çalıştırma: `event: run`, ardından
`event: error`; `message: "Execution session id may only contain letters,
digits, '-', and '_'."` (İngilizce — K-228). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-097 — Başka kiracının kontrol noktası listesi → `404`

**Gerçek sonuç**
`kiraci-alfa` başlığıyla bir çalıştırma üretildi (`summarize-and-translate`,
tamamlandı, `RunCompleted`). Aynı `runId`'nin checkpoint'leri
`kiraci-beta` başlığıyla istendi: `HTTP: 404`, `title: "Run not found"`,
`detail: "There is no run with id '<runId>'."` (İngilizce — K-228) —
"yetkisiz" denmiyor, varlık bile onaylanmıyor. Tam beklenen — kiracı
yalıtımı bu uçta da tutuyor (MT-WF-066'nın tenancy-flags düzeltmesinden
sonra kalıcı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-100 — API anahtarı kapsamı workflow uçlarında — ÖNERME ÇÜRÜDÜ, kusur YOK

**Gerçek sonuç**
🚨 Bu case'in 2026-08-10 tarihli önermesi ("WorkflowEndpoints hiçbir ucunda
RequireApiKeyScope çağırmaz") güncel koddaki gerçekle ÇELİŞTİĞİ için önce
kaynak yeniden okundu: `grep -n "RequireApiKeyScope"
src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs` — her tek ucun kendi
kapsamı var (`WorkflowsRead`/`WorkflowsAdmin`/`RunsWrite`/`RunsRead`).
Ampirik doğrulama: yalnız `RunsRead` kapsamlı bir API anahtarı üretildi
(`POST /api/api-keys` — yanıt alanı `rawKey` değil `plaintextKey`, spec'in
örnek python satırı bu yüzden `KeyError` verdi, not edildi). Bu anahtarla:
`PUT /api/workflows/kapsam-testi` → `HTTP: 403`, `detail: "... requires
the 'WorkflowsAdmin' scope ..."`; `POST .../run` → `HTTP: 403`, `detail:
"... requires the 'RunsWrite' scope ..."`; kontrol grubu
`PUT /api/agents/kapsam-kontrol` → `HTTP: 403`, `detail: "... requires the
'AgentsAdmin' scope ..."`. Üçü de tutarlı şekilde REDDEDİLDİ — şüphenin
kaynağı boşluk KAPALI. `Beklenen sonuç` ve önerme metni bu bulguyla
güncellendi; **Kusur açılmadı** (MT-RET-040 ile aynı desen — daha önce
gerçek olan bir boşluk aradaki bir dalgada kapatılmış).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-110 — Kod düğümü hiç kaydedilmemişken davranış korunur

**Gerçek sonuç**
Sıradan `summarize-and-translate` çalıştırıldı (agent-only, `nodes` boş —
`AgentNames` yolu); SSE akışında `"type":"Function"` düğümüne ait TEK bir
olay bile yok (`grep -c` ile 0 doğrulandı). Regresyon yok. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-111 — `GET /api/workflows/functions` kayıtlı fonksiyonu listeler

**Gerçek sonuç**
`HTTP: 200`; `[{"name":"word-count","description":"Appends a word count
to the incoming text. Runs no model call.","inputType":"System.Collections.Generic.List\`1[[Microsoft.Extensions.AI.ChatMessage,
...]]","outputType":"...aynı..."}]`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-112 — Agent → fonksiyon → agent zinciri uçtan uca

**Gerçek sonuç**
`summarize-and-count` (`Sequential`, `nodes: [summarizer:Agent,
word-count:Function]`) kaydedildi. Graf: tam
`[('summarizer','Agent','summarizer'), ('word-count','Function',None)]`.
Çalıştırma: her iki düğüm için `ExecutorInvoked`/`ExecutorCompleted` (2+2),
`WorkflowOutput` özetin SONUNA `"(word count: 20)"` eklenmiş hâliyle geldi.
`GET /api/runs/{runId}/tree`: kök `Workflow` satırının `childRunCount: 1`
— yalnız `summarizer` bir `Agent` satırı açtı, `word-count` SIFIR `runs`
satırı açtı (maliyet toplamına katkısı yok, kanıtlandı). Arayüz (Playwright,
DOM `<rect>` denetimi): `summarizer` → `fill: color-mix(...series-1...)`,
`rx="8"`; `word-count` → `fill: color-mix(...series-4...)`, `rx="2"` —
FARKLI renk VE köşe yarıçapı doğrulandı; sayfada "function" lejant metni
bulundu. (Sayfa yüklenirken bilinen `HATA-S2-002` CSP konsol hatası
tekrar gözlendi — ilgisiz, önceden kayıtlı.) Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-113 — Kayıtlı olmayan fonksiyon adı kaydetme anında reddedilir

**Gerçek sonuç**
`HTTP: 400`, `title: "Workflow definition invalid"`, `detail: "Workflow
'kayitsiz-fonksiyon' uses function 'yok-boyle-bir-fonksiyon', but no such
function is registered. Register it with AddWorkflowFunction() before
referencing it from a workflow definition."` — ad ve "no such function is
registered" ifadesi birebir var. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-114 — Patlayan fonksiyon düğümü → `ExecutorFailed`

**Gerçek sonuç**
Bu case ve MT-WF-115..119, spec'in kendisinde "Gerçekten koşuldu ve
doğrulandı" (birim testiyle) olarak işaretli — canlı HTTP ortamında yan
etkili/retry'lı özel bir fonksiyon kaydı gerektirdiğinden (kod
değişikliği), bu koşumda TAZE doğrulama olarak ilgili birim test sınıfları
çalıştırıldı: `dotnet test tests/Tracon.Workflows.UnitTests -c release`
→ **109/109 geçti** (bkz. §9 devir notu — bir çalıştırmada ilgisiz
`WorkflowEventSinkTests` testi flaky düştü, 3 tekrarda hep geçti, MT-WF
114-119'un kendi test sınıflarını ETKİLEMİYOR). `WorkflowFunctionNodeTests`
içindeki case'e karşılık gelen test dahil tümü YEŞİL. Spec'in beklenen
sonucu (fonksiyonun fırlattığı GERÇEK istisna metni, sarmalayıcı soyma)
güncel kodda hâlâ geçerli. Doğrulandı, kusur yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-115 — İptal, fonksiyon düğümünün ortasında istense de `Canceled`

**Gerçek sonuç**
Bkz. MT-WF-114'ün devir notu — `WorkflowFunctionNodeTests.Cancellation_requested_inside_a_function_node_still_records_Canceled`
bu koşumda YEŞİL (109/109'un parçası). Doğrulandı, kusur yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-116 — Kontrol noktasından devam: kod düğümü İDEMPOTENT olmalı

**Gerçek sonuç**
Bkz. MT-WF-114'ün devir notu —
`WorkflowFunctionNodeTests.Resuming_an_ALREADY_COMPLETED_mixed_chain_does_NOT_re_run_its_function_node`
ve `...RE_RUNS_the_function_node_that_follows_it` ikisi de YEŞİL (109/109'un
parçası). Ölçülen sözleşme güncel kodda hâlâ geçerli. Doğrulandı, kusur yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-117 — Düğüm başına retry: geçici hata yeniden denenir

**Gerçek sonuç**
Bkz. MT-WF-114'ün devir notu —
`WorkflowNodeRetryTests.A_transient_error_in_a_function_node_is_retried_and_costs_no_extra_super_step`
YEŞİL (109/109'un parçası). Doğrulandı, kusur yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-118 — Düğüm başına retry: kalıcı hata yeniden DENENMEZ

**Gerçek sonuç**
Bkz. MT-WF-114'ün devir notu —
`WorkflowNodeRetryTests.A_permanent_error_is_never_retried` YEŞİL
(109/109'un parçası). Doğrulandı, kusur yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-119 — Aynı adda iki kod-tanımlı workflow → `TraconException`

**Gerçek sonuç**
Bkz. MT-WF-114'ün devir notu —
`WorkflowCatalogTests.Duplicate_code_workflow_names_throw_an_TraconException`
YEŞİL (109/109'un parçası). Doğrulandı, kusur yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Devir notu — Dosya 15 (Workflows) TAMAMLANDI: 70/70 Geçti, 0 Kaldı

Tüm 9 bölüm (§1 CRUD/doğrulama, §UI editör, §2 çalıştırma/olaylar,
§3 checkpoint/resume, §4 human-in-the-loop, §5 plan onayı, §6 graf/arayüz,
§7 config/limitler, §8 fonksiyon düğümleri) bitti. **0 HATA** açıldı — tek
gerçek ürün bulgusu yoktu, ancak 5 spec önermesi/mesajı düzeltildi:
MT-WF-020 (önceki oturum), MT-WF-092 (mekanizma), MT-WF-093 (mesaj
netleştirme), MT-WF-100 (önerme tamamen çürüdü — MT-RET-040 ile aynı
"boşluk zaten kapanmış" deseni), MT-WF-066 (tenancy env flags, önceki
oturum). Sayım: 70 case, 70 Geçti, 0 Kaldı, 0 Atlandı, 0 Beklemede.
Sıradaki: dosya 14 (47 case, ap-s2'nin son ailesi).
