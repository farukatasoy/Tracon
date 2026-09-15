# Faz 174 — Kapasite Damgası Kapısı

> **Durum:** ✅ Tamamlandı (2026-09-15)
> **Plan onayı:** onaylandı (2026-09-15, kullanıcı) — üç açık soru + bir kapsam sorusu cevaplandı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-239**
> **Önkoşul:** [Faz 166](166-HTTP-KAPASITE-OLCUMU.md) — ölçüm aparatını, `summary.json`/`manifest.json` şemasını ve K-775'i o faz kurdu
> **Paketler:** Yok — `docs-site/scripts/` ve sayfa içinde kalır (plan `scripts/` diyordu; sapma 2)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/production.md` — yalnız **makine okunur işaret** eklenir, yayımlanan sayı değişmez · sevk edilen yapıt: Yok
> **Manuel test alanı:** `docs/manuel-test/33-DOKUMAN-KAPILARI.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 8f2412f9:docs/arsiv/fazlar/174-KAPASITE-DAMGASI-KAPISI.md
> ```
>
> Damıtıldı 2026-09-15 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 166 kapasite sayılarını ölçtü ve yayımladı. Denetimi **beş 🔴 bulgu** buldu ve beşi de aynı sınıftandı: ölçüm dosyasında duran bir sayı, sayfaya elle taşınırken bozuldu. K-775 bunu bir **sözleşme** olarak kurdu — yayımlanan her kapasite sayısı sürüm ve commit taşır. Sözleşmenin kapısı yok. Bu faz o kapıyı kurar.

## Bitiş Ölçütleri (DoD)

> 🚨 Kapı `dokuman-bakim.py`'ye değil `check-content.mjs`'e kuruldu (sapma 2).
> Aşağıdaki satırlar ve komutlar **gerçekleşen** yeri gösterir; planın ilk
> yazımı `dokuman-bakim.py` diyordu ve o adres artık yanlıştır.

- [x] `cd docs-site && node scripts/check-content.mjs` kapasite kapısını koşar ve bugünkü sayfada **temiz** döner
- [x] Sayfadaki **19 tablo satırının tamamı** işaret taşır; işaretsiz satır **ve işaretsiz tablo** bulgu üretir
- [x] Sayfadaki iki commit damgası da manifest'le eşleştiği doğrulandı — **iki yönde** (sayfanın andığı damga saklı olmalı, saklı koşum sayfada anılmalı)
- [x] Kapı hiçbir dosyayı değiştirmez — `the gate changes no file` testi ağacı bayt bayt karşılaştırır
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban f4cfb9d0`, on adımın onu da ✅
- [x] ~~`samples/Tracon.Api` ile gerçek `run`~~ — **muaf.** Bu faz çalışma anı kodu içermiyor: `git diff f4cfb9d0 --stat -- src/ tests/ samples/` boş. Çalıştırılacak yeni davranış yok; kapının kendisi `node scripts/check-content.mjs` ile gerçekten koşuldu
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅ (3,71 sn)
- [x] Manuel kabul case'leri `docs/manuel-test/33-DOKUMAN-KAPILARI.md` içine eklendi (`MT-DKP-017…021`); beşinin beşi de koşuldu
- [x] `faz-denetim` koşuldu; **2 🔴 + 6 🟡** bulgu üretti, hepsi kapandı — bkz. *Denetim Bulguları*
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` + `check-weight.mjs` temiz

### Doğrulama komutları

```bash
# Kapı bugünkü sayfada temiz mi
cd docs-site && node scripts/check-content.mjs

# Kapının kendi testleri (34 test)
cd docs-site && node --test scripts/check-capacity-stamp.test.mjs

