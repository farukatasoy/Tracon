# Test Seviyeleri — Ortak Sözleşme

> `.agents/skills/` **dışındadır** — skill keşfi bu dosyayı bir skill sanmaz.
> Bağlayan dosyalar: `faz-uygulama/SKILL.md` · `faz-denetim/SKILL.md` ·
> `kusur-giderme/SKILL.md` · `faz-planlama/resources/faz-plani-sablonu.md`
> (Faz 92) · `faz-tamamlama/SKILL.md` · `manuel-test-kosumu/SKILL.md`. Tek
> kaynak burasıdır; sınır tablosu ve kabul basamakları başka dosyada
> tekrarlanmaz.

Birim testi yeterli sanmak bu repoda **sekiz kez** bedel ödetti. Kural
basittir:

> Bir davranış bir **sınırı** geçiyorsa, o sınırın olduğu seviyede test edilir.
> Sınır: DI kapsamı · HTTP · kiracı · akış (SSE) · depo · süreç · paket sınırı.

| Davranış | Doğru seviye | Neden |
|---|---|---|
| Saf hesap, biçimlendirme, doğrulama | Birim | Sınır yok |
| Depo sözleşmesi (yazma/okuma/yalıtım) | Sözleşme testi: taban sınıf `src/Tracon.Testing.Contracts.Xunit/Contracts/`, türetilen sınıf `tests/*/Contracts/` | Bellek içi + üç SQL sağlayıcısında birden koşar |
| HTTP davranışı, DI kaydı, yetki | Fonksiyonel (`Tracon.AspNetCore.FunctionalTests`) | Bir depo davranışını düzeltmek çağıranı sessizce değiştirir (K-283) |
| `span`, `scope`, `AsyncLocal`, akışlı yol | Fonksiyonel **ve** örnek uygulama | Birim testi `AsyncLocal` akışını taklit eder, kanıtlamaz |
| Ekran, rota, iki dillilik | E2E (Playwright) | `tsc` yalnız anahtar varlığını zorlar |
| Paketlenmiş tüketicinin gördüğü yüzey | Paket testi (`tests/Tracon.Package.Tests`, alt süreç `dotnet`) + örnek uygulama | `ProjectReference` ile koşan iç test bu sınıfı hiç görmez |

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

## Kabul basamakları — insan son çaredir

> Kullanıcı kararı, 2026-10-17. Ajanın tarayıcısı, Playwright MCP'si, verilen
> `secret`'ları ve başka araçları vardır. En kaliteli kanıt için ajan hepsini
> dener; insan **son** basamaktır.

Bir davranışın kanıtı **ilk yeterli** basamakta durur. Ajan bir alt basamağa
ancak üstündeki yetmediğinde iner.

| Basamak | Ne zaman | Kanıt | Devir işareti (`docs/manuel-test/00-INDEKS.md` §9) |
|---|---|---|---|
| 1 · Otomatik test | Davranış kodla tekrarlanır | Yukarıdaki tablodaki seviye; CI koşar | ``➜ CI: `Sınıf.Test` `` |
| 2 · Ajan case'i | Davranış gerçek ortam ya da yargı ister | `docs/manuel-test/`; ajan araçlarıyla koşar | `🤖 ajan koşar — <araç>` |
| 3 · İnsan | Ajan aşağıdaki yolları denedi ve yetmedi | `docs/manuel-test/`; kullanıcı koşar | `👤 insan gerekir — <denenen araç> · <neden yetmedi>` |

Ajan 3. basamağa inmeden önce bu yolları dener:

| İhtiyaç | Ajanın yolu |
|---|---|
| Ekran, tıklama, dar ekran, konsol hatası | Playwright MCP (`manuel-test-kosumu` §5) |
| Görsel yargı: tipografi, hizalama, diyagramın anlattığı | Ekran görüntüsünü al ve kendin yargıla. Ölçütü `Gerçek sonuç`'a yaz |
| Model çıktısının kalitesi | Çıktıyı case'in ölçütüyle kendin yargıla |
| Gerçek sağlayıcı ya da dış hesap | Kullanıcının verdiği `secret` — yalnız ortam değişkeninde (K-059) |
| Ses çıktısı | Sağlayıcının STT ucuyla metne çevir ve metni karşılaştır |
| Mikrofon girişi | Chromium sahte cihazı: `--use-fake-device-for-media-stream --use-file-for-fake-audio-capture=<wav>` |
| Başka tarayıcı motoru | `npx playwright install webkit` |
| Ekran okuyucu | Erişilebilirlik ağacı (`browser_snapshot`); gerçek VoiceOver için Guidepup |
| Taze bağlamlı agent ölçümü | Yalnız hedef dokümanı alan bir alt ajan · izole dizinde `claude -p` |
| Gerçek zaman aralığı · `Ctrl+C` | Arka planda bekle · `kill -INT <pid>` |

İnsan yalnız üç durumda kalır: fiziksel bir cihaz emüle edilemez, dış hesap
insan kimliği ister (2FA, CAPTCHA, ödeme) ya da karar ürün sahibinindir.
`👤` işareti denenen aracı adlandırmazsa denetçi bunu bulgu yazar.
