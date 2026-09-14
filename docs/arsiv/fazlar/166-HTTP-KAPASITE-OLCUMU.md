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

## Bu Faza Başlarken

`faz-baslangic` skill'ini uygula. Yalnız aşağıdaki ilgili bölümleri oku:

1. Bu doküman; uygulama öncesinde `faz-uygulama` skill'i.
2. `docs/KARARLAR.md`: `rg -n 'K-634|K-738|K-059|K-008|K-641|K-739'`.
   K-634/K-738: süre rapordur; K-059: secret yazılmaz; K-008: ön sürüm MAF sınırı.
   **K-641: `IJobHandler` at-least-once'tır** — fazladan attempt meşrudur,
   eşzamanlı örtüşme değildir. **K-739: iki process ölçümü çok node DESTEK
   BEYANI değildir** — `workers` profilinin sınırını bu iki karar çizer.
3. Faz 157'nin “Denetim Bulguları” ve “Sonraki Faza Devir Notu”: bitmiş event
   stream'i canlı stream kanıtı değildir; process açılışı yarış penceresine girmez.
4. `docs/hafiza/test-altyapisi.md`: “Yalıtım ve tam koşum kırılganlığı” sonundaki
   process altyapısı notu; `docs/hafiza/test-kosum-tuzaklari.md`: MTP/process notları.
5. `.agents/ortak/kapilar.md`, `.agents/ortak/test-seviyeleri.md`.
6. Kaynakta yalnız §166.1 tablosundaki girişler; bütün endpoint dosyalarını okuma.

### Faz 170'ten devralınanlar (2026-09-14)

Bu faz Faz 170'ten **sonra** koşacak; iki nokta ölçümü ilgilendirir.

- 🚨 **`guides/production.md` değişti.** Faz 170 o sayfaya iki paragraf, bir
  tablo notu ve bir checklist satırı ekledi (`RequireProductionProfile()`).
  §166.6'nın temsilî sayı tablosu oraya eklenirken sayfanın **bugünkü** hâline
  bak; ağırlık kapısı (`npm run check:weight`, 58000 B tavan) o sayfada zaten
  koşuyor ve tablo onu zorlayabilir.
- **`AddTracon()` yedi kayıt daha yapıyor** (bir `IHostedService` +
  altı `IProductionProfileCheck`), yani başlangıç kompozisyonu bir miktar
  büyüdü. **İstek yolu değişmedi**: doğrulayıcı, uygulama
  `RequireProductionProfile()` çağırmadıkça **hiçbir şey çözmez** ve kontroller
  hiç kurulmaz. Ölçüm senaryolarında profili çağırma — çağırırsan ölçtüğün şey
  bir başlangıç kapısının maliyeti olur, HTTP kapasitesi değil. Başlangıç
  süresini ölçen bir senaryo eklersen bu yedi kaydı gerekçe olarak yaz.

## Amaç

Belirli donanım, configuration ve veri hacminde gerçek paket tüketicisinin
HTTP/SQL yolunu ölçmek. Yük arttığında latency, kaynak tüketimi, backlog ve
doğruluk birlikte görünür. Rapor, ölçülen yük aralığını anlatır; genel kapasite
veya SLA garantisi vermez. F-225, Faz 157'nin kapanmış F-217 işinin üstüne eklenir.

Revizyon sonrası faz üç soruya daha cevap verir ve üçü de **boyutlandırma**
sorusudur, hız sorusu değil: bir run diske ne kadar yazar (**write
amplification**), depolama run başına ne kadar büyür (**storage growth**), ve
işi ikinci bir worker process'i eklemek nasıl değiştirir (**çekişme**). İlk
ikisi planın zaten kurduğu izole schema ve başlangıç row/byte doğrulamasının
üstünde neredeyse bedavadır; üçüncüsü Faz 157'nin process altyapısını kullanır.
Hiçbiri yeni bir garanti kurmaz — üçü de mevcut davranışı **ölçer**.

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
| `tests/Shared/Infrastructure/WorkerProcessHost.cs` · `ProcessRunner.cs` · `ManagedProcess.cs` (2026-09-14) | Öldürülebilir uzun ömürlü worker process'i başlatma altyapısı **mevcut** — Faz 157 bunu tam bu iş için yazdı. `workers` profili yeni process altyapısı yazmaz, bunların üstüne kurulur |
| `src/Tracon.Abstractions/Scheduling/TraconSchedulingOptions.cs:22` (2026-09-14) | `RunWorker` varsayılanı `true`. `workers` profilinde HTTP host'ta **kapatılmalıdır**, yoksa "1 worker" hücresi iki worker olur |
| `docs/KARARLAR.md` K-641 · K-739 (2026-09-14) | `IJobHandler` at-least-once'tır (fazladan attempt meşru, örtüşme değil) · iki process ölçümü **destek beyanı değildir** — ikisi de worker boyutunun sınırını çizer |
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

