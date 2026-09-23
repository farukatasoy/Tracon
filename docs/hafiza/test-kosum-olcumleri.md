# Test Kosum Olcumleri — Kapanis Kapisi Taban Cizgisi

> Kapanis kapilarinin (build/test/pack/format/site) wall-clock ve proje-basina
> sonuc kaydi. Tuzak/ogrenilen ders icin: [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md).
>
> Faz 101'de ayrildi: `test-kosum-tuzaklari.md` 16.397/16.000 B'ye ulasmisti.
> Bu tablolar tarihsel olcumdur, aktif tuzak degildir — ayri dosyaya tasindi.

## Faz 91 taban ölçümü — 2026-08-23

Ölçümler macOS/Apple Silicon üzerinde, temizleme sonrası ve tüm MSBuild alt
süreçlerinde `MSBUILDDISABLENODEREUSE=1` ile alındı. Süreler wall-clock'tur.

| Kapı | Taban sonucu |
|---|---:|
| `dotnet build` — frontend açık, soğuk | 58,60 s · 0 warning · 0 error |
| `dotnet build` — frontend açık, sıcak | 6,05 s |
| `dotnet build` — frontend kapalı, soğuk | 52,88 s |
| `dotnet build` — frontend kapalı, sıcak | 6,09 s |
| Tam `dotnet test Tracon.slnx --no-build` | 164,43 s · exit 1 |
| `dotnet pack --no-build` | 6,62 s · exit 0 |
| `dotnet format --verify-no-changes --no-restore` | 76,47 s · exit 0; workspace yükleme uyarısı yazdı |
| `docs-site` `npm run check` | 9,93 s · exit 1; yerel Astro koşumu Node 20.19.4'ü, Astro'nun istediği `>=22.12.0` yerine gördü |
| Kapanış kapıları toplamı (yukarıdaki beş ayrı wall-clock koşum) | 316,05 s |

Tam testin 19 proje özeti:

| Proje | Süre | Sonuç |
|---|---:|---|
| `Anthropic.UnitTests` | 0,244 s | 44/44 |
| `Azure.UnitTests` | 0,222 s | 52/52 |
| `Client.UnitTests` | 0,149 s | 3/3 |
| `Google.UnitTests` | 0,369 s | 49/49 |
| `Mcp.UnitTests` | 0,745 s | 30/30 |
| `OpenAI.UnitTests` | 0,569 s | 87/87 |
| `Embedded.Tests` | 5,717 s | 3/3 |
| `Core.UnitTests` | 6,499 s | 1801/1801 |
| `Cli.FunctionalTests` | 8,575 s | 15/15 |
| `Generators.UnitTests` | 6,742 s | 156/156 |
| `Voice.UnitTests` | 0,764 s | 43/43 |
| `Workflows.UnitTests` | 3,504 s | 102/102 |
| `Testing.UnitTests` | 12,152 s | 32/32 |
| `Ui.E2ETests` | 29,811 s | 0/57 — yerel koşum altyapısı kırmızı |
| `Sqlite.IntegrationTests` | 65,127 s | 591/592 — bir test kırmızı |
| `PostgreSql.IntegrationTests` | 94,461 s | 638/638 |
| `Templates.Tests` | 105,001 s | 32/32 |
| `AspNetCore.FunctionalTests` | 155,219 s | 647/647 |
| `SqlServer.IntegrationTests` | 156,919 s | 574/574 |

Bu tablo ürün kusuru iddiası değildir. `Ui.E2ETests` için F-130 kırılganlık sınıfı
zaten üç kez kanıtlanmıştır. SQLite kırmızı yolu Faz 91 değişiklikleriyle
nedensel olarak ilişkili değildir; Faz 91 sonu aynı test ikinci kez izole ve
tam koşumla ayrıştırılmalıdır.

## Faz 91 sonrası ölçümü — 2026-08-23

Aynı makinede, aynı `MSBUILDDISABLENODEREUSE=1` koşuluyla tekrarlandı. Temiz
build için `dotnet clean` sonrası ölçüm alındı. Bu kez Docker ve Playwright
altyapısı hazırdı; tam test paketi 19 projenin tamamında yeşil döndü.

