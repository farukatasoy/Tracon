# Faz 157 — Sınırlı Yük ve İki Process Arıza Kanıtı

> **Durum:** ✅ Tamamlandı (2026-09-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-217**
> **Önkoşul:** Yok. [Faz 116](116-PERFORMANS-TAHSIS-KAPISI.md) tahsis kapısını ve bench projesini kurdu; kapalıdır
> **Paketler:** Yok — bu faz **sevk edilen hiçbir pakete dokunmaz**. Yalnız `bench/` ve `tests/`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/production.md` (arıza davranışı ve dağıtım örnekleri) · sevk edilen: Yok
> **Manuel test alanı:** [`docs/manuel-test/21-DAYANIKLILIK-VE-IPTAL.md`](../../manuel-test/21-DAYANIKLILIK-VE-IPTAL.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show c6a293eb:docs/arsiv/fazlar/157-SINIRLI-YUK-VE-IKI-PROCESS-ARIZA-KANITI.md
> ```
>
> Damıtıldı 2026-09-08 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün ölçtüğümüz şey **tahsis**tir, işletim değil. "İki process çalışırken biri ölürse ne olur" sorusunun koşulan bir cevabı yoktur. Bu faz o cevabı üretir — ve bunu **çok node desteği vaat etmeden** yapar: amaç mevcut lease ve reconciliation davranışının ne yaptığını kanıtlamaktır, yeni bir garanti kurmak değil.

## Bitiş Ölçütleri (DoD)

- [x] İki process senaryosunda öldürülen worker'ın işi devralınır ve **eşzamanlı çift yürütme olmaz**; çıktı belgeye yazıldı (§ Plandan Sapmalar — "bir kez yürütülür" ifadesi ölçülen sözleşmeye göre düzeltildi: `IJobHandler` **at-least-once**'tır)
- [x] Lease süresi dolmadan devralma **olmadığı** ölçüldü (`A_dead_workers_job_is_not_taken_over_before_its_lease_expires`; sahte kusur enjekte edilerek testin KIRMIZI olabildiği doğrulandı)
- [x] Altı arıza manifestinin her biri için beklenen davranış **yazıldı** ve testi koşuldu (veritabanı · yavaş sink · sağlayıcı zaman aşımı · retention hacmi · streaming fan-out · rolling upgrade)
- [x] Yük raporu ortam bilgisiyle (CPU/RAM/DB/payload/eşzamanlılık/commit) üretildi — `artifacts/load/bounded-sql-load-*.md`
- [x] Dış model gecikmesi kontrol düzlemi overhead'inden ayrı raporlandı (iki ayrı `Stopwatch`; rapor ikisini toplamamayı açıkça yazar)
- [x] `ProcessRunner` kopyalanmadı; `tests/Shared/Infrastructure/`'a taşındı ve iki projeye LINK'lendi, `WorkerProcessHost` onu kullanıyor
- [x] Faz 116'nın tahsis kapısı **değişmedi**; süre kapıya dönmedi (K-738)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı — akışsız `run` `Completed` (`echo`/`echo-1`, 13 token), akışlı SSE `run`/`update` çerçeveleri üretti, `jobs`/`health`/`workflows` uçları `200`; host log'unda hata yok
- [x] `secret` taraması boş döndü (`kapi.py tarama`)
- [x] Manuel kabul case'leri eklendi: **MT-RES-085…090**; altısının da otomatik karşılığı koşuluyor
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site` güncellendi (`guides/production.md`, `guides/model-providers.md`, `concepts/runs.md`, `concepts/workflows.md`, `http-api.md`); `npm run check` dört kapısı da temiz
- [x] 🚨 Hiçbir yerde "çok node destekleniyor" cümlesi kurulmadı; SQLite tek process tavsiyesi korundu (K-739)

### Doğrulama komutları

```bash
# İki process devralma senaryosu
dotnet test tests/AgentPrism.PostgreSql.IntegrationTests -c Release \
  -- --filter-class "*TwoProcessLeaseTakeoverTests*"

