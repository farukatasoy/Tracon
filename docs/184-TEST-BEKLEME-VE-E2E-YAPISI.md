# Faz 184 — Test Bekleme ve E2E Yapısı

> **Durum:** 📋 Planlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-261**
> **Önkoşul:** Yok
> **Paketler:** yalnız `tests/` — sevk edilen paket değişmez
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok · sevk edilen: Yok
> **Manuel test alanı:** `docs/manuel-test/09-ARAYUZ-GENEL.md` ve komşu arayüz aileleri — E2E bölünmesi bu ailelerin `➜ CI` işaretlerini etkileyebilir (Faz 180 şablonu)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.

1. Bu doküman
2. Alan hafızası — bu fazın ana okuma kümesi:
   [`hafiza/test-paralellik-ve-zamanlama.md`](hafiza/test-paralellik-ve-zamanlama.md) ·
   [`hafiza/test-kosum-tuzaklari.md`](hafiza/test-kosum-tuzaklari.md) ·
   [`hafiza/test-yalitimi.md`](hafiza/test-yalitimi.md) — ölçülmüş altı zamanlama
   kusuru ve serileştirme kararlarının gerekçesi buradadır; bu faz o kararları
   **geri almadan önce** sebeplerini söker
3. Kusur protokolü: [`kusur-giderme` SKILL](../.agents/skills/kusur-giderme/SKILL.md)
   Adım 2 — kırılgan test ayrıştırma komutları bu fazda yoğun kullanılır
4. [`tests/Tracon.Ui.E2ETests/xunit.runner.json`](../tests/Tracon.Ui.E2ETests/xunit.runner.json)
   ve [`ci.yml:211`](../.github/workflows/ci.yml) — bugünkü serileştirme yüzeyi

---

## Amaç

Testler duvar saatine yaslanıyor: senkronizasyon yerine sabit `Task.Delay`,
istikrar yerine serileştirme. Bedel iki katlı — tam koşum ~557 sn (ölçüm:
`kapi.py` yorumu, 2026-09-04) ve sabit bekleme yavaş makinede yine kırılgan.
E2E tarafında 80 test tek 145 KB dosyada ve 7 web-first `Expect`'e karşı 95
navigation var: düşen test "timeout" der, "hangi element eksikti" demez.

