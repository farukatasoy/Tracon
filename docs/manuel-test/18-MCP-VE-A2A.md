# 18 — MCP İstemcisi/Sunucusu ve A2A Dış Yüzeyi (`MCP`)

> **Alan kodu:** `MCP` · **Faz:** 6, 22, 50
> **Kaynak:** `src/AgentPrism.Mcp/` (tümü — istemci tarafı: sunucu keşfi,
> tool/prompt/resource köprüsü, OAuth) · `src/AgentPrism.AspNetCore/McpServer/`
> (tümü — AgentPrism'i MCP sunucusu olarak dışa açma) ·
> `src/AgentPrism.AspNetCore/A2A/` (tümü — AgentPrism'i A2A sunucusu olarak
> dışa açma) · `src/AgentPrism.AspNetCore/Endpoints/GovernanceEndpoints.cs`
> (yalnız `/api/mcp-servers/*` dalı — MCP sunucu kaydı CRUD, prompt/resource
> köprüsü, OAuth başlatma) · `src/AgentPrism.AspNetCore/Security/ExternalSurfaceGuard.cs`,
> `ExternalCallAudit.cs` · `src/AgentPrism.Abstractions/Mcp/McpServerDefinition.cs` ·
> `src/AgentPrism.UI/frontend/src/screens/mcp.tsx`, `tools.tsx` (yalnız MCP
> rozeti) · `src/AgentPrism.UI/frontend/src/components/mcp-server-detail.tsx` ·
> Migration'lar: `mcp_servers` tablosu (PostgreSQL `0002_observability.sql` +
> `0013_mcp_oauth.sql`; SQL Server/SQLite `0001_initial.sql`).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

AgentPrism, Model Context Protocol'ü **iki yönde** de konuşur. Faz 22
(**istemci** yönü): AgentPrism, dışarıdaki bir MCP sunucusuna bağlanıp
onun tool/prompt/resource'larını kendi agent'larına tool olarak sunar —
sunucular kodda değil, veritabanında tanımlıdır ve arka planda periyodik
taranır. Faz 50 bunun tam tersini ekledi (**sunucu** yönü): AgentPrism'in
kendi katalog agent'ları, dışarıdaki bir MCP istemcisine (ör. Claude Code
CLI) tek bir tool olarak sunulabilir. Aynı fazda, kardeş bir protokol olan
A2A (Agent2Agent) ile agent'lar kendi "agent kartı"nı yayınlayabilir. Faz 6
temel keşif altyapısını getirmişti; bu dosya Faz 22/50'nin üzerine kurulu
**bugünkü** yüzeyi test eder.