# Bozuk sayı gerçekten yakalanıyor mu (elle, geri al)
sed -i.bak 's/| 1044 ms |/| 9999 ms |/' docs-site/src/content/docs/guides/production.md
(cd docs-site && node scripts/check-content.mjs)   # bulgu vermeli
mv docs-site/src/content/docs/guides/production.md{.bak,}
```

---

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark **gizlenmez** — sonraki oturumun en değerli
> bilgisidir.

### 1 — 🚨 Planın işaret sözdizimi tabloyu YOK EDİYORDU (§174.2)

Plan işareti satırın **üstüne**, kendi satırına koyuyordu:

```markdown
<!-- kapasite: profile=sweep … -->
| Buffered (`Idempotency-Key`) | 1 | 174 | 1044 ms | 1057 ms | 0.94 |
```

`faz-uygulama` Adım 1 gereği ölçüldü — kabul edilmedi. Sonuç: **GFM tabloyu ilk
yorumda bitirir.** Repo'nun kendi remark sürümüyle koşuldu:

| Sözdizimi | Ayrıştırma sonucu |
|---|---|
| Plandaki (satır üstü yorum) | tablo **1 satıra** düşer, kalan satırlar boş `paragraph` olur |
| Kapan `\|` sonrası yorum | başlıkta olmayan **4. hücre** üretir (render'da atılır, yapı bozuk) |
| **Hücre içi** (kapan `\|` öncesi) | tablo **bozulmaz**, hücre sayısı doğru, HTML'de görünmez |

Seçilen: **hücre içi.** Kullanıcı kararı. Bu aynı zamanda repo'nun Faz 158'de
kurduğu emsaldir (`<!-- claim:option … -->`, `configuration.md:324` bunu zaten
bir tablo hücresinde yapıyor) — plan o emsali bilmiyordu.

**Ders:** plan doğru problemi çözüyordu, yanlış mekanizmayla. Sözdizimi
iddiaları da yapısal iddiadır ve ölçülmeden kabul edilmez.

### 2 — Kapı `dokuman-bakim.py`'ye değil `check-content.mjs`'e kuruldu

Plan kapıyı `scripts/dokuman-bakim.py`'ye koyuyordu. Sapma 1'den sonra işaret
sözdizimi `claim:` emsaliyle aynı aileye girdi ve o emsalin kapısı
`docs-site/scripts/check-content.mjs`'tedir. Kullanıcı kararı.

Ölçüldü: `check-content.mjs` `repositoryRoot` sabitini **zaten** taşıyor
(satır 31), yani `bench/` ağacını okumak tek satırdır — taşımanın maliyeti
yoktu. Sonuç: kapasite sayısı da, davranış iddiası da, konsol ekranı da aynı
kapıdan geçiyor ve sevk edilen sayfayı denetleyen tek bir yer var.

Bedeli: testler Python `unittest` yerine `node --test`. `package.json`'daki
`check:content` betiğine eklendi, yani CI'da **koşuyor** (eklenmeseydi test
dosyası var olur ama hiç çalışmazdı).

### 3 — İşaret Türkçe değil İngilizce: `capacity:`, `kapasite:` değil

Plan `<!-- kapasite: … -->` yazıyordu. `production.md` **sevk edilen İngilizce
bir sayfadır** ve dil sınırı (K-228) pakete giren/çalışma anında çalışan her
şeyin İngilizce olmasını ister. `SourceLanguageTests` bu dosyayı taramıyor
(kapsamı `src`·`tests`·`samples`·`packages`), yani kapı yakalamazdı — kural
yine de geçerli. Emsal de İngilizce: `claim:`.

### 4 — Kapsam 12 satırdan 19 satıra çıktı, çünkü 3 satır YANLIŞTI

DoD "12 tablo satırı" diyordu. Kapsam üç tabloya çıkarıldı (12 latency + 4
open-loop + 3 storage). Gerekçe: planın kendi eşleme tablosu Faz 166'nın
**4 numaralı** bulgusunu ("tek tekrarın ortalama gibi sunulması") Denetim 3'e
düşürüyor ve o bulgu storage tablosundadır. 12 satırla sınırlı bir kapı, var
olma sebebinin bir bölümünü açıkta bırakırdı.

🚨 **Bunu yaparken Faz 166'nın 4 numaralı 🔴 bulgusunun KAPANMADIĞI bulundu.**
Kapanış "Düzeltildi. …seed şekli de karışıktı" diyordu; ölçüm aksini gösterdi:

| Yayımlanan | İzlendiği hücre | Uyuşmazlık |
|---|---|---|
| `~8.4 KiB (1 clean repeat)` | `sweep buffered/**full**/c1` = 8.41 KiB, `storageRepeats=**2**` | seed `full`, tekrar 2 — ikisi de yanlış yazılmış |
| `~10.3 KiB (1 clean repeat)` | `sweep queued/empty/c1` = 10.26 KiB, `storageRepeats=1` | doğru |
| `~14.9 KiB (3 clean repeats)` | `sweep streaming/**full**/c8` = 14.93 KiB | seed `full`, diğer ikisi `empty` |

Satır sayıları da üç ayrı hücreden geliyordu. Yani tablo **tek bir ölçüm
hücresine izlenmiyordu** ve bunu hiçbir şey ölçmüyordu.

**Düzeltme (kullanıcı kararı):** üçü de aynı hücre ailesine (`empty`,
`concurrency=1`) bağlandı ve sayılar o hücreden yeniden üretildi:

| Sütun | Önce | Sonra |
|---|---|---|
| Buffered satır/bayt | 10.5 · ~8.4 KiB (1 clean repeat) | 10.5 · **~8.6 KiB** (1 clean repeat) |
| Queued satır/bayt | 10.6 · ~10.3 KiB (1 clean repeat) | **10.9** · ~10.3 KiB (1 clean repeat) |
| Streaming satır/bayt | 27.6 · ~14.9 KiB (3 clean repeats) | **27.8** · **~14.0 KiB** (**1** clean repeat) |
| 1M projeksiyonu | ~15 GB | **~14 GB** |

Yeniden ölçüm **gerekmedi** ve K-775 çiğnenmedi: sayılar saklanan yapıttan
geliyor, değişen yalnız hangi hücrenin alıntılandığıdır. Faz 166 denetiminin
beş bulgusunu kapattığı yöntemin aynısı.

### 5 — Plandaki `test_yuvarlama_tek_kaynaktan_gelir` gerçek bir kural buldu

Sayfanın `Completed/s` biçimi tek kural değildi: `0.94` iki ondalık, `29.5` üç
anlamlı basamak. Tek bir kuralla ifade edildi — **üç anlamlı basamak, en çok
iki ondalık** — ve 12 satırın 12'sinde tutuyor. Yuvarlama **bir kez** yapılır;
üçe sonra ikiye yuvarlamak tek turdan farklı basamak verebilir.

### 6 — Kapının kendi testleri kapıda ÜÇ kusur buldu

Test yazmak bir tören değildi; üçü de gerçek kusurdu ve ikisi kapıyı sessizce
işe yaramaz kılıyordu:

| # | Kusur | Neden ciddi |
|---|---|---|
| 1 | `processorCount` **çıplak sayıyla** aranıyordu | Sayfa "64 logical processors" yazarken kapı **yeşil** dönüyordu: `10` dizesi `.NET 10.0.**10**0` içinde geçiyor. Tesadüfen eşleşebilen bir değer hiç denetlenmiyor demektir. Düzeltme: her alan sayfanın kullandığı **ifadeyi** üretir (`10 logical processors`) |
| 2 | Bozuk işaretli satır kapıyı **çökertiyordu** | `kinds` boş kalıyor, `SCHEMAS[undefined].join` atıyordu. Kapının çökmesi, bulgu vermemesiyle aynı sonucu verir |
| 3 | Manifest'ler çelişince **yanlış profili** suçluyordu | Referans alfabetik ilk profildi; `soak`=4 iken `sweep`=10 için "sapma" raporlanıyor, üstüne bir de "sayfa '4 logical processors' yazmıyor" deniyordu. Düzeltme: çelişkide sayfa o alan için **hiç** denetlenmez, çelişki tek bulgu olarak raporlanır |

Ayrıca satır sonu kaydırmasına karşı ortam paragrafı boşluk-normalize edilerek
aranıyor: `16 GiB,\n.NET 10.0.100` ile tek satır aynı iki olguyu söyler.

### 7 — Kapı düz metindeki sayıları da denetliyor (denetim sonrası)

Plan yalnız tabloları ve ortam cümlesini kapsıyordu. `faz-denetim` sayfanın düz
metinde iki `p95` değerini **"p99" diye** yayımladığını buldu — kapının var olma
sebebi olan sınıf, kapının göremediği yerde. Yeni bir `kind=value` işareti tek
bir sayıyı tek bir ölçüm alanına bağlar:

```markdown
a p99 of 1721 ms<!-- capacity: kind=value profile=sweep scenario=buffered seed=full concurrency=1 field=latency.p99 -->
```

Beş sayı böyle bağlandı. Biri (`p50-spread`) **tavan** olarak denetlenir:
"within about N%" iddiası ölçülen en geniş farktan küçükse bulgudur.

### 8 — `Queue wait p95` artık yayılımı iki uçla yayımlıyor (denetim sonrası)

İlk yazım "yayılımın içinde olmak" istiyordu; denetim ölçtü: 16,45/16,65/16,71 s
pencereleri için **"16.5 s" de "16.7 s" de** yeşil geçiyordu. Sütun artık
`16.5-16.7 s` yazmak zorunda ve yuvarlama `formatRate` ile tek kaynaktan gelir.
Sayfanın iki satırı buna göre düzeltildi.

### 9 — Envanter kapının içine taşındı (denetim sonrası)

"İşaretli satır ara" kuralı tek yönlüydü: işaretleri **silmek** kapıyı
susturuyordu. İki kural eklendi — başlığı bir şemayla eşleşen **işaretsiz
tablo** bulgudur, ve beklenen satır sayısı (`EXPECTED_ROWS`) kapının içinde
durur. Ayrıntı ve ölçüm: *Denetim Bulguları* 4.

## Bu Fazda Verilen Kararlar

| # | Karar | Tür |
|---|---|---|
| **K-784** | Bir kapı, iddianın **makine okunur bir kaynağı varsa** DOĞRULUĞU denetler; kaynak yoksa yalnız VARLIĞI denetleyebilir. K-766'nın diğer yüzüdür, istisnası değil | Kapı yazma kuralı (§174.4, Açık Soru 2 → A) |

Karar defterine **girmeyenler** (faz dokümanında kalır): işaret sözdizimi,
kapının hangi dosyada yaşadığı, yuvarlama kuralı. Üçü de yerel tercihtir;
`AGENTS.md`'nin karar defteri eşiğini geçmezler.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 5 (işaret sözdizimi · kapı yeri · işaret dili · kapsam 12→19 satır · düz metin sayıları kapıya alındı) |
| Düzeltme turu sayısı | 5 (3'ü kapının kendi testlerinden, 2'si `faz-denetim`den) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **7 / 0 / 0** — 3'ü kapının kusuru (kendi testleri buldu), 1'i Faz 166'dan devreden açık storage bulgusu, 2'si `faz-denetim` (yanlış adres + sayfanın p95'i p99 diye yayımlaması), 1'i denetimin gösterdiği envanter deliği |
| Fazın ürettiği regresyon | 0 — on kapı adımının onu da ✅ |
| Faz kapandıktan sonra bulunan kusur | (kapanışta boş) |

Ölçülen ek: **plandaki 8 test 34 oldu.** Fark tesadüf değil — planın test
listesi mutlu yoldan türetilmişti; ters yön testleri (solid p95'in indicative
işaretlenmesi, çelişen manifest, bozuk işaret, boş envanter) kapının üç
kusurunu bunlar buldu; son 13'ü denetimin açtığı kapsanmamış yolları kapatıyor.

**Mutasyon testi koşuldu.** Üç karşılaştırma tek tek silindi (`storage` satır
sayısı · düz metin değeri · `arrival` kuyruk beklemesi); **üçü de** bir testi
düşürdü. Kapsanmayan dal oranı %10,7 ve kalanlar yalnız savunma yollarıdır.

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi (taze bağlam, salt-okunur) **2 🔴**, **6 🟡**
ve **5 🟢** bulgu üretti. Denetçi kapının kendisini bağımsız doğruladı: sayfanın
kopyası üzerinde 24 mutasyon denedi, 19 işaretin 19'unun **tek** eşleşen hücreyi
gösterdiğini doğruladı ve storage düzeltmesini kendi hesabıyla onayladı.

🚨 **Denetimin en değerli bulgusu bir 🟡 değil, bir 🔴'nin sınıfıydı:** bu fazın
kapısı kurulurken sayfa hâlâ iki `p95` değerini `p99` diye yayımlıyordu —
kapının var olma sebebi olan sınıfın ta kendisi, kapının göremediği bir yerde.

| # | Seviye | Bulgu | Kapanış |
|---|---|---|---|
| 1 | 🔴 | DoD'un 1. satırı ve "Doğrulama komutları" bloğu `dokuman-bakim.py`'yi adres gösteriyordu; sapma 2 kapıyı taşımıştı ama reçete güncellenmemişti. Sonraki oturum dokümanın kendi komutunu koşup "kapı yok" sonucuna varırdı | **Düzeltildi.** DoD ve komut bloğu `check-content.mjs`'i gösteriyor; DoD'un başına taşımayı anlatan bir uyarı kondu |
| 2 | 🔴 | Sayfa `production.md:610-612`'de iki **p95** değerini "p99" diye yayımlıyordu (9376/8946 = queued c64'ün p95'leri; gerçek p99 10429/9003). Üstelik 8946 üstteki tabloda p95 sütununda **zaten** duruyordu. Aynı paragrafın "within about 1%" iddiası da sapıyordu — ölçülen en geniş p50 farkı **%1,73** (streaming/c32) | **Düzeltildi ve KAPIYA ALINDI.** Sayılar düzeltildi (10429/9003, "about 2%") ve yeni bir `kind=value` işareti düz metindeki sayıyı da ölçüm alanına bağlıyor. Yayılım iddiası `kind=value field=p50-spread` ile **tavan** olarak denetleniyor: iddia ölçülen farktan küçükse bulgu |
| 3 | 🟡 | `storage` ve `arrival` denetimlerinin hiçbir hata yolu testle kilitli değildi; `compare()` çağrılarını silmek 21 testin hiçbirini düşürmüyordu | **Düzeltildi.** 13 test eklendi (34 toplam) ve **mutasyon testiyle** doğrulandı: üç karşılaştırmanın her biri silindiğinde bir test düşüyor |
| 4 | 🟡 | **İşaretsiz bir tablonun tamamı** kapının envanterine girmiyordu. Ölçüldü: uydurma sayılarla 4. bir kapasite tablosu → kapı yeşil; 12 latency işareti silinip sayı bozulunca → kapı yeşil | **Düzeltildi.** İki yeni kural: (a) başlığı bir şemayla eşleşen **işaretsiz tablo** bulgudur — başlık kapasite tablosunun imzasıdır; (b) `EXPECTED_ROWS` kapının içinde durur, işaret silmek kapıyı sessizleştirmez. İkisi de testle kilitli |
| 5 | 🟡 | Kapının kaç sayı denetlediği **üç ayrı yerde üç farklı** yazılmıştı (67 · 64 · 48), üçü de yanlış | **Düzeltildi.** Ölçüldü: **90** birebir karşılaştırma. Tek sayı üç belgede de aynı, ve `check-capacity-stamp.test.mjs` kind başına sayımı iddia ediyor |
| 6 | 🟡 | `Queue wait p95` yalnız aralık **içinde** olmayı istiyordu; ölçüldü: 16,45/16,65/16,71 s pencereleri için "16.5 s" de "16.7 s" de yeşil geçiyordu | **Düzeltildi.** Sütun artık ölçülen yayılımı **iki ucuyla** yayımlamak zorunda (`16.5-16.7 s`) ve yuvarlama `formatRate` ile tek kaynaktan geliyor. Sayfa buna göre düzeltildi |
| 7 | 🟡 | `Path` sütunu hiç karşılaştırılmıyordu; `Buffered` ↔ `Streaming` etiketlerini takas etmek kapıyı yeşil bırakıyordu | **Düzeltildi.** Etiket, işaretin `scenario`'sunu içermek zorunda (latency + storage, 15 satır) |
| 8 | 🟡 | İki DoD satırının (`samples/Tracon.Api` ile gerçek `run`, `secret` taraması) ne kanıtı ne muafiyeti vardı | **Düzeltildi.** `secret` taraması koşuldu (`kapi.py tarama` ✅); `run` satırı **açıkça muaf** tutuldu ve gerekçesi yazıldı — faz `src/`·`tests/`·`samples/`'a hiç dokunmuyor |

🟢 bulguların beşi de [`ADAYLAR.md`](../../ADAYLAR.md)'ye **F-240** olarak düştü:
`SCHEMAS.storage`'daki sabit tekrar sayısı · `P95_SAMPLE_FLOOR`'un elle senkronu ·
7 karakterlik commit damgasının sessizce denetlenmemesi · `checkArrivalRow`'un
`status` bakmadan toplaması · işaretlerin `llms-full.txt`'e sızması.

## Sonraki Faza Devir Notu

- **🚨 Bir kapı kurmak, kapının kapattığını iddia ettiği sınıfın kapandığını
  KANITLAMAZ.** Faz 166 denetiminin 4 numaralı 🔴 bulgusu "Düzeltildi" yazıyordu;
  bu faz ölçtüğünde **hâlâ açıktı** — storage tablosu `full` ve `empty` seed'leri
  karıştırıyor, 2 tekrarı "1 clean repeat" diye yayımlıyordu. Bir düzeltmenin
  tuttuğunu yalnız **onu koşan bir kapı** söyler. Bir bulguyu "kapandı" diye
  işaretlerken, kapanışı koşan şeyin ne olduğunu yaz.

- **🚨 Markdown tablosunun satırları arasına yorum konmaz** — tablo o satırda
  biter, 13 satır 1'e düşer ve `npm run build` **yeşil kalır**. İşaret hücrenin
  **içine**, kapan `|`'dan önce girer. Ölçüm ve emsal:
  [`hafiza/dokumantasyon.md`](../../hafiza/dokumantasyon.md).

- **🚨 Bir kapının envanterini sayfanın kendisi belirleyemez.** İlk yazım
  "işaretli satır" arıyordu; işaretleri silmek kapıyı **sessizleştiriyordu**.
  İki yönlü olmalı: beklenen sayı kapının içinde durur (`EXPECTED_ROWS`) ve
  tablonun **başlığı** onu kapasite tablosu yapar, işaretleri değil. Envanterini
  denetlediği şeyden okuyan her kapı bu deliği taşır — yeni kapı yazarken sor:
  *"denetlenecek şeyi silersem kapı bağırır mı, susar mı?"*

- **Yayımlanan sayı yalnız tabloda olmaz.** Bu fazın ikinci 🔴'si düz metin
  cümlesindeydi. `kind=value` işareti tek bir sayıyı tek bir alana bağlar ve
  ucuzdur; sayfaya yeni bir sayı yazan sonraki oturum onu da işaretlesin.

- **Tablo hâlâ elle güncelleniyor.** Bu faz yalnız **doğrular**. Ölçüm
  yenilendiğinde 90 karşılaştırmanın hepsi elle güncellenir ve `EXPECTED_ROWS`
  da değişebilir. Sayfayı ölçümden **üreten** bir adım F-NN adayıdır; kapı o
  adımın kabul testi olur.

- **Kapı `docs-site/scripts/`'te, `dokuman-bakim.py`'de değil.** Kapasite,
  davranış iddiası (`claim:`) ve konsol ekranı artık aynı kapıdan geçiyor:
  `npm run check:content`. Sevk edilen sayfaya kural ekleyen sonraki faz oraya
  baksın; `dokuman-bakim.py` geliştirme dokümanlarının kapısıdır.
