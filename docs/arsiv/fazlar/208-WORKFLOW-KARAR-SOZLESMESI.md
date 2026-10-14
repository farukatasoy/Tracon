# Faz 208 — Workflow Karar Sözleşmesi: Tek Yanıt, Karar Denetimi ve Kaynak Bağı

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** Bakımcı, 2026-10-14 — iki faz, `workflow_responses` tablosu, her `respond` fail-closed denetim, K-130 varsayılanı korunur; Açık Sorular 1-10 öneriyle
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-319** (karar yarısı; gelen kutusu ve süre [Faz 209](../../209-WORKFLOW-GELEN-KUTUSU-VE-SURE.md))
> **Genellik:** Tracon HITL'ini kullanan her host; tüketici geri bildirimi kuralı uygulandı (video tüketicisi, 2026-10-11). İlk ilke: çift `respond` iki dal açar ve tüketici yarışı kapatamaz; workflow onayı tool onayı gibi denetlenmiyordu
> **Karşı görüş:** "Onay tüketicinin ekranında tutulabilir." Ekran tüketicide kalır; iki isteğin yarışı ve denetim yalnız uygulayan katmanda kurulur. Mod opt-in
> **Önkoşul:** [Faz 205](205-GEC-BITEN-HARCAMA-KOTASI.md) — `WorkflowRunner.cs` ortak dosya
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Testing.Contracts.Xunit`, `.UI`, `Tracon.Client` (üretilen)
> **Yeni paket:** Yok · **Migration:** gerekli, üç sağlayıcı (`workflow_responses`); numara uygulama anında alınır
> **Public API:** büyüyor, `1.0.0` GA'dan (K-603) önce ucuz — ölçüldü (2026-10-14): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** gerçekleşen liste *Tüketici yüzeyi envanteri*nde
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md` (`MT-WF-*`), `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` (`MT-SEC-*`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 36c4af29:docs/arsiv/fazlar/208-WORKFLOW-KARAR-SOZLESMESI.md
> ```
>
> Damıtıldı 2026-10-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir workflow insan cevabı beklediğinde, bugün aynı isteğe gelen her cevap yeni bir dal açar. Çift tıklama, HTTP yeniden denemesi ve iki operatör aynı kararı iki kez uygular. Cevap hiçbir yere yazılmaz: "kim neyi onayladı" sorusunun cevabı yoktur, devam run'ı da hangi isteğe cevap olduğunu bilmez. Bu faz kararı bir **sözleşme** yapar.

## Bitiş Ölçütleri (DoD)

- [x] `Single` modda iki eşzamanlı aynı cevap tek devam run'ı açar; ikinci yanıt aynı devamı izler (akışlı `200` SSE, kuyruklu aynı `202`) — `WorkflowSingleResponseTests` (mutasyonla; bkz. *Önce kırmızı kaydı*), örnek uygulamada ölçüldü
- [x] `Single` modda zıt cevap `409` alır; gövde devam kimliği ve öbür cevabı taşımaz; yetkisiz çağıran `403`'te kalır — `WorkflowSingleResponseTests`, örnek uygulama
- [x] `Branch` modda (varsayılan) ikinci cevap hâlâ yeni dal açar; mevcut workflow testleri değiştirilmeden yeşil — `Branch_mode_still_opens_a_branch_for_every_answer`; `Branch` davranış değişiklikleri `CHANGELOG` Changed'de
- [x] Teslimden önce düşen devamda aynı cevap yeni devam açar; tamamlanan ya da koşan devam yeniden sürülmez — `WorkflowSingleResponseTests`, `WorkflowResponseOwnershipTests` (6-A, kuyruk işi sahibi, eksik iş)
- [x] Denetim deposu düşükken `respond` `500` döner; satır, run ve iş yoktur (akışlı ve kuyruklu) — `FailClosedAuditTests` iki case
- [x] Denetim kaydı aktör, `sourceRunId`, `requestId`, `responseRunId`, 64 karakter hash taşır; ham cevap yok — `WorkflowResponseRecordTests`, örnek uygulama
- [x] `AuditWritePolicyTests` 7 işlem / 10 çağrı yeri; `IAuditLog` XML ve site yerleri "seven" der (sekiz yer, `threat-model` ve `security-policy` dahil); K-776'yı değiştiren satır `KARARLAR.md`'de (K-996)
- [x] `WorkflowResponseStoreContract` bellek + üç SQL sağlayıcısında yeşil; `TenantIsolationContract` yeni depoyu kapsar — 15 case
- [x] `WorkflowResponseApplied` olayı `RecordToolPayloads = false` iken de kaynak run'ı taşır — `WorkflowResponseLinkTests`
- [x] K-929 satırı kodla uyumlu yazıldı (gerekçe düzeltmesi, 2026-10-14)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban 6b484f0c` (bkz. *Kapanış kapısı*)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1-4), çıktı belgeye yazıldı — üç koşum (*Örnek uygulama koşumu*)
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅ (17 işaretli sentetik credential atlandı)
- [x] Manuel kabul case'leri `docs/manuel-test/15-WORKFLOWS.md` (`MT-WF-156…164`) ve `13-KIRACI-VE-GUVENLIK.md` (`MT-SEC-219…221`) içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` iki tur koşuldu; 🔴 bulgu kalmadı (*Denetim Bulguları*)
- [x] `docs-site/` güncellendi (Tüketici yüzeyi satırı + yeni `concepts/workflow-answers.md`); `npm run check` temiz
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı gzip KB olarak ölçüldü ve yazıldı — 209.0 → 209.8 KB (§208.7 #4)

### Doğrulama komutları

`kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*WorkflowSingleResponseTests" "*WorkflowResponseOwnershipTests" "*FailClosedAuditTests"` · `--proje Tracon.Core.UnitTests --sinif "*AuditWritePolicyTests" "*RunEventTypeTests"`.


---

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir. §208.7'nin beş ölçümü buraya yazılır.

### §208.7 ölçümleri (2026-10-14, worktree `faz-208`, taban `c5c820a7`)

Hiçbir ölçüm onaylı bir öneriyi düşürmedi.

| # | Sonuç | Karar |
|---|---|---|
| 1 | MAF 1.20.0 kaynağı (ilspy): `RequestInfoExecutor.HandleAsync(object)` ve Magentic `_planReviewPort.PostRequestAsync(request)` kimlik vermez; `ExternalRequest.Create` `Guid.NewGuid().ToString("N")` üretir. Çalışma anı probu `WorkflowRequestIdentityTests` (kalıcı test): aynı oturumda ikinci soru yeni kimlik alır, aynı oturumdaki dal üçüncü kimliği alır, düz `/resume` bekleyen isteği **aynı** kimlikle yeniden yayınlar | Öneri A geçerli: `claim_scope = session_id`. Kenar: ajan host'unun tool onayı isteği (`AIContentExternalHandler`) kimliği `{uzunluk}:{portId}:{contentId}` biçiminde modelin çağrı kimliğinden türetir; sağlayıcı aynı oturumda aynı çağrı kimliğini tekrar verirse iki istek çakışır. Tracon workflow'unda bu yol bugün yayınlanmıyor; risk olarak kaydedildi |
| 2 | Aynı run için canlı akış ile depo karşılaştırıldı (geçici prob, silindi). İki fark: (a) canlı `RunStarted` `Text = workflow adı`, `Payload = oturum`; depoda ikisi de `null`. (b) Canlı kapanış olayının `Sequence`'i `EventCount`'tur, depodakinden bir fazladır. Ayrıca motorun grafın dışında yazdığı olaylar (fonksiyon düğümünün tool kaydı, kota bildirimi) yalnız depodadır | `RunEventTail` iki akışın tek okuyucusu oldu. İzleme akışı (a)'yı run kaydından kapatır; (b) ve depo fazlası belgelendi (`WorkflowFollowStream` remarks). `WorkflowResponseApplied` canlı akışta da `yield` edilir |
| 3 | Kısmi benzersiz indeks `WHERE claim_scope IS NOT NULL AND status <> 2`: PostgreSQL 18 (23505), SQL Server 2022 (2601; `QUOTED_IDENTIFIER ON` gerekir — `sqlcmd` varsayılanı OFF, SqlClient ON; `0001_initial`'daki filtreli indeksler aynı koşulda), SQLite 3.54 (UNIQUE constraint). Üçünde de çift aktif satır reddedildi, `status = 2` olunca yer boşaldı, `NULL` scope sınırsız yazıldı | Geri dönüş (`superseded_by`) gerekmedi |
| 4 | Konsol bundle'ı (`npm run build`, `postbuild.mjs`): faz öncesi **209.0 KB gzip**, faz sonrası **209.8 KB gzip** (bütçe 250 KB; pay +0.8 KB) | Arayüz payı ölçüldü |
| 5 | `DataSubjectCoverageTests` `REFERENCES {schema}.runs(id) ON DELETE CASCADE`'i kapsama sayar (`pending_approvals` ile aynı). Sözleşme case'i `Erasing_the_source_run_removes_its_answers` kaynak run'ı gerçek `SqlDataSubjectStore.EraseAsync` ile siler; üç sağlayıcıda satır gider | Yeni hedef eklenmedi. Dışa aktarımda tablo yoktur (`pending_approvals`, `run_events` emsali): satır kimlik, hash ve aktör taşır |

### Örnek uygulama koşumu (2026-10-14)

`samples/Tracon.Api`, geçici SQLite deposu (`Tracon__Sqlite__ConnectionString` scratchpad'e; ortak PostgreSQL bağlantısı ortam değişkeniyle boşaltıldı), port 5281, gerçek model çağrısı (`summarizer`). Açılışta 47 SQLite migration uygulandı (`0047_workflow_responses` dahil). Sonuç:

| Senaryo (manuel case) | Gözlem |
|---|---|
| Katalog | `summarize-and-approve` → `Branch`, `summarize-and-approve-once` → `SingleAnswer` |
| Çift tıklama, akışlı (1) | İki `200`; ikisi de aynı devamı adlandırır; izleyen akış `run` → `event`… → `done`; devam `Completed`; `…/responses` tek satır (`Delivered`, `claimScope` dolu, 64 karakter hash) |
| Zıt cevap (2) | `409` `Request already answered`; gövde devam kimliğini taşımaz |
| Kuyruklu tekrar, iki farklı `Idempotency-Key` (3) | İki `202`; aynı `runId`, aynı `jobId`, aynı `Location` |
| `Branch` (4) | İki `200`, iki ayrı devam, iki satır |
| Denetim (6) | Her cevap kümesinde aktör dolu; `after` ham cevap taşımaz. Eşzamanlı çiftte iki kayıt: biri var olan devamı (`200`), öbürü hiç açılmamış run'ı (`404`) adlandırır — "denenen cevap" |
| Geri bağ (8) | Devamın `GET /api/runs/{id}/events` çerçeveleri: `run.started`, `workflow.response-applied`, `workflow.started`… |

Uygulama günlüğünde hata yoktu.

**İkinci koşum (önce satır, 2026-10-14):** aynı sonuçlar; her denetim kaydı var olan bir run'ı adlandırdı; kaybedenler `Canceled` + `request_already_answered`, sıfır olay. `Tracon:Demo:Attribution:Mode = throw` (`MT-WF-162`): ilk cevap `200` ve kullanıcısız `Completed`, tekrar `done`, zıt `409`, kuyruklu `202` → `Completed`; istek kilitlenmedi.

**Üçüncü koşum (`aafb1d87`):** normal ve `throw` modu aynı. **Çökme penceresi gerçek uygulamada (`MT-WF-164`):** işçi kapalı iki kuyruklu cevap; uygulama durduruldu, SQLite'ta iki işin satırı silindi (run `Queued`, kayıt `Accepted`, iş yok); işçiyle yeniden açıldı. Kuyruklu tekrar `202` aynı run (`jobId` = run), akışlı tekrar `run`…`done`; iki run `Completed` (13 olay), run başına tek iş ve tek kayıt; günlükte iki kez `had a recorded answer but no job`.

### Bakımcı kararları — 2026-10-14 (bağımsız denetimden sonra)

Gerekçenin tamamı `arsiv/KARARLAR-GECMISI.md`'de (K-994…K-998); burada karar ve sapma:

- **🔴1 → önce satır, sonra iddia (K-997).** Run satırı her yolda kayıttan önce açılır; bir kayıt var olmayan bir run'ı adlandırmaz. Kaybeden kendi run'ını olaysız `Canceled` + `request_already_answered` kapatır (düz satır güncellemesi: webhook, metrik, alıcı tetiklenmez; liste ve istatistik sayar). Kayıt yazılamazsa run `Failed` + `answer_not_recorded`, cevap `500`. Atıf karardan önce `RunAttributionReader` ile bir kez okunur (run/resume kuyruk yolu da). Satırdan sonra `CancellationToken.None`. İzleyici satırsız ya da olaysız işi `error` ile bitirir; 30 sn bekleme (`missingRowGrace`) kalktı.
- **🔴2 → kuyruk işi yaşarken izle (K-997).** Baş run'ın işi `Pending`/`Leased`/`Running` ise kapı izler; izleyici ardılı bekler.
- **🟡1 → belgele.** Uzlaştırma kapalıyken çöken akışlı devam isteği kilitler; XML, `CHANGELOG`, site söyler.
- **🟡2 → zincir kuralı (seçenek b).** Zinciri birden uzun `Accepted` kayıt checkpoint kuralından geçer; (a) runner'a iş kimliği taşımayı isterdi (`JobAttemptSignals` taşımıyor).
- **🟡4 → `Branch` de önce satır.** Kuyruğa giremeyen iş satırı `Failed` kapatır; sarkık kayıt kalmaz.
- **🟡5 → metin düzeltildi.** Kaydı yalnız veri sahibi silmesi kaynak run'la siler; `0060` yorumu değişti ve yeniden ankrajlandı (eski `0060`'ı uygulamış veritabanı sıfırlanır). Sınırsız büyüme F-518.
- **İkinci tur 🟡1 → eksik işi yaz (K-997, K-998).** Tek run'lık, `Queued`, işi olmayan baş görülünce tekrar işi yazar (`Decision.MissingJob` → `WriteMissingJobAsync`); atıf satırdan, checkpoint ve sürüm tekrardan. Denetim satırı yazılmaz. Yarış güvenliği ölçüldü: üç SQL deposu kimliği reddeder, bellek içi depo değiştiriyordu → `TryAdd`; `QueuedRunPlaceholder` reddedilen yazımdan sonra işi geri okur.

### Önce kırmızı kaydı (dürüst)

| Test | Nasıl kırmızı görüldü |
|---|---|
| `WorkflowDefinitionStoreContract` yeni round-trip case'leri | Gerçek kırmızı (SQLite, payload alanı eklenmeden) |
| `full-replace-editors.test.tsx` iş akışı case'leri | Gerçek kırmızı (`tsc` + 5 vitest) |
| `WorkflowResponseOwnershipTests` ilk altı case (fırlatan atıf ×2, kaybedenin satırı ×2, satır ile iddia arası kopma, kuyruk işi yaşarken ikinci sahip) | Gerçek kırmızı (iddia-önce kodda; 2026-10-14). Kopma case'i iddia-önce kodda satır hiç açılmadığı için kırmızıydı |
| Mimari kapılar, tablo sayıları, SQL metin anlık görüntüsü, kapsama listeleri | Yeni yüzey önce kırmızı yaptı, sonra güncellendi |
| `WorkflowSingleResponseTests` (7/11) | Mutasyon (tek-cevap yolu kapatıldı); kod testten önce yazılmıştı |
| `WorkflowResponseStoreContract` (4 case) | Mutasyon (iddia denetimi silindi) |
| `WorkflowResponseModeVersionTests` | Mutasyon (kayıtlı sürüm yok sayıldı) |
| Ownership: 6-A ×2, 🟡2 zincir kuralı, motor-önce-düşerse satır kapanır, eşzamanlı iki yeniden sürme | Mutasyon (her biri ayrı: kural silindi / CAS sonucu yok sayıldı / `finally` kapatması kapatıldı) |
| İkinci tur: `A_queued_answer_whose_job_was_never_written_is_finished_by_the_same_answer` ×2 (kuyruklu ve akışlı tekrar), `Two_repeats_that_both_find_the_job_missing_write_it_once` ×2 (bellek içi, SQLite) | Gerçek kırmızı (eksik-iş yazımı yokken: iş listesi boş; eşzamanlı tekrarlar işi hiç okumadı). Mutasyon: bayrak hiç kurulmazsa dördü kırmızı; geri okuma kapatılırsa eşzamanlı ikisi kırmızı |
| İkinci tur: `JobStoreContract.An_id_that_is_already_queued_is_refused_and_the_queued_job_is_kept` | Gerçek kırmızı bellek içi depoda (işi değiştiriyordu); PostgreSQL, SQL Server, SQLite ilk koşumda yeşil — ölçüm |
| İkinci tur: izleyicinin `error` ile bitmesi ×2 (hiç başlamadı, satırı silindi) | Önce yeşil (kod vardı). Mutasyon: `readAny` denetimi kapatılınca "hiç başlamadı", satır denetimi kapatılınca "silindi" case'i kırmızı |
| Önce kırmızı görülmedi | `WorkflowResponseRecordTests`, `FailClosedAuditTests` yeni iki case, `WorkflowResponseLinkTests`, `WorkflowAnswerDigestTests`, konsol rozet/409/geri bağ testleri, izleyicinin run kimliği ve sayım iddiası |

### Tüketici yüzeyi envanteri (kapanış, `tuketici-dokuman-senkronu`)

Alt agent başlatılmadı (araç kuralı); kulvar C bu oturumda seri koştu.

1. **`docs-site/` (elle):** yeni `concepts/workflow-answers.md` (kenar çubuğu "Answering a workflow"); `concepts/workflows.md` (kısa paragraf + bağlantı), `concepts/governance.md` (fail-closed tablosu, "seven"), `capabilities.md` (iki "seven", "Human input" satırı, 175 işlem), `getting-started/security.md`, `guides/observability.md`, `reference/security-policy.md`, `reference/threat-model.md` (iki yer), `ui.md` (rozet, `409` notu, editör anahtarı, sürüm satırı, geri bağ), işlem sayısı sayfaları (`index.mdx`, `getting-started/index.md`, `http-api.md`, `reference/compatibility.md`); ekran görüntüleri E2E'den üretildi, `landing-captures.mjs` pin'i yenilendi. Üretilen (`api/`, `http-api/`) koddan gelir.
2. **Sevk edilen metin:** XML `WorkflowResponseMode` (uzlaştırmanın neyi kapsadığı, eksik iş), `IWorkflowResponseStore` (saklama), `IJobStore.EnqueueAsync` (kimlik reddi), `IAuditLog` (yedi); `src/Tracon.Workflows/README.md`, `src/Tracon/README.md`, `src/Tracon.AspNetCore/README.md`, kök `README.md`; `CHANGELOG.md` (Security, Added, Changed); paketlenen `tracon.json` yeni ucu taşır.
3. **Yerel referans ve agent haritası:** `capabilities.md` "Human input" satırı; `Tracon.AgentMap.md`, `llms.txt`, `llms-full.txt` yeniden üretildi (`--check` ✅, bütçe içinde).

Kapılar: `ShippedDocumentationSelfContainmentTests`, `CapabilityExampleTests`, `SourceLanguageTests` ✅ · `LocalReferenceTests` ✅ 10/10 · `build-agent-map.mjs --check` ✅ · `npm run check` ✅ (1101 sayfa, kırık bağlantı 0, en ağır `concepts/workflows` 58 470 B) · `dokuman-bakim.py --site-denetle` sonucu *Kapanış kapısı*nda. Muafiyet listesi ve taban çizgisi büyümedi. `--site-denetle` gerekçesi: `kalicilik` (`getting-started/persistence.md`) — yeni migration yalnız tablo ekler, sayfa tablo listesi ya da tek yönlü örnek taşımaz, saklama kuralı `concepts/workflow-answers.md`'dedir; `paket-readme` (`packages.md`) — `Tracon.AspNetCore` README'sinde yalnız işlem sayısı değişti, `packages.md` sayı taşımaz. Site yayını (Adım 10) koordinatörde, birleştirmeden sonra.

### Uygulama sapmaları

- **Adlar:** enum üyesi `SingleAnswer` (CA1720), `ReplaceAsync(…, replacement)` (CA1716); `AddWorkflow(…, string? description = null)` iki aşırı yüklemeye bölündü (RS0027) — üçüncü argümana düz `null` belirsizleşir (`CHANGELOG` Changed).
- **🚨 Tasarım denetimden sonra "önce satır, sonra iddia" oldu** (K-997). İlk kural "satırı olmayan iddia yoldadır" isteği kilitliyordu; satırı olmayan iddia artık yalnız silinmiş satırdır (`Gone`, `409`).
- **CAS `status <> 2`** (6-A teslim edilmiş satırı da yeniden sürer).
- **İzleme hedefi:** akışlı izleyici zincirin son run'ından başlar, ardılı izler, iş bitmemişse bekler; kuyruklu izleyici ilk `202`'nin aynısını alır. İlk cevap akışlıysa kuyruklu izleyicinin `jobId`'si bir işi adlandırmaz (K-924).
- **`501` kararın önünde** (aksi hâlde denetim + iddia run'sız kalırdı).
- **Fail-closed çağrı yeri `Internal/WorkflowResponseGate.cs`** (plan `WorkflowEndpoints.cs` diyordu): 7 işlem / 10 çağrı yeri.
- **Eşzamanlı iki cevap iki denetim + iki run satırı bırakabilir**; ikisi de var olan run'ı adlandırır.
- **`WorkflowContinuationChain`** `WorkflowRecovery`'den ortak yardımcıya çıktı. `RunAuthorizationCoverageTests` kaynak kapısı 2 → 3.
- **🚨 Migration:** PostgreSQL `0060`, SQL Server `0048`, SQLite `0047` (K-178); `applied-migrations.json` ankrajı `36e75e7b`, yorum düzeltmesiyle `73c11d4e`. Kardeş worktree aynı numarayı alırsa birleştirmede yeniden numaralanır.
- **Site:** kurallar yeni `concepts/workflow-answers.md`'ye taşındı — `workflows.md` 60 221 B gzip ile tavanı (59 000) aştı, tavan yükseltilmedi (58 470 B). İşlem sayısı 174 → 175 (README ×5, site ×5); ekran görüntüleri yeniden üretildi, anasayfa kırpımları aynı.
- **İkinci tur K-998 planda yoktu:** `JobStoreContract` yeni case — sevk edilen sözleşme genişledi, yayın provası *Kapanış kapısı*nda.
- **Konsol:** editör anahtarı her `Kind`'da; geri bağ `runDetail.answersRequestOf` (`locales/*/runs.ts`).

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Fazın kararları burada yazılır. Dört kategoriden birine
> giren karar (AGENTS.md) ayrıca `docs/KARARLAR.md`'ye K-NNN ve kategori
> etiketiyle girer ve numarası burada anılır; girmeyen karar yalnız burada
> kalır (`faz-tamamlama` Adım 8). Plan numara rezerve etmez.

| Karar | Kayıt |
|---|---|
| Workflow başına cevap modu, tek cevap sözleşmesi (kapsam oturum, eşitlik `approved`/`text`/`data`, izle / `409` / yeniden sür) (kullanıcı kararı) | **K-994** *(public-api, geri-dönüşü-pahalı)* |
| Cevap kaydı ve kaynak bağı: `workflow_responses`, `IWorkflowResponseStore`, `…/responses`, `WorkflowResponseApplied` (33) (kullanıcı kararı) | **K-995** *(kalıcı-veri, public-api)* |
| Fail-closed küme 6 → 7: `workflow.respond`, her modda (kullanıcı kararı) | **K-996** *(güvenlik, public-api)*; K-776 satırına işaret |
| Önce satır, sonra kayıt; kaybedenin kapatılması; kuyruk işi sahipliği; eksik işin yazımı (kullanıcı kararı, 2026-10-14, iki denetim turu) | **K-997** *(geri-dönüşü-pahalı, public-api)* |
| `IJobStore.EnqueueAsync` kayıtlı iş kimliğini reddeder; geri okuma | **K-998** *(public-api)* |
| K-929 gerekçesi kodla uyumlu yazıldı | K-929 satırı yerinde düzeltildi (tarih işaretli), `KARARLAR-GECMISI.md` K-929 |
| Kategori dışı: örnek uygulamada `Tracon:Demo:Attribution:Mode = throw` anahtarı; izleyicinin 30 sn beklemesinin kaldırılması; enum adı `SingleAnswer` (CA1720) | Yalnız burada (yerel tercih) |

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 2 (bakımcı kararları 2026-10-14: önce satır; eksik işi yaz) |
| Düzeltme turu sayısı | 2 (iki bağımsız denetim turunun düzeltmesi) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 2 / 0 / 0 (birinci tur; ikinci tur 🔴 yok) |
| Fazın ürettiği regresyon | 0 (iki 🔴 fazın kendi yeni kodundaydı; mevcut davranışı bozan bulunmadı) |
| Faz kapandıktan sonra bulunan kusur | 0 (kapanış anı; sonraki `kusur-giderme` koşumu günceller) |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı.

Bağımsız `faz-denetim`, 2026-10-14: 2 🔴, 5 🟡, 3 🟢.

| Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|
| Satırı olmayan iddia "yolda" sayılır; iddiadan sonra satır yazımı düşerse (atıf getter'ı, kopma, çökme) istek kalıcı kilitlenir | 🔴 | gerçek | Düzeltildi (K-997): önce satır; kaybeden kendi satırını kapatır; atıf bir kez okunur; `CancellationToken.None`; izleme `error` ile biter. Önce kırmızı |
| Yeniden sürme, kuyruk işinin kurtarmasının yanında ikinci sahip olur (yeniden sürülen run `ContinuedFromRunId` taşımaz, zincir onu görmez) | 🔴 | gerçek | Düzeltildi: baş run'ın işi bitmemişse kapı izler. `A_repeat_while_the_queued_answers_job_is_unfinished_…` önce kırmızı |
| Uzlaştırma kapalıyken çöken devam `Running` kalır, istek kilitlenir | 🟡 | gerçek | Belgelendi (XML, `CHANGELOG`); site kapanışta |
| Checkpoint'siz kurtarma cevabı devam kimliğiyle yeniden uygular; teslim yazılmaz, kayıt `Accepted` kalır, kapı checkpoint'e bakmadan yeniden sürer | 🟡 | gerçek | Düzeltildi: zinciri birden uzun `Accepted` kayıt checkpoint kuralından geçer. Test + mutasyon |
| İzleyici testi run kimliğini ve sayımı doğrulamıyor; `finally` kapatması, 6-A, eşzamanlı yeniden sürme case'leri yok; önce-kırmızı kaydı yok | 🟡 | gerçek | Eklendi (bkz. *Önce kırmızı kaydı*) |
| `CHANGELOG` `Branch` modu değişikliklerini (kayıt yazımı düşerse `500`; `WorkflowResponseApplied` sıra 1, sonraki olaylar kayar) ve kuyruk yolundaki sarkık kaydı anmıyor | 🟡 | gerçek | `CHANGELOG` Changed; sarkık kayıt önce-satır sırasıyla kapandı |
| Metin "saklama kaydı kaynak run'la siler" diyor; hiçbir saklama hedefi `runs` silmez | 🟡 | gerçek | Düzeltildi (XML, `CHANGELOG`, `0060` yorumu); sınırsız büyüme kapanışta aday: **F-518**, Faz 209'a not |
| Teslim edilmemiş devam düştükten sonra zıt cevap hep `409` alır; motorun teslimde reddettiği bir cevap isteği kilitler; yönetici çıkışı yok | 🟢 | gerçek | Kapanışta aday: **F-517** |
| Yalnız devam run'ının silinmesi kaydı bırakır (FK yalnız `source_run_id`'de); zincir boş görünür | 🟢 | gerçek | Kapanışta aday: **F-519** (şimdilik `Gone` → `409`) |
| Açık bir `Branch` `definitionVersion`'ıyla `/resume` tek-cevap iddiasını atlar (yalnız Operator/Admin) | 🟢 | gerçek | Kapanışta aday: **F-516** |

İkinci tur `faz-denetim`, 2026-10-14: 🔴 yok; birinci turun 🔴2, 🟡2, 🟡3, 🟡4, 🟡5'i kapandı; 🔴1 ve 🟡1 kısmen kapandı, kalan bacakları aşağıdaki iki 🟡.

| Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|
| Kuyruklu `SingleAnswer` cevabı, satır + kayıttan sonra ve kuyruğa yazımdan önce durursa kalıcı kilitlenir (uzlaştırma `Queued` okumaz, iptal `409`); üç cümle yanlıştı | 🟡 | gerçek | Düzeltildi (eksik işi yaz, K-997/K-998); önce kırmızı ×4, sözleşme ölçümü; cümleler düzeltildi |
| İzleyicinin `error` ile bittiğini (satır yok; hiç başlamadı) kanıtlayan test yok | 🟡 | gerçek | Eklendi: `A_follower_of_a_run_that_never_started_ends_with_an_error_not_done`, `A_follower_whose_run_was_erased_while_it_followed_ends_with_an_error_not_done`. Mutasyonla kanıtlandı |
| Kaybedenin `Canceled` run'ı `GET /api/runs`'ta, `RunStatistics.CanceledRuns`'ta ve `ErrorRate` paydasında görünür; `CHANGELOG` yalnız webhook/metrik/alıcıyı anıyordu | 🟢 | gerçek | `CHANGELOG` cümlesi eklendi; site kapanışta. Kapanışta aday değil (yazıldı) |
| `OpenRunAsync` hatası varsayılan `Branch` akışlı modda da `500` döner (önce motorun yazıcısı yutardı); `CHANGELOG` Changed yalnız kayıt deposunu anıyordu | 🟢 | gerçek | `CHANGELOG` Changed'e eklendi. Kapanışta aday değil (yazıldı) |
| `WorkflowEventStream`'in `finally` kapatması try/catch'siz: depo hatası başlıklardan sonra kaçar, satır `Running` kalır (uzlaştırma kapatır) | 🟢 | gerçek | Kapanışta aday: **F-514** (kuyruklu iptalde kapatılamayan `Queued` satırla birlikte) |
| İzleyicinin `NextRunAsync`'i yalnız `ContinuedFromRunId` ardılı arar; kurtarma kapalı ve `MaxAttempts > 1` iken iş aynı run kimliğiyle yeniden koşar, izleyici "hiç başlamadı" der (varsayılan olmayan yapılandırma) | 🟢 | gerçek | Kapanışta aday: **F-515** |

Planın *Kapsam dışı* tablosundan açılan adaylar: **F-520** (cevabın kendisini olaya yazmak), **F-521** (doğrudan runner çağıranın denetimi ve tek yanıtı), **F-522** (port başına mod), **F-523** (`workflow.request.answered` webhook'u); yönetici çıkışı **F-517**'dir. MCP Tasks `input_required` plan gereği aday açılmadı.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz ([Faz 209](../../209-WORKFLOW-GELEN-KUTUSU-VE-SURE.md) yanıt kaydını okur).

**Sıradaki faz: Faz 210 (F-407 — bütçe tükenince fonksiyon düğümü başlamasın).** Planı onaylı (🔒1 A, 🔒2 B, 🔒3 A) ve `worktree-faz-210-butce-dugum-kapisi` dalındadır; bu faz kapandıktan sonra `main`'e birleştirilir. Bu fazla ortak dosyası `WorkflowRunner.cs`'tir; satır kayar, sözleşme çakışmaz. **Faz 209** (gelen kutusu ve süre) altı engelleyici soruyla onay bekler ve bu fazın kaydını okur.

**Devralınan sözleşmeler**

- `IWorkflowResponseStore` + `WorkflowResponseRecord` (*Gerçekleşen Public API*). İddia kapsamı **oturumdur** (`claim_scope = session_id`, §208.7 #1 ölçüldü); Faz 209'un "yanıtlandı" işareti `(tenant, claim_scope, request_id)` ile, `Abandoned` saymadan okur (K-995).
- Cevap sırası **denetim → run satırı → kayıt** (K-997). Yeni bir cevap yolu (Faz 209'un süresi dolan isteği dahil) bu sırayı korur; bir kayıt var olmayan bir run'ı adlandıramaz.
- Fail-closed küme **yedi** işlemdir (K-996). Ekleme ya da çıkarma K-776 koşuluyla sekiz yeri birlikte günceller (*Tüketici yüzeyi envanteri*).
- `WorkflowRegistrationOptions` Faz 209'un `RequestTimeout`'unu taşıyacak tiptir. `AddWorkflow`'un üç aşırı yüklemesi vardır; üçüncü argümana düz `null` belirsizdir.
- `RunEventType.WorkflowResponseApplied = 33` (`workflow.response-applied`), devamın ikinci olayıdır.
- `IJobStore.EnqueueAsync` kayıtlı kimliği reddeder (K-998); `QueuedRunPlaceholder` reddedilen yazımdan sonra işi geri okur.

**Bilinen tuzaklar**

- 🚨 Akışlı devam çöküşte `Running` kalır ve tek cevaplı isteği kilitler; yalnız run uzlaştırması (varsayılan kapalı) kapatır. Uzlaştırma `Queued` satır okumaz.
- 🚨 `concepts/workflows.md` ağırlık tavanında (58 470 / 59 000 B gzip). Yeni workflow anlatısı `concepts/workflow-answers.md`'ye ya da yeni bir sayfaya gider; tavan yükseltilmez.
- 🚨 Migration yorumu checksum'a girer. Bu dalın eski `0060`'ını uygulamış bir veritabanı açılışta checksum hatası verir ve sıfırlanır (yayımlanmadı).
- 🚨 `docs/ADAYLAR.md` bütçesi dar; bu kapanış on iki satırı `arsiv/ERTELENEN-ADAYLAR.md`'ye damıttı.
- 🚨 Workflow cevap testlerinde depo sahtesi `configureTracon` içinde kurulur ([`hafiza/workflow-cevaplari.md`](../../hafiza/workflow-cevaplari.md)).

**Açık kalanlar:** F-514…F-523 (aday; *Denetim Bulguları*). Sevk edilen sözleşme genişlediği için yayın provası koşuldu; sonucu *Kapanış kapısı*nda.
