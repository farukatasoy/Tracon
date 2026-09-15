# Faz 173 — Çalıştırma Kaydının Kaybı Ölçülür

> **Durum:** ✅ Tamamlandı (2026-09-15)
> **Plan onayı:** kapsam seçimi kullanıcı tarafından onaylandı (2026-09-15) · uygulama kullanıcı talimatıyla başladı (2026-09-15)
> **Kaynak:** Tüketici geri bildirimi turu (2026-09-15), *"Beşinci risk: recording semantics"*. `ADAYLAR.md`'de F-NN kalemi **yoktur**; kısmi öncül 2026-09-07 turunun **A10** kalemidir ([`arsiv/incelemeler/2026-09-07-tuketici-analizi-codebase-olcumu.md`](../incelemeler/2026-09-07-tuketici-analizi-codebase-olcumu.md)).
> **Önkoşul:** [Faz 171](171-DENETIM-IZI-YAZMA-POLITIKASI.md) — sayacın deseni (`tracon.audit.write_failures`) ve `TraconMetrics`'i statik olmayan bir yazma yoluna taşıma tekniği oradan devralınır
> **Paketler:** `Tracon.Core`, `Tracon.Workflows`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — `TraconDiagnostics` iki sabit, `TraconMetrics` bir sayaç ve bir metot, `RunEventWriter` yapıcısı bir parametre alır. **Ölçüldü (2026-09-15):** her `src/*/PublicAPI.Shipped.txt` yalnız `#nullable enable` satırını taşır (17 dosya, 17 satır) — hiçbir yüzey donmadı, bugün eklemek bedava, Faz 7'den sonra yapıcı imzası değiştirmek kırıcı olur
> **Tüketici yüzeyi:** site — [`guides/observability.md`](../../../docs-site/src/content/docs/guides/observability.md) (metrik tablosu · etiket tablosu · yeni bölüm), [`concepts/governance.md`](../../../docs-site/src/content/docs/concepts/governance.md) (§ *What is guaranteed to be written* içindeki `run` paragrafı), [`reference/threat-model.md`](../../../docs-site/src/content/docs/reference/threat-model.md) (best-effort `caution` bloğu) · sevk edilen — `RunEventWriter` ve `TraconMetrics` XML dokümanı; `api/` sayfaları bundan **üretilir**
> **Manuel test alanı:** [`docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 17775687:docs/arsiv/fazlar/173-CALISTIRMA-KAYDI-GORUNURLUGU.md
> ```
>
> Damıtıldı 2026-09-15 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'in `run` kaydı best-effort'tur: `store` hata verirse `run` sürer ve kayıt düşer. Bu **bilinçli** bir tasarımdır ve değişmez. Değişen tek şey şudur: bugün o kaybı yalnız log okuyan biri fark eder.

## Bitiş Ölçütleri (DoD)

- [x] ~~PostgreSQL **kapalıyken**~~ → **`tracon.runs` yazımı engelliyken** (gerekçe: *Plandan Sapmalar* §2) `samples/Tracon.Api` üzerinde bir `run` koşulur: `run` **tamamlanır** ve `tracon.run.recording_failures` `stage=start` ile **1** artar — komut ve çıktı *Süreç Ölçümü*'ne yazıldı
- [x] `stage` etiketi altı değerin dışına **çıkamaz**; `Disable` serbest metin gerekçe **almaz** (`grep -n "Disable(" src/Tracon.Core/Recording/RunEventWriter.cs` yalnız sabit gösterir)
- [x] `RunEventWriter` yapıcısını çağıran her yer sayacı geçirir veya bilinçli olarak `null` geçer; opsiyonel parametre **yoktur**. Planın saydığı **19** çağrı yeri değişikliğin başındaki sayıdır; faz kendi testlerini de ekleyince **23** oldu (2 üretim · 20 test · 1 bench)
- [x] `IsDisabled` davranışı **değişmedi**: `run` hâlâ sürer, yanıt hâlâ akar (`DatabaseUnavailableTests` yeşil)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi (metrik tablosu · etiket tablosu · yeni bölüm · `governance.md` `run` paragrafı · `threat-model.md` `caution` bloğu); `npm run build` + `check-links.mjs` temiz
- [x] `docs/MIMARI-TEHDIT-MODELI.md` § R6 güncellendi — `run` yarısı artık ölçülebilir
- [x] Karar kaydı yazıldı: best-effort `run` kaydı **yayımlanmış sözleşmedir**, K-776'nın kardeşidir, ve ihlali sayılır

### Doğrulama komutları

```bash
# Aşama sabitleri kapalı küme mi
grep -n "Disable(" src/Tracon.Core/Recording/RunEventWriter.cs
# → 4 satır, hepsi RunRecordingStages.* ; serbest metin YOK

# Sayaç her iki üretim çağrı yerine ulaştı mı
grep -rn "new RunEventWriter(" src/
# → 2 satır, ikisi de _metrics geçiyor

# Site tablosu ile sabit aynı adı taşıyor mu
grep -rn "tracon.run.recording_failures" src/ docs-site/src/content/docs/
# → TraconDiagnostics.cs + observability.md (metrik tablosu, anlatı bölümü)
#   + governance.md + threat-model.md
# 🚨 Bu senkron ELLE denetlenmez: docs-site/scripts/check-content.mjs
#   TraconDiagnostics.cs'in HER public const string'ini toplar ve elle
#   yazılmış bir sayfada tam ad eşleşmesiyle arar (Plandan Sapmalar §1).

# Gerçek süreç kanıtı (Süreç Ölçümü'ndeki koşum)
PID=$(dotnet-counters ps | grep "Tracon.Api" | awk '{print $1}')   # 🚨 dotnet run'ın PID'i DEĞİL
dotnet-counters collect --process-id "$PID" --counters Tracon \
  --refresh-interval 1 --duration 00:00:00:30 --format csv --output faz173.csv
awk -F',' '$NF != 0' faz173.csv | grep -E "recording_failures|tracon.runs"
```

---

## Plandan Sapmalar

### 1. 🚨 Site kapısı zaten vardı — Açık Soru §2 ölçümle kapandı

Plan *"`check-content.mjs` bugün metrik adı senkronu **yapmıyor**"* diyordu ve
seçenek B'yi (yeni bir kapı yazmak) öneriyordu. **Yanlıştı.**
[`check-content.mjs:492-515`](../../../docs-site/scripts/check-content.mjs) `TraconDiagnostics.cs`
içindeki **her** `public const string` değerini toplar ve elle yazılmış bir
sayfada tam ad eşleşmesiyle arar; bulamazsa kırmızı döner. Kapı ayrıca kendi
körlüğüne karşı da korunmuş (sıfır ad topladıysa reddediyor).

Sonuç: yeni bir kapı **yazılmadı**, çünkü var olan kapı tam olarak sorulan
kaymayı zaten yakalıyor. Kanıt kod okumasıyla değil koşumla alındı — iki yeni
sabit siteye yazılmadan önce kapıyı gerçekten kırmışlardır (`Telemetry name
'tracon.run.recording_failures' appears on no hand-written page`).

### 2. 🚨 DoD'nin "PostgreSQL kapalıyken" senaryosu ULAŞILAMAZ çıktı — teknik değişti

DoD birinci satırı *"PostgreSQL kapalıyken bir `run` koşulur: `run` tamamlanır
ve sayaç `stage=start` ile 1 artar"* diyordu. Gerçek koşum bunu yalanladı:
veritabanı tamamen kapalıyken uç **HTTP 500** döndürür ve `RunEventWriter`
hiç kurulmaz.

Sebep bir kusur **değil**, kasıtlı bir tasarımdır. `AgentEndpoints.RunAsync`
akış başlamadan önce üç okuma yapar — ek dosya sahipliği, deney ataması
(`ExperimentAssignmentResolver` → `SqlExperimentStore.GetRunningAsync`) ve
parametre kapısı — ve kodun kendi yorumu gerekçeyi yazıyor: *"checked BEFORE
the run starts, so the response is a proper ProblemDetails — this is not
possible once the stream has started."* Bu, `DatabaseUnavailableTests`'in
yayımlanmış manifestosuyla da tutarlıdır: **okuma sesli düşer**, yazma sessiz.
Yani `run` henüz yokken korunacak bir `run` da yoktur.

Doğru teknik Faz 171'in kendi tekniğidir: depoyu kapatmak yerine **yazmayı**
engellemek. `tracon.runs` üzerine `RAISE EXCEPTION` yazan bir trigger kondu;
okumalar çalıştı, ön uçuş geçti, `StartRunAsync` düştü. Gerçekleşen koşum
*Süreç Ölçümü*'ndedir. DoD satırı bu tekniğe göre yeniden yazıldı.

### 3. `IsDisabled` bir `bool` alan olmaktan çıktı, `Interlocked` ile korunan bir `int` oldu

Planda yoktu. Sayaç "kaydını kaybeden `run`" sayar; `bool` alan **eşzamanlı**
iki yazımın ikisinin de `Disable`'a girmesine izin verir ve tek bir `run` iki
kez sayılırdı. Alt `run`'ın kök yazıcıya yazdığı bilindiğine göre bu
varsayımsal değildir. `Disable`'ın ilk çağıranı `Interlocked.Exchange` ile
geçişi sahiplenir; **log her çağrıda yazılır** (iki ayrı teşhistir), yalnız
sayaç bir kez artar. `IsDisabled` artık `Volatile.Read` ile okunur.

🚨 Bu değişikliğin ilk testi **yükü taşımıyordu** — aşağıya bakın.

### 4. `RunRecordingStages` yalnız sabit kümesi değil, `Describe` de taşıyor

Plan *"log cümlesi o sabitten üretilir"* diyordu ve bu uygulandı, ama bir adım
daha atıldı: `All` listesi ve `Describe` eşlemesi aynı tipte yaşıyor ve bir
test her `All` üyesinin **kendine ait** bir cümlesi olduğunu iddia ediyor.
Aksi hâlde yeni bir aşama etiketiyle gelir, cümlesiz kalır ve operatör
filtreleyebildiği bir etiketin yanında hiçbir şey anlatmayan bir log satırı
görür.

### 6. Denetim sonrası: iki fonksiyonel test, bir politika kapısı ve bir eşzamanlılık düzeltmesi eklendi

Bağımsız denetim iki 🔴 buldu ve ikisi de kapsamı **büyütmeden** kapandı
(*Denetim Bulguları*): `RunRecordingAgent` ve `WorkflowRunner` çağrı yerlerinin
her biri kendi fonksiyonel testini aldı, `sink` sayımı `Interlocked` ile
korundu, ve kapalı küme `RunRecordingStagePolicyTests` ile kaynak taramasına
bağlandı (Faz 171'in `AuditWritePolicyTests` emsali). Planın *Hata Modları*
tablosunun iki satırı (`WorkflowRecordingFailure` fonksiyonel iddiası ve
paylaşılan `sink` örneği) **planda vardı, ilk yazımda atlanmıştı** — bu sapmanın
kendisi denetimin bulgusudur.

### 5. `MetricProbe` eklendi (`Tracon.PostgreSql.IntegrationTests`)

Planın dosya listesinde yoktu. Entegrasyon test projesinde metrik dinleyici
yoktu; `Tracon.Core.UnitTests`'teki `MetricCollector` `internal`'dır ve
paylaşılamaz. `MetricProbe` meter'ı **referansla** eşler, adla değil — ad
eşlemesi yan yana koşan başka bir testin ölçümünü de toplar ve "bir kez
sayıldı" iddiasını bir yarışa çevirirdi.

## Bu Fazda Verilen Kararlar

| No | Karar |
|---|---|
| **K-782** | Best-effort `run` kaydı **yayımlanmış bir sözleşmedir** ve K-776'nın kardeşidir: `store` yazımı düşerse `run` sürer, kayıt düşer — ama ihlal **sayılır** (`tracon.run.recording_failures`). Garanti değişmedi; görünürlüğü değişti. Etiket kümesi (`tracon.recording.stage`: `start` · `event` · `tool_invocation` · `completion` · `sink` · `input`) **kapalıdır**; küme değişikliği yayımlanmış bir gözlemlenebilirlik sözleşmesini değiştirir ve üç yeri birlikte günceller: `RunRecordingStages`, `guides/observability.md` etiket satırı ve `RunRecordingFailureMetricTests`'in küme iddiası |

Açık soruların dördü de karara bağlandı:

| # | Karar | Gerekçe |
|---|---|---|
| 1 | **B** — `?? "unknown"` | Dışa aktarıcılar boş etiketi eksik etiketten ayırmaz; sayaç bir alarm kaynağıdır. `RecordJobExecution` deseni. Testi: `A_run_without_a_tenant_is_counted_under_a_named_series_not_an_empty_one` |
| 2 | **Hiçbiri** — kapı **zaten vardı** | Ölçüldü; *Plandan Sapmalar* §1 |
| 3 | **A** — `internal` sabit | Public `enum` Faz 7'den sonra donar ve yedinci aşama kırıcı olur. Tüketici bu değeri **okur**, yazmaz |
| 4 | **A** — `stage=input` (aynı sayaç) | İkisi de "bu `run`'ın kanıtı eksildi" olayıdır. Gerçek koşum bunu doğruladı: `runs` yazımı engellenince `run_inputs` de düştü (FK) ve **iki ayrı aşama** olarak sayıldı — tek sayaç ayrımı kaybetmiyor | K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan revize edilmedi; beş sapma *Plandan Sapmalar*'a yazıldı |
| Düzeltme turu sayısı | 9 (analyzer `MA0002` iki kez · `ToolInvocationRecord` alan adları · eksik `using Microsoft.Extensions.AI` · test `RunStartInfo.RunId` yazıcının `run`'ıyla eşleşmiyordu · site içi bağlantı `/guides/replay/` yok · `CS0260` eksik `partial` · politika regex'i metot BILDIRIMINI çağrı sanıyordu · beklenen çağrı yeri sayısı 5 değil 6) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **2 / 0 / 0** — ikisi de gerçekti ve kapandı. Ayrıca 4 🟡 (biri denetçinin gördüğü **bayat ağaçtan** doğdu, üçü gerçek) ve 2 🟢 (biri `ADAYLAR.md`'ye F-238 oldu) |
| Fazın ürettiği regresyon | 0 — mevcut hiçbir test kırılmadı. Kıran tek şey **derleyiciydi** ve kasıtlıydı: 17 test/bench çağrı yeri zorunlu parametreyi karşılamak zorunda kaldı |
| Faz kapandıktan sonra bulunan kusur | — |

### 🚨 Kanıt: bir mutasyon hayatta kaldı ve testi düzeltti

Bu fazın en değerli ölçümü budur ve test yazımının **ilk hâlini yalanladı**.

`Disable`'ın `Interlocked.Exchange` koruması kaldırıldı (her yutulan istisna
sayacı artırsın diye) ve **12 testin hiçbiri düşmedi**. Sebep:
`A_run_that_loses_its_record_is_counted_once_no_matter_how_many_writes_fail`
sıralı üç yazım deniyordu, ama ilk hatadan sonra `IsDisabled` diğer ikisini
zaten engelliyor — yani test "bir kez sayıldı"yı **koruma sayesinde değil,
`IsDisabled` sayesinde** görüyordu. Koruma, testten ayırt edilemeyen ölü koddu.

Düzeltme testi güçlendirmek oldu:
`Concurrent_writes_that_fail_together_still_count_the_run_once` yarışı
**zorlar** — `GatedFailingRunStore` sekiz çağıranı depo içinde tutar, hepsi
`IsDisabled` kontrolünü geçtikten sonra hep birlikte düşer. Koruma kaldırılınca
bu test **düşüyor**. Yarış varsayımsal değildir: alt `run` kök yazıcıya yazar.

| Mutasyon | Sonuç |
|---|---|
| `Disable`'ın `Interlocked` koruması kaldırıldı | ❌ önce hayatta kaldı → test güçlendirildi → ✅ yakalandı |
| `sink` hatası `stage=event` ile etiketlendi | ✅ yakalandı |
| `tenantId ?? "unknown"` → `?? ""` | ✅ yakalandı |
| `SaveInputAsync`'in sayaç çağrısı silindi | ✅ yakalandı |
| `Lifecycle.cs:87` `_metrics` → `null` | ❌ **hayatta kaldı** (denetim 🔴 1) → fonksiyonel test eklendi → ✅ yakalandı |
| `WorkflowRunner.cs:440` `_metrics` → `null` | ❌ **hayatta kaldı** (denetim 🔴 1) → workflow testi eklendi → ✅ yakalandı |
| `sink`'in `Interlocked` koruması kaldırıldı | ❌ **hayatta kaldı** (denetim 🔴 2, koruma henüz yoktu) → koruma + zorlanan yarış → ✅ yakalandı |
| `Disable(ex, "toolinvocation")` (serbest metin) | ✅ yakalandı (`RunRecordingStagePolicyTests`, denetim 🟡 6'dan sonra) |

🚨 **Sekiz mutasyonun DÖRDÜ ilk hâlinde hayatta kaldı**, ve üçünü bağımsız denetim
buldu. Ortak sebep tek bir cümleyle yazılabilir: *derleyicinin zorladığı şey
imzadır, değer değildir; ve bir korumanın varlığı onun ölçüldüğü anlamına gelmez.*
Faz kendi dersini (`Interlocked` + zorlanan yarış) `store` yoluna uyguladı,
`sink` yoluna uygulamadı.

### Kanıt: gerçek süreç, gerçek veritabanı, gerçek `run`

`samples/Tracon.Api` + PostgreSQL 18 (`pgvector/pgvector:pg18`) + `echo`
sağlayıcısı (ağ çağrısı yok). `tracon.runs` üzerine `RAISE EXCEPTION` yazan
bir trigger konarak **yalnız yazma** engellendi (gerekçe: *Plandan Sapmalar* §2).

| Ne yapıldı | Gözlenen |
|---|---|
| Trigger yokken bir `run` | `HTTP 200`; `tracon.runs[status=Completed]` → **1**; `recording_failures` **hiç artmadı** |
| Trigger varken bir `run` | `HTTP 200`, akış `event: done` ile normal bitti — **istemci yanıtın tamamını aldı** |
| Aynı koşumun sayaçları | `tracon.run.recording_failures[tracon.recording.stage=start;tracon.tenant.id=default]` → **1** · `[...stage=input;...]` → **1** · `tracon.runs[...status=Completed]` → **1** |
| Aynı koşumun log'u | `Tracon run recording was disabled (the run record could not be opened). Run 01a0a5c6-… continues normally.` |
| Trigger düşürüldükten sonra `SELECT count(*) FROM tracon.runs` | **2** — yalnız sağlıklı koşumlar; engelli koşumların satırı yok |

🚨 İki ayrı aşamanın aynı koşumda sayılması **planlanmamıştı ve Açık Soru
§4'ü doğruladı**: `runs` satırı hiç yazılmadığı için `run_inputs` yabancı
anahtarı da düştü. Tek sayaç iki kaybı iki ayrı seri olarak gösterdi — ayrı
bir sayaç bu bilgiyi eklemez, yalnız ikinci bir alarm kuralı isterdi.

🚨 **Ölçüm tuzağı:** `dotnet-counters` `dotnet run`'ın PID'ine bağlanırsa
oturum açılır, "Running" der ve **boş bir CSV** bırakır. Gerçek uygulama
ayrı bir alt süreçtir; PID `dotnet-counters ps` çıktısındaki `Tracon.Api`
satırından alınır. Bu, manuel case'lerin ön koşuluna yazıldı.

## Denetim Bulguları

> Bağımsız denetçi (`faz-denetim`, taze bağlam) `f10e87d8` → çalışma ağacını yargıladı.
> **2 🔴 · 4 🟡 · 2 🟢.** Hepsi kapandı.
>
> İki 🔴 aynı sınıfın iki yüzüydü ve ikisi de fazın **kendi yazdığı dersi** fazın
> kendi koduna uygulamamaktan doğdu.

### 🔴 1 — Sayacın üretim çağrı yerlerine ULAŞTIĞINI hiçbir test kanıtlamıyordu

**Bulgu.** 12 birim testinin hepsi `RunEventWriter`'ı **elle** kuruyordu; yani
yazıcının aritmetiğini kanıtlıyor, üretimi hiç kanıtlamıyorlardı. Derleyici iki
üretim çağrı yerini bir argüman geçmeye **zorlar**, *doğru* argümanı geçmeye
zorlamaz.

🚨 **Mutasyonla doğrulandı.** `RunRecordingAgent.Lifecycle.cs:87`'de
`_metrics` → `null` yapıldı: **12 testin hepsi yeşil kaldı.** Aynı mutasyon
`WorkflowRunner.cs:440` için de sessizdi. Yani faz, agent üzerinden koşan her
`run` için üretimde **ölü** olabilirdi ve hiçbir kapı bunu söylemezdi.

Bu tam olarak reponun kendi yara kaydıdır (Faz 20, K-157: `CompleteAsync`'e
`cost` parametresi eklendi, gövdeye `Cost = cost` yazılmadı, **1068 test**
yakalamadı) ve `AGENTS.md` bunu "imza değiştirmek ile gövdeyi kullanmak İKİ
AYRI ADIMDIR" diye yazıyor. Plan bu riski görmüştü ve *Hata Modları* tablosunda
bir `WorkflowRecordingFailure` fonksiyonel iddiası söz vermişti; o iddia
**yazılmamıştı** ve sapma da kaydedilmemişti.

| Ne | Nerede |
|---|---|
| Agent yolu | `RunRecordingFailureMetricTests.A_run_through_RunRecordingAgent_counts_its_lost_record` — gerçek `RunRecordingAgent`, kaydı reddeden `store`, `run` yine yanıt veriyor |
| Workflow yolu | `WorkflowRecordingFailureTests.A_workflow_whose_run_record_cannot_be_written_still_runs_and_counts_the_loss` (yeni dosya) + `WorkflowTestHost.CreateRunner(IRunStore, TraconMetrics, …)` aşırı yüklemesi |
| Kanıt | İki mutasyon (`_metrics` → `null`, iki çağrı yerinde ayrı ayrı) artık **ilgili testi düşürüyor** |

### 🔴 2 — `sink` aşaması eşzamanlı yazımda ÇOK KEZ sayıyordu

**Bulgu.** Aynı fazda `store` yoluna `Interlocked` koruması kondu (çünkü iki
eşzamanlı yazım tek `run`'ı iki kez sayardı) ama `sink` yolu **korumasız**
bırakıldı. `_sinkDisabled` düz bir `bool[]`'du: sekiz eşzamanlı olay kontrolü
birlikte geçer, sekizi de düşer, sayaç **8** artardı.

🚨 Bu yalnız bir tutarsızlık değildi — **sevk edilen bir cümleyi yalanlıyordu.**
Aynı fazda `observability.md`'ye *"Counted once per sink per run"* yazılmıştı ve
kodun kendi yorumu da `_sinkDisabled`'ın bunu sağladığını iddia ediyordu. İkisi
de yanlıştı. Ayrıca `bool[]` yazımı bariyersizdir: ikinci bir iş parçacığı
bozuk `sink`'i çağırmaya devam edebilirdi.

Yarış varsayımsal değildir: `AllowConcurrentToolCalls` tool olaylarını aynı anda
yayar ve alt `run` kök yazıcıya yazar — `Disable` korumasını gerekçelendiren
cümlenin **aynısı**.

| Ne | Nerede |
|---|---|
| Düzeltme | `RunEventWriter.cs` — `_sinkDisabled` `bool[]` → `int[]`; okuma `Volatile.Read`, geçişi `Interlocked.Exchange` sahipleniyor; yalnız sahiplenen çağıran sayıyor **ve logluyor** |
| Zorlanan yarış | `RunRecordingFailureMetricTests.Concurrent_events_into_one_failing_sink_count_that_sink_once` — `GatedThrowingRunEventSink` sekiz çağıranı tutar, hepsi birlikte düşer |
| Kanıt | Koruma kaldırılınca test **düşüyor** |

### 🟡 Kapananlar

| # | Bulgu | Kapanış |
|---|---|---|
| 3 | "Bir kez sayılır" testlerinin **yorumları yanlıştı**: `FailAt.Everything` yalnız `StartRunAsync`'i düşürüyordu, ama iki ayrı yorum (birim + entegrasyon) "üç yazım da reddedildi" diyordu | Enum `FailAt.StartRun` olarak yeniden adlandırıldı ve tek bir yazımı reddettiği XML'e yazıldı. 🚨 Daha önemlisi: mekanizma artık **anlatılmıyor, iddia ediliyor** — `FailingRunStore.StoreCallCount` eklendi ve test `ShouldBe(1)` ile "devre dışı yazıcı depoyu bir daha ÇAĞIRMAZ" diyor. Entegrasyon testinin yorumu da düzeltildi |
| 4 | Planın *Hata Modları* tablosundaki 11. satırın ("eşzamanlı `run`'lar tek `sink` örneğini paylaşır") karşılığı yoktu | `Two_runs_sharing_one_sink_instance_each_count_their_own_failure` — tek `sink` nesnesi, iki yazıcı, iki ayrı kiracı; her `run` kendi kaybını sayar |
| 5 | DoD bloğu işaretsizdi ve 1. satır fazın kendi koşumunun ulaşılamaz ilan ettiği tekniği yazıyordu | Denetçi **bayat bir ağaç** gördü; ikisi de denetim koşarken düzeltilmişti. Şimdi on bir satır `- [x]` ve 1. satır trigger tekniğini yazıyor (*Plandan Sapmalar* §2'ye bağlı) |
| 6 | "Kapalı küme" DoD'sini hiçbir kapı zorlamıyordu — yalnız sabit listesinin kendisiyle karşılaştırıldığı bir test ve elle grep vardı | `RunRecordingStagePolicyTests` (yeni) — Faz 171'in `AuditWritePolicyTests` emsali. `src/` taranır; her `Disable(` / `RecordRunRecordingFailure(` ikinci argümanı ya `RunRecordingStages.*` ya da `Disable`'ın kendi ileri taşıdığı `stage` parametresidir. İkinci test çağrı yeri **sayısını** rakamla kilitler (`RunEventWriter.cs` → 6, `Persistence.cs` → 1). Sıfır eşleşmeye karşı kendi körlük koruması var. Kanıt: `Disable(ex, "toolinvocation")` mutasyonu ilk testi, serbest metin mutasyonu ikincisini düşürüyor |

### 🟢 Aday / kapanış adımı

| # | Bulgu | Karar |
|---|---|---|
| 7 | `MetricProbe` (PostgreSQL) · `MetricCollector` (Core) · `WorkflowMetricProbe` (Workflows) neredeyse aynı — bu fazda **üçüncü** kopya oldu | Denetçi ikisini gördü, faz üçüncüsünü ekledi; kopya sayısı artık K-483'ün eşiğindedir. Paylaşılan test yardımcısı `Tracon.Testing` yüzeyine ait ayrı bir karardır. **`ADAYLAR.md`'ye F-238 olarak yazıldı** |
| 8 | `YOL-HARITASI.md` fazı `📋 Planlandı` gösteriyor, doküman kökte | `faz-tamamlama`'nın arşiv adımı; denetim ondan **önce** koşar. Kapanışta `faz-arsivle` + yol haritası üretimi kapattı |

### Denetçinin temiz bulduğu başlıklar

Plan dışı public API **yok** (gerçekleşen yüzey taslakla birebir; `RunRecordingStages`
`internal` kaldı) · sevk edilen `///` metninde faz numarası · `K-*` · `F-*` · 🚨 ·
`MEASURED` **yok** · her yeni public üye XML dokümanına sahip · `secret` yazımı yok ·
MAF tipi sarmalanmamış · `ValueTask` yollarında `ConfigureAwait(false)` korunmuş ·
**hiçbir muafiyet listesi veya taban çizgisi büyümedi** (`check-content.mjs`,
`DIAGRAM_EXEMPT`, `CLOSING_EXEMPT`, `SourceLanguageTests`, ağırlık tavanı diff'te yok) ·
16 mekanik test/bench düzenlemesi **saf parametre ekidir**, hiçbir `assert` değişmedi.

## Sonraki Faza Devir Notu

- 🚨 **Bir es zamanlilik korumasi eklerken, o korumanin KALDIRILMASIYLA dusen
  bir test yaz.** Bu fazda `Interlocked` koruması eklendi ve **12 testin hiçbiri**
  onu kaldırmakla düşmedi — sıralı test "bir kez sayıldı"yı korumadan değil
  `IsDisabled`'dan görüyordu, yani koruma testten ayırt edilemeyen ölü koddu.
  Yarışı **zorlayan** bir fake (`GatedFailingRunStore`: N çağıranı depo içinde
  tutar, hepsi birlikte düşer) tek kanıttır.
- 🚨 **Kayıt yolunun best-effort davranışını gerçek süreçte ölçmek isteyen faz
  veritabanını KAPATAMAZ.** `AgentEndpoints.RunAsync` akış başlamadan önce üç
  okuma yapar (ek dosya · deney ataması · parametre kapısı) ve üçü de **kasıtlı**
  olarak sesli düşer; uç `HTTP 500` döner ve `RunEventWriter` kurulmaz bile.
  Teknik Faz 171'inkidir: hedef tabloya `RAISE EXCEPTION` yazan bir trigger —
  okumalar çalışır, yalnız yazma düşer.
- 🚨 **`dotnet-counters` `dotnet run`'ın PID'ine bağlanırsa sessizce boş döner.**
  Oturum açılır, "Running" yazar, **başlığı dışında boş bir CSV** bırakır.
  Gerçek uygulama ayrı bir alt süreçtir; PID `dotnet-counters ps` çıktısındaki
  `Tracon.Api` satırındandır. `collect`'i `--duration` ile bitir; `pkill` ile
  durdurulan oturum dosyayı hiç yazmaz.
- **`docs-site`'ın telemetri kapısı ZATEN VAR ve `TraconDiagnostics.cs`'i okur**
  ([`check-content.mjs:492`](../../../docs-site/scripts/check-content.mjs)). Oraya yeni
  bir `public const string` eklemek, o adı elle yazılmış bir sayfaya yazmayı
  **zorunlu** kılar. Yeni bir metrik veya etiket eklerken kapı yazmaya kalkma —
  koş, zaten kırmızı dönecektir.
- **`RunEventWriter` yapıcısına parametre eklemek 19 çağrı yerini ziyaret
  ettirir** (2 üretim, 17 test/bench, 8 dosya). Zorunlu-nullable desen bunu
  kasten yapar. Konumsal `sinks` argümanı geçen çağrılar (`RunEventSinkTests`,
  `SlowSinkTests`) derlemede **anlaşılmaz** bir hata verir
  (`CS9174: Cannot initialize type 'TraconMetrics' with a collection
  expression`) — sebebi budur, adlandırılmış argüman (`metrics: null`) ile
  çözülür.
- **`tracon.recording.stage` kümesine aşama eklemek ÜÇ yeri birlikte değiştirir**
  (K-782): `RunRecordingStages` (sabit **ve** `Describe` cümlesi),
  `guides/observability.md`'nin etiket satırı ve aşama listesi, ve
  `RunRecordingFailureMetricTests`'in kümeyi rakamla kilitleyen iki testi.
  Yalnız sabiti eklemek `Every_stage_value_has_a_log_sentence_of_its_own`'u
  kırar — istenen budur.
- **`stage=start` ve `stage=input` aynı koşumda birlikte artar** ve bu doğrudur:
  `runs` satırı yazılamayınca `run_inputs` yabancı anahtarı da düşer. Bunu
  "çift sayım" sanıp birini bastırma — iki ayrı kayıp, iki ayrı seri.
- 🚨 **Zorunlu parametre imzayı zorlar, DEĞERI zorlamaz.** Bu fazda
  `_metrics` → `null` mutasyonu iki üretim çağrı yerinde de **12 testi yeşil
  bıraktı** (denetim 🔴 1). `RunEventWriter`'ı elle kuran bir test üretim
  hakkında hiçbir şey söylemez. Bu tipin yapıcısına bağımlılık ekleyen her faz,
  o bağımlılığın `RunRecordingAgent` **ve** `WorkflowRunner` üzerinden
  geçtiğini ayrı ayrı iddia eden bir test bırakmalıdır.
- 🚨 **Bir eşzamanlılık korumasını bir yola koyup kardeş yoluna koymamak, sevk
  edilen bir cümleyi yalanlar.** `store` yolu `Interlocked` aldı, `sink` yolu
  almadı, ve aynı fazda siteye *"Counted once per sink per run"* yazıldı —
  yanlıştı (denetim 🔴 2). Aynı sayaca yazan her yolu birlikte düşün.
- **F-237 açık duruyor** ([`ADAYLAR.md`](../../ADAYLAR.md)): etkiden önceki dar
  fail-closed `seam`. Bu fazın sayacı o adayın **önkoşuludur** — kaybın gerçek
  sıklığı ölçülmeden inşa edilen bir `seam` yanlış şekli alır.
