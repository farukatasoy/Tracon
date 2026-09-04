# Süreç Denetimi — Kaçış Ölçümü ve Zincir Revizyonu (2026-09-04)

> Bu bir faz kaydı değildir. Geliştirme **sürecinin kendisini** ölçen bir
> denetim turudur: kusurlar nerede doğuyor, nerede bulunuyor, hangi kapı
> yakalamalıydı ve zincir bunun karşılığında ne kadar bağlam yiyor.
>
> Kural: **kanıtsız iddia yok.** Her sayı bir komuttan gelir. Reddedilen
> öneriler de yazılıdır — sonraki oturum aynı fikri yeniden önermesin.
>
> Ölçüm tabanı: `e599259f` · macOS/Apple M1 Pro · Docker ayakta.

---

## 0. Yönetici özeti

| Soru | Ölçülen cevap |
|---|---|
| Kaçış oranı düşüyor mu? | **Evet, 15 kat.** Doğum dönemine göre kaçan kusur/faz: **0,545 → 0,097 → 0,035** (§A5) |
| Kusurlar hâlâ nereden kaçıyor? | Baskın sınıf **kalmadı**; zincir dönemindeki 19 kaçışta Pareto düz (§B3) |
| En pahalı kanal hangisi? | **Tüketici turu** — 19 kaçışın 5'i (%26) tüketiciye ulaştı; **beşi de sevk edilen yüzey** (doküman · örnek · paket) (§B4) |
| `faz-denetim` işe yarıyor mu? | **Evet.** Faz 115-143'te faz başına **2,2** aksiyon alınan bulgu, **0,62 🔴/faz** (§A6) |
| Zincir gereksiz tekrar taşıyor mu? | **Hayır.** Dokuz dosyada 7 birebir tekrar satırı; Faz 91/92 birleştirmesi tutmuş (§A4) |
| Bu denetim kusur buldu mu? | **İki, ikisi de canlı.** KUSUR-A1: sevk edilen örnek "altı genişleme noktası" diyor, kod yedi bildiriyor (sınıf 6'nın 3. tekrarı). KUSUR-A2: F-180 "✅ KAPANDI" işaretli, taban koşumda aynı imzayla yine düştü (§B5) |
| Kapanış süresi düştü mü? | **Hayır — iki büyük kaldıraç ölçülüp REDDEDİLDİ** (§C4, §C5); ikisi de sinyal kaybediyordu. Kazanç koşum başına değil **olay başına**: §C3 |
| Kapanış nereye gidiyor? | 758,8 sn/koşum (`performans` tetiklendiğinde; tetiklenmeyince 656 sn — §C7); **%70,6'sı tek adım** — tam test (§A2) |

---

## A. Ölçüm

### A0. Görev metnindeki ön ölçümün düzeltilmesi

| Ön ölçüm | Ölçülen | Komut |
|---|---|---|
| "29 test projesi" | **21** (`AgentPrism.slnx`) + `slnx` dışı 6 `samples/*.Tests` | `grep -oE 'Path="[^"]+\.csproj"' AgentPrism.slnx` |
| "~3747 `[Fact]`/`[Theory]`" | **3245** (`tests/` altında) | `grep -rho '\[Fact\]\|\[Theory\]' tests/ --include='*.cs' \| wc -l` |
| "609 commit, ~44'ü `fix`" | 609 commit ✅ · `fix`+`kusur`+`security` = **44** ✅ | `git log --grep` |

`samples/*.Tests` çözümde **değildir**; yalnız `kapi.py yayin` (yayın provası)
onları paketlenmiş sürüme karşı koşar. Bu sınır §B3'te bir kaçışın nedenidir.

### A1. Kusur envanteri — 74 kök neden

Kaynak: 44 `fix`/`kusur`/`security` commit'i · 2026-08-13 manuel kabul turunun
A–W aileleri (`docs/arsiv/manuel-test-kosum-2026-08/KAPANIS-PLANI.md`) ·
10 başlıklı güvenlik taraması (`docs/guvenlik-tarama/`) · üç tüketici turu ·
`kusur-giderme` ile kapanan F-180 · F-181 · F-190.

Sayım **kök neden** düzeyindedir, case düzeyinde değil (bir aile 10 bağımsız
kök neden taşıyabiliyor — örn. Aile U).

**Kaçış mesafesi**, her `fix` commit'inin sildiği satırların ebeveyn commit'te
`git blame --line-porcelain` ile doğum commit'ine bağlanmasıyla ölçüldü
(44 commit'in 33'ünde doğum bulundu; kalan 11'i yalnız test/doküman/betik
dokunuyor ve `src/` satırı silmiyor).

| Doğum dönemi → bulunuş dönemi | n | ort. mesafe (commit) | medyan | max |
|---|---:|---:|---:|---:|
| E1 → E1 | 26 | 147,8 | 148 | 185 |
| E1 → E2 | 2 | 281,5 | 306 | 306 |
| E1 → E3 | 2 | 300,0 | 351 | 351 |
| E2 → E2 | 1 | 29,0 | 29 | 29 |
| E2 → E3 | 2 | 192,0 | 212 | 212 |
| **E3 → E3** | **2** | **12,5** | **19** | **19** |

Dönem sınırları `.agents/` dosyalarının doğuş tarihidir
(`git log --diff-filter=A`):

| Dönem | Aralık | Ne yürürlükteydi |
|---|---|---|
| **E1** | 2026-08-01 → 08-15 | yalnız `faz-tamamlama` (08-02) · `faz-baslangic` (08-03) · `faz-planlama` (08-06) |
| **E2** | 08-16 → 08-22 | + `faz-denetim` · `faz-uygulama` · `kusur-giderme` (üçü de 08-16) |
| **E3** | 08-23 → 09-04 | + `scripts/kapi.py` · `.agents/ortak/kapilar.md` · `test-seviyeleri.md` (Faz 91/92) |

### A2. Kapı süre profili — ön ölçüm DOĞRULANDI

`artifacts/kapi-olcum.jsonl` (55 kayıt, 2026-09-04) yeniden toplandı; görev
metnindeki tablonun her satırı birebir tuttu.

| Komut | Koşum | Toplam sn | Koşum başına | pay |
|---|---:|---:|---:|---:|
| `dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1` | 5 | 2677,7 | **535,5** | **%70,6** |
| `dotnet format AgentPrism.slnx --verify-no-changes` | 5 | 498,4 | 99,7 | %13,1 |
| `kapi.py performans` (yol tetiklemeli) | 2 | 203,9 | 102,0 | %13,4 |
| `dotnet build AgentPrism.slnx -c Release` | 5 | 200,5 | 40,1 | %5,3 |
| `docs-site npm run check` | 4 | 98,2 | 24,5 | %3,2 |
| `dotnet pack` | 5 | 34,8 | 7,0 | %0,9 |
| `python3 -m unittest discover -s scripts` | 5 | 32,5 | 6,5 | %0,9 |
| `dokuman-bakim.py --denetle` | 5 | 27,2 | 5,4 | %0,7 |
| `kapi.py tarama` | 6 | 18,6 | 3,1 | %0,4 |
| `build-agent-map.mjs --check` · `denetim-paketi.py` | 5+5 | 1,5 | <0,3 | %0,0 |
| **`kapanis` toplamı** | **5** | **3794,2** | **758,8** | |

### A4. Zincir maliyeti

Bir fazın tipik oturumunda yüklenen bağlam (`wc -c`, token ≈ bayt/4):

| Grup | Dosyalar | Bayt | ~token |
|---|---|---:|---:|
| Açılış | `AGENTS.md` · `MEMORY.md` · `faz-baslangic` | 21 073 | ~5 268 |
| Uygulama | `faz-uygulama` · `ortak/test-seviyeleri` | 7 730 | ~1 932 |
| Denetim | `faz-denetim` | 9 237 | ~2 309 |
| Kapanış | `faz-tamamlama` · `references/gerekce` · `ortak/kapilar` | 27 348 | ~6 837 |
| Yüzey değiştiyse | `tuketici-dokuman-senkronu` (+kaynak, +gerekçe) | 23 242 | ~5 810 |
| **Zincir toplamı** | | **88 630** | **~22 157** |
| + faz dokümanı (Faz 120-143 ortalaması) | | 12 975 | ~3 244 |

`dokuman-bakim.py --denetle` bunu üç katmanda ayrı raporluyor: başlangıç
bağlamı **16 438 B (~6 849 token)** · sorgu bağlamı 104 277 B · yönetim
ledger'ı 416 974 B (son ikisi açılışta okunmaz).

**Zincir dışı** skill'ler faz faturasına girmiyor: `nuget-danismani`
(23 226 B) yalnız yayın kararında, `manuel-test-kosumu` (14 560 B) yalnız tam
koşum turunda, `aday-kesfi` (13 100 B) yalnız kullanıcı isteğinde okunur.

**Tekrar ölçümü.** Dokuz zincir dosyası normalize edilip (kod/bağlantı
soyularak) satır bazında karşılaştırıldı: **≥7 kelimelik birebir tekrar
yalnız 7 satır**, üçü bilinçli çapraz referans başlığı. Faz 91/92'nin
`.agents/ortak/`'a taşıma işi tutmuş. **Kolay silme hedefi yoktur** — zincirin
maliyeti gerçek ama içeriği fazlalık değildir.

### A5. Faz maliyeti ve kaçış oranı — ana bulgu

Faz→commit haritası commit konusundan çıkarıldı (`(phase NN)` · `faz NN` ·
`Phase NN:`); 144 fazın **143'ü** eşleşti (Faz 7 hiç uygulanmadı, `⏸ Beklemede`).

**Bulunuş dönemine göre** (yanıltıcı — turlar birikmiş borcu boşaltıyor):

| Dönem | Gün | Faz | fix commit | fix/faz |
|---|---:|---:|---:|---:|
| E1 | 15 | 55 | 30 | 0,55 |
| E2 | 7 | 31 | 4 | 0,13 |
| E3 | 13 | 57 | 12 | 0,21 |

**Doğum dönemine göre** (doğru soru — o dönemin zinciri neyi kaçırdı):

| Kodun doğduğu dönem | Kaçan kusur | O dönemde kapanan faz | **kaçan/faz** |
|---|---:|---:|---:|
| E1 (zincir öncesi) | 30 | 55 | **0,545** |
| E2 (+denetim +uygulama) | 3 | 31 | **0,097** |
| E3 (+`kapi.py` +ortak) | 2 | 57 | **0,035** |

> 🚨 **Yöntemin sınırı, dürüstçe:** (1) E3 kodu genç — en fazla 30 günlük, çoğu
> 10 günden az; sağdan sansürlüdür ve gerçek E3 oranı bundan yüksek çıkacaktır.
> (2) Sayım yalnız `fix`/`kusur`/`security` **etiketli** commit'leri görür; bir
> `feat` commit'i içinde sessizce düzeltilen kusuru göremez. (3) E1'in 26
> kaçışı kendi döneminde bulundu — çünkü 08-13 manuel turu o borcu tek seferde
> boşalttı; yani sansür kısmidir ve yön doğrudur. Eğilim **iddia edilebilir**,
> kesin oran **edilemez**.

### A6. `faz-denetim` sinyal üretiyor mu?

Faz 115-143'ün (n=29) doküman "Denetim Bulguları" tabloları sayıldı — hepsinde
bölüm var:

| Seviye | Toplam | Faz başına |
|---|---:|---:|
| 🔴 kapanmadan faz bitmez | 18 | 0,62 |
| 🟡 aynı fazda kapanır/gerekçelenir | 47 | 1,62 |
| 🟢 aday listesine | 23 | 0,79 |
| **aksiyon alınan (🔴+🟡)** | **65** | **2,24** |

53 bulgu satırı denetim listesinin başlıklarına anahtar kelimeyle atandı
(bir bulgu birden fazla başlığa düşebilir):

| Denetim başlığı | Bulgu |
|---|---:|
| **3.8 Ürün yüzeyi** | **15** |
| 3.4 Kapsanmayan hata yolu | 10 |
| 3.1 DoD ihlali | 8 |
| 3.5 İmza-gövde kayması | 8 |
| 3.7 Repo kuralları | 4 |
| 3.6 Plan dışı public API | 3 |
| 3.2 Test tiyatrosu | 3 |
| 3.3 Yanlış test seviyesi | 2 |
| (sınıflanamadı) | 16 |

**3.8 (ürün yüzeyi) denetimin en verimli başlığıdır** — ve §B3'te göreceğimiz
gibi kaçışların da en pahalı kanalı odur. İkisi aynı yeri gösteriyor.

### A3. Proje başına test süresi — YENİ ölçüm

Taban koşum: `dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1
-- --report-trx`, `MSBUILDDISABLENODEREUSE=1`. Wall-clock **557 sn**; TRX
sürelerinin toplamı 531,4 sn (aradaki 25,6 sn MSBuild düzenleme yüküdür).

| Proje | sn | pay | kümülatif | test | Docker/Playwright |
|---|---:|---:|---:|---:|---|
| `AspNetCore.FunctionalTests` | 105,7 | %19,9 | %19,9 | 767 | **hiçbiri** |
| `Package.Tests` | 93,3 | %17,6 | %37,4 | 51 | yerel besleme + pack |
| `Ui.E2ETests` | 87,3 | %16,4 | %53,9 | 58 | Playwright |
| `SqlServer.IntegrationTests` | 75,0 | %14,1 | %68,0 | 639 | **Docker** |
| `Sqlite.IntegrationTests` | 28,1 | %5,3 | %73,3 | 648 | hiçbiri (dosya) |
| `Google.UnitTests` | 26,6 | %5,0 | %78,3 | 84 | hiçbiri |
| `OpenAI.UnitTests` | 26,6 | %5,0 | %83,3 | 111 | hiçbiri |
| `PostgreSql.IntegrationTests` | 24,4 | %4,6 | %87,9 | 704 | **Docker** |
| `Anthropic.UnitTests` | 21,6 | %4,1 | %91,9 | 79 | hiçbiri |
| `Azure.UnitTests` | 18,6 | %3,5 | %95,4 | 76 | hiçbiri |
| `Cli.FunctionalTests` | 11,2 | %2,1 | %97,6 | 33 | hiçbiri |
| `Workflows` · `Core` · `Generators` · `Embedded` · `Testing` · `Mcp` · `Sql.Shared` · `Voice` · `Contracts.Xunit` · `Client` (10 proje) | 12,9 | %2,4 | %100 | 2 911 | hiçbiri |
| **TOPLAM** | **531,4** | | | **6 161** | |

**En yavaş 5 proje toplamın %73,3'üdür.** Dikkat çeken üç ölçüm:

1. **`Core.UnitTests` 2 362 testi 2,7 sn'de koşuyor.** Test *sayısı* süreyi
   açıklamıyor; süreyi **host kurulumu** açıklıyor.
2. **`AspNetCore.FunctionalTests` en pahalı projedir (105,7 sn) ve ne Docker
   ne Playwright kullanır.** `full_solution_test_command()`'ın docstring'i
   yavaşlığı "concurrent Docker containers, Playwright browser" ile
   gerekçelendiriyor — **ölçüm bu gerekçeyi yalnız kısmen doğruluyor**.
   Gerçek Docker payı (PostgreSql + SqlServer) 99,4 sn = %18,7.
3. **`-maxcpucount:1` 21 projenin tamamına uygulanıyor**, ama Testcontainers
   yalnız **2** projede (`PostgreSql`, `SqlServer`), Playwright **1** projede
   (`Ui.E2ETests`), yerel NuGet beslemesi **1** projede (`Package.Tests`).
   Kalan **17 proje** kısıtsızdır.

### A7. 🚨 Taban koşum KIRMIZI — F-180 kapalı sayıldığı hâlde tekrarladı

Taban koşum `exit 1` verdi:

```
failed AgentPrism.Ui.E2ETests.UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript (30s 951ms)
System.TimeoutException : Timeout 30000ms exceeded.
Call log: - waiting for GetByTestId("voice-transcript") to be visible
```

**İzole koşum yeşil:** `./artifacts/bin/AgentPrism.Ui.E2ETests/release/AgentPrism.Ui.E2ETests
--filter-method "*Playground_voice_mode*"` → **1/1, 2,5 sn.**

İmza F-180'in kaydıyla **birebir** aynıdır (`docs/ADAYLAR.md` § F-180: yük
altında geçtiğinde 2,6 sn, düştüğünde 30 sn'lik `voice-transcript` beklemesi).
F-180 2026-09-03'te **✅ KAPANDI** işaretlendi (K-660, `idle` sunucu çerçevesi;
`docs/hafiza/ses-ve-konusma.md`). Düzeltme **HEAD'de** ve test yine düştü.

