# MEMORY.md — Kurumsal Bilgi Defteri

> Oturumlar arası biriken keşif notları. Her agent oturuma başlamadan bu dosyanın ilk 200 satırını okur; çalışma sırasında keşfettiği codepath'leri, desenleri, kütüphane konumlarını ve mimari kararları **kısa notlar** hâlinde buraya ekler.
>
> Kurallar:
> - AGENTS.md / KARARLAR.md / skill'lerde zaten yazılı olanı buraya kopyalama — yalnızca oralarda olmayan keşifleri yaz.
> - Her not tek satır–birkaç satır; dosya/dizin yolu ver, tarih ekle (YYYY-AA-GG).
> - Bayatlayan notu güncelle veya sil; dosyayı 200 satırın altında tut.

## Codepath'ler & Konumlar

- **Build yapılandırması üç katmanlı** (2026-08-01): kök `Directory.Build.props` (dil, kalite kapıları) → `src|tests|samples/Directory.Build.props` (katman ayarları). Alt katmanlar kökü `[MSBuild]::GetPathOfFileAbove(...)` ile açıkça import eder — otomatik değil.
- **Paket sürümleri tek yerde** (2026-08-01): `Directory.Packages.props`. Projeler `Version` yazmaz. MAF GA sürümleri `$(MicrosoftAgentsAIVersion)` değişkeninden gelir; `maf-api-kesfi` script'i de bu değişkeni okur.
- **AOT bayrağı** (2026-08-01): `src/Directory.Build.props` içindeki `AgentPrismAotCompatible`. Paket bazlı kapatmak için csproj'da `<AgentPrismAotCompatible>false</AgentPrismAotCompatible>`.
- **Public API takip anahtarı** (2026-08-01): kök `Directory.Build.props` içindeki `EnablePublicApiTracking`. `false` iken RS00xx tanıları `NoWarn` ile susturulur. Faz 7'de `true` yapılır.
- **Paket doğrulama kapısı** (2026-08-01): `Directory.Build.targets` içindeki `EnablePackageValidationGate`. Faz 7'de açılır; taban sürüm yayınlanmadan açılamaz.
- **Servis kayıtları tek dosyada** (2026-08-02): `src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs`. Hangi servisin nasıl kaydedildiğini görmek için önce buraya bak.
- **Harness kullanımı tek dosyada** (2026-08-02): `AgentDefinitionCompiler.CompileHarnessAgent`. MAF harness API'si değişirse yalnız orası etkilenir.

## Desenler & Kararlar (keşfedilen)

- **MAF `Hosting.OpenAI` depolaması `TryAddSingleton`** (2026-08-01): `Microsoft.Agents.AI.Hosting.OpenAI/ServiceCollectionExtensions.cs` `IConversationStorage`, `IAgentConversationIndex`, `IResponsesService` için bellek içi implementasyonları `TryAdd` ile kaydeder. Kendi implementasyonumuzu `AddOpenAIResponses()` çağrısından **önce** kaydedersek bizimki kazanır. Sıra bozulursa kalıcılık sessizce devre dışı kalır — Faz 4'te `StorageOverrideTests` bunu korur.
- **`AgentSessionStore` soyut sınıf** (2026-08-01): `Microsoft.Agents.AI.Hosting/AgentSessionStore.cs`. `SaveSessionAsync` / `GetSessionAsync` / `DeleteSessionAsync`. Ön sürüm pakette olduğu için uygulaması Faz 4'e (AspNetCore) bırakıldı.
- **`ChatHistoryProvider` örneği tüm oturumlarda paylaşılır** (2026-08-01): oturuma özgü hiçbir durum alan olarak tutulamaz. Veritabanı anahtarı `ProviderSessionState<T>` ile `AgentSession` içinde saklanır. MAF dokümanının açık uyarısı.
- **Çok kiracılılık için MAF'ta hazır yapı var** (2026-08-01): `IsolationKeyScopedAgentSessionStore` + `SessionIsolationKeyProvider`. Sıfırdan yazmaya gerek yok.
- **Tool çağrıları içeriklerden okunur** (2026-08-02): MAF ayrı bir kanca sunmaz. `FunctionCallContent` ve `FunctionResultContent` (Microsoft.Extensions.AI) hem `AgentResponse.Messages[*].Contents` hem `AgentResponseUpdate.Contents` içinde gelir. `RunRecordingAgent.WriteContentsAsync` bunu kullanır.
- **Token kullanımı akışta `UsageContent` ile gelir** (2026-08-02): akışlı çalıştırmada `AgentResponse.Usage` yoktur; güncellemelerin içeriklerinden `UsageContent.Details` toplanır.

