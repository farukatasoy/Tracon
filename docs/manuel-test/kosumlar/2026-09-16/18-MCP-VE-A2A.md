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

Aile açılıyor — bu ilk devir notu.

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

---
