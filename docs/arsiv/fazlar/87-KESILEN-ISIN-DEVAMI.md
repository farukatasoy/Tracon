# Faz 87 — Kesilen İşin Devamı

> **Durum:** ✅ Tamamlandı (2026-08-23)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-141** — Dalga 14 Küme D
> **Önkoşul:** [Faz 46](46-DAYANIKLI-CALISTIRMA.md) — iş kuyruğu ve `202 Accepted` sözleşmesi · [Faz 47](47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md) — `RecordedToolPlayback` defteri · [Faz 54](54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md) — öksüz uzlaştırma, tetikleyicinin takılacağı yer · [Faz 55](55-ASENKRON-ONAY-KUTUSU.md) — `ApprovalResume` emsali · [Faz 44](44-HATA-SINIFLANDIRMA.md) — tipli sağlayıcı hataları (geçici/kalıcı ayrımı metin eşleştirmesi **gerektirmez**)
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.Workflows`, `Tracon.Sql.Shared` (linked-source, K-176), `Tracon.AspNetCore`
> **Yeni paket:** Yok · **Migration:** **Gerekli** — `runs` tablosuna nullable bir "hangi koşudan devam" kolonu. Üç set (PostgreSQL · SQL Server · SQLite); numaralar uygulama anında alınır (K-178)
> **Public API:** Büyüyor — 1 enum üyesi, 1 ayar bölümü, 1 tool alanı, 1 workflow retry politikası. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**
> **Tüketici yüzeyi:** `docs-site/` → `guides/reliability.md`, `guides/background-work.md`, `concepts/runs.md`, `concepts/workflows.md`, `reference/configuration.md`, `capabilities.md`
> · sevk edilen: yeni ayar ve tool alanının XML dokümanı, `src/Tracon.Core/README.md`. `api/` ve `http-api/` **üretilir**
> **Manuel test alanı:** [`docs/manuel-test/21-DAYANIKLILIK-VE-IPTAL.md`](../../manuel-test/21-DAYANIKLILIK-VE-IPTAL.md) · [`docs/manuel-test/15-WORKFLOWS.md`](../../manuel-test/15-WORKFLOWS.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/87-KESILEN-ISIN-DEVAMI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Dayanıklılık bugün **elle bir düğmeye** bağlıdır. Süreç yeniden başladığında (deploy, çökme, ölçek olayı) koşu kaybolur ve uzlaştırma onu yalnız **işaretler**. Kesilen bir workflow için `resume` ucu vardır ama **elle** çağrılır. Bir düğüm geçici bir sağlayıcı hatasıyla düşerse **tüm koşu** düşer.

## Bitiş Ölçütleri (DoD)

- [x] **Drain önce teslim edilir**: `TraconDrainService` en son hosted-service olarak kayıtlı (ters sırada durma), `JobWorkerBackgroundService` ve `DrainGate` yeni işi reddeder, `Timeout` sonunda kapanır. `TraconDrainServiceTests` (5) + `DrainTests` (4, gerçek HTTP host) + örnek uygulamada gerçek `SIGTERM` ile doğrulandı (temiz kapanış, hata yok)
- [x] Ayar **kapalıyken** hiçbir davranış değişmez — `Disabled_by_default_orphaned_run_stays_Failed_with_no_continuation` bunu HTTP seviyesinde kanıtlıyor
- [x] Ayar açıkken oturumlu bir öksüz koşu `JobKind.RunContinuation` olarak kuyruğa girer — `Continuation_replays_the_completed_call_and_runs_the_new_one_live`
- [x] Devam koşusunda kesintiden **önceki** tool çağrıları yeniden çalışmaz; **sonrakiler** canlı çalışır — aynı testte `ContinuationProbeTools.Calls == 1` (yalnız kesinti sonrası çağrı) ölçüldü; `RecordedToolPlaybackTests` (5) mekanizmayı izole doğruluyor
- [x] Replay'in `422` ile kesme davranışı **değişmedi** — `RecordedToolPlaybackTests.Stop_policy_records_the_mismatch_and_never_runs_the_real_body` + `RunReplayEndpointTests` (10, tümü geçti — regresyon yok)
- [x] `Destructive` **ve** `External` taşıyan koşular devam etmez; `SafeToRepeat` bildiren tool devam eder — `A_destructive_tool_call_blocks_continuation_and_records_why`, `A_tool_declaring_SafeToRepeat_allows_continuation_despite_a_destructive_effect`
- [x] `MaxAttempts` sonsuz zinciri kapatır — `MaxAttempts_stops_a_second_continuation_in_the_same_chain`
- [x] Workflow düğümü geçici hatada yeniden denenir, kalıcı hatada denenmez — `WorkflowNodeRetryTests` (6), Faz 44'ün `RunErrorClass`'ı kullanılır, metin eşleştirmesi yok
- [x] Süper adım sayımı **ölçüldü** ve karar dokümana yazıldı — K-587; `A_transient_error_in_a_function_node_is_retried_and_costs_no_extra_super_step` iki GERÇEK koşumu (`flaky`/`baseline`) karşılaştırarak kanıtladı
- [x] Üç migration seti yazıldı; `RunStoreContract` üçünde geçti — `ContinuedFromRunId_round_trips` ve `ClaimOrphanedRunsAsync_reports_ContinuedFromRunId` dört depoda (bellek-içi + üç SQL) geçti
- [x] Devam bağı koşu ağacında görünür — API (`continuedFromRunId`, OpenAPI'de doğrulandı) ve arayüz (`run-detail.tsx`, `MT-RES-067`, 57 E2E testi geçti)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format`, arayüz DAHİL, tekrar tekrar koşuldu
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıdaki "Örnek Uygulama Doğrulaması" bölümü
- [x] `secret` taraması boş döndü — bu fazın dokunduğu dosyalarda; repodaki önceden var olan yerel test `Password=`/`sk-` literalleri bu fazdan bağımsızdır (Faz 79/80/81 emsaliyle aynı kapsam)
- [x] Manuel kabul case'leri `docs/manuel-test/21-*` ve `15-*` içine eklendi (`MT-RES-060`–`068`, `MT-WF-117`–`118`); otomatikleştirilebilir olanlar (birim/fonksiyonel test karşılıkları) koşuldu, tam elle koşum ayrı bir `manuel-test-kosumu` oturumunu bekliyor
- [x] `faz-denetim` koşuldu; 🔴 bulgu (arayüz bağı eksikti) **kapatıldı** — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi — **argüman eşleşmesi sınırı açıkça yazıldı** (`guides/reliability.md`: "matching is by the exact recorded arguments"); dört site kapısı (`check:content`/`build`/`check:links`/`check:weight`) ve `dokuman-bakim.py --site-denetle` yeşil
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — `runDetail.continuationOf` ikisinde de, gerçek çeviri (kopya değil); bundle **175.0 KB gzip** (bütçe 250 KB, faz öncesi de yakın değerdeydi — bu fazın eklediği tek satır ölçülebilir bir artış yaratmadı)

