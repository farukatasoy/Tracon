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
- **🚨 Satir kapaninca run kapatilamazsa o satir bir daha bulunmaz** (K-1003):
  `ExpireAsync` satiri once kapatir. Servis kapatilamayan run'i bellekte tutar ve her
  turda en cok 10 kez dener; surec arada durursa run `AwaitingInput` kalir.
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
