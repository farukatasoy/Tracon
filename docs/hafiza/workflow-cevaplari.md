# Workflow Cevaplari — Tek Cevap, Cevap Kaydi, Eksik Is, Istek Kutusu

> Bekleyen bir workflow isteginin cevabi: `WorkflowResponseMode`, cevap kaydi
> (`workflow_responses`), izleme akisi ve fail-closed `workflow.respond`; ayrica
> istek kutusu (`workflow_requests`) ve istek suresi (Faz 209).
> Yurutme ve HITL'in geri kalani: [`workflows.md`](workflows.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken
> okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

## Nerede yasiyor

- **Karar `src/Tracon.AspNetCore/Internal/WorkflowResponseGate.cs`'te** (2026-10-14,
  Faz 208): denetim → run satiri → kayit/iddia; `ReadStateAsync` izle / yeniden
  sur / `Gone` / eksik is kararini zincirden, kuyruk isinden ve checkpoint'ten
  turetir. Uc `WorkflowEndpoints.RespondAsync`'tir; izleyici
  `WorkflowFollowStream` (ayni dosya) `RunEventTail`'i okur. Zincir yuruyusu
  `WorkflowContinuationChain`'dir (kurtarma ile ortak).
- **Kayit deposu** `IWorkflowResponseStore`: bellek ici
  (`Core/Workflows/InMemoryWorkflowResponseStore.cs`) + uc SQL deposu;
  sozlesme `WorkflowResponseStoreContract`. Hash `WorkflowAnswerDigest`'tir.

- **Istek kutusu** (2026-10-14, Faz 209): `Abstractions/Workflows/IWorkflowRequestStore.cs`
  (kayit + `WorkflowRequestQuery`) · `Core/Workflows/InMemoryWorkflowRequestStore.cs` ·
  `Sql.Shared/Stores/SqlWorkflowRequestStore.cs` (sorgular `SqlQueriesBase`,
  liste/sure sorgusu sağlayici basina) · satir yazimi TEK yerde:
  `Workflows/Internal/WorkflowRunner.CompleteAsync` (`RecordRequestsAsync`) · liste
  `WorkflowRunner.ListRequestsAsync`, ucu `WorkflowEndpoints.ListInboxAsync` · cevap
  isareti `WorkflowResponseGate.MarkAnsweredAsync` · kapanis
  `Workflows/Internal/WorkflowRequestExpirationService.cs`. Konsol:
  `screens/approvals/workflow-requests.tsx`. Sozlesme `WorkflowRequestStoreContract`.

## Tuzaklar

- **🚨 `IRunStore.CompleteRunAsync` olay sayisini, kullanimi ve maliyeti tamamlamanin
  tasidigiyla DEGISTIRIR** (2026-10-14, K-1003): zaten kapanmis bir run'i kapatan kod
  satiri once okur ve degerleri geri yazar. `ApprovalExpirationService` bos tamamlama
  kuruyor ve onay suresi dolunca run'in olay sayisi 0'a, kullanimi `NULL`'a dusuyordu
  (duzeltildi). Sinif taramasi: baska cagiran kayitli degeri olan bir run'i kapatmaz.
- **🚨 Satir kapaninca run kapatilamazsa o satir `ExpireAsync` ile bir daha bulunmaz**
  (K-1003): satir once kapanir. Servis kapatilamayan run'i bellekte tutar ve her turda
  en cok 10 kez dener. Surec arada durursa liste gider; lease'i alan sonraki ornek
  `ListStrandedAsync` ile (kapali satir + acik satir yok + `runs.status = AwaitingInput`)
  run'i bulup kapatir. Sonraki ornek bir kez bakar; tam tur dolarsa tekrar bakar.
  Yeni bir "satiri once kapat, sonra bir seyi yap" adimi eklersen ayni boslugu orada da
  kapat. Bellek ici depo bos doner (satir ile run birlikte gider).
