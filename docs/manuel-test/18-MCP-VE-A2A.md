# 18 — MCP İstemcisi/Sunucusu ve A2A Dış Yüzeyi (`MCP`)

> **Alan kodu:** `MCP` · **Faz:** 6, 22, 50, 89 (tool çıktısı boyut sınırı — ikinci sarmalama zinciri), 117 (Tasks uzantısı), 190 (kimlik başlıkları anahtar adıyla)
> **Kaynak:** `src/Tracon.Mcp/` (tümü — istemci tarafı: sunucu keşfi,
> tool/prompt/resource köprüsü, OAuth) · `src/Tracon.AspNetCore/McpServer/`
> (tümü — Tracon'i MCP sunucusu olarak dışa açma) ·
> `src/Tracon.AspNetCore/A2A/` (tümü — Tracon'i A2A sunucusu olarak
> dışa açma) · `src/Tracon.AspNetCore/Endpoints/GovernanceEndpoints.cs`
> (yalnız `/api/mcp-servers/*` dalı — MCP sunucu kaydı CRUD, prompt/resource
> köprüsü, OAuth başlatma) · `src/Tracon.AspNetCore/Security/ExternalSurfaceGuard.cs`,
> `ExternalCallAudit.cs` · `src/Tracon.Abstractions/Mcp/McpServerDefinition.cs` ·
> `src/Tracon.UI/frontend/src/screens/mcp.tsx`, `tools.tsx` (yalnız MCP
> rozeti) · `src/Tracon.UI/frontend/src/components/mcp-server-detail.tsx` ·
> Migration'lar: `mcp_servers` tablosu (PostgreSQL `0002_observability.sql` +
> `0013_mcp_oauth.sql`; SQL Server/SQLite `0001_initial.sql`).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/18-MCP-VE-A2A.md`](../arsiv/manuel-test-kosum-2026-09/18-MCP-VE-A2A.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/18-MCP-VE-A2A.md

---

## Bu dosya neyi kanıtlar

Tracon, Model Context Protocol'ü **iki yönde** de konuşur. Faz 22
(**istemci** yönü): Tracon, dışarıdaki bir MCP sunucusuna bağlanıp
onun tool/prompt/resource'larını kendi agent'larına tool olarak sunar —
sunucular kodda değil, veritabanında tanımlıdır ve arka planda periyodik
taranır. Faz 50 bunun tam tersini ekledi (**sunucu** yönü): Tracon'in
kendi katalog agent'ları, dışarıdaki bir MCP istemcisine (ör. Claude Code
CLI) tek bir tool olarak sunulabilir. Aynı fazda, kardeş bir protokol olan
A2A (Agent2Agent) ile agent'lar kendi "agent kartı"nı yayınlayabilir. Faz 6
temel keşif altyapısını getirmişti; bu dosya Faz 22/50'nin üzerine kurulu
**bugünkü** yüzeyi test eder.

```mermaid
flowchart TD
    subgraph Istemci["Tracon ISTEMCI (Faz 22)"]
        DB["mcp_servers tablosu<br/>(kodda degil, DB'de)"] --> DS["McpDiscoveryService<br/>5 dk'da bir tarar"]
        DS -->|basarili| TOOLS["AIFunction listesi<br/>{server}_{tool}"]
        DS -->|ulasilamaz| DEGRADE["o sunucu 0 tool<br/>digerleri etkilenmez"]
    end

    subgraph Sunucu["Tracon SUNUCU (Faz 50)"]
        CATALOG["IAgentCatalog<br/>CANLI, her istekte okunur"] --> MCPSRV["/tracon/mcp<br/>tools/list, tools/call"]
        CATALOG --> A2ASRV["/tracon/a2a/{agent}<br/>agent-card.json + SendMessage"]
        GUARD["McpApprovalGuardFilter<br/>onay gerektiren tool varsa"] -.->|UYGULAMA BASLAMAZ| MCPSRV
    end

    EXT["Dis MCP istemcisi<br/>(Claude Code CLI vb.)"] --> MCPSRV
    EXT2["Dis A2A istemcisi"] --> A2ASRV

    GOV["PUT/DELETE api/mcp-servers/name<br/>GovernanceEndpoints"] --> DB

    style DEGRADE fill:#1f4a6f,stroke:#0d2740,color:#ffffff
    style GUARD fill:#6f1f2a,stroke:#400d15,color:#ffffff
    style GOV fill:#5f4a1e,stroke:#302510,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Genel HTTP zarfı, idempotency-key deseni | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| Rol/API anahtarı kapsam sisteminin GENEL mekanizması | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — burada yalnız bu alana **özgü** kapsam boşluğu test edilir (§9) |
