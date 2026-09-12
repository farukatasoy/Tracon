# Test Seviyeleri — Ortak Sözleşme

> `.agents/skills/` **dışındadır** — skill keşfi bu dosyayı bir skill sanmaz.
> Bağlayan dosyalar: `faz-uygulama/SKILL.md` · `faz-denetim/SKILL.md` ·
> `kusur-giderme/SKILL.md` · `faz-planlama/resources/faz-plani-sablonu.md`
> (Faz 92). Tek kaynak burasıdır; sınır tablosu başka dosyada tekrarlanmaz.

Birim testi yeterli sanmak bu repoda **sekiz kez** bedel ödetti. Kural
basittir:

> Bir davranış bir **sınırı** geçiyorsa, o sınırın olduğu seviyede test edilir.
> Sınır: DI kapsamı · HTTP · kiracı · akış (SSE) · depo · süreç · paket sınırı.

| Davranış | Doğru seviye | Neden |
|---|---|---|
| Saf hesap, biçimlendirme, doğrulama | Birim | Sınır yok |
| Depo sözleşmesi (yazma/okuma/yalıtım) | `tests/Shared/Contracts/` sözleşme testi | Bellek içi + üç SQL sağlayıcısında birden koşar |
| HTTP davranışı, DI kaydı, yetki | Fonksiyonel (`Tracon.AspNetCore.FunctionalTests`) | Bir depo davranışını düzeltmek çağıranı sessizce değiştirir (K-283) |
| `span`, `scope`, `AsyncLocal`, akışlı yol | Fonksiyonel **ve** örnek uygulama | Birim testi `AsyncLocal` akışını taklit eder, kanıtlamaz |
| Ekran, rota, iki dillilik | E2E (Playwright) | `tsc` yalnız anahtar varlığını zorlar |
| Paketlenmiş tüketicinin gördüğü yüzey | Örnek uygulama + manuel case | `ProjectReference` ile koşan iç test bu sınıfı hiç görmez |

Bir davranışı yanlış seviyede test etmek, test **yokken** yanlış bir güven
üretir. Yeşil bir birim testi bir sınırın doğru çalıştığını **kanıtlamaz**.

## Hata modunu da test et, mutlu yolu değil

Planın hata modu tablosundaki her satır bir teste dönüşür. Tablo yoksa şu
beşini yine de sor:

1. İptal (`CancellationToken`)
2. Eşzamanlılık
3. Boş/aşırı girdi
4. Başka kiracının kaydı
5. Alt sistem hatası (`store` yazamıyor)
