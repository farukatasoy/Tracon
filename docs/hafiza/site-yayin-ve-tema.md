# docs-site Yayin Hatti ve Temasi

> Siteyi YAYINLAMA (`site-deploy.sh`, K-542) ve Starlight temasi (logo,
> `og:image`, baslik sarmalayicilari). Dokuman KAPILARI ve ureteclleri icin:
> [`dokumantasyon.md`](dokumantasyon.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: bu tek bolum `dokumantasyon.md`'nin %31'ine ulasmisti ve
> sonraki en buyuk bolumun DORT katiydi -- `MIMARI.md` §7 ile ayni sekil
> (K-524). Yayin/tema ekseni kapi ekseninden BAGIMSIZ buyuyor.
## Starlight: logo ve bolum basina og:image bilesen ISTEMEZ

Prizma isareti `logo: { src: './public/favicon.svg' }` ile gelir — dosya tek
kopyadir, Astro derlemede hash'li bir kopya uretir. Bolum basina `og:image`
`routeMiddleware` ile yazilir (`src/starlightRouteData.mjs`); `starlightRoute.head`
dizisi orada degistirilebilir. Faz 76 sifir bilesen gecersiz kildi.

🚨 Bir de sasirtan yer: Starlight basliklari `.sl-heading-wrapper.level-h2` icine
**sarar** (cengel baglantilari aciksa). `.sl-markdown-content > h2` seciciisi
hicbir seyi eslemez.
- **`docs-site/` yayin hatti** (AGENTS.md'den, Faz 77): site `dotnet build`'e BAGLANMAZ,
  pakete GIRMEZ, Node **22.12+** ister. `api/` ve `http-api/` sayfalari URETILIR
  (`npm run generate`) ve commit EDILMEZ; ekran goruntuleri E2E kosumundan uretilir
  (`AGENTPRISM_UI_SCREENSHOTS=1`) ve commit EDILIR.
- **🚨 Yayin GitHub Pages DEGILDIR (K-542).** Site `agentprism.doayen.web.tr` adresinde
  kendi sunucumuzda barinir; yayini `scripts/site-deploy.sh` yapar (derleme → dort kapi →
  rsync → `docker compose up -d`). CI'nin `site` isi YAYINLAMAZ, yalnizca derler ve
  kapilari kosar.
- **🚨 Sunucuda nginx/Caddy servisi YOKTUR — Traefik vardir.** :80 ve :443'u Docker
  dinler; yonlendirme Docker LABEL'iyle yapilir. Site bir `nginx:1.27-alpine`
  konteyneridir ve yapilandirmasi `docs-site/deploy/` altinda REPO'dadir. Elle
  sunucuda duzenleme yapma — script her yayinda compose dosyasini da gonderir.
- **macOS'ta `rsync` `--chmod` KABUL ETMEZ.** Apple `openrsync` sevk eder
  (`rsync version 2.6.9 compatible`); bayrak `invalid argument` verir. Derleme
  zaten 644/755 uretiyor, `-a` onu korur.
- **🚨 Adres TEK dosyada yasar: `docs-site/site.config.mjs`.** `site`, `base` ve
  `formerHosts` oradan gelir; sekiz tuketici onu import eder. Bir adres literali
  ELLE yazilirsa `check-content.mjs` 11. kontrolu kizarir — hem yeni bir kopya
  dogdugunda hem de `formerHosts`'taki eski bir barindiriciya isaret edildiginde.
  C# tarafi ayri dildir (`DocumentationLinks`); ikisini `DiagnosticIntegrityTests`
  bagli tutar — o test `site.config.mjs`'i OKUR.
- **`base` `/`dir ve oyle kalir.** El yazisi sayfalar kok-goreli baglanti yazar
  (`/guides/production/`). Alt yola donulurse 39 sayfadaki 224 baglanti da
  guncellenmelidir; `check-links.mjs` bunu yayindan once kizartir.
- **Dil sinirinin kapsadigi yuzeyler** (AGENTS.md'den, Faz 77): kod, yorum, XML dokumani,
  `exception`/log/`ProblemDetails` metni, migration `.sql` yorumu, `template.json`
  aciklamasi — hepsi Ingilizce'dir.
- **Mermaid diyagram tipi ve yazim tuzaklari** (AGENTS.md'den, Faz 77): katman/akis/karar
  agaci → `flowchart TD|LR` · cagri sirasi → `sequenceDiagram` · veri modeli → `erDiagram` ·
  durum makinesi → `stateDiagram-v2` · zaman plani → `gantt`. Turkce etiket serbest, teknik
  terim orijinal dilinde kalir (`AIAgent`). Dugum metninde `(`, `)`, `,`, `:` ayristiriciyi
  bozar — tirnak kullan. Tek fikir anlatir; on bes dugumu asiyorsa ikiye bol.
- **🚨 Arsivleme iki yonlu baglanti kirar; SADECE tasinan dosyanin kendi linklerini
  duzeltmek YETMEZ** (Faz 77): bir blogu `docs/X.md`'den `docs/arsiv/Y.md`'ye
  KOPYALADIGINDA o blogun ICINDEKI goreli linkler hâlâ `docs/`'a goredir ve arsiv
  dizininden cozulmez. Faz 77'de 17 baglanti boyle kirildi (`KARARLAR-GECMISI.md`,
  `PLANA-DONUSEN-ADAYLAR.md`). Kural: tasima sonrasi `docs/arsiv/**` icinde
  "buradan cozulmuyor ama `docs/`'tan cozuluyor" olan her linki yeniden tabanla.
- **🚨 Faz dokumani yeniden adlandirilirken/tasinirken duz `sed` KULLANMA** (Faz 77):
  `docs/manuel-test/` faz dokumanlariyla AYNI `NN-AD.md` desenini kullanir; metin
  eslemesi onlari da bozar. Yeniden yazma COZUMLEMEYE dayanmalidir — link once
  dosyanin ESKI dizinine gore cozulur, tasima haritasindan gecirilir, sonra YENI
  dizine gore gorelilestirilir. 60 faz dosyasinin 874 atifi boyle tasindi.
- **Butce raporu "✅" derken bile DAR bandina bak** (Faz 77): `dokuman-bakim.py`
  yalniz ASIM'da kirmizi verir. Faz 77 oncesi hafiza dongusunde DAR bandi HIC yoktu
  ve 16000/16000 bir dosya "ok" yaziyordu. `--projeksiyon` kalan faz sayisini basar;
  yeni bir sinir koyarken OLCULEN boyuta %15 bosluk ekle (Faz 58.4 kalibrasyon kurali).