| Kapı | Sonuç |
|---|---:|
| `dotnet build` — frontend açık, soğuk | 49,66 s · 0 warning · 0 error |
| `dotnet build` — frontend açık, sıcak | 6,09 s · `npm run build` koşmadı |
| `dotnet build` — frontend kapalı, soğuk | 48,58 s · 0 warning · 0 error |
| `dotnet build` — frontend kapalı, sıcak | 6,50 s · 0 warning · 0 error |
| Tam `dotnet test Tracon.slnx --no-build` | 173,13 s · exit 0 |
| `dotnet pack --no-build` | 5,50 s · exit 0 |
| `dotnet format --verify-no-changes --no-restore` | 76,33 s · exit 0; workspace yükleme uyarısı yazdı |
| `docs-site` `npm run check` — default PATH | 9,77 s · exit 1; Node 20.19.4 < Astro gereksinimi `>=22.12.0` |
| `docs-site` `npm run check` — CI uyumlu Node 22.23.2 | 23,36 s · exit 0; dört alt kapı yeşil |
| Kapanış kapıları toplamı (soğuk frontend-açık build + test + pack + format + geçen Node 22 site) | 327,98 s |

Tam testin 19 proje özeti:

| Proje | Süre | Sonuç |
|---|---:|---|
| `Anthropic.UnitTests` | 0,239 s | 44/44 |
| `Azure.UnitTests` | 0,290 s | 52/52 |
| `Client.UnitTests` | 0,193 s | 3/3 |
| `Google.UnitTests` | 0,428 s | 49/49 |
| `Mcp.UnitTests` | 0,859 s | 30/30 |
| `OpenAI.UnitTests` | 0,475 s | 87/87 |
| `Embedded.Tests` | 7,950 s | 3/3 |
| `Core.UnitTests` | 7,450 s | 1801/1801 |
| `Cli.FunctionalTests` | 13,885 s | 15/15 |
| `Generators.UnitTests` | 12,663 s | 156/156 |
| `Voice.UnitTests` | 2,631 s | 43/43 |
| `Workflows.UnitTests` | 4,504 s | 102/102 |
| `Testing.UnitTests` | 14,913 s | 32/32 |
| `Ui.E2ETests` | 133,156 s | 57/57 |
| `Sqlite.IntegrationTests` | 75,739 s | 592/592 |
| `PostgreSql.IntegrationTests` | 104,481 s | 638/638 |
| `Templates.Tests` | 115,698 s | 32/32 |
| `AspNetCore.FunctionalTests` | 164,438 s | 647/647 |
| `SqlServer.IntegrationTests` | 166,444 s | 574/574 |

Karşılaştırma: frontend-açık soğuk build 8,94 s hızlandı; sıcak build iki
koşumda da `npm run build` çalıştırmadı. Test süresi 8,70 s arttı; bu nedenle
tam testte iyileşme iddia edilmez. Default PATH ile site kapısı kırmızı olsa da
CI uyumlu Node 22.23.2 ile dört alt kapının tamamı yeşildir. Geçen kapanış
toplamı 327,98 s'dir; baseline'daki site ölçümü Node 20 yüzünden yarıda kaldığı
için bu toplamlarla doğrudan hız yüzdesi çıkarılmaz.

### Faz 91 kapanış tekrarında F-130 ayrıştırması — 2026-08-23

Kapanış kapılarının ikinci tekrarında tam suite `Tracon.Ui.E2ETests`
56/57 ile kırmızı oldu: önce `Pending_request_card_can_be_answered`, ayrı
UI koşumunda `Runs_button_on_session_page_navigates_to_filtered_list`.
İki case de derlenmiş test ikilisinde `--filter-method` ile izole 1/1 geçti.
Bu, ürün kusuru değil; tam Playwright suite'inin kaynak/zamanlama çekişmesine
duyarlı F-130 sınıfının yeni kanıtıdır. İlk tam kapanış koşumu 57/57 ve 19
proje ile yeşildi. Kapanış raporu tekrarlanabilirliği korumak için son kırmızı
çıkış kodunu saklamaz; izole koşum ayrıştırma adımıdır.

