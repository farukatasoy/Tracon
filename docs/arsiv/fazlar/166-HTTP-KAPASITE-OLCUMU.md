# Faz 166 — HTTP Kapasite Ölçümü

> **Durum:** ✅ Tamamlandı (2026-09-14) · plan 2026-09-13, revize 2026-09-14
> **Kaynak:** Kullanıcının ürün değerlendirmesindeki 2. problem için faz planı isteği · **F-225** ([aday kaydı](../../ADAYLAR.md))
> **Revizyon (2026-09-14, `nuget-danismani` turu):** Dış inceleyici yedi kapasite
> metriği istedi; ölçüm planın **beşini** kapsadığını gösterdi. Üç ekleme yapıldı
> ve ikisi kullanıcı kararıdır: **write amplification** ve **storage growth** artık
> açık çıktıdır (§166.5) · `queued` senaryosuna **dar kapsamlı worker-sayısı
> boyutu** eklendi (§166.3, yeni `workers` profili) · ölçülen sayılar **tek ortamlı
> temsilî tablo** olarak siteye yayımlanır (§166.6). 🚨 Worker boyutu **çok node
> desteği vaat etmez** — K-739 aynen korunur; ölçmek destek ilan etmek değildir.
> **Önkoşul:** [Faz 157](157-SINIRLI-YUK-VE-IKI-PROCESS-ARIZA-KANITI.md) — store yük raporu ve process altyapısı · [Faz 97](97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md) — exact paket tüketimi
> **Paketler:** Ölçülen: `Tracon.AspNetCore`, `Tracon.Core`, `Tracon.PostgreSql`; sevk edilen kodda değişiklik planlanmıyor.
> **Yeni paket:** 0 Tracon paketi, 0 yeni harici paket kimliği · **Migration:** 0
> **Public API:** Büyümüyor; ölçüm bileşenleri paketlenmez.
> **Tüketici yüzeyi:** Site: `docs-site/src/content/docs/guides/production.md` — kapasite kanıtının kapsamı ve sınırları **ve tek ortamlı temsilî sayı tablosu** (revizyon 2026-09-14).
> · Sevk edilen: XML, paket README, capability satırı ve HTTP contract değişmiyor; ölçüm aracı repo geliştirme aparatıdır.
> **Manuel test alanı:** `docs/manuel-test/36-GELISTIRME-KAPILARI.md`

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9f0266d8:docs/arsiv/fazlar/166-HTTP-KAPASITE-OLCUMU.md
> ```
>
> Damıtıldı 2026-09-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Belirli donanım, configuration ve veri hacminde gerçek paket tüketicisinin HTTP/SQL yolunu ölçmek. Yük arttığında latency, kaynak tüketimi, backlog ve doğruluk birlikte görünür. Rapor, ölçülen yük aralığını anlatır; genel kapasite veya SLA garantisi vermez. F-225, Faz 157'nin kapanmış F-217 işinin üstüne eklenir.

## Bitiş Ölçütleri (DoD)

Ölçüm koşumları: `sweep` (72 hücre) · `arrival` (12) · `workers` (9) · `soak` (1)
— **94 hücrenin 94'ü `complete`, 0 `incomplete`, 0 `invalid`**. Dördü de
`0.0.0-capacity166.e44d89f` sürümünü, temiz ağaçtan paketlenmiş `e44d89f5`
commit'ini ölçtü. Kayıtlar: [`bench/capacity/measurements/`](../../../bench/capacity/measurements).

- [x] `kapasite` komutu exact sürüm/izole cache ile repo dışında host/driver kurar; gerçek TCP ve SQL yolu kanıtlıdır. — `CapacityPackageIsolationTests` (5 test) `project.assets.json`'ı okur: her Tracon kütüphanesi `package` tipinde ve tam o sürümde, restore izole `NUGET_PACKAGES`'i kullanmış, hiçbiri `project` değil.
- [x] `smoke` Linux CI'da açık adım olarak koşar; ağır profiller standart test/kapanış/release'e eklenmemiştir. — `.github/workflows/ci.yml` "Kapasite smoke"; `KapasiteKomutuTestleri` `closing_commands`/`inner_loop_commands` çıktısında "kapasite" geçmediğini kanıtlar (K-774).
- [x] Üç senaryo ve iki tenant doğrulanmıştır; buffered idempotency maliyeti, iki SSE contract'ı ve canlı subscriber ayrımı raporda doğrudur. — Her hücrede `crossTenantRefusals = 2`; soak'ta 4176 `queued` isteğin **4176'sı** canlı bağlandı, `historicalOnly = 0`.
- [x] Sweep boş/dolu veriyle ve üç tekrarla koşulmuştur; resource cap nedeniyle durulan hücreler gerekçeli devredilir, tamamlanmış gibi gösterilmez. — 72/72 tamam; hiçbir cap'e çarpılmadı, dolayısıyla devredilen hücre yok.
- [x] Open-loop arrival raporu planned/sent/notSent, dispatch gecikmesi ve backlog'u birlikte taşır. — 16/s basamağında 960 planlanan isteğin 403'ü `notSent`; iki sayaç özdeşliği de her hücrede kapanıyor.
- [x] `workers` profili 1/2/4 worker process ile koşmuştur; host'un dahilî worker'ı kapalıdır ve bu **çalışma anında** doğrulanmıştır. Throughput serisi, claim çarpışma oranı ve job'ın worker'lara **dağılımı** birlikte raporlanır; tek worker tüm işi alıyorsa ölçeklenme iddia edilmez. — `hostRunWorker: false` dokuz hücrede de `/capacity/settings`'ten okundu. 4 worker: %34/%33/%19/%15. Throughput 1→4 worker'da **değişmedi** (5.90→5.97/s) ve rapor ölçeklenme iddia **etmiyor**.
- [x] Aynı job'ın iki worker'da **eşzamanlı** çalışmadığı attempt aralıklarından kanıtlanmıştır; fazladan attempt at-least-once gereği kabul edilir (K-641), örtüşme koşumu `invalid` yapar. — Dokuz hücrede `concurrentOverlaps = 0`, `attemptRatio = 1.0`.
- [x] 🚨 Hiçbir yerde "çok node destekleniyor" cümlesi kurulmamıştır; rapor ve site metni ölçümün tek makine/tek DB/tek saat sınırını açıkça yazar (**K-739 korunur**). — `production.md`: "**Important:** this says nothing about running Tracon on multiple machines."; her `report.md` aynı cümleyi taşıyor.
- [x] Write amplification tablo kırılımıyla, index–gövde ayrımıyla ve **satır ile bayt birlikte** raporlanmıştır; autovacuum karışan hücreler işaretlenmiş ve ortalamaya katılmamıştır. — 72 hücrenin 12'si temiz; kalanlar `storage/vacuum-interference` işaretli ve **yalnız bayt sütunu** dışlanıyor, satır sayıları duruyor.
- [x] Run başına ve event başına storage growth üç tekrarın değişkenliğiyle verilmiştir; projeksiyon varsa "ölçülen aralığın doğrusal uzantısı" olarak etiketlenmiştir; retention durumu manifest'tedir. — `streaming` 27,6 satır/run · ~15,0 KB/run; 1M projeksiyonu sayfada "a projection, not a measurement" diye etiketli; `retentionEnabled: false` manifest'te.
- [x] `production.md`'ye tek ortamlı temsilî sayı tablosu yazılmıştır: ortam, Tracon sürümü ve commit yazılı; "SLA veya garanti kapasite değildir" çerçevesi var; yarım hücre, tek tekrar ve uyarısız düşük örnekli percentile yayımlanmamıştır. — Yayımlanan her satır **üç tekrarın birleşimi** ve n ≥ 140; hiçbiri düşük örnek tabanının altında değil.
- [x] Seçim gerekçesi yazılmış yükte 30 dakikalık soak tamamlanmıştır; sağlıklı workload'un son mutabakatında kayıp/tenant karışması yoktur. — Gerekçe: c8, `queued`'in kuyruk biriktirmeden bitirdiği en yüksek sweep basamağı (c8 p50 1316 ms, c32 4456 ms). 1801 s ölçüm, 12 515/12 515, `missingRuns = 0`, `tenantBleed = 0`.
- [x] Süre/hız CI eşiği yoktur; K-634/K-738 ve allocation baseline korunmuştur. — `bench/baseline.json` değişmedi; tahsis kapısı kapanışta ✅.
- [x] CPU/RSS/heap/GC, SQL telemetry kapsamı, queue ve SSE ölçümleri ortamıyla raporlanmıştır; eksikler ve örnek sayıları açıktır. — Soak'ta `telemetry.unavailable` **boş**; yedi ölçümün yedisi de mevcut.
- [x] JSON/Markdown/grafikler aynı ham veriden tekrar üretilebilir; ilk darboğaz veya belirlenememe nedeni kanıtla yazılmıştır. — Beş koşumun raporu kapanışta **son sürücüyle yeniden üretildi**; darboğaz satırı `queued`'in c8→c32 platosunu sayılarla adlandırıyor.
- [x] Hata modu tablosundaki kısa testler koşmuştur; kayıp kayıt, kesik SSE, yanlış provenance ve iptal negatif kanıtları vardır. — 89 birim + 22 script + 27 packed-host kabul testi.
- [x] Dört doğrulama kapısı sıfır uyarı verir: `python3 scripts/kapi.py kapanis --taban <uygulama öncesi commit>`.
- [x] `samples/Tracon.Api` ile gerçek run yapılmış, komut ve güvenli çıktı kapanışa yazılmıştır; bu smoke sentetik kapasite verisine karıştırılmaz. — Aşağıda, **Örnek Uygulama Koşumu**.
- [x] Secret taraması boş döner; raporlar, process çıktıları ve manifest ayrıca taranmıştır. — `kapi.py tarama` ✅ (6 işaretli sentetik değer atlandı); `CapacityArtifactTests` ekilmiş canary ile her artifact'ı tarıyor ve canary'nin yakalanabilir olduğunu ayrıca kanıtlıyor.
- [x] Manuel case'ler `docs/manuel-test/36-GELISTIRME-KAPILARI.md` içine eklenmiş; otomatikleştirilebilenler koşmuştur. — `MT-GDK-039`…`048`.
- [x] `faz-denetim` uygulanmış; 🔴 bulgu kalmamıştır. `faz-tamamlama` ile site/doküman senkronu ve arşiv kapanışı yapılmıştır. — **Denetim Bulguları** bölümü.
- [x] `production.md` yalnız **ölçülen** kapsamı ve **ölçülen** sayıları anlatır — ölçülmemiş hiçbir rakam yoktur; ilgili site kapıları temizdir. — Sayfadaki her rakamın kaynağı `bench/capacity/measurements/`; `npm run check` ✅ (bağlantı, SEO, ağırlık).

## Örnek Uygulama Koşumu

Aparatın kendi fixture'ı değil, sevk edilen örnek uygulama (2026-09-14):

```bash
dotnet run --project samples/Tracon.Api -c Release --urls http://127.0.0.1:5199
curl -s -X POST http://127.0.0.1:5199/tracon/api/agents/support/run \
     -H 'Content-Type: application/json' \
     -d '{"message":"What is the status of order 1001?"}'
