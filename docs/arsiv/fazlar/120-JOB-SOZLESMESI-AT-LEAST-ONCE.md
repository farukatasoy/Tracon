# Faz 120 — `IJobHandler` Sözleşmesi: At-Least-Once Yazılı Hale Gelir

> **Durum:** ✅ Tamamlandı (2026-08-27)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) — BL-041 (yayın denetimi bulgusu, aday listesinden değil)
> **Önkoşul:** Yok — [Faz 119](119-HATA-METNI-SIZINTISI.md) ile aynı dosyaya (`JobWorkerBackgroundService.cs`) dokunur; **119 önce kapanırsa çakışma olmaz**
> **Paketler:** `AgentPrism.Abstractions`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — yalnız `Testing.Contracts.Xunit` içinde yeni contract sınıfı
> **Tüketici yüzeyi:** `docs-site/` — job/scheduling rehberi · sevk edilen: `IJobHandler` ve `JobContext.Items`'ın XML dokümanı (asıl iş budur)
> **Manuel test alanı:** `docs/manuel-test/` — mevcut zamanlama/job ailesine eklenir

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula.

1. Bu doküman
2. Kararlar — yalnız bu kalemi grep'le:
   ```bash
   grep -n "K-138" docs/KARARLAR.md
   ```
   **K-138** (zamanlama benzersizlik kısıtı — job tekrarının bugün zaten
   kapatılmış olan **ayrı** bir yüzü; bu fazla karıştırılmamalı)
3. [`119-HATA-METNI-SIZINTISI.md`](119-HATA-METNI-SIZINTISI.md) — yalnız devir notu
   (aynı dosyaya dokunur)
4. Alan hafızası: bu faz **kod davranışı değiştirmez**, sözleşme yazar — alan
   hafızası okuması gerekmiyor

---

## Amaç

`IJobHandler` üçüncü tarafın yazacağı bir genişleme noktasıdır. Bugün onun
sözleşmesi, **bir handler'ın aynı iş için birden fazla kez çağrılabileceğini
hiç söylemiyor.** Yerleşik üç handler bunu savunmacı bir kontrolle kendi
içinde çözüyor; kural yalnız o üç dosyanın yorumunda yaşıyor.

- **BL-041** — job yürütmesinin **at-least-once** olduğu arayüz sözleşmesine
  yazılır ve bir contract testiyle kilitlenir.

### 🚨 Denetimin çerçevesi düzeltildi

Yayın denetimi bu kalemi "`IIdempotencyStore` — amacına rağmen job dispatch
loop'unda hiç çağrılmıyor" diye kaydetmişti. **Ölçüldü, çerçeve yanlış:**

| İddia | Ölçüm |
|---|---|
| `IIdempotencyStore` job yürütmesi için var | **Hayır.** Kendi XML dokümanı (`IIdempotencyStore.cs:6-14`) onu açıkça HTTP `Idempotency-Key` başlığı mekanizması olarak tanımlar ("exactly as the HTTP `Idempotency-Key` standard prescribes"). Tüketicileri `IdempotencyFilter` (HTTP) ve `InboundTriggerDispatcher`'dır |
| Job loop'unda tekrar koruması yok | **Var.** `JobItemStatus.Pending` kontrolü item başına dedup sağlar ve üç yerleşik handler'ın hepsinde koşar |

