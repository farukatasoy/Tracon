# Faz 120 — `IJobHandler` Sözleşmesi: At-Least-Once Yazılı Hale Gelir

> **Durum:** 📋 Planlandı (2026-08-27)
> **Kaynak:** [YAYIN-HAZIRLIK.md](YAYIN-HAZIRLIK.md) — BL-041 (yayın denetimi bulgusu, aday listesinden değil)
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
| [`Scheduling/IJobHandler.cs`](../src/AgentPrism.Abstractions/Scheduling/IJobHandler.cs) | `ExecuteAsync`'in `<remarks>`'i yalnız "handler fırlatırsa retry edilir veya `Failed` işaretlenir" der. Retry'de `context.Items`'ın **tamamının** — zaten `Completed` olanlar dahil — geri geleceğini **söylemez** |
| [`Scheduling/IJobHandler.cs`](../src/AgentPrism.Abstractions/Scheduling/IJobHandler.cs) — `JobContext.Items` | "The job's items, by sequence number" — durum süzgeci uygulanmadığı belirtilmez |
| [`AgentBatchJobHandler.cs:35-39`](../src/AgentPrism.Core/Scheduling/AgentBatchJobHandler.cs) | Kural burada yorumla yaşıyor: *"Retry scenario: when the lease expires and the job is claimed again, items already processed successfully do not run again."* |
| [`WorkflowJobHandler.cs:41-46`](../src/AgentPrism.Core/Scheduling/WorkflowJobHandler.cs) | Aynı savunmacı kontrol, yorumsuz |
| [`EvalJobHandler.cs:150-155`](../src/AgentPrism.Core/Evaluation/EvalJobHandler.cs) | Aynı kontrol, aynı gerekçe yorumu |
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

## Bitiş Ölçütleri (DoD)

- [ ] `IJobHandler.ExecuteAsync` ve `JobContext.Items` dokümanı at-least-once'ı, süzülmemiş `Items`'ı ve handler'ın sorumluluğunu açıkça söyler
- [ ] `JobHandlerContract` var ve **üç yerleşik handler** tarafından türetiliyor
- [ ] En az bir **dış sample** (`PackageReference`, `ProjectReference` yok) contract'ı koşuyor
- [ ] `JobLeaseExpiryTests` dokümante edilen davranışın gerçekten var olduğunu **ölçüyor** (doküman runtime'dan güçlü garanti vermiyor)
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/` içine eklendi
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` job/scheduling rehberi at-least-once'ı anlatıyor
- [ ] [`YAYIN-HAZIRLIK.md`](YAYIN-HAZIRLIK.md) güncellendi (BL-041 kapandı)

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

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
