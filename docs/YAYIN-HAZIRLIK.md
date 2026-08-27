# AgentPrism Yayın Hazırlığı

> Bu dosya, AgentPrism paket ailesinin ilk NuGet.org yayını için yaşayan kontrol
> düzlemidir. Faz planı, sohbet özeti veya genel karar defteri değildir. Yalnız
> ölçülen kanıtı, yayın kararlarını, risk kabulünü ve doğrulama durumunu taşır.
>
> **Son güncelleme:** 2026-08-28  
> **Çalışma modu:** `nuget-danismani` — Yayın kararı  
> **Hedef durumu:** Seam matrisi **11/11 küme tamam**; BL-006 **kapandı**;
> BL-026 ölçümle **🟡'ye indirildi**, belge kısmı **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md) ile kapandı**;
> BL-027/BL-037 sınıf taramasıyla **21 vakaya
> genişledi** ve **[Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md) ile kapandı** (26 vaka
> kapatıldı — sınıf taraması 5 ek vaka daha buldu; `SafeErrorText` + mimari
> cırcır kapısı); BL-041 **[Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) ile
> kapandı** (`IJobHandler` sözleşmesi at-least-once'ı yazıyor, `JobHandlerContract`
> üç yerleşik handler + bir dış sample tarafından koşuyor, `JobLeaseExpiryTests`
> davranışı ölçüyor); **35× 🟡 hattının kulvar 3'ü (XML sözleşme boşlukları)
> [Faz 121](121-SEAM-SOZLESME-DOKUMANI.md) ile kapandı** — BL-024/028/029/035/038/042/043/046
> tam, BL-048/050 kısmen (KG-017); `SeamContractDocumentationTests`'in küçülen
> taban çizgisi 174 satır (78 arayüz × 3 boyut) bilinen borç olarak kalır  
> **Geçici karar:** ❌ Yayınlanmamalı — teknik sebeple değil, **kullanıcı
> kararıyla**. Açık 🔴 yoktur ve UR-003 bu turda 🟡'ye indirildi (GA kapısına
> taşındı, KG-016), yani `preview.1` yolu teknik olarak açıktı. Kullanıcı
> sıradaki iş olarak **35× 🟡 sistemik hattı** seçti; yayın o hat kapanana
> kadar beklemeye alındı. Yayın türü `preview` (UR-001), paket kapsamı tam
> entegrasyon seti (UR-002/BL-002) ve owner modeli kişisel hesap (OP-001)
> sabit — bkz. §4 ve §13.

## 1. Yayın hedefi ve kapsamı

| Alan | Değer |
|---|---|
| Amaç | Repo hakkında bilgisi olmayan üçüncü taraf geliştiricinin AgentPrism'i NuGet üzerinden anlayabilmesi, güvenli kullanabilmesi ve desteklenen seam'lerden genişletebilmesi |
| İlk hedef tüketici | Karar gerekli |
| Yayın kanalı | NuGet.org; aynı `v*` tag'i ile `@agentprism/client` npm yayını da mevcut CI kapsamındadır |
| En küçük güvenli kapsam | İnceleniyor; 20 packable proje bugün tek sürüm hattına giriyor |
| Kapsam dışı | Gerçek `push`, Git tag, GitHub release, NuGet.org sahiplik değişikliği ve credential değişikliği açık kullanıcı onayı olmadan yapılmaz |
| Kanıt standardı | Kaynak ve doküman yalnız varlığı ve vaadi gösterir. “Çalışıyor” kararı `.nupkg`, izole external consumer ve gerekli runtime/AOT koşumundan sonra verilir |

## 2. Hedef sürüm ve gerekçesi

| Alan | Değer |
|---|---|
| Geçici yayın türü | `preview` (UR-001 karar verildi, 2026-08-27) |
| Dry-run sürümü | `1.0.0-preview.1` artifact ölçümü için kullanıldı; kesin sürüm numarası (`1.0.0-preview.1` vb.) kapsam ve seam audit'i bittikten sonra sabitlenir |
| Durum | Yayın türü sabit; kapsam ve public contract audit'i sürüyor |
| Ölçülen ürün gerçekleri | Shipped giriş sayısı `0`, unshipped tip sayısı `676`. `AgentPrism.AspNetCore` pre-release MAF Hosting/A2A bağımlılıkları taşır. Bunlar karar değil, yeni değerlendirmeye giren kanıttır. |
| Önceki yayın kararları | K-602, K-603 ve diğer yayınla ilgili kayıtlar bu turda tarihsel bağlamdır; hedef, sürüm, kapsam veya compatibility politikası için normatif kaynak değildir |
| Yeni hedefin ölçütleri | Kullanıcı kararıyla sabitlenir; sonra Adım 1–8 kanıtıyla test edilir |

## 3. Yayınlanacak paket envanteri

Kaynak ölçümü `src/*/*.csproj` altında `IsPackable=false` olmayan **20** proje
buldu. Artifact kimlik kümesi dry-run sonrasında ayrıca doğrulanacaktır.

| Paket | Profil | Hedef TFM | İlk durum |
|---|---|---|---|
| `AgentPrism` | Meta paket | `net8.0;net9.0;net10.0` dependency group | İnceleniyor |
| `AgentPrism.Abstractions` | Library | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Anthropic` | Provider adapter | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.AspNetCore` | HTTP/transport host | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Azure` | Provider adapter | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Cli` | .NET tool | `net10.0` | İnceleniyor |
| `AgentPrism.Client` | Generated management client | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Core` | Runtime | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Google` | Provider adapter | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Mcp` | MCP client/tool integration | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.OpenAI` | Provider adapter | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.PostgreSql` | Storage provider | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.SqlServer` | Storage provider | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Sqlite` | Storage provider | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Templates` | `dotnet new` content package | `net10.0` build host | İnceleniyor |
| `AgentPrism.Testing` | Test helper library | `net10.0` | İnceleniyor |
| `AgentPrism.Testing.Contracts.Xunit` | Reusable contract suite | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.UI` | Embedded UI | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Voice` | Voice tool library | `net8.0;net9.0;net10.0` | İnceleniyor |
| `AgentPrism.Workflows` | Workflow runtime | `net8.0;net9.0;net10.0` | İnceleniyor |

Not: K-602'nin metni 19 paket der. Güncel kaynak 20 paket gösterir. Yeni paket
eklendiğinde kimlik kümesini dinamik çıkaran kapı bunu kapsar. Karar metnindeki
sayının ürün politikası mı yoksa bayat ölçüm mü olduğu artifact sonrasında
değerlendirilecektir.

## 4. Mevcut net yayın kararı

**❌ Yayınlanmamalı — şu an.** Yayın türü (`preview`, UR-001) ve paket kapsamı
(tam entegrasyon seti, UR-002) kullanıcı tarafından sabitlendi. 22 sütunlu seam
matrisi 11/11 kümede tamamlandı (§15) ve **4 bağımsız 🔴 preview-blocker kusur
sınıfının tamamı kapandı**:

| # | Kusur sınıfı | Kayıtlar | Durum (2026-08-27, `kusur-giderme` sonrası) |
|---|---|---|---|
| 1 | BYOK credential case-sensitivity | BL-006 | ✅ **KAPANDI.** Düşen testle yeniden üretildi → normalizasyon + 3 migration + contract case'leri → yeşil. K-639. Sınıf taraması: 4 aday temiz, `provider` tek outlier |
| 2 | Drain/yeni-run yarışı | BL-026 | ⬇️ **🟡'ye indirildi.** Pencere var ama iş kaybı yok: Kestrel request draining (HTTP) ve `WaitForRunningJobsAsync` (job) boşluğu kapatıyor; drain zaten varsayılan **kapalı**. Ölçülmüş repro üretilemedi |
| 3 | Ham exception → kalıcı/dışa açık durum | BL-027, BL-037 | ✅ **KAPANDI (Faz 119).** Sınıf taraması bilinen 2 vakanın üstüne 19 vaka daha bulmuştu (§16); uygulama sırasında **5 ek vaka** daha bulundu (`EgressAddressValidator`, `ConversationBranchService`, `RetentionJobHandler`, `RetentionExecutor`, `ModelRunJudge`) — toplam **26 vaka** kapatıldı. `SafeErrorText` (K-640) + `RawExceptionTextSiteTests` mimari cırcır kapısı 22. sızıntıyı otomatik yakalar |
| 4 | `IJobHandler` sözleşmesi at-least-once'ı söylemiyor | BL-041 | ✅ **KAPANDI (Faz 120).** `ExecuteAsync` ve `JobContext.Items`'ın XML dokümanı at-least-once'ı, süzülmemiş `Items`'ı ve handler sorumluluğunu açıkça yazıyor. `JobHandlerContract` (üç yerleşik handler + `AgentPrism.Samples.CustomJobHandler` dış sample'ı) kuralı kilitliyor; `JobLeaseExpiryTests` lease süresi dolunca gerçekten yeniden kiralandığını ve item listesinin süzülmeden geri geldiğini ölçüyor |

Bunların **hepsi** `kusur-giderme`'ye devredilmeden (ve kusur sınıfı taraması
tamamlanmadan) preview yayınlanamazdı. **Dördü de kapandı** (BL-006, BL-026
🟡'ye indirildi, BL-027/BL-037 Faz 119, BL-041 Faz 120); açık 🔴 kalmadı.
Geri kalan 35× 🟡 ve 17× 🟢 bulgu **1.0 blocker'ı değil**, ilk preview'ı
engellemez — release notes'a ve sonraki iterasyon planına girer (bkz. §6 tam
liste). Nihai "yayınlanabilir" kararı yine de verilmedi: §7'deki public API
freeze taraması (UR-003) ve §8'deki NuGet.org operasyon kararları (OP-001..009)
açık — bu ikisi bir sonraki `nuget-danismani` turunun konusudur.

Paket artifact'i (dry-run, exact sürüm, 20 paket, 160 sample testi, Native AOT
smoke) teknik olarak yeşildir — bu yalnız **başlangıç** kanıtıdır, seam
matrisinin bulduğu kusur sınıflarını geçersiz kılmaz.

**UR-003 bu turda 🟡'ye indirildi (KG-016).** 680 tiplik public yüzeyin
freeze taraması bir preview blocker'ı **değildir**, GA blocker'ıdır. Üç kanıt
aynı yöne bakıyor: (1) sevk edilen tüketici sözleşmesi bunu zaten yazıyor —
`docs-site/src/content/docs/reference/versioning.md:11-13`, *"a narrowing or a
reshaped type is not treated as a breaking change until the family reaches
`1.0.0`"*; (2) **K-603** (kullanıcı kararı) `Shipped.txt`'i preview hattı
boyunca boş tutar ve dolumu GA'ya erteler; (3) **K-602** (kullanıcı kararı) tek
sürüm hattını tam olarak "preview hattında yüzey küçültme kırıcı değişiklik
sayılmaz" gerekçesiyle seçti. Adım 7 filtresinde 3., 4., 6. ve 7. soruları
geçemiyor. Tarama, `Unshipped → Shipped` dolumuyla **aynı** GA turuna taşındı.

**Geçici en küçük güvenli yayın kapsamı:** `1.0.0-preview.1`, tam 20 paket
(UR-002 kararı). Teknik ön koşullar açık; yayın kullanıcı kararıyla 35× 🟡
sistemik hattın arkasına alındı (§13).

## 5. Ölçülen kanıtlar

| Kimlik | Konu | Durum | Ölçülen kanıt | Sonuç | Tarih |
|---|---|---|---|---|---|
| KN-001 | Packable kaynak envanteri | Tamamlandı | `grep -L '<IsPackable>false' src/*/*.csproj` | 20 proje | 2026-08-27 |
| KN-002 | Ortak TFM politikası | Tamamlandı | `src/Directory.Build.props` ve proje override'ları | Varsayılan `net8.0;net9.0;net10.0`; CLI, Templates ve Testing özel durumları var | 2026-08-27 |
| KN-003 | Paket metadatası tanımı | Tamamlandı | `src/Directory.Build.props` | MIT expression, authors, project/repository URL, icon, embedded README, symbols, Source Link ayarları tanımlı | 2026-08-27 |
| KN-004 | Deterministic build tanımı | Tamamlandı | `Directory.Build.props` | `Deterministic=true`; `ContinuousIntegrationBuild=true` yalnız CI ortamında | 2026-08-27 |
| KN-005 | Public API freeze durumu | Tamamlandı | `PublicAPI.Shipped.txt` ve `PublicAPI.Unshipped.txt` sayımı | Shipped giriş `0`; unshipped tip `676` | 2026-08-27 |
| KN-006 | CI yayın tetikleyicisi | Tamamlandı | `.github/workflows/ci.yml` | Her `v*` tag'i dry-run sonrası NuGet ve npm publish işlerini tetikler | 2026-08-27 |
| KN-007 | CI NuGet credential modeli | Tamamlandı | `.github/workflows/ci.yml` | `environment: nuget` ve `NUGET_API_KEY` secret kullanılıyor; trusted publishing yok | 2026-08-27 |
| KN-008 | Mevcut yayın kapısı kapsamı | Tamamlandı | `scripts/kapi.py` kaynak okuması | Dinamik paket kimliği, exact version, metadata, icon/README, repository commit, `.snupkg`, TFM başına XML varlığı, K-008, npm dry-run, beş external sample ve AOT smoke denetleniyor | 2026-08-27 |
| KN-009 | Worktree başlangıç durumu | Tamamlandı | `git status --short` | Kullanıcıya ait ilgisiz bir untracked keşif dosyası var; korunacak | 2026-08-27 |
| KN-010 | Doküman bütçesi | Tamamlandı | `python3 scripts/dokuman-bakim.py --denetle` | Bu dosya öncesinde `docs/**.md` bütçesinde yaklaşık %9 boşluk var; yeni ledger için ayrı kök dosya uygundur | 2026-08-27 |
| KN-011 | Exact release rehearsal | Tamamlandı | `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1` | Çıkış `0`; exact sürümlü 20 paket üretildi | 2026-08-27 |
| KN-012 | Packed extension consumers | Tamamlandı | Dry-run içindeki izole feed ve izole `NUGET_PACKAGES` koşumları | Beş sample; toplam 160 test geçti, 0 failed, 0 skipped | 2026-08-27 |
| KN-013 | Native AOT smoke | Tamamlandı | `osx-arm64` publish ve üretilen binary run | Provider, agent source ve generated tool smoke geçti | 2026-08-27 |
| KN-014 | npm dry-run | Tamamlandı | `npm publish --dry-run` | Paketleme başarılı; dry-run sürümü `0.0.0`, gerçek CI sürümü `v*` tag'inden ayrıca türetiliyor | 2026-08-27 |
| KN-015 | Artifact sayısı ve semboller | Tamamlandı | `find` ve `.snupkg` zip içeriği | 20 `.nupkg`, 19 `.snupkg`; Templates bilinçli olarak symbol paketi üretmiyor. Meta `AgentPrism.snupkg` var fakat PDB içermiyor | 2026-08-27 |
| KN-016 | Artifact boyutları | Tamamlandı | `ls -lhS` | En büyük paket `AgentPrism.Cli` yaklaşık 28 MB; sonra Core 2.0 MB, Client 1.5 MB, AspNetCore 1.1 MB | 2026-08-27 |
| KN-017 | Pre-release dependency sınırı | Tamamlandı | Üretilen `.nuspec` dosyaları | AgentPrism dışı pre-release bağımlılık yalnız `AgentPrism.AspNetCore` içinde; K-008 tutuluyor | 2026-08-27 |

