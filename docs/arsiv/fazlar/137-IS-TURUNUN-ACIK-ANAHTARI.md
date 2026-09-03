# Faz 137 — İş Türünün Açık Anahtarı

> **Durum:** ✅ Tamamlandı (2026-09-03)
> **Kaynak:** Tüketici raporu AP-REQ-001 + yanıt dokümanı §1–§3 (ProdigyEnabler, 2026-09-03) · **F-183**
> **Önkoşul:** [Faz 136](136-PAKET-KIMLIGININ-TEKILLIGI.md) — tüketici bu fazı yeni ve
> benzersiz bir paket sürümü üzerinden ölçecek; kimlik kapısı önce girer
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`,
> `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.Client`, `.UI`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** **Gerekli — üç sağlayıcı**, numara uygulama anında alınır
> **Public API:** **Büyüyor ve kırıyor** — `JobKind` kalkar. `PublicAPI.Shipped.txt`
> dosyaları boş (`wc -l src/*/PublicAPI.Shipped.txt` ile doğrula), yani bugün ucuz; ilk
> `preview` yayınından sonra pahalı
> **Tüketici yüzeyi (gerçekleşen):** `docs-site/` — elle:
> `guides/write-your-own-job-handler`, `guides/background-work`, `concepts/runs`,
> `ui.md`, `capabilities.md`, `reference/configuration`, `getting-started/persistence`,
> `guides/observability`; üretilen: `api/`, `http-api/`, `openapi/agentprism.json`,
> `llms*.txt`, `AgentPrism.AgentMap.md`, 20 ekran görüntüsü · sevk edilen:
> `IJobHandler`/`IJobDispatcher`/`JobHandlerKeys` XML dokümanı,
> `samples/AgentPrism.Samples.CustomJobHandler`
> *(planın tahmin ettiği sayfa adları — `concepts/jobs`, `guides/write-your-own-store` — bu repoda yok)*
> **Manuel test alanı:** [`manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7173d66b:docs/arsiv/fazlar/137-IS-TURUNUN-ACIK-ANAHTARI.md
> ```
>
> Damıtıldı 2026-09-03 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`IJobHandler` bir genişleme noktası olarak sunuluyor ama **yeni bir iş türü eklemeye izin vermiyor.** `JobKind` kapalı bir enum'dur ve dokuz değerinin dokuzunun da yerleşik handler'ı vardır. Worker handler'ı `Kind` eşitliğiyle ve **kayıt sırasına göre** seçer; bu yüzden tüketicinin handler'ı ya hiç çalışmaz ya da yerleşik olanı gölgeler.

## Plandan Sapmalar

| # | Plan ne diyordu | Ne yapıldı | Gerekçe |
|---|---|---|---|
| 1 | "Yeni uç **yok**." Zamanlanabilir anahtar listesi arayüze `AgentPrismMetaResponse` üzerinden gelecekti (§137.4) | `GET /api/schedules/handler-keys` eklendi (Admin + `PlatformRead`); meta yanıtı **değişmedi** | `/api/meta` `AllowAnonymous`'tur ve kendi XML dokümanı "no secret, tenant data, agent name, or count information" taşımadığını yazar. Tüketicinin `contoso.gece-raporu` gibi anahtar adları deployment ayrıntısıdır ve o sözleşmeyi bozarak anonim çağrıya sızardı. Kullanıcıya soruldu, Admin-korumalı uç seçildi (K-665) |
| 2 | SQLite'ta sütun düşürmek için tablo yeniden kurulacaktı (`0006_sessions_tenant_key.sql` emsali) | `ALTER TABLE ... DROP COLUMN` kullanıldı | Ölçüldü: `jobs` ve `job_schedules`'in ikisi de `PRAGMA foreign_keys = ON` altında çocuk taşır. FK açıkken `DROP TABLE` örtük `DELETE FROM` yapar ve `ON DELETE CASCADE`'i tetikler — `jobs`'u yeniden kurmak `job_items`'ın TÜM satırlarını silerdi. Kaçış (`PRAGMA foreign_keys = OFF`) işlem içinde no-op'tur ve `MigrationRunner` her migration'ı işlem içinde koşar. 0006'nın tablosunun çocuğu yoktu (K-666) |
| 3 | Rezerve önek denetimi `JobHandlerRegistry` kurucusunda, host başlangıcında olacaktı | Rezerve önek ve biçim denetimi **`AddJobHandler` çağrısının kendisinde** atar; duplicate denetimi registry'de kalır | Argüman doğrulaması koleksiyona bakmaz, yani K-251'in "kurulum anındaki ön-kontrol yapma" kuralını ihlal etmez ve hatayı çağrı yerinde adlandırır. Duplicate ancak tüm kayıtlar toplandıktan sonra bilinebilir, o registry'de kaldı. İkisi de host'u açtırmaz (K-663) |
| 4 | `JobHandlerContract` handler'ın kendi `Kind`'ını kullanıyordu | Sözleşmeye `protected virtual string HandlerKey` eklendi (varsayılan `"contract.handler"`) | Handler artık anahtarını bildirmiyor. Sözleşme handler'ı doğrudan çağırdığı için değer dispatch'e hiç ulaşmaz; override yalnız kaydın gerçekçi görünmesi içindir |
| 5 | Metrik etiketi `agentprism.job.kind` idi | `agentprism.job.handler_key` oldu | Etiket adı sütunun ve alanın adını izler. 🚨 Fazın ilk hâli kardinalite riskini "yok" saymıştı — kayıtlı anahtar kümesi host başlangıcında sabitlenir, doğru. Ama **kayıtsız** anahtar yolu o kümenin dışındadır ve ham anahtarı etikete yazıyordu; bağımsız denetim bunu buldu (🟡 #5) ve o yol artık sabit `"unregistered"` etiketini yazar |

### Plan dışı düzeltilen kusurlar

| Kusur | Nasıl bulundu | Düzeltme |
|---|---|---|
| 🚨 SQL Server `LeaseJob`'ın `OUTPUT` listesi `JobColumns`'u **elle** tekrarlıyordu; sütun yeniden adlandırılınca bayat kaldı | SQL Server entegrasyon koşumu: `Invalid column name 'kind'` (26 test). PostgreSQL/SQLite snapshot'ları bu sorguyu içermiyor, derleyici SQL metnini görmüyor | Liste **türetildi**: `SqlQueriesBase.InsertedJobColumns = Qualify(JobColumns, "inserted.")`. Okuyucu ORDINAL eşlediği için sıra da garanti altına girdi. Hafıza: `sql-server-tuzaklari.md` |
| `MigrationCommandTests` sayı iddiaları alt dizge eşliyordu | Migration seti 29'dan 30'a çıkınca `"30 applied"` metni `"0 applied"` alt dizgesini içerdi ve **doğru davranışı** anlatan iki test kırmızıya döndü | Dört iddia da sayıyı **ayrıştırır** (`CountOf`, satır-çıpalı regex). Fazla alakasız, gizli bir kusurdu: her `0` ile biten sayı bu testleri yanlış yönde bozardı |
| `JobWorkerBackgroundService`'in `AmbientTenantScope.Begin` yazımı yardımcı metoda kaymıştı | `AmbientWriteSiteTests` taban çizgisi (MEMORY.md'nin `AsyncLocal` kuralının makine kapısı) | Yazım çağıran metoda (`ExecuteJobAsync`) geri taşındı ve **container çağrısından önceye** alındı — scoped bir bağımlılık kurulurken `ITenantContext` okuyabilir. Taban çizgisi büyütülmedi |

## Bu Fazda Verilen Kararlar

| No | Konu |
|---|---|
| **K-662** | `JobKind` kaldırıldı; işin kimliği tek bir dizge alandır, ikinci alan tutulmaz |
| **K-663** | Anahtar kayıtta verilir, handler scoped'tır, dispatch tam eşleşmedir; duplicate/rezerve/bozuk anahtar host'u açtırmaz |
| **K-664** | Kayıtsız anahtar fail-closed'dır; kararlı kod + redacted mesaj, ham anahtar yalnız log'da |
| **K-665** | `PUT /api/schedules/{name}` izin listesi; liste Admin ardında yayınlanır, `/api/meta` ile değil |
| **K-666** | SQLite'ta sütun yerinde düşürülür; tablo yeniden kurma FK cascade veri kaybı üretirdi |
| **K-667** | Aynı tipin aynı anahtarla ikinci kaydı no-op'tur; yalnız İKİ FARKLI tip çakışmadır |
| **K-668** | Handler kurucusu çözülemezse iş `HandlerActivationFailed` ile kapanır, yeniden denenmez |

## Bitiş Ölçütleri (DoD) — sonuçlar

| Ölçüt | Sonuç |
|---|---|
| Örnek **gerçek bir worker'da** çalışır, paket seviyesinde kanıtlanır | ✅ `dotnet test` paketlenmiş `0.0.0-preview.0.540`'a karşı 10/10; `The_sample_handler_runs_a_queued_job_in_a_real_worker` → `Completed`, `doneItems=2` |
| İki custom handler iki anahtarla doğru işi çalıştırır | ✅ `JobHandlerRegistry` tam ordinal eşleşme; `JobHandlerRegistryTests` (10 case) + sample |
| Kayıt sırası tersine çevrilince sonuç değişmez | ✅ `Registration_order_does_not_change_the_outcome` (kayıt `AddAgentPrism()`'den önce) |
| Duplicate anahtar host'u açtırmaz; hata anahtarı adlandırır | ✅ `Two_handlers_sharing_one_key_stop_the_host` — `InvalidOperationException`, mesaj anahtarı ve iki tipi içerir |
| `agentprism.` öneki reddedilir | ✅ `A_consumer_cannot_register_inside_the_reserved_namespace` |
| Kayıtsız anahtar `Failed`; `ErrorMessage` ham anahtarı taşımaz | ✅ `A_job_whose_key_nobody_registered_fails_without_leaking_the_key` — kod var, anahtar yok |
| Execution başına yeni instance; retry de yeni scope | ✅ `JobHandlerScopeTests` iki iş ve bir retry için farklı instance + farklı scoped bağımlılık ölçer (denetim 🔴 #2); `ServiceRegistrationSnapshotTests` dokuz handler'ın da `Scoped` olduğunu kilitler |
| `JobContext` `IServiceProvider` taşımaz | ✅ Tip değişmedi; XML dokümanı gerekçeyi yazar |
| Lane, `TargetName`, at-least-once değişmedi | ✅ Faz 120 ve 129 testleri (lane starvation, lane kapsamı, `JobHandlerContract`) yeşil |
| Migration üç sağlayıcıda dokuzu doğru eşler; satır korunur | ✅ `JobHandlerKeyMigrationTests` **üç sağlayıcıda** eski şemaya satır yazıp migration'ı koşar ve dokuz eşlemenin dokuzunu + satır sayılarını doğrular (denetim 🔴 #3). SQLite'ta `job_items` kaybı da ölçülür |
| `JobStoreContract` dört uygulamada aynı sonucu verir | ✅ Bellek + üç SQL sağlayıcı, 39/39. Üç yeni `handlerKey` süzgeç case'i dahil (denetim 🟡 #8) |
| OpenAPI ve üretilen istemci `handlerKey` taşır; drift kapısı temiz | ✅ `docs/openapi/agentprism.json`, `AgentPrismApiClient.g.cs`, `packages/agentprism-client/src/schema.ts` tazelendi (K-627'nin dört adımı) |
| Arayüz açılır listesi meta'dan gelir | ⚠️ **Sapma 1** — listeyi `GET /api/schedules/handler-keys` verir, meta değil. Davranış (dinamik liste) sağlandı; kaynak uç değişti |
| `en.ts`/`tr.ts` eksiksiz; bundle payı gzip KB ölçüldü | ✅ Üç anahtar iki dilde; **+122 B gzip** |
| `PUT /api/schedules/{name}` izinsiz anahtarı `400` ile reddeder | ✅ `SchedulableHandlerKeyTests` (9 case, denetim 🔴 #4) + gerçek koşum: `'contoso.nightly' cannot be scheduled over HTTP…` + `status=400` |
| Dört doğrulama kapısı sıfır uyarı | ✅ `kapi.py kapanis` — aşağıdaki kapanış koşumunda |
| `samples/AgentPrism.Api` ile gerçek `run` yapıldı | ✅ `agentprism.agent-batch` + `summarizer` → `Completed`, `doneItems=1`; süzgeç `?handlerKey=` doğru sonucu döndü; izinsiz anahtar `400` aldı (çıktılar Sapma tablosunun altında) |
| `secret` taraması boş | ✅ `kapi.py tarama` |
| Manuel kabul case'leri eklendi | ✅ MT-JOB-122…131 (10 case) `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` içine |
| `faz-denetim` koşuldu; 🔴 bulgu kalmadı | ✅ Denetim Bulguları bölümü |
| `docs-site/` güncellendi; `npm run check` temiz | ✅ Dört kapı yeşil; `check:links` 155 098 bağlantı, kırık yok |
| `docs/kesif/…-tuketici-gap-yaniti.md` AP-REQ-001 dolduruldu | ⚠️ O dosya bu repoda **yok** (`ls docs/kesif/` → AP-REQ-001 yanıt dokümanı hiç oluşturulmadı). Fazın kendi "Plandan Sapmalar" ve "Gerçekleşen Public API" bölümleri tüketici yanıtı için gereken tüm bilgiyi taşır |

### Gerçek koşum çıktıları (`samples/AgentPrism.Api`)

```
GET /api/schedules/handler-keys
["agentprism.agent-batch","agentprism.workflow","agentprism.eval",
 "agentprism.webhook-delivery","agentprism.retention","agentprism.agent-run",
 "agentprism.online-eval","agentprism.approval-resume","agentprism.run-continuation"]

PUT /api/schedules/faz137-bad  {"handlerKey":"contoso.nightly", …}
status=400  "'contoso.nightly' cannot be scheduled over HTTP. Add it to
             AgentPrismSchedulingOptions.HttpSchedulableHandlerKeys to allow it."

PUT /api/schedules/faz137-ok   {"handlerKey":"agentprism.agent-batch","targetName":"summarizer", …}
POST /api/schedules/faz137-ok/trigger  → job 01a0663d-…
GET  /api/jobs?handlerKey=agentprism.agent-batch
  {'handlerKey': 'agentprism.agent-batch', 'targetName': 'summarizer',
   'status': 'Completed', 'doneItems': 1, 'failedItems': 0}
GET  /api/jobs?handlerKey=agentprism.retention   → 0 satır (süzgeç gerçekten uygulanıyor)
```

## Denetim Bulguları

> `faz-denetim` bağımsız, taze bağlamlı bir agent olarak koştu.

Denetçi dört 🔴, altı 🟡 ve dört 🟢 bulgu üretti. **Dört 🔴'nün dördü de
kapandı**; altı 🟡'nin altısı da kapandı.

### 🔴 — hepsi kapandı

| # | Bulgu | Kapanış |
|---|---|---|
| 1 | **Aynı handler'ı iki kez kaydetmek host'u açtırmıyordu.** Kayıt `AddSingleton(new JobHandlerRegistration(...))` ile yazılıyor (`TryAdd` değil, K-251 gereği koleksiyona bakılamaz), yani `AddJobHandler<T>(key)`'i veya `AddAgentPrism()`'i iki kez çağırmak iki özdeş kayıt üretiyor ve registry **aynı tip için bile** çakışma atıyordu. `AddAgentPrism`'in kendi dokümanı `TryAdd` sözü veriyor. Bunu iddia eden test registry'yi hiç kurmuyordu — tiyatro | `JobHandlerRegistry.Create` artık **aynı tip + aynı anahtar** için no-op yapar; yalnız İKİ FARKLI tip çakışma sayılır. `JobHandlerRegistryTests` iki vakayı da kilitler (`AddAgentPrism()` iki kez → dokuz anahtar, hâlâ dokuz). **Mutasyonla kanıtlandı:** no-op dalı silinince iki test kırmızıya döndü |
| 2 | **Execution başına scope kanıtsızdı.** Planın `JobHandlerScopeTests`'i yazılmamıştı ve `TestJobHandlerHost.For(...)` handler'ı **instance** olarak kaydettiği için farkı ölçmesi yapısal olarak imkânsızdı; `ForType`/`With` yardımcıları ölü koddu | `JobHandlerScopeTests` (3 case) handler'ı **tip** olarak kaydeder ve iki iş + bir retry için farklı instance ve farklı scoped bağımlılık ölçer. **Mutasyonla kanıtlandı:** scope tek sefer açılacak biçimde değiştirilince iki test kırmızıya döndü |
| 3 | **Migration eşlemesi kanıtsızdı.** Üç `.sql` dosyasında dokuz satırlık `CASE` tablosu vardı ama hiçbir test veriye bakmıyordu; 7↔8 kayması (K-583'ün ayrı tuttuğu iki işlem) hiçbir kapıya takılmazdı | `JobHandlerKeyMigrationTests` **üç sağlayıcıda** ayrı ayrı: eski şemaya kadar migration'lar uygulanır, dokuz `kind` değeri için satır yazılır, sonra faz migration'ı koşar ve dokuz eşlemenin dokuzu ile satır sayıları doğrulanır. SQLite testi ayrıca `job_items`'ın **kaybolmadığını** ölçer (K-666'nın cascade tehlikesi). **Mutasyonla kanıtlandı:** PostgreSQL eşlemesinde 7↔8 çevrilince test kırmızıya döndü |
| 4 | **HTTP izin listesi kanıtsızdı — ve bu bir güvenlik sınırı.** `IsSchedulableOverHttp` gövdesi `return true` yapılsa test seti yeşil kalıyordu | `SchedulableHandlerKeyTests` (9 case): izinsiz anahtar `400` + zamanlama oluşmaz, yerleşik varsayılan kabul edilir, izin listesine eklenen tüketici anahtarı kabul edilir, dolu liste yerleşikleri **kapatır**, liste ucu **Admin** ister ve `PUT`'un kabul ettiğiyle **aynı** kümeyi döner, duplicate anahtar host'u açtırmaz. **Mutasyonla kanıtlandı:** guard her zaman `true` dönecek biçimde değiştirilince iki test kırmızıya döndü |

### 🟡 — hepsi kapandı

| # | Bulgu | Kapanış |
|---|---|---|
| 5 | Kayıtsız anahtar yolunda `agentprism.job.handler_key` etiketi **ham anahtarı** yazıyordu — sınırsız kardinalite, `lane`'in `MaxJobLaneCardinality` tavanının karşılığı yok | O yolda etiket sabit `"unregistered"`. Kayıtlı anahtar kümesi host başlangıcında sabitlendiği için diğer yolların tavana ihtiyacı yok; gerekçe kodda yazılı |
| 6 | `HttpSchedulableHandlerKeys` yerleşikleri **değiştiriyor**, doküman eklemeli okutuyordu — operatör tek anahtar eklerken dokuz yerleşiği sessizce kapatabilirdi | Kullanıcı kararı: **değiştirme anlamı korundu** (bir izin listesi yüzeyi daraltabilmelidir). XML dokümanı, `write-your-own-job-handler`, `background-work` ve `reference/configuration` bunu açıkça yazar ve "yerleşikleri de koru" reçetesini gösterir. `A_non_empty_list_REPLACES_the_built_in_default` davranışı kilitler |
| 7 | **Handler kurucusu çözülemezse iş sonsuza kadar yeniden lease ediliyordu.** Çözüm host başlangıcından execution'a taşınmıştı; `GetRequiredService` atarsa istisna `MarkRunningAsync`'ten önce kaçıyor, dış `catch` onu Warning olarak yutuyor ve attempt sınırı hiç uygulanmıyordu | Çözüm çağrısı `try/catch` içine alındı; iş yeni kararlı `JobErrorCodes.HandlerActivationFailed` koduyla `Failed` kapanır (yapılandırma hatası, retry düzeltemez). `JobHandlerScopeTests` bunu bağımlılığı kayıtsız bırakarak ölçer |
| 8 | `JobQuery.HandlerKey = ""` bellek ve SQL depolarında **farklı** sonuç veriyordu; sözleşme `HandlerKey`'i hiç denemiyordu | Bellek içi süzgeç `Lane`/`TenantId` ile aynı (`is { }`) hâle getirildi. `JobStoreContract`'a üç case eklendi (anahtarla süzme · eşleşmeyen anahtar · boş dizge ile `null` ayrımı) ve **dört uygulamada** da yeşil |
| 9 | Plandan sapmalar yazılmamıştı | "Plandan Sapmalar" bölümü beş sapmayı ve üç plan dışı kusuru gerekçesiyle taşır |
| 10 | Sevk edilen pakette bozuk XML cümlesi (`"whatever async setup its it needs"`) | Düzeltildi |

### 🟢 — aday kanalına

Dördü de kapsam dışı bırakıldı; ikisi bu fazda kendiliğinden kapandı
(`TestJobHandlerHost.ForType`/`.With` artık `JobHandlerScopeTests` tarafından
kullanılıyor; `JobStoreContract`'ın `HandlerKey` boşluğu 🟡 #8 ile kapandı).
Kalan ikisi (`handler-keys` adlı bir zamanlamanın literal segmentle çakışması ·
arayüzdeki `Select`'te `required` olmaması) gerçek bir kusur üretmiyor.

### Denetçinin doğruladığı, iddiaya güvenmediği kalemler

İmza-gövde kayması **yok** (sekiz üretim noktasının sekizi de anahtarı yazıyor;
`EXCLUDED.`/`excluded.` upsert listeleri ve reader ORDINAL'leri tutarlı) ·
`ErrorMessage` ham anahtarı kalıcılaştırmıyor · fazın 🔴 DoD kalemi (paketlenmiş
örnek) tiyatro değil · `Lane`/`TargetName`/at-least-once bozulmamış · `kind`
sütunu hiçbir sağlayıcıda index/CHECK/trigger'a bağlı değil (iki `DROP COLUMN`
bu yüzden güvenli) · anahtar deseni gerçekten 1–128 karakter ve `nvarchar(200)`
sütununa sığıyor.

## Sonraki Faza Devir Notu

- **Sıradaki faz: [Faz 138 — Ses Tanımının Sağlayıcı Üstverisi](138-SES-TANIMININ-SAGLAYICI-USTVERISI.md).**
  Bu fazla kesişimi yoktur; 138'in kendi "Bu Faza Başlarken" listesi yeterlidir.
- **🚨 `TryAddEnumerable` + "listede ara" deseni bir genişleme noktası DEĞİLDİR.**
  Bu faz o desenin bir örneğini kapattı ama sınıfını taramadı. `TryAddEnumerable`
  yalnız AYNI tipin iki kez eklenmesini engeller; FARKLI tiplerin aynı ayırt
  ediciyi (kind, name, scheme) paylaşmasını engellemez ve seçimi yapan
  `FirstOrDefault`/`LastOrDefault` kayıt sırasına bağlıdır. Tarama komutu
  `docs/hafiza/aspnetcore-di.md` içinde yazılı; bulunan her yer ayrı bir aday olur.
- **`IJobStore.EnqueueAsync` hâlâ public ve hâlâ kayıtsız anahtar kabul ediyor.**
  `IJobDispatcher` doğru yüzeydir ama store yüzeyi bilerek daraltılmadı (plan da
  daraltmıyordu) — bir tüketici store'a doğrudan yazarsa doğrulama atlanır ve iş
  yalnız worker'da `UnknownHandlerKey` ile kapanır. Fail-closed'dır, ama geç.
  Store'u daraltmak kırıcı bir değişikliktir ve ayrı bir karar ister.
- **`HttpSchedulableHandlerKeys` bir dizgedir, kayıt değildir.** Listeye kayıtlı
  OLMAYAN bir anahtar yazılabilir; zamanlama oluşur ve işi worker'da
  `UnknownHandlerKey` ile düşer. Kayıt kontrolünü de HTTP'ye taşımak
  `JobHandlerRegistry`'yi `AgentPrism.AspNetCore`'a açmayı gerektirir
  (`InternalsVisibleTo` zaten var) — yapılmadı çünkü izin listesi bir GÜVENLİK
  kapısıdır, bir varlık kapısı değil; ikisini birleştirmek yanlış hatayı verir.
- **Ölçüm için:** `AgentPrismSchedulingOptions.LaneByHandlerKey` artık dizge
  anahtarlıdır ve doğrulayıcı anahtarın biçimini de denetler. `LaneByKind`
  kullanan bir tüketici yapılandırması **derlenmez** — bu bilinçlidir ve upgrade
  adımı olarak tüketici yanıtına yazılmalıdır.