**Sonuç:** F-180'in kapanışı vakayı kapattı, **sınıfı kapatmadı.** Bu, bu
denetimin kendi ölçümüyle bulduğu kusurdur → **KUSUR-A2** (§B4).

---

## B. Kaçış analizi

### B1. Sınıflar

Görev metnindeki on sınıf kullanıldı; ölçüm bir **on birinci** sınıf gerektirdi
(`11 · Test altyapısı yalıtımı`) — F-181, F-190 ve E3-opc bu sınıftadır ve
diğer onunun hiçbirine düşmüyorlar. `6` sınıfı genişletildi: yalnız
"imza değişti, gövde takip edilmedi" değil, **"bir karar N ayrı yüzeyde elle
tekrarlanıyor, biri güncellenmiyor"** (K-483 · K-525 sınıfı).

### B2. Pareto — TÜM tarih (74 kök neden)

| # | Sınıf | n | % | kümülatif |
|---:|---|---:|---:|---:|
| 3 | Yalnız mutlu yol; hata modu test edilmedi | 20 | %27,0 | %27,0 |
| 2 | Test yanlış seviyedeydi | 15 | %20,3 | %47,3 |
| 1 | Test hiç yoktu | 10 | %13,5 | %60,8 |
| 6 | Karar N yüzeyde tekrarlandı, biri güncellenmedi | 9 | %12,2 | %73,0 |
| 5 | Plan boşluğu | 8 | %10,8 | %83,8 |
| 9 | Doküman kayması (sevk edilen metin) | 4 | %5,4 | %89,2 |
| 7 | Platform farkı | 3 | %4,1 | %93,2 |
| 11 | Test altyapısı yalıtımı | 3 | %4,1 | %97,3 |
| 8 | MAF/SDK API varsayımı | 1 | %1,4 | %98,6 |
| 4 | Kapı koşulmadı veya DAR koşuldu | 1 | %1,4 | %100 |
| 10 | Denetim gördü ama kapatmadı | 0 | — | |