### Henüz ölçülmeyen alanlar

- Tam `.nuspec` dependency graph'ının paket stratejisine göre değerlendirilmesi ve beklenmeyen içerik taraması.
- Gerçek Source Link kaynak çözümleme davranışı. Yerel ortamda `dotnet sourcelink` aracı yoktur.
- Meta paketin PDB içermeyen `.snupkg` üretmesinin NuGet.org davranışı ve gerekliliği.
- `AgentPrism.Cli` paketinin yaklaşık 28 MB boyutunun içerik ve support açısından değerlendirilmesi.
- 22 sütunlu extension seam matrisi.
- Public API yaprakları ve her yüzey için tut/değiştir/kaldır/internal/capability/ertele kararı.
- Güvenlik ve transport sınırlarının artifact tabanlı runtime probe'ları.
- XML, package README, root README, `docs-site`, sample ve release note drift'i.
- NuGet.org hesap, sahiplik, 2FA, Package ID uygunluğu ve publishing credential durumu.
- Güncel resmi NuGet operasyon seçenekleri ve trusted publishing uygunluğu.
- Tam manuel kabul setinin güncel koşumu.

## 6. Açık blocker'lar

Henüz yeniden üretilmiş bir 🔴 blocker yoktur. Aşağıdaki maddeler blocker değil,
doğrulama kapısıdır.

