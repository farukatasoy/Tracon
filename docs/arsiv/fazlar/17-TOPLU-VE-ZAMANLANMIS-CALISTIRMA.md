# Faz 17 — Toplu ve Zamanlanmış Çalıştırma

> **Durum:** ✅ Tamamlandı (2026-08-03)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-22**
> **Önkoşul:** Yok · Faz 9 önerilir (iş oluşturma Admin yetkisidir)
> **Devreden faz:** [Faz 16](16-WORKFLOWS-ARAYUZ.md) — workflow arayüzü ve human-in-the-loop
> **Sonraki bağımlı:** [Faz 18](18-DEGERLENDIRME.md) — eval bu kuyruğu kullanır
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0008 (planlanan sırada)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Faz 16'dan Devraldıkları

### 🚨 `RunStatus` artık beş değer taşır

```csharp
public enum RunStatus { Running = 0, Completed = 1, Failed = 2, Canceled = 3, AwaitingInput = 4 }
```

`AwaitingInput` **ne çalışıyor ne sonuçlanmış** bir workflow çalıştırmasıdır: graf
bir dış istek portuna ulaşmış, durumu kontrol noktasına yazılmış ve akış
kapanmıştır. Kuyruk tasarımı bunu hesaba katmalıdır:

| Kural | Neden |
|-------|-------|
| Zamanlanmış bir tetikleyici `AwaitingInput` bir çalıştırmayı **yeniden başlatmamalıdır** | O iş bitmedi; yeniden başlatmak insanın verdiği cevabı çöpe atar |
| "Süren iş sayısı" hesabı `Running` **ve** `AwaitingInput` satırlarını ayrı saymalıdır | İkisi farklı kaynaklar tüketir: biri CPU, diğeri yalnızca bir satır |
| Yeniden deneme mantığı `AwaitingInput`'u başarısızlık **saymamalıdır** | Hata değil, bekleyiştir |

`RunStatistics.AwaitingInputRuns` alanı zaten vardır; alt toplamlar `TotalRuns`
ile tutar.

### Yeni olay tipleri (append-only, 11–19)

`WorkflowStarted` (11) … `WorkflowRequest` (18), `RunAwaitingInput` (19).
Faz 17 yeni bir olay tipi eklerse **20'den** devam etmelidir.

### Bekleyen bir çalıştırmanın kontrol noktaları silinmez

`KeepCheckpointsAfterCompletion` kapalı olsa bile `AwaitingInput` bir
çalıştırmanın kontrol noktaları korunur — yanıt tam olarak onlardan devam eder.
Faz 25'in saklama politikası bu kuralı bozmamalıdır.

### 🚨 Bekleyen çalıştırmalar süresiz bekler

`TraconWorkflowOptions.RunTimeout` yalnızca **akış açıkken** çalışır. Bir
çalıştırma `AwaitingInput` olduktan sonra hiçbir zaman aşımı onu kapatmaz. Faz
17 bir "bekleyen işler" görünümü veya bir süre sınırı getirmek isteyebilir;
getirmezse Faz 25'in temizliği bunu ele almalıdır.

### Kalıcı executor kimliği artık garanti

Faz 15'in "uygulama yeniden başlatılırsa kontrol noktaları kullanılamaz" sınırı
**kalktı** (K-127). Bir iş kuyruğu bir workflow'u başlatıp süreç yeniden
başladıktan sonra sürdürebilir; bu ölçülerek doğrulandı.

---

## Amaç

Bir agent'ı bir veri kümesi üzerinde toplu çalıştırmak ve zamanlanmış (cron) tetiklemek. Beyin fırtınası belgesi bunun eval (F-14) ile **aynı altyapıyı paylaştığını** söylüyor; bu yüzden kuyruk **önce** yapılır ve eval onun üzerine kurulur. Bu, Tracon'i "istek geldiğinde çalışan" bir kütüphaneden "kendi kendine iş yapan" bir kontrol düzlemine dönüştürür.

## Plandan Sapmalar

Aşağıdakiler bu belgenin ilk taslağıyla gerçekleşen arasındaki farklardır.
Gerekçeleri `docs/KARARLAR.md` içinde K-134 … K-138 olarak numaralıdır.

