# Faz 7 — Sağlamlaştırma ve Yayın

> **Durum:** Planlandı
> **Önkoşul:** [06-GOZLEMLENEBILIRLIK.md](06-GOZLEMLENEBILIRLIK.md)
> **Sonraki:** Yok — bu faz 1.0 yayınını kapatır

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

`src/` altında tek bir `icon.png`, tüm paketlerde paylaşılır.

---

## 7.4 Depo Adresi Düzeltmesi

Faz 0'da `PackageProjectUrl` ve `RepositoryUrl` varsayım olarak `https://github.com/farukatasoy/AgentPrism` yazıldı. Gerçek depo adresi ile güncellenir.

Bu değerler yanlışsa NuGet sayfasındaki bağlantılar kırık olur ve SourceLink çalışmaz.

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

`run_events` yazma yolu için hedef senaryo: saniyede 100 eşzamanlı çalıştırma, her biri ~50 olay. Darboğaz varsa toplu yazma ve partition ayarları düzeltilir.

---

## 7.6 Performans Ölçümü

BenchmarkDotNet ile:

| Ölçüm | Neden |
|-------|-------|
| `AgentDefinitionCompiler` önbellek isabet/ıskalama | Her istekte derleme yapılmamalı |
| `IRunStore.AppendEventAsync` gecikmesi | Akış hızını doğrudan etkiler |
| `IAgentCatalog.ListAsync` | Arayüzün ana ekranı |
| Gömülü varlık sunumu | İlk yükleme süresi |

Sonuçlar `docs/` altında kayıt altına alınır. Pazarlama iddiası yazılmaz; ölçüm ve koşulları yazılır.

---

## 7.7 Dokümantasyon

- Her paketin `README.md`'si son hâline getirilir (NuGet sayfasında görünür)
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
| Public API dondurma geç kaldığı için büyük bir tek seferlik iş çıkar | Faz 1–6 boyunca API yüzeyi `MIMARI.md`'de takip edilir |
| Paket imzalama sertifikası yok | İmzalama olmadan da yayın yapılabilir; eksik dokümante edilir |
