# Faz 176 — Evaluator Sürüm Damgası

> **Durum:** ✅ Tamamlandı (2026-09-16)
> **Plan onayı:** onaylandı (kullanıcı, 2026-09-16) — dört açık soru da öneriler yönünde kapandı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-218**
> **Önkoşul:** [Faz 155](155-KALIBRE-EDILMIS-EVALUATOR-KATALOGU.md) — `IEvaluator` → `IRunJudge` köprüsünü o faz kurdu; damga onun üstüne biner
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.PostgreSql`, `Tracon.SqlServer`, `Tracon.Sqlite`
> **Yeni paket:** Yok · **Migration:** gerekli — **üç set** (PostgreSQL + SqlServer + Sqlite); numara uygulama anında alınır
> **Public API:** büyüyor — `RunScore`'a bir alan. Faz 7'den **önce** ucuz, sonra **kırıcı**
> **Tüketici yüzeyi:** `docs-site/src/content/docs/concepts/` skor sayfası · sevk edilen: `RunScore` XML dokümanı
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
> git show 29d6363d:docs/arsiv/fazlar/176-EVALUATOR-SURUM-DAMGASI.md
> ```
>
> Damıtıldı 2026-09-16 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`AddEvaluatorJudge` ile bağlanan bir `IEvaluator`'ın puanı `Microsoft.Extensions.AI.Evaluation.Quality`'nin **prompt'una** bağlıdır ve o prompt paket sürümüyle değişir. Skor satırı hangi sürümün ürettiğini kaydetmiyor.

## Bitiş Ölçütleri (DoD)

- [x] `RunScore.EvaluatorVersion` üç sağlayıcıda **ve** bellek içi store'da gidiş-dönüş yapıyor — `RunScoreStoreContract` dört koşumda birden yeşil (PostgreSql 794 · SqlServer 711 · Sqlite 730 · bellek içi)
- [x] 🚨 Sütun üç migration'da da tablonun **sonuna** eklendi; ordinal **12** (plan 11 diyordu — `TextValue` atlanmıştı). Gerçek SQLite `PRAGMA table_info` çıktısında `evaluator_version` son sütundur
- [x] Migration öncesi yazılmış satırlar `null` ile okunuyor; hata yok — `A_score_with_no_evaluator_version_reads_back_as_null`
- [x] `AddEvaluatorJudge` ile üretilen skor sürümü taşıyor; `AddModelRunJudge` ile üretilen **taşımıyor** — örnek uygulamada tek yanıtta ikisi birden görüldü (aşağıda)
- [x] Attribute çözülemediğinde skor **yine yazılıyor** — `An_evaluator_with_NO_assembly_version_is_still_scored` (dinamik assembly probu)
- [x] Sürüm çağrı başına değil, kurulumda **bir kez** çözülüyor — `The_version_is_resolved_ONCE_at_construction_not_per_call`; **mutasyonla** doğrulandı (alan → property yapılınca test düştü)
- [x] `PublicAPI.Shipped.txt` boş olduğu ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` → **17 satır / 17 dosya**, hepsi `#nullable enable`
- [x] AOT koşumu temiz — `Tracon.Package.Tests` **53/53**. 🚨 `DEVELOPER_DIR=/Library/Developer/CommandLineTools` gerekti; sebebi repo değil, makinedeki Xcode linker / CLT SDK uyuşmazlığıdır ve faz **öncesi** commit'te de aynıdır (bkz. Plandan Sapmalar 10)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis` çıktısı aşağıda
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (SQLite + yerel OpenAI-uyumlu stub uç; **38 migration** uygulandı). Gerçek veritabanı satırları:

  ```
  name                   source             evaluator_version
  overall                human              None
  model                  judge:model        None
  relevance.Relevance    judge:relevance    '10.9.0+b10f9c0a081b5dbb7755b8f5592e1d3c3f550a3a'
  ```

  🚨 Damgalanan sürüm **katalog paketinin** sürümüdür, Tracon'un değil — `evaluator.GetType().Assembly` okumasının doğru assembly'yi bulduğunun gerçek kanıtı budur. `PRAGMA table_info(tracon_run_scores)` son sütun olarak `evaluator_version` gösterdi.