Bu tablo **zincir öncesi borcu** ölçer: 74 kök nedenin 55'i tek seferlik iki
turda (manuel kabul + güvenlik taraması) bulundu ve hepsi E1/E2 doğumludur.
**Zincir revizyonu için doğru tablo aşağıdakidir.**

### B3. Pareto — ZİNCİR YÜRÜRLÜKTEYKEN (19 kaçış)

Manuel tur ve güvenlik taraması dışlandı; kalan her kaçış zincir çalışırken
oldu.

| # | Sınıf | n | % |
|---:|---|---:|---:|
| 3 | Hata modu test edilmedi | 3 | %15,8 |
| 9 | Doküman kayması (sevk edilen metin) | 3 | %15,8 |
| 7 | Platform farkı | 3 | %15,8 |
| 11 | Test altyapısı yalıtımı | 3 | %15,8 |
| 5 | Plan boşluğu | 2 | %10,5 |
| 1 | Test hiç yoktu | 2 | %10,5 |
| 4 | Kapı DAR koşuldu | 1 | %5,3 |
| 6 | Karar N yüzeyde tekrarlandı | 1 | %5,3 |
| 2 | Test yanlış seviyedeydi | 1 | %5,3 |

> **Baskın sınıf yoktur.** Zincirin E1 döneminde hâkim olan üç sınıfı
> (1 · 2 · 3, birlikte %60,8) zincir dönemine %31,6 olarak düşmüş. Pareto
> düzleşmiştir; bu, "tek bir kural yazarsak %80'i kapatırız" cevabının artık
> **yanlış** olduğu anlamına gelir.

### B4. Asıl sinyal — kaçışın MALİYETİ, sınıfı değil

Aynı 19 kaçış, **bulunduğu kanala** göre:

| Kanal | n | Maliyet |
|---|---:|---|
| **Tüketici turu** | **5** | **en pahalı — sevk edilmişti** |
| Kapı (`kapanis` / `ic-dongu`) | 5 | ucuz |
| `kusur-giderme` (faz dışı) | 3 | orta |
| CI (yalnız `windows-latest`) | 3 | ucuz (tur maliyeti) |
| `faz-denetim` | 2 | ucuz |
| Keşif turu | 1 | orta |

