# Faz 200 — Workflow Sürüm Geçmişi ve Sürüm Sabitleme

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** onaylanmadı — plan 2026-10-12'de bakımcı talebiyle yazıldı; engelleyici kararlar § Açık Sorular'da öneriyle bekler, uygulama onaydan önce başlamaz
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

Ölçüldü (2026-10-12): 199,8 KB gzip, bütçe 250 KB; pay kapanışta ölçülür.

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

- [ ] İki `PUT` → `…/versions` iki anlık görüntü; `rollback {version:1}` → sürüm 3 = v1 (test 1, 17)
- [ ] Bekleyen run kaynak sürümle sürer, sürüm yoksa kapalı düşer (test 9-11)
- [ ] Sabitli adımın çocuk satırı sabiti yazar; sabit değişimi checkpoint'i kırmaz (test 14-15)
- [ ] §200.4 sınıf taraması belgeye yazıldı
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (sabit + resume), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/15-WORKFLOWS.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi, `npm run build` + `check-links.mjs` temiz; `en.ts`/`tr.ts` eksiksiz; bundle payı yazıldı; `CHANGELOG.md` davranışı anar

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

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen: K-126'nın açılması (`kalıcı-veri`), resume'un kaynak sürümle sürmesi (`public-api`).

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

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
