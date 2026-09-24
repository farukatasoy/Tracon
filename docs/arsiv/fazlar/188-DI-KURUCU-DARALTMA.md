# Faz 188 — DI ile Kurulan Servis Tiplerinde Kurucu Daraltması

> **Durum:** ✅ Tamamlandı (2026-09-24)
> **Plan onayı:** Bakımcı, 2026-09-23 (engelleyici kararlar sohbette alındı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-271** — A bölümü. B bölümü (`TraconToolRegistration`, kaynak üreteci, `ITraconBuilder`): [Faz 189](../../189-TUKETICI-YUZEYI-VE-BUILDER.md)
> **Önkoşul:** [Faz 185](185-KARDES-PAKET-SURUM-SABITLEME.md) — kardeş paketleri tam sürüme sabitler. Bu faz `Tracon.Workflows`'un IVT ile çağırdığı iki kurucuyu (`ChildAgentInvoker`, `RunEventWriter`) internal yapar; karışık sürümlü grafta bu bağ ancak o sabitlemeyle güvenlidir · [Faz 187](187-KIRICI-DEGISIKLIK-KAPISI.md) — `kapi.py yayin` kırıcı değişiklik kapısı; kaldırılan her imza oradan geçer · [Faz 186](186-SCRIPT-IZNI-ICERIK-PINI.md) sıra gereği önce kapanır, teknik bağ yok. [Faz 189](../../189-TUKETICI-YUZEYI-VE-BUILDER.md) bu faza bağlıdır
> **Paketler:** `Tracon.Core` (15 kurucu); K-850 dalgasında `Tracon.Abstractions` (3 aday tip). Test ve araç: `tests/Tracon.Core.UnitTests`, `tests/Tracon.AspNetCore.FunctionalTests`, `tests/Tracon.Testing.UnitTests`, `bench/Tracon.Benchmarks`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Daralıyor** — 15 kurucu satırı `src/Tracon.Core/PublicAPI.Unshipped.txt`'ten çıkar; K-850 dalgası en çok 19 tip / 172 satır daha çıkarabilir (188.4). `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosyanın her biri 1 satır (`#nullable enable`): `Shipped` boştur (K-603). Dokunulan her tip için: bugün daraltmak ucuzdur (pre-1.0, `Shipped` boş); GA'dan sonra kırıcıdır
> **Tüketici yüzeyi:** site: elle değişen sayfa yok — site, sample, şablon ve README'de 15 kurucuya `new` çağrısı **0** (§2); `api/` referansı ve `reference/changelog.md` üretilir · sevk edilen: `CHANGELOG.md` `Removed` + geçiş örneği (188.6); XML `<example>` yok (`///.*new <Tip>(` taraması 0); `<remarks>`: Açık Soru 4; paket README'si yalnız dalga tip daraltırsa elle güncellenir (`README.md:346` "673 public types", `src/Tracon.Abstractions/README.md:20` 407 tip / 85 arayüz; zorlayan kapı yok); `capabilities.md` değişmez (`:120` `AgentSessionManager`'ı anar, tip public kalır)
> **Manuel test alanı:** `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` (paketlenmiş tüketici, envanter) · `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` (`MT-TEST-027`, satır 922, `new ModelProviderRegistry(...)` yazar — yeniden yazılır)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 8e061fe3:docs/arsiv/fazlar/188-DI-KURUCU-DARALTMA.md
> ```
>
> Damıtıldı 2026-09-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`Tracon.Core`'da 15 public servis tipi public kurucu taşır. Kurucuların hepsi opsiyonel parametre taşır. Hiçbirini tüketici çağırmaz; tipleri DI fabrikası veya Tracon boru hattı kurar. GA'da (K-603) `Shipped` dolunca bu kurucular donar. Bu faz kurucuları `internal` yapar ve tipleri public bırakır. Kurucu imzası yüzünden public kalan tipleri K-850 ile yeniden yargılar.

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
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` (tahsis kapısı `RunEventWriter.cs` ve `bench/` yüzünden tetiklenir ve yeşildir) — sonuç: "Kapanış Kapısı" bölümü
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

**K-850 dalga sonucu (yargıç + bağımsız şüpheci, 19/19 `internal`, 0 gerekçeli):**
Abstractions — `QuotaDecision`, `QuotaThresholdCrossing`, `SqlPersistenceRegistrationMarker`;
Core — `AgentSkillCatalog`, `CallableAgentResolver`, `ContentGuardPipeline`,
`ModelProviderCircuitBreaker`, `ModelProviderHealthCache`, `ProviderConcurrencyLimiter`,
`QuotaEnforcer`, `RunSampleRequest`, `RunSampler`, `RunTraceCollector`,
`SandboxedSkillScriptRunner`, `SkillScriptSupport`, `TenantProviderCredentialResolver`,
`ToolApprovalPresenterRunner`, `TraconLoopEvaluatorRegistration`, `TraconMetrics`.
Gövde kullanımları mevcut IVT'lerle çözüldü; tip başına tablo tam metindedir.

**K almayan yerel kararlar:**

- Taban dosyası biçimi `<paket>:<tip>(<param>/<ops>) | <gerekçe>`; sayılar
  anahtarın parçasıdır. Ayrıştırıcı `*REMOVED*` satırlarını `Shipped`'ten düşer,
  dize varsayılanındaki `,`/`=`/`(` karakterlerini sayım dışı tutar.
- Ratchet ayrı partial dosyadadır
  (`PublicSurfaceBaselineTests.OptionalConstructors.cs`); aynı sınıfın
  `RepositoryRoot`'unu kullanır, ikinci okuyucu yazılmadı.
- `RunSampler` fabrikası `QuotaEnforcer` desenini izler: zorunlu bağımlılık
  `GetRequiredService`, opsiyonel olan `GetService`.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (sapmalar uygulama sırasında yazıldı, plan yeniden açılmadı) |
| Düzeltme turu sayısı | 1 — denetimin 🟡 bulgusu (CHANGELOG cümlesi); kapanış kapısı ilk denemede yeşil |
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

**Sıradaki faz: [189](../../189-TUKETICI-YUZEYI-VE-BUILDER.md)** — `TraconToolRegistration`
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

## Kapanış Kapısı

`DOTNET_ROOT=~/.dotnet python3 scripts/kapi.py kapanis --taban 926096d4`, commit'li ağaç
`80a60a46`, 2026-09-24 → **EXIT 0, 963 sn**:

| Adım | Süre | Sonuç |
|---|---|---|
| `kapi.py tarama` | 5,8 sn | ✅ temiz |
| `dokuman-bakim.py --denetle` · Python testleri · ajan haritası · denetim paketi | ~9 sn | ✅ |
| `dotnet build Tracon.slnx -c Release` | 95,7 sn | ✅ 0 uyarı |
| `dotnet test … -maxcpucount:2 -- --report-trx` | 588,5 sn | ✅ 17.644 test (38 koşum), 0 kırmızı |
| `dotnet pack` | 8,6 sn | ✅ |
| `dotnet format --verify-no-changes` | 122,3 sn | ✅ |
| `kapi.py performans` | 99,4 sn | ✅ `RunEventWriterBenchmarks.AppendEvent` 112 B (tahsis kapısı `RunEventWriter.cs` yüzünden tetiklendi) |
| `docs-site npm run check` | 33,1 sn | ✅ 1041 sayfa, 0 kırık bağlantı |