### Doğrulama komutları

```bash
# Devam kosusu kaynagi gosteriyor mu
curl -s http://localhost:5081/tracon/api/runs/$RUN_ID | jq '.continuedFromRunId'

# Kesinti sonrasi tool cagrilarinin hangisi oynatildi
curl -s http://localhost:5081/tracon/api/runs/$RUN_ID/tools | jq '.[] | {toolName, source}'

# Drain: SIGTERM sonrasi acik kosu tamamlaniyor mu
kill -TERM $PID && sleep 1 && curl -s .../api/runs/$RUN_ID | jq -r '.status'
```

### Örnek Uygulama Doğrulaması (2026-08-23, gerçek koşum)

`samples/Tracon.Api`, gerçek PostgreSQL'e (`AutoApplyMigrations: true`)
karşı `dotnet run -c Release` ile ayağa kaldırıldı:

- Açılış günlüğünde `Tracon applied 1 migration(s). Schema: tracon.` —
  `0037_run_continuation.sql` gerçek bir veritabanına GERÇEKTEN uygulandı
  (sözdizimi/izin hatası yok).
- `GET /tracon/api/meta` → `200`.
- Arka plan iş işçisi gerçekten çalışıyor: günlükte `tracon.jobs` üzerinde
  gerçek `UPDATE ... FOR UPDATE SKIP LOCKED` sorguları görüldü.
- `kill -TERM $PID` ile temiz kapanış doğrulandı: `Application is shutting
  down...` yazıldı, hata/istisna YOK (Drain varsayılan kapalı — süreç anında
  çıktı, K1 ile tutarlı).
- `Drain`/`RunContinuation` ayarları örnek uygulamada AÇILMADI (varsayılan
  kapalı kalması K1'in kendisidir); bu iki ayarın uçtan uca gerçek-model
  davranışı `RunContinuationTests`/`DrainTests`'in gerçek ASP.NET Core
  host'unda (sahte model sağlayıcısıyla, gerçek job worker ve gerçek arka
  plan servisleriyle) doğrulandı — DoD'un istediği "gerçek run" kanıtı bu
  ikisinin BİRLEŞİMİDİR: gerçek süreç + gerçek veritabanı (migration ve
  kapanış için), gerçek HTTP boru hattı + gerçek arka plan servisleri
  (davranış için).

---

## Plandan Sapmalar

