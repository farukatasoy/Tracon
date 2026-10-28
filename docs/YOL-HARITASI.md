# Faz Yol Haritası

> **Üretilen dosya. Elle düzenleme.** Kaynak: açık fazların kendi
> dokümanındaki `> **Durum:**` satırı ve kapanan fazlar için
> [`FAZ-DEFTERI.md`](FAZ-DEFTERI.md). Yeniden üretmek için:
> `python3 scripts/dokuman-bakim.py`

Bir fazın durumu yanlış görünüyorsa **o fazın dokümanını** düzelt;
bu dosyayı düzeltmek bir sonraki üretimde geri alınır.

**Sıradaki boş faz numarası: 255.**

## Açık fazlar (1 kalem)

| Faz | Konu | Durum |
|-----|------|-------|
| [193](193-NET8-NET9-DUSURME.md) | `net8.0` ve `net9.0` Hedeflerinin Düşürülmesi | 📋 Planlandı |

Kapanan fazlar (254 kayıt; son: Faz 254 — Tool Çağrısı Zincirde Kaydedilir: Başarısız Akışsız Run'ın Tool Satırları ve Betik Yolu): [`FAZ-DEFTERI.md`](FAZ-DEFTERI.md). Kapanan faz dokümanı silinir; tam metne defterdeki `git show` işaretçisi götürür. Seçilmemiş adaylar: [`ADAYLAR.md`](ADAYLAR.md).