## Faz 116 — beşinci (yol tetiklemeli) kapı: `kapi.py performans` — 2026-08-27

Apple M1 Pro, macOS. Üç benchmark (`RunEventWriterBenchmarks.AppendEvent`,
`CompiledAgentCacheBenchmarks.CacheHit`, `RunStoreQueryBenchmarks.QueryRuns`),
BenchmarkDotNet varsayılan `Job` (pilot + 15 iterasyon).

| Koşum | Süre |
|---|---:|
| `dotnet run -c Release --project bench/Tracon.Benchmarks -- --filter * --exporters json` (tek başına) | ~83–91 s |
| `python3 scripts/kapi.py performans` (build + koşum + karşılaştırma) | ~85–92 s |

Sıcak yol dosyası değişmediğinde adım tamamen **atlanır** (116.4); yukarıdaki
süre yalnız değiştiğinde ödenir. Kapanışın geri kalan dört kapısına (build,
tam test, pack, format) **eklenen** maliyet budur — Faz 91/91-sonrası
tablolarındaki toplamlar bu adımı içermez.

Taban çizgisi (`bench/baseline.json`, aynı makine): `CacheHit` 24 B,
`AppendEvent` 104 B, `QueryRuns` 44336 B (200 satır seed, `Take=50`).
`CompiledAgentCache.GetOrAdd`'ın kendisi düzeltilmeden önce `CacheHit` 88 B
ölçülüyordu — bkz. [`cekirdek-calistirma.md`](cekirdek-calistirma.md).

### 🚨 Taban çizgisi KODDAN bağımsız kayabilir — güncellemeden önce baseline commit'i ölç (2026-09-04, Faz 142)

`kapi.py performans` `AppendEvent` için 104 B → 112 B kırmızısı verdi;
`RunEventWriter.cs`'in tek diff'i `CompleteAsync` içindeydi, `AppendAsync`'e
HİÇ dokunmamıştı. Şüpheli: `git worktree add` ile taban commit'i (`381285c4`)
AYRI bir dizinde derlenip TEK BAŞINA ölçüldü — sonuç yine 112 B. Aynı kodun
2026-09-02 ölçümü 104 B'ydi; makine aynı (Apple M1 Pro), yalnız iki gün
içinde OS/SDK yama düzeyi kaymış olabilir (`kaynak tespit edilemedi` —
`dotnet --info` sürüm numarası değişmedi, muhtemel neden alt seviyede).
Sonuç: kod suçsuzdu, taban çizgisi bayatlamıştı. `--guncelle` çalıştırılmadan
önce **taban commit'i ayrı bir `git worktree`'de tek başına ölçmek** kodun mu
ortamın mı sorumlu olduğunu ayırt eder — doğrudan `--guncelle` çalıştırmak bu
ayrımı **atlar** ve gerçek bir regresyonu maskeleyebilirdi.

## Faz 178 — 🚨 `Barrier(N)` + `Task.Run`, N çekirdek sayısını aşınca kapıya ~70 sn ekler

**Ölçüldü (2026-09-16, 10 çekirdekli Apple Silicon).** Dört sağlayıcı birim test
projesi tam koşumda 16–43 sn sürüyordu; aynı büyüklükteki `Voice.UnitTests`
0,19 sn'de bitiyordu. TRX döküm tek sebebi gösterdi: **üç test 69,7 sn'nin
69,4'ünü** yiyordu, üçü de `Concurrent_*` adlı sözleşme testleriydi ve üçü de
aynı deseni taşıyordu:

```csharp
using var start = new Barrier(32);
await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => { start.SignalAndWait(); … })));
```

**Kök sebep bir kilitlenme değil, thread pool'un ENJEKSİYON HIZI.** Barrier her
katılımcıyı sonuncusu gelene kadar bekletir, yani havuz 32'sini **aynı anda**
tutmak zorundadır. Talep havuzun minimumunu (≈ çekirdek sayısı) aşınca .NET yeni
thread'i **saniyede ~bir** ekler; test o rampayı uyuyarak geçirir. 32 − 10 = 22
thread ≈ 22 sn — ölçülen süre tam buydu.