| Onay kartı akışının GENEL davranışı (bekleyen onay, `cancel_order` gibi kod-tanımlı tool'lar) | `10-ARAYUZ-AGENT-PLAYGROUND.md` (zaten üretildi) — burada yalnız MCP-kökenli tool'ların onay bayrağı test edilir |
| Tek yürütücü seçimi (`SingletonGuard`, `McpDiscoveryService`'in kümede tek örnekte taranması) | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` (zaten üretildi, Faz 42 kanıtı) — burada tekrarlanmaz |
| Audit izinin GENEL şeması | `13-KIRACI-VE-GUVENLIK.md` — burada yalnız `external.call` kaydının varlığı doğrulanır |

> **Rol matrisi burada da NO-OP'tur.** MCP-sunucu ve A2A grupları zaten
> `RequireRole(...)` HİÇ ÇAĞIRMAZ (kod tasarımı — dış çağıranlar için
> doğal bir Reader/Operator/Admin kavramı yoktur). **Bu dosyaya özgü
> olan**: dış yüzeyin kendisi (`/tracon/mcp`, `/tracon/a2a`)
> `RequireApiKeyScope(ExternalInvoke)`'u DOĞRU uygular, ama MCP sunucu
> **kayıt** API'si (`/api/mcp-servers/*`, `GovernanceEndpoints.cs`) hiçbir
> `RequireApiKeyScope` çağrısı taşımaz — §9'da ayrı ayrı ölçülür, biri
> pozitif kontrol biri kusur adayı.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080/tracon`.
3. Örnek uygulama `summarizer` agent'ını **hem** MCP **hem** A2A ile dışa
   açar: kayıt tarafında `.UseMcpServer(o =>
   o.ExposedAgents.Add("summarizer"))` / `.UseA2A(o =>
   o.ExposedAgents.Add("summarizer"))`, uç tarafında ise **`MapTraconMcpServer()`**
   ve **`MapTraconA2A()`** (`MapTracon()`'dan ayrı, isteğe bağlı uçlardır ve
   aynı erişim korumasını kullanırlar) — bu agent **kasıtlı olarak**
   hiçbir tool taşımaz, bu yüzden §6/§7'nin onay-sınırı guard'ını hiç
   tetiklemez. Guard'ı tetiklemek isteyen case'ler (§6 MT-MCP-034, §7
   MT-MCP-045) GEÇİCİ bir `Program.cs` değişikliği ister — bu değişiklik
   case sonunda GERİ ALINIR.
4. `Tracon:Mcp` bölümü `appsettings.json`'da tanımlı DEĞİLDİR; örnek
   uygulama `UseMcp(builder.Configuration.GetSection(...))`
   (config-bağlı overload) kullanır — §2'nin case'leri bu farkı ölçer.
5. **Yerel bir test MCP sunucusu bu repo'da hiç dokümante edilmemiştir**
   (bkz. §5 başlığı) — §1-§4'ün "gerçek bağlantı" gerektiren case'leri
   tester'ın kendi kuracağı bir sunucuya ihtiyaç duyar; §5 bu kurulumu
   tarif eder.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
export PG="docker exec -i ap-pg psql -U postgres -d tracon"
```

> **Gerçek para uyarısı.** §4 MT-MCP-023, §6 MT-MCP-032/036, §7 MT-MCP-041
> gerçek bir agent çalıştırması içerir (`echo` sağlayıcısı yeterlidir,
> OpenAI şart değildir — `summarizer`/`support` `echo` ile de çalışır).
> Kalan tüm case'ler model çağırmaz.

---

## Bu dosyanın yerel fixture'ları

| Kimlik | Değer |
|---|---|
| `FIX-MCP-01` | Sunucu adı `test-sunucu` · `endpoint: "http://localhost:6060/mcp"` · `transport: "StreamableHttp"` · `requiresApproval: false` · **tester-tedarikli** — bkz. §5, repo'da dokümante edilmiş bir yerel MCP sunucusu YOKTUR |
| `FIX-MCP-02` | `summarizer` — örnek uygulamanın kendi MCP+A2A ile dışa açtığı, tool'suz agent (`00-INDEKS.md` §3.1) |

---

# 1 — MCP İstemcisi: Sunucu Kaydı CRUD ve Doğrulama (Faz 22)

### MT-MCP-001 — `PUT {prefix}/api/mcp-servers/{name}` yeni bir sunucu kaydı oluşturur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/mcp-servers/test-sunucu" -H "$APB" \
     -H "content-type: application/json" -d '{
  "endpoint": "http://localhost:6060/mcp",
  "transport": "StreamableHttp",
  "description": "Manuel test icin yerel MCP sunucusu",
  "enabled": true,
  "requiresApproval": false
}'
```

**Beklenen sonuç**
- `HTTP: 200`, gövdede `id` dolu bir GUID, `requiresApproval: false`
  (istekte açıkça belirtildiği için varsayılan `true` geçersiz kılınmıştır).

---

### MT-MCP-002 — `requiresApproval` alanı GÖNDERİLMEZSE varsayılan `true`

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/mcp-servers/varsayilan-onay" -H "$APB" \
     -H "content-type: application/json" -d '{ "endpoint": "http://localhost:6061/mcp" }'
# Duzeltme: GET /api/mcp-servers/{name} (tekil) yoktur, yalniz liste ucu var.
curl -s "$APU/api/mcp-servers" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print([s for s in d if s['name']=='varsayilan-onay'][0]['requiresApproval'])"
```

**Beklenen sonuç**
- `True` yazdırılır — güvenlik yönü "kapalı" (onay iste) varsayılandır,
  "açık" (onaysız çalıştır) değil.

---

### MT-MCP-003 — `stdio` transport denemesi → `400`

Negatif senaryo. K-058: stdio taşıması kasıtlı olarak desteklenmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | K-058 |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/mcp-servers/stdio-denemesi" -H "$APB" \
     -H "content-type: application/json" -d '{ "endpoint": "stdio://bir-komut", "transport": "Stdio" }'
```

**Beklenen sonuç**
- `HTTP: 400`. `transport` yalnız `StreamableHttp`/`Sse` kabul eder;
  `stdio://` bir `Uri` olarak da geçersizdir.

---

### MT-MCP-004 — `http`/`https` DIŞI bir uç adresi → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/mcp-servers/ftp-sunucu" -H "$APB" \
     -H "content-type: application/json" -d '{ "endpoint": "ftp://ornek.com/mcp" }'
```

**Beklenen sonuç**
- `HTTP: 400`.

---

### MT-MCP-005 — `oauthEnabled: true` VE `authorizationConfigurationKey` BİRLİKTE → `400`

Negatif senaryo — karşılıklı dışlayan alanlar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/mcp-servers/karisik-yetki" -H "$APB" \
     -H "content-type: application/json" -d '{
  "endpoint": "http://localhost:6062/mcp",
  "oauthEnabled": true,
  "authorizationConfigurationKey": "Tracon:Mcp:BirTest"
}'
```

**Beklenen sonuç**
- `HTTP: 400` — bir sunucu ya statik başlık tabanlı yetkilendirme ya OAuth
  kullanabilir, ikisi birden olamaz.

---

### MT-MCP-006 — JSON yanıtında OAuth alanları `oauthEnabled`/`oauthClientId` biçiminde (camelCase, çift büyük harf DEĞİL)

Sınır senaryosu — Faz 22'nin kendi devir notunda kayıtlı, testler
yakalamamış bir sınıf hata (`docs/arsiv/fazlar/22-MCP-DERINLESMESI.md` "Plandan
Sapmalar"). Bu, önceden bir kez elle `curl` ile yakalanmış bir hatanın
tekrar tetiklenmediğini doğrular.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/mcp-servers/oauth-test" -H "$APB" -H "content-type: application/json" -d '{
  "endpoint": "http://localhost:6063/mcp",
  "oauthEnabled": true,
  "oauthClientId": "test-client",
  "oauthClientSecretConfigurationKey": "Tracon:Mcp:TestSecret",
  "oauthScopes": "read"
}' | python3 -m json.tool
```

**Beklenen sonuç**
- Ham JSON gövdesinde alan adları `"oauthEnabled"`, `"oauthClientId"`,
  `"oauthClientSecretConfigurationKey"`, `"oauthScopes"` biçimindedir —
  `"oAuthEnabled"` (çift büyük harf) DEĞİL. Bu bug bir kez yakalanmıştı;
  regresyon olup olmadığı burada gözle doğrulanır.

---

### MT-MCP-007 — `GET {prefix}/api/mcp-servers` listede secret DEĞERİ hiç GÖRÜNMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | K-059 |

**Girilecek veri**
```bash
curl -s "$APU/api/mcp-servers" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Her sunucu satırında `authorizationConfigurationKey` yalnız bir
  yapılandırma **anahtarı adı** (ör. `"Tracon:Mcp:TestSecret"`)
  taşır — hiçbir gerçek secret DEĞERİ (token, şifre) gövdede yer almaz
  (K-059). `headers` alanı yalnız başlık **adlarını** taşır; her değer
  `"***"` döner (2026-09-23 kusur giderme, MT-MCP-069). Değer `store`'da
  düz metin durur ve MCP sunucusuna aynen gider; bu yüzden `headers` içine
  secret yazılmaz (§8 MT-MCP-049 ile karşılaştır).

---

### MT-MCP-008 — `DELETE` sunucu kaydını siler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/mcp-servers/ftp-sunucu" -H "$APB"
```

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM tracon.mcp_servers WHERE name IN ('ftp-sunucu', 'stdio-denemesi', 'karisik-yetki');
```

**Beklenen sonuç**
> **Düzeltildi (2026-09-17):** `ftp-sunucu` MT-MCP-004'te ZATEN `400` ile
> reddedilmiş, hiç oluşmamıştı — `DELETE` bu yüzden `204` değil `404 "MCP
> server not found"` döner (uç var-olmayan bir kaydı silmeyi idempotent
> `204` değil, açık `404` ile işaretliyor). Asıl iddia (üç negatif case'in
> kalıcı iz BIRAKMADIĞI) SQL sorgusuyla doğrulanır, HTTP durum kodu değil.
- `HTTP: 404`. SQL sorgusu `0` döner (bu üç sunucu hiç başarıyla
  oluşturulmamıştı — negatif case'lerin kalıcı iz bırakmadığının kanıtı).

### MT-MCP-010 — Örnek uygulama config-bağlı `UseMcp` overload'ını kullanır — `Tracon:Mcp:RefreshInterval` GERÇEKTEN etkilidir

Bu, önceden bir kere kayda geçmiş bir hatanın (K-353: `RefreshInterval`
`IConfiguration`'a hiç bağlanmıyordu) düzeltmesinin canlı doğrulamasıdır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | K-353 |

**Girilecek veri**
```bash
cd samples/Tracon.Api
dotnet user-secrets set "Tracon:Mcp:RefreshInterval" "00:00:10"
# Uygulamayi yeniden baslat.
```

**Adımlar**
1. Uygulamayı yeniden başlat.
2. MT-MCP-001'deki `test-sunucu` kaydını (varsa yeniden oluştur) düzenle
   veya yeni bir sunucu ekle.
3. 15 saniye içinde tool kataloğunun taranıp taranmadığını (uygulama
   loglarında `McpDiscoveryService` ile ilgili bir tarama satırı, veya
   `/api/tools` çıktısındaki değişim) gözle.

**Beklenen sonuç**
- Tarama, VARSAYILAN `5 dakika` yerine `10 saniye`de bir gerçekleşir —
  `appsettings.json`'daki `Tracon:Mcp:RefreshInterval` GERÇEKTEN
  okunmuştur. (K-353 öncesi bu ayar sessizce yok sayılırdı.)
- Case sonrası `dotnet user-secrets remove "Tracon:Mcp:RefreshInterval"`.

---

### MT-MCP-011 — Per-server ayarlar (`Endpoint`, `Transport`, `Headers`...) HİÇBİR ZAMAN `IConfiguration`'dan okunmaz

Sınır senaryosu — sık karıştırılan bir ayrım.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Adımlar**
1. `appsettings.json`/`user-secrets`'a `Tracon:Mcp:Servers:0:Endpoint`
   gibi bir anahtar EKLEMEYİ dene (böyle bir yapı zaten kod tarafından
   okunmaz — bu case'in amacı, bu tür bir anahtarın SESSİZCE yok
   sayıldığını doğrulamaktır).

**Girilecek veri**
```bash
cd samples/Tracon.Api
dotnet user-secrets set "Tracon:Mcp:Servers:0:Endpoint" "http://olmayan-bir-yer/mcp"
# Uygulamayi yeniden baslat.
curl -s "$APU/api/mcp-servers" -H "$APB" | python3 -c "import json,sys; print([s['name'] for s in json.load(sys.stdin)])"
```

**Beklenen sonuç**
- Bu anahtar HİÇBİR etki üretmez — listede böyle bir sunucu YOKTUR.
  Sunucu kayıtları yalnız `mcp_servers` DB tablosundan gelir. Case sonrası
  bu `user-secrets` anahtarını kaldır.

---

### MT-MCP-012 — `authorizationConfigurationKey` config'te TANIMSIZ/BOŞSA → istisna YOK, başlık atlanır

Negatif/edge senaryo — sessiz bozulma, hata değil.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- `test-sunucu`, `authorizationConfigurationKey: "Tracon:Mcp:HicVarOlmayanAnahtar"`
  ile güncellenmiş (bu anahtar `user-secrets`'ta HİÇ tanımlı DEĞİL).

**Adımlar**
1. Bir sonraki keşif turunu bekle (veya `POST /api/mcp-servers/refresh`
   ile elle tetikle).
2. Uygulama loglarında bir `LogWarning` ara.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/mcp-servers/refresh" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200`/`204`. Uygulama ÇÖKMEZ, tarama devam eder. Bağlantı isteği
  o başlık OLMADAN gönderilir (uygun bir loglama satırı beklenir, ama
  kesin format koşumda kaydedilir).

### MT-MCP-015 — Var olmayan bir MCP sunucusu kaydetmek AGENT KAYDINI ETKİLEMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Adımlar**
1. Hiçbir yerde çalışmayan bir uç adresle sunucu kaydet.
2. Uygulamanın hâlâ ayakta ve `support` agent'ının hâlâ çalışır durumda
   olduğunu doğrula.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/mcp-servers/ulasilamayan" -H "$APB" -H "content-type: application/json" -d '{
  "endpoint": "http://localhost:59999/hic-yok"
}'
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -d '{ "message": "Merhaba" }'
```

**Beklenen sonuç**
- Sunucu kaydı `HTTP: 200` ile başarıyla oluşur (kayıt anında BAĞLANTI
  denenmez). Sonraki `run` çağrısı normal şekilde çalışır — ulaşılamayan
  MCP sunucusu agent kaydını/çalıştırmasını hiç etkilemez.

---

### MT-MCP-016 — Sunucu keşif turu ortasında OFFLINE olursa, ESKİ (bayat) tool listesi KORUNMAZ — boşaltılır

Sınır senaryosu — "bayat veriden iyi, boş liste" tasarım kararı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- §5'teki yerel test sunucusu (`FIX-MCP-01`) çalışır durumda, en az bir
  başarılı keşif turu geçmiş (tool listesi dolu).

**Adımlar**
1. Yerel test sunucusunu DURDUR.
2. Bir sonraki keşif turunu bekle (veya `POST /refresh`).
3. `GET /api/tools` (veya agent editöründeki tool listesi) ile
   `test-sunucu_*` önekli tool'ların durumunu kontrol et.

**Beklenen sonuç**
- Önceden keşfedilmiş tool'lar LİSTEDEN KAYBOLUR — `RefreshCatalogAsync`
  başarısız olduğunda önceki iyi listeyi TUTMAZ, `Tools=[]`'a düşürür. Bu,
  "eski ama muhtemelen hâlâ doğru" bilgi yerine "kesin taze" bilgiyi
  tercih eden kasıtlı bir tasarımdır.

---

### MT-MCP-017 — Bir sunucunun zaman aşımına uğraması DİĞER sunucuları ETKİLEMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- `ulasilamayan` (MT-MCP-015) ve `test-sunucu` (çalışır durumda) İKİSİ de
  kayıtlı.

**Adımlar**
1. `POST /api/mcp-servers/refresh` çağır.
2. Her iki sunucunun tool katkısını ayrı ayrı kontrol et.

**Beklenen sonuç**
- `ulasilamayan`'ın `ConnectionTimeout` (varsayılan 30sn) sonunda
  başarısız olması, `test-sunucu`'nun kendi tool'larını normal şekilde
  katmasını ENGELLEMEZ — her sunucu bağımsız değerlendirilir.

---

### MT-MCP-018 — Arka plan keşif döngüsü İSTİSNA sonrası ASLA çökmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Adımlar**
1. `ulasilamayan` sunucusu kayıtlıyken uygulamayı en az 2 keşif aralığı
   (varsayılan 5 dk × 2, veya MT-MCP-010'un kısaltılmış aralığıyla) canlı
   tut.
2. Uygulama loglarını izle.

**Beklenen sonuç**
- Her turda bir `LogError`/`LogWarning` görülür ama uygulama ÇÖKMEZ, HTTP
  uçları (`/api/agents`, vb.) normal yanıt vermeye devam eder — arka plan
  servisinin kendi istisna yakalaması (`McpDiscoveryService.ExecuteAsync`)
  bunu garanti eder.

### MT-MCP-020 — Keşfedilen tool adı `{sunucu}_{tool}` biçiminde niteleniyor — NOKTA AYRACI YOK

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | K-060 |

**Ön koşul**
- `test-sunucu` çalışır durumda, en az bir tool sunuyor (§5'te kurulur).

**Girilecek veri**
```bash
curl -s "$APU/api/tools" -H "$APB" | python3 -c "import json,sys; print([t['name'] for t in json.load(sys.stdin) if t.get('source')=='test-sunucu'])"
```

**Beklenen sonuç**
- Tool adları `test-sunucu_<orijinal-ad>` biçimindedir — `test-sunucu.
  <orijinal-ad>` (nokta ile) DEĞİL. OpenAI-uyumlu sağlayıcılar fonksiyon
  adında nokta kabul etmediği için bu kasıtlıdır.

---

### MT-MCP-021 — Kod-tanımlı bir tool ile AYNI ADA sahip MCP tool'u ÇAKIŞIRSA kod tool KAZANIR

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- Yerel test MCP sunucusunun `get_order_status` adlı bir tool sunacak
  şekilde yapılandırıldığı (§5'te tarif edilen sunucunun yapılandırma
  seçeneğiyle) VE bu sunucunun `Name` alanının `get_order_status` niteleme
  sonrası `get_order_status_get_order_status` DEĞİL, doğrudan
  `get_order_status` ile çakışacak şekilde kurulması — pratikte bu, sunucu
  adının BOŞ/aynı-önek olacak biçimde kurulmasını gerektirir; koşum
  notunda gerçek çakışmanın nasıl üretildiği kaydedilir.

**Beklenen sonuç**
- `McpToolRegistry.List()`/`TryGet()` önce kod-kayıtlı tool'lara bakar —
  aynı isimli bir MCP tool'u varsa GÖRMEZDEN GELİNİR, kod tool'u kazanır.

---

### MT-MCP-022 — `MaxToolsPerServer` aşımı → fazla tool'lar UYARIYLA düşürülür, HATA değil

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon:Mcp:MaxToolsPerServer` `1` olarak ayarlanmış (test
  amaçlı), yerel sunucu 2+ tool sunuyor.

**Girilecek veri**
```bash
cd samples/Tracon.Api
dotnet user-secrets set "Tracon:Mcp:MaxToolsPerServer" "1"
# Uygulamayi yeniden baslat, keşif turunu bekle.
curl -s "$APU/api/tools" -H "$APB" | python3 -c "import json,sys; print(len([t for t in json.load(sys.stdin) if t.get('source')=='test-sunucu']))"
```

**Beklenen sonuç**
- Sunucu keşfi BAŞARISIZ OLMAZ — yalnız ilk `1` tool tutulur, fazlası
  uyarı logu ile düşürülür. Case sonrası `MaxToolsPerServer`'ı kaldır.

---

### MT-MCP-023 — `requiresApproval=true` bir MCP tool'u agent tarafından çağrılınca ONAY KARTI üretir

Gerçek entegrasyon — mutlu yol.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- `test-sunucu`'nun `requiresApproval` alanını `true` yap (MT-MCP-001'in
  aksine).
- `test-sunucu`'nun tool'larından birini kullanan bir agent oluştur veya
  var olan bir agent'a bu tool'u ekle.

**Adımlar**
1. Agent'ı, MCP tool'unu tetikleyecek bir mesajla çalıştır.
2. Yanıtın bir onay kartı (bekleyen onay durumu) üretip üretmediğini
   gözle.

**Beklenen sonuç**
- Run, `AwaitingApproval` durumuna düşer (kod-tanımlı `cancel_order`
  tool'unun `RequiresApproval=true` ile ürettiği davranışla BİREBİR
  aynı akış) — onay bayrağı, tool'un kaynağından (MCP mi kod mu)
  bağımsız olarak aynı mekanizmayı kullanır.

---

### MT-MCP-024 — `resources` yeteneği bildiren sunucuda sentetik `{sunucu}_read_resource` tool'u OTOMATİK belirir

Sınır senaryosu — Mode B (agent zamanında karar verir), Mode A'nın
(`AgentDefinition.McpResourceUris`, derleme/çalışma zamanında enjekte)
ALTERNATİFİDİR.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- Yerel test sunucusu `resources` yeteneğini bildiriyor (§5'in
  yapılandırmasına bağlı — sunucu bunu desteklemiyorsa bu case `⏭ ATLA`
  işaretlenir).

**Girilecek veri**
```bash
curl -s "$APU/api/tools" -H "$APB" | python3 -c "import json,sys; print([t['name'] for t in json.load(sys.stdin) if 'read_resource' in t['name']])"
```

**Beklenen sonuç**
- `test-sunucu_read_resource` adlı sentetik bir tool listede görünür —
  hiçbir kod veya sunucu tarafı `PUT` ile açıkça tanımlanmamıştır,
  keşif sırasında OTOMATİK üretilir.

### MT-MCP-026 — Yerel bir MCP sunucusu kur (tester-tedarikli altyapı)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Adımlar**
1. Genel amaçlı, yaygın bilinen bir MCP referans sunucusunu Streamable
   HTTP üzerinden başlat. Bu repo'nun DIŞINDA, tester tarafından seçilen
   bir araçtır — aşağıdaki komut yalnız bir ÖRNEKTİR, Tracon
   dokümanlarının bir parçası değildir:
   ```bash
   npx -y @modelcontextprotocol/server-everything --port 6060
   ```
2. Sunucunun `http://localhost:6060/mcp` üzerinde Streamable HTTP ile
   ayakta olduğunu doğrula.

**Beklenen sonuç**
- Sunucu ayakta; en az bir tool ve mümkünse bir `resources` yeteneği
  sunuyor (§4 MT-MCP-024 buna ihtiyaç duyar).
- Kullanılan gerçek komut/araç koşum notuna KAYDEDİLİR — sonraki bir
  oturum bu kararı tekrarlamak zorunda kalmasın diye.

---

### MT-MCP-027 — Yerel sunucuyu Tracon'e kaydet, tool keşfi gerçekleşir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MCP-026 tamamlandı.
- `test-sunucu` kaydı MT-MCP-001'de oluşturulmuş, `endpoint` gerçek
  çalışan sunucuyu gösteriyor.

**Adımlar**
1. `POST /api/mcp-servers/refresh` ile keşfi elle tetikle (5 dakika
   beklemek yerine).
2. `GET /api/tools` ile `test-sunucu_*` önekli tool'ların listede
   olduğunu doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/mcp-servers/refresh" -H "$APB"
curl -s "$APU/api/tools" -H "$APB" | python3 -c "import json,sys; print([t['name'] for t in json.load(sys.stdin) if t.get('source')=='test-sunucu'])"
```

**Beklenen sonuç**
- En az bir `test-sunucu_<ad>` tool'u listede.

---

### MT-MCP-028 — Keşfedilen tool'u GERÇEK bir agent çalıştırmasında kullan (uçtan uca)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MCP-027'de keşfedilen bir tool'u kullanan bir agent oluştur
  (`tools` listesine `test-sunucu_<ad>` ekle).

**Adımlar**
1. Agent'ı, o tool'u tetikleyecek bir mesajla çalıştır.
2. Run detayında tool çağrısının GERÇEKTEN gerçekleştiğini doğrula.

**Beklenen sonuç**
- `GET /api/runs/{id}/tree` içinde `test-sunucu_<ad>` adlı bir tool
  çağrısı görünür, gerçek MCP sunucusundan dönen bir sonuç taşır.

### MT-MCP-030 — Varsayılan KAPALI: boş beyaz liste + `ExposeAllAgents=false` → `tools/list` BOŞ döner

Negatif/sınır senaryosu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Beklenen sonuç (koşum, izlek C — `Tracon.Testing`)**
- `TraconTestHost` ile `TraconMcpServerOptions`'ı hiç
  yapılandırmadan (`ExposedAgents=[]`, `ExposeAllAgents=false`
  varsayılanlarıyla) `tools/list` çağrıldığında `tools: []` döner —
  hiçbir agent, açıkça izin verilmedikçe dışa açılmaz.
- Repo'nun kendi `Bos_beyaz_liste_hicbir_tool_dondurmez` testi
  (`tests/Tracon.AspNetCore.FunctionalTests/McpServerEndpointTests.cs:14`)
  bu davranışı zaten otomatik doğruluyor — bu case, üretim benzeri örnek
  uygulama üzerinde AYNI GARANTİYİ elle tekrar doğrular: örnek
  uygulamada yalnız `summarizer` beyaz listededir, başka HİÇBİR agent
  `tools/list`'te görünmez (bkz. MT-MCP-031).

---

### MT-MCP-031 — `summarizer` fixture: `tools/list` gerçek çıktısı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X POST "$APU/mcp" -H "$APB" -H "content-type: application/json" -H "accept: application/json, text/event-stream" \
     -d '{ "jsonrpc": "2.0", "id": 1, "method": "tools/list" }'
```

**Beklenen sonuç**
> **Düzeltildi (2026-09-17):** spec `tracon_ozetleyici` yazıyordu (Aşama
> 0'da zaten kapatılan bayat Türkçe agent adı örüntüsü — örnek uygulama
> İngilizceye çevrildi). Gerçek tool adı `tracon_summarizer`'dır, bu dosyada
> her geçtiği yerde düzeltildi (MT-MCP-032/033 dahil).
- Yanıt TAM OLARAK tek bir tool içerir: `tracon_summarizer`,
  `inputSchema` yalnız `message` (string, required) alanı taşır. Başka
  hiçbir agent (ör. `support`) listede YOKTUR — beyaz listeye
  eklenmemiştir.

---

### MT-MCP-032 — `tools/call` gerçek çıktı üretir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X POST "$APU/mcp" -H "$APB" -H "content-type: application/json" -H "accept: application/json, text/event-stream" \
     -d '{ "jsonrpc": "2.0", "id": 2, "method": "tools/call",
          "params": { "name": "tracon_summarizer", "arguments": { "message": "Bugun hava cok guzeldi. Is yerinde her sey yolunda gitti. Toplantilar verimliydi." } } }'
```

**Beklenen sonuç**
- `HTTP: 200`. Yanıt `result.content[0].text` alanında `summarizer`
  agent'ının ürettiği bir özet metni içerir. `GET /api/runs?agentName=summarizer`
  bu çağrıya karşılık gelen YENİ bir kök run (Depth=0) gösterir.

---

### MT-MCP-033 — `message` alanı BOŞ/EKSİKSE hata döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X POST "$APU/mcp" -H "$APB" -H "content-type: application/json" -H "accept: application/json, text/event-stream" \
     -d '{ "jsonrpc": "2.0", "id": 3, "method": "tools/call",
          "params": { "name": "tracon_summarizer", "arguments": { "message": "" } } }'
```

**Beklenen sonuç**
- Yanıt bir hata içerir (`isError: true` veya JSON-RPC hata nesnesi) —
  boş mesajla agent çalıştırılmaz.

---

### MT-MCP-034 — Onay gerektiren tool taşıyan bir agent'ı dışa açmaya çalışmak → UYGULAMA BAŞLAMAZ

Kritik negatif senaryo — güvenlik sınırı testi. **Geçici kod değişikliği
gerektirir, case sonunda geri alınır.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Adımlar**
1. `samples/Tracon.Api/Program.cs`'te GEÇİCİ olarak
   `.UseMcpServer(o => o.ExposedAgents.Add("summarizer"))` satırını
   `.UseMcpServer(o => o.ExposedAgents.Add("support"))` ile DEĞİŞTİR
   (`support`, `cancel_order` — `RequiresApproval=true` — tool'unu
   taşır).
2. `dotnet run` ile başlatmayı dene.

**Beklenen sonuç**
- Uygulama başlangıçta `LogCritical` seviyesinde bir hata loglar ve
  `IHostApplicationLifetime.StopApplication()` ile KENDİNİ KAPATIR —
  onay gerektiren bir tool asla dış bir MCP istemcisine sessizce
  sunulamaz.
- Case sonrası `Program.cs` değişikliği GERİ ALINIR (`git checkout --
  samples/Tracon.Api/Program.cs` veya elle geri yaz), uygulama
  normal haliyle yeniden başlatılır.

---

### MT-MCP-035 — Canlı katalog: yeni bir agent DB'ye eklenince MCP sunucusu YENİDEN BAŞLATILMADAN görünür

Sınır senaryosu — MCP'nin A2A'dan (§7 MT-MCP-044) FARKLI davrandığı nokta.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul (koşumda düzeltildi)**
- Orijinal yaklaşım (`summarizer`'yi `PUT` ile güncellemek) ÇALIŞMAZ:
  `summarizer` **kodda tanımlı** bir agent'tır (`origin: "Code"`,
  `isEditable: false`) — `07-HTTP-YONETIM-API.md`'nin MT-API-008 ile zaten
  kanıtladığı gibi, kodda tanımlı agent'lar yönetim API'sinden asla
  değiştirilemez (`409`). Üstelik case'in kendi `Girilecek veri`'si `name`/
  `model` alanlarını da eksik bırakıyor (`PUT` bunları zorunlu kılar).
- Bunun yerine GEÇİCİ olarak `Program.cs`'teki `.UseMcpServer(...)`
  çağrısına, henüz VAR OLMAYAN bir veritabanı-kökenli agent adı eklenir
  (`o.ExposedAgents.Add("manuel-canli-katalog");`) — uygulama başlarken bu
  ad kataloğa henüz eşleşmediği için `McpApprovalGuardFilter` bunu
  engellemez. Uygulama başladıktan SONRA bu isimde bir agent `POST` ile
  oluşturulur, sonra `PUT` ile `description`'ı değiştirilir — hepsi TEK bir
  yeniden başlatma içinde, restart olmadan.

**Girilecek veri (düzeltilmiş)**
```bash
curl -s -X POST "$APU/mcp" -H "$APB" -H "content-type: application/json" -H "accept: application/json, text/event-stream" \
     -d '{ "jsonrpc": "2.0", "id": 1, "method": "tools/list" }'
# -> yalniz summarizer gorunur

curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-canli-katalog",
  "description": "ILK aciklama",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
curl -s -X POST "$APU/mcp" -H "$APB" -H "content-type: application/json" -H "accept: application/json, text/event-stream" \
     -d '{ "jsonrpc": "2.0", "id": 2, "method": "tools/list" }'
# -> restart OLMADAN tracon_manuel-canli-katalog gorunur, description: "ILK aciklama"

curl -s -X PUT "$APU/api/agents/manuel-canli-katalog" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-canli-katalog",
  "description": "GUNCELLENMIS aciklama - canli katalog testi",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
curl -s -X POST "$APU/mcp" -H "$APB" -H "content-type: application/json" -H "accept: application/json, text/event-stream" \
     -d '{ "jsonrpc": "2.0", "id": 3, "method": "tools/list" }'
```

**Beklenen sonuç**
- Yanıttaki `description` alanı YENİ metni gösterir — `CatalogToolListHandler`
  her `tools/list` çağrısında `IAgentCatalog`'u CANLI okur, önbelleklenmiş
  bir kopya döndürmez.

---

### MT-MCP-036 — Gerçek bir MCP istemcisiyle (Claude Code CLI) uçtan uca el sıkışma

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Test makinesinde Claude Code CLI kurulu.

**Adımlar**
1. Tracon'in MCP sunucusunu CLI'ye ekle.
2. Bağlantı durumunu kontrol et.

**Girilecek veri (düzeltilmiş — Authorization başlığı eksikti)**
```bash
claude mcp add --transport http tracon-manuel-test http://localhost:5080/tracon/mcp \
     -s local --header "Authorization: Bearer manuel-test-token-2026"
claude mcp get tracon-manuel-test
```

**Beklenen sonuç**
- `Status: ✔ Connected`. Bu, Tracon'in MCP sunucu yüzeyinin
  spesifikasyona gerçekten uygun olduğunun (protokolün kendi bir
  istemcisiyle doğrulanmış) en güçlü kanıtıdır.
- Case sonrası `claude mcp remove tracon-manuel-test -s local`.

### MT-MCP-040 — Agent kartı `GET .well-known/agent-card.json` gerçek çıktısı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/a2a/summarizer/.well-known/agent-card.json" -H "$APB"
```

**Beklenen sonuç**
- Gövde `name: "summarizer"`, `capabilities: {streaming: false,
  pushNotifications: false}`, `defaultInputModes: ["text/plain"]`,
  `defaultOutputModes: ["text/plain"]`, `supportedInterfaces[0].url`
  agent'ın alt yoluna işaret eder, `protocolBinding: "JSONRPC"`.

---

### MT-MCP-041 — `SendMessage` JSON-RPC çağrısı — PascalCase metot adı, `ROLE_AGENT`/`ROLE_USER` (spec DIŞI biçim)

Sınır senaryosu — bilinen bir uyumsuzluk, kusur değil (SDK davranışı).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Girilecek veri (düzeltilmiş — `messageId` zorunlu, eksikti)**
```bash
curl -s -X POST "$APU/a2a/summarizer/" -H "$APB" -H "content-type: application/json" -d '{
  "jsonrpc": "2.0", "id": 1, "method": "SendMessage",
  "params": { "message": { "role": "ROLE_USER", "messageId": "manuel-a2a-041", "parts": [{ "text": "Bugun hava cok guzeldi. Is yerinde her sey yolunda gitti. Toplantilar verimliydi." }] } }
}'
```

**Beklenen sonuç**
- `HTTP: 200`. Yanıttaki metot A2A spesifikasyonunun `message/send`
  METODU DEĞİL, gerçek çağrının kendisi `"SendMessage"` (PascalCase)
  kullanır — bu SDK'nın (`A2A.AspNetCore`) kendi davranışıdır. Yanıttaki
  `role` alanı `"ROLE_AGENT"` biçimindedir (`"agent"` DEĞİL,
  protobuf-tarzı). Standart bir A2A istemcisi bu ikisini beklemeden
  yazılmışsa uyumsuzluk yaşayabilir — bu, koşum notuna kaydedilecek bir
  gözlemdir, bir Tracon kusuru değildir (bağımlı SDK'nın davranışı).

---

### MT-MCP-042 — Agent kartındaki `url` alanı GÖRECELİDİR, mutlak DEĞİL

Sınır senaryosu/gözlem.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/a2a/summarizer/.well-known/agent-card.json" -H "$APB" \
  | python3 -c "import json,sys; print(json.load(sys.stdin)['supportedInterfaces'][0]['url'])"
```

