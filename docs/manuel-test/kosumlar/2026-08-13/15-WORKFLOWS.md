# 15 — Workflows: Yürütme, Kontrol Noktası, Graf ve Human-in-the-Loop (`WF`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../15-WORKFLOWS.md`](../../15-WORKFLOWS.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/15-WORKFLOWS.md
> ```

---

## Temiz geçen case'ler (46)

| Case | Durum | Başlık |
|---|---|---|
| MT-WF-001 | ☑ | `PUT /api/workflows/{name}` yeni bir Sequential tanım oluşturur (`200`) |
| MT-WF-002 | ☑ | Aynı adı tekrar `PUT` etmek günceller, `version` artar |
| MT-WF-003 | ☑ | `GET /api/workflows/{name}` veritabanında saklı bir tanımı döner |
| MT-WF-005 | ☑ | `GET /api/workflows` kod + veritabanı birleşik liste; isim çakışmasında KOD kazanır |
| MT-WF-006 | ☑ | `DELETE` veritabanı kaydını siler, sonraki `GET` `404` verir |
| MT-WF-007 | ☑ | Var olmayan bir adı silmek → `404` |
| MT-WF-008 | ☑ | Kod-tanımlı bir adı silmeye çalışmak → `404`, çalışmaya devam eder |
| MT-WF-009 | ☑ | Boşluktan ibaret ad → `400` "name alanı zorunludur" |
| MT-WF-011 | ☑ | `agentNames` boş → `400` |
| MT-WF-012 | ☑ | Aynı agent adı iki kez → `400` |
| MT-WF-013 | ☑ | `Concurrent` + tek agent → `400` (en az iki ister) |
| MT-WF-014 | ☑ | `Magentic` + boş `managerAgentName` → `400` |
| MT-WF-015 | ☑ | `Magentic` + yönetici aynı zamanda katılımcı → `400` |
| MT-WF-016 | ☑ | `GroupChat` + `managerAgentName` verilirse → `400` (bu alan yalnız `Magentic`'indir) |
| MT-WF-017 | ☑ | `Sequential` + `handoffInstructions` verilirse → `400` |
| MT-WF-018 | ☑ | `Magentic` olmayan desende `requirePlanApproval: true` → `400` |
| MT-WF-019 | ☑ | `maxIterations: 0` → `400` |
| MT-WF-030 | ☑ | Workflows ekranı: kod/veritabanı rozetleri ve katılımcı zinciri |
| MT-WF-031 | ☑ | Editör: `Save` butonu ad/katılımcı boşken devre dışı, sunucuya istek gitmez |
| MT-WF-032 | ☑ | Editör: desen değişince alan görünürlüğü ve maliyet uyarısı değişir |
| MT-WF-033 | ☑ | Editör: `Concurrent` desende tek katılımcı seçiliyken uyarı metni görünür |
| MT-WF-034 | ☑ | Detay ekranı: kod-tanımlı workflow'da `Edit` düğmesi hiç yok |
| MT-WF-035 | ☑ | Kod-tanımlı bir adın `/edit` URL'ine doğrudan gidilirse hata paneli |
| MT-WF-040 | ☑ | `ozetle-ve-cevir` çalıştırma: olay tipleri ve `runs` ağacı |
| MT-WF-041 | ☑ | `runs.kind` / `workflow_name` veritabanı doğrulaması |
| MT-WF-043 | ☑ | Kontrol noktalarının `run_id` ve `session_id` ile filtrelenebilirliği |
| MT-WF-044 | ☑ | Aynı workflow iki kez çalıştırılınca executor kimlikleri SABİT kalır (K-127 kanıtı) |
| MT-WF-050 | ☑ | `inceleme-zinciri` tanımla ve çalıştır, ağaç 3 satır |
| MT-WF-051 | ☑ | Varsayılan `resume`: `checkpointId` verilmezse SON kontrol noktası kullanılır |
| MT-WF-052 | ☑ | Belirli bir `checkpointId` ile erken bir noktadan `resume` |
| MT-WF-053 | ☑ | Tanım güncellendikten sonra ESKİ bir kontrol noktasından `resume` → uyumsuzluk hatası |
| MT-WF-060 | ☑ | Çalıştırma `AwaitingInput` ile kapanır, `Boolean` form kartı |
| MT-WF-061 | ☑ | `GET /requests` yalnız akış kapandıktan sonra çağrılır (arayüz kuralı) |
| MT-WF-063 | ☑ | `respond` reddet → çıktı BİREBİR `"Yayin iptal edildi; ozet arsivde birakildi."` |
| MT-WF-064 | ☑ | Yanlış `requestId` ile `respond` → SSE `error` |
| MT-WF-065 | ☑ | `AwaitingInput` OLMAYAN bir çalıştırmaya `respond` → SSE `error` |
| MT-WF-066 | ☑ | Başka kiracının `AwaitingInput` çalıştırmasına `/requests` → `404` |
| MT-WF-070 | ☑ | `plan-onayli` tanımla ve çalıştır → `AwaitingInput`, form `PlanReview` |
| MT-WF-080 | ☑ | `GET /graph` düğüm kimlikleri `ExecutorInvoked` ile birebir eşleşir |
| MT-WF-081 | ☑ | Graf HİÇ çalıştırılmamış bir tanım için de `200` döner (derlenmişten, geçmişten değil) |
| MT-WF-082 | ☑ | Arayüzde canlı düğüm renklendirme: `running` (cyan, nabız) → `done` (yeşil) |
| MT-WF-083 | ☑ | `Concurrent` desende `Batcher` düğümleri agent SAYILMAZ |
| MT-WF-084 | ☑ | "Copy Mermaid" panoya `flowchart` içeren metin kopyalar |
| MT-WF-090 | ☑ | `UseWorkflows()` KALDIRILIRSA çalıştırma uçları `501` döner (geçici kod değişikliği) |
| MT-WF-094 | ☑ | Hiç kontrol noktası yazılmamış bir `runId`'yi sürdürmek → "kontrol noktasi yok" |
| MT-WF-097 | ☑ | Başka kiracının kontrol noktası listesi → `404` |

## Ayrıntı taşıyan case'ler (14)

## MT-WF-004 — Aynı uç, KODda tanımlı bir workflow için ayırt edici bir `404` döner (düzeltildi)

**Gerçek sonuç**
Adım 1: `ozetle-ve-cevir` listede (`['inceleme-zinciri', 'ozetle-ve-cevir', 'ozetle-ve-onayla']`). Adım 2: `HTTP: 404`, `title: "Duzenlenebilir tanim yok"`, `detail: "'ozetle-ve-cevir' kodda tanimli bir workflow'dur (AddWorkflow). Listelenir ve calistirilabilir ama veritabaninda duzenlenebilir bir WorkflowDefinition tasimaz."` — generic mesaj DEĞİL, ayırt edici mesaj. Tam beklendiği gibi, fix regresyonu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-010 — 🚨 `kind` alanı gövdede atlanırsa sessizce `Sequential`'a düşer

**Gerçek sonuç**
Şüphe doğrulandı: `HTTP: 200`, gövdede `kind: "Sequential"` — `kind` alanı hiç gönderilmeden. Kod kusuru değil, ürünün bilinçli tasarım seçimi (`WorkflowKind.Sequential` varsayılan enum değeri `0`); dokümanın kendisi bunu zaten "şüphe" olarak işaretlemişti, şimdi ölçüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-020 — Var olmayan agent adı KAYITta kabul edilir, RUN'da SSE `error` verir

**Gerçek sonuç**
Adım 1: `HTTP: 200`. Adım 2: `event: run` → `event: event` (`{"type":"RunFailed",...,"text":"'hayali-agent' workflow'u 'yok-boyle-bir-agent' agent'ini kullaniyor ancak boyle bir agent katalogda yok. Once agent'i tanimlayin, sonra workflow'u kaydedin."}`) → `event: done`. Mesaj metni birebir doğru; yalnız SSE çerçeve adı dokümanın varsaydığından farklı (`event: error` DEĞİL, `event: event` içinde `type: RunFailed`). Ürün kusuru değil — dokümanın hangi hata mekanizmasının devreye gireceği varsayımı yanlıştı, düzeltildi (yukarıya bakınız).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Arayüz: Katalog ve Editör (Faz 16)

---

## MT-WF-042 — Kontrol noktası listesi ve `$type` ayracının ilk özellik olduğu kanıtı

**Gerçek sonuç**
HTTP: 3 kayıt, `parentCheckpointId` zinciri `null → id1 → id2` doğru sırayla. SQL: `sutun_tipi = json`. İlk 40 bayt: `{"stepNumber":0,"workflow":{"executors":` — `$type` yok ama JSON içinde başka yerde mevcut (yukarıya bakınız). K-027'nin sütun-tipi iddiası doğrulandı; "$type ilk özelliktir" alt iddiası düzeltildi.

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
(`docs/arsiv/fazlar/15-WORKFLOWS-YURUTME.md` §1: "Yürütme bir TurnToken ister") yalnız
TAZE bir çalıştırma için geçerlidir; Faz 16'nın kendi ölçtüğü DAVRANIŞ
(`docs/arsiv/fazlar/16-WORKFLOWS-ARAYUZ.md` §3: "kontrol noktası bekleyen isteği TAŞIR
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

## MT-WF-100 — 🚨 `RunsRead`-kapsamlı bir API anahtarı workflow `PUT`/`run`'a erişebiliyor mu?

**Gerçek sonuç**
🚨 **ŞÜPHE DOĞRULANDI — KUSUR, Önem: Yüksek.** Yalnız `RunsRead` kapsamlı bir anahtar üretildi (`plaintextKey` alanı — doküman `rawKey` varsaymıştı, düzeltildi). Adım 2: `PUT /api/workflows/kapsam-testi` → `HTTP: 200` — kayıt kabul edildi. Adım 3: `POST /api/workflows/kapsam-testi/run` → `HTTP: 200`, gerçek bir çalıştırma başlatıldı (`event: done`, gerçek `runId`, gerçek model çağrısı — gerçek ücret oluştu). Adım 4 (kontrol grubu): `PUT /api/agents/kapsam-kontrol` aynı anahtarla → `HTTP: 403`, `title: "Kapsam yetersiz"`, `detail: "Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor."` — kapsam sistemi `AgentEndpoints`'te ÇALIŞIYOR, `WorkflowEndpoints`'te TAMAMEN DEVRE DIŞI. Doğrulandı: yalnız-okuma niyetiyle üretilmiş bir otomasyon anahtarı workflow tanımlarını yazabilir/silebilir VE gerçek para harcayan bir çalıştırma başlatabilir.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-006/K-405 — düzeltildi):** `WorkflowEndpoints`'in tüm uçlarına `RequireApiKeyScope` eklendi — tanım yönetimi (`GET`/`PUT`/`DELETE`) yeni `WorkflowsRead`/`WorkflowsAdmin` kapsamlarını, çalıştırma/run-durumu uçları (`run`/`resume`/`respond`/checkpoint/istek listeleme) var olan `RunsRead`/`RunsWrite`'ı alıyor. Aynı senaryo birebir tekrarlandı: yalnız `RunsRead` taşıyan anahtarla `GET /api/workflows` → `403 "WorkflowsRead kapsamini gerektiriyor"`, `PUT` → `403 "WorkflowsAdmin kapsamini gerektiriyor"`, `run` → `403 "RunsWrite kapsamini gerektiriyor"`. Regresyon: dört kapsamı taşıyan bir anahtarla aynı üç uç `200`. Ayrıntı: `SONUCLAR-K-2026-08-13.md`, `K-405`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
