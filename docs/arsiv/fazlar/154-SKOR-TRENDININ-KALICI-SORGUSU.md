# Faz 154 — Skor Trendinin Kalıcı Sorgusu

> **Durum:** ✅ Tamamlandı (2026-09-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-209**
> **Önkoşul:** 🚨 [Faz 152](152-SKORUN-ADI-VE-SEKLI.md) — aynı tabloya (`run_scores`) dokunur ve **önce koşmalıdır**. 152 skora bir **ad** getiriyor; kırılım o adı içermelidir, aksi hâlde kırılım iki kez elden geçer.
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.PostgreSql`, `AgentPrism.Sqlite`, `AgentPrism.SqlServer`, `AgentPrism.Testing.Contracts.Xunit`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** 🚨 **gerekli, üç sağlayıcıda** — yalnız indeks; numaralar uygulama anında alınır (K-178). Yeni tablo **yok**
> **Public API:** Büyüyor — `IRunScoreStore`'a bir okuma üyesi + bir sorgu/sonuç tipi çifti. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (ölçüldü 2026-09-07): depo arayüzüne üye eklemek üçüncü taraf uygulayıcıyı kırar ve **`1.0` öncesi** yapılmalıdır.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/concepts/evaluation.md` · üretilen: `http-api/`, `api/agentprism.irunscorestore` · sevk edilen: `EvalEndpoints` online özet metni (bugün tüketiciyi **kendi tablomuza** yönlendiriyor), `IRunScoreStore` XML dokümanı
> **Manuel test alanı:** `docs/manuel-test/17-EVAL-VE-DENEYLER.md` · `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 99873f01:docs/arsiv/fazlar/154-SKOR-TRENDININ-KALICI-SORGUSU.md
> ```
>
> Damıtıldı 2026-09-07 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Skor özeti süreç yeniden başlayınca **sıfırlanıyor** ve ürün bunu tüketiciye **kendi tablosunu sorgulayarak** çözmesini söylüyor. Bir NuGet paketinin tüketiciyi kendi şemasına yönlendirmesi bir sözleşme boşluğudur: `run_scores` public bir yüzey değildir, migration'la değişebilir — nitekim [Faz 152](152-SKORUN-ADI-VE-SEKLI.md) tam olarak onu değiştiriyor.

## Bitiş Ölçütleri (DoD)

- [x] `GET /api/evaluation/scores/summary` sunucu yeniden başladıktan **sonra** aynı sonucu döner — `samples/AgentPrism.Api` + SQLite ile gerçek yeniden başlatmayla doğrulandı
- [x] `GET /api/evaluation/online` davranışı **değişmemiştir** (canlı gösterge hâlâ sıfırlanır) — regresyon: mevcut `OnlineEvaluationTests`
- [x] Farklı `RunScoreKind`'lar aynı ortalamaya girmez; kova anahtarı `(name, kind)`'dır — `RunScoreStoreContract` dört store'da
- [x] `Value = null` sayıma girer, ortalamaya girmez ve `noValueCount` ile raporlanır
- [x] `Categorical` skorlar kategori sayımı döner, ortalama dönmez
- [x] `created_at` indeksi üç sağlayıcıda da kuruldu; migration ikinci kez koşulunca hata vermiyor — `MigrationTests`/`MigrationRunnerTests`
- [x] Yeni tablo **açılmadı**; "no durable counter store" kuralı korundu
- [x] `EvalEndpoints.cs`'teki *"the 'run_scores' table can be queried directly"* cümlesi **kalktı**
- [x] Dört doğrulama kapısı sıfır uyarı verir — bkz. "Doğrulama komutları" çıktısı
- [x] `samples/AgentPrism.Api` ile gerçek skor yazımı + yeniden başlatma sonrası özet alındı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `17-EVAL-VE-DENEYLER.md` (EVAL-128..135) ve `12-GOZLEMLENEBILIRLIK-MALIYET.md` (MT-OBS-060) içine eklendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu **yok** — 🟡 bulgular Denetim Bulguları'nda kapatıldı
- [x] `docs-site/concepts/evaluation.md` güncellendi; `npm run check` (content/build/links/weight) temiz
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü — 153.9 KB brotli / 250 KB bütçe
- [x] OpenAPI → NSwag → TypeScript zinciri yeniden üretildi

### Doğrulama komutları

```bash
# Yeniden başlatmadan sağ çıkıyor mu
curl -s "http://localhost:5081/agentprism/api/evaluation/scores/summary?from=2026-09-01T00:00:00Z" | jq '.byName'
# ... sunucuyu yeniden başlat ...
curl -s "http://localhost:5081/agentprism/api/evaluation/scores/summary?from=2026-09-01T00:00:00Z" | jq '.byName'

# Canlı gösterge hâlâ sıfırlanıyor mu (değişmemeli)
curl -s http://localhost:5081/agentprism/api/evaluation/online | jq '.sampleCount'

# İndeks kuruldu mu (PostgreSQL)
psql -c "\di+ *run_scores*"
```

---

## Plandan Sapmalar

1. **`RunScoreBucketing` (public, plan dışı).** Plan yalnız `RunScoreBucket`'ı
   listeliyordu; kova sınırını hesaplayan `Truncate` mantığı ayrı bir public
   statik sınıfa çıkarıldı. Gerekçe: `AgentPrism.Abstractions`'ta tanımlı
   (`RunScoreBucket`'ın yanı) ama `AgentPrism.Core.InMemoryRunScoreStore`
   ondan **başka derlemeden** çağırıyor — `internal` olamaz. Üç SQL sağlayıcı
   aynı kuralı kendi `date_trunc`/`strftime`/`DATEADD` SQL'inde ayrıca
   uyguluyor (bkz. sınıfın kendi `<remarks>`'ı); paylaşılan sözleşme testi
   dördünü aynı davranışa bağlıyor. Public olması ayrıca üçüncü taraf bir
   `IRunScoreStore` uygulayıcısının Pazartesi-başlangıçlı hafta kuralını
   kendi başına türetmek zorunda kalmamasını sağlıyor.
2. **`IRunScoreStore` DI kaydı `Singleton` → `Factory`.** `ByAgent` kırılımı
   agent adını `run_id` üzerinden çözmek zorunda (skorun kendisi agent adı
   taşımıyor). `InMemoryRunScoreStore`'un kurucusuna `IRunStore`'u doğrudan
   enjekte etmek `IRunStore ⇄ IRunScoreStore` döngüsü açıyordu (ikisi de
   birbirini registration sırasında istiyor). Çözüm: kurucu artık
   `Func<Guid, CancellationToken, ValueTask<string?>>?` alıyor, kayıt bunu
   `provider.GetRequiredService<IRunStore>()`'u yalnız **çağrıldığında**
   çözen tembel bir closure ile besliyor. `ServiceRegistrationSnapshotTests`
   bu şekil değişikliğini yakaladı ve satırı güncellendi.
3. **90 günlük varsayılan pencere (plan dışı, denetimde bulundu).** Plan
   `bucket` `null` iken serinin boş kaldığını söylüyordu ama `bucket` VERİLİP
   `from` verilmediğinde ne olacağını tanımlamıyordu — sınırsız bir seri
   sorgusu tüm tabloyu tarayabilirdi. Kapanış denetimi bunu 🟡 bulgu olarak
   işaretledi (aşağıya bkz.); çözüm: `bucket` verilip `from` verilmediğinde
   **tüm sorgu** (kırılımlar dahil) son 90 güne varsayılan alınıyor —
   `EvalEndpoints.MaxRunScoreSeriesDays`. Açık `from` bu varsayılanı ezer ve
   kendisi sınırsızdır; `docs-site/concepts/evaluation.md` bunu açıkça yazar.
4. **Faz dışı üç kusur bulundu ve düzeltildi** (kullanıcı talimatı: "konuyla
   alakasız bug/defect'lerle karşılaşırsan onları da çöz"):
   - **Enum query-parametresi yanlış case ile 500 dönüyordu.** `?bucket=day`
     (küçük harf) `Nullable<RunScoreBucket>` bağlamada
     `BadHttpRequestException` fırlatıyordu; `JsonBindingProblemMiddleware`
     bunu yalnız `InnerException is JsonException` olduğunda yakalıyordu —
     query/route parametresi bağlama hatası hiç `JsonException` taşımıyor,
     dolayısıyla middleware'den kaçıp tüketicinin genel `500` işleyicisine
     düşüyordu. Sınıf taraması **altı** ucu etkilediğini gösterdi (yeni
     `scores/summary` dahil, ama esasen ÖNCEDEN VAR OLAN `stats/timeseries`,
     `runs`, `quota` uçları). Düzeltme: middleware artık AgentPrism etiketli
     her `BadHttpRequestException`'ı yakalıyor, gövde/parametre ayrımını
     `InnerException is JsonException`'a göre başlık metninde yapıyor.
     Regresyon: `JsonBindingProblemMiddlewareTests.Miscased_enum_query_parameter_returns_400_not_500`.
   - **PostgreSQL `date_trunc` oturum saat dilimine bağımlıydı.**
     `RunScoreBucketing`'in "UTC her zaman" sözleşmesini PostgreSQL'in kendi
     `date_trunc(unit, timestamptz)`'i bozuyordu — sunucunun `TimeZone`
     ayarına göre kova sınırı kayıyordu. Aynı kusur ÖNCEDEN VAR OLAN
     `SelectRunTimeSeries` sorgusunda da vardı. Düzeltme: `(date_trunc(unit,
     col AT TIME ZONE 'UTC') AT TIME ZONE 'UTC')` round-trip deyimi, ikisine
     de uygulandı. Regresyon: `Hour_buckets_group_scores_within_the_same_hour`
     ve `Week_buckets_start_on_Monday_and_group_the_whole_week` — gerçek
     PostgreSQL/SQL Server konteynerlerinde (Testcontainers) koşuyor.
   - **`samples/AgentPrism.Samples.FileRunStore` derlenmiyordu.** Bu örnek
     çözümde değildir (`kapi.py yayin`'in dışında hiçbir kapanış kapısı onu
     derlemez); yerel `dotnet pack` + dirty override ile gerçek tüketici
     paketine karşı derlenince `FileRunStore.cs`'te `sum += score.Value`
     (`Value` `double?`) derleme hatası çıktı — `RunScore.Value`'nun
     nullable olması bu dosyadan ÖNCE gelen bir kural, bu faz onu bozmadı,
     yalnız açığa çıkardı. Düzeltme: `score.Value is { } value` deseniyle
     değersiz skor atlanıyor. Ayrıca aynı dosyada `InMemoryRunScoreStore`'a
     verilen agent-adı çözücü kendi `_gate` kilidinin DIŞINDA `_runs`
     sözlüğünü okuyordu; artık kilit altında okuyor.
   - **Kalan aynı-sınıf vaka faz dışına ertelendi.** `SelectExperimentResults`
     (üç SQL sağlayıcı) ve örneğin deney-sonucu karşılığı hâlâ yalnız
     `message_id IS NULL`/`is null` kontrolü yapıyor — `MessageId = ""` yazan
     bir çağıranda deney ortalaması sessizce yanlış olur. Bu **ayrı bir
     özellik** (deneyler, skor özeti değil) ve bu fazın kapsamı dışında;
     [`ADAYLAR.md` — F-214](../../ADAYLAR.md) olarak kaydedildi.

## Bu Fazda Verilen Kararlar

Yeni `K-NNN` kaydı **açılmadı**. Yukarıdaki dört sapma da AGENTS.md'nin
eşiğine girmiyor (public API/uyumluluk sözleşmesi, güvenlik/kiracı sınırı,
kalıcı veri/migration veya geri dönüşü pahalı sistem kararı): ikisi (1, 2)
yerel implementation tercihi, ikisi (3, 4) bir kusuru zaten yazılı olan
sözleşmeyle (`RunScore.MessageId`/`Value` dokümanı, `RunScoreBucketing`'in
"UTC her zaman" sözü) hizalayan düzeltmedir — yeni bir karar değil, var olan
sözün uygulanmasıdır.

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi çalıştırıldı. 🔴 bulgu **yok**.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Seri için varsayılan pencere/tavan yok — `bucket` verilip `from` verilmezse sorgu sınırsız | 🟡 | **Düzeltildi** — 90 günlük varsayılan (`MaxRunScoreSeriesDays`), sapma 3 |
| 2 | Boş-string `MessageId`/`Author` SQL'de (`IS NULL`) ile bellek içinde (`{Length:>0}`) farklı sınıflandırılıyordu | 🟡 | **Düzeltildi** — üç SQL sağlayıcının `ScoreFilter`/`ByAuthor` sorgusu `''`'i `NULL` ile eşitledi; regresyon: `An_empty_string_MessageId_is_treated_the_same_as_no_message`, `An_empty_string_Author_is_excluded_from_the_author_breakdown_like_no_author` (dört store'da da yeşil) |
| 3 | *(orijinal denetimde numaralanmadı / erken kapandı)* | — | — |
| 4 | Sample'ın agent-adı çözücüsü `_runs`'ı kendi `_gate` kilidi DIŞINDA okuyor | 🟡 | **Düzeltildi** — okuma `lock (_gate)` içine alındı; `AgentPrism.Samples.FileRunStore.Tests` (93 test) yeşil |
| 5 | `RunScoreBucketing` planın "Planlanan Public API" listesinde yok | 🟡 | **Gerekçelendi** — bkz. Plandan Sapmalar #1 |
| 6 | `ScoreTrendChart` bir kovada bir (ad, kind), başka kovada farklı bir (ad, kind) çizebilir — çizgi iki farklı metriği birleştirir | 🟡 | **Düzeltildi** — `primaryScoreIdentity` artık TÜM seri için TEK bir (ad, kind) kimliği seçiyor (`overall` varsa o, yoksa en çok kovada geçen); `lib/chart.ts`'e taşındı ve 5 birim testiyle kanıtlandı |
| 7 | Sekiz yeni dosya `git add` edilmemiş | 🟡 | **Düzeltildi** — commit ile birlikte eklendi |

## Sonraki Faza Devir Notu

**Devraldığı sözleşmeler:**
- `IRunScoreStore.SummarizeAsync(RunScoreQuery, CancellationToken)` dört
  uygulamada da (bellek içi, PostgreSQL, SQLite, SQL Server) aynı davranışı
  verir; `RunScoreStoreContract` bunu ~35 case ile kanıtlıyor.
- `message_id`/`author` sorgu filtrelerinde `""` her zaman `NULL` ile
  eşdeğerdir — yeni bir `run_scores` sorgusu yazan her kod bu kuralı
  **tekrar türetmek zorunda değildir**, `ScoreFilter` local fonksiyonuna
  bakması yeterlidir (üç SQL sağlayıcı, aynı desen).
- `RunScoreBucketing.Truncate` UTC kova sınırının **tek kaynağıdır**; yeni bir
  zaman kovası ihtiyacı (örn. `Month`) buraya ve üç SQL sağlayıcının kendi
  `date_trunc`/`strftime`/`DATEADD` deyimine birlikte eklenmelidir.

**Bilinen tuzaklar:**
- 🚨 PostgreSQL `date_trunc(unit, timestamptz)` oturumun `TimeZone`
  ayarına bağımlıdır — yeni bir zaman-kovalı sorgu yazarken
  `PostgresQueries.cs`'teki `TruncateUtc` yerel fonksiyonunu kopyala, çıplak
  `date_trunc` kullanma.
- 🚨 `samples/AgentPrism.Samples.FileRunStore(.Tests)` hiçbir çözüm dosyasında
  değildir ve kapanış kapısı onu **derlemez**. `AgentPrism.Abstractions`'a
  (veya bağımlı olduğu başka bir pakete) dokunan bir faz, sample'ı gerçekten
  denemek isterse: `dotnet pack src/<Paket> -c Release
  -p:AgentPrismAllowDirtyPack=true -p:MinVerVersionOverride=0.0.0-dirty.<ad>`
  ile yerel feed'e bas, sonra `dotnet build
  -p:AgentPrismSamplePackageVersion=0.0.0-dirty.<ad>` ile sample'ı derle —
  varsayılan `*-*` joker karakteri her zaman en YÜKSEK sürümü seçer, bu yüzden
  dirty sürüm açıkça verilmelidir.
- `F-214` (`docs/ADAYLAR.md`) deneyler özelliğinde aynı boş-string/`NULL`
  sınıfını taşıyor — bu faz onu düzeltmedi, yalnız kaydetti.

**Yarım kalan / açık uçlar:** Yok — DoD'nin tüm satırları kapandı (aşağıya
bkz.).

> Kapanışta doldurulur.
