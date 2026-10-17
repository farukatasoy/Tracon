# Ek — Arayüze dokunulduysa: çeviri kapısı

> `faz-tamamlama` Adım 1'den açılır. Tetik: ekran metni eklendi ya da değişti.

Arayüz Faz 30'dan beri iki dillidir. Yeni bir ekran metni yalnız `en.ts`'e
eklenirse `tsc` durur, dolayısıyla **unutulamaz** — ama şunlar unutulabilir:

- Yeni metin gerçekten çevrildi mi, yoksa İngilizcesi mi kopyalandı?
  `i18n.test.ts` bunu denetler; birebir aynı kalması gereken teknik terimler
  testin içindeki listede **açıkça** yazılıdır. O listeye satır eklemek bir
  karardır, kısayol değil.
- Metin üzerine iddia kuran yeni E2E testi dili sabitledi mi?
  `Session.OpenAsync` varsayılanı `en-US`'tir (K-231).
- Yeni mesaj mevcut bir mesajın **öneki** mi? Playwright `GetByText` alt dizi
  eşler; önek çakışması testi strict mode ihlaliyle kırar.
