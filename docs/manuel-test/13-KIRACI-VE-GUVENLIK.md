# 13 — Kiracı ve Güvenlik (`SEC`)

> **Alan kodu:** `SEC` · **Faz:** 6, 9, 41, 50, 53, 63, 65, 69, 82, 139, 147, 148, 149
> **Kaynak:** `src/Tracon.AspNetCore/Security/` (tümü: `TraconEndpointFilter`,
> `LoopbackGuard`, `BearerTokenValidator`, `ApiKeyAuthenticator`, `ApiKeyRequestContext`,
> `ApiKeyScopeRequirement`, `ExternalSurfaceGuard`, `ExternalCallAudit`, `TraconPolicies`,
> `TraconRolePolicies`, `RoleEndpointConventionBuilderExtensions`) ·
> `src/Tracon.AspNetCore/Tenancy/` (tümü) ·
> `src/Tracon.AspNetCore/TraconEndpointOptions.cs` ·
> `src/Tracon.AspNetCore/TraconEndpointRouteBuilderExtensions.cs` (yalnız erişim
> katmanlaması — genel `MapTracon` sözleşmesi `07`'nin işi) ·
> `src/Tracon.AspNetCore/Endpoints/ApiKeyEndpoints.cs`, `AuditEndpoints.cs`,
> `GovernanceEndpoints.cs` (yalnız `MapTenants` + `MapApprovalRules` — MCP sunucu
> bölümleri `18`'in işi), `TenantProviderEndpoints.cs` (Faz 65 — kiracı sağlayıcı
> bağlamaları/BYOK ve egress politikası) ·
> `src/Tracon.Abstractions/Options/TraconTenantProviderOptions.cs` (Faz 65) ·
> `src/Tracon.Core/Models/ProviderCredentialClientCache.cs` (Faz 65) ·
> `src/Tracon.Abstractions/Approvals/` (tümü — `ToolArgumentCondition`,
> `ToolArgumentOperator`, `ToolApprovalPolicyDecision`, `ToolApprovalContext`) ·
> `src/Tracon.Core/Approvals/` (tümü — `ToolArgumentConditionMatcher`,
> `ToolApprovalPolicyRegistry`, `ToolApprovalRuleEvaluator`) ·
> `src/Tracon.UI/frontend/src/screens/mcp.tsx` (yalnız "Remembered approvals"
> paneli — sunucu CRUD kısmı `18`'in işi) ·
> `src/Tracon.Abstractions/Security/` (tümü) · `src/Tracon.Abstractions/Audit/` (tümü) ·
> `src/Tracon.Abstractions/Tenancy/` (tümü) ·
> `src/Tracon.Core/Security/` (tümü) · `src/Tracon.Core/Audit/` (tümü) ·
> `src/Tracon.Core/Tenancy/` (tümü) ·
> `src/Tracon.Abstractions/Tools/ToolEffect.cs`, `ToolAuthorizationTypes.cs` (Faz 69) ·
> `src/Tracon.Core/Tools/AuthorizingAIFunction.cs`, `TimeoutAIFunction.cs`,
> `AllowAllToolAuthorizationHandler.cs`, `ToolRegistry.cs` (Faz 69) ·
> `src/Tracon.Abstractions/Security/IContentProtector.cs`, `ProtectedColumn.cs`,
> `src/Tracon.Core/Security/` (`AesGcmContentProtector`, `NullContentProtector`,
> `ContentProtectionEnvelope`, `TraconContentProtectionOptions`,
> `TraconContentProtectionOptionsValidator`), `TraconContentProtectionExtensions.cs`,
> `src/Tracon.Sql.Shared/Internal/ProtectedValue.cs` (Faz 82 — at-rest içerik koruması) ·
> `src/Tracon.Abstractions/Runs/RunAuthorizationTypes.cs` (Faz 139 — `IRunAuthorizationHandler`),
> `src/Tracon.Core/Runs/AllowAllRunAuthorizationHandler.cs`,
> `src/Tracon.AspNetCore/RateLimiting/RunAuthorizationGate.cs` (Faz 139 · 147 —
> Faz 147 `CheckRunResourceAsync`'i ve `RunAccess`/`SessionAccess.Voice` üyelerini ekledi;
> çağrı yerleri `Endpoints/RunEndpoints.cs`, `ObservabilityEndpoints.cs`,
> `AttachmentEndpoints.cs`, `ApprovalEndpoints.cs`,
> `OpenAICompat/OpenAIChatCompletionsEndpoints.cs`, `Voice/VoiceConversationEndpoint.cs`).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/13-KIRACI-VE-GUVENLIK.md`](kosumlar/2026-08-13/13-KIRACI-VE-GUVENLIK.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

`app.MapTracon()`'in kurduğu **üç katmanlı erişim koruması** (loopback →
bearer/API anahtarı → authorization policy), **kiracı çözümlemesi ve izolasyonu**
(Faz 41), **kiracı bazlı API anahtarları** (Faz 53: kapsam, kiracı bağı, yaşam
döngüsü) ve **denetim izi** (Faz 9: eylemler, sır süzgeci, değiştirilemezlik).
Rol tabanlı yetkilendirme (`TraconPolicies`) da burada — Tracon kullanıcı
veya rol saklamaz; roller tüketicinin kimlik sisteminden gelir, bu yüzden bazı
case'ler geçici bir test kimlik doğrulama şeması gerektirir (aşağıda §8).

> 🚨 **§8 artık ELLE KOD YAZMAYI gerektirmez (2026-08-18, K-431).** Örnek
> uygulama gösterim amaçlı bir rol şeması taşıyor: `Tracon:Demo:Roles:Enabled`
> `true` yapılır (ortam değişkeni yeter:
> `Tracon__Demo__Roles__Enabled=true`), rol `X-Tracon-Demo-Role:
> reader|operator|admin` başlığıyla gönderilir. Bayrak açıkken üç politika
> kaydedilir **ve** `RequireRolePolicies` açılır. §8'in `RoleTestAuthHandler.cs`
> yazma adımları bu yüzden bayrağı açıp kapamaya iner; aşağıdaki case'lerde
> `X-Test-Role` yerine yeni başlık kullanılır. Bayrak kapalıyken davranış
> eskisiyle birebir aynıdır (MT-SEC-080 hâlâ geçerlidir).

```mermaid
flowchart TD
    A["Istek"] --> B{"Loopback disi mi?"}
    B -- "Evet + AllowRemoteAccess kapali" --> R1["403 Uzak erisim kapali"]
    B -- "Hayir / acik" --> C{"Authorization basligi"}
    C -- "Yok + AuthToken tanimli" --> R2["401"]
    C -- "Yok + AuthToken tanimsiz" --> H["Handler calisir (K1)"]
    C -- "Var" --> D{"Statik token mi\nAPI anahtari mi?"}
    D -- "Ikisi de gecersiz" --> R2
    D -- "Statik token dogru" --> H
    D -- "API anahtari gecerli" --> E{"Tenant basligi\nanahtarla celisiyor mu?"}
    E -- "Evet" --> R3["403 Kiraci uyusmuyor"]
    E -- "Hayir" --> F{"Uc bir kapsam istiyor,\nanahtar tasimiyor mu?"}
    F -- "Evet" --> R4["403 Kapsam yetersiz"]
    F -- "Hayir" --> G{"Rol policy'si\nkayitli VE basarisiz mi?"}
    G -- "Evet" --> R5["403"]
    G -- "Hayir (basarili/kayitsiz)" --> H
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Genel HTTP sözleşmesi (`ProblemDetails` zarfı, CRUD, sayfalama, idempotency) | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| MCP sunucusu ekleme/silme, prompt/kaynak okuma, OAuth akışı, `ExternalSurfaceGuard`'ın MCP/A2A uçlarındaki uçtan uca davranışı | `18-MCP-VE-A2A.md` |
| Skill script sandbox'ı (`SandboxedSkillScriptRunner`, yorumlayıcı beyaz listesi, ortam değişkeni filtresi) | `14-SKILL-VE-SCRIPT.md` |
| Kalıcı onay kuralları (`ToolApprovalRule`) CRUD'u | `18-MCP-VE-A2A.md`'nin GovernanceEndpoints bölümü ile birlikte (`MapApprovalRules`) |
| `PatternContentGuard`/`DeniedTerms` içerik engeli (`content.blocked` denetim kaydının KENDİSİ değil, guard'ın karar mantığı) | `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` |
| PostgreSQL/SQLite/SQL Server'a özgü şema, migration, satır düzeyi kiracı sütunu | `03-KALICILIK-POSTGRESQL.md` · `04-KALICILIK-DIGER.md` (zaten üretildi) |
| Hız sınırlama (`TraconRateLimitFilter`, varsayılan kapalı) | **Hiçbir dosyaya atanmamış** — bkz. `00-INDEKS.md` §8 düşen not |
| Ses WebSocket el sıkışması (`Sec-WebSocket-Protocol` token taşıma) | `19-COK-MODLULUK-VE-SES.md` |
| `/api/diagnostics`'in genel şekli ve alanları | `25-SAGLIK-TESHIS-OPENAPI.md` — burada yalnız rol koruması bir örnek uç olarak kullanılır |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. `Tracon:Ui:AuthToken` sırrı `manuel-test-token-2026`'dır (`FIX-TOKEN-01`).
3. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080/tracon`.
4. Makinenin LAN adresini önceden öğrenin — "loopback dışı" case'ler AYNI
   makineden, yalnızca `127.0.0.1` yerine bu adres üzerinden bağlanarak
   yapılır (ikinci bir cihaz gerekmez):
   ```bash
   ifconfig | grep 'inet ' | grep -v 127.0.0.1
   # ornek: en0: inet 192.168.1.23
   export LANIP=192.168.1.23   # kendi ciktiniza gore degistirin
   ```
5. Kestrel yalnızca loopback'e değil bu arayüze de dinlemelidir; `dotnet run`
   yerine:
   ```bash
   cd samples/Tracon.Api && dotnet run --urls "http://0.0.0.0:5080"
   ```

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
export APULAN="http://$LANIP:5080/tracon"
```

> **Gerçek para uyarısı.** §4 (kiracı izolasyonu) `FIX-AGENT-01` ile gerçek bir
> `run` başlatır ve küçük ölçüde OpenAI ücreti doğurur. Diğer tüm bölümler
> (erişim katmanları, kiracı yönetimi, API anahtarları, roller, denetim izi)
> hiçbir model çağırmaz.
>
> **Doküman düzeltmesi (koşumda, 2026-08-13).** Yukarıdaki not artık YANLIŞ:
> `Tracon:Ui:AllowRemoteAccess` anahtarı 2026-08-11'de `Program.cs`'e
> bağlandı (commit `419981b`, satır 708-715 —
> `builder.Configuration.GetValue<bool?>("Tracon:Ui:AllowRemoteAccess")`).
> §7'deki `MT-SEC-070`/`071` artık **geçici kod değişikliği GEREKTİRMİYOR**;
> `Tracon__Ui__AllowRemoteAccess` ortam değişkeni/`user-secrets` anahtarı
> yeterli. Koşum bu şekilde (yalnız yapılandırma ile) yapıldı ve doğrulandı.

---

# 1 — Üç katmanlı erişim koruması (Faz 4/50, `TraconEndpointFilter`)

### MT-SEC-001 — Loopback'ten doğru token ile istek geçer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor, `AuthToken` = `FIX-TOKEN-01`.

**Adımlar**
1. `localhost` üzerinden korumalı bir uca istek at.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200`.

---

### MT-SEC-002 — Loopback dışından (LAN adresi) istek, `AllowRemoteAccess` kapalı → `403`

Negatif senaryo. Loopback kısıtı bearer token'dan ÖNCE denetlenir; doğru token
bile bu katmanı atlatmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- `dotnet run --urls "http://0.0.0.0:5080"` ile çalışıyor.
- `AllowRemoteAccess` varsayılan (`false`, hiç ayarlanmadı).

**Adımlar**
1. `$LANIP` üzerinden, DOĞRU token ile aynı uca istek at.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APULAN/api/agents" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 403`.
- `title: "Remote access disabled"`, `detail: "Tracon endpoints are reachable
  only from the same machine by default. For remote access, enable the
  AllowRemoteAccess setting and configure an authentication method (AuthToken
  or RequireAuthorization)."` (İngilizce — koşumda düzeltildi, K-228; ürün her
  zaman İngilizce metin döner).
- Token doğru olmasına rağmen reddedilir — loopback katmanı önce çalışır.

---

### MT-SEC-003 — Authorization başlığı yok, `AuthToken` tanımlı → `401` + `WWW-Authenticate`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- `localhost` üzerinden (loopback).

**Adımlar**
1. Authorization başlığı vermeden istek at.

**Girilecek veri**
```bash
curl -s -D - -o /dev/null "$APU/api/agents"
```

**Beklenen sonuç**
- `HTTP: 401`.
- Yanıt başlıklarında `WWW-Authenticate: Bearer` vardır.
- Gövde `title: "Authentication failed"`, `detail: "A valid 'Authorization:
  Bearer <token>' header is required."` (İngilizce — koşumda düzeltildi,
  K-228).

---

### MT-SEC-004 — Yanlış bearer token → `401`, gövde token hakkında bilgi vermez

Negatif senaryo. `FIX-TOKEN-02` kullanılır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- `localhost` üzerinden.

**Adımlar**
1. Yanlış token ile istek at.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents" -H "Authorization: Bearer yanlis-token"
```

**Beklenen sonuç**
- `HTTP: 401`.
- Gövde MT-SEC-003 ile **birebir aynı** (`title`/`detail`) — hangi token'ın
  neden yanlış olduğuna dair hiçbir ipucu yoktur.

---

### MT-SEC-005 — `Bearer` şeması olmayan bir Authorization başlığı → `401`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- `localhost` üzerinden.

**Adımlar**
1. `Basic` şemasıyla istek at (doğru token'ı bile taşısa fark etmez).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents" -H "Authorization: Basic manuel-test-token-2026"
```

**Beklenen sonuç**
- `HTTP: 401` — `BearerTokenValidator.IsValid` yalnız `"Bearer "` önekini kabul
  eder (`BearerTokenValidator.cs:31`).

---

### MT-SEC-006 — `Bearer ` öneki var ama değer boş → `401`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Adımlar**
1. Boş token ile istek at.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents" -H "Authorization: Bearer "
```

**Beklenen sonuç**
- `HTTP: 401`.

### MT-SEC-010 — `/api/meta`, loopback dışından VE Authorization başlıksız yine `200` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- `dotnet run --urls "http://0.0.0.0:5080"`.

**Adımlar**
1. `$LANIP` üzerinden, hiç Authorization başlığı vermeden `/api/meta` çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APULAN/api/meta"
```

**Beklenen sonuç**
- `HTTP: 200` — `TraconEndpointRouteBuilderExtensions.cs:82-84`'teki
  `metaGroup` hiçbir `AddEndpointFilter` taşımaz; loopback kısıtı da bearer
  denetimi de bu gruba uygulanmaz.
- Aynı anda MT-SEC-002'nin AYNI koşullar altında (aynı `$LANIP`, aynı eksik
  başlık) `/api/agents` için `403` verdiği doğrulanmış olur — meta ucu
  bilerek istisnadır.

---

### MT-SEC-011 — `/api/meta` yanıtı `AuthToken` DEĞERİNİ hiçbir alanda taşımaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | K-035 |

**Ön koşul**
- `localhost` üzerinden, `AuthToken` = `manuel-test-token-2026`.

**Adımlar**
1. `/api/meta` çağır, tam gövdeyi incele.

**Girilecek veri**
```bash
curl -s "$APU/api/meta" | python3 -m json.tool
```

**Beklenen sonuç**
- `authentication.requiresBearerToken: true` alanı vardır ama `manuel-test-token-2026`
  dizgisi gövdenin HİÇBİR yerinde geçmez.
- `authentication.allowRemoteAccess` ve `authentication.requiresAuthorizationPolicy`
  boolean alanları da vardır.

---

### MT-SEC-012 — Arayüz kabuğu bearer token'dan MUAFTIR ama loopback'ten muaf DEĞİLDİR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `dotnet run --urls "http://0.0.0.0:5080"`, `AuthToken` tanımlı.

**Adımlar**
1. `localhost`'tan, Authorization başlığı VERMEDEN kabuk sayfasını iste.
2. Aynı isteği `$LANIP` üzerinden tekrarla.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "loopback: %{http_code}\n" "$APU/"
curl -s -o /dev/null -w "lan:      %{http_code}\n" "$APULAN/"
```

**Beklenen sonuç — koşumda düzeltildi (doküman koda göre yanlıştı, kod
değil).** Spec `lan: 403` bekliyordu; gerçek kod her ikisi için de `200`
döner:
- `loopback: 200` VE `lan: 200` — `MapUi`'nin grubu
  `requireLoopback: false` ile kurulur
  (`TraconEndpointRouteBuilderExtensions.cs:344`, `MapUi`), yalnız
  `requireBearerToken: false` DEĞİL. XML doc'un kendi gerekçesi
  (`TraconEndpointRouteBuilderExtensions.cs:316-325`): kabuk hiçbir veri
  taşımaz, gerçek koruma veri uçlarındaki filtre örnekleridir; kabuk LAN'dan
  da yüklenebilmelidir ki istemci tarafı `AccessGate` bileşeni "uzak erişim
  kapalı" mesajını gösterebilsin — kabuğun kendisi engellenirse kullanıcı
  boş bir bağlantı hatası görür, açıklayıcı ekranı hiç göremez. Bu kasıtlı
  bir tasarım kararıdır, kusur değildir; gerçek koruma MT-SEC-002'nin
  doğruladığı veri uçlarındadır (`lan` + `/api/agents` → `403`).

### MT-SEC-020 — `UseTenancy` hiç çağrılmamışken her istek varsayılan kiracıya düşer

Bu case §3'ün ön koşulu UYGULANMADAN, temiz durumda koşulur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon:Tenancy:Enabled` AYARLANMAMIŞ (varsayılan kapalı).

**Adımlar**
1. `GET /api/tenants/current` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "$APB"
```

**Beklenen sonuç**
- `{"tenantId":"default"}` — `TraconOptions.DefaultTenantId` varsayılanı.

---

### MT-SEC-021 — `AllowHeaderResolution` açıkken `X-Tracon-Tenant` başlığı kiracıyı belirler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- §3 ön koşulu uygulandı (Tenancy açık, header çözümü açık).

**Adımlar**
1. `X-Tracon-Tenant: kiraci-alfa` (`FIX-TENANT-01`) başlığıyla iste.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa"
```

**Beklenen sonuç**
- `{"tenantId":"kiraci-alfa"}`.

---

### MT-SEC-022 — `AllowHeaderResolution` KAPALIYKEN aynı başlık yok sayılır

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Tenancy:Enabled" "true"
dotnet user-secrets remove "Tracon:Tenancy:AllowHeaderResolution"
```
Uygulama yeniden başlatılır.

**Adımlar**
1. Aynı başlıkla aynı çağrıyı tekrarla.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa"
```

**Beklenen sonuç**
- `{"tenantId":"default"}` — `Resolve()` `AllowHeaderResolution` kapalıyken
  `null` döner, başlık hiç okunmaz (`HttpTenantContext.cs:113-116`).

---

### MT-SEC-023 — Biçimsiz kiracı kimliği başlıkta gönderilirse sessizce reddedilir (hataya düşmez)

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- §3 ön koşulu uygulandı.

**Adımlar**
1. Boşluk ve `/` içeren geçersiz bir değerle iste.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/tenants/current" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci alfa/beta"
```

**Beklenen sonuç**
- `HTTP: 200` (hata verilmez).
- `tenantId: "default"` — `IsValidTenantId` `^[a-zA-Z0-9_.-]+$` desenine
  uymayan değeri reddeder (`HttpTenantContext.cs:86-89,138`) ve `Accept`
  `null` döner; zincir varsayılana düşer.

---

### MT-SEC-024 — `AllowedTenants` beyaz listesi doluyken listede olmayan bir değer 403 ile reddedilir

Negatif senaryo. **Düzeltilmiş kusur (2026-08-10).** Bu case önceden bir
"şüpheli bulgu" olarak yazılmıştı: `HttpTenantContext.Accept`'in `null`
dönüşü, çağıran zincirde (`TenantId => ... ?? Resolve() ?? DefaultTenantId`)
sessizce varsayılan kiracıya düşüyordu — `AllowedTenants`'ın kendi XML
belgesiyle ("listede olmayan bir değer varsayılan kiracıya düşmez") doğrudan
çelişen bir davranıştı. Kod okumasıyla doğrulandı ve düzeltildi:
`TraconEndpointFilter`'a `CheckTenancyWhitelist` eklendi — istek artık
endpoint'e ulaşmadan ÖNCE 403 ile reddedilir. Bu case artık DÜZELTİLMİŞ
davranışı doğrular.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Tenancy:Enabled" "true"
dotnet user-secrets set "Tracon:Tenancy:AllowHeaderResolution" "true"
```
`--Tracon:Tenancy:AllowedTenants=kiraci-alfa` ile başlat.
**Geçici kod eklemeye GEREK YOK** — örnek uygulama bu anahtarı 2026-09-19'dan
beri kalıcı olarak okuyor (K-834). Virgülle ayrılır; anahtar boş/yoksa liste
boş kalır, yani "beyaz liste yok".

**Adımlar**
1. Listede OLMAYAN bir kiracı adıyla iste (`kiraci-gamma`).

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "$APB" -H "X-Tracon-Tenant: kiraci-gamma"
```

**Beklenen sonuç**
- `403 Forbidden` döner. Sevk edilen başlık **İngilizce**dir:
  `title: "Tenant rejected"` (K-228; ölçüldü 2026-09-19). `detail` ayrıca
  kuralı cümleyle yazar: *"This request is NOT silently downgraded to the
  default tenant's data; it is rejected."*
- `{"tenantId":"default"}` **DÖNMEZ** — beyaz listeye girmeyen bir istemci
  varsayılan kiracının verisini asla görmemelidir. Bu gözlemlenirse (fix
  öncesi davranışa dönüş) **Kusur, Önem: Kritik** — bkz. `00-INDEKS.md` §5.

### MT-SEC-030 — Kiracı A'da `FIX-AGENT-01` oluşturma

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Adımlar**
1. `kiraci-alfa` (`FIX-TENANT-01`) başlığıyla `manuel-destek` agent'ını oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-alfa" -H "content-type: application/json" -d '{
  "name": "manuel-destek",
  "instructions": "Sen bir siparis destek asistanisin. Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "toolNames": ["get_order_status"]
}'
```

**Beklenen sonuç**
- `HTTP: 201`.

---

### MT-SEC-031 — Kiracı B'de AYNI adla oluşturma çakışmaz (ayrı satır)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-030 geçti.

**Adımlar**
1. `kiraci-beta` (`FIX-TENANT-02`) başlığıyla AYNI adla oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-beta" -H "content-type: application/json" -d '{
  "name": "manuel-destek",
  "instructions": "Sen bir siparis destek asistanisin. Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "toolNames": ["get_order_status"]
}'
```

**Beklenen sonuç**
- `HTTP: 201` — MT-SEC-030'daki `409` DEĞİL. `agent_definitions` tablosunun
  `UNIQUE (tenant_id, name)` kısıtı iki farklı kiracıda aynı adı serbest
  bırakır (`0001_initial.sql:39`).

**Doğrulama sorgusu** *(PostgreSQL izleğinde)*
```sql
SELECT tenant_id, name, version FROM tracon.agent_definitions
WHERE name = 'manuel-destek' ORDER BY tenant_id;
```

---

### MT-SEC-032 — Kiracı A'nın listesi yalnız kendi agent'ını gösterir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-030, MT-SEC-031 geçti.

**Adımlar**
1. `kiraci-alfa` başlığıyla listele.

**Girilecek veri**
```bash
curl -s "$APU/api/agents" -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" | python3 -m json.tool
```

**Beklenen sonuç**
- Yanıt `manuel-destek` içerir; `InMemoryAgentDefinitionStore`/SQL deposu
  yalnız `tenant_id = 'kiraci-alfa'` satırlarını döner. Kiracı B'nin
  eklediği başka hiçbir tanım (varsa) görünmez.

---

### MT-SEC-033 — Kiracı A bir çalıştırma başlatır (`FIX-PROMPT-02`)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-030 geçti. Örnek uygulama gerçek bir OpenAI anahtarıyla çalışıyor.

**Adımlar**
1. `kiraci-alfa` olarak `manuel-destek`'i çalıştır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/manuel-destek/run" \
     -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" -H "content-type: application/json" \
     -d '{ "message": "Merhaba" }'
```

**Beklenen sonuç**
- `HTTP: 200`. Yanıt gövdesinden `runId`'yi not edin (`export RUNID=...`).

---

### MT-SEC-034 — Kiracı A kendi çalıştırmasını görebilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-033 geçti, `$RUNID` alındı.

**Adımlar**
1. Aynı kiracıyla çalıştırmayı sorgula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs/$RUNID" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-alfa"
```

**Beklenen sonuç**
- `HTTP: 200`.

---

### MT-SEC-035 — Kiracı B aynı `runId`'yi `404` ile görür (403 DEĞİL)

Negatif senaryo. `RunEndpoints.GetRunInputAsync`/benzeri her yerde "yok" ile
"başka kiracıya ait" **aynı** `404`'u döner (`RunEndpoints.cs:263-266,300-309`)
— varlık sızıntısını önlemek için bilinçli bir tasarım.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-033 geçti, `$RUNID` alındı.

**Adımlar**
1. Kiracı B başlığıyla AYNI `runId`'yi sorgula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs/$RUNID" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-beta"
```

**Beklenen sonuç**
- `HTTP: 404` (`403` DEĞİL) — kiracı B'ye "bu çalıştırma var ama senin değil"
  bilgisi bile sızdırılmaz.

---

### MT-SEC-036 — Kiracı B kendi `manuel-destek` kopyasını siler; Kiracı A'nınki etkilenmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-030, MT-SEC-031 geçti.

**Adımlar**
1. Kiracı B başlığıyla `manuel-destek`'i sil.
2. Kiracı A başlığıyla aynı adı GET ile sorgula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/agents/manuel-destek" \
     -H "$APB" -H "X-Tracon-Tenant: kiraci-beta"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/manuel-destek" \
     -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa"
```

**Beklenen sonuç**
- Adım 1: `HTTP: 204`.
- Adım 2: `HTTP: 200` — Kiracı A'nın kopyası hâlâ vardır; silme yalnızca
  kendi kiracısının satırını etkiler.

### MT-SEC-040 — `PUT /api/tenants/{slug}` yeni bir kiracı kaydı oluşturur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Adımlar**
1. `kiraci-alfa` slug'ıyla kayıt oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/tenants/kiraci-alfa" -H "$APB" \
     -H "content-type: application/json" -d '{ "displayName": "Alfa Musterisi" }'
```

**Beklenen sonuç**
- `HTTP: 200`. Gövde `slug: "kiraci-alfa"`, `displayName: "Alfa Musterisi"`.

**Doğrulama sorgusu**
```sql
SELECT slug, display_name FROM tracon.tenants WHERE slug = 'kiraci-alfa';
```

---

### MT-SEC-041 — Aynı slug'a ikinci `PUT` günceller (upsert)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-040 geçti.

**Adımlar**
1. Aynı slug'a farklı bir `displayName` ile PUT et.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/kiraci-alfa" -H "$APB" -X PUT \
     -H "content-type: application/json" -d '{ "displayName": "Alfa Musterisi (guncel)" }'
```

**Beklenen sonuç**
- `HTTP: 200`, `displayName` güncellenmiştir. İkinci bir satır OLUŞMAZ
  (`slug` anahtardır).

---

### MT-SEC-042 — Geçersiz biçimli slug → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Adımlar**
1. Boşluk içeren bir slug ile PUT dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/tenants/kiraci%20alfa" -H "$APB" \
     -H "content-type: application/json" -d '{ "displayName": "x" }'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Tenant key invalid"`, `detail: "The key must be at
  most 64 characters and contain only letters, digits, dots, underscores,
  and hyphens."` (İngilizce — koşumda düzeltildi, K-228).

---

### MT-SEC-043 — `GET /api/tenants` kayıtlı kiracıları listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-040 geçti.

**Adımlar**
1. Listele.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `kiraci-alfa` listede vardır.

---

### MT-SEC-044 — `DELETE /api/tenants/{slug}` yalnız KAYDI siler, kiracının verisi kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-030 (kiraci-alfa'da `manuel-destek` var) VE MT-SEC-040
  (`kiraci-alfa` kaydı var) geçti.

**Adımlar**
1. `kiraci-alfa` kaydını sil.
2. Aynı kiracının agent'ını GET ile sorgula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/tenants/kiraci-alfa" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/manuel-destek" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-alfa"
```

**Beklenen sonuç**
- Adım 1: `HTTP: 204`.
- Adım 2: `HTTP: 200` — kayıt olmayan bir kiracı çalışma anında hata
  üretmez (`ITenantStore` XML doc, `TenantDescriptor.cs:5-9`).

---

### MT-SEC-045 — Var olmayan slug'ı silmeye çalışmak → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Adımlar**
1. Hiç var olmamış bir slug'ı sil.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/tenants/hic-yok" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`. `title: "Tenant not found"` (İngilizce — koşumda düzeltildi,
  K-228).

### MT-SEC-050 — `POST /api/api-keys` yeni anahtar üretir, ham değer `ap_` ile başlar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Adımlar**
1. `RunsRead` kapsamlı bir anahtar oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/api-keys" -H "$APB" \
     -H "content-type: application/json" -d '{ "name": "manuel-okuma", "scopes": ["RunsRead"] }'
```

**Beklenen sonuç**
- `HTTP: 200`.
- `plaintextKey` `ap_` ile başlar (`ApiKeyGenerator.cs:39`,
  `KeyPrefixMarker = "ap_"`).
- `record.keyPrefix` de `ap_` ile başlar ve `plaintextKey`'in ilk 12
  karakteridir.
- `record.name: "manuel-okuma"`, `record.scopes: ["RunsRead"]`.
- Bu değeri kaydedin: `export APIKEY_READ=<plaintextKey>`,
  `export APIKEY_READ_ID=<record.id>`.

---

### MT-SEC-051 — `GET /api/api-keys` listesi ham değer ve özet TAŞIMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | K-059 |

**Ön koşul**
- MT-SEC-050 geçti.

**Adımlar**
1. Listele, ham gövdeyi incele.

**Girilecek veri**
```bash
curl -s "$APU/api/api-keys" -H "$APB"
```

**Beklenen sonuç**
- Yanıt gövdesinde `$APIKEY_READ` dizgisi (plaintext) HİÇBİR yerde geçmez.
- Alanlar yalnız `id`, `tenantId`, `name`, `keyPrefix`, `scopes`, `expiresAt`,
  `revokedAt`, `lastUsedAt`, `createdAt`, `isActive`'dir — `keyHash` yoktur.

---

### MT-SEC-052 — `name` boş → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Adımlar**
1. Boş adla oluşturmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/api-keys" -H "$APB" \
     -H "content-type: application/json" -d '{ "name": "", "scopes": ["RunsRead"] }'
```

**Beklenen sonuç**
- `HTTP: 400`. `detail: "'name' cannot be empty."` (İngilizce — koşumda
  düzeltildi, K-228).

---

### MT-SEC-053 — Boş `scopes` dizisi → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Adımlar**
1. Boş kapsam listesiyle oluşturmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/api-keys" -H "$APB" \
     -H "content-type: application/json" -d '{ "name": "bos-kapsam", "scopes": [] }'
```

**Beklenen sonuç**
- `HTTP: 400`. `detail: "At least one scope ('scopes') must be selected."`
  (İngilizce — koşumda düzeltildi, K-228).

---

### MT-SEC-054 — Bilinmeyen kapsam değeri → `400` (kapalı liste)

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Adımlar**
1. Serbest metin bir kapsam adıyla oluşturmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/api-keys" -H "$APB" \
     -H "content-type: application/json" -d '{ "name": "gecersiz", "scopes": ["runs:hepsi"] }'
```

**Beklenen sonuç**
- `HTTP: 400` — `JsonStringEnumConverter<ApiKeyScope>` bilinmeyen dizgiyi
  reddeder, model binding hatası döner.

---

### MT-SEC-055 — Üretilen anahtar, kapsamı yeten bir uçta Bearer olarak çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-050 geçti (`$APIKEY_READ`, `RunsRead` kapsamlı).

**Adımlar**
1. Statik token YERİNE bu anahtarla `GET /api/runs` çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs" -H "Authorization: Bearer $APIKEY_READ"
```

**Beklenen sonuç**
- `HTTP: 200` — `RunEndpoints.cs:79`'daki liste ucu `RunsRead` ister,
  anahtar bunu taşır.

---

### MT-SEC-056 — Aynı anahtar, kapsam DIŞI bir uçta `403 Kapsam yetersiz` alır

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-050 geçti (`$APIKEY_READ`, yalnız `RunsRead`).

**Adımlar**
1. `AgentsAdmin` isteyen `POST /api/agents`'ı bu anahtarla dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" \
     -H "Authorization: Bearer $APIKEY_READ" -H "content-type: application/json" -d '{
  "name": "kapsam-disi-deneme",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 403`. `title: "Insufficient scope"`, `detail: "This endpoint
  requires the 'AgentsAdmin' scope; the key does not carry it."` (İngilizce —
  koşumda düzeltildi, K-228).

---

### MT-SEC-057 — `DELETE /api/api-keys/{id}` iptal eder; sonra o anahtarla istek `401` alır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-050 geçti.

**Adımlar**
1. Anahtarı iptal et.
2. Aynı anahtarla (hâlâ elde tutulan `$APIKEY_READ`) tekrar istek at.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/api-keys/$APIKEY_READ_ID" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs" -H "Authorization: Bearer $APIKEY_READ"
```

**Beklenen sonuç**
- Adım 1: `HTTP: 204`.
- Adım 2: `HTTP: 401` — satır SİLİNMEZ, `revokedAt` yazılır ve
  `ApiKeyAuthenticator.AuthenticateAsync` bunu reddeder (`ApiKeyAuthenticator.cs:42-45`).

**Doğrulama sorgusu**
```sql
SELECT id, revoked_at FROM tracon.api_keys WHERE id = '<APIKEY_READ_ID>';
-- satir hala vardir, revoked_at doludur.
```

---

### MT-SEC-058 — Var olmayan veya zaten iptal edilmiş `id`'yi tekrar iptal etmek → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-057 geçti (`$APIKEY_READ_ID` zaten iptal edilmiş).

**Adımlar**
1. Aynı `id`'yi tekrar iptal etmeyi dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/api-keys/$APIKEY_READ_ID" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404` — `RevokeAsync` `RevokedAt is not null` satırını da `false`
  sayar (`InMemoryApiKeyStore.cs:81-86`); ikinci iptal "bulunamadı" gibi
  görünür.

---

### MT-SEC-059 — Süresi geçmiş anahtar `401` alır

Negatif senaryo. `POST /api/api-keys` gelecekteki bir tarih ister; geçmiş bir
`expiresAt` üretmek doğrudan API üzerinden mümkün değildir (mantıksız olurdu),
bu yüzden ön koşul çok kısa bir sürede dolan bir anahtar üretir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Adımlar**
1. 5 saniye sonra dolan bir anahtar oluştur.
2. 6 saniye bekle.
3. Aynı anahtarla istek at.

**Girilecek veri**
```bash
EXPIRES=$(date -u -v+5S +"%Y-%m-%dT%H:%M:%SZ")   # macOS 'date'
curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
     -d "{ \"name\": \"kisa-omur\", \"scopes\": [\"RunsRead\"], \"expiresAt\": \"$EXPIRES\" }"
# yanittaki plaintextKey'i $APIKEY_SHORT olarak kaydedin, 6 sn bekleyin, sonra:
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs" -H "Authorization: Bearer $APIKEY_SHORT"
```

**Beklenen sonuç**
- Adım 3: `HTTP: 401` — `ApiKeyAuthenticator.cs:42`, `ExpiresAt <= now`.

---

### MT-SEC-060 — `X-Tracon-Tenant` başlığı, anahtarın kiracısıyla ÇELİŞİRSE `403`

Negatif senaryo. `bölüm 53.5`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Tenancy:Enabled" "true"
dotnet user-secrets set "Tracon:Tenancy:AllowHeaderResolution" "true"
```
Uygulama yeniden başlatılır (anahtarlar sıfırlanır — yeniden üretin, bu
kez `kiraci-alfa` kiracısı için `AgentsRead` kapsamlı bir anahtar):
```bash
curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" \
     -H "content-type: application/json" -d '{ "name": "alfa-anahtar", "scopes": ["AgentsRead"] }'
# plaintextKey -> $APIKEY_ALFA
```

**Adımlar**
1. Bu anahtarla, `X-Tracon-Tenant: kiraci-beta` başlığı EKLEYEREK
   (anahtarın kiracısıyla çelişen bir değer) istek at.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents" \
     -H "Authorization: Bearer $APIKEY_ALFA" -H "X-Tracon-Tenant: kiraci-beta"
```

**Beklenen sonuç**
- `HTTP: 403`. `title: "Tenant mismatch"`, `detail: "The 'X-Tracon-Tenant'
  header CANNOT override the tenant the API key is bound to. Remove the
  header or give a value matching the key's tenant."` (İngilizce — koşumda
  düzeltildi, K-228).

---

### MT-SEC-061 — Başlık HİÇ verilmezse kiracı doğrudan anahtardan çözülür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-060'ın ön koşulu (Tenancy açık, `$APIKEY_ALFA`).

**Adımlar**
1. `X-Tracon-Tenant` başlığı VERMEDEN `GET /api/tenants/current` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "Authorization: Bearer $APIKEY_ALFA"
```

**Beklenen sonuç**
- `{"tenantId":"kiraci-alfa"}` — kiracı anahtarın `TenantId`'sinden çözülür,
  başlığa hiç ihtiyaç yoktur (`HttpTenantContext.cs:91-94`, `ResolveFromApiKey`
  `Resolve()`'dan (başlık/claim) ÖNCE denenir).

---

### MT-SEC-062 — `apikey.create`/`apikey.revoke` denetim izine düşer, ham değer YAZILMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 9, 53 |
| **İlgili karar** | K-059 |

**Ön koşul**
- Reset sonrası temiz durum (Tenancy kapalı).

**Adımlar**
1. Bir anahtar oluştur, `id`'sini not et.
2. İptal et.
3. Denetim izini o varlık için sorgula.

**Girilecek veri**
```bash
CREATED=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{ "name": "denetim-testi", "scopes": ["RunsRead"] }')
KEYID=$(echo "$CREATED" | python3 -c "import sys,json;print(json.load(sys.stdin)['record']['id'])")
curl -s -X DELETE "$APU/api/api-keys/$KEYID" -H "$APB" -o /dev/null
curl -s "$APU/api/audit/apikey:$KEYID" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- İki kayıt döner: `action: "apikey.create"` ve `action: "apikey.revoke"`.
- `create` kaydının `after` alanı yalnız `name`, `keyPrefix`, `scopes`
  taşır (`ApiKeyEndpoints.Describe`, satır 136-156) — ham değer veya özet
  YOKTUR.
- `revoke` kaydının `before`/`after` alanları `null`'dır.

### MT-SEC-070 — `external:invoke` anahtarı YOKKEN `AllowRemoteAccess = true` yapılırsa uygulama AÇILMAZ

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50, 53 |
| **İlgili karar** | — |

**Ön koşul**
- Reset sonrası temiz durum — sistemde HİÇ `ExternalInvoke` kapsamlı anahtar
  yok.
- **Koşumda düzeltildi (kural 1 istisnası):** GEÇİCİ kod değişikliği artık
  gerekmiyor — dosyanın başındaki not (§"Koşmadan önce") zaten
  `Tracon:Ui:AllowRemoteAccess`'in `Program.cs:953-956`'ya bağlandığını
  söylüyor, bu case'in kendi ön koşulu güncellenmemişti. Ortam değişkeni
  yeterli:
  ```bash
  export Tracon__Ui__AllowRemoteAccess=true
  ```

**Adımlar**
1. `dotnet run` ile başlat.

**Girilecek veri**
```bash
cd samples/Tracon.Api && dotnet run
```

**Beklenen sonuç**
- Süreç açılışta `InvalidOperationException` ile ÇÖKER. Konsol çıktısı
  `MCP cannot be exposed while AllowRemoteAccess is on: the system holds no
  API key with the 'external:invoke' scope...` dizgisini içerir (İngilizce —
  koşumda düzeltildi, K-228; kod: `ExternalSurfaceGuard.cs:142-147`).
- Bu, `EnsureRemoteAccessNotCombined`'in senkron ve açılışta çalıştığının
  kanıtıdır — hiçbir istek bu denetimin önüne geçemez.

---

### MT-SEC-071 — Önce `external:invoke` anahtarı üretilir, SONRA `AllowRemoteAccess = true` başarıyla açılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50, 53 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon__Ui__AllowRemoteAccess` ortam değişkeni KALDIRILIR (varsayılana
  dönülür — koşumda düzeltildi, bkz. MT-SEC-070).
