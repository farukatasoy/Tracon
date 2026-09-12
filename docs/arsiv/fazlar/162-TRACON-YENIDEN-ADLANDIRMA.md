# Faz 162 — Tracon Yeniden Adlandırma

> **Durum:** ✅ Tamamlandı (2026-09-12)
> **Kaynak:** Kullanıcı kararı (2026-09-12) — ürün adı değişti. Bu kalem [ADAYLAR.md](../../ADAYLAR.md) içinde hiç bulunmadı
> **Önkoşul:** Yok. Dış kimlikler faz öncesi alındı: npm org `tracon`, `tracon.dev` DNS, iki GitHub reposu
> **Paketler:** 21'inin tamamı — kök ad alanı, paket kimliği, assembly adı
> **Yeni paket:** Yok · **Migration:** Yok — yeni dosya eklenmedi. 124 migration dosyasının 18'inin İÇERİĞİ değişti: 3'ü job handler key DEĞERİ (54 satır), 15'i yalnız yorum. **Hiçbir DDL satırı değişmedi** (ölçüldü)
> **Public API:** Yüzey aynı, adı değişti. `PublicAPI.Shipped.txt` dosyalarının tamamı **boştur** — hiçbir paket yayınlanmadı, SemVer maliyeti **sıfırdır**
> **Tüketici yüzeyi:** Her şey — 21 paket README'si, site, üretilen `api/` + `http-api/` sayfaları, 19 ekran görüntüsü
> **Manuel test alanı:** Tüm set (adlar her case'te geçer)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 93be13fd:docs/arsiv/fazlar/162-TRACON-YENIDEN-ADLANDIRMA.md
> ```
>
> Damıtıldı 2026-09-12 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Ürünün adı değişti. Repo'da önceki ad hiçbir yerde kalmaz; dört doğrulama kapısı sıfır uyarı verir. Bu iş **şimdi ucuzdur ve sonra imkânsıza yakındır**: hiçbir paket NuGet'e veya npm'e gitmedi, release tag yok, `PublicAPI.Shipped.txt` dosyalarının tamamı boş. Bu yüzden hiçbir tüketici, hiçbir migration yolu, hiçbir deprecation gerekmez.

## Plandan Sapmalar

Devir teslim prompt'u beş noktada yanlıştı. Hepsi ölçümle bulundu.

### S1 — Site adresi TÜREV DEĞİLDİR 🚨

Prompt, site URL'lerini "türev kimlikler — hepsi yukarıdaki geçişin
sonucudur" tablosuna koyuyordu. Yanlış: küçük harf geçişi eski host'u
`tracon.doayen.web.tr` yapardı. Bu adres **makul görünür**, 59 dosyaya
yayılır ve desenle geri alınamaz.

Host geçişi ayrı bir kural oldu ve **ilk** koşar. Çıplak `doayen.web.tr`
(2 yer) aynı droplet'teki başka bir servistir; uzun literal önce tüketildiği
için ona dokunulmadı — doğrulandı.

### S2 — Beşinci bir yazım vardı: camelCase 🚨

Prompt dört varyant sayıyordu. Gerçekte beşinci bir yazım vardı:
camelCase — C# yerel değişken ve parametre adları (`var <ad> = ...`,
`<ad>Options`, `_<ad>Options`, `<ad>Tables`), 58 dosyada 192 yer. Dört
varyantın hiçbiri bunu yakalamaz.

**Varyant listesi bulmadı; büyük/küçük duyarsız KALINTI DENETİMİ buldu.**
Ders: geçiş listesi bir hipotezdir, kalıntı denetimi ölçümdür.

### S3 — Dört ratchet baseline'ı değiştirilir, yeniden üretilmez

Prompt H2 bunları "yeniden üret" diyordu. Ölçüldü: `<yol>:<metot>` ile
anahtarlanırlar ve refresh, yol değişince her kaydı **yeni** sayıp elle
yazılmış gerekçelerin üzerine `REPLACE ME` yazar.

Değiştirme uygulandı. **Kanıt:** `Tracon.Core.UnitTests` 2.648 test, sıfır
hata — 20 elle yazılmış güvenlik gerekçesi korundu.

### S4 — D8 (tablo hizalaması) bu repo için geçersiz

Prompt "markdown tablolarının boru hizası programatik olarak yeniden
hesaplanır (1.710 satır)" diyordu. Ölçüldü: repo tablo hücrelerini
paddinglemiyor — 4.216 kompakt `|---|---|` ayırıcıya karşı 8 padded.
Hizalanacak bir şey yok; **hiçbir işlem yapılmadı**.

### S5 — Bir arşiv dosyası bilerek eski adla duruyor (kullanıcı kararı)

`docs/arsiv/incelemeler/2026-09-07-tuketici-analizi-girdi-raporu.md` (162
eşleşme) bir **ölçüm kaydıdır**: 7 Eylül 2026'da hangi registry adresinde ne
yanıt alındığını gösterir, ve kendi 991. satırı bu adreslerin kasıtlı
tutulduğunu yazar. Adresleri çevirmek, kimsenin bakmadığı bir adres hakkında
tarihli ölçüm uydururdu. Dosya ayrıca iki üçüncü taraf projesini adıyla anar.

Kullanıcı kararı (2026-09-12): dosya bütün olarak korunur, başına gerekçe
notu düşülür.

### S6 — Bir damıtılmış kayıt işaretçisi eski yolu adlandırmak ZORUNDA

`docs/arsiv/fazlar/05-TRACON-UI.md` başındaki "tam metin" bloğu bir **git
komutudur**: `git show 7f1833e:<yol>`. Geçiş yolu çevirince komut çalışmaz
oldu — o commit'te dosya eski adıyla durur. `dokuman-bakim.py --denetle`
bunu yakaladı (`Damıtılmış kayıt tam metni: 1 bulgu`).

Başka çare yok: hiçbir commit hem yeni yolu hem tam metni taşımaz (dosya
2026-08-23'te damıtıldı). İşaretçi eski yolu adlandırır ve yanına gerekçesi
yazıldı. Arşivdeki ~160 işaretçiden yalnız **bu biri** etkilendi — dosya adı
üründen türeyen tek arşiv faz dokümanı odur.

Bitti tanımı bu yüzden **üç** istisna taşır — denetim bir dördüncüsünü ekledi
(aşağıda, bulgu 1): `site.config.mjs` içindeki `formerHosts` dizisi eski host'u
**adlandırmak zorundadır**, çünkü onu her yerde yasaklayan mekanizma odur.

### S7 — Ad, ALFABETİK SIRAYA girer; değiştirme bunu göremez

Yeni ad eski adın bulunmadığı yere sıralanır. Bu, üç ayrı yerde **içerik
değil sıra** farkı üretti; üçü de değiştirmeyle çözülemez, yalnız yeniden
üretmeyle:

| Yüzey | Nasıl çıktı |
|---|---|
| OpenAPI `components.schemas` | 5 şema 8–12. sıradan 240–244'e taşındı |
| Üretilen istemci + TS şeması | aynı satır kümesi, farklı sıra |
| C# `using` direktifleri | `dotnet format` 263 dosyada sıra hatası buldu (12 dosyada blok sonrası boş satır da düştü) |

Üçünde de **aynı kanıt yöntemi** kullanıldı: sıralanmış satır kümelerini
karşılaştır. Küme aynıysa fark yalnız sıradır ve kaçan ad yoktur.

**Bu sınıf bir yeniden adlandırmanın kaçınılmaz sonucudur.** Adın ilk harfi
değiştiği için her alfabetik sıralama etkilenir; ad aynı harfle başlasaydı
hiçbiri görünmezdi.

---

## Bu Fazda Verilen Kararlar

- **K-753** — migration bütünlük manifest'i yeniden temellendirilir.
- **K-754** — analyzer tanı öneki `TRC`; kısaltmalar ad aramasıyla bulunamaz.
- Üçüncü taraf ve tarihli ölçüm kayıtları yeniden adlandırılmaz (S5).
- `agent-prism` / `agent.prism` **dokunulmadı**: ikisi
  `MigrationRunnerTests` içinde geçersiz-identifier fixture'ıdır, biri üçüncü
  taraf GitHub adresidir. Hiçbiri marka değildir ve hiçbiri
  büyük/küçük duyarsız ad aramasıyla eşleşmez.
- Job handler key **değerleri** döndü (`tracon.agent-batch` …). Bunlar veriye
  yazılır; üç migration dosyasının içeriği bu yüzden değişti. Yaşayan
  veritabanı yok (kullanıcı kararı 2026-09-12), bu yüzden düzeltme
  migration'ı **yazılmadı**.
- Logo ve favicon değişmedi — tasarım borcu olarak [ADAYLAR.md](../../ADAYLAR.md).

---

## Örnek Uygulama Koşumu (Adım 2)

`samples/Tracon.Api`, varsayılan **dışı** yapılandırmayla
(`Tracon__Observability__SuccessSampleRatio=1`) ayağa kaldırıldı. Gerçek çıktı:

| Yoklama | Sonuç |
|---|---|
| `GET /tracon/api/meta` | `200` · gövde `"prefix":"/tracon"`, `"version":"0.0.0-preview.0.693"` |
| `GET /<eski-önek>/api/meta` | **`404`** — eski montaj öneki gerçekten yok |
| `X-Tracon-Tenant: default` | `200` |
| Arayüz `<title>` | `Tracon` |
| Log kategorisi | `Tracon.McpDiscoveryService` |

Eski kiracı header'ı da `200` döner; bu **doğru** davranıştır —
tanınmayan bir header yok sayılır ve varsayılan kiracı uygulanır. Kanıt değeri
olan yoklama eski **önekin** 404 dönmesidir.

## Manuel Kabul Seti (Adım 3)

**Yeni case eklenmedi, gerekçesi:** bu faz hiçbir davranış değiştirmedi
(denetim ölçtü: `src/**/*.cs` içinde 934 dosya çifti normalize edilerek
karşılaştırıldı, ad değişimi dışında sıfır fark). Mevcut 1597 case'in tamamı
geçişle birlikte yeni adı ve yeni komutları taşır; sayım kapısı
(`manuel_test_sayim_kaymasi`) yeşildir. Yeniden adlandırmanın kendi kanıtı
kapı koşumları ve yukarıdaki örnek uygulama koşumudur, yeni bir kabul case'i
değildir.

---

## Denetim Bulguları

Bağımsız denetçi (2026-09-12, taze bağlam, `96e515db..HEAD`).

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | Çıkan host `formerHosts`'a eklenmedi; dosyanın kendi sözleşmesi ihlal edildi | **Düzeltildi.** `site.config.mjs:34`. Bu dizi, TÜRETİLMEYEN metinlerdeki (NuGet'e giden paket README'leri, derlenmiş analyzer bağlantısı) bayat adresi yakalayan tek mekanizmadır. Eklenmeseydi kapı yeşil kalıp ölü adres sevk edilirdi |
| 2 | 🟡 | K-753 "içerik değişikliğini onaylamak için asla kullanılmaz" diyordu; kendi commit'i 18 dosyanın içeriğini onayladı | **Düzeltildi.** Kaydın son sütunu ölçümle yeniden yazıldı: rebaseline yeniden adlandırmanın içerik değişimini kapsar, bir DDL değişikliğini kapsamaz |
| 3 | 🟡 | Başlık "üç migration dosyası" diyordu; ölçüm 18 | **Düzeltildi.** 124 dosyanın 18'i: 3'ü handler key değeri (54 satır), 15'i yorum, **sıfır DDL** |
| 4 | 🟡 | Analyzer tanı öneki `APG` dokunulmamıştı — kısaltma olduğu için kalıntı denetimi yapısal olarak göremez | **Düzeltildi** (kullanıcı kararı 2026-09-12): `APG` → `TRC`, 592 yer / 62 dosya. `AnalyzerReleases.Shipped.md` boştu, yani bugün hiçbir tüketici bastırma satırı kırılmadı; yayından sonra aynı iş deprecation isterdi |
| 5 | 🟢 | Eski adın METAFORU kodda yaşıyor (`prismMark`, `_prismOptions`, `--ap-*`, "prism spectrum") | **Devredildi.** F-221'in kapsamı genişletildi — logo tasarımı zaten o dosyalara dokunacak. Hiçbiri tüketici sözleşmesi değil (tipler `internal`); site içerik sayfalarında hiç geçmiyor (ölçüldü) |
| 6 | 🟢 | `dotnet format` 12 test dosyasında `using` bloğu ile yorum arasındaki boş satırı da tüketti | **Kabul.** Derleme aynı; S7'nin "yalnız sıra" ifadesi bu kadarıyla eksik |

**Temiz çıkan başlıklar:** 3.2 · 3.3 · 3.4 · 3.5 · 3.6 · muafiyet listeleri (13 baseline'ın hiçbiri büyümedi, hiçbirinde `REPLACE ME` yok) · dil sınırı.

Denetçinin ayrıca ölçtüğü: `src/**/*.cs` içinde 934 dosya çifti normalize edilerek karşılaştırıldı — ad değişimi dışında **sıfır davranış değişikliği**.

---

## Yayın (Adım 10)

`./scripts/site-deploy.sh` — dört site kapısı yeşil (1138 sayfa, 169.440 iç
bağlantı, hiçbiri kırık değil), `rsync` ve konteyner uzlaştırma tamam.

| Doğrulama | Sonuç |
|---|---|
| `https://tracon.dev/` | `HTTP/2 200` · başlık `Agent control plane for .NET \| Tracon` |
| Ana sayfada eski ad | **0 eşleşme** — canlı içerik gerçekten yeni |
| `https://www.tracon.dev/` | `HTTP/2 308` · `location: https://tracon.dev/` |
| `https://doayen.web.tr/` | `HTTP/2 405` — apex servisi bozulmadı (`405` = HEAD, normal) |
| `tracon.dev/llms.txt` | yeniden üretilmiş `revision: 3d55b119` |

**D9'dan küçük bir sapma:** karar "301" diyordu, Traefik `permanent=true` için
**308** üretir. İkisi de kalıcı yönlendirmedir; 308 metodu korur. Mekanizmanın
davranışıdır, tercih değil.

### S8 — `www` router'ı ad geçişiyle GELMEZ, eklenir

Ölçülen sapma: compose dosyası yalnız ad geçişi olarak işlendi ve D9'un
istediği **ikinci router hiç eklenmedi**. DNS `www.tracon.dev`'i zaten
droplet'e yönlendiriyordu, bu yüzden ad çözüldü ve TLS el sıkışmasında düştü —
Traefik'in o SNI için router'ı, dolayısıyla sertifikası yoktu.

Kapıların hiçbiri bunu göremezdi: `docker-compose.yml` bir yapılandırma
dosyasıdır, testi yoktur. **Yalnız gerçek yayın yakaladı.** Bu, `faz-tamamlama`
Adım 10'un neden protokolde olduğunun kanıtıdır — yeşil kapı yayın değildir.

---

## Sonraki Faza Devir Notu

Sonraki faz dokümanı **yoktur** — 162 yol haritasının son kalemidir. Bir sonraki
faz seçildiğinde `faz-planlama` koşar.

🚨 **Bir sonraki yeniden adlandırmayı yapacak oturuma:** ad varyantı listesi bir
hipotezdir. Bu fazda listeyi değil **büyük/küçük duyarsız kalıntı denetimi**
beşinci varyantı (camelCase) buldu, ve o denetim bile **kısaltmayı** (`APG`)
göremedi — onu bağımsız denetim buldu. Sıra: (1) uzun bileşik literal'ler
(host, URL) önce, (2) duyarlı varyant geçişleri, (3) duyarsız kalıntı denetimi,
(4) kısaltmalar için ayrı elle arama.

`repositoryIsPublic` **`false` kalır**. `astro.config.mjs` bu bayrağı hiç
import etmiyor — repo public yapılırsa `editLink`/`social` blokları **elle**
geri getirilir (K-542).

---

## Bitiş Ölçütleri (DoD)

- [x] Büyük/küçük duyarsız ad araması → yalnız dört istisna: `arsiv/fazlar/INDEKS.md` notu, S5'teki ölçüm kaydı, S6'daki git işaretçisi, `site.config.mjs` içindeki `formerHosts` girdisi
- [x] Hiçbir izlenen YOL eski adı taşımıyor
- [x] `dotnet build Tracon.slnx -c Release` → 0 uyarı, 0 hata
- [x] Üretilen istemci yeniden üretildi, kaçan ad yok (aynı satır kümesi)
- [x] SQL baseline'ları yeniden üretildi → değiştirilmişle **birebir aynı**
- [x] Agent map yeniden üretildi, drift kapısı geçiyor
- [x] `kapi.py kapanis --taban 96e515db` dört kapı sıfır uyarı (çıkış 0)
- [x] `kapi.py yayin --kuru` yeşil (çıkış 0): 20 paket, npm dry-run, 6 packed sample + Native AOT smoke. Kimlik kümesi tam `Tracon.*`
- [x] 19 ekran görüntüsü yeniden üretildi (`Ui.E2ETests` 58/58)
