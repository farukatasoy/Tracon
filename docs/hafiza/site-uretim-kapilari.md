# Site Üretim Betikleri ve Kapıları

> `docs-site`'ın kendi ÜRETEÇLERİ (`build-agent-map.mjs`, `docfx`,
> `dotnet format`'ın okuduğu `obj/` çıktısı) ve bunların kapı davranışı.
>
> Sevk edilen metnin DOĞRULUĞU (paketlenen XML, `<see cref>`, metin kapısı
> yazma tuzakları) AYRI dosyadadır: [`dokumantasyon.md`](dokumantasyon.md).
> Site YAYIN hattı ve Starlight teması AYRI dosyadadır:
> [`site-yayin-ve-tema.md`](site-yayin-ve-tema.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnızca bu alana dokunurken
> okunur. Faz 122'de `dokumantasyon.md`'den ayrıldı — bölüm bütçesini (16000 B)
> aşmıştı, K-524'ün `site-yayin-ve-tema.md` ayrımıyla aynı gerekçe.

## Uretilen sayfa ve onbellek tuzaklari

- **Site sayfalarına dokunan HER düzenleme turundan sonra (yalnız ilkinden değil)
  `node docs-site/scripts/build-agent-map.mjs --check` tekrar koş** (2026-09-04,
  Faz 141) — `capabilities.md`'ye dokunmasan bile: üretici
  `docs-site/src/content/docs/**/*.md`'nin TAMAMINI `llms-full.txt`'e gömer, yani
  sonraki her düzenleme map'i yeniden bayatlatır. Vaka:
  [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`docs-site/src/content/docs/{api,http-api}/` ve `public/openapi/` GITIGNORE'dur.**
  Commit edilmezler; her yayinda uretilirler. `git status` temiz gorunurken
  uretilen icerik bayat olabilir.
- **🚨 `--skip-docfx` BAYAT onbellek okur.** `docfx/api-md` bir onceki kosumdan
  kalir; kaynak degistiyse `--skip-docfx` eski metni uretir ve olcumunu
  yaniltir. Kaynak XML'i degistiren her turda TAM kosum gerekir.
- **`dotnet build` sonrasi kosmayi unutma**: docfx `artifacts/bin/*/release_net10.0/*.xml`
  okur; derlemeden once kosarsan onceki surumun metnini alirsin.

## 🚨 `build-agent-map.mjs`'in "Rule:" satırı tablo ÖNCESİ paragrafı da toplar (Faz 85)

`section.prose` tablo dışındaki TÜM satırları biriktirir ve `Rule:` onun
`firstSentence()`'ıdır — tablo ÖNCESİNE paragraf eklersen üreteç sessizce o
cümleyi kural sanır. Her bölüm heading→table→(yalnız) kural paragrafı sırasını
izler; yeni bölüm de izlemeli. Ölçüm:
[`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

**İlgili (Faz 122):** `llms.txt` bütçe aşımında 3. sütunu kısaltmak HİÇBİR ŞEY
değiştirmez — `renderRow` yalnız 2. sütunun ilk iki kod parçasını alır. Gerçek
kaynak ya o kod parçaları ya da `guides/*.md`'nin `description`'ıdır.
`--check`'in verdiği GERÇEK sayıyla iterasyon yap, gözle tahmin etme.
Ayrıntı: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## 🚨 Agent map package adı path separator'a bağlanamaz

`projectPath.split('/')` macOS/Linux'ta package adını verdi, Windows'ta ise tam
`D:\\a\\...\\Tracon.Core.csproj` yolunu verdi. Haritadaki her package satırı
büyüdü; `Tracon.AgentMap.md` ve onu içeren `llms.txt` aynı anda hem bayat
hem budget üstünde göründü. Path bileşenini `node:path` `basename()` ile çıkar.
`build-agent-map.test.mjs`, `win32.basename` ile bu sınırı macOS'ta da doğrular;
test `npm run check:content` kapısının parçasıdır.

Aynı sınır `relative()` çıktıları için de geçerlidir. Windows bu çıktıda `\\`,
POSIX sistemler `/` üretir. Sidebar slug'ı, exemption anahtarı veya üretilen kimlik
olacak her relative path önce `/` biçimine çevrilmelidir. `path-utils.mjs` bu
kuralı merkezileştirir; `path-utils.test.mjs` Windows girdisini her platformda
doğrular.

## 🚨 DocFX assembly metadata girdisine `artifacts/bin` referansı ekleme (Faz 98)

Explicit `src` assembly'leri bağımlılıklarını kendi `.deps.json`'larından çözer;
geniş bir `artifacts` globu aynı basit adlı assembly'nin kopyalarını yükler ve
Roslyn **`CS1704`** üretir. `references.exclude` kök çözüm DEĞİLDİR. `docfx.json`
içindeki `references` girdisi bu yüzden tamamen kaldırıldı ve
`DocfxConfigurationTests` geri eklenmesini yasaklar. Bağımlılık çözümleme sorunu
çıkarsa explicit `src` girdisini ve `.deps.json`'ı incele. Ölçüm:
[`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## 🚨 `dotnet format --verify-no-changes` ve `docfx metadata`, DEBUG yapılandırmasının `obj/` çıktısını okur — yalnız Release derlemesi yeterli DEĞİL (Faz 98)

Her iki araç da MSBuildWorkspace/Roslyn analiz motorunu kullanır ve varsayılan
olarak **Debug** yapılandırmasının `GeneratedMSBuildEditorConfig.editorconfig`
ve referans bilgilerini arar. `dotnet build ... -c Release` çalıştırılmış ama
`-c Debug` hiç çalıştırılmamış YENİ bir proje için bu iki araç `CS0246`
("tip bulunamadı") üretir — proje aslında derlenir, yalnız bu iki aracın
okuduğu `obj/` klasörü boştur. Çözüm: yeni bir proje eklerken kapanış
kapılarından ÖNCE hem `-c Release` hem `-c Debug` ile bir kez derle.

- **🚨 Bir senkron kuralinin hedefi, YUZEY degistiginde gercekten bayatlayan
  IZLENEN dosya olmalidir** (2026-09-06, F-203). `--site-denetle`'nin
  `http-api` kurali elle yazilmis `http-api.md`yi hedefliyordu; o sayfa
  API'nin SEKLIDIR (kimlik dogrulama, akis, sayfalama, hata govdesi) ve bir
  ucun eklenmesi onu degistirmez. Operasyon basina dokumantasyon ise
  `http-api/` altina URETILIR ve `.gitignore`dadir — `git diff` onu HIC
  goremez. Sonuc olculdu: son 40 commit'te kural 7 kez tetiklendi, **5'i
  kirmizi** dondu ve hepsi `--site-gerekce-yazildi` ile gecildi. Surekli
  kirmizi bir kapi, kapi degildir; insanlari onu susturmaya egitir. Hedef
  `docs/openapi/tracon.json` (uretilen ama IZLENEN ve COMMIT EDILEN)
  eklendikten sonra 5 → 3. **Ders: bir kural yazarken "bu hedef, yuzey
  degisince gercekten degisir mi ve `git` onu gorebilir mi" sorusunu
  TARIHE KARSI olc** — `_kural_eslesmesi` saf fonksiyondur, `git log`
  uzerinde dogrudan kosturulabilir. `docs/` ile baslayan hedef artik depo
  koku'ne goredir; her kullaniciya donuk yuzey bir site sayfasi degildir.

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

## 🚨 Repo-geneli doküman taraması dependency cache'ini dışlamalıdır

CI `NUGET_PACKAGES` değerini repo içindeki `.nuget/packages` dizinine koyar.
Repo-geneli `*.md` taraması bu ağacı dışlamazsa dependency README'lerini ürün
dokümanı sanır; paket içinde sevk edilmeyen göreli hedefler sahte kırık link
üretir. `kirik_baglantilar()` `.nuget` ağacını atlar ve regression testi CI
dizin yapısını geçici ağaçta yeniden kurar.

*(Yukaridaki uc bolum 2026-09-07'de `dokumantasyon.md`'den butce icin tasindi; ucu de site/doküman KAPI BETIKLERININ kosum ortamiyla ilgilidir.)*

## Agirlik butcesine yakin sayfa (Faz 125, denetimde gozlemlendi)

`troubleshooting.md` (`check:weight` tavani 57 000 B gzip) bu fazda ~46 satir
eklenince 49 365 B'tan 54 706 B'a cikti — tavanin **%96'si**. Kapi bugun yesil
ve bu fazda hicbir sey olcumsuz buyumedi (yalnizca 🟢 gozlem, 🔴/🟡 degil), ama
sayfaya eklenecek **bir sonraki** icerik `check:weight`'i kirabilir. Yeni bir
APG tanisi veya troubleshooting bolumu eklerken once `npm run check:weight`
ciktisindaki en agir sayfayi kontrol et; troubleshooting.md zaten en agir
sayfaysa (`Heaviest: troubleshooting/index.html`), yeni icerigi ayri bir
sayfaya (ornek: `guides/`) tasimayi degerlendir.

*(2026-09-07'de `dokumantasyon.md`'den butce icin tasindi — `check:weight` bir site kapisidir.)*

## Icerik kapisi yazarken

> Faz 156'da `dokumantasyon.md`'den taşındı.

`check-content.mjs`'e iddia eklerken **gevsek yazmak kolaydir**; ikisi ilk
yazimda gevsekti ve hicbir sey yakalamadi:

- Bir ekranin anlatildigini "sayfada adi geciyor" ile olcme — `Jobs` kelimesi
  o ekrani hic anlatmayan bir capraz baglantida da gecer. **Baslik ara**:
  `^#{2,3} .*\bJobs\b`.
- Bir adin belgelendigini `includes()` ile olcme — `tracon.tenant.id`,
  `tracon.tenant.identifier`'in ON EKIDIR. **Kelime siniri kullan**.

Kapiyi yazdiktan sonra **kirmizi oldugunu gor**: bir ekrani yeniden adlandir,
bir adi degistir, bir sayiyi bozar. Gormeden yesil kabul etme.

## Bir davranış iddiasının ŞEKLİ burada, DEĞERİ dotnet test'te (Faz 158)

Bu dosyanın en üstteki tuzağıyla aynı sebep: `check-content.mjs` derlemeden
ÖNCE koşar, yani reflection için derlenmiş assembly, gerçek deny/allow için
çalışan bir host yoktur. İşaretli bir davranış iddiasının (`<!-- claim:option
Tip.Ozellik=deger -->` / `<!-- claim:policy METOD /yol scope=Kapsam -->`)
burada denetlenebilecek TEK şey şeklidir — tip/özellik gerçekten var mı, scope
adı `ApiKeyScope`'un bir üyesi mi. Gerçek DEĞERİ (gerçek varsayılan, gerçek
izin/red) yalnız `tests/Tracon.AspNetCore.FunctionalTests/
DocumentedPolicyTests.cs` ölçer — o dosya `dotnet test` içinde koşar ve
derlenmiş tipi `Activator.CreateInstance` ile örnekleyebilir. İkisini tek
dosyada birleştirmek bu kapıyı temiz checkout'ta `ERR_MODULE_NOT_FOUND`
tuzağıyla aynı şekilde kırar.

## Sunum kapilari (Faz 76)

`check-content.mjs` artik icerigin yaninda **sunumu** da olcer: kapanis bolumu
(`## Read next`, en fazla uc baglanti), diyagram borcu (esik 6 500 B + gerekceli
muafiyet listesi), `site.css` token ciftlerinin WCAG kontrasti, bolum basina
`og:image`, ve elle yazilan sayfalarda ic gelistirme referansi. Sayfa agirligi
ayri bir betiktedir (`check-weight.mjs`) cunku `dist/` uzerinden olculur.

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

## 🚨 Sayılabilir iddiayı TEK sayfada denetleyen kapı, kopyalarını kaçırır

Bir kapı bir iddianın TEK ÖRNEĞİNİ değil, **SINIFINI** denetlemelidir: tarama
her elle yazılan sayfayı gezer ve "sayı + en çok beş kelime + isim" kalıbını arar
(satır sarması iddiayı bölmesin diye boşluk normalize edilir). Tarihli
`release`/`changelog` sayfaları KAPSAM DIŞIDIR — oradaki sayı bir snapshot'tır.

İkinci ders: **kapı, sayfanın İDDİA ETTİĞİ şeyi ölçmelidir.** Hiçbir sayfanın
iddia etmediği bir sayıyı ölçen kapı, var olma sebebiyle kırmızı olamaz. İki
ölçüm vakası (2026-09-07 B02): [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

Üçüncü ders, aynı sınıfın düzyazı hâli (2026-09-15): `Tracon.Testing` matrise
dönünce `compatibility.md` güncellendi, `getting-started`'in "*require .NET 10*"
cümlesi kaldı; TFM iddiasının **hiçbir** kapısı yoktu. Kapı artık paket tablosunu
`csproj`'lara iki yönde bağlar (varsayılan `src/Directory.Build.props`; sapan
proje TEKİL `<TargetFramework>` yazar) ve `compatibility.md` dışında "requires
.NET N" yazılmasını yasaklar — düzyazıya kopyalanan tabloyu hiçbir şey denetleyemez.

## 🚨 `cref` duz metne donerken TIP kisa adiyla yazilir (Faz 163)

`build-api-reference.mjs` iki yoldan `xref` cozer. **Baglanti** yolu
(`resolveReference`) uid bir TIP ise `shortName` kullanir; kendi yorumu sebebini
soyler: uye bicimi namespace'i bildiren tip gibi gosterir (`Tracon.IRunStore`).
**Duz metin** yolu (`plainText` — frontmatter `description` ve ozet tablosu) ayni
kurali uygulamiyordu ve her zaman uye bicimini yaziyordu.

Sonuc yalnız cirkinlik degildi: XML artikeli **kisa ada** gore secer
(`an <see cref="IRunJudge"/>`), uzun ad onu bozar — 60 uretilen sayfada
"an Tracon.IRunJudge". `plainText` artik tip kumesini alir ve ayni kurali
uygular. **Bir kural iki yolda da gecerliyse ikisine de yaz.**
