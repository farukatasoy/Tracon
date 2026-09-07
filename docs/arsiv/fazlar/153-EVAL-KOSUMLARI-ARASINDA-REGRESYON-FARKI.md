# Faz 153 — Eval Koşumları Arasında Regresyon Farkı

> **Durum:** ✅ Tamamlandı (2026-09-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-207**
> **Önkoşul:** Yok. [Faz 152](152-SKORUN-ADI-VE-SEKLI.md) ile **çakışmaz** — o `run_scores`'a, bu `eval_case_results`'a dokunur.
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.PostgreSql`, `AgentPrism.Sqlite`, `AgentPrism.SqlServer`, `AgentPrism.Cli`, `AgentPrism.Testing.Contracts.Xunit`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok — hizalama anahtarı (`EvalCaseResult.CaseId`) ve gereken alanlar mevcut şemadadır
> **Public API:** Büyüyor — bir okuma tipi ailesi + bir `IEvalStore` üyesi + bir CLI seçeneği. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (ölçüldü 2026-09-07): `IEvalStore`'a üye eklemek üçüncü taraf uygulayıcıyı kırar ve bu **`1.0` öncesi** yapılmalıdır.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/concepts/evaluation.md` · üretilen: `http-api/`, `api/` · sevk edilen: `EvalEndpoints` `.WithDescription` metinleri, `EvalRun` XML dokümanı, `agentprism eval` yardım metni
> **Manuel test alanı:** `docs/manuel-test/17-EVAL-VE-DENEYLER.md` · `docs/manuel-test/34-ISTEMCI-VE-CLI.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 11100124:docs/arsiv/fazlar/153-EVAL-KOSUMLARI-ARASINDA-REGRESYON-FARKI.md
> ```
>
> Damıtıldı 2026-09-07 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Ürün karşılaştırmayı **vaat ediyor ama yapmıyor**. Sevk edilen uç metni *"Comparing entries over time is how a regression between agent versions is spotted"* diyor; `IEvalStore` ise yalnız **tek bir** koşumu okuyabiliyor. İki koşumu case bazında hizalamak tüketiciye kalıyor.

## Bitiş Ölçütleri (DoD)

- [x] `GET /api/evals/runs/{id}/diff?baseline={runId}` altı kümeyi ayrı ayrı döner
- [x] Suite'e eklenen case `Added`'dir, `Regressed` **değildir**
- [x] Taban çizgisinin ayrıntısı silinmişken uç `409` döner — **boş fark dönmez**
- [x] `agentprism eval <suite> --baseline previous --max-regressions 0` regresyonda çıkış kodu **3** verir
- [x] Karşılaştırılamama çıkış kodu **4** verir; 3 ile karışmaz
- [x] İlk koşumda `--baseline previous` kapıyı kırmızı yakmaz
- [x] ~~İçeriği değişen case `contentChanged` ile işaretlenir~~ — **kapsamdan çıkarıldı** (K-716 👤). Plan kendisiyle çelişiyordu ve bayrak hiçbir zaman yanamazdı; sınır `EvalRunDiff` XML dokümanına ve siteye yazıldı
- [x] `EvalEndpoints.cs:141`'deki sevk edilen metin artık **doğrudur** (uç gerçekten karşılaştırıyor)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek iki eval koşumu + fark alındı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `17-EVAL-VE-DENEYLER.md` ve `34-ISTEMCI-VE-CLI.md` içine eklendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/concepts/evaluation.md` güncellendi; `npm run build` + `check-links.mjs` temiz
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı
- [x] OpenAPI → NSwag → TypeScript zinciri yeniden üretildi; üretilen istemci derleniyor

### Doğrulama komutları

```bash
# Fark dört kümeyi ayırıyor mu
curl -s "http://localhost:5081/agentprism/api/evals/runs/$SECOND/diff?baseline=$FIRST" \
  | jq '[.cases[] | .kind] | group_by(.) | map({(.[0]): length}) | add'

# Göreli kapı regresyonda düşüyor mu
agentprism eval smoke --baseline previous --max-regressions 0; echo "exit=$?"
```

---

## Plandan Sapmalar

| Plan | Gerçek | Neden |
|---|---|---|
| Dosya listesi üç ayrı SQL store'a (`PostgreSqlEvalStore`, `SqliteEvalStore`, `SqlServerEvalStore`) dokunacağını söylüyordu | **Tek** `src/AgentPrism.Sql.Shared/Stores/SqlEvalStore.cs` değişti | Plan bayattı: Faz 94 (SQL Tek Kaynak) üç store'u birleştirmişti. Üç uygulama yerine iki oldu (bellek içi + paylaşılan SQL) |
| `EvalRunDiffBuilder` `AgentPrism.Core`'a konacaktı | `AgentPrism.Abstractions`'a kondu ve **public** | Açık Soru 1 "üçüncü taraf uygulayıcı hizalamayı yeniden yazmak zorunda kalmasın" diyordu. Üçüncü taraf `Core`'u değil `Abstractions`'ı referans eder; `Core`'da olsaydı kurallar yeniden yazılırdı (K-714) |
| `DiffRunsAsync(Guid baselineRunId, Guid candidateRunId, ct)` | `DiffRunsAsync(EvalRunDiffQuery query, ct)` | Planın imzasında **kiracı yoktu** — depo kiracı yalıtımını zorlayamazdı, oysa planın kendi test tablosu `TenantIsolationContract` istiyordu. Sayfalama da (Açık Soru 4: evet) imzaya girmeliydi. `EvalRunQuery`'nin var olan deseni izlendi |
| `ContentChanged` / `ContentChangedCount` public API'ye girecekti | **Girmedi** (kullanıcı kararı) | Plan kendisiyle çelişiyordu: hash "bugünkü `EvalCase`'ten" okunacaktı ama iki taraf aynı case'i okur, bayrak hiç yanamazdı. Ayrıca ölçüldü — `EvalCaseInput` `Id` taşımaz, `PUT /cases` her düzenlemede yeni id atar, yani sevk edilen yüzeyde içerik değişimi zaten `Added`+`Removed`'dır. Sınır sözleşmeye yazıldı (K-716) |
| Tamamlanmamış koşumun beklenen sonucu yazılı değildi | Yalnız `Completed` karşılaştırılır; diğeri `400` (kullanıcı kararı) | `Pending` bir koşumun `Total`'ı 0'dır ve "ayrıntı silinmiş" sezgisini yanlış tetikliyordu (K-717) |
| Fark sonucu yalnız `Cases` + `ContentChangedCount` taşıyacaktı | Altı kümenin her biri için ayrı sayaç + `TotalCases` | Açık Soru 4 sayfalamayı seçti; sayfalanan bir listeden küme sayıları okunamaz. Sayaçlar sayfalamadan bağımsızdır |
| DoD `agentprism eval <suite>` yazıyordu | Komut `--suite <ad>` alır | Faz 115'ten devralınan mevcut imza; değiştirmek kırıcı olurdu |
| `EvalCaseDiff` `Output` taşımayacaktı | Taşımıyor | Plan korundu — yanıt şişmez |
| Planda olmayan: `EvalRunDiffUnavailableException` + `EvalRunDiffUnavailableReason` | Eklendi | Plan "throws" diyordu ama tipi adlandırmıyordu. Uç `409`/`400` ayrımını yapabilmek için sebep makine tarafından okunabilir olmalı (K-715) |
| Planda olmayan: kültür bağımsız biçimleme düzeltmesi (3 yer) | Yapıldı | Faz DoD'sinin "örnek uygulamayla gerçek koşum" adımında bulundu: `agentprism eval` tr-TR bir makinede "in 3,7 s" yazıyordu. Sınıf **ölçülerek** daraltıldı — `0.0`/`0.000000`/`P0` riskli, `F0`/`0` değil; ilk taramada şüphelenilen üç `:F0` yeri geri alındı (K-720) |
| Planda olmayan: `karar-damit`'in "işaretçisi var, atla" kuralı kaldırıldı | Yapıldı | Karar defteri bütçesi aşıldı; kural "taşı, silme" olduğu için önce taşıma denendi ve aracın kendisi kusurluydu (K-721) |

## Bu Fazda Verilen Kararlar

K-714 · K-715 · K-716 👤 · K-717 👤 · K-718 · K-719 · K-720 · K-721 · K-722 👤 · K-723 —
[`KARARLAR-INDEKS.md`](../../KARARLAR-INDEKS.md).

## Örnek Uygulama Koşumu (kanıt)

`samples/AgentPrism.Api` gerçek bir OpenAI binding'iyle ayağa kaldırıldı
(`gpt-5.4-mini`), `diff-demo` takımı iki vakayla kuruldu ve iki kez koşuldu.
İkinci koşumdan önce **yalnız takımın `checks` alanı** sıkıldı — vakaların
kendisi değil, çünkü `PUT /cases` her düzenlemede yeni vaka kimliği atar ve
düzenlenmiş bir vaka regresyon değil `Added`+`Removed` olur.

```
$ curl -s ".../evals/runs/$SECOND/diff?baseline=$FIRST" | jq '[.cases[].kind] | group_by(.) | ...'
{'Regressed': 2}
$ curl -s ".../evals/runs/$FIRST/diff?baseline=$SECOND" | jq ...      # ters yön
{'Fixed': 2}
$ curl -s -o /dev/null -w "%{http_code}" ".../diff?baseline=<olmayan>"
404

$ agentprism eval --url ... --suite diff-demo --baseline $FIRST --max-regressions 0
Completed: 0/2 passed in 3.7 s.
  FAILED case 01a07b03-1547-7ea3-…: keyword_check: Missing keywords: zzz-never-said
  FAILED case 01a07b03-1547-7434-…: keyword_check: Missing keywords: zzz-never-said
vs baseline 01a07b03-3889-77f2-…: 2 regressed, 0 fixed, 0 added, 0 removed.
  regressed: case 01a07b03-1547-7434-…: keyword_check: Missing keywords: zzz-never-said
  regressed: case 01a07b03-1547-7ea3-…: keyword_check: Missing keywords: zzz-never-said
exit=3

$ agentprism eval ... --baseline previous --max-regressions 0   # önceki koşum da düşüktü
vs baseline 01a07b03-a27c-7bf0-…: 0 regressed, 0 fixed, 0 added, 0 removed.
exit=0

$ agentprism eval ... --max-regressions 0                       # --baseline YOK
'--max-regressions' needs '--baseline <runId|previous>'; …
exit=1
$ agentprism eval ... --baseline yesterday
exit=1
```

Bu koşum K-720'yi de ortaya çıkardı: `in 3,7 s` satırı tr-TR bir makinede
ondalık virgülle yazılıyordu (yukarıdaki blokta düzeltme sonrası hâli).

**Arayüz payı** (faz sonrası ölçüm): **153.4 KB** brotli / 250 KB bütçe
(faz öncesi 151.9 KB; 18 sözlük anahtarı ve fark görünümü +1.5 KB).

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, yalnız DoD + diff) **üç 🔴 ve beş 🟡** buldu.
Hepsi kapatıldı; hiçbiri gerekçeyle geçilmedi.

