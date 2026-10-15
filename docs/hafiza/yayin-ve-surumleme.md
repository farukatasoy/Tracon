# Yayin ve Surumleme Tuzaklari

> MinVer surumleme (git yuksekligi, kirli agac denetimi, deterministik olmayan
> `.nupkg` byte'lari), repo disi bir tuketicinin yerel feed'e baglanmasi ve tek
> derleme zinciri. Genel paketleme/pack mekanigi icin:
> [`paketleme-ve-dagitim.md`](paketleme-ve-dagitim.md). Etiket koşumu, push
> sırası ve registry tuzakları: [`yayin-turu.md`](yayin-turu.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 156'da `paketleme-ve-dagitim.md`'den ayrildi: dosya %6 bosluga dusmustu.

## MinVer surumu calisma agacinin durumunu GORMEZ (Faz 136)

- **🚨 MinVer surumu yalniz git YUKSEKLIGINDEN turetir** (`git rev-list --count
  --first-parent <commit>`); calisma agacinin KIRLI olup olmadigi HIC girdi
  degildir. Commit'siz bir degisiklik yapip `dotnet pack` calistirmak AYNI
  surumu (ayni yukseklik) ama FARKLI SHA-256 tasiyan bir artifact uretir - iki
  farkli icerik AYNI `<id, version>` ciftini adlandirir. Olculdu (2026-09-03):
  `src/Directory.Build.props`'a commit'siz bir satir eklemek `.nuspec`'te
  YALNIZ `<projectUrl>`'i degistirdi, `<repository commit="...">` AYNI kaldi.
  Cozum `Directory.Build.targets`'teki `TraconValidateCleanWorkingTree`
  hedefi (`BeforeTargets="GenerateNuspec"`, aynen `TraconValidatePackageReadme`
  gibi - yalniz `pack` yolunda kosar, `build`/`test`'i KIRMAZ): `git status
  --porcelain` bos degilse `TRACON0004` ile durur. **Untracked dosya da
  kirli sayilir** - SDK'nin varsayilan `Compile` glob'u `**/*.cs` oldugu icin
  takip edilmeyen bir `.cs` dosyasi PAKETE GIREBILIR. Override
  (`TraconAllowDirtyPack=true`) surumu OTOMATIK turetmez - `dirty` tasiyan
  ACIK bir `MinVerVersionOverride` ister (`0.0.0-dirty.<ad>`, her zaman temiz
  surumun ALTINDA sıralanır) ve CI'da (`CI=true` veya
  `ContinuousIntegrationBuild=true`) HIC calismaz.
- **🚨 CI'nin is alani ICINE yazdigi her yol `.gitignore`'da olmalidir**
  (2026-09-09): `ci.yml`'in `env` blogu `NUGET_PACKAGES`'i
  `${{ github.workspace }}/.nuget/packages` yapar, yani `restore` repo kokunde
  `.nuget/` uretir. `.nuget/` ignore EDILMIYORDU; kapi untracked dosyayi da
  kirli saydigi icin `pack` ve `yayin provasi` isleri yirmi `TRACON0004` ile
  dustu. Gelistirici makinesinde `NUGET_PACKAGES` repo DISINDA oldugu icin
  hata yerelde HIC gorunmez - Linux'ta, `CI=true` ile, taze klonda bile.
  `WorkflowWorkspacePathsTests` artik `ci.yml`'i okuyup her
  `${{ github.workspace }}/...` yolunu `git check-ignore` ile dogrular.
- **🚨 Kirli GIRDILER hata mesajinda durur** (2026-09-09): `git status` cikti
  onemi `low`'dur, yani loga HIC girmez, ve kapi her paketlenebilir proje icin
  bir kez koşar. Girdiler mesajda olmazsa CI'da yirmi ayni cumle gorunur ve
  hangi dosyanin kirlendigi OGRENILEMEZ. `TraconDirtyEntries` bunu tasir;
  `PackCleanlinessGateTests` marker adinin mesajda gorundugunu kilitler.
- **🚨 Bu kapı, iterasyon için commit isteyen çağıranları da yakalar** (Faz
  136, bağımsız denetim 🔴#1). `kapi.py kapanis`'in kendi pack adımı ve
  `Tracon.Package.Tests`'in `TemplateFixture`/`ReleaseArtifactFixture`'ı
  gerçek `dotnet pack "Tracon.src.slnf"` çalıştırır - bunlar paketleme
  SÖZLEŞMESİNİ (README, icon, K-008) doğrular, bir yayın adayı üretmez, ama
  repo commit'i yalnız kullanıcı isteyince atılır. **Çözüm:**
  `TraconSkipCleanWorkingTreeCheck=true` - kapıyı TAMAMEN atlar, yalnız bu
  üç iç araç noktasının kendi `dotnet pack` çağrısına eklenmiştir (K-661). Yeni
  bir çağıran noktasına eklemek (insanın DOĞRUDAN kullanması dahil) bu kararı
  ihlal eder. `PackCleanlinessGateTests` (aynı test projesinde) gerçek kapıyı
  KASITLI olarak dirtiler - `RepositoryTreeGate` koleksiyonu onu
  `ReleaseArtifactTests`'ten SIRALI tutar, aksi halde paralel çalışan iki
  gerçek `dotnet pack` birbirinin kirlilik durumunu görür.
- **🚨 NuGet'in KENDİSİ `.nupkg`'i iki ayrı `dotnet pack` koşumunda AYNI
  ÜRETMEZ** (Faz 136, ölçüldü: aynı commit, aynı `MinVerVersionOverride`, art
  arda iki koşum → 20/20 paket FARKLI ham SHA-256). `.nupkg`/`.snupkg` bir OPC
  (Open Packaging Conventions) zip'idir; NuGet.Packaging kendi core-properties
  parçasını HER koşumda RASTGELE (`Guid.NewGuid()`) bir dosya adıyla yazar
  (`package/services/metadata/core-properties/<32 hex>.psmdcp`) ve
  `_rels/.rels` o adı taşır - `lib/`, `.nuspec` ve geri kalan HER giriş
  birebir aynı kalsa bile. Ham dosya SHA-256'sını karşılaştırmak "aynı sürümün
  ikinci koşumu" senaryosunu HER ZAMAN sahte bir "farklı artifact" çakışmasına
  çevirirdi - tam da no-op iddiasının tersini. Çözüm `_content_fingerprint`:
  bu iki rastgele-adlı girişi HARİÇ TUTUP geri kalan girişleri (ad + bayt)
  hash'ler; manifest'in yayınlanan `sha256` alanı DEĞİŞMEDİ (hâlâ ham dosya
  hash'i - gerçekte yayınlanana eşleşen budur).
- **`scripts/kapi.py yayin` artık staging dizinine paketler, sonra promote
  eder** (`artifacts/package/staging/run-<rastgele>/`, `.gitignore`'daki
  `artifacts/` altında - bir sonraki koşumun kendi "erken ret" git denetimini
  kirletmez). `_clean_stale_packages`'ın sessiz silmesi KALDIRILDI: aynı
  `<id, sürüm>` çifti `release_dir`'de FARKLI bir içerik parmak iziyle zaten
  varsa hiçbir dosya promote edilmez (`_promote_staged_packages`, hepsi ya da
  hiçbiri), aynı parmak iziyle deterministik no-op'tur.
- **`yayin --kuru` (pack modu) `release/`'e DEĞİL, `artifacts/package/yayin/<sürüm>/`'e
  yazar** (2026-09-24). `release/` geliştirme feed'idir: `dotnet pack`, kapanış
  ve `ReleaseArtifactFixture` oraya yazar. Prova oraya promote ettiğinde her
  eski sürüm `release_extension_samples` "stale" kapısını paketlemeden
  ~10 dk SONRA düşürüyordu. Ayrı dizinde farklı sürüm karışmaz, hiçbir şey
  silinmez. Faz 136 dizinin içinde geçerlidir: aynı commit'in ikinci koşumu
  no-op'tur; aynı sürümün YENİ commit'i reddedilir ve mesaj `rm -rf
  artifacts/package/yayin/<sürüm>` der (MT-PKG-116). `--paket-dizini` (CI)
  modu `release/`'de kalır.

## Repo dışı bir tüketiciyi yerel feed'e bağlama (2026-09-04)

> Masaüstündeki ayrı bir proje entegre olurken kuruldu. Amaç: tüketicinin bir
> sonraki `v*` etiketini ve nuget.org yayınını **beklemeden** her commit'in
> paketini alabilmesi. `samples/NuGet.config` deseninin repo dışına taşınmış
> hâlidir — yeni bir mekanizma değildir.

- **🚨 MinVer her commit'te benzersiz ve monoton bir sürüm üretir; `v*` etiketi
  sürümü DEĞİL, yalnız nuget.org yayınını açar.** Ölçüldü (2026-09-04, temiz
  klon, aynı HEAD `780e45ca`): etiket yok → `0.0.0-preview.0.578`;
  `v1.0.0-preview.1` + 3 commit → `1.0.0-preview.1.3` (yayınlanan
  `preview.1`'in ÜSTÜNDE, `preview.2`'nin ALTINDA); `v1.0.0` + 3 commit →
  `1.1.0-preview.0.3` — `MinVerAutoIncrement=minor` yalnız **stable** etiketten
  sonra devreye girer. Yani döngü hiçbir etiket durumunda kırılmaz ve üretilen
  sürüm yayınlanan hiçbir sürümle çakışmaz. Tüketiciyi hızlandırmak için
  fazladan `v*` etiketi **atılmamalıdır**: nuget.org'a giden sürüm kalıcıdır
  (unlist edilir, silinmez) ve 20 paketin public sürüm listesini kirletir.
- **🚨 Tüketicinin `packageSourceMapping`'i opsiyonel DEĞİLDİR.** `Tracon*`
  yerel feed'e map'lenmezse aile yayınlandığı gün nuget.org'daki sürüm
  **sessizce** kazanır ve tüketici yerel değişikliği görmeyi bırakır — hiçbir
  uyarı çıkmaz.
- **Tüketici EXACT sürüm pin'ler, floating (`*-*`) kullanmaz.** Floating, bu
  dosyada kayıtlı bayat paket sınıfına girer: artımlı pack değişmemiş projeyi
  yeniden üretmez, global cache eskiyi tutar. Yükseklik her commit'te arttığı
  için exact pin'de bu sınıf **yapısal olarak** oluşmaz. `samples/` içindeki
  `*-*` yalnız repo içi kolaylıktır; `release_extension_samples.py` onu yayın
  yolunda zaten reddeder.
- **Feed yolu tüketicinin `NuGet.config`'ine GÖRELİ yazılır** — mutlak yol
  commit edilirse tüketici başka makinede restore edilemez.
- **İterasyon komutu `dotnet pack Tracon.src.slnf -c Release`'tir**; çıktı
  `UseArtifactsOutput` ile doğrudan `artifacts/package/release/`'e düşer.
  `kapi.py yayin` bu döngünün aracı **değildir** (yayın provasıdır). Çözüm
  dosyası seçimi ve kirli ağaç kapısı yukarıda kayıtlıdır.
- **Feed dizini sınırsız büyür** (commit başına 20 paket × 2 dosya); otomatik
  temizlik Faz 136'da bilinçli kaldırıldı. Ara sıra
  `rm -rf artifacts/package/release` çalıştırıp bir kez yeniden paketle.

## Tek derleme zinciri (Faz 191, K-871)

- **Zincir:** build (ubuntu) `Derle` → `kapi.py paketle --cikti artifacts/ci-paket`
  (`--no-build`) → testler → `paket-dogrula` → `nuget-packages` · `release-dryrun`
  `yayin --kuru --paket-dizini` (pack YOK) → `nuget-verified` · `publish`
  `paket-dogrula` → login → push. Biçimi `dokuman-bakim.py` "CI tek derleme
  zinciri" kilitler. Ham SHA-256 doğru ölçüdür: zincir iki kez paketlemez.
- **🚨 `paketle` `Derle`'nin HEMEN ardındadır.** Ölçüldü (2026-09-24, taze klon):
  `ReleaseArtifact*` fikstürü src'yi `1.0.0-preview.1` ile yeniden derler ve
  `Tracon.Core.dll` SHA-256'sı değişir. Testten sonra `--no-build` yanlış damgalı
  DLL'i paketlerdi. Taze klonda `Derle`'ye kadarki her adımdan sonra `git status`
  boştu → `TRACON0004` riski yok.
- **🚨 Pack anı ≠ artifact anı.** Semaphore (`obj/`), taban restore'u ve bayat
  ilk-yayın bayrağı pack'in yanında koşar (`_pack_release`); sonucu manifest'e
  girer (`apiCompat[].validationRan`). Rapor okuma, not eşleşmesi, metaveri,
  K-008 ortak yoldadır (`_finish_release_rehearsal`). `release-dryrun`'da `obj/`
  yoktur: yeni bir pack anı denetimini ortak yola koymak onu sessizce kırar.
- **"Rapor yok" meşrudur, "kayıt yok" değildir.** Farksız pakette SDK rapor yazmaz;
  kayıt `report: null` taşır. Beklenen kimlikler provanın **kendi** checkout'undan
  hesaplanır; eksik kayıt "rapor eksik: <paket>" verir.
- **Yerel `release_dir` eski sürüm taşıyorsa `--paket-dizini` reddeder** (Açık
  Soru 5 = A; dosya silinmez): `rm -rf artifacts/package/release`. Aynı ad + aynı
  içerik + farklı OPC baytı (eski kopya) → "manifest'i girdiden farklı: packages".
- **Yeniden koşum sabit adlı artifact'a çarpmaz** (ölçüldü 2026-09-24, Actions API,
  `farukatasoy/Tracon` koşum 35507188627 deneme 1-3 ve 35524722320 deneme 1-2):
  `nuget-packages` ve `test-results-<os>` her denemede yeniden yüklendi, hepsi
  `success`. `upload-artifact@v4` adı denemeye bağlar; `overwrite: true` gerekmez.
- **Depo imzası:** nuget.org `.signature.p7s` ekler; itilen ile indirilen ham
  SHA-256 farklıdır. Karşılaştırma girdi bazında yapılır (MT-PKG-160, etiket günü).
