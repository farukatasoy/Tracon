# 13 — Kiracı ve Güvenlik (`SEC`)

> **Alan kodu:** `SEC` · **Faz:** 6, 9, 41, 50, 53
> **Kaynak:** `src/AgentPrism.AspNetCore/Security/` (tümü: `AgentPrismEndpointFilter`,
> `LoopbackGuard`, `BearerTokenValidator`, `ApiKeyAuthenticator`, `ApiKeyRequestContext`,
> `ApiKeyScopeRequirement`, `ExternalSurfaceGuard`, `ExternalCallAudit`, `AgentPrismPolicies`,
> `AgentPrismRolePolicies`, `RoleEndpointConventionBuilderExtensions`) ·
> `src/AgentPrism.AspNetCore/Tenancy/` (tümü) ·
> `src/AgentPrism.AspNetCore/AgentPrismEndpointOptions.cs` ·
> `src/AgentPrism.AspNetCore/AgentPrismEndpointRouteBuilderExtensions.cs` (yalnız erişim
> katmanlaması — genel `MapAgentPrism` sözleşmesi `07`'nin işi) ·
> `src/AgentPrism.AspNetCore/Endpoints/ApiKeyEndpoints.cs`, `AuditEndpoints.cs`,
> `GovernanceEndpoints.cs` (yalnız `MapTenants` — MCP/onay kuralı bölümleri `18`'in işi) ·
> `src/AgentPrism.Abstractions/Security/` (tümü) · `src/AgentPrism.Abstractions/Audit/` (tümü) ·
> `src/AgentPrism.Abstractions/Tenancy/` (tümü) ·
> `src/AgentPrism.Core/Security/` (tümü) · `src/AgentPrism.Core/Audit/` (tümü) ·
> `src/AgentPrism.Core/Tenancy/` (tümü).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

`app.MapAgentPrism()`'in kurduğu **üç katmanlı erişim koruması** (loopback →
bearer/API anahtarı → authorization policy), **kiracı çözümlemesi ve izolasyonu**
(Faz 41), **kiracı bazlı API anahtarları** (Faz 53: kapsam, kiracı bağı, yaşam
döngüsü) ve **denetim izi** (Faz 9: eylemler, sır süzgeci, değiştirilemezlik).
Rol tabanlı yetkilendirme (`AgentPrismPolicies`) da burada — AgentPrism kullanıcı
veya rol saklamaz; roller tüketicinin kimlik sisteminden gelir, bu yüzden bazı
case'ler geçici bir test kimlik doğrulama şeması gerektirir (aşağıda §8).

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

**Gerçek sonuç**
HTTP: 200.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 403, title: Uzak erisim kapali, detail AllowRemoteAccess ayarini acin... ile devam ediyor. Dogru token olmasina ragmen reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 401. WWW-Authenticate: Bearer basligi var. Govde title: Kimlik dogrulanamadi, detail Gecerli bir Authorization: Bearer <token> basligi gerekiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 401. Govde MT-SEC-003 ile birebir ayni (title/detail) - yanlis token hakkinda ipucu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 401 - Basic sema reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 401.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Meta ve kabuk istisnaları

`app.MapAgentPrism` üç ayrı grup kurar: `/api/meta` (hiçbir filtre),
arayüz kabuğu + MCP OAuth geri dönüşü (loopback + policy VAR, bearer YOK) ve
geri kalan her şey (üç katman da var). Bu bölüm ayrımı somutlaştırır.

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

**Gerçek sonuç**
HTTP: 200 - loopback disindan, Authorization basliksiz bile /api/meta erisilebilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
requiresBearerToken, allowRemoteAccess, requiresAuthorizationPolicy alanlarinin ucu var. manuel-test-token-2026 dizgisi govdenin hicbir yerinde gecmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
loopback: 200, lan: 403 - kabuk bearer token'dan muaf ama loopback'ten muaf degil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Kiracılık çözümlemesi (`HttpTenantContext`, Faz 41)

**Bu bölümün ön koşulu.** Aşağıdaki `dotnet user-secrets` komutları uygulanır
(kod değişikliği GEREKMEZ — `Program.cs` bu anahtarları zaten okur):

```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Tenancy:Enabled" "true"
dotnet user-secrets set "AgentPrism:Tenancy:AllowHeaderResolution" "true"
# ClaimType KASITLI olarak verilmez: claim ayarlıysa baslik hic okunmaz.
```
Uygulamayı yeniden başlatın.

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

**Gerçek sonuç**
{"tenantId":"default"} - tenancy hic ayarlanmamisken varsayilan kiraciya dusuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
{"tenantId":"kiraci-alfa"}

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
{"tenantId":"default"} - AllowHeaderResolution kapaliyken X-AgentPrism-Tenant basligi hic okunmadi, zincir varsayilana dustu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 200, tenantId: default - IsValidTenantId gecersiz degeri reddetti, zincir varsayilana dustu, hata verilmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
`HTTP: 403`, `title: "Kiraci reddedildi"`, `detail: "Cozulen kiraci izin verilenler listesinde degil. Bu istek varsayilan kiracinin verisine SESSIZCE dusurulmez; reddedilir."` — `{"tenantId":"default"}` DÖNMEDİ. K-393 öncesi kusurun düzeltmesi doğru çalışıyor (düzeltilmiş davranış gözlendi). Geçici `options.AllowedTenants.Add("kiraci-alfa")` satırı `Program.cs`'e eklenip test koşuldu, sonra kaldırılıp yeniden derlendi (`git diff` temiz, iz bırakmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Kiracı izolasyonu (veri sınırı)

**Ön koşul (tüm bölüm)** — §3'ün ön koşulu (Tenancy açık, header çözümü açık)
uygulanmış olmalı; ek olarak `AllowedTenants` boş bırakılır (whitelist yok).

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

**Gerçek sonuç**
HTTP: 201.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 201 (409 DEGIL) - iki farkli kiracida ayni ad serbest birakildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
kiraci-alfa listesinde manuel-destek var (tek kopya, kendi kiracisinin surumu); kiraci-beta'nin ayri satiri sizmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 200. Not: Idempotency-Key basligi yokken run ucu varsayilan olarak akisli (SSE) yanit veriyor (sistem geneli tutarli davranis, dosya 07'de de gozlendi) - runId event: run cercevesinden okundu: 019ffb05-7e0d-7ade-a93a-5b240cd21758.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 200.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 404 (403 DEGIL) - kiraci-beta'ya calistirmanin var oldugu bilgisi bile sizmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adim 1: HTTP 204. Adim 2: HTTP 200 - kiraci-alfa'nin kopyasi hala var, kiraci-beta'nin silmesi yalniz kendi satirini etkiledi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Kiracı kayıt yönetimi (`GovernanceEndpoints.MapTenants`, Faz 9)

