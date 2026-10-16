# Test Kosum Tuzaklari — `dotnet test` ve MSBuild

> Paketi KOSARKEN karsilasilan tuzaklar: `dotnet test` davranisi, MSBuild alt
> sureci, komut/filtre/log tuzaklari. Test YAZARKEN karsilasilanlar icin:
> [`test-altyapisi.md`](test-altyapisi.md). Bir test TEK BASINA gecip TAM
> kosumda dustugunde (kusur mu kirilgan test mi ayrimi) icin:
> [`test-yalitimi.md`](test-yalitimi.md). Paralel test, migration fixture'i,
> kaynak cekismesi ve scheduler zamanlamasi icin:
> [`test-paralellik-ve-zamanlama.md`](test-paralellik-ve-zamanlama.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da `test-altyapisi.md`'den ayrildi (K-214 merdiveni: gercek bolunme).

## Alt surec ve MSBuild

- **🚨 Yonlendirilmis bir alt surecte MSBuild DUGUM YENIDEN KULLANIMI `WaitForExitAsync`'i ~15 DAKIKA bloke eder** (2026-08-07, Faz 47): `dotnet test Tracon.slnx` hicbir test kosmadan on dakikalarca asili kaldi. Kok sebep `Tracon.Package.Tests` fikstürüdür (Faz 95'e kadar `Tracon.Templates.Tests` adını taşıyordu): `ProcessRunner` `dotnet pack`/`build`'i `RedirectStandardOutput`/`Error` ile calistirir; `dotnet pack` MSBuild isci dugumlerini `nodeReuse:true` ile baslatir ve o dugumler komut bittikten sonra da yasar (varsayilan ~15 dk). Dugumler ebeveynin yonlendirilmis boru taniticilarini MIRAS ALIR, boru hicbir zaman EOF gormez ve .NET'in `Process.WaitForExitAsync` cagrisi cikis kodunu degil **asenkron okuyucularin bitmesini** de bekledigi icin alt surec saniyeler once cikmis olsa bile bloke kalir. **Belirti**: `ps` ciktisinda tek bir `dotnet pack` sureci yoktur, yalnizca oksuz (`ppid = 1`) `MSBuild.dll … /nodeReuse:true` dugumleri durur; dugumler `pkill` ile oldurulunce fikstür ANINDA devam eder (olculdu). **Cozum**: `ProcessRunner` her alt surece `MSBUILDDISABLENODEREUSE=1` verir. Komut satiri anahtari (`-nodeReuse:false`) yetmez — `dotnet new` gibi MSBuild'i DOLAYLI cagiran komutlar onu tasiyamaz. Olcum: 8 dk+ (asili) → **18,5 sn**. **Kural**: MSBuild cagiran her alt sureci yonlendirirken bu degisken verilir.

## 🚨 `sed -i.bak` + `mv .bak dosya` ESKI mtime'i geri getirir, `dotnet build` DERLEMEZ (Faz 94)

Bir gate'in gercekten bir kusuru YAKALADIGINI dogrulamak icin sahte bir kusur
enjekte edip test kosmak (bu depoda standart pratik) su sirayla YANLIS sonuc
verebilir: `sed -i.bak 's/X/Y/' dosya.cs` → build+test (kusur yakalanir, DOGRU)
→ `mv dosya.cs.bak dosya.cs` (geri al) → build (yesil, ama **YANLIS**: `mv`
hedefin mtime'ini KAYNAK dosyanin (`.bak`, sahte-kusurdan ONCEKI) mtime'iyla
degistirir; bu bazen sahte-kusurlu derlemenin CIKTI dosyasindan daha ESKI
kalir). `dotnet build` artimli derleme icin mtime karsilastirir, "kaynak
DLL'den eski" gorunce **YENIDEN DERLEMEZ** ve bir onceki (SAHTE KUSURLU)
derlemeyi sessizce kullanmaya devam eder — sonraki `dotnet test` calistirmasi
YESIL doner ama gercekte hala BOZUK derlemeyi test etmektedir. Bu depoda
`TRACON_SQL_SNAPSHOT_REFRESH=1` ile checked-in bir taban cizgisi dosyasi
BU SEKILDE bir kez BOZULMUS (sahte terim iceren cikti taban cizgisine
yazilmis) ve fark edilene kadar 4 test sahte SUCCESS/FAILURE dongusu
uretti. **Kural**: bir kaynak dosyayi geri aldiktan (`mv`, `git checkout`,
`cp`) hemen sonra `touch <dosya>` calistir, SONRA derle — ya da direkt
`--no-incremental` kullan. Refresh/generate gibi CIKTI-YAZAN bir komutu
supheli bir derlemeden HEMEN sonra calistirmadan once bu adimi atlama.

