# Faz 162 — Tracon Yeniden Adlandırma

> **Durum:** 🚧 Sürüyor
> **Kaynak:** Kullanıcı kararı (2026-09-12) — ürün adı değişti. Bu kalem [ADAYLAR.md](ADAYLAR.md) içinde hiç bulunmadı
> **Önkoşul:** Yok. Dış kimlikler faz öncesi alındı: npm org `tracon`, `tracon.dev` DNS, iki GitHub reposu
> **Paketler:** 21'inin tamamı — kök ad alanı, paket kimliği, assembly adı
> **Yeni paket:** Yok · **Migration:** Yok — üç migration dosyasının İÇERİĞİ değişti, yeni dosya eklenmedi
> **Public API:** Yüzey aynı, adı değişti. `PublicAPI.Shipped.txt` dosyalarının tamamı **boştur** — hiçbir paket yayınlanmadı, SemVer maliyeti **sıfırdır**
> **Tüketici yüzeyi:** Her şey — 21 paket README'si, site, üretilen `api/` + `http-api/` sayfaları, 19 ekran görüntüsü
> **Manuel test alanı:** Tüm set (adlar her case'te geçer)

---

## Amaç

Ürünün adı değişti. Repo'da önceki ad hiçbir yerde kalmaz; dört doğrulama
kapısı sıfır uyarı verir.

Bu iş **şimdi ucuzdur ve sonra imkânsıza yakındır**: hiçbir paket NuGet'e veya
npm'e gitmedi, release tag yok, `PublicAPI.Shipped.txt` dosyalarının tamamı
boş. Bu yüzden hiçbir tüketici, hiçbir migration yolu, hiçbir deprecation
gerekmez.

### Faz öncesi ölçüm (2026-09-12, taban `96e515db`)

| Varyant | Dosya | Eşleşme |
|---|---|---|
| PascalCase (sevk edilen biçim) | 2.357 | 42.987 |
| tümü küçük | 685 | 5.546 |
| tümü büyük | 119 | 328 |
| ikinci büyük harfi küçük yazılmış biçim | 3 | 4 |
| **camelCase (küçük `a`, büyük `P`)** | 58 | 192 |

Adı taşıyan yol: 2.120 (199 dosya adı). Yeni ad repo'da hiç geçmiyordu.

### Uygulanan geçiş

Sıra yük taşır. Host geçişi **önce** koşar:

```
1) <önceki-host>.doayen.web.tr  ->  tracon.dev
2) PascalCase -> Tracon  ·  tümü küçük -> tracon  ·  tümü büyük -> TRACON
3) ikinci-harf-küçük -> Tracon  ·  camelCase -> tracon
```

Uygulama: 2.392 dosyada içerik, 261 `git mv` (3 tur, en sığdan başlayarak).
Script idempotenttir — ikinci koşum sıfır dosya değiştirir.

---

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

Bitti tanımı bu yüzden **üç** istisna taşır.

---

## Bu Fazda Verilen Kararlar

- **K-753** — migration bütünlük manifest'i yeniden temellendirilir (aşağıda).
- Üçüncü taraf ve tarihli ölçüm kayıtları yeniden adlandırılmaz (S5).
- `agent-prism` / `agent.prism` **dokunulmadı**: ikisi
  `MigrationRunnerTests` içinde geçersiz-identifier fixture'ıdır, biri üçüncü
  taraf GitHub adresidir. Hiçbiri marka değildir ve hiçbiri
  büyük/küçük duyarsız ad aramasıyla eşleşmez.
- Job handler key **değerleri** döndü (`tracon.agent-batch` …). Bunlar veriye
  yazılır; üç migration dosyasının içeriği bu yüzden değişti. Yaşayan
  veritabanı yok (kullanıcı kararı 2026-09-12), bu yüzden düzeltme
  migration'ı **yazılmadı**.
- Logo ve favicon değişmedi — tasarım borcu olarak [ADAYLAR.md](ADAYLAR.md).

---

## Sonraki Faza Devir Notu

`repositoryIsPublic` **`false` kalır**. `astro.config.mjs` bu bayrağı hiç
import etmiyor — repo public yapılırsa `editLink`/`social` blokları **elle**
geri getirilir (K-542).

---

## Bitiş Ölçütleri (DoD)

- [x] Büyük/küçük duyarsız ad araması → yalnız üç istisna: `arsiv/fazlar/INDEKS.md` notu (1 satır), S5'teki ölçüm kaydı, S6'daki git işaretçisi (1 satır)
- [x] Hiçbir izlenen YOL eski adı taşımıyor
- [x] `dotnet build Tracon.slnx -c Release` → 0 uyarı, 0 hata
- [x] Üretilen istemci yeniden üretildi, kaçan ad yok (aynı satır kümesi)
- [x] SQL baseline'ları yeniden üretildi → değiştirilmişle **birebir aynı**
- [x] Agent map yeniden üretildi, drift kapısı geçiyor
- [ ] `kapi.py kapanis --taban 96e515db` dört kapı sıfır uyarı
- [ ] `kapi.py yayin --kuru` yeşil; `.nupkg` kimlik kümesi tam `Tracon.*`
- [ ] Ekran görüntüleri yeniden üretildi