Tüketiciye ulaşan **beş kaçışın beşi de sevk edilen yüzeydedir** — kod
mantığında değil:

| ID | Ne | Sınıf | Neden iç test görmedi |
|---|---|---|---|
| F-182 | Paket kimliğinin tekilliği zorlanmıyordu | 1 | Paket sınırı; `ProjectReference` ile koşan test bu sınıfı hiç görmez |
| F-183 | Sevk edilen `CustomJobHandler` örneği **gerçek worker'da hiç çalışmıyor** — `AddAgentPrism()`'den SONRA kaydoluyor | 2 | Örneğin testi yalnız **DI kaydını** ölçüyordu (test tiyatrosu) |
| F-184 | `VoiceDescriptor` sağlayıcı üstverisi yok | 5 | Plan bu davranışı hiç istememişti |
| TU3-1 | `embedding.md`'nin öznesiz cümlesi tüketiciye **var olmayan bir kanal** anlattı | 9 | Mekanik denetlenemez; kapısı **bugün de yok** |
| TU3-2 | `quota.threshold` hiçbir anlatı sayfasında yok → tüketici **var olan özelliği yeniden önerdi** | 9 | `sevk_edilen_olay_anlatisi()` kapısı BU vakadan sonra yazıldı |

Denetimin en verimli başlığı da (§A6) **3.8 Ürün yüzeyi**'ydi (15/53 bulgu).
İki bağımsız ölçüm aynı yeri gösteriyor:

> 🎯 **Zincirin kör noktası kod değil, SEVK EDİLEN YÜZEYDİR** — doküman,
> örnek ve paket. İç test paketi bu sınırı yapısal olarak göremez.

### B5. Bu denetimin kendi bulduğu kusurlar

Ölçüm sırasında iki canlı kusur çıktı. İkisi de yukarıdaki teşhisin kanıtıdır.

#### KUSUR-A1 · Sevk edilen örnek "altı genişleme noktası" diyor, yedi var

