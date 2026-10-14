# Faz 200 — Workflow Sürüm Geçmişi ve Sürüm Sabitleme

> **Durum:** 🚧 Uygulandı, kapanış sürüyor (2026-10-14)
> **Plan onayı:** Bakımcı, 2026-10-14 (Açık Soruların önerileri olduğu gibi kabul edildi: 1-A, 2-A, 3-A, 4-N, 5-A, 6-B)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-300**
> **Önkoşul:** [Faz 195](arsiv/fazlar/195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) kapanmalı (ortak düğüm modeli ve editör); Faz 197 ve 201 ile dosya ortak
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, SQL katmanı, `.Testing.Contracts.Xunit`, `.UI`
> **Yeni paket:** Yok · **Migration:** gerekli, üç sağlayıcı; numara uygulama anında alınır
> **Public API:** büyüyor, GA'dan (K-603) önce ucuz — ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/workflows.md`, `concepts/agents.md`, `ui.md`, `capabilities.md` · sevk edilen: `src/Tracon.Workflows/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md`

---

## Bu Faza Başlarken

> `faz-baslangic` uygula.

1. Bu doküman
2. `grep -n "K-126\|K-811\|K-820\|K-891\|K-895\|K-900\|K-901" docs/KARARLAR.md` — §200.1 ve §200.4 bunlara dayanır
3. `awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/195-*.md`
4. `hafiza/workflows.md`, `hafiza/agent-derleme-ve-onbellek.md`, `hafiza/sql-migration.md`
5. Faz 201 devri (editör dosyaları ortak): `awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/201-*.md`
   (arşivlenmediyse `docs/201-*.md`). 🚨 `agent-detail.tsx` `VersionCompare` artık bir Loop
   tablosu çizer (`formatLoopCriteria`, `data-testid="loop-diff"`) — `VersionHistory` ortak
   bileşene çıkarken karşılaştırma bölümü yerinde kalır. `full-replace-editors.test.tsx`'in
   agent fixture'ı üç ölçütlü bir loop ve `api/loop-criteria` fixture'ı taşır; `agent-detail.test.tsx`
   sürüm karşılaştırmasını checkbox'larla açan bir test içerir — workflow sürüm testleri bu deseni kullanabilir.
6. **Faz 196 devri** (`awk '/## Sonraki Faza Devir Notu/,0' docs/196-UZUN-ADIM-SURE-VE-IPTAL.md`, arşivlendiyse `docs/arsiv/fazlar/` altında): `WorkflowDefinition.RunTimeout` ve `WorkflowNodeReference.Timeout` tanımın parçasıdır; sürüm geçmişi ve geri alma iki alanı da taşımalı (`WorkflowDefinitionPayload`, `WorkflowDefinitionStoreContract.Node_timeouts_and_the_run_timeout_survive_the_round_trip`). 🚨 Süre değişikliği şekil (`shapeOf`) değişikliği DEĞİLDİR ve bekleyen run'ı resume edilemez yapmaz (K-959); sürüm damgası bunu "yapısal değişiklik" saymamalı. Editörde `Step.timeout`/`storedTimeout` ve `runTimeout`/`storedRunTimeout` K-918 deseniyle dokunulmadan döner; `hasStrandedTimeouts` kaydı tutar.

---

## Amaç

Operatör zinciri, ayarı ve talimatı canlı değiştirir; workflow tanımının geçmişi yoktur. Bekleyen veya kesilen run resume'da güncel tanımı derler: yapısal değişiklik onu kırar, ayar değişikliği onu sessizce değiştirir.

- **F-300** — workflow geçmişi ve geri alma, kök run'a sürüm damgası, agent düğümüne sabit sürüm.

