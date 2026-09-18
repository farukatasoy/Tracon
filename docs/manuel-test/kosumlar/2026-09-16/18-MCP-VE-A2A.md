# 18 — MCP İstemcisi/Sunucusu ve A2A Dış Yüzeyi (`MCP`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../18-MCP-VE-A2A.md`](../../18-MCP-VE-A2A.md)
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

## MT-MCP-001 — `PUT` yeni bir sunucu kaydı oluşturur

**Gerçek sonuç**
`HTTP: 200`, `id` dolu GUID, `requiresApproval: false`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-002 — `requiresApproval` GÖNDERİLMEZSE varsayılan `true`

**Gerçek sonuç**
`True` yazdırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-003 — `stdio` transport denemesi → `400`

**Gerçek sonuç**
`HTTP: 400` — `"The JSON value could not be converted to
Tracon.McpTransportMode"` (enum bağlama seviyesinde reddedildi, `Stdio`
tanımlı bir değer değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-004 — `http`/`https` DIŞI bir uç adresi → `400`

**Gerçek sonuç**
`HTTP: 400` — `"Address scheme not supported"`, detay stdio'nun kasıtlı
desteklenmediğini açıklıyor ("starting a process on the server would break
the rule that tools are defined in code only").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-005 — `oauthEnabled: true` VE `authorizationConfigurationKey` BİRLİKTE → `400`

**Gerçek sonuç — üç deneme gerekti (validasyon sırası).** İlk deneme
(spec'in kendi örneği) `Tracon:Mcp:BirTest` anahtarıyla `400` verdi ama
gerekçesi FARKLI bir kuraldı ("Configuration key not allowed... outside
allowed prefix" — MT-MCP-056'nın konusu). Anahtarı `Tracon:McpSecrets:`
altına taşıyınca `400 "OAuth client id missing"` (`oauthClientId` da
zorunlu). Her iki eksik alan tamamlanınca ASIL kural görüldü: `400
"Conflicting authentication" — "'authorizationConfigurationKey' must be
empty when OAuth is enabled; both would try to manage the same
Authorization header."` — case'in iddiası doğrulandı, yalnız minimal
örnek payload doğru dalı hiç tetiklemiyordu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-006 — JSON yanıtında OAuth alanları camelCase (çift büyük harf DEĞİL)

**Gerçek sonuç**
`"oauthEnabled":true, "oauthClientId":"test-client",
"oauthClientSecretConfigurationKey":"Tracon:McpSecrets:TestSecret",
"oauthScopes":"read"` — hepsi doğru camelCase, `"oAuthEnabled"` gibi bir
regresyon yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-007 — Liste secret DEĞERİ hiç GÖRÜNMEZ

**Gerçek sonuç**
`authorizationConfigurationKey`/`oauthClientSecretConfigurationKey`
alanları yalnız anahtar ADI taşıyor (`"Tracon:McpSecrets:TestSecret"`) —
hiçbir gerçek secret değeri yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-008 — `DELETE` sunucu kaydını siler — spec düzeltildi

**Gerçek sonuç**
`ftp-sunucu` MT-MCP-004'te zaten `400` ile reddedilmişti, hiç oluşmamıştı
— `DELETE` `404 "There is no server named 'ftp-sunucu'"` döndü (`204`
değil, spec düzeltildi). Asıl iddia SQL ile doğrulandı: `mcp_servers`'ta
`ftp-sunucu`/`stdio-denemesi`/`karisik-yetki` adlı **0** satır — üç negatif
case de kalıcı iz bırakmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

# 2 — MCP Yapılandırma Bağı ve Egress Muhafızı (sıra öne alındı — sonraki tüm case'ler yerel sunucuya bağlanmak için gerekiyor)

## MT-MCP-054 — MCP `endpoint`'i metadata/özel ağ adresine işaret ediyor: reddedilir

**Gerçek sonuç**
`169.254.169.254` → `400`, detay: `"The target resolves to a private
network address (169.254.169.254); set
'Tracon:Egress:AllowPrivateNetworkTargets' to true to allow it."` Ayrıca
`10.0.0.5:8080`, `192.168.1.10`, `127.0.0.1:9000`, NAT64
`[64:ff9b::a9fe:a9fe]` — dördü de `400`. Ad taşıyan `https://mcp.example.com/`
→ `200` (DNS kayıt anında çözülmüyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-055 — Ayar açıkken aynı kayıt kabul edilir

**Gerçek sonuç**
`Tracon:Egress:AllowPrivateNetworkTargets=true` ile yeniden başlatılınca
AYNI `169.254.169.254` isteği `200` döndü — tek satırlık yükseltme yolu
doğrulandı, ayar gerçekten bağlanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-MCP-011 — Per-server ayarlar HİÇBİR ZAMAN `IConfiguration`'dan okunmaz

**Gerçek sonuç**
`Tracon:Mcp:Servers:0:Endpoint=http://olmayan-bir-yer/mcp` env değişkeni
ile başlatıldı; `GET /api/mcp-servers` listesi yalnız DB'deki üç kaydı
gösterdi (`oauth-test`, `test-sunucu`, `varsayilan-onay`) — "olmayan-bir-yer"
hiç görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-012 — `authorizationConfigurationKey` config'te TANIMSIZ/BOŞSA → istisna YOK

**Gerçek sonuç**
`test-sunucu` `Tracon:McpSecrets:HicVarOlmayanAnahtar` (tanımsız) ile
güncellendi. `POST /refresh` → `200 {"toolCount":14}` — çökme yok, tarama
tam 14 tool'la tamamlandı. Log: `"Configuration key
'Tracon:McpSecrets:HicVarOlmayanAnahtar' for MCP server 'test-sunucu' is
empty. No authentication header will be sent."` Anahtar sonra geri alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — MCP Sunucusu: `summarizer` Fixture (Faz 50)

## MT-MCP-030 — Varsayılan KAPALI: yalnız beyaz listedeki agent görünür

**Gerçek sonuç**
`tools/list` TAM OLARAK bir tool döndü. `Bos_beyaz_liste_hicbir_tool_dondurmez`
testi (`McpServerEndpointTests.cs:14`) bu genel garantiyi zaten kapsıyor;
bu koşum örnek uygulamanın YALNIZ `summarizer`'ı açtığını doğruladı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-031 — `summarizer` fixture: `tools/list` gerçek çıktısı — spec düzeltildi

**Gerçek sonuç**
`tools/list` → tam bir tool: `tracon_summarizer` (spec `tracon_ozetleyici`
yazıyordu — Aşama 0'da kapatılan bayat Türkçe ad örüntüsü, düzeltildi),
`inputSchema` yalnız `message` (string, required). Başka hiçbir agent
listede yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-032 — `tools/call` gerçek çıktı üretir

**Gerçek sonuç**
`HTTP 200`, `result.content[0].text` gerçek bir 3-madde özet
("- Bugün hava çok güzeldi. - İş yerinde her şey yolunda gitti. - Toplantılar
verimli geçti."). `GET /api/runs?agentName=summarizer` yeni bir kök run
gösterdi (`depth:0, status:Completed`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-033 — `message` alanı BOŞ/EKSİKSE hata döner

**Gerçek sonuç**
`{"isError":true, "content":[{"text":"'message' argument cannot be empty."}]}`
— boş mesajla agent çalıştırılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-034 — Onay gerektiren tool taşıyan agent'ı dışa açmak → UYGULAMA BAŞLAMAZ

**Gerçek sonuç**
`samples/Tracon.Api/Program.cs`'te GEÇİCİ değişiklik istiyor — kural 1
kod donmasını hiçbir istisna olmadan koşum boyunca zorunlu kılıyor
(MT-MM-120-122 ile aynı emsal, dosya 19). Kapanışa ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-035 — Canlı katalog: yeni bir agent DB'ye eklenince MCP sunucusu YENİDEN BAŞLATILMADAN görünür

**Gerçek sonuç**
Spec'in kendi düzeltilmiş yordamı da `Program.cs`'e GEÇİCİ bir satır
ekliyor (`o.ExposedAgents.Add("manuel-canli-katalog")`) — `summarizer`
KOD-tanımlı olduğu için (`isEditable:false`) düzenlenemiyor, ve
`ExposedAgents` yalnız kayıt-zamanlı (config'ten okunmuyor, MT-MCP-011 ile
aynı ilke). Kod donması nedeniyle kapanışa ertelendi (MT-MCP-034 ile aynı
gerekçe).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-036 — Gerçek bir MCP istemcisiyle (Claude Code CLI) uçtan uca el sıkışma

**Gerçek sonuç**
`claude mcp add --transport http tracon-manuel-test
http://localhost:5081/tracon/mcp -s local --header "Authorization: Bearer
manuel-test-token-2026"` → `claude mcp get tracon-manuel-test` →
**`Status: ✔ Connected`**. Tracon'in MCP yüzeyi bağımsız, gerçek bir
istemciyle (Claude Code CLI) doğrulandı. Case sonrası `claude mcp remove
tracon-manuel-test -s local` ile temizlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — A2A Dış Yüzeyi (Faz 50)

## MT-MCP-040 — Agent kartı gerçek çıktısı

**Gerçek sonuç**
`name:"summarizer"`, `capabilities:{streaming:false,
pushNotifications:false}`, `defaultInputModes/OutputModes:["text/plain"]`,
`supportedInterfaces[0].url:"/tracon/a2a/summarizer"`,
`protocolBinding:"JSONRPC"` — tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-041 — `SendMessage` JSON-RPC çağrısı — PascalCase, `ROLE_AGENT`/`ROLE_USER`

**Gerçek sonuç**
`HTTP 200`. Yanıt `role:"ROLE_AGENT"`, gerçek bir 3-madde özet metni
taşıyor. Metot adı `"SendMessage"` (PascalCase, A2A spesifikasyonunun
`message/send`'i DEĞİL) — SDK'nın bilinen davranışı, kusur değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-042 — Agent kartındaki `url` alanı GÖRECELİDİR

**Gerçek sonuç**
`supportedInterfaces[0].url = "/tracon/a2a/summarizer"` — MT-MCP-040'ın
kendi çıktısından doğrulandı, mutlak URL değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-043 — A2A'da `ExposeAllAgents` seçeneği HİÇ YOKTUR

**Gerçek sonuç**
`support` için agent kartı isteği `404`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-044 — Çalışma anında eklenen agent A2A'da GÖRÜNMEZ

**Gerçek sonuç**
`yeni-a2a-adayi` `POST` ile oluşturuldu (`201`), A2A agent kartı isteği
`404` — `AddA2AServer` kayıt-zamanlı, çalışan uygulamaya sonradan eklenen
agent'ı hiç göremiyor. MCP'nin tam tersi (MT-MCP-035 ile karşılaştır —
MCP tarafı zaten ExposedAgents listesindeki bir agent'ın ÖZELLİKLERİNİ
canlı okuyor, ama YENİ bir agent'ı listeye sonradan ekleyemiyor; A2A'da
liste hiç yok, yalnız kayıt-zamanlı sabit isimler var).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-045 — Onay gerektiren tool taşıyan agent'ı A2A'ya açmak → AYNI GUARD

**Gerçek sonuç**
MT-MCP-034 ile aynı gerekçeyle (`Program.cs` geçici değişikliği, kod
donması) kapanışa ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Rol/API Anahtarı Kapsamı (sıra öne alındı — egress bayrağıyla birlikte)

## MT-MCP-050 — `/tracon/mcp` ve `/tracon/a2a` GRUP SEVİYESİNDE `ExternalInvoke` kapsamını doğru uygular

**Gerçek sonuç**
`RunsRead`-yalnız anahtarla `tools/list` → `403 "This endpoint requires
the 'ExternalInvoke' scope; the key does not carry it."` Kontrol:
`ExternalInvoke`-kapsamlı ikinci bir anahtarla AYNI çağrı → `200`, gerçek
tool listesi döndü.

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

## MT-MCP-057 — Faz öncesi öneksiz kayıt: okunur ama yeniden kaydedilemez

**Gerçek sonuç**
DB'ye elle (SQL) `authorization_configuration_key='Tracon:Mcp:LegacyToken'`
(önek dışı) taşıyan bir kayıt (`legacy-oneksiz`) eklendi. `GET
/api/mcp-servers` → `200`, kayıt LİSTEDE (okuma etkilenmedi, anahtar
olduğu gibi görünüyor). Aynı kaydı DEĞİŞTİRMEDEN `PUT` ile tekrar
kaydetmeyi denemek → `400 "'Tracon:Mcp:LegacyToken' is outside the
allowed prefix. 'authorizationConfigurationKey' may only reference a
configuration key under 'Tracon:McpSecrets:'."` — hem alan adını hem
izinli öneki taşıyor. Test satırı temizlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-MCP-060 — MCP agent-tool sonucu provider hatasının ham metnini sızdırmaz (Faz 103)

**Gerçek sonuç — otomatik test kanıtı.**
`ProviderOutageErrorHandlingTests.Mcp_tool_call_never_exposes_a_secret_like_provider_message`
`tests/Tracon.AspNetCore.FunctionalTests/`'te mevcut ve dosya 19'un
MT-MM-108'inde bu paketin TAMAMI (1077/1077) bu turda zaten koşuldu ve
geçti — canlı bir secret-like provider senaryosu bu oturumda AYRICA
kurulmadı (MT-MCP-059'un `HATA-S1-026`'sı farklı bir kırpma katmanını
ilgilendiriyor, doğrudan çakışmıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-061 — `EnableTasks=false`: davranış senkron kalır (Faz 117)

**Gerçek sonuç — otomatik test kanıtı.**
`McpTasksEndpointTests.EnableTasks_false_still_answers_synchronously`
mevcut, dosya 19'un MT-MM-108'inde TAM koşulan `Tracon.AspNetCore
.FunctionalTests` (1077/1077) paketinin parçası.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-062 — `EnableTasks=true`: task id run kimliğine eşit (Faz 117)

**Gerçek sonuç — otomatik test kanıtı.**
`McpTasksEndpointTests.EnableTasks_true_creates_a_task_whose_id_is_the_run_id`
ve `Tasks_get_transitions_from_working_to_completed_with_the_run_output`
ikisi de mevcut, aynı 1077/1077 geçen pakette.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-063 — `tasks/cancel`: koşan/kuyruklu task'ı gerçekten iptal eder (Faz 117)

**Gerçek sonuç — otomatik test kanıtı.**
`McpTasksEndpointTests.Tasks_cancel_of_a_running_task_actually_cancels_the_in_flight_tool_call`
ve `Tasks_cancel_of_a_queued_task_closes_the_row_directly` ikisi de
mevcut, aynı 1077/1077 geçen pakette.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-064 — 🚨 Onay isteyen sonradan eklenmiş tool: task modunda `InputRequired` DEĞİL `Completed`(hata) (Faz 117, K-103)

**Gerçek sonuç — otomatik test kanıtı.**
`McpTasksEndpointTests.Approval_requiring_tool_added_after_exposure_rejects_the_task_with_todays_inline_message`
(aynı örnek) ve `McpTaskCrossInstanceTests
.Second_instance_reconstructs_an_approval_rejection_generically_not_with_todays_exact_wording`
(çapraz örnek) ikisi de mevcut, aynı 1077/1077 geçen pakette.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-065 — Başka kiracının task id'si okunamaz (Faz 117)

**Gerçek sonuç — otomatik test kanıtı.**
`McpTaskTenantIsolationTests` tam 2 test taşıyor
(`Another_tenants_task_id_is_not_found`,
`Another_tenant_can_interfere_with_cancellation_but_never_reads_the_result`)
— spec'in kendi notuyla birebir eşleşiyor, aynı 1077/1077 geçen pakette.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-066 — İki Tracon örneği, tek veritabanı: task ikinci örnekten okunur (Faz 117)

**Gerçek sonuç — otomatik test kanıtı.**
`McpTaskCrossInstanceTests.Second_instance_reconstructs_a_completed_task_from_the_shared_database`
mevcut, aynı 1077/1077 geçen pakette — spec'in kendi notu bunun
ELLE koşulacak bir case olarak planlandığını ama iki gerçek
`TraconTestHost` + tek SQLite dosyasıyla OTOMATİKLEŞTİRİLDİĞİNİ söylüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-067 — 👤 `TaskTimeToLive` dolunca `tasks/get` hâlâ okunur (Faz 117, Açık Soru 2)

**Gerçek sonuç**
👤 Gerçek zaman aralığı (TTL sonrası bekleme) gerektiriyor, otomatik
karşılığı yok (spec'in kendisi de belirtmiyor) — §4.3 fiziksel eylem
tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-068 — Kayıtlı `IToolArgumentsValidator` MCP tool'unu da görür (Faz 127)

**Gerçek sonuç — otomatik test kanıtı, canlı yol koşulmadı (spec'in kendi
notu).** `ToolWrapperChainTests
.A_code_defined_registration_and_an_mcp_style_registration_produce_the_same_wrapper_layers`
ve `A_real_validator_installs_the_validating_layer_between_timeout_and_authorizing`
ikisi de mevcut, dosya 19'da TAM koşulan `Tracon.Core.UnitTests`
(2805/2805) paketinin parçası. Gerçek bir MCP sunucusuna karşı elle
koşulmadı (spec bunu zaten "⬜" işaretliyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Arayüz (UI, Playwright)

## MT-MCP-047 — Tools ekranında MCP kökenli tool `mcp: {sunucu}` rozetiyle ayrışır

**Gerçek sonuç**
Playwright tarayıcısı bu oturum boyunca başka bir şeridin kullanımındaydı
(`Browser is already in use for .../mcp-chrome-3eca5a9`) — tekrar tekrar
kontrol edildi, hep meşgul bulundu. Ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-048 — `mcp.tsx` formu: OAuth açılınca `authorizationConfigurationKey` OTOMATİK TEMİZLENİR

**Gerçek sonuç**
Aynı gerekçeyle (Playwright meşgul) ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-049 — `mcp.tsx` sunucu listesi tablosunda secret DEĞERİ hiç GÖRÜNMEZ

**Gerçek sonuç**
Aynı gerekçeyle ertelendi. Not: API seviyesindeki karşılığı (MT-MCP-007)
bu oturumda TAM doğrulandı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-MCP-015 — Var olmayan bir MCP sunucusu kaydetmek AGENT KAYDINI ETKİLEMEZ

**Gerçek sonuç**
`ulasilamayan` (`localhost:59999`) kaydı `200`. Ardından `support`
agent'ının normal çalıştırması `HTTP 200`, SSE akışı `done` ile bitti —
ulaşılamayan sunucu hiç engellemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-017 — Bir sunucunun zaman aşımına uğraması DİĞER sunucuları ETKİLEMEZ

**Gerçek sonuç**
`ulasilamayan` ve `test-sunucu` ikisi kayıtlıyken `/refresh` → `200
{"toolCount":14}` — `test-sunucu`'nun 14 tool'u tam kaldı,
`ulasilamayan`'ın 0 tool'u onu etkilemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-018 — Arka plan keşif döngüsü İSTİSNA sonrası ASLA çökmez

**Gerçek sonuç**
`ulasilamayan` kayıtlıyken birden fazla keşif turu (10s aralıkla) boyunca
her turda bağlanamama uyarısı loglandı, uygulama `/api/diagnostics`'e
yanıt vermeye devam etti — çökme yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-020 — Keşfedilen tool adı `{sunucu}_{tool}` biçiminde — NOKTA YOK

**Gerçek sonuç**
14 tool'un tamamı `test-sunucu_<ad>` biçiminde (`test-sunucu_echo`,
`test-sunucu_get-sum`, vb.) — hiçbirinde nokta yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-021 — Kod-tanımlı bir tool ile AYNI ADA sahip MCP tool'u ÇAKIŞIRSA kod tool KAZANIR (izlek C)

**Gerçek sonuç — kaynakla doğrulandı, canlı çakışma üretilmedi (spec'in
kendi notu: pratik değil).** `McpToolRegistry.cs:43-58`: `merged.AddRange(code)`
ÖNCE eklenir, `codeNames` kümesi oluşturulur, MCP tool'ları yalnız
`!codeNames.Contains(descriptor.Name)` iken eklenir — kod tool'uyla aynı
adlı bir MCP tool'u SESSİZCE atlanır. Sınıfın kendi XML dokümanı da
"Code always wins" diye belgeliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-022 — `MaxToolsPerServer` aşımı → fazla tool'lar UYARIYLA düşürülür

**Gerçek sonuç**
`MaxToolsPerServer=1` ile: `toolCount:2` (`test-sunucu_echo` +
sentetik `test-sunucu_read_resource` — 🚨 gözlem, kusur DEĞİL: sınır
yalnız SUNUCUNUN bildirdiği normal tool'lara uygulanıyor, `read_resource`
ayrı bir mekanizmayla EKLENDİĞİ için sayıma girmiyor). Log: `"MCP server
'test-sunucu' exceeded the 1 tool limit; the excess is being dropped."`
— keşif BAŞARISIZ olmadı, yalnız uyarıyla kırpıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-023 — `requiresApproval=true` bir MCP tool'u çağrılınca ONAY KARTI üretir

**Gerçek sonuç**
`test-sunucu`'yu `requiresApproval:true` yapıp `test-sunucu_echo` tool'unu
taşıyan bir agent oluşturuldu ve çalıştırıldı: yanıt
`"requiresConfirmation":true`, bir `approvals` olayı (`toolName:
"test-sunucu_echo"`), run durumu `AwaitingApproval` — kod-tanımlı
`cancel_order`'ın ürettiği akışla BİREBİR aynı mekanizma.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-024 — `resources` yeteneği bildiren sunucuda sentetik `{sunucu}_read_resource` tool'u OTOMATİK belirir

**Gerçek sonuç**
`test-sunucu_read_resource` tool listede — hiçbir kod veya `PUT` ile açıkça
tanımlanmamış, `resources` yeteneği keşfedilince otomatik üretildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-026 — Yerel bir MCP sunucusu kur

**Gerçek sonuç**
`npx -y @modelcontextprotocol/server-everything streamableHttp` —
`http://localhost:3001/mcp` üzerinde ayakta, `tools`+`prompts`+`resources`+
`tasks` yeteneklerinin hepsini bildiriyor (bkz. dosya başı ortam notu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MCP-027 — Yerel sunucuyu Tracon'e kaydet, tool keşfi gerçekleşir

**Gerçek sonuç**
`/refresh` sonrası `GET /api/tools` → 14 `test-sunucu_*` tool'u listede
(MT-MCP-020/022/024'te ayrıntılı).

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

## MT-MCP-016 — Sunucu keşif turu ortasında OFFLINE olursa, ESKİ tool listesi KORUNMAZ

**Gerçek sonuç**
`test-sunucu` 14 tool sunarken yerel sunucu durduruldu, `/refresh` →
`{"toolCount":0}`. `GET /api/tools`'ta `test-sunucu_*` tool sayısı **0**'a
düştü — `RefreshCatalogAsync` başarısız olunca önceki iyi listeyi
TUTMADI. Sunucu sonra yeniden başlatıldı (bir sonraki case'ler için).

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
