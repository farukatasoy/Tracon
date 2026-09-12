# Keşif Turu — 2026-08-23 · Yapısal Sorun Envanteri

> 🗄️ **ARŞİVLENDİ (2026-08-26) — tur tükendi.** Yirmi dört kalemin **yirmisi
> kapandı**, biri (madde 20) ölçümle **düştü**, biri (madde 15) kapsam dışı
> bırakıldı. Kalan iş [`ADAYLAR.md`](../../ADAYLAR.md)'ye taşındı:
> madde 16 → **F-67** · madde 9 → **F-165**. Bağlanmayan üç kalem:
> madde 1 (`Shipped.txt` dolumu) yayın kararına bağlıdır ve GA'ya ertelenmiştir
> (Faz 97); madde 14'ün uygulama yarısı (dağıtık hız sınırı) beyan edilmiştir
> ve gerçek bir çok örnekli tüketici talebi ölçülmeden kalemleşmez; madde 24 bir
> kullanıcı kararıdır ve yayın tarihi belirlenene kadar askıdadır.
> **Aşağıdaki metne artık bir iş listesi olarak bakılmaz** — bölüm 7.2'nin
> "sıradaki adım" satırı dahil her durum alanı bayattır. Yalnız `grep` hedefidir:
> bir iddianın nasıl ölçüldüğü ve neden reddedildiği burada durur. Faz durumunun
> tek kaynağı [`YOL-HARITASI.md`](../../YOL-HARITASI.md)'dir.
>
> Bu bir **koşum kaydıdır**, spec değildir. Kalemler tur sırasında kullanıcı
> tarafından seçilmediği için [`ADAYLAR.md`](../../ADAYLAR.md) dosyasına F
> numarası eklenmemişti; kapanışta iki kalem eklendi (yukarıya bakın).

**Tetikleyen:** Kullanıcının dört ayrı sorun listesini (bir oturum + üç ek
liste) tek envanterde birleştirme isteği.
**Yöntem:** Dört liste birleştirildi, tekrar edenler tek kaleme indirildi, her
iddia repo üzerinde **ölçüldü**. Ölçüm tutmayan iddialar bölüm 5'e alındı.
**Zemin:** Faz 89 kapalı · 90 faz kaydı · 0 git etiketi · ölçüm tarihi 2026-08-23.

---

## 1. Ölçülen zemin

| Ölçüm | Değer |
|---|---|
| Kod | `src/` 139.425 satır C# · `tests/` 80.438 satır · 2.897 test metodu |
| Doküman | `docs/` 143.671 satır Markdown · 222 dosya · `arsiv/` 4,3 MB |
| Public API | 6.301 girdi · tamamı `Unshipped` · `Shipped.txt` dosyalarının hepsi boş — 🚨 **2026-08-24'te yeniden ölçüldü: 8.063 girdi · 716 public tip**; 6.301 sayısı yanlıştı |
| Sürüm | 0 git etiketi · 337 commit · NuGet yayını yok |
| Faz | 90 kayıt · 89'u kapalı · Faz 7 (yayın) ⏸ beklemede |
| Aday | [`ADAYLAR.md`](../../ADAYLAR.md) 16 başlık · 8'i gerçekten açık |
| Manuel test | 1.315 case · 2,6 MB |
| Karar | [`KARARLAR.md`](../../KARARLAR.md) 657 satır · 596 `K-*` referansı |

**Sorunların çoğu kod satırında değil.** Uyarı bastırma gerekçeli (49 `#pragma`),
`ConfigureAwait(false)` yaygın, `async void` yok, `Skip=` yalnız 3 yerde,
contract test'ler üç SQL sağlayıcısını ortak kaynaktan koşuyor. Aşağıdaki
kalemler **yapı, doğrulama ve süreç** eksenindedir.

---

## 2. Özet tablo

Tip: **📋 Faz** = plan dokümanı ister · **⚡ Tek oturum** = bir oturumda kapanır ·
**🔁 Zincir** = birden çok faz.

