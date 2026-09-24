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
