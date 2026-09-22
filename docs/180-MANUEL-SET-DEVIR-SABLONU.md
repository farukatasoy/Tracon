# Faz 180 — Manuel Set Devir Şablonu

> **Durum:** 📋 Planlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-165**
> **Önkoşul:** Yok
> **Paketler:** yalnız `tests/` ve `docs/manuel-test/` — sevk edilen paket değişmez
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok · sevk edilen: Yok
> **Manuel test alanı:** setin kendisi — devir işareti tüm aile dosyalarına girebilir

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-166\|K-167\|K-738" docs/KARARLAR.md
   ```
   **K-166/K-167** (birim testleri gerçek hataları kaçırdı; kanıt örnek
   uygulamadan gelir), **K-738** (yük ölçümü rapordur, kapı değildir — devir
   sırasında süre eşiği yazılmaz)
3. [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md) —
   seviye seçim tablosunun tamamı; bu fazın ana aracıdır
4. [`docs/manuel-test/00-INDEKS.md`](manuel-test/00-INDEKS.md) — §1 (set
   yapısı) ve §tally (son koşumun sonucu); 36 aile dosyasını **okuma**
5. Alan hafızası: [`hafiza/test-altyapisi.md`](hafiza/test-altyapisi.md) ·
   [`hafiza/test-kosum-tuzaklari.md`](hafiza/test-kosum-tuzaklari.md)

---

## Amaç

Manuel kabul seti bugün tek regresyon ağıdır ve CI'da koşmaz: 2026-09-16
koşumu üç gün sürdü ve 43 kusur buldu — 4.199 otomatik testin hiçbirinin
görmediği kusurlar. Bu faz seti CI'a **toptan taşımaz** (adayın kendi engeli:
kuyruğu bitmez); bir case'in manuel setten otomatik teste **nasıl devredildiğini**
tek şablona bağlar ve şablonu ilk aile üzerinde uçtan uca kanıtlar.

- **F-165** — tekrar edilebilir devir şablonu + ilk aile dilimi; model-yanıtı
  ve insan-yargısı case'leri açıkça manuel kalır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `docs/manuel-test/` sayımı | **37 dosya, 1.872 benzersiz `MT-*` case** (aday gövdesindeki 1.632 sayısı bayatlamıştı — büyüme sürüyor) |
| [`00-INDEKS.md`](manuel-test/00-INDEKS.md) tally | Son tam koşum 2026-09-16→19: 1.813 ☑ · 26 ☐ · 19 ⏭ · 1 ☒; 43 kusur çıktı; yalnız **iki** tam koşum var |
| [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) | `docs/manuel-test/` hiçbir job'da geçmiyor — set CI'a görünmez |
| [`scripts/manuel-test-tazelik.py`](../scripts/manuel-test-tazelik.py) | Tazelik kapısı var ama yalnız spec bayatlamasını ölçer; devir kavramı yok |

> Kanıtlar 2026-09-22 tarihinde doğrulandı.

---

## 180.1 — Devir şablonu: bir case'in üç olası kaderi

Her `MT-*` case'i şablon uygulanınca üç sınıftan birine düşer ve sınıf spec
satırında görünür kılınır:

| Sınıf | İşaret | Kural |
|---|---|---|
| Devredildi | `➜ CI: <TestSınıfı.TestAdı>` | Davranış [test seviyeleri tablosuna](../.agents/ortak/test-seviyeleri.md) göre doğru seviyede otomatikleşti. Case spec'ten **silinmez** — koşum talimatı düşer, işaret kalır (tek kaynak: davranış tanımı spec'te, kanıt CI'da) |
| Manuel kalır | `👤 insan gerekir — <sebep>` | Model kalitesi, görsel yargı, fiziksel ortam (ses cihazı, gerçek sağlayıcı hesabı). Sebep tek cümle |
| Zaten kapsanıyor | `➜ CI: <mevcut test>` | Devir yeni test yazmaz; mevcut testi işaret eder. İlk dilimde beklenen en kalabalık sınıf budur |

Seviye kuralı plandan seçilir, uygulama oturumundan değil: sınır geçen davranış
(DI · HTTP · kiracı · akış · depo · paket) birim testine **devredilemez**.

## 180.2 — İşaretin makine tarafı

`scripts/manuel-test-tazelik.py` devir işaretini sayar ve rapora üç sütun ekler:
aile başına `devredildi / manuel / işaretsiz`. Kapı kuralı **cırcır değildir** —
bu faz yalnız saymayı kurar; "işaretsiz azalmalı" kuralı ilk dilimin ölçümü
görüldükten sonra ayrı kararla bağlanır. `➜ CI:` işaretinin gösterdiği test
sınıfının gerçekten var olduğu doğrulanır (bayat işaret = kırmızı).

## 180.3 — İlk aile dilimi

Bir aile uçtan uca devredilir ve şablonun gerçek maliyeti ölçülür: kaç case
devredildi, kaç yeni test yazıldı, kaç mevcut teste işaret verildi, kaç case
manuel kaldı. Bu sayılar kapanışta **ölçülür**, planda tahmin edilmez.

---

## Planlanan Public API

Yok — sevk edilen paketlere dokunulmaz.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
docs/manuel-test/<ilk-aile>.md        (devir işaretleri)
docs/manuel-test/00-INDEKS.md         (devir şablonu bölümü)
scripts/manuel-test-tazelik.py        (devir sayacı)
scripts/manuel_test_tazelik_test.py   (sayacın testleri)
tests/<ilgili proje>/                 (dilimin yeni testleri)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Devir işareti var olmayan bir test sınıfını gösteriyor | Script birimi | `manuel_test_tazelik_test.py` — bayat işaret vakası |
| Sınır davranışı birim teste devredilip test tiyatrosu oluşuyor | Denetim | `faz-denetim` — dilimin her devri seviye tablosuna karşı okunur |
| Devredilen case'in spec metni ile testin iddiası ayrışıyor | Script birimi | işaret + test adı eşlemesi raporda görünür; koşumda insan karşılaştırır |
| Sayaç aile dosyasındaki tablo biçim varyantlarını kaçırıyor | Script birimi | mevcut ayrıştırıcı testlerine varyant vakaları eklenir |

Beş soru bu fazda script yüzeyine uygulanır: iptal (yok — script kısa ömürlü) ·
eşzamanlılık (yok) · boş/aşırı girdi (boş aile dosyası, işaretsiz set) · başka
kiracı (yok) · alt sistem hatası (git yoksa açık hata).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Devir işaretli aile | `python3 scripts/manuel-test-tazelik.py` | Rapor aile için `devredildi/manuel/işaretsiz` sayılarını basar |
| 2 | İşaret bayat (test silindi) | Aynı komut | Kırmızı; bayat işaret adıyla listelenir |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | İlk aile hangisi? | A: `07-HTTP-YONETIM-API` · B: `02-CEKIRDEK-VE-KATALOG` | **A** — sınır HTTP'dir, `Tracon.AspNetCore.FunctionalTests` altyapısı hazırdır ve "zaten kapsanıyor" oranı yüksek çıkacağı için şablonun üç sınıfı da ilk dilimde görünür |
| 2 | `➜ CI:` işaretinin tam sözdizimi | A: satır sonu eki · B: ayrı sütun | Uygulama oturumu aile dosyasının gerçek tablo biçimine bakarak seçer; sayaç testi ikisini de kilitleyecek |

---

## Bitiş Ölçütleri (DoD)

- [ ] Devir şablonu `00-INDEKS.md`'de: üç sınıf, işaret sözdizimi, seviye kuralı
- [ ] İlk aile dilimi devredildi; dört sayı (devredilen/yeni test/mevcut teste işaret/manuel) belgeye yazıldı
- [ ] `manuel-test-tazelik.py` devir sayacı ve bayat-işaret kırmızısı çalışıyor; testleri yeşil
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri bu fazın alanına eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

### Doğrulama komutları

```bash
python3 scripts/manuel-test-tazelik.py
python3 -m unittest discover -s scripts -p "manuel_test_tazelik_test.py"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Kör devir test tiyatrosu üretir (adayın kendi uyarısı) | Seviye tablosu plana gömülü; `faz-denetim` dilimin her devrini seviyeye karşı okur |
| Çift kaynak: spec ile test ayrı yaşar | Case spec'ten silinmez, işaret tek yönlü gösterir; sayaç bayat işareti kırmızı yapar |
| Şablon ilk ailede işe yarar, ikincide yaramaz | Şablon bu fazda yalnız **bir** ailede kanıtlanır; genelleme sonraki dilim fazlarına kalır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