### 🔴

| Bulgu | Kök neden | Sonuç |
|---|---|---|
| **Kısmen budanmış koşum `409` vermiyordu.** `RequireDetails` yalnız "hiç satır yok" durumunu reddediyordu | `IRetentionStore.DeleteBatchAsync` **parça parça** siler ve sweep iki parti arasında durabilir; kalan satırlarla fark üretmek silinen her vakayı `Removed` yapar ve regresyon sayısını sessizce daraltır — kapının yeşil yandığı yol | Denetim SAYIYOR: `distinct case < run.Total` ⇒ `DetailsRemoved`. Birim + sözleşme testi eklendi (`A_PARTLY_trimmed_run_…`, `DiffRunsAsync_throws_when_retention_removed_only_SOME_…`). Tekrarların yanlış alarm üretmediği ayrıca test edildi |
| **`--json --baseline` birlikte verilince `stdout` ayrıştırılamaz oluyordu** | Özet satırı koşulsuz `Console.WriteLine`'daydı; `MT-CLI-021`'in sözünü bozuyordu | `--json` altında özet `stderr`'e gider. `Json_output_stays_a_single_parseable_document_with_a_baseline` |
| **`docs/manuel-test/34-ISTEMCI-VE-CLI.md` hiç güncellenmemişti** — DoD satırı `[x]` ama CLI case'leri yoktu | Yeni çıkış kodu ve iki seçenek manuel kabul setinde ölçülmüyordu | `MT-CLI-023`…`031` eklendi (dokuzu da otomasyon karşılığıyla) |

