# Faz 7 — Sağlamlaştırma ve Yayın

> **Durum:** 🔜 Sıradaki
> **Önkoşul:** [06-GOZLEMLENEBILIRLIK.md](06-GOZLEMLENEBILIRLIK.md) — tamamlandı
> **Sonraki:** Yok — bu faz 1.0 yayınını kapatır

---

## Bu Faza Başlarken

Önce şunları bu sırayla okuyun:

1. [`MIMARI.md`](MIMARI.md) — bölüm 2 (katmanlar), bölüm 5 (veri modeli), bölüm 7 (güvenlik)
2. [`KARARLAR.md`](KARARLAR.md) — kapatılmış tartışmaları yeniden açmayın
3. [`06-GOZLEMLENEBILIRLIK.md`](06-GOZLEMLENEBILIRLIK.md) — "Gerçekleşen Public API",
   "Plandan Sapmalar" ve **"Faz 7'ye Devreden Notlar"**
4. [`../MEMORY.md`](../MEMORY.md) — önceki oturumların keşfettiği tuzaklar
5. Bu doküman

Skill'ler: `.agents/skills/faz-tamamlama/` (faz sonu protokolü),
`.agents/skills/maf-api-kesfi/` (MAF/MEAI/MCP imzalarını doğrulama).

Çalışan bir arka uç ve arayüz:

```bash
cd samples/AgentPrism.Api && dotnet run
# http://localhost:5080/agentprism
```

---

## Devraldığınız Durum

| Ne | Durum |
|----|-------|
| Paket sayısı | **8** (`Abstractions`, `Core`, `PostgreSql`, `OpenAI`, **`Mcp`**, `AspNetCore`, `UI`, meta) |
| Test | 383 .NET + 40 Vitest, tamamı yeşil |
| Migration | `0001_initial`, `0002_observability` |
| Bundle | 92,4 / 250 KB gzip |
| Doğrulama kapıları | Dördü de sıfır uyarı |

**Faz 6'da eklenen ve Faz 7'yi doğrudan etkileyen şeyler:**

- `AgentPrism.Mcp` yeni bir **yayınlanabilir pakettir**; ikon, README, sürüm
  politikası ve yayın zinciri onu da kapsamalıdır. `AgentPrismAotCompatible` **false**.
- Public API yüzeyi ciddi büyüdü — `PublicAPI.Shipped.txt` dosyaları faz 5 sonuna
  göre belirgin biçimde uzun olacaktır. Tam liste faz 6 dokümanının
  "Gerçekleşen Public API" bölümündedir.
- `run_events` partition kararı **bu fazın yük testine bağlandı** (K-063).

---

## Amaç

Paketi gerçekten yayınlanabilir hâle getirmek. Faz 6 sonunda AgentPrism çalışır ve işletilebilir; bu faz sonunda **başkalarının güvenle bağımlı olabileceği** bir paket olur.

---

## 7.1 Public API Dondurma

```xml
<EnablePublicApiTracking>true</EnablePublicApiTracking>
```

Faz 0'da kurulan `Microsoft.CodeAnalysis.PublicApiAnalyzers` burada açılır.

1. Her paketin `PublicAPI.Unshipped.txt` dosyası tam API yüzeyi ile doldurulur
2. `PublicAPI.Shipped.txt`'ye taşınır (dondurulur)
3. `Directory.Build.props` içindeki `NoWarn` listesi kaldırılır

Bundan sonra public API'ye eklenen her üye analyzer tarafından yakalanır ve kaydedilmesi zorunlu olur. Kazara API sızıntısı imkânsız hale gelir.

---

## 7.2 Paket Doğrulama

```xml
<EnablePackageValidationGate>true</EnablePackageValidationGate>
<PackageValidationBaselineVersion>1.0.0-preview.1</PackageValidationBaselineVersion>
```

`Directory.Build.targets` içindeki kapı açılır. İki denetim otomatikleşir:

- **TFM'ler arası uyum** — `net8.0` ve `net10.0` yüzeyleri tutarlı mı
- **Geriye uyum** — önceki yayınlanmış sürümle uyumlu mu

`EnableStrictModeForCompatibleTfms` ve `EnableStrictModeForCompatibleFrameworksInPackage` açık.

---

## 7.3 Paket İkonu

Faz 0'dan devreden eksik. NuGet ikonu 128×128 PNG olmalıdır.

```xml
<PackageIcon>icon.png</PackageIcon>
```

`src/` altında tek bir `icon.png`, **sekiz** pakette paylaşılır (`AgentPrism.Mcp` dahil).

---

## 7.4 Depo Adresi Düzeltmesi

Faz 0'da `PackageProjectUrl` ve `RepositoryUrl` varsayım olarak
`https://github.com/farukatasoy/AgentPrism` yazıldı.

**Durum (2026-08-02):** Kullanıcı gerçek adresin **henüz belli olmadığını** bildirdi;
değer bilinçli olarak yer tutucu bırakıldı. Yayından **önce** doğrulanmalıdır:
yanlışsa NuGet sayfasındaki bağlantılar kırık olur ve SourceLink çalışmaz.

Aynı adres `src/AgentPrism.Mcp/README.md` içindeki doküman bağlantısında da geçer.

---

## 7.5 Test Kapsamı

| Katman | Araç | Hedef |
|--------|------|-------|
| Birim | xunit.v3 + NSubstitute + Shouldly | `Core` mantık yolları |
| Entegrasyon | Testcontainers PostgreSQL | Tüm store'lar, migration'lar |
| Fonksiyonel | `WebApplicationFactory` | Tüm HTTP uçları, güvenlik katmanları |
| E2E | Playwright | Arayüz akışları |

Kapsam raporu CI'da üretilir ve PR'da görünür. Sayısal bir eşik hedefi konmaz — kritik yolların kapsanması gözle denetlenir.

### Yük testi

`run_events` yazma yolu için hedef senaryo: saniyede 100 eşzamanlı çalıştırma, her
biri ~50 olay. Darboğaz varsa toplu yazma ve partition ayarları düzeltilir.

**Bu test K-063'ün tetikleyicisidir.** Faz 6 partition'ı bilerek açmadı: birincil
anahtarı değiştirmek ve tabloyu yeniden kurmak, ölçüm olmadan çözdüğünden fazla
risk taşır. Darboğaz burada ölçülürse partition o kanıtla açılır.

Aynı testte **span yazma yolu** da ölçülmelidir. Varsayılan örnekleme oranı 0,1'dir;
`SuccessSampleRatio = 1` ile davranış farklı olacaktır ve `MaxSpansPerRun` (200)
sınırının bellek etkisi bu senaryoda görülür.

---

## 7.6 Performans Ölçümü

BenchmarkDotNet ile:

| Ölçüm | Neden |
|-------|-------|
| `AgentDefinitionCompiler` önbellek isabet/ıskalama | Her istekte derleme yapılmamalı |
| `IRunStore.AppendEventAsync` gecikmesi | Akış hızını doğrudan etkiler |
| `IAgentCatalog.ListAsync` | Arayüzün ana ekranı |
| Gömülü varlık sunumu | İlk yükleme süresi |
| `RunTraceCollector` span tamponu (bellek) | Örnekleme kararı sonda verilir; span'ler o ana kadar bellekte durur (K-056) |
| `ToolApprovalRuleEvaluator.IsAutoApprovedAsync` | **Her** tool çağrısında kural deposunu okur; kural sayısı arttıkça maliyeti ölçülmeli |

Sonuçlar `docs/` altında kayıt altına alınır. Pazarlama iddiası yazılmaz; ölçüm ve koşulları yazılır.

---

## 7.7 Dokümantasyon

- Her paketin `README.md`'si son hâline getirilir (NuGet sayfasında görünür) —
  `AgentPrism.Mcp/README.md` faz 6'da yazıldı, gözden geçirilmeli
