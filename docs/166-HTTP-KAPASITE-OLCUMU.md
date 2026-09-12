# Faz 166 — HTTP Kapasite Ölçümü

> **Durum:** 📋 Planlandı (2026-09-13)
> **Kaynak:** Kullanıcının ürün değerlendirmesindeki 2. problem için faz planı isteği · **F-225** ([aday kaydı](ADAYLAR.md))
> **Önkoşul:** [Faz 157](arsiv/fazlar/157-SINIRLI-YUK-VE-IKI-PROCESS-ARIZA-KANITI.md) — store yük raporu ve process altyapısı · [Faz 97](arsiv/fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md) — exact paket tüketimi
> **Paketler:** Ölçülen: `Tracon.AspNetCore`, `Tracon.Core`, `Tracon.PostgreSql`; sevk edilen kodda değişiklik planlanmıyor.
> **Yeni paket:** 0 Tracon paketi, 0 yeni harici paket kimliği · **Migration:** 0
> **Public API:** Büyümüyor; ölçüm bileşenleri paketlenmez.
> **Tüketici yüzeyi:** Site: `docs-site/src/content/docs/guides/production.md` — kapasite kanıtının kapsamı ve sınırları.
> · Sevk edilen: XML, paket README, capability satırı ve HTTP contract değişmiyor; ölçüm aracı repo geliştirme aparatıdır.
> **Manuel test alanı:** `docs/manuel-test/36-GELISTIRME-KAPILARI.md`

## Bu Faza Başlarken

`faz-baslangic` skill'ini uygula. Yalnız aşağıdaki ilgili bölümleri oku:

1. Bu doküman; uygulama öncesinde `faz-uygulama` skill'i.
2. `docs/KARARLAR.md`: `rg -n 'K-634|K-738|K-059|K-008'`.
   K-634/K-738: süre rapordur; K-059: secret yazılmaz; K-008: ön sürüm MAF sınırı.
3. Faz 157'nin “Denetim Bulguları” ve “Sonraki Faza Devir Notu”: bitmiş event
   stream'i canlı stream kanıtı değildir; process açılışı yarış penceresine girmez.
4. `docs/hafiza/test-altyapisi.md`: “Yalıtım ve tam koşum kırılganlığı” sonundaki
   process altyapısı notu; `docs/hafiza/test-kosum-tuzaklari.md`: MTP/process notları.
5. `.agents/ortak/kapilar.md`, `.agents/ortak/test-seviyeleri.md`.
6. Kaynakta yalnız §166.1 tablosundaki girişler; bütün endpoint dosyalarını okuma.

## Amaç

Belirli donanım, configuration ve veri hacminde gerçek paket tüketicisinin
HTTP/SQL yolunu ölçmek. Yük arttığında latency, kaynak tüketimi, backlog ve
doğruluk birlikte görünür. Rapor, ölçülen yük aralığını anlatır; genel kapasite
veya SLA garantisi vermez. F-225, Faz 157'nin kapanmış F-217 işinin üstüne eklenir.

## 166.1 — Doğrulanmış başlangıç

Kanıtlar **2026-09-13**, HEAD `4e982ade9736bf847934c9540c9fdbc5e4411e17`
üzerinde doğrulandı. Çalışma ağacında başka UI/site değişiklikleri vardı; bu fazın
uygulayıcısı kendi başlangıç commit'ini ve ilgili diff'ini yeniden kaydeder.

| Kaynak ve satır | Gözlem |
|---|---|
| `tests/Tracon.PostgreSql.IntegrationTests/Load/BoundedSqlLoadTests.cs:44` | 8 worker × 25 run, 20 event/run, 256 karakter; model yerine 20 ms gecikme |
| Aynı dosya `:81`, `:120` | Doğrudan `RunEventWriter` kullanır; run sayısını doğrular. Gerçek HTTP istemcisi değildir |
| `bench/baseline.json:1` | Üç microbenchmark; tahsis kapısının girdisi. Bu faz dosyayı değiştirmez |
| `src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs:939` | `Idempotency-Key` varsa buffered yanıt; yoksa doğrudan SSE |
| `tests/Tracon.AspNetCore.FunctionalTests/AsyncRunTests.cs:25` | `Prefer: respond-async` → 202, `runId`, `jobId`, `Location` |
| `src/Tracon.AspNetCore/Endpoints/RunEndpoints.cs:226`, `:1200` | Kayıtlı/canlı event SSE'si store'u poll eder; doğrudan run SSE'sinden farklı frame contract'ı vardır |
| `scripts/release_extension_samples.py:114`, `:152` | Exact sürüm, izole feed/cache ve restore doğrulama deseni mevcut |
| `tests/Tracon.WorkerHarness/Tracon.WorkerHarness.csproj:31` | Worker harness source `ProjectReference` kullanır; doğrudan packed-consumer kanıtı sayılamaz |
| `src/Tracon.Abstractions/Scheduling/JobRecord.cs:79` | `ScheduledFor`, `StartedAt`, `CompletedAt`, `CreatedAt` mevcut; job bekleme/çalışma süreleri ayrı hesaplanabilir |