Tüketici: kiracı başına stored `Sequential` workflow (podcast hattı). Kabul: "dünkü düzenleme bozdu" anında geri dönüş; üretim başladığı sürümle biter.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Skill geçmişi; alt agent sürüm sabiti | Açık Soru 6 |
| Agent → skill sabiti; sabitsiz adımı dondurma | Ayrı aday (F-409); K-895 (c) |
| Sürümle yeni run; `Sequential` dışı sabit | İhtiyaç ölçülmedi; `Nodes` yalnız `Sequential` |
| Geçmiş tavanı, sürümde yazar | Agent geçmişinde de yok |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `IAgentDefinitionStore.cs:42,69,79` · `AgentEndpoints.cs:138,151,164` | Agent geçmişi, geri alma, fark — desen |
| `IWorkflowDefinitionStore.cs:6-12,20-54` · `0007_workflows.sql:22-25` | Yalnız `Get/List/Save/Delete`; "geçmiş yok, denetim izi yeter" (K-126) |
| `AuditingWorkflowDefinitionStore.cs:72-85` · `AuditRecorder.cs:12-13` | `before`/`after` best-effort, hata yutulur; okuma yolu yok |
| `WorkflowRunner.cs:586-611`, `:666` | Kök run sürümsüz; satır tanım çözülmeden açılır |
| `WorkflowRunner.cs:285-301`, `:310-350`, `:387` | Resume/respond yalnız ad taşır; şema güncel tanımdan |
| `WorkflowCatalog.cs:159-167` | Derleme hep güncel tanımı okur |
| `WorkflowDefinitionCompiler.cs:185-187` · `concepts/workflows.md:187-190` | "Resume güncel ayarı okur" |
| `locales/en/workflows.ts:94` | Yapısal değişiklik bekleyen run'ı kırar (K-901) |
| `WorkflowNodeReference.cs:27-72` | Düğüm sürüm taşımaz |
| `WorkflowAgentCache.cs:131,167` · `CallableAgentResolver.cs:45` · `ChildAgentInvoker.cs:569` | Sarmalayıcı `Version: 0`, anahtar sürümsüz; çözüm güncel sürüm |
| `IAgentCatalog.cs:54-58` · `concepts/agents.md:304-307` | Sürümlü çözüm var; site "adım güncel sürümü çözer" der |
| `WorkflowRunner.cs:828` | Parametre varsayılanı adım talimatına girer |
| `hafiza/agent-derleme-ve-onbellek.md` (K-811) | Silme + oluşturma sürümü `1`'e sıfırlar |
| `WorkflowDefinitionStoreContract.cs` | Sürüm senaryosu yok; paket üçüncü taraf içindir |

> Kanıtlar 2026-10-12'de `99fe0b13` zemininde doğrulandı.

---

## 200.1 — Geçmiş ve migration

- Kayıt aynı işlemde sürüm satırı (`WorkflowDefinitionPayload`, `Settings` dahil) yazar (`SqlAgentDefinitionStore.cs:184-223` deseni); geri alma eski içeriği yeni sürüm yazar; dekoratör `workflow.rollback` yazar.
- 🚨 Numara kimliktir (Açık Soru 4): silme geçmişi silmez. Resume yine güncel tanımı ister (`WorkflowEndpoints.cs:124-125`).
- `workflow_definition_versions`: PK `(tenant_id, name, version)`. Migration mevcut satırları kopyalar; `UpsertWorkflow` INSERT dalı geçmişin en büyüğü + 1 yazar. `IWorkflowDefinitionStore.cs:6-12`, `MIMARI.md:204` yeniden yazılır.

## 200.2 — HTTP ve geri alma

- Liste tam anlık görüntü, fark iki ham anlık görüntü döner (`AgentEndpoints.cs:138-174` eşi).
- Geri alma `PUT` gibi doğrulanır (validator, fonksiyon, `ValidateSettings`); uymayan sürüm `400`. Kod tanımlı ad: okuma `404` + gerekçe (K-820), geri alma `409`.

## 200.3 — Düğümde `AgentVersion`

- `WorkflowNodeReference.AgentVersion` (`int?`): yalnız agent düğümü; `null` güncel sürüm (K1); fonksiyon düğümünde veya `< 1` → `400`. Konsol fonksiyon **veya** sabit varsa `nodes` yazar (K-901).
- Varlık derlemede denetlenir (`WorkflowDefinition.cs:66-72`); kod agent'ı veya olmayan sürüm adımı adıyla düşürür.
- `WorkflowAgentCache` anahtarı sabiti alır; `ChildAgentInvoker` onu `ResolveAsync`/`FindAsync`'e geçirir (K-891); çocuk satır onu yazar.
- Executor kimliği `(workflow, agent)`'tan türer; sabit değişimi yapısal değildir — ölçülmeli (test 15).