| # | Sorun | Öncelik | Tip |
|---|---|---|---|
| 1 | 1.0 yok; public API tamamı `Unshipped` | P0 | 📋 Faz (Faz 7) |
| 2 | Stabil sürüm MAF ön sürümüne yapısal olarak bağlı | P0 | 📋 Faz (Faz 7'nin içinde) |
| 3 | Tekrarlayan kusur sınıfları yalnız dokümanla korunuyor | P0 | 📋 Faz |
| 4 | Senkronizasyon kopyası kapısı hâlâ insan dikkatinde | P0 | ⚡ Tek oturum |
| 5 | CI'da `secret` taraması yok | P0 | ⚡ Tek oturum |
| 6 | CI matrix'i Docker gerçeğiyle uyuşmuyor | P0 | ⚡ Tek oturum |
| 7 | Public yüzey yayın kararından önce şişti | P1 | 📋 Faz |
| 8 | Üç SQL dialect'inde ~7.000 satır elle yazılmış sorgu | P1 | 📋 Faz |
| 9 | 1.315 manuel case otomatikleşmedi | P1 | 🔁 Zincir |
| 10 | Yeşil test gerçek davranışı kanıtlamıyor | P1 | 📋 Faz |
| 11 | AOT beyanı paket gerçeğiyle uyuşmuyor | P1 | ⚡ Tek oturum |
| 12 | Varsayılan yol kalıcı değil ve çalışma anında uyarmıyor | P1 | ⚡ Tek oturum |
| 13 | Varsayılan store'un conformance testi Docker'a bağlı | P1 | ⚡ Tek oturum |
| 14 | Çok kiracılı üretim sığ (RLS yok, hız sınırı bellekte) | P1 | 📋 Faz |
| 15 | NSwag üretimi client komple susturulmuş | P1 | ⚡ Tek oturum |
| 16 | Performans korunmuyor; benchmark projesi yok | P1 | 📋 Faz (F-67) |
| 17 | Karmaşıklık birkaç dev dosyada yoğunlaşmış | P2 | 📋 Faz 105–108 |
| 18 | Frontend ekranları ve sözlükler monolitleşiyor | P2 | 📋 Faz 109 |
| 19 | Frontend test kapsamı ince | P2 | 📋 Faz 109 |
| 20 | Doküman yükü kodu geçti | P2 | 🔁 Zincir |
| 21 | Yol haritası üretecinde görünür kusur | P2 | ⚡ Tek oturum |
| 22 | Bağımlılık kirliliği kuralı kendi istisnasını taşıyor | P2 | ⚡ Tek oturum |
| 23 | Bus factor = 1; topluluk giriş rampası yok | P2 | 📋 Faz |
| 24 | Kapsam genişliyor, konsolidasyon ertelenmiyor | P2 | (karar) |

---

## 3. P0 — Yayın ve güvenlik kapıları

### 1. 1.0 yok; public API'nin tamamı `Unshipped` · 📋 Faz

**Ölçüm:** `git tag` boş (0 etiket). Her `PublicAPI.Shipped.txt` 1 satır (boş).
6.301 API girdisinin tamamı `Unshipped`. 🚨 **Yeniden ölçüm (2026-08-24): 8.063 girdi**, arkasında **716 public tip**. Dolum listesi tahmin edilenden büyüktür. [Faz 7](../fazlar/07-SAGLAMLASTIRMA-VE-YAYIN.md)
2026-08-02'den beri ⏸ beklemede (K-068).

**Neden P0:** Bu bir NuGet paket ailesidir. API tasarımı hiçbir gerçek tüketiciyle
sınanmadı. Fazın kendi dokümanı "yayın geciktikçe ilk dolum büyür" diyor; 21 gün
ve 82 faz sonra hâlâ büyüyor. Diğer sorunların çoğu bunun türevi.

**Neden faz:** `Shipped.txt` dolumu 6.301 girdinin tek tek gözden geçirilmesidir.
Her faz dokümanının "Gerçekleşen Public API" bölümü kaynaktır. Sürüm politikası,
`README` ve `docs-site` senkronu da kapsamdadır.

---

### 2. Stabil sürüm MAF ön sürümüne yapısal olarak bağlı · 📋 Faz (Faz 7 içinde)

**Ölçüm:** [`Directory.Packages.props:37-51`](../../../Directory.Packages.props) —
`Microsoft.Agents.AI.Hosting` **1.18.0-preview**, `.Hosting.OpenAI` **1.18.0-alpha**,
`.Hosting.A2A` / `.Hosting.AspNetCore` preview, `A2A.AspNetCore` preview2.
`README.md` bunu doğru beyan ediyor: "Tracon publishes as `1.0.0-preview.N`
until both reach GA."

**Neden P0:** Bu, kalem 1'in **kontrol dışı** bileşenidir. Faz 7 bitse bile stabil
1.0 çıkamaz — NuGet'te stabil paket ön sürüm bağımlılığı taşıyamaz. Karar şudur:
`1.0.0-preview.N` ile çıkmak, yoksa MAF GA'sını beklemek. İkincisi seçilirse
bekleme süresi kimsenin takviminde değildir.

**Kapsam sınırı:** K-008 preview bağımlılığını yalnız `Tracon.AspNetCore`
içinde tutuyor. Diğer 20 paket GA bağımlılıklıdır ve **ayrı** stabil sürümle
çıkabilir. Bu ölçülmemiş bir seçenektir.

---

### 3. Tekrarlayan kusur sınıfları yalnız dokümanla korunuyor · 📋 Faz

> **Durum:** 📋 Planlandı (2026-08-23) — script ile yakalanabilen kalemler
> [Faz 91](../fazlar/91-GELISTIRME-DONGUSU-KAPILARI.md)'e girdi; kalanı
> [Faz 93](../fazlar/93-KUSUR-SINIFI-KAPILARI.md)'tür.
>
> **Plan anında ölçülen düzeltme:** (a) kuralı **sevk edilen bir analyzer kuralı
> olarak yazılamaz** — "async metotta ambient yazımı" bugünkü kodda altı kez öter
> ve altısı da doğrudur (`AsyncLocal` yazımı aşağı akar, yukarı akmaz). Faz 93
> sınıfı üçe böler: `TRC0501` (akışlı yolda yineleme dışı yazım) ve `TRC0502`
> (ambient kapsamın `IDisposable`'ı atıldı) sevk edilir; yardımcı-metot vakası
> taban çizgili bir repo kapısı olur. (b) kuralının C# tarafı **zaten kapalıdır**
> (`RunCost.Total()`, `CostTotals.Total()`); canlı 18 vakası SQL metnindedir ve
> [Faz 94](../fazlar/94-SQL-TEK-KAYNAK.md)'e gitti. Playwright locator sınıfı (3 tekrar)
> Faz 93'e eklendi.

**Ölçüm:** [`docs/hafiza/cekirdek-calistirma.md:18`](../../hafiza/cekirdek-calistirma.md)
`AsyncLocal` / `Activity.Current` kusurunun **üç vakasını** kaydediyor (Faz 6, 11, 12);
[`MEMORY.md`](../../../MEMORY.md) senkronizasyon kopyasını **beş kez** yaşandı diye
yazıyor. K-483: elle tekrarlanan `InputCost + OutputCost` ifadesine üçüncü terim
eklenince yalnız SQL düzeltildi ve bir kiracı maliyet tavanını aşabilirdi —
4.241 test yakalamadı, bağımsız denetim buldu.

**Neden P0:** Koruma bugün "bir sonraki oturumun doğru hafıza dosyasını okuması"
şartına bağlı. Bu bir kapı değil, bir umuttur. Kanıtlanmış kusur sınıfı bu şekilde
kapanmaz.

**Neden faz:** [`Tracon.Generators`](../../../src/Tracon.Generators) zaten
analyzer taşıyor (`APG*`). Kural yazımı, tanı kodu, test ve `.editorconfig`
severity kaydı gerektirir. En az üç kural adayı var: (a) `async` metotta
`AsyncLocal`/`Activity` yazımı, (b) toplama alan `record`'da elle yazılmış toplam
ifadesi, (c) `IAsyncEnumerable` gövdesinde `scope` yazımı.

---

### 4. Senkronizasyon kopyası kapısı hâlâ insan dikkatinde · ⚡ Tek oturum

> **Durum:** ✅ Kapandı (2026-08-23, aynı oturumda) — CI'a
> `.github/workflows/ci.yml` "Senkronizasyon kopyası taraması" adımı eklendi.

**Ölçüm:** `<ad> 2.<uzantı>` kopyaları beş kez yaşandı; Faz 57 kopyaları
**commit etti** ve `main` derlenmedi (K-411). Kapı bugün `faz-tamamlama` Adım 1 —
yani bir skill adımı. `git status` temiz görünür (kopya izleniyordur).

**Neden P0:** `main`'i kıran, kanıtlanmış, sıfır maliyetle kapanabilen bir sınıf.

**Neden tek oturum:** CI'a bir adım eklemek yeterli:
`find . -name "* 2.*" -not -path "./.git/*" -not -path "*/node_modules/*"` —
bulgu varsa job kırılır. Bir pre-commit hook ikinci savunma hattıdır.

---

### 5. CI'da `secret` taraması yok · ⚡ Tek oturum

> **Durum:** ✅ Kapandı (2026-08-23, aynı oturumda) — CI'a
> `.github/workflows/ci.yml` "Secret taraması" adımı eklendi;
> `faz-tamamlama`'daki elle koşulan desenin birebir aynısı.

**Ölçüm:** [`ci.yml`](../../../.github/workflows/ci.yml) içinde `gitleaks`,
`trufflehog` veya `secret-scan` geçen **sıfır** satır. `secret` taraması yalnız
`faz-tamamlama` protokolünde, elle.

**Neden P0:** K-059 (`secret` veritabanına da yazılmaz) ve `dotnet user-secrets`
disiplini doğru kurulmuş. Ama tek koruma commit yapan tarafın dikkatidir. Bir
`secret` `main`'e girdikten sonra git geçmişinden temizlemek pahalıdır ve depo
özel olsa bile anahtarın döndürülmesi gerekir.

**Neden tek oturum:** `gitleaks-action` veya GitHub secret scanning'i açmak +
baseline dosyası + bir kabul case'i.

---

### 6. CI matrix'i Docker gerçeğiyle uyuşmuyor · ⚡ Tek oturum

> **Durum:** ✅ Kapandı (2026-08-23, aynı oturumda) — yeni
> [`Tracon.no-docker.slnf`](../../../Tracon.no-docker.slnf) (K-268
> desenini izler) `Tracon.PostgreSql.IntegrationTests` ve
> `Tracon.SqlServer.IntegrationTests`'i dışlar; `windows-latest` job'u
> artık `dotnet test Tracon.slnx` yerine bu filtreyi koşar. Kalem 13'ün
> taşıması sayesinde InMemory sözleşme testleri bu filtrede de yaşıyor.

**Ölçüm:** [`ci.yml`](../../../.github/workflows/ci.yml) `dotnet test Tracon.slnx`
komutunu `ubuntu-latest` **ve** `windows-latest` üzerinde koşuyor. Integration
projeleri Testcontainers ile Linux image başlatıyor (`pgvector/pgvector:pg18`).
Testlerde tek bir OS/Docker guard yok — `OSPlatform` / `IsWindows` taraması
**boş** döndü.

**Neden P0:** Windows runner Linux container koşamaz. İki sonuçtan biri doğrudur:
Windows job'u kırık, ya da yeşil görünen bir şey aslında koşmuyor. İkisi de
doğrulama kapısının anlamını yok eder. (Koşum geçmişi kontrol edilemedi — `gh`
kurulu değil.)

**Neden tek oturum:** Ya integration projeleri Windows job'undan dışlanır
(`--filter` veya ayrı `.slnf`), ya da fixture'lara Docker guard eklenir. İkinci
kazanç: CI'ı unit / integration / E2E olarak ayırmak geri bildirim döngüsünü
kısaltır — bugün Docker isteyen 4 proje **her** koşuda hatta.

---

## 4. P1 — Yayın öncesi kapanması gerekenler

### 7. Public yüzey yayın kararından önce şişti · 📋 Faz

> **Durum:** 📋 Planlandı (2026-08-24) — [Faz 96](../fazlar/96-PUBLIC-YUZEY-KUCULTME.md).
>
> 🚨 **Plan anında bölüm 7.3'ün önerdiği ölçüt YANLIŞLANDI.** "`src/` dışından
> referans almayan tip `internal`'a çekilir" mekanik olarak uygulanamaz: ad
> araması üç yanlış pozitif sınıfı üretir — uzatma metodu sınıfı (çağrı yerinde
> adı yazılmaz; hiçbir yerde geçmeyen 57 tipin **22'si** `*Extensions`),
> öznitelik tipi (`[TraconTool]` yazılır, `TraconToolAttribute` değil) ve
> `<see cref>` yorumu (kullanım değildir ama `internal` olunca CS1574 üretir).
> Faz 96 bunun yerine **erişilebilirlik** ölçütünü kullanır: başka bir public
> üye imzasında geçmeyen tip yapraktır. Ölçüldü: 714 tipin **129'u** yaprak,
> 23'ü `interface` (genişleme noktası), kalan **106** aday havuzu. Elle
> doğrulamayla kapsama giren aday sayısı **96**'dır. 6.301 API girdisi; 4.761'i tek pakette
([`Tracon.Abstractions`](../../../src/Tracon.Abstractions/PublicAPI.Unshipped.txt)).
68 interface, 76 `Options` sınıfı, 1.502 property.

> 🚨 **Yeniden ölçüm (2026-08-24):** toplam **8.063** girdi. Asıl hedef girdi
> değil **tiptir: 716 public tip**, 339'u `Abstractions`'ta. Kalan girdinin
> büyük kısmı `Options` erişimcisidir — yalnız `Abstractions`'ta 1.250 `get`
> + 1.170 `set/init`. Bunlar tip kaldırılmadan küçülmez. Yüzey küçültme
> fazının gözden geçireceği liste 716 satırdır, 8.063 değil.

**Neden P1:** Yüzeyi küçültmenin bedeli bugün sıfır, yayından sonra kırıcı
değişikliktir. Kalem 1'den **önce** koşmalıdır: `internal`'a çekilebilecek her
tip, dolum listesini kalıcı olarak küçültür.

---

### 8. Üç SQL dialect'inde ~7.000 satır elle yazılmış sorgu · 📋 Faz

> **Durum:** 📋 Planlandı (2026-08-23) — [Faz 94](../fazlar/94-SQL-TEK-KAYNAK.md).
>
> **Plan anında yapılan ölçüm** ("ölçüm ve karar ister" maddesinin cevabı):
> 199 ortak sorgunun **117'si** (%59) üç dialect'te özdeştir — ama şema
> niteleyicisi yüzünden bugün **hiçbiri** paylaşılamaz (PostgreSQL/SQL Server
> `{Schema}.runs`, SQLite `{Schema}runs`). Bu 117 sorgu, sorgu bloklarının yalnız
> **%37**'sidir (dialect başına ~610 satır): birleştirme ~**1.220** satır düşürür,
> 7.000'in tamamını değil. Kalan 82 sorgu gerçekten farklıdır ve birleştirilmez.
> Asıl kazanç satır değil kusur sınıfıdır: maliyet toplama ifadesi **18 yerde**
> elle yazılıdır. Faz 94 iki ekseni birden alır.

**Ölçüm:** [`SqlServerQueries.cs`](../../../src/Tracon.SqlServer/Internal/SqlServerQueries.cs)
2.477 + [`PostgresQueries.cs`](../../../src/Tracon.PostgreSql/Internal/PostgresQueries.cs)
2.282 + [`SqliteQueries.cs`](../../../src/Tracon.Sqlite/Internal/SqliteQueries.cs)
2.241 satır. Query adları ve döndürülen sütun sırası özdeş; yalnız metin farklı.

**Neden P1:** K-483'ün doğduğu yapı budur. Dördüncü sağlayıcı borcu %33 büyütür.
Contract test'ler davranışı koruyor (güçlü yan), ama **eklenmemiş** bir terimi
hiçbir test yakalayamaz.

**Neden faz:** Çözüm seçenekleri farklı maliyetlerde — ortak sorgu ağacı + dialect
emitter, ya da yalnız aggregate ifadeleri için tek kaynak. Ölçüm ve karar ister.

---

### 9. 1.315 manuel case otomatikleşmedi · 🔁 Zincir

**Ölçüm:** `docs/manuel-test/` 2,6 MB, 1.315 case başlığı, 24 aile. Koşum kendi
skill'ini gerektiriyor (`manuel-test-kosumu`, 316 satır).

**Neden P1:** Bir kütüphanenin regresyon güvencesi CI'da koşmalıdır. İnsan zamanı
isteyen bir set, yayın baskısı altında ilk atlanan şeydir. Ayrıca bu set
tek kişiye kilitlidir (bkz. kalem 23).

**Neden zincir:** 1.315 case tek fazda otomatikleşmez. Aile aile taşınır; her
faz bir aileyi kapatır ve manuel setten siler. Öncelik sırası: kiracı/güvenlik
(13), çekirdek (02), kalıcılık (03/04).

---

### 10. Yeşil test gerçek davranışı kanıtlamıyor · 📋 Faz

> **Durum:** 📋 Planlandı (2026-08-24) — [Faz 95](../fazlar/95-GERCEK-TUKETICI-KAPISI.md).
>
> 🚨 **Plan anında bu maddenin kanıtı KISMEN YANLIŞLANDI.** "Paket olarak
> tüketilebiliyor mu" sorusu zaten kapılıdır: `Tracon.Templates.Tests`
> çözümü `pack` eder, `artifacts/package/release`'i yerel feed yapar, global
> paket önbelleğini temizler ve `PackageReference` ile beslenen bir proje
> üretir. Üstünde 10 case koşuyor — derleme sıfır uyarı, uygulama ayağa
> kalkıyor, `buildTransitive/` hedefleri paket üzerinden akıyor. Hepsi CI'da,
> `Skip` yok. **Gerçek boşluk tek cümleye indi:** paket tüketicisi üzerinden
> hiçbir **gerçek `run`** koşulmuyor — `TemplateRunTests` yalnız katalog ucunu
> çağırıyor, `POST .../run` hiçbir yerde geçmiyor (ölçüldü, sıfır sonuç).
> Faz 95 yalnız o boşluğu kapatır.

**Ölçüm:** [`MEMORY.md`](../../../MEMORY.md) — "sekiz fazda gerçek hatalar **yalnız**
örnek uygulamada çıktı; hepsi testlerden geçmişti" (K-166, K-167). Test dağılımı
ters piramit: 1.109 unit, 622 functional, 199 integration (Postgres 104 · Sqlite 55 ·
SqlServer 40), 57 E2E.

**Neden P1:** Kapı bugün "derlendi" der, "çalışır" demez. Yalnız 2 sample
uygulama var ve ikisi de CI'da **çalıştırılmıyor** (yalnız derleniyor).

**Neden faz:** Çözüm bir smoke-run kapısıdır: sample uygulamayı CI'da ayağa
kaldır, gerçek bir `run` koştur, `run` kaydını doğrula. Fake model provider zaten
var (`Tracon.Testing`).

---

### 11. AOT beyanı paket gerçeğiyle uyuşmuyor · ⚡ Tek oturum

> **Durum:** ✅ Kapandı (2026-08-23, aynı oturumda) — `docs-site/reference/compatibility`
> ve `packages.md` zaten doğruydu (ölçüldü); eksik olan tek yer `README.md`'ydi.
> README'ye "AOT compatibility" bölümü eklendi, site sayfasına bağlantı verdi.

**Ölçüm:** **12** csproj `AotCompatible=false` taşıyor —
`AspNetCore`, `Cli`, `Client`, `SqlServer`, `Templates`, `UI`, `Mcp`,
`Generators`, `Sqlite`, `Workflows`, `Testing` ve çatı paketi **`Tracon`**.
`Tracon.Client` AOT denemesi 146 `IL2026`/`IL3050` tanısıyla terk edildi (K-567).
SQLite AOT/publish ölçümü yapılmadı (K-196).

**Neden P1:** Çatı paketi `Tracon`'i referans alan tüketici, "AOT uyumlu paket
ailesi" algısıyla gelirse duvara çarpar. Bu bir doğruluk sorunudur, kod sorunu değil.

**Neden tek oturum:** Paket bazında yetenek matrisini `README` ve `docs-site`'ta
beyan etmek. Kod değişmez.

---

### 12. Varsayılan yol kalıcı değil ve çalışma anında uyarmıyor · ⚡ Tek oturum

> **Durum:** 📋 Planlandı (2026-08-25) — [Faz 104](../fazlar/104-BEYAN-DOGRULUGU-VE-GIRIS-RAMPASI.md). Kapsam **yalnız uyarı log'udur**
> (👤 karar); hata fırlatılmaz, seçenek eklenmez — kalıcı olmayan store
> desteklenen bir moddur. 🚨 Yeniden ölçüldü 2026-08-25: arayüz zaten dürüst
> (`settings.tsx:129`, `settings.inMemoryNotice`). Eksik olan **sunucu tarafı**
> sinyalidir; `IsProduction` kontrolü kodda hiç yoktur.

**Ölçüm:** [`TraconServiceCollectionExtensions.cs:514-605`](../../../src/Tracon.Core/TraconServiceCollectionExtensions.cs#L514-L605)
**27** InMemory store kaydeder (toplam 5.382 satır). Uyarı yalnız bir XML
yorumunda: "Use `Tracon.PostgreSql` in production". Diagnostics
`persistenceProvider = "InMemory"` raporluyor ama bunu bir teşhis uyarısına
çevirmiyor.

**Neden P1:** Persistence paketi eklemeyen tüketicinin control plane'i process
belleğinde çalışır: restart'ta veri kaybı, tek instance, sınırsız bellek büyümesi.

**Neden tek oturum:** Sağlık/teşhis yüzeyi zaten var
([`TraconDiagnosticsCollector.cs:108`](../../../src/Tracon.Core/Diagnostics/TraconDiagnosticsCollector.cs#L108)).
`Production` ortamında InMemory tespit edilirse bir `Warning` tanısı üretmek yeterli.

---

### 13. Varsayılan store'un conformance testi Docker'a bağlı · ⚡ Tek oturum

> **Durum:** ✅ Kapandı (2026-08-23, aynı oturumda).

**Ölçüm (tespit anında):** `InMemoryStoreContractTests.cs`
`tests/Tracon.PostgreSql.IntegrationTests/Contracts/` altındaydı; XML'i
"It requires no database" diyordu, ama dosya PostgreSQL integration
projesindeydi ve o assembly `[assembly: AssemblyFixture(typeof(PostgresFixture))]`
taşıyordu.

**Çözüm:** Dosya
[`tests/Tracon.Core.UnitTests/Contracts/InMemoryStoreContractTests.cs`](../../../tests/Tracon.Core.UnitTests/Contracts/InMemoryStoreContractTests.cs)'a
taşındı; `Tracon.Core.UnitTests.csproj` paylaşılan sözleşme testlerini
(`TenantCoverageTests.cs` hariç — o `typeof(SqlRunStore)` ile SQL'e sabitli)
bağladı. 1.801 test, Docker olmadan yeşil.

**Neden P1 idi:** Docker'sız makinede, en çok kullanılacak store'un 5.382
satırlık implementasyonu hiç sınanmıyordu. Doküman ile gerçek çelişiyordu.

**Neden tek oturum:** Dosyayı Docker istemeyen bir projeye taşımak
(`Tracon.Core.UnitTests` + paylaşılan `tests/Shared` referansı). Kalem 6'nın
CI ayrıştırmasıyla birlikte koşulmalı.

---

### 14. Çok kiracılı üretim sığ · 📋 Faz

> **Durum:** 📋 KISMEN planlandı (2026-08-25) — [Faz 104](../fazlar/104-BEYAN-DOGRULUGU-VE-GIRIS-RAMPASI.md). Blok C'den **öne
> çekildi** (👤 karar). Faza giren: RLS **kararı** (uygulama katmanı tek hat
> kalır) ve hız sınırının kapsam beyanı. Faza girmeyen: RLS uygulaması, dağıtık
> hız sınırı.
> 🚨 Yeniden ölçüldü 2026-08-25 ve **iddianın yarısı düştü**: hız sınırının tek
> süreç oluşu zaten beyan edilmiştir — `InboundTriggerRateLimiter.cs:8-13` "PER
> INSTANCE" yazar ve `guides/inbound-triggers.md:156-160` bir `caution` bloğu
> taşır. Gerçek boşluk ikidir: `TraconRateLimitOptions` XML'i ("lives in
> memory" der, çok örnekli kapsamı yazmaz) ve `guides/production.md` (hiç
> geçmez). RLS kararı ise hiçbir yerde kayıtlı değildi — `KARARLAR.md`,
> `MIMARI-GUVENLIK.md` ve `docs-site/` taramaları **0** döndü.

**Ölçüm:** `ROW LEVEL SECURITY` taraması `src/` altında **0** sonuç — kiracı
yalıtımı tümüyle uygulama katmanındadır (`ITenantContext` + `TenantIsolationTests`).
[`InboundTriggerRateLimiter`](../../../src/Tracon.Core/Triggers/InboundTriggerRateLimiter.cs)
process belleğindedir. SQL Server gerçek `mssql/server` üzerinde doğrulanmadı (K-186).

**Neden P1:** Çok örnekli kurulumda kota ve kilit davranışı tek süreç varsayımına
yaslanır. Faz 42 (tek yürütücü seçimi) bunu kısmen ele aldı, hız sınırı almadı.

**Not:** RLS bilinçli bir tercih olabilir (K-014 ve FK kararına benziyor). Faz,
önce **kararı** kaydetmeli: RLS savunma derinliği olarak eklenecek mi, yoksa
uygulama katmanı tek hat mı kalacak?

---

### 15. NSwag üretimi client komple susturulmuş · ⚡ Tek oturum

> **Durum:** ❌ Kapsam dışı (2026-08-25, 👤 karar). Yeniden ölçüldü ve kalem
> zayıf çıktı: 14 `#pragma` **NSwag'ın kendi standart başlığıdır** ve hepsi CS
> **derleyici** uyarısıdır (CS0108/114/472/1591/8603…), analyzer kuralı değil.
> Üretilen istemcinin drift'i zaten üç kapıyla korunuyor: OpenAPI snapshot ·
> `ClientCoverageTests` · `ClientDescriptionBaselineTests` (Faz 83, K-424).
> Kazanç düşük, iş orta — kalem kapandı.

**Ölçüm:** [`TraconApiClient.g.cs`](../../../src/Tracon.Client/Generated/TraconApiClient.g.cs)
25.128 satır, **14** `#pragma warning disable` (10'u ilk 20 satırda, nullability dahil).

**Neden P1:** `TreatWarningsAsErrors` tüm repoda geçerliyken en çok tüketilen
yüzeylerden biri analyzer kör noktasıdır. NSwag yeniden üretiminde davranış
kayması sessiz geçer.

**Neden tek oturum:** Üretilen dosyayı `<Compile>` üzerinden ayrı bir analyzer
profiliyle derlemek, ya da schema-drift testini genişletmek. TypeScript client
tarafında bu kapı zaten var (`schema-drift` testi) — .NET tarafında yok.

---

### 16. Performans korunmuyor · 📋 Faz (F-67 zaten açık)

**Ölçüm:** Repo'da **benchmark projesi yok** (`*benchmark*` taraması boş). Dört
doğrulama kapısı yalnız **doğruluğu** korur. Sıcak yol tahsisi, `run_events`
yazımı ve arama yolu için taban çizgisi yok. SQLite'ta yük altında `SQLITE_BUSY`
davranışı ölçülmedi.

**Neden P1:** Kütüphanedir; tüketicinin sıcak yolunda çalışır. Bir regresyon
sürüm sonrası fark edilir.

---

## 5. P2 — Yayından sonra veya paralel

### 17. Karmaşıklık birkaç dev dosyada yoğunlaşmış · 📋 Faz

> **Durum:** 📋 Dört faza ayrıldı (2026-08-26) — [Faz 105](../fazlar/105-DI-BILESEN-KOKU-AYRISTIRMA.md) DI composition root · [Faz 106](../fazlar/106-AGENT-DERLEYICI-AYRISTIRMA.md) compiler · [Faz 107](../fazlar/107-RUN-KAYIT-AKISI-AYRISTIRMA.md) run recording · [Faz 108](../fazlar/108-BELLEK-ICI-RUN-STORE-AYRISTIRMA.md) in-memory run store. Tek mega refactor reddedildi; dört dosya farklı sözleşme ve test sınırı taşır.

[`TraconServiceCollectionExtensions.cs`](../../../src/Tracon.Core/TraconServiceCollectionExtensions.cs)
2.662 satır / 47 metot · [`AgentDefinitionCompiler.cs`](../../../src/Tracon.Core/Compilation/AgentDefinitionCompiler.cs)
1.617 satır · [`RunRecordingAgent.cs`](../../../src/Tracon.Core/Recording/RunRecordingAgent.cs)
1.331 satır · [`InMemoryRunStore.cs`](../../../src/Tracon.Core/Storage/InMemoryRunStore.cs) 1.432 satır.

Her yeni yetenek DI dosyasına dokunuyor; `TryAdd*` sırası gözden kaçma riski
dosyayla birlikte büyüyor. Faz 20'de 1.068 testin kaçırdığı imza–gövde kusuru tam
bu tür dosyalarda doğdu. Faz 89'un devir notu "tool wrapper zinciri dört halkaya
çıktı" diyor — bu bir refactor sinyalidir.

### 18. Frontend ekranları ve sözlükler monolitleşiyor · 📋 Faz

> **Durum:** 📋 [Faz 109](../fazlar/109-FRONTEND-MODULLERI-VE-EKRAN-TESTLERI.md) içinde kalem 19 ile birleşti (2026-08-26). Screen ve catalogue modül sınırları component-test harness'inin doğal test sınırıdır.

`agent-editor.tsx` 1.243 · `en.ts` 1.222 · `tr.ts` 1.213 · `playground.tsx` 944
satır. `Messages` tipi eksik anahtarı derleme anında yakalıyor (güçlü yan), ama
ekran başına bileşen ayrışması yapılmadıkça her yeni özellik bu dosyaları büyütür.

### 19. Frontend test kapsamı ince · 📋 Faz

> **Durum:** 📋 [Faz 109](../fazlar/109-FRONTEND-MODULLERI-VE-EKRAN-TESTLERI.md) içinde kalem 18 ile birleşti (2026-08-26). Güncel ölçüm: 24.011 TypeScript satırı · 28 screen · 23 component · 14 Vitest dosyası · gerçek build'de 172 case · 57 E2E.

24.011 satır TypeScript · 28 screen · 23 component. Buna karşılık 14 test dosyası,
172 Vitest case'i ve 57 E2E testi. Ekranların çoğunun otomatik testi yok; güvence
manuel sete dayanıyor (bkz. kalem 9).

### 20. Doküman yükü kodu geçti · 🔁 Zincir

> **Durum:** 📋 KISMEN planlandı (2026-08-23) — kopyalanan komut ve
> regex'lerin tek kaynağa inmesi [Faz 91](../fazlar/91-GELISTIRME-DONGUSU-KAPILARI.md)'de;
> skill metinlerinin konsolidasyonu [Faz 92](../fazlar/92-ZINCIR-KONSOLIDASYONU.md)'ye ayrıldı.

`docs/` 143.671 satır Markdown; `src/` 139.425 satır C#. `KARARLAR.md` 657 satır /
596 `K-*` · `ADAYLAR.md` 793 satır · `arsiv/` 4,3 MB. Bunu yönetmek için özel bir
script (`dokuman-bakim.py`) ve 10 skill gerekiyor. Bakım aparatının kendisi artık
bakım isteyen bir sistemdir.

### 21. Yol haritası üretecinde görünür kusur · ⚡ Tek oturum

> **Durum:** ✅ Kapandı (2026-08-23, aynı oturumda) — `ham[:40]` kırpması
> kaldırıldı, `YOL-HARITASI.md` yeniden üretildi.

[`YOL-HARITASI.md:39`](../../YOL-HARITASI.md) Faz 24 satırı kesik:
`Kod tamam · 205/205 sözleşme+diyalekt te`. Kaynak dosya
`✅ Kod tamam · 205/205 sözleşme+diyalekt testi yeşil · AOT ölçülmedi` diyor.
Sebep: [`dokuman-bakim.py:456`](../../../scripts/dokuman-bakim.py#L456) — kısaltma
sözlüğüyle eşleşmeyen durum `ham[:40]` ile kesiliyor. "Tek kaynak, elle yazılmaz"
iddiasındaki dosya bozuk çıktı üretiyor.

### 22. Bağımlılık kirliliği kuralı kendi istisnasını taşıyor · ⚡ Tek oturum

> **Durum:** 📋 Planlandı (2026-08-24) — [Faz 95](../fazlar/95-GERCEK-TUKETICI-KAPISI.md)
> bölüm 95.3. Faz 95'in tüketici fikstürü zaten kuruluydu; kalem oraya bindi.
> Plan iki eksen alır: geçişli kapanışı bir taban çizgisine bağlayan kapı, ve
> `docs-site/packages.md`'deki beyan. Ölçüldü: `packages.md`'nin
> `## What does not enter your graph` bölümü yalnız **girmeyeni** sayıyor;
> `Google.GenAI` üzerinden geleni hiç yazmıyor.

[`Directory.Packages.props:100`](../../../Directory.Packages.props) — `Google.GenAI`
üzerinden `Newtonsoft.Json`, `System.Management` ve `System.CodeDom` geçişli
geliyor. Yorum "bilerek kabul edildi" diyor. Kural ("tüketicinin bağımlılık
grafiğini kirletme") ile pratik çelişiyor; en azından `docs-site`'ta paket başına
beyan edilmeli.

### 23. Bus factor = 1 · 📋 Faz

> **Durum:** 📋 Planlandı (2026-08-25) — [Faz 104](../fazlar/104-BEYAN-DOGRULUGU-VE-GIRIS-RAMPASI.md). Kapsam: kökte **İngilizce**
> `CONTRIBUTING.md` ve `ARCHITECTURE.md` (👤 karar). Kalemin kendisi ("bus
> factor = 1") bir doküman fazıyla çözülmez; faz yalnız **giriş rampasını**
> kurar. 🚨 Dil kapısı bugün kök dosyaları görmüyor (`SourceLanguageTests.cs:53`
> yalnız `src|packages/*/README.md` tarar); faz regex'i genişletir.

Tüm mimari bilgi Türkçe `docs/` ağacına ve tek bir kişinin oturum akışına kilitli.
Süreç insan katkıcıya değil AI oturumuna optimize. ``COMMERCIAL.md``
36 satır — lisans/ticari model var, topluluk katkısı alacak giriş rampası
(`CONTRIBUTING.md`, İngilizce mimari özeti, "good first issue") yok.

### 24. Kapsam genişliyor, konsolidasyon ertelenmiyor · (karar)

90 faz + 8 açık aday + 2026-08-23'te üretilmiş
[10 yeni fikir](../../kesif/2026-08-23-yeni-feature-fikirleri.md). Aynı anda: 0 sürüm,
2 sample, 12 paket AOT-dışı, 1.315 manuel case. Yeni özellik üretimi
konsolidasyondan hızlı. Bu bir kod sorunu değil, bir **öncelik kararıdır** ve
yalnız kullanıcı verebilir.

---

## 6. Doğrulanamayan iddialar

Ekli listelerden gelen, ölçümle **tutmayan** kalemler. Kayda geçiriliyor ki
tekrar önerilmesinler.

| İddia | Ölçüm | Sonuç |
|---|---|---|
| "Skill'ler iki yerde tutuluyor; `.claude/skills` ile `.agents/skills` birebir kopya" | `.claude/skills` bir **symlink**'tir → `../.agents/skills` | ❌ Yanlış. `AGENTS.md`'nin symlink kuralı **uygulanmış** |
| "Seçilmemiş 29 aday duruyor" | `ADAYLAR.md`'de 16 başlık; 8'i gerçekten açık (F-48, F-51, F-67, F-72, F-106, F-109, F-144, F-145) | ❌ Sayı yanlış |
| "263 markdown dosyası" | `docs/` altında 222 dosya | ⚠️ Kaynak belirsiz |
| "`AsyncLocal` kusuru 4/5 kez" | `cekirdek-calistirma.md` **üç vaka** kaydediyor (Faz 6/11/12). "Beş kez" **senkronizasyon kopyası** kalemine aittir | ⚠️ İki kalem karışmış |
| "NSwag'de 13 `#pragma`" | 14 | ⚠️ Küçük sapma |
| "RLS yok → F-90", "hız sınırı bellekte → F-92" | Bu F numaraları `ADAYLAR.md`'de yok | ⚠️ Alt iddia (RLS yok) **doğru**, F numarası bayat |
| "Faz 24 ✅ değil" | Kaynak dosya `✅` taşıyor; **üreteç** kesiyor | ✅ Gerçek kusur, sebebi farklı (kalem 21) |

---

## 7. Sıra ve devir notu — karara bağlandı (2026-08-24) 👤

Bu bölüm bir **öneri değil, kayıttır**. Kararlar kullanıcı tarafından verildi.
Sonraki oturumlar bu bölümü tek başına okuyup çalışabilmelidir; bölüm 1–6 arka
plandır, buraya bakmadan bir kalem seçilmez.

### 7.1 Verilen kararlar

| # | Karar | Gerekçe |
|---|---|---|
| 1 | **Sürüm hattı:** paketlenen **19** projenin hepsi tek hatta `1.0.0-preview.N` | Stabil paket ön sürüm bağımlılığı taşıyamaz (madde 2). Ayrık hat (20 stabil + 1 preview) `Abstractions` ve `Core`'u anında dondurur ve madde 7'yi acil + pahalı yapar. Preview hattında yüzey küçültme kırıcı değişiklik sayılmaz |
| 2 | **Sıra:** tüketici kapısı → yüzey küçültme → yayın | Yüzey küçültme, gerçek tüketiciyi kırmaya en yatkın değişikliktir; onu koruyacak dedektör **önce** kurulur |
| 3 | **Kapsam:** ilk yayına kadar yeni yetenek üretimi **durur** (madde 24) | 94 faz, 0 yayınlanmış sürüm. Her yeni faz public yüzeye ekliyor ve dolum listesini büyütüyor. `ADAYLAR.md`'deki 8 açık kalem yayından sonra değerlendirilir |
| 4 | **Madde 4:** smoke-run yayın kapısına; benchmark ve manuel set devri yayından sonraya | Ölçüldü: benchmark projesi yok, CI'da `sample`/`smoke` geçen sıfır satır var, `docs/manuel-test/` 2,3 MB duruyor |

### 7.2 Uygulama sırası — bağlayıcı 👤

Her adım **ayrı bir oturumda** yapılır. Adım atlanmaz; sıra kullanıcı
tarafından sabitlendi.

| Sıra | Adım | Skill |
|---:|---|---|
| 1 | **Faz 95 uygulama** — plan hazır: [`95-GERCEK-TUKETICI-KAPISI.md`](../fazlar/95-GERCEK-TUKETICI-KAPISI.md) | `faz-baslangic` → `faz-uygulama` → `faz-denetim` → `faz-tamamlama` |
| 2 | ~~**Faz 96 yazma**~~ ✅ 2026-08-24 — plan hazır: [`96-PUBLIC-YUZEY-KUCULTME.md`](../fazlar/96-PUBLIC-YUZEY-KUCULTME.md) | `faz-planlama` |
| 3 | ~~**Faz 96 uygulama**~~ ✅ 2026-08-24 — [`96-PUBLIC-YUZEY-KUCULTME.md`](../fazlar/96-PUBLIC-YUZEY-KUCULTME.md) (arşivlenecek) | `faz-baslangic` → `faz-uygulama` → `faz-denetim` → `faz-tamamlama` |
| 4 | ~~**Faz 97 yazma**~~ ✅ 2026-08-24 — plan hazır: [`97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md`](../fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md) | `faz-planlama` |
| 5 | ~~**Faz 97 uygulama**~~ ✅ 2026-08-24 | `faz-baslangic` → `faz-uygulama` → `faz-denetim` → `faz-tamamlama` |
| 6 | ~~**Blok B**~~ 2026-08-25'te ayrıştı: madde **12** ve **23** [Faz 104](../fazlar/104-BEYAN-DOGRULUGU-VE-GIRIS-RAMPASI.md)'e girdi, madde **15** kapsam dışı bırakıldı (👤) | — |
| 7 | **Faz 104 uygulama** ← **sıradaki adım** — madde 14 (karar + beyan yarısı) · 12 · 23 | `faz-baslangic` → `faz-uygulama` → `faz-denetim` → `faz-tamamlama` |
| 8 | **Blok C / yapısal refactor** — Faz 105 → 106 → 107 → 108 → 109 planlandı (2026-08-26): madde 17 dört bağımsız Core fazı; madde 18+19 ortak frontend fazı | Her faz kendi zinciriyle: `faz-baslangic` → `faz-uygulama` → `faz-denetim` → `faz-tamamlama` |

> 🚨 **Bu tablo yayını kapsamıyor.** 2026-08-24'ten sonra bu turun dışında altı
> faz daha koşuldu ve kapandı (Faz 98–103, sözleşme yayını turu — bkz.
> [`YOL-HARITASI.md`](../../YOL-HARITASI.md)). Sürüm etiketi 2026-08-25 itibarıyla
> **hâlâ yoktur** (`git tag` yalnız `docs/damitma-oncesi-2026-08` döndürür);
> yayın kullanıcının elindedir (👤) ve yakın planda değildir.

### 7.3 Faz 96 yazacak oturuma — ölçülmüş zemin

**Kapsam:** madde 7. Public yüzeyi yayından **önce** küçültmek. Bugün bedeli
sıfırdır; yayından sonra bir sürüm kararıdır.

Ölçüldü (2026-08-24):

- **8.063** `Unshipped` girdi, **716** public tip. `Shipped.txt` dosyalarının
  hepsi boştur (yalnız `#nullable enable`).
- Tip dağılımı: **339** tip `Tracon.Abstractions`'ta.
- 🚨 **Hedef girdi değil tiptir.** `Abstractions`'ın 4.760 girdisinin
  **1.250'si `get`**, **1.170'i `set/init`** erişimcisidir. Bunlar tip
  kaldırılmadan küçülmez. Gözden geçirilecek liste **716 satırdır**, 8.063 değil:
  ```bash
  find src -name PublicAPI.Unshipped.txt -exec cat {} + | grep -vE '^\s*$|^#' | grep -vE ' -> |\(' | sort
  ```
- `EnablePublicApiTracking` açıktır (K-421) ve yayın kararından bağımsızdır.

**Bağlayıcı sıra kısıtı:** Faz 96, Faz 95'in tüketici kapısı **kurulduktan
sonra** koşar. Sebep: bir tipi `internal`'a çekmek gerçek tüketiciyi kırabilir
ve bunu yalnız `PackageReference` ile derlenen bir proje ölçer. `ProjectReference`
taşıyan sample'lar bu kusuru **göremez**.

**Planlama turunda cevaplanacak açık soru:** ölçüt ne olacak? Aday: `src/`
dışından hiç referans almayan tip `internal`'a çekilir. Bu ölçüt `Testing`,
`Client` ve `Cli` paketlerinin `Core`/`Abstractions` kullanımını da saymalıdır
— yoksa kendi paket ailesini kırar.

### 7.4 Faz 97 yazacak oturuma — ölçülmüş zemin

> **Durum:** 📋 Planlandı (2026-08-24) — [Faz 97](../fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md). Kapsam plan turunda **daraldı**: faz yayını kendisi yapmaz. `v1.0.0-preview.1` etiketi geri alınamaz olduğu için kullanıcının elinde kalır (👤); faz sürüm politikasını, yayın provası kapısını, paket ikonunu ve `PackageValidation`'ı kapatır. 🚨 `Shipped.txt` dolumu **GA'ya ertelendi** — bu, aşağıdaki zemin notunun ve Faz 7'nin özgün DoD'sinin bilinçli olarak değiştirilmesidir.
> Plan turunda yeniden ölçüldü: Faz 96 sonrası **7.532** girdi · **618** tip (bölüm 7.3'ün 8.063/716 değeri artık bayattır); `dotnet pack` **19** paket üretiyor ve ön sürüm bağımlılığı beyan eden **tek** paket `Tracon.AspNetCore` (K-008 tutuyor); `tracon`, `tracon.core` ve `@tracon/client` kimliklerinin üçü de **boşta**.

**Kapsam:** madde 2 (sürüm politikası) + madde 1 (Faz 7 dolumu ve yayın).
[Faz 7](../fazlar/07-SAGLAMLASTIRMA-VE-YAYIN.md) 2026-08-02'den beri
⏸ beklemededir (K-068).

Ölçüldü (2026-08-24):

- **Sürüm MinVer ile git etiketinden türer** (K-017). Etiket yokken
  `0.0.0-preview.0`. Depoda tek etiket vardır: `docs/damitma-oncesi-2026-08` —
  eğik çizgili adı **bilinçlidir**, MinVer'in SemVer ayrıştırıcısına takılmaz
  (K-598). Yani bugün hâlâ **sürüm etiketi yoktur**.
- 🚨 **Yayın `v*` etiketiyle tetiklenir ve kuru koşumu yoktur.**
  [`ci.yml:6`](../../../.github/workflows/ci.yml) `tags: ['v*']` dinler;
  `publish` (satır 211) ve npm yayını (satır 245) `startsWith(github.ref,
  'refs/tags/v')` koşuluyla açılır. İkisi de GitHub `environment` kapısı
  arkasındadır (`nuget`, `npm`) — onay gerektirecek biçimde yapılandırılabilir.
  **İlk `v1.0.0-preview.1` etiketi hem NuGet hem npm yayınını başlatır.**
  Fazın kendi DoD'si bu tetiği ve geri alınamazlığını ele almalıdır.
- Ön sürüm MAF bağımlılıkları [`Directory.Packages.props:37-51`](../../../Directory.Packages.props)
  içindedir ve K-008 gereği yalnız `Tracon.AspNetCore`'a girer.
- Paketlenen proje sayısı **19**'dur (`src/` altındaki 20 projeden
  `Tracon.Generators` `IsPackable=false`; `Tracon.Sql.Shared` bir
  `.csproj` DEĞİLDİR — üç sağlayıcıya derlenen paylaşılan kaynak dizinidir).

**Kapsama giren doküman doğruluğu kalemi:**
[`reference/compatibility.md:24`](../../../docs-site/src/content/docs/reference/compatibility.md)
başlığı **"The 17 packages"** diyor ve 17 satır listeliyor; depo **19** paket
üretiyor. `Tracon.Client` ve `Tracon.Cli` o tabloda yoktur
(`packages.md` ikisini de kapsıyor — eksik olan yalnız bu sayfa).

### 7.5 🚨 Bu turun yöntem dersi — atlanmaması gereken

**Bu envanterin iddiaları bayattır ve seçilen her kalem yeniden ölçülmelidir.**
2026-08-24'te iki iddia düştü:

| İddia | Gerçek |
|---|---|
| "Public API 6.301 girdi" | **8.063** girdi · 716 tip. Sayı %28 küçük yazılmıştı |
| Madde 10: "sample'lar CI'da çalıştırılmıyor" → genel tüketim boşluğu | `pack → yerel feed → PackageReference → derle → ayağa kalk` zinciri **zaten kurulu ve CI'da**. Gerçek boşluk yalnız **gerçek `run`**'dı. Faz 95'in kapsamı bu yüzden envanterin tarif ettiğinin çok altındadır |
| Bölüm 7.3'ün ölçütü: "`src/` dışından referans almayan tip `internal`'a çekilir" | **Uygulanamaz.** Ad araması uzatma metodu sınıfını, öznitelik tipini ve `<see cref>` bağını göremez. Körü körüne uygulansaydı her paketin `Add*` giriş noktası kapanırdı (22 vaka ölçüldü) |
| Bölüm 7.3'ün alt önerisi: `Abstractions`'ın tek tüketicili 30 tipi `Core`'a taşınır | **Mekanik olarak imkânsız + kazancı sıfır.** 30 tipin 24'ü yine `Abstractions`'ın kendi public imzalarında geçiyor; `Core`, `Abstractions`'a bağımlı olduğu için taşınamazlar. Taşınabilen 6 tipin hepsi genişleme noktası ve public kalmak zorunda. Net yüzey azalması: **0 tip** |
| "`Tracon.Client` 302 public tipi takipsiz — sessiz boşluk" *(bu planlama turunda üretilen iddia)* | **Bilinçli karar.** `src/Directory.Build.props:66-77` dört projeyi `TraconPublicApiTrackingEnabled=false` ile hariç tutuyor (K-424), gerekçesi yazılı ve drift kapısı gerçekten var (`ClientDescriptionBaselineTests`, Faz 83). Faz 96 istisnalara dokunmaz; yalnız "yeni packable paket beyansız eklenemez" kapısını kurar |

Ders: `faz-planlama` Adım 1 (kanıtı yeniden doğrula) bu dosyadan gelen her
kalem için **zorunludur**. Doğrulanmadan plana yazılan bir kanıt, var olmayan
bir deliği kapatan bir faz üretir.

### 7.6 Blok B — yayınla birlikte, tek oturumluk

| Kalem | İş | Neden yayına yakın |
|---|---|---|
| 12 | `Production` ortamında InMemory store için teşhis uyarısı | Davranış değişikliğidir; yayından sonra eklemek tüketiciyi şaşırtır → **Faz 104** |
| 15 | ~~NSwag üretimi client için ayrı analyzer profili~~ | ❌ Kapsam dışı (2026-08-25): pragmalar NSwag'ın standart CS başlığı, drift zaten üç kapıda. Gerekçe kalem 15'in kendi bölümünde |
| 23 | `CONTRIBUTING.md` + İngilizce mimari özeti | Depo yayında görünür olur; bugün giriş rampası yok → **Faz 104** |

### 7.7 Blok C — yayından sonra veya paralel

16 (benchmark · F-67) · 9 (manuel set devri, aile aile) · **14'ün uygulama
yarısı** (RLS uygulaması + dağıtık hız sınırı) · 17 (dev dosyalar) · 18
(frontend monolit) · 19 (frontend test kapsamı) · 20 (doküman yükü).

Hiçbiri yayın maliyetini değiştirmez — sonra yapılırsa bir şey kırılmaz.

**2026-08-25 ayrıştırması (👤).** Blok C yeniden yargılandı ve yalnız **madde
14'ün karar yarısı** öne çekildi: gerekçe, RLS'in kalıcı veri kararı olması ve
hiçbir yerde kayıtlı olmamasıdır. Geri kalan altı kalem yerinde kaldı; ikisinin
gerekçesi değişti:

| Kalem | 2026-08-25 yargısı |
|---|---|
| 17 | Şimdi yapmak **daha risklidir**. 2.644 satırlık DI dosyasını bölmek taze regresyon riskini yayına en yakın ana koyar; public üyeler yerinde kaldıkça maliyeti sonra da sıfırdır |
| 20 | **Başlık iddiası düştü.** Ölçüm: `docs/` 106.805 satır, `src/` 149.862 satır — damıtma işe yaradı (envanterde 143.671 / 139.425 yazıyordu). Kalan kısım bakım aparatıdır, tüketiciye etkisi yok |
