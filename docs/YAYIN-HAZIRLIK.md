# Tracon Yayın Hazırlığı

> `nuget-danismani`'nin yayın defteri. Yalnız **bugün geçerli** olanı taşır:
> canlı durum, freeze yolu, açık kalemler ve yürürlükteki kararlar. Kapanmış
> kayıtlar burada durmaz; tam metinleri git'tedir (§7). İş kalemi buraya değil,
> [`ADAYLAR.md`](ADAYLAR.md)'ye yazılır.
>
> **Son güncelleme:** 2026-10-20 (UR-002 ve UR-005 uydu paket için genişledi: K-1036, K-1037; ölçümler satırında tarihli)  
> **Çalışma modu:** `nuget-danismani` — public API freeze programı (UR-009 B)  
> **GÜNCEL DURUM:** Canlı sürüm `1.0.0-preview.4`. Freeze yolunun ilk üç
> ölçütü bitti: küçültme (Faz 212–213), bağımlılık yükseltmesi (Faz 214–216)
> ve modernizasyon (Faz 217). **UR-012 = B (2026-10-19): ölçüt en iyi RC'dir;
> `rc.1`'e kadar kırıcılık ölçüt değildir, RC'yi iyileştiren ekleyici yetenek de
> girer.** Ölçüt 5'ten F-290 Faz 218'de kapandı (2026-10-17: son `[Obsolete]` üye ve sütun kalktı); sıradaki iş F-559 — Faz 237 (A-76) ve Faz 238 (A-67, A-68) olarak planlandı (2026-10-23); F-560 MAF GA'sını bekler (UR-013). Faz 212–218 `origin/main`'de yok; CI onları hiç koşmadı. GA
> hâlâ ❌ (`NU5104`, ER-001). UR-004 = A (2026-10-17): `rc.1` de ER-001 kalkana kadar çıkmaz.

## 1. Canlı durum (ölçüldü 2026-10-16)

