# Faz 118 — Yargıç Başına Checkpoint

> **Durum:** ✅ Tamamlandı (2026-08-27)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-152**
> **Önkoşul:** Faz 49 (çevrimiçi değerlendirme) ve Faz 103 (`JudgeTimeout` wait cutoff, K-621) — ikisi de arşivde
> **Paketler:** `AgentPrism.Core` (yalnız `Evaluation/`)
> **Yeni paket:** Yok · **Migration:** **Yok** — checkpoint bugünkü `run_scores` satırlarından okunur (§ 118.2)
> **Public API:** **Büyümüyor.** Değişen tek şey `internal` bir handler'ın davranışı
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/write-your-own-judge.md` (retry davranışının bir cümlelik düzeltmesi)
> · sevk edilen: `IRunJudge` XML dokümanına retry notu
> **Manuel test alanı:** `docs/manuel-test/17-EVAL-VE-DENEYLER.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show eca3b9d:docs/arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md
> ```
>
> Damıtıldı 2026-08-27 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir `OnlineEval` işi yeniden denendiğinde **başarılı yargıçlar da yeniden çalışır**. `run_scores` tablosunun benzersizlik kısıtı görünür sonucu birleştirir, yani kullanıcı ikinci bir satır görmez — fakat pahalı veya yan etkili üçüncü taraf yargıç çağrısı **gerçekten ikinci kez yapılır** ve ikinci kez faturalanır.

## Bitiş Ölçütleri (DoD)

- [x] İki yargıçlı bir işte biri ilk denemede başarılı olursa, yeniden denemede **çağrılmaz** (çağrı sayacı ile kanıtlanır) — `OnlineEvalCheckpointTests`, `OnlineEvalRetryTests.Successful_judges_are_not_re_invoked_when_a_queued_job_retries`
- [x] Başarısız, timeout'a düşen ve çekimser yargıçlar yeniden denemede **koşar** — `OnlineEvalCheckpointTests` (üç ayrı test)
- [x] İlk denemede hiçbir yargıç atlanmaz — bugünkü davranış bit-bit aynı — `ExecuteAsync_does_not_skip_on_the_first_attempt_even_if_a_score_already_exists`
- [x] `POST /api/runs/{id}/judge` davranışı **değişmedi**; elle yeniden yargılama gerçekten yeniden koşuyor — `OnlineEvalRetryTests.Manual_scoring_still_reruns_a_judge_that_already_scored_the_run` VE canlı doğrulama: `samples/AgentPrism.Api`'de aynı run'a arka arkaya iki `POST /judge` çağrısı, skor `12 → 20` değişti (gerçek model her iki kez de çağrıldı)
- [x] Başka kiracının skor satırı checkpoint sayılmıyor — sözleşme testi dört koşumda yeşil — `RunScoreStoreContract.Another_tenants_score_is_not_visible` (mevcut, değişmedi; checkpoint'in okuduğu `ListAsync` zaten bu sözleşmeye tabi)
- [x] `ListAsync` hata verirse iş **durmuyor**; atlama yapılmıyor, tüm yargıçlar koşuyor — `A_score_read_failure_does_not_skip_any_judge_and_does_not_fail_the_call`
- [x] Tüm yargıçlar atlanınca iş **tamamlanıyor**, sonsuz yeniden deneme yok — `ExecuteAsync_completes_the_item_when_every_judge_is_already_scored_on_retry`
- [x] `judge:` öneki **tek bir sabitten** üretiliyor — `OnlineEvalJobHandler.JudgeAuthorPrefix`
- [x] Yeni tablo **yok**, migration **yok** (`ls src/AgentPrism.*/Migrations/` değişmedi) — doğrulandı
- [x] `PublicAPI.*.txt` dosyaları **değişmedi** — `git diff --stat` boş döndü
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban ca1b2c0` yeşil (588 test + tam matris + format + pack, docs-site `check:content` düzeltmesiyle birlikte)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — gerçek PostgreSQL + gerçek model + gerçek `ModelRunJudge`: run `01a041c8-…`, `/judge` skor `12`→`20` (ikinci çağrı gerçekten yeniden koştu). Otomatik retry-skip bu ortamda `OnlineEvaluation` kapalı olduğu için canlı tetiklenmedi; `OnlineEvalRetryTests` gerçek `IJobStore`+DI ile kanıtladı.
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` ✅
- [x] Manuel kabul case'leri `docs/manuel-test/17-EVAL-VE-DENEYLER.md` içine eklendi; otomatikleştirilebilenler koşuldu — EVAL-105..111; EVAL-107/108'in otomatik karşılığı yukarıdaki test dosyalarında zaten yeşil
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. § Denetim Bulguları
- [x] `docs-site/guides/write-your-own-judge.md` retry davranışını doğru anlatıyor; `npm run build` + `check-links.mjs` temiz — `npm run check` (content+build+links+weight) tam yeşil
- [x] K-621'in "yeni katman eklenmedi" cevabı karar defterine kaydedildi — K-638

### Doğrulama komutları

```bash
# Migration alınmadı
ls src/AgentPrism.PostgreSql/Migrations/ | tail -3

