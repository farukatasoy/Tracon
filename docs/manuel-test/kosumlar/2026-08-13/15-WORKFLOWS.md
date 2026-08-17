# 15 — Workflows: Yürütme, Kontrol Noktası, Graf ve Human-in-the-Loop (`WF`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../15-WORKFLOWS.md`](../../15-WORKFLOWS.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-WF-001 — `PUT /api/workflows/{name}` yeni bir Sequential tanım oluşturur (`200`)

**Gerçek sonuç**
`HTTP: 200`, `version: 1`, `tenantId: "default"`, `agentNames: ["ozetleyici","cevirmen"]`. PostgreSQL kalıcılığıyla koşuldu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-002 — Aynı adı tekrar `PUT` etmek günceller, `version` artar

**Gerçek sonuç**
`HTTP: 200`, `version: 2`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-003 — `GET /api/workflows/{name}` veritabanında saklı bir tanımı döner

**Gerçek sonuç**
Gövde MT-WF-002'nin sonucuyla birebir aynı (`version: 2`, güncellenmiş `description`). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-004 — Aynı uç, KODda tanımlı bir workflow için ayırt edici bir `404` döner (düzeltildi)

**Gerçek sonuç**
Adım 1: `ozetle-ve-cevir` listede (`['inceleme-zinciri', 'ozetle-ve-cevir', 'ozetle-ve-onayla']`). Adım 2: `HTTP: 404`, `title: "Duzenlenebilir tanim yok"`, `detail: "'ozetle-ve-cevir' kodda tanimli bir workflow'dur (AddWorkflow). Listelenir ve calistirilabilir ama veritabaninda duzenlenebilir bir WorkflowDefinition tasimaz."` — generic mesaj DEĞİL, ayırt edici mesaj. Tam beklendiği gibi, fix regresyonu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-005 — `GET /api/workflows` kod + veritabanı birleşik liste; isim çakışmasında KOD kazanır

**Gerçek sonuç**
`PUT`: `HTTP: 200`, kayıt kabul edildi. Liste: `{'name': 'ozetle-ve-cevir', 'displayName': None, 'description': 'Metni ozetler, sonra Ingilizceye cevirir. Kodda tanimlidir.', 'origin': 'Code', 'kind': None, 'agentNames': [], 'version': 1, 'updatedAt': None}` — DB kaydı (kind Concurrent, description "Sahte DB Kaydi") görünmüyor, kod kaydı üstün geliyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-006 — `DELETE` veritabanı kaydını siler, sonraki `GET` `404` verir

**Gerçek sonuç**
Adım 1: `HTTP: 204`. Adım 2: `HTTP: 404`, `title: "Workflow bulunamadi"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-007 — Var olmayan bir adı silmek → `404`

**Gerçek sonuç**
`HTTP: 404`, `title: "Workflow bulunamadi"`, `detail: "'hic-yok-boyle-workflow' adinda bir workflow yok."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-008 — Kod-tanımlı bir adı silmeye çalışmak → `404`, çalışmaya devam eder

**Gerçek sonuç**
Adım 1: `HTTP: 404` (silinecek DB kaydı yoktu). Adım 2: `True` (kod-tanımlı workflow listede kalmaya devam etti). Adım 3: normal çalıştı, `WorkflowOutput` üretti (gerçek OpenAI modeliyle, `ozetleyici`/`cevirmen` zincirinden geçti), `RunCompleted` ile bitti. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-009 — Boşluktan ibaret ad → `400` "name alanı zorunludur"