| Ne | Değer | Kaynak |
|---|---|---|
| nuget.org | 20/20 paket `1.0.0-preview.4` | registration API |
| npm | `@tracon/client`: `latest` = `next` = `1.0.0-preview.4` | `npm view @tracon/client dist-tags` |
| Deprecation — nuget.org | preview.1, .2, .3: 20/20 pakette ✅ | registration API, `deprecation` alanı |
| Deprecation — npm | preview.1 ve .2 ✅ (mesaj preview.3'ü gösterir); **preview.3 deprecate edilmedi** | `npm view @tracon/client@<sürüm> deprecated` |
| CI'ın gördüğü | `origin/main` 2026-10-15 tarihli; yerelde 51 commit (Faz 212–216) CI'da koşmadı | `git rev-list --count origin/main..HEAD` |
| Bekleyen sürüm notu | `CHANGELOG.md` `[Unreleased]` dolu (Faz 212–216 daraltmaları ve yükseltmeleri) | `CHANGELOG.md` |
| Upstream | MAF `1.25.0` + MEAI `10.11.0` pinli (Faz 214). Hosting ailesi ve `A2A.AspNetCore` için kararlı sürüm **yok** | nuget.org flat container |
| Public yüzey | `PublicAPI.Shipped.txt` boş — yüzey donmadı | K-603 |
| Dış geri bildirim | İnsan kaynaklı issue 0, yıldız 0 | GitHub API |
| Zafiyet bildirimi | Private vulnerability reporting `enabled: false`, advisory yok (A-61) | anonim GitHub API |
| Bakımcı SDK'sı | 10.0.402 global kurulu; `global.json` aynı sürüm (A-73 kapandı) | `dotnet --list-sdks` |

## 2. Hedef ve freeze yolu

**Hedef:** yüzey `1.0.0-rc.1`'de donar; `Shipped` dolumu tek commit'tir
(K-603). **Upstream GA beklenir (UR-004 A):** `rc.1` de `Tracon.AspNetCore`'un
ön sürüm bağımlılıkları (Hosting ailesi, `A2A.AspNetCore`) kararlı olunca
çıkar (ER-001, K-008, K-602). O güne kadar preview hattı sürer. GA (`1.0.0`)
`rc.1`'den sonra ayrı bir adımdır.

Giriş ölçütleri, bakımcı sırasıyla (UR-009 B, 2026-10-16 👤):

| # | Ölçüt | Durum | İş |
|---|---|---|---|
| 1 | Küçültme | ✅ | Faz 212 (A-74) · Faz 213 (A-75, A-79) · A-78 · A-80 |
| 2 | Her bağımlılık en yeni sürümde | ✅ | Faz 214 (çalışma anı, MAF 1.25) · Faz 215 (test/derleme araçları) · Faz 216 (npm). Gerekçeli istisna: `Microsoft.OpenApi` 3 (ASP.NET Core 11 ister), `SQLitePCLRaw` 3 (`Data.Sqlite` 11'i izler), Roslyn (F-569, Faz 193'ten sonra) |
| 3 | Modernizasyon | ✅ **Faz 217** (K-1019, K-1020, K-1021) | Azure → düz `OpenAI` + `/openai/v1/`; AOT vaadi = smoke + sıfır uyarı (`Anthropic` ve `Google` vaadi geri çekildi); ElevenLabs beyanı + kayma testi; Anthropic `ReasoningEffort` uyarlanabilir (canlı ölçüm). Canlı Azure kaynağı ile doğrulama `MT-PROV-091` 👤 |
| 4 | Yeni yetenekler toplu girer | ⏳ UR-012 = B 👤 (2026-10-19) | Ölçüt en iyi RC'dir: RC'yi iyileştiren iş, kırıcı ya da ekleyici, freeze'den **önce** girer. Kaynak MAF kapsama matrisinin dalgalarıdır; Dalga 1 ölçüldü (2026-10-19, kırıcı kalem kalmadı). F-290 ✅ Faz 218. RC ile GA arasında yetenek eklenmez. Önceki karar (A, 2026-10-17) UR-012 satırındadır |
| 5 | Kırıcı kalemler kapandı · ailenin public imzasında upstream deneysel tip yok (uydu hattı hariç, K-1036) · 1.x evrim kuralları yazılı ve kapılı | ⏳ | F-290 ✅ **Faz 218** (2026-10-17; `[Obsolete]` kalmadı, `git grep "\[Obsolete" src` boş) · F-559 📋 **Faz 237** (A-76) + **Faz 238** (A-67, A-68) · **F-560 upstream'e bağlı** (UR-013): `LoopEvaluator` ve `AgentFileStore` MAF'ta `[Experimental]`; GA'larını bekler. F-466 ✅ (ölçüm) |
| 6 | Faz 193 (`net8.0`/`net9.0` düşer) | 📋 planlandı | En erken 2026-11-10 (K-855) |
| 7 | Bir preview aralığı kararlı katmanda **sıfır** kırılmayla geçer | ⏳ | **Yalnız son adımda sayılır** 👤 (2026-10-19, UR-012 B): 4–6 bittikten sonra, `rc.1`'den hemen önceki son preview sessiz geçer; `kapi.py yayin` kırılma listesi boş olur. Pencere o güne kadar sayılmaz; o aralıkta giren kırıcı değişiklik ya da yeni yetenek pencereyi yeniden başlatır |
| 8 | Upstream kararlı (ER-001 kalktı) · `1.0.0-rc.1` provası yeşil | ⏳ dış | UR-004 A: Hosting ailesi ve `A2A.AspNetCore` kararlı sürüm alır. Sonra `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-rc.1` |

## 3. Açık kalemler

### 3.1 GA blocker'ları

| Kimlik | Kalem | Seviye | Sahip |
|---|---|---|---|
| ER-001 | Hosting ailesi ve `A2A.AspNetCore` kararlı değil; kararlı pack `NU5104` ile durur. UR-004 A ile `rc.1` de bunu bekler | 🔴 rc.1 + GA · dış | upstream |
| BL-057 | nuget.org `Tracon` organizasyonunda ikinci üye yok. Tek bakımcı erişilemezse güvenlik yaması çıkmaz (RK-012) | 🔴 GA | 👤 nuget.org |
| A-72 | `Tracon.Cli` 54 üçüncü taraf ikiliyi bildirim dosyası olmadan taşır (Apache-2.0 ayrıca `NOTICE` ister). Çözüm: `kapi.py` `THIRD_PARTY_NOTICE_PACKAGES`'e ekle; bildirimi NuGet bağımlılıklarının lisans dosyalarından üret | 🟡 preview · 🔴 GA | `kusur-giderme` |

### 3.2 Operasyon — süreli veya 👤

| Kimlik | Kalem | Seviye | Sahip |
|---|---|---|---|
| A-66 | `NPM_TOKEN` süreli: en geç **2026-11-26** biter (KN-021 tarihinden hesap; gerçek süre okunmadı). Bitince `npm-publish` kırmızı olur ve ona bağlı NuGet `publish` da koşmaz. Çözüm: npm trusted publishing (OIDC) | 🟡 tarihli | 👤 npm + `ci.yml` |
| — | Faz 212–217'nin yerel commit'leri CI'da koşmadı. OP-012: CI kör noktasını `main`'i sık itmek kapatır | 🟡 | 👤 `git push` |
| A-61 | Private vulnerability reporting kapalı, GHSA hattı yok. `dotnet restore` (NuGetAudit) GitHub Advisory Database'i okur; nuget.org deprecation'ı restore'da uyarı üretmez | 🟡 GA | 👤 repo ayarı + `SECURITY.md` |
| OP-008 | npm'de `@tracon/client@1.0.0-preview.3` deprecate edilmedi (nuget.org tamam) | 🟢 | 👤 `npm deprecate` |
| — | Anthropic .NET SDK deposuna "Native AOT'ta `IChatClient` adaptörü ilk istekte çöker (`Reflection-based serialization has been disabled`)" issue'su (K-1019 yeniden açılma koşulu; bağlantı karara yazılır) | 🟢 | 👤 |
| `MT-PROV-091` | Azure OpenAI v1 ucu gerçek kaynakta ölçülmedi (anahtar ve Entra, sağlık, görsel, yanlış deployment durumu) | 🟡 | 👤 Azure kaynağı |
| MT-PKG-160 | preview.4 için "nuget.org baytı = `nuget-verified`" karşılaştırması yapılmadı (GitHub `token`'ı ister) | 🟢 | sonraki etikette |

### 3.3 Sevk edilen metin ve kapılar

| Kimlik | Kalem | Seviye | Sahip |
|---|---|---|---|
| A-65 | Repo public (2026-09-19), metin hâlâ "private" der: `src/Directory.Build.props:46,71,80,134` · `docs-site/scripts/build-changelog.mjs:6`. K-659'un yeniden açılma koşulu sağlandı; karar yeniden değerlendirilmedi | 🟡 | `tuketici-dokuman-senkronu` + 👤 K-659 |
| A-69 | `reference/licensing.md` contract paketi için "yalnız `Tracon.Abstractions`'a bağlı" der; nuspec beş paket daha taşır | 🟢 | `tuketici-dokuman-senkronu` |
| A-71 | Sevk edilen sürüm metni (`Footer.astro`, paket README'leri) her kesimde elle değişir; etikete bağlayan kapı yok | 🟢 | `kusur-giderme` |

### 3.4 1.0 sözleşme borcu

Kaynak: 2026-08 seam denetimi (11 küme, 78 seam). Contract sınıflarının
yokluğu 2026-10-16'da yeniden sayıldı; geri kalanı yeniden ölçülmedi.

| Kimlik | Kalan iş |
|---|---|
| BL-007 | `ITenantStore`, `IContentProtector`, `IDataSubjectStore`, `IDataSubjectResolver` için contract yok |
| BL-017 | `IContentGuard`, `IToolAuthorizationHandler` için contract yok |
| BL-022 | `IMcpOAuthCoordinator` (state/kiracı bağlama) hiçbir testte yok |
| BL-023 | `IMcpPromptClient`, `IMcpResourceClient`, `IMcpServerStore` için contract yok; MCP'yi `PackageReference` ile koşan dış sample yok |
| BL-030 | `IRunEventSink`, `IRunAttributionContext`, `IRunCancellationRegistry`, `IRunErrorClassifier`, `IRunPricingResolver`, `ITraconDrainState` için contract yok |
| BL-055 | `JobStoreContract`'ı paketlenmiş tüketici olarak koşan dış sample yok |
| BL-044 | Kısmen: `tracon.job.*` metrikleri var; webhook teslim ve lease çekişmesi sinyali yok |
| BL-008 · BL-019 · BL-050 | Kayıt API'si: altı tekil store, `IToolAuthorizationHandler` ve Küme I için `Add*` yok (Faz 122 bilinçli daralttı; tekil seam `TryAdd*` ile çalışır) |
| BL-039 · BL-048 | Workflow ve ses arayüzleri için contract ve dış sample; workflow arayüzlerinde DI lifetime dokümanı |
| BL-016 | `IContentProtector` fail-open notu; `NullDataSubjectStore` erasure tutarsızlığı; Küme B dış sample; Anthropic/Azure/Google AOT kaydı |
| 🟢 cila | BL-021 · BL-025 · BL-031 · BL-036 · BL-040 · BL-045 · BL-049 — doküman notları (kardinalite, stream sahipliği, ad doğrulaması) |

### 3.5 Durumu bilinmeyen eski GA bulguları

Son ölçüm 2026-09-20; sonra kimse ölçmedi. GA turunda önce yeniden ölçülür.

- **A-16** — `Tracon.Mcp`'nin üç sevk edilen tipi testsiz; `IMcpOAuthCoordinator` bekleyen akışı süreç içinde tutar (çok replikada kırılır).
- **A-17** — `Tracon.Azure` çağrı yolunun kanıtı yok.
- **A-18** — Dış sample kanıtı yalnız `IRunStore`'u kapsar.
- **A-28** — Sesli delegasyonun zaman aşımı barge-in'den ayırt edilemez.
- **A-31** — Kapanış kapısı artımlı durumdan yeniden üretilebilir değil.

### 3.6 GA öncesi hiç ölçülmemiş alanlar

- Runner'lar arası ve üçüncü taraf yeniden üretilebilir derleme.
- Artifact üzerinden kiracı izolasyonu, BYOK fail-closed ve `secret`/exception/log sızıntı probe'ları.
- HTTP, SSE, MCP, A2A ve OpenAI uyumlu transport sözleşmeleri, artifact seviyesinde.
- Yetkilendirme, onay, egress ve content guard sınırları; timeout ile iptal ayrımı; retry ve circuit breaker.
- AOT ve trimming iddiaları (Anthropic ve Google için F-558'de kısmen).
- Her public değişikliğin SemVer sınıflandırması (F-559: Faz 237 + Faz 238).

## 4. Açık ürün kararları 👤

| Kimlik | Soru | Seçenekler | Öneri |
|---|---|---|---|
| UR-007 | Hangi 1.x sürümü ne kadar güvenlik yaması alır? | A: yalnız son minor · B: son minor + yeni major çıkınca önceki major N ay | **B**; N bakımcı kararıdır. `SECURITY.md` ve site aynı tabloyu taşır |

## 5. Yürürlükteki kararlar

| Kimlik | Karar | Yeniden açılma ölçütü |
|---|---|---|
| UR-001 | Yayın türü `preview` | UR-009 ölçütleri sağlanınca **ve** ER-001 kalkınca `rc` (UR-004 A) |
| UR-002 | Kapsam: paketlenen her proje (bugün 20) + `@tracon/client`, tek sürüm hattı (K-602). **Genişletildi 👤 (2026-10-20, K-1037):** ön sürüm upstream'e bağlı **yeni** uydu paket ayrı ön sürüm hattında çıkar ve ailenin `rc.1`/GA'sını bekletmez. İlk uydu paketin fazı hattı kurar: `PackageFamilyAlignment` listesi dışı, `uyum-probu deneysel` sayımı dışı, ayrı yayın provası | Uydu paketin upstream'i GA olunca paket aileye katılır |
| UR-004 | **A** 👤 (2026-10-17): upstream GA beklenir. Freeze (`1.0.0-rc.1`) de ER-001 kalkana kadar çıkmaz — Hosting ailesi ve `A2A.AspNetCore` kararlı sürüm alana kadar preview hattı sürer. B (Hosting'e bağlı yüzeyi ayrı ön sürüm pakete ayırmak) ve C (önce dondur, `rc.N` çık, yalnız GA upstream'i bekler) seçilmedi | Upstream kararlı sürüm takvimi uzar ve preview hattında bekleyen iş birikirse C yeniden tartılır |
| UR-005 | **A:** tek kararlı katman; Tracon'un kendi `[Experimental]` katmanı yok. **Genişletildi 👤 (2026-10-20, K-1036):** kural aileye uygulanır; ayrı hattaki uydu paket upstream deneysel tipi public imzasında taşıyabilir | Uydu paket aileye katılınca kural ona da uygulanır |
| UR-006 | **A:** ön sürüm upstream bağımlılığı tam sürüm `[x]` yazılır (K-872) | Upstream kararlı olunca alt sınıra döner |
| UR-009 | **B:** küçült → yükselt → modernize et → yetenekler → kurallar → RC'de dondur (§2). RC upstream kararlı olunca çıkar (UR-004 A) | — |
| UR-012 | **B** 👤 (2026-10-19): **ölçüt Tracon'un en iyi tasarımla `rc.1`'e gitmesidir.** `rc.1`'e kadar kırıcı değişiklik bir maliyet ya da sıralama ölçütü değildir; daha iyi kalıcı tasarım kırıyorsa o seçilir ve sürüm notunda adıyla anılır (yayın provası zorlar). RC'yi iyileştiren ekleyici yetenek de freeze'den önce girer; kaynak MAF kapsama matrisinin dalgalarıdır ([`MAF-KAPSAMA.md`](MAF-KAPSAMA.md)). Değişmeyen sınırlar: public imzada upstream deneysel tip yok (ölçüt 5, UR-005, UR-013); RC ile GA arasında yetenek eklenmez. A (2026-10-17: freeze'den önce yeni yetenek yok, yalnız kırıcı kalemler; ekleyici yetenek 1.x minor'a) seçilmedi: ER-001 ile `rc.1` tarihsizdir ve seam'ler donmadan şekillenmelidir | `rc.1` çıkınca biter; kırıcılık o andan sonra sürüm kararıdır |
| UR-013 | **F-560: MAF'ın GA'sı beklenir** 👤 (2026-10-17). `AddLoopEvaluator` ×4 ve `AgentFileStoreContract` olduğu gibi kalır; Tracon kendi ölçüt tipini kurmaz, sözleşmeyi `internal`'a çekmez. Freeze (`rc.1`) bu tetiğe bağlıdır | `uyum-probu.cs -- deneysel` public maruziyeti 0 gösterince F-560 kapanır; MAF GA uzarsa freeze'in bu tetiğe bağlılığı yeniden tartılır |
| OP-001 | Paket sahibi nuget.org organizasyonu `Tracon` (K-755) | — |
| OP-005 | NuGet yayını OIDC trusted publishing: `Tracon` sahipliğinde policy, `Tracon*`, push-only; kalıcı API key yok (OP-004) | — |
| OP-006 · OP-007 | Her `v*` etiketi NuGet, npm ve GitHub release basar; not `CHANGELOG.md`'den gelir, gövde en fazla 125 000 karakter (F-553) | — |
| OP-008 | Yalnız ileri sürüm: unlist veya yank yok. Güvenlik veya veri bütünlüğü kusurunda önceki sürüm deprecate edilir ve yeni sürüme yönlendirilir | — |
| OP-009 | Bağımlılık izleme: dependabot (dört ekosistem; çalışma anı yığını tek `agent-stack` grubu) + NuGetAudit `NU1903` restore'u kırar | — |
| OP-010 | npm önce, NuGet en son basar (`publish` `needs: npm-publish`); npm düşerse NuGet hiç basmaz | Trusted publishing npm'e geçince yeniden bakılır (A-66) |
| OP-011 | GitHub Free planı; repo public olduğu için environment koruması (`required_reviewers`) var | Plan veya görünürlük değişirse |
| OP-012 | Yayın tek atomik push'tur; `main` yeşili beklenmez; düşen etiket `main`'de ata olarak bırakılmaz | — |
| RK-008 | Drain servisi kendi başına rezervasyon garantisi vermez; Kestrel draining ve iş bekleme onu tamamlar (🟡 kabul) | Ölçülmüş bir iş kaybı repro'su çıkarsa |
| RK-015 | Kararlı upstream alt sınırda kalır (K-872); `uyum-probu.cs ileri` her upstream sürümünde ve etiketten önce koşar | Bir upstream minor ileri probu kırarsa |

## 6. Yayın günü sırası

1. **Kesim commit'i:** `## [Unreleased]` → `## [1.0.0-preview.N] - <tarih>`;
   sevk edilen sürüm metni (A-71); site changelog ve agent map yeniden üretilir.
2. **Prova:** temiz ağaçta `python3 scripts/kapi.py yayin --kuru --surum <sürüm>` yeşil.
3. 👤 **Push:** `git push --atomic origin main v<sürüm>` (OP-012). Etiket
   koşumu: build ×2 → prova → npm → NuGet → GitHub release. Koşum düşerse:
   kırılgan test → "Re-run failed jobs"; kod kusuru → etiketi sil, düzelt,
   etiketi yeni commit'e at.
4. **Doğrula:** nuget.org 20/20 (flat container), npm dist-tag, release
   sayfası, `MT-PKG-160`.
5. 👤 **Site:** `scripts/site-deploy.sh` etiketten **sonra**.
6. **Güvenlik içerikli sürümse:** OP-008 deprecation — nuget.org arayüzden
   (herkese açık API yok), npm `npm deprecate` ile.
7. **İlk 72 saat:** restore/build sorunu, issue ve advisory izlenir; 48–72
   saatte patch veya sonraki preview kararı verilir.

Ölçülmüş yayın tuzakları: [`hafiza/yayin-turu.md`](hafiza/yayin-turu.md).

## 7. Kimlikler ve kapanmış kayıtlar

Önekler: `BL-` blocker · `RK-` risk · `OP-` operasyon kararı · `KG-` karar
günlüğü · `KN-` kanıt · `UR-` ürün kararı · `ER-` ertelenen iş · `A-` dış
denetim bulgusu (`ADAYLAR.md` ile ortak). **Sıradaki boş numara:** A-84 ·
BL-059 · KN-036 · KG-050 · UR-014 · OP-013 · RK-016 · ER-004.

Kapanmış kayıt bu dosyada tutulmaz. Bir kimlik burada yoksa önce
`grep -rn "<kimlik>" docs/` ile ara; sonra 2026-10-16 sadeleştirmesinden
önceki tam deftere bak:

> git show 96d3929328cc738d5f3de84274348cd27093c7b9:docs/YAYIN-HAZIRLIK.md

O sürüm şunları tam metinle taşır: preview.1–4 yayın ve karar blokları,
KN-001…035, BL-001…058, RK-001…015, OP-001…012 gerekçeleri, KG-040…049,
A-59…A-83 bulgu metinleri, UR-001…011 gerekçeleri ve yayın checklist'i.
KG-001…039:

> git show 55584bb6:docs/arsiv/YAYIN-HAZIRLIK-KARAR-GUNLUGU.md

2026-09-20 öncesi tur anlatısı ve A-1…A-58:

> git show e35b4914:docs/YAYIN-HAZIRLIK.md
