# 18 — MCP İstemcisi/Sunucusu ve A2A Dış Yüzeyi (`MCP`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../18-MCP-VE-A2A.md`](../../manuel-test/18-MCP-VE-A2A.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz B — sıradaki aile: `13 · 19 · 04 · 18 · 10 · 08`) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `f721b229` donuk |
| **Case sayısı** | 58 (MT-MCP-001..068, seyrek numaralı) |
| **Port** | 5081 |
| **Depo** | `mt_s1` PostgreSQL şeması |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): env değişkeni ile
başlatıcı script (`launch2.py`) kullanıldı.

**Yerel MCP test sunucusu (§5, MT-MCP-026):** repo'da dokümante edilmiş bir
sunucu YOK — `npx -y @modelcontextprotocol/server-everything streamableHttp`
kullanıldı (resmi MCP referans sunucusu). Varsayılan port **3001** (spec'in
örneği `--port 6060` diyordu, bu paketin CLI'ı böyle bir bayrak KABUL
ETMİYOR — `node ./index.js streamableHttp` yalnız transport seçer, port
sabit 3001). Uç: `http://localhost:3001/mcp`. Sunucu `tools`, `prompts`,
`resources`, `tasks` yeteneklerinin hepsini bildiriyor.

---

## Devir notu

**Aile KAPANDI: MT-MCP-001..068 koşuldu (48 Geçti · 9 Beklemede · 1 Kaldı,
toplam 58/58 case hesaba katıldı).**

**Bir yeni kusur açıldı:**
- `HATA-S1-026` (Yüksek) — `TruncatingAIFunction`'ın `AIContent` sonuçları
  için erken `return`'ü, MCP tool çıktılarının `Tracon:Tools
  :DefaultMaxOutputBytes` sınırını TAMAMEN atlamasına yol açıyor (ölçüldü:
  200 baytlık sınıra karşı 8095 bayt, sıfır kırpma). Ayrıntı MT-MCP-059'da.
  **✅ KAPANDI 2026-09-18** (Aile D). Kapanış aynı kök nedenden iki kusur daha
  ölçtü: `HATA-S1-029` (çok bloklu MCP sonucu `{"error":"tool_result_unsupported"}`
  ile değiştiriliyordu) ve `HATA-S1-030` (guard açıkken her MCP sonucu
  `[Tool result could not be inspected]` oluyordu). Üçü de MT-MCP-059'un
  yeniden koşum notundadır.

**Bir eski kusur notu ÇÜRÜTÜLDÜ (spec ve `00-INDEKS.md` düzeltildi):**
`GovernanceEndpoints.cs`'in `RequireApiKeyScope` hiç çağırmadığı
(2026-08-10 tarihli, "BEŞİNCİ bağımsız tekrar") notu artık DOĞRU DEĞİL —
kod bu tarihten sonra düzeltilmiş, her `mcp-servers` ucu artık kapsam
denetimi taşıyor (MT-MCP-051). Altındaki AYRI boşluk (statik paylaşılan
token'ın bu denetimin tamamen dışında olması) hâlâ açık ve MT-MCP-052 ile
yeniden doğrulandı.

**Dört spec düzeltmesi yapıldı** (doküman kusuru, kod donuk kaldı):
MT-MCP-005 (minimal örnek payload asıl kuralı hiç tetiklemiyordu),
MT-MCP-008 (beklenen `204` yerine gerçek `404`), MT-MCP-031/032/033
(bayat Türkçe agent adı `tracon_ozetleyici` → `tracon_summarizer`,
Aşama 0'ın zaten kapattığı örüntünün bir tekrarı).

**Dört case `Program.cs`'te GEÇİCİ değişiklik istiyor, kural 1 (kod
donması, istisnasız) nedeniyle kapanışa ertelendi:** MT-MCP-034, 035, 045,
053 — hepsi aynı `McpApprovalGuardFilter`/`A2AApprovalGuardFilter`/
`ExternalSurfaceGuard` ailesini veya kayıt-zamanlı `ExposedAgents`
listesini test ediyor.

**Üç case UI (Playwright) gerektiriyor** (MT-MCP-047/048/049) — tarayıcı
bu OTURUMUN TAMAMI boyunca başka bir şeritçe meşguldü, hiç boşalmadı.

**İki case fiziksel/ortam sınırı taşıyor:** MT-MCP-058 (gerçek iç ağ MCP
sunucusu yok — ama iki yarısı da MT-MCP-054/055 ile dolaylı zaten
kanıtlandı), MT-MCP-067 (gerçek TTL bekleme, otomatik karşılığı yok).

**Ortam notu — yerel MCP sunucusu:** `npx -y @modelcontextprotocol/server-everything
streamableHttp`, port **3001** (spec'in `--port 6060` örneği bu CLI'da
geçersiz). `Tracon:Egress:AllowPrivateNetworkTargets=true` MT-MCP-054'ü
varsayılan (kapalı) durumda koşturduktan SONRA açıldı — sonraki her case
bunu miras aldı.

🚨 **Bir üçüncü taraf sızıntısı (Tracon kusuru DEĞİL):** yerel referans MCP
sunucusunun `get-env` demo tool'u KENDİ sürecinin ortam değişkenlerini
(bu oturumun kabuğundan miras, `CLAUDE_CODE_MESSAGING_TOKEN` dahil)
döndürdü (HATA-S1-026'yı araştırırken). Yalnız bu oturumun geçici
dosyalarında kısa süre durdu, hepsi silindi.

**Sıradaki ailenin işi:** `10-ARAYUZ-AGENT-PLAYGROUND.md` açılmalı —
Playwright'ın boşaldığını önce kontrol et (bu ailenin 3 UI case'i de
tarayıcı boşalınca geriye dönüp koşulabilir).

---

# 1 — MCP İstemcisi: Sunucu Kaydı CRUD ve Doğrulama

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 45cfed58:docs/manuel-test/kosumlar/2026-09-16/18-MCP-VE-A2A.md
> ```