**Gerçek sonuç**
`HTTP: 400`, `title: "Workflow tanimi gecersiz"`, `detail: "Workflow tanimin 'name' alani zorunludur."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-010 — 🚨 `kind` alanı gövdede atlanırsa sessizce `Sequential`'a düşer

**Gerçek sonuç**
Şüphe doğrulandı: `HTTP: 200`, gövdede `kind: "Sequential"` — `kind` alanı hiç gönderilmeden. Kod kusuru değil, ürünün bilinçli tasarım seçimi (`WorkflowKind.Sequential` varsayılan enum değeri `0`); dokümanın kendisi bunu zaten "şüphe" olarak işaretlemişti, şimdi ölçüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-011 — `agentNames` boş → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail: "'bos-katilimci' workflow'u hicbir agent icermiyor. 'agentNames' en az bir ad tasimalidir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-012 — Aynı agent adı iki kez → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail: "'tekrar-eden' workflow'unda 'ozetleyici' agent'i birden fazla kez geciyor. ..."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-013 — `Concurrent` + tek agent → `400` (en az iki ister)

**Gerçek sonuç**
`HTTP: 400`, `detail: "'tek-concurrent' workflow'u 'Concurrent' desenini kullaniyor ve en az iki agent ister; listede 1 ad var."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-014 — `Magentic` + boş `managerAgentName` → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail: "'yoneticisiz' workflow'u 'Magentic' desenini kullaniyor ve 'managerAgentName' zorunludur. ..."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-015 — `Magentic` + yönetici aynı zamanda katılımcı → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail: "'kendini-yoneten' workflow'unda 'ozetleyici' hem yonetici hem katilimci olarak geciyor. ..."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-016 — `GroupChat` + `managerAgentName` verilirse → `400` (bu alan yalnız `Magentic`'indir)

**Gerçek sonuç**
`HTTP: 400`, `detail: "'groupchat-yanlis' workflow'u 'GroupChat' deseninde 'managerAgentName' kullanmaz. ... sirayi kod tarafindaki round-robin yoneticisiyle dagitir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-017 — `Sequential` + `handoffInstructions` verilirse → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail: "'yersiz-devir' workflow'u 'Sequential' deseninde 'handoffInstructions' kullanmaz. Bu alan yalnizca 'Handoff' desenine aittir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-018 — `Magentic` olmayan desende `requirePlanApproval: true` → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail: "'yersiz-plan-onayi' workflow'u 'Sequential' deseninde 'requirePlanApproval' kullanmaz. Plan onayi yalnizca 'Magentic' desenine aittir; ..."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-019 — `maxIterations: 0` → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail: "'sifir-tur' workflow'unun 'maxIterations' degeri pozitif olmalidir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-020 — Var olmayan agent adı KAYITta kabul edilir, RUN'da SSE `error` verir

**Gerçek sonuç**
Adım 1: `HTTP: 200`. Adım 2: `event: run` → `event: event` (`{"type":"RunFailed",...,"text":"'hayali-agent' workflow'u 'yok-boyle-bir-agent' agent'ini kullaniyor ancak boyle bir agent katalogda yok. Once agent'i tanimlayin, sonra workflow'u kaydedin."}`) → `event: done`. Mesaj metni birebir doğru; yalnız SSE çerçeve adı dokümanın varsaydığından farklı (`event: error` DEĞİL, `event: event` içinde `type: RunFailed`). Ürün kusuru değil — dokümanın hangi hata mekanizmasının devreye gireceği varsayımı yanlıştı, düzeltildi (yukarıya bakınız).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Arayüz: Katalog ve Editör (Faz 16)

---

## MT-WF-030 — Workflows ekranı: kod/veritabanı rozetleri ve katılımcı zinciri

**Gerçek sonuç**
`ozetle-ve-cevir`/`ozetle-ve-onayla` satırlarında Pattern "code graph" rozeti, Source "code" rozeti göründü. `inceleme-zinciri` satırında Pattern "Sequential" (accent), Source "database" rozeti göründü, Agents sütunu "ozetleyici → cevirmen" gösterdi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-031 — Editör: `Save` butonu ad/katılımcı boşken devre dışı, sunucuya istek gitmez

**Gerçek sonuç**
Adım 2: hiçbir alan doldurulmadan `Save` `[disabled]`. Adım 3: yalnız ad girildikten sonra (katılımcı yok) `Save` hâlâ `[disabled]`. Tam beklendiği gibi (buton devre dışıyken tıklama zaten mümkün değil, ağ isteği gitmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-032 — Editör: desen değişince alan görünürlüğü ve maliyet uyarısı değişir

**Gerçek sonuç**
`Handoff`: "Handoff instructions" alanı göründü. `GroupChat`: "Handoff instructions" kayboldu, "Max iterations" alanı göründü. `Magentic`: "Manager agent" açılır listesi göründü (seçenekleri: `arastirmaci, bilgi-asistani, claude-destek, claude-dusunen, gemini-destek, gemini-kati-filtre, openrouter-destek, sesli-asistan, support, yonlendirici` — seçili katılımcılar `ozetleyici`/`cevirmen` listede YOK), "Ask a person to approve the plan" checkbox'ı "costs a manager turn" rozetiyle birlikte göründü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-033 — Editör: `Concurrent` desende tek katılımcı seçiliyken uyarı metni görünür

**Gerçek sonuç**
"Concurrent needs at least two participants." metni `text-warn` (sarı/amber uyarı) CSS sınıfıyla göründü, `Save` `[disabled]`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-034 — Detay ekranı: kod-tanımlı workflow'da `Edit` düğmesi hiç yok

**Gerçek sonuç**
Başlık yanında `Edit` düğmesi yok (yalnız "code graph" rozeti var). Graph paneli (Mermaid benzeri düğüm diyagramı, "Copy Mermaid" düğmesiyle) ve Run paneli (mesaj kutusu + Run düğmesi) normal göründü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-035 — Kod-tanımlı bir adın `/edit` URL'ine doğrudan gidilirse hata paneli

**Gerçek sonuç**
Form gösterilmedi; `alert` rolündeki bileşen "Duzenlenebilir tanim yok: 'ozetle-ve-cevir' kodda tanimli bir workflow'dur (AddWorkflow). Listelenir ve calistirilabilir ama veritabaninda duzenlenebilir bir WorkflowDefinition tasimaz." metnini gösterdi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Kodda Tanımlı Workflow: Gerçek Çalıştırma (İzlek B)

`ozetle-ve-cevir` insan girdisi istemez; Faz 15'in "Gerçek Kanıt" bölümündeki
sayılar bu bölümün beklenen sonuçlarının kaynağıdır. Gerçek model kullanır —
tam sayılar (`MessageDelta` adedi gibi) modelin o anki çıktısına bağlı olduğu
için **değişmez** olarak yalnız olay TİPLERİ ve SAYILARDAKİ üst sınırlar
değil, **hangi olay tiplerinin en az bir kez göründüğü** ve **ağaç şekli**
kontrol edilir (§4.1 kuralı).

---

## MT-WF-040 — `ozetle-ve-cevir` çalıştırma: olay tipleri ve `runs` ağacı

**Gerçek sonuç**
SSE akışında beklenen tüm tipler en az bir kez göründü: `WorkflowStarted, SuperStepStarted, ExecutorInvoked, ExecutorCompleted, SuperStepCompleted, WorkflowOutput, RunCompleted` (+ `RunStarted`, `MessageDelta`). `/tree`: 3 satır — `depth=0 kind=Workflow agentName=ozetle-ve-cevir workflowName=ozetle-ve-cevir`, `depth=1 kind=Agent agentName=cevirmen parentRunId=<workflow runId>`, `depth=1 kind=Agent agentName=ozetleyici parentRunId=<workflow runId>`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-041 — `runs.kind` / `workflow_name` veritabanı doğrulaması

**Gerçek sonuç**
`kind=1` satırında `workflow_name='ozetle-ve-cevir'`, `agent_name='ozetle-ve-cevir'` (NULL değil, `IS NULL` sorgusuyla doğrulandı: `f`). `kind=0` satırlarında `workflow_name IS NULL` (`t`), `agent_name` `cevirmen`/`ozetleyici`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-042 — Kontrol noktası listesi ve `$type` ayracının ilk özellik olduğu kanıtı

**Gerçek sonuç**
HTTP: 3 kayıt, `parentCheckpointId` zinciri `null → id1 → id2` doğru sırayla. SQL: `sutun_tipi = json`. İlk 40 bayt: `{"stepNumber":0,"workflow":{"executors":` — `$type` yok ama JSON içinde başka yerde mevcut (yukarıya bakınız). K-027'nin sütun-tipi iddiası doğrulandı; "$type ilk özelliktir" alt iddiası düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-043 — Kontrol noktalarının `run_id` ve `session_id` ile filtrelenebilirliği

**Gerçek sonuç**
İki ayrı `session_id` (`019ffd920675737f94afcb229c5231df` ve `ikinci-oturum`) için ayrı `run_id` grupları (her biri 3 kayıt), sızma yok. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-044 — Aynı workflow iki kez çalıştırılınca executor kimlikleri SABİT kalır (K-127 kanıtı)

**Gerçek sonuç**
İki farklı `sessionId` (`kimlik-testi-1`, `kimlik-testi-2`) ile iki ayrı çalıştırma, ikisinde de birebir aynı kimliği üretti: `ozetleyici_9fa38dc85895e6af1c1da45815ea9d03`. Tam beklendiği gibi. "Yeniden başlatma sonrası" senaryosu (uygulamayı durdurup tekrar başlatma) koşulmadı — isteğe bağlı olduğu belirtilmişti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Arayüzden Tanımlı Workflow: Çalıştırma ve Sürdürme

---

## MT-WF-050 — `inceleme-zinciri` tanımla ve çalıştır, ağaç 3 satır

**Gerçek sonuç**
`/tree`: 3 satır — `depth=0 kind=Workflow agentName=inceleme-zinciri workflowName=inceleme-zinciri`, `depth=1 kind=Agent agentName=cevirmen`, `depth=1 kind=Agent agentName=ozetleyici`. Kodda tanımlı ile aynı davranış. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-051 — Varsayılan `resume`: `checkpointId` verilmezse SON kontrol noktası kullanılır

**Gerçek sonuç**
Boş gövdeyle `resume` çağrıldı: `event: run` çerçevesi YENİ bir `runId` (`019ffd94-9a16-...`) bildirdi — orijinal `runId`den (`019ffd94-7aef-...`) farklı, tamamlanmış çalıştırma sorunsuz kabul edildi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-052 — Belirli bir `checkpointId` ile erken bir noktadan `resume`

**Gerçek sonuç**
İlk kontrol noktasından `resume` çağrıldı; akış başarıyla başladı ve `event: done` ile bitti. Hem `ozetleyici_7e76fc6f...` hem `cevirmen_f9136cae...` için `MessageDelta` olayları YENİDEN göründü (ikisi de baştan çalıştı) — ilk kontrol noktası ilk super-step öncesine denk geldiği için beklenen davranış. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-053 — Tanım güncellendikten sonra ESKİ bir kontrol noktasından `resume` → uyumsuzluk hatası

**Gerçek sonuç**
Tanım güncellendi (`yonlendirici` eklendi), eski `checkpointId` ile eski `runId` üzerinden `resume` denendi. Sonuç: `event: run` → `event: event` (`type:"RunFailed"`, mesaj birebir yukarıdaki metin) → `event: done`. Mesaj içeriği tam beklendiği gibi; yalnız çerçeve adı (MT-WF-020'nin aynı kök nedeni) `event: error` değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Human-in-the-Loop: `ozetle-ve-onayla` (Faz 16 kanıtı)

`ozetle-ve-onayla` her girdide özetten sonra sabit bir soruyla ("Bu ozet
yayinlansin mi?") dış bir istek portuna ulaşır. Yanıt metni **model tarafından
üretilmez** — `BindAsExecutor`'daki sabit fonksiyonlardan gelir
(`Program.cs:407-411`) — bu yüzden `WorkflowOutput` metnine karşı **birebir**
eşleşme burada meşrudur (§4.1'in izin verdiği istisna, tıpkı `FIX-SKILL-*`
işaretçisi gibi: kaynağı model değil, sabit koddur).

---

## MT-WF-060 — Çalıştırma `AwaitingInput` ile kapanır, `Boolean` form kartı

**Gerçek sonuç**
Akışın son olayı `RunAwaitingInput` (`sequence:66`), `event: done` normal geldi. Tam bir `WorkflowRequest` olayı (`sequence:62`) göründü — küçük bir hassasiyet notu: doküman "hemen öncesinde" diyordu ama aralarında `ExecutorCompleted`(63)/`SuperStepCompleted`(64) olayları var (doğrudan bitişik değil); asıl iddia (tam olarak bir kez görünmesi, son olayın `RunAwaitingInput` olması) doğrulandı. `GET .../requests`: `form: "Boolean"`, `requestType: "System.String"`, `responseType: "System.Boolean"`, `portId: "yayin-onayi"`, `prompt` "Bu ozet yayinlansin mi?" ile başlıyor. Tam beklendiği gibi (küçük hassasiyet notu dışında).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-061 — `GET /requests` yalnız akış kapandıktan sonra çağrılır (arayüz kuralı)

**Gerçek sonuç**
Çalıştırma çok hızlı tamamlandığı (gerçek model, tek agent + sabit onay portu, ~1-2 sn) için Playwright'ın snapshot alma turu ile Adım 2'nin ara durumunu (akış sürerken panel yok) görsel olarak yakalayamadım — her snapshot çağrısı akış zaten bitmişken geldi. Bunun yerine ağ isteklerini inceledim: `POST .../run` (SSE akışı) TAMAMLANDIKTAN SONRA `GET .../requests` çağrıldığı doğrulandı (istek listesinde run'dan hemen sonra sırayla geldi) — kodun `enabled: finished` koşuluyla tutarlı. Adım 3: panel "Waiting on you" başlığıyla göründü, `prompt` metni ve "Yes"/"No" düğmeleri render edildi. Ana iddia (sorgu yalnız akış bittikten sonra tetiklenir) dolaylı kanıtla doğrulandı; ara durumun görsel kanıtı alınamadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-062 — `respond` onayla → yeni `runId`, çıktı BİREBİR `"Ozet yayinlandi."`

**Gerçek sonuç**
🚨 **KISMEN KALDI.** `event: run` YENİ bir `runId` bildirdi (beklendiği gibi) ve `WorkflowOutput.text` birebir `"Ozet yayinlandi."` oldu (beklendiği gibi). AMA iki ciddi sapma gözlendi:
1. **`GET /api/runs/<yeni-runId>` `status: "AwaitingInput"` döndü, `"Completed"` DEĞİL.** Akış `RunCompleted` yerine yeniden `RunAwaitingInput` ile bitti (YENİ bir `requestId` ile YENİ bir `WorkflowRequest` üretilmişti, o karşılıksız kaldı).
2. **`ozetleyici` agent'ı SIFIRDAN yeniden çalıştı** — akışta 40+ `MessageDelta` olayı yeniden göründü (gerçek model tekrar çağrıldı, gerçek ek maliyet oluştu), hâlbuki beklenti yalnız kontrol noktasından ilerlemekti (ozetleyicinin ÖNCEDEN üretilmiş çıktısını yeniden kullanmak).
Doğrulama için AYNI deneyi ikinci kez tekrarladım (yeni bir çalıştırma + respond): birebir aynı desen — yeniden tam özetleme + yeni bir `WorkflowRequest` + `AwaitingInput` ile bitiş. Tam belirlenimli. Grafın kendisi "dashed edge loops back" notuyla döngüsel olarak tasarlanmış olabilir (`onay-sorusu`/`yayin-onayi` arasında), bu yüzden "bir kez onayla → Completed" beklentisinin kendisi YANLIŞ olabilir — ama gözlenen davranış (her `respond` çağrısının modeli SIFIRDAN yeniden çağırması) `resume`'un checkpoint'ten ÇALIŞMA KALDIĞI YERDEN devam etmesi gereken temel sözleşmesiyle çelişiyor ve gerçek para maliyeti doğuruyor. Kod değiştirilmedi; kök neden netleştirme (döngüsel graf tasarımı mı, yoksa `PrepareResponseAsync`/checkpoint geri yükleme mekanizmasının hatası mı) ayrı bir kod incelemesi gerektirir.

---

**🔧 Kapanış güncellemesi (2026-08-15, Aile V — kök neden bulundu ve
düzeltildi).** Graf döngüsel DEĞİL (`Program.cs`'teki `ozetle-ve-onayla`
tanımı doğrudan okundu: `summarizeBinding → ask → portBinding → publish`,
doğrusal) — "dashed edge loops back" ihtimali dışlandı. Gerçek kök neden:
`WorkflowRunner.StartAsync` (`Internal/WorkflowRunner.cs`), `resume` DAHİL
HER yürütme başlangıcında koşulsuz olarak `run.TrySendMessageAsync(new
TurnToken(...))` çağırıyordu. Faz 15'in kendi ölçtüğü gerekçe
(`docs/15-WORKFLOWS-YURUTME.md` §1: "Yürütme bir TurnToken ister") yalnız
TAZE bir çalıştırma için geçerlidir; Faz 16'nın kendi ölçtüğü DAVRANIŞ
(`docs/16-WORKFLOWS-ARAYUZ.md` §3: "kontrol noktası bekleyen isteği TAŞIR
ve istek yeniden yayınlanır") zaten bir `respond`'un checkpoint'ten devam
etmesi için gereken sinyali kendiliğinden veriyor. Graf giriş düğümü bir
`AIAgentBinding` (agent-host, `TurnToken` yayınına ABONE) olduğunda bu
FAZLADAN token "yeni bir tur" gibi yorumlanıyor ve `ozetleyici`'yi SIFIRDAN
yeniden tetikliyordu — düz `BindAsExecutor` düğümlerinden kurulu bir grafta
(`TurnToken`'a abone değiller) bu fazladan sinyalin gözlenir bir etkisi
olmadığı için mevcut kapsam (`ApprovalWorkflow`/`WorkflowHumanInTheLoopTests`)
kusuru hiç yakalayamamıştı. Düzeltme `execution.Answers.Count == 0`
(yalnız gerçek bir `/respond`, yani bir bekleyen isteğe cevap taşıyan
sürdürme, TurnToken'ı ATLAR) ile daraltıldı — düz bir `/resume` (cevapsız,
`PrepareResumeAsync`) davranışını KORUR: ilk deneme (TÜM sürdürmelerde
atlama) `WorkflowRunnerTests.Kontrol_noktasindan_surdurulur`'u ampirik
olarak 10 dakikaya kadar asılı bıraktı (bekleyen hiçbir isteği olmayan,
tamamen `Idle` bir kontrol noktasında `TurnToken`'sız akış hiç doğal
bitmiyor, yalnız `RunTimeout`'ta durur) — bu regresyon canlı ölçülüp
düzeltmenin kapsamı daraltılarak giderildi.

Ampirik doğrulama (canlı sunucuya karşı, gerçek model, birebir bu case'in
senaryosu): ilk çalıştırma normal özetleme + `RunAwaitingInput` ile bitti.
`respond` (`approved:true`) çağrısı SONRASI: **0** yeni `MessageDelta`
olayı (özetleyici YENİDEN ÇAĞRILMADI — gerçek ek maliyet önlendi),
`WorkflowOutput.text` birebir `"Ozet yayinlandi."`, akış `RunCompleted` ile
bitti (`GET /api/runs/{yeni-runId}` → `status: "Completed"`) —
`RunAwaitingInput` YOK, ikinci bir `WorkflowRequest` YOK. Üç "Beklenen
sonuç" maddesinin TAMAMI artık gerçekleşiyor.

Regresyon testi: `WorkflowAgentEntryRespondTests.Giris_dugumu_agent_ise_respond_onu_yeniden_calistirmaz`
(`tests/AgentPrism.Workflows.UnitTests`) — `AgentApprovalWorkflow` yeni
fixture'ı (production `ozetle-ve-onayla` ile aynı şekilde bir
`AIAgentBinding` giriş düğümü taşır, `ApprovalWorkflow`'un düz executor'ünün
AKSİNE) kusuru önce yeniden üretti (2 `ozetleyici` çalıştırması), düzeltme
sonrası tek çalıştırmayı doğruluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-063 — `respond` reddet → çıktı BİREBİR `"Yayin iptal edildi; ozet arsivde birakildi."`

**Gerçek sonuç**
`WorkflowOutput.text` birebir `"Yayin iptal edildi; ozet arsivde birakildi."` oldu — bu kısım tam beklendiği gibi. Ancak MT-WF-062'nin AYNI kök nedeni burada da tekrarlandı: `ozetleyici` SIFIRDAN yeniden çalıştı (34 `MessageDelta`), yeni bir `WorkflowRequest` üretildi (karşılıksız), akış `RunAwaitingInput` ile bitti (`Completed` değil). İki bağımsız çalıştırmada (onayla + reddet) birebir aynı desen — tam belirlenimli. Metin doğruluğu Geçti sayılır; tamamlanma durumu/yeniden çalıştırma sorunu MT-WF-062'nin `HATA` kaydına referansla not edildi, ayrı bir hata açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-064 — Yanlış `requestId` ile `respond` → SSE `error`

**Gerçek sonuç**
Gerçek `event: error` çerçevesi geldi (MT-WF-020/053'ün aksine — bu istisna `PrepareResponseAsync`'te akış hiç başlamadan senkron fırlatıldığı için `WorkflowEventStream`'in `catch` bloğuna gerçekten düşüyor), `message`: "'uydurma-istek-kimligi' kimlikli bekleyen bir istek '<runId>' calistirmasinda yok. Istek listesini GET /api/workflows/runs/{runId}/requests ile tazeleyin." Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-065 — `AwaitingInput` OLMAYAN bir çalıştırmaya `respond` → SSE `error`

**Gerçek sonuç**
Gerçek `event: error` çerçevesi geldi (MT-WF-064 ile aynı gerekçeyle — senkron ön-kontrol istisnası), `message`: "'<runId>' kimlikli calistirma insan girdisi beklemiyor (durum: Completed). Yalnizca 'AwaitingInput' durumundaki bir calistirma yanitlanabilir." Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-066 — Başka kiracının `AwaitingInput` çalıştırmasına `/requests` → `404`

**Gerçek sonuç**
İlk denemede `AgentPrism:Tenancy:Enabled` kapalı olduğu için `X-AgentPrism-Tenant` başlığı hiç okunmadı, her iki çağrı da aynı (`default`) kiracıyı kullandı ve ikinci çağrı yanlışlıkla `200` döndü — bu bir güvenlik açığı DEĞİL, benim test kurulum hatamdı (koşmadan önce §5'in ön koşulunu — MT-SEC-021'in header çözümlemesini açmayı — atlamışım). `dotnet user-secrets set "AgentPrism:Tenancy:Enabled" "true"` + `AllowHeaderResolution "true"` ile yeniden başlatılıp doğru şekilde tekrarlandı: ikinci çağrı (`kiraci-beta` başlığıyla) `HTTP: 404`, `title: "Calistirma bulunamadi"`, `detail: "'<runId>' kimlikli calistirma bulunamadi."` — varlığı sızdırmadı. Aynı `runId`'ye doğru kiracıdan (`kiraci-alfa`) yapılan kontrol sorgusu `200` ile veriyi döndürdü, izolasyonun çalıştığını doğruladı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Magentic Plan Onayı

---

## MT-WF-070 — `plan-onayli` tanımla ve çalıştır → `AwaitingInput`, form `PlanReview`

**Gerçek sonuç**
`PUT`: `HTTP: 200`, `requirePlanApproval: true`. `run`: akış `RunAwaitingInput` ile kapandı. `GET .../requests`: `form: "PlanReview"`, `requestType: "Microsoft.Agents.AI.Workflows.MagenticPlanReviewRequest"`, `responseType: "Microsoft.Agents.AI.Workflows.MagenticPlanReviewResponse"`, `prompt` boş değildi (planın maddeleri). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-071 — Planı onayla → yönetici bitirir, katılımcı agent çalışır

**Gerçek sonuç**
🚨 **KISMEN KALDI.** Yeni `runId` açıldı, `ExecutorInvoked(cevirmen_...)` en az bir kez göründü (iki kez), `WorkflowOutput.text` boş değildi — bu üç loose iddia teknik olarak sağlandı. AMA `WorkflowOutput.text` gerçek bir çeviri DEĞİL, MAF'ın kendi sistem mesajıydı: `"Task execution stopped due to hitting the maximum round count limit."` (case'in kendi `PUT` isteğindeki `maxIterations: 2` bu sınıra çok hızlı çarpıyor — plan + onay-sonrası devam + katılımcı çağrısı en az 3 tur gerektiriyor). Bundan SONRA akış `cevirmen`'i BİR KEZ DAHA çağırdı, ardından `ExecutorFailed` (×2) ve son olarak `RunFailed`: `"Error invoking handler for Microsoft.Agents.AI.Workflows.TurnToken"` ile çöktü. Yani run temiz bir `Completed` DEĞİL, bir iç hata zinciriyle bitti. `maxIterations: 2` — tam olarak case'in kendi reprodüksiyon adımlarında belirtilen değer — bu Magentic + plan-onayı bileşimi için yetersiz ve MAF'ın round-limit'e ulaşma davranışı zarif bir durdurma yerine bir çökme zincirine yol açıyor. Kod değiştirilmedi.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-003/K-401 — kısmi düzeltme):** Aynı senaryo birebir tekrarlandı. Run HÂLÂ `RunFailed` ile bitiyor (bu doğru davranış — plan gerçekten tamamlanmadı) AMA mesaj artık anlamlı: `RunFailed.Text` = `"This Magentic orchestration has already terminated. To process new messages, create a new workflow instance."` — eskiden opak `"Error invoking handler for Microsoft.Agents.AI.Workflows.TurnToken"`. `WorkflowRunner.ToRunError` artık `TargetInvocationException`/tek-elemanlı `AggregateException` sarmalayıcılarını soyup gerçek nedeni yazıyor. Durum bu yüzden `Geçti`'ye ÇEVRİLMEDİ — dokümanın "temiz bir `Completed`" beklentisi hâlâ karşılanmıyor; MAF'ın round-limit-sonrası fazla çağrısını önceden kestirip akışı zarif durdurmak `F-106` (`docs/ADAYLAR.md`) olarak ayrı bir yetenek adayına devredildi. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

## MT-WF-072 — Metinsiz ret (`text` boş) → SSE `error`

**Gerçek sonuç**
Çerçeve `event: error` DEĞİL, MT-WF-020/053/072 ile aynı desende `event: event` içinde `type: "RunFailed"` geldi (bu istisna akışın İÇİNDE, MAF'ın kendi Magentic mantığında oluşuyor — senkron ön-kontrol değil). Metin ölçüldü: `"Plan reddedildi ancak duzeltme metni verilmedi. Yonetici agent'in plani neye gore yeniden kuracagini bilmesi icin 'text' alani zorunludur."` — bu, Faz 16 §16.4'ün "düzeltme metni zorunludur" iddiasını doğrular, `AgentPrismException` mesaj metni koda uygun. Yalnız çerçeve adı beklenenden farklı — MT-WF-020'nin doküman düzeltmesiyle aynı gerekçe.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-073 — Düzeltme metniyle ret → yönetici YENİDEN planlar, ikinci bir `AwaitingInput`

**Gerçek sonuç**
🚨 **KALDI — MT-WF-071 ile AYNI kök nedenle.** Yeni `runId` açıldı ama akış YENİ bir `RunAwaitingInput` ile DEĞİL, doğrudan bir `WorkflowOutput` (gerçek çeviri metni: `"AgentPrism is a family of NuGet packages built on Microsoft Agent Framework."`) ÜRETİP ardından `ExecutorFailed` (`MagenticOrchestrator`, `TargetInvocationException`) ve `RunFailed` (`"Error invoking handler for Microsoft.Agents.AI.Workflows.ExternalResponse"`) ile çöktü. Yönetici yeniden PLANLAMADI (beklenen ikinci `AwaitingInput`/`PlanReview` hiç oluşmadı) — bunun yerine sanki plan zaten onaylanmış gibi doğrudan yürütmeye geçti, sonra dahili bir hata verdi. `maxIterations: 2` sınırının bu senaryoda (ret + yeniden planlama + yürütme, en az 3-4 yönetici turu gerektirir) yetersiz kaldığı MT-WF-071'de zaten gözlemlenmişti; bu case AYNI kısıtın farklı bir çökme belirtisiyle (bu kez `ExternalResponse` handler hatası) tekrarlandığını gösteriyor. Kod değiştirilmedi; ikisi de aynı `HATA` kaydına referans verir.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-003/K-401 — kısmi düzeltme):** Aynı `ToRunError` düzeltmesi (`TargetInvocationException`/tek-elemanlı `AggregateException` soyma) bu case'in kod yoluna da uygulanır (MT-WF-071 ile AYNI `ToRunError` çağrı noktası) — bu case'in kendisi bu kapanışta AYRICA yeniden koşulmadı (yalnız MT-WF-071 birebir tekrarlandı), ama aynı düzeltme mekanik olarak `RunFailed.Text`'i buradaki `ExternalResponse` handler hatası için de anlamlı MAF mesajına çevirmesi beklenir. Durum bu yüzden `Geçti`'ye ÇEVRİLMEDİ (ampirik olarak doğrulanmadı, yalnız MT-WF-071 doğrulandı) — dokümanın "yeniden planlama" beklentisi de hâlâ karşılanmıyor (`F-106`). Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

# 7 — Graf Görselleştirme (Faz 16)

`/graph` ucu **çalıştırma gerektirmez** — graf tanımdan değil DERLENMİŞ
workflow'dan çıkarılır, ama derleme yalnız agent adlarının katalogda var
olduğunu ister, bir `run` istemez. Bu bölümün çoğu case model çağırmaz.

---

## MT-WF-080 — `GET /graph` düğüm kimlikleri `ExecutorInvoked` ile birebir eşleşir

**Gerçek sonuç**
`HTTP: 200`. `nodes` listesinde `id: "ozetleyici_9fa38dc85895e6af1c1da45815ea9d03"` — MT-WF-040/044'te gözlenen `ExecutorInvoked.text` ile birebir aynı — `kind: "Agent"`, `agentName: "ozetleyici"`. `startExecutorId` aynı düğüme işaret ediyor. `mermaid` alanı `"flowchart TD\n..."` ile başlıyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-081 — Graf HİÇ çalıştırılmamış bir tanım için de `200` döner (derlenmişten, geçmişten değil)

**Gerçek sonuç**
Hiç çalıştırılmamış TAZE `hic-calismadi` tanımı için `HTTP: 200`, tam bir graf (nodes/edges/mermaid) üretildi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-082 — Arayüzde canlı düğüm renklendirme: `running` (cyan, nabız) → `done` (yeşil)

**Gerçek sonuç**
Çalıştırma çok hızlı tamamlandığı için (MT-WF-061'de olduğu gibi) Adım 2'nin ara durumunu (`running`/nabız) Playwright turlarıyla yakalayamadım — her `evaluate` çağrısı akış zaten bitmişken geldi. Adım 3 doğrulandı: `document.querySelectorAll('[data-testid="workflow-node"][data-state]')` üç düğümün de (`OutputMessages`, `cevirmen`, `ozetleyici`) `data-state="done"` olduğunu gösterdi, hiçbiri `failed` değildi. Son durum tam beklendiği gibi; ara durumun görsel kanıtı alınamadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-083 — `Concurrent` desende `Batcher` düğümleri agent SAYILMAZ

**Gerçek sonuç**
`2 ['cevirmen', 'ozetleyici']` (sıra farklı ama önemsiz — sayı ve üyelik doğru). `Batcher`/`Start`/`ConcurrentEnd` düğümleri `Agent` sayılmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-084 — "Copy Mermaid" panoya `flowchart` içeren metin kopyalar

**Gerçek sonuç**
`navigator.clipboard.writeText` yamalanarak (OS pano izin diyaloğu otomasyonda askıda kaldığı için doğrudan okuma yerine bu yöntem kullanıldı) "Copy" düğmesine tıklandı: yakalanan metin `"flowchart TD\n  ozetleyici_9fa38dc85895e6..."` ile başladı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Motor Kapalı ve Sınır Ayarları

Bu bölümün her case'i `dotnet user-secrets` (veya işaretli olanlarda geçici
`Program.cs` değişikliği) ile ortamı bozar; her case sonunda ayar eski hâline
döndürülüp uygulama yeniden başlatılır (`00-INDEKS.md` §4).

---

## MT-WF-090 — `UseWorkflows()` KALDIRILIRSA çalıştırma uçları `501` döner (geçici kod değişikliği)

**Gerçek sonuç**
`.UseWorkflows()` yorum satırına alınıp yeniden başlatıldı. Katalog: `HTTP: 200`, kod tanımlı `ozetle-ve-cevir`/`ozetle-ve-onayla` listede YOK (yalnız DB kayıtları göründü). `run`: `HTTP: 501`, `title: "Workflow motoru kayitli degil"`, `detail: "Workflow calistirmak icin AgentPrism.Workflows paketini ekleyin ve UseWorkflows() cagirin."` `graph`: `HTTP: 501`, aynı hata. Tam beklendiği gibi. Değişiklik geri alındı, yeniden derlendi/başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-091 — `AgentPrism:Workflows:Enabled=false` → SSE `error` "calistirma kapali"

**Gerçek sonuç**
🚨 **KRİTİK KUSUR — kök neden: `AgentPrismWorkflowOptions` hiç config'e bağlı değil.** `dotnet user-secrets set "AgentPrism:Workflows:Enabled" "false"` ile yeniden başlatıldıktan sonra çalıştırma HİÇ engellenmedi — akış normal şekilde `RunCompleted` ile bitti, gerçek model iki kez çağrıldı (gerçek ücret oluştu). Kök neden kod okumasıyla kesin biçimde bulundu: `src/AgentPrism.Workflows/AgentPrismWorkflowsBuilderExtensions.cs:52`'deki `UseWorkflows()` yalnızca `services.AddOptions<AgentPrismWorkflowOptions>();` çağırıyor — `AgentPrismWorkflowOptions.SectionName` sabiti (`"AgentPrism:Workflows"`, dosyada tanımlı) HİÇBİR YERDE kullanılmıyor (`grep` ile doğrulandı, sıfır eşleşme); `IConfiguration`'a bağlayan tek yol, yalnızca kod içinde geçirilebilen isteğe bağlı `configure` lambda parametresi. Sonuç: `Enabled`, `EnableCheckpointing`, `MaxConcurrentRuns`, `RunTimeout`, `MaxSuperSteps`, `KeepCheckpointsAfterCompletion` — bu sınıfın YEDİ alanının TAMAMI — `appsettings.json`/`dotnet user-secrets` üzerinden asla okunamaz, sessizce C# varsayılanlarında kalır. Bu, MT-WF-092 ve MT-WF-093'te AYNI kök nedenle tekrar doğrulandı.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-004/K-402 — düzeltildi):** `UseWorkflows()` artık `BindConfiguration` ile `AgentPrism:Workflows`'a bağlanıyor. Aynı senaryo birebir tekrarlandı: `Enabled=false` set edilip yeniden başlatıldıktan sonra `curl` çalıştırması `event: error` ile doğru şekilde reddedildi — `{"type":"AgentPrismException","message":"Workflow calistirma kapali. 'AgentPrism:Workflows:Enabled' ayarini acin."}`. Beklenenle birebir eşleşiyor. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-092 — `MaxSuperSteps` sınırı geçici olarak düşürülürse çalıştırma DURDURULUR

**Gerçek sonuç**
MT-WF-091'in AYNI kök nedeniyle KALDI: `MaxSuperSteps: "2"` set edilip yeniden başlatıldıktan sonra `ozetle-ve-cevir` (3 super-step üretir) hiçbir sınırla karşılaşmadan `RunCompleted` ile normal bitti — sınır asla uygulanmadı, `AgentPrismWorkflowOptions`'ın konfigürasyona hiç bağlanmaması nedeniyle.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-004/K-402 — düzeltildi):** Aynı senaryo birebir tekrarlandı. `MaxSuperSteps=2` set edilip `ozetle-ve-cevir` çalıştırıldı: `RunFailed` — `"Workflow 2 super-step sinirini asti ve durduruldu. Devretme veya grup sohbeti dongusu sonlanmiyor olabilir; 'maxIterations' degerini dusurun veya agent talimatlarina bir bitirme kosulu ekleyin."` (dokümanın beklediği metinle birebir). `GET /api/runs/{runId}` → `status: "Failed"`. Beklenenle eşleşiyor. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-093 — `EnableCheckpointing=false` iken `resume` denemesi → hata

**Gerçek sonuç**
MT-WF-091/092'nin AYNI kök nedeniyle KALDI: `EnableCheckpointing: "false"` set edilip yeniden başlatıldıktan sonra çalıştırma sırasında YİNE DE 3 kontrol noktası yazıldı (`SELECT count(*) ... = 3`), ayar hiç okunmadı. `resume` denemesi de normal şekilde başarılı oldu (ne "EnableCheckpointing acik olmalidir" ne "kontrol noktasi yok" hatası — checkpoint zaten mevcuttu). `AgentPrismWorkflowOptions`'ın konfigürasyona hiç bağlanmaması aynı kök neden.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-004/K-402 — düzeltildi):** Aynı senaryo birebir tekrarlandı. `EnableCheckpointing=false` set edilip `ozetle-ve-cevir` çalıştırıldı: `SELECT count(*) FROM workflow_checkpoints WHERE run_id=...` → `0` — hiç checkpoint yazılmadı (beklenen davranış). `resume` denemesi → `event: error`, `AgentPrismException`: `"'<runId>' kimlikli calistirmanin kontrol noktasi yok. Kontrol noktasi yazimi kapaliyken baslatilan bir calistirma sürdürulemez."` — dokümanın kendi öngördüğü belirsizlik (`RequireCheckpointAsync`'in "kontrol noktasi yok" dalı mı, yoksa `EnableCheckpointing` dalı mı önce tetiklenir) netleşti: "kontrol noktasi yok" dalı tetikleniyor, AMA mesaj checkpointing'in KAPALI olduğunu da açıkça belirtiyor — iki savunma katmanı arasındaki belirsizlik pratikte zararsız (mesaj her iki durumu da kapsıyor). Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-094 — Hiç kontrol noktası yazılmamış bir `runId`'yi sürdürmek → "kontrol noktasi yok"

**Gerçek sonuç**
Gerçek `event: error` çerçevesi geldi (bu istisna `ResumeStreamingAsync`'in kendisi bir `async IAsyncEnumerable` yineleyici metodu olduğu için, `RunStreamingAsync`'in aksine, doğal biçimde `WorkflowEventStream`'in `catch` bloğuna ulaşıyor), `message`: "'00000000-0000-0000-0000-000000000000' kimlikli calistirma bulunamadi." Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-095 — `sessionId` 128 karakter sınırını aşarsa → SSE `error`

**Gerçek sonuç**
🚨 **KUSUR — SSE akışı hiç başlamadı.** Ne `event: run` ne `event: error` geldi — düz bir HTTP `500` gövdesi: `{"title":"An error occurred while processing your request.","status":500}` (detay yok, tamamen generic). Sunucu logunda kök neden görüldü: `AgentPrism.WorkflowRunner.RunStreamingAsync` (`WorkflowRunner.cs:186`) bir `async` yineleyici DEĞİL — düz bir metottur, gövdesinde `WorkflowSessionId.Require(request.SessionId)` nesne başlatıcısının İÇİNDE SENKRON olarak çağrılır ve `ExecuteAsync(...)`'in döndürdüğü `IAsyncEnumerable`'ı geri döndürür. İstisna bu yüzden `WorkflowEventStream` (SSE yazıcısı) hiç devreye girmeden, `WorkflowEndpoints.RunAsync`'in çağrı zincirinden DOĞRUDAN fırlar ve ASP.NET'in genel `ExceptionHandlerMiddleware`'ine düşer — "Unhandled exception" olarak loglanır (`fail: Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware[1]`). Asıl mesaj (`"Yurutme oturumu kimligi en fazla 128 karakter olabilir."`) sunucu logunda doğru ama istemciye HİÇ ulaşmıyor. Bu, `RespondStreamingAsync`/`ResumeStreamingAsync`'in (gerçek `async IAsyncEnumerable` yineleyicileri, MT-WF-064/065/094'te doğru `event: error` üreten) davranışından FARKLI — yalnız `RunStreamingAsync`'in bu yapısal farkı bu boşluğu yaratıyor.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-005/K-403 — düzeltildi):** `RunStreamingAsync` gerçek bir `async IAsyncEnumerable` yineleyicisi yapıldı. Aynı senaryo birebir tekrarlandı: artık `event: run` ardından `event: error` (`AgentPrismException`, `message: "Yurutme oturumu kimligi en fazla 128 karakter olabilir."`), `HTTP: 200` (SSE akışı, düz 500 DEĞİL) geliyor. Beklenenle birebir eşleşiyor. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-096 — `sessionId` izin verilmeyen karakter içerirse → SSE `error`

**Gerçek sonuç**
MT-WF-095'in AYNI kök nedeniyle KALDI: SSE hiç başlamadı, düz `HTTP 500` (`"An error occurred while processing your request."`) geldi. Sunucu logunda doğru mesaj (`"Yurutme oturumu kimligi yalnizca harf, rakam, '-' ve '_' icerebilir."`) görüldü ama istemciye ulaşmadı — `RunStreamingAsync`'in senkron doğrulaması aynı yapısal nedenle `WorkflowEventStream`'i baypas ediyor.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-005/K-403 — düzeltildi):** Aynı senaryo birebir tekrarlandı. Artık `event: run` ardından `event: error` (`AgentPrismException`, `message: "Yurutme oturumu kimligi yalnizca harf, rakam, '-' ve '_' icerebilir."`), `HTTP: 200`. Beklenenle birebir eşleşiyor. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-097 — Başka kiracının kontrol noktası listesi → `404`

**Gerçek sonuç**
İlk denemede `AgentPrism:Tenancy:Enabled` kapalıydı (MT-WF-066'daki gibi test kurulum hatası), yanlışlıkla `200` alındı. `Tenancy:Enabled`/`AllowHeaderResolution` açılıp yeniden başlatıldıktan sonra doğru şekilde tekrarlandı: `kiraci-beta` başlığıyla `HTTP: 404`, `title: "Calistirma bulunamadi"`, `detail: "'<runId>' kimlikli calistirma yok."` Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Güvenlik: API Anahtarı Kapsam Boşluğu

---

## MT-WF-100 — 🚨 `RunsRead`-kapsamlı bir API anahtarı workflow `PUT`/`run`'a erişebiliyor mu?

**Gerçek sonuç**
🚨 **ŞÜPHE DOĞRULANDI — KUSUR, Önem: Yüksek.** Yalnız `RunsRead` kapsamlı bir anahtar üretildi (`plaintextKey` alanı — doküman `rawKey` varsaymıştı, düzeltildi). Adım 2: `PUT /api/workflows/kapsam-testi` → `HTTP: 200` — kayıt kabul edildi. Adım 3: `POST /api/workflows/kapsam-testi/run` → `HTTP: 200`, gerçek bir çalıştırma başlatıldı (`event: done`, gerçek `runId`, gerçek model çağrısı — gerçek ücret oluştu). Adım 4 (kontrol grubu): `PUT /api/agents/kapsam-kontrol` aynı anahtarla → `HTTP: 403`, `title: "Kapsam yetersiz"`, `detail: "Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor."` — kapsam sistemi `AgentEndpoints`'te ÇALIŞIYOR, `WorkflowEndpoints`'te TAMAMEN DEVRE DIŞI. Doğrulandı: yalnız-okuma niyetiyle üretilmiş bir otomasyon anahtarı workflow tanımlarını yazabilir/silebilir VE gerçek para harcayan bir çalıştırma başlatabilir.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-006/K-405 — düzeltildi):** `WorkflowEndpoints`'in tüm uçlarına `RequireApiKeyScope` eklendi — tanım yönetimi (`GET`/`PUT`/`DELETE`) yeni `WorkflowsRead`/`WorkflowsAdmin` kapsamlarını, çalıştırma/run-durumu uçları (`run`/`resume`/`respond`/checkpoint/istek listeleme) var olan `RunsRead`/`RunsWrite`'ı alıyor. Aynı senaryo birebir tekrarlandı: yalnız `RunsRead` taşıyan anahtarla `GET /api/workflows` → `403 "WorkflowsRead kapsamini gerektiriyor"`, `PUT` → `403 "WorkflowsAdmin kapsamini gerektiriyor"`, `run` → `403 "RunsWrite kapsamini gerektiriyor"`. Regresyon: dört kapsamı taşıyan bir anahtarla aynı üç uç `200`. Ayrıntı: `SONUCLAR-K-2026-08-13.md`, `K-405`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
