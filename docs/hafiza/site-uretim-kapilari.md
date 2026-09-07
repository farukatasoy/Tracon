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

- **`build-agent-map.mjs`'i EN SON içerik düzenlemesinden SONRA çalıştır, ilk
  düzenlemeden sonra değil** (2026-09-04, Faz 141): `tuketici-dokuman-senkronu`
  Adım 5'in 2. kapısı (`--check`) yeşil olduktan SONRA `dokuman-bakim.py
  --site-denetle`'nin "arayuz"/"kalicilik" gibi bir kuralı yeni bir sayfa
  düzenlemesi (`ui.md`) daha ister — o düzenleme agent map'i YENİDEN bayatlatır
  ve `--check` bunu bir SONRAKİ koşuma kadar yakalamaz. `kapi.py kapanis`
  kendi `node build-agent-map.mjs --check` adımını koşana kadar fark edilmedi.
  Kural: site sayfalarına dokunan HER düzenleme turundan sonra (yalnız ilk
  turdan sonra değil) `node docs-site/scripts/build-agent-map.mjs --check`
  tekrar koş; `capabilities.md`'ye dokunmasan bile — üretici `docs-site/src/
  content/docs/**/*.md`'nin TAMAMINI (yalnız `capabilities.md`'yi değil)
  `llms-full.txt`'e gömer.
- **`docs-site/src/content/docs/{api,http-api}/` ve `public/openapi/` GITIGNORE'dur.**
  Commit edilmezler; her yayinda uretilirler. `git status` temiz gorunurken
  uretilen icerik bayat olabilir.
- **🚨 `--skip-docfx` BAYAT onbellek okur.** `docfx/api-md` bir onceki kosumdan
  kalir; kaynak degistiyse `--skip-docfx` eski metni uretir ve olcumunu
  yaniltir. Kaynak XML'i degistiren her turda TAM kosum gerekir.
- **`dotnet build` sonrasi kosmayi unutma**: docfx `artifacts/bin/*/release_net10.0/*.xml`
  okur; derlemeden once kosarsan onceki surumun metnini alirsin.

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

**İlgili (Faz 122):** aynı üreteçte `renderRow` bir capability tablosundan
yalnız 2. sütunun (başlığı `registration|enable|surface|definition|choice|
where|output` desenine uyan) ilk iki backtick-kod parçasını alır — 3. sütunu
("Boundary"/"Important behavior") kısaltmak `llms.txt`'in 20480 B bütçesini
DEĞİŞTİRMEZ. Bütçe aşımında gerçek kaynak ya 2. sütun kod parçaları ya da
`guides/*.md`'nin `description`'ı (`renderIndex`, sayfa başına bir satır);
`node docs-site/scripts/build-agent-map.mjs --check`'in verdiği GERÇEK sayıyla
iterasyon yap, sütun metnini gözle kısaltıp tahmin etme.

## 🚨 Agent map package adı path separator'a bağlanamaz

`projectPath.split('/')` macOS/Linux'ta package adını verdi, Windows'ta ise tam
`D:\\a\\...\\AgentPrism.Core.csproj` yolunu verdi. Haritadaki her package satırı
büyüdü; `AgentPrism.AgentMap.md` ve onu içeren `llms.txt` aynı anda hem bayat
hem budget üstünde göründü. Path bileşenini `node:path` `basename()` ile çıkar.
`build-agent-map.test.mjs`, `win32.basename` ile bu sınırı macOS'ta da doğrular;
test `npm run check:content` kapısının parçasıdır.

Aynı sınır `relative()` çıktıları için de geçerlidir. Windows bu çıktıda `\\`,
POSIX sistemler `/` üretir. Sidebar slug'ı, exemption anahtarı veya üretilen kimlik
olacak her relative path önce `/` biçimine çevrilmelidir. `path-utils.mjs` bu
kuralı merkezileştirir; `path-utils.test.mjs` Windows girdisini her platformda
doğrular.

## 🚨 DocFX assembly metadata girdisine `artifacts/bin` referansı ekleme (Faz 98 · onarım 2026-08-26)

`docfx metadata --logLevel verbose` kök nedeni gösterdi. `src`, API üretilecek
18 assembly'yi açıkça seçiyordu. `references` ise `artifacts/bin` altındaki test,
örnek ve paket çıktılarının tüm DLL'lerini yüklüyordu. Bu dizinler aynı
AgentPrism assembly'sinin çok sayıda kopyasını taşır. Roslyn aynı basit adlı
assembly'leri birlikte görünce **360 `CS1704`** üretti. Hatanın çalışma ağacı
tabanında da görülmesinin nedeni birikmiş çıktı ağacıydı.

`references.exclude` kök çözüm değildir. Denemelerde hata sayısı değişmedi;
yalnız çakışma mesajında adı geçen assembly değişti. Explicit `src` assembly'leri
bağımlılıklarını kendi `.deps.json` dosyalarından ve NuGet cache'inden çözer.
Bu nedenle `docfx.json` içindeki `references` girdisi tamamen kaldırıldı. Aynı
birikmiş `artifacts/bin` ağacında metadata üretimi 0 warning ve 0 error ile
bitti; 678 API Markdown dosyası üretildi.

`DocfxConfigurationTests`, assembly metadata girdisine yeniden `references`
eklenmesini yasaklar. Mutation koşumunda yalnız boş bir `references` dizisi
eklemek bile testi düşürdü. Yeni bir proje için bağımlılık çözümleme sorunu
çıkarsa önce explicit `src` girdisini ve assembly'nin `.deps.json` dosyasını
incele; geniş bir artifacts globu ekleme.

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
  `docs/openapi/agentprism.json` (uretilen ama IZLENEN ve COMMIT EDILEN)
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

## 🚨 Sayılabilir iddiayı TEK sayfada denetleyen kapı, kopyalarını kaçırır

2026-09-07 (B02). `check-content.mjs` operasyon sayısını yalnız landing page ile
`http-api.md`'de ölçüyordu. Üç elle yazılan sayfa 143/143/162 operasyon, biri de
19 tag iddia ederken (gerçek: 165 ve 23) kapı YEŞİL kaldı. Bir kapı bir iddianın
TEK ÖRNEĞİNİ değil, SINIFINI denetlemelidir: tarama artık her elle yazılan
sayfayı gezer ve "sayı + en çok beş kelime + isim" kalıbını arar (satır sarması
iddiayı bölmesin diye boşluk normalize edilir).

Tarihli `release`/`changelog` sayfaları KAPSAM DIŞIDIR — oradaki sayı bir
snapshot'tır; bugünkü değerle güncellemek kaydı düzeltmez, BOZAR.

İkinci ders: **kapı, sayfanın İDDİA ETTİĞİ şeyi ölçmelidir.** Ekran sayısı
`from './screens/…'` import MODÜLLERİNİ sayıyordu (28); sayfalar ise kullanıcının
gördüğü Screen COMPONENT'ini söylüyordu (30) — `skills` ve `triggers` ikişer
component export eder. Kapı, hiçbir sayfanın iddia etmediği bir sayıyı ölçtüğü
sürece var olma sebebiyle kırmızı olamaz.
