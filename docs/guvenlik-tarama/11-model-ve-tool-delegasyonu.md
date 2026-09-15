# Konu 11 — Model ve Tool Delegasyonu

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

Bu konu tehdit modelinin **A4** (`tool` çıktısı üzerinden gelen içerik) ve
**A5** (kötü niyetli MCP sunucusu) profillerini karşılar
([`MIMARI-TEHDIT-MODELI.md`](../MIMARI-TEHDIT-MODELI.md) § 2). 2026-08-20
koşumunda bu iki profilin karşılığı olan bir konu YOKTU.

## Kapsam

`src/Tracon.Core/Approvals/`, `src/Tracon.Abstractions/Approvals/`,
`src/Tracon.Core/Tools/` (özellikle `ToolWrapperChain.cs`),
`src/Tracon.Mcp/` (`McpToolRegistry.cs`, `McpOAuthAuthorizationCoordinator.cs`,
`Internal/InMemoryMcpTokenCache.cs`),
`src/Tracon.AspNetCore/McpServer/CatalogToolCallHandler.cs`,
`src/Tracon.AspNetCore/A2A/ExternalAgentProxy.cs`,
`src/Tracon.Core/Graph/ChildAgentInvoker.cs`, `src/Tracon.Core/Knowledge/`,
`src/Tracon.Core/Guards/`,
`src/Tracon.Core/Attachments/AttachmentResolvingChatClient.cs`,
`src/Tracon.AspNetCore/Endpoints/ApprovalEndpoints.cs`.

## Bilinen tasarım

**Çerçeve kuralı:** prompt injection'ın kendisi bulgu DEĞİLDİR. Bulgu, kodda
bir sınırın düşmesidir — içerik başka bir principal'ın bağlamına girer,
çağıranın sahip olmadığı yetkiyi kullanır ya da doğrudan erişemeyeceği bir
sink'e ulaşır. Sistem prompt'undaki talimat güvenlik sınırı SAYILMAZ; yalnız
deterministik kontrol sayılır.

Tool sarmalama sırası (dıştan içe, `ToolWrapperChain.cs` doküman yorumu):
Authorizing → Validating → Timeout → ApprovalRequired → Truncating → gerçek
fonksiyon. Yetkilendirme her şeyden önce koşar; MCP yolunda AYNI mantık
tekrarlanır (K-487). MAF'ın onay kısa devresi `AITool.GetService<T>()`
pipeline'ı üzerinden çalışır — `ApprovalRequiredAIFunction.InvokeCoreAsync`'i
DOĞRUDAN çağırmak defer ETMEZ, gerçek gövdeyi çalıştırır (K-490).

Onay kuralı argüman koşuluyla değerlendirilir (`ToolApprovalRuleEvaluator`,
`ToolArgumentConditionMatcher`); kural gövdesi `ArgumentsHash` TAŞIMAZ
(K-452). Sunum karar anında yeniden çözülmez, kalıcı sütundur (K-675).
`IToolApprovalPresenter` fail-OPEN'dır (K-676). Onay kararı aynı `RunId`'yi
sürdürmez, YENİ çalıştırma açar (K-368); resume kimliği approval'dan
türetilir (K-726). Senkron/MCP/A2A yolu `pending_approvals`'a HİÇ yazmaz
(K-372); alt agent onay isteyemez (K-103).

MCP: yalnız uzak HTTP (K-058), tool adı `{sunucu}_{tool}` (K-060), auth
değeri veritabanına yazılmaz (K-059), OAuth token'ı `(kiracı, sunucu)` başına
tek bellek içi önbellekte İKİ tüketici arasında PAYLAŞILIR (K-170). MCP task
kimliği Tracon'un `run` kimliğidir; kiracı-oblivious SDK-içi `tasks/cancel`
**kabul edilmiş, kapatılamayan** bir sınırdır (K-637).

İçerik koruması: `ContentGuardContext.Source` içerik TİPİNE göre sınıflanır
(`FunctionResultContent` → `ToolResult`, rolden bağımsız) ve
`PatternContentGuard` `Source`'u KASITLI okumaz (K-672).

Bilgi tabanı: `VectorSearchToolFactory` `tenantId`'yi **derleme anında**
çözer. Kalıcı vektör tabanlı model belleği (`ChatHistoryMemoryProvider`)
kapsam dışıdır (K-105) — bellek zehirlenmesi sınıfı bugün yalnız bilgi
tabanı ve oturum geçmişi üzerinden gelebilir.

## Ara

- Onay **bağlama** kusuru: kullanıcı bir argüman kümesini onaylarken,
  kararın uygulandığı çağrının argümanlarının AYNI olduğunu ne garanti
  ediyor? `ArgumentsHash` kural gövdesinde yok (K-452). Karar ile yürütme
  arasında argümanın değişebildiği bir yol var mı — resume (K-726), retry,
  yeniden kuyruklama?
- `VectorSearchToolFactory`'nin derleme anında yakaladığı `tenantId`'nin,
  önbelleğe alınan derlenmiş agent ile birlikte başka bir kiracıya
  taşınamadığını doğrula. Bu K-380/K-471 ile AYNI sınıftır.
- `PatternContentGuard`'ın `Source`'u okumaması (K-672) bilinçlidir. Ama
  `tool` sonucu olarak dönen metnin bir sonraki turda model bağlamına
  girerken hangi deterministik kontrolden geçtiğini izle. Hiçbirinden
  geçmiyorsa, o metnin ulaşabildiği EN GÜÇLÜ sink'i adlandır.
- MCP sunucusunun beyan ettiği `tool` adı, açıklaması veya şeması bir yetki
  kaynağı olarak KULLANILIYOR mu — yoksa yetki her zaman yerel allowlist ve
  `IToolAuthorizationHandler`'dan mı geliyor? İki sunucunun aynı
  `{sunucu}_{tool}` adını üretebildiği bir durum var mı (K-060)?
- K-170'in paylaşılan OAuth token önbelleğinde anahtarın `(kiracı, sunucu)`
  olarak KALDIĞINI doğrula. İki tüketiciden birinin anahtara kiracıyı
  katmadığı bir yol varsa bu doğrudan kiracılar arası `secret` sızıntısıdır.
- `ChildAgentInvoker`, `ExternalAgentProxy` ve `CatalogToolCallHandler`
  yollarında alt çağrının hangi principal ve kiracı ile koştuğunu izle.
  K-337 her zaman yeni bir kök çalıştırma açar — bu yetkiyi daraltıyor mu,
  yoksa oturumun tam yetkisini mi devrediyor?
- `AttachmentResolvingChatClient`'ın çözdüğü `AttachmentUriReference`'ın
  model tarafından üretilebilir olup olmadığını ölç. Üretilebiliyorsa bu
  model-kontrollü bir kaynak seçicisidir ve SSRF/dosya okuma sınıfına düşer
  (konu 05 ile birlikte değerlendir).
- K-490'ın kısa devresinin HER çağrı yolunda (MCP sunucusu, A2A, workflow,
  zamanlanmış iş) gerçekten MAF pipeline'ı üzerinden geçtiğini doğrula.
  Doğrudan `InvokeCoreAsync` çağıran bir yol onayı sessizce atlar.