Mevcut yerel rapor `artifacts/load/bounded-sql-load-20260912-204135.md`,
M1 Pro / 16 GiB / PostgreSQL 18.4 üzerinde 1,07 s wall-clock ve 21,93 ms/run
control-plane ortalaması kaydeder. Commit'i `c4d72c6...` olduğundan HEAD ölçümü
değildir. Artifact git'te olmayabilir; planın uygulanması ona bağlı değildir.

`maf-api-kesfi` koşuldu: `IChatClient`, `ChatResponse`, `ChatResponseUpdate`,
`FunctionCallContent`, `FunctionResultContent` reflection ile doğrulandı.
`GetResponseAsync` → `Task<ChatResponse>`; `GetStreamingResponseAsync` →
`IAsyncEnumerable<ChatResponseUpdate>`. Kullanılan paket sürümleri
`Directory.Packages.props`'tan gelir. Nullability/varsayılan parametreleri
uygulama sırasında derleyiciyle doğrula; MAF için yeni paralel tip yaratma.

## 166.2 — Sınırlar ve topoloji

- İlk sağlayıcı PostgreSQL; tek gerçek ASP.NET Core host ve onun job worker'ı.
  Ayrı driver process'i gerçek loopback TCP üzerinden çağırır. In-process
  TestServer kapasite kanıtı sayılmaz. Host, driver ve DB yükleri ayrı raporlanır.
- Host yalnız exact `PackageReference` ile çalışır. Host/driver repo dışındaki
  geçici dizine kopyalanır; bağımsız restore/build yapılır. Repo `Directory.*`
  mirası veya `src/` bağlantısı çalışma zamanı bağımlılığı olamaz.
- Tracon paketleri yalnız izole yerel feed'den; cache boş ve koşuma özeldir.
  Sürüm zorlanarak yeni pack üretilir. `.nupkg` hash'leri ve assets doğrulanır.
  Mevcut release altyapısının uygun parçaları paylaşılır; ikinci pack/cache
  algoritması kopyalanmaz. Kapasite komutu yayın, tag veya npm publish yapmaz.
- Ölçüm opt-in'dir. Tam yük ve soak PR/standart kapanış/release yoluna otomatik
  eklenmez. Kısa harness doğrulaması normal CI'a girer; zaman limiti SLA değildir.
- Yeni public ürün tipi, endpoint, runtime option, migration, UI değişimi yok.
  Yeni proje oluşturmak yeni NuGet paketi oluşturmak değildir: `IsPackable=false`.
  Tüketici dependency grafiğine ek ağırlık **0 paket**; UI bundle artışı **0 KB**.
- SQL Server/SQLite kapasite karşılaştırması, dağıtık driver, çok host ölçekleme,
  iki sürümlü rolling upgrade, yeni chaos matrisi ve gerçek provider yükü kapsam dışı.
  Faz 157'nin iki-process arıza kanıtı korunur; bu faz onun tekrarını yazmaz.
- Ölçümde bulunan runtime kusuru gizlenmez. `kusur-giderme` ile ayrı kapsamda
  kapatılır; planı sessizce optimizasyon fazına dönüştürme.

## 166.3 — Üç senaryo ve kontrollü model

Prefix `/tracon`; fixture agent adı `capacity-agent`. İsteklerin her biri
benzersiz correlation değeri taşır. İlk kapsam bağımsız yeni session'lardır;
uzayan konuşma geçmişi için kapasite iddiası verilmez.

