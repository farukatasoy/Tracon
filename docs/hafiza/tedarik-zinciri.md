# Tedarik Zinciri Tuzaklari — Üçüncü Taraf Kod ve Ön Sürüm Üst Akış

> Paketin taşıdığı üçüncü taraf kodu (lisans bildirimi) ve bağlandığı ön sürüm
> üst akış (aralık). Bu dosya `MEMORY.md`'nin alan dosyasıdır; yalnız bu alana
> dokunurken okunur.

## Ön sürüm üst akış tam aralıktır (A-59, K-872, 2026-09-24)

- **🚨 Ön sürüm bağımlılığın açık alt sınırı çalışma anında kırılır, restore'da
  değil.** Ölçüldü (`uyum-probu.cs ileri`): yayınlanmış `Tracon.AspNetCore`
  preview.2 + Hosting `1.22.0-preview` → `Microsoft.Agents.AI.Hosting.AgentSessionStore`
  yok (başka derlemeye forwarder'sız taşındı), `AgentRunMode.DisallowBackground`
  yok. NuGet `version="x"`'i `>= x` okur; yükseltme uyarısız restore olur.
- **Tam aralık CPM'de yazılır:** `Directory.Packages.props` `Version="[x]"`;
  nuspec aynısını taşır. K-858'in `TraconPinSiblingDependencies` hedefi yalnız
  Tracon→Tracon kenarını düzenler, üçüncü tarafa dokunmaz. Beş satır birlikte
  yükselir.
- **Kararlı üçüncü taraf alt sınırda kalır** (MAF `1.22.0` ölçümü 0 eksik).
  `EverySiblingDependencyIsExactAndMatchesOwnVersion` kararlı üçüncü taraf `[`
  aralığını hâlâ reddeder; ön sürümü `EveryPrereleaseThirdPartyDependencyIsExact`
  ve `kapi.py yayin` (`EXACT_RANGE_PATTERN`) zorlar.
- **`dokuman-bakim.py` sürüm damgası pini okurken `[x]`'i `x`'e indirir.**
  İndirmeden önce `[x]` pini, `x` diyen üç kod yorumunu "sapmış" gösterdi.

## Geçişli açık, bağımlılığı yükseltmekle kapanmaz (F-524, 2026-10-14)

- **🚨 `Microsoft.Data.SqlClient`'ı yükseltmek açık uyarılı `System.Security.Cryptography.Pkcs`'ı
  kapatmadı.** Ölçüldü: 7.0.2'den 7.1.1'e her yayın net8.0'da Pkcs 8.0.1, net9.0/net10.0'da
  9.0.13 (7.1.x'te 9.0.18) çeker; üçü de GHSA-846f-f764-76mx / GHSA-2fmv-rv2c-59x7 kapsamındadır.
  Yama: 8.0.2 · 9.0.21 · 10.0.13. Önce NuGet'in `*-flatcontainer`ında hangi sürümün yamalı
  olduğuna bak (`api.github.com/advisories/<GHSA>` `first_patched_version`), sonra yükseltmenin
  işe yarayıp yaramadığını ilgili sürümün `.nuspec`'inden ölç.
- **Yama `Tracon.SqlServer`'da hedef çatıya göre eşleşen sürümdür** (`Directory.Packages.props`,
  koşullu `PackageVersion`). Tek `10.0.13` net8.0/net9.0 tüketicisine `Microsoft.Bcl.Cryptography`
  ve `System.Formats.Asn1` getirirdi; eşleşen sürüm paket getirmez. Doğrudan bağımlılık
  tüketicinin grafiğinde SqlClient'in sürümünün önüne geçer (`dotnet nuget why`).
- **Önbellekli restore açığı göstermez; soğuk restore `NU1902`/`NU1903`'ü HATA yapar.**
  Ölçüldü: yamasız `Tracon.SqlServer`'da `dotnet build` sıfır uyarı verdi (restore no-op,
  denetim yeniden koşmadı); `dotnet restore --force` aynı projede `Warning As Error` ile düştü
  (`TreatWarningsAsErrors`). Yani yeni bir advisory yayımlanınca temiz bir CI checkout'u
  yerel makineden önce kırılır. Şablon testi
  (`TemplateInstantiationTests.Most_full_combination_compiles_with_zero_warnings`) da boş bir
  dizinde restore ettiği için aynı yeri görür. `kapi.py kapanis` bu yüzden derlemeden önce
  `dotnet restore Tracon.slnx --force` koşar (~5 sn; F-525). Elle tarama için ikinci yol:
  `dotnet list Tracon.slnx package --vulnerable --include-transitive` (~40 sn, çıkış kodu
  vermez; çıktıyı oku).