Yani job loop'una `IIdempotencyStore` bağlamak **gereksiz ikinci bir
mekanizmadır** ve bu faz onu yapmaz. Geriye kalan gerçek kusur tektir ve
dardır: **sözleşme bu davranışı söylemiyor.**

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`Scheduling/IJobHandler.cs`](../../../src/AgentPrism.Abstractions/Scheduling/IJobHandler.cs) | `ExecuteAsync`'in `<remarks>`'i yalnız "handler fırlatırsa retry edilir veya `Failed` işaretlenir" der. Retry'de `context.Items`'ın **tamamının** — zaten `Completed` olanlar dahil — geri geleceğini **söylemez** |
| [`Scheduling/IJobHandler.cs`](../../../src/AgentPrism.Abstractions/Scheduling/IJobHandler.cs) — `JobContext.Items` | "The job's items, by sequence number" — durum süzgeci uygulanmadığı belirtilmez |
| [`AgentBatchJobHandler.cs:35-39`](../../../src/AgentPrism.Core/Scheduling/AgentBatchJobHandler.cs) | Kural burada yorumla yaşıyor: *"Retry scenario: when the lease expires and the job is claimed again, items already processed successfully do not run again."* |
| [`WorkflowJobHandler.cs:41-46`](../../../src/AgentPrism.Core/Scheduling/WorkflowJobHandler.cs) | Aynı savunmacı kontrol, yorumsuz |
| [`EvalJobHandler.cs:150-155`](../../../src/AgentPrism.Core/Evaluation/EvalJobHandler.cs) | Aynı kontrol, aynı gerekçe yorumu |
| `grep -c "IdempotencyStore" JobWorkerBackgroundService.cs` | **0** — doğrulandı; ama yukarıdaki gerekçeyle bu bir kusur değildir |

> Kanıtlar 2026-08-27 tarihinde doğrulandı.

**Tüketici etkisi:** Arayüz dokümanını okuyup kendi `IJobHandler`'ını yazan bir
geliştirici, `context.Items` üzerinde durum kontrolü yapmaz — doküman ona böyle
bir kontrolün gerektiğini söylemez. Lease süresi dolduğunda veya süreç
çöktüğünde handler yeniden çağrılır ve **yan etki ikinci kez çalışır**
(e-posta ikinci kez gider, ödeme ikinci kez denenir).

---

## 120.1 — Sözleşme: at-least-once yazılı hale gelir

`IJobHandler.ExecuteAsync` ve `JobContext.Items` dokümanı üç şeyi açıkça söyler:

1. **Çağrı at-least-once'tır.** Lease süresi dolarsa, süreç çökerse veya
   handler fırlatırsa aynı job yeniden çağrılır.
2. **`Items` süzülmemiş gelir.** Zaten `Completed`/`Failed` olan item'lar da
   listede olur; handler `Status != Pending` olanı **atlamalıdır**.
3. **Yan etkisi olan handler idempotent olmalıdır** ya da bu kontrolü
   yapmalıdır.

Yerleşik handler'ların yorumundaki bilgi arayüze taşınır; yorumlar kalır ama
artık sözleşmenin tekrarı olurlar, tek kaynağı değil.

## 120.2 — Kural bir contract testiyle kilitlenir

Yazı yetmez — bu repoda sözleşme testi, üçüncü taraf implementasyonun
davranışını kanıtlayan mekanizmadır (`AgentSourceContract`,
`ModelProviderContract`, `RunJudgeContract` emsalleri).

`JobHandlerContract` eklenir. En az şunu kanıtlar:

- Handler, zaten `Completed` olan bir item taşıyan bir `JobContext` ile
  çağrıldığında o item'ı **yeniden işlemez**.
- İkinci çağrıda yalnız `Pending` item'lar işlenir.
- İptal (`IsCancelledAsync`) item'lar arasında gözlenir.

🚨 Contract'ın **gerçek bir consumer'ı olmalıdır** — yayın denetimi
(`nuget-danismani` Adım 5) "hiç consumer'ı olmayan contract" durumunu test
tiyatrosu olarak sayar. Üç yerleşik handler bu contract'ı türetir; en az biri
`samples/` altında dış bir sample olarak da koşar.

---

## Planlanan Public API

> Taslak imzalardır.

```csharp
// AgentPrism.Testing.Contracts.Xunit
namespace AgentPrism.Testing.Contracts.Scheduling;

/// <summary>Behavior tests for the <see cref="IJobHandler"/> contract.</summary>
public abstract class JobHandlerContract
{
    protected abstract IJobHandler Handler { get; }

    // Türeyen sınıf, kendi JobKind'ine uygun item üretir.
    protected abstract JobItemRecord CreateItem(int sequence, JobItemStatus status);

    [Fact] public Task Completed_items_are_not_processed_again();
    [Fact] public Task Only_pending_items_are_processed_on_a_retry();
    [Fact] public Task Cancellation_is_observed_between_items();
}
```