- Uygulama `AllowRemoteAccess` OLMADAN normal başlatılır.
- **Ekleme (koşumda öğrenildi):** varsayılan bellek içi depoda `ExternalInvoke`
  anahtarı restart'ta KAYBOLUR — bu case'in "önce anahtar üret, SONRA
  yeniden başlat" akışı gerçek anlamda ancak KALICI bir depoyla (SQLite/
  PostgreSQL/SQL Server) test edilebilir. Bellek içiyle koşulursa MT-SEC-070
  ile birebir aynı çöküş tekrar gözlenir (yanlış negatif değil, doğru ama
  farklı bir senaryo test edilmiş olur).

**Adımlar**
1. `ExternalInvoke` kapsamlı bir anahtar oluştur.
2. Uygulamayı durdur, `Tracon__Ui__AllowRemoteAccess=true` İLE (kalıcı depo
   AÇIK kalarak, aynı veritabanı dosyası/şeması) yeniden başlat.
3. `$LANIP` üzerinden, doğru bearer token ile bir uca istek at.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
     -d '{ "name": "mcp-disa-acik", "scopes": ["ExternalInvoke"] }'
# ... uygulamayi AllowRemoteAccess=true ile yeniden baslat ...
curl -s -w "\nHTTP: %{http_code}\n" "$APULAN/api/agents" -H "$APB"
```

**Beklenen sonuç**
- Adım 1: `HTTP: 200`, anahtar üretilir.
- Adım 2: uygulama BAŞARIYLA açılır (çökmez).
- Adım 3: `HTTP: 200` — `AllowRemoteAccess=true` artık loopback kısıtını
  kaldırmıştır; MT-SEC-002'nin verdiği `403` burada ALINMAZ.

### MT-SEC-080 — Hiçbir rol testi kurulmadan (varsayılan): Admin gerektiren uç bile rol kontrolüne takılmaz

Bu case §8'in GEÇİCİ kurulumu UYGULANMADAN, sade haliyle koşulur (kurulum
sırası: önce bu case, sonra 1-5 adımları).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | K-042 |

**Adımlar**
1. Hiçbir rol başlığı olmadan `POST /api/agents` (Admin gerektirir) çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "rol-yok-deneme",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 201` — hiçbir `Tracon.*` policy'si kayıtlı olmadığından
  `RequireRole` hiçbir şey eklemez (K-042'nin aynı gerekçesi: rol modeli
  yükseltmeyi kırmaz).

