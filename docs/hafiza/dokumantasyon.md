# Dokumantasyon Tuzaklari

> Sevk edilen dokumantasyonun DOGRULUGU (paketlenen XML, paket README'leri,
> paketlenen OpenAPI belgesi, metin kapisi yazma tuzaklari).
>
> Site YAYIN hatti ve Starlight temasi AYRI dosyadadir:
> [`site-yayin-ve-tema.md`](site-yayin-ve-tema.md). Site UREtim betikleri
> (`build-agent-map.mjs`, `docfx`) ve onlarin kapi davranisi da AYRI dosyadadir:
> [`site-uretim-kapilari.md`](site-uretim-kapilari.md) (Faz 122'de ayrildi).
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

## 🚨 `faz-arsivle` kendi bağlantı onarımını kaçırabilir — koştuktan SONRA denetle (Faz 104)

Skill "tek bir yeni kırık bağlantı üretirse taşımayı geri alır" diyor. Faz
104'te geri **almadı**: fazın kendi gövdesindeki `../arsiv/fazlar/103-*.md`
bağlantısı `../../../arsiv/fazlar/103-*.md` olarak yeniden yazıldı — dosya
zaten `docs/arsiv/fazlar/` içine taşındığı için doğru yol yalnız
`103-*.md`'dir. Onarım, dosyanın **yeni** konumunu değil eski derinliğini
kullanmış. Kural: `faz-arsivle` koştuktan sonra `dokuman-bakim.py --denetle`
çıktısındaki **Kırık bağlantı** satırını oku; sıfır değilse elle düzelt.
Aynı ağaçtaki kardeş faza verilen bağlantılar en riskli olanlardır.

## Sevk edilen XML dokumani kendi kapisina takilir (Faz 99)

`ShippedDocumentationSelfContainmentTests` yalniz `docs/` yollarini ve faz/karar
numaralarini degil, **sesi** de denetler: alarm emojisi (🚨), `Rationale:`
acilisi ve `Measured (…)` bloklari sevk edilen `///` XML'inde YASAKTIR (site
ureticileri bunlari zaten kirpiyor; paket kirpilmamis metni sevk ettigi icin
kural kaynakta zorlanir). Faz 99'da `IModelProvider`'in yeni `<remarks>`'ina
konan tek bir 🚨 kapiyi kirmiziya dondurdu. Implementation yorumu (`//`) kapsam
DISINDADIR — maintainer icin "K-320 bu konumu olctu" yazmak serbesttir, ayni
cumle `<summary>` icinde degildir.

## 🚨 Sayısal sıralama knob'unun YÖNÜ dokümanda ters yazılır

`IAgentDecorator.Order`'ın `<summary>`'si yıllarca gerçek davranışın tersini
söyledi ("a lower value wraps *inside*", oysa düşük değer **dışta** sarar).
Üç şey bunu görünmez yaptı:

- **Davranış testi bir cümleyi göremez.** `Priority`'nin davranışı
  `CompositeAgentCatalogTests` ile zaten test ediliyordu; public cümlesi yine
  de belirsizdi. Bu sınıfın kapısı kaynak **metnini** okumak zorundadır.
- **Arayüz kendi kendisiyle çelişiyordu.** Yanlış kuraldan hemen sonraki örnek
  cümle ("Run recording uses 0, which makes it the outermost") doğruydu; el
  yazısı `docs-site/concepts/runs.md` diyagramı da doğruydu. Tek bir cümle
  yanlıştı ve çevresindeki her şey doğru olduğu için kimse şüphelenmedi.
- **"Higher priority" İKİ anlama gelir.** `IAgentCatalog.ListAsync` "the source
  with the higher priority wins" diyordu; `AgentSourcePriority.Database = 100`
  yanında bu **yüksek sayı** gibi okunur, oysa düşük sayı kazanır. Sıralama
  dokümanında "higher/lower priority" yazma — **"lower value"** yaz.

Kapı: `OrderingContractDocumentationTests` (`tests/AgentPrism.Core.UnitTests/
Architecture/`). Yeni bir sayısal sıralama/öncelik yüzeyi eklersen `Contracts()`
tablosuna satır ekle (K-642).

**Kapıyı yazarken tuzak:** ilk sürümü test tiyatrosuydu. `"lower"` ve
`"value wins"` ayrı ayrı arandığı için, kasıtlı bozma (`lower` → `higher`)
YEŞİL geçti — aynı özetin ilerisindeki ikinci bir "lower" kontrolü kurtarıyordu.
Metin kapısı **bağlı tek ifade** aramalıdır, parça parça değil; ve her vakada
ayrı ayrı kırmızı verdiğini ölç.

## 🚨 Bir arayüzün "kendi dokümanı" başlığıyla sınırlı değildir (Faz 121, K-643)

`SeamContractDocumentationTests` ilk sürümü yalnız `public interface I...`
satırının HEMEN ÜSTÜNDEKİ `<summary>`/`<remarks>` bloğunu okuyordu. Bu,
`IJobHandler`'ı (K-641'in referans örneği) yanlışlıkla "delivery guarantee
belgesiz" işaretledi — çünkü "at-least-once" cümlesi arayüz başlığında değil,
`ExecuteAsync`'in KENDİ `<remarks>`'inde duruyor. Bir metin kapısı "arayüzün
kendi XML dokümanı" derken, arayüzün İÇİNDEKİ her üyenin doküman bloğunu da
kapsamalı — yalnız başlığı değil. Düzeltme: tarama artık başlık doc'u +
gövdedeki her `///` satırını birleştirip arıyor (`InterfaceDocSurface`).
Regresyon testi: `A_dimension_answered_on_a_MEMBERs_doc_counts_as_answered`.

## 🚨 Site içeriği repo private iken kendi GitHub URL'ine bağlanamaz (Faz 123)

`check-content.mjs` `repositoryIsPublic=false` (`site.config.mjs`) iken
`repositoryUrl` metnini (link veya düz metin, ikisi de) her sayfada reddeder
— okuyucuya bugün `404` dönerdi. Elle yazılan bir sayfa `CHANGELOG.md`'ye
"bağlantı ekle" gibi bir plan kararını uygularken bunu yakaladı. Repo private
kaldığı sürece dosya adını düz metinle (`` `CHANGELOG.md` ``) anlat, GitHub
URL'i yazma; repo açıldığında `repositoryIsPublic=true` olur ve gerçek
bağlantı eklenebilir.

## 🚨 Repo-geneli doküman taraması dependency cache'ini dışlamalıdır

CI `NUGET_PACKAGES` değerini repo içindeki `.nuget/packages` dizinine koyar.
Repo-geneli `*.md` taraması bu ağacı dışlamazsa dependency README'lerini ürün
dokümanı sanır; paket içinde sevk edilmeyen göreli hedefler sahte kırık link
üretir. `kirik_baglantilar()` `.nuget` ağacını atlar ve regression testi CI
dizin yapısını geçici ağaçta yeniden kurar.
