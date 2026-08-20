# 13 — Kiracı ve Güvenlik (`SEC`)

> **Alan kodu:** `SEC` · **Faz:** 6, 9, 41, 50, 53, 63, 65, 69
> **Kaynak:** `src/AgentPrism.AspNetCore/Security/` (tümü: `AgentPrismEndpointFilter`,
> `LoopbackGuard`, `BearerTokenValidator`, `ApiKeyAuthenticator`, `ApiKeyRequestContext`,
> `ApiKeyScopeRequirement`, `ExternalSurfaceGuard`, `ExternalCallAudit`, `AgentPrismPolicies`,
> `AgentPrismRolePolicies`, `RoleEndpointConventionBuilderExtensions`) ·
> `src/AgentPrism.AspNetCore/Tenancy/` (tümü) ·
> `src/AgentPrism.AspNetCore/AgentPrismEndpointOptions.cs` ·
> `src/AgentPrism.AspNetCore/AgentPrismEndpointRouteBuilderExtensions.cs` (yalnız erişim
> katmanlaması — genel `MapAgentPrism` sözleşmesi `07`'nin işi) ·
> `src/AgentPrism.AspNetCore/Endpoints/ApiKeyEndpoints.cs`, `AuditEndpoints.cs`,
> `GovernanceEndpoints.cs` (yalnız `MapTenants` + `MapApprovalRules` — MCP sunucu
> bölümleri `18`'in işi), `TenantProviderEndpoints.cs` (Faz 65 — kiracı sağlayıcı
> bağlamaları/BYOK ve egress politikası) ·
> `src/AgentPrism.Abstractions/Options/AgentPrismTenantProviderOptions.cs` (Faz 65) ·
> `src/AgentPrism.Core/Models/ProviderCredentialClientCache.cs` (Faz 65) ·
> `src/AgentPrism.Abstractions/Approvals/` (tümü — `ToolArgumentCondition`,
> `ToolArgumentOperator`, `ToolApprovalPolicyDecision`, `ToolApprovalContext`) ·
> `src/AgentPrism.Core/Approvals/` (tümü — `ToolArgumentConditionMatcher`,
> `ToolApprovalPolicyRegistry`, `ToolApprovalRuleEvaluator`) ·
> `src/AgentPrism.UI/frontend/src/screens/mcp.tsx` (yalnız "Remembered approvals"
> paneli — sunucu CRUD kısmı `18`'in işi) ·
> `src/AgentPrism.Abstractions/Security/` (tümü) · `src/AgentPrism.Abstractions/Audit/` (tümü) ·
> `src/AgentPrism.Abstractions/Tenancy/` (tümü) ·
> `src/AgentPrism.Core/Security/` (tümü) · `src/AgentPrism.Core/Audit/` (tümü) ·
> `src/AgentPrism.Core/Tenancy/` (tümü) ·
> `src/AgentPrism.Abstractions/Tools/ToolEffect.cs`, `ToolAuthorizationTypes.cs` (Faz 69) ·
> `src/AgentPrism.Core/Tools/AuthorizingAIFunction.cs`, `TimeoutAIFunction.cs`,
> `AllowAllToolAuthorizationHandler.cs`, `ToolRegistry.cs` (Faz 69).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/13-KIRACI-VE-GUVENLIK.md`](kosumlar/2026-08-13/13-KIRACI-VE-GUVENLIK.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

`app.MapAgentPrism()`'in kurduğu **üç katmanlı erişim koruması** (loopback →
bearer/API anahtarı → authorization policy), **kiracı çözümlemesi ve izolasyonu**
(Faz 41), **kiracı bazlı API anahtarları** (Faz 53: kapsam, kiracı bağı, yaşam
döngüsü) ve **denetim izi** (Faz 9: eylemler, sır süzgeci, değiştirilemezlik).
Rol tabanlı yetkilendirme (`AgentPrismPolicies`) da burada — AgentPrism kullanıcı
veya rol saklamaz; roller tüketicinin kimlik sisteminden gelir, bu yüzden bazı
case'ler geçici bir test kimlik doğrulama şeması gerektirir (aşağıda §8).

> 🚨 **§8 artık ELLE KOD YAZMAYI gerektirmez (2026-08-18, K-431).** Örnek
> uygulama gösterim amaçlı bir rol şeması taşıyor: `AgentPrism:Demo:Roles:Enabled`
> `true` yapılır (ortam değişkeni yeter:
> `AgentPrism__Demo__Roles__Enabled=true`), rol `X-AgentPrism-Demo-Role:
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
| Hız sınırlama (`AgentPrismRateLimitFilter`, varsayılan kapalı) | **Hiçbir dosyaya atanmamış** — bkz. `00-INDEKS.md` §8 düşen not |
| Ses WebSocket el sıkışması (`Sec-WebSocket-Protocol` token taşıma) | `19-COK-MODLULUK-VE-SES.md` |
| `/api/diagnostics`'in genel şekli ve alanları | `25-SAGLIK-TESHIS-OPENAPI.md` — burada yalnız rol koruması bir örnek uç olarak kullanılır |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. `AgentPrism:Ui:AuthToken` sırrı `manuel-test-token-2026`'dır (`FIX-TOKEN-01`).
3. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080/agentprism`.
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
   cd samples/AgentPrism.Api && dotnet run --urls "http://0.0.0.0:5080"
   ```

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export APULAN="http://$LANIP:5080/agentprism"
```

> **Gerçek para uyarısı.** §4 (kiracı izolasyonu) `FIX-AGENT-01` ile gerçek bir
> `run` başlatır ve küçük ölçüde OpenAI ücreti doğurur. Diğer tüm bölümler
> (erişim katmanları, kiracı yönetimi, API anahtarları, roller, denetim izi)
> hiçbir model çağırmaz.
>
> **Doküman düzeltmesi (koşumda, 2026-08-13).** Yukarıdaki not artık YANLIŞ:
> `AgentPrism:Ui:AllowRemoteAccess` anahtarı 2026-08-11'de `Program.cs`'e
> bağlandı (commit `419981b`, satır 708-715 —
> `builder.Configuration.GetValue<bool?>("AgentPrism:Ui:AllowRemoteAccess")`).
> §7'deki `MT-SEC-070`/`071` artık **geçici kod değişikliği GEREKTİRMİYOR**;
> `AgentPrism__Ui__AllowRemoteAccess` ortam değişkeni/`user-secrets` anahtarı
> yeterli. Koşum bu şekilde (yalnız yapılandırma ile) yapıldı ve doğrulandı.

---

# 1 — Üç katmanlı erişim koruması (Faz 4/50, `AgentPrismEndpointFilter`)

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
- `title: "Uzak erisim kapali"`, `detail` `AllowRemoteAccess ayarini acin...`
  ile devam eder.
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
- Gövde `title: "Kimlik dogrulanamadi"`, `detail`
  `Gecerli bir 'Authorization: Bearer <token>' basligi gerekiyor.`

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
- `HTTP: 200` — `AgentPrismEndpointRouteBuilderExtensions.cs:82-84`'teki
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

**Beklenen sonuç**
- `loopback: 200` — `MapUi`'nin grubu `requireBearerToken: false` ile kurulur
  (`AgentPrismEndpointRouteBuilderExtensions.cs:294`); tarayıcı bir
  `<script src>` isteğine `Authorization` ekleyemeyeceği için bu katman
  bilerek atlanır.
- `lan: 403` — loopback kısıtı bu grup için de geçerlidir, yalnız bearer
  katmanı atlanır.

### MT-SEC-020 — `UseTenancy` hiç çağrılmamışken her istek varsayılan kiracıya düşer

Bu case §3'ün ön koşulu UYGULANMADAN, temiz durumda koşulur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- `AgentPrism:Tenancy:Enabled` AYARLANMAMIŞ (varsayılan kapalı).

**Adımlar**
1. `GET /api/tenants/current` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "$APB"
```

**Beklenen sonuç**
- `{"tenantId":"default"}` — `AgentPrismOptions.DefaultTenantId` varsayılanı.

---

### MT-SEC-021 — `AllowHeaderResolution` açıkken `X-AgentPrism-Tenant` başlığı kiracıyı belirler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 41 |
| **İlgili karar** | — |

**Ön koşul**
- §3 ön koşulu uygulandı (Tenancy açık, header çözümü açık).

**Adımlar**
1. `X-AgentPrism-Tenant: kiraci-alfa` (`FIX-TENANT-01`) başlığıyla iste.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa"
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
dotnet user-secrets set "AgentPrism:Tenancy:Enabled" "true"
dotnet user-secrets remove "AgentPrism:Tenancy:AllowHeaderResolution"
```
Uygulama yeniden başlatılır.

**Adımlar**
1. Aynı başlıkla aynı çağrıyı tekrarla.

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa"
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
     -H "X-AgentPrism-Tenant: kiraci alfa/beta"
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
`AgentPrismEndpointFilter`'a `CheckTenancyWhitelist` eklendi — istek artık
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
dotnet user-secrets set "AgentPrism:Tenancy:Enabled" "true"
dotnet user-secrets set "AgentPrism:Tenancy:AllowHeaderResolution" "true"
```
`Program.cs`'in `UseTenancy` bloğu `AllowedTenants`'ı okumaz (yalnız
`Enabled`/`ClaimType`/`AllowHeaderResolution`) — bu yüzden bu case'i
koşabilmek için `agentPrism.UseTenancy(...)` çağrısına GEÇİCİ olarak
`options.AllowedTenants.Add("kiraci-alfa");` satırı eklenir (Program.cs
satır ~666-672 civarı), test bitince kaldırılır.

**Adımlar**
1. Listede OLMAYAN bir kiracı adıyla iste (`kiraci-gamma`).

**Girilecek veri**
```bash
curl -s "$APU/api/tenants/current" -H "$APB" -H "X-AgentPrism-Tenant: kiraci-gamma"
```

**Beklenen sonuç**
- `403 Forbidden` döner (`title: "Kiraci reddedildi"`).
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
     -H "X-AgentPrism-Tenant: kiraci-alfa" -H "content-type: application/json" -d '{
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
     -H "X-AgentPrism-Tenant: kiraci-beta" -H "content-type: application/json" -d '{
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
SELECT tenant_id, name, version FROM agentprism.agent_definitions
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
curl -s "$APU/api/agents" -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa" | python3 -m json.tool
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
     -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa" -H "content-type: application/json" \
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
     -H "X-AgentPrism-Tenant: kiraci-alfa"
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
     -H "X-AgentPrism-Tenant: kiraci-beta"
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
     -H "$APB" -H "X-AgentPrism-Tenant: kiraci-beta"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/manuel-destek" \
     -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa"
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
SELECT slug, display_name FROM agentprism.tenants WHERE slug = 'kiraci-alfa';
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
- `HTTP: 400`. `title: "Kiraci anahtari gecersiz"`, `detail`
  `Anahtar en fazla 64 karakter olmali ve yalnizca harf, rakam, nokta, alt
  cizgi ve tire icermelidir.`

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
     -H "X-AgentPrism-Tenant: kiraci-alfa"
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
- `HTTP: 404`. `title: "Kiraci bulunamadi"`.

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
- `HTTP: 400`. `detail: "'name' bos olamaz."`

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
- `HTTP: 400`. `detail: "En az bir kapsam ('scopes') secilmelidir."`

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
- `HTTP: 403`. `title: "Kapsam yetersiz"`, `detail`
  `Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor.`

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
SELECT id, revoked_at FROM agentprism.api_keys WHERE id = '<APIKEY_READ_ID>';
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

### MT-SEC-060 — `X-AgentPrism-Tenant` başlığı, anahtarın kiracısıyla ÇELİŞİRSE `403`

Negatif senaryo. `bölüm 53.5`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 53 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "AgentPrism:Tenancy:Enabled" "true"
dotnet user-secrets set "AgentPrism:Tenancy:AllowHeaderResolution" "true"
```
Uygulama yeniden başlatılır (anahtarlar sıfırlanır — yeniden üretin, bu
kez `kiraci-alfa` kiracısı için `AgentsRead` kapsamlı bir anahtar):
```bash
curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa" \
     -H "content-type: application/json" -d '{ "name": "alfa-anahtar", "scopes": ["AgentsRead"] }'
# plaintextKey -> $APIKEY_ALFA
```

**Adımlar**
1. Bu anahtarla, `X-AgentPrism-Tenant: kiraci-beta` başlığı EKLEYEREK
   (anahtarın kiracısıyla çelişen bir değer) istek at.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents" \
     -H "Authorization: Bearer $APIKEY_ALFA" -H "X-AgentPrism-Tenant: kiraci-beta"
```

**Beklenen sonuç**
- `HTTP: 403`. `title: "Kiraci uyusmuyor"`, `detail`
  `'X-AgentPrism-Tenant' basligi API anahtarinin baglandigi kiraciyi EZEMEZ...`

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
1. `X-AgentPrism-Tenant` başlığı VERMEDEN `GET /api/tenants/current` çağır.

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
- `samples/AgentPrism.Api/Program.cs`'te `app.MapAgentPrism("/agentprism", options => { ... })`
  bloğuna (satır ~701-712, `options.AuthToken = ...` bloğunun hemen altına)
  GEÇİCİ olarak şu satır eklenir:
  ```csharp
  options.AllowRemoteAccess = true;
  ```

**Adımlar**
1. `dotnet run` ile başlat.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Süreç açılışta `InvalidOperationException` ile ÇÖKER. Konsol çıktısı
  `AllowRemoteAccess acikken mcp disa acilamaz: sistemde 'external:invoke'
  kapsamli...` dizgisini içerir (`ExternalSurfaceGuard.cs:72-76`).
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
- MT-SEC-070'in geçici satırı GERİ ALINIR (`AllowRemoteAccess = true` kaldırılır,
  varsayılana dönülür).
- Uygulama `AllowRemoteAccess` OLMADAN normal başlatılır.

**Adımlar**
1. `ExternalInvoke` kapsamlı bir anahtar oluştur.
2. Uygulamayı durdur, `options.AllowRemoteAccess = true;` satırını TEKRAR
   ekle (MT-SEC-070'teki gibi), `dotnet run --urls "http://0.0.0.0:5080"`
   ile yeniden başlat.
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
- `HTTP: 201` — hiçbir `AgentPrism.*` policy'si kayıtlı olmadığından
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
`options.RequireRolePolicies = true;` `MapAgentPrism` lambda'sına eklenir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- `RoleTestAuthHandler.cs` silinmiş, `AddAuthentication`/`AddAuthorization`
  eklemeleri geri alınmış (temiz `Program.cs`).
- `MapAgentPrism("/agentprism", options => { ... options.RequireRolePolicies = true; })`
  satırı eklenmiş.

**Adımlar**
1. `dotnet run` ile başlat.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Açılışta `InvalidOperationException`. Mesaj üç policy adını da içerir:
  `AgentPrism.Reader`, `AgentPrism.Operator`, `AgentPrism.Admin`
  (`AgentPrismRolePolicies.cs:82-87`).

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
- Örnek uygulamada `AddToolApprovalPolicy("refund_order", ctx => ToolApprovalPolicyDecision.Required)` kayıtlı bir agent VAR (`samples/AgentPrism.Api` içinde bu tool için ayrı bir sabit politika örneği kurulmalı; yoksa 👤 insan gerekir — kodda geçici olarak eklenip test edilir, kalıcı örnek şart değil).
- MT-SEC-100'ün koşulsuz kuralı (her çağrıyı otomatik onaylayan) kayıtlı.

**Adımlar**
1. `refund_order`'ı çağırt.

**Beklenen sonuç**
- Çağrı onay İSTER — veri kuralı otomatik onaylardı ama kod politikası `Required` döndüğü için kod kazanır (`IsAutoApprovedAsync`, politika `Required`/`NotRequired` dalı veri kurallarından ÖNCE değerlendirilir).

**Not:** 👤 insan gerekir — `samples/AgentPrism.Api`'ye geçici bir `AddToolApprovalPolicy` çağrısı eklemeden koşulamaz.

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
- `HTTP: 400`, `title: "Invalid request"`; gövde "AgentPrism:ProviderKeys:" önekini anar.
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
1. `dotnet user-secrets set "AgentPrism:ProviderKeys:Acme:OpenAI" "sk-..." --project samples/AgentPrism.Api`
2. Bağlamayı kaydet, sonra listele.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/tenants/acme/providers/openai" -H "$APB" -H "content-type: application/json" -d '{
  "apiKeyConfigurationName": "AgentPrism:ProviderKeys:Acme:OpenAI"
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
1. `dotnet user-secrets remove "AgentPrism:ProviderKeys:Acme:OpenAI" --project samples/AgentPrism.Api` (değeri sil, bağlama kaydı DURSUN).
2. `acme` kiracısı olarak bir `run` başlat.

**Beklenen sonuç**
- Çalıştırma **global anahtarla devam ETMEZ**; `AgentPrismException` kaynaklı
  anlaşılır bir hata alınır ve mesaj `AgentPrism:ProviderKeys:Acme:OpenAI`
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
psql "$PG_CONN" -c "SELECT tenant_id, provider_name, api_key_configuration_name, endpoint FROM agentprism.tenant_provider_bindings;"
```

**Beklenen sonuç**
- `api_key_configuration_name` sütunu yalnız ADI taşır (`AgentPrism:ProviderKeys:...`);
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
- `AgentPrismCompilationException` kaynaklı bir hata; mesaj `openai` adını ve
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
  "apiKeyConfigurationName": "AgentPrism:ProviderKeys:Acme:OpenAI"
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
`AddAgentPrism()`'in varsayılan `AllowAllToolAuthorizationHandler`'ı geçerli.

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
`samples/AgentPrism.Api`'nin `get_slow_report` tool'u (1 sn timeout, 5 sn
uyuyan gövde) kayıtlı — bkz. `samples/AgentPrism.Api/OrderTools.cs`.

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

**Ön koşul** Varsayılan ayarlar (`AgentPrism:Egress:AllowPrivateNetworkTargets`
tanımlı DEĞİL). Örnek uygulama ayakta.

**Adımlar**
```bash
curl -s -X PUT "$APU/api/tenants/acme/providers/anthropic" -H "$APB" \
  -H 'Content-Type: application/json' \
  -d '{"apiKeyConfigurationName":"AgentPrism:ProviderKeys:acme","endpoint":"http://10.0.0.5/"}' \
  | jq -r '.detail'
```

**Beklenen sonuç**
- `400`. `detail` şu metni içerir:
  `The target resolves to a private network address (10.0.0.5); set
  'AgentPrism:Egress:AllowPrivateNetworkTargets' to true to allow it.`
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
  'AgentPrism:WebhookSecrets:'.`
- Aynı istek `"secretConfigurationKey":"AgentPrism:WebhookSecrets:orders"` ile
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
1. Aboneliğe `headers` içinde `{"X-AgentPrism-Signature":"sahte"}` ekle.
2. Bir `run` tamamlanmasını tetikle ve alıcının aldığı ham isteği incele.
3. Sunucu loglarını oku.

**Beklenen sonuç**
- Alıcıya **tek bir** `X-AgentPrism-Signature` başlığı ulaşır ve değeri
  AgentPrism'in hesapladığı imzadır — `sahte` değildir.
- Sunucu logunda uyarı: `Webhook subscription 'orders' carries the reserved
  header 'X-AgentPrism-Signature'; it was not sent.`
- Sıradan adlı bir ek başlık (örn. `X-Tenant`) normal şekilde iletilir.