---

### MT-SEC-081 — Yalnız `reader` rolüyle Admin ucu `403` alır

Negatif senaryo. §8'in geçici kurulumu (1-5) UYGULANMIŞ olmalı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- §8 geçici kurulumu tamam.

**Adımlar**
1. `X-Test-Role: reader` ile `POST /api/agents` dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "X-Test-Role: reader" -H "content-type: application/json" -d '{
  "name": "reader-deneme",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 403`.

---

### MT-SEC-082 — `admin` rolüyle aynı istek `201` alır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- §8 geçici kurulumu tamam.

**Adımlar**
1. `X-Test-Role: admin` ile aynı isteği tekrarla.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "X-Test-Role: admin" -H "content-type: application/json" -d '{
  "name": "admin-deneme",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 201`.

---

### MT-SEC-083 — `operator` rolü çalıştırma başlatabilir ama Admin ucuna erişemez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- §8 geçici kurulumu tamam. `manuel-bos` (`FIX-AGENT-02`) mevcut değilse
  önce (rol başlıksız, kod tanımlı `support` yeterlidir) örnek uygulamanın
  kod tanımlı bir agent'ı kullanılabilir — `support`.

**Adımlar**
1. `X-Test-Role: operator` ile `POST /api/agents/support/run` çağır
   (Operator yeter).
2. Aynı rolle `DELETE /api/api-keys/{herhangi-bir-guid}` dene (Admin ister).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" \
     -H "$APB" -H "X-Test-Role: operator" -H "content-type: application/json" \
     -d '{ "message": "Merhaba" }'
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/api-keys/00000000-0000-0000-0000-000000000000" \
     -H "$APB" -H "X-Test-Role: operator"
```

**Beklenen sonuç**
- Adım 1: `HTTP: 200`.
- Adım 2: `HTTP: 403` (`404` DEĞİL — rol denetimi handler'dan ÖNCE çalışır).

---

### MT-SEC-084 — `RequireRolePolicies = true` + hiçbir policy kayıtlı değilken uygulama AÇILMAZ

Negatif senaryo. §8'in kurulumu (1-4) GERİ ALINIR (yalnız bu case için) —
`AddAuthentication`/`AddAuthorization` çağrıları KALDIRILIR, yalnız
`options.RequireRolePolicies = true;` `MapTracon` lambda'sına eklenir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- `RoleTestAuthHandler.cs` silinmiş, `AddAuthentication`/`AddAuthorization`
  eklemeleri geri alınmış (temiz `Program.cs`).
- `MapTracon("/tracon", options => { ... options.RequireRolePolicies = true; })`
  satırı eklenmiş.

**Adımlar**
1. `dotnet run` ile başlat.

**Girilecek veri**
```bash
cd samples/Tracon.Api && dotnet run
```

**Beklenen sonuç**
- Açılışta `InvalidOperationException`. Mesaj üç policy adını da içerir:
  `Tracon.Reader`, `Tracon.Operator`, `Tracon.Admin`
  (`TraconRolePolicies.cs:82-87`).

---

### MT-SEC-085 — `/api/meta`'nın `roles` alanı: hiçbir policy kayıtlı değilken hepsi `true`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-084'ün geçici satırı da kaldırılmış, sade örnek uygulama çalışıyor.

**Adımlar**
1. `/api/meta` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/meta" | python3 -c "import sys,json;print(json.load(sys.stdin)['roles'])"
```

**Beklenen sonuç**
- `{"canRead": true, "canOperate": true, "canAdminister": true}` —
  `MetaEndpoints.SatisfiesAsync` `policyName is null` iken `true` döner
  (`MetaEndpoints.cs:106-109`): rol kısıtı yoksa herkes "yetkili" görünür,
  çünkü gerçek kısıt üç katmanlı korumadadır.

### MT-SEC-090 — `agent.create` → `agent.update` → `agent.delete` sırası izlenebilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Ön koşul**
- Reset sonrası temiz durum.

**Adımlar**
1. `manuel-audit` adıyla bir agent oluştur.
2. `instructions`'ını değiştirerek güncelle (`PUT`).
3. Sil.
4. `GET /api/audit?entity=agent:manuel-audit` sorgula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-audit",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}' -o /dev/null
curl -s -X PUT "$APU/api/agents/manuel-audit" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-audit",
  "instructions": "Guncellendi.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}' -o /dev/null
curl -s -X DELETE "$APU/api/agents/manuel-audit" -H "$APB" -o /dev/null
curl -s "$APU/api/audit?entity=agent:manuel-audit" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Üç kayıt döner, EN YENİDEN ESKİYE sıralı: `agent.delete`, `agent.update`,
  `agent.create`.
- Her kaydın `entity: "agent:manuel-audit"`.

---

### MT-SEC-091 — `authToken`/`apiKey` gibi sır adlı bir alan varsa değeri `"***"` olur

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Ön koşul**
- Reset sonrası temiz durum. Bir MCP sunucusu kaydı yazılır — `Headers`
  sözlüğü serbest anahtar/değer taşıdığı için sır adı içeren bir alan
  üretilebilir.

**Adımlar**
1. `Authorization` başlığı taşıyan bir MCP sunucusu tanımı kaydet.
2. Denetim izini o varlık için sorgula.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/mcp-servers/manuel-sir-testi" -H "$APB" \
     -H "content-type: application/json" -d '{
  "endpoint": "https://ornek.invalid/mcp",
  "headers": { "Authorization": "Bearer cok-gizli-deger" }
}' -o /dev/null
curl -s "$APU/api/audit/mcp:manuel-sir-testi" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `after` alanının JSON'unda `"Authorization":"***"` görünür — ham değer
  (`cok-gizli-deger`) gövdenin hiçbir yerinde YOKTUR
  (`AuditSecretFilter.cs:24-31`, anahtar adı `"authorization"` fragmanını
  içerir).

---

### MT-SEC-092 — Çoğul `tokens` içeren bir alan (örn. `maxOutputTokens`) REDAKTE EDİLMEZ

Negatif senaryo / sınır durumu — `AuditSecretFilter`'ın "token" fragmanı
istisnası.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Ön koşul**
- Reset sonrası temiz durum.

**Adımlar**
1. `model.maxOutputTokens` alanı dolu bir agent oluştur.
2. Denetim kaydını incele.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-token-alani",
  "model": { "provider": "openai", "model": "gpt-5.4-mini", "maxOutputTokens": 512 }
}' -o /dev/null
curl -s "$APU/api/audit/agent:manuel-token-alani" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `after.model.maxOutputTokens: 512` — GERÇEK sayısal değeriyle görünür,
  `"***"` DEĞİL. `AuditSecretFilter.IsSecretKey` `"token"` fragmanını
  `"tokens"` (çoğul) geçen alan adlarında BİLEREK atlar
  (`AuditSecretFilter.cs:119-129`) — aksi halde her agent kaydı anlamsızca
  boşalırdı.

---

