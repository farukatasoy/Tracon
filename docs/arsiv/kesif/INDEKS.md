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

**Neden ayrı dizin:** `docs/kesif` ve `docs/arsiv` doküman bütçesinden zaten
ayrı ayrı hariçtir (K-426, `scripts/dokuman-bakim.py` `HARIC`). Taşıma bir
bütçe hamlesi **değildir**; sıcak dizinde yalnız açık turların kalmasını
sağlar — `grep` sonucu bir tur kaydına düştüğünde okuyanın onu bir iş listesi
sanmaması içindir.