- `docs/MIMARI.md` uygulanan mimari ile hizalanır
- XML doküman kapsamı: **tüm public API**
- Kök `README.md`: kurulum, hızlı başlangıç, özellik matrisi, DevUI karşılaştırması, yol haritası
- Geçiş rehberi: DevUI'den AgentPrism'e

---

## 7.8 Yayın Zinciri

Faz 0'da kurulan `.github/workflows/ci.yml` içindeki `publish` işi kullanılır.

```
git tag v1.0.0-preview.1
git push origin v1.0.0-preview.1
   → CI: build → test → pack → publish (environment: nuget)
```

Zincirde açık olanlar:

| Özellik | Durum |
|---------|-------|
| Deterministik build | Faz 0'da açıldı |
| SourceLink | Faz 0'da açıldı (`PublishRepositoryUrl`, `EmbedUntrackedSources`) |
| Sembol paketi (`.snupkg`) | Faz 0'da açıldı |
| `ContinuousIntegrationBuild` | CI'da açık |
| Paket imzalama | Bu fazda eklenir |

`NUGET_API_KEY` GitHub `nuget` environment'ında saklanır ve onay gerektirir.

---

## 7.9 Sürüm Politikası

| Koşul | Sürüm |
|-------|-------|
| `Microsoft.Agents.AI.Hosting` preview / `.Hosting.OpenAI` alpha iken | `1.0.0-preview.N` |
| Her ikisi GA olduğunda | `1.0.0` |

Ön sürüm bağımlılığı yalnız `AgentPrism.AspNetCore` içindedir (Faz 0'da izole edildi). GA geçişi tek pakette sürüm güncellemesidir.

Bundan sonra SemVer:

- **Major** — public API kırılması
- **Minor** — geriye uyumlu yeni yetenek
- **Patch** — düzeltme

---

## Bitiş Ölçütleri (DoD)

- [ ] `EnablePublicApiTracking=true`, tüm `PublicAPI.Shipped.txt` dolu
- [ ] Paket doğrulama açık, TFM ve geriye uyum denetimleri geçiyor
- [ ] Paket ikonu tüm paketlerde
- [ ] Depo adresi gerçek değeriyle
- [ ] Tüm test katmanları CI'da geçiyor
- [ ] Benchmark sonuçları kayıtlı
- [ ] XML doküman kapsamı tam
- [ ] `v1.0.0-preview.1` etiketi NuGet.org'a yayınlanıyor
- [ ] Temiz bir makinede `dotnet add package AgentPrism` → örnek çalışıyor

### Son doğrulama

```bash
# Temiz makine simülasyonu
dotnet new web -o /tmp/agentprism-smoke
cd /tmp/agentprism-smoke
dotnet add package AgentPrism --prerelease
# Program.cs'e iki satır eklenir, uygulama çalıştırılır
# http://localhost:5xxx/agentprism açılır
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| MAF GA'ya geçmezse 1.0 gecikir | `1.0.0-preview.N` yayınlanmaya devam eder; kullanıcı engellenmez |
| Public API dondurma geç kaldığı için büyük bir tek seferlik iş çıkar | Faz 1–6 boyunca API yüzeyi `MIMARI.md`'de takip edildi; faz 6'nın "Gerçekleşen Public API" bölümü tam listedir |
| Paket imzalama sertifikası yok | İmzalama olmadan da yayın yapılabilir; eksik dokümante edilir |
| Depo adresi hâlâ yer tutucu | Yayından önce doğrulanır; bölüm 7.4 |
| İkinci faz planı public API'yi büyütür | Yayın **önce** yapılırsa her yeni kalem `PublicAPI.Unshipped.txt` disiplinine girer — bu iyidir ama yavaşlatır. Sıra kullanıcı kararıdır; bkz. `BEYIN-FIRTINASI.md` açık soru 5 |
