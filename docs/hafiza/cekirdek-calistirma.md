# Cekirdek Calistirma Yolu Tuzaklari

> RunRecording zinciri, sir suzgeci, metrik, surumleme, kaynak sahipligi ve
> arka plan gorev omru.
>
> Metrik, maliyet, kota ve `secret` suzgeci AYRI dosyadadir:
> [`olcum-kota-ve-secenekler.md`](olcum-kota-ve-secenekler.md). Tool onayi ve
> yetkilendirme ekseni de AYRI dosyadadir:
> [`tool-onay-ve-yetkilendirme.md`](tool-onay-ve-yetkilendirme.md). `RunEventType`/
> terminal durum switch/`JobRecord.Payload` enum sozlesmesi AYRI dosyadadir:
> [`run-olay-sozlesmesi.md`](run-olay-sozlesmesi.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **🚨 Agac genelinde W3C trace kimligi TEKTIR; `RunTraceCollector` yalniz `Depth == 0` iken cagrilir** (2026-08-02, Faz 12): tampon trace kimligiyle anahtarlanir ve `CompleteRunAsync` onu KALDIRIR — once biten ALT calistirma tum agacin span'lerini sahipleniyordu. Birim testi yakalamadi, ornek uygulama yakaladi. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 ASP.NET host'ta `Activity.Parent` zincirinin tepesi run root DEGIL, HTTP server span'idir** (2026-08-26, F-164): `RunTraceCollector` tampon sahibini zincirde `(traceId, spanId)` anahtari kayitli **en yakin ancestor** ile bulur. Tepedeki local parent'i kullanmak `BeginRun` anahtariyla eslesmedi ve gercek HTTP yolunda tum span'leri sessizce dusurdu. HTTP parent'i olmayan birim testi kusuru gizledi; `RunTraceEndToEndTests` gercek DI + SSE + trace endpoint zincirini kalici olarak olcer.
- **`RunEventWriter.AppendAsync` artik `ValueTask<RunEvent>` doner** (2026-08-03, Faz 15): workflow akisi ayni olayi hem `store`'a yazip hem istemciye gonderir; ikinci kez kurmak sira numarasini ikiye bolerdi. Devre disi bir yazicida da olay URETILIR (yalnizca kalicilastirilmaz) — gozlemlenebilirligin kapanmasi akan yaniti kesmemelidir.
- Compaction sarmalama sınırı (Faz 13) ve `IAgentSource` sürüm marker deseni (Faz 19): `docs/arsiv/FAZ-GECMISI.md` "Faz 13/19".
- **🚨 `AgentDefinitionCompiler.Compile` TAMAMEN senkron; kiraci kimlik bilgisi cozumlemesi async `store` gerektirir — ikisi celisince YENI paralel async yol acildi, mevcut sync yol DEGISTIRILMEDI** (Faz 65). Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`AgentRunScope.SessionId` tool'un urettigi icerigin sahibidir** (Faz 28, K-217): oturumsuz yazilan ek, saklama politikasinca **sahipsiz** sayilip silinir (`session_id IS NULL`). Kimlik `runs.session_id`'den GENIS: alt calistirma MAF oturumu almaz, icerik yine kok oturuma aittir. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
<!-- MEMORY.md'de kisa ozet var (Her Oturumda Gecerli); ayrinti buradaki tek konsolide maddededir. -->
- **🚨 `AsyncLocal` (span/`run scope`) yazimi ASYNC METOTTAN cagirana geri akmaz — uc vaka** (2026-08-02, Faz 6/11/12): (1) `Activity.Current`: kok span `async BeginRunAsync` icinde acilinca ic span'ler (`invoke_agent`, `chat`) kok'un cocugu degil **kardesi** oldu; span cagiranin **kendi govdesinde** acilmali (`RunRecordingAgent.PrepareRun` bu yuzden essenkron). (2) `run scope`: `TraconRunContext.SetCurrent` ayni sebeple `RunRecordingAgent`'in kendi govdesinde cagrilir. (3) **`async IAsyncEnumerable` govdesinde `yield return` siniri da asilmaz**: `RunCoreStreamingAsync` icinde bir kez yazilan `scope` ic cagrida `null` goruluyordu (`"calistirma kaydi kapali"` reddi) — cagri driver'a donunce `ExecutionContext` geri alinir. Cozum: akisli yolda `scope` **her `MoveNextAsync`'ten hemen once** yeniden yazilir. Regresyon: `Ic_spanler_kok_spanin_cocugu_olur`.
- **🚨 İmza+gövde iki ayrı adım vakası: `RunEventWriter.CompleteAsync`** (2026-08-03, Faz 20, K-157): `RunCost? cost` parametresi eklendi ama `new RunCompletion { ... }`'a `Cost = cost` yazılmadı — 1068 test yakalamadı (hiçbiri `RunRecordingAgent → RunEventWriter → Store` zincirinin ORTASINI uçtan uca sınamıyordu), yalnız örnek uygulamada gerçek bir çağrıyla (`cost: null`) ortaya çıktı. Genel kural AGENTS.md'de.
- **🚨 Disaridan iptal, `RunRecordingAgent`'in KENDI `CancellationTokenSource`'una guvenir, gelen `cancellationToken`'a DEGIL** (Faz 32): gelen token'in KENDISI iptal edilemez — `CreateLinkedTokenSource(cancellationToken)` ile kendi kaynagi kurulur, deftere O yazilir. Aksi halde defterin `Cancel()`'i hicbir seyi etkilemezdi. `WorkflowRunner.ExecuteAsync` ayni deseni tekrarlar.
- **Dogrulanamayan workflow iptal tuzagi** (2026-08-06, Faz 32, terk edildi) — `docs/arsiv/FAZ-GECMISI.md`, "Faz 32".
- **`ITenantStore` kaydi zorunlu DEGILDIR; kayitsiz kiracinin verisi `ListAsync()` taramasinda GORUNMEZ** (Faz 35, K-257): `QuotaUsageObserver` yalniz KAYITLI kiracilari tarar. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Resmi saglayici SDK'lari `HttpRequestException` FIRLATMAZ** (2026-08-07, Faz 44, K-296): gercek bir OpenAI 404'unde SDK `System.ClientModel.ClientResultException` firlatir; birim testleri gecen ilk taslak hatayi `Unknown`'a dusurdu. Desen `ClientResultException`/`RequestFailedException`/`ApiException` + mesajdaki `HTTP 4xx/5xx`'i kapsar. Yeni saglayicida gercek istisna adini OLC, tahmin etme. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `ISessionStore.SaveAsync` YALNIZ basari yolunda cagrilir** (Faz 45, K-300): `catch` bloklari onu cagirmaz; oturum uzerinden girdi metni okuyan tasarim basarisiz calistirmalarda bosa cikar. Girdi metni `RunStarted.Text`'ten okunur — HER zaman dolu tek kaynak. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Cok turluluk icin tam sohbet gecmisi okumaya gerek yoktur** (2026-08-07, Faz 45, K-301): "Bu `sessionId`'de ONCE baslamis baska bir calistirma var mi" sorusu `IRunStore.QueryRunsAsync(new RunQuery { SessionId = ..., OnlyRootRuns = false })` ile cevaplanir; `ISessionStore`/`IAgentCatalog`/`ChatHistoryProvider` zincirine gerek kalmaz.
- **Oksuz calistirma uzlastirmasi `heartbeat_at`'i toplu okur — calistirma basina degil TUR basina bir sorgu** (Faz 54, K-362): sicak yol (`RunEventWriter`) hic degismedi. Ayrinti: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md), `docs/arsiv/fazlar/54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md`.
- **🚨 Bucket araligi `Variants`'in fiziksel sirasina bagliysa, bir kolun agirligini degistirmek DIGER kollarin araligini kaydirir ve var olan atamalari bozar** (Faz 56, K-374): degisken agirlikli kolun araligi konumdan bagimsiz sabit bir uca ankorlanmali.
- **🚨 Skill script: korumasiz `StandardInput.Close()` ve JSON'a cevrilmemis denetim `after`'i SESSIZCE coker** (Faz 65 oncesi, Aile W): sahte `IAuditLog` YAKALAMAZ. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Bellek ici store ile SQL store'un AYNI sorguya farkli yanit vermesi sozlesme testinden kacabilir — test o sorguyu hic sormuyorsa** (2026-08-19, Faz 68): bir alani "her yerde" ekledigini dusundugunde, o alani OKUYAN her depo metodunun sozlesme testinde bir iddiasi var mi diye bak. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Ambient bir baglami bir kayit yolunda DOGRUDAN okuma — tuketicinin uygulamasi firlatabilir ve dogrulanmamis deger dondurebilir** (2026-08-19, Faz 68): garantiler `RunAttributionReader.Read(...)`'e cikarildi; her kayit yolu onu kullanir. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `CompleteAsync` ustundeki `if (IsDisabled) return;` korumasi kapanis olayini (RunCompleted/RunFailed) HIC uretmiyordu** (Faz 70, K-493): depo ve sink BAGIMSIZ olmali; koruma kaldirildi, yalniz `_store.CompleteRunAsync` `IsDisabled`'a bagli. Yeni bir "erken don" eklerken sor: bu YALNIZ depo icin mi, depo-DISI tuketiciyi de susturuyor mu? Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `IRunStore.ListToolInvocationsAsync(runId)` tenant'i `ITenantContext`'ten ORTUK okur** (Faz 85): `QueryRunsAsync`'in aksine `RunQuery.TenantId` almaz; HTTP disindan (test, konsol araci) ambient scope acik degilse SESSIZCE bos doner (`IsOwnedByCurrentTenant` `false`). Cozum: `AmbientTenantScope.Begin(tenantId)` ile sarmala.
- **🚨 Ateşle-unut görev, sahiplenilen kaynağın ömrünü AŞAMAZ** (2026-08-25, Faz 99, F-150): `JobWorkerBackgroundService` bir işi başlatmadan önce completion görevini kaydeder; `ExecuteAsync`, slot `SemaphoreSlim`'ini dispose etmeden önce kaydedilen görevlerin tamamını bekler. Aksi sıra, host kapanışında gecikmiş `Release()` ile işlenmemiş `ObjectDisposedException` ve süreç çöküşü üretir. `JobWorkerBackgroundServiceTests` `StopAsync`'in çalışan job slotu bırakılmadan dönmediğini doğrudan ölçer. Yeni bir `_ = SomeAsync(...)` görürsen iki soruyu sor: görev kapanışta gözlemleniyor mu; yakaladığı kaynak onu bekleyen scope'tan uzun mu yaşıyor?
- **🚨 `ConcurrentDictionary<TKey,TValue>.GetOrAdd(key, valueFactory)` tahsis eder
  — HER cagrida, CACHE ISABETINDE bile** (2026-08-27, Faz 116, ölçüldü): C#
  argümanları çağrılan metottan ÖNCE değerlendirir, yani `_ => factory()` gibi bir
  kapanış her seferinde HEAP'e yeni bir delege olarak yazılır — sözlük anahtarı
  zaten var olsa da, `valueFactory` hiç ÇAĞRILMASA da. `CompiledAgentCache.GetOrAdd`
  bunu yapıyordu; `GetOrAddAsync` kardeşi zaten `TryGetValue`-önce desenini
  kullanıyordu, sync taraf kullanmıyordu. Ölçüldü (BenchmarkDotNet,
  `bench/Tracon.Benchmarks`): düzeltme öncesi isabet başına 88 B, sonrası
  24 B — kalan 24 B `ConcurrentDictionary<CacheKey,AIAgent>.TryGetValue`'nun
  kendi maliyeti (izole ölçüldü, kaynağı bulunamadı; her koşumda sabit ve
  deterministik). Kural: `GetOrAdd(key, _ => ...)` yazarken önce
  `TryGetValue(key, out var existing)` dene, yalnız KAÇIRINCA `GetOrAdd`'a düş.
- **🚨 `RunRecordingAgent.ToRunError`'un `Message = exception.Message` satırı
  yıllarca "zaten redakte ediyor" sanılan ama redakte ETMEYEN bir kod yoluydu**
  (2026-08-27, Faz 119, K-640). `Type` alanı `TraconException.ErrorType`
  ile zaten stabil bir kod taşıyordu — bu, okuyana "hata sınıflandırması
  yapılıyor, güvenli" izlenimi veriyordu, ama `Message` alanı HER ZAMAN ham
  `exception.Message`'ı yazıyordu, `Type` ayrımından bağımsız. Bir alanın
  güvenli görünmesi (stabil kod, sınıflandırılmış tip) komşu alanın da güvenli
  olduğunu KANITLAMAZ — ikisi ayrı ayrı denetlenir. Düzeltme:
  `SafeErrorText.ForPersistence(exception, correlationId)`; `ToRunError` artık
  `static` değil, `_logger.LogError` çağırabilmek için instance metot.
- **🚨 İstisnayla biten `run`'ın TEK usage kaynağı `scope.ExtraUsage`'dır**
  (2026-09-02, Faz 134): `catch (Exception)` `usage`'ı DAİMA `null` geçirir.
  Faz 134'ün onarım döngüsü `scope.ExtraUsage?.Add(...)` ile bunu kullandı ve
  bir Faz 131 eksiğini kapattı: reddedilen bir denemenin token'ı artık
  `run.Usage`'da görünür (önceden sessiz `null`). `AgentResponse.Usage`'a
  güvenme — yalnız SON dönen yanıt için işler.
- **🚨 Kararı uygulayıp sonra handoff yazan uç, hatada kararı ASILI bırakır** (2026-09-07, B03/K-726): `DecideAsync` → `StartRunAsync` → `EnqueueAsync` üçlüsünü saran transaction yok; ikinci adımdan sonraki hata "karar verildi, iş başlamadı" üretir ve tekrar `409 AlreadyDecided` alırdı. `RunReconciliationService` kurtarmaz: yalnız `Running` claim eder, default kapalı. Çare transaction değil TEKRAR SÜRÜLEBİLİRLİK — id'yi approval'dan türet, yazımları "satır yoksa yaz" yap.
- **🚨 `OperationCanceledException` bir IPTAL DEĞİL, bir SORUDUR: "gerçekten iptal edildi mi?"** (2026-09-08, Faz 157, K-737). `HttpClient` kendi istek zaman aşımını `TaskCanceledException` (inner `TimeoutException`) olarak bildirir ve her resmî sağlayıcı SDK'si `HttpClient` üzerindedir. Çıplak `catch (OperationCanceledException)` bu yüzden bir sağlayıcı kesintisini kullanıcı iptali sanıyordu: `run` `Canceled` + `error: null`, uç `200` + **boş gövde**, panolarda hiçbir arıza. Kural: iptal kararı istisnanın tipinden değil, o kapsamın KENDİ token'ından okunur — `RunRecordingAgent` `cancellationSource` (kayıt defterinin de tetiklediği kaynak), HTTP uçları `HttpContext.RequestAborted`, `WorkflowRunner` `linked`. Token'ın olmadığı sınıflandırma yolunda ayrım **zaman aşımı sinyali** ile kurulur (graf içinde `TimeoutException` ya da zaman aşımı deseniyle eşleşen mesaj); `DefaultRunErrorClassifier`'da zaman aşımı kontrolü `CanceledTypePattern`'den ÖNCE gelir, aksi hâlde tip kontrolü her zaman kazanır. Regresyon: `RunCancellationStatusTests.An_uncancelled_OperationCanceledException_writes_Failed_not_Canceled`.
- **🚨 `IRunEventSink` dağıtımı SICAK YOLDA `await` edilir — yavaş bir `sink` `run`'ı yavaşlatır** (2026-09-08, Faz 157, ölçüldü). "Gözlemlenebilirlik işlevselliği bozmaz" kuralı yalnız ARIZAYI kapsar: fırlatan bir `sink` ilk hatada o `run` için devre dışı kalır ve `run` sürer. GECİKME izole edilmez; olay başına ödenir. Ölçüm: `SlowSinkTests.A_slow_sink_delays_the_run_because_dispatch_is_inline` (alt sınır olarak yazılır ki hızlı makine testi tesadüfen yeşile çeviremesin). Ağ hedefi olan bir `sink` kendi içinde tamponlamalı ve hemen dönmelidir.
- **Ölen worker'ın işi `lease_until` dolana kadar KİMSEYE geçmez; sonra `attempt` artarak devralınır** (2026-09-08, Faz 157, K-739). Ölçülen üç şey: devralma `lease_until`'dan sonra başlar · madde sayaçları idempotenttir (`doneItems` ikiye katlanmaz) · iki worker aynı işte ASLA aynı anda bulunmaz. Yürütme yine de **at-least-once**'tır: çöken denemenin dış yan etkileri geri alınmaz. Bu bir çok node DESTEK beyanı değildir; SQLite tek process tavsiyesi korunur.