## Paketin içindeki üçüncü taraf kodu bildirim taşır (BL-058, 2026-09-24)

- **🚨 Vite lisans yorumlarını atar; `Tracon.UI` kodu bildirimsiz dağıtıyordu.**
  Ölçüldü: 748 KB bundle'da `@license`/`Copyright` 0. Bildirim artık
  `src/Tracon.UI/frontend/scripts/third-party-notices.mjs`'ten üretilir ve
  `src/Tracon.UI/THIRD-PARTY-NOTICES.txt` olarak commit edilir.
- **Kaynak `package-lock` değil, modül grafiğidir.** Eklenti (`thirdPartyModules`)
  yalnız `renderedLength > 0` modüllerin paketini yazar. Ölçüldü: `openapi-fetch`
  frontend'in lock'unda yok (`@tracon/client` `file:` bağının kendi
  `node_modules`'ündedir) ama bundle'dadır.
- **🚨 `tailwindcss` grafikte görünmez.** `@tailwindcss/vite` preflight ve
  temayı CSS'e gömer; modül yoktur. `vite.config.ts` onu `css:` seçeneğiyle
  adlandırır; CSS üreten derlemede listeye girer.
- **Bağımlılık değişince derleme düşer:** `postbuild-embed.mjs` son adımda
  commit edilen dosyayı üretilenle karşılaştırır. Düzeltme (frontend
  dizininde): `node scripts/third-party-notices.mjs --write` ve commit.
  wwwroot'a commit edilen **baytlar** kopyalanır; paket kökü, DLL kaynağı ve
  repo dosyası aynı baytlardır (`ReleaseArtifactTests.UiPackageCarriesThirdPartyNotices`,
  `kapi.py` `THIRD_PARTY_NOTICE_PACKAGES`).
- Adımlar `scripts/` ve `vite*.config.ts`'tedir; `package.json` değişmez.
  Değişseydi ekran görüntüsü damgası (`.ui-source.sha256`) bayatlardı.
- **🚨 Sınıf açık: `Tracon.Cli`.** `dotnet tool` paketi yayın çıktısının
  tamamını taşır: 54 üçüncü taraf ikili (Npgsql, SQLitePCLRaw, Google.Protobuf,
  Microsoft.Data.SqlClient …), bildirim dosyası yok (A-72). Kapanınca
  `THIRD_PARTY_NOTICE_PACKAGES`'e eklenir.

## Çalışma anı yükseltmesi ve CVE sabitleri (Faz 214, 2026-10-16)

- **🚨 Eski bir CVE sabiti, yükseltmenin kendisini kırar.** Data.Sqlite 10.0.13 `SQLitePCLRaw >= 2.1.13`,
  AspNetCore.OpenApi 10.0.13 `Microsoft.OpenApi [2.12.2, 3.0.0)` ister; 2.1.12 ve 2.11.0 sabitleri `NU1605`
  verir. Sabitin açığı artık yok, bu yüzden sabit **kalkar**, yükseltilmez (K-197). Üstünde işlem yapmadan önce
  sabitin ihtiyacını `dotnet list package --vulnerable --include-transitive` ve boş bir projede `NuGetAudit` ile ölç.
- **Sabiti silmek tüketen `csproj`'u da kırar.** Merkezî yönetimde `PackageReference` sürümsüzdür; `Microsoft.OpenApi`
  (FunctionalTests, `samples/Tracon.Api`), `SQLitePCLRaw.bundle_e_sqlite3` (`Tracon.Sqlite`) ve `Microsoft.Bcl.Memory`
  (`Tracon.Core`) referansları sabitle birlikte gitti; `Microsoft.Data.Sqlite` ve `AspNetCore.OpenApi` aynı paketleri
  zaten çeker.
- **`Microsoft.Bcl.Memory` CVE'si grafikten çıktı, sabitlenmedi:** `Microsoft.ML.Tokenizers.Data.O200kBase` 2.0.1 kendi
  `net8.0` grubunu taşır (2.0.0 yalnız `netstandard2.0` taşıyordu ve oradan `Bcl.Memory 9.0.4` geliyordu). Grafik
  tabanı (`transitive-dependencies.txt`) üç ad kaybetti: `Microsoft.Bcl.{AsyncInterfaces,HashCode,Memory}`.
- **`System.Security.Cryptography.Pkcs` ×3 sabiti KALIR** (yeniden ölçüldü, SqlClient 7.1.1): net8.0 8.0.1,
  net9.0/net10.0 9.0.18, `NU1902` + `NU1903`.
- **Bot tek grup açar:** `agent-stack` (MAF + `Microsoft.Extensions.*` + OpenAI + A2A + AspNetCore/Bcl/Data). Zincir
  koparsa her PR `restore`'da kırmızı kalır (A-62).

## npm araç zinciri majörleri (Faz 216, 2026-10-16)

- **🚨 Vite 8 (Rolldown) IIFE'nin `"use strict"`'ini bırakır.** Ölçüldü: `embed.js` `(function(){var e=…` ile
  başladı, 418 test ve E2E yeşildi. `vite.embed.config.ts` `rolldownOptions.output.strict: true` ister; yönerge artık
  IIFE'nin **önüne** yazılır (`"use strict";(function(){`). Kapı `src/embed/embed-build.test.ts`: gerçek yapılandırmayı
  bellek içinde derler. `// @vitest-environment node` ÇALIŞMAZ — `setup.ts` `Element`'e dokunur; test jsdom'da koşar.
- **🚨 `@tracon/client` TypeScript 5.9'da kalır.** `openapi-typescript` 7.13 `typescript ^5` ister ve derleyici API'sini
  çağırır; TypeScript 7'nin `typescript` paketi onu taşımaz (`createKeywordTypeNode` hatası; `overrides` ERESOLVE'yi
  çözmez). `tsgo` takma adlı ikinci derleyici reddedildi: iki paket de `tsc` bin'i taşır, `.bin/tsc` hangisine bağlanır
  belgelenmemiş. Arayüz TypeScript 7'de. `dependabot.yml` istemcide `typescript` major'ını yok sayar.
