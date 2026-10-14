# Workflow Cevaplari — Tek Cevap, Cevap Kaydi, Eksik Is

> Bekleyen bir workflow isteginin cevabi: `WorkflowResponseMode`, cevap kaydi
> (`workflow_responses`), izleme akisi ve fail-closed `workflow.respond`.
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

## Tuzaklar

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
