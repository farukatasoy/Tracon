# Faz 185 — Kardeş Paket Sürüm Sabitleme ve Karışık Graf Koruması

> **Durum:** ✅ Tamamlandı (2026-09-24)
> **Plan onayı:** Bakımcı, 2026-09-23 (engelleyici kararlar sohbette alındı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-265**
> **Önkoşul:** Yok — turun ilk fazı. Varsayım: K-852…K-854 (`bb9953e3`) ve bu turun `kusur-giderme` işleri ana dalda. **Sonraki yayın (K-852…K-854 güvenlik yayını) bu fazı bekler** (kullanıcı kararı). Sonra 186…191 sırayla. 187 `src/Directory.Build.props:86-95`'i düzenler; 191 `kapi.py yayin`'in pack çağrısını değiştirir. İkisi de `TraconPinSiblingDependencies`'i adlandırmaz: hedefi nuspec fact'i korur, devir notu ikisini adlandırır
> **Paketler:** `Tracon.Core` (başlangıç kontrolü) · 16 paketin nuspec'i (`src/Directory.Build.props` hedefi) · `Tracon.Templates` (şablon) · örnek `samples/Tracon.Samples.ExtensionAotSmoke`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor, daralmıyor — kontrol `internal`. `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya × 1 satır; K-603 gereği Shipped boş (2026-09-23). Değişen: nuspec aralığı ve başlangıç davranışı; ikisi de K-* açar (185.9)
> **Tüketici yüzeyi:** site: `reference/versioning.md`, `troubleshooting.md`, `capabilities.md` (bir satır), `packages.md` · sevk edilen: `src/Tracon/README.md`, `src/Tracon.Core/README.md`, `src/Tracon.Templates/README.md` (birer satır), şablon `Tracon.Starter.csproj`, `CHANGELOG.md`. XML `<example>`: Yok (public API yok)
> **Manuel test alanı:** `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` (son `MT-PKG-129`) · `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` (son `MT-TEST-094`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 2ba59274:docs/arsiv/fazlar/185-KARDES-PAKET-SURUM-SABITLEME.md
> ```
>
> Damıtıldı 2026-09-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon tek sürüm hattında çıkar (K-602). Ama `dotnet pack` her `ProjectReference`'ı nuspec'e **alt sınır** yazar (`version="x"` = `>= x`). Tek paketi yükselten tüketicinin karışık grafı **sıfır uyarıyla** restore olur ve çalışma anında `MissingMethodException` veya `MethodAccessException` verir.

## Bitiş Ölçütleri (DoD)

- [x] Faz başında yerel feed kaynaklı `tracon*/1.0.0-preview.1` dizinleri silindi (9 dizin, hepsinin `source`'u yerel feed); kapanışta `ls` boş — ve kaynağı kapandı (Sapma 5)
- [x] Nuspec fact'i hedef yokken grup başına **66 satırla kırmızı**, hedefle yeşil — iki çıktı "Süreç Ölçümü" § Kırmızı → yeşil
- [x] `dotnet pack` sonrası 16 paketin her `Tracon.*` bağımlılığı her TFM grubunda kendi sürümüne eşit tam aralık; `exclude` korunur; Tracon dışı `[` yok — `MT-PKG-130`: 66 / 66 / 0
- [x] Dört şekil önce kırmızı, sonra yeşil (`MixedVersionGraphTests`) — çıktılar "Süreç Ölçümü" § Kırmızı → yeşil
- [x] `PackageFamilyAlignmentHostTests.A_mixed_family_stops_the_host_before_any_hosted_service_starts(false/true)`: `AddTracon()`'dan önce kayıtlı servisin `StartAsync`'i koşmaz, SQLite'ta tablo yok
- [x] `ExtensionAotSmoke` NativeAOT altında gerçek host başlatıp durdurur — elle AOT publish'i çıkış 0; AOT'de görülen aile derlemesi **2** (Sapma 8). `kapi.py yayin --kuru` commit sonrası: son madde
- [x] Hizalı graf regresyonu yok: `Tracon.Package.Tests` 67/67 (`ConsumerRunTests`, `TemplateRunTests`, `ObjectToolAotPackageTests`, `ConstrainedToolPackageTests` dahil); `AspNetCore.FunctionalTests` kapanış kapısında tam koştu
- [x] Package testlerinin süre farkı — "Süreç Ölçümü" § Test süreleri
- [x] 185.7 aracı koştu (11.415 başvuru; beş kırılma, biri planın listesinde yoktu — Sapma 6); her kırılma CHANGELOG yükseltme notunda adıyla. Aracın koştuğu ağaçtan sonra `src/` public imzası değişmedi
- [x] K-858 ve K-859 `*(kategori: public-api)*` ile açıldı; K dışı kararlar "Bu Fazda Verilen Kararlar"da
- [x] Devir notu 185.8 bakımcı eylemini açık iş yazar (20 kimlik × 2 sürüm, ölçüldü); `TraconPinSiblingDependencies` ve nuspec fact'ini adlandırır
- [x] `hafiza/test-kosum-tuzaklari.md` preview.1 önbellek zehirlenmesi 🚨 satırını taşır (kaynağıyla birlikte)
- [x] Dört doğrulama kapısı sıfır uyarı: `python3 scripts/kapi.py kapanis --taban 04532a83` — sonuç "Kapanış Kapısı" bölümünde
- [x] `samples/Tracon.Api` ile gerçek `run` — "Örnek Uygulama Koşumu"
- [x] `secret` taraması boş (`kapi.py tarama`, kapanışın ilk adımı)
- [x] Manuel case'ler `MT-PKG-130…134` ve `MT-TEST-095…097`; 134 👤 dışında hepsi koşuldu
- [x] `faz-denetim` koşuldu; 🔴 yok, 🟡 1 düzeltildi
- [x] `docs-site/` güncel; `npm run check` temiz (içerik · derleme · bağlantı · ağırlık)
- [x] `python3 scripts/kapi.py yayin --kuru` commit sonrası yeşil (`044fbe42`): 20 paket `1.0.0-preview.2.46`, 6 exact-version örnek, NativeAOT host smoke ve net8 tüketici — "Kapanış Kapısı"

### Doğrulama komutları

```bash
# Nuspec aralıkları: manuel case 1
python3 scripts/kapi.py test --proje Tracon.Package.Tests --sinif ReleaseArtifactTests
python3 scripts/kapi.py test --proje Tracon.Core.UnitTests --sinif PackageFamilyAlignmentTests
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif PackageFamilyAlignmentHostTests
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>
python3 scripts/kapi.py yayin --kuru   # AOT host smoke burada
```

---

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

1. **Hedefin yeri ölçüldü: `BeforeTargets="GenerateNuspec"`.** İki taslak da
   çalıştı (`AfterTargets="_GetProjectReferenceVersions"` ve `BeforeTargets`);
   seçilen yalnız **bir** SDK-private ada bağlıdır (öğe `_ProjectReferencesWithVersions`),
   çünkü `GenerateNuspec`'in `DependsOnTargets`'ı `BeforeTargets` kancasından önce
   koşar. Ölçümler (plan 185.2 (1)–(6)): `PackTask` **`[x]`** yazar (`[x, x]`
   değil); `exclude="Build,Analyzers"` korunur; üç TFM grubu değişir; meta `Tracon`
   ve `Tracon.Testing` doğru; `Tracon.Generators` nuspec'e girmez (Core'da yalnız
   `Tracon.Abstractions`); public mekanizma yok (`NuGet.Build.Tasks.Pack.targets`
   aralığı yalnız bu öğeden okur). Hedef içinde metadata, öğe elemanına koşul
   yazılarak güncellenir. 🚨 `StartsWith('(')` koşulu `MSB4109` verdi (MSBuild
   koşul ayrıştırıcısı tırnaklı argümandaki parantezi sayar); idempotens yalnız `[`
   ile korunur.
2. **`ReleaseArtifactFixture` koleksiyon fixture'ı oldu.** Yeni `MixedVersionGraphTests`
   aynı `1.0.0-preview.1` paketlerine ihtiyaç duyar; sınıf fixture'ı olarak kalsaydı
   ikinci kez paketlerdi (plan 185.6 yasak). `RepositoryTreeGate : ICollectionFixture<ReleaseArtifactFixture>`;
   `ReleaseArtifactTests` `IClassFixture`'ı bıraktı. Bedel: `PackCleanlinessGateTests`
   tek başına koşulduğunda da sürüm paketlemesini öder.
3. **Seam DI'dadır, kurucu değil.** Plan "internal kurucu derleme kaynağı ve sürüm
   okuyucu alır" diyordu. Fonksiyonel testin "kontrolü `AddTracon()` kaydediyor"
   iddiası için seam `internal class LoadedPackageFamily` oldu (`TryAddSingleton`):
   test onu `AddTracon()`'dan **önce** kaydeder, kontrolü kendisi eklemez. Okuyucu
   seam'i `PackageFamilyAlignment.ReadLoaded(assemblies, readVersion)` parametresidir
   (birim testleri).
4. **`PackableProjects` `tests/Shared/Infrastructure`'a taşındı.** Ad listesinin
   senkron kapısı (`Library` paketleri − `Tracon.Client`) profil kurallarını
   yeniden yazmadan okur; `Tracon.Package.Tests` ve `Tracon.Core.UnitTests` aynı
   dosyayı link'ler (K-411). Namespace `Tracon.Tests.Common` (zaten global using).
5. **🚨 Fazda bulunan ve düzeltilen kusur — önbellek zehirlenmesinin KAYNAĞI.**
   `TemplateFixture` sürümü "en son yazılan `Tracon.<v>.nupkg`" ile seçiyordu. Artımlı
   pack meta paketi, girdisi değişmediği için (build çıktısı yok, sürüm dosya girdisi
   değil) **yeniden yazmaz**; arka arkaya ikinci koşumda önceki koşumun
   `ReleaseArtifactFixture` paketi (`Tracon.1.0.0-preview.1.nupkg`) en yeni dosyaydı,
   fixture `1.0.0-preview.1`'i seçti ve şablon testleri yerel preview.1'i **global**
   önbelleğe restore etti. Planlamada bulunan 9 zehirli dizin (meta, Abstractions,
   AspNetCore, Core, Mcp, OpenAI, PostgreSql, UI, Workflows) varsayılan şablonun
   bağımlılık kümesiyle **birebir** aynıdır — "kaynağı ölçülmedi" satırının cevabı
   budur. Repro: kod değiştirmeden Package testlerini iki kez koş. Önce düşen test:
   `TemplateFixtureVersionTests` (`The template fixture resolved 1.0.0-preview.1 ...`).
   Düzeltme: sürüm MinVer'e sorulur (`dotnet msbuild src/Tracon/Tracon.csproj
   -t:MinVer -getProperty:PackageVersion`, 0,5 sn). Sınıf taraması: zaman damgasıyla
   sürüm seçen tek yer buydu; `kapi.py _resolve_nupkg` yalnız koşuma özgü taze staging
   dizininde zaman damgasına bakar (belgelenmiş, aynı sınıf değil).
6. **Kapanış ölçümü (185.7) planın listesinde olmayan bir kırılma buldu.**
   Araç repo dışında (scratch) `System.Reflection.Metadata` ile yazıldı: tip ve üye
   başvuruları (generic `TypeSpecification` ebeveynleri açık tanıma indirilerek, iç
   içe tipler dahil) HEAD'de ad **ve** imza ile (`modreq` dahil) arandı; erişim
   public ya da IVT'nin tüketeni adlandırmasıyla; uygulama yönü (arayüz ve soyut
   taban üyeleri) ad ile. Girdi: nuget.org kaynaklı 17 `1.0.0-preview.2` derlemesi
   (`lib/net10.0`) ve HEAD `release_net10.0` derlemeleri; kardeşe giden **11.415**
   başvuru denetlendi. Sonuç (CHANGELOG yükseltme notunda adıyla):

   | Tüketen → Sağlayan | Üye | Neden |
   |---|---|---|
   | AspNetCore → Core | `TenantProviderCredentialResolver.ValidatePrefix(string)` | yok (`MissingMethodException`) |
   | AspNetCore → Core | `InboundTriggerSecretResolver.ValidatePrefix(string)` | yok |
   | AspNetCore → Core | `ConfigurationKeyGuard.RequirePrefix(string,string,string)` | **yeni**: preview.2'de public, HEAD'de `private` (`MethodAccessException`); MCP sunucusu ve webhook kaydında |
   | Mcp → Core | `ConfigurationKeyGuard.RequirePrefix(...)` | **yeni**: aynı; MCP taşıması açılırken |
   | Contracts.Xunit → Abstractions | `JobPayload.ExtractItems`, `WorkflowCheckpointState.IsOmitted` | tip `internal`, IVT yok |

   Uygulama yönünde kırılma yok. Başvurular uç işleyicisinde ya da çalışma anında
   derlenir; hiçbiri kayıt metodunda değil, yani başlangıç kontrolü hepsinden önce koşar.
7. **Şekil 4 kontrolden önce çökmedi.** nuget.org `Tracon.AspNetCore 1.0.0-preview.2`'nin
   `MapTracon`'u HEAD Core'a karşı derlendi; host `StartingAsync`'te durdu (planın
   endişesi gerçekleşmedi, bakımcıya sorulacak durum oluşmadı).
8. **AOT ölçümü:** NativeAOT'de `AppDomain.GetAssemblies()` 81 derleme döndürdü;
   aile derlemesi **2** (`Tracon.Abstractions`, `Tracon.Core`; meta paketin öteki
   kardeşleri kırpıldı), ikisinde de informational version tam
   (`1.0.0-preview.2.43+04532a83…`). Hizalı AOT host çıkış 0 verdi. Sıfır değil —
   bakımcıya sorulacak durum oluşmadı. Ölçüm geçici bir prob satırıyla yapıldı; prob
   geri alındı.
9. **`NU1605` SDK'da varsayılan olarak hatadır** (185.5 "ölçülmeli"): daha düşük
   doğrudan başvuru `error NU1605: Warning As Error: Detected package downgrade`
   verdi (fixture kusurunun koşumunda görüldü).
10. **Restore `WarningsAsErrors`'u NU uyarıları için okur** (şekil 3 ölçer):
    şablon projesi `error NU1608` ile durur.
11. **`troubleshooting.md` girdileri eklenmedi.** Sayfa 58 994 B gzip, tavan
    59 000 B (6 B boşluk). İki başlık 59 850 B, tek başlığa indirilmiş hâli
    59 335 B ölçüldü; kapının kendi yorumu bir sonraki adımın tavanı yükseltmek değil
    sayfayı bölmek olduğunu söyler. İçerik `reference/versioning.md`'dedir
    (diyagram + beş madde); hata mesajı eylemi kendisi söyler. Bölme işi **F-282**.
12. **`capabilities.md` satırı "Observability and operations" tablosundadır** (plan)
    ama tablonun kapanış cümlesi "her sinyal run'ın yan etkisidir" diyordu; satır bir
    başlangıç koruması olduğu için cümleye tek istisna eklendi.
13. **`guides/coding-agents.md` `llms-full.txt` boyutu 700 → 825 KB.** Faz
    eklemesi dosyayı %15 toleransın dışına itti (821 → 825 KB); içerik kapısı
    kızardı, sayı güncellendi.
14. **Ortam:** net8 runtime'ı bu makinede yalnız `~/.dotnet`'tedir; çok hedefli
    testler `DOTNET_ROOT=$HOME/.dotnet` ile koşuldu (hafıza notu zaten vardı).

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

- **K-858** *(public-api)* — Tracon→Tracon nuspec bağımlılıkları tam sürüm, paketin
  kendi sürümüne eşit; kapı `ReleaseArtifactTests.EverySiblingDependencyIsExactAndMatchesOwnVersion`.
- **K-859** *(public-api)* — karışık aile grafında host başlamaz; opt-out yok; silinen
  üyeye shim yazılmaz.

K-* almayanlar:

| Karar | Gerekçe |
|---|---|
| Şablon projesi `NU1608`'i hata sayar (kullanıcı kararı, 185.1-4) | Tüketici dilinde yorum; satır silinebilir |
| Eski preview'lar yayından sonra deprecated (kullanıcı kararı, 185.1-5) | Bakımcı eylemi, 185.8 — agent yapmaz |
| Ad listesi, önek değil (16 ad) | `Tracon.`-önekli test ve tüketici derlemeleri aynı süreçtedir |
| `IHostedLifecycleService.StartingAsync` | Bütün `StartAsync`'lerden önce; fonksiyonel testle ölçüldü |
| İki damgalı feed (yeni pack yok) | K-661 — `TraconSkipCleanWorkingTreeCheck`'e yeni çağıran yok |
| AOT host smoke'un yeri `ExtensionAotSmoke` | `release_extension_samples.py` zaten koşturup çıkış kodunu denetliyor; betik değişmedi |
| Açık Soru 1 → **A** (`AppDomain` taraması, iki ALC'de farklı sürüm reddedilir) | Karar 2 ile uyumlu |
| Açık Soru 2 → **A** (`unknown` fark sayılır, tek başına bile) | Fail-closed; MinVer'li her aile derlemesi sürüm taşır |
| Açık Soru 3 → **A** (`Tracon.Testing`, `Testing.Contracts.Xunit` listede) | Aynı hatta çıkar |
| Açık Soru 4 → **açık** (deprecate'i kalıcı `YAYIN-HAZIRLIK` adımı yapmak) | Öneri A'dır ama "bakımcı onaylarsa" koşuludur; bu faz `YAYIN-HAZIRLIK`'e yazmadı — devir notunda |
| `TemplateFixture` sürümü MinVer'e sorar | Kusur düzeltmesi (Sapma 5) |

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: arşivdeki her
> `- [ ]` satırı `tamamlanmis_faz_isaretsiz_kutular()` kapısında ayrıca hata
> sayılır ve bulgunun kaynağı bulanıklaşır.
>
> `dokuman-bakim.py --denetle` 14. kapısı (`surec_olcumu_bulgulari`) bu tabloyu
> **eşik 167**'den itibaren her kapanmış fazda arar. Boş bir değer hücresi
> kırmızıdır; `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir
> **karar** arar. Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez
> (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan metni değişmedi; sapmalar yukarıda) |
| Düzeltme turu sayısı | 5 — `TemplateFixture` kusuru (Sapma 5) · sevk kapısı: `///` bloğunda alarm işareti · site ağırlık tavanı ve `llms-full.txt` boyut iddiası (Sapma 11, 13) · F-ID sayacı (devir notunda numara anmak onu "kullanılmış" sayar) · denetim 🟡 1 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 — iki doküman kapısı kızarması (ağırlık, boyut iddiası) commit'ten önce kapandı; `ServiceRegistrationSnapshotTests` güncellemesi beklenen değişiklik |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz yeni kapandı) |

**Test süreleri** (`DOTNET_ROOT=~/.dotnet`, aynı makine, 2026-09-24):

| Koşum | Süre | Not |
|---|---|---|
| `ReleaseArtifactTests` + `MixedVersionGraphTests` — kırmızı (hedef ve kontrol yok) | 2 dk 13 sn | iki paketleme dahil; şekiller 13,4 · 1,2 · 4,1 · 2,5 sn (ilki izole önbelleği doldurur) |
| Aynı sınıflar + `TemplateFixtureVersionTests` — yeşil | 1 dk 37 sn | şekiller 2,9 · 2,0 · 2,8 · 0,9 sn |
| `Tracon.Package.Tests` tamamı — yeşil | 1 dk 33 sn (67/67) | Fazın eklediği süre ≈ dört şekil (~10–20 sn); iki fixture paketlemesi fazdan önce de vardı |

İzole `NUGET_PACKAGES` ağa bağlanır (preview.2 ve üçüncü taraf paketler) ama
HTTP önbelleğinden beslenir; ilk şekil 13 sn, sonrakiler 1–3 sn.

### Kırmızı → yeşil kanıtı

- **Nuspec fact'i, hedef yokken:** `66 sibling dependency group(s) are not pinned to
  the package's own version:` ve grup başına 66 satır, örn. `Tracon · net8.0 ·
  Tracon.AspNetCore · 1.0.0-preview.1: expected [1.0.0-preview.1]`. Hedefle: yeşil.
- **Şekil 1, bugün:** `Restored .../MixedLibrary.csproj` (çıkış 0). Hedefle: `error
  NU1107: Version conflict detected for Tracon.Core.`
- **Şekil 2, bugün:** `Build succeeded. 0 Warning(s)`. Hedef + kontrolle: `warning
  NU1608: ... Tracon.AspNetCore 1.0.0-preview.1 requires Tracon.Core (= 1.0.0-preview.1)
  but version Tracon.Core 1.0.0-preview.2.43 was resolved.` ve `Hosting failed to start`
  · `Tracon.AspNetCore 1.0.0-preview.1` · `Tracon.Core 1.0.0-preview.2.43` satırları.
- **Şekil 3, bugün:** `Restored .../Mix.csproj` (çıkış 0). Şablon satırıyla: `error NU1608`.
- **Şekil 4, bugün:** `MIXED-GRAPH-HOST-STARTED` (host başladı, çıkış 0). Kontrolle:
  `Tracon.AspNetCore 1.0.0-preview.2` satırı, host başlamaz.
- **`TemplateFixtureVersionTests`, düzeltmeden önce (ikinci arka arkaya koşum):**
  `The template fixture resolved 1.0.0-preview.1, the version ReleaseArtifactFixture
  forces, not the one its own pack produced.`

## Örnek Uygulama Koşumu

`samples/Tracon.Api` (`dotnet build -c Release`, `--contentRoot` ile DLL, Production;
agent'lar `echo` sağlayıcısında):

- Hizalı: `POST /tracon/api/agents/support/run` `{"message":"Hello from phase 185"}` →
  SSE `event: run` ×1 · `event: update` ×5 · `event: done` ×1; `GET /tracon/api/runs/01a0d0e3-164e-7399-b773-c473ea1cb964` → `Completed`.
- Case 4 (`MT-PKG-133`): `Tracon.AspNetCore.dll` nuget.org `1.0.0-preview.2` kopyasıyla
  değişti → çıkış 134, `Unhandled exception. Tracon.TraconException: Tracon packages from
  more than one release ...`, 14 satır; geri alma sonrası DLL HEAD çıktısıyla aynı (`cmp`).

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

Denetçi: `faz-denetcisi` (salt-okunur alt agent), çalışma ağacı, taban `04532a83`,
2026-09-24. **🔴 yok** (triyaj gerekmedi). 🟢 yok.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | CHANGELOG kontrolün kapsamını koddan geniş anlatıyordu: "only the start-up check stops that graph" (host başlatmayan Contracts.Xunit koşumu kontrolü koşturmaz) ve "loaded Tracon assemblies" (`Tracon.Client` bilerek listede yok). Aynı ifade iki README'de | 🟡 | **Düzeltildi** — CHANGELOG iki madde, `src/Tracon/README.md`, `src/Tracon.Core/README.md`, `reference/versioning.md`, `capabilities.md`: "package assemblies", `Tracon.Client`'ın neden karşılaştırılmadığı ve host'suz sözleşme koşumunun çağrıda düştüğü yazıldı |

Denetçinin temiz saydığı başlıklar: 3.1 (kod/test), 3.2–3.8. Kendi ölçümleri: iki
damgada 66 bağımlılık `[v]`; `BeforeTargets` hedefi artımlı atlamada da koşar;
iki `AddTracon` aşırı yüklemesi `RegisterCoreInfrastructure`'dan geçer; Tracon'da
başka `IHostedLifecycleService` yok; CHANGELOG kırılma listesi `git grep` ile kısmen
yeniden ölçüldü. Uyarısı: yedi yeni dosya izlenmiyordu — commit'e girmezse `main`
derlenmez (K-411 sınıfı); commit bunları içerir.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

**Sıradaki faz:** [186](186-SCRIPT-IZNI-ICERIK-PINI.md) (sözleşme devralmaz;
ortam notları o dokümanın "Bu Faza Başlarken" 4. maddesinde). Sonraki yayın
(K-852…K-854 güvenlik yayını) artık bu fazı beklemiyor.

**🚨 Açık iş — bakımcı eylemi (185.8, agent yapmaz).** Sonraki yayın nuget.org'da
**görünür** olduktan sonra `1.0.0-preview.1` ve `1.0.0-preview.2`'nin her paket
kimliği deprecated işaretlenir. Ölçüldü (2026-09-24, flat-container): iki sürüm de
**20** kimlikle yayınlı (meta, Abstractions, Anthropic, AspNetCore, Azure, Cli,
Client, Core, Google, Mcp, OpenAI, PostgreSql, SqlServer, Sqlite, Templates,
Testing, Testing.Contracts.Xunit, UI, Voice, Workflows) — 40 sürüm. Alternatif: aynı
kimliğin yeni sürümü; neden: sürüm desteklenmez (`reference/security-policy.md`),
bütün paketler birlikte yükseltilir. `dotnet nuget delete` ve unlist bu adım
**değildir**. Doğrulama: `MT-PKG-134`. Açık Soru 4 (bunu kalıcı `YAYIN-HAZIRLIK`
adımı yapmak) bakımcı onayı bekliyor.

**Faz 187 ve 191 için (ikisi de yayın zincirine dokunur):**

- 🚨 `TraconPinSiblingDependencies` `src/Directory.Build.props`'ta
  `TraconSetPackageReleaseNotes`'ın **altındadır** (187'nin dokunduğu
  `EnablePackageValidation` bloğu `:86-95` değişmedi). Hedef her `dotnet pack`'te
  koşar; `--no-build` pack'te de (`GenerateNuspec` yine koşar). Aralığı başka bir
  yoldan (`.nuspec` dosyası, `NuspecFile`) üreten bir pack `_GetProjectReferenceVersions`'ı
  atlar — hedef boş koşar ve aralık açık kalır.
- Kapı `ReleaseArtifactTests.EverySiblingDependencyIsExactAndMatchesOwnVersion`
  `dotnet pack Tracon.src.slnf` zincirini ölçer. 191 `kapi.py yayin`'in pack
  çağrısını değiştirirse yayın zincirinin de aynı hedefi koşturduğunu kanıtlayan
  bir kontrol ekler (ya da nuspec'i yayın çıktısında okur).
- 🚨 `ReleaseArtifactFixture` artık `RepositoryTreeGate` koleksiyonunun fixture'ıdır;
  koleksiyona sınıf ekleyen her test aynı `1.0.0-preview.1` paketlemesini paylaşır.
- 🚨 `TemplateFixture.Version` MinVer'e sorulur; feed'deki dosya zaman damgası
  **kullanılmaz** (Sapma 5, `TemplateFixtureVersionTests`).

**Bilinen tuzaklar:**

- 🚨 Her Tracon host'u başlangıçta aile derlemelerinin sürümünü karşılaştırır.
  Farklı damgayla derlenmiş bir bin klasörü (`ReleaseArtifactFixture`,
  `PackCleanlinessGateTests` src'yi başka `MinVerVersionOverride` ile derler) o
  klasörden **doğrudan** yükleyen bir süreci durdurur; test projeleri kendi
  kopyalarını taşıdığı için bugün etkilenmez. Mesaj sürümleri adlandırır; kontrolü
  gevşetme, kök nedeni düzelt.
- 🚨 `troubleshooting.md` ağırlık tavanında (58 994 / 59 000 B) — yeni semptom girdisi
  için önce sayfa bölünmeli (F-282).
- Host başlatmayan süreç (düz `BuildServiceProvider()`, Contracts.Xunit ile store
  testi) kontrolü koşturmaz; yükseltme notu ve NU1608 kapsar.

## Kapanış Kapısı

`DOTNET_ROOT=~/.dotnet python3 scripts/kapi.py kapanis --taban 04532a83`, commit
öncesi çalışma ağacı (kod bu hâliyle commit edildi; sonraki arşivleme ve damıtma
yalnız dokümandır), 2026-09-24 → **EXIT 0, ~600 sn**:

| Adım | Süre | Sonuç |
|---|---|---|
| `kapi.py tarama` | 4,9 sn | ✅ temiz |
| `dokuman-bakim.py --denetle` · Python testleri · ajan haritası · denetim paketi | ~7 sn | ✅ |
| `dotnet build Tracon.slnx -c Release` | 40,1 sn | ✅ 0 uyarı |
| `dotnet test … -maxcpucount:2 -- --report-trx` | **410,2 sn** | ✅ 17.441 test (38 TRX), 0 kırmızı |
| `dotnet pack` | 6,5 sn | ✅ |
| `dotnet format --verify-no-changes` | 100,7 sn | ✅ |
| `docs-site npm run check` | 32,1 sn | ✅ en ağır sayfa `troubleshooting` 58 994 B |

Performans kapısı tetiklenmedi (sıcak yol değişmedi). İlk kapanış denemesi doküman
denetiminde durdu: Faz 186 devir notunda anılan bir F-ID sayacı "kullanılmış" saydırdı
(düzeltme turu 4).

**Yayın provası** (`kapi.py yayin --kuru`, temiz ağaç `044fbe42`, 2026-09-24) → **EXIT 0**:
20 paket `1.0.0-preview.2.46` · `npm publish --dry-run` ✅ ·
`provider/source/generated-tool AOT host smoke passed` (gerçek generic host,
başlangıç kontrolü AOT altında koştu) · `net8.0 consumer smoke passed on .NET 8.0.31`
· `6 exact-version packed sample`. İlk deneme örnek sözleşmesinde durdu: yerel
feed'de 26 eski damga vardı (`release feed contains stale Tracon packages`); belgelenmiş
tarifle `rm -rf artifacts/package/release` sonrası yeniden koşuldu.

