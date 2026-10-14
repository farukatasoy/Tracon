# Faz 200 — Workflow Sürüm Geçmişi ve Sürüm Sabitleme

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** Bakımcı, 2026-10-14 (Açık Soruların önerileri olduğu gibi kabul edildi: 1-A, 2-A, 3-A, 4-N, 5-A, 6-B)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-300**
> **Önkoşul:** [Faz 195](195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) kapanmalı (ortak düğüm modeli ve editör); Faz 197 ve 201 ile dosya ortak
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, SQL katmanı, `.Testing.Contracts.Xunit`, `.UI`
> **Yeni paket:** Yok · **Migration:** gerekli, üç sağlayıcı; numara uygulama anında alınır
> **Public API:** büyüyor, GA'dan (K-603) önce ucuz — ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/workflows.md`, `concepts/agents.md`, `ui.md`, `capabilities.md` · sevk edilen: `src/Tracon.Workflows/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 8e4786fc:docs/arsiv/fazlar/200-WORKFLOW-SURUM-GECMISI-VE-SABITLEME.md
> ```
>
> Damıtıldı 2026-10-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Operatör zinciri, ayarı ve talimatı canlı değiştirir; workflow tanımının geçmişi yoktur. Bekleyen veya kesilen run resume'da güncel tanımı derler: yapısal değişiklik onu kırar, ayar değişikliği onu sessizce değiştirir. - **F-300** — workflow geçmişi ve geri alma, kök run'a sürüm damgası, agent düğümüne sabit sürüm.

## Bitiş Ölçütleri (DoD)

- [x] İki `PUT` → `…/versions` iki anlık görüntü; `rollback {version:1}` → sürüm 3 = v1 (test 1, 17) — `WorkflowVersionEndpointTests.Two_saves_list_two_snapshots_…` · `A_rollback_writes_the_old_content_as_a_new_version_…` · `WorkflowRollbackTests` (altı case: geçersiz içerik `400`, olmayan sürüm `404`, hiçbiri yazılmaz ve denetlenmez) · örnek uygulamada ölçüldü (aşağıda)
- [x] Bekleyen run kaynak sürümle sürer, sürüm yoksa kapalı düşer (test 9-11) — `WorkflowResumePinTests` (6) · `WorkflowStructuralChangeResumeTests` (iki yarı) · `WorkflowResumeVersionHttpTests` (akış açılmadan `400`) · `WorkflowFunctionSettingsResumeTests`
- [x] Sabitli adımın çocuk satırı sabiti yazar; sabit değişimi checkpoint'i kırmaz (test 14-15) — `WorkflowAgentVersionPinTests.A_pinned_step_runs_the_pinned_text_while_the_agent_is_edited` (modele ulaşan talimat metniyle) · `…binds_the_workflows_parameters_still_runs_its_pinned_version` (parametreli yol) · `WorkflowStructuralChangeResumeTests.Re_pinning_an_agent_step_leaves_the_old_checkpoint_usable_for_either_version`
- [x] §200.4 sınıf taraması belgeye yazıldı — aşağıda
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban c8b9ea01`: `build` 0 uyarı; tam test koşumu yeşil (4. koşum; 3. koşumda `JobWorkerDeadlineTests` net9.0 tam koşumda 30 sn'de düştü, yalıtılmış geçti, sonraki tam koşumda geçti — kaynak çekişmesi sınıfı); `pack` yeşil; `dotnet format` ilk koşumda `WorkflowRollbackTests` içe aktarma sırasını yakaladı, düzeltildi, temiz; `npm run check` yeşil (1088 sayfa, bağlantı ve SEO temiz)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (sabit + resume), çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`: ✅ temiz
- [x] Manuel kabul case'leri `docs/manuel-test/15-WORKFLOWS.md` içine eklendi (`MT-WF-150`…`155`); otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 🔴 yok; dört 🟡 (üçü düzeltildi, biri gerekçelendi + F-492, `respond` kısmı F-496) ve beş 🟢 (aşağıda)
- [x] `docs-site/` güncellendi, `npm run check` (build, bağlantılar, SEO, ağırlık) temiz; `en.ts`/`tr.ts` eksiksiz (`i18n.test.ts`); konsol paketi 209,0 KB gzip; `workflow-editor*` ekran görüntüleri ve damga yenilendi; `CHANGELOG.md` davranış değişimini anar. **Site yayınlanmadı** (`site-deploy.sh` bakımcının kararıdır)

## Kapanış kanıtı

### §200.4 sınıf taraması

`grep -rn "new WorkflowExecution" src/Tracon.Workflows` → `WorkflowRunner.cs`'te üç yer: `RunStreamingAsync` (yeni run: sürüm kayıt anında **okunan** tanımdan damgalanır), `PrepareResumeAsync` ve `PrepareResponseAsync` (`DefinitionVersion = istek ?? kaynak.AgentVersion`). Faz 197'nin kurtarma yolu (`WorkflowRecovery`, `WorkflowRunJobHandler.Continue`) checkpoint varsa resume'u çağırır: kaynak olarak checkpoint'i yazan satırı verir (`checkpoint.RunId`), o satır kendi damgasını taşır (`The_stamp_follows_a_chain_of_resumes`). Checkpoint yoksa işlem yeni kimlikle yeniden uygulanır; `resume`/`respond` yükü `definitionVersion` taşır, yeni `run` güncel tanımı derler (sapma 11). Kuyruk yükü (`WorkflowRunJobPayload`) `definitionVersion` taşır ve geçersizse işi KAPALI düşürür. `new WorkflowExecution` tarandıktan sonra sürüm taşımayan üçüncü bir yer yoktur; `ReadRecordedParametersAsync` ve `RequireParticipantCoverageAsync` de aynı sürümü kullanır.

### Örnek uygulama koşumu (`samples/Tracon.Api`, SQLite, Echo sağlayıcı)

`ASPNETCORE_ENVIRONMENT=Development`, `Tracon__PostgreSql__ConnectionString=` `Tracon__SqlServer__ConnectionString=` `Tracon__Ui__AuthToken=` boş (bkz. hafıza: elle koşum ortamı); `scribe` stored agent (`echo-instructions-1` modeli: yanıt aldığı talimatı yazar) iki sürümlü, `proof` stored agent.

```text
2. refused pins
   400 Node 'scribe' in workflow 'pinned-demo' pins agent 'scribe' to version 9, but the agent has no version 9. List its versions with GET /api/agents/scribe/versions.
   400 Node 'scribe' in workflow 'pinned-demo' has an 'agentVersion' of 0. Versions are numbered from 1. ...
   400 Node 'word-count' in workflow 'pinned-demo' is a function node but carries an 'agentVersion'. ...
   400 Node 'podcast-writer' in workflow 'pinned-demo' pins agent 'podcast-writer' to version 1, but 'podcast-writer' is defined in code and keeps no version history. ...
3. PUT scribe(agentVersion 1) + word-count            -> {'version': 1, 'pin': 1}
4. run (scribe artık sürüm 2)
   output: Echo [instructions: SCRIBE-ALPHA: write one line.]: hello  (word count: 7)
   run row: Completed, workflow version 1 · tree: Workflow pinned-demo v1 > Agent scribe v1
5. PUT (proof adımı eklendi)                          -> {'version': 2, nodes: scribe, word-count, proof}
6. resume (sürümsüz)  [HTTP 200] completed · yeni satır workflow version 1   (düzenleme run'ı etkilemedi)
7. resume definitionVersion=2 [HTTP 200] FAILED: ... the graph's structure differs from when the checkpoint was written ...
8. resume definitionVersion=9 [HTTP 400] "Definition version not found" - Workflow 'pinned-demo' has no version 9 ...
9. versions [[2,3],[1,2]] · diff left [scribe, word-count] right [scribe, word-count, proof]
   rollback 1 -> version 3 [scribe, word-count] · versions [3,2,1] · rollback 42 -> 404 "Workflow 'pinned-demo' has no version 42."
10. DELETE [204] sonra PUT -> version 4                (numara yeniden verilmedi)
11. audit: workflow.rollback workflow:pinned-demo {"rolledBackToVersion":1,"newVersion":3}
```

Süreç notu: ilk başlatma `user-secrets`'taki PostgreSQL dizesini okudu ve `0059`'u paylaşılan yerel veritabanına (`agentprism` şeması) uyguladı; bkz. `docs/hafiza/elle-kosum-ortami.md` ve Devir Notu.

### Doğrulama komutları

```bash
curl -s http://localhost:5081/tracon/api/workflows/narrated/versions
```

---

## Plandan Sapmalar

Plan 2026-10-12'de yazıldı, bakımcı 2026-10-14'te altı Açık Sorunun önerilerini (1-A, 2-A, 3-A, 4-N, 5-A, 6-B) olduğu gibi onayladı; plan revize edilmedi. Uygulamada alınan kararlar:

| # | Plan | Gerçekleşen | Gerekçe |
|---|---|---|---|
| 1 | §200.1 "geri alma eski içeriği yeni sürüm yazar" yalnız çalışan tanım için tarif edilmişti | `IWorkflowDefinitionStore.RollbackAsync` **silinmiş bir adı yeniden yaratır** ve numara sürer; uçlar (`versions`, `diff`, `rollback`) güncel tanım yoksa `404` döner | Geçmiş silmede kalır (Açık Soru 4-N); mağaza düzeyinde "içerik zaten var, numarası sürer" tek tutarlı kuraldır. Uç, silinmiş workflow'a geçmiş sunmaz: geçmiş numara kimliği içindir, çalıştırmak için değil. `A_rollback_of_a_deleted_definition_creates_it_again_…` sabitler |
| 2 | §200.4 "Resume yine güncel tanımı ister (`WorkflowEndpoints.cs:124-125`)" | `WorkflowCatalog.FindAsync` önce **güncel** tanımı okur (yoksa `null`); sürüm adı konmuşsa ve güncelden farklıysa ayrıca `GetVersionAsync` | Silinmiş workflow'un resume'unun reddi korundu (`A_resume_of_a_deleted_workflow_is_still_refused_…`); ek bir okuma yalnız eski sürüm istenince yapılır |
| 3 | §200.4 "Tanım yoksa satır açılır, `Failed` kapanır" | Tanım okuma hatası da (`GetAsync` fırlatırsa) tutulur: satır açılır, hata kurulum bloğunda atılır, run `Failed` kapanır, damga yazılmaz | `ProbeWorkflowDefinitionStore.GetFailure` ile sınandı; yoksa uçun duyurduğu run kimliği hiçbir satıra işaret etmezdi |
| 4 | §200.3 "Varlık derlemede denetlenir" | Denetim kayıtta DA yapılır (`AgentVersionPin`, aynı sınıf iki çağıran); agent betimleyicisi + tek sürüm satırı okunur, agent **derlenmez**; özel kaynak agent'ı (`Custom`) atlanır | Test 16 "olmayan sürüm → `400`" kayıtta ister. Derleme MCP bağlantısı açar ve denetim graf görünümünde de koşar |
| 5 | Plan test sınıfı adları ve seviyeleri | `WorkflowRunStampTests`, `WorkflowResumePinTests`, `WorkflowNodeAgentVersionTests`, `WorkflowNodeAgentVersionValidationTests` **birim** (Workflows.UnitTests); `WorkflowStructuralChangeResumeTests` (birim) iki yarıya bölündü; fonksiyonel: `WorkflowVersionEndpointTests`, `WorkflowRollbackTests`, `WorkflowAgentVersionPinTests`, `WorkflowResumeVersionHttpTests`; Core: `AgentVersionPinTests`, `AuditingWorkflowDefinitionStoreTests` | Runner davranışı süreç içi `WorkflowTestHost` ile hızlı ve deterministik ölçülür; HTTP sınırı (400/404/409, kiracı, rol, denetim) fonksiyonel testte |
| 6 | Test 8 "alt sistem hatası: geçmiş yazımı düşer, ana satır kalır" `StoreCancellationContract`'a bağlıydı | `WorkflowDefinitionStoreContract`'a isteğe bağlı SQL kancaları (`SupportsRawHistory`, `ExecuteRawAsync`, `QualifiedWorkflowHistoryTable`, `RawNowExpression`) eklendi; üç sağlayıcı geçmiş tablosuna çakışan satır yazar | İptal sözleşmesi çalışan işi değil yalnız önceden iptal edilmiş token'ı sınar; başarısız geçmiş yazımını ancak ham SQL ile üretebiliriz. Kanca kapalıysa test erken döner (`AuditLogContract` emsali). İşlem iki parçaya bölününce testin KIRILDIĞI ölçüldü |
| 7 | "Mevcut resume testleri" | 12 mevcut test eski kuralı (resume güncel olanı derler) kodluyordu ve bilerek değişti: `WorkflowStructuralChangeResumeTests` (8), `WorkflowFunctionSettingsTests`, `WorkflowFunctionTimeoutResumeTests`, `WorkflowParameterRespondTests` (2), fonksiyonel `WorkflowFunctionSettingsResumeTests` | Davranış değişimi bu fazın amacıdır (K-988); her biri iki yarıya bölündü: kaydedilen sürüm sürer, düzenlenen sürüm adı konunca eski davranış |
| 8 | `IWorkflowRunner` | Üye eklenmedi; `WorkflowResumeRequest.DefinitionVersion` ve `WorkflowRespondRequest.DefinitionVersion` yetti | Arayüz üyesi uygulayıcıyı kırardı (Açık Soru 2'nin ruhu); HTTP erken denetimi `IWorkflowDefinitionStore` ile uçta yapılır |
| 9 | Kuyruk yükü planda yoktu | `WorkflowRunJobPayload` `definitionVersion` taşır (`1`'den küçük ya da tam sayı olmayan → iş KAPALI düşer) | `resume`/`respond` kuyruğa alınabildiği için sürümü sessizce düşürmek K-988'ü kuyrukta çiğnerdi |
| 10 | — | `WorkflowEndpoints.ValidateDefinitionAsync` **tek** doğrulama yeri: `PUT` ve geri alma ikisi de onu çağırır | Geri alma içeriği "kaydedildiğinde geçerliydi"; sonradan kaldırılan fonksiyon veya silinip yeniden yaratılan agent'ın sürümü "güncel" tanımı derlenemez yapardı |
| 11 | Faz 197'nin kurtarma yolları damgayı taşır (§200.4 sınıf taraması) | Checkpoint'siz yeniden başlatma — `WorkflowRunJobHandler.Continue` → `Apply` ve `WorkflowJobHandler` (batch öğesi) — yeni bir `run` açar ve **güncel** tanımı derler; kesilen denemenin damgası taşınmaz | Checkpoint yoksa hiçbir super-step bitmedi, kayıp iş yoktur; kesilen satır `Failed` kalır, yeni satır kendi damgasını yazar. Resume yolları (`checkpoint.RunId`) doğru kalır. Kapatmak için damgayı `WorkflowRunRequest`'e taşımak gerekir — bilinçli olarak yapılmadı: sürüm geçişinde "yeniden başlatma yeni sürümle koşar" tutarlı ve zararsız, kapsam büyütür. Aday: F-492 |
| 12 | Test 5 "mevcut migration testleri" geri doldurmayı sınar | `WorkflowVersionHistoryMigrationTests` üç sağlayıcıda eski şemaya iki workflow tohumlar, migration'ı uygular, geçmişi, bir sonraki kaydı ve geri alma'yı sınar; migration SQL'i ikinci kez koşar, satır eklenmez ya da değişmez. Ayrıca kuyruklu `resume` için `definitionVersion` iki test (`WorkflowAsyncRunTests`) | Denetimin 🟡1 ve 🟡2 bulgusu: plan "mevcut testler" dedi, hiçbiri geri doldurmayı ya da HTTP → iş yükü → işçi zincirini koşmuyordu. Bağımsız bir test yoksa `SELECT` sütunu ya da `WHERE` bozulduğunda hiçbir test kırılmazdı |
| 13 | Geri alma ucu depo hatasını da yakalar (agent ucunun kalıbı) | `RollbackAsync` ucu `TraconException`'ı yakalamaz: "sürüm yok" `GetVersionAsync` ile önceden `404` olur | Sürüm hiç silinmez; yakalanan istisna okunamayan bir yük gibi gerçek bir depo hatasını `404`'e çevirirdi (denetim 🟢1) |

## Bu Fazda Verilen Kararlar

Dört kategoriye giren üç karar `docs/KARARLAR.md`'ye yazıldı (tam gerekçe `arsiv/KARARLAR-GECMISI.md`):

- **K-987** *(kalıcı-veri, public-api, geri-dönüşü-pahalı)* — **K-126 açıldı:** workflow tanımının sürüm geçmişi tutulur. `workflow_definition_versions`, yabancı anahtarsız, numara kimlik; kayıt iki satırı tek işlemde yazar; `IWorkflowDefinitionStore` soyut üye alır; migration PostgreSQL `0059` · SQL Server `0047` · SQLite `0046`.
- **K-988** *(public-api, geri-dönüşü-pahalı)* — Kök satır derlediği sürümü `runs.agent_version`'a yazar; resume/respond kaynak damgasını derler; `definitionVersion` başka sürümü seçer; olmayan sürüm kapalı düşer. K-900'ün "resume güncel ayarı okur" ve K-901'in "bekleyen run kırılır" cümleleri **değişti** (K-901 satırı yerinde düzeltildi).
- **K-989** *(public-api)* — `WorkflowNodeReference.AgentVersion`: yalnız `nodes`, yalnız veritabanı agent'ı, kayıtta ve derlemede doğrulanır, önbellek anahtarında ama kimlikte değil; K-901 konsolun `nodes` yazma kuralını "fonksiyon veya sabit" yapar.

Kategori dışı yerel kararlar (burada kalır, `K-*` açılmadı): silinmiş bir ad için mağaza `RollbackAsync`'i yeniden yaratır (sapma 1) · geri alma `PUT` ile aynı tek doğrulayıcıyı kullanır (sapma 10) · SQL kancaları yalnız sözleşme testinde (sapma 6) · checkpoint'siz yeniden başlatma güncel tanımı derler (sapma 11).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — bakımcı 2026-10-14'te altı Açık Soru önerisini olduğu gibi onayladı; 13 uygulama sapması "Plandan Sapmalar"da, plan metni değişmedi |
| Düzeltme turu sayısı | 3 — kapanış kapısı iki tur kırmızı döndü (ilk tur: E2E ve doküman sayıları; ikinci tur: üç sağlayıcının tablo sayısı ve SQL Server kiracı kapsamı listesi), bir tur denetim bulguları |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 — denetimde 🔴 çıkmadı (🟡: 4 gerçek, 0 gürültü) |
| Fazın ürettiği regresyon | 0 davranışta. Kapı turunda 2 bayat sayım: `MigrationRunnerTests` tablo sayısı (×3 sağlayıcı) ve SQL Server `TenantCoverageTests` listesi — yeni tablo ve üyeler onları eskitti; bilerek kırılan kapılardır, düzeltildi |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi — kapanış anında; sonraki `kusur-giderme` koşumu doldurur |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetcisi`, taban `c8b9ea01`): **🔴 yok.** Dört 🟡 ve beş 🟢. Triyaj hepsinde "gerçek".

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Geri doldurma hiçbir testle sınanmıyordu | **Düzeltildi** — `WorkflowVersionHistoryMigrationTests` ×3 sağlayıcı (sapma 12) |
| 2 | 🟡 | Kuyruklu `resume`/`respond` için `definitionVersion` zinciri uçtan uca sınanmıyordu | **Kısmen düzeltildi** (`resume`), `respond` devredildi — `WorkflowAsyncRunTests`: kuyruklu resume kaydedilen sürümü derler, adı konan sürüm işçiye ulaşır, olmayan sürüm `400` ve iş kuyruğa girmez. Kuyruklu `respond` için uçtan uca test **yazılmadı**: stored workflow'un insan girdisi bekleyen bir tezgâhı yok; `WorkflowRunJobPayload` ve handler birim testleri yalnız parçaları sınar — **F-496** |
| 3 | 🟡 | Sevk edilen metinler run satırı damgasının alanını `version` diye yazıyordu; alan `agentVersion` | **Düzeltildi** — `Tracon.Workflows` README, iki `WorkflowEndpoints` açıklaması; OpenAPI, TS ve C# istemcisi yeniden üretildi |
| 4 | 🟡 | Checkpoint'siz kurtarma yeniden başlatması güncel tanımı derliyor; belgede yok; "son satır" cümlesi koda uymuyor | **Gerekçelendi + devredildi** — sapma 11, §200.4 cümlesi düzeltildi, **F-492** |
| 5 | 🟢 | Geri alma ucu her `TraconException`'ı `404` yapıyordu; yorum yanlıştı | **Düzeltildi** — yakalama kalktı, yorum düzeldi (sapma 13) |
| 6 | 🟢 | Birim test sabit sürümünü kendi sahtesinin yazdığı değerden okuyor | **Gerekçelendi** — gerçek kanıt `WorkflowAgentVersionPinTests` (agent v2 iken satır v1); birim test yalnız bağlantıyı sınar |
| 7 | 🟢 | Geçmiş girdisi silinemez; ayara yanlışlıkla yazılan `secret` kalır | **Devredildi** — **F-493** (denetim izi zaten aynı tanımı tutar; site uyarır) |
| 8 | 🟢 | Graf görünümü her zaman güncel grafiği çizer | **Devredildi** — **F-494** |
| 9 | 🟢 | "Olmayan sürüm run'ı düşürür" sözü doğrudan API'de istisnadır | **Düzeltildi** — `IWorkflowRunner` XML'i ve `CHANGELOG.md` |

Ayrıca bu fazda çıkan, faz dışı kusur: üretilen `docs-site/src/content/docs/api/` sayfaları xunit `[Fact]` özniteliğinin mutlak kaynak yolunu taşır (yerel kullanıcı yolu siteye gider) — **F-495**.

## Sonraki Faza Devir Notu

Faz 201 (Loop ölçütü) ile dosya ortaktır (`workflow-editor/steps.tsx`, `lib/workflow-steps.ts`, `WorkflowEndpoints.cs`); yeni `WorkflowNodeReference` alanı ve `WorkflowDefinitionPayload` kuralları geçerlidir.

- **Kural (yeni):** bir workflow run satırı açan her yol `AgentVersion` damgasını yazar ve resume/respond kaynağın damgasını derler. Yeni bir `new WorkflowExecution` ya da yeni bir kuyruk işlemi eklenirse `definitionVersion` taşımalıdır; sınıf taraması §200.4'te.
- **Kural:** `IWorkflowDefinitionStore` üç soyut üye alır — üçüncü taraf uygulayıcıyı kırar (K-987). Bir üyeyi daha eklemek `WorkflowDefinitionStoreContract`, `InMemoryWorkflowStores.WorkflowHistory`, `AuditingWorkflowDefinitionStore` ve üç SQL katmanını birlikte ister.
- **🚨 Tuzaklar:**
  - Yeni bir tablo üç sağlayıcının `MigrationRunnerTests` sayısını ve yeni bir mağaza üyesi SQL Server `TenantCoverageTests.Covered` listesini kırar — kapanış kapısı bunu ancak tam koşumda gösterir.
  - Migration numaraları bu fazda: PostgreSQL `0059`, SQL Server `0047`, SQLite `0046`. Başka bir oturum aynı numarayı almış olabilir; birleştirmede `scripts/applied-migrations.json` ve numara çakışmasını denetle.
  - `docs-site/src/content/docs/api/` üretilen sayfalar mutlak yol gömer; adında `faz` geçen bir çalışma ağacında `SourceLanguageTests` kırılır. Koşmadan önce klasörü sil (F-495).
  - `TRACON_UI_SCREENSHOTS=1` yetmiş kadar PNG'yi yeniden yazar; yalnız gerçekten değişen ekranı commit et (bu fazda `workflow-editor*`) ve `refresh-console-screenshot-stamp.mjs` çalıştır.
  - Örnek uygulamayı `Tracon__PostgreSql__ConnectionString=` boş koşmazsan `user-secrets`'taki paylaşılan yerel veritabanına migration yazar (`docs/hafiza/elle-kosum-ortami.md`).
- **Aday olarak açık:** F-408 (skill sürüm geçmişi) · F-409 (alt agent ve skill sabiti) · F-492 (checkpoint'siz yeniden başlatma) · F-493 (geçmiş budama) · F-494 (sürüme göre graf) · F-495 (mutlak yol sızıntısı) · F-496 (kuyruklu `respond`'un `definitionVersion` testi).