curl -s http://127.0.0.1:5199/tracon/api/models/health
```

Akışlı yanıt geldi: `event: run` çerçevesi `runId` taşıdı
(`01a0a14f-a429-73cf-854d-db98318c34f6`), ardından `EchoModelProvider`'ın delta
zinciri `update` çerçeveleri olarak aktı. `models/health` `echo` sağlayıcısını
`Unknown` durumuyla döndürdü — ağ çağrısı yapmayan bir sağlayıcı için doğru
cevap. Uygulama `Application is shutting down...` ile temiz kapandı.

🚨 Bu koşum kapasite verisine **karışmaz**: farklı sağlayıcı (`echo`), farklı
agent (`support`), farklı depo (bellek içi) ve tek istek. Buradaki tek iddia
sevk edilen örneğin çalıştığıdır.

### Planlanan doğrulama komutları

Aşağıdaki `kapasite` komutları **henüz yoktur**, bu fazda eklenecektir.
`--surum` exact sürüm ister; aşağıdaki sürüm yalnız yerel ölçüm örneğidir.

```bash
python3 scripts/kapi.py kapasite --profil smoke --surum 1.0.0-preview.capacity166
python3 scripts/kapi.py kapasite --profil sweep --surum 1.0.0-preview.capacity166
python3 scripts/kapi.py kapasite --profil arrival --surum 1.0.0-preview.capacity166
python3 scripts/kapi.py kapasite --profil workers --surum 1.0.0-preview.capacity166
python3 scripts/kapi.py kapasite --profil soak --surum 1.0.0-preview.capacity166
```

`workers` profili `sweep` sonrasında koşulur: sabit concurrency'sini onun
tamamlanan basamaklarından seçer, bu yüzden tek başına anlamlı değildir.

Soak seçimi `profiles/soak.json` içinde açık concurrency olarak kaydedilir;
öneri algoritması gizlice workload değiştirmez. Connection yalnız environment
veya mevcut secret mekanizmasından okunur; komut argümanına konmaz.

## Plandan Sapmalar

Altı sapma. Hiçbiri kapsamı daraltmadı; dördü planın yapısal bir iddiasını
ölçtükten sonra doğdu (`faz-uygulama` Adım 1), ikisi repo'nun kendi kapılarıyla
çarpışmaktan.

### 1 · Hücre yalıtımı **schema** değil **veritabanı** sınırında

Plan (§166.4) her hücre/tekrar için izole bir *schema* istiyordu. Ölçüldü:
`full` fixture'ı **public store yolundan** yazmak 10.000 run × (1 `StartRun` +
20 `AppendEvent` + 1 `CompleteRun`) = 220.000 store çağrısıdır. Sweep'in 36
dolu hücresinin her birinde tekrarlamak seed'i saatlerce kritik yola koyardı ve
plan seed süresinin ölçüm penceresine girmemesini zaten şart koşuyordu.

Bunun yerine **şekil başına bir şablon veritabanı** kurulur (migrate + seed, bir
kez) ve her hücre `CREATE DATABASE … TEMPLATE …` ile kendi veritabanını alır —
PostgreSQL için bu bir dosya kopyasıdır. Şema adı her veritabanının içinde
`capacity` olarak sabittir. **Yalıtım zayıflamaz, güçlenir**: sınır artık schema
değil veritabanıdır, yani bir hücrenin `pg_stat` sayaçları bile komşusundan
etkilenmez. Seed süresi hiçbir pencereye girmez.

Bedeli: koşum artık veritabanı yaratma/düşürme yetkisi ister, bu yüzden harici
bir sunucuya (`TRACON_CAPACITY_CONNECTION`) bağlıyken `psql` yönetimi gereken
profiller açıkça reddedilir.

### 2 · Host'un dördüncü modu: `migrate`

Plan üç mod sayıyordu (`api` · `worker` · seed). Uygulamada dördüncüsü zorunlu
çıktı: şemayı **ayrı bir çağrı** uygular. Ölçülen gerekçe planın kendi iki
cümlesinden gelir — şema kurulumu ölçülen host'un açılışında olursa ilk hücrenin
penceresine girer, ve `workers` profilinde N worker process'i aynı migration
kilidi için yarışır. `AutoApplyMigrations` bu yüzden her ölçülen process'te
kapalıdır.

### 3 · Secret kapısı **işaretli istisna** mekanizması kazandı

Bir redaction taramasını doğrulamanın tek yolu ona credential şeklinde bir şey
göstermektir; plan da (§166.5, hata modu tablosu) "sentetik canary credential"
istiyordu. Repo'nun kendi kapısı (`kapi.py tarama`) bu sentetik değerleri
yakalayıp kapanışı kırdı — doğru davranış.

Çözüm **gizlemek değil işaretlemek** oldu: `SYNTHETIC-CREDENTIAL` belirtecini
**aynı satırda** taşıyan satır atlanır ve kaç satırın atlandığı her koşumda
raporlanır. Gerekçe: dize birleştirmeyle saklamak (`"Pass" + "word=..."`) gerçek
bir secret'ın yapacağı şeyin aynısıdır ve istisnayı kod incelemesinden gizler;
sessizce büyüyen bir allowlist ise kapının hiç olmamasıyla aynıdır. Üç test bunu
kilitler (`SentetikCredentialTestleri`): işaretsiz satır yakalanır, işaretli
satır atlanır **ve sayılır**, bir üst satırdaki yorum yetmez.

### 4 · `bench/capacity/Directory.Packages.props` — işi yalnız aramayı durdurmak

Plan dosya listesinde yoktu. Ölçüldü: yalnız `Directory.Build.props` yazmak
yetmiyor — NuGet yukarı yürüyüp repo'nun `Directory.Packages.props`'unu buluyor,
merkezî paket yönetimini geri açıyor ve aparatın csproj'larındaki her `Version`
`NU1008` hatası oluyor. Geçici dizine taşınan kopyada ise o dosya hiç yok, yani
iki ortam farklı davranırdı. Tek işi aramayı durdurmak olan bir dosya eklendi.

### 5 · Kabul paketinin analyzer gevşetmeleri `.editorconfig`'te değil `NoWarn`'da

Plan bunu konuşmuyordu; uygulamada repo'nun `[tests/**]` gevşetmelerinin
(CA1707 · xUnit1051 · CA2007) geçici dizindeki kopyaya **ulaşmadığı** görüldü.
🚨 Ölçüldü ve üç yerleşimde de tekrarlandı — `.editorconfig`
`bench/capacity/`'de, kabul projesinin kendi dizininde, `[*.cs]` ve `[**.cs]`
bölüm desenleriyle, `root = true` ile ve onsuz: **hiçbiri tanıyı susturmadı**;
csproj'daki `NoWarn` sustudu. Aynı `.editorconfig` çıplak bir test projesinde
(aynı SDK, aynı `AnalysisMode`) çalışıyor, yani mekanizma bu ağaca özgü bir
şeyle etkileşiyor ve **saptanamadı**. Tuzak kaydı:
[`docs/hafiza/analyzer-tanilari.md`](../../hafiza/analyzer-tanilari.md).

### 6 · Kiracı yalıtımı her hücrede **aktif olarak** yoklanıyor

Plan (§166.5) karşı kiracının kimliğini okuma girişiminin HTTP'de reddedilmesini
istiyordu ama bunu kabul testlerine bırakmış görünüyordu. Uygulamada sürükleyici
her hücrenin drain'inden sonra kendisi de yokluyor: bir kiracının run kimliği
diğerinin başlığıyla istenir ve **reddedilmezse hücre `invalid` olur**. Gerekçe
planın kendi cümlesidir — sessiz bir yükte yalıtım, sakin bir testteki
yalıtımdan başka bir iddiadır.

## Bu Fazda Verilen Kararlar

Karar defterine **iki** kalem girer; geri kalanı yerel implementation tercihidir
ve bu dokümanda kalır (AGENTS.md, doküman bütçesi).

| # | Karar | Neden defterlik |
|---|---|---|
| K-774 | **Kapasite ölçümü bir kapı DEĞİLDİR ve hiçbir profili standart kapanışa, PR yoluna veya release hattına girmez; CI'da yalnız `smoke` koşar ve hızı değil DOĞRULUĞU kontrol eder** | K-738'in (yük ölçümü rapordur) bu faza uygulanması, ama yeni bir yüzeyle: `kapi.py`'nin komut listesine kapı OLMAYAN bir alt komut girdi. Bunu yazmazsak bir sonraki oturum "kapı listesinde duruyor, kapanışa ekleyelim" der ve her faz saatlerce uzar |
| K-775 | **Yayımlanan kapasite sayısı sürüm + commit taşır ve ölçüm yenilenmeden sürüm satırı güncellenmez** 👤 | Sevk edilen bir iddiadır. `production.md`'deki tablo bir sonraki sürümde sessizce devralınırsa ölçülmemiş bir sayı yayınlanmış olur — F-171'in `bagimlilik_surum_damgasi` kapısının davranış iddiaları için kurduğu disiplinin aynısı |

**Defterlik olmayan, burada kalan tercihler:** hücre yalıtımının veritabanı
sınırında olması (Sapma 1) · `migrate` modunun ayrılığı (Sapma 2) ·
`SYNTHETIC-CREDENTIAL` işaretinin biçimi (Sapma 3) · kabul paketinin
`NoWarn` gevşetmeleri (Sapma 5) · profil dosyalarının şekli.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan uygulama sırasında revize edilmedi; altı yapısal sapma ölçülüp **Plandan Sapmalar**'a yazıldı |
| Düzeltme turu sayısı | 5 — (1) analyzer tanıları (`CA1822`/`CA1051`/`CA1707`), (2) PostgreSQL hazırlık yarışı + `numeric` cast, (3) `model.turns` kapsamı, (4) worker modunun Kestrel'i, (5) denetimin beş 🔴'sı |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **5 / 0 / 0** — beşi de aritmetik uyuşmazlıktı (yayımlanan sayı ≠ kaydedilen sayı); triyaj kullanıcı yerine **kanıtla** yapıldı, gerekçesi Denetim Bulguları'nda |
| Fazın ürettiği regresyon | 0 — `src/` diff'i **boş**; 17 `PublicAPI.*.txt` dokunulmadı; tahsis taban çizgisi değişmedi |
| Ölçüm koşumu | 94 hücre (72 sweep · 12 arrival · 9 workers · 1 soak), **94'ü `complete`**; 2,5 saat duvar saati; iki aparat kusuru yalnız koşum sırasında çıktı |
| Faz kapandıktan sonra bulunan kusur | — (kapanış anında boş; sonraki oturum doldurur) |

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi (taze bağlam, yalnız DoD + diff, salt-okunur)
**beş 🔴**, **altı 🟡** ve **iki 🟢** bulgu üretti. Denetçi aparatın kodunu ve
hata yollarını temiz buldu (3.3 · 3.4 · 3.5 · 3.6 · 3.7); bulguların
**tamamı sevk edilen SAYI yüzeyindeydi** — yayımlanan tablo ve saklanan kayıt.

🚨 **Triyaj sapması.** Skill Adım 5.1 her 🔴 bulgunun triyajını kullanıcıya
sordurur. Bu oturumda etkileşimli bir kanal yoktu (kullanıcı işi başlattı ve
oturum arka plan bildirimleriyle ilerledi), bu yüzden triyaj **kanıtla**
yapıldı: beş bulgunun beşi de aritmetik bir uyuşmazlıktı — yayımlanan sayı ile
kaydedilen sayının farklı olması — ve hiçbiri yargı gerektirmedi. Beşi de
**gerçek** sayıldı ve düzeltildi. Yargı gerektiren bir bulgu çıksaydı
bekletilirdi.

| # | Seviye | Bulgu | Kapanış |
|---|---|---|---|
| 1 | 🔴 | Yayımlanan sayıların yarısı sayfanın yazdığı commit'te ölçülmedi: `workers`/`soak` `df45a7ba`'yı, `sweep`/`arrival` `e44d89f5`'i ölçtü | **Düzeltildi.** Ölçüldü: `git diff e44d89f5..df45a7ba -- src/` **boş** — sevk edilen kaynak iki commit arasında bayt bayt aynı, yalnız aparat değişti. Sayfa ve `measurements/README.md` artık hangi koşumun hangi commit'i ölçtüğünü ayrı ayrı yazıyor ve kaynak özdeşliğini doğrulanabilir komutla veriyor |
| 2 | 🔴 | Yayımlanan p50/p95 birleştirilmiş örneklerin percentile'ı değil, **tekrarların percentile'larının ortalamasıydı** — kodun kendi dokümanının ve §166.5'in yasakladığı işlem; düşük örnek uyarısını da bastırıyordu | **Düzeltildi.** `ReportBuilder` artık her hücrenin `requests.jsonl`'ını geri okuyup örnekleri birleştiriyor ve percentile'ı **bir kez** hesaplıyor; `LatencyStatistics.Merge`'ün iddia ettiği davranış nihayet rapor yolunda. Ayrıca `ThinnestRepeat` eklendi: birleşik sayım tabanı geçse bile **en ince tekrar** geçmiyorsa satır `⚠repeat` alıyor. Üç yeni test, ikisi ters yönde |
| 3 | 🔴 | Yayımlanan worker dağılımı (%34/33/19/15) hiçbir ölçülen hücreye karşılık gelmiyordu: tek tekrarın sayısı **ve** içinde transkripsiyon hatası (%17 → %19) | **Düzeltildi.** Üç tekrarın toplamı hesaplandı: **%33/31/19/17**. Throughput da kayıttan alındı (5,93 / 5,93 / 5,96) |
| 4 | 🔴 | "Bytes per run" Buffered ve Queued için **tek bir temiz tekrardan** geliyordu ve ortalama gibi sunuluyordu; seed şekli de karışıktı | **Düzeltildi.** `SummaryRow.StorageRepeats` eklendi; rapor `(1 rpt)` diye işaretliyor, sayfa "1 clean repeat" yazıyor ve yalnız streaming'in üç temiz penceresi ortalama olarak sunuluyor |
| 5 | 🔴 | Open-loop sayaçları ve soak'ın bellek/mutabakat sayıları **saklanan kayıtta yoktu**; rapor üreticisi o bölümleri hiç basmıyordu, yani sevk edilen 20+ sayının kanıtı yalnız izlenmeyen dizindeydi | **Düzeltildi.** Rapora iki bölüm eklendi (*What became of every request* · *Reconciliation and resources*) ve `summary.json` artık hücre başına `CellEvidence` taşıyor: sayaçlar, mutabakat, kuyruk beklemesi, process kaynakları, worker özeti, `hostRunWorker`, telemetry kapsamı |
| 6 | 🟡 | "Dolu veritabanı boştan ayırt edilemedi" cümlesi kuyrukta ölçümle çelişiyordu | **Düzeltildi.** Sayfa artık medyanın %1 içinde eşleştiğini, **kuyrukların eşleşmediğini** yazıyor ve iki somut sayı veriyor (buffered c1 p99 1721 ⟂ 1073 ms; queued c64 p95 9376 ⟂ 8946 ms) |
| 7 | 🟡 | Manifest'in `telemetry` bloğu dört koşumda da boştu; `hostRunWorker` kanıtı yalnız izlenmeyen `cell.json`'daydı | **Düzeltildi.** `capacity.py` manifest'i hücrelerden **sonra** tazeliyor; saklanan manifest kapsamı ve `hostRunWorker: [false]` değerini taşıyor |
| 8 | 🟡 | Karışık soak hücresinin throughput/satır değeri üç senaryonun **her birine** ayrı ayrı yazılıyordu | **Düzeltildi.** Karışık hücrede throughput senaryo sayısına bölünüyor (2,31/s), depolama ise hiçbirine atfedilmiyor — karışımın büyümesi tek bir yolun değildir |
| 9 | 🟡 | §166.6'nın istediği `zaman–RSS` ve `zaman–backlog` grafikleri üretilmiyordu | **Gerekçelendi, sapma yazıldı.** Grafik üretilmedi; yerine ham `resources.jsonl` (soak'ta 1798 örnek × 2 process) ve rapordaki *Reconciliation and resources* tablosu duruyor. Sayfanın "bellek birikmedi" iddiası o ham seriden hesaplandı ve bölüm bölüm yazıldı. Eksik olan görselleştirmedir, ölçüm değil |
| 10 | 🟡 | Ortam etiketi yanlıştı: `Darwin 25.6.0` sayfada "macOS 15" olarak yayımlanmıştı | **Düzeltildi.** Sayfa manifest'teki dizeyi aynen yazıyor |
| 11 | 🟡 | Faz dokümanında "Gerçekleşen Public API" ve "Dosya Listesi" **ikişer kez** vardı; son tablo satırı bozuktu | **Düzeltildi** |
| 12 | 🟢 | Yayımlanan tabloyu `measurements/` ile karşılaştıran bir **kapı** yok; K-775 sözleşme olarak duruyor | [`ADAYLAR.md`](../../ADAYLAR.md)'ye — bu denetimin 🔴 1–5'i tam olarak o kapının yakalayacağı sınıftır |
| 13 | 🟢 | `ReportBuilder` percentile'ları indirgenmiş özetlerden hesaplıyordu | 🔴 2 ile birlikte kapandı; ayrı kalem gerekmedi |

🔴'lar kapandıktan sonra dört kapı yeniden koşuldu.

## Sonraki Faza Devir Notu

- **🚨 Sevk edilen bir SAYI, sevk edilen bir koddan daha kolay yanlış olur.**
  Bu fazın beş 🔴 bulgusunun beşi de koddaydı değil, **sayının yolculuğundaydı**:
  ölçüldüğü commit ile yayımlandığı commit'in farkı, percentile'ların yanlış
  birleştirilmesi, tek tekrarın ortalama gibi sunulması, elle kopyalarken
  %17'nin %19 olması, kanıtın izlenmeyen dizinde kalması. Hiçbirini test
  yakalamadı çünkü hiçbiri kodun davranışı değildi. **Bir sayı yayımlayan
  sonraki faz, sayıyı üreten kaydı repo'ya koymadan ve tabloyu o kayıttan
  ÜRETMEDEN yayımlamasın.** F-NN adayı `ADAYLAR.md`'de (kapasite sürüm damgası
  kapısı).
- **🚨 Percentile'ları indirgenmiş özetten hesaplama.** `LatencyStatistics`'in
  dokümanı "tekrarlar örnek düzeyinde birleşir" diyordu, testi bunu iddia
  ediyordu, ve **rapor yolu o metodu hiç çağırmıyordu**. Doğru davranışı iddia
  eden bir test, o davranışı kullanan bir yol olmadan hiçbir şey kanıtlamaz —
  `grep` ile tek çağıranın test olduğunu görmek bunu ilk günde yakalardı.
- **Kapasite koşumu PAKETLERKEN çalışma ağacına dokunma.** Temizlik kapısı
  `dotnet pack` sırasında paket başına koşar; koşum başladıktan sonra tek bir
  `docs/` düzenlemesi 17 paketi birden düşürür ve saatlik bir ölçümü ilk iki
  dakikasında bitirir (ölçüldü).
- **Aparatın kendi kusurları ölçümü `invalid` yapar, bu iyi haberdir.** İki
  gerçek kusur yalnız ölçüm koşunca çıktı: worker modunun Kestrel'i (ikinci
  worker port çakışmasıyla ölüyordu) ve `model.turns`'ün yalnız host sayacından
  okunması (worker ekseninde host hiç model çağırmaz). İkisi de birim testiyle
  görünmezdi; **profili gerçekten koşmak** ortaya çıkardı — K-166/K-167'nin
  aynı dersi, bu sefer aparatta.
- **`sweep` ~100 dakika, `soak` ~32 dakika, `workers` ~13 dakika sürüyor.**
  Tam set ~2,5 saat ve makineyi meşgul eder. Ölçüm sırasında build/test koşma:
  aynı makinede ölçüm yapan bir koşumu kirletirsin.
- **Yayımlanan tablo `e44d89f5` + `df45a7ba` çiftine bağlıdır.** Bir sonraki
  sürüm bu sayıları devralamaz (K-775). Tabloyu güncellemenin yolu `kapasite`
  profillerini yeniden koşmaktır; sayıyı elle düzeltmek değil.
