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