- [x] `secret` taraması boş döndü — `kapi.py tarama`
- [x] Manuel kabul case'leri eklendi: **EVAL-143…146**; dördü de örnek uygulamada elle koşuldu
- [x] `faz-denetim` koşuldu; bulgular §Denetim Bulguları'nda
- [x] `docs-site/` güncellendi (`concepts/evaluation.md` · `capabilities.md` · `getting-started/persistence.md`); **dört** site kapısı da temiz (`npm run check`: content 1146 · links 188 146 · SEO 0 hata · weight)

### Doğrulama komutları

```bash
# Üç sağlayıcıda sözleşme
dotnet test tests/Tracon.PostgreSql.IntegrationTests
dotnet test tests/Tracon.SqlServer.IntegrationTests
dotnet test tests/Tracon.Sqlite.IntegrationTests

# Public API yüzeyi bugün gerçekten boş mu
wc -l src/*/PublicAPI.Shipped.txt

# Skor satırını gör
curl -s http://localhost:5081/tracon/api/runs/{runId}/scores | jq '.[].evaluatorVersion'
```

---

## Plandan Sapmalar

**1. 🚨 Plan TAŞIYICI KATMANI atlamıştı — fazın tek gerçek tasarım boşluğu buydu.**
Plan sürümün `EvaluatorRunJudge`'de çözüleceğini ve `RunScore`'a yazılacağını
söylüyordu, ama `RunScore`'u köprü kurmuyor: `OnlineEvalJobHandler` kuruyor
(`grep -rn "new RunScore" src/` iki üretim noktası gösterdi). Aradaki sözleşmede
sürümü taşıyacak bir alan yoktu. Üç seçenek kullanıcıya soruldu ve
**`RunJudgment.EvaluatorVersion`** seçildi (K-788). Planın dosya listesinde
`OnlineEvalJobHandler.cs` ve `IRunJudge.cs` **yoktu**.

**2. Planın ordinal kanıtı bir eksikti.** Plan `SqlRunScoreStore`'un
`reader.GetString(10)`'a kadar okuduğunu yazıyordu; gerçek okuyucu **11**'e kadar
gidiyor (`TextValue`, Faz 152). Yeni ordinal bu yüzden **12**'dir, 11 değil.

**3. Planın dosya listesi ÜÇ DIALEKT SORGUSUNU atlamıştı.** Yalnız
`SqlQueriesBase.cs` yazılıydı, ama `UpsertRunScore` üç sağlayıcıda **ayrı ayrı**
tanımlıdır (`PostgresQueries` · `SqlServerQueries` · `SqliteQueries`) ve SQL
Server ayrıca bir `runScoreOutput` sabiti taşır. Dördü de değişti.

**4. `MigrationParityTests` YOKTU.** Plan onu mevcut bir kapıymış gibi anıyordu;
`find tests -iname "*migration*"` böyle bir dosya bulamadı. Faz onu **yazdı**
(`tests/Tracon.Sql.Shared.UnitTests/MigrationParityTests.cs`) — Docker istemez,
üç sağlayıcının gömülü migration metnini okur. Mutasyonla doğrulandı: SQLite
migration'ı silindiğinde kapı `Sqlite: run_scores.evaluator_version` diyerek
düştü.

**5. `InMemoryRunScoreStore` DEĞİŞMEDİ.** Plan onu listelemişti; store `RunScore`
kaydını bütün olarak tuttuğu için yeni alan kendiliğinden gidiş-dönüş yapıyor.

**6. Köprü sürümü KIRPIYOR, doğrulamaya bırakmıyor.** Açık Soru 1'in "C" cevabı
`RunScoreRules`'a bir sınır koydu. Ama köprü o sınırı **aşan** bir assembly
sürümü görürse doğrulamaya düşmek skorun **hiç yazılmaması** demekti — §176.2
kural 2 ile çelişir. Köprü bu yüzden kırpar; sınır ihlali yalnız **üçüncü taraf
bir yargıç** `RunJudgment.EvaluatorVersion`'ı elle doldurduğunda sözleşme hatası
olur.

**7. `TryValidate`'in PROBE kaydı da sürümü taşır.** Faz 155 devir notu
"doğrulama ilk yazmadan önce topludur" diyor. Sürüm probe'a konmasaydı aşırı uzun
bir sürüm ilk satırda **store'da** patlar ve yarım yazılmış küme bırakırdı.