# Public yüzey büyümedi
git diff --stat src/*/PublicAPI.Unshipped.txt
```

---

## Plandan Sapmalar

1. **Parametre sırası plandan farklı.** Plan `(run, cancellationToken, skipAlreadyScored = false)` gösteriyordu; gerçek imza `(run, skipAlreadyScored = false, cancellationToken = default)` — CA1068 (`CancellationToken` son parametre olmalı) planı ihlal ediyordu. `cancellationToken`'ın varsayılanı korundu; düşürmek mevcut çağrı sitelerini faydasız kırardı.
2. **`{ Length: > JudgeAuthorPrefix.Length }` derlenmedi** (CS9135 — ilişkisel örüntü sabit ister, `const string.Length` sabit sayılmaz). `StartsWith` + alt dizgi kesme ile değiştirildi.
3. **Tenant-izolasyon için YENİ sözleşme testi yazılmadı** — checkpoint okuması `IRunScoreStore.ListAsync` üzerinden gider, bu zaten `RunScoreStoreContract.Another_tenants_score_is_not_visible` ile dört store'da kanıtlıydı (EVAL-109).
4. **"Tüm yargıçlar atlanınca tamamlanır" birim seviyesinde kanıtlandı**, fonksiyonel değil — bu durum (`Attempt > 1` + tüm yargıçlar skorlu) `ExecuteAsync`'in kendi retry tetikleyicisiyle doğal üretilemez, yalnız harici zorlamayla (operatör/çifte-lease) oluşur.
5. **`OnlineEvalRetryTests` gerçek `JobWorkerBackgroundService`'i çalıştırmıyor**, `IJobStore`'u elle sürüyor (Lease→MarkRunning→`ExecuteAsync`→`JobRetryException`→`ReleaseForRetryAsync(TimeSpan.Zero)`) — gerçek 30 sn+ backoff beklemek ya da global `FakeTimeProvider` enjekte etmek yerine. Gerçek `IJobStore` + DI'dan çözülen gerçek handler yine çalışıyor.
6. **Manuel case'ler `EVAL-NNN` numaralandırmasıyla eklendi** (105..111), `MT-EVAL-NNN` değil — dosyanın en son eklenen kalemleri (Faz 102/103/104) zaten bu biçime geçmişti.
7. **`docs-site/public/llms-full.txt` iki kez yeniden üretildi** (`build-agent-map.mjs`) — önce `write-your-own-judge.md` düzenlemesi, sonra denetim ajanının `git stash`/`pop` turu bayatlattı.
8. **`--site-denetle` iki kural tetikledi** (`http-api`, `cekirdek-kavram`) ama `--site-gerekce-yazildi` ile geçildi: `EvalEndpoints.cs`'deki değişiklik yalnız adlandırılmış argüman (HTTP sözleşmesi aynı, `http-api.md` bunu hiç anlatmıyor); `IRunJudge.cs` yalnız XML doküman, `concepts/evaluation.md` retry detayını zaten `write-your-own-judge.md`'ye yönlendiriyor (o sayfa güncellendi).

## Bu Fazda Verilen Kararlar

- **K-638** — Checkpoint mevcut `run_scores` satırlarından okunur, yeni tablo/migration açılmadı; K-621'in "sonraki adım" sorusuna (F-152 timeout modeline yeni katman ekler mi) HAYIR cevabı verildi. Bkz. `docs/KARARLAR.md`.

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, `git diff ca1b2c0` + bizzat koşulan testler) 🔴/🟡 bulgu üretmedi. On maddelik kontrol listesinin tamamı (Attempt→skip bağlantısı, manuel uç etkilenmemesi, `judge:` sabiti tekliği ve `:` içeren isim ayrıştırması, `OperationCanceledException` yutulmaması, okuma hatasında atlama kapanması, skip dalının `Scores`'a eklenip metrik/summary'e tekrar sayılmaması, testlerin gerçek olması, dört-store sözleşme testi, XML/site metin tutarlılığı, DoD) kod okuması ve/veya bizzat koşulan testlerle doğrulandı.

Üç 🟢 gözlem, ikisi kapanışta doğrudan düzeltildi:

1. **Kapatıldı** — `docs/manuel-test/17-EVAL-VE-DENEYLER.md` başlığındaki `Faz:` listesi 103/118 ile güncellenmemişti; eklendi.
2. **Kapatıldı** — `judge_contract` (aralık dışı skor) durumu için ayrı bir retry-checkpoint testi yoktu (yalnız `judge_failed`/`judge_timeout`/çekimser test edilmişti); `A_judge_with_an_out_of_range_score_is_not_checkpointed_and_runs_again_when_skipping_is_requested` eklendi.
3. **Gerekçelendi, devredilmedi** — atlanan bir yargıcın skorunun `AgentPrismMetrics.RecordJudgeScore`/`OnlineEvalSummaryService.RecordScoreAsync`'e tekrar SAYILMADIĞI yalnız kod okumasıyla kanıtlanıyor (skip dalı `JudgeOneAsync`'e hiç girmiyor, bu iki çağrı yalnız o metodun içinde) — doğrudan bir spy/sayaç testi yok. Bu iki sınıf `sealed` ve arayüzsüzdür; bu, fazdan ÖNCE de var olan bir test edilebilirlik boşluğudur (`OnlineEvalJobHandlerTests.cs` da bu bağımlılıkları hiç test etmiyordu), fazın kendisi bir gerileme getirmedi. Planın kendi risk tablosu bu satır için "kodda açık ayrım" istiyordu ("test kanıtı" değil) — bu karşılandı. `AgentPrismMetrics`/`OnlineEvalSummaryService`'i arayüz arkasına almak ayrı, ölçülmesi gereken bir kapsam genişletmesi olur; bu fazın dar hedefine (§ 118.5) sessizce eklenmedi.

Denetim ajanının `git stash`/`pop` turu `llms-full.txt`'i bir defa daha bayatlattı — `build-agent-map.mjs` ile ikinci kez düzeltildi, `npm run check` yeniden yeşil.

## Sonraki Faza Devir Notu

- **Devraldığı sözleşme**: `OnlineEvalJobHandler.JudgeRunAsync(RunRecord, bool skipAlreadyScored = false, CancellationToken cancellationToken = default)` — `skipAlreadyScored: true` yalnız kuyruklu işin `Attempt > 1` yolunda geçilir; başka hiçbir çağrı sitesi bunu `true` geçmemelidir.
- **`judge:` öneki artık davranış taşır** (`OnlineEvalJobHandler.JudgeAuthorPrefix`, private const). Bir gün bu önek public bir sözleşmeye taşınırsa (Açık Soru 1'in B seçeneği), okuma VE yazma tarafının aynı sabitten türediğinden emin ol.
- **Çekimser yargıcın (`Score is null`) checkpoint'lenmemesi bilinçli bir boşluktur** (§ 118.3) — ölçülmüş bir vaka doğarsa ayrı bir aday (F-NN) olarak ele alınmalı, bu fazın kapsamına sessizce eklenmemeli.
- **Timeout'a düşen yargıcın geç sonucunun kurtarılması** K-621'in bilinçli kararıdır ve bu faz onu tersine çevirmedi; retry'de böyle bir yargıç her zaman yeniden koşar.
- Bu ortamda `AgentPrism:OnlineEvaluation` `dotnet user-secrets` ile açılmadığı sürece varsayılan kapalıdır (K1) — canlı bir retry senaryosunu uçtan uca gözlemlemek isteyen sonraki oturum önce bunu açmalı.