- **🚨 `mermaid` 11'de kalır.** `astro-mermaid` 2.1.0 eşi `^10 || ^11`: mermaid 12'de `npm ci` ERESOLVE ile düşer;
  `overrides` kurulumu geçirir ama 1,46 MB (449 KB gzip) elkjs parçası ekler ve hiçbir diyagram ELK kullanmaz.
- **🚨 Node alt sınırı 22.22.2.** jsdom 30 `^22.22.2 || ^24.15.0 || >=26`, Vitest 5 `^22.12 || ^24 || >=26`, Astro 7.4
  `>=22.22`. Node 20.19.4'te `npm ci` geçer ama Vitest 36 kez `Failed to start forks worker` verir (undici,
  `Cannot read properties of undefined (reading 'bind')`) ve Node'dan söz eden tek cümle çıkmaz.
  `src/Tracon.UI/frontend/.npmrc` `engine-strict=true` taşır: `npm ci` `EBADENGINE` ile `node_modules`'e dokunmadan
  durur. `TraconDetectNode` yalnız `npm --version` çalışıyor mu bakar; sürüm denetlemez.
- **`npm audit fix` yalnız aralık içi kilit değişimidir.** İstemci ve arayüz 0 açıkta; `docs-site`'ta 12 uyarı
  (`katex` mermaid'den, `postcss-selector-parser` expressive-code'dan) kalır ve çözümü `--force` ile starlight'ı geriye
  almaktır. Yükseltme (mermaid 12) bile ikisini kapatmaz; üst akış sürümünü bekle.

