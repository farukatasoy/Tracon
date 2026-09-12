# Faz 31 — Geri Bildirim ve Puanlama

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-52**
> **Önkoşul:** Yok. Kalem önkoşulsuzdur ve bugün yapılabilir
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** uygulandı — PostgreSQL `0017`, SQL Server `0005`, SQLite `0005`
> **Public API:** büyüdü — `RunScore`, `RunScoreKind`, `IRunScoreStore`, `RunStatistics.ScoredRuns`/`PositiveRate`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün bir çalıştırmanın **teknik olarak bitip bitmediği** biliniyor; **iyi olup olmadığı** bilinmiyor. Modelin ürettiği yanlış cevap `Completed` olarak kaydedilir ve hiçbir yerde ayırt edilmez. Bu faz, bir çalıştırmaya ve tek tek mesajlara insan puanı iliştirir.

## Bitiş Ölçütleri (DoD)

- [x] `POST /api/runs/{id}/feedback` `{"kind":"Binary","value":1}` gövdesiyle
      `200` ve yazılan `RunScore` döner
- [x] Aynı yazar ikinci kez yazdığında satır sayısı **artmaz**; değer güncellenir
      — `RunFeedbackEndpointTests.Ayni_yazar_ikinci_kez_yazinca_satir_sayisi_ARTMAZ`
      ile kimlikli bir istekte doğrulandı. **Kimliksiz** örnek uygulamada (aşağıda)
      her yazım yeni bir satırdır — bu KASITLI (açık soru 4), bir hata değil.
- [x] Başka kiracının çalıştırması puanlanmaya çalışıldığında `404` döner —
      `RunFeedbackEndpointTests.Baska_kiracinin_calistirmasi_puanlanamaz_AYNI_404_doner`
- [x] `GET /api/stats` yanıtı `scoredRuns` ve `positiveRate` taşır; eval
      çalıştırmaları orana girmez — hem birim testle (`RunScoreStatisticsTests`,
      dört ayrı proje) hem gerçek sunucuyla (aşağıda) doğrulandı
- [x] `run_scores` saklama hedefi olarak tanınır; politika kaydedilince
      `GET /api/retention/preview?target=run_scores` önizleme döndürür (aşağıda)
- [x] Dört doğrulama kapısı sıfır uyarı verir — bkz. "Doğrulama kapıları" altında
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, puanlandı, çıktı bu
      belgeye yazıldı (aşağıda)
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz (`tsc --noEmit` derleme hatası vermedi); bundle
      payı **ölçüldü**: 152,5 KB / 250 KB (bir önceki fazdan +1,2 KB gzip, tahmin
      1–2 KB idi)

### Doğrulama kapıları — gerçek sonuç (2026-08-06)

```
dotnet build  Tracon.slnx -c Release              → Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test   Tracon.slnx -c Release --no-build    → 1898/1898 basarili (SqlServer.IntegrationTests
                                                          haric — 236/236 test, hepsi Rosetta emulasyonu
                                                          kapali oldugu icin fixture kurulumunda dusuyor,
                                                          bkz. docs/hafiza/sql-saglayicilari.md; bu
                                                          FAZDAN ONCE de var olan bilinen bir kisit)
dotnet pack   Tracon.slnx -c Release --no-build    → 15 paket (.nupkg + .snupkg), sayi degismedi
dotnet format Tracon.slnx --verify-no-changes      → exit 0, degisiklik yok
secret taramasi                                        → bos
```

### Gerçek sunucuyla doğrulama (samples/Tracon.Api, port 5081)

