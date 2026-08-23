# Kapanmış Faz Dokümanları — Arşiv

> **Baştan sona okunmaz.** Bu dizin kapanmış fazların (00–89) planlarını
> tutar; hiçbir oturum bunları açılışta okumaz. Bir fazın neden öyle
> yapıldığını ararken `grep`'le:
>
> ```bash
> grep -rn "idempotency" docs/arsiv/fazlar/
> awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/20-MALIYET-VE-GOSTERGE-PANELI.md
> ```
>
> Faz durumu ve bağlantılar **üretilir**: [`../../YOL-HARITASI.md`](../../YOL-HARITASI.md).
> Yalnız açık veya planlanan fazlar `docs/` kökünde kalır.

## Neden burada

`docs/**.md` dizin bütçesi (5 MB) her fazda ~41 KB büyüyordu ve Faz 76
kapanışında yalnız 74 KB boşluk kalmıştı — bir sonraki faz sınırı aşacaktı.
`scripts/dokuman-bakim.py` `docs/arsiv`'i **bilerek** denetim dışı tutar
("arşive taşımak sayacı gerçekten düşürür"); kapanmış faz dokümanı bu
tanıma uyar çünkü `faz-baslangic` yalnız **güncel** fazı ve bir öncekinin
"Sonraki Faza Devir Notu" bölümünü okur.

Taşıma sonrası sayaç: **4.925.858 → 3.146.178 bayt** (%37 boş).
Karar: `docs/KARARLAR.md` — K-523.

## Bu kayıtlar damıtılmıştır (Faz 90)

Bir faz kapandığında dokümanı **tam metniyle** arşivlenmez. `faz-damit` planın
kapanışta ölen kısmını düşürür ve kalıcı bilgiyi tutar:

| Kalır | Düşer |
|---|---|
| Başlık bloğu + `> **Durum:**` (üreteç bunu okur) | `Bu Faza Başlarken` — bir oturum talimatı |
| `Plandan Sapmalar` · `Bu Fazda Verilen Kararlar` | `Planlanan Public API` / `Dosya Listesi` |
| `Denetim Bulguları` · `Sonraki Faza Devir Notu` | `NN.x` iş kalemleri — planın gövdesi |
| `Bitiş Ölçütleri (DoD)` — aynen, işaretsiz kutu dahil | `Gerçekleşen Public API` / `Dosya Listesi` |
| Tanınmayan her bölüm (korunur + raporlanır) | `Riskler` · `Testler` · `Açık Sorular` |

**İçerik silinmez.** Her kaydın başındaki blok tam metne götüren `git show`
komutunu taşır ve `dokuman-bakim.py --denetle` her koşumda o SHA'nın
çözüldüğünü **kanıtlar** — git geçmişine güvenmek ancak bir kapı onu
doğruluyorsa meşrudur.

Ölçüm (2026-08-23): 3.040.663 → 1.200.025 B (−%61); medyan kayıt 12.584 B.
`docs/arsiv/**` artık kendi bütçesine tabidir — muafiyet sınırsız değildir.