`AgentPrismDiagnosticsCollector.CollectExtensionPoints()` **yedi** nokta
döndürüyor (Faz 142 `IToolApprovalPresenter`'ı ekledi). Sevk edilen metin
takip etmedi:

| Yüzey | Ne diyor | Gerçek |
|---|---|---|
| `samples/AgentPrism.Embedded/Program.cs:42-43` | "AddAgentPrism() below calls `TryAdd*` for all **six**" | **yedi** |
| `samples/AgentPrism.Embedded/Program.cs:11` | "The **six** embedding points" | yedi nokta var, örnek altısını bağlıyor |
| `samples/AgentPrism.Embedded/README.md:35,41` | "Confirm all **six** took over" · "**Every** entry reads `isBuiltInDefault: false` here" | 7. nokta bağlı değil → `true` okur. Testin kendi adı zaten `Six_of_the_seven_...` |
| `samples/AgentPrism.Embedded/README.md:42` | "`samples/AgentPrism.Api`, which reports `true` for **five of the six**" | Api artık `IToolApprovalPresenter`'ı da bağlıyor (`Program.cs:145`) |

**Sınıf 6, ÜÇÜNCÜ tekrar.** Faz 139 altıncı noktayı ekledi → örnek geride
kaldı (`a377106e`). Faz 142 yedinciyi ekledi → denetim `capabilities.md` +
`DiagnosticsCollector`'ı yakaladı (bulgu #2) ama **örneğin kendi metnini
kaçırdı**. Faz 142'nin devir notu bunu zaten yazmış:

> "ikisi de elle senkron tutulur, **hiçbir kapı bu boşluğu otomatik yakalamaz**"

#### KUSUR-A2 · F-180 kapalı sayıldı, sınıfı kapanmadı

§A7. Kapanış kapısının kendisi kırmızı çıkabiliyor; bu, gerçek bir regresyonu
maskeleyebilir — F-190'ın kaydı da aynı riski yazıyor.

### B6. Mekanizma merdiveni

Her baskın sınıf için soru: **hangi mekanizma bu sınıfı yapısal olarak
imkânsız kılar?** Sıra: (a) derleyici/analyzer → (b) kapı betiği →
(c) test sözleşmesi → (d) skill talimatı. Aşağıdaki basamak her zaman daha
ucuz ve güvenilirdir; (d) en zayıf cevaptır.

| Sınıf | Bugünkü mekanizma | Ölçülen boşluk | Önerilen basamak |
|---|---|---|---|
| **9 · sevk edilen doküman** | `sevk_edilen_olay_anlatisi()` (yalnız `WebhookEvents`) · `check-content.mjs` | TU3-1'in kapısı **yok**, kayıt "ikinci kez görülürse kapı zorunlu" diyor | **(b)** — envanteri genişlet |
| **6 · N yüzey** | yok (Faz 142 devir notu bunu yazıyor) | KUSUR-A1 = üçüncü tekrar | **(b)** — `CollectExtensionPoints()` ↔ `capabilities.md` ↔ örnek metni tek kapıda |
| **4 · kapı DAR koşuldu** | `affected_test_projects()` | iki ölçülmüş boşluk, §C3 | **(b)** — proje haritasını düzelt |
| **11 · test altyapısı yalıtımı** | `MeterListenerIsolationTests` (F-181) | KUSUR-A2 açık | **(c)** — ayrı iş; bu turda **kapatılmıyor**, kaydediliyor |
| **7 · platform farkı** | `ortak/kapilar.md` § "CI'ın Windows ayağı" (6 tuzak, 2026-08-28) | 08-28'den beri **yeni Windows kaçışı yok** | **(d) yeterli** — dokunma |
| **3 · hata modu** | `ortak/test-seviyeleri.md` beş soru · `faz-denetim` 3.4 | 3.4 denetimin 2. verimli başlığı; çalışıyor | **(d) yeterli** — dokunma |
| **2 · yanlış seviye** | `ortak/test-seviyeleri.md` tablosu | F-183 sevk edilen **örnekte** oldu; tablonun son satırı bunu zaten söylüyor ama örnek testlerine uygulanmamış | **(c)** — §D'de değerlendirilecek |

---

## C. Kapı optimizasyonu

Yöntem: ölç → hipotez → deney → tekrar ölç. Her hipotez için önce/sonra süre ve
**"ne kaybettik"** satırı. H4 (CI'a devretme) kullanıcı kararıyla kapsam dışı.

### C0. Reddedilen — `dotnet format --no-restore`

Görev metni `--no-restore`'u bir hızlandırma adayı olarak sordu. **Bu zaten
ölçülmüş ve reddedilmiş.** `scripts/kapi.py:632-640` yorumu (2026-08-27):
restore'suz `MSBuildWorkspace` yüklemesi, tam test koşumundan HEMEN SONRA her
`samples/*.Tests` projesinin `PackageReference` tiplerini çözemiyor —
**bu komut zinciriyle 3 kez yeniden üretildi, izole hiç görülmedi.** Değişiklik
yoksa restore birkaç saniyedir.

> Yeniden denenmemesi için buraya yazıldı.

### C1. Taban ölçümü — 3 koşum

`dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1`

| Koşum | Süre | Sonuç |
|---|---:|---|
| 1 | 557 sn | ❌ `Playground_voice_mode_opens_microphone_and_shows_transcript` (30 sn timeout) |
| 2 | 561 sn | ✅ |
| 3 | (aşağıda) | |

### C2. `ic-dongu` proje haritası — ölçülen iki boşluk KAPATILDI

Ölçüm (`affected_test_projects`, `git show HEAD:scripts/kapi.py` ile önce/sonra):

| Değişen yol | ÖNCE seçilen | SONRA seçilen | Bağlı kaçış |
|---|---|---|---|
| `src/AgentPrism.Core/Builder/IAgentPrismBuilder.cs` | `AspNetCore.FunctionalTests`, `Core.UnitTests` | + **`Generators.UnitTests`** | `9433efe4` |
| `samples/AgentPrism.Embedded/Program.cs` | **hiçbiri** | **`Embedded.Tests`** | `a377106e` |
| `samples/AgentPrism.Samples.CustomJobHandler/Program.cs` | hiçbiri, **sessizce** | hiçbiri + **uyarı** | F-183 |

**Kanıt 1.** `ExampleExtractor.SourceFiles` = `Directory.EnumerateFiles(root/src,
"*.cs", AllDirectories)` — `Generators.UnitTests` `src/`'nin **tamamındaki**
`<example>` bloklarını derler. Harita ise yalnız `src/AgentPrism.Generators` ve
`src/AgentPrism.OpenAI`'ı oraya yönlendiriyordu. `9433efe4`'ün commit gövdesi
bunu kendisi yazmış: *"caught only by the full solution test run, not by
AgentPrism.Core.UnitTests alone"*.

**Kanıt 2.** `tests/AgentPrism.Embedded.Tests` `ProjectReference` ile
`samples/AgentPrism.Embedded`'a bağlıdır, ama `affected_test_projects` bir
`samples/` yolunda hiçbir dala girmiyordu — `needs_full` bile kurulmuyordu.

**Kanıt 3.** `samples/AgentPrism.Samples.*` çözümde değildir; hiçbir kapanış
koşumu onları kapsamaz, yalnız `kapi.py yayin`. Seçim yine boş kalır ama
**sessiz değildir** — F-183 tam olarak burada kaçtı.

**Ne kaybettik:** `Generators.UnitTests` her `src/` değişikliğinde koşuyor;
ölçülen maliyet **2,4 sn**. Kayıp yok.

**Kapı:** `kapi_test.py` — `test_her_src_degisikligi_ornek_derleyen_projeyi_secer`,
`test_embedded_ornegi_kendi_test_projesini_secer`, `test_yayin_ornekleri_sessiz_gecmez`.
Üçü de düzeltmeden önce kırmızıydı (yukarıdaki "ÖNCE" sütunu ölçümdür).

### C3. Düşen testin izole yeniden koşumu — sınıf 11'in kapısı

**Ölçülen sorun.** "Tam koşumda düşer, izole geçer" sınıfı **altı** vakayla
kayıtlıydı (sınıf Faz 103'te açıldı; Faz 82 · 130 · 134 · 141 ayrı testlerle
tekrarladı — kayıt artık [`hafiza/test-yalitimi.md`](../hafiza/test-yalitimi.md));
bu denetimin taban koşumu **yedinciyi** üretti (§A7).
`kusur-giderme` Adım 6'nın kendi kuralı — *"Bir kusur sınıfı üçüncü kez
tekrarlıyorsa yazı yetmemiştir: o zaman kapı gerekir"* — bu sınıfa **hiç
uygulanmamış**: yedi vaka, sıfır kapı.

Ayırt etme her seferinde elle yapıldı ve faz kayıtları maliyeti yazıyor:
Faz 141 *"commit sonrası **iki** tam koşum"*, Faz 81 *"tam çözümde **İKİ** kez
koşuldu"*, Faz 98 *"**dört ayrı** koşumda dört ayrı ilgisiz test"*. Tam koşum
**557–561 sn**; izole koşum **2,5 sn** (§A7'de ölçüldü).

**Kapı.** `kapi.py` → `failed_tests_from_trx()` + `isolate_failed_tests()`.
Test komutu kırmızı dönerse düşen testler TRX'ten okunur, her biri derlenmiş
ikilide `--filter-method` ile **tek başına** koşulur ve hüküm basılır.

> 🚨 **Çıkış kodunu DEĞİŞTİRMEZ.** Kırmızı kırmızı kalır; bu bir teşhistir,
> kapı gevşemesi değil. Verdiği tek şey bir sonraki adımın ne olduğudur —
> "tekrar koş" mu, "`kusur-giderme` koş" mu.

Tam koşum komutu `-- --report-trx` aldı; artık `ci.yml:183` ile **birebir
aynıdır** (düşen testin adı makine okunur olmadan izole koşum yazılamaz).
Ölçülen TRX maliyeti: ölçüm gürültüsünün altında (557 sn TRX'li · 561 sn
TRX'siz).

**Kapı testleri:** `test_trx_yalniz_dusen_testi_verir` ·
`test_bayat_trx_sayilmaz` (önceki koşumun TRX'i bu koşumun bulgusu değildir) ·
`test_izole_kosum_cikis_kodunu_degistirmez_ama_hukum_verir` ·
`test_izole_de_dusen_test_gercek_regresyondur` · `test_tam_kosum_komutu_trx_uretir`.

---

## D. Zincir revizyonu

Kapsam (kullanıcı kararı): **faz başına yüklenen** dosyalar. `nuget-danismani`
(23 226 B), `manuel-test-kosumu` (14 560 B) ve `aday-kesfi` (13 100 B) faz
token faturasına girmiyor — açılmadılar.

### D0. Ölçüm: silinecek fazlalık YOK

Dokuz zincir dosyası normalize edilip (kod bloğu ve bağlantı soyularak) satır
bazında karşılaştırıldı: **≥7 kelimelik birebir tekrar yalnız 7 satır**, üçü
bilinçli çapraz referans başlığı. Faz 91/92'nin `.agents/ortak/`'a taşıma işi
tutmuş. `faz-denetim`'in hiçbir alt başlığı **sıfır bulgu** üretmedi
(§A6: en zayıfı 3.3 ile 2 bulgu). **"Sil"** için ölçülmüş tek aday çıktı.

### D1. Silinen — `faz-tamamlama` Adım 1'in ayrı `tarama` ön koşumu

Skill "kapılardan ÖNCE `kapi.py tarama` koş, bu adım atlanamaz" diyordu.
Ölçüm: `closing_commands()`'ın **birinci** komutu zaten
`python3 scripts/kapi.py tarama`'dır ve `run_commands` ilk kırmızıda durur
(`test_first_failure_stops_pahali_kapilari` bunu kanıtlar). `tarama` **3,1 sn**.
Ayrı ön koşum **hiçbir tur kazandırmıyordu**.

13 satır → 5 satır. Vaka kayıtları `references/gerekce.md`'de kalıyor;
delegasyon dizesi (`tekrarlanan_kapi_tanimlari` kapısının aradığı) korundu.

### D2. Sıkılaştırılan — `kusur-giderme` kapanış kontrolü #4

**Yeni adım değildir**; var olan kapanış kontrolüne bir soru eklendi:
*"Adım 1'in repro'su, kendi koşullarında tekrar koşuldu mu?"*

Bağlı bulgu: **KUSUR-A2**. F-180'in kapanışı iki gerçek ürün yolunu düzeltti ve
hedefli bir testle kanıtladı, ama kaydı açan repro (yük altındaki tam paket
koşumu) tekrar koşulmadı; kayıt "✅ KAPANDI" işaretlendi, test bir gün sonra
aynı imzayla düştü. Kural artık şunu da söylüyor: kapatamıyorsan **"vaka
kapandı, sınıf açık"** yaz — "kapandı" deme.

`kusur-giderme` faz başına yüklenmez (yalnız kusur bulununca), bu yüzden
+600 B faz faturasına girmez.

### D3. Düzeltilen — `.agents/ortak/kapilar.md` · `ic-dongu` kapsamı

`samples/AgentPrism.Samples.*`'ın çözümde olmadığı ve yalnız `kapi.py yayin`
tarafından kapsandığı yazıldı (C2, F-183).

### D4. Düzeltilen — `faz-denetim` 3.8'in artık YANLIŞ olan cümlesi

Madde *"bugün hiçbir kapı bu boşluğu yakalamıyor"* diyordu. C1'den sonra bu
**yarı yanlış**: genişleme noktası kümesinin kapısı var, yetenek ve paket
satırları hâlâ elle. Denetçiyi hâlâ elle olan yere yönlendirecek şekilde
düzeltildi — uzunluk artmadı, doğruluk arttı.

### D5. Reddedilenler

| Öneri | Neden reddedildi |
|---|---|
| Sınıf 7 (platform farkı) için yeni kural | `kapilar.md` § "CI'ın Windows ayağı" 2026-08-28'de yazıldı; **o tarihten beri yeni Windows kaçışı yok**. Mekanizma (d) yeterli, kanıt var. |
| Sınıf 3 (hata modu) için yeni kural | `ortak/test-seviyeleri.md`'nin beş sorusu + `faz-denetim` 3.4 çalışıyor: 3.4 denetimin **ikinci** en verimli başlığı (10/53). Yeni kural bilgi eklemez, fatura ekler. |
| TU3-1 sınıfı (öznesiz cümle) için kapı | **Tek vaka.** Kaydın kendisi "sınıf ikinci kez görülürse kapı zorunlu" diyor. Bu denetim ikinci vakayı bulmadı; kapı yazmak spekülasyondur. |
| `faz-denetim` 3.2/3.3'ü silmek | En az bulgu üreten başlıklar (3 ve 2), ama **sıfır değil** — ve F-183 tam olarak 3.2'nin sınıfıdır. Ölçüm silmeyi desteklemiyor. |
| `dotnet format --no-restore` | §C0 — zaten ölçülmüş ve reddedilmiş (3 kez yeniden üretildi). |
| Kırılganlıkta çıkış kodunu uyarıya düşürmek | Kapı gevşemesidir. `isolate_failed_tests` **teşhis** verir, kırmızıyı yeşile çevirmez. |

### D6. Ölçülen zincir maliyeti — ARTTI, gerekçesiyle

| Dosya | Önce | Sonra | Fark |
|---|---:|---:|---:|
| `faz-denetim/SKILL.md` | 9 237 | 9 468 | +231 |
| `faz-tamamlama/SKILL.md` | 19 624 | 19 525 | **−99** |
| `ortak/kapilar.md` | 5 677 | 5 974 | +297 |
| diğer altı dosya | — | — | 0 |
| **Faz başına toplam** | **65 388** | **65 817** | **+429 B (~+107 token)** |

> **Dürüst kayıt: zincir metni büyüdü, küçülmedi.** Hedef "daha az token"dı;
> ölçüm bunu vermedi. Karşılığında alınan şey metin değil **mekanizma**: üç
> yeni kapı (genişleme noktası sayımı · iç döngü haritası ×2 · izole yeniden
> koşum teşhisi) daha önce elle yapılan doğrulamayı üstlendi. Sadece C3'ün
> kazancı, sınıf her tekrarladığında **~9 dakikalık ikinci tam koşumdur** —
> ve sınıf 30 günde yedi kez tekrarladı.

### C4. H1 — iki dalgalı test koşumu: **REDDEDİLDİ**

**Hipotez.** `-maxcpucount:1` 21 projenin tamamına uygulanıyor ama gerçek kısıt
4 projede (§A3). Hafif 16 proje varsayılan paralellikte, ağır 5 proje seri
koşarsa süre düşer.

**Dalgalar.** Ağır: `AspNetCore.FunctionalTests` · `Package.Tests` ·
`Ui.E2ETests` · `SqlServer.IntegrationTests` · `PostgreSql.IntegrationTests`.
Hafif: kalan 16 proje.

**Ön kayıtlı kabul ölçütü** (deneyden ÖNCE yazıldı): *3 tekrarlı koşumda da aynı
yeşil sonuç ve düşen test sayısı artmamış olmalı.*

| Koşum | Süre | Sonuç |
|---|---:|---|
| Taban 1 | 557 sn | ❌ 1 düşen (E2E ses, F-180 sınıfı) |
| Taban 2 | 561 sn | ✅ |
| Taban 3 | 542 sn | ✅ |
| **Taban ortalaması** | **553,3 sn** | 3 koşumda **1** düşen test |
| İki dalga 1 | **425 sn** (hafif 35 · ağır 390) | ✅ |
| İki dalga 2 | **4 475 sn** (hafif 31 · ağır **4 444**) | ❌ **121 düşen test** |
| **İki dalga 3** | **433 sn** (hafif 32 · ağır 401) | ✅ |

**İkinci koşum hipotezi öldürdü.** `SqlServer.IntegrationTests` tek başına
**48 dk 53 sn** sürdü (tabanda 75,0 sn) ve 121 test şu imzayla düştü:

```
Class fixture 'SqlServerSchemaFixture' threw in InitializeAsync
---- Migration '0024_run_continuation' could not be applied:
     Execution Timeout Expired ... (error -2)
```

Testler yanlış cevap vermedi; **SQL Server konteyneri migration'ı
uygulayamayacak kadar boğuldu.** Aynı koşumda `AspNetCore` (767/767),
`Package` (51/51) ve `PostgreSql` (704/704) yeşildi — kayıp yalnız SQL
Server'daydı ve tam olarak `full_solution_test_command()`'ın docstring'inin
koruduğu şeydi: *"each of them can start additional processes and exhaust the
local Docker memory budget."*

**Ne kaybettik:** 128 sn kazanç için, üç koşumun birinde **121 sahte kırmızı**
ve **74 dakikalık** bir koşum. Ön kayıtlı ölçüt açıkça ihlal edildi.

> 📋 **Karar: `-maxcpucount:1` KORUNUR.** Hipotez ölçüldü ve reddedildi.
> Kazanç gerçekti (−%23) ama sinyal kaybı gerçek ve büyüktü — çalışma
> kuralı gereği birinci hedef (kaçış oranı) ikinciyi (hız) yener.

> 🚨 **Yeniden denenmemesi için:** ölçülen kısıt "kaç proje paralel" değil,
> **hafif dalganın ardında bıraktığı sistem/Docker baskısıdır**. Hafif dalga
> 31–35 sn'de biter ve ağır dalga hemen başlar; SQL Server konteyneri o
> pencerede migration timeout'una düşebilir. Bir sonraki deneme bunu ölçmeden
> başlamasın: dalgalar arasına Docker sağlık beklemesi koymak ayrı bir
> hipotezdir ve ayrı ölçülmelidir.

---

## E. Ölçülebilir başarı kriterleri

Bu revizyon **başarısızdır** eğer aşağıdakilerden biri gerçekleşirse. Sonraki
denetim turu bunları ölçer; her satırın kontrol komutu yanındadır.

| # | Revizyon | Başarısızlık koşulu | Kontrol |
|---|---|---|---|
| 1 | Genişleme noktası kapısı (C1) | Sevk edilen bir metin kodun bildirdiğinden **farklı** sayıda genişleme noktası ilan eder ve bunu bir insan/tüketici bulur | `python3 scripts/dokuman-bakim.py --denetle` → "Sevk edilen genişleme noktası" |
| 2 | İç döngü haritası (C2) | Bir `src/` veya `samples/AgentPrism.Embedded/` değişikliği yine **yalnız tam koşumda** yakalanır | `git log --grep='caught only by the full solution'` boş kalmalı |
| 3 | İzole yeniden koşum (C3) | Bir faz kaydı yine "kırılgan mı, gerçek mi" ayrımı için **ikinci bir tam koşum** yapar | faz kayıtlarında "iki tam koşum" ifadesi |
| 4 | `kusur-giderme` #4 (D2) | Bir kusur kaydı "✅ KAPANDI" işaretlenir ve **aynı repro** ile tekrarlar | `docs/ADAYLAR.md`'de "YENİDEN DÜŞTÜ" |
| 5 | `-maxcpucount:1` kararı (C4) | Biri ölçmeden paralellik açar ve `SqlServer` migration timeout'u geri gelir | `full_solution_test_command()` docstring'i |

**Kapatılmayan, bilerek açık bırakılan:**

| Açık kalem | Neden bu turda kapatılmadı |
|---|---|
| **KUSUR-A2'nin kök nedeni** (F-180 sınıfı, 7. vaka) | Kök neden avı `kusur-giderme`'nin işidir ve bu denetimin kapsamı süreçtir. Bu tur sınıfa **teşhis kapısı** verdi (C3) ve kaydı düzeltti; kök neden hâlâ açık. |
| Sınıf 9'un "öznesiz cümle" alt sınıfı | Tek vaka; kaydın kendi kuralı ikinci vakayı bekliyor (D5). |
| `samples/AgentPrism.Samples.*`'ın kapanış kapsamı | Çözümde olmamaları bilinçli bir karardır (paketlenmiş tüketiciyi taklit ederler). `ic-dongu` artık **uyarıyor**; kapsamı genişletmek yayın provasını her fazda koşmak demektir ve ayrı bir maliyet kararıdır. |

### C5. H2 — `dotnet format` kapsamı: **DARALTILMADI, gerekçesi ölçüldü**

**Hipotez.** `dotnet format --verify-no-changes` 99,7 sn (kapanışın %13,1'i).
`.editorconfig` `dotnet_diagnostic.IDE0055.severity = warning` +
`EnforceCodeStyleInBuild=true` + `TreatWarningsAsErrors=true` taşıyor; yani
`dotnet build` biçimlendirmeyi zaten yakalıyor olabilir. Öyleyse kapı yalnız
değişen dosyalarda koşabilir veya daralabilir.

**Deney.** Aynı dosyaya sırayla iki KASITLI ihlal sokuldu, her biri için iki
kapı ayrı ayrı koşuldu.

| İhlal | `dotnet build` | `dotnet format --verify-no-changes` |
|---|---|---|
| Girinti 8→12 + ikili boşluk (`var sinks  =  ...`) | ❌ **9 × `error IDE0055: Fix formatting`** (31 sn) | — |
| Dosya sonu satır sonu silindi (`insert_final_newline`) | ✅ **0 Warning, 0 Error** | ❌ **`error FINALNEWLINE: Fix final newline. Insert '\n'`** (91 sn) |

**Sonuç.** Token düzeyindeki biçimlendirmeyi `build` zaten yakalıyor; ama
**dosya düzeyindeki** `.editorconfig` kuralları (`insert_final_newline` —
büyük olasılıkla `end_of_line` ve `charset` de) Roslyn'in IDE0055'inde
**yoktur** ve yalnız `dotnet format` görür.

> 📋 **Karar: `dotnet format` kapısı olduğu gibi KALIR.** Benzersiz sinyali
> ölçüldü ve gösterildi. Değişen dosyaya daraltmak da reddedildi: bu denetimin
> ana bulgusu **sevk edilen yüzeyin kör nokta olduğudur** (§B4), ve üretilen
> dosyalar (OpenAPI istemcisi, `llms-full.txt`, agent haritası) fazın `git
> diff`'inde her zaman görünmez. En yanlış kısılacak kapı budur.

### C6. Ölçülen önce/sonra — kapanış süresi

| Adım | Önce | Sonra | Değişim | Gerekçe |
|---|---:|---:|---|---|
| `dotnet test` (tam çözüm) | 553,3 sn (3 koşum ort.) | **değişmedi** | H1 reddedildi (C4) | Paralellik ölçüldü: −%23 kazanç, 3 koşumun 1'inde 121 sahte kırmızı |
| `dotnet format` | 99,7 sn | **değişmedi** | H2 reddedildi (C5) | `FINALNEWLINE` yalnız burada yakalanıyor |
| `kapi.py performans` | 102,0 sn | **değişmedi** | zaten yol tetiklemeli (116.4) | `performance_gate_triggered` doğrulandı |
| `dotnet build` | 40,1 sn | değişmedi | — | |
| `faz-tamamlama`'nın ayrı `tarama` ön koşumu | 3,1 sn | **0 sn** | D1 — silindi | `kapanis`'in ilk komutu zaten `tarama` |
| **`kapanis` toplamı** (performans hariç) | **656,8 sn** | **656 sn ölçüldü** (§C7) | ~**−3 sn** | |

> **Dürüst kayıt: kapanış süresi pratikte DÜŞMEDİ.** Ölçülen iki büyük
> kaldıraç (test paralelliği %70,6 · format kapsamı %13,1) deneyle
> **reddedildi** — ikisi de sinyal kaybediyordu. Görev "hızlanmak için sinyal
> düşürme" diyordu; ölçüm bu iki kaldıracın tam olarak bunu yapacağını
> gösterdi.
>
> Gerçek zaman kazancı başka yerdedir ve **koşum başına değil, olay başınadır**:
> C3'ün izole yeniden koşumu, kırılganlık sınıfı her tekrarladığında bir
> **ikinci tam koşumu** (553 sn) gereksiz kılar. Sınıf 30 günde **yedi** kez
> tekrarladı.

### C7. Bu turun kendi kapanış koşumu — ÖLÇÜLDÜ

`python3 scripts/kapi.py kapanis --taban e599259f` · **EXIT=0 · 656 sn**

| Adım | Süre | Sonuç |
|---|---:|---|
| `kapi.py tarama` | 2,75 sn | ✅ |
| `dokuman-bakim.py --denetle` | 5,23 sn | ✅ |
| `python3 -m unittest discover -s scripts` (221 test) | 6,15 sn | ✅ |
| `build-agent-map.mjs --check` | 0,09 sn | ✅ |
| `denetim-paketi.py --taban e599259f` | 0,25 sn | ✅ |
| `dotnet build AgentPrism.slnx -c Release` | 19,43 sn | ✅ 0 uyarı |
| `dotnet test AgentPrism.slnx ... -- --report-trx` | 497,48 sn | ✅ |
| `dotnet pack` | 6,07 sn | ✅ |
| `dotnet format --verify-no-changes` | 90,30 sn | ✅ |
| `docs-site npm run check` | 27,95 sn | ✅ |
| `kapi.py performans` | **koşmadı** | Yol tetiklemeli (116.4): sıcak yol dosyası değişmedi |
| **TOPLAM** | **656 sn** | **EXIT=0** |

Taban profiliyle tutarlı: 758,8 sn ortalaması `performans` adımını (102,0 sn)
içeriyordu; 758,8 − 102,0 = **656,8 sn**. Bu koşumda `performans` tetiklenmedi
ve `performance_gate_triggered`'ın doğru çalıştığı böylece de doğrulandı.

Bu koşumda **hiçbir test düşmedi**, dolayısıyla C3'ün izole yeniden koşumu
tetiklenmedi — kırmızı yolun kanıtı birim testlerindedir
(`test_izole_kosum_cikis_kodunu_degistirmez_ama_hukum_verir`).

---

## F. Sonuç

**Zincir çalışıyor.** Kaçış oranı doğum dönemine göre 0,545 → 0,097 → 0,035
kaçan kusur/faz (§A5, sansür uyarısıyla). `faz-denetim` faz başına 2,2 aksiyon
alınan bulgu üretiyor (§A6). E1'in hâkim üç kaçış sınıfı (%60,8) zincir
döneminde %31,6'ya inmiş ve Pareto **düzleşmiş** (§B3) — artık "tek bir kural
%80'i kapatır" cevabı yanlıştır.

**Kör nokta sevk edilen yüzeydir.** Tüketiciye ulaşan beş kaçışın beşi de kod
mantığında değil, doküman · örnek · paket yüzeyindeydi (§B4); denetimin en
verimli başlığı da aynı yerdi (3.8, 15/53 bulgu). Bu turda o yüzeye **iki**
mekanik kapı yazıldı (C1, C2) ve o yüzeyde **iki canlı kusur** bulundu (§B5).

**Hız hedefi karşılanmadı ve karşılanmaması doğrudur.** İki büyük kaldıraç
ölçüldü ve reddedildi (C4, C5); ikisi de sinyal kaybediyordu. Zincir metni
**+429 B/faz büyüdü** (D6). Alınan karşılık metin değil mekanizmadır: elle
yapılan üç doğrulama artık kapıdır.

---

## G. Bu turun yaptığı değişikliklerin izlenebilirliği

| Değişiklik | Bağlı bulgu | Kanıt |
|---|---|---|
| `dokuman-bakim.py` → `sevk_edilen_genisleme_noktasi()` + 6 test | **KUSUR-A1** (sınıf 6, 3. tekrar) | Kapı düzeltmeden önce **3 gerçek bulgu** verdi, sonra temiz (§B5) |
| `samples/AgentPrism.Embedded/{Program.cs,README.md}` · `docs-site/guides/embedding.md` | KUSUR-A1 | Kod 7 nokta bildiriyordu, metin 6 diyordu |
| `kapi.py` → `affected_test_projects()` + 3 test | `9433efe4` · `a377106e` · F-183 (sınıf 4) | Önce/sonra seçim tablosu (§C2) |
| `kapi.py` → `failed_tests_from_trx()` · `isolate_failed_tests()` + 5 test | Sınıf 11, **7 vaka** | §C3; çıkış kodu değişmez |
| `kapi.py` → tam test komutuna `-- --report-trx` | Yukarıdakinin ön koşulu | `ci.yml:183` ile birebir aynı oldu |
| `faz-tamamlama` Adım 1 — ayrı `tarama` ön koşumu **silindi** | Ölçüm-D1 | `closing_commands()[0]` zaten `tarama`, ilk kırmızıda durur |
| `kusur-giderme` kapanış kontrolü #4 | **KUSUR-A2** | F-180 "✅ KAPANDI" işaretli, bir gün sonra aynı imzayla düştü |
| `ortak/kapilar.md` — `ic-dongu` kapsam sınırı | F-183 | `samples/AgentPrism.Samples.*` çözümde değil |
| `faz-denetim` 3.8 — "hiçbir kapı yakalamıyor" düzeltildi | C1'in sonucu | Artık yarı yanlıştı |
| `docs/hafiza/test-yalitimi.md` **yeni** | Sınıf 11 | `test-altyapisi.md` 17.337/16.000 B'yi aşmıştı; konu ayrımı yapıldı |
| `docs/ADAYLAR.md` F-180 başlığı → "VAKA KAPANDI, SINIF AÇIK" | KUSUR-A2 | §A7 |
| `docs/KARARLAR.md` Bölüm 1 — `-maxcpucount:1` | §C4 | Yeniden denemek 74 dakikalık bir koşuma mal oldu |

**Bütçe hamlesi.** Bu rapor `docs/kesif/`'i 376.792/370.000 B'ye çıkardı. Kural
gereği **içerik silinmedi, taşındı**: iki tükenmiş tur
`docs/arsiv/kesif/`'e alındı — `2026-08-20-maf-ekosistem-taramasi.md` (2026-08-26'nın
iki turu geçersizleştirdi) ve `2026-08-21-faz-adaylari-tespiti.md` (ürettiği 16
kalemin tamamı tek tek `grep`'lendi, hiçbiri öksüz değil). Sonuç: **341.973 B**.

