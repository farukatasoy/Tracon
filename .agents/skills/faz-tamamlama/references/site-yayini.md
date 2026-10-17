# Ek — Siteyi yayınla

> `faz-tamamlama` Adım 10'dan açılır. Tetik: `docs-site/` içinde bir şey,
> ya da `///` XML dokümanı, `capabilities.md` veya OpenAPI belgesi değişti —
> üçü de üretilen sayfalara girer.

**Site kendiliğinden güncellenmez.** GitHub Pages bunu her push'ta yapıyordu;
K-542'den beri yapmıyor — CI'nin `site` işi yalnız derler ve kapıları koşar,
YAYINLAMAZ. Bu adım atlanırsa kod, doküman ve karar defteri günceldir ama
**kullanıcının gördüğü site bir önceki fazdan kalmadır** ve bunu hiçbir kapı
söylemez.

```bash
./scripts/site-deploy.sh
```

Script derler, dört kapıyı koşar, `rsync`'ler ve konteyneri uzlaştırır. Kapılardan
biri kırmızıysa hiçbir şey yayınlanmaz. Sonra doğrula:

```bash
curl -sI https://tracon.dev/ | head -1     # 200
```

`scripts/site-deploy.local.env` bir `SITE_NEIGHBOR_URL` tanımlıyorsa script sonda
bir `curl` satırı daha basar; onu da koş — aynı sunucudaki diğer servis bozulmamalı.

🚨 **Fazın DEĞİŞTİRDİĞİ sayfayı canlıda aç.** `200` yalnız sitenin ayakta
olduğunu söyler, YENİ olduğunu değil.