| Proje | Önce | Sonra |
|---|---:|---:|
| `Anthropic.UnitTests` (79 test) | 21,50 s | **1,12 s** |
| `Google.UnitTests` (84 test) | 17,47 s | **0,99 s** |
| `Azure.UnitTests` (76 test) | 17,69 s | **0,94 s** |
| `OpenAI.UnitTests` (126 test) | 16,12 s | **1,63 s** |

Çözüm havuz değil **gerçek thread**: `SimultaneousCalls.Run`
(`src/Tracon.Testing.Contracts.Xunit/Internal/`). Barrier artık hepsini
gerçekten birlikte bırakır — yani sonda daha HIZLI değil, daha DOĞRU bir
eşzamanlılık probu var; eskiden ilk gelenler sonuncuyu yirmi saniye bekliyordu.

🚨 **İki yan kural:**

- **Sevk edilen bir sözleşmede imzayı koruyun.** `async Task` → `void` yapmak
  `RS0016`/`RS0017` ile public API kapısını kırar. Hız düzeltmesi public yüzeyi
  oynatmaz: gövde senkronlaştı, dönüş tipi `Task` kaldı (`Task.CompletedTask`).
- **Sınıf taraması yapıldı** (`grep -rn "new Barrier(" src/ tests/`): kalan
  katılımcı sayıları 8 · 8 · 3 · 2 · 2 ve `ConcurrentCallCount = 8`. Bu makinede
  (10 çekirdek) hiçbiri stall etmiyor — ama **8, dört çekirdekli bir CI'da aynı
  kusurdur**. Yeni bir barrier boyutlandırırken sayıyı çekirdek sayısına göre
  seç, ya da doğrudan `SimultaneousCalls` kullan.

- **`-maxcpucount:1` test PROJELERİNİ serileştirir, proje İÇİNİ etmez.** Ölçüldü
  (2026-09-19): tam koşum boyunca işlem tablosu örneklendiğinde her an **tek**
  test süreci vardı — yani projeler arası çekişme yok. Doygunluk xunit'in proje
  içi paralelliğinden gelir (varsayılan: işlemci başına bir thread). Zaman-duyarlı
  bir proje (gerçek tarayıcı, gerçek zamanlı ses döngüsü, Native AOT `publish`)
  makineyi tek başına doyurabilir; çözüm o projenin `xunit.runner.json`'ında
  `maxParallelThreads`'tir. Bedeli ölçüldü ve küçüktür (fonksiyonel testler
  2 dk 43 sn → 3 dk 07 sn).
- **Kendi koşumunu kendin kirletme.** Tam koşum arka plandayken derleme ya da
  başka bir test koşmak ölçümü geçersizleştirir (ve `--no-build` koşumu
  değişmiş ikilileri çalıştırır). Bu turda bir koşum bu yüzden **iptal edildi**;
  raporlanmadı.

## Tahsis taban çizgisi — 2026-09-19 güncellemesi (Faz 179 kapanışı)

`bench/baseline.json` `44832 → 45040 B` (`RunStoreQueryBenchmarks.QueryRuns`,
+208 B). Güncelleme Faz 116'nın yazdığı yoldan yapıldı
(`kapi.py performans --guncelle`, tek satırlık gözden geçirilebilir diff).

**Artışın sahibi Faz 179 DEĞİL.** Üç ölçüm bunu ayırdı:

| Commit | Ölçüm | Ne söyler |
|---|---:|---|
| `381285c4` (taban çizgisinin kendi tarihi) | 44 832 B | Taban bu makinede **birebir** üretilebiliyor — sapma ölçüm gürültüsü değil |
| `85505780` (Faz 179 **öncesi**) | 45 040 B | Artış faz başlamadan önce oradaydı |
| `HEAD` (Faz 179 dahil) | 45 040 B | Faz 179 tahsis açısından **nötr** |