### 🟡

| Bulgu | Sonuç |
|---|---|
| **Test tiyatrosu:** `An_exhausted_token_budget_…` hiçbir koşulda düşemezdi — `long` + format belirteci yok | Kaldırıldı. Yerine **ölçüm** kondu: tr-TR'de `0.0`/`0.000000`/`P0` farklı, **`F0` ve `0` DEĞİL**. Bu yüzden ilk taramada "düzeltilen" üç `:F0` yeri **geri alındı** — orada kusur yoktu. Gerçek sınıf üç yerdir; biri (`AgentRunBudget`) düşen-sonra-geçen bir testle kilitli, diğer ikisi (`PreflightGate`, `EvalCommand`) host/süreç sınırının arkasında ve **kapsanmadıkları açıkça yazıldı** — kültür kapsamlı bir test paralel testlere sızardı |
| **Arayüzde sayaç tüm farkı, tablo yalnız ilk sayfayı gösteriyordu** — rozet "200" derken gövde "bu grupta durum yok" diyebilirdi; fazın kendi tezinin arayüz karşılığı | İstemci `take=500` (sunucu tavanı) ister; ayrıca grup `N/M` söyler ve sayfa dışı kalan varsa bunu yazar (`evals.diff.partialPage`, `evals.diff.beyondPage`) |
| **İki DoD satırı kanıtsız `[x]`** (örnek uygulama çıktısı, bundle ölçümü) | Yukarıdaki "Örnek Uygulama Koşumu" bölümü eklendi; bundle faz sonrası yeniden ölçüldü |
| **Bütçe tavanı yükseltildi, oysa kural "taşı"** | Önce TAŞIMA denendi: `karar-damit`'in "işaretçisi var, atla" kuralı kaldırıldı (işaretçi ≠ sınıra indirilmiş) ve 1.527 B geri kazanıldı. Yetmedi — kalan büyüme satır **iskeletinde** ve o kesilmez. Tavan ölçümle yeniden kondu ve **K-721** olarak yazıldı |
| **`capabilities.md` yeni yeteneği yazmıyordu** | İki satır eklendi (koşum karşılaştırması · göreli CI kapısı) ve CLI satırı güncellendi. Bu iki satır **iki ayrı kusuru** açığa çıkardı: sevk edilen agent haritası Faz 148'den beri tavanına dayalıydı ve en kısa satır bile taşırıyordu (tavan 10 → 11 KiB, **K-722** 👤), ve üreteç bir hücredeki kaçırılmış boruda (`\|`) hücreyi kesip haritaya sarkan bir ters bölü sevk ediyordu (**K-723**, regresyon testi `build-agent-map.test.mjs`) |