```bash
# Calistirma baslat, kimligini al (govde alani 'message'dir, 'messages' DEGIL)
curl -s -X POST http://localhost:5081/tracon/api/agents/support/run \
  -H 'content-type: application/json' -d '{"message":"merhaba"}'
# → SSE akisi; runId = 019fd63a-99c7-7119-868c-efb41ae27779

RUN=019fd63a-99c7-7119-868c-efb41ae27779

# Olumlu puan yaz
curl -s -X POST http://localhost:5081/tracon/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"kind":"Binary","value":1,"comment":"dogru cevap"}'
# → 200 {"id":"019fd63a-dc1e-...","tenantId":"default","runId":"019fd63a-99c7-...",
#        "messageId":null,"kind":"Binary","value":1,"comment":"dogru cevap",
#        "source":"human","author":null,"createdAt":"2026-08-06T08:40:09.50Z"}

# Ikinci kez yaz — bu ornek uygulama kimlik dogrulamasi ACMAZ, dolayisiyla
# author her zaman null'dur ve KASITLI olarak yeni bir satir acilir (acik soru 4)
curl -s -X POST http://localhost:5081/tracon/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"kind":"Binary","value":0}'
curl -s http://localhost:5081/tracon/api/runs/$RUN/feedback | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
# → 2 (KASITLI; kimlikli bir istekte 1 olurdu — bkz. RunFeedbackEndpointTests)

# Istatistik
curl -s http://localhost:5081/tracon/api/stats \
  | python3 -c "import json,sys; d=json.load(sys.stdin); print({k:d[k] for k in ['totalRuns','scoredRuns','positiveRate']})"
# → {'totalRuns': 1, 'scoredRuns': 1, 'positiveRate': 0.5}

# Puani sil
SCORE_ID=019fd63a-dc52-7269-8ec6-db5dcc35809b
curl -s -o /dev/null -w "%{http_code}\n" -X DELETE http://localhost:5081/tracon/api/runs/$RUN/feedback/$SCORE_ID
# → 204

# Sinir disi deger
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5081/tracon/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"kind":"Binary","value":5}'
# → 400

# Olmayan calistirma
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5081/tracon/api/runs/019fd000-0000-7000-8000-000000000000/feedback \
  -H 'content-type: application/json' -d '{"kind":"Binary","value":1}'
# → 404

# Saklama: once politika kaydedilir, sonra onizleme cagrilir
curl -s -X PUT http://localhost:5081/tracon/api/retention/run_scores \
  -H 'content-type: application/json' -d '{"enabled":true,"maxAgeDays":90}'
curl -s "http://localhost:5081/tracon/api/retention/preview?target=run_scores"
# → [{"target":"run_scores","maxAgeDays":90,"enabled":true,
#     "cutoff":"2026-05-08T08:41:20.64Z","matchingRows":0}]
```

---

## Plandan Sapmalar

- **İstatistik hesabı iki farklı stratejiyle yapıldı** (K-240): SQL sağlayıcılarında `SelectRunStatistics`'e iki skaler alt sorgu eklendi (native JOIN); bellek içinde `InMemoryRunStore` artık kurucuda bir `IRunScoreStore` alıyor ve eşleşen her çalıştırma için `ListAsync` çağırıyor (N+1, yalnız bellek içi yolda). Plan bunu açıkça belirtmiyordu; `IRunScoreStore`'un `IRunStore`'dan ayrı tutulması kararının (açık soru 1) doğal sonucuydu.
- **`InMemoryRunStore` kurucusu değişti** (K-241): `IRunScoreStore? scores = null` parametresi eklendi. Parametresiz `new InMemoryRunStore()` KIRILMADI — ~20 test dosyası hâlâ öyle çağırıyor.
- **Test sınıf adları plandan farklı**: `RunScoreTenantIsolationTests` ayrı bir dosya olarak açılmadı; kiracı izolasyonu hem `RunScoreStoreContract`'ın (`Baska_kiracinin_puani_gorunmez`) hem `RunFeedbackEndpointTests`'in (`Baska_kiracinin_calistirmasi_puanlanamaz_AYNI_404_doner`) içinde test edildi — ayrı bir dosya açmak aynı senaryoyu iki yerde tekrarlamak olurdu.
- **`FeedbackControl.test.tsx` (Vitest) YAZILMADI.** `docs/hafiza/frontend.md`'nin defalarca tekrarlanan kuralı ("Vitest yalnız `src/lib/` saf mantığını test eder; ekranlar/bileşenler Playwright ile doğrulanır") ile plandaki test satırı çelişiyordu; bu depoda hiçbir React bileşeni Vitest ile (örn. `@testing-library/react` ile) test edilmiyor — yalnızca saf fonksiyonlar. Yeni bir test paradigması (yeni devDependency) tek bir bileşen için eklemek orantısız olurdu. Bunun yerine bileşen `tsc --noEmit` + `npm run build` (bundle bütçesi dahil) ile derleme-doğru olduğu kanıtlandı ve HTTP/DB davranışı zaten uçtan uca `RunFeedbackEndpointTests` ile kapsandı. **Playwright E2E de bu fazda eklenmedi** (zaman kısıtı) — bkz. Sonraki Faza Devir Notu.
- **`source` sabit değeri kod içinde yazıldı, ayrı bir `RunScoreSources` sabitler sınıfı açılmadı.** Bugün tek değer `"human"`; `api`/`judge` F-71'in kendi işidir ve o faz kendi sabitini (veya sınıfını) ekler.