| Profildeki senaryo | HTTP yolu | Beklenen kanıt |
|---|---|---|
| `buffered` | `POST /api/agents/capacity-agent/run`, benzersiz `Idempotency-Key` | Buffered gövde, run kimliği, terminal durum ve tam yanıt; idempotency yazma maliyeti bu senaryoya dahildir |
| `streaming` | Aynı POST, idempotency başlığı yok | Canlı `run/update/done/error` frame'leri gerçekten tüketilir; ilk update, toplam süre ve birleştirilmiş yanıt doğrulanır |
| `queued` | Aynı POST + `Prefer: respond-async`; sonra `Location` ve `/api/runs/{id}/events` | 202 kabulü, job çalışması, canlı kayıt stream'i, son event ve terminal job/run durumu |

`buffered` ile `streaming` farkı yalnız serialization farkı diye sunulmaz.
`queued` senaryosunda event subscriber run bitmeden bağlanmalıdır. Canlı bağlanma
kanıtı yoksa koşum “historical-only” işaretlenir ve canlı kabul case'i geçmez.
Doğrudan SSE sequence'i ile store event sequence'i birbirine eşit sayılmaz.

Test provider'ı `AddModelProvider` ile kayıt edilir; ham `IChatClient` verir.
MAF tool döngüsü, compiler, kayıt ve store yolu gerçek kalır. Provider, session
ve run başına durumu ayırır; global sayaçla “ikinci çağrı” kararı verilmez.

Varsayılan sentetik yük tanımı: 1 KiB UTF-8 istek; toplam 1 s model bekleme
bütçesi; 20 × 256 ASCII karakter final çıktı. Streaming bu çıktıyı 20 delta'ya
böler. Bir kod tool'u bir kez çağrılır ve 1 KiB deterministik sonuç verir.
Model iki turda tamamlar; provider kendi tool loop'unu kurmaz. Tool sonucu
alınmadan final yanıt üretilemez. Tool argümanında correlation taşınır.
Bu değerler **deney girdisidir**, üretim trafik dağılımı veya ölçülmüş süre değildir.
Gerçekleşen model bekleme süresi ayrıca kaydedilir; timer gecikmesi varsayılmaz.
Rastgelelik gerekiyorsa seed manifest'e yazılır; varsayılan yük deterministiktir.

İki sentetik tenant eşit payla çalışır. Test host'u sabit tenant header'ını
yalnız loopback fixture sınırında çözer; kayıtlar gerçek tenant store yolunu
kullanır. Bu kurulum production kimlik doğrulama performansı iddiası vermez.
Rate limit, quota, worker sayısı, poll aralığı, kayıt, retention, cache, retry
ve telemetry ayarlarının etkili değerleri manifest'te açıkça bulunur.
Yanıt cache'i kapalı; kayıt açık; seed veri yük sırasında silinmez. Worker
sayısı tüm basamaklarda sabit 8; connection pool limiti açıkça 100 olarak
kurulur ve raporlanır. Başka bir değer seçilirse ayrı configuration sayılır.

## 166.4 — Koşum profilleri ve kaynak sınırı

Yeni geliştirme komutu: `python3 scripts/kapi.py kapasite`.
Komutun `--profil`, `--surum`, `--cikti` parametreleri vardır; adları bu fazın
planlanan geliştirme yüzeyidir. Mevcut `performans` komutu değişmez.

| Profil | Yük tanımı | Nerede koşar |
|---|---|---|
| `smoke` | Üç senaryo; tenant başına en az 2 run; canlı akış ve mutabakat | Normal Linux CI, kısa doğruluk koşumu; latency eşiği yok |
| `sweep` | Üç senaryo × 1/8/32/64 concurrency × boş/dolu DB × 3 tekrar | Açık komutla; her hücrede 15 s warm-up + 60 s ölçüm |
| `arrival` | `queued`; 1/4/8/16 planlanan istek/s; her seviyede 60 s | Açık komutla; geliş zamanlaması önceki yanıtı beklemez |
| `soak` | Üç senaryonun eşit karışımı; sweep sonrası seçilen sabit yükte 30 dakika | Açık komutla; seçilen concurrency gerekçesi raporda |

Warm-up run'ları ölçüm örneklerinden ayrılır. Ölçüm sonrasında yeni giriş
durur; kabul edilen işlerin tamamlanması ayrı drain evresinde izlenir. Pencere
içinde tamamlanan throughput ile o pencereye giren cohort'un eventual sonucu
ayrı tutulur. Drain süresi ana throughput paydasına karıştırılmaz.