### 🟢 (kapsam dışı gözlem, yine de kapatıldı)

- Yeni karar satırları tablonun dördüncü sütununu kaybetmişti (benim düzenleme
  hatam) — geri kondu.
- `ApplyBaselineGateAsync` **her** HTTP hatasını `4` sayıyordu; artık yalnız
  ucun karşılaştırmayı REDDETTİĞİ üç kod (`400`/`404`/`409`) `4`'tür, kimlik ve
  sunucu arızası `2` kalır (README'nin zaten söylediği şey).
- `--baseline` verilip `--max-regressions` verilmeyince komut rapor üretir ama
  düşmez; bu artık yardım metninde, README'de ve sitede **yazılı**.

Kapı koşumu ayrıca üç taban çizgisi/kapsam kalemi buldu (izole koşumda da düştüler):

| Bulgu | Kök neden | Düzeltme |
|---|---|---|
| `ShippedDocumentationSelfContainmentTests` | `EvalRunDiffUnavailableException`'ın XML dokümanında 🚨 vardı; sevk edilen metin geliştirme günlüğünün sesini taşıyamaz | Emoji kaldırıldı — taban çizgisi yükseltilmedi, kaynak düzeltildi |
| `PublicSurfaceBaselineTests` | Yedi yeni public tip | Taban çizgisi 381 → 388 (bilinçli yüzey büyümesi) |
| `TenantCoverageTests` | Yeni depo metodu ne test edilmiş ne muaf tutulmuştu | `DiffRunsAsync` kapsanan listeye eklendi — `EvalStoreContract` onu üç dialektte de kiracı sınırında ölçüyor |

Yazarken bulunan iki kusur:

- **Bellek içi depo `CancellationToken`'ı görmüyordu.** Sözleşme testi
  (`DiffRunsAsync_observes_cancellation`) düştü; iki uygulamaya da açık
  `ThrowIfCancellationRequested()` eklendi. Birim testi bunu kanıtlayamazdı —
  planın test tablosu bu satırı doğru biçimde **sözleşme** seviyesine koymuştu.
- **Kültür bağımlı biçimleme (K-720).** DoD'nin örnek uygulama adımında görüldü;
  sınıf tarandı, altı yer düzeltildi, davranışsal bir regresyon testi yazıldı ve
  düzeltme geri alınarak testin GERÇEKTEN kırmızı olduğu doğrulandı.

`docs/KARARLAR.md` bütçesi bu fazda aşıldı (389.987 B, 13 bayt boşluk).
`karar-damit` geri kazanamaz — 720 satırın tamamı işaretçili olduğu için
"zaten damıtılmış" sayılıp atlanıyor (`--sinir 330` kuru koşumda yalnız 129 B).
Sınır 420.000'e yükseltildi; gerekçe `scripts/dokuman-bakim.py` içinde yazılı.

### Kapı koşumunun bulduğu üçüncü kusur

Tavan yükseltilince `TemplateAgentsFileTests` düştü: bütçe sayısı orada
**elle kopyalanmış** bir `const`'tu ve yorumu bunu açıkça söylüyordu
("Mirrors `agentMapBudgetBytes` … Repeated here"). Üreteç, kendi kapısı ve site
hepsi anlaştı, yalnız bu kopya geride kaldı — repo'nun daha önce bedelini
ödediği "elle tekrarlanan ifade" sınıfının aynısı. Sayı artık tek kaynaktan
(`build-agent-map.mjs`) **okunuyor**; ikinci bir düzenleme artık kaymaz.
Sınıf tarandı: kalan iki kopya `docs/manuel-test/29-AGENT-DESTEGI.md`'deydi ve
sabit sayı yerine sabitin **adına** çevrildi.

## Sonraki Faza Devir Notu

- **`EvalRunDiffBuilder` artık üçüncü tarafın hizalama sözleşmesidir.** Yeni bir
  küme eklemek (`EvalCaseDiffKind`'a append) hem sayaç alanı hem arayüz grubu
  hem site tablosu ister; enum değer sırası **değişmez**.
- **Fark, case içeriğini koşum başına saklamaz** (K-716). Biri gerçekten
  elma-armut tespiti isterse yol `eval_case_results`'a nullable bir
  `case_content_hash` sütunu (üç dialekt + migration) ve eval koşucusunun onu
  yazmasıdır; eski satırlar `null` kalır ve bu sözleşmede yazılı olmalıdır.
- **Kültür sınıfının kapısı yoktur.** CA1305 format belirteçli interpolasyonu
  GÖRMEZ (ölçüldü: `warning`'e çekildiğinde altı ihlal dururken sıfır bulgu).
  Bugünkü guard davranışsaldır (`InvariantShippedTextTests`, iki vaka). Kalıcı
  kapı bir kaynak taraması olurdu — `docs/ADAYLAR.md` kalemi hak eder.
- **`--baseline previous` en yeni 50 koşuma bakar** (`PreviousRunSearchWindow`).
  Bir suite 50'den fazla ardışık başarısız/iptal koşum biriktirirse taban çizgisi
  bulunamaz ve kapı atlanır (sessizce değil — `stderr`'e yazar).