`AgentPrism.Abstractions` tarafında **yeni tip yoktur** — yalnız mevcut
`IJobHandler` ve `JobContext` üyelerinin XML dokümanı değişir. Doküman
değişikliği `PublicAPI.*.txt`'yi etkilemez.

### HTTP `endpoint`'leri

Yok. Bu faz çalışma anı davranışını **değiştirmez**.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
└── Scheduling/IJobHandler.cs                 (yalnız XML dokümanı)

src/AgentPrism.Testing.Contracts.Xunit/
├── Contracts/Scheduling/JobHandlerContract.cs (yeni)
└── PublicAPI.Unshipped.txt                    (yeni contract üyeleri)

tests/AgentPrism.Core.UnitTests/Contracts/
└── JobHandlerContractTests.cs                 (üç yerleşik handler türetir)

samples/
└── <mevcut bir sample'a veya yeniye> custom IJobHandler + contract koşumu
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Üçüncü taraf handler tamamlanmış item'ı yeniden işler | Sözleşme (`JobHandlerContract`) | üç yerleşik + bir dış sample'da koşar |
| Yerleşik handler'ın savunmacı kontrolü ileride silinir | Sözleşme | aynı contract — regresyon kapısı |
| Lease süresi dolunca job gerçekten yeniden çağrılıyor mu (sözleşmenin dayanağı) | Fonksiyonel (depo sınırı) | `JobLeaseExpiryTests` — **iddia edilen davranışın kendisi ölçülür** |
| İptal item'lar arasında gözlenmiyor | Sözleşme | `JobHandlerContract` |
| Boş `Items` listesi | Birim | `JobHandlerContract` |
| Başka kiracının job'u görünür | Sözleşme (`TenantIsolationContract`) | mevcut suite — regresyon |

🚨 Üçüncü satır atlanamaz: bu faz bir davranışı **dokümante ediyor**. O
davranışın gerçekten var olduğu (lease dolunca handler'ın tam `Items`
listesiyle yeniden çağrıldığı) fonksiyonel olarak ölçülmeden dokümante
edilirse, doküman runtime'dan güçlü bir garanti vermiş olur — yayın
danışmanının "en tehlikeli drift" dediği durum budur.