| Kimlik | Durum | Bulgu veya soru | Seviye | Ölçülen kanıt | Sorumlu workflow | Doğrulama ölçütü |
|---|---|---|---|---|---|---|
| BL-001 | Tamamlandı | Exact release artifact'i üret ve temel kapıyı çalıştır | Blocker değil | Exact dry-run çıkış `0`; 20 paket, 160 sample testi ve AOT smoke yeşil | `nuget-danismani` | Tamamlandı |
| BL-002 | Tamamlandı | 20 public paketin tamamının ilk preview için gerekli ve yeterince olgun olup olmadığı bilinmiyor | Blocker değil — kapsam kararı verildi | UR-002: kullanıcı tam entegrasyon setini seçti | `nuget-danismani` | Kapsam sabit; olgunluk artık paket bazında değil seam bazında (BL-003) ölçülür |
| BL-003 | **Tamamlandı** | Extension seam sözleşmelerinin birbiriyle tutarlılığı ölçüldü — 11/11 küme, 78 seam | Blocker değil — ölçüm bitti, bulgular BL-006/026/027/037/041 (🔴) + BL-007…051 (🟡/🟢) olarak kaydedildi | §15 tam matris planı ve küme raporları | `nuget-danismani` | Tamamlandı — bkz. §4 nihai özet |
| BL-004 | Karar gerekli | NuGet.org hesap, owner ve credential modeli bilinmiyor | Sınıflandırılmadı | Repo yalnız `NUGET_API_KEY` kullanan CI yolunu gösteriyor | Kullanıcı + yayın operasyonu | Sahiplik ve yetkilendirme kararının kaydı |
| BL-005 | Doğrulama gerekli | Meta paket PDB içermeyen `.snupkg` üretiyor | 🟢 Dokümantasyon veya cila | Artifact içinde 4 metadata girdisi ve 0 PDB ölçüldü; tüketici etkisi veya NuGet.org reddi yeniden üretilmedi | `nuget-danismani`; aksiyon çıkarsa faz zinciri | Resmi NuGet davranışı + push olmayan doğrulama |
| BL-006 | **KAPANDI** (2026-08-27, `kusur-giderme`) | `ITenantProviderBindingStore` BYOK lookup'ı üç farklı case-sensitivity davranışı taşıyor: `InMemoryTenantProviderBindingStore` ordinal case-sensitive `(TenantId, ProviderName)` anahtarı kullanıyor; SQL store'lar ham `=` predikatı kullanıyor (DB collation'a bağlı — Postgres/SQLite case-sensitive, SQL Server genelde değil); `ModelProviderRegistry` ve `TenantProviderEndpoints` ise `OrdinalIgnoreCase` kullanıyor. Admin `"OpenAI"` yazıp agent tanımı `"openai"` beklerse, Postgres/SQLite'ta binding sessizce bulunamaz ve akış global setup credential'ına düşer — bu, `ModelProviderRegistry.cs:330-332`'deki "sessiz düşme yok" yorumunun tam reddettiği senaryo | 🔴 Preview blocker | `src/AgentPrism.Core/Tenancy/InMemoryTenantProviderBindingStore.cs:8,24`; `src/AgentPrism.Sql.Shared/Internal/SqlQueriesBase.cs:1655-1658`; `src/AgentPrism.Core/Models/ModelProviderRegistry.cs:135,321,330-332`; `src/AgentPrism.AspNetCore/Endpoints/TenantProviderEndpoints.cs:169` | `nuget-danismani` → `kusur-giderme` | **Tamamlandı.** Kırmızı test önce yazıldı (`A_binding_saved_under_a_different_letter_case_is_still_the_tenants_binding`, 3/3 düştü: `LastCredential should not be null`) → `TenantProviderBinding.NormalizeProviderName` (public, invariant lower) eklendi, her iki store hem yazarken hem sorgularken uyguluyor → 3 migration mevcut satırları katlıyor (PostgreSQL 0038, SQLite/SQL Server 0025) → contract'a 3 case-mismatch case'i eklendi (4 implementasyonda koşar) → yeşil (Core 2056/2056, SQLite entegrasyon 15/15). K-639. **Sınıf taraması yapıldı:** 5 aday store incelendi, 4'ü tutarlı çıktı (`Experiment`/`AgentDefinition` her katmanda `Ordinal`, `Idempotency-Key` opak token, `Session.Id` sunucu üretimli) — `provider` tek outlier'dı |
| BL-007 | Açık | `ITenantStore`, `IContentProtector`, `IDataSubjectStore`, `IDataSubjectResolver` için reusable contract test taban sınıfı yok — üçüncü taraf implementasyonun koşabileceği bir suite yok, oysa `ApiKeyStoreContract`/`QuotaStoreContract`/`TenantEgressPolicyStoreContract`/`TenantProviderBindingStoreContract` aynı kümede gerçek davranış ölçen contract'lar olarak var | 🟡 1.0 blocker | `src/AgentPrism.Testing.Contracts.Xunit/Contracts/` içinde bu dört arayüz için sınıf yok | `nuget-danismani` → faz zinciri | Her dördü için contract sınıfı eklenir; en az bir dış sample'da koşulur |
| BL-008 | Açık | Kayıt API ergonomisi Küme B içinde tutarsız — yalnız `IContentProtector` (`AddContentProtection`/`AddContentProtection<T>()`) ve `ITenantContext` (`UseTenancy()`) için dedicated builder metodu var; `ITenantStore`, `ITenantEgressPolicyStore`, `ITenantProviderBindingStore`, `IApiKeyStore`, `IQuotaStore`, `IDataSubjectStore` için yok — consumer ham `services.Replace(ServiceDescriptor.Singleton<...>())` çağırmak zorunda ve bu desen hiçbir yerde dokümante değil (düz `AddSingleton` iki rakip kayıt bırakır) | 🟡 1.0 blocker | Küme B raporu — 6 arayüz için dedicated `Add*`/`Use*` yok | `nuget-danismani` → faz zinciri | Her store için dedicated builder extension eklenir veya `services.Replace` deseni XML doc + docs-site'ta açıkça anlatılır |
| BL-015 | Açık | `ModelProviderContract`/`ModelProviderCredentialContract` hiçbir shipped adapter (Anthropic/Azure/Google/OpenAI) test projesinde türetilmiyor — yalnız `AgentPrism.Samples.CustomModelProvider` sample'ında gerçek kullanılabilirlik kanıtlanmış. Bir adaptörün credential-cache mantığındaki regresyon (örn. "iki farklı credential aynı client'ı paylaşmamalı") shipped provider'larda CI'da yakalanmaz | 🟡 1.0 blocker | `tests/AgentPrism.{Anthropic,Azure,Google,OpenAI}.UnitTests/*.csproj` bu contract sınıflarını türetmiyor | `nuget-danismani` → faz zinciri | Her adapter test projesi contract sınıfını türetir; CI bunu zorunlu kılar |
| BL-016 | Bilgi | Küme B/C cila bulguları (🟢, toplu): `IContentProtector`'ın kayıtsız durumda fail-**open** (plaintext) davranışı release notes'ta vurgulanmalı (davranış doğru ve dokümante, keşfedilebilirlik eksik); `NullDataSubjectStore` erasure isteğine sessizce "başarılı, 0 satır silindi" dönüyor — `IDataSubjectResolver`'ın `409`'una kıyasla tutarsız bir tuzak; Küme B seam'leri için hiç dış sample yok; `AddModelProvider<T>()` generic overload'ı yok (yalnız instance/factory var); 4 provider adaptöründen 3'ü (Anthropic/Azure/Google) için AOT ölçüm kaydı dokümante değil (yalnız OpenAI ölçülmüş, `docs/MIMARI.md` §9) | 🟢 Doküman/cila | Küme B ve C raporları | `nuget-danismani` → doküman senkronu | Release notes ve docs-site'a eklenir; kod değişikliği şart değil |
| BL-017 | Açık | `IContentGuard` ve `IToolAuthorizationHandler` için reusable contract test yok (`IToolRegistry`, `IPendingApprovalStore`, `IToolApprovalRuleStore`'un aksine) — üçüncü taraf implementasyon fail-closed/thread-safety/tenant davranışını doğrulayacak resmi bir suite'e sahip değil | 🟡 1.0 blocker | `src/AgentPrism.Testing.Contracts.Xunit/Contracts/` içinde bu ikisi için sınıf yok | `nuget-danismani` → faz zinciri | Her ikisi için contract sınıfı eklenir; en az bir dış sample'da koşulur |
| BL-018 | Açık | `IContentGuard` kayıtsızken fail-open (hiç guard koşmuyor); davranış XML dokümanda açık ama benzer "sessiz boşluk" seam'lerinde kullanılan `NonPersistentStorageWarningService` türünden bir başlangıç uyarısı yok | 🟡 1.0 blocker | `src/AgentPrism.Core/Registration.Operations.cs:96-99`; `IContentGuard.cs:14-21` | `nuget-danismani` → faz zinciri | Guard kayıtsızken opsiyonel bir startup log/uyarı eklenir |
| BL-019 | Açık | Küme E kayıt ergonomisi tutarsız — `IToolAuthorizationHandler` için dedicated `Add*` yok, `IContentGuard` yalnız generic `AddContentGuard<T>()` sunuyor (instance/factory yok); custom `IContentGuard` veya SQL-dışı approval store için dış sample yok; sütun 14 (custom tool/guard exception normalizasyonu) tam doğrulanamadı | 🟡 1.0 blocker | Küme E raporu | `nuget-danismani` → faz zinciri | Builder extension'lar tamamlanır; `IncludeDetailedErrors`/exception normalizasyonu ayrıca ölçülür |
| BL-021 | Bilgi | Küme E cila bulguları (🟢, toplu): `RecordToolInvocation` metric `TenantId` tag'i taşımıyor (kasıtlı kardinalite hijyeni, dokümante değil); `IToolAuthorizationHandler` denial tracking'in dedicated metric'i yok | 🟢 Doküman/cila | Küme E raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-022 | Açık | `IMcpOAuthCoordinator` — kümenin en yüksek kiracı-izolasyon/CSRF riskli sınıfı — hiçbir testte referans edilmiyor; `McpTenantServerKey`'in kendisi önceki bir string-interpolation sızıntısını kapatmak için özel yazılmış, yani bu alan daha önce kusur üretmiş | 🟡 1.0 blocker | `tests/` altında `McpOAuthAuthorizationCoordinator`/`IMcpOAuthCoordinator` referansı yok; `McpTenantServerKey.cs:1-13` | `nuget-danismani` → faz zinciri | State/tenant binding'i kilitleyen bir contract/regresyon testi eklenir |
| BL-023 | Açık | `IMcpPromptClient`/`IMcpResourceClient`/`IMcpServerStore` için reusable contract test yok; MCP client/host'u `PackageReference` ile (yalnız `ProjectReference` değil) koşan dış bir sample yok — paketlenmiş tüketici davranışı 1.0 öncesi doğrulanmamış | 🟡 1.0 blocker | Küme F raporu | `nuget-danismani` → faz zinciri | Contract sınıfları eklenir; `samples/` içinde en az biri `PackageReference`'a geçirilir |
| BL-024 | **KAPANDI (Faz 121)** | Küme F'nin 6 arayüzünün hiçbirinin XML dokümanı DI lifetime'ı (`singleton`) açıkça belirtmiyordu — üçüncü taraf implementasyon bunu kaynağı okuyarak öğrenmek zorundaydı | ~~🟡~~ ✅ Kapandı | Küme F raporu | **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** | `IMcpResourceClient`, `IMcpPromptClient`, `IMcpResourceContextProviderFactory`, `IMcpOAuthCoordinator`, `IMcpToolRefresher`, `IMcpServerStore` — hepsinin XML dokümanına "DI lifetime — singleton" bölümü eklendi; `SeamContractDocumentationTests` tarafından kilitli |
| BL-025 | Bilgi (kısmen kapandı) | Küme F cila bulguları (🟢, toplu): `IMcpServerStore` ad-şekli doğrulaması abstraction'da değil bağlantı katmanında (yalnız log uyarısı, sessiz başarısızlık); ~~`CatalogToolCallHandler` MCP-host tarafında kendi run hatasının ham `ex.Message`'ını dış çağırana döndürüyor~~ **Faz 119'da kapandı** (BL-027/BL-037 sınıf taramasının bulduğu 21 vakadan biri, `SafeErrorText` uygulandı); MCP'ye özel `span`/tag yok, genel MAF OpenTelemetry enstrümantasyonuna biniyor | 🟢 Doküman/cila | Küme F raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-026 | **SEVİYE DÜŞÜRÜLDÜ** (2026-08-27, `kusur-giderme` Adım 2/7) | **Drain/yeni-run yarışı**: `DrainGate.Check` (`AgentEndpoints.cs:193`) ile `IRunCancellationRegistry.Register` (`RunRecordingAgent.cs:214-218`) arasında bir pencere var ve `AgentPrismDrainService.StopAsync` bu pencerede `ActiveCount==0` görüp erken dönebilir. **Ancak iş kaybı OLUŞMUYOR** — iki bağımsız mekanizma bu boşluğu zaten kapatıyor | ~~🔴~~ → 🟡 1.0 blocker (muhasebe hassasiyeti, iş kaybı değil) | **Ölçüldü:** (1) HTTP yolu — `AgentPrismDrainService` DI'ye **son** kaydedildiği için `StopAsync`'i **ilk** koşar; `GenericWebHostService` ise **son** durur, yani Kestrel'in kendi request draining'i o isteği tamamlanana kadar bekletir. (2) Job yolu — `JobWorkerBackgroundService.ExecuteAsync`'in `finally` bloğu `WaitForRunningJobsAsync()` çağırır (satır 82, 181-187) ve leased her işi bekler. (3) `AgentPrismDrainOptions.Enabled` **varsayılan `false`** (`AgentPrismDrainOptions.cs:18`) — yarış yalnız drain'i açıkça açan kurulumu ilgilendirir | `faz-planlama` (blocker değil) → **belge kısmı [Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)'de kapandı** | Denetimin "süreç başlamak üzere olan run'ı yarıda keser" iddiası yeniden üretilemedi. Kalan gerçek kusur: drain servisi reklam ettiği garantiyi **kendi başına** sağlamıyor, iki yedek mekanizmaya bel bağlıyor — bu artık dokümante: `IAgentPrismDrainState`'in XML dokümanı iki yedek mekanizmayı (Kestrel request draining, `WaitForRunningJobsAsync`) ve bunlara bel bağladığını açıkça anlatıyor; `AgentPrismDrainOptions.Enabled` de aynı nota işaret ediyor (Açık Soru 4, seçenek C — ikisine de). Ölçülmüş bir iş-kaybı repro'su üretilmeden 🔴 sayılmaz |
| BL-027 | **KAPANDI (Faz 119)** | **`IRunStore`'a ham exception mesajı sızıyor**: `RunRecordingAgent.Completion.cs:282` `RunError.Message = exception.Message`'ı `ContentGuardPipeline`'dan geçirmeden yazıyor. Kardeş yol `IRunInputStore` aynı sınıf bir kusur için (`HATA-S3-006`) daha önce düzeltilmiş ve guard'dan geçiriliyor — düzeltme run-error yoluna uygulanmamış | ~~🔴~~ ✅ Kapandı | `src/AgentPrism.Core/.../RunRecordingAgent.Completion.cs:282`; kıyasla `RunRecordingAgent.Persistence.cs:36-41` (`HATA-S3-006` düzeltmesi) | `nuget-danismani` → **`faz-planlama`** → **[Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md)** | **SINIF TARAMASI YAPILDI (2026-08-27) — bilinen iki vakanın ÜSTÜNE 19 vaka daha bulundu, uygulama sırasında 5 ek vaka daha (26 toplam).** Bkz. §16. `ContentGuardPipeline` genişletilmedi (opt-in, varsayılan boş); onun yerine yeni paylaşılan `SafeErrorText` (K-640) her 26 sitede uygulandı, mimari cırcır kapısı (`RawExceptionTextSiteTests`) 22. sızıntıyı otomatik yakalar |
| BL-028 | **KAPANDI (Faz 121)** | Cancellation tamamen cooperative (`RunCancellationRegistry.TryCancel` yalnız `CancellationTokenSource.Cancel()` çağırıyor) ama `IRunCancellationRegistry`'nin arayüz dokümanı bu sınırı belirtmiyordu — tüketici `TryCancel`'ın işi/faturalamayı gerçekten durdurduğunu varsayabilirdi | ~~🟡~~ ✅ Kapandı | `src/AgentPrism.Core/.../RunCancellationRegistry.cs:27-51`; `IRunCancellationRegistry.cs` | **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** | `TryCancel`'ın XML dokümanına "Guarantee limit: cancellation is cooperative, not forced" bölümü eklendi; `RunCancellationRegistryTests.TryCancel_does_not_stop_a_run_body_that_never_reads_its_token` bunu FONKSİYONEL testle de ölçüyor (token'ı hiç okumayan bir "run body" sinyalden sonra da çalışmaya devam ediyor) |
| BL-029 | **KAPANDI (Faz 121)** | Tenant-mode dokümantasyonu Küme A içinde tutarsızdı — yalnız `IRunStore` EXPECTED/AMBIENT/tenant-independent tablosu taşıyordu; `ITraceStore.GetTraceByRunAsync` fiilen ambient-tenant (`SqlTraceStore.cs:85`) ama arayüz dokümanında hiç tenant notu yoktu; `IRunScoreStore`/`IRunInputStore` yalnız parametre bazlıydı, adlandırılmış mod yoktu | ~~🟡~~ ✅ Kapandı | Küme A raporu | **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** | `IRunStore`'un tenant-mode deseni Küme A'nın kalan 9 üyesine uygulandı: `IRunScoreStore`/`IRunInputStore`/`IRunEventSink` EXPECTED; `ITraceStore` karışık (yaz=EXPECTED, oku=AMBIENT); `IRunCancellationRegistry` karışık (kayıt=EXPECTED, sayaçlar=TENANT-INDEPENDENT); `IRunAttributionContext`/`IRunErrorClassifier`/`IRunPricingResolver`/`IAgentPrismDrainState` TENANT-INDEPENDENT (tenant kavramı yok) |
| BL-030 | Açık | `IRunEventSink`, `IRunAttributionContext`, `IRunCancellationRegistry`, `IRunErrorClassifier`, `IRunPricingResolver`, `IAgentPrismDrainState` için reusable contract test yok (yalnız 4 storage arayüzünde var); 8/10 arayüz için dış sample yok (yalnız `IRunStore`/`IRunScoreStore` için `FileRunStore` var) | 🟡 1.0 blocker | Küme A raporu | `nuget-danismani` → faz zinciri | En azından `IRunCancellationRegistry` (root/child, tenant-mismatch) için contract sınıfı eklenir |
| BL-031 | Bilgi | Küme A cila bulguları (🟢, toplu): `ITraceStore`'un yüksek-kardinalite tag'lerinin (run id, tenant id) kasıtlı olduğu dokümante değil (`IRunAttributionContext.Labels`'ın kardinalite uyarısıyla tezat); `IRunPricingResolver`/`IRunErrorClassifier` "cache yok" tasarım kararı dokümante değil | 🟢 Doküman/cila | Küme A raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-033 | Açık | Decorator exception'ları source exception'larının aksine normalize edilmiyor — `CompositeAgentCatalog.cs:142-145,216-219` `decorator.Decorate(...)`'ı try/catch olmadan çağırıyor, oysa aynı metotta iki satır üstteki source çağrısı `HandleSourceFailure` ile sarılı. Üçüncü taraf bir `IAgentDecorator` (veya `ToolApprovalAgentDecorator`) fırlatırsa ham exception `AgentEndpoints.cs:774`'ün yalnız `AgentPrismException` yakalayan catch'ini atlayabilir — ASP.NET Core varsayılan handler'ına sızıp sızmadığı **ölçüldü (2026-08-27): sızmıyor** — `grep -rn "UseExceptionHandler|IExceptionHandler|AddProblemDetails" src --include="*.cs"` yalnız `JsonBindingProblemMiddleware.cs:16`'daki yorumu buluyor, yani AgentPrism global handler **kaydetmez** ve Production'da ASP.NET Core varsayılanı gövdesiz `500` döner; ham mesaj HTTP'ye çıkmaz. **Kalan gerçek kusur dar ve tutarlılıktır:** üçüncü taraf decorator hatası `AgentPrismException`'a normalize edilmediği için kardeş `source` yolunun ürettiği sınıflandırılmış hata yerine çıplak `500` verir | 🟡 1.0 blocker (leak değil, tutarlılık) | `src/AgentPrism.Core/.../CompositeAgentCatalog.cs:142-145,216-219`; `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs:774` | `nuget-danismani` → derinleştirme, sonra `kusur-giderme` | Global exception handler davranışı ölçülür; gerekiyorsa decorator çağrısı da normalize edilir |
| BL-034 | **KISMEN KAPANDI (2026-08-27, `kusur-giderme`)** | `IAgentDecorator` için hiç builder registration API'si yok (`IAgentSource`/`IRunJudge`/`IModelProvider`'ın aksine) — consumer ham `services.TryAddEnumerable(...)` çağırmak zorunda; ~~ayrıca `IAgentDecorator.Order`'ın XML dokümanı kendi kendiyle çelişiyor~~ **doküman kusuru KAPANDI** | 🟡 1.0 blocker (kalan: registration API'si — kulvar 2) | **Ölçüldü ve karar verildi (K-642):** sayısal yön DEĞİŞMEDİ, yanlış olan cümleydi. Yeni yazılan iki davranış testi (`AgentDecoratorOrderingTests`) mevcut mekanizmayla yeşil geçti; el yazısı `docs-site/concepts/runs.md` diyagramı da zaten doğruydu. **Sınıf taraması 2 vaka daha buldu** — `IAgentSource.Priority` yönü hiç söylemiyordu, `IAgentCatalog.ListAsync` "higher priority" diyerek yüksek sayı gibi okunuyordu; ikisi de düzeltildi. Kapı: `OrderingContractDocumentationTests` (üç vakada da ayrı ayrı kırmızı verdiği ölçüldü) | `nuget-danismani` → `kusur-giderme` (doküman ✅) → faz zinciri (registration API'si) | Doküman kısmı tamamlandı; dedicated `AddAgentDecorator<T>()` kulvar 2 kapsamında kalır |
| BL-035 | **KAPANDI (Faz 121)** | `IAgentDefinitionStore` ambient `ITenantContext` kullanırken `IAgentSkillStore`/`ISkillScriptGrantStore` explicit `tenantId` parametresi kullanıyor — aynı kümede tutarsız tenant-parametre şekli (bug değil, her ikisi de doğru filtreleniyor, ama üçüncü taraf implementer'ın arayüz başına ayrı öğrenmesi gerekiyordu) | ~~🟡~~ ✅ Kapandı | Küme D raporu | **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** | Üçünün de XML dokümanına tenant-mode bölümü eklendi; `IAgentDefinitionStore`'un dokümanı şekil farkının GERÇEK gerekçesini yazıyor: iki nesil farklı store konvansiyonu (kurucu enjeksiyon vs. açık parametre), derin bir tasarım kuralı değil — bir implementer her arayüzün KENDİ remarks'ına bakmalı, kümeden tutarlılık varsaymamalı |
| BL-036 | Bilgi | Küme D cila bulguları (🟢, toplu): decorator sıralama mantığı (`OrderByDescending(d => d.Order)`) 4 yerde ayrı ayrı tekrarlanıyor (`CompositeAgentCatalog.cs:40`, `RunContinuationJobHandler.cs:55`, `RunReplayService.cs:77`) — var olan `AgentDecoratorPipeline.Apply` yalnız 2 yerde kullanılıyor, şu an tutarlı ama bakım riski; `IAgentDecorator`/`IAgentCatalog` için contract test veya dış sample yok | 🟢 Doküman/cila | Küme D raporu | `nuget-danismani` → doküman senkronu / iç refactor | Docs-site'a eklenir; refactor isteğe bağlı |
| BL-037 | **KAPANDI (Faz 119)** | **`WorkflowRunner.ToRunError`'a ham exception mesajı sızıyor** — BL-027 ile birebir aynı kusur sınıfı, farklı yol: `unwrapped.Message` hiçbir guard'dan geçirilmeden `RunEvent.Text`'e yazılıyor ve persist ediliyor | ~~🔴~~ ✅ Kapandı (BL-027 ile birlikte) | `src/AgentPrism.Workflows/Internal/WorkflowRunner.cs:1179-1198` | `nuget-danismani` → **[Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md)** (BL-027 ile BİRLİKTE, tek sınıf taraması) | `ToRunError` artık `SafeErrorText.ForPersistence` + korelasyon kimlikli `ILogger` çağrısı kullanıyor; `WorkflowJobHandler`'ın kendi ayrı `exception.Message` yolu da aynı turda kapatıldı |
| BL-038 | **KAPANDI (Faz 121)** | Workflow resume'un side-effecting adımları tekrar çalıştırabileceği (at-least-once semantics) yalnız `AddWorkflowFunction<T>()`'ın XML dokümanında anlatılıyordu — `AgentPrism.Abstractions`'daki `IWorkflowRunner`/`IWorkflowCheckpointStore` (paketin asıl public sözleşme yüzeyi) bundan hiç bahsetmiyordu; davranış doğru ve kasıtlı, yalnız yanlış dosyada dokümanteydi | ~~🟡~~ ✅ Kapandı | `AgentPrismWorkflowFunctionExtensions.cs:66-77` vs. `IWorkflowRunner.cs`, `IWorkflowCheckpointStore.cs` | **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** | At-least-once notu `IWorkflowRunner.ResumeStreamingAsync` ve `IWorkflowCheckpointStore.CreateAsync`'in kendi XML dokümanına da eklendi (eski konumdaki metin duruyor, tekrar etmiyor, birbirine referans veriyor) |
| BL-039 | Açık | `IWorkflowRunner`/`IWorkflowFunctionCatalog` için contract test yok (store'ların aksine); 4 arayüzün hiçbiri için dış `Custom*` sample yok (`CustomTool`/`CustomModelProvider`/`CustomRunJudge`/`CustomAgentSource`'un aksine); iki kod-tanımlı workflow aynı adı paylaşırsa ham `.NET ArgumentException` fırlıyor (`WorkflowCatalog.cs:41-44`) — `WorkflowFunctionRegistry`'nin aynı durumda verdiği net `AgentPrismException`'la tutarsız; DI lifetime/thread-safety 4 arayüzün hiçbirinde dokümante değil | 🟡 1.0 blocker | Küme G raporu | `nuget-danismani` → faz zinciri | Contract sınıfları + sample eklenir; duplicate-name hatası `AgentPrismException`'a çevrilir |
| BL-040 | Bilgi | Küme G cila bulguları (🟢, toplu): `IWorkflowFunctionCatalog`'un cache semantiği (executor identity stability) yalnız kayıt call site'ındaki `//` yorumunda anlatılıyor, arayüz dokümanında değil; workflow/function adları için ad-şekli doğrulaması dokümante/zorlanmış değil (yalnız non-empty kontrolü var) | 🟢 Doküman/cila | Küme G raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-041 | **KAPANDI (Faz 120)** | Denetim bunu "`IIdempotencyStore` amacına rağmen job loop'unda kullanılmıyor" diye kaydetmişti. **Ölçüldü, çerçeve yanlıştı:** `IIdempotencyStore`'un kendi XML dokümanı (`IIdempotencyStore.cs:6-14`) onu açıkça **HTTP `Idempotency-Key` başlığı** mekanizması olarak tanımlıyor ("exactly as the HTTP `Idempotency-Key` standard prescribes"); tüketicileri `IdempotencyFilter` ve `InboundTriggerDispatcher`. Job loop'unda tekrar koruması **zaten vardı** (`JobItemStatus.Pending` kontrolü, üç yerleşik handler'da da koşuyor). Job loop'una bu store'u bağlamak gereksiz ikinci bir mekanizma olurdu. **Geriye kalan gerçek kusur tek ve dardı:** `IJobHandler`'ın sözleşmesi at-least-once'ı söylemiyordu | ~~🔴~~ ✅ Kapandı | `IJobHandler.cs` (artık `ExecuteAsync`'in `<remarks>`'i at-least-once'ı ve `JobContext.Items`'ın süzülmemiş geleceğini açıkça söylüyor) | **[Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md)** | Sözleşme XML'e yazıldı + `JobHandlerContract` (`AgentPrism.Testing.Contracts.Scheduling`, üç yerleşik handler + `AgentPrism.Samples.CustomJobHandler` dış sample'ı türetiyor) + `JobLeaseExpiryTests` (`InMemoryJobStore` üzerinde: abandoned lease süresi dolunca gerçekten yeniden kiralanıyor ve `ListItemsAsync` daha önce `Completed` işaretlenen item'ı süzmeden geri veriyor) davranışın gerçekten var olduğunu ölçüyor |
| BL-042 | **KAPANDI (Faz 121)** | `ISingletonLeaseStore`'un lease-sahibi donduğunda (`GC` duraklaması, thread starvation, ağ bölünmesi) oluşan sınırlı split-brain penceresi ne arayüz dokümanında ne `SingletonGuard.cs`'de belirtiliyordu — "eventually correct, strictly exclusive değil" garantisi dokümante değildi | ~~🟡~~ ✅ Kapandı | `SingletonGuard.cs:99`; `SqlSingletonLeaseStore.cs:61-79` | **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** | `ISingletonLeaseStore`'un XML dokümanına "Guarantee limit: eventually correct, NOT strictly exclusive" bölümü eklendi, split-brain penceresini ve iki-owner senaryosunu açıkça anlatıyor; `SeamContractDocumentationTests`'in dördüncü boyut kapısı bu metni kilitliyor |
| BL-043 | **KAPANDI (Faz 121)** | Webhook teslimatı at-least-once (aynı `delivery.Id` her retry'da `X-AgentPrism-Delivery` header'ıyla gönderiliyor, alıcı-taraflı dedup bekleniyor) ama bu `IWebhookStore`/`IWebhookPublisher`'ın XML dokümanında hiç belirtilmiyordu — yalnız `WebhookDeliveryJobHandler.cs:299`'da görülebiliyordu | ~~🟡~~ ✅ Kapandı | `WebhookDeliveryJobHandler.cs:299` | **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** | At-least-once garantisi ve `X-AgentPrism-Delivery` dedup header'ı hem `IWebhookStore` hem `IWebhookPublisher`'ın XML dokümanına eklendi |
| BL-044 | Açık | Kümede hiç `ActivitySource`/`Meter` yok (`Scheduling`, `Webhooks`, `Coordination`, `Idempotency`, `Triggers` içinde grep boş) — yalnız hata yollarında `ILogger` uyarısı var; job backlog, webhook teslim başarısızlık oranı, lease çekişmesi gibi operasyonel sinyaller `IRunStore`/`AgentPrismMetrics` seviyesine kıyasla eksik | 🟡 1.0 blocker | Küme H raporu | `nuget-danismani` → faz zinciri | Operasyonel metric'ler eklenir |
| BL-045 | Bilgi | Küme H cila bulguları (🟢, toplu): 6 store arayüzü için `Testing.Contracts` taban sınıflarını `PackageReference` ile koşan dış sample yok (contract'lar repo içinde gerçek ve koşuluyor, yalnız dış tüketici perspektifinden kanıtlanmamış); schedule/webhook/trigger adları için ad-şekli doğrulaması yok (yalnız non-empty, DB uniqueness var) | 🟢 Doküman/cila | Küme H raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-046 | **KAPANDI (Faz 121) — dokümanın ÖTESİNDE, gerçek bir kusur olarak** | `IAuditLog`/`AuditQuery.TenantId=null` "çağıranın kiracısına düşer" davranışı yalnız DTO yorumunda anlatılıyordu, `IAuditLog` arayüzünün kendisinde bir sözleşme değildi — kayıt "bugün hiçbir shipped kod yolu bunu tetiklemiyor" diyerek riski küçümsüyordu, ama ölçüldüğünde `InMemoryAuditLog` (AgentPrism'in KENDİ referans implementasyonu) null/boş tenant'ta gerçekten TÜM kiracıların kaydını tarıyordu — dokümante edilen niyet hiçbir implementasyonda gerçek davranış değildi | ~~🟡~~ ✅ Kapandı (K-644) | `AuditQuery.cs:6`; `InMemoryAuditLog.cs` (eski `SnapshotAll`); `SqlAuditLog.cs` (`?? string.Empty`, sessizce boş sonuç) | **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** | `IAuditLog`/`AuditQuery`/`AuditChainQuery`'nin XML dokümanı sözleşmeyi açıkça yazdı VE `InMemoryAuditLog`/`SqlAuditLog` `SqlRunStore`'un zaten kullandığı `ITenantContext` fallback desenine hizalandı (kod düzeltmesi, yalnız doküman değil). `AuditLogContract`'a (public) 3 yeni test eklendi, `InMemory`/`PostgreSQL`/`SqlServer`/`Sqlite`'ın DÖRDÜNDE de ayrı ayrı yeşil koştu |
| BL-047 | Açık | Audit-write başarısızlığı loglanıyor (`AuditRecorder.cs:53-60`) ama dedicated metric/counter yok — production'da audit-log bozulmasını yakalamak tamamen log taramasına bağlı | 🟡 1.0 blocker | `AuditRecorder.cs`; `AgentPrismMetrics.cs` (audit counter yok) | `nuget-danismani` → faz zinciri | Audit-write-failure metric eklenir |
| BL-048 | **KISMEN KAPANDI (Faz 121)** | `ISpeechSynthesizer`/`ISpeechTranscriber`/`IVoicePricingReader`/`IVoiceHealthCheck` için contract test yok; custom `ISpeechSynthesizer`/`IAuditLog` için dış sample yok; ~~`IAuditActorResolver`'ın 2 satırlık XML dokümanı AsyncLocal/ambient-context bağımlılığını ve singleton lifetime etkisini hiç anlatmıyor~~ **doküman kısmı kapandı** | 🟡 1.0 blocker (kalan: contract test + dış sample — kulvar 1/4) | Küme J raporu | `nuget-danismani` → faz zinciri (kalan) · **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** (doküman ✅) | `IAuditActorResolver`'ın dokümanı `AuditActorContext`'in AsyncLocal mekanizmasını ve singleton-safe olma gerekçesini artık anlatıyor; 4 ses arayüzüne de DI lifetime (singleton) + tenant mode (TENANT-INDEPENDENT) eklendi. Contract sınıfları ve dış sample kulvar 1/4 kapsamında kalır |
| BL-049 | Bilgi | Küme J cila bulguları (🟢, toplu): `ISpeechTranscriber.TranscribeAsync`'in `Stream audio` sahipliği (kim dispose eder) arayüzde belirtilmiyor; `IAuditLog.WriteAsync`'in retry'de idempotency'si (dedup anahtarı yok) dokümante değil; voice arayüzleri abstraction seviyesinde hiç `TenantId` taşımıyor (tenant başka yerde uygulanıyor, makul ama not gerekiyor) | 🟢 Doküman/cila | Küme J raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-050 | **KISMEN KAPANDI (Faz 121)** | Küme I kayıt ergonomisi tutarsız — yalnız `IRunJudge` tam üçlü (`AddRunJudge<T>()`/instance/factory) alıyor; diğer arayüzler için hiç `AddX()` yok (kulvar 2, açık kalır); `IVectorSearchStore`/`IConversationBranchStore`/`IMigrationApplier`/`ISqlPersistenceDiagnostics` için contract test yok (kulvar 1, açık kalır); ~~singleton/thread-safety/no-per-run-state 10/11 arayüzde dokümante değil~~ **doküman kısmı kapandı**; ~~`IAttachmentStore.OpenReadAsync`/`IAttachmentStorage.ReadAsync` stream sahipliğini belirtmiyor~~ **kapandı** | 🟡 1.0 blocker (kalan: registration API + contract test — kulvar 1/2) | Küme I raporu | `nuget-danismani` → faz zinciri (kalan) · **[Faz 121](121-SEAM-SOZLESME-DOKUMANI.md)** (doküman ✅) | 12 üyenin (`ISessionStore`, `IConversationBranchStore`, `IAttachmentStore`, `IAttachmentStorage`, `IRetentionStore`, `IArchiveSink`, `IRetentionPolicyStore`, `IEvalStore`, `IExperimentStore`, `IMigrationApplier`, `ISqlPersistenceDiagnostics`, `IVectorSearchStore`) hepsine DI lifetime (singleton, ikisi optional) eklendi; `IAttachmentStore.OpenReadAsync`/`IAttachmentStorage.WriteAsync`/`ReadAsync` stream sahipliğini (çağıran sahiplenir ve dispose eder) açıkça yazıyor. Registration API ve contract test kulvar 1/2 kapsamında kalır |
| BL-051 | Bilgi | Küme I cila bulgusu (🟢): retention "politika yok = sonsuza kadar sakla" dokümante ve kasıtlı bir varsayılan (Faz 25 kararı) ama `Enabled=false` + politika yoksa hiçbir başlangıç uyarısı yok — compliance için retention'a güvenen bir tüketici yanlış yapılandırmayı fark etmeyebilir | 🟢 Doküman/cila | Küme I raporu | `nuget-danismani` → doküman senkronu | Docs-site'a not eklenir; isteğe bağlı diagnostics kontrolü değerlendirilir |

## 7. Ürün ve public API kararları

| Kimlik | Konu | Durum | Bulgu veya soru | Ölçülen kanıt | Seçenekler | Alınan karar | Gerekçe | Risk | Workflow | Doğrulama | Tarih |
|---|---|---|---|---|---|---|---|---|---|---|---|
| UR-001 | Yayın türü | Tamamlandı | İlk dış yayın preview, rc veya stable mı olmalı? | KN-005 ve pre-release Hosting bağımlılıkları | `preview` / `rc` / `stable 1.0` | `preview` | Shipped baseline boş (0 giriş, 676 unshipped tip) ve `AgentPrism.AspNetCore` pre-release MAF bağımlılığı taşıyor — stable/RC taahhüdü bugün karşılanamaz; preview SemVer'de kırıcı değişikliğe izin verir | Yüksek | `nuget-danismani` | Kullanıcı kararı | 2026-08-27 |
| UR-002 | İlk hedef tüketici | Tamamlandı | Ürün anlatısı ve en küçük paket kapsamı hangi birincil persona için optimize edilmeli? | Repo bir control plane, provider, storage, transport, UI ve extension paketleri taşıyor | Yalnız çekirdek / tam entegrasyon seti / çekirdek + kanıtlanmış alt küme | Tam entegrasyon seti (20 paketin tamamı) | Kullanıcı, dry-run'da zaten kanıtlanmış tam kapsamı (20 paket, 160 sample testi, AOT smoke yeşil) korumayı seçti | Yüksek — audit yükü en geniş seçenek düzeyinde | `nuget-danismani` | Kullanıcı kararı | 2026-08-27 |
| UR-003 | Public API freeze | **Tamamlandı — GA'ya ertelendi** | Preview öncesinde hangi yüzey korunmalı veya küçültülmeli? | Ölçüldü 2026-08-27: 17 `PublicAPI.Shipped.txt` **0** satır, unshipped **8417** satır / **680** tip | Preview'dan önce tara / **GA turuna ertele** | **GA turuna ertelendi; seviye 🔴 → 🟡** | Sevk edilen `versioning.md:11-13` preview hattında yüzey daralmasının kırıcı sayılmadığını zaten söylüyor; K-603 `Shipped` dolumunu GA'ya erteledi; K-602 tek sürüm hattını bu gerekçeyle seçti. Faz 96 aynı erişilebilirlik ölçütünü koşup 96 tipi `internal` yapmıştı (K-601) — ikinci turun verimi düşük | Orta — GA'da yüzey büyükse daraltma pahalılaşır; azaltım: tarama `Unshipped → Shipped` dolumuyla aynı turda koşar | `nuget-danismani` (GA turu) | GA kapısında `Shipped.txt` dolumu | 2026-08-27 |

## 8. Operasyonel yayın kararları

| Kimlik | Konu | Durum | Mevcut kanıt | Karar |
|---|---|---|---|---|
| OP-001 | NuGet owner modeli | **Tamamlandı** | Repo kanıtı yok | **Kişisel hesap (`farukatasoy`)** — kullanıcı kararı 2026-08-27. Kabul edilen risk: sahiplik devri paket başına elle yapılır, tek bakımcı riski kalıcıdır (bkz. RK-012) |
| OP-002 | 2FA | Doğrulama gerekli | Repo kanıtı yok | — |
| OP-003 | Package ID sahipliği/uygunluğu | **Ölçüldü — 20/20 müsait** | 2026-08-27, salt-okunur `registration5-semver1` GET × 20: hepsi `404`. npm `@agentprism/client` de `404`. **`agentprism` npm scope'u YOK** (`/-/org/agentprism` → `404`) | Kimlik çakışması yok. `AgentPrism.*` ID prefix reservation başvurusu ayrı bir kalem — karar verilmedi |
| OP-004 | Publishing authentication | **Kullanıcı erteledi** (2026-08-27) | CI, süre ve scope'u repo dışında olan `NUGET_API_KEY` kullanıyor ([`ci.yml:303-306`](../.github/workflows/ci.yml#L303-L306)) | — RK-004 açık kalır |
| OP-005 | Trusted publishing | **Kullanıcı erteledi** (2026-08-27) | CI'da `id-token` izni yok (`permissions: contents: read`, `ci.yml:11-12`); güncel resmi uygunluk **hâlâ araştırılmadı** | — |
| OP-006 | Tag ve GitHub release | Karar gerekli | Her `v*` tag'i gerçek NuGet ve npm publish tetikler. **Ölçüldü 2026-08-27: CI hiç GitHub release ÜRETMİYOR** — `gh release` / `action-gh-release` workflow'da geçmiyor | — |
| OP-007 | Release notes | Aksiyon gerekli | **Ölçüldü 2026-08-27: hiçbir artifact yok** — `CHANGELOG.md` yok, `PackageReleaseNotes` hiçbir `Directory.Build.props`'ta tanımlı değil, `docs-site`'ta changelog sayfası yok. 20 paket sayfası boş release-notes alanıyla çıkar | — |
| OP-008 | Deprecation/yank/hotfix | Karar gerekli | Repo politikası henüz bu ledger'a doğrulanmadı | — |
| OP-009 | Dependency/vulnerability takibi | İnceleniyor | Dependabot NuGet yapılandırması mevcut (`.github/dependabot.yml`, haftalık) | — |
| OP-010 | npm/NuGet asimetrik kısmi yayın | **Kullanıcı erteledi** (2026-08-27) | Aynı `v*` tag'i `nuget-publish` ([`ci.yml:284`](../.github/workflows/ci.yml#L284)) ve `npm-publish` ([`ci.yml:319`](../.github/workflows/ci.yml#L319)) işlerini **paralel** tetikler (farklı `needs`). `agentprism` npm scope'u bugün yok; `NPM_TOKEN` durumu repo dışında. Scope hazır değilse 20 NuGet paketi **kalıcı** yayınlanır, npm işi kırılır — ve sevk edilen doküman `npm install @agentprism/client` diyor (`docs-site/src/content/docs/packages.md:70`, `guides/typescript-client.md:25`) | — Tag'den önce çözülmelidir; bkz. RK-011 |

## 9. Risk kaydı

| Kimlik | Durum | Risk | Seviye | Olasılık / etki | Azaltım | Sorumlu workflow |
|---|---|---|---|---|---|---|
| RK-001 | İnceleniyor | İlk `v*` tag'inin NuGet ve npm'e kalıcı yayın tetiklemesi | Yüksek | Orta / yüksek | Tag öncesi exact dry-run, environment protection ve credential doğrulaması | Yayın operasyonu |
| RK-002 | İnceleniyor | 20 paketlik ilk yayın, gereksiz public yüzeyi ve support yükünü aynı anda kalıcılaştırabilir | Yüksek | Orta / yüksek | Paket stratejisi ve public API freeze audit | `nuget-danismani` |
| RK-003 | İnceleniyor | Shipped baseline boşken preview tüketicileri kırıcı değişiklik yaşayabilir | Orta | Yüksek / orta | Açık preview compatibility politikası ve release notes | `nuget-danismani` + doküman senkronu |
| RK-004 | İnceleniyor | `NUGET_API_KEY` scope/süre/owner belirsizliği yayın veya supply-chain riski üretir | Yüksek | Bilinmiyor / yüksek | En az yetki, kısa süre, environment protection; resmi yöntem araştırması | Yayın operasyonu |
| RK-005 | İnceleniyor | K-602'nin “19 paket” sayısı güncel 20 paketle drift gösteriyor | Orta | Kesin / düşük-orta | Artifact kümesini doğrula; kalıcı karardaki sayısal ifadeyi gerekiyorsa drift üretmeyecek biçimde güncelle | Karar defteri kuralları |
| RK-006 | İnceleniyor | Meta paketin boş symbol package'i ve CLI'ın 28 MB paketi kapıdan geçiyor, fakat kapı içerik uygunluğunu yargılamıyor | Orta | Kesin / bilinmiyor | Resmi NuGet symbol davranışı ve package content audit | `nuget-danismani` |
| RK-007 | Açık | `ITenantProviderBindingStore` case-sensitivity tutarsızlığı (BL-006) BYOK credential'ının sessizce global setup credential'ına düşmesine yol açabilir — kiracı izolasyonu ihlali | Yüksek | Orta (Postgres/SQLite dağıtımlarında + admin yazım farkı) / yüksek (yanlış kiracının credential'ı kullanılmaz ama yanlış tenant'ın isteği yanlış/paylaşılan credential ile gider) | BL-006 düzeltmesi: canonical case normalizasyonu veya üç katmanda tutarlı ordinal-ignore-case + regresyon testi | `nuget-danismani` → `kusur-giderme` |
| RK-008 | Açık | Drain/yeni-run yarışı (BL-026) `ApplicationStopping` ile aynı ana denk gelen bir run'ın yarıda kesilmesine yol açabilir — zero-downtime deploy varsayımı kırılır | Orta | Düşük (dar pencere) / orta (tek run kaybı, veri bozulması değil ama tutarsız durum) | Register'ı erken taşımak veya reservation adımı; eşzamanlılık testiyle kilitleme | `nuget-danismani` → `kusur-giderme` |
| RK-009 | Açık | Ham exception mesajı sızıntısı **bir sınıf** olarak doğrulandı — `IRunStore.RunError.Message` (BL-027) ve `WorkflowRunner.ToRunError`/`RunEvent.Text` (BL-037) aynı desenin iki bağımsız örneği; `HATA-S3-006`'nın kapattığı sınıfın tekrarı; `secret`/PII sızıntı riski (K-059 ruhuna aykırı) | Yüksek | Orta (provider SDK exception'ları request detayı taşıyabilir) / yüksek (persisted run/workflow kaydı, admin API/UI üzerinden okunabilir) | Her iki yol da `ContentGuardPipeline`'dan geçirilir; sınıf taraması çalıştırma yolundaki (run, workflow, job, webhook) tüm exception→persist noktalarını tek seferde tarar | `nuget-danismani` → `kusur-giderme` (BL-027 + BL-037 birlikte) |
| RK-010 | **Kapandı (Faz 120)** | `IJobHandler`'ın sözleşmesi at-least-once'ı söylemiyordu (BL-041) — dokümante edilen örneği izleyen bir tüketici crash/retry'de side effect'i iki kez çalıştırabilirdi | ~~Yüksek~~ | Orta (lease kaybı/retry production'da olağan) / yüksek (dokümante edilen doğrudan örnek yanlış) | `IJobHandler.cs`'nin XML dokümanına at-least-once uyarısı ve süzülmemiş `Items` notu eklendi; `JobHandlerContract` kuralı kilitliyor, `JobLeaseExpiryTests` davranışı ölçüyor. `IIdempotencyStore`'u job loop'una bağlamak değerlendirilmedi — BL-041'in kapanış notunun gerekçesiyle gereksiz ikinci bir mekanizma olurdu | `nuget-danismani` → `kusur-giderme` → [Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) |
| RK-011 | **Açık** | Asimetrik kısmi yayın: aynı `v*` tag'i NuGet ve npm işlerini paralel tetikler; `agentprism` npm scope'u yok. npm kırılırsa 20 NuGet paketi kalıcı olarak yayınlanmış olur ve sevk edilen doküman var olmayan bir npm paketini tarif eder (OP-010) | Yüksek | Yüksek (scope bugün yok) / yüksek (NuGet'te geri dönüş yok, düzeltme yalnız yeni sürümle) | Tag'den ÖNCE scope + `NPM_TOKEN` hazırlanır, ya da `npm-publish` işi devre dışı bırakılıp docs-site'ın üç sayfası aynı turda düzeltilir | Yayın operasyonu — **kullanıcı kararı ertelendi** |
| RK-012 | **Kabul edildi** | Kişisel owner modeli (OP-001): 20 paketin sahipliği tek hesaba bağlıdır; devir paket başına elle yapılır ve hesap kaybı 20 kimliği birden etkiler | Orta | Düşük / yüksek | Kullanıcı bilinçli olarak kabul etti (2026-08-27). Azaltım: 2FA (OP-002) ve gerekirse sonradan organization'a devir | Yayın operasyonu |

## 10. Yayın checklist'i

### Ürün ve artifact

- [ ] Hedef yayın türü kullanıcı tarafından onaylandı.
- [ ] Hedef sürüm ve prerelease etiketi onaylandı.
- [ ] En küçük güvenli paket kümesi onaylandı.
- [x] Exact sürümlü temiz pack başarılı.
- [x] Üretilen paket kimlik kümesi beklenen kümeyle aynı.
- [ ] Yerel feed ve izole `NUGET_PACKAGES` ile external consumer restore/build başarılı.
- [x] Mevcut beş packed extension sample'ın contract testleri başarılı.
- [ ] Gerçek runtime `run` başarılı.
- [x] Mevcut extension Native AOT smoke publish ve run başarılı.
- [x] Kapının beklediği her TFM assembly ve XML documentation dosyası artifact içinde mevcut.
- [x] `.nuspec` pre-release dependency sınırı doğrulandı.
- [x] Paket README, icon, license, repository ve project URL varlığı doğrulandı.
- [ ] `.snupkg` içerikleri ve Source Link gerçek kaynak çözümleme davranışı doğrulandı.
- [ ] Deterministic/reproducible release ölçüldü.
- [ ] Package validation sonucu incelendi.

### Public contract ve güvenlik

- [ ] 22 sütunlu seam matrisi tamamlandı.
- [ ] Her public yüzey için freeze kararı verildi.
- [ ] Her değişiklik SemVer etkisiyle sınıflandırıldı.
- [ ] Tenant isolation ve BYOK fail-closed davranışı artifact üzerinden ölçüldü.
- [ ] `secret`, connection string, exception, log ve persistence sızıntı probe'ları tamamlandı.
- [ ] HTTP, SSE, MCP, A2A ve OpenAI-compatible transport sözleşmeleri ölçüldü.
- [ ] Authorization, approval, egress, content guard ve output sınırları ölçüldü.
- [ ] Timeout ile caller cancellation ayrımı ölçüldü.
- [ ] Retry ve circuit breaker sözleşmeleri ölçüldü.
- [ ] Canonical serialization ve tool sonucu normalizasyonu ölçüldü.
- [ ] AOT ve trimming iddiaları doğrulandı.

### Dokümantasyon

- [ ] XML documentation ile runtime davranışı hizalı.
- [ ] Package README ile root README hizalı.
- [ ] `docs-site/`, extension sample'ları ve API reference hizalı.
- [ ] Compatibility ve versioning belgeleri yayın politikasını doğru anlatıyor.
- [ ] Package description ve tags güncel.
- [ ] Release notes hazır ve artifact kümesini doğru anlatıyor.

### NuGet.org ve yayın operasyonu

- [ ] NuGet.org hesabı doğrulandı.
- [ ] 2FA etkinliği doğrulandı.
- [ ] Kişisel veya organization owner modeli seçildi.
- [ ] 20 Package ID'nin uygunluğu ve sahiplik planı doğrulandı.
- [ ] API key veya resmi alternatif yöntemin scope, süre ve secret yönetimi onaylandı.
- [ ] CI environment protection ve yayın yetkilendirmesi doğrulandı.
- [ ] License expression, repository URL, project URL, icon, README, authors, owners, tags ve description doğrulandı.
- [ ] Tag stratejisi ve GitHub release akışı onaylandı.
- [ ] Deprecation/yank yaklaşımı onaylandı.
- [ ] Bozuk release için hotfix ve geri dönüş planı onaylandı.
- [ ] Dependency ve vulnerability izleme sorumluluğu onaylandı.
- [ ] İlk 72 saat gözlem ve destek sorumluluğu onaylandı.
- [ ] Gerçek yayın için kullanıcıdan açık onay alındı.

## 11. Karar günlüğü

| Kimlik | Tarih | Durum | Karar | Gerekçe | Doğrulama |
|---|---|---|---|---|---|
| KG-001 | 2026-08-27 | Geçersiz kılındı | Önceki `1.0.0-preview.1` önerisi | Kullanıcı, tüm önceki yayın kararlarının bağlayıcı olmadan yeniden değerlendirilmesini istedi | Yeni yayın hedefi kararı bekleniyor |
| KG-002 | 2026-08-27 | Tamamlandı | Yaşayan kayıt `docs/YAYIN-HAZIRLIK.md` konumunda tutulur | Bu çalışma faz değildir; kökteki tek ledger, faz planı ve karar defteriyle rol çakışması üretmez. Doküman bütçesi içinde kalır | `dokuman-bakim.py --denetle` yeniden koşulacak |
| KG-003 | 2026-08-27 | Geçersiz kılındı | Geçici karar ⚠️ “teknik olarak yayınlanabilir, fakat önerilmez” | Bu sonuç eski hedef varsayımına dayanıyordu | Yeni hedef ve kapsam belirlendikten sonra yeniden karar verilecek |
| KG-004 | 2026-08-27 | Tamamlandı | Önceki yayın kararları bağlayıcı olmadan baştan değerlendirme yapılacak | Kullanıcı talimatı | Bu dosyada tarihsel karar ile ölçülen kanıt ayrımı korunacak |
| KG-005 | 2026-08-27 | Tamamlandı | Yayın türü `preview` olarak sabitlendi (UR-001) | Kullanıcı kararı; shipped baseline boş ve `AgentPrism.AspNetCore` pre-release bağımlılık taşıyor | Sonraki karar grubu: birincil hedef tüketici (UR-002) ve paket kapsamı (BL-002) |
| KG-006 | 2026-08-27 | Tamamlandı | Paket kapsamı tam entegrasyon seti (20 paket) olarak sabitlendi (UR-002, BL-002) | Kullanıcı kararı; dry-run zaten tam kümeyi kanıtlamıştı | Audit yükü şimdi 22 sütunlu seam matrisi (BL-003) ve 4.3/4.4 güvenlik-capability mercekleri üzerinde yoğunlaşacak |
| KG-007 | 2026-08-27 | Tamamlandı | 22 sütunlu seam matrisi 78 seam'in tamamına, tam kapsam ve çok turlu olarak uygulanacak | Kullanıcı kararı; hiçbir seam kapsam dışı bırakılmayacak | §15'teki küme planı ve öncelik sırası; her küme raporu bu dosyaya işlenir |
| KG-008 | 2026-08-27 | Tamamlandı | Küme B ve C ölçüldü: Küme B 1× 🔴 (BL-006, BYOK case-sensitivity) + 3× 🟡 + 2× 🟢 üretti; Küme C 0× 🔴 (BYOK fail-closed tüm adaptörlerde tutarlı) + 2× 🟡 + 2× 🟢 üretti | İki arka plan ajanının bağımsız, file:line kanıtlı ölçümü | BL-006 bir preview blocker'dır — sıradaki küme çalışmasından önce ya da paralel olarak `kusur-giderme`'ye devredilmeli |
| KG-009 | 2026-08-27 | Tamamlandı | Küme E ve F ölçüldü: ikisi de 0× 🔴 üretti (Küme E 4× 🟡 + 1× 🟢; Küme F 4× 🟡 + 3× 🟢). Ajanın önerdiği 2 aday 🔴 (Küme E) Adım 7 filtresiyle 🟡'ye indirildi — gerekçe BL-017/BL-018'de | Bağımsız ölçüm + skill'in kendi Adım 7 seviyelendirme tablosuna karşı elle doğrulama | 4/11 küme tamam (B, C, E, F); A, D, G, H, I, J, K sırada; toplam açık 🔴 hâlâ yalnız BL-006 |
| KG-010 | 2026-08-27 | Tamamlandı | Küme A ve D ölçüldü: Küme A 2× 🔴 (BL-026 drain yarışı, BL-027 `IRunStore` ham exception sızıntısı) + 4× 🟡 + 2× 🟢; Küme D 0× 🔴 (görevin şüphelendiği decorator-sırası ve version-fallback ikisi de fail-closed çıktı) + 4× 🟡 + 2× 🟢 üretti | Bağımsız ölçüm; BL-027 özellikle önemli çünkü aynı sınıf kusur (`HATA-S3-006`) daha önce bir kardeş yolda düzeltilmiş ama burada tekrarlanmış | 6/11 küme tamam (A, B, C, D, E, F); G, H, I, J, K sırada; toplam açık 🔴 sayısı 3 (BL-006, BL-026, BL-027) |
| KG-011 | 2026-08-27 | Tamamlandı | Küme G ölçüldü: 1× 🔴 (BL-037 — BL-027 ile aynı sınıf, `WorkflowRunner`'da tekrarı) + 3× 🟡 + 1× 🟢 | Bağımsız ölçüm; ham exception sızıntısının **iki bağımsız çalıştırma yolunda** bağımsız olarak keşfedilmesi bunu tek vaka değil sınıf yapıyor | 7/11 küme tamam; H, I, J, K sırada; toplam açık 🔴 sayısı 4 (BL-006, BL-026, BL-027, BL-037 — son ikisi tek sınıf) |
| KG-012 | 2026-08-27 | Tamamlandı | Küme H ölçüldü: 1× 🔴 (BL-041 — `IIdempotencyStore` job loop'unda kullanılmıyor) + 4× 🟡 + 2× 🟢 | Bağımsız ölçüm | 8/11 küme tamam; I, J, K sırada; toplam açık 🔴 sayısı 5 |
| KG-013 | 2026-08-27 | Tamamlandı | Küme K elle ölçüldü (ajan gerekmedi, yalnız 2 arayüz) — 0× 🔴, 0× 🟡, bulgu yok | Doğrudan kaynak okuması | 9/11 küme tamam; I, J sırada (arka planda çalışıyor) |
| KG-014 | 2026-08-27 | Tamamlandı | Küme J ve I ölçüldü — ikisi de 0× 🔴 (Küme J 3× 🟡 + 1× 🟢; Küme I 4× 🟡 + 1× 🟢). **11/11 küme tamam.** Toplam: 5 blocker kaydı / 4 bağımsız 🔴 kusur sınıfı (BL-027+BL-037 tek sınıf), 35× 🟡, 17× 🟢 | Bağımsız ölçüm; BL-003 (seam matrisi) artık kapalı | Nihai yayın kararı verilebilir — bkz. §4 |
| KG-015 | 2026-08-27 | Tamamlandı | BL-041 [Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) ile kapandı — `IJobHandler.ExecuteAsync`/`JobContext.Items`'ın XML dokümanı at-least-once'ı ve süzülmemiş item listesini açıkça yazıyor; `JobHandlerContract` (üç yerleşik handler + `AgentPrism.Samples.CustomJobHandler` dış sample'ı) kuralı kilitliyor; `JobLeaseExpiryTests` lease-expiry davranışının gerçekten var olduğunu `InMemoryJobStore` üzerinde ölçüyor. **4/4 preview-blocker kusur sınıfı artık kapalı; açık 🔴 kalmadı** | Faz uygulaması + kendi kendini doğrulayan regresyon kanıtı (bir yerleşik handler'ın skip-kontrolü geçici olarak bozulup contract'ın gerçekten kırmızı verdiği doğrulandı, sonra geri alındı) | Nihai "yayınlanabilir" kararı hâlâ §7 (UR-003 public API freeze) ve §8 (OP-001..009 NuGet.org operasyonu) açık kararlarını bekliyor — bir sonraki `nuget-danismani` turunun konusu |
| KG-016 | 2026-08-27 | Tamamlandı | **UR-003 (public API freeze) 🔴'dan 🟡'ye indirildi ve GA turuna ertelendi**; **OP-001 kişisel hesap olarak sabitlendi**; OP-004/OP-005/OP-010 kullanıcı tarafından ertelendi; **sıradaki iş 35× 🟡 sistemik hat seçildi** (yayın onun arkasına alındı) | UR-003 için üç bağımsız kanıt aynı yöne bakıyor: sevk edilen `versioning.md:11-13`, K-603 ve K-602 — üçü de preview hattında yüzey daralmasını kırıcı saymıyor; Adım 7 filtresinin 3/4/6/7. sorularını geçemiyor. OP-001, OP-004, OP-005, OP-010 ve iş sıralaması kullanıcı kararıdır 👤 | Bu turda ölçülenler: 20/20 NuGet ID müsait (OP-003), release notes artifact'i yok (OP-007), CI GitHub release üretmiyor (OP-006), BL-033 HTTP sızıntısı yeniden üretilemedi (🟡'de kaldı, iddiası daraltıldı), OP-010/RK-011 yeni kaydedildi |
| KG-017 | 2026-08-28 | Tamamlandı | **35× 🟡 hattının kulvar 3'ü (XML sözleşme boşlukları) [Faz 121](121-SEAM-SOZLESME-DOKUMANI.md) ile kapandı.** BL-024, BL-028, BL-029, BL-035, BL-038, BL-042, BL-043, BL-046 tam kapandı; BL-048, BL-050 kısmen kapandı (yalnız doküman boyutu; contract test/registration API kulvar 1/2'de kalır); BL-026 belge kısmı kapandı, muhasebe-hassasiyeti riski değişmedi. **Beklenmedik bulgu:** BL-046'nın "hiçbir shipped yol tetiklemiyor" değerlendirmesi eksikti — `InMemoryAuditLog` (referans implementasyon) null-tenant'ta GERÇEKTEN tüm kiracıları tarıyordu; `kusur-giderme` kapsamına girmeden aynı fazda düzeltildi (K-644) | Bağımsız denetim (taze bağlamlı agent, `faz-denetim`): 0× 🔴, 1× 🟡 (Public API planı ile gerçekleşen arasındaki fark dokümana yazılmalı — bu turda yazıldı), 1× 🟢 (`IAgentSkillStore`'un audit decorator'ı yok — aday listesine) | Kulvar 3'ün geri kalanı (dokunulmayan ~28 arayüzün taban çizgisindeki 174 satırı) `seam-contract-baseline.txt`'de bilinen borç olarak duruyor; sıradaki iş kullanıcı kararına bağlı — kulvar 2/1/4 turu mu, yoksa doğrudan `nuget-danismani`'nin yayın kararı turu mu |

## 12. Ertelenen işler ve gerekçeleri

| Kimlik | Konu | Durum | Gerekçe | Yeniden açılma ölçütü |
|---|---|---|---|---|
| ER-001 | `stable 1.0` freeze | Ertelendi | Preview artifact ve üçüncü taraf feedback kanıtı yok; MAF Hosting/A2A pre-release | Stable ölçütlerinin tamamlanması |
| ER-002 | Gerçek NuGet.org push | Ertelendi | Açık kullanıcı onayı ve operasyon checklist'i yok | Tüm preview blocker'lar kapanır ve kullanıcı onay verir |
| ER-003 | Kapsamlı düzeltme fazı | Ertelendi | Henüz doğrulanmış aksiyon kapsamı yok | Audit ölçülmüş bir iş üretir ve kullanıcı faz açılmasını onaylar |

## 13. Sonraki adım

**Seçim yapıldı (2026-08-27, KG-016): sıradaki iş 35× 🟡 sistemik hattır.**
Yayın teknik olarak açıktı — açık 🔴 yok, UR-003 GA'ya ertelendi — ama kullanıcı
`preview.1`'i bu hattın arkasına aldı.

### Hattın tek cümlelik tanımı

25 blocker kaydı bağımsız 25 iş değildir. Hepsi **aynı** boşluğun örnekleridir:
AgentPrism'in extension seam'leri için **tek bir sözleşme standardı** yoktur.
Repo'da o standardın iki referans örneği zaten var — `IRunJudge` (kayıt üçlüsü +
contract test + startup validation) ve `IRunStore` (tenant-mode tablosu taşıyan
XML dokümanı). İş, bu iki deseni kalan seam'lere yaymaktır.

### Altı kulvar (kapsam)

| # | Kulvar | Kayıtlar | Ölçülen kapsam |
|---|---|---|---|
| 1 | **Reusable contract testi yok** — üçüncü tarafın koşabileceği suite | BL-007, BL-015, BL-017, BL-022, BL-023, BL-030, BL-039, BL-048, BL-050 | **26 arayüz** + shipped 4 provider adaptörünün mevcut `ModelProviderContract`'ı türetmemesi |
| 2 | **Kayıt (registration) API'si yok veya eksik** | BL-008, BL-019, BL-034, BL-050 (+🟢 BL-016) | **~19 arayüz** ham `services.Replace(...)`/`TryAddEnumerable(...)` gerektiriyor ve bu desen hiçbir yerde dokümante değil |
| 3 | **XML sözleşme boşlukları** — davranış doğru, keşfedilebilir değil | BL-024, BL-028, BL-029, BL-035, BL-038, BL-042, BL-043, BL-046, BL-048, BL-050, BL-026 | DI lifetime, tenant-mode, thread-safety, `Stream` sahipliği, at-least-once ve cooperative-cancellation sınırları |
| 4 | **Dış sample yok** (`PackageReference` ile koşan tüketici) | BL-016, BL-019, BL-023, BL-030, BL-036, BL-039, BL-045, BL-048 | Bugün beş sample var; Küme B, F, G, H ve I'nın hiçbirinde yok |
| 5 | **Operasyonel gözlemlenebilirlik** | BL-044, BL-047 | `Scheduling`, `Webhooks`, `Coordination`, `Idempotency`, `Triggers` içinde hiç `ActivitySource`/`Meter` yok; audit-write-failure sayacı yok |
| 6 | **Kod düzeltmesi (doküman değil)** | BL-018, BL-033, BL-039, BL-051 | Decorator hatasının normalize edilmemesi; duplicate workflow adında ham `ArgumentException`; guard/retention kayıtsızken başlangıç uyarısı yok |

### Sıralama gerekçesi

**Kulvar 3 önce gelmelidir** ve tek başına en yüksek kaldıraçlıdır: iki gerçek
hata içerir, ölçüldü —

- **`IAgentDecorator.Order`'ın XML dokümanı gerçek davranışın TERSİNİ söylüyor**
  (BL-034). Doğrulandı: `RunRecordingAgentDecorator.cs:114` `Order=0` ile
  **dıştan**, `ToolApprovalAgentDecorator.cs:38` `Order=20` ile **içten** sarıyor;
  doküman bunun tersini yazıyor. Güvenlikle ilişkili bir decorator'ı yanlış
  katmana koyduran tek kalem budur — sözleşme kusuru olarak diğer 34'ün önündedir.
- **`IAuditLog`/`AuditQuery.TenantId=null`** kiracı sınırını yalnız DTO yorumunda
  taşıyor (BL-046); bugün hiçbir shipped yol tetiklemiyor ama üçüncü taraf
  implementasyon tüm kiracıların kaydını dönebilir.

Sonra **kulvar 2 → kulvar 1 → kulvar 4** birlikte yürür: kayıt API'si olmayan bir
seam'in contract testi de sample'ı da yazılamaz, çünkü tüketicinin yazacağı kod
henüz yoktur. Kulvar 5 ve 6 bağımsızdır, paralel gidebilir.

**Kulvar 3, [Faz 121](121-SEAM-SOZLESME-DOKUMANI.md) ile kapandı (2026-08-28, KG-017).** Her iki
gerekçe kalemi kapandı: `IAgentDecorator.Order` daha önce Faz 120'de (K-642),
`IAuditLog`/`AuditQuery.TenantId=null` bu fazda — hem doküman hem gerçek
implementasyon davranışı olarak (K-644, aşağı bakınız). Sıradaki iş kullanıcı
kararına bağlı: kulvar 2 (kayıt API'si) veya doğrudan `nuget-danismani`'nin
yayın kararı turu.

### Bu hat kapandıktan sonra yayın için kalanlar

Aşağıdakiler bu hattın parçası **değildir**; `preview.1` tag'inden önce ayrıca
kapanmalıdır:

1. **OP-007 release notes hattı** — ölçüldü: hiçbir artifact yok (`CHANGELOG.md`
   yok, `PackageReleaseNotes` tanımsız, changelog sayfası yok) ve CI **GitHub
   release üretmiyor** (OP-006).
2. **OP-010 / RK-011** — npm scope'u tag'den önce hazırlanmalı, yoksa asimetrik
   kısmi yayın oluşur. Kullanıcı kararı ertelendi.
3. **OP-002, OP-004, OP-005, OP-008** — kullanıcının kendi hesap/organizasyon
   tercihleri. OP-004/OP-005 ertelendiği sürece RK-004 açık kalır.
4. **Doküman drift taraması** (Adım 6) → `tuketici-dokuman-senkronu`.

### GA turuna ertelenenler

- **UR-003 public API freeze taraması** — 680 tip, `Unshipped → Shipped`
  dolumuyla aynı turda (KG-016).
- **RK-005** — K-602'nin "19 paket" ifadesi güncel 20 ile drift gösteriyor;
  `docs-site` doğru sayıyı yazıyor, düzeltme yalnız karar defteri metnindedir.

## 14. Yayın sonrası ilk 72 saat planı

| Zaman | Durum | Plan |
|---|---|---|
| Yayın öncesi | Karar gerekli | Support kanalı, sorumlu kişi, telemetry ve package health gözlem yüzeyleri seçilir |
| 0–2 saat | Bekliyor | NuGet.org paket sayfaları, dependency graph, README/icon/license, symbol görünürlüğü ve temiz makinede install doğrulanır |
| 2–24 saat | Bekliyor | Restore/build/runtime sorunları, issue'lar, dependency ve security uyarıları izlenir; doğrulanmış kritik kusurda yeni indirmeler için deprecation değerlendirilir |
| 24–48 saat | Bekliyor | İlk tüketici geri bildirimi public API, docs ve extension ergonomisi sınıflarına ayrılır; preview compatibility etkisi yazılır |
| 48–72 saat | Bekliyor | Patch/sonraki preview kararı verilir; release retrospective ve risk kaydı güncellenir |

Bozuk yayın silinebilir varsayılmaz. NuGet.org üzerinde kalıcı artifact mantığı
esas alınır. Düzeltme yeni sürümle yapılır; deprecation ve yönlendirme kararı
olayın etkisine göre verilir.

## 15. Seam envanteri ve matris planı (BL-003)

Kullanıcı kararı: 22 sütunlu matris **tam kapsam, çok turlu** çalışır (bkz.
KG-007). `src/AgentPrism.Abstractions` tek başına **76** public interface,
diğer paketlerde **2** daha (`IAgentPrismUiProvider`, `IAgentPrismBuilder`)
taşıyor — toplam **78** aday seam. Skill'in örnek listesindeki 9 kategoriden
çok daha geniş. Matris interface başına değil **küme başına** doldurulur:
kümenin temsilci üyesi tam derinlikte ölçülür, kümenin geri kalanı sütun 1-2
ve dokümantasyon tutarlılığı için taranır; sapma bulunursa o üye ayrıca
derinleştirilir.

### Küme planı

| Küme | Kapsam | Üye sayısı | Öncelik | Durum |
|---|---|---|---|---|
| A | Runs & Observability: `IRunStore`, `IRunScoreStore`, `IRunInputStore`, `IRunEventSink`, `IRunAttributionContext`, `IRunCancellationRegistry`, `IRunErrorClassifier`, `IRunPricingResolver`, `ITraceStore`, `IAgentPrismDrainState` | 10 | Yüksek — çekirdek çalıştırma yolu | **Tamamlandı** — 2× 🔴, 4× 🟡, 2× 🟢 (§6) |
| B | Tenancy, Security, Privacy: `ITenantContext`, `ITenantStore`, `ITenantEgressPolicyStore`, `ITenantProviderBindingStore`, `IApiKeyStore`, `IContentProtector`, `IDataSubjectStore`, `IDataSubjectResolver`, `IQuotaStore` | 9 | En yüksek — `secret`/kiracı sızıntı riski | **Tamamlandı** — 1× 🔴 (BL-006), 3× 🟡, 2× 🟢 (§6, §9) |
| C | Model Provider & Retry (BYOK): `IModelProvider`, `ITenantCredentialModelProvider`, `IModelProviderHealthCheck`, `IModelProviderRegistry`, `IProviderRetryClassifier`, `IModelProviderConfigurationDiagnostics` | 6 | En yüksek — capability fail-closed (4.4) | **Tamamlandı** — 0× 🔴, 2× 🟡, 2× 🟢 (§6) |
| D | Agents: `IAgentSource`, `IVersionedAgentSource`, `IAgentDecorator`, `IAgentCatalog`, `IAgentDefinitionStore`, `IAgentSkillStore`, `ISkillScriptGrantStore` | 7 | Yüksek | **Tamamlandı** — 0× 🔴, 4× 🟡, 2× 🟢 (§6) |
| E | Tools, Guards, Approvals: `IToolRegistry`, `IToolAuthorizationHandler`, `IContentGuard`, `IPendingApprovalStore`, `IToolApprovalRuleStore` | 5 | Yüksek — güvenlik sınırı (4.3) | **Tamamlandı** — 0× 🔴, 4× 🟡, 1× 🟢 (§6) |
| F | MCP / Transport: `IMcpOAuthCoordinator`, `IMcpPromptClient`, `IMcpResourceClient`, `IMcpResourceContextProviderFactory`, `IMcpToolRefresher`, `IMcpServerStore` | 6 | Yüksek — wire contract | **Tamamlandı** — 0× 🔴, 4× 🟡, 3× 🟢 (§6) |
| G | Workflows: `IWorkflowRunner`, `IWorkflowDefinitionStore`, `IWorkflowCheckpointStore`, `IWorkflowFunctionCatalog` | 4 | Orta | **Tamamlandı** — 1× 🔴 (sınıf tekrarı, BL-027 ile aynı kök neden), 3× 🟡, 1× 🟢 (§6) |
| H | Scheduling, Coordination, Idempotency, Webhooks, Triggers: `IJobStore`, `IJobHandler`, `IJobScheduleStore`, `ISingletonLeaseStore`, `IIdempotencyStore`, `IWebhookStore`, `IWebhookPublisher`, `IInboundTriggerStore` | 8 | Orta | **Tamamlandı** — 1× 🔴, 4× 🟡, 2× 🟢 (§6) |
| I | Sessions, Attachments, Retention, Evaluation, Experiments, Knowledge: `ISessionStore`, `IConversationBranchStore`, `IAttachmentStore`, `IAttachmentStorage`, `IRetentionStore`, `IRetentionPolicyStore`, `IArchiveSink`, `IEvalStore`, `IRunJudge`, `IExperimentStore`, `IMigrationApplier`, `ISqlPersistenceDiagnostics`, `IVectorSearchStore` | 13 | Orta | **Tamamlandı** — 0× 🔴, 4× 🟡, 1× 🟢 (§6) |
| J | Voice & Audit: `ISpeechSynthesizer`, `ISpeechTranscriber`, `IVoicePricingReader`, `IVoiceHealthCheck`, `IVoiceSessionStore`, `IAuditLog`, `IAuditActorResolver`, `IAuditDecorated` | 8 | Orta | **Tamamlandı** — 0× 🔴, 3× 🟡, 1× 🟢 (§6) |
| K | Builder/UI surfaces: `IAgentPrismUiProvider`, `IAgentPrismBuilder` | 2 | Düşük | **Tamamlandı** (elle ölçüldü, ajan gerekmedi) — 0× 🔴, 0× 🟡, bulgu yok |

**Sıra gerekçesi:** B ve C önce — kiracı/`secret`/BYOK sızıntısı ve capability
fail-closed ihlali preview'da bile 🔴 üretebilecek tek iki alan. E ve F hemen
ardından — tool authorization ve transport wire contract'ı sonradan kırmak
pahalı. A (çalıştırma yolu) ve D (agent source) sonra. G, H, I, J, K en
sonda — ölçülmüş kanıt bugüne kadar bu kümelerde bilinen bir kusur riski
göstermiyor.

Her küme tamamlandığında bu tablo güncellenir ve bulgular §6 (blocker) veya §9
(risk)'e taşınır. Matrisin ham hücreleri bu dosyada değil, kümeyi ölçen ajanın
raporunda tutulur; yalnız **bulgu** (tutarsızlık, dokümansız davranış,
fail-open) buraya girer — 78×22 boş matrisi doğrudan bu dosyaya basmak
doküman bütçesini anlamsızca şişirir.

### Küme B ve C sonuçları (2026-08-27)

**Küme C (Model Provider/BYOK) temiz çıktı:** `ModelProviderRegistry.BuildPipeline`
(`src/AgentPrism.Core/Models/ModelProviderRegistry.cs:396-402`), bir kiracı
credential'ı çözülmüş ama provider `ITenantCredentialModelProvider` değilse
sessiz global-credential düşüşü yerine `ProviderInvocationException.CredentialUnsupported`
fırlatıyor — dört adaptörün (Anthropic/Azure/Google/OpenAI) tamamında tutarlı,
dedicated testle kilitli (`ModelProviderRegistryTenantCredentialTests.cs:348`).
4.4 mercek için bu kümede 🔴 yok.

**Küme B (Tenancy/Security/Privacy) 1× 🔴 üretti:** `ITenantProviderBindingStore`
BYOK lookup'ı üç farklı case-sensitivity davranışı taşıyor (bkz. BL-006) —
tam olarak `ModelProviderRegistry`'nin kendi yorumunun reddettiği "sessiz
global credential düşüşü" senaryosunu üretebilir. Diğer sekiz seam temiz;
`IDataSubjectResolver`'ın kayıtsız durumda `409` dönmesi kümenin en iyi
fail-closed örneği olarak ölçüldü.

Bulguların tamamı §6'ya işlendi: BL-006 (🔴), BL-007/BL-008/BL-015 (🟡),
BL-016 (🟢, toplu).

### Küme E ve F sonuçları (2026-08-27)

**Küme E (Tools/Guards/Approvals) 0× 🔴 üretti — ajanın önerdiği iki 🔴 aday
(`kusur-giderme`'ye değil) Adım 7 filtresinden geçirilip 🟡'ye indirildi:**
"contract test eksikliği" skill'in kendi Adım 7 tablosunda açıkça 🟡
kategorisidir (`Executable contract eksikliği`), 🔴 değil; `IContentGuard`'ın
kayıtsızken fail-open olması ise **dokümante edilmiş, kasıtlı bir varsayılan**
— BL-006'nın aksine dokümante sözle çelişmiyor, yalnız operasyonel bir uyarı
eksik. CLAUDE.md'nin "Tool'lar yalnızca kodda tanımlanır" iddiası kodda
doğrulandı: `AgentDefinitionCompiler.ChatOptions.cs:78` veri/JSON kaynaklı tool
kaydını açıkça reddediyor.

**Küme F (MCP/Transport) 0× 🔴 üretti — temiz.** Kiracı izolasyonu OAuth token
cache'i için `McpTenantServerKey` ile yapısal olarak kilitli (önceki bir
string-interpolation sızıntısını kapatmak için özel olarak yazılmış);
kaynak/resource okumaları sunucunun kendi bildirdiği URI listesiyle
sınırlanıyor (SSRF koruması); OAuth token yenileme başarısızlığında sessiz
stale-token kullanımı yok, `fail-closed`. En büyük açık: `IMcpOAuthCoordinator`
— kümenin en yüksek riskli sınıfı — hiçbir testte referans edilmiyor.

Bulguların tamamı §6'ya işlendi: BL-017/BL-018/BL-019/BL-022/BL-023/BL-024
(🟡), BL-021/BL-025 (🟢, toplu).

### Küme A sonuçları (2026-08-27)

**Küme A (Runs/Observability) 2× 🔴 üretti — bu tur en ciddi kümesi:**

1. **Drain/yeni-run yarışı** — `AgentEndpoints.cs:193-196`'daki `DrainGate.Check`
   kontrolü, `IRunCancellationRegistry.Register`'ın gerçekten çağrıldığı
   `RunRecordingAgent.cs:214-218`'den **önce**, ama body binding/attribution/
   quota/preflight/catalog resolution'dan **sonra** çalışıyor. `ApplicationStopping`
   tam bu pencerede tetiklenirse `AgentPrismDrainService.StopAsync` `ActiveCount==0`
   görüp hemen dönebilir — süreç, başlamak üzere olan bir run'ı yarıda
   kesebilir. Kanıt seviyesi kaynak izleme (Seviye 1-2); yarışı fiilen tetikleyen
   bir eşzamanlılık testi yok — `kusur-giderme`'ye devredilirken bu da istenmeli.
2. **`IRunStore`'a ham exception mesajı sızıyor** — `RunRecordingAgent.Completion.cs:282`
   `RunError.Message = exception.Message`'ı `ContentGuardPipeline`'dan
   **geçirmeden** yazıyor. Kardeş yol `IRunInputStore` tam olarak bu sınıf
   bir kusur için (`HATA-S3-006`) daha önce düzeltilmiş ve guard'dan geçiriliyor
   (`RunRecordingAgent.Persistence.cs:36-41`) — aynı düzeltme run-error yoluna
   uygulanmamış. Bu, K-059'un `secret` disiplini ruhuna doğrudan aykırı bir
   sınıf tekrarı örneği.

Diğer bulgular: cancellation cooperative-only ama arayüz dokümanında bu sınır
belirtilmiyor (🟡); tenant-mode dokümantasyonu yalnız `IRunStore`'da tam,
`ITraceStore`'un ambient-tenant davranışı dokümante değil (🟡); 6 arayüz için
contract test yok, 8/10 için dış sample yok (🟡, BL-008 ile aynı repo-geneli
kayıt ergonomisi deseni tekrar gözlendi — ayrı kayıt açılmadı). Bulguların
tamamı §6'ya işlendi: BL-026/BL-027 (🔴), BL-028/BL-029/BL-030 (🟡), BL-031
(🟢, toplu).

### Küme D sonuçları (2026-08-27)

**Küme D (Agents) 0× 🔴 üretti.** Görevin şüphelendiği iki en kritik davranış —
decorator kompozisyon sırası ve version-not-found — ikisi de ölçümde
fail-closed çıktı: sıralama framework tarafından sabit (`OrderByDescending`,
consumer DI kaydıyla değiştiremez), olmayan bir agent versiyonu sessizce
"latest"e düşmüyor, `AgentPrismException` fırlatıyor. En dikkat çekici bulgu
`IAgentDecorator.Order`'ın XML dokümanının **kendi kendiyle çelişmesi** —
"lower value wraps inside, higher value wraps outside" cümlesi gerçek
davranışın (düşük = dıştan, yüksek = içten; `RunRecordingAgentDecorator`
Order=0/dıştan, `ToolApprovalAgentDecorator` Order=20/içten) tam tersini
söylüyor. Güvenlik-ilişkili bir decorator yazan üçüncü taraf bu cümleye
güvenirse yanlış katmana yerleştirebilir — runtime doğru, doküman yanlış.

Diğer bulgular: decorator exception'ları source exception'larının aksine
normalize edilmiyor, ham exception ASP.NET Core'un varsayılan handler'ına
kadar sızabilir (doğrulanmadı, derinleştirme gerekiyor); `IAgentDecorator`
için hiç builder API'si yok (yalnız ham `TryAddEnumerable`); `IAgentDefinitionStore`
ambient tenant kullanırken `IAgentSkillStore`/`ISkillScriptGrantStore` explicit
`tenantId` parametresi kullanıyor — aynı kümede tutarsız desen. Bulguların
tamamı §6'ya işlendi: BL-033/BL-034/BL-035 (🟡), BL-036 (🟢, toplu).

### Küme G sonuçları (2026-08-27)

**Küme G (Workflows) 1× 🔴 üretti — ve bu BL-027'yle AYNI kusur sınıfının
ikinci örneği:** `WorkflowRunner.ToRunError` (`WorkflowRunner.cs:1179-1198`)
yakaladığı exception'ın ham `.Message`'ını `RunEvent.Text`'e **hiçbir
guard'dan geçirmeden** yazıyor — `IRunStore`'daki `RunError.Message` sızıntısıyla
(BL-027) birebir aynı desen, farklı bir çalıştırma yolunda. Bu, CLAUDE.md'nin
kendi tuzak kaydının tarif ettiği "aynı kusur sınıfı defalarca tekrarladı"
örüntüsünün tam bu turda yakalanmış hâli — `kusur-giderme`'nin SINIF TARAMASI
adımı bu ikisini birlikte kapatmalı, ayrı ayrı değil.

Ayrıca: workflow resume'un side-effecting adımları **tekrar çalıştırabileceği**
(at-least-once semantics) yalnız `AddWorkflowFunction<T>()`'ın XML dokümanında
(`AgentPrismWorkflowFunctionExtensions.cs:66-77`) anlatılıyor — bu bilginin asıl
karşılığı olması gereken `IWorkflowRunner`/`IWorkflowCheckpointStore`
(`AgentPrism.Abstractions`, paketin asıl public sözleşme yüzeyi) bundan hiç
bahsetmiyor. Davranışın kendisi doğru ve kasıtlı (tool'lardaki `SafeToRepeat`
deseniyle tutarlı), yalnız yanlış dosyada dokümante — 🟡.

Bulguların tamamı §6'ya işlendi: BL-037 (🔴, BL-027 ile bağlantılı), BL-038/
BL-039 (🟡), BL-040 (🟢, toplu).

### Küme H sonuçları (2026-08-27)

**Küme H (Scheduling/Coordination/Idempotency/Webhooks/Triggers) 1× 🔴
üretti:** `IIdempotencyStore` — kendi dokümanına göre "özellikle bu iş için"
var olan tip — job dispatch loop'unda (`JobWorkerBackgroundService.cs`) hiç
kullanılmıyor. `IJobHandler.cs`'nin kendi örneği (`NightlyReportJobHandler`)
lease kaybı/retry sonrası `context.Items`'ın tamamen yeniden geleceğini ve
zaten `Completed` item'ların da geleceğini söylemiyor — built-in handler'lar
(`AgentBatchJobHandler`, `WorkflowJobHandler`, `EvalJobHandler`) bunu savunmacı
`item.Status != Pending` kontrolüyle örtük olarak çözüyor ama bu **sözleşme
değil, kabile bilgisi**. Dokümante edilen örneği harfiyen izleyen bir
tüketici crash/lease-kaybı/retry'de side effect'i (örn. e-posta) iki kez
gönderir.

Diğer bulgular: `ISingletonLeaseStore`'un lease-sahibi donduğunda oluşan
sınırlı split-brain penceresi dokümante değil; webhook teslimatı at-least-once
ama arayüz dokümanında belirtilmiyor (dedup anahtarı yalnız kaynak kodunda
görülebiliyor); kümede hiç `span`/metric yok; 6 store arayüzünün lifetime/
thread-safety dokümantasyon eksikliği zaten bilinen tekrarlayan desenin
(BL-024/BL-029/BL-039) bir örneği daha — ayrı kayıt açılmadı.

Bulguların tamamı §6'ya işlendi: BL-041 (🔴), BL-042/BL-043/BL-044 (🟡),
BL-045 (🟢, toplu).

### Küme K sonuçları (2026-08-27, elle ölçüldü)

**Küme K (Builder/UI) 0× 🔴, 0× 🟡 üretti — tek düşük öncelikli, gerçekten
temiz küme.** `IAgentPrismBuilder`'ın kendisi bu ölçümde görülen en iyi
dokümante edilmiş arayüz — her metotta thread-safety notu, AOT annotasyonu
(`[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` doğru yerlerde) ve
örnek kod var. `AddModelProvider`'ın generic `<T>()` overload'ı olmadığı
burada da doğrulandı (BL-016'nın parçası, yeni kayıt açılmadı).
`IAgentPrismUiProvider` da güçlü dokümante — `HasAssets=false` durumunda
boş sayfa yerine `404` dönmesi (build-time varlık eksikliğinde) kasıtlı ve
dokümante bir fail-safe. Contract test taban sınıfı yok ama bu arayüz zaten
E2E/functional testlerle (`AgentPrism.Ui.E2ETests`, `AgentPrism.AspNetCore.FunctionalTests/SecurityTests.cs`)
kapsanıyor — HTTP sunan bir seam için makul bir seçim, ayrı bulgu açılmadı.

### Küme J sonuçları (2026-08-27)

**Küme J (Voice/Audit) 0× 🔴 üretti.** `IAuditLog`'un fail-open tasarımı
(audit write başarısız olursa asıl işlem yine de tamamlanır) doğrulandı ve bu
**sessiz değil** — `AuditRecorder.WriteAsync` her başarısızlığı loglar.
`AuditSecretFilter.Redact` tüm decorator'ları tek bir merkezi noktadan
geçiriyor, atlanamaz. En yakın 🔴 adayı (`AuditQuery.TenantId=null` →
`InMemoryAuditLog`'da tüm kiracıları dönebilme) bugün hiçbir shipped kod
yolunda tetiklenmiyor (`AuditEndpoints.cs` her zaman tenant'ı açıkça geçiyor)
— 🟡'ye indirildi.

Bulguların tamamı §6'ya işlendi: BL-046/BL-047/BL-048 (🟡), BL-049 (🟢, toplu).

### Küme I sonuçları (2026-08-27) — SON KÜME

**Küme I (Sessions/Attachments/Retention/Evaluation/Experiments/Knowledge)
0× 🔴 üretti.** Üç en olası 🔴 adayı — attachment'ta path traversal, judge
başarısızlığında sessiz geçer not, cross-tenant sızıntı — üçü de temiz
çıktı: attachment fiziksel anahtarı her zaman sunucu üretimli GUID (dosya adı
yalnız metadata), `IRunJudge` parse hatasında `Score: null` yazıyor (asla
sessiz `0` değil, "a silent 0 is NOT written" yorumuyla açıkça belgelenmiş),
`EvalRunQuery.TenantId` K-277 sonrası `required` (önceden nullable="tüm
kiracılar" tuzağıydı, kasıtlı düzeltilmiş). `IRunJudge` kümenin referans
deseni: tam kayıt üçlüsü, contract test hem built-in hem dış
`CustomRunJudge` sample'ında koşuyor — diğer 10 arayüz bu deseni taşımıyor.

Bulguların tamamı §6'ya işlendi: BL-050 (🟡), BL-051 (🟢).

---

## 11/11 küme tamam — özet

| Küme | 🔴 | 🟡 | 🟢 |
|---|---|---|---|
| A (Runs/Observability) | 2 | 4 | 2 |
| B (Tenancy/Security/Privacy) | 1 | 3 | 2 |
| C (Model Provider/BYOK) | 0 | 2 | 2 |
| D (Agents) | 0 | 4 | 2 |
| E (Tools/Guards/Approvals) | 0 | 4 | 1 |
| F (MCP/Transport) | 0 | 4 | 3 |
| G (Workflows) | 1 | 3 | 1 |
| H (Scheduling/Coordination/Idempotency/Webhooks/Triggers) | 1 | 4 | 2 |
| I (Sessions/Attachments/Retention/Evaluation/Knowledge) | 0 | 4 | 1 |
| J (Voice/Audit) | 0 | 3 | 1 |
| K (Builder/UI) | 0 | 0 | 0 |
| **Toplam** | **5 kayıt / 4 bağımsız kusur sınıfı** | **35** | **17** |

BL-027 ve BL-037 tek bir kusur sınıfının iki bağımsız örneği (ham exception
mesajının `ContentGuardPipeline`'dan geçirilmeden persist edilmesi) —
`kusur-giderme`'ye tek sınıf taraması olarak birlikte gider. Kalan üç 🔴
(BL-006, BL-026, BL-041) birbirinden bağımsız.

**En dikkat çekici cross-cluster örüntü:** `IRunStore` (Küme A) ve `IRunJudge`
(Küme I) bu ölçümde görülen iki referans-kalite arayüz — tam dokümantasyon,
tam kayıt üçlüsü, gerçek contract test + dış sample. Geri kalan ~74 arayüzün
çoğu aynı bar'a ulaşmıyor: kayıt ergonomisi (dedicated `AddX()` yok),
singleton/thread-safety dokümantasyonu ve contract test/sample kapsamı
tutarlı biçimde eksik. Bu, tek tek düzeltilecek 30+ ayrı 🟡 değil, **tek bir
sistemik desen** — `IRunJudge`'ın kayıt+contract+sample üçlüsü şablon
alınarak kalan arayüzlere uygulanabilir.

---

## 16. Sınıf taraması: ham exception → kalıcı/dışa açık durum (BL-027 · BL-037)

> **✅ Kapandı — [Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md), K-640 (2026-08-27).**
> Aşağıdaki 21 vaka artık `AgentPrism.SafeErrorText` üzerinden geçiyor; uygulama
> sırasında 5 ek vaka daha bulundu ve kapatıldı (`EgressAddressValidator`,
> `ConversationBranchService`, `RetentionJobHandler`, `RetentionExecutor`,
> `ModelRunJudge`) — toplam 26. Bu bölüm artık **tarihsel kanıt kaydı**dır,
> güncel durum için Faz 119 dokümanına bakın.

`kusur-giderme` Adım 5 uygulandı. Denetim iki vaka bildirmişti; tarama **19 vaka
daha** buldu. Kusur artık "iki satırı guard'dan geçir" değil, **sistemik bir
kapsam sorunudur**: `ContentGuardPipeline`'ın `src/` içinde yalnız iki tüketicisi
var — `ContentGuardingChatClient` (model giriş/çıkışı) ve `HATA-S3-006`
düzeltmesi (`RunRecordingAgent.Persistence.cs:41-46`). Kalıcılaştıran veya dışa
gönderen **başka hiçbir yol** guard'dan geçmiyor.

### Kalıcılaştıran vakalar (12)

| file:line | Giren metin | Nereye düşüyor |
|---|---|---|
| `Recording/RunRecordingAgent.Notifications.cs:138` | bilinen vaka 1'in aynı `RunError`'ı | `WebhookRunSummary.Error` → `webhook_deliveries.payload` **ve kiracının tanımladığı dış URL'ye POST edilir** — kutudan çıkıyor |
| `Scheduling/AgentRunJobHandler.cs:265` | kuyruklu run'ın provider SDK exception'ı | `runs.error_message` |
| `Scheduling/RunContinuationJobHandler.cs:200` | aynı | aynı |
| `Approvals/ApprovalResumeJobHandler.cs:174` | aynı | aynı |
| `Webhooks/WebhookDeliveryJobHandler.cs:327` | **uzak webhook hedefinin ham HTTP gövdesi** (`$"HTTP {status}: {body}"`) | `webhook_deliveries.error` — tamamen üçüncü taraf kontrolünde |
| `Webhooks/WebhookDeliveryJobHandler.cs:335` | `HttpRequestException.Message` (host:port taşır) | aynı |
| `Scheduling/JobWorkerBackgroundService.cs:269` | **her** handler'ın exception'ı (AgentRun · Eval · Webhook · Workflow) | `jobs.error_message` — kodun en geniş hunisi |
| `Scheduling/JobWorkerBackgroundService.cs:280` | aynı | `ReleaseForRetryAsync(...)` |
| `Scheduling/AgentBatchJobHandler.cs:84` | provider SDK exception | `job_items.error` |
| `Scheduling/WorkflowJobHandler.cs:87` | aynı (`:70` ayrıca bilinen vaka 2'nin çıktısını kopyalar) | aynı |
| `Evaluation/EvalJobHandler.cs:351` | `agent.RunAsync` exception'ı | `eval_case_results.failure_reason` |
| `Recording/RunReconciliationService.cs:328` | job-store exception (iç kaynaklı — daha düşük) | `runs.error_message` |

### Dışa açık vakalar (8)

Hepsi, kendi yorumları "provider SDK exception'ları ortak bir taban tip
paylaşmadığı için" geniş `catch (Exception)` olduğunu söyleyen bloklarda —
sonra o exception'ın mesajını çağırana aynen yazıyorlar.

| file:line | Sink |
|---|---|
| `AgentEndpoints.cs:1135` | SSE `error` frame |
| `AgentEndpoints.cs:1233` | 502 `ProblemDetails.detail` |
| `OpenAICompat/OpenAIChatCompletionsEndpoints.cs:163`,`:318` | 502 gövde / SSE hata gövdesi |
| `OpenAICompat/OpenAIResponsesEndpoints.cs:237`,`:397` | 502 gövde / SSE hata |
| `RunEndpoints.cs:603` | replay 502 `detail` |
| `ImageEndpoints.cs:169` | 502 `detail` (`:157` `HttpRequestException` için doğrusunu yapıyor — `:169` sızıntı) |
| `McpServer/CatalogToolCallHandler.cs:106` | MCP `CallToolResult` hata metni (BL-025'te kayıtlıydı, doğrulandı) |
| `Mcp/McpOAuthAuthorizationCoordinator.cs:253` | `McpOAuthCompleteResult.Error` → `GovernanceEndpoints.cs:84` → **HTML sayfasına basılır**; bu uç **bearer auth'tan muaf** (`GovernanceEndpoints.cs:40`) ve token-exchange bacağı `client_secret` yankılayabilir |

OpenAI-uyumlu yüzey en açık olanı: dış OpenAI istemcileri için drop-in olarak
tasarlandı.

### Doğru desenin repo içindeki emsalleri

Düzeltme sıfırdan tasarlanmayacak — repo bunu üç yerde zaten doğru yapıyor:
`ToolFailureText.cs:6-11` (yalnız tip adı), `ElevenLabsSpeechClient.cs:637-659`
(provider gövdesi okunur ve **atılır**), dört provider health check
(`OpenAIProviderHealthCheck.cs:84` vd. — `exception.Message`'ı host:port taşıdığı
için açıkça reddeder). `RunTraceCollector.cs:282-286` farklı ama gerçek bir
guard taşır (`IsSensitive("error.message")` + `RecordSensitiveData`).

### En yüksek kaldıraçlı üç düzeltme

1. `JobWorkerBackgroundService.cs:269,280` — tek nokta, **her** job türünü kapatır.
2. `RunRecordingAgent.Notifications.cs:138` — ham provider metninin kutudan çıktığı tek yol.
3. `WebhookDeliveryJobHandler.cs:327` — uzaktan kontrol edilen gövdenin kalıcılaştığı yer.

### Yargı gerektiren, ölçülmesi gereken 5 kalem — Faz 119 kapanış kararı

`RetentionJobHandler.cs:35` · `RetentionExecutor.cs:202` (Npgsql mesajı SQL metni
taşıyabilir) · `WorkflowRunner.cs:890` ← `WorkflowResponseFactory.cs:106`
(kullanıcı girdisi kaynaklı `JsonException`) · `ModelRunJudge.cs:255` →
`run_scores.comment` (model çıktısı kaynaklı) · `WorkflowEndpoints.cs:636` ve
`VoiceConversationDriver.cs:554` (dar filtre; yalnız `HttpRequestException` kolu
host:port sızdırır).

**Sonuç (2026-08-27):**

| Kalem | Karar | Gerekçe |
|---|---|---|
| `RetentionJobHandler.cs:35` | Düzeltildi | `SafeErrorText` + yeni opsiyonel `ILogger<RetentionJobHandler>` |
| `RetentionExecutor.cs:202` | Düzeltildi | `SafeErrorText` + mevcut `ILogger<RetentionExecutor>` |
| `ModelRunJudge.cs:255` | Düzeltildi | `ParseJudgment` artık `ILogger` alıyor; `SafeErrorText` uygulanıyor |
| `WorkflowEndpoints.cs:636` + `VoiceConversationDriver.cs:554` | Düzeltildi | `AgentPrismException` kolu korunur, `HttpRequestException`/`InvalidOperationException` kolu `SafeErrorText`'e yönlendirildi |
| `WorkflowRunner.cs:890` ← `WorkflowResponseFactory.cs:106` | **Kapsam dışı bırakıldı (gerekçeyle)** | `WorkflowRunner.cs:890`'daki `catch (AgentPrismException exception)` zaten kural #1'i sağlıyor (mesaj bizim). Asıl soru `WorkflowResponseFactory.cs:106`'nın kendi `AgentPrismException`'ının mesajına bir iç `JsonException.Message` gömmesi — ama bu, workflow'u DEVAM ETTİRMEK için cevap gönderen AYNI çağrının KENDİ gönderdiği bozuk JSON'ı açıklıyor (self-referential doğrulama geri bildirimi, `OpenAIResponsesEndpoints.HandleAsync`'in istek gövdesi ayrıştırma hatasıyla aynı desen — bkz. `raw-exception-text-baseline.txt`). Host/credential/altyapı detayı taşımaz |

Ayrıca sınıf taraması bu 21+5 kalemin ÜSTÜNE **5 vaka daha** buldu (uygulama
sırasında, mimari cırcır kapısı + elle inceleme ile): `EgressAddressValidator.cs:290`
(DNS/argüman hatası — düzeltildi, yalnız tip adı tutulur; paylaşılan statik
sınıfa `ILogger` eklemek üç çağıran yüzeyi ölçüsüz büyütür, bu yüzden korelasyon
kimliği YOK — SocketException/ArgumentException mesajı zaten yalnız çağıranın
KENDİ verdiği host adını anlatır, `secret`/host:port taşımaz), `ConversationBranchService.cs:146`
(düzeltildi, yeni opsiyonel `ILogger<ConversationBranchService>`),
`RetentionJobHandler.cs`, `RetentionExecutor.cs`, `ModelRunJudge.cs` (üçü de
yukarıda). Toplam kapatılan vaka: **26**.

**Mimari cırcır kapısının bilinen kapsam sınırı:** `RawExceptionTextSiteTests`
yalnız `catch (Exception` (isimsiz/genel) şeklini tarar; `catch (HttpRequestException`
gibi isimli bloklar kapsam dışıdır — bu fazda ELLE incelendi ve gerekliyse
düzeltildi, ama gelecekte isimli bir catch'te YENİ bir sızıntı açılırsa kapı
onu YAKALAMAZ. Kabul edilen bir sınır (K-640); genişletme ayrı bir kalem.
