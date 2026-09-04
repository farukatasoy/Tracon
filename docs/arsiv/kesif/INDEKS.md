# Tükenmiş Keşif Turları — Arşiv

> **Baştan sona okunmaz.** Bu dizin, kalemleri kapanmış veya
> [`ADAYLAR.md`](../../ADAYLAR.md)'ye taşınmış keşif turlarının kaydını tutar.
> Açık turlar [`docs/kesif/`](../../kesif/README.md) altında kalır.
>
> Bir tur buraya, **yalnız** açık kalemi bitince taşınır. Taşınan dosyanın
> başına bir arşiv bloğu yazılır: hangi kalem nereye gitti, hangi kalem düştü.
> Durum alanları taşındığı anda bayatlar; dosya bundan sonra **yalnız
> `grep` hedefidir** — bir iddianın nasıl ölçüldüğünü ve neden reddedildiğini
> aramak için.
>
> ```bash
> grep -rn "AsyncLocal" docs/arsiv/kesif/
> ```

| Tur | Sonuç |
|---|---|
| [`2026-08-23-yapisal-sorun-envanteri.md`](2026-08-23-yapisal-sorun-envanteri.md) | 24 kalem: 20 kapandı (Faz 91–109) · 1 ölçümle düştü · 1 kapsam dışı · 2 adaya taşındı (F-67, F-165) · 3 karara bağlı |
| [`2026-08-21-faz-adaylari-tespiti.md`](2026-08-21-faz-adaylari-tespiti.md) | 16 kalem üretti (F-101…F-138); dokuzu `ADAYLAR.md`'de, yedisi `PLANA-DONUSEN-ADAYLAR.md`'de. Öksüz kalem yok |
| [`2026-08-20-maf-ekosistem-taramasi.md`](2026-08-20-maf-ekosistem-taramasi.md) | MAF/MEAI/MCP yüzey taraması; F-45 · F-133 · F-134. 2026-08-26'nın iki turu daha yeni bir yüzey tarayıp geçersizleştirdi |
| [`2026-08-21-uygulanabilirlik-raporu.md`](2026-08-21-uygulanabilirlik-raporu.md) | Dış tüketici raporu (`0.0.0-preview.0.291`): F-113 → Faz 113 · F-115 → Faz 115 · üç ardıl tur onu geçersizleştirdi |

**Neden ayrı dizin:** `docs/kesif` ve `docs/arsiv` doküman bütçesinden zaten
ayrı ayrı hariçtir (K-426, `scripts/dokuman-bakim.py` `HARIC`). Taşıma bir
bütçe hamlesi **değildir**; sıcak dizinde yalnız açık turların kalmasını
sağlar — `grep` sonucu bir tur kaydına düştüğünde okuyanın onu bir iş listesi
sanmaması içindir.