Boş/dolu karşılaştırmasında her hücre/tekrar izole schema ve temiz process ile
başlar. Dolu fixture: 10.000 tamamlanmış run, her birinde 20 kayıtlı delta;
iki tenant'a eşit bölünür. Seed public store yollarını kullanır, süresi ölçüm
penceresine girmez. Başlangıç gerçek row/byte sayıları doğrulanır. Bu hacim
büyük üretim verisi iddiası değildir; ilk karşılaştırma noktasıdır.

Geliş hızında `planned/sent/accepted/rejected/completed/failed/timedOut/notSent`
sayaçları ayrı tutulur. Driver maksimum in-flight sınırında slot bulamazsa
planlanan isteği bekletip yükü sessizce düşürmez; `notSent` ve dispatch gecikmesi
yazar. Başarı latency'si yanında tüm planlanan isteklerin sonucu gösterilir.
HTTP 429/503 veya transport hatası başarı dağılımından çıkarılıp unutulmaz.

Kaynak sınırları profil girdisidir: varsayılan en fazla 128 driver in-flight,
host RSS 4 GiB, driver RSS 1 GiB, koşum DB boyutu 5 GiB, minimum boş disk
10 GiB. Kontrollü request bekleme limiti 120 s, drain limiti 300 s; bunlar
harness'in sonsuz beklememesini sağlar, performans başarı eşiği değildir.
Limit aşımında yeni giriş kesilir, mümkünse drain ve mutabakat yapılır;
rapor `incomplete/resource-limit` veya `incomplete/drain-limit` olur.
Bir basamak yarım kaldığında daha yüksek basamaklar otomatik koşulmaz.
Kullanıcı kesintisi de partial rapor üretir; başarıya çevrilmez.

Soak yükü, sweep'te tamamlanan ve backlog'u birikmeyen basamakların en yükseğinin
altından seçilir; böyle bir basamak yoksa önce sorun araştırılır. Seçim raporda
gerekçelendirilir; kapasite sertifikası verilmez. Host heap/RSS ve backlog
zaman serisinde birikim incelenir. Süre veya eğim için CI eşiği eklenmez.

## 166.5 — Ölçüm ve doğruluk sözleşmesi

| Boyut | Ölçüm ve sınırı |
|---|---|
| HTTP | Gerçek dispatch → headers, ilk içerik, son içerik süreleri; status ve timeout; JSON parse/SSE tüketimi dahil uçtan uca süre |
| İstatistik | Her hücre için n, p50/p95/p99 ve throughput; nearest-rank yöntemi; p95 için n<100, p99 için n<1000 ise düşük örnek uyarısı; tekrar percentile'ları ortalanmaz |
| Model | Provider sınırında gerçekleşen süre ve tur sayısı; istemci p95'inden model p95'i çıkarılmaz; tool ve kayıt maliyeti modele yazılmaz |
| Host/driver | Ayrı PID ile CPU, RSS, managed heap, allocation ve GC zaman serisi; sampling aralığı 1 s; fiziksel RAM ile process limiti ayrı alanlar |
| SQL | Query süresi/sayısı, connection pool bekleme, aktif bağlantı, database boyutu; kaynak/instrument adı raporda |
| Queue | Kabul süresi; `JobRecord.StartedAt-CreatedAt` ile ilk attempt dispatch beklemesi; attempt çalışma süresi; backlog, en eski hazır işin yaşı |
| SSE | İlk update gecikmesi, frame araları, aktif bağlantı, parse/erken EOF hatası; kayıtlı stream'de sequence ve son event doğruluğu |

İlk implementasyon adımı bir telemetry probudur: runtime ve Npgsql
instrument'larının adları/birimleri yüklü sürümden doğrulanır. Npgsql imzası
ve instrument isimleri bu plan anında **doğrulanmadı — uygulanırken ölçülmeli**.
Var olmayan metriğe sıfır yazılmaz; raporda `unavailable` + sebep bulunur.
HTTP sayaçları/süreleri, model tur/süre kaydı, host/driver RSS, queue durumları
ve run/event mutabakatı zorunludur; bunlar yoksa ölçüm geçersizdir. Platforma
bağlı CPU/GC/SQL instrument ayrıntıları eksikse rapor geçerli ama telemetry
kapsamı dar olabilir. Zorunlu ve isteğe bağlı alanlar schema'da ayrılır.
SQL query ve pool bekleme ölçümü yoksa darboğaz “pool” diye ilan edilmez.
Yalnız ölçüm için ürünün public API'si veya dependency grafiği büyütülmez.
Yeni harici paket ihtiyacı doğarsa net geçişli maliyet ölçülerek kapsam yeniden
değerlendirilir; planın sıfır paket iddiası sessizce değiştirilmez.