### MT-SEC-093 — Kimlik doğrulaması yokken `actor` her zaman `null`'dur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Ön koşul**
- Sade örnek uygulama (§8'in geçici kimlik doğrulaması KURULU DEĞİL).

**Adımlar**
1. Bir agent oluştur (statik bearer token ile, kimlik doğrulama şeması yok).
2. Denetim kaydını incele.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-aktor-testi",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}' -o /dev/null
curl -s "$APU/api/audit/agent:manuel-aktor-testi" -H "$APB" | python3 -c "import sys,json;print(json.load(sys.stdin)[0]['actor'])"
```

**Beklenen sonuç**
- `None` (JSON `null`) — `AmbientAuditActorResolver.Resolve` `user.Identity.IsAuthenticated
  != true` olduğunda `null` döner (`AmbientAuditActorResolver.cs:32-35`); bu
  durum arayüzde "bilinmiyor" gösterilir, gizlenmez.

---

### MT-SEC-094 — `limit` parametresi dönen kayıt sayısını sınırlar

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-090 geçti (en az 3 denetim kaydı var).

**Adımlar**
1. `limit=1` ile sorgula.

**Girilecek veri**
```bash
curl -s "$APU/api/audit?entity=agent:manuel-audit&limit=1" -H "$APB" | python3 -c "import sys,json;print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- `1` — `AuditEndpoints.cs:40`, `Math.Clamp(max, 1, 500)`.

---

### MT-SEC-095 — Denetim izinde silme/düzeltme ucu YOKTUR

Negatif senaryo, mimari değişmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 9 |
| **İlgili karar** | — |

**Adımlar**
1. `/api/audit/{entity}` yoluna `DELETE` dene (yol GET için kayıtlı,
   metod için değil).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/audit/agent:manuel-audit" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 405` (Method Not Allowed) — `AuditEndpoints.cs` yalnız iki `MapGet`
  içerir (satır 20, 58), hiçbir `MapDelete`/`MapPut`/`MapPatch` yoktur; ASP.NET
  Core aynı şablona eşleşen ama kabul edilmeyen bir metotta `405` döner.

---

### MT-SEC-100 — Koşulsuz kural eskisi gibi çalışır: eşik altında otomatik geçer

Faz 63. `ArgumentConditions` boşken bir kural `tool`'un HER çağrısıyla eşleşir — geriye dönük davranış birebir korunur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | K-451, K-452 |

**Adımlar**
1. `refund_order` tool'unu onay isteyecek şekilde kaydet.
2. Koşulsuz bir kural yaz (`argumentConditions: []`).
3. Tool'u çağırt.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/approvals/rules" -H "$APB" -H "content-type: application/json" -d '{
  "toolName": "refund_order"
}'
```

**Beklenen sonuç**
- `HTTP: 201`, gövdede `"argumentConditions":[]` — kural her çağrıyı otomatik onaylar (`ToolApprovalRuleEvaluator.IsAutoApprovedAsync`, "Neither an argument fingerprint nor conditions" dalı).

---

### MT-SEC-101 — `amount <= 100` koşullu kural: eşik altında otomatik geçer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | K-451 |

**Ön koşul**
- `refund_order` tool'u onay ister, MT-SEC-100'ün kuralı SİLİNMİŞ (aksi hâlde koşulsuz kural her şeyi zaten onaylar).

**Adımlar**
1. `amount <= 100` koşullu kural yaz.
2. `amount: 50` ile çağırt.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/approvals/rules" -H "$APB" -H "content-type: application/json" -d '{
  "toolName": "refund_order",
  "argumentConditions": [{"path": "amount", "operator": "LessThanOrEqual", "value": 100}]
}'
```

**Beklenen sonuç**
- `HTTP: 201`, gövdede `"operator":"LessThanOrEqual"` (SAYI değil, DİZE — `ToolArgumentOperator`
  `JsonStringEnumConverter` ile işaretlidir, K-040). Ardından `amount: 50` ile
  çağrılan `refund_order` onay İSTEMEDEN çalışır.

---

### MT-SEC-102 — Aynı kural, eşik üstünde onay ister

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-101'in kuralı kayıtlı.

**Adımlar**
1. `amount: 500` ile çağırt.

**Beklenen sonuç**
- Çağrı onay İSTER (`ToolApprovalRequestContent` döner) — 500, 100'den büyük olduğu için koşul eşleşmez ve kapalı düşme devreye girer.

---

### MT-SEC-103 — Argüman hiç gönderilmezse onay ister (yol çözülemez → kapalı düşme)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-101'in kuralı kayıtlı.

**Adımlar**
1. `amount` alanı OLMADAN `refund_order`'ı çağırt.

**Beklenen sonuç**
- Çağrı onay İSTER — `ToolArgumentConditionMatcher.TryResolvePath` yolu çözemez, koşul eşleşmez.

---

### MT-SEC-104 — Tip uyuşmazsa onay ister (`"50"` metni sayı kuralını geçemez)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-101'in kuralı kayıtlı.

**Adımlar**
1. `amount: "50"` (DİZE olarak) ile çağırt.

**Beklenen sonuç**
- Çağrı onay İSTER — sessiz tip dönüştürme yapılmaz; `"50"` metni ile `100` sayısı aynı JSON türünde değildir.

---

### MT-SEC-105 — Kodda kayıtlı politika veri kuralını EZER

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | K-451 |

**Ön koşul**
- Örnek uygulamada `AddToolApprovalPolicy("refund_order", ctx => ToolApprovalPolicyDecision.Required)` kayıtlı bir agent VAR (`samples/Tracon.Api` içinde bu tool için ayrı bir sabit politika örneği kurulmalı; yoksa 👤 insan gerekir — kodda geçici olarak eklenip test edilir, kalıcı örnek şart değil).
- MT-SEC-100'ün koşulsuz kuralı (her çağrıyı otomatik onaylayan) kayıtlı.

**Adımlar**
1. `refund_order`'ı çağırt.

**Beklenen sonuç**
- Çağrı onay İSTER — veri kuralı otomatik onaylardı ama kod politikası `Required` döndüğü için kod kazanır (`IsAutoApprovedAsync`, politika `Required`/`NotRequired` dalı veri kurallarından ÖNCE değerlendirilir).

**Not:** 👤 insan gerekir — `samples/Tracon.Api`'ye geçici bir `AddToolApprovalPolicy` çağrısı eklemeden koşulamaz.

---

### MT-SEC-106 — Aynı kapsam ve aynı koşulla ikinci kural `409` alır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | K-454 |

**Ön koşul**
- MT-SEC-101'in kuralı kayıtlı.

**Adımlar**
1. AYNI gövdeyle ikinci kez yaz.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/approvals/rules" -H "$APB" -H "content-type: application/json" -d '{
  "toolName": "refund_order",
  "argumentConditions": [{"path": "amount", "operator": "LessThanOrEqual", "value": 100}]
}'
```

**Beklenen sonuç**
- `HTTP: 409`, `title: "Rule already exists"` — `conditions_hash` benzersizlik anahtarına girdiği için (K-455) ikinci kural yeni bir satır AÇMAZ.

---

### MT-SEC-107 — Sayısal olmayan bir değerle `GreaterThan` yazmak `400` alır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | — |

**Adımlar**
1. `tier` (metin) alanına `GreaterThan` operatörüyle bir kural yazmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/approvals/rules" -H "$APB" -H "content-type: application/json" -d '{
  "toolName": "refund_order",
  "argumentConditions": [{"path": "tier", "operator": "GreaterThan", "value": "gold"}]
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Invalid condition value"`, `detail: "Operator 'GreaterThan' expects a number."`

---

### MT-SEC-108 — Arayüzden koşullu kural eklenip geri okunur; serbest ifade kutusu YOKTUR

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 63 |
| **İlgili karar** | — |

**Adımlar**
1. `/mcp` ekranına git, "Add rule" ile aç.
2. `toolName: refund_order`, koşul satırı ekle (`amount`, `LessThanOrEqual`, `100`), kaydet.
3. Kural listede görünsün mü kontrol et. Operatör alanının bir AÇILIR LİSTE (dropdown) olduğunu, serbest metin bir "ifade" kutusu OLMADIĞINI doğrula.

**Beklenen sonuç**
- Yeni kural tabloda `amount ≤ 100` rozetiyle görünür; operatör seçimi yalnız
  sekiz sabit değerden biri olabilir, hiçbir alanda ifade/formül yazılamaz (K2).
  Otomatikleştirilmiş karşılığı: `UiTests.Approval_rule_with_condition_is_created_and_shown`.

---

### MT-SEC-109 — Bağlama önek dışındaki bir yapılandırma anahtarı adıyla reddedilir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-467 |

**Adımlar**
1. `ConnectionStrings:Default` gibi önek dışı bir ad ile bağlama yazmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/tenants/acme/providers/openai" -H "$APB" -H "content-type: application/json" -d '{
  "apiKeyConfigurationName": "ConnectionStrings:Default"
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Invalid request"`; gövde "Tracon:ProviderKeys:" önekini anar.
  Kayıt hiç oluşmaz — `GET /api/tenants/acme/providers` boş liste döner.

---

### MT-SEC-110 — Anahtar değeri `user-secrets`'e yazılınca bağlama `resolved: true` olur; hiçbir yanıt DEĞERİ TAŞIMAZ

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-059, K-467 |

**Adımlar**
1. `dotnet user-secrets set "Tracon:ProviderKeys:Acme:OpenAI" "sk-..." --project samples/Tracon.Api`
2. Bağlamayı kaydet, sonra listele.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/tenants/acme/providers/openai" -H "$APB" -H "content-type: application/json" -d '{
  "apiKeyConfigurationName": "Tracon:ProviderKeys:Acme:OpenAI"
}'

curl -s "$APU/api/tenants/acme/providers" -H "$APB"
```

**Beklenen sonuç**
- İlk çağrı `resolved: true` döner (adım 1'de anahtar zaten yazılıydıysa) veya
  önce `resolved: false` görülür, sunucu yeniden başlatılmadan `user-secrets`
  yazıldıktan SONRAKİ istekte `true`'ya döner (önbellek yok, K1). İkinci çağrının
  gövdesinde `sk-` ile başlayan hiçbir metin, hiçbir alan adı `apiKey`/`value`
  YOKTUR — yalnız `providerName`, `apiKeyConfigurationName` (ADIN kendisi),
  `endpoint`, `resolved`, `updatedAt`.

---

### MT-SEC-111 — Ad var, değer yok: çalıştırma global anahtara SESSİZCE düşmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-467 |

**Adımlar**
1. `dotnet user-secrets remove "Tracon:ProviderKeys:Acme:OpenAI" --project samples/Tracon.Api` (değeri sil, bağlama kaydı DURSUN).
2. `acme` kiracısı olarak bir `run` başlat.

**Beklenen sonuç**
- Çalıştırma **global anahtarla devam ETMEZ**; `TraconException` kaynaklı
  anlaşılır bir hata alınır ve mesaj `Tracon:ProviderKeys:Acme:OpenAI`
  adını ve `dotnet user-secrets set` ipucunu içerir. Otomatikleştirilmiş
  karşılığı: `ModelProviderRegistryTenantCredentialTests.A_binding_that_exists_but_resolves_to_no_value_does_not_fall_back_silently`.

---

### MT-SEC-112 — İki kiracı, iki farklı anahtar: her çağrı kendi anahtarını kullanır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-467, K-468 |

**Adımlar**
1. `acme` ve `globex` kiracılarına FARKLI `user-secrets` anahtarlarıyla birer
   OpenAI bağlaması yaz.
2. Her iki kiracı olarak sırayla bir `run` başlat (gerçek bir sahte/gözlemlenebilir
   uca karşı, örn. istek başlıklarını loglayan bir `openai-compatible` sunucu).

**Beklenen sonuç**
- İki çağrı da FARKLI `Authorization` başlığıyla gider; hiçbir çağrı diğer
  kiracının anahtarını kullanmaz. Otomatikleştirilmiş karşılığı: sözleşme testi
  `TenantProviderBindingStoreContract` (bellek içi + üç SQL sağlayıcısı).

---

### MT-SEC-113 — Bağlama yazımından sonra veritabanında `secret` YOKTUR

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-059 |

**Adımlar**
1. MT-SEC-110/112'nin ardından, PostgreSQL/SQL Server/SQLite'a doğrudan bağlanıp
   `tenant_provider_bindings` tablosunu oku.

**Girilecek veri**
```bash
psql "$PG_CONN" -c "SELECT tenant_id, provider_name, api_key_configuration_name, endpoint FROM tracon.tenant_provider_bindings;"
```

**Beklenen sonuç**
- `api_key_configuration_name` sütunu yalnız ADI taşır (`Tracon:ProviderKeys:...`);
  hiçbir satırda `sk-` ile başlayan bir metin veya gerçek anahtar değeri YOKTUR.

---

### MT-SEC-114 — Bağlama yazımı ve silinmesi denetim izine düşer

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | — |

**Adımlar**
1. Bir bağlama kaydet, sonra sil.
2. Denetim iznini aç.

**Girilecek veri**
```bash
curl -s "$APU/api/audit/tenant_provider:default:openai" -H "$APB"
```

**Beklenen sonuç**
- İki kayıt görünür: `tenant_provider.save` ve `tenant_provider.delete`.
  `save` kaydının `after` alanında `configKeyName` görünür ama gerçek anahtar
  DEĞERİ hiçbir zaman görünmez.

---

### MT-SEC-115 — Egress politikası tanımlanmamış bir kiracıda davranış DEĞİŞMEZ

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-467 |

**Adımlar**
1. Hiç egress politikası kaydetmeden `GET /api/tenants/acme/egress` çağır.
2. `acme` kiracısı olarak herhangi bir sağlayıcıya `run` başlat.

**Beklenen sonuç**
- `allowedProviders: null` (kısıtsız, K1). `run` bugünkü gibi çalışır, hiçbir
  sağlayıcı reddedilmez.

---

### MT-SEC-116 — İzinsiz sağlayıcıya işaret eden agent tanımı DERLEME ANINDA reddedilir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-467 |

**Adımlar**
1. `acme` kiracısı için egress politikasını yalnız `["anthropic"]` olarak kaydet.
2. `openai` sağlayıcısına bağlı bir agent tanımını `POST /api/agents/validate`
   (veya doğrudan bir `run`) ile derlemeyi dene.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/tenants/acme/egress" -H "$APB" -H "content-type: application/json" \
  -d '{"allowedProviders": ["anthropic"]}'
```

**Beklenen sonuç**
- `TraconCompilationException` kaynaklı bir hata; mesaj `openai` adını ve
  izinli listeyi (`anthropic`) anar. Gerçek bir model çağrısı YAPILMAZ — hata
  ağa hiç çıkmadan, derleme adımında oluşur. Otomatikleştirilmiş karşılığı:
  `AgentDefinitionCompilerEgressTests.Compiling_an_agent_bound_to_a_forbidden_provider_throws_a_compilation_exception`.

---

### MT-SEC-117 — İzinsiz sağlayıcı için bağlama yazımı da `400` alır (iki yüzey tutarlı)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-467 |

**Adımlar**
1. MT-SEC-116'nın egress politikası dururken, `openai` için bir bağlama yazmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/tenants/acme/providers/openai" -H "$APB" -H "content-type: application/json" -d '{
  "apiKeyConfigurationName": "Tracon:ProviderKeys:Acme:OpenAI"
}'
```

**Beklenen sonuç**
- `HTTP: 400`; egress kontrolü kayıt zamanında da çalışır — bağlama uçuyla
  agent derleme yolu aynı kısıtı uygular.

---

### MT-SEC-119 — Egress politikası silinince kiracı tekrar kısıtsız olur

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | — |

**Adımlar**
1. Bir egress politikası kaydet.
2. Sil, sonra tekrar oku.
3. Aynı politikayı ikinci kez silmeyi dene.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/tenants/acme/egress" -H "$APB" -H "content-type: application/json" -d '{"allowedProviders": ["openai"]}'
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/tenants/acme/egress" -H "$APB"
curl -s "$APU/api/tenants/acme/egress" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/tenants/acme/egress" -H "$APB"
```

**Beklenen sonuç**
- İlk silme `HTTP: 204`; ardından `GET` `allowedProviders: null` döner (kısıtsız,
  politika hiç kaydedilmemiş gibi). İkinci silme `HTTP: 404`,
  `title: "Policy not found"`. Boş bir dizi (`allowedProviders: []`, hiçbir
  sağlayıcıya izin yok) ile "politika yok" (kısıtsız) durumu AYNI ŞEY DEĞİLDİR —
  bu yüzden ayrı bir `DELETE` ucu vardır.

---

### MT-SEC-118 — Arayüzden bağlama ekranında değer girme alanı YOKTUR

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 65 |
| **İlgili karar** | K-059 |

**Adımlar**
1. Kiracı ekranını aç, sağlayıcı bağlama panelini bul.
2. Yeni bağlama formunu incele: hangi alanlar var?

**Beklenen sonuç**
- Form yalnız sağlayıcı adı, yapılandırma anahtarı ADI ve isteğe bağlı uç adresi
  ister. Hiçbir alan doğrudan bir `secret`/API anahtarı DEĞERİ istemez; çözümleme
  durumu (`resolved`) salt-okunur bir rozet olarak gösterilir.