**Beklenen sonuç**
- Çıktı `/tracon/a2a/summarizer` gibi GÖRECELİ bir yoldur, `http://...`
  ile başlayan MUTLAK bir URL DEĞİLDİR. Bir ters vekil (reverse proxy)
  arkasındaki gerçek bir A2A istemcisi bu URL'yi kendisi tamamlamak
  zorunda kalabilir — bu bilinen bir sınırlamadır, kusur değildir.

---

### MT-MCP-043 — A2A'da `ExposeAllAgents` seçeneği HİÇ YOKTUR — yalnız kayıt-zamanı sabit liste

Negatif/sınır senaryosu — API yüzeyi karşılaştırması.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Adımlar**
1. `TraconA2AOptions` tipinin genel API yüzeyini incele (kod
   okuması, `src/Tracon.AspNetCore/A2A/TraconA2AOptions.cs`) veya
   dolaylı olarak: `summarizer` dışında herhangi bir agent'a A2A yoluyla
   erişmeyi dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/a2a/support/.well-known/agent-card.json" -H "$APB"
```

**Beklenen sonuç**
- `support` için `HTTP: 404` (veya eşdeğer "bulunamadı") — MCP'nin
  aksine (§6, `ExposeAllAgents` seçeneği var), A2A tarafında agent'ları
  toptan açan bir seçenek yoktur; yalnız `Program.cs`'te `UseA2A(...)`
  ANINDA açıkça listelenen agent'lar erişilebilir.

---

### MT-MCP-044 — Çalışma anında eklenen agent A2A'da GÖRÜNMEZ — MCP'nin TAM TERSİ davranış

Sınır senaryosu — MT-MCP-035 ile doğrudan karşılaştırmalı, ölçülmüş
asimetri.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Adımlar**
1. Uygulama ÇALIŞIRKEN yeni bir agent oluştur (`PUT /api/agents/yeni-a2a-adayi`).
2. Bu agent'ın adını, geçici olarak `Program.cs`'teki `UseA2A(o =>
   o.ExposedAgents.Add(...))` listesine EKLEMEDEN, A2A yoluyla erişmeyi
   dene.

**Girilecek veri (düzeltilmiş — `PUT` upsert değildir, `POST` ile oluşturulur)**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "yeni-a2a-adayi",
  "instructions": "Test.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
curl -s -w "\nHTTP: %{http_code}\n" "$APU/a2a/yeni-a2a-adayi/.well-known/agent-card.json" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404` — `AddA2AServer` KAYIT ZAMANLI bir API'dir
  (`Microsoft.Agents.AI.Hosting.A2A`), uygulama başladıktan sonra
  kataloğa eklenen bir agent'ı GÖREMEZ. Uygulamayı yeniden başlatmadan
  bu agent'ı A2A'ya açmanın hiçbir yolu yoktur — bu, ölçülmüş ve
  testlerle (`tests/Tracon.AspNetCore.FunctionalTests/A2AEndpointTests.cs`)
  kanıtlanmış, kasıtlı bir sınırlamadır.