## Tuzaklar (AGENTS.md'de olmayan)

- **MAF tip adları tahmin edilemez** (2026-08-02): plan `AgentRunResponse` varsaydı; gerçek ad **`AgentResponse`**. Aynı şekilde `AgentResponseUpdate`. MAF'ın .NET dokümanı çoğu sayfada "Coming Soon" diyor. Yeni tip kullanmadan önce `.agents/skills/maf-api-kesfi/scripts/dump-api.sh` çalıştır.
- **`MAAI001` derlemeyi kırar** (2026-08-02): `HarnessAgentOptions` üyeleri "evaluation purposes only" işaretli. `TreatWarningsAsErrors` ile hata olur. Bastırma gerekçeyle ve tek dosyada yapılır.
- **`TryAdd` sırası Faz 2'yi vuracak** (2026-08-02): `AddAgentPrism()` bellek içi depoları `TryAddSingleton` ile kaydeder. `UsePostgreSql()` zincirde **sonra** çalıştığı için `TryAdd` ile kayıt yapamaz — `services.Replace(...)` kullanılmalı. Ayrıntı: `docs/02-POSTGRESQL-KALICILIK.md`.
- **AOT üç yerde ödün istedi** (2026-08-02): `ValidateDataAnnotations()` → elle validator; `optionsBuilder.Bind()` → elle bağlama; tool argümanı serileştirme → elle biçimlendirme. Faz 2'de `jsonb` için `JsonSerializerContext` gerekecek.
- **`dotnet format`, `dotnet build`'den fazlasını yakalar** (2026-08-02): `EnableConfigurationBindingGenerator=true` ile build temiz geçti ama format `IL2026`/`IL3050` gösterdi — kaynak üreteci format'ın analyzer geçişinde devreye girmiyor. **Dört kapıyı da çalıştır**; sadece build'e güvenme.
- **`Guid.CreateVersion7()` net9+** (2026-08-02): `net8.0` da hedeflediğimiz için `AgentPrismId.NewId()` yazıldı (RFC 9562). Birincil anahtarlarda `Guid.NewGuid()` **kullanma** — index parçalanır.
- **`dotnet pack` kodsuz uyarı üretir** (2026-08-01): `IsPackable=false` olan projeler için NuGet **kodsuz** bir uyarı verir; `NoWarn` ile susturulamaz. Çözüm `<WarnOnPackingNonPackableProject>false</WarnOnPackingNonPackableProject>`. Kaynak: `NuGet.Build.Tasks.Pack.targets` satır 204. `Directory.Build.props` içinde boş `<Target Name="Pack" />` tanımlamak **çalışmaz** — props SDK hedeflerinden önce yüklenir.
- **`CentralPackageTransitivePinningEnabled` kütüphanede zararlı** (2026-08-01): geçişli bağımlılıkları üretilen `.nuspec` içine **doğrudan** bağımlılık olarak yazar. Ölçüldü: `AgentPrism.PostgreSql` 13 → 2 doğrudan bağımlılık.
- **Trim/AOT analyzer'ları kök seviyede açılamaz** (2026-08-01): `app.MapGet(pattern, delegate)` `IL2026` + `IL3050` üretir. Analyzer'lar `src/` katmanında, paket bazlı kapatılabilir olmalı.
- **xunit.v3 VSTest ile çalışmaz** (2026-08-01): MTP kullanır. `Microsoft.NET.Test.Sdk` ve `xunit.runner.visualstudio` referans **edilmez**. TRX eklentisi de sürüm uyumlu olmalı: xunit.v3 3.2.2 → Platform **v1** → `Microsoft.Testing.Extensions.TrxReport` **1.9.1** (2.x `TypeLoadException` verir).
- **Bash komutlarında `cd` kalıcıdır** (2026-08-01): bir komutta `cd artifacts/...` yapıldıysa sonraki komut oradan başlar. `rm -rf artifacts && dotnet build AgentPrism.slnx` sessizce yanlış dizinde çalıştı ve eski paketler doğru sanıldı. Doğrulama komutlarında mutlak yol kullan.
- **`.editorconfig` isimlendirme kurallarında sıra önemli** (2026-08-01): ilk eşleşen kural kazanır. `const` ve `static readonly` kuralları genel private alan kuralından **önce** gelmelidir.
