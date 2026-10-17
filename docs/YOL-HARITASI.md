# Faz Yol Haritası

> **Üretilen dosya. Elle düzenleme.** Kaynak: her fazın kendi
> dokümanındaki `> **Durum:**` satırı. Açık fazlar `docs/` kökünde,
> kapanmış fazlar `docs/arsiv/fazlar/` altında yaşar.
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

Bir fazın durumu yanlış görünüyorsa **o fazın dokümanını** düzelt;
bu dosyayı düzeltmek bir sonraki üretimde geri alınır.

## Fazlar (4 kalem)

| Faz | Konu | Durum |
|-----|------|-------|
| [183](arsiv/fazlar/183-COKLU-TFM-TEST-MATRISI.md) | Çoklu TFM Test Matrisi | ✅ Tamamlandı |
| [187](arsiv/fazlar/187-KIRICI-DEGISIKLIK-KAPISI.md) | Yayınlanmış Sürüme Karşı Kırıcı Değişiklik Kapısı | ✅ Tamamlandı |
| [193](193-NET8-NET9-DUSURME.md) | `net8.0` ve `net9.0` Hedeflerinin Düşürülmesi | 📋 Planlandı |
| [218](arsiv/fazlar/218-YETKI-ANAHTARI-ALANINI-KALDIRMA.md) | `AuthorizationConfigurationKey` Alanını Kaldırma | ✅ Tamamlandı |

Seçilmemiş adaylar: [`ADAYLAR.md`](ADAYLAR.md). Faz 0–218 arasındaki kapanmış kayıtların çoğu 2026-10-17'de silindi; numaralar kullanılmıştır ve tam metinleri `2a8149ef` commit'indedir (`git show 2a8149ef:docs/arsiv/fazlar/<dosya>`).
