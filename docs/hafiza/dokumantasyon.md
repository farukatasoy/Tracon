# Dokumantasyon Tuzaklari

> Sevk edilen dokumantasyon (paketlenen XML, paket README'leri, paketlenen
> OpenAPI belgesi) ve `docs-site/` ureteclerinin tuzaklari.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Sinir: **nasil paketlenir** sorusu
> [`paketleme-ve-dagitim.md`](paketleme-ve-dagitim.md)'dedir; **pakete giren
> metnin dogrulugu** buradadir.

## Sevk edilen metnin kurali (Faz 75, K-514)

- **Pakete giren bir metin yalniz tuketicinin elindeki seylere gonderme yapar.**
  Yasak: faz numarasi, `K-NNN`, `F-NN`, `K1`–`K4`, `MT-*`/`HATA-*`,
  `section N.N`, `open question N`, `docs/NN-*.md`. Serbest: paketin kendi
  tipleri, yapilandirma anahtarlari, HTTP yollari, MSBuild ozellikleri ve
  `farukatasoy.github.io/AgentPrism`.
- **Kural sesi de kapsar**: 🚨/⚠️, `Rationale:` acilisi, `Measured (2026-…)`.
  Site ureteci bunlari zaten siliyordu — yani proje bu sesi tuketiciye uygun
  bulmuyordu, yalniz paket tarafinda zorlamiyordu.
- **Kapsam disi ve bilerek**: `//` uygulama yorumlari. Bakimcinin okudugu bir
  yorumun "K-320 bu konumu olctu" demesi DOGRU davranistir; ayni cumle `///`
  icine girerse `.xml` olarak tuketiciye gider.
- Kapi: `ShippedDocumentationSelfContainmentTests` — `SourceLanguageTests`
  mekanigi, taban cizgisi BOS.

## 🚨 Satir bazli tarama XML yorumunda YETMEZ (K-515)

XML yorumu kaynak genisliginde sarilir, bu yuzden `(phase 65)` rutin olarak iki
satira bolunur: bir satir `(phase` ile biter, sonraki `65)` ile baslar. Satir
satir tarayan bir regex **iki yariyi da goremez**. Olculdu (Faz 75): uc referans
bu delikten gecti ve yalnizca site ureteci blogu birlestirdiginde yakalandi.
Kural: `///` blogunu birlestir, oyle esles, sonra eslesmeyi kapsadigi satirlara
dagit.

## 🚨 Metin onarim zinciri kusuru GIZLER, cozmez (K-516)

`build-api-reference.mjs` ve `build-http-api.mjs` toplam ~110 satirlik bir
`replace` zinciri tasiyordu. Zincir sessizce bozuyordu:
`"Request to promote a run to a case,."`, `"Example: `[...]`. for the format."`,
`POST.../trigger`. Kaynak temizlenince zincir **olculerek** kaldirildi: 689
uretilen sayfanin yalniz biri degisti. Yeni desen: **onarma, hata ver.**

Ayni sinifin ikinci tuzagi: `\s+([,.;:])` kurali bir ELIPSI bozar
(`POST .../trigger` → `POST.../trigger`). Noktalama sikistiran her kural
`(?!\.)` ile korunmalidir.

## 🚨 `<see cref>` paketlenen OpenAPI'de TAM IMZA olur (K-517)

ASP.NET Core'un XML dokuman ureteci `<see cref="X"/>`'i cumlenin ortasina
`string? ClientToolResult.ErrorMessage` olarak basar. Olculdu: 26 yer. Site
kopyasi bunu bir suzgecle siliyordu, paketlenen kopya silmiyordu. OpenAPI'nin
seri hale getirdigi sozlesme tiplerinde `<c>UyeAdi</c>` yaz; ic tiplerde
`<see cref>` IDE gezinmesi icin kalir.

## Uretilen sayfa ve onbellek tuzaklari

- **`docs-site/src/content/docs/{api,http-api}/` ve `public/openapi/` GITIGNORE'dur.**
  Commit edilmezler; her yayinda uretilirler. `git status` temiz gorunurken
  uretilen icerik bayat olabilir.
- **🚨 `--skip-docfx` BAYAT onbellek okur.** `docfx/api-md` bir onceki kosumdan
  kalir; kaynak degistiyse `--skip-docfx` eski metni uretir ve olcumunu
  yaniltir. Kaynak XML'i degistiren her turda TAM kosum gerekir.
- **`dotnet build` sonrasi kosmayi unutma**: docfx `artifacts/bin/*/release_net10.0/*.xml`
  okur; derlemeden once kosarsan onceki surumun metnini alirsin.

## Ekran goruntusu ureteci

- `AGENTPRISM_UI_SCREENSHOTS=1 dotnet test tests/AgentPrism.Ui.E2ETests -c Release`
  ile uretilir; liste `DocumentationScreenshotTests.Screens` icindedir ve her
  girdi bir **landmark** tasir — render olmayan ekran testi dusurur, spinner
  fotografi uretmez.
- **🚨 Bos ekran ogretmez.** `SeedCatalogAsync` bir skill, bir schedule,
  tetiklenmis bir job, bir trigger ve bir MCP sunucusu kurar; iki `run` tek bir
  `sessionId` paylasir. Tohumlama iki sozlesmeyi de ortaya cikardi:
  `JobScheduleSaveRequest.Payload` atanmazsa uc **500** doner (`JsonElement`
  `Undefined` tuzagi) ve tetikleyici imzalama anahtari
  `AgentPrism:TriggerSecrets:` onekini ZORUNLU tutar.

