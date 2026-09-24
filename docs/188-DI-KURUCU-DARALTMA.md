# Faz 188 — DI ile Kurulan Servis Tiplerinde Kurucu Daraltması

> **Durum:** ✅ Tamamlandı (2026-09-24)
> **Plan onayı:** Bakımcı, 2026-09-23 (engelleyici kararlar sohbette alındı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-271** — A bölümü. B bölümü (`TraconToolRegistration`, kaynak üreteci, `ITraconBuilder`): [Faz 189](189-TUKETICI-YUZEYI-VE-BUILDER.md)
> **Önkoşul:** [Faz 185](arsiv/fazlar/185-KARDES-PAKET-SURUM-SABITLEME.md) — kardeş paketleri tam sürüme sabitler. Bu faz `Tracon.Workflows`'un IVT ile çağırdığı iki kurucuyu (`ChildAgentInvoker`, `RunEventWriter`) internal yapar; karışık sürümlü grafta bu bağ ancak o sabitlemeyle güvenlidir · [Faz 187](arsiv/fazlar/187-KIRICI-DEGISIKLIK-KAPISI.md) — `kapi.py yayin` kırıcı değişiklik kapısı; kaldırılan her imza oradan geçer · [Faz 186](arsiv/fazlar/186-SCRIPT-IZNI-ICERIK-PINI.md) sıra gereği önce kapanır, teknik bağ yok. [Faz 189](189-TUKETICI-YUZEYI-VE-BUILDER.md) bu faza bağlıdır
> **Paketler:** `Tracon.Core` (15 kurucu); K-850 dalgasında `Tracon.Abstractions` (3 aday tip). Test ve araç: `tests/Tracon.Core.UnitTests`, `tests/Tracon.AspNetCore.FunctionalTests`, `tests/Tracon.Testing.UnitTests`, `bench/Tracon.Benchmarks`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Daralıyor** — 15 kurucu satırı `src/Tracon.Core/PublicAPI.Unshipped.txt`'ten çıkar; K-850 dalgası en çok 19 tip / 172 satır daha çıkarabilir (188.4). `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosyanın her biri 1 satır (`#nullable enable`): `Shipped` boştur (K-603). Dokunulan her tip için: bugün daraltmak ucuzdur (pre-1.0, `Shipped` boş); GA'dan sonra kırıcıdır
> **Tüketici yüzeyi:** site: elle değişen sayfa yok — site, sample, şablon ve README'de 15 kurucuya `new` çağrısı **0** (§2); `api/` referansı ve `reference/changelog.md` üretilir · sevk edilen: `CHANGELOG.md` `Removed` + geçiş örneği (188.6); XML `<example>` yok (`///.*new <Tip>(` taraması 0); `<remarks>`: Açık Soru 4; paket README'si yalnız dalga tip daraltırsa elle güncellenir (`README.md:346` "673 public types", `src/Tracon.Abstractions/README.md:20` 407 tip / 85 arayüz; zorlayan kapı yok); `capabilities.md` değişmez (`:120` `AgentSessionManager`'ı anar, tip public kalır)
> **Manuel test alanı:** `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` (paketlenmiş tüketici, envanter) · `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` (`MT-TEST-027`, satır 922, `new ModelProviderRegistry(...)` yazar — yeniden yazılır)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.** Bu listenin dışında
> okuma gerekmez; Faz 185'in kapısı hata mesajıyla kendini anlatır.

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-850\|K-614\|K-603" docs/KARARLAR.md
   ```
   **K-850** (kanıtsız public tip `internal` olur; birinci taraf gövde kullanımı
   IVT ile çözülür; gerekçe `scripts/public-yuzey-gerekceleri.tsv`'ye yazılır) ·
   **K-614** (`grep -n "K-614" docs/KARARLAR.md`; `IToolRegistry` public kalır; yeniden açılma koşulu "iki ctor'un
   public'liği kalkarsa". Bu iki kurucu `AgentDefinitionCompiler`
   (`Core/PublicAPI.Unshipped.txt:4`) ve `TraconDiagnosticsCollector`'dır
   (`:417`); bu faz koşulu gerçekleştirir) · **K-603** (`Shipped` GA'ya kadar boş)
3. Faz 187 — yalnız devir notu. Bu faz kırıcı değişiklik kapısının **ilk gerçek
   tüketicisidir** (`docs/187:503`); kapının son biçimi oradadır:
   ```bash
   f=$(ls docs/187-*.md docs/arsiv/fazlar/187-*.md 2>/dev/null | head -1)
   awk '/## Sonraki Faza Devir Notu/,0' "$f"
   ```
4. Alan hafızası (iki alan):
   ```bash
   sed -n '45,54p;82,98p' docs/hafiza/analyzer-tanilari.md
   sed -n '17p;20p;27p' docs/hafiza/aspnetcore-di.md
   ```
   `analyzer-tanilari.md`: RS0026/RS0027 ve "Public yuzey daraltma tarifi" (`~`
   önekli satırlar, IVT) · `aspnetcore-di.md`: yerleşik DI kabı varsayılan
   parametreyi doldurmaz; fabrikada unutulan parametre sessizce `null` kalır

---

## Amaç

`Tracon.Core`'da 15 public servis tipi public kurucu taşır. Kurucuların hepsi
opsiyonel parametre taşır. Hiçbirini tüketici çağırmaz; tipleri DI fabrikası
veya Tracon boru hattı kurar. GA'da (K-603) `Shipped` dolunca bu kurucular
donar. Bu faz kurucuları `internal` yapar ve tipleri public bırakır. Kurucu
imzası yüzünden public kalan tipleri K-850 ile yeniden yargılar. Sınıfın
yeniden büyümesini bir ratchet kapısıyla engeller. Analyzer'ın izin verdiği
RS0026/RS0027 yolu her yeni bağımlılıkta bir aşırı yükleme biriktirir; K-848
bu bedeli zaten ödedi (logger `internal init` ile bağlandı,
`ModelProviderHealthCache.cs:45`).

- **F-271 (A)** — tüketicisiz 15 opsiyonel parametreli public kurucu `internal`
  olur; kanıtı düşen tipler K-850 ile işlenir; opsiyonel parametreli public
  kurucular için yalnız küçülen bir taban kapısı kurulur.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `src/*/PublicAPI.Unshipped.txt` (DoD §1 komutu) | 17 dosyada **482** public kurucu; **19**'u opsiyonel parametre taşır; 15'i tüketicisiz servis tipidir, hepsi `Tracon.Core`'da |
| [`PublicAPI.Unshipped.txt:300`](../src/Tracon.Core/PublicAPI.Unshipped.txt) | `RunRecordingAgent` 24 parametre, 19 opsiyonel. İmzası `QuotaEnforcer`, `RunSampler`, `RunTraceCollector`, `ContentGuardPipeline`, `ToolApprovalPresenterRunner`'ı public yüzeye çeker |
| §2 tüketici yüzeyi grep'i | **0** eşleşme. Site yalnız DI çözümü gösterir (`write-your-own-agent-source.md:30`: `GetRequiredService<AgentDefinitionCompiler>()`) |
| `python3 scripts/public-yuzey-envanteri.py --denetle` | Çıkış 0: 673 tip, kanıtsız 0. 15 kurucu satırı çıkınca 19 tip kanıtsız olur (188.4) |

> HEAD `bb9953e3`, 2026-09-23. Diğer `file:line` kanıtları 188.2 ve 188.3'tedir.

---

## 188.1 — Bağlayıcı kararlar

Engelleyici sorular plan yazılmadan önce soruldu. Aşağıdaki sekiz karar
bağlayıcıdır (kullanıcı kararı, 2026-09-23):

| # | Karar |
|---|---|
| 1 | Yalnız DI'ın veya Tracon boru hattının kurduğu public servis tipinin kurucusu `internal` olur; **tip public kalır**. Tip tabanlı kayıtlı tip (`RunSampler`) fabrika kaydına geçer |
| 2 | Kapsam: opsiyonel parametre taşıyan ve tüketici çağrı yeri olmayan **15 kurucunun tamamı** (188.2). DI dışı kurulum yerleri (`ModelRunJudge.cs:93`, `AgentDefinitionCompiler.Agents.cs:162`, iki `Tracon.Workflows` yeri) çalışmaya devam eder. IVT'siz tek test çağrısı (`FakeModelProviderTests.cs:104`) DI ile yeniden yazılır |
| 3 | Tek public kanıtını kaybeden tipler **bu fazda** K-850 süreciyle işlenir (envanter + TSV; yargıç + şüpheci). Birinci taraf gövde kullanımı IVT ile çözülür |
| 4 | Opsiyonel parametreli **public kurucuları** izleyen bir ratchet kapısı kurulur (`PublicSurfaceBaselineTests` genişler); taban yalnız küçülür. `AgentRunBudget`, `FakeModelProvider` ve tüketicinin kurduğu imzalar gerekçeyle tabanda kalır. Metotlar kapsam dışıdır |
| 5 | `IAuditDecorated`'a dokunulmaz |
| 6 | `IToolRegistry` public kalır. K-614'ün yeniden açılma koşulu gerçekleşir ama karar korunur; K-614 satırına fazın karar kaydıyla not düşülür |
| 7 | Kaldırılan imzalar doğrudan gider (pre-1.0; `[Obsolete]` yok). Sürüm notu bir geçiş örneği verir |
| 8 | `AgentRunScope.Writer` üzerinden açılan `RunEventWriter` yaşam döngüsü metotları: **Açık Soru 1** |

🚨 Karar 1 ile 3 çelişmez. Karar 1 **kurucu** politikasıdır; bir tip bu kural
yüzünden `internal` olmaz. Karar 3 kanıtı düşen tipi K-850'ye sokar; o süreç
tipi `internal` yapabilir. 15 tipin 8'i (188.4) bu yolla yargılanır.

## 188.2 — Kapsam: 15 kurucu

Ölçüm 2026-09-23, HEAD `bb9953e3`. `Registration.*.cs` =
`src/Tracon.Core/TraconServiceCollectionExtensions.Registration.*.cs`. Yol
verilmeyen kaynak `src/Tracon.Core/` altındadır. Son sütun `new <Tip>(`
çağıran test **dosyası** sayısıdır. 15 tipin her birinde kurucuyu bugün
değiştirmek ucuzdur (pre-1.0, `Shipped` boş); GA'dan sonra kırıcıdır.

| # | Tip | Unshipped | Kurucu | Param/ops. | Kurulum yeri | `new` çağıran testler |
|---|---|---|---|---|---|---|
| 1 | `RunRecordingAgent` | Core:300 | `Recording/RunRecordingAgent.cs:138` | 24/19 | `Recording/RunRecordingAgentDecorator.cs:129` (internal decorator) · `Evaluation/ModelRunJudge.cs:93` | Core.UnitTests 21 · Workflows.UnitTests 1 |
| 2 | `AgentDefinitionCompiler` | Core:4 | `Compilation/AgentDefinitionCompiler.cs:148` | 20/18 | `Registration.Core.cs:281` | Core.UnitTests 39 |
| 3 | `TraconDiagnosticsCollector` | Core:417 | `Diagnostics/TraconDiagnosticsCollector.cs:67` | 17/4 | `Registration.Core.cs:224` | — |
| 4 | `ModelProviderRegistry` | Core:184 | `Models/ModelProviderRegistry.cs:117` | 14/13 | `Registration.Core.cs:154` (`IModelProviderRegistry` olarak) | Core.UnitTests 16 · Anthropic.UnitTests 1 · **Testing.UnitTests 1 (IVT yok)** |
| 5 | `SandboxedSkillScriptRunner` | Core:328 | `Skills/Scripts/SandboxedSkillScriptRunner.cs:70` | 9/3 | `Skills/TraconSkillScriptBuilderExtensions.cs:64` (`UseSkillScripts`) | Core.UnitTests 1 |
| 6 | `ChildAgentInvoker` | Core:92 | `Graph/ChildAgentInvoker.cs:69` | 8/1 | `Compilation/AgentDefinitionCompiler.Agents.cs:162` · **`src/Tracon.Workflows/Internal/WorkflowAgentCache.cs:117`** | Core.UnitTests 1 |
| 7 | `ContentGuardPipeline` | Core:103 | `Guards/ContentGuardPipeline.cs:53` | 7/1 | `Registration.Core.cs:121` | — |
| 8 | `RunEventWriter` | Core:295 | `Recording/RunEventWriter.cs:65` | 6/1 | `Recording/RunRecordingAgent.Lifecycle.cs:87` · **`src/Tracon.Workflows/Internal/WorkflowRunner.cs:440`** · **`bench/Tracon.Benchmarks/RunEventWriterBenchmarks.cs:21` (IVT yok)** | Core.UnitTests 7 · PostgreSql.IntegrationTests 3 |
| 9 | `AgentSessionManager` | Core:63 | `Sessions/AgentSessionManager.cs:120` | 5/3 | `Registration.Core.cs:414` | Core.UnitTests 4 |
| 10 | `QuotaEnforcer` | Core:250 | `Quotas/QuotaEnforcer.cs:26` **primary** | 5/3 | `Registration.Storage.cs:260` | Core.UnitTests 3 |
| 11 | `ModelProviderHealthCache` | Core:176 | `Models/ModelProviderHealthCache.cs:58` | 4/2 | `Registration.Core.cs:202` | Core.UnitTests 1 |
| 12 | `RunSampler` | Core:316 | `Evaluation/RunSampler.cs:24` **primary** | 4/2 | **`Registration.Storage.cs:216` tip tabanlı** | Core.UnitTests 1 |
| 13 | `SkillScriptSupport` | Core:333 | `Skills/Scripts/SkillScriptSupport.cs:45` | 3/1 | `Skills/TraconSkillScriptBuilderExtensions.cs:75` | — |
| 14 | `ModelProviderCircuitBreaker` | Core:169 | `Models/ModelProviderCircuitBreaker.cs:44` | 2/1 | `Registration.Core.cs:107` | Core.UnitTests 3 |
| 15 | `TraconMetrics` | Core:493 | `Diagnostics/TraconMetrics.cs:54` | 2/2 | `Registration.Core.cs:387` | Core.UnitTests 10 · PostgreSql.IntegrationTests 1 · Workflows.UnitTests 1 |

Tiplerin hepsi `sealed`'dır (`protected` kurucu yok). Her tip tek public
kurucu taşır.

**Kapsam dışı dört opsiyonel parametreli kurucu** (ratchet tabanına girer,
188.5): `TraconToolRegistration` 8/7 (tüketici ve `[TraconTool]` üreteci kurar,
`src/Tracon.Generators/SourceWriter.cs:106-114`; Faz 189 yeniden biçimlendirir)
· `TraconAgentSourceException` 4/1 (istisna kurucu deyimi) · `AgentRunBudget`
2/2 (tüketici kurar, `guides/external-agents.md:93`) · `FakeModelProvider` 1/1
(tüketici test kodu kurar, `guides/testing.md:51`).

## 188.3 — Kurucu politikası, kayıt ve DI dışı kurulum

**Adım 0 — yeniden ölç.** Aynı turda iki iş bu dosyalara dokunur:
`QuotaEnforcer` eşik önbelleğini budar; kota ve kuyruk derinliği gauge'ları
bir `BackgroundService`'te tazelenir. Önce bak:

```bash
git log --oneline bb9953e3..HEAD -- src/Tracon.Core/Quotas/QuotaEnforcer.cs \
  src/Tracon.Core/Diagnostics/TraconMetrics.cs \
  'src/Tracon.Core/TraconServiceCollectionExtensions.Registration.*.cs'
```

Sonra DoD §1 komutunu koş. 15 satırlık tablo değiştiyse tabloyu düzelt ve
plandan sapma olarak yaz.

**Kurucular.** 15 kurucu `public` → `internal` olur. İmza değişmez. 15 satır
`PublicAPI.Unshipped.txt`'ten silinir (yoksa RS0017). XML dokümanı kalır.

**Primary constructor.** `QuotaEnforcer` ve `RunSampler` primary constructor
taşır. Primary constructor erişim belirleyicisi almaz; iki tip açık bir
`internal` kurucuya geçer. Emsal: `Skills/AgentSkillCatalog.cs:8,15` (public
tip, internal kurucu, fabrika kaydı `Registration.Storage.cs:39`). Dönüşüm
yakalanan parametreleri alanlara açıkça atar ve
`timeProvider ?? TimeProvider.System` varsayılanını korur. 🚨
`QuotaEnforcer.cs` aynı turda değişti; dönüşümü yeni metnin üstüne yap.

**`RunSampler` fabrika kaydı.** 🚨 `Registration.Storage.cs:216` tipi **tip
tabanlı** kaydeder: `services.TryAddSingleton<RunSampler>();`. Kurucu internal
olursa MS DI public kurucu bulamaz. Hata **çalışma anında** çıkar (çözüm yeri
`Registration.Core.cs:513`); derleme yakalamaz. Kayıt `QuotaEnforcer`
deseniyle (`Registration.Storage.cs:260-265`) fabrikaya döner: zorunlu
bağımlılık `GetRequiredService`, opsiyonel olan (`TimeProvider`,
`ILogger<RunSampler>`) `GetService`. 🚨 Bugün MS DI bu iki opsiyoneli
kayıtlıysa doldurur. Fabrika birini unutursa değer sessizce `null` kalır
(`aspnetcore-di.md:27`). Testler bunu davranışla kanıtlar: saat (Hata
Modları #2) ve logger (#4). Logger yalnız `IJobStore.EnqueueAsync` atınca
kullanılır (`Evaluation/RunSampler.cs:102-113`).
`ServiceRegistrationSnapshotTests.cs:207`
(`"Tracon.RunSampler | Singleton | Tracon.RunSampler"`) `Factory` olur.

**DI dışı kurulum yerleri.**

| Yer | Çözüm |
|---|---|
| Core içi: `ModelRunJudge.cs:93`, `RunRecordingAgentDecorator.cs:129`, `AgentDefinitionCompiler.Agents.cs:162`, `RunRecordingAgent.Lifecycle.cs:87` | Aynı derleme; değişiklik yok |
| `Tracon.Workflows`: `WorkflowAgentCache.cs:117`, `WorkflowRunner.cs:440` | Core → Workflows IVT **zaten var** (`src/Tracon.Core/Properties/AssemblyInfo.cs:29`). Kenardan geçen internal üye artar; karışık sürüm riskini Faz 185 kapatır |
| IVT'li test projeleri: Core.UnitTests (`AssemblyInfo.cs:45`), Workflows.UnitTests (`:40`), PostgreSql.IntegrationTests (`:42`), Anthropic.UnitTests (`:52`) | Değişiklik yok |
| `tests/Tracon.Testing.UnitTests/FakeModelProviderTests.cs:104` (`new ModelProviderRegistry([provider])`) | DI ile yeniden yazılır, IVT **eklenmez** (karar 2): `new ServiceCollection()` → `AddTracon().AddModelProvider(provider)` → `BuildServiceProvider()` → `GetRequiredService<IModelProviderRegistry>().CreateChatClient(...)`. `AddLogging()` gereği **ölçülmeli**. Emsal: `tests/Tracon.Core.UnitTests/Catalog/AgentDecoratorRegistrationTests.cs:20-24` |
| `bench/Tracon.Benchmarks/RunEventWriterBenchmarks.cs:21` | **Açık Soru 2** |
| `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md:922` (`MT-TEST-027`) | Test koduyla aynı DI kodu; beklenen çıktı aynı (`Sonuc: hazirlaniyor (ORD-7)`) |

## 188.4 — İmza kapanışı: K-850 dalgası

**Simülasyon (2026-09-23).** Envanter modülü içe aktarıldı; 15 kurucu satırı
bellekte çıkarıldı; sınıflandırma yeniden koşuldu. Sonuç: tüketici 567 → 527 ·
seam 91 → 112 · kanıtsız 0 → **19** (tip satırı dahil 172 Unshipped satırı).
Uygulamada simülasyon gerekmez: kurucuları internal yap, satırları sil,
`python3 scripts/public-yuzey-envanteri.py --liste kanıtsız` koş. 21 tip
seam'e geçer; iş yok.

**Kanıtsız kalan 19 tip**, bugünkü tek kanıtı olan kurucu imzasına göre.
Paket yazılmayan tip Core'dadır. Parantez başka paketteki gövde kullanımıdır
(`git grep -lw <Tip> -- 'src/*.cs'`; yorumları da sayar, üst sınır). O
kenarların IVT'si **bugün vardır**: Core → AspNetCore/Workflows/SQL
`AssemblyInfo.cs:6,19-21,29`; Abstractions → Core/AspNetCore/SQL
`Tracon.Abstractions.csproj:14,29-33`. Bu tipleri bugün daraltmak ucuzdur
(pre-1.0, `Shipped` boş); GA'dan sonra kırıcıdır.

- `AgentDefinitionCompiler`: `AgentSkillCatalog` · `CallableAgentResolver`
  (AspNetCore, Workflows) · `SkillScriptSupport` · `TraconLoopEvaluatorRegistration`
- `ModelProviderRegistry`: `ContentGuardPipeline` · `ModelProviderCircuitBreaker`
  · `ProviderConcurrencyLimiter` · `TenantProviderCredentialResolver`
  (AspNetCore) · `TraconMetrics` (AspNetCore, 3 SQL paketi, Workflows)
- `RunRecordingAgent`: `QuotaEnforcer` (AspNetCore, Workflows) · `RunSampler`
  · `RunTraceCollector` (Workflows) · `ToolApprovalPresenterRunner`
- `TraconDiagnosticsCollector`: `ModelProviderHealthCache` (AspNetCore) ·
  `SqlPersistenceRegistrationMarker` — Abstractions (Core, 3 SQL paketi;
  `Sql.Shared` bağlı kaynak; **test: Açık Soru 3**)
- `QuotaEnforcer`: `QuotaDecision` — Abstractions (Core, AspNetCore) ·
  `QuotaThresholdCrossing` — Abstractions (Core)
- `RunSampler`: `RunSampleRequest` · `SkillScriptSupport`: `SandboxedSkillScriptRunner`

**Süreç (K-850, Faz 182 emsali).**

1. Yargıç her tip için `internal` veya `gerekçeli` der. `gerekçeli` somut
   kanıt ister; TSV başlığındaki dışlamalar geçerlidir (gövde kullanımı, test
   kullanımı, "ileride bir tüketici isteyebilir" **sayılmaz**).
2. Bağımsız şüpheci (ayrı alt agent, taze bağlam) her `internal` kararında
   tüketici yolu, her `gerekçeli` kararında spekülasyon arar. Anlaşmazlık
   kullanıcıya gider.
3. `internal` tipin Unshipped satırlarının **hepsi** silinir, `~` önekliler
   dahil (`analyzer-tanilari.md:84-91`). `CS0122` IVT isteyen derlemeyi söyler.
4. `--denetle` çıkış 0 olana kadar tekrarlanır; envanter düşen kanıtları
   kendisi yeniden hesaplar.
5. Sonuç tablosu kapanışta "Gerçekleşen Public API"ye yazılır.

`PublicSurfaceBaselineTests` tip tabanı (Core 95, Abstractions 407) daralma
kadar küçülür ve yenilenir (`TRACON_PUBLIC_SURFACE_REFRESH=1`).

## 188.5 — Ratchet kapısı: opsiyonel parametreli public kurucu

**Yer.** `tests/Tracon.Core.UnitTests/Architecture/PublicSurfaceBaselineTests.cs`
içine yeni bir `[Fact]`. Sınıf Unshipped dosyalarını ve depo kökünü zaten
bulur; ikinci okuyucu yazılmaz.

**Taban dosyası.** `tests/Tracon.Core.UnitTests/Architecture/optional-parameter-constructor-baseline.txt`,
`raw-exception-text-baseline.txt` biçiminde:

```
<paket>:<tam tip adı>(<parametre>/<opsiyonel>) | <gerekçe, en az 40 karakter>
```

Sayılar anahtarın parçasıdır. Tabandaki kurucuya opsiyonel parametre eklemek
anahtarı değiştirir; kapı bunu yeni + bayat girdi görür ve kırmızı olur.

| Durum | Sonuç |
|---|---|
| Opsiyonel parametreli public kurucu tabanda yok | Kırmızı: "yeni girdi — bir public API kararı gerekir" |
| Tabandaki girdinin kurucusu yok (internal oldu, silindi, imza değişti) | Kırmızı: "bayat girdi — satırı sil" |
| Gerekçe 40 karakterden kısa veya boş | Kırmızı |
| Aynı anahtar iki kez | Kırmızı |
| Taban dosyası yok veya boş | Kırmızı (sessiz yeşil yok) |

Kapı her paketin `PublicAPI.Shipped.txt` **ve** `Unshipped.txt` dosyasını
okur; GA'dan sonra donan kurucu `Shipped`'te durur. Yenileme
(`TRACON_OPTIONAL_CTOR_REFRESH=1`) **yalnız bayat satırı siler**. Yeni satır
elle eklenir ve bu fazın K-*'sını anar.

**Başlangıç tabanı (4 satır, gerekçe 188.2).**
`Tracon.Abstractions:Tracon.AgentRunBudget(2/2)` ·
`Tracon.Abstractions:Tracon.TraconAgentSourceException(4/1)` ·
`Tracon.Abstractions:Tracon.TraconToolRegistration(8/7)` (gerekçe Faz 189'u
adlandırır; o faz satırı siler) · `Tracon.Testing:Tracon.Testing.FakeModelProvider(1/1)`.

**Ayrıştırıcı tuzakları** (sentetik metinle birim testi): generic tip kurucusu
(`StoreCancellationContract<TStore>.StoreCancellationContract() -> void` bugün
Unshipped'te) · generic argümanda virgül (`IReadOnlyDictionary<string!, string!>`)
· `= default(System.Threading.CancellationToken)` · dize varsayılan
(`string! name = "fake"`) · `~` öneki (kurucu satırında bugün 0; biçim repoda
var). DoD §1 komutu aynı kuralları kullanır ve bugün 482 / 19 verir.

**Bilinen sınır.** Kapı opsiyonelsiz yeni public kurucuyu yakalamaz. Metotlar
kapsam dışıdır (karar 4); eşik altı metotlar (`EvalRunDiffBuilder.Build` 6/2
vb.) görünmez kalır.

## 188.6 — Karar kaydı ve sürüm notu

**Yeni K-\* (numara kapanışta alınır).** 🚨 K-855'ten itibaren her K satırı bir
kategori etiketi taşır: `*(kategori: <değer>)*`, değer `public-api` ·
`güvenlik` (kiracı sınırı dahil) · `kalıcı-veri` (migration dahil) ·
`geri-dönüşü-pahalı`. Yalnız bu dördü K alır. Bu fazın tek yeni K'sı
**`*(kategori: public-api)*`** etiketini taşır:

> DI'ın veya Tracon boru hattının kurduğu public servis tipinin kurucusu
> `internal`'dır; tip public kalabilir. Tip tabanlı DI kaydı olan böyle bir tip
> fabrika kaydı kullanır. Opsiyonel parametreli yeni bir public kurucu yalnız
> tüketicinin kurduğu tipte olur ve ratchet tabanına gerekçeyle girer; taban
> yalnız küçülür. Yeniden açılma koşulu: bir tüketici bu tiplerden birini DI
> dışında kurma ihtiyacını kanıtlarsa (issue, sample), o tip için opsiyonel
> parametresiz bir yol tasarlanır.

**Mevcut satırlara not (yeni K değil).** **K-614**: "Yeniden açılma koşulu Faz
188'de gerçekleşti (iki kurucu internal). `IToolRegistry` bilerek public kalır
(Faz 188 karar 6)." · **K-850**: "Üye düzeyi dalga (Faz 188): 19 aday, sonuç
`<n>` internal / `<m>` gerekçeli."

**K almayan yerel kararlar** "Bu Fazda Verilen Kararlar"a gider: taban
dosyasının biçimi, testlerin yeri, Açık Soru 2 ve 3'ün sonucu.

**Hafıza.** `aspnetcore-di.md`: "Tip tabanlı kayıtlı tipin kurucusu internal
olursa MS DI çalışma anında düşer; derleme yakalamaz. Primary constructor
erişim belirleyicisi almaz." `analyzer-tanilari.md` "Public yuzey daraltma
tarifi": üye düzeyi (kurucu) adımı ve ratchet kapısının adı.

**Sürüm notu (`CHANGELOG.md` `[Unreleased]` → `Removed`, İngilizce).** Bir
madde 15 tipi listeler, kurucunun neden kalktığını bir cümleyle söyler ve bir
geçiş örneği verir:

```csharp
// Before (1.0.0-preview.1 / preview.2)
var registry = new ModelProviderRegistry([provider]);

// After: resolve the service Tracon registers
var services = new ServiceCollection();
services.AddTracon().AddModelProvider(provider);
using var serviceProvider = services.BuildServiceProvider();
var registry = serviceProvider.GetRequiredService<IModelProviderRegistry>();
```

Dalgada `internal` olan her tip de aynı bölümde adıyla geçer (Faz 182 emsali).
🚨 Eksik ad **hatadır**, rapor değil. Faz 187 kapısı kaldırılan her üyeyi
(kurucu: `CP0002`) bildiren tipe indirger ve notlarda arar (`docs/187:387,503`).
Notta adıyla geçmeyen kırılmış tip her koşumu kırar, `release-dryrun` dahil
(Faz 187 karar 2, `docs/187:123`). Her tip **tam adıyla** yazılır; joker
yoktur (karar 5, `docs/187:126`).

## 188.7 — Kapsam dışı

| Kalem | Neden |
|---|---|
| `TraconToolRegistration`, `SourceWriter.cs:106-114` üreteç çıktısı, `ITraconBuilder` | Faz 189 (F-271 B) |
| `IAuditDecorated` | Karar 5 |
| Opsiyonel parametreli public metotlar | Karar 4 |
| `AgentRunBudget`, `FakeModelProvider`, `TraconAgentSourceException` | Tüketici kurar veya istisna deyimidir; tabanda kalır (188.2) |

---

## Planlanan Public API

> Taslak imzalar; gerçekleşenler kapanışta yazılır. Yeni public üye yok; net
> değişim **negatiftir**.

```csharp
// Tracon.Core — tip public kalır, kurucu internal olur (15 tipte aynı desen)
public sealed partial class RunRecordingAgent : DelegatingAIAgent
{
    internal RunRecordingAgent(AIAgent innerAgent, IRunStore runStore, /* ... 24 parametre, imza aynı */);
}

