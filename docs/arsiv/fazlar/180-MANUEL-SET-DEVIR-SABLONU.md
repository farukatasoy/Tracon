# Faz 180 — Manuel Set Devir Şablonu

> **Durum:** ✅ Tamamlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-165**
> **Önkoşul:** Yok
> **Paketler:** yalnız `tests/` ve `docs/manuel-test/` — sevk edilen paket değişmez
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok · sevk edilen: Yok
> **Manuel test alanı:** setin kendisi — devir işareti tüm aile dosyalarına girebilir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 4b227a7f:docs/arsiv/fazlar/180-MANUEL-SET-DEVIR-SABLONU.md
> ```
>
> Damıtıldı 2026-09-22 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Manuel kabul seti bugün tek regresyon ağıdır ve CI'da koşmaz: 2026-09-16 koşumu üç gün sürdü ve 43 kusur buldu — 4.199 otomatik testin hiçbirinin görmediği kusurlar.

## Bitiş Ölçütleri (DoD)

- [x] Devir şablonu `00-INDEKS.md`'de: üç sınıf, işaret sözdizimi, seviye kuralı
- [x] İlk aile dilimi devredildi; dört sayı (devredilen/yeni test/mevcut teste işaret/manuel) belgeye yazıldı
- [x] `manuel-test-tazelik.py` devir sayacı ve bayat-işaret kırmızısı çalışıyor; testleri yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri bu fazın alanına eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

### Doğrulama komutları

```bash
python3 scripts/manuel-test-tazelik.py
python3 -m unittest discover -s scripts -p "manuel_test_tazelik_test.py"
```

---

## Plandan Sapmalar

| Plan | Gerçek | Gerekçe |
|---|---|---|
| Açık Soru 2: işaret "satır sonu eki" ya da "ayrı sütun" | **İkisi de, ama TEK ev kuralıyla**: başlık biçiminde `\| **Devir** \| … \|` satırı, tablo biçiminde `Devir` sütunu | İki biçim iki ev ister; ortak olan, işaretin gövde metnine karışmamasıdır. Gövdeye karışsaydı ön koşul metnindeki bir `👤` cümlesi sayıma girerdi — denetim bunu **ölçtü** (devredilmemiş dört ailede yedi sahte `manuel`) |
| 180.2: "`manuel-test-tazelik.py` devir işaretini sayar" | `--taban` **isteğe bağlı** oldu; bayraksız mod yalnız devir raporunu basar ve dosya yazmaz | Fazın kendi doğrulama komutu bayraksızdı. Ayrı bir betik case ayrıştırıcısını kopyalardı (K-411 sınıfı); zorunlu `--taban` ise tabana ihtiyaç duymayan bir ölçüme anlamsız bir argüman dayatırdı |
| Planda yok | **İşaret case imzasından düşürüldü** | Plan bunu öngörmemişti. Düşürülmeseydi bu fazın 43 işareti bir sonraki tazelik ölçümünde **43 sahte `değişti`** üretirdi — `YENIDEN_ADLANDIRMALAR`'ın kapattığı sınıfın aynısı. Ölçüldü: `0` sahte `değişti` |
| Planda yok | **Aile 07'de altı bayat case metni düzeltildi** | Dilim, spec metninin bugünkü kodla çeliştiği beş `title` alıntısı (K-228 öncesi Türkçe) ve kendi kayıtlı sonucuyla çelişen bir case başlığı (`MT-API-054`) buldu. İşaretlemek bunları görünür kıldı — şablonun beklenmedik ikinci faydası |
| Planda yok | **Bir betik kusuru düzeltildi** | `--kosum` repo DIŞINDA bir dizin alınca `relative_to` ham `ValueError` traceback'i atıyordu (dosya yazıldıktan SONRA). Betiğin kendi kabul kuralı (`MT-GDK-022`) ham traceback'i yasaklar |
| 180.3: "kaç yeni test yazıldı" | 13 yeni test metodu + **9 mevcut testin genişletilmesi** | Denetim, işaretlenen testlerin bir kısmının case'in yalnız durum kodunu sınadığını gösterdi. "Zaten kapsanıyor" demek için testin case'in **ayırt edici** iddiasını da sınaması gerekir; eksik iddialar eklendi |

## Bu Fazda Verilen Kararlar

Karar defterine giren yok. Üçü de yerel tercihtir ve burada yaşar:

1. **İşaretin tek evi `Devir` satırı/sütunudur.** Gövdede geçen bir `👤`
   cümlesi işaret değildir.
2. **`➜ CI:` ile `👤` bir aradaysa `➜ CI:` kazanır.** Kanıtın CI'da olduğu
   iddiası doğrulanabilir; "insan gerekir" doğrulanamaz.
3. **`işaretsiz` bir kapı değildir.** Cırcır kurmak, yargılanmamış case'leri
   gelişigüzel işaretlemeye davet ederdi (180.2'nin kendi kuralı).

## Süreç Ölçümü

**Dilimin dört sayısı** (DoD):

| Ölçüm | Değer |
|---|---|
| Devredilen case | **42** / 43 |
| Yeni yazılan veya genişletilen teste işaret eden case | **12** |
| Dokunulmamış mevcut teste işaret eden case | **30** |
| Manuel kalan case | **1** (`MT-API-040`) |
| İşaretsiz kalan case | **0** |

Devredilen 42 case **51 benzersiz testi** gösterir; hepsi
`Tracon.AspNetCore.FunctionalTests` altındadır — HTTP sınırı geçen hiçbir
davranış birim testine devredilmedi. Yeni test metodu: **13**; genişletilen
mevcut test: **9**.

`MT-API-040` neden manuel kaldı: iddiası, **gerçek** bir sağlayıcı
istisnasının `ProviderError` sınıfına düşmesidir (K-296). Sahte sağlayıcı bunu
kanıtlayamaz — gruplamanın kendisi devredildi
(`StatsErrorEndpointTests.Repeated_provider_failures_are_grouped_under_one_error_class`),
sınıflandırma kalmadı. Kapanışta örnek uygulamayla **gözlendi**: gerçek OpenAI
`404`'ü `class: "ProviderError"`, `ClientResultException`, `totalRuns: 2`,
tek küme `count: 2` verdi.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 |
| Düzeltme turu sayısı | 1 (denetim sonrası) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | — |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetcisi`, taze bağlam) bir 🔴, beş 🟡 ve üç 🟢 buldu.