- **Skill/çağrılabilir alt-agent kullanan agent'lar devam ETTİRİLEMEZ (K-586).**
  Plan bunu öngörmüyordu. `RecordedToolPlayback` yalnız standart tool-çağırma
  boru hattını sarmalar; skill script'leri ve alt-agent çağrıları
  `AIContextProvider` üzerinden çalışır ve bu boru hattını hiç görmez. Devam
  koşusunda bu ikisi ya sessizce GERÇEKTEN yeniden çalışırdı (yanlış — kesinti
  öncesi bir skill/alt-agent çağrısı tekrar para/yan etki üretirdi) ya da hiç
  ele alınmazdı. Güvenli seçenek: `SkillNames`/`CallableAgentNames` dolu olan
  bir agent'ın devamı `RunContinuationJobHandler`'da açıkça reddedilir.
- **`ContinuedFromRunId`'nin SQL ordinal konumu plandaki varsayımdan farklı
  çıktı.** İlk deneme ordinal 42'yi boş sanıyordu; gerçekte `ReadUsage`/
  `ReadCost`/`ReadTreeUsage`/`ReadTreeCost` yardımcı okuyucularına dağılmış
  Faz 68 alanları (`cached_input_tokens` vb.) o aralığı ZATEN dolduruyordu.
  Gerçek boş ordinal **52**'ydi — `SqlRunStore.cs`'yi okuyarak (varsaymadan)
  bulundu. `docs/hafiza/sql-saglayicilari.md` güncellendi.
- **`TraconDrainService`'in "yeni koşu kabul edilmez" kapsamı, plandakinden
  DAR tutuldu — bilinçli bir kapsam sınırlaması, eksiklik değil.** Doğrudan
  HTTP çalıştırma uçları (senkron/akışlı ve kuyruklu) ve arka plan iş
  işçisinin yeni iş kiralaması kapsanır. Workflow/toplu/tetikleyici giriş
  noktaları AYRICA kapsanmadı — gerekçesi: bunların hepsi ZATEN aynı dayanıklı
  iş kuyruğundan geçer ve `JobWorkerBackgroundService`'in kendisi drain
  sırasında yeni iş kiralamayı DURDURUR; kuyruğa giren bir iş kaybolmaz,
  yalnız süreç yeniden başlayana kadar bekler — bu, DoD'un asıl ölçtüğü acıyı
  (senkron/akışlı bir isteğin sunucu kapanınca bağlantısının KOPMASI) zaten
  kapatır.
- **`TraconDrainService`'in hosted-service DURMA sırası ölçülmedi, yalnız
  belgelenen .NET davranışına DAYANDIRILDI** (jenerik host, hosted service'leri
  KAYIT sırasının TERSİNDE durdurur). Kayıt `AddTracon()`'in son satırıdır;
  bu, `TraconDrainService.StopAsync`'in `JobWorkerBackgroundService`'ten
  ÖNCE çalışmasını GARANTİ eder ama gerçek bir çok-servisli entegrasyon testiyle
  ÖLÇÜLMEDİ (yalnız kod okumasıyla doğrulandı). Gerçek bir davranış sapması
  bulunursa `docs/hafiza/aspnetcore-di.md`'ye not düşülür.

## Bu Fazda Verilen Kararlar

- **K-583** — `RunContinuation`, `Replay` (K-315) ve `ApprovalResume` (K-368)
  ile karıştırılmayan üçüncü, ayrı bir işlemdir.
- **K-584** — `Destructive`/`External` tool'lar devamı varsayılan olarak
  engeller; gevşetme bir ayarla değil, tool'un kendi `SafeToRepeat`
  bildirimiyle yapılır.
- **K-585** — Eşleşmeme politikası `RecordedToolPlayback`'in kurucu
  parametresi olarak eklendi; `ReplayToolMode`'a yeni üye açılmadı.
- **K-586** — Skill/çağrılabilir alt-agent kullanan agent'lar devam
  ettirilemez (plan dışı, uygulama sırasında ölçülüp keşfedildi).
- **K-587** — Workflow düğüm retry'ı yalnız fonksiyon düğümlerini kapsar;
  süper adım sayımı ÖLÇÜLDÜ (retry döngüsü sayacı etkilemez).

Tam gerekçeler: `docs/KARARLAR.md` K-583–K-587.

## Denetim Bulguları