- **F-261** — sabit beklemeleri koşul-beklemeye çevir; `UiTests.cs`'i ekran
  başına dosyalara böl; `WaitForAsync` kalıplarını web-first `Expect`'e taşı.
  Paralellik artırımı **ayrı ve ölçüm-kapılı** son adımdır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -rE "Task\.Delay\(" tests` | **121 satır**; senkronizasyon amaçlı sabit örnekler: [`DrainTests.cs:110`](../tests/Tracon.AspNetCore.FunctionalTests/DrainTests.cs) (200 ms), [`ToolGovernanceEndpointTests.cs:326`](../tests/Tracon.AspNetCore.FunctionalTests/ToolGovernanceEndpointTests.cs) (1 sn) |
| [`UiTests.cs`](../tests/Tracon.Ui.E2ETests/UiTests.cs) | **144.937 bayt, 80 test tek dosyada**; `Expect(` yalnız **7** |
| [`xunit.runner.json`](../tests/Tracon.Ui.E2ETests/xunit.runner.json) | `maxParallelThreads: 4` + `conservative` |
| [`ci.yml:211`](../.github/workflows/ci.yml) | `-maxcpucount:1` — çözüm düzeyi serileştirme |
| [`kapi.py:695`](../scripts/kapi.py) | Tam koşum ölçümü 557 sn (TRX'li) |

> Kanıtlar 2026-09-22 tarihinde doğrulandı.

---

## 184.1 — Sabit beklemeden koşul-beklemeye

121 `Task.Delay` satırı üç sınıfa ayrılır ve sınıfı satırın yanına yazılır:

| Sınıf | Kader |
|---|---|
| Senkronizasyon (bir durumun oluşmasını bekliyor) | Koşul-bekleme: durumu yoklayan `WaitUntilAsync(koşul, zamanAşımı)` yardımcıları veya ürünün kendi sinyali (`SSE` olayı, store sorgusu, `TaskCompletionSource`) |
| Ürün davranışının parçası (ör. lease süresi dolmalı) | Kalır; yorumu sınıfını söyler — sayaç bunları ayırt eder |
| Gerçek zaman kısıtı taklidi | Test zamanı kısaltılabilir yapılandırmaya bağlanır (seçenek zaten varsa) |

Yardımcı `tests/Shared/` altına girer; her dönüşüm tek tek commit edilir ve
`kusur-giderme` Adım 2 komutlarıyla (paket koşumu + izole koşum) kırılganlık
ölçülür. Dönüşüm bir testi düşürürse bu **bilgidir** — sabit bekleme gerçek
bir yarışı gizliyordu; kusur ayrı kaydedilir, sessiz geçilmez.

## 184.2 — `UiTests.cs` bölünmesi

Ekran başına dosya (`Ui/DashboardTests.cs`, `Ui/PlaygroundTests.cs`, …);
paylaşılan akış `BrowserFixture` + yardımcılarda kalır. Bölme **davranış
değiştirmez** — `git log --follow` kaybını göze alıp içerik birebir taşınır.
`PlaywrightLocatorTests` kapısı (küçülen taban) bölünmeden etkilenmemeli.

## 184.3 — `WaitForAsync` → web-first `Expect`

95 navigation'lık akışta bekleme kalıpları `Expect(locator).ToBeVisibleAsync()`
ailesine çevrilir: düşüş mesajı "timeout" yerine eksik elementi söyler.
Dönüşüm ekran dosyası başına yapılır; her dosyada dönüşüm sonrası koşum kanıtı.

## 184.4 — Paralellik: yalnız ölçümle

Serileştirme kararları (`-maxcpucount:1`, `maxParallelThreads: 4`) ölçülmüş
altı zamanlama kusurunun sonucuydu — sebepler 184.1'de söküldükten **sonra**,
tek değişken oynatılarak ölçülür: önce E2E `maxParallelThreads`, sonra çözüm
düzeyi. Süre ve kırmızı sayısı kapanışa yazılır; hedef süre **yazılmaz**
(ölçülmemiş rakam yazılmaz kuralı).

---

## Planlanan Public API

Yok.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
tests/Shared/WaitUntil.cs                     (koşul-bekleme yardımcıları)
tests/Tracon.Ui.E2ETests/Ui/<Ekran>Tests.cs   (bölünmüş E2E)
tests/Tracon.Ui.E2ETests/UiTests.cs           (silinir — içerik taşınır)
tests/** (121 satırın dokunulan kısmı)
.github/workflows/ci.yml                      (yalnız 184.4 ölçümü gerektirirse)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Koşul-bekleme sonsuz döngüye girer | Birim (yardımcının kendisi) | `WaitUntilTests` — zaman aşımı ve iptal vakaları |
| Dönüşüm gizli yarışı açığa çıkarır (test düşmeye başlar) | Süreç | `kusur-giderme` Adım 2 ayrıştırması; kusur F-kaydı alır, geri sabit beklemeye dönülmez |
| E2E bölünmesi locator kapısını bozar | Repo kapısı | `PlaywrightLocatorTests` küçülen taban — bölünme sonrası aynı sayı |
| Paralellik artırımı gizli paylaşımı tetikler | Ölçüm | tek değişkenli koşum; kırmızı çıkarsa o adım geri alınır ve sebep hafızaya yazılır |
| `Expect` dönüşümü iddiayı zayıflatır (yanlış locator'a bakar) | Gözden geçirme | dönüşüm ekran başına ayrı commit; `faz-denetim` örneklem okur |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Faz bitti | Tam paket koşumu ×2 (arka arkaya) | İki koşumda da aynı sonuç; kırmızı yok |
| 2 | E2E bölündü | Bir E2E dosyasını tek başına koştur | Ekran dosyası bağımsız koşuyor; fixture paylaşımı çalışıyor |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | 121 satırın hepsi bu fazda mı? | A: hepsi sınıflandırılır, senkronizasyon sınıfının tamamı dönüştürülür · B: yalnız fonksiyonel testler, entegrasyon sonraya | **A** sınıflandırma + **B** dönüşüm — sınıflandırma ucuzdur ve tam yapılır; dönüşüm en kalabalık ve en kırılgan aileden başlar, faz sonunda kalan varsa sayısıyla devredilir |
| 2 | `WaitUntil` yardımcısı `Tracon.Testing`'e (sevk edilen) girer mi? | A: hayır, `tests/Shared` · B: evet, tüketici de ister | **A** — sevk edilen yüzey büyütmek Faz 182 ile ters yönde; B ancak dış talep kanıtıyla |

---

## Bitiş Ölçütleri (DoD)

- [ ] 121 satırın sınıflandırması tamam; senkronizasyon sınıfından dönüştürülen/kalan sayılar kapanışta
- [ ] `UiTests.cs` silindi; ekran başına dosyalar koşuyor; `PlaywrightLocatorTests` tabanı değişmedi
- [ ] Dönüştürülen E2E dosyalarında `Expect` kullanımı; düşüş mesajı element adı veriyor (bir örnek kapanışa yapıştırılır)
- [ ] Tam paket koşum süresi önce/sonra ölçüldü ve yazıldı (hedef rakam vaat edilmez)
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri eklendi ve koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

### Doğrulama komutları

```bash
grep -rE "Task\.Delay\(" tests --include="*.cs" | wc -l   # sınıflandırma yorumu taşımayan satır kalmamalı
python3 scripts/kapi.py ic-dongu
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Dönüşüm dalgası koşumu uzun süre kırmızı tutar | Ekran/dosya başına küçük adımlar; her adım kendi koşum kanıtıyla |
| Serileştirmeyi erken gevşetmek eski kusurları geri getirir | 184.4 en sona; tek değişken; hafızadaki altı vaka önce okunur |
| Bölünmüş E2E dosyaları fixture'ı yanlış paylaşır | `BrowserFixture` sözleşmesi bölünmeden önce yazılır; collection tanımı açık |

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