---

### MT-MCP-045 — Onay gerektiren tool taşıyan bir agent'ı A2A'ya açmaya çalışmak → AYNI GUARD, UYGULAMA BAŞLAMAZ

Kritik negatif senaryo. **Geçici kod değişikliği gerektirir.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Adımlar**
1. `Program.cs`'te GEÇİCİ olarak `.UseA2A(o =>
   o.ExposedAgents.Add("summarizer"))` satırını `.UseA2A(o =>
   o.ExposedAgents.Add("support"))` ile DEĞİŞTİR.
2. `dotnet run` ile başlatmayı dene.

**Beklenen sonuç**
- MT-MCP-034 ile BİREBİR aynı sonuç: uygulama `LogCritical` loglar ve
  kendini kapatır — `A2AApprovalGuardFilter`, `McpApprovalGuardFilter`
  ile aynı deseni (arka plan görev + `SchemaReadyGate` + her isteğin bu
  görevi bekelemesi) izler.
- Case sonrası `Program.cs` değişikliği GERİ ALINIR.

### MT-MCP-047 — Tools ekranında MCP kökenli tool `mcp: {sunucu}` rozetiyle ayrışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Adımlar**
1. `/tracon/tools` ekranını aç.
2. `test-sunucu_*` önekli bir tool'a bak.

**Beklenen sonuç**
- Sarı/uyarı tonlu bir rozet `mcp: test-sunucu` metnini gösterir.
  Kod-tanımlı tool'larda (ör. `get_order_status`) bu rozet HİÇ YOKTUR.
  Ayrıca, `requiresApproval=true` ise ayrı bir onay rozeti de görünür —
  ikisi BAĞIMSIZDIR (bir kod tool'u da onay gerektirebilir).

---

### MT-MCP-048 — `mcp.tsx` formu: OAuth açılınca `authorizationConfigurationKey` alanı OTOMATİK TEMİZLENİR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Adımlar**
1. `/tracon/mcp` → "Yeni sunucu".
2. `authorizationConfigurationKey` alanına bir metin yaz.
3. "OAuth kullan" onay kutusunu işaretle.

**Beklenen sonuç**
- `authorizationConfigurationKey` alanı OTOMATİK boşalır (istemci tarafı)
  — sunucu tarafındaki karşılıklı dışlama kuralını (MT-MCP-005) form
  seviyesinde önceden yansıtır.

---

### MT-MCP-049 — `mcp.tsx` sunucu listesi tablosunda secret DEĞERİ hiç GÖRÜNMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | K-059 |

**Adımlar**
1. `/tracon/mcp` listesindeki "Yetki" sütununa bak.

**Beklenen sonuç**
- Sütun ya OAuth istemci kimliğini ya da yapılandırma ANAHTARI ADINI
  gösterir (`Tracon:McpSecrets:GithubToken` gibi) — asla gerçek bir token/
  şifre DEĞERİ göstermez. Bu, MT-MCP-007'nin API seviyesindeki kanıtının
  arayüz tarafındaki karşılığıdır.

### MT-MCP-050 — `/tracon/mcp` ve `/tracon/a2a` GRUP SEVİYESİNDE `ExternalInvoke` kapsamını doğru uygular (pozitif kontrol)

Bu, §9'un geri kalanının aksine bir POZİTİF doğrulamadır — dış yüzeyin
kendisi doğru korunuyor.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50, 53 |
| **İlgili karar** | — |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md`'nin API anahtarı oluşturma deseni.