Process'ler arası monotonic timestamp doğrudan çıkarılmaz. Her süre kendi
process saatinde ölçülür; UTC yalnız korelasyon içindir. Model delta üretimi
ile socket teslimi arasında güvenilir ortak zaman tabanı yoksa “teslim gecikmesi”
uydurulmaz; istemcideki frame araları raporlanır. Monitoring/HTTP polling yükü
de workload'un parçasıdır, request sayısı ve örnekleme aralığı belirtilir.

Her run için sentetik bir beklenen-sonuç kaydı oluşturulur. Drain sonrasında
iki tenant için public store üzerinden, gerekirse read-only SQL aggregate ile:

- Kabul edilen run kimliği benzersizdir; her kimlik terminal run'a karşılık gelir.
- Sağlıklı senaryoda yanıt correlation ve içerik checksum'u tam eşleşir;
  tool bir kez çalışır ve sonucu modele geri döner.
- Beklenen delta içeriği store'da bulunur; buffer/stream modelinin ürettiği
  gerçek event şekli küçük smoke ile sabitlenir. “Her run toplam 20 event”
  denmez: lifecycle ve tool event'leri ayrıca vardır.
- Kayıtlı SSE, store'daki event'leri sıra ve içerik olarak eksiksiz taşır.
  Run terminal olduktan sonra son event'in okunması ayrıca doğrulanır.
- Tenant A/B eşzamanlıdır; karşı tenant'ın kimliğini okuma girişimi HTTP'de
  reddedilir, listede ve stream'de karşı tenant içeriği yoktur.
- Retry/iptal/fault manifest'lerinde beklenen sonuç ayrı tanımlanır. At-least-once
  davranışa genel exactly-once iddiası yüklenmez; sağlıklı tool sayımı fault
  koşumuna taşınmaz. Log'daki kayıt hatası ve eksik kayıt mutabakatı raporu bozar.

Kapasite raporunun geçerliliği `complete/incomplete/invalid` olarak ayrılır.
Hızın düşük olması `invalid` sebebi değildir. Harness sayım hatası, veri kaybı,
tenant karışması veya eksik zorunlu ölçüm kanıtı koşumu başarısız yapar.
Gözlenen 429/backlog/timeout bir doygunluk bulgusudur; başarılı run diye sayılmaz.
Request timeout sonrası run hâlâ ilerliyorsa drain'de nihai sonucu izlenir;
timeout'u otomatik server cancellation sayma.

## 166.6 — Artifact ve paylaşım

Her koşum `artifacts/capacity/<run-id>/` altında sürümlü `manifest.json`,
bounded/akışlı `requests.jsonl`, `resources.jsonl`, `summary.json`, `report.md`
üretir. Rapor ve ham örnekler aynı veri kümesinden hesaplanır. Tüm run'ları
RAM'de biriktirerek uzun koşumun driver'ını büyütme; histogram/özet ve akışlı
dosya kullan. Quantile yöntemi ve varsa histogram hassasiyeti açık yazılır.

Manifest: commit + dirty durumu + ilgili diff hash'i, paket sürümü/hash'leri,
OS/architecture/CPU/fiziksel RAM/process limitleri, .NET ve PostgreSQL sürümü,
image kimliği, schema kimliği, etkili allowlist configuration, seed şekli,
worker/pool/istemci sınırları, warm-up/ölçüm/drain aralıkları, telemetry kapsamı.
Connection string, credential, ham environment dökümü ve kullanıcı içeriği yok.

Grafikler standalone SVG olarak üretilir: yük–latency/throughput, zaman–RSS/heap,
zaman–backlog. Bunlar ölçüm grafiğidir; mimari diyagram gerekirse Mermaid kullan.
Eksik basamaklar ve ölçülemeyen noktalar sıfırla doldurulmaz.