## 🚨 `dotnet test --filter` SESSIZCE YUTULUR (Faz 77)

- **`--filter` MTP'de YOKTUR ve hata da vermez — tum paketi kosar.** 2026-08-20'de
  olculdu: `dotnet test tests/Tracon.Core.UnitTests -c Release --no-build
  --filter CapabilityExampleTests` **1004 testin tamamini** kosar ve yesil doner.
  Daralttigini sanirsin; kosum suresi seni yanilmaz cunku paket zaten hizlidir.
  Tehlike yesil bir yanlistir: bir kapiyi "kostum" diye isaretlersin ama aslinda
  hangi testin gectigini bilmezsin.
  **Dogru bicim derlenmis ikiliyi DOGRUDAN cagirmaktir:**
  `./artifacts/bin/<Proje>/release/<Proje> --filter-class "*Ad*" "*Ad2*"`
  (çok hedefli projede `release_<tfm>/` — aşağıdaki Faz 183 bölümü)
  (birden cok desen bosluk ile ayrilir). Secenekler: `--filter-class`,
  `--filter-method`, `--filter-namespace`, `--filter-uid` ve `--filter-not-*`.
  Olcum: 1004 test → **15 test / ~2 sn**.
  🚨 **Bayat komut dokumanlarda duruyor:** `docs/73`, `docs/74` ve `docs/75`
  `dotnet test --filter <Ad>` yazar. Oradan kopyalama; uc dosya da kapanmis
  kayittir ve geriye donuk duzeltilmez.

- **🚨 `dotnet test ... | grep ... | head -N` KOSUMU ERKEN KESER.** `head` N
  satiri alinca boruyu kapatir, `dotnet test` SIGPIPE alir ve kalan test
  projeleri **hic kosmaz**; kabuk yine de `exit 0` doner ve kosum basarili
  GORUNUR. 2026-08-08'de yasandi: 16 projeden yalniz 9'u kostu. Tam paketi
  **dosyaya yaz**, sonra dosyayi filtrele.

- **🚨 `| tail -200` erken KESMEZ ama alfabetik olarak ONCE gelen projelerin
  sonucunu GORUNMEZ kilar** (2026-09-01, Faz 130). `Tracon.slnx`'teki
  projeler alfabetik kosar; `Tracon.Core.UnitTests` ve
  `Tracon.Sql.Shared.UnitTests` `Ui.E2ETests`/`Voice.UnitTests`'ten CIDDI
  ONCE biter. `dotnet test Tracon.slnx ... | tail -200` komple kosumu
  BEKLER (SIGPIPE yok, yukaridaki tuzaktan farkli) ama yalniz SON 200 satiri
  saklar — erken projelerdeki gercek KIRMIZI satirlar sessizce disaridadir,
  koşum "temiz" GORUNUR. Faz 130'da tam bu sekilde iki bagimsiz kusur
  (`PlaywrightLocatorTests`, `SqlTextSnapshotTests` — ikisi de Faz 129'un
  kapanisinda atlanmis bayat taban cizgisi) ilk `tail -200`'lu kosumda
  gorulmedi, ikinci kosumda (tam log DOSYAYA yazilinca) ortaya cikti. **Kural**:
  `kapi.py kapanis` gibi uzun bir kapiyi HER ZAMAN tam log dosyasina yaz
  (`> log.txt 2>&1`), `tail`'i yalniz o dosyayi SONRADAN okurken kullan —
  komutun kendisine asla `| tail` ekleme.