- **Cevap ile sure ayni anda:** satir kapandi, run henuz degil → cevap alinir, isi
  kosar, satir isaretlenmez, run `Failed` + `WorkflowRequestExpired` kapanir. Kuyruklu
  cevap is kosarken run kapanmissa kendi run'i reddeder. Testler
  `WorkflowRequestExpirationHttpTests` (iki SQLite host'u art arda: ikinci host ilk
  surecin biraktigi run'i kapatir).
- **🚨 Arka plan servisi kiracisiz calisir:** satir ve run okumasi
  `AmbientTenantScope.Begin(satirin kiracisi)` ister (`SingleTenantContext` ve
  `HttpTenantContext` onu okur; test `FixedTenantContext`'i OKUMAZ — kiracili sureyi
  HTTP host'unda sina).
- **`UPDATE … RETURNING`/`OUTPUT` sirasi tanimsizdir:** `ExpireAsync` testleri kumeyi
  karsilastirir, sirayi degil. Hangi satirlarin kapandigini vade sirasi belirler.
- **Minimal API enum sorgu baglayicisi buyuk/kucuk harfe duyarlidir** (`state=all`
  `400` verdi): `state` metin okunur, `Enum.TryParse(ignoreCase)` ile cozulur.
- **Sema tiplerinde `<see cref>` OpenAPI'de CLR imzasi olur** (`TimeSpan? X.Y`):
  `WorkflowPendingRequest`/`WorkflowDescriptor` dokumaninda duz metin kullanilir.
- **Retention arsiv anahtari testi sutun tipini kendi tahmin eder** (`UnmatchedKey`):
  metin sutunu (`request_id`) `Guid` varsayilirsa PostgreSQL `text = uuid` verir.
- **Sure testi:** `ManualTimeProvider` (zamanlayicilar gercek, `GetUtcNow` sahte)
  `AddTracon`'dan ONCE kaydedilir; servis 50 ms'de bir bakar, test saati ilerletir ve
  `WaitForRunStatusAsync` ile bekler. Gercek sure beklemek yerine `RequestTimeout`'u
  uzun tutup saati ilerlet.
- **Ornek uygulamayi calistirirken kullanici sirlari PostgreSQL'e baglanir** ve
  migration'lari o veritabanina uygular (Faz 209'da yerel `agentprism` semasi iki
  migration aldi). SQLite icin `Tracon__PostgreSql__ConnectionString=""` ve
  `Tracon__Sqlite__ConnectionString` ver; belirteç `Tracon__Ui__AuthToken` ile gelir.

- **🚨 Once satir, sonra kayit** (2026-10-14, K-997): satiri olmayan bir kayit
  istegi kalici kilitler (tekrar onu izler, izleyecek bir sey yok). Iddiadan
  SONRA satir yazan ilk tasarim, firlatan atif getter'i ve istemci kopmasiyla
  kilitlendi (bagimsiz denetim 🔴1). Satirdan sonraki her yazim
  `CancellationToken.None` kullanir; atif karardan once `RunAttributionReader`
  ile bir kez okunur.
- **🚨 Kuyruk isi yasarken yeniden surme** (K-997, 🔴2): is kimligi run
  kimligidir (K-924). Is `Pending`/`Leased`/`Running` ise kurtarmasi zinciri
  surdurur; ayni cevabi yaninda yeniden baslatmak cevabi iki kez uygular.
- **🚨 Isi yazilmamis `Queued` run'i hicbir sey kapatmaz** (ikinci tur 🟡1):
  uzlastirma yalniz `Running` satir secer (`PostgresQueries` yetim sorgusu),
  isi olmayan `Queued` run'in iptali `409` verir. Ayni cevabin tekrari isi yazar
  (`Decision.MissingJob` → `WriteMissingJobAsync`). Yaris guvenligi kuyrugun
  ayni kimlikli ikinci isi REDDETMESINE dayanir (K-998): bellek ici depo isi
  DEGISTIRIYORDU; `QueuedRunPlaceholder` reddedilen yazimdan sonra isi geri
  okur ve satiri kapatmaz.
- **🚨 Izleyici eksik isi `done` ile bitirmez**: satir yoksa ya da tek olay
  okunmadiysa `error` cercevesi yazar. Eski 30 sn bekleme `done` ile bitiyordu.
- **🚨 Akisli devam cokuste `Running` kalir**: yalniz run uzlastirmasi (varsayilan
  kapali) kapatir; `WorkflowResponseMode` XML'i ve site bunu soyler.
- **Test: depo sahtesi `configureTracon` icinde** (2026-10-14):
  `WorkflowResponseHarness.StartAsync(configureServices:)` aninda `IJobStore` ve
  `IRunStore` henuz koleksiyonda degildir; `FaultInjection.ThrowFrom<T>`
  `configureTracon: b => b.Services...` icinde cagrilir ("Sequence contains no
  matching element"). `when:` suzgeci yalniz eslesen argumanla hata verir.
- **Test: TestServer'da `500` istisna olarak gelir** — uygulamada exception
  handler yok; ilk cagri `try/catch` icinde yapilir, sonra durum okunur.
- **Elle repro: cokme penceresi** — ornek uygulama `Tracon__Scheduling__RunWorker=false`
  ile kuyruklu cevap al, uygulamayi durdur, SQLite'ta `tracon_jobs` ve
  `tracon_job_items` satirini sil (kimlikler BUYUK harf), isciyle yeniden ac,
  ayni cevabi gonder.

- **🚨 `ExternalRequest.TryGetDataAs<T>` checkpoint'ten gelen JSON'u İSTENEN tipe deserialize etmeyi dener** (2026-10-15, Faz 211, ölçüldü): cevap run'ı isteği checkpoint'ten okur, veri artık canlı nesne değil JSON'dur; `{"text":"...","violation":null}` gibi bir kayıt `MagenticPlanReviewRequest`'e (tüm üyeleri isteğe bağlı) hatasız okunur ve cevap yanlış tipte (`MagenticPlanReviewResponse`) üretilirdi. İsteği yayınlayan ilk run canlı nesneyi tuttuğu için kusur yalnız CEVAP run'ında çıkar. Plan-review kararı port'un bildirdiği istek tipine bağlanır (`WorkflowRequestDescriptor.IsPlanReview`); `ChatMessage` için de aynı. Sınıf taraması: `TryGetDataAs` çağıranları (`Prompt`, `Create`). Console bir kayıt tipini yalnız tip adıyla gösterir; insana gösterilecek metin `string` istek tipidir.
- **Süre dolumu servisi yalnız `Failed` kapanışında `run.failed` yayınlar** (2026-10-15, Faz 211, K-1011): cevaplanan isteğin run'ı süre dolunca `Completed` kapanır (sık yol); o kapanışta olay yayınlamak aynı üretim için ikinci bir `run.completed` olurdu. Yük `WorkflowRunWebhooks.RunPayload`'dan gelir (runner ile ortak); yayın `CancellationToken.None` ile, hata loglanır ve kapanışı bozmaz.

## Kod grafı: onay ve revizyon döngüsü (Faz 211)

- **🚨 Geri kenara dönen mesaj `AIAgentBinding`'i koşturmaz; kod grafında agent `GetWorkflowAgentStep` ile bağlanır** (2026-10-15, Faz 211, K-1007, K-495'in kod grafı yüzü): `GetWorkflowAgent` giriş dışında kalan bir düğümde TurnToken beklediği için run çıktısız biter. `GetWorkflowAgentStep` düz bir `FunctionExecutor`'dır (`WorkflowAgentStepExecutor`, kimlik = agent adı); mesaj nereden gelirse gelsin koşar. Bedeli: MessageDelta yaymaz, MAF oturumu yoktur, revizyon girdisi taslağı ve notu kendisi taşır. Adım `agent.RunAsync` çağırır (akışsız, K-1008): yapılandırılmış yanıt onarımı çalışır; alt run satırı `IsStreaming = false` yazar ve akışlı yola göre bir `MessageCompleted` olayı fazladır (ölçüldü).
- **🚨 Revizyon sayacı executor alanında tutulursa her cevap run'ında sıfırlanır** (2026-10-15, Faz 211, ölçüldü): her cevap yeni bir run'dır ve kod grafı yeniden kurulur. `IWorkflowContext.QueueStateUpdateAsync/ReadStateAsync` durumu checkpoint'te tutar; cevap run'ı onu geri okur (`CodeWorkflowRevisionTests`). Güncelleme yazan executor için hemen, diğerleri için sonraki super-step'ten itibaren görünür.
- **Kod grafı yapısı değişirse bekleyen run sürmez, zaman aşımı/ayar değişirse sürer** (2026-10-15, Faz 211, K-901'in kod yüzü; `CodeWorkflowRevisionTests`): düğüm ekleme checkpoint'i reddettirir (`Failed`); `GetWorkflowAgentStep` `Timeout`'u ve `AgentVersion` kimliğe girmez (önbellek anahtarındadır).
- **Kod `RunTimeout`'u tavana karşı AÇILIŞTA doğrulanır** (2026-10-15, Faz 211, K-1010): kayıt yapılandırma okunmadan yazıldığı için doğrulama `TraconWorkflowOptionsValidator`'dadır (`ValidateOnStart`, yalnız `Enabled` iken); tavan sonradan düşerse run tavana kesilir ve loglanır (saklanan tanımla aynı). Katılımcı kapsaması (`RequireParticipantCoverageAsync`) kod grafında yoktur: adımlar graf kurulmadan bilinmez, eksik zorunlu değeri adım koşarken `ChildAgentInvoker` adıyla düşürür.