Bu bölüm §3/§4'ten BAĞIMSIZDIR: `ITenantStore` kaydı `UseTenancy` açık
olmasa bile çalışır (kayıt zorunlu değildir, yalnız arayüz için bir isim/açıklama
kaynağıdır).

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

**Gerçek sonuç**
HTTP: 200. Govde slug: kiraci-alfa, displayName: Alfa Musterisi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 200, displayName guncellendi (Alfa Musterisi (guncel)), ayni id (019ffb04...) - ikinci satir olusmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 400, title: Kiraci anahtari gecersiz, detail beklenen metinle birebir eslesiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
kiraci-alfa listede var.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adim 1: HTTP 204. Adim 2: HTTP 200 - kiraci kaydi silinmesi calisma anindaki agent cozumlemesini etkilemedi (ITenantStore kaydi yalniz isim/aciklama kaynagi, zorunlu degil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 404, title: Kiraci bulunamadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — API anahtarları (Faz 53)

Reset yordamı yeniden uygulanır (§3-5'in geçici ayarları temizlenir); bu
bölüm `UseTenancy` AÇIK OLMADAN başlar (MT-SEC-060/061 kendi ön koşulunu
ayrıca belirtir).

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

**Gerçek sonuç**
HTTP: 200. plaintextKey ap_ ile basliyor (ap_default_RPaDJLnwS0...). record.keyPrefix (ap_default_R, 12 karakter) plaintextKey'in ilk 12 karakteriyle ayni. record.name: manuel-okuma, record.scopes: [RunsRead].

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Yanit govdesinde plaintextKey (ap_default_RPaDJ...) hicbir yerde gecmiyor. Alanlar yalniz id, tenantId, name, keyPrefix, scopes, expiresAt, revokedAt, lastUsedAt, createdAt, isActive - keyHash yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 400, detail: 'name' bos olamaz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 400, detail: En az bir kapsam ('scopes') secilmelidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
**KALDI - HATA-S2-006 (Orta).** Beklenen HTTP 400 yerine HTTP 500 (genel ProblemDetails, 'An error occurred while processing your request.') dondu. Kok neden: CreateAsync (ApiKeyEndpoints.cs:57-64) [FromBody] ApiKeyCreateRequest ile OTOMATIK minimal-API govde baglama kullaniyor; bilinmeyen bir ApiKeyScope dizgisi System.Text.Json'in JsonStringEnumConverter'inda bir JsonException firlatir ve bu istisna handler govdesine HIC ULASMADAN once, framework'un kendi govde-baglama asamasinda olusur. Diger uclar (orn. /api/agents/validate, /v1/chat/completions) govdeyi ELLE JsonSerializer.Deserialize + try/catch (JsonException) ile okuyup temiz 400 'Govde cozumlenemedi:' uretiyor; bu uc ise otomatik baglamaya guveniyor ve app.UseExceptionHandler() (Program.cs:682, ozellestirilmemis) istisnayi genel 500 ProblemDetails'a ceviriyor. Kapsam: [FromBody] kullanan diger 10 dosya da (ApprovalEndpoints, EvalEndpoints, ExperimentEndpoints, RetentionEndpoints, QuotaEndpoints, RunEndpoints, SchedulingEndpoints, SkillScriptGrantEndpoints, WebhookEndpoints, WorkflowEndpoints) potansiyel olarak ayni deseni tasiyabilir - ayrintili dogrulanmadi, yalniz bu case olculdu.

---

**Yeniden koşum (Aile G, 2026-08-14).** DÜZELTİLDİ — **HTTP 400**:
`{"title":"Gecersiz istek govdesi","detail":"The JSON value could not be converted to AgentPrism.ApiKeyScope. Path: $.scopes[0]..."}`.
Kök neden düzeltmesi tek endpoint'e özel bir yama DEĞİL, kütüphane çapında bir
yeniden tasarımdır: `ApiKeyEndpoints.CreateAsync` artık `[FromBody]` otomatik
baglamasi yerine `RequestBodyBinding.ReadAsync<T>` (yeni,
`AgentPrism.AspNetCore/Internal/RequestBodyBinding.cs`) ile govdeyi elle okur —
`AgentEndpoints`'in zaten kullandığı desenle aynı. Bu koşumda tahmin edilen 10
dosyanın TAMAMI (ve tahminin KAÇIRDIĞI, implicit binding kullanan
`GovernanceEndpoints`, `AgentEndpoints.RollbackAsync`, `.../run`,
`SkillEndpoints`, `SessionEndpoints`, `KnowledgeEndpoints` ×2, `VoiceEndpoints`,
`GovernanceEndpoints` tenants/mcp-prompts uçları) aynı desene taşındı — ayrıntı
`KAPANIS-PLANI.md` §6 Aile G. Ayrıca kütüphane çapında bir savunma katmanı
(`JsonBindingProblemMiddleware`) eklendi: elle okumayı unutan gelecekteki bir
uç için, yalnız `Development` ortamında (framework'ün `ThrowOnBadRequest`
bayrağı yalnız orada açık) 500'ü 400'e çevirir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 200.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 403, title: Kapsam yetersiz, detail: Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adim 1: HTTP 204. Adim 2: HTTP 401 - satir silinmedi, revokedAt yazildi, sonraki istek reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 404 - ikinci iptal 'bulunamadi' gibi goruldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
6 saniye sonra HTTP: 401 - suresi gecmis anahtar reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 403, title: Kiraci uyusmuyor, detail: 'X-AgentPrism-Tenant' basligi API anahtarinin baglandigi kiraciyi EZEMEZ...

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
{"tenantId":"kiraci-alfa"} - baslik verilmeden kiraci dogrudan anahtardan cozuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Iki kayit dondu: apikey.create ve apikey.revoke. create kaydinin after alani yalniz name, keyPrefix, scopes tasiyor - ham deger veya ozet yok. revoke kaydinin before/after alanlari null.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Uzak erişimin API anahtarıyla koşullanması (Faz 50 × 53, `ExternalSurfaceGuard`)

Örnek uygulama `.UseMcpServer(...)` VE `.UseA2A(...)` çağırır ve
`app.MapAgentPrismMcpServer(); app.MapAgentPrismA2A();` ile bunları açar
(`Program.cs:98-99,717-718`). Bu, `AllowRemoteAccess = true` yapıldığı anda
`ExternalSurfaceGuard.EnsureRemoteAccessNotCombined`'in **açılışta** devreye
girdiği anlamına gelir — sıra önemlidir.

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

**Gerçek sonuç**
**Dokuman duzeltmesi uygulandi (yukaridaki not).** Gecici kod degisikligi yerine `AgentPrism__Ui__AllowRemoteAccess=true` ortam degiskeniyle baslatildi ('external:invoke' kapsamli hicbir anahtar yokken). Surec aciliste `Unhandled exception: System.InvalidOperationException: AllowRemoteAccess acikken MCP disa acilamaz: sistemde 'external:invoke' kapsamli, suresi gecmemis ve iptal edilmemis bir API anahtari yok...` ile COKTU (ExternalSurfaceGuard.cs:144). Sync/aciliste calisan denetim dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
SQLite'a gecici olarak gecildi (anahtarin yeniden baslatma boyunca hayatta kalmasi icin - bellek ici depoda case dogal olarak test edilemez, anahtar da fixture'lar gibi silinirdi). Adim 1: HTTP 200, ExternalInvoke kapsamli anahtar uretildi. Adim 2: `AgentPrism__Ui__AllowRemoteAccess=true` ile 0.0.0.0'a baglanarak yeniden baslatildi, surec COKMEDI (basariyla acildi). Adim 3: LAN adresinden dogru token ile istek HTTP 200 dondu - MT-SEC-002'nin 403'u burada alinmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Bu bölümden sonra temizlik.** `options.AllowRemoteAccess = true;` satırı
> Program.cs'ten kaldırılır ve reset yordamı yeniden uygulanır — §8 bu
> varsayımla başlar.

---

# 8 — Rol tabanlı yetkilendirme (`AgentPrismPolicies`, Faz 6)

AgentPrism rol/kullanıcı SAKLAMAZ; `AgentPrismPolicies.Reader/Operator/Admin`
yalnızca policy ADLARIdır ve tüketicinin kendi `AddAuthorization` çağrısında
tanımlanmadıkça (`AgentPrismRolePolicies.Resolve`) hiçbir şey yapmazlar
(`RoleEndpointConventionBuilderExtensions.cs:25-34`). Örnek uygulama hiçbir
rol policy'si veya kimlik doğrulama şeması TANIMLAMAZ (`grep -rn
"AddAuthorization\|AddAuthentication" samples/AgentPrism.Api/Program.cs`
boş döner) — bu yüzden MT-SEC-071'den sonrasını çalıştırmak için GEÇİCİ bir
test kimlik doğrulama şeması eklenir.

**Bu bölümün ön koşulu — geçici kod (test bitince İKİSİ de kaldırılır).**

1. Yeni dosya `samples/AgentPrism.Api/RoleTestAuthHandler.cs`:
   ```csharp
   // GECICI TEST DOSYASI — yalniz MT-SEC-08x rol testleri icindir. Test
   // bitince bu dosyayi silin.
   using System.Security.Claims;
   using System.Text.Encodings.Web;
   using Microsoft.AspNetCore.Authentication;
   using Microsoft.Extensions.Options;

   namespace AgentPrism.Api;

   public sealed class RoleTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
   {
       public const string SchemeName = "RoleTest";

       public RoleTestAuthHandler(
           IOptionsMonitor<AuthenticationSchemeOptions> options,
           ILoggerFactory logger,
           UrlEncoder encoder)
           : base(options, logger, encoder)
       {
       }

       protected override Task<AuthenticateResult> HandleAuthenticateAsync()
       {
           var role = Request.Headers["X-Test-Role"].ToString();

           if (string.IsNullOrEmpty(role))
           {
               return Task.FromResult(AuthenticateResult.NoResult());
           }

           var roleClaims = role switch
           {
               "reader" => new[] { "agentprism-reader" },
               "operator" => new[] { "agentprism-reader", "agentprism-operator" },
               "admin" => new[] { "agentprism-reader", "agentprism-operator", "agentprism-admin" },
               _ => Array.Empty<string>(),
           };

           var claims = roleClaims
               .Select(r => new Claim(ClaimTypes.Role, r))
               .Append(new Claim(ClaimTypes.NameIdentifier, "manuel-test-kullanici"));

           var identity = new ClaimsIdentity(claims, SchemeName);
           var principal = new ClaimsPrincipal(identity);

           return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
       }
   }
   ```

2. `samples/AgentPrism.Api/Program.cs`'in en üstündeki `using` bloğuna
   (satır 61 civarı) şu satırı ekle:
   ```csharp
   using Microsoft.AspNetCore.Authentication;
   ```

3. `var app = builder.Build();` satırının (satır ~680) HEMEN ÜSTÜNE:
   ```csharp
   builder.Services.AddAuthentication(RoleTestAuthHandler.SchemeName)
       .AddScheme<AuthenticationSchemeOptions, RoleTestAuthHandler>(RoleTestAuthHandler.SchemeName, null);

   builder.Services.AddAuthorization(options =>
   {
       options.AddPolicy(AgentPrismPolicies.Reader,
           p => p.RequireRole("agentprism-reader", "agentprism-operator", "agentprism-admin"));
       options.AddPolicy(AgentPrismPolicies.Operator,
           p => p.RequireRole("agentprism-operator", "agentprism-admin"));
       options.AddPolicy(AgentPrismPolicies.Admin,
           p => p.RequireRole("agentprism-admin"));
   });
   ```

4. `var app = builder.Build();` satırının HEMEN ALTINA (`app.UseExceptionHandler();`'dan önce):
   ```csharp
   app.UseAuthentication();
   app.UseAuthorization();
   ```

5. Yeniden başlat: `dotnet run`. Rol seçimi artık her istekte
   `X-Test-Role: reader|operator|admin` başlığıyla yapılır; başlık
   verilmezse istek kimliksiz kalır (üç katmanlı korumadan geçer ama
   `AuthenticateResult.NoResult()` nedeniyle hiçbir rolü karşılamaz).

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

**Gerçek sonuç**
HTTP: 201 - hicbir AgentPrism.* policy'si kayitli olmadigindan RequireRole hicbir sey eklemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 403.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 201.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adim 1: HTTP 200. Adim 2: HTTP 403 (404 DEGIL) - rol denetimi handler'dan once calisti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Gecici olarak §8'in AddAuthentication/AddAuthorization + RoleTestAuthHandler.cs kurulumu geri alinip, MapAgentPrism lambda'sina `options.RequireRolePolicies = true;` eklendi, yeniden derlendi. `dotnet run` aciliste `Unhandled exception: System.InvalidOperationException: AgentPrismEndpointOptions.RequireRolePolicies acik ama su policy'ler kayitli degil: AgentPrism.Reader, AgentPrism.Operator, AgentPrism.Admin...` ile COKTU (AgentPrismRolePolicies.cs:82). Uc policy adi da mesajda gecti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
{'canRead': True, 'canOperate': True, 'canAdminister': True} - hicbir policy kayitli degilken hepsi true. RequireRolePolicies satiri kaldirilip yeniden derlendi (git diff temiz), RoleTestAuthHandler.cs silindi, sade ornek uygulamayla dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Denetim izi (`IAuditLog`, Faz 9)

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

**Gerçek sonuç**
Uc kayit dondu, en yeniden eskiye: [agent.delete, agent.update, agent.create]. Her kaydin entity alani agent:manuel-audit.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
after JSON'unda "Authorization":"***" gorunuyor - ham deger (cok-gizli-deger) govdenin hicbir yerinde yok. Not: ayni kayitta authorizationConfigurationKey, oauthClientSecretConfigurationKey, oauthAuthorizationMode alanlari da *** olarak redakte edilmis (asiri-redaksiyon, alan adinda key/secret/authorization fragmani geciyor olabilir) - bu sizinti degil tam tersi yonde bir gozlem, case'in kendi iddiasini etkilemiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
after.model.maxOutputTokens: 512 - gercek sayisal degeriyle gorunuyor, *** degil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
actor: None (null).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
limit=1 ile 1 kayit dondu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 405 (Method Not Allowed).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bölüm sonu — temizlik

Bu dosyadaki case'ler bittiğinde:

1. `samples/AgentPrism.Api/RoleTestAuthHandler.cs` (varsa) silinir.
2. `Program.cs`'e eklenen `AddAuthentication`/`AddAuthorization`,
   `UseAuthentication`/`UseAuthorization`, `options.AllowRemoteAccess = true;`
   ve `options.RequireRolePolicies = true;` satırları geri alınır.
3. `git diff samples/AgentPrism.Api/Program.cs` çalıştırılıp değişiklik
   KALMADIĞI doğrulanır.
4. `dotnet user-secrets list` ile `AgentPrism:Tenancy:*` girdileri temizlenir
   (isteğe bağlı — sonraki dosya zaten kendi reset yordamını uygular).