**8. Sevk edilen doküman cırcırı dört dosyada kızardı.** `///` blokları içine
yazdığım 🚨 işaretleri `ShippedDocumentationSelfContainmentTests`'i düşürdü. Taban
**yalnız küçülür**, bu yüzden yenilenmedi: işaretler uygulama yorumuna (`//`)
taşındı, gerekçe cümleleri XML dokümanında kaldı.

**9. Kapsam dışı kusur çözüldü — `tuketici-dokuman-senkronu` skill'i bayat sayı
taşıyordu.** SKILL.md agent haritası bütçesini "10 240 B" diye yazıyordu; gerçek
sabit `agentMapBudgetBytes = 11264`. Kendi kalite sözleşmesi dosyası bu sayının
bir kez bayatladığını **zaten** kaydetmişti. Satır sayı yerine sabite yönlendirdi.

**10. Kapsam dışı ortam kusuru teşhis edildi (repo kusuru DEĞİL).**
`Tracon.Package.Tests`in NativeAOT case'i bu makinede linker hatasıyla düşüyor.
`git worktree` ile **faz öncesi** commit'te birebir aynı hata alındı, yani fazın
regresyonu değildir. Kök sebep: `xcode-select` Xcode.app'i gösteriyor
(`ld-1267`, Haz 2026) ama SDK Command Line Tools'un 27.0'ı (CLT'nin kendi
linker'ı `ld-27037.1`). `DEVELOPER_DIR=/Library/Developer/CommandLineTools` ile
53/53 yeşil. Tuzak `docs/hafiza/test-kosum-tuzaklari.md`'ye yazıldı; kalıcı
çözüm sistem düzeyindedir ve kullanıcıya bırakıldı. CI etkilenmez
(`ubuntu-latest`).

**11. Planlanan manuel case 5 ("üç sağlayıcı da kurulu — 1. case'i her birinde
tekrarla") YAZILMADI.** Onu kapatan şey `RunScoreStoreContract`'tır: aynı beş
case üç SQL sağlayıcısında **ve** bellek içi store'da otomatik koşuyor, yani elle
koşumdan hem daha sık hem daha güvenilir. Elle bir kopyası iki yerde bakım
maliyeti üretirdi. Bu sapma denetimde 🟡 olarak bulundu ve buraya yazıldı.

**12. Denetimin bulduğu ÜÇ test zayıflığı kapatıldı.** (a) `RunJudgeContract`
yeni değişmezi denetlemiyordu — üçüncü taraf bir yargıç sözleşmeyi geçip üretimde
**bütün** skorlarını kaybedebilirdi. (b) `MigrationParityTests` sütun adını
**tablo bağlamı olmadan** arıyordu; `status` gibi başka tablolarda geçen bir ad
kapıyı sessizce yeşil bırakırdı (mutasyonla doğrulandı: artık üç sağlayıcıda da
reddediliyor). Aynı düzeltme, SQL yorumundaki bir `;`'nin `CREATE TABLE`'ı
kesip altı sütunu düşürdüğünü de ortaya çıkardı. (c) İkinci parite testi
`SelectRunScores`'u kendisiyle karşılaştırıyordu (tek atama paylaşılan tabandadır);
gerçekten kayabilen yüzeye — üç dialektin `UpsertRunScore` `VALUES` listesi ve SQL
Server'ın `OUTPUT` listesi — çevrildi ve mutasyonla doğrulandı.

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-788** — Sürüm `RunJudgment.EvaluatorVersion` ile taşınır; `IRunJudge` seam'i **değişmez** | Üç seçenek ölçüldü. `IRunJudge.Version` anlamsal olarak en yakınıydı ama public extension seam'ini büyütürdü; `JudgeScore.Version` aynı dizeyi metrik başına tekrarlardı. `RunJudgment` yargıcın Tracon'a **zaten** veri döndürdüğü kanaldır ve provenance kararın parçasıdır. **(kullanıcı kararı)** |
| **K-789** — `RunScore.EvaluatorVersion` `varchar(128)`; sınır `RunScoreRules.MaxEvaluatorVersionLength`'tedir ve `Validate` zorlar | Değeri **yargıç** sağlar. Sınırsız bir sütun, üçüncü taraf bir yargıcın tüketicinin tablosunda bir satırın ne kadar yer kaplayacağına karar vermesi demektir. `MaxTextValueLength` emsali. **(kullanıcı kararı)** |
| **K-790** — `null` = **sürüm çözülmedi**, "sürüm sıfır" değil; çözülemezse skor **yine yazılır** | K-711'in `Value` için kurduğu kuralın aynısı. Sürüm gözlemlenebilirliktir ve gözlemlenebilirlik işlevselliği bozmaz: okunamayan bir assembly attribute'u kiracıya skor satırlarına mal olamaz. |
| **K-791** — `ModelRunJudge` alanı **doldurmaz** | Yerleşik yargıcın puanı bir paketin prompt'una değil **yapılandırılan modele** bağlıdır; o bilgi `ModelBinding`'de durur. Tracon'un kendi sürümünü basmak sütunu iki farklı anlama gelir hâle getirirdi. |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan onaylandı; 4 açık soru + 1 yapısal boşluk uygulama öncesi kapatıldı) |
| Düzeltme turu sayısı | 4 (public API bildirimi · sevk doküman cırcırı · analyzer MA0002/CA1859 · site llms yeniden üretimi) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | `faz-denetim` bölümüne bak |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | — |
| Plan yapısal iddiası düşen | **4/4 boşluk** (taşıyıcı katman · ordinal sayısı · üç dialekt · var olmayan test) |
| Mutasyonla doğrulanan kapı | 2 (`Version_is_resolved_ONCE` · `MigrationParityTests`) |

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, salt-okunur) `git diff 04ec6d91` + dört izlenmeyen
dosya üzerinde koştu.

**🔴 bulgu: YOK.** Denetimin ağır dört maddesi temiz çıktı: ordinal zinciri
(paylaşılan sütun listesi ↔ üç `VALUES` ↔ `runScoreOutput` ↔ okuyucu ordinali)
tutarlı · üç migration anlamca aynı (nullable, backfill yok) · imza-gövde kayması
yok (`new RunScore` üç üretim noktası, `new RunJudgment` üçü de izlendi; dördüncü
store kaydı bütün olarak sakladığı için geride kalmıyor) · `PublicAPI.Unshipped.txt`
girdileri gerçek imzalarla eşleşiyor.

**🟡 beş bulgu — hepsi bu fazda kapatıldı:**

| # | Bulgu | Kapanış |
|---|---|---|
| 1 | `RunJudgeContract.ShouldBeStorable` yeni değişmezi denetlemiyordu: 129 karakterlik bir sürüm sözleşmeyi **geçiyor**, sonra üretimde yargıcın **bütün** skorlarını kaybettiriyordu | Sınır kontrolü sözleşmeye eklendi, skorlardan **önce** — çünkü ihlal tek alanı değil tüm kümeyi düşürür |
| 2 | 🚨 `MigrationParityTests` sütunu **tablo bağlamı olmadan** arıyordu; 13 sütunun 12'si için kapı fiilen boştu (`status`, `state`, `key` her sağlayıcının setinde zaten geçiyor) | Arama `CREATE TABLE`/`ALTER TABLE … run_scores` ifadeleriyle sınırlandı. **Mutasyonla doğrulandı:** `status` artık üç sağlayıcıda da reddediliyor. Düzeltme ikinci bir kusuru açığa çıkardı: SQL yorumundaki bir `;` `CREATE TABLE`'ı kesip **altı sütunu** düşürüyordu — yorumlar artık taramadan önce ayıklanıyor |
| 3 | İkinci parite testi bir sabiti **kendisiyle** karşılaştırıyordu: `SelectRunScores`'un tek ataması paylaşılan tabandadır, hiçbir dialekt ezmez | Test gerçekten kayabilen yüzeye çevrildi: üç dialektin `UpsertRunScore` `VALUES` listesi + SQL Server'ın `OUTPUT` listesi. **Mutasyonla doğrulandı:** SQL Server'ın `VALUES` listesinde iki sütun yer değiştirince kapı düşüyor |
| 4 | Sevk edilen `Validate` dokümanının `<exception>` listesi üçüncü `throw` sebebini saymıyordu; `IRunScoreStore` bir genişleme noktasıdır ve tüketici o dokümana bakarak kendi store'unu yazar | Sürüm sınırı listeye eklendi |
| 5 | Planlanan manuel case 5 sessizce düşmüştü | Sapma olarak yazıldı (§Plandan Sapmalar 11) |

**🟢 üç bulgu:** biri kapatıldı (site örneği artık gerçek damganın şeklini —
`+` sonrası build revizyonunu — gösteriyor ve eşitlikle karşılaştırma öğütlüyor),
ikisi `ADAYLAR.md`'ye **F-241** ve **F-242** olarak yazıldı.

## Sonraki Faza Devir Notu

Devralınan sözleşme birebir:

```csharp
public sealed record RunJudgment
{
    public IReadOnlyList<JudgeScore> Scores { get; init; } = [];
    public string? EvaluatorVersion { get; init; }   // null = surum cozulmedi
}

public sealed record RunScore
{
    // … 12 alan …
    public string? EvaluatorVersion { get; init; }   // <= 128 karakter
}
```

### 🚨 Sonraki fazın bilmesi gerekenler

- **`run_scores` okuyucusu ÇIPLAK ORDINAL kullanır ve son ordinal artık `12`'dir**
  (`SqlRunScoreStore.Read`). Yeni bir sütun **SONA** eklenir; ortaya eklemek her
  alanı sessizce kaydırır ve komşu tipler uyuşuyorsa test **yeşil kalır**. Sütun
  listesi tek yerdedir: `SqlQueriesBase.RunScoreColumns`. Ama `UpsertRunScore`
  **üç dialektte ayrı** yazılıdır ve SQL Server ayrıca `runScoreOutput` taşır —
  bir sütun eklerken **dört** yer güncellenir.
- **`MigrationParityTests` artık bu sınıfı kapatıyor.** Bir sağlayıcının
  migration'ını unutursan kapı sağlayıcıyı **adıyla** söyler. Docker istemez.
  Yeni bir ordinal-okunan tablo eklersen o tabloyu da bu teste ekle.
- **Sürüm damgası `RunJudgment` üzerinden akar, `IRunJudge` üzerinden DEĞİL**
  (K-788). Bir yargıcın sürüm bildirmesini istiyorsan `JudgeAsync`'in döndürdüğü
  kayda yaz ve **kurulumda bir kez** çöz — `EvaluatorRunJudge._evaluatorVersion`
  bunun emsalidir ve bir test (`The_version_is_resolved_ONCE_at_construction_not_per_call`)
  onu zorlar.
- **Sürüm doğrulaması `TryValidate`'in probe kaydındadır.** `RunJudgment`'a
  saklanan yeni bir alan eklersen probe'a da ekle; yoksa ihlal ilk yazmada
  store'da patlar ve yarım küme bırakır.
- **`ModelRunJudge` bilerek damgalanmaz** (K-791). Onu damgalamak isteyen bir faz
  önce "hangi bilgiyi taşıyor" sorusunu cevaplamalı; model bilgisi
  `ModelBinding`'dedir, sürüm sütunu değildir.
- **🚨 Dinamik assembly probu testte meşru bir araçtır.** Bu repodaki her derlenen
  assembly bir informational version taşır, bu yüzden "sürümsüz assembly" ve
  "aşırı uzun sürüm" şekillerine **ancak** `AssemblyBuilder` ile ulaşılır
  (`EvaluatorRunJudgeTests.EvaluatorInAssemblyWithVersion`). Yerine bir stand-in
  koymak stand-in'i test eder, assembly okumasını değil.

### Açık uçlar

- `RunScoreQuery` sürüme göre **süzmez** (Açık Soru 2, "A"). Satır okunabildiği
  için soru bugün de cevaplanabilir; süzgeç eklemek beş özet sorgusunu, üç
  dialekti ve HTTP ucunu büyütür.
- Arayüzde **gösterilmez** (Açık Soru 3, "A"). Skor detayında bir sürüm rozeti
  ayrı bir karardır ve ölçülmedi.
- Faz 153'ün regresyon farkı sürümü **henüz okumuyor**: iki koşumun sürümü
  farklıysa bunu okuyucu elle görür, araç "yargıç değişti" diye **etiketlemez**.
  Aday olabilir.