# Tahsis kapısı hâlâ yerinde
python3 scripts/kapi.py performans
```

---

## Plandan Sapmalar

| Plan ne diyordu | Ne yapıldı | Neden |
|---|---|---|
| Dosya listesi: yük senaryosu `bench/AgentPrism.Benchmarks/SqlRunStoreLoadBenchmarks.cs` | `tests/AgentPrism.PostgreSql.IntegrationTests/Load/BoundedSqlLoadTests.cs` | Planın **kendi** Açık Soru 4'ü B'yi öneriyordu (BenchmarkDotNet'in istatistik modeli uzun süren, dış kaynak isteyen bir koşuma uymaz). Dosya listesi bayattı; öneri izlendi. |
| Dosya listesi: arıza testleri `tests/AgentPrism.Sqlite.IntegrationTests/` | `tests/AgentPrism.PostgreSql.IntegrationTests/` | Aynı çelişki: Açık Soru 1 ve DoD'nin doğrulama komutu PostgreSQL diyordu, dosya listesi SQLite. SQLite'ın tek process tavsiyesi senaryoyu anlamsız kılar. |
| "üçüncüsü … `MaxAttempts = 1` varsayılanının anlamıdır" | Cümle kullanılmadı | Ölçüldü: varsayılan `MaxAttempts` **3**'tür (`AgentPrismSchedulingOptions`). Ölçülen iddia yeniden yazıldı: çift yürütme **eşzamanlı** olamaz; çökmeden sonra yeniden yürütme sözleşmenin kendisidir (`IJobHandler` "at-least-once" der). |
| "Yavaş sink: kayıt gecikmesi işlevi bozmaz (mevcut kural)" | Kural **daraltıldı**: bozmaz ama YAVAŞLATIR | `RunEventWriter.DispatchToSinksAsync` sıcak yolda `await` edilir. Fırlatan `sink` izole edilir; GECİKME edilmez ve olay başına ödenir. `SlowSinkTests` bunu alt sınır olarak ölçer, `production.md` ve `hafiza/cekirdek-calistirma.md` yazar. |
| Yeni proje yok varsayımı (yalnız `bench/` ve `tests/`) | `tests/AgentPrism.WorkerHarness` eklendi (👤 kullanıcı kararı) | `ProcessRunner` ölçüldü: tamamlanmayı bekleyen `static` bir yardımcı, öldürülebilir uzun ömürlü bir host başlatamaz. Öldürülecek gerçek bir AgentPrism host'u gerekiyordu; `samples/AgentPrism.Api` (956 satır, HTTP portu, frontend varlıkları, kontrollü uzun handler yok) bu iş için ağırdı. Sevk edilen paketlere dokunulmadı. |
| `WorkerProcessHost` `ProcessRunner`'ı kullanır | Kullanıyor — ama `ProcessRunner` `tests/Shared/Infrastructure/`'a **taşındı** ve iki projeye LINK'lendi | Kopyalanmadı (DoD'nin şartı). `ProcessRunner`'a `StartAsync` eklendi; MSBuild `nodeReuse` deadlock düzeltmesi tek bir `CreateStartInfo` gövdesinde kaldı. |
| Faz kapsamı yalnız ölçüm | **Sevk edilen kodda bir kusur bulundu ve düzeltildi** (K-737) | Aşağıda. |

### 🚨 Kapsam dışı ama sevk edilen: sağlayıcı zaman aşımı kusuru (K-737)

`ProviderTimeoutTests` manifesti yazılırken ortaya çıktı ve `kusur-giderme`
protokolüyle kapatıldı.

**Repro:** `IChatClient` `TaskCanceledException` (inner `TimeoutException`)
fırlatır — `HttpClient` kendi istek zaman aşımını tam olarak böyle bildirir ve
her resmî sağlayıcı SDK'si `HttpClient` üzerindedir. Hiçbir token iptal
edilmemiştir.

**Gözlenen (düzeltmeden önce):** `run` `Canceled` + `error: null` yazılıyor,
uç **`200` + boş gövde** dönüyor, fallback zinciri sonraki halkayı
**denemiyordu**. Yani kesinti hiçbir arıza panosunda görünmüyor ve çağıran onu
başarı sanıyordu.

**Kök sebep:** iptal kararı istisnanın TİPİNDEN okunuyordu. Düzeltilen yerler:

| Yer | Ne değişti |
|---|---|
| `RunRecordingAgent` (akışlı + akışsız) | `when (cancellationSource.IsCancellationRequested)` |
| `FallbackChatClient` (akışlı + akışsız) | `when (cancellationToken.IsCancellationRequested)`; `IsCancellation` artık zaman aşımını iptal SAYMAZ; yeni `FallbackSkipReason.Timeout` (`"timeout"`) |
| `AgentEndpoints`, `WorkflowEndpoints`, `OpenAIChatCompletionsEndpoints`, `OpenAIResponsesEndpoints` | `when (httpContext.RequestAborted.IsCancellationRequested)` |
| `WorkflowRunner` (başlatma + adım) | `when (linked.IsCancellationRequested)` |
| `DefaultRunErrorClassifier` | zaman aşımı kontrolü `CanceledTypePattern`'den ÖNCE |

**Sınıf taraması:** `grep -rn "catch (OperationCanceledException)$" src/` → 32
yer. Yukarıdaki 11'i düzeltildi (kullanıcıya dönük yanlış-başarı üretenler).
Kalanlar arka plan döngülerinin "host kapanıyor" dalları (`JobWorkerBackgroundService`,
`RunHeartbeatWriter`, `RunReconciliationService`, `CanaryEvaluationService`,
`ApprovalExpirationService`, `McpDiscoveryService`), CLI'nin Ctrl+C dalları ve
`CompositeAgentCatalog`/`AgentDecoratorPipeline` gibi katalog yollarıdır;
hiçbiri bir sağlayıcı çağrısını sarmalamaz ve hiçbiri bir yanıt gövdesi
üretmez. Devir notunda açık kalem olarak duruyor.

**Kapı:** `RunCancellationStatusTests.An_uncancelled_OperationCanceledException_writes_Failed_not_Canceled`
(akışlı + akışsız) ve `FailureManifests.ProviderTimeoutTests`. Mevcut
`External_cancellation_writes_Canceled_*` testleri gerçek iptalin hâlâ
`Canceled` yazdığını kanıtlar — düzeltme iptal davranışını bozmadı.

**Bir testin beklentisi DEĞİŞTİ:** `StructuredResponseEndpointTests` içindeki
`Cancellation_during_validation_closes_the_run_as_canceled_not_as_a_validation_error`
eski davranışı kodluyordu ve kendi yorumu kusuru tarif ediyordu ("uç HİÇBİR
ŞEY yazmaz, yanıt ASP.NET Core'un varsayılan 200'ünde kalır"). Yeniden
adlandırıldı: `A_validator_that_throws_a_cancellation_nobody_requested_fails_the_run`,
beklenen sonuç `502` + `Failed`.

## Bu Fazda Verilen Kararlar

| # | Karar |
|---|---|
| K-737 | Bir `OperationCanceledException` ancak İLGİLİ TOKEN gerçekten iptal edildiyse iptaldir; aksi hâlde ARIZADIR |
| K-738 | Yük ve arıza ölçümleri RAPORDUR, kapı değildir; süre hiçbir eşiğe bağlanmaz |
| K-739 | İki process senaryosu bir ÖLÇÜMDÜR, çok node DESTEK BEYANI değildir 👤 |

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir denetçiyle koşuldu (2026-09-08, taban
`a3ec7527`). **🔴 bulgu yok.** Denetçi K-737 düzeltmesinde aradığı dört
gerilemenin hiçbirini bulamadı: gerçek iptal hâlâ `Canceled` yazıyor,
`when` filtreleri doğru token'ı okuyor, `WorkflowEndpoints`'in dar `catch`'ine
`TaskCanceledException` ulaşamıyor (`WorkflowRunner` onu `PumpedEvent`'e
çeviriyor) ve `DefaultProviderRetryClassifier` bozulmadı.

Sekiz 🟡 bulgunun **tamamı kapatıldı**:

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | K-737'nin en riskli satırı — sınıflandırıcıdaki SIRA — hiçbir testle sabitlenmemişti; eski sıraya dönülse her şey yeşil kalırdı | **Düzeltildi.** `DefaultRunErrorClassifierTests`'e aynı tipin mesajla ayrışan iki satırı, `FallbackRetryRegressionTests.DecisionTable`'a iki zaman aşımı satırı eklendi. Sıra elle geri alınarak testin KIRMIZI olduğu doğrulandı (1 düşen), sonra geri konup yeşile döndü |
| 2 | Sınıf taraması `when`/`is not` biçimini göremedi; `WorkflowNodeRetry` bir sağlayıcı çağrısı sarmalıyor ve `TransientClasses` `Timeout` içerdiği hâlde zaman aşımını hiç denemiyordu | **Düzeltildi.** `WorkflowNodeRetry.cs` filtresi `!cancellationToken.IsCancellationRequested`'a çevrildi. Devir notundaki "kalanların hiçbiri sağlayıcı çağrısı sarmalamaz" cümlesi de düzeltildi |
| 3 | "İş dokunulmadan kalır" iddiası ölçülmüyordu; worker kesintiden önce işi lease etmiş olabilir | **Düzeltildi.** Test adı ve iddiası daraltıldı (`..._the_job_is_not_lost`): `Status != Completed/Failed`, `DoneItems == 0`, `FailedItems == 0`. `production.md` de "jobs are untouched" yerine "in-flight attempt is lost, the job is not" diyor |
| 4 | Retention kilit manifesti sevk edilen `DeleteBatchAsync` ifadesini değil elle yazılmış bir `DELETE`'i koşuyor | **Gerekçelendi ve daraltıldı.** Ölçülen şey bir delete AÇIK/COMMIT EDİLMEMİŞken ne olduğudur; `DeleteBatchAsync` kendi bağlantısını sahiplenip dönmeden commit ettiği için public API üzerinden bu tutulamaz. Predicate store'unkiyle aynı; sınır hem test dokümanına hem `production.md`'ye ("verified against PostgreSQL with the delete deliberately left uncommitted") yazıldı. İkinci test zaten sevk edilen metodu koşuyor |
| 5 | 6 sn'lik lease penceresi worker B'nin tam process açılışını emmek zorundaydı — yüklü ajanda kırılgan | **Düzeltildi.** İki worker da `job` ENQUEUE EDİLMEDEN önce başlatılıyor; pencereye artık hiçbir process açılışı girmiyor. Yarışı kim kazanırsa o öldürülüyor (`FirstStarter()`), ikisi de aynı `WorkDuration`'ı alıyor — sonucu değiştiren bir kurulum ölçüm değil yazı turadır |
| 6 | Fan-out manifesti yalnız BİTMİŞ bir `run`'ı okuyor; canlı kuyruk hiç koşmuyor | **Kapsam yazıldı.** Sınıf dokümanı ve `production.md` artık "recorded stream of a finished run" diyor ve canlı kuyruğun `StreamingTests`'te olduğunu söylüyor |
| 7 | `production.md`'nin "measured, not inferred" başlığı rolling-upgrade'i de kapsıyordu, oysa orada iki gerçek sürüm ölçülmedi | **Düzeltildi.** O alt bölüme kendi sınırı yazıldı: tek build iki tarafı temsil ediyor, iki yayınlanmış sürüm yan yana ölçülmedi |
| 8 | MT-RES-089 `K-727` (Faz 155'in evaluator kararı) diyordu | **Düzeltildi** → `K-737` |

İki 🟢 bulgu (`ChildAgentInvoker`'ın zaman aşımını kendi deadline'ı sanması ·
yük raporundaki RAM/CPU alanlarının GC/container değerleri olması)
[`ADAYLAR.md`](../../ADAYLAR.md)'ye yazıldı.

🔴 olmadığı için kapılar bir kez daha koşuldu; 🟡 düzeltmeleri sevk edilen koda
(`WorkflowNodeRetry`) dokunduğu için tam kapanış kapısı tekrarlandı.

## Sonraki Faza Devir Notu

- **🚨 `catch (OperationCanceledException)` sınıfı KAPANMADI, daraltıldı.** 32
  yerin 11'i düzeltildi. Kalan 21'i bugün zararsızdır (arka plan döngüsü,
  CLI Ctrl+C, katalog) ama kural artık yazılıdır: **iptal kararı token'dan
  okunur**. Bu yollardan birine bir sağlayıcı/ağ çağrısı eklenirse aynı kusur
  yeniden doğar. Üçüncü tekrar olursa yazı yetmez — bir analyzer kuralı
  (`catch (OperationCanceledException)` filtre yoksa uyar) gerekir.
- **🚨 Tarama grep'i `catch (OperationCanceledException)$` idi ve `when` /
  `is not` biçimlerini GÖREMEDİ.** Denetim bu yüzden ikinci bir vaka buldu
  (`WorkflowNodeRetry`, düzeltildi). Bir sonraki tarama üç deseni birden
  arasın: `catch (OperationCanceledException)`, `is not OperationCanceledException`,
  `IsCancellation(`. `ChildAgentInvoker.cs:208,329` bilinçli olarak
  bırakıldı — orada zaman aşımı zaten bir zaman aşımı mesajına dönüşüyor,
  sessiz başarı üretmiyor; `ADAYLAR.md`'de F kalemi olarak duruyor.
- **Yük raporu bir kapı DEĞİLDİR ve öyle olması ayrı bir karardır** (K-738).
  `AGENTPRISM_LOAD=1` ile koşar, `artifacts/load/` altına yazar. Bu makinede
  ölçülen ilk değerler: 8 eşzamanlı, 200 `run` × 20 olay, 256 karakter payload
  → kontrol düzlemi `run` başına ~17,7 ms / olay başına ~0,88 ms
  (macOS arm64, PostgreSQL 18.4 container, commit `a3ec7527`).
- **Rolling upgrade manifesti "eski sürüm" olarak DAHA AZ opsiyonel migration
  seti açan bir context kullanır** — ikinci bir AgentPrism ikilisi değil. İki
  gerçek sürümü yan yana koşturmak ölçülmedi; `production.md` bunu vaat etmez.
  Gerçek iki-ikili ölçümü isteyen bir faz `nuget-danismani` ile yayınlanmış iki
  paket sürümü üzerinden kurmalıdır.
- **`docs/hafiza/test-altyapisi.md` %0 boşlukta** (15932/16000). Bir sonraki
  faz oraya not eklerse önce böler; `test-kosum-tuzaklari.md` (%34 boş) ve
  `test-yalitimi.md` (%16 boş) kardeşleridir.
- **`tests/Shared/Infrastructure/` bir csproj DEĞİLDİR**, LINK'lenen kaynak
  dosyalardır. Yeni bir tüketici projesi `<Compile Include=... Link=.../>`
  satırlarını ve `<Using Include="AgentPrism.Tests.Common" />` girdisini
  kendi csproj'una ekler. İkinci bir kopya açma (K-411).
- **Arıza manifestleri kapıdadır, yük raporu değildir.** `TwoProcessFailureProof`
  koleksiyonu process başlatan sınıfları birbirine karşı seri hâle getirir;
  yeni bir process testi o koleksiyona girmelidir, yoksa dört host aynı makinede
  aynı lease saatini ölçer.