// Primary constructor → açık internal kurucu (QuotaEnforcer, RunSampler)
public sealed class RunSampler
{
    internal RunSampler(
        IJobStore jobStore,
        IOptionsMonitor<OnlineEvaluationOptions> optionsMonitor,
        TimeProvider? timeProvider = null,
        ILogger<RunSampler>? logger = null);
}

// Registration.Storage.cs:216 — tip tabanlı kayıt fabrika kaydına döner
services.TryAddSingleton(static provider => new RunSampler(
    provider.GetRequiredService<IJobStore>(),
    provider.GetRequiredService<IOptionsMonitor<OnlineEvaluationOptions>>(),
    provider.GetService<TimeProvider>(),
    provider.GetService<ILogger<RunSampler>>()));
```

Açık Soru 1 = A ise `RunEventWriter`'ın `StartAsync`, `CompleteAsync`,
`RecordToolInvocationAsync`, `CompleteLateToolInvocationAsync` metotları da
`internal` olur (`Core/PublicAPI.Unshipped.txt:290,291,294,297`).

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Core/
  Recording/RunRecordingAgent.cs · RunEventWriter.cs
  Compilation/AgentDefinitionCompiler.cs
  Diagnostics/TraconDiagnosticsCollector.cs · TraconMetrics.cs
  Models/ModelProviderRegistry.cs · ModelProviderHealthCache.cs · ModelProviderCircuitBreaker.cs
  Skills/Scripts/SandboxedSkillScriptRunner.cs · SkillScriptSupport.cs
  Graph/ChildAgentInvoker.cs
  Guards/ContentGuardPipeline.cs
  Sessions/AgentSessionManager.cs
  Quotas/QuotaEnforcer.cs  (primary → açık internal kurucu)
  Evaluation/RunSampler.cs  (primary → açık internal kurucu)
  TraconServiceCollectionExtensions.Registration.Storage.cs  (RunSampler fabrika kaydı)
  Properties/AssemblyInfo.cs  (Açık Soru 2 = A ise Tracon.Benchmarks)
  PublicAPI.Unshipped.txt  (15 kurucu + dalga)
src/Tracon.Abstractions/
  PublicAPI.Unshipped.txt  (dalga sonucu)
  Tracon.Abstractions.csproj  (Açık Soru 3 = A ise iki test projesi)
src/**/*.cs  (dalgada internal olan tipler)
tests/Tracon.Core.UnitTests/Architecture/
  PublicSurfaceBaselineTests.cs  (+ ratchet [Fact] ve ayrıştırıcı testleri)
  optional-parameter-constructor-baseline.txt (yeni)
  public-surface-baseline.txt  (dalga kadar küçülür)
tests/Tracon.Core.UnitTests/Configuration/ServiceRegistrationSnapshotTests.cs  (:207)
tests/Tracon.AspNetCore.FunctionalTests/DiConstructedServiceResolutionTests.cs  (yeni)
tests/Tracon.Testing.UnitTests/FakeModelProviderTests.cs  (:104)
bench/Tracon.Benchmarks/RunEventWriterBenchmarks.cs  (yalnız Açık Soru 2 = B ise)
scripts/public-yuzey-gerekceleri.tsv  (dalga sonucu)
CHANGELOG.md · README.md:346 · src/Tracon.Abstractions/README.md:20  (son ikisi yalnız tip daralırsa)
docs/manuel-test/01-KURULUM-VE-PAKETLEME.md · 24-TEST-PAKETI-VE-SABLON.md
docs/hafiza/aspnetcore-di.md · analyzer-tanilari.md
docs/KARARLAR.md  (yeni K + K-614/K-850 notları)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Sınır geçen davranış
> (DI · HTTP · kiracı · akış · depo · paket) birim testiyle kanıtlanamaz —
> [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

🚨 `kapi.py yayin --kuru` kirli ağaçta koşmaz (`scripts/kapi.py:1353-1361`,
`hafiza/test-altyapisi.md:126`). Commit ise kullanıcı istemedikçe atılmaz
(AGENTS.md). #7, #13, #16 ve Manuel case 1 bu yüzden iki yoldan biriyle koşar:
kullanıcıdan commit izni alınır, ya da scratch worktree'de yerel bir commit
üzerinde koşulur (Faz 187 manuel case 2 deseni, `docs/187:694`).

`DiConstructedServiceResolutionTests` = yeni sınıf,
`tests/Tracon.AspNetCore.FunctionalTests/`. `Infrastructure/` = aynı projenin
mevcut yardımcıları.

| # | Ne bozulabilir | Seviye | Test |
|---|---|---|---|
| 1 | `RunSampler` kurucusu internal, kayıt tip tabanlı kalır → ilk `run`da `InvalidOperationException`; derleme yeşil | Fonksiyonel (DI) | `DiConstructedServiceResolutionTests`: `AddTracon()` + `UseSkillScripts()` host'u 12 DI tipini `GetRequiredService` ile çözer (`ModelProviderRegistry` `IModelProviderRegistry` üzerinden) |
| 2 | Fabrika `TimeProvider`'ı unutur → saat sessizce `TimeProvider.System` (`aspnetcore-di.md:27`) | Fonksiyonel (DI) | Aynı sınıf. `Infrastructure/ManualTimeProvider.cs` (internal, `Advance(TimeSpan)`) kayıtlı; `Enabled = true`, `SampleRate = 1`, `MaxScoresPerHour = 1`. İkinci örnekleme reddedilir; saat bir saat ilerler; üçüncüsü kabul edilir. `Microsoft.Extensions.TimeProvider.Testing` **eklenmez** |
| 3 | Fabrika `RunSampler`'ı iki kez kurar → iki saatlik pencere, kiracı bütçesi iki kat (eşzamanlılık) | Fonksiyonel (DI) | Aynı sınıf: iki çözüm aynı örneği döner (`ShouldBeSameAs`) |
| 4 | Alt sistem hatası: `IJobStore.EnqueueAsync` atar; fabrika `ILogger<RunSampler>`'ı unutmuşsa hata iz bırakmaz, ya da istisna `run`a sızar | Fonksiyonel (DI) | Aynı sınıf. `EnqueueAsync`'i atan bir `IJobStore` ve `Infrastructure/RecordingLoggerProvider.cs` kayıtlı. DI'dan çözülen `SampleAsync` `false` döner, atmaz; log `Warning Tracon.RunSampler` satırını taşır (`RunSampler.cs:102-113`); aynı host'ta bir `run` `Completed` biter |
| 5 | Primary constructor dönüşümü bir parametreyi veya saat varsayılanını kaybeder | Birim + Fonksiyonel | Mevcut `QuotaEnforcer*`/`RunSampler*` testleri (Core.UnitTests) · `QuotaEndpointTests:126` (`GetRequiredService<QuotaEnforcer>()`) |
| 6 | Boru hattının kurduğu `RunRecordingAgent`, `RunEventWriter`, `ChildAgentInvoker` gerçek `run`da kurulamaz | Fonksiyonel + örnek uygulama | Mevcut `AgentDelegationTests` · `AgentCallGraphTests`; `samples/Tracon.Api` `router` → `support` (DoD) |
| 7 | Paketlenmiş tüketici artık olmayan public kurucuyu arar; IVT'li testler bunu gizler | Paket | `kapi.py yayin --kuru` (6 dış sample paketlenmiş sürüme karşı; temiz ağaç) + §2 grep'i boş |
| 8 | Karışık sürüm: Workflows@N, Core@N+1'deki internal kurucuyu bulamaz | Paket | Faz 185'in tam sürüm kapısı; bu faz yeni test yazmaz |
| 9 | DI ile yeniden yazılan `FakeModelProviderTests` registry'nin tool döngüsünü artık kanıtlamaz (test tiyatrosu) | Birim | Test ham `provider.CreateChatClient` ile bir kez koşulur ve **kırmızı** görülür; sonuç dokümana yazılır |
| 10 | Ratchet ayrıştırıcısı bir biçimi kaçırır (188.5 tuzakları) | Birim | `PublicSurfaceBaselineTests` sentetik metin testleri (sınır yok) |
| 11 | Ratchet sessizce büyür: yeni/bayat girdi, kısa gerekçe, eksik/boş dosya (boş/aşırı girdi) | Kapı (mimari test) | Yeni `[Fact]`; bir kez sentetik opsiyonel kurucuyla kırmızıya düşürülür, çıktı dokümana yazılır |
| 12 | Dalgada internal olan tip başka paketin gövdesinde IVT'siz kullanılır | Derleme kapısı | `CS0122` — `kapi.py kapanis` derleme adımı |
| 13 | Dalgada internal olan tipi bir tüketici gerçekten kullanıyordu | Paket | #7'nin koşumu; derlemesi bozulan sample = tip tüketici yüzeyidir, tip kalır, envanter düzelir (K-850 emsali) |
| 14 | Envanter sonunda kanıtsız > 0 kalır | Script | `public-yuzey-envanteri.py --denetle` çıkış 0 |
| 15 | Site API referansı silinen kurucuya veya internal tipe bağ verir | Site | `npm run build` + `check-links.mjs` |
| 16 | Sürüm notu kaldırılan bir tipi atlar veya jokerle anar | Paket (kapı) | Faz 187 kapısı, `kapi.py yayin --kuru` (temiz ağaç): eksik ad **hatadır**, çıkış 1 (Faz 187 karar 2 ve 5) |
| 17 | `RunEventWriter.cs` ve `bench/` değişikliği tahsisi kaydırır | Kapı | `kapi.py performans` (`PERFORMANCE_HOT_PATHS`, `kapi.py:437-442`; kapanışta tetiklenir) |

**Beş soru.** Eşzamanlılık #3, boş/aşırı girdi #11, alt sistem hatası #4'tedir.
Kalanlar:

- **İptal:** Yeni kod yolu `CancellationToken` almaz; değişiklik erişim ve kayıt
  biçimidir. Mevcut iptal testleri regresyon görevi görür.
- **Başka kiracı:** Kiracı yolu değişmez. `RunSampler` penceresi kiracı
  anahtarlıdır (`RunSampler.cs:168`); #3 tek örneği kanıtlar, mevcut
  `RunSampler` testleri kiracı ayrımını koşar.
- **Çözüm hatası:** `IJobStore` çözülemezse fabrikanın `GetRequiredService`'i
  bugünkü kayıtla aynı `InvalidOperationException`'ı verir. #1 varsayılan
  kayıtla çözümü kanıtlar.

---

## Manuel Kabul Case'leri

> Kapanışta `01-KURULUM-VE-PAKETLEME.md` ve `24-TEST-PAKETI-VE-SABLON.md`
> içine eklenir. ID'ler kapanışta alınır.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Daraltma ve dalga bitti; **temiz ağaç** (kullanıcı commit izni veya scratch worktree'de yerel commit) | `python3 scripts/kapi.py yayin --kuru` | 6 dış sample paketlenmiş sürüme karşı derlenir ve koşar; Faz 187 kapısı çıkış 0, eksik tip adı yok |
| 2 | Paketlenmiş `Tracon.Testing` ile konsol projesi (`MT-TEST-027` ön koşulu) | `MT-TEST-027`'nin yeni DI kodunu koş | Çıktı `Sonuc: hazirlaniyor (ORD-7)`; kodda `new ModelProviderRegistry(` yok |
| 3 | Paketlenmiş `Tracon.Core`'a bağlı atılabilir konsol projesi | `var r = new ModelProviderRegistry([]);` yazıp `dotnet build` | `CS0122` (koruma düzeyi) — kurucu tüketiciye kapalı |
| 4 | Envanter | `python3 scripts/public-yuzey-envanteri.py` | Kanıtsız sütunu 0; toplam tip sayısı dalga sonucuyla tutarlı |
| 5 | `samples/Tracon.Api` ayakta (`http://localhost:5080`, `launchSettings.json:8`) | Doğrulama komutları §6 | `200` + rapor (`TraconDiagnosticsCollector`) · `200` + sağlayıcı listesi (`ModelProviderHealthCache`) · SSE `done`; `router` `support`'u çağırır (`ChildAgentInvoker`); `run` `Completed` |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `AgentRunScope.Writer` (`Core/PublicAPI.Unshipped.txt:60`) tüketiciye bir `RunEventWriter` verir. Yaşam döngüsü metotları (`StartAsync`, `CompleteAsync` 8/7, `RecordToolInvocationAsync`, `CompleteLateToolInvocationAsync`) `internal` olur mu? (karar 8). Tüketici kanıtı: `concepts/runs.md:146-150`, `samples/Tracon.Api/OrderTools.cs:122-130`. Workflows çağrısı: `WorkflowRunner.cs:481,1113` | A: olur; tüketiciye `AppendAsync` ve okuma özellikleri (`RunId`, `TenantId`, `EventCount`, `IsDisabled`) kalır · B: public kalır, GA'da donar | **A** — tüketici kanıtı yalnız `AppendAsync`'tir ve Workflows iki metodu mevcut IVT ile çağırır. |
| 2 | IVT'siz benchmark `new RunEventWriter(...)` çağırır (`RunEventWriterBenchmarks.cs:21`). `Tracon.Benchmarks` çözümdedir (`Tracon.slnx:59`); derleme kapısı onu derler | A: Core → `Tracon.Benchmarks` IVT · B: benchmark writer'ı boru hattı veya DI ile kurar | **A** — emsal `Tracon.Sqlite.csproj:64-71`'dir; B ölçülen yolu değiştirir ve `bench/baseline.json` tahsisini kaydırabilir. |
| 3 | `SqlPersistenceRegistrationMarker` `internal` olursa iki test projesi onu IVT'siz kurar: `AspNetCore.FunctionalTests/HealthCheckTests.cs:141,142,160` · `PostgreSql.IntegrationTests/ServiceRegistrationTests.cs:220,247`. Yalnız yargıç tipi `internal` bulursa açılır | A: Abstractions → iki test projesi IVT · B: testler işareti gerçek `Use*` kaydıyla üretir | **A** — K-850 test projelerine IVT verdi; B iki gerçek sağlayıcı kaydı ister. |
| 4 | Kurucusu kalkan public tiplerin XML `<remarks>`'ı "nasıl elde edilir" cümlesi taşısın mı? | A: evet, tip başına bir cümle (DI: `GetRequiredService`; boru hattı: nereden gelir) · B: hayır | **A** — kurucusuz tip API referansında yol göstermez; bedel en çok 15 İngilizce cümledir. |

---

## Bitiş Ölçütleri (DoD)

- [x] DoD §1 komutu yalnız 4 satır basar: `AgentRunBudget 2/2` · `TraconAgentSourceException 4/1` · `TraconToolRegistration 8/7` · `FakeModelProvider 1/1`; toplam public kurucu sayısı yazıldı (482'den en az 15 düşer) — **ölçüldü: 4 satır, 482 → 457** (15 kurucu + dalgada `internal` olan tiplerin 10 kurucusu)
- [x] `wc -l src/*/PublicAPI.Shipped.txt` hâlâ 17 × 1 satır; 15 kurucu satırı Core `Unshipped`'te yok — `17 total`
- [x] Ratchet `[Fact]` yeşil; taban 4 satır, her gerekçe ≥ 40 karakter; kapı bir kez mutasyonla kırmızıya düştü, çıktı bu dokümanda — mutasyon (`TraconMetrics` kurucusu public + Unshipped satırı): `+ Tracon.Core:Tracon.TraconMetrics(2/2): new public constructor with optional parameters - this needs a public API decision`
- [x] `DiConstructedServiceResolutionTests` yeşil: 12 DI tipi çözülür; `RunSampler` tek örnek; kayıtlı `ManualTimeProvider` saatlik pencereyi döndürür; atan `IJobStore` ile `SampleAsync` `false` döner, `Warning Tracon.RunSampler` yazılır, `run` `Completed` biter — 4/4; mutasyonla 2/4 ve 0/4 kırmızı (Testler tablosu)
- [x] `ServiceRegistrationSnapshotTests` satır 207 `Factory` bekler ve yeşil (satır 211'e kaymıştı)
- [x] `FakeModelProviderTests` DI ile; `git grep -n "new ModelProviderRegistry(" -- tests/Tracon.Testing.UnitTests` boş; `Tracon.Testing.UnitTests`'e IVT eklenmedi; test ham istemciyle bir kez kırmızı gösterildi — `AddLogging()` gerekmedi
- [x] `public-yuzey-envanteri.py --denetle` → çıkış 0; 19 adayın yargıç + şüpheci sonucu "Gerçekleşen Public API"de; `PublicSurfaceBaselineTests` tip tabanı yenilendi — 654 tip, kanıtsız 0; Core 79, Abstractions 404
- [x] §2 tüketici yüzeyi grep'i boş; `MT-TEST-027` yeni koduyla paketlenmiş `Tracon.Testing`'e karşı koşuldu — çıktı `Sonuc: hazirlaniyor (ORD-7)` (`1.0.0-preview.2.61`)
- [x] `CHANGELOG.md` `Removed`: 15 tip + dalgada internal olan her tip, tam adla + geçiş örneği; `kapi.py yayin --kuru` temiz ağaçta (commit izni veya scratch worktree) çıkış 0, Faz 187 kapısı dahil — commit `c10d0032`: `✅ Kırıcı liste: 119 tip, 0 TFM düşüşü, 10 paket — hepsi 'Unreleased' notunda`
- [x] 188.6'daki üç karar kaydı `docs/KARARLAR.md`'de; `aspnetcore-di.md` ve `analyzer-tanilari.md` güncel — K-866 + K-614/K-850 notları
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` (tahsis kapısı `RunEventWriter.cs` ve `bench/` yüzünden tetiklenir ve yeşildir)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — komut ve beklenen çıktı: Doğrulama komutları §6; ortam: `docs/hafiza/elle-kosum-ortami.md` — "Örnek Uygulama Koşumu"
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` — `kapi.py kapanis` ilk adımı
- [x] Manuel kabul case'leri `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` ve `24-TEST-PAKETI-VE-SABLON.md` içine eklendi; otomatikleştirilebilenler koşuldu — `MT-PKG-145`…`148` ✅ · `MT-TEST-027` yeniden yazıldı ✅
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 🔴 0; 🟡 1 düzeltildi; 🟢 1 gerekçelendi ("Denetim Bulguları")
- [x] `docs-site/` API referansı yeniden üretildi; `npm run build` + `check-links.mjs` temiz; `llms-full.txt` güncel. Dalga tip daralttıysa `README.md:346` ve `src/Tracon.Abstractions/README.md:20` güncel — `npm run check` çıkış 0 (1041 sayfa, 0 kırık); agent haritası güncel; 673 → 654, 407 → 404

### Doğrulama komutları

```bash
# §1 — opsiyonel parametreli public kurucular (bugün: 482 kurucu, 19 satır)
# Çalıştığın ağacın kökünde koşar (ana checkout veya worktree)
cd "$(git rev-parse --show-toplevel)" && python3 - <<'EOF'
import glob, re
toplam = 0
for f in sorted(glob.glob("src/*/PublicAPI.Unshipped.txt")):
    for n, l in enumerate(open(f), 1):
        m = re.match(r"^(?:[\w.]+\.)?(\w+)(?:<[^>]*>)?\.\1\((.*)\) -> void$", l.strip())
        if not m: continue
        toplam += 1
        d, p = 0, [""]
        for c in m.group(2):
            d += c in "<([" ; d -= c in ">)]"
            if c == "," and d == 0: p.append("")
            else: p[-1] += c
        o = sum(" = " in x for x in p)
        if o: print(f"{f}:{n} {m.group(1)} {len(p)}/{o}")
print("public kurucu:", toplam)
EOF

# §2 — tüketici yüzeyinde 15 kurucuya çağrı (beklenen: boş)
git grep -nE "new (RunRecordingAgent|AgentDefinitionCompiler|TraconDiagnosticsCollector|ModelProviderRegistry|SandboxedSkillScriptRunner|ChildAgentInvoker|ContentGuardPipeline|RunEventWriter|AgentSessionManager|QuotaEnforcer|ModelProviderHealthCache|RunSampler|SkillScriptSupport|ModelProviderCircuitBreaker|TraconMetrics)\s*\(" \
  -- docs-site samples 'src/*/README.md' README.md src/Tracon.Templates docs/manuel-test

# §3 — envanter
python3 scripts/public-yuzey-envanteri.py --liste kanıtsız
python3 scripts/public-yuzey-envanteri.py --denetle; echo "çıkış=$?"

# §4 — hedefli testler
python3 scripts/kapi.py test --proje Tracon.Core.UnitTests --sinif "*PublicSurfaceBaselineTests*" "*ServiceRegistrationSnapshotTests*"
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*DiConstructedServiceResolutionTests*"

# §5 — paketlenmiş tüketici + kırıcı değişiklik kapısı (temiz ağaç: commit izni
#      veya scratch worktree'de yerel commit), sonra kapanış
python3 scripts/kapi.py yayin --kuru
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>
python3 scripts/kapi.py tarama

# §6 — örnek uygulama (port: samples/Tracon.Api/Properties/launchSettings.json:8)
dotnet run --project samples/Tracon.Api
curl -s http://localhost:5080/tracon/api/diagnostics        # 200 — TraconDiagnosticsCollector
curl -s http://localhost:5080/tracon/api/models/health      # 200 — ModelProviderHealthCache
curl -N -X POST http://localhost:5080/tracon/api/agents/router/run \
     -H 'Content-Type: application/json' \
     -d '{"message":"Where is order 4182?"}'                # SSE: run … done; router support'u çağırır
curl -s http://localhost:5080/tracon/api/runs/<runId>       # status: Completed
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Aynı turdaki kusur-giderme işleri `QuotaEnforcer.cs`, `TraconMetrics.cs` ve kayıt dosyalarını değiştirdi; satır numaraları kayar | 188.3 Adım 0: `git log` + DoD §1; fark "Plandan Sapmalar"a yazılır |
| Yargıç bir tüketicinin kullandığı tipi `internal` yapar | Şüpheci + paketlenmiş sample'lar (#13); kanıt bulunursa tip public'e döner (K-850) |
| preview.1/preview.2 ile derlenmiş üçüncü taraf kütüphane kalkan kurucuyu çağırırsa `MissingMethodException` alır | Karar 7: pre-1.0 politikası (`versioning.md:15-17`) izin verir; sürüm notu geçiş örneği verir. Site/sample/şablonda çağrı 0 |
| Ratchet tabanı "yenileme" ile büyütülür | Yenileme yalnız siler; yeni satır elle eklenir ve K-* anar (188.5) |
| Faz 189 `TraconToolRegistration` satırını silmeyi unutur | Gerekçe Faz 189'u adlandırır; imza değişince kapı bayat girdiyi kırmızı yapar |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Örnek Uygulama Koşumu

2026-09-24, `samples/Tracon.Api` Development, `--urls http://127.0.0.1:5199`
(`docs/hafiza/elle-kosum-ortami.md` tarifi), PostgreSQL, gerçek sağlayıcılar.

| Çağrı | Sonuç | Kanıtladığı |
|---|---|---|
| `GET /tracon/api/diagnostics` | `200` — `persistenceProvider: PostgreSQL`, `canConnect: true`, 5 sağlayıcı | `TraconDiagnosticsCollector` DI'dan kurulur |
| `GET /tracon/api/models/health` | `200` — `anthropic` `Healthy` (0,78 sn), `google` `Healthy` | `ModelProviderHealthCache` |
| `POST /tracon/api/agents/router/run` `{"message":"Where is order 4182?"}` | SSE `run` · 140 `update` · `done`; run `01a0d2a6-…` | `RunRecordingAgent`, `RunEventWriter` |
| `GET /tracon/api/runs/01a0d2a6-…` | `status: Completed`, `router`, `openai/gpt-5.4-mini`, `depth: 0` | Kayıt tamam |
| `GET /tracon/api/runs?parentRunId=01a0d2a6-…` | `support` · `Completed` · `parentRunId` = kök | `ChildAgentInvoker` alt run'ı ağaca bağlar |

İlk deneme `401` verdi: örnek uygulama `Tracon:Ui:AuthToken` taşır; token
user-secrets'tan okunup yalnız `Authorization` başlığına verildi (dosyaya
yazılmadı).

## Plandan Sapmalar

Ölçüm 2026-09-24, taban `926096d4`. Adım 0: `bb9953e3..HEAD` üç commit bu
dosyalara dokunmuştu; DoD §1 yine **482 / 19** verdi, 15 satırlık tablo
değişmedi.

| # | Plan | Gerçek | Gerekçe |
|---|---|---|---|
| 1 | Açık Soru 3 = A: Abstractions → **iki** test projesi IVT | **Üç** proje: `Tracon.AspNetCore.FunctionalTests`, `Tracon.PostgreSql.IntegrationTests`, `Tracon.Sql.Shared.UnitTests` | Şüpheci üçüncüyü buldu: `SqlProviderRegistrationParityTests.cs:119` `typeof(SqlPersistenceRegistrationMarker)` kullanır |
| 2 | Manuel case 3: paketlenmiş tüketici `CS0122` alır | `CS1729` (`does not contain a constructor that takes 1 arguments`) | Referans derlemesi `internal` kurucuyu taşımaz; kurucu aday bile olmaz. Proje referanslı IVT'siz test de `CS1729` verdi (`FakeModelProviderTests`, ilk derleme). `MT-PKG-146` ve `MT-TEST-027` metni buna göre yazıldı |
| 3 | `MT-TEST-027` ön koşulu `Microsoft.Extensions.DependencyInjection` paketini ayrıca ister | Eklenmez | Açık `10.0.0` referansı `NU1605` (downgrade) verdi; `Tracon.Testing` DI kabını geçişli getirir (`Microsoft.Agents.AI.Hosting` → `>= 10.0.11`) |
| 4 | K-850 dalgası "en çok 19 tip" | **19'u da** `internal` (0 gerekçeli) | Yargıç ve bağımsız şüpheci ayrı ayrı tüketici yolu bulamadı. Tip tabanı Core 95 → 79, Abstractions 407 → 404; envanter 673 → 654 |
| 5 | Plan dışı | Public XML dokümandaki iki `cref` yeniden yazıldı: `TraconDiagnosticsCollector` → `ModelProviderHealthCache`, `RunEventWriter.TenantId` → `StartAsync` | Hedefler `internal` oldu; API referansında çözülmeyen bağ bırakmamak için düz metne döndü |
| 6 | AS 4 = A: "en çok 15 cümle" | 7 cümle | 15 tipin 8'i dalgada `internal` oldu; kurucusu kalkan ve public kalan 7 tip (`RunRecordingAgent`, `AgentDefinitionCompiler`, `TraconDiagnosticsCollector`, `ModelProviderRegistry`, `ChildAgentInvoker`, `RunEventWriter`, `AgentSessionManager`) cümle aldı. `IAgentCatalog.ResolveAsync` cref'i iki aşırı yükleme yüzünden `CS0419` verdi; `IAgentCatalog`'a indirildi |
| 7 | `bench/…/RunEventWriterBenchmarks.cs` yalnız AS 2 = B ise değişir | Değişmedi (AS 2 = A) | Core → `Tracon.Benchmarks` IVT (`Properties/AssemblyInfo.cs`) |
| 8 | `DiConstructedServiceResolutionTests` `AddTracon()` + `UseSkillScripts()` | `UseSkillScripts` seçenek ister | `PlatformIsolationAcknowledged = true` ve bir yorumlayıcı olmadan açılış doğrulaması düşer (`SkillScriptContentPinTests` deseni) |

`internal` olan sekiz tipin kurucusu da `internal` yazılı kaldı (tip içinde
gereksiz ama zararsız); ratchet onları zaten görmez. Dalgada `internal` olan
üç tip (`CallableAgentResolver`, `RunTraceCollector`, `ToolApprovalPresenterRunner`)
tip tabanlı kayıtlıdır; kurucuları `public` anahtar sözcüğünü korur, MS DI onları
kurar (denetçi doğruladı).

### Tüketici yüzeyi envanteri (`tuketici-dokuman-senkronu`)

| Kova | Yüzey | Sonuç |
|---|---|---|
| `docs-site/` elle | Yok | §2 grep'i boş; site içeriği (üretilen `api/` hariç) 19 tipin ve 4 metodun hiçbirini anmaz (şüpheci taraması). `write-your-own-agent-source.md:30` `GetRequiredService<AgentDefinitionCompiler>()` yazar — geçerli kalır |
| `docs-site/` üretilen | `api/` (DocFX), `reference/changelog.md` | `npm run check` yeniden üretti: 1041 sayfa, 171 408 iç bağlantı, 0 kırık; ağırlık tavanı altında |
| Sevk edilen metin | 7 tipin `<remarks>` cümlesi · 2 `cref` yeniden yazımı · `CHANGELOG.md` `Removed` · `README.md:346` (673 → 654) · `src/Tracon.Abstractions/README.md:20` (407 → 404) | `ShippedDocumentationSelfContainmentTests` · `CapabilityExampleTests` · `SourceLanguageTests` yeşil |
| Agent haritası / `capabilities.md` | Değişmez (`:120` `AgentSessionManager`'ı anar; tip public) | `build-agent-map.mjs --check`: up to date |

**`--site-denetle` gerekçesi (`--site-gerekce-yazildi`):** üç kural tetiklendi —
`cekirdek-kavram` (`ISqlPersistenceDiagnostics.cs`), `paket-tanimi`
(`Tracon.Abstractions.csproj`), `paket-readme` (`src/Tracon.Abstractions/README.md`).
Üçü de görünürlük/IVT/sayı değişimidir: `concepts/` ve `packages.md` ne
`SqlPersistenceRegistrationMarker`'ı, ne IVT listesini, ne tip sayısını anar
(`grep` boş). Hedef sayfada değişecek cümle yoktur.

## Bu Fazda Verilen Kararlar

**K-866** *(kategori: public-api)* — DI'ın veya boru hattının kurduğu public
servis tipinin kurucusu `internal`; tip tabanlı kayıt fabrikaya döner;
opsiyonel parametreli public kurucu yalnız tüketicinin kurduğu tipte ve
gerekçeli ratchet tabanında olur.

**Mevcut satırlara not:** K-614 (koşul gerçekleşti, `IToolRegistry` public
kalır) · K-850 (üye düzeyi dalga: 19 `internal` / 0 gerekçeli).

**Açık sorular (kullanıcı kararı, 2026-09-24):** AS 1 = A (`RunEventWriter`
yaşam döngüsü metotları `internal`) · AS 2 = A (Core → `Tracon.Benchmarks`
IVT) · AS 3 = A (Abstractions → test projesi IVT; sapma #1) · AS 4 = A
(`<remarks>` cümlesi).

**K almayan yerel kararlar:**

- Taban dosyası biçimi `<paket>:<tip>(<param>/<ops>) | <gerekçe>`; sayılar
  anahtarın parçasıdır. Ayrıştırıcı `*REMOVED*` satırlarını `Shipped`'ten düşer,
  dize varsayılanındaki `,`/`=`/`(` karakterlerini sayım dışı tutar.
- Ratchet ayrı partial dosyadadır
  (`PublicSurfaceBaselineTests.OptionalConstructors.cs`); aynı sınıfın
  `RepositoryRoot`'unu kullanır, ikinci okuyucu yazılmadı.
- `RunSampler` fabrikası `QuotaEnforcer` desenini izler: zorunlu bağımlılık
  `GetRequiredService`, opsiyonel olan `GetService`.

## Gerçekleşen Public API

Yeni public üye yok; net değişim **negatif**. `wc -l src/*/PublicAPI.Shipped.txt`
→ 17 × 1 satır (değişmedi).

```csharp
// 7 tip public kalır; kurucu internal (imza değişmedi)
public sealed partial class RunRecordingAgent : DelegatingAIAgent { internal RunRecordingAgent(/* 24 parametre */); }
public sealed partial class AgentDefinitionCompiler { internal AgentDefinitionCompiler(/* 20 */); }
public sealed class TraconDiagnosticsCollector { internal TraconDiagnosticsCollector(/* 17 */); }
public sealed class ModelProviderRegistry : IModelProviderRegistry { internal ModelProviderRegistry(/* 14 */); }
public sealed class ChildAgentInvoker : AIAgent { internal ChildAgentInvoker(/* 8 */); }
public sealed class AgentSessionManager { internal AgentSessionManager(/* 5 */); }
public sealed class RunEventWriter
{
    internal RunEventWriter(/* 6 */);
    // public kalan: AppendAsync, RunId, TenantId, IsDisabled, EventCount
    internal ValueTask StartAsync(...);                       // AS 1 = A
    internal ValueTask CompleteAsync(...);
    internal ValueTask RecordToolInvocationAsync(...);
    internal ValueTask<bool> CompleteLateToolInvocationAsync(...);
}

// Dalgada internal olan 8 tip de açık/primary kurucuyu internal taşır;
// QuotaEnforcer ve RunSampler primary constructor'dan açık kuruculara döndü.
services.TryAddSingleton(static provider => new RunSampler(      // Registration.Storage.cs
    provider.GetRequiredService<IJobStore>(),
    provider.GetRequiredService<IOptionsMonitor<OnlineEvaluationOptions>>(),
    provider.GetService<TimeProvider>(),
    provider.GetService<ILogger<RunSampler>>()));
```

Core `Unshipped`'ten 19 satır (15 kurucu + 4 metot), dalgada Core'dan 107 ve
Abstractions'tan 65 satır silindi (172 — plan simülasyonuyla aynı).

**K-850 dalgası — yargıç + şüpheci sonucu (19 aday):**

| Tip | Paket | Yargıç | Şüpheci | Sonuç | Gövde kullanımı (IVT) |
|---|---|---|---|---|---|
| `QuotaDecision` | Abstractions | internal | katılır | internal | Core, AspNetCore |
| `QuotaThresholdCrossing` | Abstractions | internal | katılır | internal | Core |
| `SqlPersistenceRegistrationMarker` | Abstractions | internal | katılır (public kalması zararlı: `SchemaReadyGate` hiç açılmaz) | internal | 3 SQL paketi, Core; 3 test projesine yeni IVT |
| `AgentSkillCatalog` | Core | internal | katılır | internal | AspNetCore |
| `CallableAgentResolver` | Core | internal | katılır | internal | AspNetCore, Workflows |
| `ContentGuardPipeline` | Core | internal | katılır | internal | — |
| `ModelProviderCircuitBreaker` | Core | internal | katılır | internal | — |
| `ModelProviderHealthCache` | Core | internal | katılır | internal | AspNetCore |
| `ProviderConcurrencyLimiter` | Core | internal | katılır | internal | — |
| `QuotaEnforcer` | Core | internal | katılır | internal | AspNetCore, Workflows |
| `RunSampleRequest` | Core | internal | katılır | internal | — |
| `RunSampler` | Core | internal | katılır | internal | — |
| `RunTraceCollector` | Core | internal | katılır | internal | Workflows |
| `SandboxedSkillScriptRunner` | Core | internal | katılır | internal | — |
| `SkillScriptSupport` | Core | internal | katılır | internal | — |
| `TenantProviderCredentialResolver` | Core | internal | katılır | internal | AspNetCore |
| `ToolApprovalPresenterRunner` | Core | internal | katılır | internal | — |
| `TraconLoopEvaluatorRegistration` | Core | internal | katılır | internal | — (`AddLoopEvaluator` sarar) |
| `TraconMetrics` | Core | internal | katılır | internal | AspNetCore, Workflows, 3 SQL (tüketici `TraconDiagnostics.MeterName`'e abone olur) |

Kanıt taraması: docs-site içerik (üretilen `api/` hariç), `samples/`,
`src/Tracon.Templates`, README'ler, `src/Tracon.Generators`, `Tracon.Testing*`,
OpenAPI belgeleri — 19 adın hiçbiri tüketici yolunda geçmedi. Yeni gerekçe
(TSV) satırı yok.

## Dosya Listesi (gerçekleşen)

```
src/Tracon.Core/
  Recording/RunRecordingAgent.cs · RunEventWriter.cs     (kurucu/metot internal, <remarks>)
  Compilation/AgentDefinitionCompiler.cs · TraconLoopEvaluatorRegistration.cs
  Diagnostics/TraconDiagnosticsCollector.cs · TraconMetrics.cs · RunTraceCollector.cs
  Models/ModelProviderRegistry.cs · ModelProviderHealthCache.cs · ModelProviderCircuitBreaker.cs · ProviderConcurrencyLimiter.cs
  Skills/AgentSkillCatalog.cs · Skills/Scripts/SandboxedSkillScriptRunner.cs · SkillScriptSupport.cs
  Graph/ChildAgentInvoker.cs · CallableAgentResolver.cs
  Guards/ContentGuardPipeline.cs · Sessions/AgentSessionManager.cs
  Approvals/ToolApprovalPresenterRunner.cs · Tenancy/TenantProviderCredentialResolver.cs
  Quotas/QuotaEnforcer.cs · Evaluation/RunSampler.cs     (primary → açık internal kurucu)
  TraconServiceCollectionExtensions.Registration.Storage.cs  (RunSampler fabrika kaydı)
  Properties/AssemblyInfo.cs                              (Tracon.Benchmarks IVT)
  PublicAPI.Unshipped.txt
src/Tracon.Abstractions/
  Quotas/QuotaTypes.cs · QuotaThresholdCrossing.cs · Diagnostics/ISqlPersistenceDiagnostics.cs
  Tracon.Abstractions.csproj                              (3 test projesi IVT)
  PublicAPI.Unshipped.txt · README.md
tests/Tracon.Core.UnitTests/Architecture/
  PublicSurfaceBaselineTests.cs (partial) · PublicSurfaceBaselineTests.OptionalConstructors.cs (yeni)
  optional-parameter-constructor-baseline.txt (yeni) · public-surface-baseline.txt
tests/Tracon.Core.UnitTests/Configuration/ServiceRegistrationSnapshotTests.cs
tests/Tracon.AspNetCore.FunctionalTests/DiConstructedServiceResolutionTests.cs (yeni)
tests/Tracon.Testing.UnitTests/FakeModelProviderTests.cs
CHANGELOG.md · README.md
docs/manuel-test/01-KURULUM-VE-PAKETLEME.md · 24-TEST-PAKETI-VE-SABLON.md · 00-INDEKS.md
docs/hafiza/aspnetcore-di.md · analyzer-tanilari.md
docs/KARARLAR.md (K-866; K-614/K-850 notları)
```

Değişmeyen (plan listesinde koşullu): `bench/Tracon.Benchmarks/RunEventWriterBenchmarks.cs` (AS 2 = A).

**Testler:**

| Sınıf | Seviye | Test | Doğruladığı |
|---|---|---|---|
| `PublicSurfaceBaselineTests` (ratchet) | Mimari kapı | 1 `[Fact]` | Opsiyonel parametreli her public kurucu tabanda; mutasyon (`TraconMetrics` kurucusu public) → `+ Tracon.Core:Tracon.TraconMetrics(2/2): new public constructor…` kırmızı |
| aynı sınıf (ayrıştırıcı/kapı) | Birim | 8 + 5 teori satırı, 1 + 1 + 2 | Generic tip, generic virgülü, `default(...)`, dize varsayılanı (içinde `, = (`), `~` öneki, iç içe tip; kurucu olmayan satır; `*REMOVED*`; yeni/bayat/kısa/tekrar/biçimsiz girdi; eksik/boş dosya |
| `DiConstructedServiceResolutionTests` | Fonksiyonel (DI) | 4 | 12 tip DI'dan çözülür; `RunSampler` tek örnek; kayıtlı saat penceresi döndürür; atan `IJobStore` → `SampleAsync` `false` + `Warning Tracon.RunSampler`, run `Completed`. Mutasyon: fabrikada saat+logger `null` → 2 kırmızı; tip tabanlı kayıt → 4 kırmızı (`A suitable constructor … could not be located`) |
| `FakeModelProviderTests.EchoesLastToolResult_…` | Birim (DI) | 1 (yeniden yazıldı) | Ham `provider.CreateChatClient` ile koşulunca kırmızı (`ShouldAssertException`) — tiyatro değil |
| `ServiceRegistrationSnapshotTests` | Birim | `Tracon.RunSampler \| Singleton \| Factory` | Kayıt biçimi |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (sapmalar uygulama sırasında yazıldı, plan yeniden açılmadı) |
| Düzeltme turu sayısı | TUR |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 1 — `IAgentCatalog.ResolveAsync` cref'i `CS0419` verdi; örnek uygulama derlemesinde yakalandı, commit'ten önce düzeltildi |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

`faz-denetcisi`, 2026-09-24, kapsam `926096d4...c10d0032` + çalışma ağacı.
**🔴 yok.** Temiz başlıklar: 3.1–3.7; 3.8 kod/test tarafında temiz.

| # | Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | `CHANGELOG.md` `SqlPersistenceRegistrationMarker` gerekçesi yanlış etki söylüyordu ("made start-up wait"). Sahte işaret açılışı bekletmez; `SchemaReadyGate` hiç açılmaz ve arka plan servisleri (`JobWorkerBackgroundService`, `ApprovalExpirationService`) ile MCP/A2A onay filtreleri bekler | 🟡 | — | **Düzeltildi**: cümle "made background services and approval requests wait for a migration that never ran" oldu |
| 2 | DoD §1 betiği ile ratchet ayrıştırıcısı aynı kuralı kullanmaz: betik `~` önekli kurucuyu atlar ve virgül taşıyan dize varsayılanında yanlış böler; ratchet ikisini de doğru okur | 🟢 | — | **Gerekçelendi**: §1 tek seferlik plan ölçümüdür; kalıcı kapı ratchet'tir ve iki biçimi birim testle kilitler. Bugün ikisi aynı 4 satırı verir (`~` önekli kurucu satırı 0). Aday açılmadı — kapatacağı bir tüketici riski yok |

Denetçinin doğruladığı ek noktalar: primary constructor dönüşümünde davranış
kaybı yok (null denetimi eskiden de yoktu); public XML'de `internal` hedefe
sarkan `cref` yok; 7 `<remarks>` cümlesinin dayandığı kayıtlar ve API'ler
gerçek (`TraconRunContext.Current`, `AgentRunScope.Writer`, dört singleton
kaydı, `RunRecordingAgentDecorator`, `AgentDefinitionCompiler.Agents.cs:160-171`).

## Sonraki Faza Devir Notu

**Sıradaki faz: [189](189-TUKETICI-YUZEYI-VE-BUILDER.md)** — `TraconToolRegistration`
ve `ITraconBuilder`.

**Devralınan sözleşmeler:**

- **K-866** (kurucu politikası, `public-api`). 189'un sürüm notu ve karar satırı
  bu numarayı anar.
- Ratchet: `tests/Tracon.Core.UnitTests/Architecture/optional-parameter-constructor-baseline.txt`,
  kapı `PublicSurfaceBaselineTests.Public_constructors_with_optional_parameters_match_the_checked_in_baseline`
  (`PublicSurfaceBaselineTests.OptionalConstructors.cs`). Anahtar
  `<paket>:<tam tip adı>(<param>/<ops>)`. 189 kurucuyu tek zorunlu parametreye
  indirince `Tracon.Abstractions:Tracon.TraconToolRegistration(8/7)` **bayat** olur
  ve kapı kırmızı verir; `TRACON_OPTIONAL_CTOR_REFRESH=1 ./artifacts/bin/Tracon.Core.UnitTests/release_net10.0/Tracon.Core.UnitTests --filter-method "*Public_constructors_with_optional*"`
  satırı siler. Yeni opsiyonel kurucu **elle** eklenir ve K-866'yı anar.
- Taban (4 satır): `AgentRunBudget(2/2)` · `TraconAgentSourceException(4/1)` ·
  `TraconToolRegistration(8/7)` · `Testing.FakeModelProvider(1/1)`.
- Tip tabanı (`public-surface-baseline.txt`): Core 79 · Abstractions 404. Envanter
  toplam 654, kanıtsız 0.

**🚨 Tuzaklar:**

- Paketlenmiş tüketici `internal` kurucuyu **`CS1729`** ile görür, `CS0122` ile
  değil. Manuel case beklentisi buna göre yazılır.
- Tip tabanlı kayıtlı (`TryAddSingleton<T>()`) bir tipin kurucusunu `internal`
  yapmak derlemede değil **ilk çözümde** düşer; fabrika opsiyonel bağımlılığı
  `GetService` ile açıkça geçirir (`hafiza/aspnetcore-di.md`).
- Public XML dokümanda `internal` hedefe `cref` kalmamalı — Faz 188'de iki tane
  yeniden yazıldı. Aşırı yüklenmiş metoda çıplak `cref` `CS0419` verir.
- `kapi.py yayin --kuru` temiz ağaç ister ve Faz 188 sonunda **119 tip** listeler
  (Faz 187 sonunda 95). 189'un kıracağı her tip notta tam adıyla geçmelidir.

**Açık iş:** yok. Site yayını (`faz-tamamlama` Adım 10) bakımcı eylemidir.