---

### MT-SEC-120 — Yetkilendirme kancası kayıtlı değilken davranış değişmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 69 |
| **İlgili karar** | K1 |

**Ön koşul**
`Program.cs`'de özel bir `IToolAuthorizationHandler` KAYITLI DEĞİL —
`AddTracon()`'in varsayılan `AllowAllToolAuthorizationHandler`'ı geçerli.

**Adımlar**
1. `get_order_status` tool'unu çağıran bir `run` başlat (gerçek bir model
   anahtarı gerekir).

**Beklenen sonuç**
- Çağrı bugünkü gibi çalışır; ek bir gecikme veya davranış farkı yoktur. Ret
  metni, yetki reddi olayı yoktur.

---

### MT-SEC-121 — Reddeden bir kanca `run`'ı düşürmez, model devam eder

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 69 |
| **İlgili karar** | — |

**Ön koşul**
Her çağrıyı reddeden bir `IToolAuthorizationHandler`
(`ToolAuthorizationResult.Deny(...)`) `Program.cs`'de kayıtlı (geçici test
kaydı; kalıcı değil).

**Adımlar**
1. `cancel_order` tool'unu çağıran bir `run` başlat.

**Beklenen sonuç**
- Model ret metnini bir tool sonucu olarak alır ve turuna devam eder; `run`
  `Completed` durumunda biter, `Failed` DEĞİL. `GET /api/runs/{id}/tools`
  kaydında `authorizationDenied: true`, `error: null`.

---

### MT-SEC-122 — Yetki reddi olay akışında `ToolFailed`'den ayırt edilebilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 69 |
| **İlgili karar** | — |

**Ön koşul** MT-SEC-121 ile aynı.

**Adımlar**
1. MT-SEC-121'deki `run`'ın kimliğiyle `GET /api/runs/{id}/tools` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/runs/$RUN_ID/tools" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Kayıtta `authorizationDenied: true` ve `timedOut: false` alanları vardır;
  `error` alanı BOŞTUR (ret bir hata değildir). Aynı kayıt genel bir
  `ToolFailed`/hata görünümünden ayırt edilebilir.

---

### MT-SEC-123 — Timeout'lu bir tool ~1 saniyede kesilir, `run` devam eder

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 69 |
| **İlgili karar** | — |

**Ön koşul**
`samples/Tracon.Api`'nin `get_slow_report` tool'u (1 sn timeout, 5 sn
uyuyan gövde) kayıtlı — bkz. `samples/Tracon.Api/OrderTools.cs`.

**Adımlar**
1. `get_slow_report` tool'unu çağıran bir `run` başlat, süreyi ölç.

**Girilecek veri**
```bash
time curl -s -X POST "$APU/api/agents/order-support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Fetch report R-1 with get_slow_report."}'
```

**Beklenen sonuç**
- İstek ~1 saniyede döner (gerçek gövdenin 5 saniyesini BEKLEMEZ). Model bir
  tool hatası görür ve `run` `Completed` durumunda biter. `GET
  /api/runs/{id}/tools` kaydında `timedOut: true`, `error` dolu.

---

### MT-SEC-124 — Aynı tool arka arkaya 6 kez zaman aşımına uğrarsa devre kesici AÇILMAZ

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 69 |
| **İlgili karar** | — |

**Ön koşul** MT-SEC-123 ile aynı tool.

**Adımlar**
1. `get_slow_report`'u art arda 6 kez çağıran 6 ayrı `run` başlat.
2. Her `run`'dan sonra `GET /api/models/health`'i kontrol et.

**Beklenen sonuç**
- Sağlayıcının devresi (`circuit breaker`) AÇILMAZ — tool zaman aşımı
  `ModelProviderCircuitBreaker`'a hiç ulaşmaz, yalnız model çağrısı hataları
  ulaşır. 7. `run` de aynı hızda başlar.

---

### MT-SEC-125 — Onay bekleme süresi timeout'a düşmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 69 |
| **İlgili karar** | K-368 |

**Ön koşul**
`cancel_order` (`RequiresApproval = true`, artık `TimeoutSeconds` de kısa
ayarlanmış bir kopyası — bkz. faz dokümanı) çağıran bir `run`.

**Adımlar**
1. `cancel_order`'ı çağıran bir `run` başlat; model onay isteği üretsin.
2. En az 2 dakika bekle.
3. `POST /api/approvals/{id}/decide` ile onayla.

**Beklenen sonuç**
- 2 dakikalık bekleme timeout'a düşmez (`ToolTimeout` üretmez) — onay bekleme
  süresi hiçbir zaman `TimeoutAIFunction`'ın içinden geçmez (K-368: onay kararı
  YENİ bir `run` açar). Onaydan sonra ikinci `run` normal hızda tamamlanır.

---

### MT-SEC-126 — Tool kataloğunda etki rozeti ve izin adı görünür

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 69 |
| **İlgili karar** | — |

**Adımlar**
1. Arayüzde Tools ekranını aç.
2. `cancel_order` satırını bul.

**Beklenen sonuç**
- `cancel_order` satırında 🔴 kırmızı "geri alınamaz" rozeti VE
  `orders.cancel` izin rozeti görünür. `get_order_status`/`list_recent_orders`
  satırlarında etki rozeti YOKTUR (`Read` nötrdür, rozet basılmaz).

---

### MT-SEC-127 — Token okumayan tool: `run` 1 saniyede devam eder, gövde arkada biter

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 69 |
| **İlgili karar** | — |

**Ön koşul** MT-SEC-123 ile aynı (`get_slow_report`, `CancellationToken` PARAMETRESİ ALMAZ).

**Adımlar**
1. MT-SEC-123'ü koş; sunucu loglarını izle.

**Beklenen sonuç**
- `run` ~1 saniyede devam eder (MT-SEC-123). ~4 saniye sonra sunucu loglarında
  `Tool 'get_slow_report' finished after its 1s timeout had already been
  reported to the model.` uyarısı görünür — gövde arka planda gerçekten
  bitmiştir, yalnız BEKLEME kesilmiştir. **Belgelenmiş sınır**: `run`'ın
  kendisi bu ikinci tamamlanmayı bir olay olarak yazmaz.

---

### MT-SEC-128 — Kiracı sağlayıcı `Endpoint`'i özel ağa işaret ediyor: reddedilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 77 |
| **İlgili karar** | K-530 |

**Ön koşul** Varsayılan ayarlar (`Tracon:Egress:AllowPrivateNetworkTargets`
tanımlı DEĞİL). Örnek uygulama ayakta.

**Adımlar**
```bash
curl -s -X PUT "$APU/api/tenants/acme/providers/anthropic" -H "$APB" \
  -H 'Content-Type: application/json' \
  -d '{"apiKeyConfigurationName":"Tracon:ProviderKeys:acme","endpoint":"http://10.0.0.5/"}' \
  | jq -r '.detail'
```

**Beklenen sonuç**
- `400`. `detail` şu metni içerir:
  `The target resolves to a private network address (10.0.0.5); set
  'Tracon:Egress:AllowPrivateNetworkTargets' to true to allow it.`
- Mesaj ayarın **adını** yazar — iç ağda gerçekten sağlayıcı proxy'si olan bir
  operatörün yolu kapanmaz.
- `endpoint` alanı hiç verilmezse istek `200` döner: kısıt yalnız override'a uygulanır.

---

### MT-SEC-129 — Webhook `secretConfigurationKey` önek dışında: reddedilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 77 |
| **İlgili karar** | K-533 |

**Ön koşul** Örnek uygulama ayakta.

**Adımlar**
```bash
curl -s -X PUT "$APU/api/webhooks/orders" -H "$APB" \
  -H 'Content-Type: application/json' \
  -d '{"url":"https://hooks.example.com/o","events":["run.completed"],
       "secretConfigurationKey":"ConnectionStrings:Default"}' | jq -r '.detail'
```

**Beklenen sonuç**
- `400`. `detail`: `'ConnectionStrings:Default' is outside the allowed prefix.
  'secretConfigurationKey' may only reference a configuration key under
  'Tracon:WebhookSecrets:'.`
- Aynı istek `"secretConfigurationKey":"Tracon:WebhookSecrets:orders"` ile
  `200` döner.
- `secretConfigurationKey` hiç verilmezse `200` — alan opsiyoneldir.

---

### MT-SEC-130 — Webhook ek başlığı imza başlığının adını taşıyor: gönderilmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 77 |
| **İlgili karar** | K-535 |

**Ön koşul** Alıcı tarafta başlıkları yazdıran bir dinleyici (örn. `nc -l 9099`
veya bir webhook echo servisi) ve ona işaret eden etkin bir abonelik.

**Adımlar**
1. Aboneliğe `headers` içinde `{"X-Tracon-Signature":"sahte"}` ekle.
2. Bir `run` tamamlanmasını tetikle ve alıcının aldığı ham isteği incele.
3. Sunucu loglarını oku.

**Beklenen sonuç**
- Alıcıya **tek bir** `X-Tracon-Signature` başlığı ulaşır ve değeri
  Tracon'in hesapladığı imzadır — `sahte` değildir.
- Sunucu logunda uyarı: `Webhook subscription 'orders' carries the reserved
  header 'X-Tracon-Signature'; it was not sent.`
- Sıradan adlı bir ek başlık (örn. `X-Tenant`) normal şekilde iletilir.

---

### MT-SEC-131 — Koruma açıkken oturum durumu ve `run` girdisi veritabanında şifreli durur

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 82 |
| **İlgili karar** | K-561, K-562 |

**Ön koşul** `samples/Tracon.Api`, bir SQL sağlayıcısı (`UsePostgreSql`/
`UseSqlServer`/`UseSqlite`) yapılandırılmış, `Tracon:ContentProtection:Enabled`
`true` ve `dotnet user-secrets set "Tracon:ContentProtection:RawKeys:sample"
"$(openssl rand -base64 32)"` ile 32 baytlık bir anahtar tanımlanmış
(`appsettings.json`'daki `ContentProtection` bölümünün `Keys:sample` girdisi bu
anahtarın **adını** gösterir, değerini değil).

**Adımlar**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H 'Content-Type: application/json' \
  -d '{"sessionId":"cp-demo","message":"secret marker XYZZY-CP-DEMO"}' \
  | grep -o '"runId":"[^"]*"'
```
Sonra veritabanını doğrudan sorgula (PostgreSQL örneği):
```sql
SELECT messages FROM tracon.run_inputs WHERE run_id = '<runId>';
SELECT state FROM tracon.sessions WHERE id = 'cp-demo';
```

**Beklenen sonuç**
- İki sütun da `{"$apEnc":1,"kid":"sample","n":"...","c":"..."}` biçiminde bir
  zarftır; `XYZZY-CP-DEMO` metni sütunda **hiç** görünmez.
- `curl -s "$APU/api/runs/<runId>/input" -H "$APB"` isteğin **düz metnini**
  döner — çözme şeffaftır, API hiçbir zaman zarfı göstermez.
- Aynı `sessionId` ile transcript arayüzde/`GET /api/sessions/cp-demo`
  üzerinden okunduğunda mesaj yine düz metindir.

---

### MT-SEC-132 — Koruma açılmadan önce yazılmış satır, açıldıktan sonra da okunabilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 82 |
| **İlgili karar** | K-562 |

**Ön koşul** MT-SEC-131'in kurulumu, ama `ContentProtection:Enabled` **henüz
kapalı**.

**Adımlar**
1. `Enabled: false` iken bir `run` yap (`sessionId: "legacy-demo"`).
2. Uygulamayı durdur, `appsettings.json`'da (veya ortam değişkeniyle)
   `Enabled: true` yap, yeniden başlat.
3. `GET $APU/api/sessions/legacy-demo` ile eski oturumu oku.

**Beklenen sonuç**
- Eski satır **düz metin** olarak veritabanında kalır (adım 1'den sonra
  kontrol edilirse `$apEnc` yoktur).
- Adım 3'teki okuma **başarılıdır** ve içerik birebir aynıdır — koruma
  yalnızca yeni yazmaları etkiler, var olan satırları bozmaz.

---

### MT-SEC-133 — Bilinmeyen `kid` sessiz değil, adını söyleyen net bir hata verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 82 |
| **İlgili karar** | K-563 |

**Ön koşul** MT-SEC-131'in kurulumu; **iki** anahtar tanımlı (`sample` eski,
`sample2` yeni) ve `ActiveKeyId` `sample2`'ye çevrilmiş. En az bir satır eski
`sample` kid'i ile şifrelenmiş olmalı.

**Adımlar**
1. `Tracon:ContentProtection:Keys:sample` girdisini `appsettings.json`'dan
   kaldır (`ActiveKeyId`'yi DEĞİL — o zaten `sample2`).
2. Uygulamayı yeniden başlat. Başlangıç **başarılıdır**: doğrulayıcı yalnız
   `ActiveKeyId`'nin (`sample2`) `Keys`'te karşılığı olduğunu ister,
   sözlükteki HER kid'i değil.
3. `sample` ile yazılmış eski satırı okuyan bir isteği çağır (ör.
   `GET .../api/sessions/{id}`).

**Beklenen sonuç**
- Uygulama **açılır** — eksik olan `sample`, `ActiveKeyId` değildir.
- Adım 3'teki istek bir sunucu hatası döner ve mesaj **`sample`'ı adıyla**
  söyler — örn. `Content protection key 'sample' is not configured. Add it to
  TraconContentProtectionOptions.Keys, ...`
- Hata sessiz bir `null`/boş yanıt DEĞİLDİR; okunamayan veri fark edilir hâlde
  kalır.
- `sample2` ile yazılmış YENİ bir satır aynı anda sorunsuz okunur.
- `Keys:sample`'ı geri eklemek eski satırı yeniden okunur hâle getirir.

---

### MT-SEC-134 — Koruma kapalıyken davranış Faz 82 öncesiyle birebir aynıdır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 82 |
| **İlgili karar** | K-559 |

**Ön koşul** `samples/Tracon.Api`, bir SQL sağlayıcısı yapılandırılmış,
`AddContentProtection()` kayıtlı ama `appsettings.json`'da `ContentProtection:Enabled`
`false` (varsayılan).

**Adımlar**
1. Bir `run` yap, `run_inputs`/`sessions` satırlarını `psql`/`sqlcmd`/`sqlite3`
   ile doğrudan oku.
2. `Tracon:ContentProtection` bölümünü `appsettings.json`'dan tamamen
   kaldırıp uygulamayı yeniden başlat, aynı isteği tekrar gönder.

**Beklenen sonuç**
- İki adımda da sütunlar **düz metin**dir; `$apEnc` hiçbir satırda görünmez.
- `AddContentProtection()` çağrısının varlığı/yokluğu davranışı değiştirmez —
  kapıyı açan `Enabled` bayrağıdır, çağrının kendisi değil.

---

### MT-SEC-135 — Koruma açıkken agent dosya araması hâlâ doğru sonuç verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 82 |
| **İlgili karar** | K-562 |

**Ön koşul** MT-SEC-131'in kurulumu; dosya belleği (`FileMemoryProvider`) açık
bir agent.

**Adımlar**
1. Agent'a bir dosya yazdır: içeriğinde tek bir satırda geçen ayırt edici bir
   dize olsun (örn. `"needle-XYZZY"`).
2. Veritabanında `agent_files.content` sütununu doğrudan oku.
3. Agent'a aynı dizeyi arattır (dosya arama tool'u/uç noktası üzerinden).

**Beklenen sonuç**
- Adım 2'de sütun bir zarftır (`$apEnc`); `needle-XYZZY` sütunda görünmez.
- Adım 3'teki arama **doğru dosyayı ve doğru satırı** bulur — sunucu tarafı ön
  süzgeç (yalnız PostgreSQL'de var) devre dışı kalsa da nihai eşleşme
  istemcide çalışır ve sonuç değişmez.

---

# 2 — Çalıştırma ve Oturum Yetkilendirmesi (Faz 139, F-185, `IRunAuthorizationHandler`)

Tracon sahipliği kiracı düzeyinde çizer; kiracı **içindeki** kullanıcıyı
hiçbir yerde ayırmaz. Bu bölüm, tüketicinin kendi `IRunAuthorizationHandler`
kaydının run başlatmayı ve session erişimini (Read/List/Delete/Branch)
doğru reddettiğini/izin verdiğini kanıtlar. Otomatikleştirilmiş karşılığın
tamamı `RunAuthorizationEndpointTests` (18 test, `TestServer` üzerinden gerçek
HTTP) ve `RunAuthorizationResultTests`'tedir (Core.UnitTests) — koşuldu, hepsi
yeşil (bkz. faz dokümanının DoD bölümü).

### MT-SEC-140 — Handler kayıtlı değilken hiçbir şey değişmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Ön koşul**
- `IRunAuthorizationHandler` kaydı yapılmadı (varsayılan kurulum).

**Adımlar**
1. `/api/diagnostics` çağır, altıncı genişleme noktasını kontrol et.
2. `support` agent'ına normal bir run at.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -c \
  "import json,sys; d=json.load(sys.stdin); p=[x for x in d['extensionPoints'] if x['contract']=='IRunAuthorizationHandler'][0]; print(p)"
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" \
     -H "$APB" -H 'content-type: application/json' -d '{"message":"What is your return policy?"}'
```

