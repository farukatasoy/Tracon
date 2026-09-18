# Site Icerigi Yazimi — Makine Okuyucusu ve Markdown Tuzaklari

> Site sayfalarinin METNINI yazarken dusulen tuzaklar: markdown'in kendi
> ayristiricisi, ve sayfayi bir tarayici degil bir MODEL okudugunda ne oluyor.
>
> Faz 174'te `dokumantasyon.md` 16.593/16.000 B'ye ulasinca ayrildi. Kardes
> dosyalar: sevk edilen XML/README/OpenAPI metni
> [`dokumantasyon.md`](dokumantasyon.md) · site yayini ve temasi
> [`site-yayin-ve-tema.md`](site-yayin-ve-tema.md) · site uretim betikleri
> [`site-uretim-kapilari.md`](site-uretim-kapilari.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken
> okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

## 🚨 Makine okuyucusu için HTML, kod bloğunu SESSİZCE bozar (2026-09-14, GEO denetimi)

Expressive Code her kod SATIRINI kendi `<div class="ec-line">`'ına yazar ve aralarına
newline **koymaz**. Etiketleri düşüren bir metin dönüşümü şunu üretir:

```
var builder = WebApplication.CreateBuilder(args);builder.AddTracon().UseUI();
```

Derlenmeyen C#. Tablolarda aynı kusur sütun sınırında olur: satır ayrımı korunur,
sütun ayrımı kaybolur. Bunu hiçbir kapı göremezdi çünkü **render doğru**; bozulan
yalnız düz metne indirgeme.

Çözüm sayfa sayfa değil merkezî: `src/pages/[...slug].md.ts` her içerik sayfasının
markdown kaynağını `<adres>index.md` olarak yayınlar (1.136 dosya). Kapı
`scripts/site-seo-denetle.py` içindedir ve **varlığı yetmez** diye yazılmıştır:
kopyanın gövdesindeki `> Page:` satırı sayfanın canonical'ına eşit olmalıdır. İlk
koşumda açılış sayfası `/index/index.md` altına, var olmayan `/index/` adresiyle
yazıldı — sebebi loader'ın `concepts/index.md`'den `index`'i düşürüp site kökünde
düşürmemesi. Site içinden bakan hiçbir kontrol bunu göremezdi.

İki yan koşul, ikisi de `deploy/nginx.conf`'ta:

- Stok `mime.types`'ta `.md` **yoktur**. `location ~ \.md$ { default_type text/markdown; }`
  kullan — server seviyesinde `types { }` **miras haritanın tamamını siler**.
- `charset_types`'a `text/markdown` ekle, ama `text/html` **yazma**: o zaten örtük ve
  listelemek `nginx -t`'de uyarı verir.

## 🚨 Markdown tablosunun SATIRLARI ARASINA yorum koymak tabloyu YOK EDER (Faz 174)

Bir tablo satırını makine okunur bir işaretle bağlarken ilk akla gelen yazım
**çalışmaz** — GFM'de tablo bitişik satırlardan oluşur ve bir HTML bloğu (yorum
da bir HTML bloğudur) tabloyu **o satırda kapatır**:

```markdown
<!-- capacity: … -->
| Buffered | 1 | 174 |      ← tablo BURADA biter
```

Repo'nun remark sürümüyle ölçüldü: 13 satırlık tablo **1 satıra** düştü.
`npm run build` yeşil kalır — yalnız tablo kaybolur. Kapan `|`'dan **sonra**
yazmak da bozar: başlıkta olmayan fazladan bir hücre üretir.

**Doğru yer hücrenin içidir, kapan `|`'dan önce:**

```markdown
| Buffered | 1 | 174 | 1044 ms <!-- capacity: profile=sweep concurrency=1 --> |
```

Repo bunu Faz 158'den beri yapıyor (`<!-- claim:option … -->`,
`reference/configuration.md`) — yeni işaret tasarlamadan önce o emsale bak.
İşaret sevk edilen İngilizce sayfaya girdiği için anahtarı da İngilizcedir;
`SourceLanguageTests` `docs-site/`'ı taramaz, kapı bu hatayı yakalamaz (K-228).
- **🚨 `SourceLanguageTests`'in iki-harfli kelime dışlaması bir VARSAYIMDI ve sevk edilen metne Türkçe sızdırdı** (2026-09-18, manuel kapanış Aile K, K-819): kapı Türkçe harfleri ve bir kelime listesini tarar; liste **her** iki harfli kelimeyi "yanlış pozitif seli olur" gerekçesiyle dışlıyordu. `TraconOptionsValidator`'ın üç satırı iki anahtar adının etrafına Türkçe bir eş bağlaç sarıyordu ve kapı bunu **yapısal olarak** göremedi: bağlaç Türkçe harf taşımıyor, cümlede üç harfli Türkçe kelime yok. Taban çizgisi boştu — borç kayıtlı bile değildi, kaçmıştı. Varsayım ölçümle değişti: 16 aday sayıldı, sıfır çakışmalı yedisi (`ne · ya · ki · mi · mu · da · ve`) listeye girdi, çakışan üçü gerekçesiyle dışarıda (`de`/`en` BCP-47 etiketi, `bu` çevrilmiş arayüzü doğrulayan E2E testinde). **Bir kapının dışlama listesi de kod kadar ölçülmelidir; "yanlış pozitif üretir" gerekçe değil hipotezdir.**
- **🚨 Bir kapıyı güçlendiren değişiklik, o kapının KARŞI ÖRNEĞİNİ yazacak yeri de daraltır** (2026-09-18, Aile K): yeni kural ilk olarak kendi regresyon testimin XML dokümanını yakaladı — kusuru göstermek için kalıbı alıntılıyordu. Aile B'nin `HATA-*` referansı ve Aile D'nin `🚨` emojisiyle aynı sınıf. Cümle yeniden yazılır, kapı gevşetilmez.