## Native AOT vaadi ve sağlayıcı smoke'u (Faz 217, 2026-10-17)

- **🚨 "AOT: Yes" bir ölçüm sonucudur, bayrak değil** (K-1019). `TraconAotCompatible` paketin KENDİ IL'ini analiz eder; SDK'sı yansımalı JSON kullanan paket bayrakla temiz görünür ve tüketicide çöker (`Tracon.Anthropic`: ilk istekte `Reflection-based serialization has been disabled`; `Tracon.Google`: ~100 uyarı). Vaat `samples/Tracon.Samples.ProviderAotSmoke` + `compatibility.md` tablosunun `release_extension_samples.py` kapısıyla verilir.
- **Smoke'a paket eklemek** = csproj'a `PackageReference` + `Program.cs`'e gerçek istek/yanıt çağrısı + `FakeProviderServer`'a yanıt. `TrimmerSingleWarn=false` kalsın: uyarı üyeyi adıyla göstersin. Negatif kontrol: kırılgan sağlayıcıyı geçici ekle, yayın hata vermeli.
- **Örnekler hiçbir çözümde olmadığı için hızlı döngü yok.** Yerelde ölçmek için geçici bir klasörde aynı kaynakları `ProjectReference` ile derle (silmeyi unutma); gerçek kanıt `kapi.py yayin --kuru`dur ve **temiz ağaç** ister. Commit atmadan koşmak için: `git worktree add --detach <dizin> HEAD`, değişen dosyaları `rsync -R` ile kopyala, orada commit'le, provayı orada koş, `git worktree remove --force <dizin>`.
- **🚨 Smoke artık OpenAI sesini de koşar (Faz 220)** ve bunu ancak `AudioClient`'SIZ başardı: OpenAI 2.14.0 System.ClientModel 1.15.0'a karşı derlendi, 1.16.0 (Azure.Core 1.63.0 ister) `AsyncStreamingClientResult<T>`'yi yeniden adlandırdı; `AudioClient`/`ImageClient` kuran host `ilc` ile düşer (K-1026). Görsel de aynı sınıftaydı (F-584, 2026-10-19 kapandı): OpenAI ve Azure görsel üreticisi artık `ImageClient`'sız, ortak kaynak `src/Tracon.OpenAI.Shared/OpenAIPipelineImageGenerator.cs`; smoke iki sağlayıcının görselini de çağırır. Hızlı AOT ölçümü: repo içinde (CPM'in uygulanması için) geçici `samples/<Ad>Tmp/` + `ProjectReference` + `dotnet publish -r osx-arm64`; alt çizgisiz proje adı (CA1707), işi bitince sil.
- **`CreateChatClient(binding)` HAM istemcidir:** `MaxOutputTokens`/`ReasoningEffort` gibi alanları agent derleyicisi `ChatOptions`'a koyar. Elle prob yazarken `ChatOptions.Reasoning`'i kendin ver; yoksa ayar hiç gitmez ve "çalışıyor" görünür.
- **macOS python.org kurulumunda `urllib` CA bulamaz** (`CERTIFICATE_VERIFY_FAILED`): sertifika doğrulamasını kapatma; `curl` ile indirip betiğe `--dosya` ver (`scripts/elevenlabs-kayma.py`).
- **`dokuman-bakim.py` "bayat kod yolu" `git ls-files` okur:** henüz commit edilmemiş yeni dosyaya hafızadan yol verirsen kırmızı olur; `git add -N <dosya>` yeter.