**Adımlar**
1. `ExternalInvoke` kapsamı OLMAYAN (ör. yalnız `RunsRead`) bir API
   anahtarı üret.
2. Bu anahtarla `/tracon/mcp`'ye `tools/list` gönder.
3. Kontrol: `ExternalInvoke` kapsamlı ikinci bir anahtar üret, aynı
   çağrının BAŞARILI olduğunu doğrula.

**Girilecek veri**
```bash
KEY_JSON=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{ "name": "mcp-kapsam-testi-yetersiz", "scopes": ["RunsRead"] }')
RAWKEY=$(echo "$KEY_JSON" | python3 -c "import json,sys; print(json.load(sys.stdin)['rawKey'])")

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/mcp" -H "Authorization: Bearer $RAWKEY" \
     -H "content-type: application/json" -H "accept: application/json, text/event-stream" \
     -d '{ "jsonrpc": "2.0", "id": 1, "method": "tools/list" }'
```

**Beklenen sonuç**
- `ExternalInvoke` kapsamı OLMAYAN anahtarla çağrı `HTTP: 403` döner —
  dış yüzeyin kendisi API-anahtarı kapsam sistemini DOĞRU uygular. (Statik
  paylaşılan bearer token'ın bu denetimden muaf olduğunu unutma — bu case
  yalnız veritabanı-destekli API anahtarları için geçerlidir.)

---

### MT-MCP-051 — `GovernanceEndpoints` (`/api/mcp-servers/*`) API ANAHTARI KAPSAMI HİÇ ÇAĞIRMAZ

🚨 Şüpheli davranış — kod okumasıyla ölçüldü, koşumda doğrulanır. Bu,
`WorkflowEndpoints`/`SchedulingEndpoints` için önceden ölçülen kalıbın
(bkz. `00-INDEKS.md` §8) **BEŞİNCİ** bağımsız tekrarıdır — MCP sunucu
**kayıt** API'si (`GET/PUT/DELETE /api/mcp-servers[/{name}]`, `POST
/api/mcp-servers/refresh`, `GET/POST /api/mcp-servers/{name}/prompts[/{prompt}]`,
`GET /api/mcp-servers/{name}/resources[/read]`, `POST
/api/mcp-servers/{name}/oauth/start` — toplam 15 uç eşlemesi) hiçbir
`RequireApiKeyScope(...)` çağrısı TAŞIMAZ.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22, 53 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MCP-050'deki `RunsRead`-kapsamlı (`ExternalInvoke`'suz) anahtar.

**Adımlar**
1. Aynı anahtarla YENİ bir MCP sunucusu KAYDETMEYİ dene.
2. Kontrol grubu: MT-MCP-050'nin doğrudan `/mcp` dış yüzeyi çağrısını
   (`403` beklenir) tekrar karşılaştır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/mcp-servers/kapsam-testi" -H "Authorization: Bearer $RAWKEY" \
     -H "content-type: application/json" -d '{ "endpoint": "http://localhost:6070/mcp" }'
```

**Beklenen sonuç (şüphe)**
> **Düzeltildi (2026-09-17) — şüphe ÇÜRÜTÜLDÜ, kod 2026-08-10 ile
> 2026-09-16 arasında düzeltilmiş:** `GovernanceEndpoints.cs`'teki `PUT
> /api/mcp-servers/{name}` artık `.RequireRole(roles.Admin)` VE
> `.RequireApiKeyScope(ApiKeyScope.AgentsAdmin)` taşıyor (satır ~269-270).
> Bu dosyanın TÜM `mcp-servers` uçları (9 `Map*` çağrısı) aynı desende
> `RequireApiKeyScope` taşıyor. `00-INDEKS.md` §8'deki "BEŞİNCİ bağımsız
> tekrar" notu da düzeltildi.
- ~~`HTTP: 200` — yalnız `RunsRead` taşıyan, `ExternalInvoke`'u OLMAYAN bir
  anahtar YENİ bir dış MCP sunucusu kaydedebilir.~~ **Gerçek:** `HTTP:
  403 "This endpoint requires the 'AgentsAdmin' scope; the key does not
  carry it."`