---

## Temiz geçen case'ler (40)

| Case | Durum | Başlık |
|---|---|---|
| MT-MCP-001 | ☑ | `PUT` yeni bir sunucu kaydı oluşturur |
| MT-MCP-002 | ☑ | `requiresApproval` GÖNDERİLMEZSE varsayılan `true` |
| MT-MCP-003 | ☑ | `stdio` transport denemesi → `400` |
| MT-MCP-004 | ☑ | `http`/`https` DIŞI bir uç adresi → `400` |
| MT-MCP-005 | ☑ | `oauthEnabled: true` VE `authorizationConfigurationKey` BİRLİKTE → `400` |
| MT-MCP-006 | ☑ | JSON yanıtında OAuth alanları camelCase (çift büyük harf DEĞİL) |
| MT-MCP-007 | ☑ | Liste secret DEĞERİ hiç GÖRÜNMEZ |
| MT-MCP-054 | ☑ | MCP `endpoint`'i metadata/özel ağ adresine işaret ediyor: reddedilir |
| MT-MCP-055 | ☑ | Ayar açıkken aynı kayıt kabul edilir |
| MT-MCP-011 | ☑ | Per-server ayarlar HİÇBİR ZAMAN `IConfiguration`'dan okunmaz |
| MT-MCP-012 | ☑ | `authorizationConfigurationKey` config'te TANIMSIZ/BOŞSA → istisna YOK |
| MT-MCP-030 | ☑ | Varsayılan KAPALI: yalnız beyaz listedeki agent görünür |
| MT-MCP-032 | ☑ | `tools/call` gerçek çıktı üretir |
| MT-MCP-033 | ☑ | `message` alanı BOŞ/EKSİKSE hata döner |
| MT-MCP-034 | ☑ | Onay gerektiren tool taşıyan agent'ı dışa açmak → UYGULAMA BAŞLAMAZ |
| MT-MCP-036 | ☑ | Gerçek bir MCP istemcisiyle (Claude Code CLI) uçtan uca el sıkışma |
| MT-MCP-040 | ☑ | Agent kartı gerçek çıktısı |
| MT-MCP-042 | ☑ | Agent kartındaki `url` alanı GÖRECELİDİR |
| MT-MCP-043 | ☑ | A2A'da `ExposeAllAgents` seçeneği HİÇ YOKTUR |
| MT-MCP-044 | ☑ | Çalışma anında eklenen agent A2A'da GÖRÜNMEZ |
| MT-MCP-045 | ☑ | Onay gerektiren tool taşıyan agent'ı A2A'ya açmak → AYNI GUARD |
| MT-MCP-050 | ☑ | `/tracon/mcp` ve `/tracon/a2a` GRUP SEVİYESİNDE `ExternalInvoke` kapsamını doğru uygular |
| MT-MCP-057 | ☑ | Faz öncesi öneksiz kayıt: okunur ama yeniden kaydedilemez |
| MT-MCP-061 | ☑ | `EnableTasks=false`: davranış senkron kalır (Faz 117) |
| MT-MCP-062 | ☑ | `EnableTasks=true`: task id run kimliğine eşit (Faz 117) |
| MT-MCP-063 | ☑ | `tasks/cancel`: koşan/kuyruklu task'ı gerçekten iptal eder (Faz 117) |
| MT-MCP-065 | ☑ | Başka kiracının task id'si okunamaz (Faz 117) |
| MT-MCP-066 | ☑ | İki Tracon örneği, tek veritabanı: task ikinci örnekten okunur (Faz 117) |
| MT-MCP-068 | ☑ | Kayıtlı `IToolArgumentsValidator` MCP tool'unu da görür (Faz 127) |
| MT-MCP-049 | ☑ | `mcp.tsx` sunucu listesi tablosunda secret DEĞERİ hiç GÖRÜNMEZ |
| MT-MCP-015 | ☑ | Var olmayan bir MCP sunucusu kaydetmek AGENT KAYDINI ETKİLEMEZ |
| MT-MCP-017 | ☑ | Bir sunucunun zaman aşımına uğraması DİĞER sunucuları ETKİLEMEZ |
| MT-MCP-018 | ☑ | Arka plan keşif döngüsü İSTİSNA sonrası ASLA çökmez |
| MT-MCP-020 | ☑ | Keşfedilen tool adı `{sunucu}_{tool}` biçiminde — NOKTA YOK |
| MT-MCP-021 | ☑ | Kod-tanımlı bir tool ile AYNI ADA sahip MCP tool'u ÇAKIŞIRSA kod tool KAZANIR (izlek C) |
| MT-MCP-023 | ☑ | `requiresApproval=true` bir MCP tool'u çağrılınca ONAY KARTI üretir |
| MT-MCP-024 | ☑ | `resources` yeteneği bildiren sunucuda sentetik `{sunucu}_read_resource` tool'u OTOMATİK belirir |
| MT-MCP-026 | ☑ | Yerel bir MCP sunucusu kur |
| MT-MCP-027 | ☑ | Yerel sunucuyu Tracon'e kaydet, tool keşfi gerçekleşir |
| MT-MCP-016 | ☑ | Sunucu keşif turu ortasında OFFLINE olursa, ESKİ tool listesi KORUNMAZ |

