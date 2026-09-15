# Geliştirme Defterinin Bakımı

> **Kardeş dosya:** sevk edilen ve kullanıcıya dönük doküman tuzakları
> [`dokumantasyon.md`](dokumantasyon.md) içindedir.

`docs/KARARLAR.md`, üretilen karar indeksleri ve `docs/arsiv/fazlar/`
arşivlemesi — bunlar birikimli defterlerdir ve kendi tuzak sınıflarını
üretirler. Ortak desen: **kapı yeşil kalır, kayıt sessizce bozulur.**

## 🚨 Karar numarasını tablonun SONUNA bakarak seçme (K-539)

Faz 77 ve Faz 78 aynı tabandan yazıldı. İkisi de `KARARLAR.md` §2'nin son satırına
baktı, `K-534` gördü ve **ikisi de** `K-535` ile `K-536`'yı aldı. Dört satır, iki
numara, farklı içerik — ve indeks üreteci hiç ötmedi, çünkü `_kararlar_kalemleri`
satır satır regex okur ve tablo **yapısına** bakmaz.

İki şey bunu görünmez kılmıştı:

- Tablo **sıralı değildi** (`K-018` satır 70'te, `K-524` en sonda), yani "son
  satır = en büyük numara" varsayımı zaten yanlıştı.
- Tabloyu **kesen boş satırlar** vardı (`K-351`/`K-352` ve `K-535`/`K-536` arası).
  Markdown'da boş satır tabloyu orada bitirir; sonraki kararlar başlıksız ikinci
  bir tabloya düşer. Faz 77 denetimi bunlardan yalnız birini gördü ve "kozmetik"
  diye kapattı — kozmetik değildi, numara çakışmasını gizleyen şeyin yarısıydı.

Doğrusu: numarayı **maksimumdan** al, son satırdan değil.

```bash
grep -oE "^\| \*\*K-[0-9]+" docs/KARARLAR.md | grep -oE "[0-9]+" | sort -n | tail -1
```

Kapı artık var: `python3 scripts/dokuman-bakim.py --denetle` yinelenen numarayı,
tabloyu kesen boş satırı ve sıra dışı numarayı **hata** olarak bildirir. Çakışma
çıkarsa tarih kuralı uygulanır — **önce tahsis edilen numarayı korur**; sonraki
taşınır ve o fazın dokümanındaki referansları da taşınır.

## 🚨 Karar satırı §2 DIŞINDA yaşarsa üç kapı birden körleşir (2026-09-15)

K-539'un kapısı (`kararlar_denetle`) yalnız `_kararlar_tablosu()`'nun döndürdüğü
**§2 gövdesine** bakar. İndeks üreteci (`_kararlar_kalemleri`) ise dosyanın
TAMAMINI okur. Bu asimetri ölçüldü: **K-662…K-782 arası 101 karar**, §3'ün
*şablon* kod bloğunun içine yazılmıştı.

Sonuç, "yalnız okunabilirlik" diye 🟢 işaretlenmişti ama üç kapı birden kördü —
yinelenen numara · tabloyu kesen boş satır · sıra dışı numara. Kalemler indekste
göründüğü için kayıp fark edilmiyordu. Bedel gerçekti: **iki farklı karar aynı
numarayı (K-703) taşıyordu** — K-539'un tam olarak kapattığını sandığı sınıf — ve
iki üretilen indeks BİRBİRİNDEN FARKLI bir K-703 gösteriyordu
(`KARARLAR-INDEKS.md` F-211'inkini, `KARARLAR-INDEKS-ARSIV.md` Faz 151'inkini).

Çakışma tarih kuralıyla çözüldü: ilk tahsis (Faz 151, 03:25) K-703'ü korudu,
ikincisi (F-211, 18:53) **K-783**'e taşındı ve beş referansı birlikte taşındı.

Kapı: `_bolum_disi_karar_satirlari()` — tarama artık dosyanın tamamındadır ve
§2 dışındaki her `| **K-NNN` satırını hata sayar. Mutasyonla doğrulandı.

🚨 **Ders: bir kapı ile onu besleyen üreteç AYNI satır kümesine bakmalıdır.**
Aynı asimetri Faz 169'da da görüldü (kesik başlık taraması §2 ile sınırlıydı) —
bu ikinci vakadır.

## 🚨 `faz-arsivle` kendi bağlantı onarımını kaçırabilir — koştuktan SONRA denetle (Faz 104)

Skill "tek bir yeni kırık bağlantı üretirse taşımayı geri alır" diyor. Faz
104'te geri **almadı**: fazın kendi gövdesindeki `../arsiv/fazlar/103-*.md`
bağlantısı `../../../arsiv/fazlar/103-*.md` olarak yeniden yazıldı — dosya
zaten `docs/arsiv/fazlar/` içine taşındığı için doğru yol yalnız
`103-*.md`'dir. Onarım, dosyanın **yeni** konumunu değil eski derinliğini
kullanmış. Kural: `faz-arsivle` koştuktan sonra `dokuman-bakim.py --denetle`
çıktısındaki **Kırık bağlantı** satırını oku; sıfır değilse elle düzelt.
Aynı ağaçtaki kardeş faza verilen bağlantılar en riskli olanlardır.

**Aynı sınıf ÜÇÜNCÜ kez tekrarladı (2026-09-01) — artık kapı var.** Onarım iki
eksende kördü: yalnız `*.md` dosyalarını tarıyor **ve** yalnız `](...)`
sözdizimini eşleştiriyordu. İkisi birlikte 43 bayat referans biriktirdi:
`.sql`/`.cs`/`.yml`/`.props`/`.py`/`.tsx` hiç taranmıyordu, `.md` içindeki
**düz metin** yol (`See docs/NN-AD.md` — analyzer sürüm notu, pakete **sevk
edilen** bir dosya) eşleşmiyordu. Onarım `_duz_yol_referanslarini_cevir` ile
genişletildi; kapı `kapi.py tarama` → *bayat doküman referansı*.

🚨 **Uygulanmış migration'daki referans ONARILAMAZ** — bayt donmuştur
(`migration_integrity_violations`), yorumunu değiştirmek bile kapıyı kırar
(ölçüldü). Kapı `Migrations*/` dizinlerini dışlar, arşivleme onları uyarı
olarak listeler: bir yorum sevk edildiği **anın** doğru kaydıdır.

## 🚨 Karar başlığındaki İÇ İÇE `**` indeksi SESSİZCE keser (Faz 169, faz dışı)

`docs/KARARLAR-INDEKS.md` üretilir ve başlığı tembel bir `\*\*(.+?)\*\*` ile
okur. Başlığın **içinde** ikinci bir `**` varsa desen orada kapanır: üretilen
indeks satırı cümlenin yarısında biter ve kalem **aranamaz** hâle gelir.
Ölçüldü: K-413 indekste `… kaynak her fazın kendi \`>` diye, K-523
`… \`docs/` diye bitiyordu. `--denetle` **yeşildi**; hiçbir kapı görmedi.

İki ayrı vaka, iki ayrı çözüm — karıştırma:

| Nerede | Doğru çözüm |
|---|---|
| `**` bir **kod parçasının içinde** (`` `> **Durum:**` ``) | Bir şey yapma. `_karar_basligi()` artık `_kod_bloklarini_soy` ile bakıyor; kod parçası içindeki `**` vurgu değildir. 🚨 Kaçış (`\*\*`) **ekleme** — kod parçasının içinde birebir görüntülenir |
| `**` gerçekten **vurgu** (`sabit sayı **167**'dir`) | Başlıkta iç vurgu kullanma; kod parçasına çevir (`` `167` ``) |

Kapı: `_kesik_karar_basliklari()` (`kararlar_denetle` içinde). İmza, başlığı
kapatan `**`den sonraki kuyruğun **boşlukla başlamamasıdır** — meşru kuyruklar
(`**(Faz 168)**`, `*(kullanıcı kararı)*`, `🚨`) her zaman boşlukla başlar.
