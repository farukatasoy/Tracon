# Kapanmış Faz Dokümanları — Arşiv

> **Baştan sona okunmaz.** Bu dizin kapanmış fazların (00–59) planlarını
> tutar; hiçbir oturum bunları açılışta okumaz. Bir fazın neden öyle
> yapıldığını ararken `grep`'le:
>
> ```bash
> grep -rn "idempotency" docs/arsiv/fazlar/
> awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/20-MALIYET-VE-GOSTERGE-PANELI.md
> ```
>
> Faz durumu ve bağlantılar **üretilir**: [`../../YOL-HARITASI.md`](../../YOL-HARITASI.md).
> Açık ve yeni fazlar `docs/` kökünde kalır.

## Neden burada

`docs/**.md` dizin bütçesi (5 MB) her fazda ~41 KB büyüyordu ve Faz 76
kapanışında yalnız 74 KB boşluk kalmıştı — bir sonraki faz sınırı aşacaktı.
`scripts/dokuman-bakim.py` `docs/arsiv`'i **bilerek** denetim dışı tutar
("arşive taşımak sayacı gerçekten düşürür"); kapanmış faz dokümanı bu
tanıma uyar çünkü `faz-baslangic` yalnız **güncel** fazı ve bir öncekinin
"Sonraki Faza Devir Notu" bölümünü okur.

Taşıma sonrası sayaç: **4.925.858 → 3.146.178 bayt** (%37 boş).
Karar: `docs/KARARLAR.md` — K-523.