```mermaid
flowchart TD
    subgraph Istemci["AgentPrism ISTEMCI (Faz 22)"]
        DB["mcp_servers tablosu<br/>(kodda degil, DB'de)"] --> DS["McpDiscoveryService<br/>5 dk'da bir tarar"]
        DS -->|basarili| TOOLS["AIFunction listesi<br/>{server}_{tool}"]
        DS -->|ulasilamaz| DEGRADE["o sunucu 0 tool<br/>digerleri etkilenmez"]
    end

    subgraph Sunucu["AgentPrism SUNUCU (Faz 50)"]
        CATALOG["IAgentCatalog<br/>CANLI, her istekte okunur"] --> MCPSRV["/agentprism/mcp<br/>tools/list, tools/call"]
        CATALOG --> A2ASRV["/agentprism/a2a/{agent}<br/>agent-card.json + SendMessage"]
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
> olan**: dış yüzeyin kendisi (`/agentprism/mcp`, `/agentprism/a2a`)
> `RequireApiKeyScope(ExternalInvoke)`'u DOĞRU uygular, ama MCP sunucu
> **kayıt** API'si (`/api/mcp-servers/*`, `GovernanceEndpoints.cs`) hiçbir
> `RequireApiKeyScope` çağrısı taşımaz — §9'da ayrı ayrı ölçülür, biri
> pozitif kontrol biri kusur adayı.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080/agentprism`.
3. Örnek uygulama `ozetleyici` agent'ını **hem** MCP **hem** A2A ile dışa
   açar (`Program.cs:98-99`, `.UseMcpServer(o =>
   o.ExposedAgents.Add("ozetleyici"))` / `.UseA2A(o =>
   o.ExposedAgents.Add("ozetleyici"))`) — bu agent **kasıtlı olarak**
   hiçbir tool taşımaz, bu yüzden §6/§7'nin onay-sınırı guard'ını hiç
   tetiklemez. Guard'ı tetiklemek isteyen case'ler (§6 MT-MCP-034, §7
   MT-MCP-045) GEÇİCİ bir `Program.cs` değişikliği ister — bu değişiklik
   case sonunda GERİ ALINIR.
4. `AgentPrism:Mcp` bölümü `appsettings.json`'da tanımlı DEĞİLDİR; örnek
   uygulama `UseMcp(builder.Configuration.GetSection(...))`
   (config-bağlı overload) kullanır — §2'nin case'leri bu farkı ölçer.
5. **Yerel bir test MCP sunucusu bu repo'da hiç dokümante edilmemiştir**
   (bkz. §5 başlığı) — §1-§4'ün "gerçek bağlantı" gerektiren case'leri
   tester'ın kendi kuracağı bir sunucuya ihtiyaç duyar; §5 bu kurulumu
   tarif eder.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export PG="docker exec -i ap-pg psql -U postgres -d agentprism"
```

> **Gerçek para uyarısı.** §4 MT-MCP-023, §6 MT-MCP-032/036, §7 MT-MCP-041
> gerçek bir agent çalıştırması içerir (`echo` sağlayıcısı yeterlidir,
> OpenAI şart değildir — `ozetleyici`/`support` `echo` ile de çalışır).
> Kalan tüm case'ler model çağırmaz.

---

## Bu dosyanın yerel fixture'ları

| Kimlik | Değer |
|---|---|
| `FIX-MCP-01` | Sunucu adı `test-sunucu` · `endpoint: "http://localhost:6060/mcp"` · `transport: "StreamableHttp"` · `requiresApproval: false` · **tester-tedarikli** — bkz. §5, repo'da dokümante edilmiş bir yerel MCP sunucusu YOKTUR |
| `FIX-MCP-02` | `ozetleyici` — örnek uygulamanın kendi MCP+A2A ile dışa açtığı, tool'suz agent (`00-INDEKS.md` §3.1) |

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

**Gerçek sonuç**
HTTP: 200, id dolu bir GUID, requiresApproval: false (istekte acikca belirtildigi icin varsayilan gecersiz kilindi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
**Dokuman duzeltmesi.** Script GET /api/mcp-servers/{name} (tekil) cagiriyordu - boyle bir uc YOK (yalniz liste ucu GET /api/mcp-servers var, GovernanceEndpoints.cs sadece MapGet(list)/MapPut/MapDelete/{name}'e ozel MapGet YOK). Script listeden filtrelemeye duzeltildi. Duzeltilmis sorguyla: True yazdirildi - requiresApproval alani gonderilmedigi icin varsayilan true kullanildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
**KALDI - HATA-S2-007 (Orta, HATA-S2-006 ile ayni kok neden).** Beklenen HTTP 400 yerine HTTP 500 (genel ProblemDetails) dondu. Kok neden: PUT /api/mcp-servers/{name} handler'i (GovernanceEndpoints.cs:190-195) McpServerRequest request parametresini ACIK [FromBody] ozniteligi OLMADAN (ORTUK govde baglama) aliyor; McpTransportMode enum'u (McpServerDefinition.cs:33-40) yalniz StreamableHttp ve Sse tasiyor, 'Stdio' hic bir enum uyesi degil - JsonStringEnumConverter bunu JsonException ile reddediyor, bu istisna Validate(name, request) (satir 197, 527) hic cagirilmadan, govde-baglama asamasinda olusuyor ve app.UseExceptionHandler() genel 500'e ceviriyor. Onemli ek bulgu: bu ORTUK (oznitelik olmadan) govde baglama ornegi, HATA-S2-006'nin 'ac [FromBody] kullanan 10 dosya' kapsam tahminini ASIYOR - grep '[FromBody]' bu deseni YAKALAMAZ, gercek etkilenen yuzey daha genis olabilir.

---

**Yeniden koşum (Aile G, 2026-08-14).** DÜZELTİLDİ — **HTTP 400**:
`{"title":"Gecersiz istek govdesi","detail":"The JSON value could not be converted to AgentPrism.McpTransportMode. Path: $.transport..."}`.
`GovernanceEndpoints`'in `/api/mcp-servers/{name}` PUT handler'i artık
`RequestBodyBinding.ReadAsync<McpServerRequest>` ile govdeyi elle okuyor —
implicit binding tamamen kaldırıldı. Bu case'in kendi bulgusu ("grep tabanlı
tahmin ORTUK baglamayı kaçırır") doğrulandı ve düzeltmenin kapsamını
genişletti: OpenAPI belgesindeki (`docs/openapi/agentprism.json`)
`requestBody` taşıyan TÜM rotalar tek tek çapraz kontrol edildi (yalnız
grep'e güvenilmedi) — implicit binding kullanan 9 EK uç bulundu
(`AgentEndpoints.RollbackAsync`, `.../run`, `SkillEndpoints.SaveAsync`,
`SessionEndpoints.BranchSessionAsync`, `KnowledgeEndpoints.UploadAsync`/`SearchAsync`,
`VoiceEndpoints.SpeakAsync`, `GovernanceEndpoints` tenants PUT + mcp-prompts
POST) ve hepsi aynı desene taşındı. Ayrıntı `KAPANIS-PLANI.md` §6 Aile G.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP: 400, title: Adres semasi desteklenmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "authorizationConfigurationKey": "AgentPrism:Mcp:BirTest"
}'
```

**Beklenen sonuç**
- `HTTP: 400` — bir sunucu ya statik başlık tabanlı yetkilendirme ya OAuth
  kullanabilir, ikisi birden olamaz.

**Gerçek sonuç**
Ilk denemede oauthClientId eksikti, farkli (ama gecerli) bir 400 (OAuth istemci kimligi eksik) tetiklendi - test verisi eksikti, urun kusuru degil. oauthClientId eklenerek tekrarlandiginda: HTTP 400, title: Cakisan kimlik dogrulama, detail: OAuth acikken authorizationConfigurationKey bos olmalidir... - beklenen karsilikli dislama kurali dogru calisiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MCP-006 — JSON yanıtında OAuth alanları `oauthEnabled`/`oauthClientId` biçiminde (camelCase, çift büyük harf DEĞİL)

Sınır senaryosu — Faz 22'nin kendi devir notunda kayıtlı, testler
yakalamamış bir sınıf hata (`docs/22-MCP-DERINLESMESI.md` "Plandan
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
  "oauthClientSecretConfigurationKey": "AgentPrism:Mcp:TestSecret",
  "oauthScopes": "read"
}' | python3 -m json.tool
```

**Beklenen sonuç**
- Ham JSON gövdesinde alan adları `"oauthEnabled"`, `"oauthClientId"`,
  `"oauthClientSecretConfigurationKey"`, `"oauthScopes"` biçimindedir —
  `"oAuthEnabled"` (çift büyük harf) DEĞİL. Bu bug bir kez yakalanmıştı;
  regresyon olup olmadığı burada gözle doğrulanır.

**Gerçek sonuç**
Ham JSON govdesinde alan adlari oauthEnabled, oauthClientId, oauthClientSecretConfigurationKey, oauthScopes bicimindedir - oAuthEnabled (cift buyuk harf) DEGIL. Regresyon yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  yapılandırma **anahtarı adı** (ör. `"AgentPrism:Mcp:TestSecret"`)
  taşır — hiçbir gerçek secret DEĞERİ (token, şifre) gövdede yer almaz
  (K-059). `headers` alanındaki değerler ise OLDUĞU GİBİ döner — bir
  sunucu kaydı `headers` içine yanlışlıkla bir secret koyarsa, şema bunu
  ENGELLEMEZ; bu, formun kendi UI notunda da belirtilen bir sorumluluk
  sınırıdır (§8 MT-MCP-049 ile karşılaştır).

**Gerçek sonuç**
Her sunucu satirinda authorizationConfigurationKey yalniz bir yapilandirma anahtari adi tasiyor (orn. AgentPrism:Mcp:TestSecret icin oauthClientSecretConfigurationKey alaninda), hicbir gercek secret DEGERI govdede yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
SELECT count(*) FROM agentprism.mcp_servers WHERE name IN ('ftp-sunucu', 'stdio-denemesi', 'karisik-yetki');
```

**Beklenen sonuç**
- `HTTP: 204`. SQL sorgusu `0` döner (bu üç sunucu hiç başarıyla
  oluşturulmamıştı — negatif case'lerin kalıcı iz bırakmadığının kanıtı).

**Gerçek sonuç**
HTTP: 404 - ftp-sunucu hic basariyla olusturulmamisti (negatif case kalici iz birakmadi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — MCP Keşfi: Yenileme Aralığı ve Yapılandırma Bağlama (Faz 22, K-353)

### MT-MCP-010 — Örnek uygulama config-bağlı `UseMcp` overload'ını kullanır — `AgentPrism:Mcp:RefreshInterval` GERÇEKTEN etkilidir

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
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Mcp:RefreshInterval" "00:00:10"
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
  `appsettings.json`'daki `AgentPrism:Mcp:RefreshInterval` GERÇEKTEN
  okunmuştur. (K-353 öncesi bu ayar sessizce yok sayılırdı.)
- Case sonrası `dotnet user-secrets remove "AgentPrism:Mcp:RefreshInterval"`.

**Gerçek sonuç**
AgentPrism__Mcp__RefreshInterval=00:00:10 ile yeniden baslatildi. test-sunucu kaydedildikten 15 saniye sonra /api/tools listesinde 14 adet test-sunucu_* onekli tool goruldu (test-sunucu_echo, test-sunucu_get-sum, vb.) - varsayilan 5 dakika yerine 10 saniyede bir tarama gerceklesti (K-353 duzeltmesi dogrulandi). Uygulama loglarinda 4 McpDiscoveryService satiri gozlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. `appsettings.json`/`user-secrets`'a `AgentPrism:Mcp:Servers:0:Endpoint`
   gibi bir anahtar EKLEMEYİ dene (böyle bir yapı zaten kod tarafından
   okunmaz — bu case'in amacı, bu tür bir anahtarın SESSİZCE yok
   sayıldığını doğrulamaktır).

**Girilecek veri**
```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Mcp:Servers:0:Endpoint" "http://olmayan-bir-yer/mcp"
# Uygulamayi yeniden baslat.
curl -s "$APU/api/mcp-servers" -H "$APB" | python3 -c "import json,sys; print([s['name'] for s in json.load(sys.stdin)])"
```

**Beklenen sonuç**
- Bu anahtar HİÇBİR etki üretmez — listede böyle bir sunucu YOKTUR.
  Sunucu kayıtları yalnız `mcp_servers` DB tablosundan gelir. Case sonrası
  bu `user-secrets` anahtarını kaldır.

**Gerçek sonuç**
AgentPrism__Mcp__Servers__0__Endpoint=http://olmayan-bir-yer/mcp ayarlandi, uygulama yeniden baslatildi. GET /api/mcp-servers listesinde yalniz elle PUT edilen test-sunucu var - bu config anahtari hicbir etki uretmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- `test-sunucu`, `authorizationConfigurationKey: "AgentPrism:Mcp:HicVarOlmayanAnahtar"`
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

**Gerçek sonuç**
test-sunucu authorizationConfigurationKey: AgentPrism:Mcp:HicVarOlmayanAnahtar (hic tanimli olmayan bir user-secrets anahtari) ile guncellendi. POST /api/mcp-servers/refresh HTTP 200 dondu (toolCount:14, uygulama COKMEDI, tarama devam etti). Log satiri: "warn: AgentPrism.McpToolCatalog[0] MCP sunucusu 'test-sunucu' icin 'AgentPrism:Mcp:HicVarOlmayanAnahtar' yapilandirma anahtari bos. Kimlik dogrulama basligi gonderilmeyecek." - baglanti istegi o baslik olmadan gonderildi (tool sayisi degismedi, sunucu yine erisilebilirdi cunku gercek sunucu auth istemiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — MCP Keşfi: Sunucuya Ulaşılamaması — Zarif Bozulma (Faz 22)

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

**Gerçek sonuç**
Sunucu kaydi HTTP 200 ile basariyla olustu (kayit aninda baglanti denenmedi). Ardindan support/run cagrisi HTTP 200 ile normal calisti - ulasilamayan MCP sunucusu agent calistirmasini etkilemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Yerel test sunucusu durduruldu, bir sonraki kesif turu (10s araliktan) beklendi. /api/tools sorgusunda test-sunucu_* tool sayisi 14 -> 0'a dustu - onceden kesfedilmis tool'lar LISTEDEN KAYBOLDU, bayat liste korunmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
ulasilamayan (port 59999) VE test-sunucu (calisir durumda) birlikte kayitliyken /api/mcp-servers/refresh sonrasi test-sunucu 14 tool katti, ulasilamayan 0 katti - bir sunucunun basarisiz olmasi digerini etkilemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
ulasilamayan kayitliyken uygulama 22+ saniye (2+ kesif araligi, 10s ayarlanmis) canli tutuldu. Loglarda tekrarlayan 'warn: AgentPrism.McpToolCatalog[0] Client... client initialization error.' satirlari gorundu ama uygulama COKMEDI - GET /api/agents sonrasinda hala HTTP 200 dondu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — MCP Tool Adlandırma, Onay Sınırı, Kaynak Modları (Faz 22)

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

**Gerçek sonuç**
/api/tools listesinde test-sunucu_echo, test-sunucu_get-sum, test-sunucu_read_resource gibi tool adlari goruldu - test-sunucu_<orijinal-ad> bicimi, nokta ile DEGIL alt cizgi ile.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
**Dokuman duzeltmesi (onemli).** Senaryonun kendi onerdigi kurulum yolu (sunucu adinin bos/ayni-onek olacak sekilde ayarlanmasi) kod ile IMKANSIZ: McpToolNaming.TryQualify (McpToolNaming.cs:32-33) HER ZAMAN kosulsuz $"{serverName}_{toolName}" ureterek onek ekliyor; IsValidServerName (satir 25-26) bos/whitespace sunucu adini zaten reddediyor (SafeName regex ^[a-zA-Z0-9_-]+$). Yani bir MCP tool adi hicbir zaman onek TASIMADAN kod-tanimli bir tool ile (orn. get_order_status) TAM ESLESEMEZ - carpisma yapisal olarak olusturulamiyor, sadece 'kod kazanir' varsayimi test EDILEMIYOR degil, senaryo TAMAMEN gereksiz hale geliyor (carpisma zaten imkansiz). Buna ragmen alttaki guvence dogrulandi: McpToolRegistry.TryGet (McpToolRegistry.cs:69-74) `_codeTools.TryGet(name, out tool) || _catalog...TryGet(...)` sirasiyla ONCE kod tool'larina bakiyor - kod okumasiyla dogrulandi, calisir kanit yerine geciyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- `AgentPrism:Mcp:MaxToolsPerServer` `1` olarak ayarlanmış (test
  amaçlı), yerel sunucu 2+ tool sunuyor.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Mcp:MaxToolsPerServer" "1"
# Uygulamayi yeniden baslat, keşif turunu bekle.
curl -s "$APU/api/tools" -H "$APB" | python3 -c "import json,sys; print(len([t for t in json.load(sys.stdin) if t.get('source')=='test-sunucu']))"
```

**Beklenen sonuç**
- Sunucu keşfi BAŞARISIZ OLMAZ — yalnız ilk `1` tool tutulur, fazlası
  uyarı logu ile düşürülür. Case sonrası `MaxToolsPerServer`'ı kaldır.

**Gerçek sonuç**
MaxToolsPerServer=1 ile yeniden baslatildi. /api/tools listesinde test-sunucu kaynakli TAM 2 tool goruldu: test-sunucu_echo (gercek, alfabetik ilk) VE test-sunucu_read_resource (sentetik kaynak-okuma tool'u, MT-MCP-024). Log: "MCP sunucusu 'test-sunucu' 1 tool sinirini asti; fazlasi atiliyor." Sinirlama GERCEK tool'lara dogru uygulaniyor (14 -> 1); sentetik read_resource tool'u bu sinirin DISINDA ayrica ekleniyor (ayri kod yolu) - dokuman bunu hesaba katmamis ama davranis mantikli ve kusur degil, kesif BASARISIZ OLMADI, uyari logu ile duzgun dusuruldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
**KALDI - HATA-S2-004 kapsam genislemesi (onemli).** test-sunucu requiresApproval:true yapildi, test-sunucu_echo tool'unu tasiyan bir agent olusturuldu ve tetiklendi. Oturum gecmisinde bir toolApprovalRequest kaydi olustu (tool GERCEKTEN onay bekliyor, dogrulandi) - ama /api/runs?agentName=...&take=1 (YONETIM API'si, compat DEGIL) run kaydi status: Completed, completedAt dolu, error: null gosterdi - AwaitingInput/AwaitingApproval DEGIL. Kok neden bulundu: RunStatus.AwaitingInput (RunStatus.cs:29-48) kendi XML belgesinde ACIKCA 'Yalnizca RunKind.Workflow satirlarinda gorulur' diyor - yani bu durum kod-tanimli (Workflow olmayan, Kind:'Agent') calistirmalar icin YAPISAL OLARAK HIC KULLANILMIYOR. Bu, HATA-S2-004'un (dosya 08, MT-COMPAT-029) 'yalniz compat uclarini etkiliyor' seklindeki onceki cerceevelemesini YANLISLIYOR: sorun compat'a ozgu degil, TUM Agent-turu calistirmalarin genel bir mimari sinirlamasidir - onay bekleyen bir kod VEYA MCP tool'u calistiran herhangi bir Agent-turu run, HANGI ucten (yonetim API'si veya compat) tetiklenirse tetiklensin, status alaninda bunu hic yansitamiyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
/api/tools listesinde test-sunucu_read_resource adli sentetik bir tool otomatik belirdi - yerel test sunucusu (server-everything) resources yetenegini bildiriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Yerel Test MCP Sunucusu (İzlek B)

> **Önemli not.** `PROMPT.md` §3'ün "Kapsam kararları" tablosu "Yerel test
> MCP sunucusu kurulur — dış bağımlılık yok" der, ama bu repo'da böyle bir
> sunucu **hiç dokümante edilmemiştir**: `docs/`, `samples/`, `scripts/`
> içinde `npx`/`docker` ile başlatılacak bir MCP sunucusuna dair TEK bir
> satır yoktur, `tests/AgentPrism.Mcp.UnitTests/` içinde de gerçek/sahte
> bir üst akış MCP HTTP sunucusu başlatan hiçbir test yoktur. Aşağıdaki
> kurulum bu boşluğu dolduran **tester-tedarikli altyapıdır** — AgentPrism
> deposunun bir parçası veya onaylı bir fixture DEĞİLDİR.

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
   bir araçtır — aşağıdaki komut yalnız bir ÖRNEKTİR, AgentPrism
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

**Gerçek sonuç**
Yerel test sunucusu olarak resmi referans sunucusu kullanildi: `npx -y @modelcontextprotocol/server-everything streamableHttp` (CLI --port secenegi desteklemiyor, sabit port 3001 kullaniyor - dokumandaki 6060 ornegi yerine 3001 kullanildi, tum sonraki case'lerde tutarli). Sunucu http://localhost:3001/mcp uzerinde Streamable HTTP ile ayakta, en az 13 tool VE resources yetenegi sunuyor (initialize yanitinda dogrulandi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MCP-027 — Yerel sunucuyu AgentPrism'e kaydet, tool keşfi gerçekleşir

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

**Gerçek sonuç**
POST /api/mcp-servers/refresh sonrasi GET /api/tools listesinde test-sunucu_echo, test-sunucu_get-sum gibi en az bir test-sunucu_<ad> tool'u goruldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
test-sunucu_echo tool'unu tasiyan bir agent olusturuldu, "MERHABA-MCP-TEST" metnini yankilamasi istendi. Yanit gercek tool cagrisi (functionCall test-sunucu_echo) + gercek MCP sunucu sonucu (functionResult: "Echo: MERHABA-MCP-TEST") + son metin ("Echo sonucu: MERHABA-MCP-TEST") icerdi. Olay akisinda ToolInvoking/ToolInvoked cerceveleri test-sunucu_echo adiyla goruldu - gercek MCP sunucusundan donen sonuc.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — MCP Sunucusu: AgentPrism'i Dışa Açma (Faz 50)

### MT-MCP-030 — Varsayılan KAPALI: boş beyaz liste + `ExposeAllAgents=false` → `tools/list` BOŞ döner

Negatif/sınır senaryosu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Beklenen sonuç (koşum, izlek C — `AgentPrism.Testing`)**
- `AgentPrismTestHost` ile `AgentPrismMcpServerOptions`'ı hiç
  yapılandırmadan (`ExposedAgents=[]`, `ExposeAllAgents=false`
  varsayılanlarıyla) `tools/list` çağrıldığında `tools: []` döner —
  hiçbir agent, açıkça izin verilmedikçe dışa açılmaz.
- Repo'nun kendi `Bos_beyaz_liste_hicbir_tool_dondurmez` testi
  (`tests/AgentPrism.AspNetCore.FunctionalTests/McpServerEndpointTests.cs:14`)
  bu davranışı zaten otomatik doğruluyor — bu case, üretim benzeri örnek
  uygulama üzerinde AYNI GARANTİYİ elle tekrar doğrular: örnek
  uygulamada yalnız `ozetleyici` beyaz listededir, başka HİÇBİR agent
  `tools/list`'te görünmez (bkz. MT-MCP-031).

**Gerçek sonuç**
Yanit tam olarak tek tool icerdi (asagida MT-MCP-031 ile birlikte kanitlandi) - baska hicbir agent listede yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MCP-031 — `ozetleyici` fixture: `tools/list` gerçek çıktısı

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
- Yanıt TAM OLARAK tek bir tool içerir: `agentprism_ozetleyici`,
  `inputSchema` yalnız `message` (string, required) alanı taşır. Başka
  hiçbir agent (ör. `support`) listede YOKTUR — beyaz listeye
  eklenmemiştir.

**Gerçek sonuç**
Yanit TAM OLARAK tek tool icerdi: agentprism_ozetleyici, inputSchema yalniz message (string, required) alani tasiyor. Baska hicbir agent (support dahil) listede YOK.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
          "params": { "name": "agentprism_ozetleyici", "arguments": { "message": "Bugun hava cok guzeldi. Is yerinde her sey yolunda gitti. Toplantilar verimliydi." } } }'
```

**Beklenen sonuç**
- `HTTP: 200`. Yanıt `result.content[0].text` alanında `ozetleyici`
  agent'ının ürettiği bir özet metni içerir. `GET /api/runs?agentName=ozetleyici`
  bu çağrıya karşılık gelen YENİ bir kök run (Depth=0) gösterir.

**Gerçek sonuç**
HTTP 200 (SSE event: message). result.content[0].text ozetleyici agent'inin urettigi gercek bir uc maddeli ozet metni icerdi. GET /api/runs?agentName=ozetleyici bu cagriya karsilik gelen YENI bir kok run (depth:0, status:Completed) gosterdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
          "params": { "name": "agentprism_ozetleyici", "arguments": { "message": "" } } }'
```

**Beklenen sonuç**
- Yanıt bir hata içerir (`isError: true` veya JSON-RPC hata nesnesi) —
  boş mesajla agent çalıştırılmaz.

**Gerçek sonuç**
Yanit isError:true tasiyor, content[0].text: "'message' argumani bos olamaz." - bos mesajla agent calistirilmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. `samples/AgentPrism.Api/Program.cs`'te GEÇİCİ olarak
   `.UseMcpServer(o => o.ExposedAgents.Add("ozetleyici"))` satırını
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
  samples/AgentPrism.Api/Program.cs` veya elle geri yaz), uygulama
  normal haliyle yeniden başlatılır.

**Gerçek sonuç**
Program.cs'te GEÇICI olarak .UseMcpServer(o => o.ExposedAgents.Add("support")) yapildi (cancel_order tasiyan agent), yeniden derlendi, dotnet run ile baslatildi. Loglarda 'crit: AgentPrism.McpApprovalGuardFilter[0] MCP disa acik yuzey denetimi basarisiz oldu; uygulama durduruluyor.' + InvalidOperationException ("'support' agent'i MCP uzerinden disa acilamaz: 'cancel_order' tool'lari kullanici onayi istiyor...") gorundu, sonra 'Application is shutting down...' - surec kendini kapatti (once dinlemeye basliyor, arka plan denetimi sonra durduruyor - LogCritical + StopApplication paterni, cikri unhandled exception degil). Program.cs degisikligi geri alindi (git diff temiz), yeniden derlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- Orijinal yaklaşım (`ozetleyici`'yi `PUT` ile güncellemek) ÇALIŞMAZ:
  `ozetleyici` **kodda tanımlı** bir agent'tır (`origin: "Code"`,
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
# -> yalniz ozetleyici gorunur

curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-canli-katalog",
  "description": "ILK aciklama",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
curl -s -X POST "$APU/mcp" -H "$APB" -H "content-type: application/json" -H "accept: application/json, text/event-stream" \
     -d '{ "jsonrpc": "2.0", "id": 2, "method": "tools/list" }'
# -> restart OLMADAN agentprism_manuel-canli-katalog gorunur, description: "ILK aciklama"

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

**Gerçek sonuç**
**Dokuman duzeltmesi (onemli, yontem degisti).** Orijinal senaryo ozetleyici'yi PUT ile guncellemeyi oneriyordu - bu ISLEMEZ cunku ozetleyici KODDA TANIMLI bir agent'tir (origin:Code, isEditable:false, MT-API-008'in zaten kanitladigi 409 kurali gecerli); ustelik senaryonun kendi govdesi name/model alanlarini da eksik birakmisti (400 alindi, PUT hicbir zaman basarili olmadi). Duzeltilmis yontem: Program.cs'e GECICI olarak henuz var olmayan bir DB-kokenli agent adi (manuel-canli-katalog) ExposedAgents'e eklendi, TEK bir yeniden baslatma icinde: (1) tools/list -> yalniz ozetleyici, (2) POST /api/agents ile manuel-canli-katalog olusturuldu (ILK aciklama) -> restart OLMADAN tools/list'te agentprism_manuel-canli-katalog (description: ILK aciklama) gorundu, (3) PUT ile description GUNCELLENMIS aciklama - canli katalog testi yapildi -> AYNI restart icinde, tekrar tools/list cagrildiginda YENI aciklama goruldu. CatalogToolListHandler IAgentCatalog'u dogrulanmis sekilde CANLI okuyor, onbelleklenmis kopya donmuyor. Program.cs degisikligi geri alindi (git diff temiz).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. AgentPrism'in MCP sunucusunu CLI'ye ekle.
2. Bağlantı durumunu kontrol et.

**Girilecek veri (düzeltilmiş — Authorization başlığı eksikti)**
```bash
claude mcp add --transport http agentprism-manuel-test http://localhost:5080/agentprism/mcp \
     -s local --header "Authorization: Bearer manuel-test-token-2026"
claude mcp get agentprism-manuel-test
```

**Beklenen sonuç**
- `Status: ✔ Connected`. Bu, AgentPrism'in MCP sunucu yüzeyinin
  spesifikasyona gerçekten uygun olduğunun (protokolün kendi bir
  istemcisiyle doğrulanmış) en güçlü kanıtıdır.
- Case sonrası `claude mcp remove agentprism-manuel-test -s local`.

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi komutu Authorization basligi TASIMIYORDU - ilk deneme HTTP 404 ("Failed to connect", OAuth kesif hatasi olarak yanlis yorumlandi) ile basarisiz oldu. --header "Authorization: Bearer manuel-test-token-2026" eklenerek (claude mcp add --help'te belgelenen secenek) duzeltildi: claude mcp get agentprism-manuel-test -> Status: Connected. Case sonrasi claude mcp remove agentprism-manuel-test -s local ile temizlendi (git status: yalniz .claude.json degisti, repo etkilenmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — A2A: AgentPrism'i Dışa Açma (Faz 50)

### MT-MCP-040 — Agent kartı `GET .well-known/agent-card.json` gerçek çıktısı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/a2a/ozetleyici/.well-known/agent-card.json" -H "$APB"
```

**Beklenen sonuç**
- Gövde `name: "ozetleyici"`, `capabilities: {streaming: false,
  pushNotifications: false}`, `defaultInputModes: ["text/plain"]`,
  `defaultOutputModes: ["text/plain"]`, `supportedInterfaces[0].url`
  agent'ın alt yoluna işaret eder, `protocolBinding: "JSONRPC"`.

**Gerçek sonuç**
Govde name: ozetleyici, capabilities: {streaming:false, pushNotifications:false}, defaultInputModes: [text/plain], defaultOutputModes: [text/plain], supportedInterfaces[0].url: /agentprism/a2a/ozetleyici (goreceli), protocolBinding: JSONRPC.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
curl -s -X POST "$APU/a2a/ozetleyici/" -H "$APB" -H "content-type: application/json" -d '{
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
  gözlemdir, bir AgentPrism kusuru değildir (bağımlı SDK'nın davranışı).

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi govdesi 'messageId' alanini eksik birakmisti - A2A SDK'si bunu zorunlu kildigi icin ilk deneme -32602 'Invalid parameters: request body could not be deserialized as SendMessageRequest.' hatasi verdi (urun kusuru degil, eksik test verisi). messageId eklenerek duzeltildi: HTTP 200, metot gercekten SendMessage (PascalCase, A2A spec'inin message/send'i DEGIL - SDK davranisi). Yanittaki role alani ROLE_AGENT (protobuf-tarzi, 'agent' DEGIL). Beklenen uyumsuzluk dogrulandi - AgentPrism kusuru degil, bagimli SDK'nin (A2A.AspNetCore) davranisi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
curl -s "$APU/a2a/ozetleyici/.well-known/agent-card.json" -H "$APB" \
  | python3 -c "import json,sys; print(json.load(sys.stdin)['supportedInterfaces'][0]['url'])"
```

**Beklenen sonuç**
- Çıktı `/agentprism/a2a/ozetleyici` gibi GÖRECELİ bir yoldur, `http://...`
  ile başlayan MUTLAK bir URL DEĞİLDİR. Bir ters vekil (reverse proxy)
  arkasındaki gerçek bir A2A istemcisi bu URL'yi kendisi tamamlamak
  zorunda kalabilir — bu bilinen bir sınırlamadır, kusur değildir.

**Gerçek sonuç**
Cikti /agentprism/a2a/ozetleyici - goreceli bir yol, mutlak URL DEGIL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. `AgentPrismA2AOptions` tipinin genel API yüzeyini incele (kod
   okuması, `src/AgentPrism.AspNetCore/A2A/AgentPrismA2AOptions.cs`) veya
   dolaylı olarak: `ozetleyici` dışında herhangi bir agent'a A2A yoluyla
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

**Gerçek sonuç**
HTTP: 404 - A2A'da support icin agent karti yok, ExposeAllAgents secenegi bu tarafta yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  testlerle (`tests/AgentPrism.AspNetCore.FunctionalTests/A2AEndpointTests.cs`)
  kanıtlanmış, kasıtlı bir sınırlamadır.

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi scripti PUT kullaniyordu - PUT bir upsert DEGILDIR (MT-API-009), var olmayan bir agent'i olusturamaz, 404 doner. POST /api/agents ile duzeltildi: HTTP 201, agent basariyla olusturuldu. Ardindan GET /a2a/yeni-a2a-adayi/.well-known/agent-card.json -> HTTP 404 - AddA2AServer kayit-zamanli bir API, calisirken eklenen bir agent'i GOREMEDI (MT-MCP-035'in MCP tarafindaki canli davranisinin TAM TERSI).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
   o.ExposedAgents.Add("ozetleyici"))` satırını `.UseA2A(o =>
   o.ExposedAgents.Add("support"))` ile DEĞİŞTİR.
2. `dotnet run` ile başlatmayı dene.

**Beklenen sonuç**
- MT-MCP-034 ile BİREBİR aynı sonuç: uygulama `LogCritical` loglar ve
  kendini kapatır — `A2AApprovalGuardFilter`, `McpApprovalGuardFilter`
  ile aynı deseni (arka plan görev + `SchemaReadyGate` + her isteğin bu
  görevi bekelemesi) izler.
- Case sonrası `Program.cs` değişikliği GERİ ALINIR.

**Gerçek sonuç**
Program.cs'te GECICI olarak .UseA2A(o => o.ExposedAgents.Add("support")) yapildi, yeniden derlendi, dotnet run ile baslatildi. MT-MCP-034 ile BIREBIR ayni sonuc: 'crit'/istisna log satiri (A2AApprovalGuardFilter.RunCheckAsync, ExternalSurfaceGuard.EnsureNoApprovalRequiredTools ayni yardimci metot) + 'Application is shutting down...' - ayni desen (arka plan gorev + guard, LogCritical + StopApplication). Program.cs degisikligi geri alindi (git diff temiz), yeniden derlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Arayüz: MCP Sunucu Yönetimi ve Tool Kataloğu Rozetleri

### MT-MCP-047 — Tools ekranında MCP kökenli tool `mcp: {sunucu}` rozetiyle ayrışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Adımlar**
1. `/agentprism/tools` ekranını aç.
2. `test-sunucu_*` önekli bir tool'a bak.

**Beklenen sonuç**
- Sarı/uyarı tonlu bir rozet `mcp: test-sunucu` metnini gösterir.
  Kod-tanımlı tool'larda (ör. `get_order_status`) bu rozet HİÇ YOKTUR.
  Ayrıca, `requiresApproval=true` ise ayrı bir onay rozeti de görünür —
  ikisi BAĞIMSIZDIR (bir kod tool'u da onay gerektirebilir).

**Gerçek sonuç**
**KALDI - HATA-S2-008 (Yuksek).** /agentprism/tools ekraninda test-sunucu_* tool'lari dogru sekilde sari 'mcp: test-sunucu' rozeti gosterdi. AMA ayni ekranda get_order_status, cancel_order, list_recent_orders (UCU DE KOD-TANIMLI, [AgentPrismTool] ozniteligiyle isaretli, MCP ile hicbir ilgisi yok) da YANLIS bir sekilde 'mcp: generated' rozeti gosteriyor - case'in kendi beklentisi ('Kod-tanimli tool'larda bu rozet HIC YOKTUR') ihlal edildi. Kok neden bulundu: src/AgentPrism.Generators/SourceWriter.cs:105-107, [AgentPrismTool] kaynak ureteci HER kod-tanimli tool kaydi icin KOSULSUZ `source: "generated"` literal string'i geciriyor (AgentPrismToolRegistration constructor'inin varsayilani null'dir, XML belgesi de 'Kodda tanimli tool'larda null' diyor - SourceWriter bu sozlesmeyi ihlal ediyor). ToolDescriptor.Source (ToolDescriptor.cs:29-37) kendi belgesinde bu alanin yalniz 'uzak MCP sunucusundan gelen tool'larda' dolu olmasi gerektigini soyluyor. tools.tsx:71 (`{tool.source != null && <Badge>mcp: {tool.source}</Badge>}`) bu degeri kosulsuz MCP rozeti olarak yorumluyor. **Kapsam genis**: [AgentPrismTool] ozniteligi AgentPrism'in ONERILEN, kaynak-ureteci-tabanli (AOT uyumlu) tool tanimlama yontemidir - resmi `dotnet new` sablonu da (AgentPrism.Templates/content/AgentPrism.Starter/Tools/OrderTools.cs) ayni ozniteligi kullaniyor. Bu, [AgentPrismTool] kullanan HER projede, HER kod-tanimli tool'un arayuzde yaniltici bir 'mcp: generated' rozetiyle gosterilecegi anlamina geliyor - yalniz bu ornek uygulamaya ozgu degil, framework genelinde.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-MCP-048 — `mcp.tsx` formu: OAuth açılınca `authorizationConfigurationKey` alanı OTOMATİK TEMİZLENİR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | — |

**Adımlar**
1. `/agentprism/mcp` → "Yeni sunucu".
2. `authorizationConfigurationKey` alanına bir metin yaz.
3. "OAuth kullan" onay kutusunu işaretle.

**Beklenen sonuç**
- `authorizationConfigurationKey` alanı OTOMATİK boşalır (istemci tarafı)
  — sunucu tarafındaki karşılıklı dışlama kuralını (MT-MCP-005) form
  seviyesinde önceden yansıtır.

**Gerçek sonuç**
/agentprism/mcp -> Yeni sunucu formu acildi. Authorization configuration key alanina metin yazildi (AgentPrism:Mcp:TestKeyName). OAuth (Authorization Code) onay kutusu isaretlendi. JS ile dogrulandi: alanin value'su OTOMATIK bosaldi ("") - MT-MCP-005'in sunucu tarafi kuralini form seviyesinde onceden yansitiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MCP-049 — `mcp.tsx` sunucu listesi tablosunda secret DEĞERİ hiç GÖRÜNMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22 |
| **İlgili karar** | K-059 |

**Adımlar**
1. `/agentprism/mcp` listesindeki "Yetki" sütununa bak.

**Beklenen sonuç**
- Sütun ya OAuth istemci kimliğini ya da yapılandırma ANAHTARI ADINI
  gösterir (`AgentPrism:Mcp:GithubToken` gibi) — asla gerçek bir token/
  şifre DEĞERİ göstermez. Bu, MT-MCP-007'nin API seviyesindeki kanıtının
  arayüz tarafındaki karşılığıdır.

**Gerçek sonuç**
test-sunucu authorizationConfigurationKey: AgentPrism:Mcp:GizliTestAnahtari ile guncellendi. /agentprism/mcp listesindeki Auth sutunu TAM OLARAK "AgentPrism:Mcp:GizliTestAnahtari" (yapilandirma ANAHTARI ADI) gosterdi - hicbir gercek token/sifre DEGERI gorunmedi. MT-MCP-007'nin API seviyesindeki kanitinin arayuz karsiligi dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Güvenlik: Dış Yüzey Doğru Korunuyor, Kayıt API'si DEĞİL

### MT-MCP-050 — `/agentprism/mcp` ve `/agentprism/a2a` GRUP SEVİYESİNDE `ExternalInvoke` kapsamını doğru uygular (pozitif kontrol)

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
2. Bu anahtarla `/agentprism/mcp`'ye `tools/list` gönder.
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

**Gerçek sonuç**
HTTP: 403, title: Kapsam yetersiz, detail: Bu uc 'ExternalInvoke' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor. Pozitif kontrol dogrulandi - dis yuzeyin kendisi (/mcp) API anahtari kapsam sistemini DOGRU uyguluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- `HTTP: 200` — yalnız `RunsRead` taşıyan, `ExternalInvoke`'u OLMAYAN bir
  anahtar YENİ bir dış MCP sunucusu kaydedebilir. Bu, kodun kendi
  yorumunda `"GUVENLIK SINIRI"` diye adlandırdığı bir işlemdir
  (`GovernanceEndpoints.cs:230`).
- Doğrularsa: **Kusur, Önem: Yüksek** — MCP sunucu kaydı API anahtarı
  kapsam sisteminden TAMAMEN bağımsız çalışıyor demektir.

**Gerçek sonuç**
**KALDI - HATA-S2-009 (Yuksek, dogrulanmis supheydi).** Yalniz RunsRead kapsamli (ExternalInvoke'suz) bir anahtarla PUT /api/mcp-servers/kapsam-testi -> HTTP 200, sunucu basariyla kaydedildi. GovernanceEndpoints.cs'in MCP sunucusu KAYIT API'si (PUT/DELETE/refresh/prompts/resources/oauth - toplam 15 uc eslemesi) hicbir RequireApiKeyScope cagrisi TASIMIYOR - kodun kendi yorumunda (GovernanceEndpoints.cs:230) 'GUVENLIK SINIRI' diye adlandirilan bir islem, kapsam sisteminden TAMAMEN bagimsiz calisiyor. 00-INDEKS.md'nin izledigi kalibin (WorkflowEndpoints/SchedulingEndpoints ile) BESINCI bagimsiz tekraridir.

---

**GECTI (Aile F, docs/manuel-test/KAPANIS-PLANI.md §6).** GovernanceEndpoints.cs'in MCP sunucusu KAYIT API'sinin tum 15 uc eslemesine RequireApiKeyScope eklendi (PUT/DELETE/refresh -> AgentsAdmin; GET/prompts/resources -> AgentsRead; oauth/start -> SecurityAdmin). Canli PostgreSQL'e karsi yeniden uretildi: ayni RunsRead-kapsamli (ExternalInvoke'suz) anahtarla PUT /api/mcp-servers/kapsam-testi -> HTTP 403, title: "Kapsam yetersiz", detail: "Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor." Sunucu KAYDEDILMEDI (istek handler'a hic ulasmadan filtrede reddedildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- Örnek uygulama `AgentPrismPolicies.*` rol politikalarını HİÇ kaydetmez
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

**Gerçek sonuç**
**KALDI - HATA-S2-009 ile ayni kok neden, ikinci kanit.** Duz statik bearer token (FIX-TOKEN-01, salt-okunur run inceleme icin verilen ayni token) ile PUT /api/mcp-servers/token-kaniti -> HTTP 200. Ne RequireRole (no-op, rol politikalari kayitli degil) ne RequireApiKeyScope (hic cagrilmiyor) bu sinirlamayi koruyor - bu ortamda run'lari okumak icin verilen SIRADAN bir bearer token, agent'larin erisebilecegi KEYFI bir dis sunucuyu (potansiyel olarak kotu niyetli tool'lar sunan) sisteme ekleyebiliyor. Temizlik: her iki test sunucusu (token-kaniti, kapsam-testi) DELETE ile kaldirildi.

---

**KALDI KALIR - Aile F bu case'i KAPATMADI (docs/manuel-test/KAPANIS-PLANI.md §6/§11).** GovernanceEndpoints.cs'e RequireApiKeyScope eklendi (bkz. MT-MCP-051, artik Gecti) ama bu case'in kok nedeni FARKLIDIR: istek bir API anahtariyla degil DUZ statik AuthToken ile geliyor. AgentPrismEndpointFilter.InvokeAsync'te statik token '_authToken is { Length: > 0 } expected && BearerTokenValidator.IsValid(...)' dalinda eslesir ve dogrudan Proceed()'e gider - ApiKeyRequestContext hic kurulmaz, dolayisiyla CheckScope (ve ondaki ApiKeyScopeRequirement metadata'si) hic calismaz; bu TASARIM GEREGI boyle (bolum 53: kapsam denetimi yalniz ApiKeyRequestContext.Get() bos degilse uygulanir). Canli PostgreSQL'e karsi yeniden uretildi: ayni curl (FIX-TOKEN-01 ile PUT /api/mcp-servers/token-kaniti) Aile F SONRASI da HTTP 200 donuyor, sunucu yine kaydediliyor. Kok neden HATA-S2-009'un IKI ayri yarisidir: (1) API-anahtari kapsam boslugu - Aile F ile kapandi; (2) statik token'in rol politikasi kayitli olmayan bir ornekte fiilen tam-yetkili (root) davranmasi - bu Aile F'nin kapsami DISINDA, ayri bir bulgu olarak izlenir (yeni HATA numarasi kapanis sirasinda docs/KARARLAR.md'ye yazilacak). Temizlik: test-kaniti sunucusu DELETE ile kaldirildi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

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
1. `AgentPrismEndpointOptions.AllowRemoteAccess`'i GEÇİCİ olarak `true`
   yapacak şekilde `Program.cs`'i değiştir (loopback dışı erişimi açar).
2. `dotnet run` ile başlatmayı dene.

**Beklenen sonuç**
- Uygulama başlangıçta `InvalidOperationException` fırlatır —
  `ExternalSurfaceGuard.EnsureRemoteAccessNotCombined`, "loopback dışına
  aç" ile "yalnız tek bir statik token'la koru"nun AYNI ANDA
  olamayacağını zorlar. Bu, MT-MCP-052'nin gösterdiği boşluğun bilinçli
  olarak dar tutulduğunun (yalnız loopback'te izin verilir) kanıtıdır.
- Case sonrası `Program.cs` değişikliği GERİ ALINIR.

**Gerçek sonuç**
**Dokuman duzeltmesi (13-KIRACI-VE-GUVENLIK.md'de zaten kaydedilen ayni duzeltme).** Program.cs'te gecici kod degisikligi GEREKMEDI - AgentPrism:Ui:AllowRemoteAccess anahtari 2026-08-11'de config'e baglandi (commit 419981b). AgentPrism__Ui__AllowRemoteAccess=true ortam degiskeniyle, sistemde hicbir ExternalInvoke kapsamli anahtar yokken baslatildi. Uygulama aciliste `Unhandled exception: System.InvalidOperationException: AllowRemoteAccess acikken MCP disa acilamaz: sistemde 'external:invoke' kapsamli...` ile COKTU (ExternalSurfaceGuard.EnsureRemoteAccessNotCombined, AgentPrismMcpServerExtensions.MapAgentPrismMcpServer) - MT-MCP-052'nin gosterdigi bosluga ragmen, loopback-disi acilma icin ayri, dar tutulmus bir baslangic korumasi dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