`git bisect` (8 adım, her adımda derleme + filtreli benchmark) tek suçluyu
buldu: **`bf319755`** — `SqliteRetryingCommand`. Benchmark SQLite üzerinde
koşuyor ve dekoratör komut başına bir sarmalayıcı nesne + retry closure'ı +
async durum makinesi ekliyor. Bu bir kusur değil, bir **doğruluk düzeltmesinin
kabul edilmiş bedelidir**; taban çizgisi bu yüzden güncellendi, kapı
gevşetilmedi.

### 🚨 Asıl ders: kapı 460 commit boyunca hiç koşmadı

`kapi.py kapanis` **ilk kırmızıda durur** ve `performans` listenin onuncu
adımıdır. Doküman kapısı (adım 2) uzun süre kırmızı kaldığı için — kapanmış faz
dokümanı kökte beklerken bu normaldir — sıra performans adımına hiç gelmedi ve
gerçek bir gerileme o pencerede sessizce içeri girdi.

Bunun sonuçları, bir dahaki sefere:

- **Kapanış kapısının yeşili, "her adım koştu" demek değildir.** Hangi adımların
  koştuğunu görmek için çıktıdaki `$ ...` satırlarını say; eksik adım varsa kapı
  o adım hakkında hiçbir şey söylememiştir.
- **Sapmayı ararken önce taban çizgisinin kendi commit'ini ölç.** Taban orada
  üretilemiyorsa sorun koddan önce ortamdadır (SDK, makine) ve bisect boşa gider.
  Burada birebir üretildi, bu yüzden bisect meşruydu.
- **Bisect ölçümü `kapi.py performans`'ın tamamıyla yapılmaz** — üç benchmark'ın
  üçünü de koşar. Tek benchmark'ı `--filter '*QueryRuns*' --exporters json` ile
  koşup `Memory.BytesAllocatedPerOperation` okumak adım başına ~2 dakika kazandırır.

### Ölçmeden "optimizasyon" yazma — bu turda bir kez olundu

`AmbientTenantScope.Normalize`'ın `ToLowerInvariant()`'ı "zaten kanonik değerde
de kopya üretiyor" varsayımıyla bir hızlı yol yazıldı, sonra ölçüldü:

```
zaten kanonik girdi : ToLowerInvariant 0,0 B/call · hızlı yol 0,0 B/call
katlanması gereken  : ToLowerInvariant 48,0 B/call · hızlı yol 48,0 B/call
```

**.NET'in kendisi zaten aynı örneği geri veriyor.** Hızlı yol tamamen gereksizdi
ve geri alındı. Ölçüm `GC.GetAllocatedBytesForCurrentThread()` ile yapıldı;
🚨 sonucu tüketmezsen (`sink += ...Length`) JIT çağrıyı tamamen eler ve her iki
taraf da yanıltıcı biçimde `0 B` görünür.

## CI iş süreleri ve `timeout-minutes` — 2026-09-23

Kaynak: GitHub Actions API (anonim, public repo), 16 koşum (09-06…09-21).
Sınır = en uzun × ~2, 5 dk'ya yukarı; 1 dk altındaki işte taban 10 dk.

| İş / adım | En uzun (dk) | Sınır |
|---|---:|---:|
| `build` windows · ubuntu | 49,1 · 37,9 | 100 |
| Test adımı ubuntu (Faz 183 öncesi) | 24,6 | 60 |
| Test adımı windows | 30,9 | 65 |
| Kapasite smoke | 4,2 | 10 |
| `pack` · `release-dryrun` · `site` | 7,4 · 5,7 · 7,9 | 15 · 15 · 20 |
| `publish` · `npm-publish` · `github-release` | 0,5 · 0,3 · 0,1 | 10 |

🚨 `ci.yml` iş iş büyüdü ve hiçbir kapı sınır istemedi: 233 koşum boyunca her iş
GitHub'ın 360 dk varsayılanını taşıdı. Kapı artık `zaman_siniri_olmayan_isler`
(`dokuman-bakim.py --denetle`). Ubuntu test sınırı Faz 183'ün üç TFM bacağını
tahminle kapsar (yerel 782 → 910 sn); bacaklı ilk CI koşumunda yeniden ölç.
