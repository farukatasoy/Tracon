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

## 🚨 Bir kapının DIŞLAMA listesi onun sözleşmesidir — `arsiv` dışlanınca secret kapısı körleşti (2026-09-19)

`kapi.py tarama` ✅ **temiz** derken `docs/arsiv/fazlar/53-KIRACI-API-ANAHTARLARI.md:78`
içinde GERÇEK bir `ApiKeyGenerator.Generate` çıktısı duruyordu. Repo public
yapılmadan önceki denetimde bulundu; kapı 13 ay boyunca yeşildi.

İki bağımsız boşluk vardı ve **her biri tek başına yeterliydi**:

1. **Desen ürünün KENDİ credential formatını tanımıyordu.** `SECRET_PATTERN`
   `sk-*`, `AVNS_*` ve yerel kurulum deyimini biliyordu; Tracon'un kendi
   `ap_<kiracı>_<base64url>` formatını bilmiyordu. Bir secret kapısının kendi
   ürününün anahtarını tanımaması, kapının olmamasıyla aynı sınıftadır.
2. **`SCAN_EXCLUDED_DIRS` `arsiv` ve `manuel-test` ağaçlarını hiç yürümüyordu** —
   oysa **gerçek koşum çıktısı taşıyan tek yer orasıdır.** Arşivlenmiş faz kaydı
   "doğrulama komutları — gerçek çıktı" blokları taşır; üretilmiş bir
   credential'ın yapışacağı yer tam olarak burasıdır. Kapı, en çok bakması
   gereken ağacı dışlamıştı.

🚨 **Ders: dışlama listesi performans ayarı değil, kapının kapsam sözleşmesidir.**
Bir ağacı "gürültülü" diye dışlamak, o ağaçta aradığın şeyin olmadığını
varsaymaktır. Buradaki varsayım tam tersine çıktı.

**Kapsamı tüm desenlere açmak çözüm DEĞİLDİR** — ölçüldü: 51 eşleşmenin 48'i
localhost docker parolasıdır ve hepsini `SYNTHETIC-CREDENTIAL` ile işaretlemek
`find_secrets`'in kendi yorumunun yasakladığı şeyi (sessizce büyüyen istisna
listesi) üretirdi. Ayrım **şekildedir**: bir credential ŞEKLİ dokümanda asla
meşru değildir, yerel kurulum deyimi ise manuel testin kendisidir. Tarama bu
yüzden iki katmanlıdır ve maliyet 51'den **9'a** iner (KG-031).

Desen yazarken iki tuzak ölçüldü:

- **Uzunluk TAM verilir.** `ap_[a-z0-9]{1,12}_[A-Za-z0-9_-]{43,}` snake_case
  İngilizce metni yakalar (`ap_on_total_source_code_size_for_...`) — 70+ yanlış
  pozitif. `{43}` + `\b` sıfır verir; base64url(32 bayt) tam 43 karakterdir.
- **Deseni düz yazan yorum kapıyı kendi kaynağı üzerinde kırmızı yapar.**
  `kapi.py` kendi taramasına girer. `kapi_test.py` fixture'ı bu yüzden parçalı
  yazar (`"Pass" + "word="`); aynı disiplin yorumlar için de geçerlidir.

Mutasyonla doğrulandı (8/8), `IkiKatmanliSecretTaramasiTestleri` sınıfı kilitler.

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

## 🚨 Kabul kuralı kapısız kaldı ve defter kuralın tersine büyüdü (2026-09-23)

AGENTS.md'nin `K-*` kabul kuralı (dört kategori) 2026-08-23'te yazıldı ama hiçbir
kapı onu zorlamadı. Agent'ların izlediği skill'ler ise kaydı **koşulsuz**
deftere yolluyordu (`faz-tamamlama` Adım 7/8, `kusur-giderme` Adım 6/7,
`manuel-test-kosumu` bitti listesi ve altı yer daha). Sonuç: kuraldan sonraki 32
günde 272 karar açıldı; kusur kapanışları, test kuralları ve arayüz ayrıntıları
`K-*` oldu. Bir kural ile onu uygulayan skill ayrışırsa **skill kazanır**.

Kapı: `_kategorisiz_karar_satirlari()` (`kararlar_denetle` içinde). `K-855`'ten
itibaren her §2 satırı `*(kategori: <değer>)*` taşır; eşik sabittir ve geriye
dönük doldurma yapılmaz (K-767 emsali). Kategori dışı kaydın yeri
`faz-tamamlama` Adım 8'dedir.

### Süreç maliyeti — elle ölçülür, kapı değildir

Kapının etkisini görmek için üç sayıyı ölç ve tabanla karşılaştır:

```bash
# 1. Yalnız dokümana dokunan commit oranı (docs/, docs-site/, .agents/, *.md)
git log --format='@%h' --name-only | awk '
  /^@/ { if (n) { t++; if (d == n) k++ } n = 0; d = 0; next }
  NF { n++; if ($0 ~ /^(docs\/|docs-site\/|\.agents\/)|\.md$/) d++ }
  END { if (n) { t++; if (d == n) k++ } print k "/" t }'
# 2. Tarih başına yeni K satırı (son 14 tarih)
python3 -c 'import re,collections; c=collections.Counter(m.group(1) for s in open("docs/KARARLAR.md",encoding="utf-8") if s.startswith("| **K-") and (m:=re.search(r"\|\s*(\d{4}-\d{2}-\d{2})\s*\|",s))); print(sorted(c.items())[-14:])'
# 3. Defter boyutu ve bütçesi
wc -c docs/KARARLAR.md; grep -n '"docs/KARARLAR.md":' scripts/dokuman-bakim.py
```

| Ölçü (2026-09-23, `bb9953e3`) | Taban |
|---|---|
| Yalnız doküman commit'i | 608/1135 (%54; dosya değiştiren commit) · 2026-09-02'den beri 376/581 (%65) |
| Yeni K / gün | tüm dönem 15,8 (853/54 gün) · kural sonrası 8,5 (272/32) · son 7 gün 8,1 |
| `KARARLAR.md` | 468.280 B / bütçe 496.000 B (%94) |

**Defter bütçesi dar.** Büyüme son 7 günde ~5,0 KB/gün (433.271 → 468.280),
son 4 günde ~3,3 KB/gün; kalan 27.720 B yaklaşık 6–8 gün yeter. Aşımda K-781
kuralı geçerlidir: önce `karar-damit` koşulur (bugün kuru koşum 8 satırda
4.419 B taşır), taşıma tükenmişse sınır damıtma sonrası ölçülen değer / 0,85
ile yükselir. Bu turda bütçe değişmedi.
