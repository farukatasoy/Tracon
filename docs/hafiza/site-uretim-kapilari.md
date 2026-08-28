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