- İlk sağlayıcı PostgreSQL; gerçek ASP.NET Core host ve onun job worker'ı.
  Ayrı driver process'i gerçek loopback TCP üzerinden çağırır. In-process
  TestServer kapasite kanıtı sayılmaz. Host, driver ve DB yükleri ayrı raporlanır.
  **`workers` profilinde HTTP host bir tanedir ve kendi job worker'ı KAPALIDIR**
  (`Tracon:Scheduling:RunWorker=false`); işi yalnız 1/2/4 ayrı worker process'i
  yapar, hepsi aynı makinede aynı kuyruğa abone olur. Yani eksendeki sayı
  toplam worker sayısıdır — host'unki üstüne eklenmez. Faz 157'nin
  `WorkerProcessHost`'u kullanılır, ikinci bir process altyapısı yazılmaz.
  Worker sayısı raporda ayrı bir eksendir, concurrency ile çarpılmaz.
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
- SQL Server/SQLite kapasite karşılaştırması, dağıtık driver, **birden çok
  MAKİNE**, iki sürümlü rolling upgrade, yeni chaos matrisi ve gerçek provider
  yükü kapsam dışı. Faz 157'nin iki-process **arıza** kanıtı korunur; bu faz
  onun tekrarını yazmaz.
- 🚨 **Worker sayısı ölçümünün sınırı yazılıdır.** Faz 157 arıza tarafını
  kanıtladı (ölen worker'ın işi devralınır, lease dolmadan devralma olmaz).
  Bu faz **yük altında çekişmeyi** ölçer: claim çarpışma oranı, worker sayısıyla
  throughput'un nasıl değiştiği, backlog'un nasıl dağıldığı. Ölçüm tek makinede,
  tek DB'de, tek saat tabanındadır — **ağ bölünmesi, saat kayması ve makineler
  arası gecikme kapsam dışıdır**, dolayısıyla rapor çok-node davranışı hakkında
  hiçbir şey kanıtlamaz. K-739 aynen geçerlidir: hiçbir yerde "çok node
  destekleniyor" cümlesi kurulmaz, SQLite tek process tavsiyesi korunur.
  Ölçülen throughput ölçeklenmiyorsa bu bir bulgudur, gizlenmez.
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

**Worker sayısı boyutu yalnız `queued` senaryosundadır** (revizyon 2026-09-14).
Sebebi ölçülebilirdir: `buffered` ve `streaming` isteği kabul eden HTTP
process'inde tamamlanır, kuyruğa girmez, dolayısıyla worker sayısı onların
yolunu değiştirmez. Kuyruğa giren iş ise her worker'ın **aynı satırı claim etme
girişimiyle** yarışır; ölçülecek şey odur. Her worker process'i kendi PID'iyle
ayrı raporlanır ve hangi job'ı hangi worker'ın çalıştırdığı kaydedilir — “toplam
throughput arttı” demek yetmez, işin gerçekten dağıldığı gösterilir. Tek bir
worker tüm işi alıyorsa bu bir bulgudur ve öyle yazılır.

Çekişmenin doğruluk tarafı hızdan ayrı doğrulanır: kabul edilen her job **en az
bir kez** çalışır (`IJobHandler` at-least-once'tır — K-641, `JobHandlerContract`) ve
**eşzamanlı çift yürütme olmaz**. Sağlıklı koşumda beklenen attempt sayısı
manifest'te yazılır; fazladan attempt bir doygunluk/lease bulgusudur, sessizce
başarıya sayılmaz. Lease yenileme sayısı ve süresi ayrıca kaydedilir: uzun
çalışan bir job'ın lease'i yenilenemiyorsa throughput düşmeden önce devralma
başlar ve bu, hızdan önce görülmesi gereken bir sinyaldir.

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
| `workers` | `queued`; **1/2/4 worker process** × tek sabit concurrency × 3 tekrar | Açık komutla; HTTP host tek kalır, worker sayısı **ayrı eksendir** — sweep basamaklarıyla çaprazlanmaz |
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

`workers` profilinde sabit concurrency **sweep'ten seçilir**: `queued` senaryosunun
tek worker ile backlog biriktirmeden tamamladığı en yüksek basamak. Seçim raporda
gerekçelendirilir; keyfî bir sayı kullanılmaz. Worker sayısı arttıkça **istek yükü
sabit tutulur** — amaç daha çok iş göndermek değil, aynı işin kaç worker arasında
nasıl dağıldığını görmektir. Her hücre yine izole schema ve temiz process ile
başlar; worker process'leri ölçüm penceresinden **önce** ayağa kalkar ve hazır
olduklarını kuyruğa abone olarak gösterir, açılış yarışı pencereye girmez
(Faz 157'nin devir notu bunu açıkça ister).

Worker process'leri host'un kendi job worker'ına **ek** değil, onun **yerine**
sayılır: HTTP host'un dahilî worker'ı `workers` profilinde kapatılır
(`Scheduling.RunWorker = false`), yoksa "1 worker" hücresi aslında iki worker
olur ve tüm eksen kayar. Etkin değer manifest'te yazılır.

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
| **Write amplification** | Ölçüm penceresinde tamamlanan run **başına** yazılan satır ve bayt, **tablo tablo** (`runs`, `run_events`, `tool_invocations`, `jobs`, `traces`, …). Hücrenin izole schema'sında pencere öncesi/sonrası `pg_total_relation_size` ve satır sayısı farkından hesaplanır; index ve TOAST payı gövdeden **ayrı** gösterilir. Tek satır "N KB/run" yetmez — hangi tablonun büyüdüğü kapasite kararını değiştirir |
| **Storage growth** | Aynı farktan türetilen run başına toplam bayt ve event başına bayt; üç tekrarın değişkenliğiyle. Projeksiyon (ör. 1M run) **yalnız ölçülen aralığın doğrusal uzantısı olarak** ve öyle etiketlenerek verilir; retention kapalıyken ölçülür ve bu manifest'e yazılır — retention açıkken sayı **başka bir sorunun** cevabıdır |
| **Worker çekişmesi** (`workers`) | Worker başına PID, claim denemesi, başarılı claim, **çarpışan claim** oranı, çalıştırılan job sayısı ve dağılımı; lease yenileme sayısı/süresi; devralma olayı; toplam attempt sayısının kabul edilen job sayısına oranı. Throughput worker sayısına karşı ayrı seri olarak verilir, tek bir "ölçeklenir/ölçeklenmez" cümlesine indirgenmez |
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

🚨 **Write amplification ölçümünün kendi tuzağı vardır ve raporda yazılır.**
`pg_total_relation_size` farkı yalnız mantıksal veriyi değil **bloat'ı** da
sayar; autovacuum ölçüm penceresi içinde çalışırsa aynı yük için farklı sayı
çıkar. Üç önlem zorunludur: hücre başına taze schema (zaten planda var),
pencere öncesi ve sonrası autovacuum/`n_dead_tup` durumunun kaydı, ve ölçümün
**pencere sonunda drain bittikten sonra** alınması. Autovacuum pencerede
çalıştıysa hücre `storage/vacuum-interference` ile işaretlenir ve sayı temiz
hücrelerle aynı tabloda ortalanmaz. Satır sayısı farkı bloat'tan etkilenmez;
bayt ile satır **birlikte** raporlanır, çünkü ikisi ayrışırsa sebebi budur.
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
Siteye ham büyük artifact ve secret girmez. Ayrıntılı koşum komutları
`bench/capacity/README.md`. Repo private olduğu sürece tüketiciyi erişemeyeceği
bir script bağlantısına yönlendirme.

**Yayımlanan tablo (👤 kullanıcı kararı, 2026-09-14).** `production.md` yalnız
kapsamı değil, **tek bir referans ortamda ölçülmüş sayıları** da taşır. Sınırları
plana yazılıdır:

- **Tek ortam.** Bir makine, bir PostgreSQL sürümü, bir configuration. Tablonun
  başlığı ortamı söyler; manifest'in tamamı siteye değil repo'ya gider, sayfa
  ona referans verir.
- **Ne yayımlanır:** üç senaryonun tamamlanan basamakları için concurrency,
  n, p50/p95, throughput · run başına write amplification (tablo kırılımıyla) ·
  run başına storage · `workers` profilinin worker sayısı–throughput serisi.
- **Ne yayımlanmaz:** yarım kalan hücreler tamamlanmış gibi, tek tekrarın sayısı
  ortalama gibi, düşük örnekli percentile uyarısız. Eksik ölçüm sıfırla dolmaz.
- **Çerçeve zorunludur.** Her tablo "bu ortamda ölçüldü, **SLA veya garanti
  kapasite değildir**" cümlesini taşır. Karşılaştırma davetiyesi verilmez:
  başka ürünlerle kıyas, hedef sayı ve "yeterli/hızlı" nitelemesi yazılmaz.
- **Bayatlama sözleşmesi.** Tablo hangi Tracon sürümünde ve hangi commit'te
  ölçüldüğünü yazar. Sayı sevk edilen bir iddiadır; ölçüm yenilenmeden sürüm
  satırı güncellenmez. Bu, `bagimlilik_surum_damgasi` kapısının davranış
  iddiaları için kurduğu disiplinin aynısıdır (F-171).

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
| `bench/capacity/Tracon.CapacityHost/` | Paket tüketen host; test provider, kod tool'u, telemetry ve seed modu. **`workers` profili için worker-only mod** (`Scheduling.RunWorker` açık, `MapTracon` yok) — ikinci bir host projesi yazılmaz, aynı host iki modda çalışır |
| `bench/capacity/Tracon.CapacityDriver/` | Ayrı HTTP driver; üç senaryo, bounded yük, SSE reader, rapor üretimi |
| `bench/capacity/Directory.Build.props` | Yalnız apparatus ayarları; geçici dizine taşındığında bağımsız build/restore |
| `scripts/capacity.py`, `scripts/capacity_test.py` | Pack/restore/process/cleanup orchestration ve script hata yolları |
| `scripts/kapi.py`, `scripts/kapi_test.py` | `kapasite` alt komutu; standart kapanışa ağır koşum eklemez |
| `tests/Tracon.Capacity.Tests/` | Normal suite'te çalışan histogram/sayaç/profil testleri; Docker veya yeni pack istemez |
| `bench/capacity/Tracon.Capacity.Acceptance/` | Kısa packed-host HTTP/store ve process hata testleri; yalnız `kapasite --profil smoke` pack/restore sonrasında çağırır |
| `tests/Shared/Infrastructure/ProcessRunner.cs`, `ManagedProcess.cs`, `WorkerProcessHost.cs` | Var olan altyapıyı link'le; gerekli düzeltme dışında kopya üretme. `workers` profilinin N worker process'i **bu üçünün üstünde** kurulur — Faz 157 bunları tam da öldürülebilir uzun ömürlü host için yazdı |
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
| Host'un dahilî worker'ı açık kaldığı için "1 worker" aslında 2 olur; tüm eksen kayar | Process + packed HTTP | `CapacityWorkerAxisTests`: `workers` profilinde host'un etkin `Scheduling:RunWorker` değeri **çalışma anında** okunur ve manifest'e yazılır; `true` ise profil başlamaz. Kaydedilen worker PID sayısı beklenen sayıya eşittir |
| Worker sayısı artınca throughput artmış görünür, ama işi tek worker yapar | Process + gerçek SQL | `CapacityWorkerDistributionTests`: her job'ın hangi worker PID'inde çalıştığı kaydedilir; dağılım raporlanır. Tek worker'ın payı eşiği aşarsa koşum "çekişme ölçülemedi" diye işaretlenir, ölçeklenme iddia edilmez |
| Aynı job iki worker'da **eşzamanlı** çalışır (lease kusuru) | Process + gerçek SQL | `CapacityLeaseOverlapTests`: attempt kayıtlarında aynı job için örtüşen çalışma aralığı aranır. At-least-once fazladan attempt'e izin verir (K-641), **örtüşmeye izin vermez** — örtüşme koşumu `invalid` yapar |
| Write amplification bloat veya autovacuum yüzünden yanlış ölçülür | Gerçek SQL | `CapacityStorageAccountingTests`: bilinen satır sayısı yazılan izole schema'da beklenen satır farkı doğrulanır; pencerede autovacuum çalıştıysa hücre `storage/vacuum-interference` işaretlenir ve ortalamaya girmez |

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
| 8 | `kapasite --profil workers` | 1/2/4 worker için throughput serisi, claim çarpışma oranı ve job'ın worker'lara dağılımı görünür; hiçbir job eşzamanlı iki worker'da çalışmaz; host'un dahilî worker'ı kapalı olarak manifest'e yazılmıştır |
| 9 | Bir `sweep` hücresinin write amplification çıktısını incele | Tablo kırılımı (`runs`/`run_events`/`jobs`/…), index–gövde ayrımı, satır **ve** bayt birlikte; autovacuum penceresi işaretliyse hücre ortalamaya girmemiştir |
| 10 | Yayımlanacak `production.md` tablosunu koşum çıktısından üret | Tablo ortamı, Tracon sürümünü ve commit'i yazar; "SLA değildir" çerçevesi vardır; yarım hücre ve düşük örnekli percentile yayımlanmamıştır |

## Açık Sorular

Planı bloklayan soru yok. Kayıt ve 30 dakika için önerilen varsayılanlar
kullanıldı; farklı kullanıcı yönlendirmesi gelirse uygulama başlamadan güncellenir.

**2026-09-14 revizyonunda iki soru soruldu ve ikisi de kullanıcı tarafından
cevaplandı** (👤), bu yüzden açık değildirler:

1. **Multi-node lease davranışı fazın kapsamına girsin mi?** → *Girsin, dar
   kapsamlı.* `queued` senaryosuna tek eksenli worker-sayısı boyutu eklendi;
   sweep basamaklarıyla çaprazlanmaz, birden çok makine kapsam dışı kalır.
2. **Ölçülen sayılar siteye yayımlansın mı?** → *Evet, tek ortamlı temsilî
   tablo.* Çerçeve, bayatlama sözleşmesi ve ne yayımlanmayacağı §166.6'da yazılı.
Npgsql/runtime instrument erişimi §166.5'teki ilk probda ölçülecek teknik
belirsizliktir. Ölçülemeyen alan için yalan sıfır veya tahminî isim kullanılmaz.

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

## Riskler

| Risk | Önlem |
|---|---|
| M1 Pro/16 GiB üzerinde host+driver+DB birbirini sınırlar | Ayrı PID/container ölçümleri, kaynak cap'leri; sonuç yalnız bu topoloji için yorumlanır |
| Telemetry'nin kendisi latency veya allocation ekler | Collector ayarlarını sabitle ve manifest'e yaz; gerektiğinde aynı fixture ile açık/kapalı kontrol ölçümü |
| Büyüyen veri her tekrarın koşulunu değiştirir | Her hücreye aynı seed ve yeni schema; gerçek başlangıç boyutu |
| İstemci daha önce doyar | Driver CPU/RSS, dispatch lag ve notSent görünür; server kapasitesi diye raporlanmaz |
| Uzun koşum sınırsız log/event dosyası üretir | Bounded writer, disk bütçesi, partial rapor ve ölçüm dışı temizlik |
| Örnek sayısı az veya doygunluk hiç bulunmaz | Eksik güven saklanmaz; ölçülen üst basamak garanti kapasite diye sunulmaz |
| Dört worker aynı makinede **CPU** için yarışır; ölçülen şey lease çekişmesi değil makine doygunluğu olur | Worker başına CPU/RSS ayrı raporlanır. Makine doyduysa sonuç "lease çekişmesi ölçülemedi, makine doydu" diye yazılır — ölçeklenme iddiası da, ölçeklenememe iddiası da yapılmaz |
| Worker ölçümü "çok node destekleniyor" diye okunur | K-739 fazın DoD'sinde; rapor ve site metni tek makine/tek DB/tek saat sınırını yazar. SQLite tek process tavsiyesi değişmez |
| Yayımlanan sayı bayatlar ve sevk edilmiş yanlış bir iddiaya döner | Tablo sürüm ve commit taşır; ölçüm yenilenmeden sürüm satırı güncellenmez. Aynı disiplinin kapısı F-171'de kuruldu |
| Yayımlanan sayı karşılaştırma daveti üretir | Tablo hedef sayı, kıyas ve "yeterli/hızlı" nitelemesi taşımaz; çerçeve cümlesi zorunludur ve site kapısında kontrol edilir |

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

## Gerçekleşen Public API

**Değişiklik yok.** Ne yeni public üye, ne değişen imza, ne yeni HTTP ucu, ne
migration. 17 `PublicAPI.Shipped.txt` dosyasının tamamı fazın başındaki hâlinde;
`PublicAPI.Unshipped.txt` dosyalarına tek satır eklenmedi. Tüketicinin
bağımlılık grafiğine **0 paket**, arayüz paketine **0 KB** eklendi.

Bu fazın ürettiği her şey `bench/capacity/`, `tests/Tracon.Capacity.Tests/` ve
`scripts/` altında **paketlenmeyen geliştirme aparatıdır**
(`IsPackable=false`). Ölçülen host Tracon'i `PackageReference` ile tüketir;
`src/` ağacına hiçbir yerden bağlanmaz.

## Dosya Listesi (gerçekleşen)

| Yol | İş |
|---|---|
| `bench/capacity/Directory.Build.props` · `Directory.Packages.props` | Repo mirasından kesilmiş bağımsız build sınırı; ikincisinin tek işi aramayı durdurmak (Sapma 4) |
| `bench/capacity/Shared/CapacityContract.cs` · `CapacityPayload.cs` · `CapacityExecutionLog.cs` | Üç projeye **link'lenen** ad/yük/kayıt sözleşmesi (kopya değil, K-411) |
| `bench/capacity/Tracon.CapacityHost/` (8 dosya) | Ölçülen host: `api` · `worker` · `seed` · `migrate` modları, deterministik sağlayıcı, kod tool'u, aparat ucu |
| `bench/capacity/Tracon.CapacityDriver/` (16 dosya) | HTTP + salt-okunur SQL sürücüsü; hücre koşumu ve rapor üretimi |
| `bench/capacity/Tracon.Capacity.Acceptance/` (7 dosya) | Packed host'a karşı 27 kabul testi; yalnız `smoke` koşar |
| `bench/capacity/profiles/*.json` (5) · `README.md` | Beş yük şekli ve aparatın kendi anlatısı |
| `scripts/capacity.py` · `scripts/capacity_test.py` | Pack/izole tüketici/veritabanı/process orkestrasyonu · 22 test |
| `scripts/kapi.py` · `scripts/kapi_test.py` | `kapasite` alt komutu + `SYNTHETIC-CREDENTIAL` işaretli istisna mekanizması · 3+4 yeni test |
| `tests/Tracon.Capacity.Tests/` (9 dosya) | Normal suite'in keşfettiği tek kapasite projesi — 89 test |
| `Tracon.slnx` · `Tracon.no-docker.slnf` · `.github/workflows/ci.yml` | Test keşfi + Linux'a özel packed smoke adımı |
| `docs-site/src/content/docs/guides/production.md` | Ölçülen sayılar, çerçevesi ve bayatlama sözleşmesi |
| `docs/manuel-test/36-GELISTIRME-KAPILARI.md` · `00-INDEKS.md` | `MT-GDK-039`…`048` |
| `docs/hafiza/kapasite-olcumu.md` (yeni) · `analyzer-tanilari.md` · `00-INDEKS.md` | Bedeli ödenmiş tuzaklar |
| `.agents/ortak/kapilar.md` | "Kapı OLMAYAN komut" bölümü |

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