Karşılaştırma yalnız workload/configuration/dataset/ortam kimlikleri uyumluysa
verilir; farklı makineler otomatik regresyon kararı üretmez. İlk rapor en az
üç tekrarın değişkenliğini gösterir. Darboğaz yorumu kanıta bağlanır; telemetry
yetersizse “belirlenemedi”, tavan bulunmadıysa “ölçülen aralıkta bulunmadı” denir.
Siteye ham büyük artifact ve secret girmez. `production.md` ölçüm kapsamını ve
bir temsilî ortamlı özeti anlatır; repo private ise tüketiciyi erişemeyeceği
script bağlantısına yönlendirme. Ayrıntılı koşum komutları `bench/capacity/README.md`.

## Planlanan Public API

Ürün için yeni/değişen public üye ve HTTP endpoint yok. `PublicAPI.Shipped.txt`
dosyalarının 17'si de yalnız `#nullable enable` taşıyor (2026-09-13); yine de
ölçüm kolaylığı için public seam açılmaz. MAF tipleri doğrudan kullanılır.
K1: opt-in ölçüm; K2: tool yalnız kod; K3: MAF sarmalanmaz; K4: fixture'ın
consumer kaydı kazanır. Arayüz payı: 0 KB gzip, yeni ekran metni: 0.

## Planlanan Dosya Listesi

| Yol | İş |
|---|---|
| `bench/capacity/README.md`, `profiles/*.json` | Tekrarlanabilir komutlar, workload ve kaynak bütçeleri |
| `bench/capacity/Tracon.CapacityHost/` | Paket tüketen host; test provider, kod tool'u, telemetry ve seed modu |
| `bench/capacity/Tracon.CapacityDriver/` | Ayrı HTTP driver; üç senaryo, bounded yük, SSE reader, rapor üretimi |
| `bench/capacity/Directory.Build.props` | Yalnız apparatus ayarları; geçici dizine taşındığında bağımsız build/restore |
| `scripts/capacity.py`, `scripts/capacity_test.py` | Pack/restore/process/cleanup orchestration ve script hata yolları |
| `scripts/kapi.py`, `scripts/kapi_test.py` | `kapasite` alt komutu; standart kapanışa ağır koşum eklemez |
| `tests/Tracon.Capacity.Tests/` | Normal suite'te çalışan histogram/sayaç/profil testleri; Docker veya yeni pack istemez |
| `bench/capacity/Tracon.Capacity.Acceptance/` | Kısa packed-host HTTP/store ve process hata testleri; yalnız `kapasite --profil smoke` pack/restore sonrasında çağırır |
| `tests/Shared/Infrastructure/ProcessRunner.cs`, `ManagedProcess.cs` | Var olan altyapıyı link'le; gerekli düzeltme dışında kopya üretme |
| `Tracon.slnx`, `Tracon.no-docker.slnf`, `.github/workflows/ci.yml` | Test keşfi/build; Linux packed smoke'un ayrı ve açık adımı; Windows'a Docker bağımlılığı sızmaz |
| `scripts/release_extension_samples.py` | Yalnız ortak izole tüketici hazırlığı ayrıştırılacaksa; mevcut altı sample kapsamı azalmaz |
| `docs-site/src/content/docs/guides/production.md`, `docs/manuel-test/36-GELISTIRME-KAPILARI.md` | Kanıt sınırı ve kabul case'leri |

