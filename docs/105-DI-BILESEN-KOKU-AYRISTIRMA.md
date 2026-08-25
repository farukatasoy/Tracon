# Faz 105 — DI Bileşen Kökü Ayrıştırma

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. Mevcut iki `AddAgentPrism` ve `UseScheduling` imzası değişmez. `EnablePublicApiTracking` açıktır (K-421); `PublicAPI.Shipped.txt` girdisi bugün **0**
> **Tüketici yüzeyi:** Yok. Public imza, XML metni, HTTP ucu, ekran ve sevk edilen yapılandırma anahtarı değişmez
> **Manuel test alanı:** [`manuel-test/01-KURULUM-VE-PAKETLEME.md`](manuel-test/01-KURULUM-VE-PAKETLEME.md) · [`manuel-test/02-CEKIRDEK-VE-KATALOG.md`](manuel-test/02-CEKIRDEK-VE-KATALOG.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız ilgili satırlar:
   ```bash
   grep -n "K-021\|K-421" docs/KARARLAR.md
   ```
   K-021 yapılandırmayı yansımasız ve elle bağlar. K-421 public API takibini açık tutar.
3. Alan hafızası: [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) ve [`hafiza/build-ve-analyzer.md`](hafiza/build-ve-analyzer.md)
4. Mimari: [`MIMARI.md`](MIMARI.md) — yalnız `AgentPrism.Core` ve DI kayıt akışı

---

## Amaç

`AgentPrismServiceCollectionExtensions.cs`, DI kayıtlarını ve 40'tan fazla options bağlayıcısını aynı dosyada tutuyor. Faz, bu sınıfı public yüzeyi değiştirmeden sorumluluk odaklı `partial` dosyalara ayırır. Tüketicinin kayıt sırası, `TryAdd*` davranışı ve varsayılanları birebir kalır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentPrismServiceCollectionExtensions.cs:13`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs) | Tek `public static class`, **2.662 satırdır**. Public girişler, servis kayıtları ve bütün elle bağlayıcılar aynı gövdededir. |
| [`AgentPrismServiceCollectionExtensions.cs:58`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs) | `AddAgentPrism(IServiceCollection, IConfiguration?)` yaklaşık bin satırlık composition root'u tek gövdede taşır. |
| [`AgentPrismServiceCollectionExtensions.cs:1070`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs) | Elle options binding zinciri dosyanın kalan yaklaşık 1.600 satırını taşır. K-021 gereği genel `Bind()` ile değiştirilemez. |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

## 105.1 — Public facade ve kayıt gövdesi

`AgentPrismServiceCollectionExtensions` `public static partial class` olur. Public extension metotları `AgentPrismServiceCollectionExtensions.cs` içinde kalır. Kayıt grupları `AgentPrismServiceCollectionExtensions.Registration.<Alan>.cs` dosyalarına özel yardımcılar olarak taşınır.

Gruplar işlev sınırına göre ayrılır: çekirdek katalog/derleme, run yaşam döngüsü, store varsayılanları, işletim özellikleri ve doğrulayıcılar. Taşıma sırasında kayıt sırası korunur. `TryAdd*`, `TryAddEnumerable` ve açık fabrika biçimleri değiştirilmez.

## 105.2 — Elle options binding

K-021 korunur. Root `Bind` metodu yalnız alt bağlayıcıları çağırır. Alt bağlayıcılar şu dosya ailelerine ayrılır:

- model, pricing, image, attachment ve agent graph;
- scheduling, quota, rate limit, async run ve reconciliation;
- security, egress, approval, protection, webhook ve retention.

Yeni reflection tabanlı binder veya yeni package eklenmez. Yapılandırma anahtarı, varsayılan değer ve doğrulama sırası değişmez.

## 105.3 — Kayıt anlık görüntüsü kapısı

Refactor öncesi `AddAgentPrism()` sonucu üretilen `ServiceDescriptor` kümesi kararlı bir metne dönüştürülür. Service tipi, lifetime ve bilinen implementation tipi kaydedilir. Factory kayıtları service tipi ve lifetime ile temsil edilir. Taban çizgisi taşıma öncesi üretilir; taşıma sonrası sıfır fark gerekir.

Bu kapı davranış testlerinin yerine geçmez. Ama taşınırken unutulan tek bir kayıt veya değişen lifetime'ı doğrudan gösterir.

## Planlanan Public API

Yeni public üye yoktur. Mevcut sınıfa `partial` eklemek metadata sözleşmesini değiştirmez. `PublicAPI.Unshipped.txt` farkı boş kalmalıdır.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

## Planlanan Dosya Listesi

```text
src/AgentPrism.Core/
├── AgentPrismServiceCollectionExtensions.cs
├── AgentPrismServiceCollectionExtensions.Registration.Core.cs
├── AgentPrismServiceCollectionExtensions.Registration.Operations.cs
├── AgentPrismServiceCollectionExtensions.Registration.Storage.cs
├── AgentPrismServiceCollectionExtensions.Binding.Models.cs
├── AgentPrismServiceCollectionExtensions.Binding.Operations.cs
└── AgentPrismServiceCollectionExtensions.Binding.Security.cs
tests/AgentPrism.Core.UnitTests/
└── Configuration/ServiceRegistrationSnapshotTests.cs
```

Dosya adları uygulama anında sorumluluk kümeleri ölçülerek daraltılabilir. Tek koşul, her dosyanın tek bir kayıt veya binding ekseni taşımasıdır.

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Bir service kaydı taşınırken kaybolur veya lifetime değişir | Birim / yapısal | `ServiceRegistrationSnapshotTests` |
| Tüketicinin önce yaptığı kayıt artık kazanmaz | Birim | mevcut registration testleri + seçilmiş `TryAdd` senaryoları |
| Bir configuration anahtarı artık bağlanmaz | Birim | `AgentPrismOptionsBindingCoverageTests` ve options-aile testleri |
| Çıplak `ServiceCollection` host-only bağımlılık yüzünden çözülemez | Birim | `NonPersistentStorageWarningRegistrationTests` ve provider çözümleme senaryosu |
| İptal, eşzamanlılık veya kiracı davranışı değişir | Fonksiyonel | değişen kayıtların mevcut Core/HTTP testleri; bu faz yeni davranış eklemez |
| Alt sistem yokken varsayılan in-memory kurulum kalkmaz | Gerçek sample | `samples/AgentPrism.Api` varsayılan kalkış + gerçek `run` |

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Temiz build | Varsayılan sample'ı çalıştır, `/api/meta` ve bir support `run` çağır | Host kalkar; run tamamlanır; varsayılan store'lar çözülür |
| 2 | Özel `IRunStore` kaydı | Kaydı `AddAgentPrism()` öncesi yap, provider'ı çöz | Tüketicinin kaydı kazanır |
| 3 | Dolu configuration | Mevcut options binding coverage girdisini çalıştır | Tüm yaprak değerler beklenen options alanlarına bağlanır |

## Açık Sorular

Yok. Bu fazda yeni abstraction veya davranış kararı alınmaz.

## Bitiş Ölçütleri (DoD)

- [x] Ana facade yalnız public girişleri ve üst düzey yönlendirmeyi taşır; registration ve binding gövdeleri sorumluluk dosyalarındadır
- [x] Refactor öncesi ve sonrası service descriptor snapshot'ı sıfır fark verir
- [x] `AgentPrismOptionsBindingCoverageTests` ve ilgili registration testleri yeşildir
- [x] `git diff -- 'src/*/PublicAPI.*.txt'` boş döner
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili ailelere eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Riskler

| Risk | Önlem |
|---|---|
| Kayıt sırası davranışı değiştirir | Önce snapshot alınır; taşıma küçük kümeler halinde yapılır; her kümeden sonra test koşar |
| `partial` dosyalar yeni bir monolite dönüşür | Dosya sınırı teknik türe göre değil domain sorumluluğuna göre kurulur |
| Binder ayrışması anahtar kaydırır | K-021 korunur; her helper gövdesi mekanik taşınır; binding coverage bütün yaprakları karşılaştırır |

---

## Plandan Sapmalar

- **Binding dosya sayısı 3 değil 4.** 105.2'nin metni üç grup örnekliyordu
  (model/pricing/image/attachment/agent-graph · scheduling/quota/rate-limit/
  async-run/reconciliation · security/egress/approval/protection/webhook/
  retention). On dört `Bind*` yardımcısı (`BindTools`, `BindPreflight`,
  `BindModelConcurrency`, `BindValidation`, `BindAudit`, `BindSkills`,
  `BindSkillScripts`, `BindList`, `BindCircuitBreaker`, `BindHealth`,
  `BindRunRecording`, `BindObservability`, `BindOnlineEvaluation`,
  `TryReadBool`, artı yeni `BindCoreFields`) üç grubun hiçbirine temiz
  oturmadı; bunlar `AgentPrismServiceCollectionExtensions.Binding.Core.cs`
  adıyla dördüncü bir dosyaya toplandı. Skill'in kendi metni bunu açıkça
  serbest bırakıyor ("Dosya adları uygulama anında sorumluluk kümeleri
  ölçülerek daraltılabilir... Tek koşul, her dosyanın tek bir kayıt veya
  binding ekseni taşımasıdır") — dördüncü dosya da tek eksen taşıyor
  ("çekirdek/gözlemlenebilirlik ayarları"), sapma yalnız isimlendirme.
- **`Registration.Core.cs` iki yardımcı taşıyor, tek değil.** Plan metni
  "çekirdek katalog/derleme, run yaşam döngüsü, store varsayılanları,
  işletim özellikleri ve doğrulayıcılar" olmak üzere beş kavramdan
  bahsediyordu ama dosya listesi yalnız üç `Registration.*.cs` adı
  veriyordu. Gerçekleşen: `RegisterCoreInfrastructure` (model provider
  registry, tool registry, compiler zinciri, audit — orijinal dosyanın
  282-521. satırları) ve `RegisterCatalogAssembly` (session/replay/catalog
  composition/decorator zinciri — 836-996. satırları) aynı
  `Registration.Core.cs` dosyasında iki ayrı `private static` metot olarak
  durur; ikisi de "çekirdek agent yaşam döngüsü" eksenine ait, ayrı dosyaya
  bölünmeleri gerekmiyordu.
- **Denetimde bulunan `BindCoreFields` çıkarımı.** `faz-denetim` bağımsız
  denetçisi, root `Bind()` metodunun DoD'un "yalnız alt bağlayıcıları
  çağırır" iddiasına rağmen iki alanı (`DefaultTenantId`,
  `MaxParameterValueLength`) kendi gövdesinde bağladığını buldu — orijinal
  2662 satırlık dosyada da aynıydı, taşıma bunu miras almıştı. İki satır
  `BindCoreFields(IConfiguration, AgentPrismOptions)` adıyla
  `Binding.Core.cs`'e çıkarıldı; `Bind()` artık gerçekten yalnız
  yönlendirme yapıyor.
- **Kayıt anlık görüntüsü kapısı (105.3), plandaki "taşıma öncesi üretilir"
  akışının tersine, taşıma TAMAMLANDIKTAN sonra üretildi** — ama eşdeğerliği
  kanıtlamak için `git stash` ile pre-refactor dosyaya geçici olarak
  dönülüp aynı probe orada da çalıştırıldı; iki çıktı `diff` ile
  bayt-bayt karşılaştırıldı (sıfır fark, 160 kayıt). Sonuç aynı: taşıma
  öncesi/sonrası fark yok, yalnız kanıtlama sırası ters çalıştı çünkü
  bölme işlemi zaten atomikti (script tek seferde tüm dosyaları üretti).
- **Denetimde denenip reddedilen bir yaklaşım:** `ServiceRegistrationSnapshotTests`
  başlangıçta her factory kaydını düz "Factory" yerine
  `ImplementationFactory.Method.Name` ile ayırt etmeyi denedi (denetçinin
  2 numaralı 🟡 bulgusuna karşılık). Bu, `BindCoreFields` gibi ilgisiz tek
  bir private metot eklenince derleyicinin tüm sonraki closure'ları
  yeniden numaralandırdığını gösterdi (ölçüldü: `RegisterCoreInfrastructure`
  içindeki ilk factory `b__48_0`'dan `b__49_0`'a kaydı) — ilgisiz her
  değişiklik testi kırardı. Yaklaşım geri alındı; sınırlama testin kendi
  XML dokümanına yazıldı (bkz. Denetim Bulguları #2).

## Bu Fazda Verilen Kararlar

Yok. Plan bunu önceden belirtmişti — mekanik taşıma yeni bir public API,
compatibility contract, güvenlik/kiracı sınırı veya kalıcı veri kararı
üretmedi.

## Gerçekleşen Public API

Yok. `git diff --stat -- 'src/*/PublicAPI.*.txt'` boş döner.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

## Dosya Listesi (gerçekleşen)

```text
src/AgentPrism.Core/
├── AgentPrismServiceCollectionExtensions.cs                  (165 satır — public giriş noktaları + üst düzey yönlendirme)
├── AgentPrismServiceCollectionExtensions.Registration.Core.cs       (422 satır — RegisterCoreInfrastructure, RegisterCatalogAssembly)
├── AgentPrismServiceCollectionExtensions.Registration.Operations.cs (234 satır — RegisterOptions)
├── AgentPrismServiceCollectionExtensions.Registration.Storage.cs    (329 satır — RegisterStorageAndJobs)
├── AgentPrismServiceCollectionExtensions.Binding.Core.cs      (531 satır — BindCoreFields, Tools/Preflight/ModelConcurrency/Validation/Audit/Skills/SkillScripts/List/CircuitBreaker/Health/RunRecording/Observability/OnlineEvaluation/TryReadBool)
├── AgentPrismServiceCollectionExtensions.Binding.Models.cs    (317 satır — Pricing/VoicePricing/ImagePricing/Images/ReadDecimal/UtilityModel/AgentGraph/Attachments)
├── AgentPrismServiceCollectionExtensions.Binding.Operations.cs (350 satır — Scheduling/SingletonExecution/Quotas/RateLimit/Idempotency/AsyncRun/RunReconciliation/RunContinuation/Drain/Canary)
└── AgentPrismServiceCollectionExtensions.Binding.Security.cs  (405 satır — Approval/Egress/McpSecurity/TenantProviders/InboundTriggers/ContentGuard/PatternContentGuard/ContentProtection/Webhooks/Retention/RetentionTarget)
tests/AgentPrism.Core.UnitTests/
└── Configuration/ServiceRegistrationSnapshotTests.cs (yeni — 105.3 kapısı)
```

Toplam 2753 satır — orijinal 2662'den 91 satır fazla (dosya başına `using`
blokları ve `partial class` gövdeleri tekrarı; XML doküman içeriği bire bir
korundu).

## Denetim Bulguları

Bağımsız denetçi (`faz-denetim`, taze bağlamlı alt agent) çalışma ağacını
`git show 12c867a:...` ile method-method karşılaştırdı (48 orijinal metodun
tamamı — örnekleme değil), build/format/PublicAPI/test kanıtlarını bağımsız
yeniden koştu. 🔴 bulgu **yok**.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Gerçekleşen dosya seti (4 Binding dosyası, `Registration.Core.cs` içinde 2 metot) plan metninden sapıyor | **Düzeltildi** — bkz. "Plandan Sapmalar" |
| 2 | 🟡 | `ServiceRegistrationSnapshotTests.Describe` tüm factory kayıtlarını "Factory" string'ine indirgiyor; aynı `ServiceType+Lifetime` için birden fazla factory (4× `IJobHandler`, 3× `IHostedService`) birbirinden ayırt edilemiyor — bunlar arası bir sıra değişimini test yakalamaz | **Gerekçelendi** — daha zengin bir temsil (`Method.Name`) denendi, derleyicinin closure numaralandırmasının **partial class** genelinde (dosya değil) atandığı ve ilgisiz bir metot eklemenin bile tüm sonraki numaraları kaydırdığı ölçüldü; bu, kapattığı boşluktan daha büyük bir kırılganlık üretti. Sınırlama testin kendi XML dokümanına yazıldı; `IHostedService` sırasının gerçekten önemli olduğu davranış (kapanış sırası) zaten bütünleşik/fonksiyonel test seviyesinde kanıtlanıyor (`.agents/ortak/test-seviyeleri.md`), tek başına bir DI-kayıt unit testine değil. |
| 3 | 🟡 | Root `Bind()` metodu DoD'un "yalnız alt bağlayıcıları çağırır" iddiasına rağmen iki alanı kendi gövdesinde bağlıyordu (orijinal dosyadan miras) | **Düzeltildi** — `BindCoreFields` adıyla `Binding.Core.cs`'e çıkarıldı; `Bind()` artık gerçekten yalnız yönlendirme |

**Temiz çıkan başlıklar:** 3.1 (DoD ihlali — build 0 uyarı, format temiz,
`PublicAPI.*.txt` boş, 1951/1951 yeşil, hepsi bağımsız doğrulandı), 3.2 (test
tiyatrosu yok), 3.4 (yeni kod yolu yok — saf taşıma), 3.5 (imza-gövde kayması
yok — hiçbir metot imzası değişmedi), 3.6 (plan dışı public API yok), 3.7
(İngilizce metin, `TryAdd*` korunmuş, `secret` taraması temiz), 3.8 (tüketici
yüzeyi yok).

Düzeltmeler sonrası dört kapı yeniden koşuldu (`dotnet build`, `dotnet test
AgentPrism.slnx --no-build -maxcpucount:1`, `dotnet format
--verify-no-changes`) — hepsi yeşil, `git diff --stat -- 'src/*/PublicAPI.*.txt'`
hâlâ boş.

## Gerçek Run Kanıtı

`samples/AgentPrism.Api` PostgreSQL (`ap-pg` container) ile ayağa kaldırıldı:

```text
GET /agentprism/api/meta → 200
  {"version":"0.0.0-preview.0.402", ...,
   "storage":{"persistent":true,"agentDefinitionStore":"SqlAgentDefinitionStore",
   "runStore":"SqlRunStore","sessionStore":"SqlSessionStore","jobStore":"SqlJobStore",
   "jobWorkerEnabled":true}}

POST /agentprism/api/agents/support/run (OpenAI, gerçek tool çağrısı) → SSE stream,
  event: done, {"sessionId":null}

GET /agentprism/api/runs?limit=1 → [{"id":"01a03b1d-...", "agentName":"support",
  "status":"Completed", "usage":{"inputTokens":283,"outputTokens":16,"totalTokens":299}, ...}]
```

Host kalktı, PostgreSQL tabanlı store'lar (bölünmüş DI kaydından)
çözüldü, gerçek bir `run` tamamlandı ve kalıcı depoya yazıldı. Manuel kabul
case 1 ve 2 (bkz. faz dokümanının "Manuel Kabul Case'leri" tablosu) bununla
ve mevcut `MT-PKG-080`/`MT-PKG-081` ile karşılanmış sayılır; case 3
`AgentPrismOptionsBindingCoverageTests` ile zaten otomatik koşuluyordu (1951
yeşil test setinin içinde).

## Sonraki Faza Devir Notu

- Bu fazın script'i (satır aralıklarını brace-derinliğiyle hesaplayıp
  bitişik dilimleri yeni dosyalara taşıyan Python betiği) Faz 106'nın
  konusu olan `AgentDefinitionCompiler`/derleyici ayrıştırması için de
  yeniden kullanılabilir bir desendir — ama derleyici muhtemelen `Bind*`
  gibi zaten ayrık metotlara sahip değil, tek büyük bir akış (`Compile`)
  olabilir; o zaman "bitişik dilim → adlandırılmış yardımcı metot" deseni
  (bu fazda `AddAgentPrism` gövdesi için kullanıldı) daha çok işe yarar.
- Kayıt anlık görüntüsü deseni (`ServiceRegistrationSnapshotTests`,
  `ServiceType | Lifetime | Implementation` üçlüsü) benzer bir DI kökü
  ayrıştırması gerekiyorsa (`AgentPrism.AspNetCore`, provider paketlerinin
  `Use*` metotları) doğrudan kopyalanabilir; yalnız factory ayrımını
  `Method.Name`'e genişletmeyi TEKRAR DENEME — bu fazda ölçülüp reddedildi
  (bkz. Denetim Bulguları #2).
- `git stash` ile pre-refactor/post-refactor karşılaştırması (probe konsol
  uygulaması, `ProjectReference` ile ilgili `.csproj`'a bağlanan tek
  dosyalık scratch proje) büyük mekanik refactor'larda "davranış
  değişmedi" iddiasını insan gözünden bağımsız kanıtlamanın ucuz bir
  yoludur; sonraki ayrıştırma fazlarında (106-108) tekrar kullanılabilir.