Beş soru: **iptal** (contract'ta) · **eşzamanlılık** (iki worker aynı job'u
lease edemez — `IJobStore` sözleşmesi, mevcut) · **boş/aşırı girdi** (boş
`Items`) · **başka kiracı** (regresyon) · **alt sistem hatası** (depo düşerse
job retry'e döner — mevcut davranış).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Çok item'lı bir batch job, kısa lease süresi | Job'u başlat, ilk item işlendikten sonra worker'ı öldür, yeniden başlat | İkinci koşumda **yalnız kalan item'lar** işlenir; ilk item'ın yan etkisi tekrarlamaz |
| 2 | Dokümanı izleyerek yazılmış dış bir `IJobHandler` (sample) | `JobHandlerContract` koşumu | Üç case de geçer |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Contract yalnız item bazlı mı, job bazlı tekrarı da kapsasın mı? | A: yalnız item · B: job bazlı senaryo da | **A** — item durumu bugünkü gerçek mekanizmadır; job bazlı dedup için ayrı, ölçülmüş bir ihtiyaç yok (YAGNI) |
| 2 | Dış sample yeni bir proje mi olsun, mevcut bir sample'a mı eklensin? | A: yeni `samples/AgentPrism.Samples.CustomJobHandler` · B: mevcut sample'a ek | **A** — diğer seam'lerin (`CustomTool`, `CustomRunJudge`, `CustomAgentSource`) hepsi ayrı sample; tutarlılık ve `PackageReference` ile koşum kolaylığı |

---

### `--site-gerekce-yazildi` gerekçesi

`dokuman-bakim.py --site-denetle`, `src/AgentPrism.Abstractions/`
değiştiğinde `concepts/` altında bir sayfanın da değişmesini bekleyen
kasıtlı-geniş `cekirdek-kavram` kuralını tetikledi. Bu sitede jobs/scheduling
konusu `concepts/` altında değil `guides/background-work.md`'de yaşıyor —
`concepts/` sekiz sayfadan hiçbiri (agents, evaluation, governance, index,
runs, sessions, tools, workflows) job kuyruğunu konu almıyor. Bu fazın
dokunduğu tek `concepts/`-benzeri davranış zaten `guides/background-work.md`
(at-least-once notu + yeni "Read next" bağlantısı) ve yeni
`guides/write-your-own-job-handler.md`'de güncellendi. Kural bu yüzden
`--site-gerekce-yazildi` ile geçilir.

## Bitiş Ölçütleri (DoD)

- [x] `IJobHandler.ExecuteAsync` ve `JobContext.Items` dokümanı at-least-once'ı, süzülmemiş `Items`'ı ve handler'ın sorumluluğunu açıkça söyler
- [x] `JobHandlerContract` var ve **üç yerleşik handler** tarafından türetiliyor
- [x] En az bir **dış sample** (`PackageReference`, `ProjectReference` yok) contract'ı koşuyor — `AgentPrism.Samples.CustomJobHandler(.Tests)`
- [x] `JobLeaseExpiryTests` dokümante edilen davranışın gerçekten var olduğunu **ölçüyor** (doküman runtime'dan güçlü garanti vermiyor)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 11df16c` yeşil (build, 3153 test, pack, format, docs-site dört alt kapısı)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. § *Gerçekleşen Public API* altındaki koşum notu
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` ✅
- [x] Manuel kabul case'leri `docs/manuel-test/` içine eklendi — `MT-JOB-103`, `MT-JOB-104` (`16-IS-KUYRUGU-VE-ZAMANLAMA.md`)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 3× 🟡 kapatıldı, 2× 🟢 not edildi (bkz. § *Denetim Bulguları*)
- [x] `docs-site/` job/scheduling rehberi at-least-once'ı anlatıyor — yeni `guides/write-your-own-job-handler.md` + `background-work.md` güncellemesi
- [x] [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) güncellendi (BL-041 kapandı)

### Doğrulama komutları

```bash
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests \
  --filter-method "*JobHandlerContract*"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| **Doküman runtime'dan güçlü garanti verir** — at-least-once yazılır ama ölçülmez | `JobLeaseExpiryTests` DoD'de ayrı satır; davranış dokümante edilmeden önce ölçülür |
| Contract'ın gerçek consumer'ı olmaz (test tiyatrosu) | DoD üç yerleşik handler + bir dış sample şartı koyar |
| Faz 119 ile aynı dosyada çakışma | 119 önce kapanır; bu fazın önkoşul satırı bunu söyler |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

- **`JobHandlerContract`'ın taslak imzası gerçekleşmedi, aynı ailedeki emsallerin (`AgentSourceContract`, `RunJudgeContract`) desenine geçti.** Plan yalnız `protected abstract IJobHandler Handler { get; }` + `CreateItem(...)` öngörüyordu. Gerçekte: `IAsyncLifetime` tabanlı, `CreateHandlerAsync()` (async kurulum — Eval kendi suite/case/run kaydını burada yapıyor), `JobId` (paylaşılan job kimliği; Eval kendi run kaydını buna bağlıyor) ve `virtual CreateJob(items)` (hedef ad/payload override'ı) eklendi. Sebep: `EvalJobHandler` bir suite + case + run kaydı olmadan çalışamıyor, bu kurulum async ve tek bir `Handler` property'sine sığmıyor.
- **`AgentPrism.Testing` paket referansı planın taslak sample kurulumunda yoktu, gerçekte de eklenmedi** — `NightlyReportJobHandler`in `AgentPrismTestHost` gibi bir gerçek-run altyapısına ihtiyacı yok (bkz. aşağıdaki JobKind bulgusu: gerçek bir host üzerinden dispatch edilemiyor, bu yüzden öyle bir test yanlış yolu doğrulardı).
- **`docs-site/guides/write-your-own-job-handler.md` planda yoktu, uygulama sırasında eklendi.** Diğer tüm extension seam'lerinin (`write-your-own-judge.md`, `-tool.md`, `-agent-source.md`, `-store.md`, `-error-classifier.md`) kendi rehber sayfası varken `IJobHandler`'ın yoktu; tutarlılık için eklendi.
- **Kod dışı, kapsam dışı bir bulgu doğrulandı ve belgelendi, düzeltilmedi:** `JobWorkerBackgroundService.ExecuteJobAsync`'in `handlers.FirstOrDefault(h => h.Kind == job.Kind)` dispatch'i, DI'nin `IEnumerable<IJobHandler>`'ı kayıt sırasına göre çözmesi yüzünden, `AddAgentPrism()` içinde ÖNCE kayıtlı yerleşik handler'ı her zaman kazandırıyor. Bugün **9/9 `JobKind` değerinin** zaten bir yerleşik handler'ı var, yani `AddJobHandler<T>()` ile (dokümante edilen tek örnekteki gibi `AddAgentPrism()`'den SONRA) eklenen bir özel handler mevcut hiçbir `Kind` için gerçek dünyada asla dispatch edilmiyor. Bu, fazın kapsamının çok üstünde bir mimari karar gerektiriyor (dispatch sırasını "son kazanır"a çevirmek mi, `JobKind`'i string'e açmak mı, yoksa mevcut hâliyle mi bırakmak). `docs/hafiza/aspnetcore-di.md`'ye tuzak notu yazıldı, `docs-site` rehberine `:::caution` kutusu eklendi, `AddJobHandler<T>()`'ın XML dokümanına tek cümlelik uyarı eklendi — kod DEĞİŞMEDİ. Önerilen sonraki adım: `aday-kesfi` ile bir F-NN adayı üretilmesi.
- **Kapsam dışı, tesadüfen karşılaşılan bir tooling kusuru düzeltildi:** `scripts/kapi.py`'nin kapanış zincirindeki `dotnet format AgentPrism.slnx --verify-no-changes --no-restore` komutu, tam solution test koşumunun (400s+) hemen ardından çalıştığında `samples/*.Tests` projelerinin (floating-version, local-feed paket tüketicisi altı örnek çifti) tiplerini bulamıyordu — üç bağımsız `kapi.py kapanis` koşumunda tekrarlanabilir şekilde ölçüldü, izole çalıştırıldığında hiç görülmedi. `--no-restore` kaldırıldı (restore hiçbir şey değişmemişse birkaç saniye); `scripts/kapi_test.py`'deki ilgili assertion güncellendi. Kök neden tam açıklanamadı (MSBuildWorkspace'in restore'suz workspace yüklemesiyle ilgili, muhtemelen tam test koşumunun bıraktığı bir durumla etkileşiyor); iz `docs/hafiza/build-ve-analyzer.md`'ye (mevcut MSBuildWorkspace/restore tuzakları ailesine) eklenebilir — bu fazda eklenmedi, düşük öncelik.

## Bu Fazda Verilen Kararlar

- **K-641** — `IJobHandler`'ın at-least-once yürütme sözleşmesi public XML dokümana yazılır ve `JobHandlerContract` ile kilitlenir; `IIdempotencyStore`'u job dispatch loop'una bağlamak reddedilir (bkz. `docs/KARARLAR.md`).

## Gerçekleşen Public API

`AgentPrism.Abstractions` tarafında **yeni tip yok** — yalnız `IJobHandler.ExecuteAsync`, `JobContext.Items` ve `AgentPrismServiceCollectionExtensions.AddJobHandler<T>()`'ın XML dokümanı değişti (public API yüzeyi etkilenmedi, `PublicAPI.*.txt` bu paket için değişmedi).

`AgentPrism.Testing.Contracts.Xunit` içinde yeni namespace ve tip (`PublicAPI.Unshipped.txt`'ye eklendi, `public-surface-baseline.txt` 45→46 güncellendi):

```csharp
namespace AgentPrism.Testing.Contracts.Scheduling;

public abstract class JobHandlerContract : IAsyncLifetime
{
    protected IJobHandler Handler { get; }                                     // get-only, InitializeAsync'te set edilir
    protected Guid JobId { get; }                                              // = Guid.NewGuid(), tüm test boyunca sabit

    protected abstract ValueTask<IJobHandler> CreateHandlerAsync();
    protected abstract JobItemRecord CreateItem(int sequence, JobItemStatus status);
    protected virtual JobRecord CreateJob(IReadOnlyList<JobItemRecord> items);  // varsayılan: TenantId="contract-tenant" vb.

    [Fact] public Task Completed_items_are_not_processed_again();
    [Fact] public Task Only_pending_items_are_processed_on_a_retry();
    [Fact] public Task Cancellation_is_observed_between_items();
}
```

`AgentPrism.Testing.Contracts.ContractCoverage.SchedulingContracts` sabiti eklendi (`"AgentPrism.Testing.Contracts.Scheduling"`).

**`samples/AgentPrism.Api` koşum notu:** Uygulama `dotnet run --no-build -c Release` ile PostgreSQL bağlantısıyla (mevcut `user-secrets`) başlatıldı, `AgentPrism__Scheduling__PollInterval=00:00:02`/`LeaseDuration=00:00:10` ile. Uygulama hatasız açıldı, `GET /agentprism/api/agents` yetkisiz istekte `401` döndü (auth doğru çalışıyor, regresyon yok). Tam canlı "worker öldür, yeniden başlat, yalnız kalan item işlenir" senaryosu (MT-JOB-103) yönetim API kimlik doğrulaması ve gerçek bir agent tanımı gerektirir; bu oturumda koşulmadı — bu davranış zaten `JobLeaseExpiryTests` ile `InMemoryJobStore` sınırında fonksiyonel olarak ölçüldü (bkz. DoD). MT-JOB-103 bir sonraki `manuel-test-kosumu` turunda 👤 gerektirir olarak işaretlenmiştir.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Scheduling/IJobHandler.cs         (yalnız XML dokümanı)
src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs  (yalnız AddJobHandler<T>()'ın XML dokümanı)

src/AgentPrism.Testing.Contracts.Xunit/
├── Contracts/Scheduling/JobHandlerContract.cs                (yeni)
├── ContractCoverage.cs                                       (SchedulingContracts sabiti)
└── PublicAPI.Unshipped.txt

tests/AgentPrism.Core.UnitTests/
├── Contracts/JobHandlerContractTests.cs                      (üç yerleşik handler + coverage testi)
├── Scheduling/JobLeaseExpiryTests.cs                         (yeni — fonksiyonel, depo sınırı)
└── Architecture/public-surface-baseline.txt                  (45→46)

samples/AgentPrism.Samples.CustomJobHandler/
├── NightlyReportJobHandler.cs
├── NightlyReportJobHandlerRegistrationExtensions.cs
└── AgentPrism.Samples.CustomJobHandler.csproj

samples/AgentPrism.Samples.CustomJobHandler.Tests/
├── NightlyReportJobHandlerContractTests.cs
├── NightlyReportJobHandlerRegistrationTests.cs
└── AgentPrism.Samples.CustomJobHandler.Tests.csproj

AgentPrism.slnx                                                (iki yeni sample proje girdisi)

docs-site/src/content/docs/
├── guides/write-your-own-job-handler.md                       (yeni)
├── guides/background-work.md                                  (at-least-once notu + Read next)
├── capabilities.md                                             (Custom jobs satırı)
docs-site/src/sidebar.mjs                                       (yeni rehber girdisi)
docs-site/public/llms.txt, llms-full.txt                        (üretilmiş)

docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md                  (MT-JOB-103, MT-JOB-104)
docs/manuel-test/00-INDEKS.md                                   (satır 16 güncellendi)
docs/YAYIN-HAZIRLIK.md                                          (BL-041/RK-010 kapandı, KG-015)
docs/KARARLAR.md                                                (K-641)
docs/hafiza/aspnetcore-di.md                                    (FirstOrDefault dispatch tuzağı)

scripts/kapi.py, scripts/kapi_test.py                           (kapsam dışı: dotnet format --no-restore düzeltmesi)
```

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, `11df16c` tabanına karşı) **🔴 bulgu üretmedi**. Üç 🟡 ve iki 🟢 bulgu:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `AddJobHandler<T>()`'ın kendi XML dokümanı (paketle giden, IntelliSense'te görünen birincil kanal) dispatch-order sınırlamasından hiç bahsetmiyordu — yalnız `docs-site`'ta vardı | **Düzeltildi** — `<remarks>`'e tek cümlelik uyarı eklendi |
| 2 | 🟡 | `background-work.md`'nin "Read next" listesinden gerekçesiz olarak `Production deployment` bağlantısı kaldırılmıştı (4→3 link sınırı için), oysa sayfa konuyla ilgiliydi | **Düzeltildi** — `Inbound triggers` yerine değiştirildi, `Production deployment` geri eklendi |
| 3 | 🟡 | Kapsam dışı mimari bulgu (`FirstOrDefault` dispatch + kapalı `JobKind`) yalnız `docs-site` caution kutusunda yaşıyordu, `docs/hafiza/`'da veya aday listesinde kayıtlı değildi | **Kısmen düzeltildi** — `docs/hafiza/aspnetcore-di.md`'ye tuzak notu eklendi. `docs/ADAYLAR.md`'ye F-NN olarak eklenmedi: o dosyanın kendi süreci (`aday-kesfi` skill'i, sekiz mercekli yargı) atlanmadan mekanik ekleme yapmak dosyanın kendi yönetişimini ihlal eder — bu fazın "Sonraki Faza Devir Notu"na taşındı |
| 4 | 🟢 | Rehberin önerdiği "AddAgentPrism()'den önce kaydet" çözümü hiçbir testte kanıtlanmıyor | Devredilmedi — rehber zaten kendi iddiasını "until this ordering limitation is resolved" diyerek sınırlıyor, yanıltıcı değil |
| 5 | 🟢 | `dotnet format --no-restore` MSBuildWorkspace tuzağı yalnız `kapi.py` kod yorumunda yaşıyor, `docs/hafiza/build-ve-analyzer.md`'ye taşınabilirdi | Devredilmedi — küçük, düşük risk, kod yorumu yeterince açıklayıcı |

Denetim ayrıca bağımsız olarak doğruladı: `dotnet build` (0 uyarı), yeni testlerin tamamı (`JobHandlerContract*` 10/10, `*LeaseExpiry*` 1/1, dış sample 5/5), `kapi_test.py` (38/38), `docs-site` dört kapısının tamamı, `secret` taraması (0 sonuç). Test tiyatrosu yok (3.2 temiz), test seviyesi doğru (3.3 temiz — contract testleri handler sınırında, `JobLeaseExpiryTests` depo sınırında), imza-gövde kayması yok (3.5 temiz), repo kuralları temiz (3.7).

## Sonraki Faza Devir Notu

- **BL-041 kapandı; YAYIN-HAZIRLIK.md'deki 4 preview-blocker kusur sınıfının tamamı artık kapalı.** Kalan iş kusur değil, karar: §7 (UR-003, public API freeze taraması) ve §8 (OP-001..009, NuGet.org operasyon kararları). Sıradaki adım büyük olasılıkla **`nuget-danismani`'nin yeni bir turu** — nihai "yayınlanabilir mi" kararını bu iki karar grubu üzerinden verecek.
- **🚨 Yeni bir "F-NN aday üret" turu (`aday-kesfi`) çalıştırılırsa şu bulguyu girdiye ekle:** `IJobHandler`/`AddJobHandler<T>()` genişleme noktası, mevcut 9 `JobKind` değerinin hiçbiri için üçüncü tarafça gerçekten kullanılamıyor — `JobWorkerBackgroundService`'in `FirstOrDefault(h => h.Kind == job.Kind)` dispatch'i DI kayıt sırasına bağlı ve yerleşik handler'lar her zaman önce kayıtlı. Tam kanıt: `docs/hafiza/aspnetcore-di.md` (Faz 120 notu), `docs-site/guides/write-your-own-job-handler.md`'deki caution kutusu. Olası çözüm yönleri (hiçbiri bu fazda değerlendirilmedi): dispatch sırasını "son kazanır"a çevirmek, `JobKind`'i açık bir string'e dönüştürmek, veya mevcut sınırı kalıcı olarak kabul edip yalnız AgentPrism'in kendi iç modüllerinin (Eval gibi) kullanacağı bir seam olarak yeniden çerçevelemek.
- Bu fazdan sonra planlanmış bir F-NN yok — `docs/YOL-HARITASI.md`'de 120 son kalemdir. Sıradaki iş kullanıcı kararına bağlı: yeni bir `aday-kesfi` turu mu, yoksa doğrudan `nuget-danismani`'nin yayın kararı turu mu.