## 200.4 — Damga ve resume/respond

- Kök satır derlediği sürümü taşır (Açık Soru 5); kod workflow'u `null`.
- 🚨 Tanım `StartAsync`'ten önce bir kez okunur; damga ve graf aynı okumadan gelir (`WorkflowCatalog.cs:142-147`). Tanım yoksa satır açılır, `Failed` kapanır.
- Resume/respond (Açık Soru 3) kaynak damgasını derler ve damgalar; `definitionVersion` başka sürüm seçer (uymayan yapı: "structure differs"). Şema aynı sürümden. Damgasız run güncel tanımla sürer; sürüm yoksa kapalı düşer.
- K-900/K-901'in resume cümlesi, editör uyarısı ve site değişir. Sabitsiz adım güncel agent'ı çözer (K-895 c).
- Sınıf taraması: `grep -rn "new WorkflowExecution" src/Tracon.Workflows` → `WorkflowRunner.cs:244,291,338`; hepsi sürüm taşır, Faz 197 yolları da.

## 200.5 — Konsol

- `agent-detail.tsx:236-396` `VersionHistory` ortak bileşene çıkar; workflow ayrıntısı onu kullanır.
- Editörde agent düğümüne sürüm seçici; kod agent'ında gizli. Metin `locales/{en,tr}`.

---

## Planlanan Public API

> Taslak. `WorkflowNodeReference.AgentVersion`, `Workflow{Resume,Respond}Request.DefinitionVersion` (`int?`), HTTP DTO'ları: **bugün eklemek bedava / GA sonrası kırıcı**. Arayüz üyesi uygulayıcıyı bugün de kırar (Açık Soru 2).

```csharp
// IWorkflowDefinitionStore; her üye CancellationToken alır
ValueTask<WorkflowDefinition?> GetVersionAsync(string tenantId, string name, int version, …);
ValueTask<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(string tenantId, string name, …);
ValueTask<WorkflowDefinition> RollbackAsync(string tenantId, string name, int version, …);
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/workflows/{name}/versions` · `…/versions/{a}/diff/{b}` | Reader | Liste · fark |
| `POST` | `/api/workflows/{name}/rollback` | Admin | Eski içerik yeni sürüm |
| `POST` | `/api/workflows/runs/{runId}/resume` · `…/respond` | Operator | İsteğe bağlı `definitionVersion` |

### Arayüz payı