**🔴-1 — kapatıldı.** Beş case `➜ CI:` ile "kanıtı CI'da" ilan edilmişti; ama
BEKLENEN SONUÇ metinleri K-228 öncesi Türkçe `title` alıntıları taşıyordu ve
gösterilen testler yalnız durum kodunu sınıyordu. İşaret, **zaten yanlış olan**
bir case'i kanıtlı gösteriyordu. İki yönlü kapatıldı: metinler bugünkü
İngilizce başlıklara düzeltildi **ve** `IdempotencyTests` ile
`ProblemDetailsTests`'e `title`/`detail`/`type` iddiaları eklendi.

**🟡 beşi de kapatıldı:**

1. Yedi case'in kapsanmayan davranışsal iddiası — versiyon listesi sıralaması,
   reddedilen `DELETE` sonrası agent'ın durması, replay gövdesinin birebir
   aynılığı, ikinci `DELETE`'in `404`'ü, `meta`'nın `roles` bloğu, `401`
   `detail`'inin token sızdırmaması, `ProblemDetails` `type` alanı — hepsine
   iddia eklendi; `MT-API-100`'ün işareti `400` ve `403` testleriyle genişletildi.
2. `SessionEndpointTests.Missing_session_cannot_be_branched_returns_404` **501**
   iddia ediyordu → `Missing_session_branch_reports_not_supported_BEFORE_not_found`.
   `MT-API-054`'ün başlığı da kendi kayıtlı sonucuyla çelişiyordu; düzeltildi.
3. İmzadan düşürme yalnız başlık biçimini kapsıyordu → ayrıştırıcı `CaseBloku`
   ile yeniden yapılandırıldı; `Devir` **sütunu** da düşüyor (hücre boşaltılmaz,
   **düşürülür** — boş bırakmak satıra fazladan bir ayırıcı ekler ve imza yine ayrışırdı).
4. `👤` sayacı gövde metnini sayıyordu (yedi sahte `manuel`) ve kalın yazılmış
   bir işareti kaçırıyordu → tek ev kuralı ikisini birden kapattı.
5. DoD'nin istediği dört sayı → yukarıdaki Süreç Ölçümü tablosu.

**🟢 üçü aday listesine:** `test_envanteri`'nin dosya düzeyinde
`sınıf × metot` çarpımı (bilinçli, tek yönlü güvenli) · `ListPagingClampTests`
iki satırlık tohumla `200` tavanını gözlemleyemiyor · `--kosum`, `--taban`
yokken sessizce yok sayılıyor.

## Sonraki Faza Devir Notu

**Şablon bir ailede kanıtlandı, genelleme sonraki dilimlere kalıyor.**
Kalan 1.832 case işaretsizdir ve bu bir kusur değildir.

Sonraki dilimi alacak oturuma:

- **İşaretlemeden önce case'in BEKLENEN SONUÇ'unu testin gövdesiyle satır satır
  karşılaştır.** Bu fazın tek 🔴'ı ve beş 🟡'ından üçü buradan çıktı: durum kodu
  eşleşiyor diye "zaten kapsanıyor" demek, case'in ayırt edici iddiasını
  (`title` metni, sıralama, ikinci silme, gövde eşitliği) sessizce kanıtsız bırakır.
- **Tablo biçimli bir aile (31–36) ilk kez devredildiğinde** `Devir` sütununu
  başlık satırına ekle; sayaç sütunu adından bulur ve imzadan düşürür. Bu yol
  `manuel_test_tazelik_test.py`'de kilitlidir, ama gerçek bir aile üzerinde
  henüz koşulmadı.
- **İşaretlemek bayat spec metni bulur.** Aile 07'de altı tane çıktı. Bu bir yan
  ürün değil, dilimin ikinci değeridir — bulduğunu düzelt ve say.
- **"İşaretsiz azalmalı" kuralını bağlamak için yeterli ölçüm yok.** En az bir
  dilim daha gerekir; o zamana kadar sayı rapordur, kapı değildir (180.2).
