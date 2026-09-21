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

## Ilk yayin gunu olculenler (2026-09-20, `1.0.0-preview.1`)

- **🚨 `NuGet/login`'in `user:` alani policy'yi OLUSTURAN hesabin profil adidir,
  policy'nin SAHIBI degil.** Ikisi ayri eksendir: sahiplik (kisi ya da
  organizasyon) yalnizca policy'nin HANGI paketleri yayinlayabilecegini belirler;
  token'i kimin takas ettigini degistirmez. Policy `Tracon` organizasyonunun
  sahipliginde oldugu icin `user: Tracon` dogru gorunuyordu ve `ci.yml`'deki
  yorum bunu savunuyordu — ikisi de yanlisti. nuget.org'un cevabi:
  `Token exchange failed (HTTP 401) ... Make sure you are using the username of
  the policy creator, not the policy owner: No matching trust policy owned by
  user 'Tracon' was found.` Dogru deger kisisel profil adidir (`farukatasoy`).
  **Bu iddia hicbir testle yanlislanamazdi**: yalnizca gercek bir token takasi
  onu olcer ve o takas yalnizca bir `v*` etiketinde olur.
- **🚨 npm granular token'inda "Bypass two-factor authentication (2FA)" kutusu
  ISARETLI olmali.** Scope'lar (`@tracon` + `tracon` org, read/write) dogru olsa
  bile kutu kapaliyken her yazma OTP ister ve CI `npm error code EOTP` ile duser.
  Org duzeyinde 2FA enforcement acikken bu zorunludur. Granular token, klasik
  Automation token'dan daha dar oldugu icin tercih edilir; bypass'i isaretlemek
  o daraltmayi bozmaz, yalniz CI'in OTP soramayacagi gercegini kabul eder.
- **Sira geri donusu olmayani korudu ve isini yapti.** `publish` (NuGet)
  `needs: npm-publish` oldugu icin npm `EOTP`'de dustugunde NuGet **hic
  kosmadi**. Sonraki turda npm gecti, NuGet OIDC'de dustu ve `dotnet nuget push`
  yine hic kosmadi. Iki yarim yayin denemesinin ikisinde de surum numarasi
  yanmadi. `npm-publish` var olan surumu `npm view` ile gorup ATLAR ve is
  KIRILMAZ — bu yuzden etiketi tasiyip yeniden denemek bedelsizdir.
- **Yayindan sonra nuget.org hemen gorunmez.** `dotnet nuget push` basarili
  donse ve GitHub release olussa bile `v3-flatcontainer`, registration ucu ve
  paket sayfasi bir sure `404` verir; ilk kez yayinlanan paket kimlikleri
  dogrulamadan gecer. "Push basarili" ile "paket gorunur" ayri anlardir.

## Ikinci yayin turunda olculenler (2026-09-20, `1.0.0-preview.2`)

- **🚨 Site deploy'u yalnız HTTP 200 ve `Content-Type` ile doğrulanamaz.** Yayın
  sunucusu geçerli bir HTML/Markdown ağacını servis ederken package sayfaları
  eski release'in "not published yet" metnini taşıyabiliyordu; iki kanalın da
  başarılı görünmesi bu drift'i sakladı. `scripts/site-deploy.sh` artık
  `CHANGELOG.md` içindeki en son dated release version'ını `/`,
  `/getting-started/`, `/packages/` ve `/reference/changelog/` gövdelerinde
  arar ve eski unpublished-status metni varsa yayını reddeder. Release sonrası
  site deploy'u yine ayrı ve zorunlu bir adımdır.

- **🚨 npm `latest` dist-tag'i SILINEMEZ.** `npm dist-tag rm <paket> latest`
  registry'den `403 Forbidden - DELETE .../dist-tags/latest` alir. Buna bagli
  ikinci gercek: bir paketin **ILK** yayini `--tag next` verilse bile `latest`i
  o surume baglar — `1.0.0-preview.1` canlida `latest` oldu. "Preview'i
  `latest`ten kaldir" adimi bu yuzden TEK bir turda degil, HER preview turunda
  kirmizi biter. Dogru kural: stable **varsa** preview `latest`e hic dokunmaz;
  stable **yoksa** `latest` en yeni preview'e TASINIR (silinemedigine gore eski
  bir preview'de takili kalmasi daha kotudur). Kalici kapi:
  `dokuman-bakim.py` → `geri_alinamaz_registry_islemi`; ayni kapi
  `npm unpublish` ve `dotnet nuget delete`'i de ci.yml'de yasaklar.
- **Dist-tag adimi kirildiginda paket yayinda KALIR.** `npm publish` gecti,
  yalnizca etiket hizalama adimi dustu; artifact byte'lari degismedi. Fakat
  `github-release` isi `needs: [publish, npm-publish]` oldugu icin ATLANDI —
  paketler canlida, release sayfasi eksik kaldi. Dist-tag/release gibi
  yayin-sonrasi adimlarin duzeltilmesi etiketi yeniden kosturmakla olur; yayin
  adimi var olan surumu `npm view` ile gorup atlar.
