> ## 📦 Arşiv bloğu — taşındı 2026-09-06
>
> **Bu yanıt tükenmiştir.** Anlattığı üç talebin (AP-REQ-001/002/003) üçü de
> plana dönüştü ve kapandı: **F-182** → Faz 136 · **F-183** → Faz 137 ·
> **F-184** → Faz 138. `ADAYLAR.md`'de açık bölümü kalmadı.
>
> Aynı tüketiciyle iki tur daha yapıldı ve bu yanıtın anlattığı yüzeyin çoğu
> değişti (altı faz + bir kusur turu). Güncel durum:
> [`2026-09-06-tuketici-turu-4-yaniti.md`](../../kesif/2026-09-06-tuketici-turu-4-yaniti.md).
>
> Aşağıdaki durum alanları taşındığı anda bayatladı. Bu dosya bundan sonra
> **yalnız `grep` hedefidir**.

---

# AgentPrism → ProdigyEnabler · Tüketici Turu 2 Rapor Yanıtı

> **Kimden:** AgentPrism geliştirme tarafı · **Tarih:** 2026-09-03
> **Neye yanıt:** Tüketici raporu AP-REQ-001/002/003 (ProdigyEnabler,
> 2026-09-03) · §9 şablonu
> **Kaynak kayıt:** [`docs/ADAYLAR.md`](../../ADAYLAR.md), "Ek (2026-09-03,
> tüketici turu 2)" — üç iddianın üçü de koda karşı doğrulandı

---

## Özet

Üç talebin üçü de kapatıldı. Sıra, sizin §10'unuzla aynı yürüdü: önce paket
kimliği, sonra job dispatch, sonra voice üstverisi.

| Talep | Karar | Faz | Kırıcı |
|---|---|---|---|
| AP-REQ-002 · Paket kimliği | Kabul edildi | 136 | Hayır |
| AP-REQ-001 · Custom job dispatch | Alternatif tasarımla kabul edildi | 137 | **Evet, geniş** |
| AP-REQ-003 · Voice üstverisi | Kabul edildi | 138 | Hayır |