- **🚨 Tüketici testleri GLOBAL NuGet önbelleğine takılır — değişiklik görünmez olur** (2026-08-19, Faz 73): MinVer sürümü git yüksekliğinden türediği için iki commit arasındaki her `dotnet pack` **aynı** sürüm dizesini üretir (`0.0.0-preview.0.271`). NuGet bir sürümü global paket klasörüne BİR KEZ açar ve sonra hep onu kullanır; yeniden paketlenen `.nupkg` hiç açılmaz. Belirti: kodda yaptığın değişiklik `TemplateFixture` tabanlı testlerde **hiç görünmez** ve teşhis yanlış yere gider (Faz 73'te bir analyzer değişikliği üç koşum boyunca yok sanıldı). Çözüm fixture'a girdi: `TemplateFixture.ClearGlobalPackageCache` paketlenen sürümün `~/.nuget/packages/tracon*/<sürüm>` dizinlerini siler. **Depo dışında elle bir tüketici denerken aynı dizini sen de sil.**
- **🚨 Yerel `1.0.0-preview.1` global önbelleği ZEHİRLER** (Faz 185): nuget.org aynı kimliklerde FARKLI bir preview.1 sunar; yereli global klasöre açan restore onu kalıcı yerleştirir. Preview.1 restore eden test/case izole `NUGET_PACKAGES` + `Tracon*`→yerel eşleme ile koşar (`MixedVersionConsumerProject`). Kaynak: `TemplateFixture` sürümü zaman damgasıyla seçiyordu (`TemplateFixtureVersionTests`).

## 🚨 macOS'ta AOT kosumu Xcode'un ESKI linker'iyla CLT'nin YENI SDK'sini birlestirir (Faz 176)

`Tracon.Package.Tests`in NativeAOT case'leri makinede su hatayla duser ve hata
**koda ait degildir**:

```
ld: multiple errors: tapi error: malformed file
/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk/usr/lib/libobjc.A.tbd:4:54:
error: unknown architecture ... arm64e.x1-macos ...
```

Olculdu (2026-09-16): `xcode-select -p` **Xcode.app**'i gosteriyordu ve onun
linker'i `ld-1267` (Haz 2026) idi; `arm64e.x1` mimarisini tanimiyor. Publish ise
SDK'yi `/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk` (-> 27.0) altindan
aliyordu. CLT'nin **kendi** linker'i `ld-27037.1` (Agu 2026) o mimariyi tanir.
Yani eski linker + yeni SDK.

- **Kosum basina cozum (sistemi degistirmez):**
  `DEVELOPER_DIR=/Library/Developer/CommandLineTools` ile kos. Olculdu: 53/53 yesil.
- **Kalici cozum kullanicinindir:** `sudo xcode-select -s /Library/Developer/CommandLineTools`
  veya Xcode'u 27.0 SDK tasiyan surume yukseltmek. Agent bunu **kendisi yapmaz**.
- **CI etkilenmez** — `ci.yml` bu isleri `ubuntu-latest` uzerinde kosar. Bu yuzden
  kusur yalnizca yerel kapanis kapisinda gorunur.

**✅ COZULDU (2026-09-16, Faz 178).** Xcode 27.0 kuruldu, linker 27.0 SDK'yi taniyor.
Yukseltme yeni bir kapi getirdi: `You have not agreed to the Xcode license
agreements.` 🚨 Bu satir bir linker hatasi gibi gorunmez, `dotnet publish`
ciktisinda `MSB3073` altinda kaybolur. Ayirt etme (repo'dan bagimsiz):

```bash
printf 'int main(void){return 0;}\n' > /tmp/t.c && cc /tmp/t.c -o /tmp/t
```

Iki satirlik C programi linklenmiyorsa hata koda ait degildir. `isysroot`
degistirmek YARAMAZ. Cozum kullanicinindir (agent `sudo` kosmaz):
`sudo xcodebuild -license accept`. Sonrasi: `ObjectToolAotPackageTests` 1/1,
`DEVELOPER_DIR` gecici cozumune gerek kalmadi.

🚨 Ayirt etme yolu: ayni testi `git worktree add <dizin> <faz oncesi sha>` ile
temel surumde de kos. Ayni hatayi veriyorsa ortamdir, fazin regresyonu degildir.

## 🚨 AOT publish'i deponun PAYLASILAN `.pdb`'sini acar — tam kosumda cakisir (Faz 176)

`ObjectToolAotPackageTests` ikinci bir sekilde de duser ve sebep linker degil
**dosya cekismesidir**: `ILCompiler.CodeGenerationFailedException` →
`IOException: … artifacts/obj/Tracon.Abstractions/release_net10.0/Tracon.Abstractions.pdb
… being used by another process`. Paketlenen DLL'in debug dizini deponun kendi
`artifacts/obj` yolunu gosterir; tam kosumda baska bir surec ayni `.pdb`'yi
tutuyorsa publish **5-6 sn icinde** duser ve gercek bir AOT kusuru gibi gorunur.

🚨 Hata `IOException`/`being used by another process` iceriyorsa kod yolunu
okuma: case izole kosulunca gecer (olculdu 2026-09-16: tam kapanista dustu, izole
53/53). Ayni kosumda `Tracon.Ui.E2ETests`'ten baska bir testin dusmesi ayni
cekismenin ikinci yuzudur ([`test-yalitimi.md`](test-yalitimi.md) Faz 161).

## 🚨 `dotnet test` MTP 2 kipindedir — eski bayraklar SESSİZCE test koşturmaz (Faz 215)

`global.json` `test.runner = Microsoft.Testing.Platform` ister. Yoksa xunit.v3 4 +
TrxReport 2 ile her `dotnet test` "VSTest target is no longer supported" ile düşer.
Üç eski alışkanlık çalışmaz:

- **`-maxcpucount:N` → `--max-parallel-test-modules N`.** Eski bayrak bu kipte
  **çıkış kodu 5** verir ve **sıfır test** koşar (ölçüldü 2026-10-16: beş modülün
  hepsi "Exit code: 5"). `--` ayıracı yoktur; `--report-trx` doğrudan verilir.
  Hedef `--solution` / `--project` ile verilir; "modül" = bir test ikilisi.
- **TRX ikilinin yanına yazılmaz.** `--results-directory artifacts/test-results
  --results-directory-layout per-module` → `<proje>/<tfm>_<mimari>/*.trx`.
  `kapi.py` ve `ci.yml` aynı yolu okur; `kapi_test.py` ikisini birlikte denetler.
  Eski `release*/TestResults/` yolunu taramak düşen test **bulmaz**: kapı kırmızı
  kalır, izole teşhis sessizce kaybolur. Bacağın ikilisi TRX'in `codeBase`'inden okunur.
- **Çıkış kodu 0 "test koştu" demek değildir.** `kapi.py` tam koşumdan sonra TRX'teki
  testleri sayar; sıfırsa kırmızı döner (çıkış 8). Negatif kontrol `kapi_test.py`'dedir.

## Kapanış kapısı taban ölçümleri

Faz 91 taban/sonrası wall-clock ve proje-başına sonuç tabloları
[`test-kosum-olcumleri.md`](test-kosum-olcumleri.md)'ye taşındı (Faz 101 —
bu dosya bütçeyi aştı). Aktif tuzak değil, tarihsel ölçüm kaydıdır.

- **🚨 `tests/` altındaki test OLMAYAN bir proje `IsTestProject=false` demekle yetinmez** (2026-09-08, Faz 157, ölçüldü). `Tracon.WorkerHarness` yalnız `IsTestProject` koşullandırıldığında `dotnet test Tracon.slnx` onu VSTest'e veriyordu ve TÜM koşum `testhost.dll bulunamadı` ile ABORT oluyordu — tek bir proje yüzünden hiçbir test sonucu alınamaz. Gereken: csproj'da AÇIKÇA `<IsTestProject>false</IsTestProject>` **ve** `<IsTestingPlatformApplication>false</IsTestingPlatformApplication>`.

## 🚨 Bekleme "zaman aşımı" ile düşerse önce verinin HİÇ yazılıp yazılmadığını sor (Faz 163 → F-257)

`LiveVoiceLifecycleTests` transkript testinin "okuma yazmayı geçti" teşhisi yanlıştı: veri yavaş değil KAYIPTI (kapanış pompayı erken iptal ediyordu). Bekleme yardımcısı kayıp veriyi gecikme gibi gösterir. Teşhis sırası ucuzdan pahalıya: tek test izole → tek assembly'nin tamamı → tüm çözüm; ortadaki adım belirleyici oldu (1046 testin geçmesi "sıra bağımlılığı değil, zamanlama" dedi). Tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

**Sahte sunucuda gonderim ile baglanti sokumu yarisir** (2026-10-11, F-257):
`SendClosedAsync` yazarken Tracon kapatiyor, attach handler'i `using` ile soketi
atip yaniti tamamliyordu; suren flush `ObjectDisposedException` aldi (Windows
CI). Tetikleyen cerceve kendi tepkisiyle yarisir: gonderim ve sokum ayni kapiyi
alir; esin kapanmasindan sonraki gonderim hatasi BEKLENEN sonuctur. Yukte
olculdu: CPU stresi + 8 serit, 120 sinif kosumu, 0 dusus.

- **`ActivityListener` SÜREÇ GENELİDİR.** Bir teste listener takıp "durdurulan
  tek span" iddiası kurmak, aynı assembly'deki diğer testlerin aynı kaynakta
  açtığı span'ler yüzünden düşer — ölçüldü: bir yerine **on** span geldi. Span'i
  kendi etiketiyle (ör. script adı) seç ve o adı başka hiçbir test kullanmasın.
  Filtreli koşum bunu göstermez; süreçteki tek listener odur.

## Çoklu TFM test runtime'ları (Faz 183)

Temsilci küme (`tests/Directory.Build.props`, `TraconMultiTargetTest`) üç TFM'de
derlenir ve koşar. Üç sonuç:

- **Çıktı `release/` değil `release_<tfm>/`'dir.** Yol kuran her şey
  `kapi.test_output_directories`'ten geçer; `release/` klasörü proje çok
  hedefli olunca **bayat** kalır ve diskte durur — yol diskten değil beyandan
  çözülür. Test kodunda yapılandırma adı `Name.Split('_')[0]`'dır.
- **🚨 Test apphost'u runtime'ı `DOTNET_ROOT`'tan (önce `DOTNET_ROOT_<ARCH>`)
  çözer, PATH'teki muxer'dan DEĞİL** (ölçüldü). Makinede net8 runtime'ı yoksa bacak "You must install or
  update .NET" ile düşer; `kapi.py` bunu komuttan önce yakalar. **Önce global
  kurulum** — 🚨 `--skip-non-versioned-files` şart:
  [elle-kosum-ortami](elle-kosum-ortami.md#makineye-runtime-ekleme). Kullanıcı düzeyi yol: `dotnet-install.sh --install-dir
  ~/.dotnet` ile SDK 10.0.100 + `--runtime dotnet --channel 8.0` / `9.0`, sonra
  **yalnız** `DOTNET_ROOT=~/.dotnet` — PATH'teki `dotnet` sistemin kalsın.
  🚨 `DOTNET_ROOT` o kökü **tek** kök yapar: fonksiyonel/E2E testleri için
  ASP.NET Core 10 runtime'ı da orada olmalıdır (SDK getirir). 🚨 Özel kökü
  PATH'e de koymak ölçüldü ve 3 `Tracon.Package.Tests` case'ini düşürdü:
  kökte eski bir 8.0 SDK'sı vardı ve `dotnet new sln --format slnx` 127 verdi.
  `kapi.py`'nin izole koşumu da aynı ortamda koştuğu için "izole de düştü"
  dedi — ortamı regresyon sandırır.
- **🚨 `dotnet run` çağıranın `DOTNET_ROOT`'unu EZER** — uygulamaya muxer'ın kendi
  kökünü verir (ölçüldü: yayın provasında net8 tüketicisi `~/.dotnet`'i göremedi).
  Runtime'ı belli bir kökten istenen bir smoke `dotnet build` + apphost ile koşar.
- **Roll-forward kanıtı siler.** `DOTNET_ROLL_FORWARD=LatestMajor` ile net8
  bacağı net8 kurulu olsa bile en yeni runtime'da koşar ve her test geçer
  (`Major` yalnız net8 **yoksa** ileri sarar — ölçüldü). `RuntimeMatchesTargetFrameworkTests` (her
  temsilci projeye bağlanır) bunu düşürür.

Test kodunda net8/net9'da olmayan üç şey derlemeyi kırdı: `System.Threading.Lock`
(koleksiyonun kendisine kilitlen), `System.Linq.AsyncEnumerable` (`await foreach`
yaz) ve Shouldly'nin net8 derlemesindeki
`IReadOnlyDictionary.ShouldNotContainKey` (`ContainsKey(...).ShouldBeFalse()`).
