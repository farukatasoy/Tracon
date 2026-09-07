# Yayin ve Surumleme Tuzaklari

> MinVer surumleme (git yuksekligi, kirli agac denetimi, deterministik olmayan
> `.nupkg` byte'lari) ve repo disi bir tuketicinin yerel feed'e baglanmasi.
> Genel paketleme/pack mekanigi icin:
> [`paketleme-ve-dagitim.md`](paketleme-ve-dagitim.md).
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
  Cozum `Directory.Build.targets`'teki `AgentPrismValidateCleanWorkingTree`
  hedefi (`BeforeTargets="GenerateNuspec"`, aynen `AgentPrismValidatePackageReadme`
  gibi - yalniz `pack` yolunda kosar, `build`/`test`'i KIRMAZ): `git status
  --porcelain` bos degilse `AGENTPRISM0004` ile durur. **Untracked dosya da
  kirli sayilir** - SDK'nin varsayilan `Compile` glob'u `**/*.cs` oldugu icin
  takip edilmeyen bir `.cs` dosyasi PAKETE GIREBILIR. Override
  (`AgentPrismAllowDirtyPack=true`) surumu OTOMATIK turetmez - `dirty` tasiyan
  ACIK bir `MinVerVersionOverride` ister (`0.0.0-dirty.<ad>`, her zaman temiz
  surumun ALTINDA sıralanır) ve CI'da (`CI=true` veya
  `ContinuousIntegrationBuild=true`) HIC calismaz.
- **🚨 Bu kapı, iterasyon için commit isteyen çağıranları da yakalar** (Faz
  136, bağımsız denetim 🔴#1). `kapi.py kapanis`'in kendi pack adımı ve
  `AgentPrism.Package.Tests`'in `TemplateFixture`/`ReleaseArtifactFixture`'ı
  gerçek `dotnet pack "AgentPrism.src.slnf"` çalıştırır - bunlar paketleme
  SÖZLEŞMESİNİ (README, icon, K-008) doğrular, bir yayın adayı üretmez, ama
  repo commit'i yalnız kullanıcı isteyince atılır. **Çözüm:**
  `AgentPrismSkipCleanWorkingTreeCheck=true` - kapıyı TAMAMEN atlar, yalnız bu
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
  hiçbiri), aynı parmak iziyle deterministik no-op'tur. Sonuç: tekrarlanan
  yerel `--surum` koşumları artık `release_dir`'i ESKİ sürümlerden OTOMATİK
  temizlemez - bu bilinçlidir (silme davranışı kaldırıldı, eklenmedi); gerekiyorsa elle
  `rm -rf artifacts/package/release`.

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
- **🚨 Tüketicinin `packageSourceMapping`'i opsiyonel DEĞİLDİR.** `AgentPrism*`
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
- **İterasyon komutu `dotnet pack AgentPrism.src.slnf -c Release`'tir**; çıktı
  `UseArtifactsOutput` ile doğrudan `artifacts/package/release/`'e düşer.
  `kapi.py yayin` bu döngünün aracı **değildir** (yayın provasıdır). Çözüm
  dosyası seçimi ve kirli ağaç kapısı yukarıda kayıtlıdır.
- **Feed dizini sınırsız büyür** (commit başına 20 paket × 2 dosya); otomatik
  temizlik Faz 136'da bilinçli kaldırıldı. Ara sıra
  `rm -rf artifacts/package/release` çalıştırıp bir kez yeniden paketle.