Üç faz da bağımsız bir denetimden geçti (taze bağlamlı ayrı bir yargılayıcı,
yalnız DoD ve diff'e bakar). Denetim toplam **7 🔴** buldu; hepsi kendi fazında
kapandı. Bunlardan üçü fazın kendi iddiasını çürüttü — ayrıntılar ilgili
bölümlerde yazılıdır, gizlenmedi.

**Ölçüm sırasında bulunan, raporunuzda olmayan üç kusur da kapatıldı:**

1. Sevk ettiğimiz `IJobHandler` örneği gerçek bir worker'da **hiç
   çalışmıyordu** — genişleme noktasının kendi kanıtı boştu (AP-REQ-001).
2. `ListVoicesAsync` ses havuzunuzun yalnız **ilk 10'unu** döndürüyordu
   (AP-REQ-003) — bu sizi bugün etkiliyor olabilir.
3. `dotnet pack` dış kapsayıcı için deterministik değildir; "aynı commit ⇒ aynı
   SHA-256" bir doğrulama kuralı olarak kullanılamaz (AP-REQ-002).

### Bağımsız doğrulama — bu yanıt yazılmadan önce koşuldu

Aşağıdakiler faz dokümanlarının iddiası değil, bu yanıtı hazırlarken **yeniden
ölçülen** sonuçlardır (commit `b6711737`, macOS/arm64):

| Ölçüm | Sonuç |
|---|---|
| Kirli ağaçta `dotnet pack` | ❌ `AGENTPRISM0004` — paket üretilmedi |
| Kirli ağaçta `dotnet build` | ✅ `0 Error(s)` — kapı yalnız pack yolunda |
| Override, `dirty` sürümü verilmeden | ❌ `AGENTPRISM0006` |
| Override, `CI=true` ile | ❌ `AGENTPRISM0005` |
| Override + `MinVerVersionOverride=0.0.0-dirty.olcum` | ✅ `AgentPrism.Abstractions.0.0.0-dirty.olcum.nupkg` |
| `kapi.py yayin --kuru --surum 1.0.0-preview.1` | ✅ `EXIT=0` — 20 paket · manifest · `npm publish --dry-run` · 6 exact-sürüm sample · Native AOT smoke koştu ve geçti |
| Aynı commit'in **ikinci** yayın koşumu | ✅ Sahte çakışma yok — OPC parmak izi düzeltmesi gerçek koşulda doğrulandı |
| `package-manifest.json` | ✅ 20 paket · `1.0.0-preview.1` · commit `b6711737` · `dirty: false` · her paket için `sha256` **ve** `symbolsSha256` |

🚨 **Local feed'iniz için bir operasyon notu.** İlk yayın koşumu
`EXIT=1` verdi ve sebebi bir regresyon değildi: `artifacts/package/release/`
dizininde eski bir geliştirme hattı (`0.0.0-preview.0.548`) duruyordu ve yeni
kapı iki sürüm hattının bir arada bulunmasını **reddediyor**. Faz 136 sessiz
silmeyi kaldırdığı için bayat paketler artık kendiliğinden yok olmuyor; feed'i
temizlemek bilinçli bir operatör adımıdır. Bu, floating restore'un (`*-*`)
yanlış hattı seçmesini engelleyen kasıtlı davranıştır — sizin de local
feed'inizde iki hat bir arada durmamalıdır.

---

## AP-REQ-001 — Açık ve çakışmasız custom job dispatch

**Faz:** [137 — İş Türünün Açık Anahtarı](../fazlar/137-IS-TURUNUN-ACIK-ANAHTARI.md)

### Karar

**Alternatif tasarımla kabul edildi.** İddianız doğruydu ve ölçüm onu
raporunuzun yazdığından daha ağır buldu (aşağıda). Zorunlu davranış
sözleşmenizin 19 maddesinin 19'u karşılandı; iki maddede uygulama ayrıntısı
farklıdır ve ikisi de tek tek aşağıda yazılıdır.

Yanıt dokümanınız seçimi bize bıraktı: *"`HandlerKey` alanının bütün job'lar
için required olması ve built-in canonical key'leri de taşıması da kabul
edilir. Bu seçim AgentPrism'in iç tutarlılık kararıdır."* O seçim kullanıldı:
**`JobKind` enum'u tamamen kaldırıldı**, `HandlerKey` hem sınıflandırma hem
dispatch kimliğidir. `JobKind.Custom` + `HandlerKey` ikilisi kurulmadı.

### Gerekçe

İki alan tutmak kalıcı bir çift kimlik üretirdi: yerleşik işler için `Kind`,
custom işler için `HandlerKey` sorgulanır ve ikisi zamanla birbirinden kayar.
Yanıt dokümanınızın 7. ve 8. kuralları bunu zaten kabul ediyordu — panolar
yerleşikte `Kind`, custom'da `HandlerKey` kullanacaktı. Tek alan bu ayrımı
ortadan kaldırır; gruplama ad alanı önekiyle korunur (`agentprism.*` ↔
`prodigy.*`).

Maliyet ölçüldü ve küçük değildi: **132 dosya, 174 geçiş**, üç sağlayıcıda
sütun değişimi, sevk edilen sözleşme paketi, OpenAPI enum'u, üretilen istemci
ve arayüz. `preview` aşamasında olduğumuz ve kırıcı değişikliği açıkça kabul
ettiğiniz için bugün yapıldı; 1.0'dan sonra aynı iş çok daha pahalı olurdu.

**Raporunuzun görmediği bulgu — kanıtınız olduğundan güçlüydü.** Sevk
ettiğimiz `IJobHandler` örneği (`samples/AgentPrism.Samples.CustomJobHandler`)
`JobKind.AgentBatch` bildiriyor ve `AddAgentPrism()`'den **sonra** kaydoluyordu;
yerleşik `AgentBatchJobHandler` her zaman önce kayıtlı olduğu için bu örnek
**gerçek bir worker'da hiç çalışmıyordu**. Testi yalnız DI kaydını ölçüyordu,
dispatch'i hiç ölçmüyordu — yani genişleme noktasının kendi kanıtı boştu.
Faz 137 bu testi önce **kırmızı** hâle getirip açtı.

### Uygulanan public sözleşme

```csharp
// KALDIRILDI
// public enum JobKind { … }
// IJobHandler.Kind

public sealed record JobRecord   { public required string HandlerKey { get; init; } /* … */ }
public sealed record JobSchedule { public required string HandlerKey { get; init; } /* … */ }

public interface IJobHandler
{
    ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default);
}

public static class JobHandlerKeys
{
    public const string ReservedPrefix    = "agentprism.";
    public const string AgentBatch        = "agentprism.agent-batch";
    public const string Workflow          = "agentprism.workflow";
    public const string Eval              = "agentprism.eval";
    public const string WebhookDelivery   = "agentprism.webhook-delivery";
    public const string Retention         = "agentprism.retention";
    public const string AgentRun          = "agentprism.agent-run";
    public const string OnlineEval        = "agentprism.online-eval";
    public const string ApprovalResume    = "agentprism.approval-resume";
    public const string RunContinuation   = "agentprism.run-continuation";
}

public static class JobErrorCodes
{
    public const string UnknownHandlerKey        = "agentprism.job.unknown-handler-key";
    public const string HandlerActivationFailed  = "agentprism.job.handler-activation-failed";
}

public interface IJobDispatcher
{
    ValueTask<JobRecord> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default);
}

// AgentPrism.Core
public static IServiceCollection AddJobHandler<THandler>(this IServiceCollection services, string handlerKey)
    where THandler : class, IJobHandler;
```

Yerleşik anahtarlar raporunuzun §3.6 tablosunun **birebir aynısıdır**; migration
da o eşlemeyi kullanır.

### Zorunlu davranış sözleşmeniz — 19 maddenin tek tek durumu

| # | Garanti | Durum | Nasıl |
|---:|---|---|---|
| 1 | Enum'dan bağımsız kararlı handler anahtarı | ✅ | `JobRecord.HandlerKey` (required string) |
| 2 | Worker tam eşleşmeyle seçer | ✅ | `JobHandlerRegistry`, `FrozenDictionary` + `StringComparer.Ordinal` |
| 3 | Kayıt sırası sonucu değiştirmez | ✅ | `Registration_order_does_not_change_the_outcome` — kayıt `AddAgentPrism()` öncesine alınıp aynı sonuç ölçülür |
| 4 | Duplicate anahtar startup'ta durdurur | ⚠️ **Farklı ayrıntı** | İki **farklı tip** aynı anahtarda → host açılmaz. **Aynı** tipin aynı anahtarla ikinci kaydı no-op'tur (K-667); `AddX()` çağrısını iki kez yazmak hata değildir |
| 5 | Kayıtsız anahtar fail-closed, job `Failed` | ✅ | `A_job_whose_key_nobody_registered_fails_without_leaking_the_key` |
| 6 | Kararlı error code + redacted mesaj | ✅ | `agentprism.job.unknown-handler-key` + correlation id; **ham anahtar yalnız log'da** |
| 7 | Execution başına yeni DI scope | ✅ | `CreateAsyncScope()`, `ExecuteAsync` dönene kadar açık |
| 8 | Handler scope içinden çözülür | ✅ | Kayıt `Scoped`; `ServiceRegistrationSnapshotTests` dokuz handler için kilitler |
| 9 | Scoped repository/DbContext/service | ✅ | Constructor injection; `JobContext`'e `IServiceProvider` **eklenmedi** (talebiniz) |
| 10 | Built-in davranışlar geriye uyumlu | ✅ | Faz 120 (at-least-once) ve 129 (lane) testleri yeşil |
| 11 | Deterministik migration | ✅ | `JobHandlerKeyMigrationTests` üç sağlayıcıda dokuz eşlemeyi ve satır sayılarını doğrular |
| 12 | In-memory + PG + SQL Server + SQLite parity | ✅ | `JobStoreContract` 39/39, dört uygulamada |
| 13 | One-off enqueue ve schedule aynı anahtar | ✅ | `IJobDispatcher` ve `JobSchedule` aynı alanı taşır |
| 14 | Lane anahtardan ayrı | ✅ | `Lane` değişmedi; `LaneByKind` → `LaneByHandlerKey` |
| 15 | `TargetName` anahtardan ayrı | ✅ | Alan değişmedi |
| 16 | Retry/cancel/lease/progress/at-least-once değişmez | ✅ | Faz 120 sözleşmesi ve `JobHandlerContract` korundu |
| 17 | API, client, UI, log, metric, trace anahtarı gösterir | ⚠️ **Farklı ayrıntı** | Hepsi gösterir. Anahtar **listesi** `/api/meta` yerine `GET /api/schedules/handler-keys` (Admin) ile yayımlanır — gerekçe aşağıda |
| 18 | Yalnız kayıtlı anahtar için job yaratılabilir | ✅ | `IJobDispatcher.EnqueueAsync` kayıtsız anahtarı reddeder |
| 19 | Authorization ve tenant kuralları custom'da da geçerli | ✅ | Worker işin kendi tenant scope'unu açar; HTTP tarafında izin listesi |

### Bu rapordan farklı davranış

**1. `JobKind.Custom` + `HandlerKey` ikilisi kurulmadı; enum tamamen kalktı.**
Seçimi bize bıraktığınız madde buydu. Sonuç sizin için daha basittir: custom
bir job'ın anlamsız bir enum değeri taşıması gerekmez.

**2. Anahtar listesi `/api/meta` ile yayımlanmadı.** `/api/meta`
`AllowAnonymous`'tur ve kendi sözleşmesi "no secret, tenant data, agent name,
or count information" taşımadığını yazar. `prodigy.podcast-audio` gibi anahtar
adları deployment ayrıntısıdır; meta'ya koymak o sözleşmeyi bozup anonim
çağrıya sızdırırdı. Liste `GET /api/schedules/handler-keys` (Admin) ardındadır
(K-665).

**3. Aynı tipin aynı anahtarla ikinci kaydı hata değil, no-op'tur** (K-667).
Sizin şartınız "aynı anahtarın **iki handler** için kaydı"ydı; iki farklı tip
hâlâ host'u açtırmaz. Aynı kayıt uzantısını iki kez çağırmayı hata saymak
`TryAdd*` desenimizle çelişirdi.

**4. Genel amaçlı `POST /api/jobs` eklenmedi** — §2.2'de istemediğinizi
yazmıştınız. Custom job yaratma yolu .NET tarafındadır (`IJobDispatcher`).

**5. Rezerve önek ve biçim denetimi `AddJobHandler` çağrısında atar**, registry
kurucusunda değil (K-663). Hata çağrı yerini adlandırır; duplicate denetimi —
ancak tüm kayıtlar toplandıktan sonra bilinebildiği için — registry'de kalır.
İkisi de host'u açtırmaz.

**6. Metrik etiketi kardinalite için sertleştirildi.** Etiket
`agentprism.job.handler_key`'dir. Kayıtlı anahtar kümesi host başlangıcında
sabittir, ama **kayıtsız** anahtar yolu o kümenin dışındaydı ve ham anahtarı
etikete yazıyordu; bağımsız denetim bunu buldu. O yol artık sabit
`"unregistered"` yazar.

**7. Handler kurucusu çözülemezse** iş `agentprism.job.handler-activation-failed`
ile kapanır ve **yeniden denenmez** (K-668) — yapılandırma hatası retry ile
düzelmez.

### Breaking change

Evet, geniş. `preview` aşamasında kabul ettiğiniz kapsamdadır:

| Değişen | Eski | Yeni |
|---|---|---|
| İş türü alanı | `JobKind Kind` | `string HandlerKey` |
| Handler kimliği | `IJobHandler.Kind` özelliği | `AddJobHandler<T>("key")` parametresi |
| Handler yaşam süresi | `Singleton` | `Scoped` |
| Lane eşlemesi | `LaneByKind` | `LaneByHandlerKey` |
| HTTP/istemci alanı | `kind` (enum) | `handlerKey` (string) |
| Metrik etiketi | `agentprism.job.kind` | `agentprism.job.handler_key` |
| DB sütunu | `jobs.kind`, `job_schedules.kind` (`smallint`) | `handler_key` (metin) |

### Store migration davranışı

Üç sağlayıcıda birer migration (PostgreSQL `0043`, SQL Server `0030`, SQLite
`0030`): `handler_key` eklenir → `kind` değerinden §3.6 tablonuzla backfill
edilir → `NOT NULL` yapılır → `kind` düşürülür. `jobs` ve `job_schedules` için
ayrı ayrı. Veri kaybı yoktur; eski satır sessizce yanlış handler'a gitmez.

🚨 SQLite'ta plan tabloyu yeniden kurmayı öngörüyordu; ölçüm bunu **reddetti**.
`jobs` ve `job_schedules` `PRAGMA foreign_keys = ON` altında çocuk taşır ve
`DROP TABLE` örtük `DELETE FROM` yapıp `ON DELETE CASCADE`'i tetikler — `jobs`
yeniden kurulsaydı `job_items`'ın **tüm** satırları silinirdi. Sütun yerinde
düşürüldü (K-666); testi bu veri kaybını da ölçer.

### Hedef source commit

`931ec530` (ana uygulama) · `e05c15a3` (migration bütünlük manifesti)

### Hedef package version

`1.0.0-preview.1` ve sonrası — tag henüz atılmadı.

### Eklenen testler

- `JobHandlerRegistryTests` — 10 case: tam eşleşme, duplicate, rezerve önek, bozuk biçim
- `JobHandlerScopeTests` — iki iş ve bir retry için farklı instance + farklı scoped bağımlılık
- `ServiceRegistrationSnapshotTests` — dokuz handler'ın da `Scoped` olduğunu kilitler
- `JobHandlerKeyMigrationTests` — **üç sağlayıcıda** eski şemaya satır yazıp migration koşar; dokuz eşleme + satır sayısı + SQLite `job_items` korunumu
- `JobStoreContract` — 39/39, dört uygulamada; üç yeni `handlerKey` süzgeç case'i
- `SchedulableHandlerKeyTests` — 9 case, HTTP izin listesi
- `samples/AgentPrism.Samples.CustomJobHandler.Tests` — **paketlenmiş** pakete karşı 10/10: gerçek host'ta iş koşumu, ters kayıt sırası, kayıtsız anahtar sızıntısı, rezerve önek, duplicate host durdurma
- `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` — MT-JOB-122…131 (10 elle kabul case'i)

### Tüketici upgrade adımları

1. `IJobHandler` uygulamalarınızdan `Kind` özelliğini **silin** — arayüzde artık yok.
2. Kaydı anahtarla yapın: `services.AddJobHandler<PodcastAudioHandler>("prodigy.podcast-audio");`
   Anahtar `^[a-z0-9][a-z0-9._-]{0,127}$` desenine uymalı ve `agentprism.` ile **başlamamalıdır**.
3. Handler'larınız artık **`Scoped`**'tır. Scoped `DbContext`/repository'yi doğrudan
   constructor'dan alabilirsiniz. Handler'da süreç ömrü boyunca yaşayan alan
   tutuyorsanız o varsayım artık geçersizdir.
4. İş yaratmayı `IJobDispatcher.EnqueueAsync(new JobRequest { … HandlerKey = "prodigy.podcast-audio" … })`
   üzerinden yapın. `IJobStore.EnqueueAsync` duruyor ama anahtarın kayıtlı olduğunu doğrulamaz.
5. Yapılandırmada `AgentPrism:Scheduling:LaneByKind` → `LaneByHandlerKey`, anahtarlar artık dizge.
6. Bir custom anahtarı **HTTP'den** zamanlanabilir yapmak istiyorsanız
   `AgentPrism:Scheduling:HttpSchedulableHandlerKeys` listesine ekleyin —
   varsayılan yalnız yerleşik anahtarlardır ve izinsiz anahtar `400` alır.
7. Kendi istemci/pano kodunuzda `kind` → `handlerKey`; enum değil dizge.
8. Migration ilk açılışta otomatik koşar. Önce **yedek alın**; `kind` sütunu düşürülür.
9. Panolarda gruplama için anahtar önekini kullanın (`agentprism.` = AgentPrism'in kendi işleri).

---

## AP-REQ-002 — Paket kimliğinin tekilliği

**Faz:** [136 — Paket Kimliğinin Tekilliği](../fazlar/136-PAKET-KIMLIGININ-TEKILLIGI.md)

### Karar

Kabul edildi ve kapatıldı. İddianız doğruydu: aynı `<id, version>` çifti
farklı içerikli iki artifact adlandırabiliyordu.

### Gerekçe

Repro bu oturumda kilitlendi: temiz bir ağaçta `dotnet pack
src/AgentPrism.Abstractions` ile üretilen paket ve `src/Directory.Build.props`'a
commit'siz bir satır eklendikten sonra üretilen paket **aynı** sürümü ve
**aynı** `<repository commit="...">` iddiasını taşıdı, ama farklı SHA-256
değerleriyle. Kök neden üç katmandı: MinVer sürümü yalnız git yüksekliğinden
türetir (çalışma ağacının kirli olup olmadığı hiç girdi değildir),
`Directory.Build.targets`'te böyle bir kapının deseni zaten vardı (README
kontrolü) ama eşdeğeri yoktu, ve `scripts/kapi.py`'nin `_clean_stale_packages`'ı
paketlemeden önce aynı kimlikteki artifact'i sessizce siliyordu — sessiz
overwrite bir kaza değil, mevcut tasarımın kendisiydi.

### Uygulanan sözleşme

- **`AgentPrismValidateCleanWorkingTree`** (`Directory.Build.targets`,
  `BeforeTargets="GenerateNuspec"`): `git status --porcelain` boş değilse
  (untracked dosya dahil) pack `AGENTPRISM0004` ile durur. Yalnız pack
  yolundadır — `dotnet build`/`dotnet test` etkilenmez. Yerel deneme için
  `AgentPrismAllowDirtyPack=true` **ve** `dirty` taşıyan açık bir
  `MinVerVersionOverride` (`0.0.0-dirty.<ad>`) birlikte gerekir; CI'da
  (`CI=true`/`ContinuousIntegrationBuild=true`) bu override tamamen
  reddedilir (`AGENTPRISM0005`), sürüm verilmeden istenirse `AGENTPRISM0006`
  verilir.
- **`scripts/kapi.py yayin`**: pack'ten önce koşulsuz bir `git status
  --porcelain` denetimi yapar (kirli ağaçta hiçbir override yoktur — bir
  yayın provasının kanıt değeri kirli bir ağaçta yoktur). Paketler artık
  doğrudan `release_dir`'e değil, koşum başına benzersiz bir staging
  dizinine paketlenir; metaveri/K-008 doğrulaması geçtikten sonra promote
  edilir. `_clean_stale_packages`'ın sessiz silmesi kaldırıldı: aynı
  `<id, version>` çifti `release_dir`'de **farklı** bir SHA-256 ile zaten
  varsa hiçbir dosya promote edilmez ve koşum durur (mevcut artifact
  korunur); aynı SHA-256 ise deterministik no-op'tur.
- **Manifest**: her başarılı koşum `artifacts/package/release/package-manifest.json`
  yazar — sürüm, commit, `dirty: false`, ve her paket için id, dosya adı,
  SHA-256 (varsa `.snupkg` için de ayrıca).

### Bu rapordan farklı davranış

Raporunuz tanı kodu olarak `APREL001` öneriyordu; **kullanılmadı**. Repo
private olduğu için hiçbir tüketici bu tanıyı göremez — repodaki mevcut
konvansiyon `AGENTPRISM000N`'dir (bugün `0001`–`0003` kullanımda) ve bu faz
`0004`–`0006`'yı aldı. İkinci bir kod ailesi açmak yalnız kendi
konvansiyonumuzu bölerdi.

Kirli bir pack'in sürümü **otomatik türetilmez** — bir çözüm olarak
değerlendirilip reddedildi. Temiz sürüme bir sonek eklemek
(`1.0.0-preview.1.dirty.N`) SemVer'de temiz sürümden **sonra** sıralanır ve
`samples/` içindeki floating restore varsayılanının kirli bir artifact'i
temiz sürüme tercih etmesine yol açabilirdi. Sürüm insana yazdırılır
(`MinVerVersionOverride=0.0.0-dirty.<ad>`, her zaman temiz sürümün altında
sıralanır).

**Determinizm — kabul testiniz #7'nin dürüst cevabı.** `dotnet pack` dış
kapsayıcı için determinizm garanti **etmez**: NuGet.Packaging her koşumda OPC
core-properties parçasını (`package/services/metadata/core-properties/<32 hex>.psmdcp`)
rastgele bir GUID adıyla yazar ve `_rels/.rels` o adı gömer. Aynı commit'i iki
kez paketlemek `lib/`, `.nuspec` ve diğer her girdide **byte-byte aynı** ama ham
SHA-256'sı **farklı** iki dosya üretir. Bu, DoD doğrulaması sırasında ölçüldü
(20/20 pakette) ve ayrı bir düzeltme gerektirdi.

Sonuçları sizin için şunlardır:

- Çakışma kararı ham hash ile **verilmez**; iki rastgele-adlı OPC girdisini
  hariç tutan bir içerik parmak izi kullanılır. Aksi hâlde aynı commit üzerinde
  ikinci koşum her zaman sahte bir "farklı artifact" çakışması bildirirdi.
- Manifest'teki `sha256`, **gerçekten sevk edilen** dosyanın ham hash'idir;
  indirdiğiniz `.nupkg`'yi doğrulamak için doğru değerdir.
- 🚨 **"Aynı commit ⇒ aynı SHA-256" bir doğrulama kuralı olarak kullanılamaz.**
  Kullanılabilir kural şudur: bir `<id, version>` çifti tek bir yayınlanmış
  artifact adlandırır, o artifact'in hash'i manifest'tedir ve o kimlik bir daha
  farklı içerikle üretilemez.

### Breaking change

Yok. Public C# API'si bu fazda büyümedi; değişen yüzey yalnız MSBuild'dir
(yeni bir yayınlanmış paketin `.nuspec`'i etkilenmez, yalnız paketleme
sürecinin kendisi sertleşti).

### Store migration

Yok.

### Hedef commit

`2fd0c3ab57fba074f43012aa7b747e419f5194b9` (ana uygulama), takip eden düzeltme
`3928f50d` (OPC rastgeleliği kaynaklı yanlış-pozitif çakışma).

### Hedef paket sürümü

`1.0.0-preview.1` ve sonrası — henüz tag atılmadı (bkz. `docs/YAYIN-HAZIRLIK.md`
§4, kalan tek adım kullanıcının tag onayıdır).

### Eklenen testler

- `tests/AgentPrism.Package.Tests/PackCleanlinessGateTests.cs` — gerçek
  `dotnet pack`/`dotnet build` ile `AGENTPRISM0004`/`0005`/`0006` ve başarılı
  override yolu (paket sınırı, `RepositoryTreeGate` koleksiyonuyla izole).
- `scripts/kapi_test.py`, `YayinTestleri` — `_promote_staged_packages` (yeni
  paket promote edilir, aynı SHA-256 no-op, farklı SHA-256 koşumu durdurur ve
  mevcut artifact korunur), `_write_manifest`, ve `release_rehearsal`'ın erken
  ret davranışı (kirli ağaçta pack hiç denenmez; git yoksa atlanır).

### Tüketici upgrade adımları

Yok — bu faz tüketicinin bağımlılık grafiğini veya kod yüzeyini değiştirmez.
Etkisi yalnız AgentPrism'in kendi yayın sürecindedir: `1.0.0-preview.1`'den
itibaren her yayınlanan paket artık bu kapıdan geçmiş olur. Tüketici tarafında
tek pratik fayda: [`versioning.md`](https://agentprism.doayen.web.tr/reference/versioning/)
"Package identity" bölümü, NuGet.org'un kendi SHA-512 hash'ini kendi restore'unuzla
karşılaştırma adımını anlatır.

---

## AP-REQ-003 — Ses tanımının sağlayıcı üstverisi

**Faz:** [138 — Ses Tanımının Sağlayıcı Üstverisi](../fazlar/138-SES-TANIMININ-SAGLAYICI-USTVERISI.md)

### Karar

Kabul edildi ve kapatıldı. İddianız doğruydu: `VoiceDescriptor` yalnız üç alan
taşıyordu (`VoiceId`, `Name`, `Category`) ve ElevenLabs'in `labels` alanı hiç
parse edilmiyordu — kendi konsolumuz bile bu boşluk yüzünden elle bir dil
eşlemesi taşıyordu (`settings.tsx`/`voice.ts`).

### Gerekçe

Kanıt bu oturumda ölçüldü: `ElevenLabsVoice` (`ElevenLabsJson.cs`) yalnız
`voice_id`/`name`/`category` okuyordu, `ReadVoicesAsync` yalnız bu üçünü
eşliyordu. §5'in istediği minimum kümenin (`gender`/`language`/`accent`) hangi
alandan geldiği ölçüm gerektiriyordu: ElevenLabs'in yayınladığı OpenAPI
belgesi (`api.elevenlabs.io/openapi.json`) doğrudan indirilip
`components.schemas.VoiceResponseModel` incelendi. Sonuç raporun varsayımından
farklı çıktı — `labels` (`additionalProperties: string`) `gender`/`accent`/
`age`/`use_case`/`description` taşıyor ama **`language` taşımıyor**; dil ayrı
bir alanda, `verified_languages` (bir voice birden çok model için doğrulanmış
olabileceğinden dizi, her öge `VerifiedVoiceLanguageResponseModel.language`
zorunlu alanı) durur. Sorgu dokümantasyon prosasının ("filtering, based on the
voice's 'language' label") şemanın kendisiyle çeliştiği de bu turda görüldü —
gerçek karar örnek JSON ve şema tanımından alındı, prosadan değil.

İkinci bir ölçüm daha aynı dosyada çıktı: `ListVoicesAsync` `/v2/voices`'i hiç
sorgu dizesi eklemeden çağırıyordu; o uç `page_size` verilmezse **varsayılan
10** ses döndürür. Kod `MaxReportedVoices = 500` sınırını varsayıyordu ama
gerçekte hiçbir hesap 10'dan fazla ses hiç görmüyordu — raporunuzun kapsamı
dışında ama aynı dosyada karşılaşılan bir kusurdu, bu fazda birlikte kapatıldı.

### Uygulanan sözleşme

- `VoiceDescriptor.Attributes` — `IReadOnlyDictionary<string, string>`,
  varsayılan boş (mevcut kod değişmeden derlenir). `VoiceAttributeNames`
  sabitleri (`Gender`, `Language`, `Accent`, `Age`, `UseCase`) yazım hatasını
  önler; küme kapalı değildir, bilinmeyen güvenli bir etiket de kendi anahtarı
  altında taşınır (raporun §5'i bunu açıkça istedi).
- **Sınırlar** (`VoiceAttributeMapper`): en fazla 32 attribute, 64 karakter
  key, 256 karakter value; case-insensitive duplicate key tek kanonik
  (küçük harf, `_`→`-`) değere iner; yalnız `JsonValueKind.String` değer
  taşınır — sayı/nesne/dizi/null güvenle atlanır (`VoiceAttributeMapperTests`,
  15 birim testi).
- **`preview_url` ve API key hiçbir koşulda taşınmaz** — ilki mevcut, bilinçli
  bir karar (`SpeechModels.cs`); ikincisi `ElevenLabsVoice`'ta hiç alan olarak
  yok. `SecretLeakTests` tam alan taraması yapar.
- **`language` normalizasyonu**: `verified_languages` dizisindeki dağınık
  dil kodları küçük harfe indirgenip tekilleştirilir, sıralanır ve tek bir
  `Attributes["language"]` değerine virgülle birleştirilir (`"en,fr"`).
- **Sayfalama düzeltmesi**: `ListVoicesAsync` artık `page_size=100` ile
  başlar ve `has_more`/`next_page_token` bitene veya 500 sınırına ulaşana
  kadar sayfaları takip eder.
- **`list_voices` çıktısı** artık bilinen bir `gender` varsa gösterir; aracın
  iki Türkçe dizgesi ("Kullanilabilir ses yok." / "… ve … ses daha.")
  İngilizce'ye çevrildi ve `SourceLanguageTests`'in kelime listesi bu sınıfı
  yakalayacak biçimde genişletildi (taban çizgisi büyümedi — aynı turda
  ortaya çıkan beş test dosyasındaki benzer Türkçe test verisi de temizlendi).
- **Arayüz**: `settings.tsx`'teki ses seçici artık `language`/`gender`
  varsa `"Amy (en, female)"` biçiminde gösterir; elle dil eşlemesi bu fazda
  KALDIRILMADI (bkz. aşağı, kapsam bilinçli dar tutuldu).

### Bu rapordan farklı davranış

Raporun 9. maddesi `language`'ın `labels` altında olacağını varsayıyordu;
ölçüm bunun yanlış olduğunu gösterdi (`verified_languages` ayrı bir alan).
Sözleşme aynı kaldı (`Attributes["language"]`), yalnız eşleme kaynağı farklı.

Raporun önerdiği gibi typed `Gender`/`Language`/`Accent` özellikleri yerine
sınırlı bir sözlük seçildi — raporun kendisi de §5'te bunu tercih etmişti;
her yeni sağlayıcı etiketi (`use_case`, `age`, ileride başkaları) aksi hâlde
yeni bir public sözleşme değişikliği isterdi (K-669).

Konsolun (`settings.tsx`) elle dil eşlemesi bu fazda **kaldırılmadı**. Ölçüm
dilin gerçekten geldiğini gösterdi, ama yalnız ElevenLabs için ve yalnız
sağlayıcı bunu doğrularsa (`verified_languages` boş dönebilir); tüm
sağlayıcılar için garanti değildir, bu yüzden operatörün elle seçimi hâlâ tek
güvenilir yoldur. Seçici artık bu üstveriyle zenginleşir ama seçimin yerini
almaz — kapsam bilinçli dar tutuldu, kaldırma kararı ayrı bir tur gerektirir.

### Breaking change

Yok. `VoiceDescriptor.Attributes` varsayılanlı bir alan; var olan her
`ISpeechSynthesizer` uygulaması değişmeden derlenir (`Attributes` yalnız
`VoiceDescriptor`'ı üreten kodun doldurabileceği bir alan, uygulamanın
kendisinin değil).

### Store migration

Yok — `VoiceDescriptor` hiç kalıcılaştırılmaz.

### Hedef commit

`28ca187f` (ana uygulama), takip eden arşivleme/damıtma `3982ce8e`.

### Hedef paket sürümü

`1.0.0-preview.1` ve sonrası — henüz tag atılmadı (AP-REQ-002 ile aynı durum).

### Eklenen testler

- `tests/AgentPrism.Voice.UnitTests/VoiceAttributeMapperTests.cs` — 15 birim
  testi: bilinen etiketler, `null`/nesne/dizi/sayısal değer güvenli atlama,
  32/64/256 sınırları, case-insensitive + `_`→`-` kanonikleştirme,
  `verified_languages` birleştirme.
- `tests/AgentPrism.Voice.UnitTests/ElevenLabsSpeechClientTests.cs` — yeni:
  `labels`+`verified_languages` uçtan uca eşleme, boş `labels` → boş
  koleksiyon, `has_more`/`next_page_token` sayfalama takibi, 500 sınırında
  durma.
- `tests/AgentPrism.Voice.UnitTests/SecretLeakTests.cs` — yeni:
  `preview_url`'in `VoiceDescriptor`'a hiçbir koşulda ulaşmadığının tam alan
  taraması.
- `tests/AgentPrism.Voice.UnitTests/ListVoicesToolTests.cs` — yeni: İngilizce
  mesajlar, gender gösterimi.
- `tests/AgentPrism.Core.UnitTests/Architecture/SourceLanguageTests.cs` —
  kelime listesi genişletildi (`yok`, `ses`, `kullanilabilir`, `daha`); aynı
  turda beş `AgentPrism.AspNetCore.FunctionalTests` dosyasındaki benzer
  Türkçe test verisi (`"yok-boyle"`, `"merhaba"`, ...) İngilizce'ye çevrildi.
- `src/AgentPrism.UI/frontend/src/lib/voice.test.ts` — `voiceOptionMeta` için
  4 yeni test.

### 🚨 Aynı fazda kapatılan, sizi doğrudan etkileyen bir kusur

Üstveri işi sırasında ilgisiz ama ağır bir kusur ölçüldü ve kapatıldı:
`ListVoicesAsync` `/v2/voices` ucunu **hiç sorgu dizesi eklemeden** çağırıyordu.
Bu uç `page_size` verilmezse **varsayılan 10 ses** döndürür. Kod 500'lük bir
sınır varsayıyordu, ama hiçbir hesap gerçekte 10'dan fazla ses görmüyordu —
ElevenLabs'in premade kataloğu bile bunu aşar.

Sizin için anlamı: **`ListVoicesAsync` bugüne kadar ses havuzunuzun yalnız ilk
10'unu döndürüyordu.** Podcast speaker atamasını bu listeye dayandırıyorsanız
seçim havuzu sessizce kırpılmıştı. `page_size=100` + `has_more`/`next_page_token`
takibiyle düzeltildi; sınır artık gerçek 500'dür.

### Tüketici upgrade adımları

Kod tarafında yok — kaynak uyumlu bir büyüme. `AgentPrism.Voice`/
`AgentPrism.Abstractions`'ı güncelleyen bir tüketici `VoiceDescriptor.Attributes`'a
hemen erişebilir; erişmeyen kod değişmeden çalışmaya devam eder.

Davranış tarafında **bir şeyi doğrulayın**: yukarıdaki 10-ses kusuru yüzünden
ses listeniz bugüne kadar kırpılmış olabilir. Güncellemeden sonra
`ListVoicesAsync`'in döndürdüğü sayıyı bir kez ölçün; speaker havuzunuzu ilk
10 sesin özelliklerine göre elle ayarladıysanız o ayar artık gereksiz olabilir.
