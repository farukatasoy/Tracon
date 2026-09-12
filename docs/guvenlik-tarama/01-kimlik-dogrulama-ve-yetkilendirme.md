# Konu 1 — Kimlik Doğrulama, Oturum ve Yetkilendirme

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`src/Tracon.AspNetCore/Security/`,
`src/Tracon.AspNetCore/Tenancy/HttpTenantContext.cs`, konuşma WebSocket
handshake kodu, `MapTraconEndpointRouteBuilderExtensions.cs`.

## Bilinen tasarım

Üç sıralı katman: loopback kısıtı (`AllowRemoteAccess=false` varsayılan) →
sabit-zamanlı `Bearer` token / kiracı API anahtarı karşılaştırması
(`IApiKeyStore`, SHA-256 `key_hash`, K-356) → ASP.NET Core
`AuthorizationPolicy`. K-359: `Authorization` başlığı varsa `AuthToken`
tanımsız olsa bile doğrulanır — önceki davranışta başlık ne taşırsa taşısın
istek geçiyordu (sessiz-geçiş açığı, kapatıldı). Konuşma WebSocket'i token'ı
`Sec-WebSocket-Protocol` alt protokolünde alır (K-224, sorgu dizesi kasıtlı
reddedilir — proxy loglarına yazılmasın diye). `[Authorize]`/`[AllowAnonymous]`
attribute'ları KULLANILMAZ; merkezi `TraconEndpointFilter` bu işi yapar.
`/api/meta`, UI statik dosyaları, OAuth callback/webhook uçları bilinçli
olarak bearer/loopback'ten muaf (gerekçe kod içi XML dokümanında).

## Ara

- K-359 sınıfı "başlık/claim var ama içeriği tanımsız" sessiz-geçiş
  yolunun başka bir doğrulama noktasında (WebSocket, webhook imza, API key
  header) tekrarlanıp tekrarlanmadığını.
- Örnek uygulamadaki `RequireRole(null)` tuzağının (K-431) benzerinin başka
  bir policy kaydında olup olmadığını — policy adı yanlış yazılmış/kayıtsız
  bırakılmış bir endpoint var mı.
- `MapTraconEndpointRouteBuilderExtensions.cs`'teki muafiyet listesini
  son fazlarda eklenmiş her yeni `MapGet`/`MapPost` ile karşılaştır: yeni
  bir uç merkezi filtreyi atlayarak mı eklenmiş.
- Bearer token karşılaştırmasının GERÇEKTEN sabit-zamanlı olduğunu (erken
  çıkışlı `==`/`StartsWith` değil, `CryptographicOperations.FixedTimeEquals`
  benzeri) doğrula.