- 🚨 **Ayrı ve HÂLÂ açık kalan boşluk (bu case'in KAPSAMADIĞI):**
  `RequireApiKeyScope` yalnız DB-destekli API anahtarlarına uygulanır —
  STATİK paylaşılan bearer token bu denetimin tamamen dışındadır. Bkz.
  `MT-MCP-052`.

---

### MT-MCP-052 — Varsayılan örnek uygulamada: statik bearer token sahibi HERKES dış MCP sunucusu kaydedebilir

Somut, uçtan uca kanıt — MT-MCP-051'in rol katmanı boyutu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama `TraconPolicies.*` rol politikalarını HİÇ kaydetmez
  (`00-INDEKS.md` §8'de zaten ölçülmüş) — bu durumda
  `RequireRole(roles.Admin)` de no-op'tur.

**Adımlar**
1. `FIX-TOKEN-01` (`manuel-test-token-2026` — aynı token, salt-okunur run
   incelemesi için de kullanılan token) ile bir MCP sunucusu kaydet.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/mcp-servers/token-kaniti" -H "$APB" \
     -H "content-type: application/json" -d '{ "endpoint": "http://saldiran-sunucu.ornek/mcp" }'
```

**Beklenen sonuç**
- `HTTP: 200` — bu güvenlik sınırını korumak için ne `RequireRole`
  (no-op, rol politikaları kayıtlı değil) ne de `RequireApiKeyScope`
  (hiç çağrılmıyor, MT-MCP-051) devrededir. Bu ortamda, `run`'ları
  okumak için verilen SIRADAN bir bearer token, agent'ların erişebileceği
  KEYFİ bir dış sunucuyu (potansiyel olarak kötü niyetli tool'lar
  sunan) sisteme ekleyebilir.
- Case sonrası `DELETE /api/mcp-servers/token-kaniti` ile temizle.

---

### MT-MCP-053 — `AllowRemoteAccess=true` + `ExternalInvoke` kapsamlı anahtar YOKKEN → UYGULAMA BAŞLAMAZ (bağımsız koruma)

Pozitif kontrol — MT-MCP-052'nin gösterdiği boşluğa rağmen, ayrı bir
başlangıç koruması hâlâ vardır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Sistemde `ExternalInvoke` kapsamlı HİÇBİR API anahtarı yok (varsayılan
  temiz durum).

**Adımlar**
1. Uygulamayı `--Tracon:Ui:AllowRemoteAccess=true` ile başlatmayı dene
   (loopback dışı erişimi açar). **`Program.cs` değişikliği GEREKMEZ** —
   örnek uygulama bu anahtarı seçeneğe bağlıyor
   (`samples/Tracon.Api/Program.cs:953-956`); ölçüldü 2026-09-19.
2. Karşı kontrol: `ExternalInvoke` kapsamlı bir anahtar yarat
   (`POST /api/api-keys`) ve **aynı** komutu tekrar koş.

**Beklenen sonuç**
- Uygulama başlangıçta `InvalidOperationException` fırlatır —
  `ExternalSurfaceGuard.EnsureRemoteAccessNotCombined`, "loopback dışına
  aç" ile "yalnız tek bir statik token'la koru"nun AYNI ANDA
  olamayacağını zorlar. Bu, MT-MCP-052'nin gösterdiği boşluğun bilinçli
  olarak dar tutulduğunun (yalnız loopback'te izin verilir) kanıtıdır.
- **Karşı kontrol:** `ExternalInvoke` kapsamlı bir anahtar varken aynı komut
  temiz başlar (`health=200`, sıfır `InvalidOperationException`). Kapı bayrağa
  değil EKSİK ANAHTARA bakar; bu ikinci ölçüm olmadan case yalnız "bayrak
  uygulamayı çökertiyor" der.
- Geri alma: yalnız bayrağı kaldır — kalıcı bir değişiklik yapılmaz.

---

### MT-MCP-054 — MCP `endpoint`'i metadata adresine işaret ediyor: reddedilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 77 |
| **İlgili karar** | K-529 · K-530 |

**Ön koşul** Varsayılan ayarlar (`Tracon:Egress:AllowPrivateNetworkTargets`
tanımlı DEĞİL). Örnek uygulama ayakta.

**Adımlar**
```bash
curl -s -X PUT "$APU/api/mcp-servers/probe" -H "$APB" \
  -H 'Content-Type: application/json' \
  -d '{"endpoint":"http://169.254.169.254/","transport":"StreamableHttp"}' \
  | jq -r '.detail'
```

**Beklenen sonuç**
- `400`. `detail`: `The target resolves to a private network address
  (169.254.169.254); set 'Tracon:Egress:AllowPrivateNetworkTargets' to true
  to allow it.`
- Aynısı `http://10.0.0.5:8080/mcp`, `https://192.168.1.10/mcp`,
  `http://127.0.0.1:9000/mcp` ve NAT64 biçimi
  `http://[64:ff9b::a9fe:a9fe]/mcp` için de geçerlidir.
- **Ad** taşıyan bir adres (`https://mcp.example.com/`) `200` döner: kaydetme
  anında DNS çözülmez. O adresin özel bir IP'ye çözülmesi hâlinde bağlantı,
  kurulduğu anda `EgressSocketGuard` tarafından reddedilir.

---

### MT-MCP-055 — Ayar açıkken aynı kayıt kabul edilir (yükseltme yolu)

| | |
|---|---|
| **İzlek** | A (izole — uygulama yeniden başlatılır) |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 77 |
| **İlgili karar** | K-530 |

**Ön koşul** Uygulamayı `Tracon__Egress__AllowPrivateNetworkTargets=true`
ortam değişkeniyle başlat.

**Adımlar** MT-MCP-054'ün ilk komutunu tekrarla.

**Beklenen sonuç**
- `200`; kayıt oluşur. İç ağında MCP sunucusu çalıştıran bir kurulumun
  yükseltme yolu budur ve **tek satırdır**.
- 🚨 Ayar `Tracon` bölümünün altındadır (`Tracon:Egress:...`). Bu case
  aynı zamanda ayarın gerçekten **bağlandığını** ölçer — tanımlı ama okunmayan
  bir ayar sınıfı bu repoda daha önce yaşandı (K-406).

---

### MT-MCP-056 — MCP yapılandırma anahtarı önek dışında: reddedilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 77 |
| **İlgili karar** | K-533 · K-534 |

**Ön koşul** Örnek uygulama ayakta.

**Adımlar**
```bash
curl -s -X PUT "$APU/api/mcp-servers/probe" -H "$APB" \
  -H 'Content-Type: application/json' \
  -d '{"endpoint":"https://mcp.example.com/","transport":"StreamableHttp",
       "authorizationConfigurationKey":"ConnectionStrings:Default"}' | jq -r '.detail'
```

**Beklenen sonuç**
- `400`. `detail`: `'ConnectionStrings:Default' is outside the allowed prefix.
  'authorizationConfigurationKey' may only reference a configuration key under
  'Tracon:McpSecrets:'.`
- `authorizationConfigurationKey` yerine `oauthClientSecretConfigurationKey`
  kullanıldığında (OAuth açıkken) aynı red, alan adı değişerek gelir.
- `Tracon:McpSecrets:Token` ile aynı istek `200` döner.

---

### MT-MCP-057 — Faz öncesi öneksiz kayıt: okunur ama yeniden kaydedilemez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 77 |
| **İlgili karar** | K-533 |

**Ön koşul** Veritabanında `authorization_configuration_key` sütunu
`Tracon:Mcp:LegacyToken` (önek DIŞI) olan bir MCP kaydı. Faz 77 öncesi
kaydedilmiş bir kurulumda bu kendiliğinden vardır; yoksa SQL ile yazılır.

**Adımlar**
1. `curl -s "$APU/api/mcp-servers" -H "$APB" | jq` — listeyi oku.
2. Arayüzden kaydı aç, **hiçbir şey değiştirmeden** kaydet.

**Beklenen sonuç**
- Adım 1 `200` döner ve kayıt listede görünür: **okuma etkilenmez.** Operatör
  neyi düzelteceğini görebilmelidir.
- Adım 2 `400` döner ve mesaj hem alan adını (`authorizationConfigurationKey`)
  hem izinli öneki (`Tracon:McpSecrets:`) yazar.
- Düzeltme elle yapılır: anahtar adı öneke taşınır ve `dotnet user-secrets`
  içindeki değer yeni adla yazılır. Taşıma yardımcısı **yoktur** ve bilinçlidir
  (kayıt başına tek alan; değer değil **ad** taşınır).

---

### MT-MCP-058 — 👤 İç ağdaki gerçek MCP sunucusu: önce red, ayar sonrası bağlantı

| | |
|---|---|
| **İzlek** | A (izole — insan gerekir, gerçek iç ağ) |
| **Önem** | Orta |
| **İlgili faz** | Faz 77 |
| **İlgili karar** | K-529 · K-530 |

**Ön koşul** İç ağda gerçekten erişilebilir, çalışan bir MCP sunucusu (örn.
`http://10.0.0.12:3000/mcp`) ve varsayılan ayarlar.

**Adımlar**
1. Sunucuyu kaydetmeyi dene.
2. `Tracon__Egress__AllowPrivateNetworkTargets=true` ile yeniden başlat, tekrar kaydet.
3. Tool listesinin tazelenmesini bekle ve `GET $APU/api/tools` ile tool'ları gör.

**Beklenen sonuç**
- Adım 1'de `400`.
- Adım 2'de `200`.
- Adım 3'te sunucunun tool'ları listelenir — yani muhafız yalnız kaydetmeyi
  değil, **gerçek bağlantıyı** da geçirir. Bu, `ConnectCallback` içindeki
  denetimin çalışan bir bağlantıyı yanlışlıkla kesmediğini kanıtlayan tek case'tir.

---

### MT-MCP-059 — 🚨 👤 MCP tool'undan gelen büyük çıktı da kurulum varsayılanıyla kırpılır

### MT-MCP-060 — MCP agent-tool sonucu provider hatasının ham metnini sızdırmaz (Faz 103)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |

**Adımlar**
1. Secret-like mesajla başarısız olan bir provider'a bağlı agent'ı MCP tool olarak aç.
2. `tools/call` ile çağır.

**Beklenen sonuç**
- Sonuç generic, safe bir hata metni taşır; secret-like metin görünmez.
- Otomatikleştirildi: `ProviderOutageErrorHandlingTests.Mcp_tool_call_never_exposes_a_secret_like_provider_message`.


| | |
|---|---|
| **İzlek** | A (izole — insan gerekir, `tester-tedarikli` MCP sunucusu) |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 89 |
| **İlgili karar** | — |

**Ön koşul**
Büyük metin döndüren en az bir tool taşıyan bir MCP sunucusu kayıtlı (bkz. §5 —
repo'da dokümante edilmiş bir yerel MCP sunucusu yoktur, tester kendi
sunucusunu getirir). Uygulama küçük bir kurulum varsayılanıyla başlatılmış:
```bash
export Tracon__Tools__DefaultMaxOutputBytes=200
```

**Adımlar**
1. MCP sunucusundaki büyük çıktılı tool'u bir agent'a bağla, agent'ı çalıştırarak
   tool'u çağırt.
2. `run_events`'i oku:
```bash
curl -s "$BASE/api/runs/$RUN_ID/events" -H "$APB" \
  | jq '.[] | select(.type=="ToolOutputTruncated")'
```

**Beklenen sonuç**
- Modele giden `functionResult` içeriği `{"truncated":true,"omittedBytes":N,"content":"..."}`
  zarfıdır ve toplam boyutu 200 baytı aşmaz — kod-tanımlı bir tool'la
  **aynı davranış**, MCP tool'ları kırpmanın **ikinci, ayrı** sarmalama
  zincirinden (`McpTenantTools.Create`) geçtiği için bu ayrı case gerekir.
- `run_events`'te bir `ToolOutputTruncated` satırı vardır; `toolName` MCP
  sunucusundaki tool'un adıyla eşleşir.
- 🚨 Bu case atlanırsa ve MCP zincirine yeni bir sarmalayıcı eklenmesi
  unutulursa, MCP tool'ları sınırsız kalır ve bu sessizce fark edilmez —
  kod-tanımlı tool'ların case'i (MT-OBS-048) bu boşluğu KANITLAMAZ.

### MT-MCP-061 — `EnableTasks=false` (varsayılan): task-aware istemciyle bile davranış senkron kalır (Faz 117)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |

**Adımlar**
1. `EnableTasks` ayarlanmadan (varsayılan `false`) bir agent'ı MCP tool olarak aç.
2. Tasks capability'sini beyan eden, 2026-07-28 protokolünü tercih eden gerçek bir istemciyle (`CallToolAsTaskAsync`) çağır.

**Beklenen sonuç**
- `IsTask=false`; sonuç doğrudan döner, hiçbir `TaskId` üretilmez.
- Otomatikleştirildi: `McpTasksEndpointTests.EnableTasks_false_still_answers_synchronously`.

### MT-MCP-062 — `EnableTasks=true`: task id run kimliğine eşit, `tasks/get` Working→Completed geçişini doğru izler (Faz 117)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |

**Adımlar**
1. `EnableTasks=true` ile bir agent'ı MCP tool olarak aç, `CallToolAsTaskAsync` ile çağır.
2. Dönen `TaskId`'yi `GET /api/runs/{id}` ile sorgula.
3. `tasks/get`'i terminal duruma kadar poll et.

**Beklenen sonuç**
- `TaskId`, `/api/runs/{TaskId}` altında GÖRÜNEN gerçek bir run kimliğidir; `agentName` doğru.
- `tasks/get` önce `Working`, sonra `Completed` döner; sonuç metni gerçek agent çıktısını taşır.
- Otomatikleştirildi: `McpTasksEndpointTests.EnableTasks_true_creates_a_task_whose_id_is_the_run_id`, `Tasks_get_transitions_from_working_to_completed_with_the_run_output`. Gerçek `samples/Tracon.Api` koşumu: bkz. faz dokümanı § "Gerçek sunucu koşumu".

### MT-MCP-063 — `tasks/cancel`: koşan bir task'ı GERÇEKTEN iptal eder, kuyruklu (henüz başlamamış) task'ı doğrudan kapatır (Faz 117)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |

**Adımlar**
1. Gerçekten uzun süren (engelleyen) bir tool taşıyan agent'ı task modunda çağır; run'ın `Running`'e geçtiğini doğrula.
2. `tasks/cancel` gönder.
3. Ayrıca: henüz `Queued` durumdaki (arka plan yürütmesi başlamamış) bir task için de `tasks/cancel` dene.

**Beklenen sonuç**
- Koşan task: `tasks/get` `Cancelled` döner; `GET /api/runs/{id}` `Canceled` gösterir.
- Kuyruklu task: aynı sonuç, doğrudan kapatma yoluyla.
- İkinci bir iptal modeli yoktur — `RunEndpoints.CancelRunAsync`'in izdüşümüdür.
- Otomatikleştirildi: `McpTasksEndpointTests.Tasks_cancel_of_a_running_task_actually_cancels_the_in_flight_tool_call` (gerçek engelleyen tool + `SemaphoreSlim`, `Task.Delay` YOK), `Tasks_cancel_of_a_queued_task_closes_the_row_directly`.

### MT-MCP-064 — 🚨 Onay isteyen tool sonradan eklenmiş agent, task modunda `InputRequired` DEĞİL `Completed`(hata) döner (Faz 117, K-103)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili karar** | K-103, K-637 |

**Ön koşul**
Onay gerektiren bir tool kodda kayıtlı; agent önce bu tool OLMADAN dışa açık, sonra `PUT /api/agents/{name}` ile tool eklenir (dinamik katalog).

**Adımlar**
1. Agent'ı task modunda, onaylı tool'u tetikleyecek bir mesajla çağır.
2. `tasks/get`'i terminal duruma kadar poll et.

**Beklenen sonuç**
- Task **hiçbir zaman** `InputRequired` görünmez.
- Aynı örnekte poll ediliyorsa: `Completed`, mesaj bugünkü senkron yoldaki inline metnin AYNISI (tool adını taşır).
- FARKLI bir örnekte poll ediliyorsa: `Completed`, GENEL bir ret metni (tool adı YOK — `IRunStore`'dan kurtarılamaz, bilinçli fark).
- Otomatikleştirildi: `McpTasksEndpointTests.Approval_requiring_tool_added_after_exposure_rejects_the_task_with_todays_inline_message` (aynı-örnek), `McpTaskCrossInstanceTests.Second_instance_reconstructs_an_approval_rejection_generically_not_with_todays_exact_wording` (çapraz-örnek).

### MT-MCP-065 — Başka kiracının task id'si okunamaz; iptal edebilir ama ASLA okuyamaz (Faz 117)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili karar** | K-637 |

**Adımlar**
1. Kiracı A olarak bir task oluştur.
2. Kiracı B olarak AYNI task id ile `tasks/get` çağır.
3. Kiracı B olarak AYNI task id ile `tasks/cancel` çağır, sonra tekrar A olarak `tasks/get` çağır.

**Beklenen sonuç**
- Adım 2: "Unknown task" (kiracı A'nın verisi hiçbir koşulda B'ye sızmaz).
- Adım 3: 🚨 SDK'nin kendi `tasks/cancel`'ı KOŞULSUZ ack döner VE run'ı GERÇEKTEN iptal edebilir (kapatılamayan bir SDK sınırı, K-637) — ama kiracı A'nın kendi görünümü hâlâ DOĞRU ve OKUNABİLİR kalır (asla `Working` durumunda takılı kalmaz).
- Otomatikleştirildi: `McpTaskTenantIsolationTests` (2 test).

### MT-MCP-066 — İki Tracon örneği, tek veritabanı: task ikinci örnekten okunur (Faz 117)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |

**Adımlar**
1. Örnek A'da (SQLite/PostgreSQL, paylaşılan dosya/sunucu) bir task oluştur ve tamamlanmasını bekle.
2. Örnek B'yi AYNI veritabanına bağla; AYNI task id ile `tasks/get` çağır.

**Beklenen sonuç**
- Örnek B, hiç görmediği bir task'ı `IRunStore`'dan doğru şekilde yeniden inşa eder — bellek içi bir önbelleğe bağlı değildir.
- Otomatikleştirildi: `McpTaskCrossInstanceTests.Second_instance_reconstructs_a_completed_task_from_the_shared_database` — plan bunu 👤 elle koşulacak bir case sayıyordu, iki gerçek `TraconTestHost` + tek SQLite dosyasıyla otomatikleştirildi.

### MT-MCP-067 — 👤 `TaskTimeToLive` dolunca `tasks/get` hâlâ okunur (Faz 117, Açık Soru 2)

| | |
|---|---|
| **İzlek** | A (izole — insan gerekir, gerçek zaman aralığı) |
| **Önem** | Düşük |

**Ön koşul**
`TaskTimeToLive` kısa bir değere ayarlanmış (`TimeSpan.FromSeconds(5)` gibi).

**Adımlar**
1. Bir task oluştur, tamamlanmasını bekle.
2. `TaskTimeToLive`'dan uzun süre bekle (TTL'in üstünde).
3. `tasks/get` çağır.

**Beklenen sonuç**
- Task hâlâ okunur, doğru terminal durumu ve sonucu taşır. `TaskTimeToLive` yalnız istemciye tavsiyedir; run kaydının ömrünü saklama politikası (Faz 25) yönetir, MCP ikinci bir silme takvimi AÇMAZ (Açık Soru 2, Seçenek A).
- Not: `RunBackedMcpTaskStore`'un KENDİ bellek-içi önbelleği TTL'den sonra opportunistic olarak tahliye edilir (30 sn'de bir taranan bir sweep) — bu, yalnız aynı-örnek hızlı yolu etkiler, yukarıdaki davranışı DEĞİŞTİRMEZ (yeniden inşa yoluna düşer).

---

### MT-MCP-068 — Kayıtlı `IToolArgumentsValidator` MCP tool'unu da görür (Faz 127)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 127 |
| **İlgili karar** | — |

Kod tool'unun karşılığı `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` §9, MT-GUARD-080'dedir.
Bu case aynı doğrulama halkasının **MCP tarafını** kapatır — halka
`ToolWrapperChain.Compose` üzerinden tek noktadan kurulur (docs/127, 127.1);
öncesinde MCP tool'ları için hiçbir argüman denetimi yoktu (yalnız bağlamanın
yan etkisi vardı).

**Ön koşul**
- Bir MCP sunucusundan discover edilmiş en az bir tool kayıtlı (bkz. §5).
- Her çağrıyı reddeden bir `IToolArgumentsValidator` DI'a eklenmiş.

**Adımlar**
1. Reddeden doğrulayıcı kayıtlıyken MCP tool'unu bir agent üzerinden çağır.
2. Çalıştırma olaylarını oku.

**Beklenen sonuç**
- Kod tool'u ile BİREBİR aynı davranış: `run` `Completed` biter, modelin
  gördüğü sonuç doğrulayıcının güvenli metnidir, argüman değeri hiçbir
  yere yazılmaz, bir `ToolFailed` olayı vardır.
- 🚨 Bu case atlanırsa ve `ToolWrapperChain.Compose` yerine MCP tarafı kendi
  elle yazılmış bir zincire geri dönerse, MCP tool'ları argüman kapısını
  sessizce kaybeder — kod tool'unun case'i (MT-GUARD-080) bu boşluğu
  KANITLAMAZ.

> **Otomatik karşılığı:** `ToolWrapperChainTests.A_code_defined_registration_and_an_mcp_style_registration_produce_the_same_wrapper_layers`
> ve `A_real_validator_installs_the_validating_layer_between_timeout_and_authorizing`
> (`tests/Tracon.Core.UnitTests/Tools/ToolWrapperChainTests.cs`) iki çağrı
> yolunun aynı zinciri kurduğunu birim seviyesinde doğrudan ölçer. ⬜ Gerçek
> bir MCP sunucusuna karşı elle koşulmadı — `18-MCP-VE-A2A.md`'nin genelinde
> §5'in kendi notu geçerlidir: repo'da dokümante edilmiş bir yerel MCP
> sunucusu yoktur, tester kendi sunucusunu getirir.

---

### MT-MCP-069 — `headers` değerleri hiçbir yanıtta ve audit'te görünmez; `"***"` geri yazılamaz

Regresyon senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Kusur giderme (2026-09-23, d-mask) |
| **İlgili karar** | — (K-059 korunur) |
| **İnsan gerekir** | Hayır |
| **Devir** | koşulmadı; ➜ CI: `GovernanceEndpointTests.Mcp_header_values_are_masked_in_every_response_and_kept_in_the_store` · `…Mcp_audit_trail_records_header_names_but_no_values` · `…Mcp_save_that_sends_the_mask_back_is_rejected` · `WebhookEndpointTests.Header_values_are_masked_in_every_response_and_kept_in_the_store` · `…Save_that_sends_the_mask_back_is_rejected` |

Düzeltme öncesinde `GET /api/mcp-servers` her başlık değerini düz döndürüyordu.
Liste `Reader` rolüne ve `AgentsRead` scope'una açıktır. Değer bir API key ise
okuyan taraf MCP sunucusunu onay kapısının dışından doğrudan çağırabilirdi.
Audit, `Cookie` ve `Ocp-Apim-Subscription-Key` gibi adların değerini düz
yazıyordu.

**Adımlar**

> Faz 190'dan beri kimlik benzeri ad (`Ocp-Apim-Subscription-Key`, `X-Api-Key`,
> `Cookie`) düz `headers`'ta `400` alır. Maske artık ad kuralının **kaçırdığı**
> adlar içindir; case bu yüzden `X-Session-Id` (bilinen kalıntı) kullanır.

```bash
curl -s -X PUT "$APU/api/mcp-servers/manuel-maske" -H "$APB" \
  -H 'Content-Type: application/json' -d '{
  "endpoint": "https://ornek.invalid/mcp",
  "headers": { "X-Session-Id": "gizli-deger-1", "X-Trace": "duz-deger" }
}' | jq '.headers'
curl -s "$APU/api/mcp-servers" -H "$APB" | jq '.[] | select(.name=="manuel-maske") | .headers'
curl -s "$APU/api/audit/mcp:manuel-maske" -H "$APB" | grep -c 'gizli-deger-1'
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$APU/api/mcp-servers/manuel-maske" -H "$APB" \
  -H 'Content-Type: application/json' -d '{
  "endpoint": "https://ornek.invalid/mcp",
  "headers": { "X-Session-Id": "***" }
}'
```

**Beklenen sonuç**
- İlk iki istek aynı sözlüğü döner:
  `{"X-Session-Id":"***","X-Trace":"***"}`. Maske ada bakmaz;
  sıradan `X-Trace` değeri de gizlenir.
- Audit sorgusu `0` yazar. Kayıt başlık **adını** taşır, değeri taşımaz.
- Son istek `400` döner. `detail`, `X-Session-Id` adını ve
  gerçek değerin yeniden gönderilmesi gerektiğini söyler. Kayıt değişmez.
- Webhook ek başlıkları aynı kuralı izler: `GET /api/webhooks`,
  `GET /api/webhooks/{name}` ve `PUT` yanıtı her değeri `"***"` döner;
  `"***"` değerli `PUT` `400` alır.
- Faz 190'dan beri arayüz formunun `headers`'sız `PUT`'u saklı başlıkları
  **korur** (`null` → saklı değer, `{}` → siler); ölçüm MT-MCP-075.

---

## Faz 190 — Kimlik başlıkları anahtar adıyla

> Ortak kurulum (case 070–077): başlık yakalayıcı + örnek uygulama. Değerler
> yalnız ortam değişkenindedir.
>
> ```bash
> # 1) Yakalayıcı (ayrı terminal): her isteğin X-API-Key değerini yazar
> python3 -c 'import http.server as h
> class H(h.BaseHTTPRequestHandler):
>     def do_POST(s):
>         print(s.path, s.headers.get("X-API-Key"), flush=True); s.send_response(200); s.end_headers()
> h.HTTPServer(("127.0.0.1", 9099), H).serve_forever()'
> # 2) Örnek uygulama
> Tracon__McpSecrets__DemoKey=dogrulama-degeri Tracon__WebhookSecrets__DemoKey=dogrulama-degeri \
> Tracon__Egress__AllowPrivateNetworkTargets=true Tracon__Webhooks__AllowInsecureHttp=true \
> Tracon__Sqlite__ConnectionString="Data Source=$TMPDIR/faz190.db" dotnet run --project samples/Tracon.Api
> export M="$APU/api/mcp-servers/m1" J='content-type: application/json'
> ```

### MT-MCP-070 — Düz `headers`'ta kimlik benzeri ad `400` alır; değer yankılanmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 190 |
| **İlgili karar** | K-868 |
| **İnsan gerekir** | Hayır |
| **Devir** | ✅ 2026-09-24 (örnek uygulama, SQLite) · ➜ CI: `CredentialHeaderSaveTests.Mcp_plain_credential_header_is_rejected_without_echoing_the_value` |

**Ön koşul** — Temiz durum.

**Adımlar**
```bash
curl -s -w '\n%{http_code}\n' -X PUT "$M" -H "$APB" -H "$J" \
  -d '{"endpoint":"https://mcp.example.com/mcp","headers":{"X-API-Key":"v"}}'
for h in '{"Cookie":"a=b"}' '{"Ocp-Apim-Subscription-Key":"x"}'; do
  curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$M" -H "$APB" -H "$J" \
    -d "{\"endpoint\":\"https://mcp.example.com/mcp\",\"headers\":$h}"
done
```

**Beklenen sonuç**
- Üç istek `400`. İlk gövdenin `title`'ı `Credential header stored in the clear`;
  `detail` `X-API-Key` ve `headerConfigurationKeys` adlarını taşır, örnek anahtar
  adı `Tracon:McpSecrets:<KeyName>`'dir. `"v"` değeri gövdede YOKTUR.
- Ölçülen (2026-09-24): üçü `400`; gövde beklenen metni taşıdı.

### MT-MCP-071 — `headerConfigurationKeys` değeri bağlantıda çözülür, hiçbir yanıtta görünmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 190 |
| **İlgili karar** | K-059 · K-868 |
| **İnsan gerekir** | Hayır |
| **Devir** | ✅ 2026-09-24 · ➜ CI: `CredentialHeaderDeliveryTests.Mcp_connection_sends_the_resolved_header_and_no_response_returns_it` |

**Ön koşul** — Ortak kurulum.

**Adımlar**
```bash
curl -s -X PUT "$M" -H "$APB" -H "$J" \
  -d '{"endpoint":"http://127.0.0.1:9099/mcp","headerConfigurationKeys":{"X-API-Key":"Tracon:McpSecrets:DemoKey"}}'
curl -s -X POST "$APU/api/mcp-servers/refresh" -H "$APB"
curl -s "$APU/api/mcp-servers" -H "$APB" | grep -c dogrulama-degeri
```

**Beklenen sonuç**
- `PUT` `200`; yanıt `"headerConfigurationKeys":{"X-API-Key":"Tracon:McpSecrets:DemoKey"}`
  taşır (maskesiz: değer değil ad).
- Yakalayıcı `/mcp dogrulama-degeri` yazar. Yakalayıcı gerçek bir MCP sunucusu
  olmadığı için bağlantı sonra "could not connect" logu ile düşer — beklenen.
- Son komut `0`. Uygulama logunda `dogrulama-degeri` geçmez.

### MT-MCP-072 — Kiracı dışı anahtar adı kaydedilmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 190 |
| **İlgili karar** | K-852 · K-868 |
| **İnsan gerekir** | Hayır |
| **Devir** | ✅ 2026-09-24 (tek kiracılı kurulum, `default` → `globex`) · ➜ CI: `CredentialHeaderDeliveryTests.Mcp_key_outside_the_tenant_is_never_resolved` |

**Ön koşul** — Temiz durum. Çok kiracılı kurulumda çağıran `acme` olabilir;
tek kiracılı kurulumda çağıran `default`'tur.

**Adımlar**
```bash
curl -s -w '\n%{http_code}\n' -X PUT "$APU/api/mcp-servers/m3" -H "$APB" -H "$J" \
  -d '{"endpoint":"https://mcp.example.com/mcp","headerConfigurationKeys":{"X-API-Key":"Tracon:McpSecrets:globex:Key"}}'
```

**Beklenen sonuç**
- `400`. `detail` `headerConfigurationKeys[X-API-Key]` alanını ve çağıranın
  anahtar alanını (`Tracon:McpSecrets:default:` veya düz ad) adlandırır.

### MT-MCP-073 — `Authorization` iki alandan veya OAuth ile birlikte `400`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 190 |
| **İlgili karar** | K-869 |
| **İnsan gerekir** | Hayır |
| **Devir** | ✅ 2026-09-24 · ➜ CI: `CredentialHeaderSaveTests.Authorization_from_both_fields_or_with_oauth_is_rejected` · `…Authorization_conflict_counts_the_preserved_map` |

**Adımlar**
```bash
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$APU/api/mcp-servers/m4" -H "$APB" -H "$J" \
  -d '{"endpoint":"https://mcp.example.com/mcp","authorizationConfigurationKey":"Tracon:McpSecrets:A","headerConfigurationKeys":{"Authorization":"Tracon:McpSecrets:B"}}'
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$APU/api/mcp-servers/m5" -H "$APB" -H "$J" \
  -d '{"endpoint":"https://mcp.example.com/mcp","oauthEnabled":true,"oauthClientId":"c","headerConfigurationKeys":{"Authorization":"Tracon:McpSecrets:B"}}'
```

**Beklenen sonuç** — İkisi `400`; `detail` "'headerConfigurationKeys' names 'Authorization'" der.

### MT-MCP-074 — 👤 Arayüz formundan kayıt iki başlık haritasını korur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 190 |
| **İlgili karar** | K-870 |
| **İnsan gerekir** | Evet (form) — form gövdesi `curl` ile ölçüldü |
| **Devir** | ◐ 2026-09-24: form gövdesiyle `curl` ✅; tarayıcıdan tıklama koşulmadı · ➜ CI: `HeaderPreservationTests.A_form_save_keeps_both_mcp_maps` |

**Ön koşul** — MT-MCP-071 ve `headers` `{"X-Tenant":"plain-190"}` kaydı.

**Adımlar**
1. 👤 `/tracon/mcp` ekranında `m1`'i aç, yalnız açıklamayı değiştir, kaydet.
2. `sqlite3 $TMPDIR/faz190.db "SELECT headers, header_configuration_keys FROM tracon_mcp_servers WHERE name='m1';"`
3. Listede `m1` satırının kimlik sütununa bak.

**Beklenen sonuç**
- `200`. Satırda `{"X-Tenant":"plain-190"}` ve `{"X-API-Key":"Tracon:McpSecrets:DemoKey"}` kalır.
- Liste kimlik sütununda `X-API-Key` görünür (başlık adı; `none` değil).
- Ölçülen (form gövdesi `curl` ile): `200`; iki harita korundu.

### MT-MCP-075 — `{}` haritayı temizler; `null` korur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 190 |
| **İlgili karar** | K-870 |
| **İnsan gerekir** | Hayır |
| **Devir** | koşulmadı; ➜ CI: `HeaderPreservationTests.An_empty_map_clears_it` |

**Adımlar**
```bash
curl -s -X PUT "$M" -H "$APB" -H "$J" -d '{"endpoint":"http://127.0.0.1:9099/mcp","headers":{}}' | jq '.headers, .headerConfigurationKeys'
curl -s -X PUT "$M" -H "$APB" -H "$J" -d '{"endpoint":"http://127.0.0.1:9099/mcp","headerConfigurationKeys":{}}' | jq '.headers, .headerConfigurationKeys'
```

**Beklenen sonuç** — İlki `{}` ve korunan anahtar haritası; ikincisi iki `{}`.

### MT-MCP-076 — Eski düz kimlik başlığı gönderilir; uyarı değeri taşımaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 190 |
| **İlgili karar** | K-868 (karar 7) |
| **İnsan gerekir** | Hayır |
| **Devir** | ✅ 2026-09-24 · ➜ CI: `McpHeaderBuilderTests.A_stored_plain_credential_is_sent_with_a_warning_that_carries_no_value` |

**Ön koşul** — Ortak kurulum, SQLite.

**Adımlar**
```bash
sqlite3 $TMPDIR/faz190.db "INSERT INTO tracon_mcp_servers (id, tenant_id, name, endpoint, headers, created_at, updated_at) VALUES ('$(uuidgen)', 'default', 'm2', 'http://127.0.0.1:9099/mcp2', '{\"X-API-Key\":\"eski\",\"X-Team\":\"t1\"}', '2026-09-24T00:00:00.0000000Z', '2026-09-24T00:00:00.0000000Z');"
curl -s -X POST "$APU/api/mcp-servers/refresh" -H "$APB"
```

**Beklenen sonuç**
- Yakalayıcı `/mcp2 eski` yazar (satır çalışmaya devam eder).
- Uygulama logu "MCP server 'm2' stores the credential header 'X-API-Key' in plain
  headers, in the clear. Move it to headerConfigurationKeys." yazar; `eski` logda YOKTUR.

### MT-MCP-077 — Göç tarifi: düz kimliği anahtar adına tek kayıtla taşımak

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 190 |
| **İlgili karar** | K-870 |
| **İnsan gerekir** | Hayır |
| **Devir** | ✅ 2026-09-24 · ➜ CI: `HeaderPreservationTests.Migrating_a_plain_credential_needs_the_plain_map_in_the_same_save` |

**Ön koşul** — MT-MCP-076'nın `m2` satırı.

**Adımlar**
```bash
# (a) yalnız yeni alan
curl -s -w '\n%{http_code}\n' -X PUT "$APU/api/mcp-servers/m2" -H "$APB" -H "$J" \
  -d '{"endpoint":"http://127.0.0.1:9099/mcp2","headerConfigurationKeys":{"X-API-Key":"Tracon:McpSecrets:DemoKey"}}'
# (b) aynı + diğer düz başlıklar
curl -s -o /dev/null -w '%{http_code}\n' -X PUT "$APU/api/mcp-servers/m2" -H "$APB" -H "$J" \
  -d '{"endpoint":"http://127.0.0.1:9099/mcp2","headers":{"X-Team":"t1"},"headerConfigurationKeys":{"X-API-Key":"Tracon:McpSecrets:DemoKey"}}'
curl -s -X POST "$APU/api/mcp-servers/refresh" -H "$APB"
```

**Beklenen sonuç**
- (a) `400`; `detail` "Remove 'X-API-Key' from headers" der.
- (b) `200`; yakalayıcı `/mcp2 dogrulama-degeri` yazar; `m2` için yeni
  "stores the credential header" uyarısı çıkmaz.
- Ölçülen (2026-09-24): (a) `400` beklenen metinle, (b) `200`, yakalayıcı değeri aldı.