| Sapma | Gerekçe |
|-------|---------|
| `WorkflowJobHandler` ayrı bir pakete değil, `Tracon.Core`'a kondu | `IWorkflowRunner` zaten `Tracon.Abstractions`'ta; Core zaten ona bağımlı. Somut uygulama yalnız `UseWorkflows()` çağrılırsa DI'a girer — `runner` nullable'dır (K-134) |
| `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi | Bölüm 17.5'in metni bu kısıtın var olduğunu söylüyordu ama 17.2'deki DDL örneğinde eksikti; migration 0008 metne göre tamamlandı (K-138) |
| İptal için ayrı bir `cancel_requested` sütunu **açılmadı** | `jobs.status` tek gerçek kaynak: CAS ile `Cancelled` yapılır, işçi ogeler arasında bunu tekrar okur (K-137) |
| Zamanlanmış bir işin kiracısı `AsyncLocal` tabanlı `AmbientTenantScope` ile taşınır | `ITenantContext` uygulamaları singleton'dır ve ne HTTP bağlamı ne sabit varsayılan "bu iş hangi kiracı için" sorusunu cevaplayabilir (K-136) |
| `IJobStore`'a doc taslağında olmayan `MarkRunningAsync` eklendi | `Leased` → `Running` geçişi açık bir CAS adımı gerektirir; `RenewLeaseAsync` durumu değiştirmez |
| `IJobStore.CompleteAsync` tek bir `JobCompletion` kaydı alır (4 ayrı parametre değil) | `IRunStore.CompleteRunAsync`/`RunCompletion` ile aynı sözleşme biçimi — belirlenimcilik ve tutarlılık için |

---

## Bu Fazda Verilecek Kararlar

1. **Kuyruk PostgreSQL üzerinde, `SKIP LOCKED` ile** — ek altyapı gerektirmez.
2. **Cron ayrıştırıcısı elle yazılır** — K-007; desteklenen alt küme yazılır.
3. **İşçi kapatılabilir** (`RunWorker = false`) — barındırma modeli tüketicinindir.
4. **`IJobHandler` genişleme noktasıdır** — K4; Faz 18 bunu kullanır.
5. **Bellek içi kuyruk desteklenir ama sınırları `/api/meta`'da bildirilir** — K-018.

---

## Çözülen Açık Sorular

1. **Toplu işin öğeleri paralel mi çalışsın?** **Sıralı** (iş başına), işler
   arası paralellik `MaxConcurrentJobs` ile. Uygulandığı gibi: `AgentBatchJobHandler`/
   `WorkflowJobHandler` `context.Items`'ı `foreach` ile sırayla işler.
2. **Başarısız öğede iş devam etsin mi?** **Evet.** `failed_items` sayılır;
   tüm öğeler başarısızsa iş `Failed` olur (bkz. `JobWorkerBackgroundService.ExecuteJobAsync`).
3. **`MaxItemsPerJob` 1000 uygun mu?** **1000**, değiştirilmedi. Hem zamanlama
   kaydında hem elle tetiklemede kontrol edilir (`SchedulingEndpoints`).
4. **Zamanlanmış iş hangi kiracıyla çalışır?** Zamanlama kaydındaki `tenant_id`.
   🚨 **Öneriden sapma:** doc taslağı "açık parametre" öneriyordu ama
   `IAgentCatalog.ResolveAsync`/`RunRecordingAgent`/`WorkflowRunner` kiracıyı
   zaten `ITenantContext` üzerinden **ambient** okuyor — parametre olarak
   geçirilecek bir çağrı zinciri yok. Çözüm `AmbientTenantScope` (AsyncLocal,
   K-136): `IHttpContextAccessor` ile aynı desen.

---

## Bitiş Ölçütleri (DoD)

- [x] Bir agent bir girdi kümesi üzerinde toplu çalıştırılıyor; her öge kendi
      `runs` satırını üretiyor. Gerçek çalıştırmada doğrulandı (örnek uygulama,
      `support` agent'ı, gerçek OpenAI modeli): 2 ögeli iş ~5 saniyede
      `Completed`, her ikisi de ayrı `run_id` ile `runs`'a bağlandı (gerçek
      `usage.totalTokens=527`). Mekanizma öge sayısından bağımsızdır; 20 öge
      aynı kod yolunu izler
- [x] Cron ile zamanlanmış iş tetikleniyor — **gerçek çıktı:** `48 8 * * *`
      cron'lu bir zamanlama saat `08:46:58`'de oluşturuldu, `nextRunAt =
      08:48:00` hesaplandı; hiçbir elle tetikleme yapılmadan saat `08:48:00`'de
      iş otomatik olarak `Running` durumuna geçti (`scheduledFor = 08:48:00`,
      `startedAt = 08:48:00.32`). Tamamlanınca `nextRunAt` bir sonraki güne
      (`2026-08-04T08:48:00Z`) ilerledi ve `lastRunAt` yazıldı
- [x] İki işçi aynı anda koşarken iş **tam bir kez** çalışıyor — gerçek
      PostgreSQL'e karşı kanıtlandı: `JobStoreConcurrencyTests` iki gerçek
      `IJobStore` örneğini 50 iş için yarıştırır, hiçbiri iki kez kiralanmaz
- [x] İşçi öldürülüp yeniden başlatıldığında yarım kalan iş devam ediyor —
      `JobStoreContract.Kira_suresi_dolunca_is_yeniden_kiralanabilir`
      (hem InMemory hem Postgres) kira süresi dolan bir işin `attempt`
      artırılarak yeniden kiralandığını doğrular
- [x] `RunWorker = false` ile işçi hiç başlamıyor — `JobWorkerBackgroundService.ExecuteAsync`
      `ModelProviderHealthBackgroundService` ile aynı desen: seçenek kapalıyken
      hiçbir zamanlayıcı kurulmadan hemen döner
- [x] Zamanlanmış işte kiracı doğru; sızıntı yok — `AmbientTenantScope`
      mekanizması `default` kiracısıyla gerçek çalıştırmada doğrulandı;
      `JobStoreContract`'in kiracı yalıtım testleri (`GetAsync_baska_kiracidan_null_doner`,
      `CancelAsync_baska_kiracinin_isini_etkilemez`) hem InMemory hem Postgres'te geçiyor
- [x] Dört doğrulama kapısı sıfır uyarı — `dotnet build`/`test`/`pack`/`format`
      hepsi temiz; `dotnet pack` 9 paket üretiyor (yeni paket yok)

---

## Sonraki Faza Devir Notu

- **Faz 18 (eval) bu kuyruğun ilk gerçek kullanıcısıdır.** `IJobHandler`
  sözleşmesi zaten eval'in ihtiyacını karşılayacak biçimde tasarlandı: bir iş
  N girdi çalıştırır (`JobContext.Items`), her biri için `ReportItemAsync` ile
  sonuç raporlar. Eval kendi `EvalJobHandler : IJobHandler`'ını (`Kind =>
  JobKind.Eval`) yazıp `services.AddJobHandler<EvalJobHandler>()` ile ekler —
  yeni bir depo, yeni bir tablo veya `JobWorkerBackgroundService`'te değişiklik
  **gerekmez**. `JobItemResult.RunId` alanı eval'in "bu değerlendirme hangi
  çalıştırmaya karşılık geliyor" sorusunu zaten cevaplar.
- 🚨 **`IJobHandler` isleyici, `IJobStore`'a dogrudan erismez** (bkz. `JobContext`).
  Eval kendi sonuçlarını (skor, geçti/kaldı) `job_items`'a değil kendi
  tablosuna yazmalıdır; `JobItemResult` yalnızca `Completed`/`Failed` +
  `RunId` + `Error` taşır, serbest bir skor alanı yoktur.
- `AmbientTenantScope.Begin(tenantId)` yalnızca `JobWorkerBackgroundService`
  içinde çağrılır. Eval kendi arka plan döngüsünü **yazmaz** — `IJobHandler`
  üzerinden bu kuyruğa katılır, dolayısıyla kiracı bağlamı otomatik gelir.
- Faz 21 (webhook) iş tamamlanma olayını yayınlayabilir.
- Faz 25 (saklama) tamamlanmış işleri ve öğelerini temizlemekle yükümlüdür.
  `jobs`/`job_items`'ın `runs`/`run_events` gibi büyüyeceği unutulmamalı.