Host/driver/acceptance `bench/` altındadır; normal solution test keşfine veya
extension sample inventory'sine girmez. Acceptance projesi kendi test ayarlarını
taşır, var olan test paket sürümlerini kullanır ve paketlenmez. Normal suite
yalnız `tests/Tracon.Capacity.Tests` projesini keşfeder. Source link'i yalnız
apparatus kodunda kullanılabilir, ölçülen Tracon runtime'ında olamaz. CI'da
Linux smoke adımı pack → izole restore → acceptance sırasını açık kurar;
eksik Docker veya paket hazırlanması sessiz skip değil başarısız önkoşuldur.

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Planlanan kanıt |
|---|---|---|
| Eski paket/global cache veya source proje yüklenir | Packed consumer + script | `CapacityPackageIsolationTests`: exact olmayan sürüm reddi; assets/DLL provenance; temiz cache |
| HTTP yerine fixture kısa yolu ölçülür; tool loop atlanır | Packed HTTP + SQL | `CapacityPathTests`: gerçek TCP, custom provider, tool sonucu ve kalıcı run |
| SSE tüketilmez veya son frame kaybolur | Packed HTTP + SQL | `CapacityStreamTests`: canlı bağlanma barrier'ı, exact çıktı, store sıra mutabakatı, kesik EOF |
| Closed-loop driver geliş yükünü sessiz azaltır | Birim + ayrı yavaş HTTP server | `ArrivalScheduleTests`: planned/sent/notSent; dispatch gecikmesi; concurrency sınırı |
| Percentile veya cohort paydası yanlış | Birim | `CapacityStatisticsTests`: elle hesaplanan küçük veri kümesi, timeout, drain, tekrarların birleşimi |
| DB yazımı hata verir; run devam ettiği için ölçüm yeşil görünür | Packed HTTP + gerçek SQL | `CapacityIntegrityTests`: izole schema'da kayıt fault'u; checksum/count mutabakatı koşumu reddeder |
| Tenant/senaryo durumu eşzamanlı çağrıda karışır | Packed HTTP + SQL | `CapacityTenantTests`: gerçek overlap ve karşı tenant read/list/stream reddi |
| İptal/timeout sonrası process, bağlantı veya job unutulur | Process + packed HTTP | `CapacityLifecycleTests`: in-flight Ctrl+C/timeout, drain, partial rapor, child process cleanup |
| Boş/aşırı profil, output veya metric buffer belleği tüketir | Birim + process | `CapacityBudgetTests`: sıfır/negatif süre/hız reddi, cap aşımı; büyük fixture'da bounded writer |
| Kaynak/telemetry toplama başarısızlığı sıfır değer sayılır | Birim + process | `CapacityTelemetryTests`: unsupported alan, collector hatası, geçersiz ölçüm durumu |
| Secret process args/log/rapora sızar | Process + artifact | `CapacityRedactionTests`: sentetik canary credential ile stdout/stderr/JSON/Markdown taraması |
| Seed/cleanup başka schema'ya dokunur | Gerçek SQL | `CapacityIsolationTests`: benzersiz ownership marker; yalnız yaratılan schema/container temizlenir |

Fault testleri tam 30 dakikalık koşum istemez. Kısa deterministik case'ler
raporlayıcıyı iki yönde doğrular: doğru veri geçer, eksik veri düşer.

## Manuel Kabul Case'leri

Numaralar uygulamada alan dosyasındaki sıradaki boş MT-GDK kimliklerinden alınır.

| # | Ön koşul / adım | Beklenen sonuç |
|---|---|---|
| 1 | Yerel PostgreSQL, exact pack; `kapasite --profil smoke` | Üç yol gerçek HTTP'den geçer; iki tenant'ın store/yanıt mutabakatı tamdır |
| 2 | `kapasite --profil sweep` | İki seed hacmi, üç tekrar ve tamamlanan basamakların n/latency/throughput grafikleri; yarım kalan hücreler açık |
| 3 | `kapasite --profil arrival` | Gönderim planı yanıtı beklemez; planned/sent/notSent ve backlog birlikte görünür |
| 4 | Gerekçeli yük seçimi; `kapasite --profil soak` | 30 dakikalık ölçüm ve drain; kaynak zaman serisi, son mutabakat, tamamlanma durumu |
| 5 | Canlı run sırasında driver iptali veya düşük kaynak cap'i | Partial rapor korunur; sahip olunan process'ler temizlenir; başka çalışma etkilenmez |
| 6 | Doğrulama fixture'ında kayıt kaybı / kesik SSE | Koşum başarı göstermez; kayıp kimlik/frame açıklanır |
| 7 | Aynı raporu tekrar üret; farklı makine manifest'i ile karşılaştır | Özet tekrar üretilebilir; uygunsuz karşılaştırma ve düşük örnek açık işaretlenir |

## Açık Sorular

Planı bloklayan soru yok. Kayıt ve 30 dakika için önerilen varsayılanlar
kullanıldı; farklı kullanıcı yönlendirmesi gelirse uygulama başlamadan güncellenir.
Npgsql/runtime instrument erişimi §166.5'teki ilk probda ölçülecek teknik
belirsizliktir. Ölçülemeyen alan için yalan sıfır veya tahminî isim kullanılmaz.

## Bitiş Ölçütleri (DoD)