**Beklenen sonuç**
- `IRunAuthorizationHandler` → `AllowAllRunAuthorizationHandler`,
  `isBuiltInDefault: true`.
- Run `HTTP: 200`, akış normal tamamlanır — hiçbir davranış değişmez (K1).
- **Koşuldu (2026-09-03):** diagnostics 6 genişleme noktası döndü, altıncısı
  tam olarak beklenen şekilde; run `200` ile gerçek bir OpenAI yanıtı üretti.
  Otomatikleştirilmiş karşılığı: `Nothing_changes_when_no_handler_is_registered`,
  `Bare_setup_reports_the_run_authorization_handler_as_built_in_default`.

---

### MT-SEC-141 — Handler yalnız beklenen kullanıcıya izin verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulamaya GEÇİCİ olarak `UserId == "a"` dışını reddeden bir
  `IRunAuthorizationHandler` kaydı eklenir (`Program.cs`, `AddTracon()`
  zincirine `services.Replace(...)`); `IRunAttributionContext` sabit `"a"` veya
  `"b"` döner.

**Adımlar**
1. Kimliği `"a"` iken run at.
2. Kimliği `"b"` iken aynı agent'a run at.

**Beklenen sonuç**
- Adım 1: `HTTP: 200`, run çalışır.
- Adım 2: `HTTP: 403`, `title: "Run not authorized"`, `runs` satırı **açılmaz**
  (`GET /api/runs` boş kalır).
- Otomatikleştirilmiş karşılığı: `Handler_allows_the_expected_user`,
  `Handler_denies_a_different_user_and_no_run_row_opens`.

---

### MT-SEC-142 — Reddedilen bir run başkasının session'ını da kapsar

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SEC-141'in kurulumu; `"a"` kimliğiyle `sessionId: "session-of-a"` taşıyan
  bir run zaten var.

**Adımlar**
1. Kimliği `"b"` iken AYNI `sessionId` ile run at.

**Beklenen sonuç**
- `HTTP: 403` — handler'a giden istek `sessionId: "session-of-a"`,
  `userId: "b"` taşır; karar SessionId'yi de görebilir.
- Otomatikleştirilmiş karşılığı: `Handler_denies_a_run_against_another_users_session`.

---

### MT-SEC-143 — `throw` eden handler reddeder (fail-closed)

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Ön koşul**
- Handler'ın `AuthorizeRunAsync`'i her çağrıda `InvalidOperationException` fırlatır.

**Adımlar**
1. Herhangi bir agent'a run at.

**Beklenen sonuç**
- `HTTP: 403` (`500` DEĞİL) — bir gate hatada açık kalırsa gate değildir.
  `runs` satırı açılmaz.
- Otomatikleştirilmiş karşılığı: `Throwing_handler_denies_the_run_fail_closed`.

---

### MT-SEC-144 — Reddedilen run kotayı tüketmez (sıra: atıf → yetki → kota)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Ön koşul**
- `MaxRuns=1` günlük kota tanımlı; her çağrıyı reddeden bir handler kayıtlı.

**Adımlar**
1. Reddedilecek bir run dene.
2. `/api/quotas/usage` (veya `QuotaEnforcer.CheckAsync`) ile sayaç durumunu
   kontrol et.

**Beklenen sonuç**
- Adım 1: `HTTP: 403`.
- Adım 2: sayaç **hâlâ boş** — reddedilen çağrı kotayı tüketmemiştir; aynı
  limitle yapılan bir sonraki (izinli) çağrı hâlâ geçer.
- Otomatikleştirilmiş karşılığı: `Denied_run_does_not_consume_the_quota`.

---

### MT-SEC-145 — Kuyruğa alınan (`Prefer: respond-async`) run da kapsanır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | K-103 sınıfı |

**Ön koşul**
- Her çağrıyı reddeden bir handler kayıtlı.

**Adımlar**
1. `Prefer: respond-async` başlığıyla run dene.

**Beklenen sonuç**
- `HTTP: 403` — `202 Accepted` DEĞİL. İş kuyruğuna hiç yazılmaz
  (`GET /api/jobs` bu çağrı için boş kalır); kapı worker'a düşmeden,
  HTTP katmanında çalışır.
- Otomatikleştirilmiş karşılığı: `Queued_run_is_denied_before_it_is_queued`.

---

### MT-SEC-146 — Session erişimi: `List` → `403`, `Read`/`Delete`/`Branch` → `404`

Negatif senaryo. `List` bir kaynak değil bir işlemdir; diğer üçü var olan bir
kaynağa erişimdir ve reddi `404`'e (var olmayanla AYNI gövde) düşer — `403`
kaynağın var olduğunu sızdırırdı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Ön koşul**
- Gerçek bir session var (`"a"` ile açılmış). Handler sırayla yalnız
  `List`/`Read`/`Delete`/`Branch` erişimini reddedecek şekilde değiştirilir.

**Adımlar**
1. `GET /api/sessions` çağır (List reddedilirken).
2. `GET /api/sessions/{id}` çağır (Read reddedilirken).
3. `DELETE /api/sessions/{id}` çağır (Delete reddedilirken).
4. `POST /api/sessions/{id}/branch` çağır (Branch reddedilirken).

**Beklenen sonuç**
- Adım 1: `HTTP: 403`.
- Adım 2-4: `HTTP: 404`, gövde (`title`/`detail`) hiç var olmamış bir
  `sessionId` ile YAPILAN aynı çağrının gövdesiyle **birebir aynı** — reddedilen
  ile gerçekten yok olan ayırt edilemez.