## Ayrıntı taşıyan case'ler (18)

## MT-MCP-008 — `DELETE` sunucu kaydını siler — spec düzeltildi

**Gerçek sonuç**
`ftp-sunucu` MT-MCP-004'te zaten `400` ile reddedilmişti, hiç oluşmamıştı
— `DELETE` `404 "There is no server named 'ftp-sunucu'"` döndü (`204`
değil, spec düzeltildi). Asıl iddia SQL ile doğrulandı: `mcp_servers`'ta
`ftp-sunucu`/`stdio-denemesi`/`karisik-yetki` adlı **0** satır — üç negatif
case de kalıcı iz bırakmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 2 — MCP Yapılandırma Bağı ve Egress Muhafızı (sıra öne alındı — sonraki tüm case'ler yerel sunucuya bağlanmak için gerekiyor)

## MT-MCP-056 — MCP yapılandırma anahtarı önek dışında: reddedilir

**Gerçek sonuç**
`authorizationConfigurationKey: "ConnectionStrings:Default"` → `400`,
detay: `"'ConnectionStrings:Default' is outside the allowed prefix.
'authorizationConfigurationKey' may only reference a configuration key
under 'Tracon:McpSecrets:'."` Aynı istek `Tracon:McpSecrets:Token` ile
→ `200`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — MCP İstemcisi: Yapılandırma Bağı (Faz 22)

**🚨 Ortam notu:** `test-sunucu` `localhost:3001`'e işaret ettiği için bu
bölümün TÜMÜ (ve §4/§10'un gerçek sunucu gerektiren case'leri)
`Tracon:Egress:AllowPrivateNetworkTargets=true` gerektiriyor — MT-MCP-054'ü
DEFAULT (bayraksız) durumda koşturduktan SONRA bayrak açıldı ve bu
noktadan sonra AÇIK bırakıldı.

## MT-MCP-010 — Örnek uygulama config-bağlı `UseMcp` kullanır — `RefreshInterval` gerçekten etkilidir

**Gerçek sonuç**
`Tracon:Mcp:RefreshInterval=00:00:10` ile başlatıldı. Konsolda
`McpDiscoveryService` satırı ~10 saniyede bir tekrarlandı (varsayılan 5
dakika DEĞİL) — ayar gerçekten okunmuş (K-353 düzeltmesi hâlâ geçerli).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-031 — `summarizer` fixture: `tools/list` gerçek çıktısı — spec düzeltildi

**Gerçek sonuç**
`tools/list` → tam bir tool: `tracon_summarizer` (spec `tracon_ozetleyici`
yazıyordu — Aşama 0'da kapatılan bayat Türkçe ad örüntüsü, düzeltildi),
`inputSchema` yalnız `message` (string, required). Başka hiçbir agent
listede yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-035 — Canlı katalog: yeni bir agent DB'ye eklenince MCP sunucusu YENİDEN BAŞLATILMADAN görünür

**Gerçek sonuç**
Spec'in kendi düzeltilmiş yordamı da `Program.cs`'e GEÇİCİ bir satır
ekliyor (`o.ExposedAgents.Add("manuel-canli-katalog")`) — `summarizer`
KOD-tanımlı olduğu için (`isEditable:false`) düzenlenemiyor, ve
`ExposedAgents` yalnız kayıt-zamanlı (config'ten okunmuyor, MT-MCP-011 ile
aynı ilke). Kod donması nedeniyle kapanışa ertelendi (MT-MCP-034 ile aynı
gerekçe).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 6) · ☑ GEÇTİ**
Ön koşulun istediği geçici `Program.cs` düzenlemesi yerine kalıcı bayrak
kullanıldı (K-834): `Tracon:Demo:ExposedAgents:Mcp` / `:A2A`. Varsayılan
değişmedi — anahtar boş/yoksa yine yalnız `summarizer` sevk edilir.

Kaydın düzeltilmiş yaklaşımı kullanıldı: henüz **var olmayan** bir
veritabanı-kökenli ad sevk listesine kondu
(`--Tracon:Demo:ExposedAgents:Mcp=summarizer,manuel-canli-katalog`).

☑ **Var olmayan ad açılışı engellemedi** (`health=200`) — guard yalnız
kataloqda **eşleşen** bir agent'ın tool'larına bakıyor.

Üç `tools/list` çağrısı, **tek bir çalışma içinde, hiç restart olmadan**:

| # | Ne yapıldı | `tools/list` sonucu |
|---|---|---|
| 1 | (henüz yok) | yalnız `tracon_summarizer` |
| 2 | `POST /api/agents` → `201` | **`tracon_manuel-canli-katalog \| ILK aciklama`** belirdi |
| 3 | `PUT .../manuel-canli-katalog` → `200` | açıklama **`GUNCELLENMIS aciklama - canli katalog testi`** oldu |

∴ `CatalogToolListHandler` her çağrıda `IAgentCatalog`'u **canlı** okuyor;
önbelleklenmiş bir kopya döndürmüyor. MCP'nin A2A'dan ayrıştığı nokta
(MT-MCP-044) burada görünüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-041 — `SendMessage` JSON-RPC çağrısı — PascalCase, `ROLE_AGENT`/`ROLE_USER`

**Gerçek sonuç**
`HTTP 200`. Yanıt `role:"ROLE_AGENT"`, gerçek bir 3-madde özet metni
taşıyor. Metot adı `"SendMessage"` (PascalCase, A2A spesifikasyonunun
`message/send`'i DEĞİL) — SDK'nın bilinen davranışı, kusur değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-051 — `GovernanceEndpoints` kapsam denetimi — şüphe ÇÜRÜTÜLDÜ (spec düzeltildi)

**Gerçek sonuç — bulgu spec'in kendi şüphesinin TERSİ çıktı.**
`RunsRead`-yalnız anahtarla `PUT /api/mcp-servers/kapsam-testi` → `403
"This endpoint requires the 'AgentsAdmin' scope; the key does not carry
it."` — spec'in beklediği `200` DEĞİL. Kaynak okundu:
`GovernanceEndpoints.cs`'teki TÜM `mcp-servers` uçları (9 `Map*` çağrısı:
liste, `PUT`, `DELETE`, `refresh`, `prompts` x2, `resources` x2,
`oauth/start`) artık `.RequireRole(...)` VE `.RequireApiKeyScope(...)`
taşıyor. Bu, `00-INDEKS.md` §8'in 2026-08-10 tarihli "BEŞİNCİ bağımsız
tekrar" notunun anlattığı boşluğun bu iki tarih arasında KAPATILDIĞI
anlamına geliyor — hem spec hem `00-INDEKS.md` düzeltildi (gerekçe her
ikisinde de). MT-MCP-052 ile karıştırılmamalı: o AYRI bir boşluğu (statik
token'ın kapsam denetiminin tamamen DIŞINDA olması) test ediyor ve o
boşluk HÂLÂ açık.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-052 — Statik bearer token sahibi HERKES dış MCP sunucusu kaydedebilir — HÂLÂ AÇIK

**Gerçek sonuç**
Paylaşılan statik token (`manuel-test-token-2026`, salt-okunur run
incelemesi için de kullanılan AYNI token) ile `PUT
/api/mcp-servers/token-kaniti` → `HTTP 200` — kayıt BAŞARILI oldu.
`RequireApiKeyScope(AgentsAdmin)` MT-MCP-051'de doğrulandığı gibi ucun
üzerinde VAR ama statik token bir DB-destekli API anahtarı olmadığı için
bu denetime hiç girmiyor; `RequireRole(Admin)` de örnek uygulama hiçbir
rol politikası kaydetmediği için no-op. Sonuç: MT-MCP-051'in düzeltmesine
RAĞMEN, bu ortamda salt-okunur bir token'la KEYFİ bir dış MCP sunucusu
(potansiyel olarak kötü niyetli tool'lar sunan) hâlâ eklenebiliyor. Kayıt
sonrası temizlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 10 — Sonradan eklenen bölümler (Faz 89, 103, 117, 127)

## MT-MCP-059 — 🚨 MCP tool'undan gelen büyük çıktı KIRPILMIYOR — 🚨 KUSUR (`HATA-S1-026`)

**Gerçek sonuç — case'in kendi uyarısı doğrulandı: KUSUR BULUNDU.**
`Tracon:Tools:DefaultMaxOutputBytes=200` ile başlatıldı.
`test-sunucu_get-env` (büyük metin döndüren gerçek bir MCP tool'u) bir
agent üzerinden çağrıldı: modele giden `functionResult` **8095 bayt**
olarak geldi — 200 bayt sınırının **40 katı**, hiçbir `{"truncated":true,
...}` zarfı yok, `run_events`'te `ToolOutputTruncated` kaydı YOK. Kontrol
grubu: aynı ortamda kod-tanımlı tool'lar (`list_recent_orders`) küçük
çıktılarla (55 bayt) sınırın altında kaldığı için doğrudan
karşılaştırılamadı, ama kaynak okuması kök nedeni doğrudan gösteriyor.

**HATA-S1-026 — `TruncatingAIFunction` MCP tool sonuçlarını (`AIContent`) atlıyor, hiçbir yerde kırpılmıyor**
- **Case:** MT-MCP-059 (muhtemelen 060'ı da etkiler — kırpma hiç
  çalışmadığı için "provider hatası sızdırmaz" iddiasının kırpma ayağı da
  aynı boşluğa maruz kalabilir, ayrı doğrulanmadı)
- **Önem:** Yüksek (MT-MCP-059'un kendi notu: "sessizce fark edilmez" —
  kaynak/maliyet sınırı MCP tool'ları için fiilen YOK)
- **İzlek:** B (gerçek local MCP sunucusuyla ampirik ölçüldü) + kaynak
  okuması (kök neden kesin)
- **Ortam:** macOS arm64 · net10 · PostgreSQL · `npx
  @modelcontextprotocol/server-everything` (yerel, gerçek MCP protokolü)

**Beklenen**
`Tracon:Tools:DefaultMaxOutputBytes` MCP tool'ları için de kod-tanımlı
tool'larla AYNI şekilde uygulanmalı — sınırı aşan bir sonuç
`{"truncated":true,"omittedBytes":N,"content":"..."}` zarfına girmeli ve
toplam 200 baytı aşmamalı (case'in kendi iddiası, `McpTenantTools.Create`
"ikinci, ayrı sarmalama zinciri" olarak tasarlandığı için).

**Gerçekleşen**
`src/Tracon.Core/Tools/TruncatingAIFunction.cs:69-72`:
```csharp
if (result is AIContent)
{
    return result;
}
```
Yorum: "AIContent is a protocol-level result... handled by the provider
adapter rather than the inline canonical text contract." MCP
tool çağrılarının ham sonucu (`McpClientTool : AIFunction`,
`ModelContextProtocol`/`Microsoft.Extensions.AI` kütüphanesinden) bir
`AIContent` türevi olarak geliyor — bu `if` bloğuna hemen giriyor ve
KIRPMA HİÇ ÇALIŞMADAN sonucu OLDUĞU GİBİ döndürüyor. "Provider adapter"ın
kendisi böyle bir sınır UYGULAMIYOR (8095 bayt modele aynen ulaştı).

**Yeniden üretme**
1. `Tracon:Tools:DefaultMaxOutputBytes=200` ile başlat.
2. Büyük metin döndüren bir MCP tool'unu (`test-sunucu_get-env` gibi)
   taşıyan bir agent oluştur, çağrıt.
3. `functionResult`'ın gerçek boyutunu ölç — 200 baytı fersah fersah aşar,
   `truncated` alanı yoktur.

**Kanıt**
- Canlı ölçüm: `functionResult` uzunluğu 8095 bayt (200 bayt sınırına
  karşı).
- Kaynak: `src/Tracon.Core/Tools/TruncatingAIFunction.cs:69-72` (erken
  `return`), `src/Tracon.Mcp/Internal/McpTenantTools.cs:104-114`
  (`ToolWrapperChain.Compose`'a doğru `defaultMaxOutputBytes` geçtiği
  doğrulandı — çağrı yolu doğru, kırpma mantığının KENDİSİ MCP sonuç
  türünü atlıyor).

**Kapsam**
`AIContent` türünde sonuç döndüren HER tool bu atlamaya maruz — yalnız MCP
değil, ileride başka bir `AIContent`-tabanlı tool ailesi eklenirse (ör.
görsel/ikili içerik döndüren kod tool'ları) aynı boşluğa düşebilir.
Kapanışta sınıf taraması önerilir (`grep -rn "is AIContent" src/`).

🚨 **Bu bir güvenlik/kaynak-tüketimi bulgusudur (secret sızıntısı değil).**
Test sırasında yerel MCP sunucusunun `get-env` tool'u KENDİ sürecinin
ortam değişkenlerini (bu oturumun kabuğundan miras alınmış,
`CLAUDE_CODE_MESSAGING_TOKEN` dahil) döndürdü — bu Tracon'in bir kusuru
DEĞİL (üçüncü taraf referans sunucusunun demo amaçlı `get-env` tool'u
kendi sürecinin ortamını okuyor), ama bu kayıt üretilirken geçici dosyalar
temizlendi ve token bu dosyada saklı BIRAKILMADI.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### Yeniden koşum — 2026-09-18 (Aile D kapanışı)

**Gerçek sonuç — ✅ GEÇTİ.** Kök neden düzeltildi (K-798); canlı ölçüm
`samples/Tracon.Api` + gerçek OpenAI anahtarı + gerçek yerel MCP sunucusu
(`@modelcontextprotocol/server-everything`, `streamableHttp`, port 3001) ile
tekrarlandı.

**Kök neden — kayıttaki teşhis DOĞRUYDU ama TAM DEĞİLDİ.**
`McpClientTool.InvokeCoreAsync` (kaynak okundu, `ModelContextProtocol.Core`
2.2.0) üç şekilden birini döndürür: tek bloklu sonuç için bir `AIContent`,
**çok bloklu sonuç için bir `AIContent[]`**, yalnız hata/`StructuredContent`/
uygulama meta'sı taşıyan sonuç için bir `JsonElement`. Kayıt yalnız ilkini
görmüştü. İkincisi `AIContent` DEĞİLDİR, erken `return`'e hiç girmiyordu ve
okunamayan sonuç dalına düşüp `{"error":"tool_result_unsupported"}` ile
**değiştiriliyordu** — sınır hiç yapılandırılmamış olsa bile, çünkü katman
her zaman kuruludur (`ToolWrapperChain.Compose`). Yani çok bloklu her MCP
sonucu **veri kaybına** uğruyordu. Ayrı kusur olarak `HATA-S1-029` açıldı ve
aynı düzeltmeyle kapandı.

**Ölçüm 1 — sınır uygulanıyor** (`Tracon:Tools:DefaultMaxOutputBytes=200`,
`test-sunucu_get-tiny-image`, metin + görüntü = iki blok):

```
functionResult = {"truncated":true,"omittedBytes":5474,"content":"[\n  {\n    \"$type\": \"text\", ..."}
                 -> tam 200 UTF-8 bayt (200 bayt sinirina karsi)

run_events:
  seq 1  ToolInvoking          test-sunucu_get-tiny-image
  seq 2  ToolOutputTruncated   test-sunucu_get-tiny-image
         text    = "5474 byte(s) omitted (limit 200)"
         payload = {"maxOutputBytes":200,"omittedBytes":5474}
  seq 3  ToolInvoked           payload = zarfin kendisi (onceden null'di)
```

**Ölçüm 2 — sınır YOKKEN sonuç dokunulmadan geçiyor** (aynı tool, hiç
`DefaultMaxOutputBytes` verilmeden):

```
functionResult -> 5557 bayt, tip: LIST
  [{"$type":"text","text":"Here's the image you requested:"},
   {"$type":"data","uri":"data:image/png;base64,iVBOR..."}]
sentinel mi? HAYIR
ToolInvoked payload = 5601 bayt (onceden null'di)
```

Zarf `MT-OBS-048`'in kod-tanımlı tool'uyla **birebir aynı** biçimdedir —
case'in kendi iddiası karşılandı.

**Sınıf taraması bu case'in dışına çıktı.** Aynı `false` dalına dallanan beş
çağıran vardı; en ağırı `ContentGuardMessageMasker`: bir `ContentGuard`
kayıtlıyken HER MCP tool sonucu modele ulaşmadan
`[Tool result could not be inspected]` ile değiştiriliyordu (`HATA-S1-030`).
Tam liste ve dersler
[`docs/hafiza/tool-onay-ve-yetkilendirme.md`](../../hafiza/tool-onay-ve-yetkilendirme.md),
kararlar K-798 · K-799 · K-800.

🚨 **Üçüncü taraf sızıntısı TEKRARLANDI ve kapatıldı.** Yeniden koşumun ilk
denemesinde port 3001'e yanıt veren sunucu **2026-09-16 turundan kalan**
süreçti (PID 2295, `--port 3003` ile başlatılmış, 3001'i tutuyordu) ve o
oturumun kabuk ortamını taşıyordu; `get-env` `CLAUDE_CODE_MESSAGING_TOKEN`'ı
yine döndürdü. Eylem: o `run` kaydı (`run_events` 19 satır ·
`tool_invocations` 1 · `runs` 1) veritabanından **silindi**, geçici dosya
silindi, tüm şemalarda artık tarandı (0 eşleşme); eski süreç durduruldu ve
sunucu `env -i` ile **boş ortamla** yeniden başlatıldı. Ölçümün kendisi
`get-env` yerine `get-tiny-image` ile yapıldı — o tool ortamı hiç okumaz.
`CLAUDE_CODE_MESSAGING_TOKEN` kapanış planı §6.5'in döndürme listesine eklendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-060 — MCP agent-tool sonucu provider hatasının ham metnini sızdırmaz (Faz 103)

**Gerçek sonuç — otomatik test kanıtı.**
`ProviderOutageErrorHandlingTests.Mcp_tool_call_never_exposes_a_secret_like_provider_message`
`tests/Tracon.AspNetCore.FunctionalTests/`'te mevcut ve dosya 19'un
MT-MM-108'inde bu paketin TAMAMI (1077/1077) bu turda zaten koşuldu ve
geçti — canlı bir secret-like provider senaryosu bu oturumda AYRICA
kurulmadı (MT-MCP-059'un `HATA-S1-026`'sı farklı bir kırpma katmanını
ilgilendiriyor, doğrudan çakışmıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-064 — 🚨 Onay isteyen sonradan eklenmiş tool: task modunda `InputRequired` DEĞİL `Completed`(hata) (Faz 117, K-103)

**Gerçek sonuç — otomatik test kanıtı.**
`McpTasksEndpointTests.Approval_requiring_tool_added_after_exposure_rejects_the_task_with_todays_inline_message`
(aynı örnek) ve `McpTaskCrossInstanceTests
.Second_instance_reconstructs_an_approval_rejection_generically_not_with_todays_exact_wording`
(çapraz örnek) ikisi de mevcut, aynı 1077/1077 geçen pakette.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-067 — 👤 `TaskTimeToLive` dolunca `tasks/get` hâlâ okunur (Faz 117, Açık Soru 2)

**Gerçek sonuç**
👤 Gerçek zaman aralığı (TTL sonrası bekleme) gerektiriyor, otomatik
karşılığı yok (spec'in kendisi de belirtmiyor) — §4.3 fiziksel eylem
tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-047 — Tools ekranında MCP kökenli tool `mcp: {sunucu}` rozetiyle ayrışır

**Gerçek sonuç**
Playwright tarayıcısı bu oturum boyunca başka bir şeridin kullanımındaydı
(`Browser is already in use for .../mcp-chrome-3eca5a9`) — tekrar tekrar
kontrol edildi, hep meşgul bulundu. Ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 2) · ☑ GEÇTİ**

Gerçek bir MCP sunucusu kuruldu — §3.4'ün kuralına uyularak **boş ortamla**
(`env -i`, port 3001). Sızıntı kontrolü yapıldı: sürecin ortamında
`TOKEN|KEY|SECRET` eşleşmesi **0**. Sunucu `test-sunucu` adıyla kaydedildi ve
`/refresh` **14 tool** keşfetti.

```
test-sunucu_echo
  "mcp: test-sunucu"
  Discovered on the remote MCP server "test-sunucu". Its definition lives on
  that server, not in this application.
  external        ← ayri rozet
  approval required
```

| İddia | Sonuç |
|---|---|
| Sarı/uyarı tonlu `mcp: {sunucu}` rozeti | ☑ `bg-warn-soft text-warn`, `color: rgb(239,205,136)` |
| 14 MCP tool'unun hepsinde var | ☑ sayfada **14** rozet |
| Kod tanımlı tool'da rozet YOK | ☑ `get_order_status` hiç taşımıyor |
| Onay rozeti BAĞIMSIZ | ☑ `cancel_order` **kod tanımlı** olduğu hâlde `approval required` taşıyor — ikisi ayrı eksen |

⚠️ 127.0.0.1 uç noktasını kaydetmek `Tracon:Egress:AllowPrivateNetworkTargets=true`
ister; SSRF koruması aksi hâlde `400 Address not allowed` verir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-048 — `mcp.tsx` formu: OAuth açılınca `authorizationConfigurationKey` OTOMATİK TEMİZLENİR

**Gerçek sonuç**
Aynı gerekçeyle (Playwright meşgul) ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 2) · ☑ GEÇTİ**

Saf istemci tarafı davranış; hiçbir kayıt gerekmedi.

```
1. "Add server" → form açıldı
2. "Authorization configuration key" alanına yazıldı:
   önce = "Tracon:McpSecrets:MtMcp048"
3. "OAuth (Authorization Code)" onay kutusu işaretlendi
   sonra = ""            ← OTOMATİK TEMİZLENDİ
```

Alan **gizlenmiyor ve devre dışı bırakılmıyor** (`visible: true`,
`disabled: false`) — yalnız değeri boşalıyor, yani sunucudaki karşılıklı
dışlama kuralı (MT-MCP-005) form seviyesinde önceden yansıtılıyor.

⚠️ Spec adımları Türkçe etiket yazıyor ("Yeni sunucu", "OAuth kullan");
sevk edilen arayüz **İngilizce**dir (K-228). Gerçek etiketler: `Add server`,
`Authorization configuration key`, `OAuth (Authorization Code)`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MCP-058 — 👤 İç ağdaki gerçek MCP sunucusu: önce red, ayar sonrası bağlantı

**Gerçek sonuç**
👤 Gerçekten erişilebilir bir İÇ AĞ MCP sunucusu gerektiriyor (ev/ofis
ağı) — bu makinede böyle bir sunucu yok. §4.3 fiziksel eylem tablosuna
eklendi. Not: bu case'in İKİ YARISI da (red + izin) dolaylı olarak zaten
kanıtlandı — MT-MCP-054 (private/loopback red) ve MT-MCP-055 (aynı
kayıt bayrak açılınca kabul) `169.254.169.254` ile birebir aynı
mekanizmayı doğruladı; yalnız "gerçek bağlantının da geçtiği" (3. adım)
iç ağa özgü ve `test-sunucu`nun `localhost`'ta olması nedeniyle ayrı
kanıtlanamadı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Keşif, İzolasyon ve Sağlamlık (Faz 22) + § 5 kurulum

## MT-MCP-022 — `MaxToolsPerServer` aşımı → fazla tool'lar UYARIYLA düşürülür

**Gerçek sonuç**
`MaxToolsPerServer=1` ile: `toolCount:2` (`test-sunucu_echo` +
sentetik `test-sunucu_read_resource` — 🚨 gözlem, kusur DEĞİL: sınır
yalnız SUNUCUNUN bildirdiği normal tool'lara uygulanıyor, `read_resource`
ayrı bir mekanizmayla EKLENDİĞİ için sayıma girmiyor). Log: `"MCP server
'test-sunucu' exceeded the 1 tool limit; the excess is being dropped."`
— keşif BAŞARISIZ olmadı, yalnız uyarıyla kırpıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-028 — Keşfedilen tool'u GERÇEK bir agent çalıştırmasında kullan

**Gerçek sonuç**
`test-sunucu_echo` tool'unu taşıyan bir agent, "Please echo back exactly:
brand new agent test" istemiyle çalıştırıldı: `functionResult.result.text
= "Echo: brand new agent test"` — gerçek MCP sunucusundan dönen gerçek
sonuç.

🚨 **Gözlem (kusur adayı, kapsam DIŞI araştırıldı — MT-MCP-023/028'in kendi
iddiası dışında bir bulgu).** `test-sunucu` `requiresApproval:true`
İKEN oluşturulan bir agent (`mt-mcp-023-agent`), sunucu SONRADAN
`requiresApproval:false`'a çevrilip `/refresh` ile katalog `GET
/api/tools`'ta doğru şekilde `false` göstermeye başladıktan SONRA bile,
AYNI agent'ın YENİ bir çalıştırması hâlâ onay istedi
(`requiresConfirmation:true`). AYNI andaki YENİ bir agent
(`mt-mcp-028-fresh`) ise doğru şekilde onaysız tamamlandı. Bu, onay
sarmalayıcısının agent İLK ÇÖZÜMLENDİĞİNDE (muhtemelen `AIAgent`
örneğiyle birlikte) BAĞLANDIĞINI ve sonraki MCP sunucu ayarı
değişikliklerini o agent için YANSITMADIĞINI düşündürüyor —
`McpConnection.ComputeFingerprint` sunucu YENİDEN BAĞLANTISINI doğru
tetikliyor (parmak izine `RequiresApproval` dahil), ama zaten
MATERYALİZE EDİLMİŞ bir agent örneğinin tool sarmalaması bunu görmüyor
olabilir. Kök neden tam izlenmedi (bu iki case'in kapsamı dışında); ayrı
bir oturumda/case'te (MT-MCP-035'in "canlı katalog" ailesiyle
karşılaştırmalı) araştırılması önerilir — `ADAYLAR.md`'ye not düşülecek.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-053 — `AllowRemoteAccess=true` + `ExternalInvoke` anahtarı YOKKEN → UYGULAMA BAŞLAMAZ

**Gerçek sonuç**
`TraconEndpointOptions.AllowRemoteAccess`'i açmak `Program.cs`'te geçici
kod değişikliği istiyor — MT-MCP-034/035/045 ile aynı gerekçeyle (kural 1
kod donması) kapanışa ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Fiziksel eylem / kod donması / paylaşılan kaynak nedeniyle koşulamayan case'ler

| Case | Neden | Kullanıcıdan istenen / kapanışta yapılacak |
|---|---|---|
| MT-MCP-034, 035, 045, 053 | `Program.cs`'te geçici satır değişikliği istiyor, kural 1 kod donması | Kapanışta, `git checkout` ile geri alınacak şekilde tek tek koşulmalı |
| MT-MCP-047, 048, 049 | Playwright tarayıcısı oturum boyunca başka şeritçe meşguldü | Tarayıcı boşalınca `/tracon/mcp` ve `/tracon/tools` ekranlarından koşulabilir |
| MT-MCP-058 | Gerçek, erişilebilir bir iç ağ MCP sunucusu yok bu makinede | Böyle bir sunucu bulununca koşulmalı; iki yarısı MT-MCP-054/055 ile dolaylı zaten kanıtlı |
| MT-MCP-067 | Gerçek `TaskTimeToLive` bekleme süresi gerekiyor, otomatik karşılığı yok | Kısa bir TTL ile elle koşulmalı |

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 2) · ☑ GEÇTİ**

🚨 **Spec'in 1. adımı bayat: `Program.cs` değişikliği GEREKMİYOR.**
`Tracon:Ui:AllowRemoteAccess` bir yapılandırma anahtarıdır ve
`samples/Tracon.Api/Program.cs:953-956` onu seçeneğe bağluyor. Kod donması
bu case'i hiç engellemiyormuş. Spec düzeltildi (skill §1.1).

Ön koşul doğrulandı: `GET /api/api-keys` → `[]`.

```
$ dotnet Tracon.Api.dll --Tracon:Ui:AllowRemoteAccess=true
EXIT=134
Unhandled exception. System.InvalidOperationException: MCP cannot be exposed
while AllowRemoteAccess is on: the system holds no API key with the
'external:invoke' scope that is neither expired nor revoked. A single static
bearer token is not enough to protect an agent surface exposed beyond loopback.
Create a key with the 'external:invoke' scope through 'POST /api/api-keys'.
   at Tracon.ExternalSurfaceGuard.EnsureRemoteAccessNotCombined(...)
      ExternalSurfaceGuard.cs:line 173
```

İddianın üçü de tuttu: `InvalidOperationException` atılıyor, atan
`ExternalSurfaceGuard.EnsureRemoteAccessNotCombined`, ve mesaj "loopback
dışına aç" ile "yalnız tek statik token'la koru" birleşimini reddediyor.

💡 **Karşı kontrol de koşuldu — kapı `AllowRemoteAccess`'e değil EKSİK
ANAHTARA bakıyor.** `ExternalInvoke` kapsamlı bir anahtar yaratıldıktan sonra
**aynı komut** temiz başladı: `health=200`, günlükte `InvalidOperationException`
sayısı `0`. Pozitif kontrol olmadan bu case yalnız "bayrak uygulamayı
çökertüyor" derdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