`faz-denetim` bir kez koşuldu (taze bağlamlı `general-purpose` agent, tam
`git diff HEAD`).

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Devam bağı arayüzde hiç yazılmamış — plan "Arayüz payı" bölümünde bunu kod teslimatı olarak tanımlıyordu | 🔴 | **Düzeltildi** — `run-detail.tsx`'e bağ eklendi, `en.ts`/`tr.ts` çevrildi, tam arayüz kapısı (`npm run check`, 57 E2E testi) koşuldu |
| 2 | `ClaimOrphanedRunsAsync`'in okuma yolu (`ReadOrphanedRun`, ordinal 23) hiç test edilmemiş — `MaxAttempts` zincir-sayımının dayandığı alan | 🟡 | **Düzeltildi** — `RunStoreContract.ClaimOrphanedRunsAsync_reports_ContinuedFromRunId_on_the_claimed_record`, dört depoda geçti |
| 3 | `RunContinuationStoreFailureTests` (kuyruk yazamazsa devam durur, koşu Failed kapanır) yazılmamış | 🟡 | **Düzeltildi** — `RunReconciliationTests.Continuation_store_failure_is_logged_and_the_placeholder_closes_to_Failed` |
| 4 | `RunContinuationConcurrencyTests` (iki uzlaştırıcı örneği aynı öksüz koşuyu görebilir) yazılmamış | 🟡 | **Düzeltildi** — `RunReconciliationTests.The_same_orphaned_run_is_never_continued_twice_by_two_concurrent_reconcilers`, 5 kez tekrar koşularak kırılganlık denetlendi |
| 5 | `ITraconDrainState` planın "Planlanan Public API" bölümünde yoktu | 🟡 | **Gerekçelendi** — bu bölümde ve K-583–587'de kaydedildi; mimari olarak gerekli (Core'daki drain durumunu AspNetCore'a taşımanın tek yolu, `IRunCancellationRegistry` emsaliyle tutarlı) |
| 6 | `ToolEffect.External`'ın `Destructive` ile aynı kod yolunu paylaştığına dair ayrı bir test yok | 🟢 | **Devredilmedi** — kapsam çok dar (`Effect is Destructive or External` tek satırlık `or`); gerekli görülürse gelecekte eklenir |

Denetimden sonra dört kapı (`build`/`test`/`pack`/`format`, arayüz dahil)
yeniden koşuldu; 🔴 kalmadı.

## Sonraki Faza Devir Notu

- **Devraldığı sözleşmeler** (birebir imza): `ITraconDrainState.IsDraining`
  (`Tracon.Abstractions`) — yeni bir HTTP giriş noktası "yeni koşu"
  sayılıyorsa `DrainGate.Check(drainState)`'i kendi kontrol noktasına ekle.
  `WorkflowNodeRetryPolicy` — yalnız `AddWorkflowFunction`'a bağlıdır, diğer
  dört workflow desenine (Concurrent/Handoff/GroupChat/Magentic) UYGULANAMAZ.
  `TraconToolRegistration.SafeToRepeat`/`TraconToolAttribute.SafeToRepeat`
  — yalnız `Destructive`/`External` etkili tool'larda anlamlıdır.
- **🚨 Bilinen tuzaklar:**
  - `RunRecord`/`RunStartInfo`/`TraconRunOptions` üçlüsüne yeni bir lineage
    alanı eklenirken (`ReplayOfRunId`/`ContinuedFromRunId` deseni) üçü de
    GÜNCELLENMELİDİR — `RunRecordingAgent.WriteRunStartAsync`'in `RunStart`
    positional record'u dördüncü bir taşıyıcıdır, unutulması sessizce alanı
    `null` bırakır.
  - `runs` tablosunun ordinal-okunan sütun sırası artık **52**'de bitiyor
    (Faz 68'in 51'i değil). Yeni bir sütun eklerken `SqlRunStore.ReadRun`'ı
    OKUYARAK doğrula, plandaki "sıradaki boş ordinal" iddiasını asla varsayma
    — yardımcı okuyucular (`ReadUsage`/`ReadCost`/`ReadTreeUsage`/`ReadTreeCost`)
    ordinalleri dağıtır.
  - Skill/çağrılabilir alt-agent kullanan bir agent hiçbir "devam" veya
    benzeri otomatik-tekrar mekanizmasına GİREMEZ (K-586) — bu boru hattı
    henüz `RecordedToolPlayback`'in sarmaladığı tool-çağırma yoluna dahil
    değil.
- **Yarım kalan işler:** Skill/alt-agent replay'i genişletmesi (K-586'nın
  bıraktığı boşluk) `docs/ADAYLAR.md`'ye aday olarak yazılabilir. Diğer dört
  workflow deseni için retry (K-587'nin bıraktığı boşluk) ayrı bir araştırma
  gerektirir. `TraconDrainService`'in hosted-service durma sırası yalnız
  kod okumasıyla doğrulandı — gerçek bir çoklu-servis entegrasyon testi
  yazılmadı (Plandan Sapmalar'da not düşüldü).
- **Sıradaki faz:** [Faz 88 — Görsel Üretim Tool'u](88-GORSEL-URETIM-TOOLU.md).
  O fazın kendi dokümanı ZATEN bu fazın `ToolEffect.External` kısıtını
  (88.2) doğru şekilde öngörüyor — ayrıca bir güncelleme gerekmedi.
