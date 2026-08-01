# MEMORY.md — Kurumsal Bilgi Defteri

> Oturumlar arası biriken keşif notları. Her agent oturuma başlamadan bu dosyanın ilk 200 satırını okur; çalışma sırasında keşfettiği codepath'leri, desenleri, kütüphane konumlarını ve mimari kararları **kısa notlar** hâlinde buraya ekler.
>
> Kurallar:
> - AGENTS.md / KARARLAR.md / skill'lerde zaten yazılı olanı buraya kopyalama — yalnızca oralarda olmayan keşifleri yaz.
> - Her not tek satır–birkaç satır; dosya/dizin yolu ver, tarih ekle (YYYY-AA-GG).
> - Bayatlayan notu güncelle veya sil; dosyayı 200 satırın altında tut.

## Codepath'ler & Konumlar

- **Build yapılandırması üç katmanlı** (2026-08-01): kök `Directory.Build.props` (dil, kalite kapıları) → `src|tests|samples/Directory.Build.props` (katman ayarları). Alt katmanlar kökü `[MSBuild]::GetPathOfFileAbove(...)` ile açıkça import eder — otomatik değil.
- **Paket sürümleri tek yerde** (2026-08-01): `Directory.Packages.props`. Projeler `Version` yazmaz. MAF GA sürümleri `$(MicrosoftAgentsAIVersion)` değişkeninden gelir.
- **AOT bayrağı** (2026-08-01): `src/Directory.Build.props` içindeki `AgentPrismAotCompatible`. Paket bazlı kapatmak için csproj'da `<AgentPrismAotCompatible>false</AgentPrismAotCompatible>`.
- **Public API takip anahtarı** (2026-08-01): kök `Directory.Build.props` içindeki `EnablePublicApiTracking`. `false` iken RS00xx tanıları `NoWarn` ile susturulur. Faz 7'de `true` yapılır.
- **Paket doğrulama kapısı** (2026-08-01): `Directory.Build.targets` içindeki `EnablePackageValidationGate`. Faz 7'de açılır; taban sürüm yayınlanmadan açılamaz.

## Desenler & Kararlar (keşfedilen)

- **MAF `Hosting.OpenAI` depolaması `TryAddSingleton`** (2026-08-01): `Microsoft.Agents.AI.Hosting.OpenAI/ServiceCollectionExtensions.cs` `IConversationStorage`, `IAgentConversationIndex`, `IResponsesService` için bellek içi implementasyonları `TryAdd` ile kaydeder. Kendi implementasyonumuzu `AddOpenAIResponses()` çağrısından **önce** kaydedersek bizimki kazanır. Sıra bozulursa kalıcılık sessizce devre dışı kalır — Faz 4'te `StorageOverrideTests` bunu korur.
- **`AgentSessionStore` soyut sınıf** (2026-08-01): `Microsoft.Agents.AI.Hosting/AgentSessionStore.cs`. `SaveSessionAsync` / `GetSessionAsync` / `DeleteSessionAsync`. PostgreSQL implementasyonu bundan türer.
- **`ChatHistoryProvider` örneği tüm oturumlarda paylaşılır** (2026-08-01): oturuma özgü hiçbir durum alan olarak tutulamaz. Veritabanı anahtarı `ProviderSessionState<T>` ile `AgentSession` içinde saklanır. MAF dokümanının açık uyarısı.
- **Çok kiracılılık için MAF'ta hazır yapı var** (2026-08-01): `IsolationKeyScopedAgentSessionStore` + `SessionIsolationKeyProvider`. Sıfırdan yazmaya gerek yok.

## Tuzaklar (AGENTS.md'de olmayan)

- **`dotnet pack` kodsuz uyarı üretir** (2026-08-01): `IsPackable=false` olan projeler için NuGet **kodsuz** bir uyarı verir; `NoWarn` ile susturulamaz. Çözüm `<WarnOnPackingNonPackableProject>false</WarnOnPackingNonPackableProject>`. Kaynak: `NuGet.Build.Tasks.Pack.targets` satır 204. `Directory.Build.props` içinde boş `<Target Name="Pack" />` tanımlamak **çalışmaz** — props SDK hedeflerinden önce yüklenir.
- **`CentralPackageTransitivePinningEnabled` kütüphanede zararlı** (2026-08-01): geçişli bağımlılıkları üretilen `.nuspec` içine **doğrudan** bağımlılık olarak yazar. Ölçüldü: `AgentPrism.PostgreSql` 13 → 2 doğrudan bağımlılık.
- **Trim/AOT analyzer'ları kök seviyede açılamaz** (2026-08-01): `app.MapGet(pattern, delegate)` `IL2026` + `IL3050` üretir; `TreatWarningsAsErrors` ile build kırılır. Analyzer'lar `src/` katmanında, paket bazlı kapatılabilir olmalı.
- **Bash komutlarında `cd` kalıcıdır** (2026-08-01): bir komutta `cd artifacts/...` yapıldıysa sonraki komut oradan başlar. `rm -rf artifacts && dotnet build AgentPrism.slnx` sessizce yanlış dizinde çalıştı ve eski paketler doğru sanıldı. Doğrulama komutlarında mutlak yol kullan.
- **`.claude/.DS_Store` git index'inde** (2026-08-01): ilk commit'te izlenmeye başlamış. `.gitignore` artık kapsıyor ama izlenen dosya için `git rm --cached` gerekir.