- [ ] `kapasite` komutu exact sürüm/izole cache ile repo dışında host/driver kurar; gerçek TCP ve SQL yolu kanıtlıdır.
- [ ] `smoke` Linux CI'da açık adım olarak koşar; ağır profiller standart test/kapanış/release'e eklenmemiştir.
- [ ] Üç senaryo ve iki tenant doğrulanmıştır; buffered idempotency maliyeti, iki SSE contract'ı ve canlı subscriber ayrımı raporda doğrudur.
- [ ] Sweep boş/dolu veriyle ve üç tekrarla koşulmuştur; resource cap nedeniyle durulan hücreler gerekçeli devredilir, tamamlanmış gibi gösterilmez.
- [ ] Open-loop arrival raporu planned/sent/notSent, dispatch gecikmesi ve backlog'u birlikte taşır.
- [ ] Seçim gerekçesi yazılmış yükte 30 dakikalık soak tamamlanmıştır; sağlıklı workload'un son mutabakatında kayıp/tenant karışması yoktur.
- [ ] Süre/hız CI eşiği yoktur; K-634/K-738 ve allocation baseline korunmuştur.
- [ ] CPU/RSS/heap/GC, SQL telemetry kapsamı, queue ve SSE ölçümleri ortamıyla raporlanmıştır; eksikler ve örnek sayıları açıktır.
- [ ] JSON/Markdown/grafikler aynı ham veriden tekrar üretilebilir; ilk darboğaz veya belirlenememe nedeni kanıtla yazılmıştır.
- [ ] Hata modu tablosundaki kısa testler koşmuştur; kayıp kayıt, kesik SSE, yanlış provenance ve iptal negatif kanıtları vardır.
- [ ] Dört doğrulama kapısı sıfır uyarı verir: `python3 scripts/kapi.py kapanis --taban <uygulama öncesi commit>`.
- [ ] `samples/Tracon.Api` ile gerçek run yapılmış, komut ve güvenli çıktı kapanışa yazılmıştır; bu smoke sentetik kapasite verisine karıştırılmaz.
- [ ] Secret taraması boş döner; raporlar, process çıktıları ve manifest ayrıca taranmıştır.
- [ ] Manuel case'ler `docs/manuel-test/36-GELISTIRME-KAPILARI.md` içine eklenmiş; otomatikleştirilebilenler koşmuştur.
- [ ] `faz-denetim` uygulanmış; 🔴 bulgu kalmamıştır. `faz-tamamlama` ile site/doküman senkronu ve arşiv kapanışı yapılmıştır.
- [ ] `production.md` yalnız ölçülen kapsamı anlatır; ilgili site kapıları temizdir.

### Planlanan doğrulama komutları

Aşağıdaki `kapasite` komutları **henüz yoktur**, bu fazda eklenecektir.
`--surum` exact sürüm ister; aşağıdaki sürüm yalnız yerel ölçüm örneğidir.

```bash
python3 scripts/kapi.py kapasite --profil smoke --surum 1.0.0-preview.capacity166
python3 scripts/kapi.py kapasite --profil sweep --surum 1.0.0-preview.capacity166
python3 scripts/kapi.py kapasite --profil arrival --surum 1.0.0-preview.capacity166
python3 scripts/kapi.py kapasite --profil soak --surum 1.0.0-preview.capacity166
```

Soak seçimi `profiles/soak.json` içinde açık concurrency olarak kaydedilir;
öneri algoritması gizlice workload değiştirmez. Connection yalnız environment
veya mevcut secret mekanizmasından okunur; komut argümanına konmaz.

## Riskler

| Risk | Önlem |
|---|---|
| M1 Pro/16 GiB üzerinde host+driver+DB birbirini sınırlar | Ayrı PID/container ölçümleri, kaynak cap'leri; sonuç yalnız bu topoloji için yorumlanır |
| Telemetry'nin kendisi latency veya allocation ekler | Collector ayarlarını sabitle ve manifest'e yaz; gerektiğinde aynı fixture ile açık/kapalı kontrol ölçümü |
| Büyüyen veri her tekrarın koşulunu değiştirir | Her hücreye aynı seed ve yeni schema; gerçek başlangıç boyutu |
| İstemci daha önce doyar | Driver CPU/RSS, dispatch lag ve notSent görünür; server kapasitesi diye raporlanmaz |
| Uzun koşum sınırsız log/event dosyası üretir | Bounded writer, disk bütçesi, partial rapor ve ölçüm dışı temizlik |
| Örnek sayısı az veya doygunluk hiç bulunmaz | Eksik güven saklanmaz; ölçülen üst basamak garanti kapasite diye sunulmaz |

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K numarası rezerve edilmez.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
