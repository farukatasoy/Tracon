# Faz 152 — Skorun Adı ve Şekli

> **Durum:** ✅ Tamamlandı (2026-09-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-208**
> **Önkoşul:** Yok. **Ardılı vardır:** [Faz 154](154-SKOR-TRENDININ-KALICI-SORGUSU.md) aynı tabloya dokunur ve **bu fazdan sonra** koşar.
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.AspNetCore`, `Tracon.PostgreSql`, `Tracon.Sqlite`, `Tracon.SqlServer`, `Tracon.Testing.Contracts.Xunit`, `Tracon.UI`
> **Yeni paket:** Yok — karar 152.1'de ölçümle verildi · **Migration:** PostgreSQL `0048` · SQLite `0035` · SQL Server `0035` (K-178: numaralar sağlayıcı başına bağımsızdır)
> **Public API:** 🔴 Büyüyor **ve kırıyor** — `RunScore.Value` tipi değişir. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (ölçüldü 2026-09-07): hiçbir yüzey sevk edilmemiştir, bu değişiklik **bugün bedava**, `1.0`'dan sonra **imkânsızdır**.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/concepts/evaluation.md` · üretilen: `http-api/schema-runscore.md`, `api/tracon.runscore` · sevk edilen: `RunScore` XML dokümanı, `IRunScoreStore` XML dokümanı, `RunEndpoints` `.WithDescription` metinleri
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
> git show ed8daa25:docs/arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md
> ```
>
> Damıtıldı 2026-09-07 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`run_scores` iki sınır taşıyor ve ikisi de bu fazda kalkar. **Skorun adı yoktur**, bu yüzden bir yazar bir `run`'a yalnız BİR skor yazabilir — insan gözden geçiren aynı `run`'a hem "helpfulness" hem "accuracy" yazamaz. **Skorun şekli sabittir**, bu yüzden 0.87 gibi bir ondalık veya `severe` gibi kategorik bir değer saklanamaz.

## Bitiş Ölçütleri (DoD)

| # | Ölçüt | Durum | Kanıt |
|---|---|---|---|
| 1 | Aynı yazar aynı `run`'a `helpfulness` ve `accuracy` yazabilir; ikisi de ayrı satır durur | ✅ | `RunScoreStoreContract.Same_author_scoring_the_same_target_under_a_DIFFERENT_name_opens_a_new_row` (dört koşum) · `RunScoreNameTests.Two_names_from_the_SAME_author_are_two_rows` · canlı koşum: iki `POST` `200`, `GET` iki satır |
| 2 | Aynı `(run, name)` çiftine ikinci yazım satırı **günceller** | ✅ | `Updating_ONE_name_leaves_the_authors_other_names_untouched` · canlı: `helpfulness` `4 → 5`, `accuracy` `1` değişmedi, toplam satır 2 |
| 3 | `Kind = Categorical` skor `TextValue` ile saklanır ve geri okunur | ✅ | `A_categorical_score_is_stored_as_text` (dört koşum) · canlı: `{"kind":"Categorical","textValue":"minor","value":null}` |
| 4 | `Value = 0.87` üç SQL sağlayıcısında da `0.87` döner | ✅ | `A_decimal_value_is_NOT_rounded` — PostgreSQL 737/737 · SQL Server 672/672 · SQLite 681/681 yeşil. Ek olarak `A_whole_number_value_is_read_back_as_a_number_not_an_integer` (SQLite REAL affinity) |
| 5 | `Value = null` "ölçüm yok" döner, `0` değil | ✅ | `A_null_value_means_NO_MEASUREMENT_not_zero` (dört koşum) |
| 6 | Dolu bir Faz 151 veritabanı üç sağlayıcıda **veri kaybı olmadan** göç eder | ✅ | `Populated_pre_152_run_scores_upgrade_without_data_loss` × 3: satır sayısı 2 → 2 · `judge:quality` → `name = quality` · insan satırı → `name = overall` · göç sonrası `0.87` yazılabiliyor · eski tekillik indeksi düşmüş (ikinci ad yazılabiliyor) |
| 7 | K-638 korunur: bir judge birden çok ad yazsa da retry'da yeniden çağrılmaz | ✅ | `OnlineEvalRetryTests.A_judge_holding_two_named_scores_is_still_skipped_on_retry` — `goodJudge.CallCount == 1`, üç skor satırı |
| 8 | `EvalCaseResult.Scores` değer ve `rating` taşır; `metadata` taşımaz | ✅ | `SerializeScoresTests` (8 case) · canlı eval koşumu: `{"name":"non_empty","kind":"boolean","value":true,"passed":true,"rating":"Good","reason":"Response length 32 meets minimum 1"}` — `metadata` yok |
| 9 | AOT kapısı geçer — `SerializeScores` `reflection` kullanmaz | ✅ | Desen eşlemesi (`is BooleanMetric` / `is NumericMetric` / `is StringMetric`), `Utf8JsonWriter` elle; `kapi.py kapanis` içindeki tam test koşumu yeşil |
| 10 | Dört doğrulama kapısı sıfır uyarı verir | ✅ | `python3 scripts/kapi.py kapanis --taban 3e528aa0` — bkz. **Kapı koşumu** |
| 11 | `samples/Tracon.Api` ile gerçek `run` + skor yazımı yapıldı | ✅ | Aşağıdaki **Canlı koşum çıktısı** |
| 12 | `secret` taraması boş döndü | ✅ | `kapi.py tarama` |
| 13 | Manuel kabul case'leri sete eklendi; otomatikleştirilebilenler koşuldu | ✅ | `docs/manuel-test/17-EVAL-VE-DENEYLER.md` **EVAL-112 … EVAL-119**; 112–116 ve 118 canlı koşuldu, 117 ve 119 `👤 insan gerekir` |
| 14 | `faz-denetim` koşuldu; 🔴 bulgu kalmadı | ✅ | Bkz. **Denetim Bulguları** |
| 15 | `docs-site/concepts/evaluation.md` güncellendi; site kapıları temiz | ✅ | `npm run check` — 1083 sayfa, 159 630 bağlantı, kırık 0; en ağır sayfa 56 439 B / 57 000 B |
| 16 | `RunScore` XML dokümanındaki *"1.5 for Stars"* hatası düzeltildi | ✅ | Artık *"1 to 5 for Stars"*; aynı hata `RunFeedbackRequest.Value`'da da vardı ve orada da düzeltildi |
| 17 | `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü | ✅ | Yeni anahtar: `feedback.otherScores` (iki dilde). Bundle **152,1 KB** brotli / 250 KB bütçe (fazdan önce 151,9 KB — **+0,2 KB**) |
| 18 | OpenAPI → NSwag → TypeScript zinciri yeniden üretildi | ✅ | Dört adım da koşuldu: `TRACON_OPENAPI_REFRESH=1` → `npm run generate` → `nswag-prepare` + `nswag run` + `postprocess` + `json-context` → `packages/tracon-client` `npm run build` |

### Canlı koşum çıktısı (2026-09-07, `samples/Tracon.Api`)

Kimlik çözümlenebilir bir kurulumda koşuldu
(`Tracon__Demo__Roles__Enabled=true`, `X-Tracon-Demo-Role: operator`) —
🚨 kimliksiz kurulumda tekillik hiç devreye girmez ve **her çağrı yeni satır
açar** (K1, 0017'nin kaydettiği davranış). İlk koşum kimliksiz yapıldı ve
`helpfulness` iki satır olarak göründü; bu bir kusur değil, o kuralın kendisidir.

```
case1 helpfulness/Stars 4  -> 200
case2 accuracy/Binary 1    -> 200
case3 helpfulness 4->5     -> 200
case4 severity=minor       -> 200
case5 similarity=0.87      -> 200
case6 illegal name         -> 400
no-name body (compat)      -> 200

accuracy       kind=Binary       value=1    textValue=None   author=demo-tracon-operator
helpfulness    kind=Stars        value=5    textValue=None   author=demo-tracon-operator
overall        kind=Binary       value=0    textValue=None   author=demo-tracon-operator
severity       kind=Categorical  value=None textValue=minor  author=demo-tracon-operator
similarity     kind=Numeric      value=0.87 textValue=None   author=demo-tracon-operator
rows: 5
```

Eval koşumu (`GET /api/evals/runs/{id}`):

```json
"scores": [
  { "name": "non_empty", "kind": "boolean", "value": true,
    "passed": true, "rating": "Good", "reason": "Response length 32 meets minimum 1" }
]
```

Faz öncesi aynı alan yalnız `{"name":…,"passed":…,"reason":…}` taşıyordu.

### Doğrulama komutları

```bash
# İki farklı ad, iki ayrı satır
curl -s -X POST http://localhost:5081/tracon/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"name":"helpfulness","kind":"Stars","value":4}'
curl -s -X POST http://localhost:5081/tracon/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"name":"accuracy","kind":"Binary","value":1}'
curl -s http://localhost:5081/tracon/api/runs/$RUN/feedback | jq 'length'   # 2 bekleniyor

# Ondalık korunuyor mu
curl -s -X POST http://localhost:5081/tracon/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"name":"similarity","kind":"Numeric","value":0.87}'
curl -s http://localhost:5081/tracon/api/runs/$RUN/feedback | jq '.[] | select(.name=="similarity").value'
```

---

## Plandan Sapmalar

| # | Plan ne diyordu | Ne yapıldı | Gerekçe |
|---|---|---|---|
| 1 | Planlanan Public API yalnız `RunScore` ve `RunScoreKind`'ı büyütüyordu | **`public static class RunScoreRules`** eklendi (`DefaultName`, `MaxNameLength`, `MaxTextValueLength`, `NameDescription`, `IsValidName`, `Validate`) | Invariant'ı **dört** store birden zorlamalı ve sözleşme testi bunu talep ediyor. `Tracon.Sql.Shared` linked source'tur (K-176) ve üç ayrı derlemeye derlenir; `Abstractions`'ın `internal`'ına erişemez — üç yeni `InternalsVisibleTo` yazmak paket üstverisine sızardı. `IRunScoreStore` bir **genişleme noktasıdır**: tüketicinin kendi store'u da aynı kuralı uygulamak zorunda ve sevk edilen `RunScoreStoreContract` onu buna zorluyor. Kuralı ikinci kez elle yazmak ikinci bir doğrulama yolu üretirdi (planın 152.2'de kendi yazdığı gerekçe). Public tip sayısı 380 → **381** |
| 2 | 152.3(4): "`Kind` `Categorical` ise `TextValue` doludur ve `Value` `null`'dır; **değilse tersi**" | Invariant bir **şekil** kuralıdır, bir **varlık** kuralı değil: `Categorical` → `TextValue` dolu **ve** `Value` null; diğer kind'ler → `TextValue` null, `Value` **null olabilir** | Planın harfi kendi DoD'siyle çelişiyordu: "`Value = null` 'ölçüm yok' olarak geri döner" satırı, `Value`'nun non-categorical kind'lerde zorunlu olmasıyla aynı anda doğru olamaz. Depo katmanında `null` meşrudur (ölçüm yok); **HTTP ucu** daha katıdır ve non-categorical bir gövdede `value` ister — bugünkü davranış korunur |
| 3 | Hata modu tablosunda "İptal: yazma ortasında `CancellationToken` iptal olur → Sözleşme" | Sözleşmeye **eklenmedi** | Repoda hiçbir store sözleşmesinin iptal case'i yok ve bellek içi store'lar token'ı hiç okumuyor. `RunScoreStoreContract` **sevk edilen** bir sözleşmedir; oraya iptal case'i eklemek her üçüncü taraf store'a yeni bir zorunluluk yükler ve bu tek fazın değil, tüm store ailesinin kararıdır. `docs/ADAYLAR.md` **F-213** olarak yazıldı |
| 4 | Hata modu tablosunda "Eşzamanlılık: aynı `(run, author, name)` iki eşzamanlı yazım → Sözleşme" | `Repeated_writes_of_the_SAME_name_leave_exactly_one_row` yazıldı (art arda beş yazım) | Sözleşme fixture'ı sağlayıcı başına **tek bağlantı** tutar; paralel bir yazım demeti tekillik indeksini değil bağlantıyı ölçerdi. Art arda yazım, indeks değişiminin ilk göstereceği arızayı (anahtar tutmuyor ⇒ her yazım yeni satır) **gerçekten** yakalar. Adı da bunu söyler — "Concurrent" demez |
| 5 | Manuel case tablosu `201` bekliyordu | Uç `200 OK` döner | Uç Faz 31'den beri `TypedResults.Ok` döndürüyor; plan bunu yanlış hatırlamış. Davranış değiştirilmedi — geriye uyumluluk `201`'e geçmekten daha değerli |
| 6 | Plan `POST /feedback` rolünü `Reader` yazıyordu | Uç `Operator` ister (değişmedi) | Plan tablosu yanlıştı; yazma ucunun `Reader` olması bir güvenlik gerilemesi olurdu |
| 7 | Plan yalnız `run_scores` tekillik indeksinin `COALESCE(message_id,'')` biçiminden söz ediyordu | SQL Server indeksi `message_id`'yi **`COALESCE`'suz** kullanır ve `WHERE author IS NOT NULL` ile **filtrelidir** | K-184: SQL Server `NULL`'ları birbirine **eşit** sayar — PostgreSQL'in tam tersi. `0005_run_scores.sql` bunu yazıyordu; `0035` yalnız `name`'i ekleyip filtreyi korudu |
| 8 | Plan SQLite için "tablo yeniden yazımı gerektirir" diyordu | Tablo **yeniden kurulmadı**; `ADD COLUMN value_real` → `UPDATE` → `DROP COLUMN value` → `RENAME COLUMN` kullanıldı | K-666: `foreign_keys = ON` altında `DROP TABLE` örtük bir `DELETE FROM` yapar ve kaçış (`PRAGMA foreign_keys = OFF`) migration'ın işlemi içinde **no-op**'tur. `value` hiçbir indekste değil, yani `ALTER TABLE ... DROP COLUMN` (SQLite 3.35+) tam olarak bu vaka için doğru araç |
| 9 | Plan `SerializeScores`'un `Metadata` yazmamasını XML dokümanına yazmayı öneriyordu | Gerekçe **implementation yorumuna** taşındı | `ShippedDocumentationSelfContainmentTests`: 🚨 emoji ve iç referans sevk edilen XML dokümanına giremez. Aynı sebeple `OnlineEvalJobHandler`'ın K-638 uyarısı da `///`'den `//`'ye taşındı |
| 10 | Planda yoktu | **Arayüz kusuru düzeltildi:** `feedback-control.tsx` "benim skorum"u yalnız `messageId == null` ile arıyordu | Bir yargıç skoru da `messageId` taşımaz. Sıralama garantisi olmadığı için (`ListAsync`: "No order is guaranteed") başparmak paneli **yargıcın** satırını kendi satırı sanabiliyor, `Kaldır` da **yargıcın skorunu silebiliyordu**. Eşleşme artık `source === 'human' && name === 'overall'`. Faz 152 öncesinden gelen bir kusurdur; aynı kod yolu bu fazda zaten elden geçtiği için burada kapatıldı |

## Bu Fazda Verilen Kararlar

> `K-*` numaraları `docs/KARARLAR.md` içindedir.

| Karar | Nerede |
|---|---|
| `RunScore.Name` tekillik anahtarına girer; `author` `COALESCE` edilmez ve judge'ın `author` kaçışı korunur | K-710 |
| `RunScore.Value` `required int` → `double?`; `null` "ölçüm yok" demektir; `TextValue` + `RunScoreKind.Categorical` eklenir | K-711 |
| `RunScoreRules` **public**'tir; invariant dört store'da da aynı tek kaynaktan zorlanır | K-712 |
| `SerializeScores` metriğin somut tipine göre değer/derece/tanı yazar; `Metadata` ve `Context` **yazılmaz** | K-713 |

Kullanıcı kararları (2026-09-07, açık sorular): varsayılan ad `overall` · `name`
HTTP gövdesinde opsiyonel · `Stars` ve `Numeric` ayrı kalır · `EvaluationRating`
kalıcı sütuna girmez · `TextValue` sınırı 256.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bağımsız bir denetçiyle koşuldu (2026-09-07).
Denetçi kendi doğrulamalarını da koştu: `dotnet build` 0 uyarı ·
`SerializeScoresTests` 8/8 · `RunScoreValidationTests` 21/21 ·
`OnlineEvalCheckpointTests` 13/13 · `RunScoreNameTests` 14/14 ·
`OnlineEvalRetryTests` 3/3 · `RunFeedbackEndpointTests` 10/10 · SQLite ve
bellek içi `RunScoreStoreContract` 28/28 · Architecture 63/63 · frontend
`tsc` + `vitest` 231/231 · docs-site üç kapı.

### 🔴 Kapatıldı

| # | Bulgu | Kapanış |
|---|---|---|
| 1 | **`PositiveRate`'in PAYDASI `Value = null` olan `Binary` satırları sayıyordu.** Faz `null` kuralını ortalama yolunda (`InMemoryRunStore.Analytics.cs`) düzeltti, oran yolunda düzeltmedi. Yalnız pozitif skorlanmış bir `run`, yanına ölçümsüz bir satır düşünce `positiveRate = 0.5` okuyordu; doğru cevap `1.0`. `RunScoreRules` ölçümsüz bir `Binary` skoru kabul ettiği için (`A_numeric_score_with_no_measurement_is_accepted`) bu yolu tüketici kodu doğrudan üretebilirdi | Üç sağlayıcının `SelectRunStatistics` ifadesine **paydada da** `value IS NOT NULL` eklendi; `InMemoryRunStore.Statistics.cs` `score.Value is null` satırını **tamamen** atlıyor. İki yeni test dört yerde birden: `A_binary_score_with_no_measurement_leaves_the_rate_ALONE` ve `A_run_scored_ONLY_without_a_measurement_has_NO_rate`. 🚨 Düzeltmeden **önce** koşuldu ve **ikisi de kırmızıydı** (`failed: 2`) |

### 🟡 Kapatıldı

| # | Bulgu | Kapanış |
|---|---|---|
| 2 | Planın söz verdiği **eşzamanlılık** sözleşme case'i yoktu | `Concurrent_writes_of_the_SAME_name_leave_exactly_one_row` — sekiz paralel `UpsertAsync`, dört koşumda. Emsal `IdempotencyStoreContract` |
| 5 | `RunScoreRules` *"iki ad kuralı yazmak iki doğrulama yolu üretir"* diyerek public yapıldı ama **ikinci kopya `RunJudgeSet.cs:12-19`'da duruyordu** | `RunJudgeSet` artık `RunScoreRules.IsValidName` çağırıyor. Kural tek yerde; `The_name_rule_matches_the_judge_name_rule` artık kaymayı değil eşdeğerliği koruyor |
| 6 | Üç migration testi `migrations[^1]`'e sabitliydi — **Faz 154 ilk migration'ını ekler eklemez üçü birden kırılırdı** | Üçü de artık migration'ı **adıyla** buluyor (`migrations.Single(… Name == "0048_…")`) |
| 7 | Arayüz kusuru düzeltmesi (sapma 10) **testsizdi**; tek kanıtı kodun kendi yorumuydu | `feedback-control.test.tsx` — iki case. Birincisi eski seçiciye karşı **kırmızı görüldü** |
| 8 | Manuel kabul case'leri henüz sette değildi; ayrıca `MT-EVAL-084` artık var olmayan `run_scores_target_author_idx` indeksini adıyla anlatıyordu | `EVAL-112 … EVAL-119` eklendi (`201` değil `200` bekleniyor — uç `TypedResults.Ok` döner). `MT-EVAL-084` yeni indeks adına güncellendi |
| 9 | Faz dokümanının HTTP tablosu *"Gövde `name` (zorunlu)"* diyordu; karar ve kod opsiyonel | Tablo düzeltildi; rol sütunu da düzeltildi (`Reader` → `Operator`, kod hiç değişmedi) |
| 10 | 🚨 **SQL Server backfill'i `nvarchar(64)` taşırabilir ve dolu bir müşteri veritabanında migration'ı yarıda kesebilirdi.** `author` `nvarchar(200)`; `judge:` önekli 71+ karakterlik bir `author` *"String or binary data would be truncated"* verirdi. İnsan `author`'ı `actorResolver.Resolve()`'dan gelir ve 64 karakter sınırına tabi değildir | `LEFT(SUBSTRING(author, 7, LEN(author)), 64)` ile sınırlandı; gerekçe migration yorumunda. Kesilmiş bir eski ad okunur, yarıda kalmış bir migration okunmaz |
| 3 | Planın söz verdiği **iptal** sözleşme case'i yoktu | **Gerekçelendi, eklenmedi** — bkz. sapma 3. `docs/ADAYLAR.md` F-213 |
| 4 | `RunScoreRules` plan dışı public API; gerekçe yalnız XML dokümanındaydı | **Gerekçelendi** — sapma 1 ve K-712 |

### 🟢 Kapatıldı veya aday listesine

| # | Bulgu | Sonuç |
|---|---|---|
| 12 | SQLite `0035` reponun standart *"`ADD COLUMN IF NOT EXISTS` yok; güvence runner'dan gelir"* notunu taşımıyordu | **Kapatıldı** — yorum eklendi, `DEFAULT`'un üç sağlayıcıda neden aynı olduğu da yazıldı |
| 15 | Üretilen sayfada bozuk cümle: *"Set only when RunScoreKind RunScore.Kind is RunScoreKind.Categorical"* | **Kapatıldı** — `<see cref="Kind"/>` iç içe `cref` render'ı bozuyordu; cümle yeniden yazıldı |
| 11 | Bir judge birden çok ad tutarsa `ReadAlreadyScoredJudgesAsync` sözlüğe **son** satırı yazar; `SelectRunScores`'ta `ORDER BY` yok | **Devredildi.** Checkpoint kararı yalnız *varlığa* bakar (K-638 doğru korunmuş) ve dönen `RunScore` yalnız raporlamada kullanılır; bugün judge tek ad yazar |
| 13 | `RunScoreRules.Validate` `Binary` 0/1 ve `Stars` 1..5 **aralıklarını** kontrol etmiyor; o kontrol yalnız HTTP ucunda | **Devredildi.** Faz öncesi de böyleydi; aralık kuralı bir **sunum** kuralıdır ve kalıcı kayda giren yanlış bir aralığı bugün de hiçbir depo reddetmiyordu |
| 14 | `RunToCasePromoter` artık gözden geçirenin **herhangi bir adındaki** 0/≤2 skoruyla `run`'ı negatif sayıyor | **Devredildi.** İstenen davranıştır (bir ad kötüyse `run` terfi adayıdır) ama ölçülmüş bir talep yok |

### Denetimin temiz bulduğu başlıklar

Test tiyatrosu yok · test seviyeleri doğru (depo → sözleşme, HTTP → fonksiyonel,
migration → sağlayıcı başına integration, saf fonksiyon → birim) · imza-gövde
kayması yok (`required string Name` derleyiciyi zorluyor; `RunScoreColumns`
sona eklendiği için ordinal okuma bozulmuyor) · repo kuralları temiz (İngilizce,
XML dokümanı, MAF tipi sarmalanmamış, `ConfigureAwait(false)`, `reflection` yok)
· K-638 korunmuş · üç migration da veri kaybı üretmiyor ve sıra doğru ·
muafiyet listeleri ve taban çizgileri **büyümedi**.

🚨 Denetçi PostgreSQL ve SQL Server koşumlarını kendi ortamında
çalıştıramadı (container gerektirir) ve bunu açıkça yazdı. Ana oturum ikisini
de koştu: **PostgreSQL 737/737 · SQL Server 672/672 · SQLite 681/681** —
düzeltmelerden sonra. Arayüz E2E 58/58, `Core.UnitTests` 2503/2503.

## Sonraki Faza Devir Notu

**Faz 154 doğrudan bu fazın çıktısına oturur.** Devraldığı sözleşme birebir:

```csharp
public sealed record RunScore
{
    public required string Name { get; init; }   // [A-Za-z0-9._-]{1,64}
    public required RunScoreKind Kind { get; init; }
    public double? Value { get; init; }          // null = ÖLÇÜM YOK, sıfır DEĞİL
    public string? TextValue { get; init; }      // yalnız Kind == Categorical
    // Id · TenantId · RunId · MessageId · Comment · Source · Author · CreatedAt değişmedi
}

public enum RunScoreKind { Binary = 1, Stars = 2, Numeric = 3, Categorical = 4 }

public static class RunScoreRules
{
    public const string DefaultName = "overall";
    public const int MaxNameLength = 64;
    public const int MaxTextValueLength = 256;
    public const string NameDescription = "…";
    public static bool IsValidName(string? name);
    public static void Validate(RunScore score);  // ArgumentException
}
```

`IRunScoreStore` imzaları **değişmedi**. `UpsertAsync` artık
`RunScoreRules.Validate` çağırır ve invariant ihlalinde `ArgumentException`
atar — dört uygulamada da (bellek içi, üç SQL, ve `samples/…FileRunStore`).

### 🚨 Faz 154'ün bilmesi gerekenler

- **`Value` `null` olabilir ve bu ORTALAMAYA GİRMEZ.** `AVG` `NULL`'ları
  zaten yok sayar; bellek içi karşılığı `InMemoryRunStore.Analytics.cs`'te
  elle yazılıdır (`score.Value is { } value`). 154'ün `NoValueCount` alanı bu
  ayrımın devamıdır ve **sayıma** girmelidir.
- **Kırılım anahtarı `(name, kind)` çiftidir, tek başına `name` DEĞİL.** Aynı
  ad iki farklı `kind` ile yazılabilir; şema bunu yasaklamıyor ve
  `RunScoreRules` de yasaklamıyor.
- **`Categorical` skorun `Value`'su her zaman `null`'dır.** Sayısal bir
  toplulaştırma onu **hiç görmemelidir**; kategorik dağılım `text_value`
  üzerinden sayılır.
- **Tekillik indeksi yeniden yazıldı ve adı değişti:**
  `run_scores_target_author_idx` → `run_scores_target_author_name_idx`.
  PostgreSQL/SQLite `(tenant_id, run_id, COALESCE(message_id,''), author, name)`;
  🚨 SQL Server `COALESCE`'suz `(tenant_id, run_id, message_id, author, name)`
  ve `WHERE author IS NOT NULL` ile **FİLTRELİ** (K-184). 154'ün ekleyeceği
  `(tenant_id, created_at)` indeksi bunlardan bağımsızdır.
- **Migration numaraları alındı:** PostgreSQL `0048`, SQLite `0035`,
  SQL Server `0035`. 154 sıradakileri alır (`0049` / `0036` / `0036`).
- 🚨 **Yeni migration'ı `scripts/applied-migrations.json`'a PİNLEMEYİ unutma.**
  `kapi.py tarama` yeni bir migration dosyasını manifest'te bulamazsa kırmızı
  olur; akış: önce özellik commit'i, sonra o commit'in sha'sıyla manifest
  commit'i (emsal: `9a981009`).
- **`SqlDialect.AddDouble` ve `DbHelpers.GetNullableDouble` eklendi.** Nullable
  bir `double` bağlarken/okurken bunları kullan; `AddDecimal` para içindir
  (SQL Server `Precision`/`Scale` ister).
- **`RunScoreColumns` sırası: `… created_at, name, text_value`.** `name` ve
  `text_value` **SONA** eklendi çünkü `SqlRunScoreStore.Read` ordinal okur.
  Yeni bir sütun yine **sona** eklenir.

### Faz 153'e devir

Faz 153 `eval_case_results`'a dokunur ve bu fazla **çakışmaz**. Tek kesişim:
`EvalCaseResult.Scores` JSON'unun şekli değişti — artık her metrik
`kind`/`value` ve (yorumlanmışsa) `rating` ile `diagnostics` taşıyor.
Regresyon farkı skoru okuyacaksa artık `passed` yerine `value`'yu
karşılaştırabilir. `metadata` **hiçbir zaman** yazılmaz (K-713).

### Açık uçlar

- İptal davranışı store sözleşmelerinde hâlâ yazılı değil — `docs/ADAYLAR.md`
  **F-213**.
- Mesaj düzeyinde skorlama ve yıldız derecelendirme arayüzde hâlâ açık
  değil (Faz 31'den kalan boşluk); `feedback-control.tsx` yalnız `overall`
  adını **yazar**, diğer adları salt okunur listeler.