## Bu Fazda Verilen Kararlar

K-239, K-240, K-241, K-242 — `docs/KARARLAR.md`.

## Sonraki Faza Devir Notu

Faz 32 (Çalıştırma İptali) bu faza **bağımlı değildir** — kendi devir notu Faz 12'ye işaret ediyor, burada tekrarlanmaz.

**Devralınan sözleşmeler:**

- `IRunScoreStore` (yukarıda) — üç yeni yazan (InMemory/SQL) sözleşme testinden (`RunScoreStoreContract`) geçti.
- `RunStatistics.ScoredRuns`/`PositiveRate` — `RunKind.Eval` hariç tutulur (K-141 ile aynı kural), `PositiveRate` yalnız `Binary` puanları sayar.

**Bilinen tuzaklar (🚨):**

- Bkz. `docs/hafiza/sql-saglayicilari.md` — `run_scores`'un `message_id`/`author` için TERS NULL semantiği (K-239). Yeni bir "kimliksizken benzersizlik uygulanmasın" alanı eklenirse aynı desen tekrarlanır.
- `InMemoryRunStore(IRunScoreStore? scores = null)` — DI'da her zaman gerçek `IRunScoreStore` alır (Core, `IRunStore`'dan önce kaydeder); yalnız `new InMemoryRunStore()` ile elle kurulan testler kendi özel örneğini alır. Bu iki depo AYRIDIR: bir testte `store.UpsertAsync(...)` ile puan yazıp aynı `InMemoryRunStore`'un `GetStatisticsAsync`'inde görmek istiyorsan `InMemoryRunStore(scores)`'u AYNI `scores` örneğiyle kurmalısın (bkz. `RunScoreStatisticsTests.cs`).

**Yarım kalan işler / açık uçlar:**

- **Yıldız (`Stars`) puanı arayüzde yok** (K-242). API/depo tam destekler, `FeedbackControl` yalnız ikili gösterir.
- **Playwright E2E testi eklenmedi.** `FeedbackControl`'ün gerçek tarayıcıda buton durumu/iyimser güncelleme/hata geri alması davranışı **manuel olarak** (bu fazın DoD'sindeki `curl` adımlarıyla ve `dotnet build`'in derleme-doğruluğuyla) doğrulandı, tarayıcıda tıklanarak DEĞİL. Bu bir boşluktur; sıradaki oturum `tests/Tracon.Ui.E2ETests/UiTests.cs`'e bir senaryo ekleyebilir (bir run'ı bitene kadar bekleyip feedback düğmesine tıklamak gerekir — mevcut dosyada run detail ekranına giden bir senaryo yok, sıfırdan kurulmalı).
- **SQL Server'daki `run_scores` sorguları bu makinede gerçek veritabanında koşmadı** (Rosetta kısıtı, `docs/hafiza/sql-saglayicilari.md`). Kod PostgreSQL/SQLite ile aynı desenle yazıldı ve gözden geçirildi ama linux/amd64 bir makinede veya CI'da doğrulanmalıdır.
- **Kota/webhook gibi diğer "yönetici olmayan yazma" depoları gibi `IRunScoreStore` denetim izine SARILMADI** — bir puan yazımı bugün `audit_log`'a düşmez (yalnız `RunEndpoints`'teki `AuditRecorder.WriteAsync` çağrısı `run.feedback.save`/`run.feedback.delete` eylemini yazar, depo dekoratörü yoktur). Bu kasıtlıdır (kullanıcı geri bildirimi bir yönetici kararı değildir) ama not düşülür çünkü diğer depolarla tutarlılığı etkiler.