Ölçüldü (2026-10-12): 199,8 KB gzip, bütçe 250 KB; pay kapanışta ölçülür. Faz 201'den sonra
203,7 KB (208 567 B, 2026-10-13, Faz 204 hariç) — taban birleştirilmiş `main`'de yeniden ölçülür.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Workflows/  IWorkflowDefinitionStore, WorkflowNodeReference, istek tipleri
src/Tracon.{Core,Workflows}/        store'lar, validator, ChildAgentInvoker, WorkflowAgentCache, Catalog, Runner
src/Tracon.{Sql.Shared,PostgreSql,SqlServer,Sqlite}/  SqlWorkflowDefinitionStore, *Queries, migration
src/Tracon.{AspNetCore,Testing.Contracts.Xunit,UI}/   uçlar, sözleşmeler, version-history, editör
```

---

## Hata Modları ve Testler

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Kayıt geçmişsiz commit eder; geri alma eski satırı değiştirir | Sözleşme (dört depo) | `WorkflowDefinitionStoreContract` |
| 2 | **Eşzamanlılık:** iki kayıt aynı numara | Sözleşme | aynı |
| 3 | Silme + oluşturma numarayı yeniden kullanır | Sözleşme | aynı |
| 4 | Payload `AgentVersion`'ı düşürür (K-580) | Sözleşme | aynı |
| 5 | Geri doldurma eksik; damga satır bulamaz | Migration (üç sağlayıcı) | mevcut migration testleri |
| 6 | **Başka kiracı:** geçmiş okunur/geri alınır | Sözleşme (`TenantIsolationContract`) + Fonksiyonel | `WorkflowVersionEndpointTests` |
| 7 | **İptal:** yeni üye iptali yok sayar | Sözleşme (`StoreCancellationContract`) | mevcut |
| 8 | **Alt sistem hatası:** geçmiş yazımı düşer, ana satır kalır | Sözleşme | `WorkflowDefinitionStoreContract` |
| 9 | Resume/respond güncel tanımı derler | Fonksiyonel (ayar değişir → resume → eski ayar) | `WorkflowResumePinTests` |
| 10 | Yapısal değişiklikten sonra bekleyen run sürmez | Fonksiyonel (aynı süreç) | `WorkflowStructuralChangeResumeTests` |
| 11 | Sürüm yokken güncele düşer; uyumsuz `definitionVersion` sessiz sürer | Fonksiyonel | `WorkflowResumePinTests` |
| 12 | Şema güncel sürümden okunur | Fonksiyonel | `WorkflowResumePinTests` |
| 13 | Damga ve graf iki okumadan gelir; tanım yoksa satır açılmaz | Fonksiyonel | `WorkflowRunStampTests` |
| 14 | Sabit yok sayılır (parametreli yol, önbellek anahtarı) | Birim + Fonksiyonel | `WorkflowNodeAgentVersionTests` |
| 15 | Sabit değişimi executor kimliğini değiştirir | Fonksiyonel (aynı süreç) | `WorkflowStructuralChangeResumeTests` |
| 16 | **Boş/aşırı girdi:** fonksiyon düğümünde, `0`/negatif, olmayan sürüm | Fonksiyonel (`400`, adlı hata) | `WorkflowNodeAgentVersionValidationTests` |
| 17 | Geri alma geçersiz sürümü yazar; denetime girmez; kod adında `409` yok | Fonksiyonel | `WorkflowRollbackTests` |
| 18 | Fark yanlış alan; editör sabiti düşürür; sabitli zincir `agentNames` | Vitest | `version-history`, `full-replace-editors`, `workflow-editor` testleri |
| 19 | Konsoldan geri alınan workflow eski içerikle koşmaz | E2E | mevcut workflow E2E |
| 20 | Üretilen istemciler alanı taşımaz | Paket | NSwag/TS kapıları |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Ayarlı workflow | Ayarı iki kez değiştir → Sürümler | Fark ayarı gösterir — 👤 |
| 2 | Aynı | v1'e geri al | Yeni sürüm, içerik v1 |
| 3 | Yavaş fonksiyonlu workflow | İptal et → düğüm ekle → resume | Eski zincirle sürer |
| 4 | Aynı run | `definitionVersion` = güncel | "structure differs" reddi |
| 5 | İki sürümlü agent | Düğüme `agentVersion: 1` → run | Çocuk satır `agentVersion: 1` |
| 6 | Workflow v3 | Sil → aynı adla oluştur | İlk sürüm 4 |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | K-126 açılsın mı? (**engelleyici**) | A: aç · B: açma; damga + uyuşmazlıkta red | **A** — dayanak okunamayan best-effort kayıttır; tanım artık talimata giren değer ve ayar taşır |
| 2 | Üçüncü taraf store | A: soyut üye (preview'da kırıcı) · B: varsayılan üye, yalnız güncel sürüm (`ISessionStore.cs:55`) · C: ayrı arayüz | **A** — geçmiş store'un işidir; B sabitlemeyi her düzenlemede düşürür |
| 3 | Resume varsayılanı | A: kaynak damgası + `definitionVersion` · B: güncel, uyuşmazlıkta red · C: yalnız damga | **A** — kabul budur; CHANGELOG yazar |
| 4 | Silme + oluşturmada kimlik | N: numara yeniden kullanılmaz · K: agent gibi, risk yazılır · H: run'a içerik özeti | **N** — vekil numara başka içeriği derletir (K-811); H sütun ister |
| 5 | Damganın yeri | A: `runs.agent_version` · B: yeni sütun | **A** — kök satırda `AgentName` zaten workflow adı; sürüm istatistiği workflow'u da sayar |
| 6 | Skill geçmişi ve alt agent sabiti bu fazda mı? | A: evet · B: iki ayrı aday (F-408, F-409) | **B** — skill K-082'yi (kullanıcı kararı) açar, üç tablo ve özet liste ister (20 × 256 KiB kaynak), K-860 grant'ına dokunmamalı. Alt sabit ayrı yüzeydir |

---

## Bitiş Ölçütleri (DoD)

- [x] İki `PUT` → `…/versions` iki anlık görüntü; `rollback {version:1}` → sürüm 3 = v1 (test 1, 17) — `WorkflowVersionEndpointTests.Two_saves_list_two_snapshots_…` · `A_rollback_writes_the_old_content_as_a_new_version_…` · `WorkflowRollbackTests` (altı case: geçersiz içerik `400`, olmayan sürüm `404`, hiçbiri yazılmaz ve denetlenmez) · örnek uygulamada ölçüldü (aşağıda)
- [x] Bekleyen run kaynak sürümle sürer, sürüm yoksa kapalı düşer (test 9-11) — `WorkflowResumePinTests` (6) · `WorkflowStructuralChangeResumeTests` (iki yarı) · `WorkflowResumeVersionHttpTests` (akış açılmadan `400`) · `WorkflowFunctionSettingsResumeTests`
- [x] Sabitli adımın çocuk satırı sabiti yazar; sabit değişimi checkpoint'i kırmaz (test 14-15) — `WorkflowAgentVersionPinTests.A_pinned_step_runs_the_pinned_text_while_the_agent_is_edited` (modele ulaşan talimat metniyle) · `…binds_the_workflows_parameters_still_runs_its_pinned_version` (parametreli yol) · `WorkflowStructuralChangeResumeTests.Re_pinning_an_agent_step_leaves_the_old_checkpoint_usable_for_either_version`
- [x] §200.4 sınıf taraması belgeye yazıldı — aşağıda
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban c8b9ea01` (kapanışta koşulur)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (sabit + resume), çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`: ✅ temiz
- [x] Manuel kabul case'leri `docs/manuel-test/15-WORKFLOWS.md` içine eklendi (`MT-WF-150`…`155`); otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi, `npm run build` + `check-links.mjs` temiz; `en.ts`/`tr.ts` eksiksiz; bundle payı yazıldı; `CHANGELOG.md` davranışı anar

## Kapanış kanıtı

### §200.4 sınıf taraması

`grep -rn "new WorkflowExecution" src/Tracon.Workflows` → `WorkflowRunner.cs`'te üç yer: `RunStreamingAsync` (yeni run: sürüm kayıt anında **okunan** tanımdan damgalanır), `PrepareResumeAsync` ve `PrepareResponseAsync` (`DefinitionVersion = istek ?? kaynak.AgentVersion`). Faz 197'nin kurtarma yolu (`WorkflowRecovery`, `WorkflowRunJobHandler.Continue`) resume'u çağırır: kaynak olarak zincirin son satırını verir, bu satır kendi damgasını taşır (`The_stamp_follows_a_chain_of_resumes`). Kuyruk yükü (`WorkflowRunJobPayload`) `definitionVersion` taşır ve geçersizse işi KAPALI düşürür. `new WorkflowExecution` tarandıktan sonra sürüm taşımayan üçüncü bir yer yoktur; `ReadRecordedParametersAsync` ve `RequireParticipantCoverageAsync` de aynı sürümü kullanır.

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

## Riskler

| Risk | Önlem |
|------|-------|
| Operatör düzeltmeyi bekleyen run'a uygulayamaz | `definitionVersion`; site yazar |
| Agent numarası silmede yeniden kullanılır; sabit bunu miras alır | Ayrı aday (F-409) |
| Faz 195/197/201 dosya çakışması | Önkoşul |

---

<!-- AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama`. Başlıkları SİLME. -->

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
| 7 | "Mevcut resume testleri" | 12 mevcut test eski kuralı (resume güncel olanı derler) kodluyordu ve bilerek değişti: `WorkflowStructuralChangeResumeTests` (8), `WorkflowFunctionSettingsTests`, `WorkflowFunctionTimeoutResumeTests`, `WorkflowParameterRespondTests` (2), fonksiyonel `WorkflowFunctionSettingsResumeTests` | Davranış değişimi bu fazın amacıdır (K-983); her biri iki yarıya bölündü: kaydedilen sürüm sürer, düzenlenen sürüm adı konunca eski davranış |
| 8 | `IWorkflowRunner` | Üye eklenmedi; `WorkflowResumeRequest.DefinitionVersion` ve `WorkflowRespondRequest.DefinitionVersion` yetti | Arayüz üyesi uygulayıcıyı kırardı (Açık Soru 2'nin ruhu); HTTP erken denetimi `IWorkflowDefinitionStore` ile uçta yapılır |
| 9 | Kuyruk yükü planda yoktu | `WorkflowRunJobPayload` `definitionVersion` taşır (`1`'den küçük ya da tam sayı olmayan → iş KAPALI düşer) | `resume`/`respond` kuyruğa alınabildiği için sürümü sessizce düşürmek K-983'ü kuyrukta çiğnerdi |
| 10 | — | `WorkflowEndpoints.ValidateDefinitionAsync` **tek** doğrulama yeri: `PUT` ve geri alma ikisi de onu çağırır | Geri alma içeriği "kaydedildiğinde geçerliydi"; sonradan kaldırılan fonksiyon veya silinip yeniden yaratılan agent'ın sürümü "güncel" tanımı derlenemez yapardı |

## Bu Fazda Verilen Kararlar

Dört kategoriye giren üç karar `docs/KARARLAR.md`'ye yazıldı (tam gerekçe `arsiv/KARARLAR-GECMISI.md`):

- **K-982** *(kalıcı-veri, public-api, geri-dönüşü-pahalı)* — **K-126 açıldı:** workflow tanımının sürüm geçmişi tutulur. `workflow_definition_versions`, yabancı anahtarsız, numara kimlik; kayıt iki satırı tek işlemde yazar; `IWorkflowDefinitionStore` soyut üye alır; migration PostgreSQL `0059` · SQL Server `0047` · SQLite `0046`.
- **K-983** *(public-api, geri-dönüşü-pahalı)* — Kök satır derlediği sürümü `runs.agent_version`'a yazar; resume/respond kaynak damgasını derler; `definitionVersion` başka sürümü seçer; olmayan sürüm kapalı düşer. K-900'ün "resume güncel ayarı okur" ve K-901'in "bekleyen run kırılır" cümleleri **değişti** (K-901 satırı yerinde düzeltildi).
- **K-984** *(public-api)* — `WorkflowNodeReference.AgentVersion`: yalnız `nodes`, yalnız veritabanı agent'ı, kayıtta ve derlemede doğrulanır, önbellek anahtarında ama kimlikte değil; K-901 konsolun `nodes` yazma kuralını "fonksiyon veya sabit" yapar.

Kategori dışı yerel kararlar (burada kalır, `K-*` açılmadı): silinmiş bir ad için mağaza `RollbackAsync`'i yeniden yaratır (sapma 1) · geri alma `PUT` ile aynı tek doğrulayıcıyı kullanır (sapma 10) · SQL kancaları yalnız sözleşme testinde (sapma 6).

## Gerçekleşen Public API

```csharp
// Tracon.Abstractions — IWorkflowDefinitionStore (soyut üyeler; üçüncü taraf uygulayıcıyı kırar)
ValueTask<WorkflowDefinition?> GetVersionAsync(string tenantId, string name, int version, CancellationToken cancellationToken = default);
ValueTask<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(string tenantId, string name, CancellationToken cancellationToken = default); // yeniden eskiye
ValueTask<WorkflowDefinition> RollbackAsync(string tenantId, string name, int version, CancellationToken cancellationToken = default);     // yok → TraconException

// Tracon.Abstractions
WorkflowNodeReference.AgentVersion { get; init; }            // int?
WorkflowResumeRequest.DefinitionVersion { get; init; }       // int?
WorkflowRespondRequest.DefinitionVersion { get; init; }      // int?

// Tracon.AspNetCore (HTTP DTO'ları; PublicAPI.Unshipped)
WorkflowResumeHttpRequest.DefinitionVersion · WorkflowRespondHttpRequest.DefinitionVersion   // int?
sealed record WorkflowRollbackRequest { required int Version }
sealed record WorkflowVersionDiffResponse { required WorkflowDefinition Left; required WorkflowDefinition Right }

// Tracon.Testing.Contracts.Xunit — WorkflowDefinitionStoreContract
13 yeni [Fact] + virtual SupportsRawHistory · ExecuteRawAsync · QualifiedWorkflowHistoryTable · RawNowExpression
```

HTTP: `GET /api/workflows/{name}/versions` (Reader, `workflows:read`) · `GET …/versions/{a:int}/diff/{b:int}` (Reader) · `POST …/rollback` (Admin, `workflows:admin`); `resume`/`respond` gövdesine isteğe bağlı `definitionVersion`. İç (public değil): `AgentVersionPin`, `WorkflowCatalog.FindAsync`/`CompileAsync`/`FoundWorkflow`, `CallableAgentResolver.ResolveAsync(ad, sürüm)`/`FindPinProblemAsync`, `ChildAgentInvoker`'ın `pinnedVersion` parametresi, `WorkflowAgentCache.Get(..., agentVersion)`.

Public tür sayısı: `Tracon.AspNetCore` 84 → 86 (`public-surface-baseline.txt` yenilendi). `Tracon.Abstractions` yeni tür eklemedi.

## Dosya Listesi (gerçekleşen)

```
src/Tracon.Abstractions/Workflows/     IWorkflowDefinitionStore · WorkflowNodeReference · IWorkflowRunner (WorkflowResumeRequest) · WorkflowPendingRequest (WorkflowRespondRequest) · WorkflowDefinition (yalnız belge)
src/Tracon.Core/                       Storage/InMemoryWorkflowStores.cs (WorkflowHistory) · Audit/AuditingWorkflowDefinitionStore.cs ·
                                       Workflows/AgentVersionPin.cs (YENİ) · Workflows/WorkflowDefinitionValidator.cs · Graph/CallableAgentResolver.cs · Graph/ChildAgentInvoker.cs ·
                                       Scheduling/WorkflowRunJobPayload.cs · Scheduling/WorkflowRunJobHandler.cs
src/Tracon.Workflows/Internal/         WorkflowCatalog · WorkflowRunner · WorkflowAgentCache · WorkflowDefinitionCompiler (+ README, XML dokümanı)
src/Tracon.Sql.Shared/                 Stores/SqlWorkflowDefinitionStore.cs · Internal/SqlQueriesBase.cs
src/Tracon.{PostgreSql,SqlServer,Sqlite}/  Internal/*Queries.cs · Migrations/0059_ · 0047_ · 0046_workflow_definition_versions.sql (YENİ ×3)
src/Tracon.AspNetCore/                 Endpoints/WorkflowEndpoints.cs · Contracts/WorkflowContracts.cs
src/Tracon.Testing.Contracts.Xunit/    Contracts/WorkflowDefinitionStoreContract.cs
src/Tracon.UI/frontend/                bkz. "Konsol" altında
scripts/applied-migrations.json · docs/openapi/tracon.json · src/Tracon.Client/Generated/* · packages/tracon-client/src/schema.ts (üretilen)
tests/                                 Workflows.UnitTests (7 dosya) · AspNetCore.FunctionalTests (5 dosya) · Core.UnitTests (3 dosya) · {Sqlite,PostgreSql,SqlServer}.IntegrationTests/Contracts · Sql.Shared.UnitTests/Baselines
```

## Süreç Ölçümü

> Kapanışta doldurulur; tablo olarak.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur; bulgu yoksa "🔴 ve 🟡 yok".

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
