# Konu 2 — Çok Kiracılı İzolasyon (Tenancy)

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`src/AgentPrism.Sql.Shared/Stores/*.cs`, `TenantAgnosticAttribute.cs`,
`TenantCoverageTests`, `CompiledAgentCache`, dosya belleği kodu
(`src/AgentPrism.Core/`), `HttpTenantContext.cs`.

## Bilinen tasarım

EF Core kullanılmıyor (ham ADO.NET); global query filter yok. Bunun yerine
her sorguya elle `tenant_id` eklenir (`DbHelpers.Add(command, "tenant_id",
...)`); disiplin **test-zamanında** `TenantCoverageTests` ile zorlanır —
`[TenantAgnostic]` işaretlenmemiş her public store metodu taranır. Kiracı
çözümleme önceliği: API key'in `tenant_id`'si (kanıt) > claim (beyan) >
header (yalnız `AllowHeaderResolution` açıkken) — claim tanımlıysa header
HİÇ okunmaz. K-380 (`CompiledAgentCache` kiracılar arası sızıntısı),
K-381 (dosya belleği), K-382 (`AllowedTenants` beyaz listesi) önceki
sızıntı sınıflarını kapattı.

## Ara

- Son fazlarda eklenmiş her yeni store metodunun `TenantCoverageTests`
  kapsamına girdiğini; `[TenantAgnostic]` işaretli metotların yazılı
  gerekçesinin hâlâ geçerli olduğunu.
- Herhangi bir cache/bellek-içi yapının (K-380/381 sınıfı) `tenant_id`'yi
  anahtarına katmadan veri tuttuğu yeni bir nokta var mı — özellikle
  `IMemoryCache`, `ConcurrentDictionary`, statik alan kullanımlarını tara.
- Header/claim önceliğinin HER kod yolunda (HTTP, WebSocket, webhook,
  MCP/A2A) tutarlı uygulandığını — bir yolda claim varken header'ın yine de
  okunduğu bir istisna var mı.
- Ambient kiracı bağlamıyla süzmenin (`postgresql.md:38-47`'de not edilen
  tuzak) meşru bir yazmayı yanlışlıkla düşürüp düşürmediğini.
