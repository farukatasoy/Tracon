# Faz 107 — Run Kayıt Akışı Ayrıştırma

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`arsiv/kesif/2026-08-23-yapisal-sorun-envanteri.md`](../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 106](106-AGENT-DERLEYICI-AYRISTIRMA.md) — teknik zorunluluk yoktur; yapısal tur compiler'dan runtime wrapper'a ilerler
> **Paketler:** `Tracon.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `RunRecordingAgent` constructor ve davranışı değişmez; `PublicAPI.Shipped.txt` girdisi bugün **0**
> **Tüketici yüzeyi:** Yok. Public imza, XML metni ve observable contract değişmez
> **Manuel test alanı:** [`manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](../../manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md) · [`manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show c101130:docs/arsiv/fazlar/107-RUN-KAYIT-AKISI-AYRISTIRMA.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`RunRecordingAgent`, iki yürütme gövdesini, ambient scope/span yaşamını, store yazımını, event sink yayınını, maliyeti, kotayı, online eval sampling'i ve hata dönüşümünü 1.331 satırda toplar. Faz bu sorumlulukları `partial` dosyalara ayırır. Span ve `AsyncLocal` yazım yerlerini değiştirmez.

## Bitiş Ölçütleri (DoD)

- [x] Entry-point gövdeleri span/scope yazımını kendi gövdelerinde tutar
- [x] `AmbientWriteSiteTests` yazım yeri sayısının artmadığını kanıtlar
- [x] Outcome matrisi streaming ve non-streaming dört sonucu kapsar
- [x] Store/sink failure testleri run davranışının bozulmadığını gösterir
- [x] `git diff -- 'src/*/PublicAPI.*.txt'` boş döner
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili ailelere eklendi ve otomatik olanlar koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Plandan Sapmalar

Plana büyük ölçüde birebir uyuldu. Dosya adları (`RunRecordingAgent.Lifecycle.cs`,
`.Persistence.cs`, `.Completion.cs`, `.Notifications.cs`) planla aynen eşleşti.
Plan dört dosyanın sorumluluğunu düz yazıyla anlatıyordu, hangi metodun hangi
dosyaya gideceğini vermiyordu; uygulama sırasında şu eşleme yapıldı:

- `Lifecycle.cs`: `PrepareRun`, `CreateScope`, `ResolveAttribution` ve
  `RunStart`/`RunScope` `record` tipleri. `ResolveAttribution` plandaki dört
  bulletten hiçbirine açıkça girmiyordu; `PrepareRun`'ın kendi gövdesinden
  çağrıldığı ve aynı "run hazırlama" sorumluluğunu taşıdığı için buraya kondu.
  İki `record` tipi de burada kaldı — `PrepareRun`/`CreateScope`'un ürettiği
  veri sözleşmesi oldukları için üretildikleri dosyada durmaları doğal.
- `Persistence.cs`: `WriteRunStartAsync`, `WriteDocumentAttachedEventsAsync`,
  `SaveInputAsync`, `WriteContentsAsync`, `WriteToolCallAsync`,
  `WriteToolResultAsync`, `FormatArguments`, `GetSessionId`, `ExtractQuery`,
  `IsDocumentChannelMessage`.
- `Completion.cs`: `CompleteAsync`, `ApprovalError`,
  `InvokeBeforePendingApprovalAsync`, `MergeUsage`, `AddOrNull`, `ToRunUsage`,
  `ToRunError`.
- `Notifications.cs`: `RecordQuotaAsync`, `SampleForOnlineEvalAsync`,
  `PublishRunEventAsync`.

`RunRecordingAgent.cs` constructor, `RunCoreAsync`, `RunCoreStreamingAsync`
dışında hiçbir gövde taşımadı — 1331 satırdan 464 satıra indi.

Taşıma sırasında hiçbir `using`/pragma tahminle eklenmedi; Faz 106'nın devrettiği
teknik izlendi — önce hiç `using` eklemeden taşı, sonra `dotnet build` çalıştır.
`CS0246` iki dosyada eksik `using Microsoft.Agents.AI;`/`Microsoft.Extensions.Logging;`
gösterdi, ilk denemede düzeltildi; `IDE0079`/`MAAI001` hiç çıkmadı (dosya bugün
hiç pragma taşımıyordu, plan bunu doğru öngörmüştü).

107.3'ün istediği outcome matrisi `RunRecordingAgentOutcomeMatrixTests.cs`
olarak eklendi; dört sonucun (Completed, Failed, Canceled, AwaitingApproval)
ikisi (Canceled, AwaitingApproval) `RunEventWriter.CompleteAsync`'in **var
olan** davranışı gereği kapanış olayı olarak `RunEventType.RunFailed` yazıyor
— bu Faz 107'nin ürettiği bir şey değil, dokunulmayan `RunEventWriter.cs`'in
önceden beri sahip olduğu bir quirk (default `switch` kolu). Matris bu
gerçek davranışı PİNLEDİ, "doğru" olanı değil — düzeltme bu fazın kapsamı
dışında.

## Bu Fazda Verilen Kararlar

Yok. Faz saf bir kod taşıma işiydi; public API/compatibility contract,
güvenlik/kiracı sınırı veya kalıcı veri kararı gerektiren bir seçim yapılmadı.

## Denetim Bulguları

Taze bağlamlı bir `general-purpose` agent, `faz-denetim` skill'ini uygulayarak
koştu (2026-08-26). Yöntem: silinen bloğun tam metni (`git diff`) her yeni
partial dosyanın içeriğiyle satır satır karşılaştırıldı — hepsi karakter
düzeyinde birebir taşınmış bulundu; `dotnet build src/Tracon.Core` üç
hedefte de bağımsız olarak yeniden koşuldu.

**🔴 ve 🟡 yok.**

**🟢 (aday listesine, F-164 olarak eklendi):** Örnek uygulamada
(`samples/Tracon.Api`) `RunTraceCollector` span örneklemesi hiç
tutmuyor — Faz 107'nin dokunmadığı bir alan (DI kaydı/config bağlama),
kod diff'iyle ilgisiz.

## Sonraki Faza Devir Notu

- Aynı taşıma deseni Faz 108'de (`InMemoryRunStore` ayrıştırması) tekrar
  geçerli: önce hiç `using` eklemeden taşı, `dotnet build` derleyiciye
  eksik/gereksiz `using`'i söyletsin.
- `AmbientWriteSiteTests`'in `<path>:<method>` anahtarı dosya taşımasına
  duyarlıdır — bir metot yeni bir `partial` dosyaya taşınırsa baseline'da
  yalnız o satırın **yolu** değişir, sayısı değişmez;
  `TRACON_AMBIENT_WRITE_REFRESH=1` ile yenile ve `REPLACE ME` yer
  tutucusunu eski satırdaki gerekçeyle değiştir — otomatik yenileme
  gerekçeyi KORUMAZ, yalnız yeni yolu placeholder'la yazar.
- Örnek uygulamada (`samples/Tracon.Api`) span örneklemesi
  (`RunTraceCollector`/`ITraceStore`) ~45 ayrı denemede hiç tutmadı —
  `SuccessSampleRatio=1` ortam değişkeni de etkisizdi. Kök neden
  ölçülmedi; Faz 107'nin dokunmadığı bir alan (DI kaydı/config bağlama).
  Otomatik testler (`ObservabilityTests`) aynı mekanizmayı izole biçimde
  doğru çalıştırıyor, yani bu örnek-uygulamaya özgü bir sorun — bir
  sonraki oturum `RunTraceCollector`'ın örnek uygulamada gerçekten
  `IsCollecting=true` olup olmadığını ölçebilir. Ayrıntı:
  `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` MT-OBS-050.