## Icerik kapisi yazarken

`check-content.mjs`'e iddia eklerken **gevsek yazmak kolaydir**; ikisi ilk
yazimda gevsekti ve hicbir sey yakalamadi:

- Bir ekranin anlatildigini "sayfada adi geciyor" ile olcme — `Jobs` kelimesi
  o ekrani hic anlatmayan bir capraz baglantida da gecer. **Baslik ara**:
  `^#{2,3} .*\bJobs\b`.
- Bir adin belgelendigini `includes()` ile olcme — `agentprism.tenant.id`,
  `agentprism.tenant.identifier`'in ON EKIDIR. **Kelime siniri kullan**.

Kapiyi yazdiktan sonra **kirmizi oldugunu gor**: bir ekrani yeniden adlandir,
bir adi degistir, bir sayiyi bozar. Gormeden yesil kabul etme.

## Sunum kapilari (Faz 76)

`check-content.mjs` artik icerigin yaninda **sunumu** da olcer: kapanis bolumu
(`## Read next`, en fazla uc baglanti), diyagram borcu (esik 6 500 B + gerekceli
muafiyet listesi), `site.css` token ciftlerinin WCAG kontrasti, bolum basina
`og:image`, ve elle yazilan sayfalarda ic gelistirme referansi. Sayfa agirligi
ayri bir betiktedir (`check-weight.mjs`) cunku `dist/` uzerinden olculur.

## 🚨 `check-content.mjs` TEMIZ bir checkout'ta kosar — statik import onu kirar

Kapi derlemeden **once** kosar, yani `src/generated/*-sidebar.json` ve
`content/docs/{api,http-api}/` henuz YOKTUR (ucu de `.gitignore`'da). `sidebar.mjs`
o JSON'lari **statik** import edince kapi temiz klonda `ERR_MODULE_NOT_FOUND` ile
dustu — ve yerelde yesil gorundugu icin ancak bagimsiz denetim buldu. Uretilen bir
dosyayi okuyan her modul `existsSync` ile kosullu okumali. Dogrulama:
`mv src/generated /tmp && node scripts/check-content.mjs`.

## 🚨 Kapiyi CI'da hangi is kosuyor?

`pages` isi `github.event_name != 'pull_request'` kosulludur. Oraya konan bir kapi
**hicbir PR'i durdurmaz**. `check-content.mjs` yalniz `node:` yerlesikleri ve
yerel dosya okur (olculdu: `node_modules` silinmisken kosuyor), bu yuzden `npm ci`
olmadan PR'da kosan `build` isine konabilir. Agirlik kapisi `dist/` ister ve
`pages`'te kalir.

## 🚨 Onek karsilastirmasinda ayirici

`file.startsWith(join(docsRoot, 'http-api'))` elle yazilan **`http-api.md`**'yi de
yakalar; o sayfa boylece frontmatter, aciklama uzunlugu, kurulum komutu ve diyagram
erisilebilirligi denetimlerinin hepsinden sessizce muaf kaldi. `sep` eklendi;
denetlenen sayfa 38 → 39.

## 🚨 Mermaid: kenar etiketi dugumun degil PLAKANIN uzerindedir

Flowchart stil sayfasi HER `.label`'i `nodeTextColor` ile boyar — kenar etiketleri
dahil — ve kenar etiketi %50 saydam bir dikdortgene cizilir, yani zemini plakayla
**karisimdir**. Koyu dolgu + beyaz metin kutularin icinde okunur, aralarinda
okunmaz. Tek murekkep rengi + acik dolgu ikisini birden cozer (K-520).

Ikinci tuzak: **`astro-mermaid` kendi CSS'ini calisma aninda `document.head`'e
ekler.** `[data-theme="dark"] pre.mermaid[data-processed]` bizim
`.sl-markdown-content pre.mermaid[data-processed]`'imizle esit puanlidir ve sonra
geldigi icin beraberligi kazanir. Plaka rengi ozniteligi tekrarlayarak yazildi
(`[data-processed][data-processed]`); `!important` secilmedi cunku o gelecekteki
her duzeltmeyi de yener.

Ucuncusu: **diyagram sozdizimi derleme aninda dogrulanmaz** — mermaid tarayicida
render eder ve bozuk bir diyagram sessizce bir hata kutusu cizer. Yeni diyagram
eklerken `astro preview` + tarayici ile bak; `mermaid.parse` Node'da DOM olmadan
calismaz.

## Starlight: logo ve bolum basina og:image bilesen ISTEMEZ

Prizma isareti `logo: { src: './public/favicon.svg' }` ile gelir — dosya tek
kopyadir, Astro derlemede hash'li bir kopya uretir. Bolum basina `og:image`
`routeMiddleware` ile yazilir (`src/starlightRouteData.mjs`); `starlightRoute.head`
dizisi orada degistirilebilir. Faz 76 sifir bilesen gecersiz kildi.

🚨 Bir de sasirtan yer: Starlight basliklari `.sl-heading-wrapper.level-h2` icine
**sarar** (cengel baglantilari aciksa). `.sl-markdown-content > h2` seciciisi
hicbir seyi eslemez.

