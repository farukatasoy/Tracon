# Faz Yol Haritası

> **Üretilen dosya. Elle düzenleme.** Kaynak: açık fazların kendi
> dokümanındaki `> **Durum:**` satırı ve kapanan fazlar için
> [`FAZ-DEFTERI.md`](FAZ-DEFTERI.md). Yeniden üretmek için:
> `python3 scripts/dokuman-bakim.py`

Bir fazın durumu yanlış görünüyorsa **o fazın dokümanını** düzelt;
bu dosyayı düzeltmek bir sonraki üretimde geri alınır.

**Sıradaki boş faz numarası: 255.**

## Açık fazlar (5 kalem)

| Faz | Konu | Durum |
|-----|------|-------|
| [193](193-NET8-NET9-DUSURME.md) | `net8.0` ve `net9.0` Hedeflerinin Düşürülmesi | 📋 Planlandı |
| [251](251-KESIK-VE-FILTRELI-YANIT-SINYALI.md) | Kesik ve Filtreli Yanıtın Sinyali: Responses `incomplete` ve `tracon.output.filtered` | 📋 Planlandı |
| [252](252-MODEL-BASINA-ISTEK-ALANI-VE-SINIRLARI.md) | Model Başına İstek Alanı Reddi ve Değer Sınırları | ✅ Tamamlandı |
| [253](253-ONBELLEK-YAZMA-MALIYETI.md) | Önbellek Yazma Token'ı ve Fiyatı Run Maliyetine Girer | 📋 Planlandı |
| [254](254-TOOL-CAGRISI-ZINCIRDE-KAYIT.md) | Tool Çağrısı Zincirde Kaydedilir: Başarısız Akışsız Run'ın Tool Satırları ve Betik Yolu | 📋 Planlandı |

Kapanan fazlar (250 kayıt; son: Faz 250 — Alt Agent Sürüm Sabiti): [`FAZ-DEFTERI.md`](FAZ-DEFTERI.md). Kapanan faz dokümanı silinir; tam metne defterdeki `git show` işaretçisi götürür. Seçilmemiş adaylar: [`ADAYLAR.md`](ADAYLAR.md).
