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
  `agentprism.doayen.web.tr`.
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

## 🚨 Karar numarasını tablonun SONUNA bakarak seçme (K-539)

Faz 77 ve Faz 78 aynı tabandan yazıldı. İkisi de `KARARLAR.md` §2'nin son satırına
baktı, `K-534` gördü ve **ikisi de** `K-535` ile `K-536`'yı aldı. Dört satır, iki
numara, farklı içerik — ve indeks üreteci hiç ötmedi, çünkü `_kararlar_kalemleri`
satır satır regex okur ve tablo **yapısına** bakmaz.

İki şey bunu görünmez kılmıştı:

- Tablo **sıralı değildi** (`K-018` satır 70'te, `K-524` en sonda), yani "son
  satır = en büyük numara" varsayımı zaten yanlıştı.
- Tabloyu **kesen boş satırlar** vardı (`K-351`/`K-352` ve `K-535`/`K-536` arası).
  Markdown'da boş satır tabloyu orada bitirir; sonraki kararlar başlıksız ikinci
  bir tabloya düşer. Faz 77 denetimi bunlardan yalnız birini gördü ve "kozmetik"
  diye kapattı — kozmetik değildi, numara çakışmasını gizleyen şeyin yarısıydı.

Doğrusu: numarayı **maksimumdan** al, son satırdan değil.

```bash
grep -oE "^\| \*\*K-[0-9]+" docs/KARARLAR.md | grep -oE "[0-9]+" | sort -n | tail -1
```

Kapı artık var: `python3 scripts/dokuman-bakim.py --denetle` yinelenen numarayı,
tabloyu kesen boş satırı ve sıra dışı numarayı **hata** olarak bildirir. Çakışma
çıkarsa tarih kuralı uygulanır — **önce tahsis edilen numarayı korur**; sonraki
taşınır ve o fazın dokümanındaki referansları da taşınır.

## 🚨 `build-agent-map.mjs`'in "Rule:" satırı tablo ÖNCESİ paragrafı da toplar (Faz 85)

`section.prose` bir bölümün tablo dışındaki TÜM satırlarını sırayla biriktirir
— tablo öncesi bir lead-in cümle de, tablo sonrası kural cümlesi de. `Rule:`
satırı bu birikmiş metnin `firstSentence()`'ıdır, yani tablo öncesine bir
paragraf eklersen üreteç SESSİZCE o cümleyi kural sanır ve doğru kural asla
görünmez. Ölçüldü: "Embedding points" bölümüne tablo öncesi bir açıklama
eklenince map bunu "Rule:" olarak bastı, gerçek kural cümlesi (tablo sonrası)
hiç görünmedi — hiçbir kapı bunu yakalamadı çünkü üreteç GEÇERLİ bir metin
üretti, yalnız yanlış cümleyi seçti. Var olan HER bölüm heading→table→(yalnız)
kural paragrafı sırasını izler; yeni bölüm de bunu izlemeli.
