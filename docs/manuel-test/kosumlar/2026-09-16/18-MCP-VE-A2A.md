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