- Adım 2 için canlı temel çizgi (2026-09-03, handler'sız): var olmayan bir
  session `GET`'i zaten `404` + `{"title":"Session not found","detail":"There
  is no session with id '...'."}` döndürüyor; reddedilen senaryo bununla
  aynı şekli üretir.
- Otomatikleştirilmiş karşılığı: `Denied_session_list_returns_403`,
  `Denied_session_read_returns_404_not_403`, `Denied_session_delete_returns_404`,
  `Denied_session_branch_returns_404`.

---

### MT-SEC-147 — Dört run başlatan yüzeyin dördü de kapsanır (bypass yok)

🚨 Bu case fazın en kritik iddiasını doğrular: run başlatan uçlar ortak bir
filtre PAYLAŞMAZ, her biri kapıyı kendi gövdesinde çağırır. Bir uç
unutulmuşsa bu case onu yakalar.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Ön koşul**
- Her çağrıyı reddeden bir handler kayıtlı; bir workflow (`chain`) ve bir
  inbound trigger (imzalı) tanımlı.

**Adımlar**
1. `POST /api/agents/{name}/run` dene.
2. `POST /api/workflows/{name}/run` dene.
3. İmzalı bir inbound trigger isteği gönder.
4. `POST /v1/responses` (OpenAI uyumlu yüzey) dene.

**Beklenen sonuç**
- Dördü de `HTTP: 403` döner; hiçbiri handler'ı atlayıp çalışmaz.
- Otomatikleştirilmiş karşılığı: `Handler_denies_a_different_user_and_no_run_row_opens`
  (yüzey 1), `Workflow_run_is_covered` (yüzey 2), `Inbound_trigger_is_covered`
  (yüzey 3), `OpenAI_compatible_endpoint_is_covered` (yüzey 4).

---

### MT-SEC-148 — Handler'a giden `TenantId` ambient kiracıdır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Adımlar**
1. Handler'ın gördüğü `RunAuthorizationRequest.TenantId`'yi logla/kaydet.
2. Normal (kiracı başlığı olmadan) bir run at.

**Beklenen sonuç**
- `TenantId` `"default"` (`SingleTenantContext`'in varsayılanı) — kapı
  kendi kiracı değeri UYDURMAZ, her zaman `ITenantContext`'ten okur.
- Otomatikleştirilmiş karşılığı: `Handler_receives_the_ambient_tenant`.

---

### MT-SEC-149 — İzin verilen çağıranın kimliği tool gövdesine ulaşır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 139 (A8) |
| **İlgili karar** | — |

**Ön koşul**
- `IRunAttributionContext` `"ada"` döner; çağıranın kimliğini döndüren bir
  test tool'u agent'a bağlı.

**Adımlar**
1. Tool'u tetikleyecek bir run at.
2. Tool'un ürettiği sonucu (veya modelin yankıladığı metni) incele.

**Beklenen sonuç**
- Tool `TraconRunContext.Current?.UserId` üzerinden `"ada"`yı görür — yeni
  bir kavram değil, `IRunAttributionContext`'in run kaydı için zaten okuduğu
  değerin tool'a açılan aynı kopyasıdır.
- Otomatikleştirilmiş karşılığı: `Allowed_user_id_reaches_the_tool_via_scope`.

---

### MT-SEC-150 — Eşzamanlı çağrılar aynı handler örneğinde birbirini bozmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 139 |
| **İlgili karar** | — |

**Adımlar**
1. Aynı handler örneğine karşı çok sayıda run'ı EŞZAMANLI gönder.

**Beklenen sonuç**
- Hepsi tutarlı şekilde karar alır (istisna/çakışma yok); handler her çağrıyı
  ayrı ayrı görür.
- Otomatikleştirilmiş karşılığı: `Concurrent_runs_are_each_authorized_independently`.

---

### MT-SEC-151 — Handler kayıtlı değilken 21 kaynak ucunun hiçbiri değişmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | — |

**Ön koşul**
- `IRunAuthorizationHandler` kaydı yapılmadı (varsayılan `AllowAll`).
- En az bir tamamlanmış `run`, bir ek ve bir onay kaydı var.

**Adımlar**
1. Run okuma uçlarının hepsini çağır: `{id}`, `/tree`, `/events`, `/input`,
   `/trace`, `/tools`, `/feedback`, `/compare/{b}`.
2. Liste uçlarını çağır: `/api/runs`, `/api/attachments`, `/api/approvals/pending`.
3. Yazma uçlarını çağır: `/cancel`, `/feedback`, ek yükle/indir/sil, onay oku/karar ver.

**Girilecek veri**
```bash
for p in "" "/tree" "/input" "/tools" "/feedback"; do
  printf '%-10s -> %s\n' "$p" \
    "$(curl -s -o /dev/null -w '%{http_code}' "$APU/api/runs/$RUNID$p" -H "$APB")"
done
```

**Beklenen sonuç**
- Her yanıt Faz 147 öncesiyle **birebir aynı**. Okuma uçları `200`, liste uçları
  `200`, tamamlanmış bir `run`'ın `cancel`'ı `409`.
- Otomatikleştirilmiş karşılığı:
  `Every_resource_endpoint_is_unchanged_when_no_handler_is_registered`.

---

### MT-SEC-152 — Reddedilen tekil `run` okuması, var olmayan `run` ile BİREBİR aynıdır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | K-684 |

**Ön koşul**
- Handler B kullanıcısının `run`'ını A kullanıcısına reddediyor.

**Adımlar**
1. A olarak B'nin `run`'ını oku.
2. A olarak var olmayan bir `run` id'si oku.
3. İki gövdeyi id'ler eşitlenerek karşılaştır.

**Girilecek veri**
```bash
diff <(curl -s "$APU/api/runs/$OTHER_RUN" -H "$APB" | sed "s/$OTHER_RUN/{id}/g") \
     <(curl -s "$APU/api/runs/00000000-0000-0000-0000-000000000000" -H "$APB" \
       | sed 's/00000000-0000-0000-0000-000000000000/{id}/g')
```

**Beklenen sonuç**
- İkisi de `404`; `diff` **boş**. `403` dönseydi `run`'ın varlığını doğrulardı.
- **Koşuldu (2026-09-05):** `samples/Tracon.Embedded` üzerinde koşuldu, iki
  gövde birebir aynı çıktı (`title: "Run not found"`).
- Otomatikleştirilmiş karşılığı:
  `Denied_run_read_is_indistinguishable_from_a_missing_run`.

---

### MT-SEC-153 — Reddedilen olay akışı hiç açılmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | — |

**Adımlar**
1. Reddeden handler'la `GET /api/runs/{B}/events` çağır.

**Beklenen sonuç**
- `404`; `Content-Type` **`text/event-stream` DEĞİL**. Akış başladıktan sonra
  durum kodu değiştirilemez; ret bir durum kodu olarak gelmelidir.
- Otomatikleştirilmiş karşılığı:
  `Denied_run_events_returns_404_and_the_stream_never_opens`.

---

### MT-SEC-154 — Reddedilen `trace`, `input` ve `tools` üçü de `404`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | K-685 |

**Adımlar**
1. Reddeden handler'la `/trace`, `/input`, `/tools` uçlarını çağır.
2. `/tools` ucunu **var olmayan** bir `run` id'siyle de çağır.

**Beklenen sonuç**
- Üçü de `404`.
- `/tools` var olmayan `run` için de `404` — Faz 147 öncesi `200 []` dönüyordu.
  Değişiklik kasıtlıdır: aksi hâlde ret bir varlık kanıtı olurdu (K-685).
- Otomatikleştirilmiş karşılıkları: `Denied_run_trace_returns_404`,
  `Denied_run_input_returns_404`, `Denied_run_tool_invocations_returns_404`.

---

### MT-SEC-155 — Reddedilen `cancel` `404` döner, `409` DEĞİL

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | K-684 |

**Ön koşul**
- Tamamlanmış bir `run` (normalde `cancel` için `409` döner).

**Adımlar**
1. Reddeden handler'la `POST /api/runs/{id}/cancel` çağır.

**Beklenen sonuç**
- `404`. `409` dönseydi reddedilen çağırana `run`'ın var olduğunu ve bittiğini
  söylerdi — kapı durum okumasından ÖNCE çalışır.
- Otomatikleştirilmiş karşılığı:
  `Denied_cancel_returns_404_before_the_status_is_read`.

---

### MT-SEC-156 — Reddedilen `replay` `403` döner, satır açılmaz, kota tüketilmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | — |

**Ön koşul**
- Kaynak `run` girdisiyle birlikte kayıtlı; agent tanımı `store`'da.
- Agent için `MaxRuns: 1` günlük kota tanımlı.

**Adımlar**
1. Reddeden handler'la `POST /api/runs/{id}/replay` çağır.
2. `/api/runs` sayısını önce/sonra karşılaştır.
3. Kota sayacını oku.

**Beklenen sonuç**
- `403` — replay bir `run` BAŞLATIR, kaynak okuması değildir.
- Yeni `runs` satırı **açılmaz**; kota sayacı **hareket etmez**.
- Handler'a giden istek `Access = Start` **ve** `RunId = kaynak run` taşır —
  ikincisi olmasa handler "bu agent'ı çalıştır" ile "başkasının konuşmasını
  yeniden oynat"ı ayırt edemezdi.
- Otomatikleştirilmiş karşılıkları:
  `Denied_replay_returns_403_and_opens_no_run_row`,
  `Denied_replay_does_not_consume_the_quota`.

---

### MT-SEC-157 — `/v1/chat/completions` akışlı ve akışsız dalda ayrı ayrı kapsanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | — |

**Adımlar**
1. Reddeden handler'la `stream` olmadan çağır.
2. Aynı çağrıyı `"stream": true` ile tekrarla.

**Girilecek veri**
```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$APU/../v1/chat/completions" \
  -H "$APB" -H 'content-type: application/json' \
  -d '{"model":"support","messages":[{"role":"user","content":"hi"}]}'
```

**Beklenen sonuç**
- İkisi de `403`; akışlı dalda yanıt `text/event-stream` **değil**.
- **Koşuldu (2026-09-05):** `samples/Tracon.Embedded` üzerinde bilinmeyen
  kiracıyla `403` alındı.
- Otomatikleştirilmiş karşılıkları:
  `Denied_chat_completions_returns_403_on_the_non_streaming_path`,
  `Denied_chat_completions_returns_403_on_the_streaming_path`.

---

### MT-SEC-158 — Ek uçları: yükleme ve liste `403`, indirme ve silme `404`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | K-686 |

**Adımlar**
1. Reddeden handler'la ek yükle, listele, indir ve sil.

**Beklenen sonuç**
- `POST /api/attachments` → `403` (adreslenen kaynak yok, gizlenecek kimlik yok).
- `GET /api/attachments` → `403`.
- `GET`/`DELETE /api/attachments/{id}` → `404`, var olmayan ekle birebir aynı gövde.
- Reddedilen yükleme hiçbir şey yazmaz; reddedilen silme baytları korur.
- Otomatikleştirilmiş karşılıkları:
  `Denied_attachment_upload_returns_403_and_stores_nothing`,
  `Denied_attachment_download_returns_404`,
  `Denied_attachment_delete_returns_404_and_keeps_the_bytes`,
  `Denied_attachment_list_returns_403`.

---

### MT-SEC-159 — Onay uçları: liste `403`, okuma ve karar `404`, kayıt `Pending` kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | K-684 |

**Ön koşul**
- `Pending` durumda bir onay kaydı.

**Adımlar**
1. Reddeden handler'la `/api/approvals/pending`, `/api/approvals/{id}` ve
   `/api/approvals/{id}/decide` çağır.
2. Onayın durumunu tekrar oku.

**Beklenen sonuç**
- Liste `403`; okuma ve karar `404` (var olmayan onayla birebir aynı gövde).
- Onay hâlâ `Pending`. Kapı `Status != Pending` kontrolünden ÖNCE çalışır:
  aksi hâlde `409`, reddedilen çağırana kaydın zaten karara bağlandığını
  söylerdi.
- Otomatikleştirilmiş karşılıkları: `Denied_approval_list_returns_403`,
  `Denied_approval_read_returns_404`,
  `Denied_approval_decision_returns_404_and_leaves_it_pending`.

---

### MT-SEC-160 — `throw` eden handler her kaynağı reddeder (fail-closed)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | — |

**Adımlar**
1. `AuthorizeRunAsync`'i istisna fırlatan bir handler kaydet.
2. Tekil kaynak, liste ve ek uçlarını çağır.

**Beklenen sonuç**
- Tekil kaynaklar `404`, listeler `403`. Hiçbiri açılmaz.
- Otomatikleştirilmiş karşılığı:
  `Throwing_handler_denies_every_resource_fail_closed`.

---

### MT-SEC-161 — Handler'a başka kiracının kimliği HİÇ gitmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | — |

**Adımlar**
1. Başka kiracıya ait bir `run` id'sini oku.
2. Var olmayan bir `run` id'sini oku.
3. Handler'ın gördüğü istek listesini kontrol et.

**Beklenen sonuç**
- İkisi de `404`; handler **hiç çağrılmaz**. Kapı, kaynak bulunduktan ve
  kiracısı doğrulandıktan SONRA sorulur.
- Otomatikleştirilmiş karşılıkları:
  `A_missing_run_is_answered_without_asking_the_handler`,
  `Another_tenants_run_never_reaches_the_handler`.

---

### MT-SEC-162 — Ses: başka kullanıcının oturumuna bağlanma reddedilir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | K-687 |

**Ön koşul**
- `UseVoiceConversation()` etkin, transcriber ve synthesizer kayıtlı.
- Handler `SessionAccess.Voice`'ı reddediyor.

**Adımlar**
1. Var olan bir oturumun WebSocket akışına bağlan.

**Beklenen sonuç**
- El sıkışma reddedilir; taşınan durum kodu `404` — `401` DEĞİL (kimlik
  doğrulaması başarılıydı) ve `403` DEĞİL (oturumun varlığını doğrulardı).
  Erişilemeyen oturumun aldığı yanıtla aynı metin.
- Otomatikleştirilmiş karşılıkları:
  `Denied_caller_cannot_open_another_users_conversation`,
  `A_denial_is_refused_with_404_not_401_or_403`.

---

### MT-SEC-163 — Ses: var olmayan oturumun ilk turu HÂLÂ açılır (K-283 korunur)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 147 |
| **İlgili karar** | K-283 · K-687 |

**Ön koşul**
- Handler izin veriyor.
- Sunucunun hiç görmediği bir `sessionId`.

**Adımlar**
1. O `sessionId` ile WebSocket akışına bağlan.

**Beklenen sonuç**
- Bağlantı **açılır**; ilk tur oturumu oluşturur. Handler yine de **sorulur** —
  yeni bir konuşma açma iznini yalnız tüketici verebilir.
- Bu davranış bozulursa her kurulumdaki İLK konuşma sessizce çalışmaz hâle
  gelir; başka hiçbir test bunu görmez.
- Otomatikleştirilmiş karşılığı:
  `An_unknown_session_still_opens_when_the_handler_allows_it`.


---

### MT-SEC-164 — Sahiplik kapalıyken (varsayılan) hiçbir davranış değişmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 148 |
| **İlgili karar** | K1 |

**Ön koşul**
- `Tracon:SessionOwnership` bölümü **hiç yok** (varsayılan kurulum).
- `IRunAttributionContext` bir kullanıcı çözüyor.

**Adımlar**
1. Bir oturum aç, listele, oku, dallandır, sil.
2. Veritabanında `SELECT owner_id FROM tracon.sessions;` koştur.

**Beklenen sonuç**
- Dört işlem de bu fazdan önceki davranışı birebir verir.
- `owner_id` her satırda `NULL`; yeni indeks boştur.
- Otomatikleştirilmiş karşılığı: `Nothing_changes_when_ownership_is_not_configured`.

---

### MT-SEC-165 — Sahipli liste yalnız çağıranın oturumlarını döner

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 148 |

**Ön koşul**
- `Tracon:SessionOwnership:Enabled=true`.
- A ve B kullanıcısının birer oturumu var.

**Adımlar**
1. A olarak `GET /api/sessions`.
2. B olarak aynı çağrı.

**Beklenen sonuç**
- Her biri **yalnız kendi** oturumunu görür; `ownerId` alanı doludur.
- Otomatikleştirilmiş karşılığı: `The_list_returns_only_the_callers_own_sessions`.

---

### MT-SEC-166 — Başka sahibin oturumu okuma/silme/dallandırmada `404`

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 148 |
| **İlgili karar** | K-671 |

**Ön koşul**
- Mod açık, A'nın bir oturumu var.

**Adımlar**
1. B olarak `GET /api/sessions/{A}`, sonra var olmayan bir id ile aynı çağrı.
   İki gövdeyi `diff` ile karşılaştır.
2. B olarak `DELETE /api/sessions/{A}`.
3. B olarak `POST /api/sessions/{A}/branch`.

**Beklenen sonuç**
- Üçü de `404`. Okuma gövdesi var olmayan oturumunkiyle **birebir** aynıdır
  (yalnız id metni farklı) — ret, oturumun varlığını doğrulamaz.
- A'nın oturumu **durmaya devam eder**; silme yan etki bırakmaz.
- Otomatikleştirilmiş karşılıkları:
  `Reading_another_owners_session_answers_the_same_404_a_missing_one_does`,
  `Deleting_another_owners_session_answers_404_and_leaves_it_alone`,
  `Branching_another_owners_session_answers_404`.

---

### MT-SEC-167 — Başka sahibin oturumuna `run` atmak reddedilir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 148 |

**Ön koşul**
- Mod açık, A'nın bir oturumu var.

**Adımlar**
1. B olarak `POST /api/agents/{ad}/run` gövdesinde `sessionId = {A}` ile çağır.
2. B olarak `POST /v1/responses` gövdesinde `conversation = {A}` ile çağır.

**Beklenen sonuç**
- İkisi de `403`; `errorType` = `session_owner_required`.
- A'nın oturumunun `version` alanı **değişmez** — reddedilen tur hiç yazmaz.
- 🚨 Bu case'in kapsadığı kapı, oturum uçlarınınkinden **ayrıdır**: bir turu
  sürdürmek konuşmanın tamamını modele geri okur. Yalnız `GET /api/sessions/{id}`
  korunursa sınır süs olur.
- Otomatikleştirilmiş karşılıkları:
  `Running_against_another_owners_session_is_refused`,
  `Running_against_another_owners_conversation_is_refused_on_the_OpenAI_surface`.

---

### MT-SEC-168 — Süzgeç sayfalamadan ÖNCE uygulanır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 148 |

**Ön koşul**
- Mod açık. A'nın 5 oturumu var; ayrıca 5 **sahipsiz** oturum var ve
  bunlar daha yeni güncellenmiş (yani filtresiz listenin başında gelirler).

**Adımlar**
1. A olarak `GET /api/sessions?take=3`.

**Beklenen sonuç**
- **Üç** satır döner ve üçü de A'nındır. Sahipsiz satır hiç görünmez.
- İki veya sıfır satır dönerse süzgeç sayfalamadan **sonra** uygulanıyordur;
  bu bir sızıntıdır — boşluklardan başka kullanıcının oturum sayısı çıkarılabilir.
- Otomatikleştirilmiş karşılığı: `The_owner_filter_is_applied_before_paging`.

---

### MT-SEC-169 — Kimlik çözülemezse oturum açılmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 148 |

**Ön koşul**
- Mod açık, `RequireAuthenticatedOwner=true` (varsayılan).
- `IRunAttributionContext` `null` dönüyor.

**Adımlar**
1. `sessionId` vererek `run` at.
2. Aynı isteği `sessionId` **vermeden** at.
3. Veritabanında o `sessionId`'yi ara.

**Beklenen sonuç**
- 1. adım `403`; başlık `Session owner required`.
- 2. adım `200` — oturumsuz turun sahibi olmaz, reddedilmez.
- 3. adım hiçbir satır bulmaz: **sahipsiz satır açılmaz**.
- Otomatikleştirilmiş karşılıkları:
  `Opening_a_session_without_an_identity_is_refused_and_writes_no_row`,
  `A_sessionless_run_is_unaffected_by_the_owner_requirement`.

---

### MT-SEC-170 — Yönetim rolü filtresiz kiracı listesini görür

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 148 |

**Ön koşul**
- Mod açık. `TraconPolicies.Operator` politikası **kayıtlı**.
- A'nın bir oturumu ve mod açılmadan önce yazılmış bir **sahipsiz** oturum var.

**Adımlar**
1. Operator olarak `GET /api/sessions`.
2. Politikayı kaldır, aynı çağrıyı tekrarla.

**Beklenen sonuç**
- 1. adımda iki oturum da görünür — sahipsiz eski satırın erişilebilir kaldığı
  **tek yol** budur.
- 2. adımda liste daralır. Kayıtlı olmayan politika **filtresiz liste vermez**;
  yön bilinçlidir.
- Otomatikleştirilmiş karşılıkları:
  `A_management_caller_sees_the_whole_tenant_including_unowned_rows`,
  `An_unregistered_management_policy_narrows_the_list_instead_of_opening_it`.

---

### MT-SEC-171 — Sahip gövdeden değiştirilemez, ikinci yazımda düşmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 148 |
| **İlgili karar** | K-486 |

**Ön koşul**
- Mod açık, A kimliği çözülüyor.

**Adımlar**
1. Gövdeye `ownerId`, `userId`, `owner` alanlarını `"kurban"` değeriyle koyarak
   `run` at.
2. Aynı oturumda **ikinci** bir tur at.
3. Her adımdan sonra `owner_id` ve `version` sütunlarını oku.

**Beklenen sonuç**
- Sahip her zaman **A**'dır; gövde alanları etkisizdir.
- `version` ikinci turda ilerler (gerçekten güncelleme yolu) ama `owner_id`
  değişmez ve **`NULL`'a düşmez**.
- Otomatikleştirilmiş karşılıkları: `No_body_field_can_set_the_owner`,
  `A_second_turn_on_the_same_session_keeps_the_owner`.

---

### MT-SEC-172 — Kuyruğa alınmış `run` sahibi iş zarfından alır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 148 |

**Ön koşul**
- Mod açık, iş kuyruğu işçisi koşuyor.

**Adımlar**
1. A olarak `Prefer: respond-async` ve `sessionId` ile `run` at (`202` bekle).
2. İşçi işi almadan önce çözülen kimliği **başka** bir kullanıcıya çevir.
3. İş bittikten sonra `owner_id`'yi oku ve A olarak listele.

**Beklenen sonuç**
- Sahip **A**'dır — kimliği zarf taşır, işçinin o an gördüğü değer değil.
  `HttpContext` yoktur; zarf tek doğru kaynaktır.
- Oturum A'nın listesinde çıkar, ikinci kullanıcının listesinde çıkmaz.
- Otomatikleştirilmiş karşılıkları:
  `A_queued_run_takes_its_owner_from_the_job_envelope`,
  `A_queued_run_lists_under_its_owner_afterwards`.

---

### MT-SEC-173 — Dallandırma sahibi KAYNAKTAN korur

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 148 |

**Ön koşul**
- Mod açık, kalıcı SQL sağlayıcısı etkin, A'nın bir oturumu var.

**Adımlar**
1. A olarak kendi oturumunu dallandır.
2. Yeni oturumun `owner_id` alanını oku.

**Beklenen sonuç**
- Yeni oturumun sahibi **A**'dır — dallandırma bir kopyadır, bir devir değil.
- Otomatikleştirilmiş karşılığı: `Branching_keeps_the_source_sessions_owner`.

---

### MT-SEC-174 — 👤 Dolu bir `sessions` tablosunda migration kilidi ÖLÇÜLÜR

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 148 |
| **İnsan gerekir** | 👤 Evet |

**Ön koşul**
- Üretim benzeri boyutta (en az birkaç milyon satır) dolu bir `sessions` tablosu.
- Üç sağlayıcıdan en az biri; her biri ayrı ölçülür.

**Adımlar**
1. `ALTER TABLE ... ADD COLUMN owner_id` + indeks migration'ını uygula.
2. Uygulama süresini ve tablo üzerindeki kilit süresini **ölç**.
3. Ölçülen değeri satır sayısıyla birlikte yaz.

**Beklenen sonuç**
- Sütun `NULL` varsayılanlı olduğu için tablo yeniden yazımı **beklenmez** ve
  indeks kısmidir (`WHERE owner_id IS NOT NULL`), yani boş tabloda ucuzdur.
- 🚨 Bu bir beklenti, bir ölçüm değildir. Süre **ölçülür ve yazılır**; tahmin
  edilmez. Bu case'in otomatik karşılığı **yoktur** ve olamaz — ölçüm gerçek
  veri hacmi ister.

---

### MT-SEC-175 — `RefuseUnownedSessions` tek başına hiçbir şey yapmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 149 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `Tracon:SessionOwnership:Enabled=false`, `RefuseUnownedSessions=true`.
- Sahipsiz (`owner_id IS NULL`) bir oturum var.

**Adımlar**
1. `GET /api/sessions/{sahipsiz-id}`.

**Beklenen sonuç**
- `200`. Sahiplik kapalıyken kapı hiçbir satır okumaz; bu bayrak inerttir.
- 🚨 Bu case yalnız bayrağı okuyan bir kurulumu değil, **yanlış anlaşılmayı**
  kapatır: yalnız bu satırı ekleyen bir operatör hiçbir sınır açmamıştır.
- Otomatikleştirilmiş karşılığı:
  `Strict_mode_does_nothing_while_ownership_itself_is_off`.

---

### MT-SEC-176 — Katı mod sahipsiz satırı var olmayanla AYNI gövdeyle reddeder

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 149 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `Enabled=true`, `RefuseUnownedSessions=true`.
- Sahipsiz bir oturum (`legacy`) var.

**Adımlar**
1. ```bash
   diff <(curl -s "$APU/api/sessions/legacy" -H "Authorization: Bearer $TOKEN") \
        <(curl -s "$APU/api/sessions/yok-boyle-bir-id" -H "Authorization: Bearer $TOKEN")
   ```
2. Aynı oturuma `DELETE` at; sonra satırın hâlâ durduğunu doğrula.

**Beklenen sonuç**
- İkisi de `404`; gövdeler **yalnız id metninde** ayrışır (K-671).
- `DELETE` de `404` döner ve satır **silinmez** — kapı silmeden önce sorulur.
- Otomatikleştirilmiş karşılıkları:
  `Strict_mode_answers_an_unowned_session_with_the_same_404_a_missing_one_gets`,
  `Strict_mode_refuses_deleting_an_unowned_session_and_leaves_the_row_alone`.

---

### MT-SEC-177 — Katı modda sahipsiz oturuma `run` başlatılamaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 149 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `Enabled=true`, `RefuseUnownedSessions=true`; sahipsiz `legacy` oturumu.

**Adımlar**
1. `POST /api/agents/{ad}/run`, gövdede `"sessionId":"legacy"` (varsayılan akışlı).
2. Aynı isteği `Idempotency-Key` başlığıyla tekrarla (akışsız dal).
3. Aynı oturuma ses WebSocket'i aç.

**Beklenen sonuç**
- 1 ve 2: `403` + `errorType: session_owner_required`. 🚨 **İkisi de gerçek
  `403`'tür** — sahiplik kapısı `SseWriter.StartAsync`'ten önce koşar, bu yüzden
  akışlı yol SSE `error` çerçevesine düşmez (K-324 sınıfı burada geçerli değil).
- Ret gövdesi **başkasının oturumu** reddiyle birebir aynıdır.
- 3: soket açılmaz, `404` (K-687).
- Otomatikleştirilmiş karşılıkları:
  `Strict_mode_refuses_a_run_that_names_an_unowned_session`,
  `Strict_mode_refuses_a_non_streaming_run_the_same_way`,
  `The_unowned_refusal_reads_exactly_like_another_users_refusal`,
  `Strict_mode_refuses_a_voice_socket_on_an_unowned_session`.

---

### MT-SEC-178 — 🚨 Katı modda VAR OLMAYAN oturum yine açılır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 149 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `Enabled=true`, `RefuseUnownedSessions=true`; kimlik çözülüyor.

**Adımlar**
1. ```bash
   curl -s -o /dev/null -w '%{http_code}\n' -X POST "$APU/api/agents/support/run" \
     -H "Authorization: Bearer $TOKEN" -H 'content-type: application/json' \
     -d '{"input":"merhaba","sessionId":"hic-olmayan-id"}'
   ```
2. Hiç yazılmamış bir `sessionId` ile ses WebSocket'i aç.

**Beklenen sonuç**
- 1: `200`; oturum açılır ve sahibi çağırandır.
- 2: soket **açılır**.
- 🚨 Bu case K-283'ü korur. "Henüz yok" ile "sahipsiz yazılmış" **ayrı**
  satırlardır; ikisi birleşirse her kurulumdaki İLK konuşma sessizce ölür.
- Otomatikleştirilmiş karşılıkları:
  `Strict_mode_still_opens_a_session_that_does_not_exist_yet`,
  `Strict_mode_still_opens_a_voice_socket_on_a_session_that_does_not_exist_yet`.

---

### MT-SEC-179 — Katı modda yönetim payı okur ama `run` başlatamaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 149 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `Enabled=true`, `RefuseUnownedSessions=true`.
- `Tracon.Operator` politikası kayıtlı; sahipsiz `legacy` oturumu.

**Adımlar**
1. Yönetim payı taşıyan token ile `GET /api/sessions/legacy`.
2. Aynı token ile `legacy` oturumuna `run` başlat.
3. Politikayı sağlamayan bir token ile 1'i tekrarla.

**Beklenen sonuç**
- 1: `200` — destek ekibi listede zaten gördüğü satırı açabilir.
- 2: `403` — muafiyet **okumayı** kapsar, konuşmaya **yazmayı** değil.
- 3: `404`. Politika kayıtlı değilse de `404` (fail-closed).
- Otomatikleştirilmiş karşılıkları:
  `A_management_caller_still_reads_an_unowned_session_in_strict_mode`,
  `The_management_exemption_does_not_extend_to_starting_a_run`,
  `An_unregistered_management_policy_refuses_the_unowned_row_instead_of_opening_it`.

---

### MT-SEC-180 — `/v1/conversations` tüketicinin yetki handler'ından geçer

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 149 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `AuthorizeSessionAsync`'i reddeden bir `IRunAuthorizationHandler` kayıtlı.
  Örnek uygulamada kod değişikliği gerekmez:
  `Tracon:Demo:RunAuthorization:Mode=deny-all` (K-834).
- Var olan bir oturum (`conv-1`) — mod KAPALIYKEN bir `run` ile yaratılır.

**Adımlar**
1. `GET /v1/conversations/conv-1`.
2. `GET /v1/conversations/conv-1/items`.
3. `DELETE /v1/conversations/conv-1`; sonra satırın durduğunu doğrula.
4. `POST /v1/conversations` (yeni kimlik).

**Beklenen sonuç**
- 1–3: `404`, OpenAI hata biçiminde ve başka kiracının kimliğiyle **birebir**
  aynı gövde. `403` **dönmez** — konuşmanın varlığını doğrulardı.
- 3'te satır **silinmez**.
- 4: `200` — bu uç yalnız kimlik ayırır, hiçbir şey yazmaz; kapıya bağlı değildir.
- ⚠️ Bu, handler kaydetmiş MEVCUT kurulumlar için bir **davranış
  değişikliğidir**: bu uçlar önce hiç sormuyordu.
- Otomatikleştirilmiş karşılıkları:
  `OpenAIConversationsAuthorizationTests` sınıfının tamamı.

---

### MT-SEC-181 — `/v1/conversations` haritalaması kapatılabilir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 149 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `app.MapTracon(prefix, options => options.MapOpenAIConversations = false);`
  Örnek uygulamada kod değişikliği gerekmez: `Tracon:Demo:MapOpenAIConversations=false`
  gerçek seçeneği bağlar (K-834 deseni).
- Var olan bir oturum (`conv-1`).

**Adımlar**
1. Dört uca da istek at (`POST`, `GET`, `GET …/items`, `DELETE`).
2. ```bash
   curl -s "$APU/openapi/v1.json" | jq '.paths | keys | map(select(startswith("/v1/conversations")))'
   ```
3. `GET /api/sessions/conv-1`.
4. `/v1/responses` ve `/v1/chat/completions` yollarının belgede durduğunu doğrula.

**Beklenen sonuç**
- 1: dördü de `404` — rota **hiç yok**, reddedilmiyor. ⚠️ Konsolu sunan bir
  host'ta (örnek uygulama) GET dışı metotlar `405` alır: SPA yedek rotası her
  yolu yalnız GET için eşler. Ayrım yol değil metottur — **hiç var olmamış**
  bir yol aynı cevabı verir, karşı kontrolle ayrıştır.
- 2: `[]`.
- 3: `200` — oturum kendi ucundan hâlâ erişilebilir; satır silinmemiştir.
- 4: iki yol da **durur**; bayrak yalnız conversations'ı yönetir.
- Otomatikleştirilmiş karşılıkları:
  `Turning_the_surface_off_removes_all_four_routes`,
  `Turning_the_surface_off_removes_it_from_the_OpenAPI_document`,
  `Turning_the_surface_off_leaves_the_other_OpenAI_routes_alone`.

---

### MT-SEC-182 — Profil çağrılmayan kurulum aynı kalır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 170 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `samples/Tracon.Api`, değiştirilmemiş (`RequireProductionProfile` **çağrılmamış**).

**Adımlar**
1. `ASPNETCORE_URLS=http://localhost:5199 ./artifacts/bin/Tracon.Api/release/Tracon.Api`
2. Log'da `Application started` ve `Production profile` ara.

**Beklenen sonuç**
- Host normal başlar; `Application started` **1**.
- `Production profile` geçen satır **0** — metot çağrılmayan kurulum tek bir log
  satırı bile ödemez.
- Otomatikleştirilmiş karşılığı:
  `A_host_that_does_not_declare_the_profile_starts_unchanged`.

---

### MT-SEC-183 — Açık kalan kararların hepsi tek mesajda sayılır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 170 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `--Tracon:Demo:ProductionProfile:Enabled=true` ile başlatılmış.
  **Geçici kod eklemeye GEREK YOK** — örnek uygulama bu bayrağı kalıcı olarak
  taşır (K-834). Kabul edilecek riskler
  `--Tracon:Demo:ProductionProfile:Accept=<virgüllü adlar>` ile verilir.

**Adımlar**
1. Host'u başlat.
2. Çıkış kodunu ve hata mesajını incele.

**Beklenen sonuç**
- Host **başlamaz**; `exit=134`, `Application started` **0**.
- Mesaj açık kalan **her** kararı ayrı ayrı sayar. Örnek uygulamada bugün
  **4**: `SingleTenant` · `UnownedSessions` · `UnlimitedRequestRate` ·
  `UnboundedRetention`. Altı riskten **ikisi** karşılanmış olduğu için listede
  **yoktur** — `UninspectedContent` (`AddPatternContentGuard` kayıtlı) ve
  `UnencryptedContentAtRest` (`appsettings.json`'da
  `Tracon:ContentProtection:Enabled` `true`). ⚠️ Sayı örneğin
  yapılandırmasına bağlıdır; sabitleme, **gerekçesiyle** doğrula
  (ölçüldü 2026-09-19: 4).
- Her kalem üç bilgiyi taşır: `Setting` · `Today` · `Fix`.
- Mesajın sonu `Accept(...)` yolunu ve "bu kapı hiçbir ayarı değiştirmez"
  cümlesini taşır.
- Otomatikleştirilmiş karşılığı:
  `One_permissive_decision_stops_the_host_and_is_named` (altı kalem için ayrı ayrı).

---

### MT-SEC-184 — Kabul edilen risk host'u durdurmaz ve adıyla loglanır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 170 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- MT-SEC-183'ün geçici satırı şuna çevrilmiş:
  ```csharp
  tracon.RequireProductionProfile(profile => profile
      .Accept(TraconProductionRisk.SingleTenant)
      .Accept(TraconProductionRisk.UnencryptedContentAtRest));
  ```

**Adımlar**
1. ```bash
   Logging__LogLevel__Tracon=Information \
   Tracon__SessionOwnership__Enabled=true \
   Tracon__RateLimit__Enabled=true \
   Tracon__Retention__Enabled=true \
   ./artifacts/bin/Tracon.Api/release/Tracon.Api
   ```
2. `curl -s "$APU/api/meta"`.
3. Log'da `Production profile` ara.
4. `POST /api/agents/cached-support/run` ile gerçek bir `run` yap.

**Beklenen sonuç**
- 1: host başlar, `Application started` **1**.
- 2: `200`.
- 3: **iki** satır, `Information` seviyesinde, riskleri **adıyla** sayar
  (`SingleTenant is accepted`, `UnencryptedContentAtRest is accepted`).
- 4: SSE akışı `run` → `update` → `done` turunu verir; `/api/stats` `totalRuns:1`.
- Otomatikleştirilmiş karşılığı:
  `An_accepted_risk_starts_the_host_and_is_logged_by_name`.

---

### MT-SEC-185 — Kabul komşu kalemi kapsamaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 170 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- MT-SEC-184'ün kurulumu, ama `Tracon__SessionOwnership__Enabled` **verilmemiş**.

**Adımlar**
1. Host'u başlat.

**Beklenen sonuç**
- Host **başlamaz**; mesaj yalnız `UnownedSessions` kalemini sayar.
- Kabul edilen `SingleTenant` mesajda **yoktur** — kabul yalnız adlandırılan riski
  kapsar, komşusunu değil.
- Otomatikleştirilmiş karşılığı: `An_accept_does_not_cover_the_decision_next_to_it`.

---

### MT-SEC-186 — İçerik denetimi kayıtla ölçülür, bayrakla değil

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 170 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- MT-SEC-183'ün kurulumu, ama örnek uygulamanın `AddPatternContentGuard` /
  `AddContentGuard` çağrısı geçici olarak yorumda.

**Adımlar**
1. Host'u başlat, mesajı incele.
2. Yorumu geri al, tekrar başlat.

**Beklenen sonuç**
- 1: mesaj `UninspectedContent` kalemini sayar; `Setting` satırı `IContentGuard`
  der ve `no content guard is registered` yazar — bir **seçenek bayrağı** değil.
- 2: kalem listeden düşer.
- Otomatikleştirilmiş karşılığı:
  `Content_inspection_is_measured_by_the_registration_not_by_a_flag`.

---

### MT-SEC-187 — `UseTenancy(Enabled = false)` hâlâ tek kiracı sayılır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 170 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- MT-SEC-183'ün kurulumu; ayrıca `Tracon__Tenancy__Enabled=false` ile birlikte örnek
  uygulamanın `UseTenancy` dalı geçici olarak koşulur hâle getirilmiş
  (`options.Enabled = false`).

**Adımlar**
1. Host'u başlat.

**Beklenen sonuç**
- Host **başlamaz**; `SingleTenant` kalemi listede **durur**.
- `Setting` satırı `UseTenancy(options => options.Enabled)` der — bir
  `appsettings` anahtarı DEĞİL, çünkü Tracon `TraconTenancyOptions`'a hiçbir
  yapılandırma bölümü bağlamaz. Core'un cevabı (`ITenantContext`
  değiştirildi, dolayısıyla `Satisfied`) **değil**. İki kontrol aynı riski taşır ve
  **en katı** cevap kazanır.
- Otomatikleştirilmiş karşılığı:
  `UseTenancy_with_resolution_off_is_still_reported_as_single_tenant`.

---

### MT-SEC-188 — Mesaj ve log hiçbir yapılandırma değeri taşımaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 170 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- MT-SEC-183'ün kurulumu.
- İçerik koruma **kapalı**, ama anahtar haritası canary değerlerle dolu:
  ```bash
  Tracon__ContentProtection__Keys__canary-4f2a-key-id=CanaryKeys:canary-4f2a
  ```

**Adımlar**
1. Host'u başlat.
2. Hata mesajını ve log'un tamamını canary dizeleri için tara.

**Beklenen sonuç**
- Mesaj `Tracon:ContentProtection:Enabled` **adını** taşır.
- `canary-4f2a-key-id` ve `CanaryKeys:canary-4f2a` **hiçbir yerde** yoktur — ne
  mesajda, ne log'da (K-059).
- Otomatikleştirilmiş karşılığı:
  `Neither_the_failure_nor_the_log_carries_a_configured_value`.

---

### MT-SEC-189 — Kapı HTTP yüzeyi olmayan host'ta da koşar

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 170 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `MapTracon` çağırmayan gömülü bir host; `RequireProductionProfile()` çağrılmış.

**Adımlar**
1. Host'u başlat.

**Beklenen sonuç**
- Host **başlamaz** — bunlar kompozisyon kararlarıdır, bir HTTP kaygısı değil.
- `SingleTenant` kalemi `SingleTenantContext` adını taşır; kiracı sorusu gömülü
  host'ta da **anlamlı** yanıtlanır, sessizce eksilmez.
- Otomatikleştirilmiş karşılığı: `The_gate_runs_in_a_host_with_no_HTTP_surface`.

---

### MT-SEC-190 — Denetim izi yazılamazken onay kararı uygulanmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 171 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- `samples/Tracon.Api` koşuyor, PostgreSQL bağlı.
- Bekleyen bir onay var (`GET /api/approvals/pending` bir kalem döner).

**Adımlar**
1. `audit_log` tablosuna yazmayı reddet — uygulamanın rolünden `INSERT` iznini al:
   ```sql
   REVOKE INSERT ON audit_log FROM tracon_app;
   ```
2. Bekleyen onayı onayla: `POST /api/approvals/{id}/decide` `{"approved": true}`.
3. `GET /api/approvals/{id}` ile durumu oku.

**Beklenen sonuç**
- Adım 2 `500` döner.
- Onay hâlâ `Pending`'dir — karar **uygulanmamıştır** (K-089, K-370).
- Log'da bir `LogError` vardır ve `tracon.audit.write_failures` sayacı
  `tracon.audit.outcome=refused` etiketiyle **1** artmıştır.
- Otomatikleştirilmiş karşılığı:
  `An_approval_decision_is_not_applied_when_its_audit_entry_cannot_be_written`.

---

### MT-SEC-191 — Denetim izi yazılamazken yönetim çağrısı devam eder

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 171 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- MT-SEC-190'ın adım 1'i uygulanmış (`audit_log` yazılamıyor).

**Adımlar**
1. Yeni bir agent tanımı oluştur: `POST /api/agents`.
2. `GET /api/agents/{name}` ile oku.

**Beklenen sonuç**
- Adım 1 `201` döner; agent **gerçekten** oluşmuştur — best-effort yol işlevselliği
  bozmaz.
- Log'da bir `LogWarning` vardır ve `tracon.audit.write_failures` sayacı
  `tracon.audit.outcome=swallowed` etiketiyle **1** artmıştır.
- 🚨 İki case'in farkı buradadır: aynı arıza bir işlemi durdurur, diğerini
  durdurmaz. Sayacın `outcome` etiketi bu ikisini ayırır.
- Otomatikleştirilmiş karşılığı:
  `An_administration_call_still_succeeds_when_its_best_effort_audit_write_fails`.

---

### MT-SEC-192 — `audit_log` onarıldıktan sonra aynı karar uygulanır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 171 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- MT-SEC-190 koşulmuş; onay hâlâ `Pending`.

**Adımlar**
1. Yazma iznini geri ver:
   ```sql
   GRANT INSERT ON audit_log TO tracon_app;
   ```
2. Aynı onayı tekrar onayla.
3. `GET /api/audit/verify` ile zinciri doğrula.

**Beklenen sonuç**
- Karar uygulanır (`Approved`), audit satırı yazılır.
- Zincir `Valid` döner — reddedilen deneme zincirde **boşluk bırakmaz**, çünkü
  hiç satır yazılmamıştır.
- Ret bir zehirli hap değildir: başarısız deneme sonraki kararı engellemez.
- Otomatikleştirilmiş karşılığı:
  `The_same_approval_is_decided_once_the_audit_store_recovers`.

---

### MT-SEC-193 — Denetim izi yazılamazken veri konusu silinmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 171 |
| **İnsan gerekir** | Hayır |

**Ön koşul**
- MT-SEC-190'ın adım 1'i uygulanmış.
- Kayıtlı bir `IDataSubjectResolver` ve silinecek içeriği olan bir veri konusu var.

**Adımlar**
1. `DELETE /api/data-subjects/{id}?dryRun=true` ile satır sayımını al.
2. `DELETE /api/data-subjects/{id}?dryRun=false` çağır.
3. Adım 1'i tekrarla.

**Beklenen sonuç**
- Adım 2 `500` döner.
- Adım 3'ün sayımı adım 1'inkiyle **aynıdır** — silme `COMMIT` edilmemiştir
  (K-462: audit yazımı `beforeCommitAsync` içinden koşar, hata her `DELETE`'i geri alır).
- Otomatikleştirilmiş karşılığı: `Erase_fails_loudly_when_the_audit_write_fails`.
