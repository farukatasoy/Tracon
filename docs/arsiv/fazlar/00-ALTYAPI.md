# Faz 0 — Repository ve Build Altyapısı

> **Durum:** Tamamlandı
> **Önkoşul:** Yok
> **Sonraki:** [01-CEKIRDEK-SOYUTLAMALAR.md](01-CEKIRDEK-SOYUTLAMALAR.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/00-ALTYAPI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir NuGet paket ailesinin ihtiyaç duyduğu build, kalite ve sürümleme altyapısını kurmak. Bu fazda **ürün kodu yazılmaz**; iskelet ve kapılar kurulur. Gerekçe: paketleme kararları sonradan değiştirilmesi en pahalı kararlardır. Hedef framework kümesi, paket sınırları ve bağımlılık grafiği ilk günden doğru olmalıdır. ---

## Kapsam

- Çözüm ve proje iskeleti (7 kütüphane, 1 örnek, 1 test projesi)
- Merkezî build yapılandırması ve kalite kapıları
- Merkezî Paket Yönetimi (Central Package Management)
- Sürümleme (MinVer, git etiketinden)
- CI iş akışı
- Tüm faz dokümanları ve `README.md`

## Kapsam Dışı

- Herhangi bir ürün kodu — Faz 1'den itibaren
- Frontend kaynağı — Faz 5
- SQL migration içerikleri — Faz 2
- Public API dondurma — Faz 7

---

## Tasarım Kararları

### Hedef framework: `net8.0;net9.0;net10.0`

`Microsoft.Agents.AI` kendisi `net8.0`, `net9.0`, `net10.0`, `netstandard2.0` ve `net472` hedefler. Bugün üretimdeki .NET projelerinin büyük kısmı `net8.0` (LTS) üzerindedir. Tek `net10.0` hedefi, paketin erişimini gereksiz yere daraltırdı.

`netstandard2.0` ve `net472` **dahil edilmedi**: AgentPrism'in gerçek çalışma yeri modern ASP.NET Core'dur ve bu iki hedef `IAsyncEnumerable`, `System.Text.Json` kaynak üreteçleri gibi yapılarda ciddi ek yük getirir.

Doğrulandı: tüm bağımlılıklar (`Npgsql 10.0.3` dahil) üç hedefi de destekler.

### Modüler paketleme

Yedi paket, tek meta paket. Gerekçe: PostgreSQL kullanmayan bir tüketici `Npgsql`'i çekmemelidir. İleride `AgentPrism.SqlServer` veya `AgentPrism.Anthropic` eklemek breaking change olmaz.

### Geçişli sabitleme kapalı

`CentralPackageTransitivePinningEnabled=false`.

Açık olduğunda NuGet, geçişli bağımlılıkları üretilen `.nuspec` dosyasına **doğrudan** bağımlılık olarak yazar. Bir uygulamada bu istenen davranıştır; bir kütüphanede tüketicinin bağımlılık grafiğini kirletir.

Ölçüldü: bayrak açıkken `AgentPrism.PostgreSql` 13 doğrudan bağımlılık bildiriyordu. Kapalıyken 2 (`AgentPrism.Core`, `Npgsql`).

### Trim/AOT analyzer'ları katman bazlı

`Abstractions`, `Core`, `PostgreSql`, `OpenAI` → açık.
`AspNetCore`, `UI`, meta → kapalı.

Minimal API delege yönlendirmesi (`MapGet(pattern, delegate)`) doğası gereği reflection kullanır ve `IL2026`/`IL3050` üretir. Bu paketler için AOT vaadi verilemez; vermek yanıltıcı olurdu.

### Public API takibi kademeli

`Microsoft.CodeAnalysis.PublicApiAnalyzers` Faz 0'da kurulur, ancak `EnablePublicApiTracking=false` ile tanıları susturulur. Faz 7'de açılır ve `PublicAPI.Shipped.txt` dondurulur.

Gerekçe: Faz 1–6 boyunca API yüzeyi hızla değişecek. Her değişikliği manuel kaydettirmek geliştirmeyi yavaşlatır, karşılığında hiçbir koruma sağlamaz — çünkü henüz yayınlanmış bir sürüm yok.

### Test projeleri fazına göre eklenir

Faz 0 yalnız `AgentPrism.Core.UnitTests` projesini oluşturur. Diğer üçü test edecekleri şeyle birlikte gelir:

| Proje | Faz |
|-------|-----|
| `AgentPrism.PostgreSql.IntegrationTests` | 2 |
| `AgentPrism.AspNetCore.FunctionalTests` | 4 |
| `AgentPrism.Ui.E2ETests` | 5 |

Gerekçe: `dotnet test` sıfır testli bir projede hata verir ve CI'ı kırar. Sahte bir test eklemek çözüm değil — test etmediği bir şeyi test ediyormuş gibi görünen kod üretir. Proje, gerçek testiyle birlikte oluşturulur.

Faz 0'ın kendi testi vardır ve gerçektir: `DependencyDirectionTests` katman mimarisini `src/**/*.csproj` dosyalarını okuyarak zorlar. Ürün kodu yazılmadan önce de geçerlidir ve kalıcıdır.

### Test platformu: MTP, VSTest değil

`xunit.v3` artık Microsoft Testing Platform üzerinde çalışır; test projesi kendi başına çalıştırılabilir bir uygulamadır. `Microsoft.NET.Test.Sdk` ve `xunit.runner.visualstudio` **bilerek referans edilmez** — bunlar VSTest içindir ve birlikte kullanıldıklarında `dotnet test` şu hatayı verir:

```
The argument ...AgentPrism.Core.UnitTests.dll is invalid.
```

`tests/Directory.Build.props` içinde:

```xml
<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
```

### Sürüm MinVer ile git etiketinden

Elle sürüm düzenlemesi yok. `v1.0.0-preview.1` etiketi atıldığında paketler o sürümü alır. Etiket yokken `0.0.0-preview.0` üretilir.

---

## Doğrulanan Bağımlılık Grafiği

`dotnet pack` çıktısındaki `net10.0` grubundan ölçüldü:

| Paket | Doğrudan bağımlılık |
|-------|--------------------|
| `AgentPrism.Abstractions` | 2 |
| `AgentPrism.Core` | 10 |
| `AgentPrism.PostgreSql` | 2 |
| `AgentPrism.OpenAI` | 4 |
| `AgentPrism.AspNetCore` | 3 |
| `AgentPrism.UI` | 1 |
| `AgentPrism` (meta) | 4 |

Grafik tek yönlüdür ve döngü içermez.

---

## Karşılaşılan Sorunlar ve Çözümleri

### 1. Trim/AOT analyzer'ları örnek uygulamayı kırdı

`EnableAotAnalyzer` kök seviyede açıktı. `app.MapGet(...)` çağrısı `IL2026` ve `IL3050` üretti ve `TreatWarningsAsErrors` build'i kırdı.

**Çözüm:** Analyzer'lar `src/Directory.Build.props`'a taşındı; `AgentPrismAotCompatible` özelliği ile paket bazlı kapatılabilir hale getirildi.

### 2. `dotnet pack` paketlenmeyen projeler için uyarı üretti

NuGet, `IsPackable=false` olan projeler için **kodsuz** bir uyarı üretiyordu. Kodsuz olduğu için `NoWarn` ile susturulamıyordu.

İlk deneme `Directory.Build.props` içinde boş `<Target Name="Pack" />` tanımlamaktı — **işe yaramadı**, çünkü props dosyası SDK hedeflerinden önce yüklenir ve SDK tanımı üzerine yazar.

**Kök neden:** `NuGet.Build.Tasks.Pack.targets` satır 204:
```xml
<Target Name="Pack" DependsOnTargets="$(PackDependsOn)">
   <IsPackableFalseWarningTask Condition="'$(IsPackable)' == 'false' AND '$(WarnOnPackingNonPackableProject)' == 'true'"/>
</Target>
```

**Çözüm:** `<WarnOnPackingNonPackableProject>false</WarnOnPackingNonPackableProject>` — hedef geçersiz kılma yerine bayrağın kendisi.

### 3. Geçişli sabitleme paket grafiğini kirletti

Bkz. yukarıdaki tasarım kararı. `Npgsql`, `OpenAI`, `OpenTelemetry.Api` hiç referans verilmemiş paketlerde doğrudan bağımlılık olarak görünüyordu.

### 4. `dotnet test` xunit.v3 ile çalışmadı

`Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` (VSTest) ile `xunit.v3` (MTP) birlikte kullanılamaz. Çözüm: VSTest paketleri kaldırıldı, MTP özellikleri açıldı.

### 5. TRX raporlama eklentisi sürüm çakışması yaptı

`Microsoft.Testing.Extensions.TrxReport 2.3.3` kuruldu ve çalışma anında patladı:

```
System.TypeLoadException: Could not load type
'Microsoft.Testing.Platform.Extensions.TestHost.IDataConsumer'
from assembly 'Microsoft.Testing.Platform, Version=2.3.3.0'
```

**Kök neden:** `xunit.v3 3.2.2` Microsoft Testing Platform **v1** üzerine kuruludur (paket açıklamasında yazıyor). TrxReport 2.x, Platform 2.x çeker ve `IDataConsumer` tipi iki sürüm arasında değişmiştir.

**Çözüm:** TrxReport `1.9.1`'e sabitlendi.

---

## Bitiş Ölçütleri (DoD)

| Ölçüt | Durum |
|-------|-------|
| `dotnet restore AgentPrism.slnx` başarılı | ✅ |
| `dotnet build -c Release` — 0 uyarı, 0 hata | ✅ |
| `dotnet pack -c Release` — 0 uyarı, 7 `.nupkg` + 7 `.snupkg` | ✅ |
| `dotnet test -c Release` — 4 test geçiyor | ✅ |
| `dotnet format --verify-no-changes` temiz | ✅ |
| Her paket 3 TFM için `lib/` klasörü ve XML doküman içeriyor | ✅ |
| Her pakette `README.md` var | ✅ |
| Bağımlılık grafiği tek yönlü ve minimal | ✅ |
| Örnek API çalışıyor, `/health` yanıt veriyor | ✅ |

Doğrulama komutu:

```bash
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes
```

---

## Sonraki Faza Devreden Riskler

| Risk | Etki | Takip |
|------|------|-------|
| `Microsoft.Agents.AI.Hosting` preview, `.Hosting.OpenAI` alpha | Kararlı 1.0 verilemez | Faz 4'te izole edildi; Faz 7'de sürüm politikası uygulanır |
| Paket ikonu yok | NuGet sayfası eksik görünür | Faz 7 |
| `PackageProjectUrl` / `RepositoryUrl` varsayım (`github.com/farukatasoy/AgentPrism`) | Yanlış bağlantı | Gerçek depo adresi belli olunca `src/Directory.Build.props` güncellenir |
| Faz 0'da yalnız mimari testi var | Ürün mantığı henüz test edilmiyor | Faz 1'den itibaren birim testleri eklenir |
