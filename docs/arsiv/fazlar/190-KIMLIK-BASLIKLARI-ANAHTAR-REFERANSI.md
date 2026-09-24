# Faz 190 — Kimlik Taşıyan Başlıklar İçin Yapılandırma Anahtarı Referansı

> **Durum:** ✅ Tamamlandı (2026-09-24)
> **Plan onayı:** Bakımcı, 2026-09-23 (engelleyici kararlar sohbette alındı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-272**
> **Önkoşul:** K-852 (`RequireTenantKey`, `bb9953e3`) — yeni alanın her anahtar adı ondan geçer ·
> bu turun maskeleme `kusur-giderme`'si — okuma tarafı (GET `"***"`, PUT `"***"` → `400`) şart; denetim tarafı ölçülür, inmediyse bu fazındır (190.6) ·
> [Faz 187](187-KIRICI-DEGISIKLIK-KAPISI.md) — **teknik**: `kapi.py yayin --kuru` bu fazın `CHANGELOG` satırlarını denetler ·
> turun K-* kategori etiketi kapısı (K-855'ten itibaren) · Faz 185, 186, 188, 189 — yalnız sıra (kullanıcı kararı; teknik bağımlılık yok)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Mcp`, `.AspNetCore`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Testing.Contracts.Xunit`, `.Client` + npm `@tracon/client` (üretilir), `.UI` (`server-types.ts`; Açık Soru 3 → `mcp.tsx`)
> **Yeni paket:** Yok · **Migration:** gerekli — numara uygulama anında alınır (iki tablo × üç sağlayıcı; K-178)
> **Public API:** büyüyor — dört tipe `HeaderConfigurationKeys`, iki sözleşme sınıfına birer test; `AuthorizationConfigurationKey` `[Obsolete]` olur. `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya × 1 satır: `Shipped` boş (K-603). Bugün ucuz; GA'dan sonra kırıcı
> **Tüketici yüzeyi:** site: `getting-started/security.md`, `guides/production.md`, `guides/external-agents.md`, `concepts/governance.md`, üretilen `api/` + `http-api/`
> · sevk edilen: XML `<example>` (iki `HeaderConfigurationKeys`), `src/Tracon.Mcp/README.md`, `capabilities.md` satır 99 ve 198, `docs/openapi/tracon.json` + iki istemci (üretilir), `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/18-MCP-VE-A2A.md` (MCP) · `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` (webhook, denetim, MT-SEC-091)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show de138077:docs/arsiv/fazlar/190-KIMLIK-BASLIKLARI-ANAHTAR-REFERANSI.md
> ```
>
> Damıtıldı 2026-09-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

MCP server'ı veya webhook alıcısı `X-API-Key`, `Ocp-Apim-Subscription-Key` veya `Cookie` ister. Tracon yalnız `Authorization` için anahtar referansı sunar; diğer başlıklar `Headers`'ta DB'ye düz yazılır (K-059 ihlali). Kazanan: API anahtarlı hedef kullanan operatör; değer `user-secrets`'ta kalır.

## Bağlayıcı kararlar (kullanıcı kararı, 2026-09-23)

1. Yeni public alan `HeaderConfigurationKeys` (`IReadOnlyDictionary<string, string>`,
   başlık adı → anahtar adı) `McpServerDefinition`/`McpServerRequest` ve
   `WebhookSubscription`/`WebhookSaveRequest`'e girer; `mcp_servers` ve
   `webhook_subscriptions` yeni JSON sütunu alır (üç sağlayıcı). Her ad K-852
   `RequireTenantKey`'den kaydetmede **ve** çözmede geçer. Değer bağlantı/teslim anında çözülür.
2. `AuthorizationConfigurationKey` şimdi `[Obsolete]`, GA'da kalkar. Aynı başlığı iki
   alandan yöneten kayıt `400`.
3. Düz `Headers`'ta kimlik benzeri ad `400`. Sınıflandırıcı: `AuditSecretFilter`
   parça kuralı + `Cookie` + `-key` soneki (`Proxy-Authorization` parçayla yakalanır).
   Hata yeni alanı gösterir.
4. Webhook aynı fazdadır: aynı alan, kural, migration turu.
5. Formlar yeni alanı göstermez; arayüzden kayıt `Headers` ve
   `HeaderConfigurationKeys`'i **silmez**.
6. Denetim izi hiçbir başlık değeri yazmaz.
7. Düz kimlik başlıklı eski satır gönderilmeye devam eder; bağlantıda uyarı loglanır.

Okuma tarafı (GET `"***"`, PUT `"***"` → `400`) bu fazdan **önce** biter.

---

## Kapsam dışı

Form alanı (karar 5) · `authorization_configuration_key` verisini yeni sütuna taşıma
(iki alanı birlikte doldurur, form çakışır; GA işi, Açık Soru 9) ·
`IdempotencyResponse.Headers` · sınıflandırıcı dışı adların DB'de düz durması (okuma
maskeli, denetim değersiz; dokümana yazılır) · MCP toplam başlık sınırı.

## Uygulama sırası

1. "Bu Faza Başlarken" 3-4.
2. **Düşen testler önce** (DoD'nin ilk dört satırı + harf tekrarı).
3. Sınıflandırıcı + filtre bağlantısı (190.2).
4. Model + migration + store + sözleşme + `MigrationParityTests` (190.1).
5. Kaydetme (190.3).
6. `maf-api-kesfi` ölçümü, sonra çözme (190.4).
7. `[Obsolete]` + `CS0618` (190.5); **erken kapı**: `kapi.py` iç döngüsü.
8. Denetim ve okuma (190.6).
9. OpenAPI + iki istemci + tüketici yüzeyi (190.7).

---

## Bitiş Ölçütleri (DoD)

- [x] Komut 3 → `400`; `detail` `headerConfigurationKeys` içerir, `v` içermez; webhook eşi aynı — Örnek Uygulama Koşumu satır 3, 3b
- [x] Komut 4 → yakalayıcı değeri alır; GET adı döner, değeri dönmez — satır 4
- [x] Form gövdeli PUT iki alanı korur, `{}` temizler (`HeaderPreservationTests` + case 6); `endpoint` yan etkisi `.WithDescription` ve `security.md`'de
- [x] Karar 6: komut 6 → `mcp:m1` ve `webhook:w1` denetiminde `plain-190` sayısı `0`; `mcp:m1`'de `X-Tenant` var — `0` · `0` · `1`
- [x] Göç tarifi `production.md`'de (`## Upgrading: credential headers`); case 13 geçti
- [x] Kiracı dışı anahtar adı: MCP bağlanmaz, webhook `Dropped` — `CredentialHeaderDeliveryTests` (plan adı `CredentialHeaderResolveGuardTests` yerine; aynı sınıfta)
- [x] Round-trip bellek içi + üç SQL'de yeşil, `SaveAsync` dönüşünü doğrular; `MigrationParityTests` iki tabloyu kapsar
- [x] `grep -rn "server\.Headers" src/Tracon.Mcp` → yalnız `McpHeaderBuilder.cs` ve `McpConnection.cs` (bir satır, parmak izi) — `McpHeaderBuilderTests.Only_the_builder_and_the_fingerprint_read_a_servers_headers`
- [x] `HeaderBearingTypeTests` yeşil; sınıflandırıcı ve filtre tek listeyi okur (`CredentialHeaderNames`)
- [x] `grep -rn "pragma warning disable CS0618" src tests` → **11** (src 9, tests 2); her biri satır içi gerekçeli
- [x] `AdditionalHeaders` + OAuth ölçümü devir notunda
- [x] `CHANGELOG.md` `Added`/`Deprecated`/`Security`/`Fixed`; `python3 scripts/kapi.py yayin --kuru` — sonuç "Kapanış Kapısı"nda
- [x] Dört doğrulama kapısı: `python3 scripts/kapi.py kapanis --taban cd985bd7` — sonuç "Kapanış Kapısı"nda
- [x] `samples/Tracon.Api` ile gerçek `run`: komut 1-6 ve `run.completed` webhook'u yakalayıcıya `X-API-Key: dogrulama-degeri` ile ulaştı
- [x] `secret` taraması: `python3 scripts/kapi.py tarama` — migration ankrajı commit sonrası; sonuç "Kapanış Kapısı"nda
- [x] Manuel kabul case'leri: `MT-MCP-070`…`077`, `MT-SEC-207`…`210` eklendi, `MT-MCP-069` ve `MT-SEC-091` yeniden yazıldı; otomatikleştirilebilenler koşuldu (Örnek Uygulama Koşumu)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` kapanış kapısında
- [x] Arayüz: `mcp.tsx` kimlik sütunu (AS 3 = A) yeni metin eklemedi → `en.ts`/`tr.ts` değişmedi; bundle 196,3 KB gzip (bütçe 250 KB, `ic-dongu` ölçümü)

### Doğrulama komutları

```bash
# 1) Başlık yakalayıcı (ayrı terminal)
python3 -c 'import http.server as h
class H(h.BaseHTTPRequestHandler):
    def do_POST(s):
        print(s.path, s.headers.get("X-API-Key"), flush=True); s.send_response(200); s.end_headers()
h.HTTPServer(("127.0.0.1", 9099), H).serve_forever()'

# 2) Örnek uygulama — değerler yalnız ortam değişkeninde
Tracon__McpSecrets__DemoKey=dogrulama-degeri Tracon__WebhookSecrets__DemoKey=dogrulama-degeri \
Tracon__Egress__AllowPrivateNetworkTargets=true Tracon__Webhooks__AllowInsecureHttp=true \
Tracon__Sqlite__ConnectionString="Data Source=$TMPDIR/faz190.db" dotnet run --project samples/Tracon.Api
export APU="http://localhost:5080/tracon" J='content-type: application/json'
M="$APU/api/mcp-servers/m1" W="$APU/api/webhooks/w1"

# 3) Kimlik benzeri düz başlık — beklenen: 400
curl -s -o /dev/null -w "%{http_code}\n" -X PUT "$M" -H "$J" \
  -d '{"endpoint":"https://mcp.example.com/mcp","headers":{"X-API-Key":"v"}}'

# 4) Anahtar referansı çözülür — yakalayıcı "dogrulama-degeri" yazar
curl -s -X PUT "$M" -H "$J" -d '{"endpoint":"http://127.0.0.1:9099/mcp","headerConfigurationKeys":{"X-API-Key":"Tracon:McpSecrets:DemoKey"}}'
curl -s -X POST "$APU/api/mcp-servers/refresh"
curl -s "$APU/api/mcp-servers" | grep -c dogrulama-degeri   # 0

# 5) Webhook test olayı değerle ulaşır
curl -s -X PUT "$W" -H "$J" -d '{"url":"http://127.0.0.1:9099/hook","events":["run.completed","test.ping"],"headerConfigurationKeys":{"X-API-Key":"Tracon:WebhookSecrets:DemoKey"}}'
curl -s -X POST "$W/test"

# 6) Karar 6 — X-Tenant sınıflandırıcı dışıdır
curl -s -X PUT "$M" -H "$J" -d '{"endpoint":"http://127.0.0.1:9099/mcp","headers":{"X-Tenant":"plain-190"}}'
curl -s -X PUT "$W" -H "$J" -d '{"url":"http://127.0.0.1:9099/hook","events":["run.completed","test.ping"],"headers":{"X-Tenant":"plain-190"}}'
curl -s "$APU/api/audit/mcp:m1" | grep -c plain-190       # 0
curl -s "$APU/api/audit/webhook:w1" | grep -c plain-190   # 0
curl -s "$APU/api/audit/mcp:m1" | grep -c X-Tenant        # 1
```

Kimlik doğrulama açıksa her `curl`'e `-H "$APB"` eklenir.

---

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, SQLite (`Data Source=<scratchpad>/faz190.db`), `--no-build -c Release`,
2026-09-24. Değerler yalnız ortam değişkeninde (`Tracon__McpSecrets__DemoKey`,
`Tracon__WebhookSecrets__DemoKey` = `dogrulama-degeri`); `Tracon__Scheduling__PollInterval=00:00:01`.
Yakalayıcı `127.0.0.1:9099`. İkinci koşum sağlayıcı anahtarları boş verilerek (echo) yapıldı.

| Komut / case | Gerçek çıktı |
|---|---|
| 3 · MCP düz `X-API-Key` | `400`, `title` "Credential header stored in the clear", `detail` "…Declare it in 'headerConfigurationKeys' instead…: {"X-API-Key": "Tracon:McpSecrets:<KeyName>"}"; `v` yok |
| 3b · webhook düz `Authorization` (case 8) | `400`, aynı metin, önek `Tracon:WebhookSecrets:` |
| case 2 · `Cookie`, `Ocp-Apim-Subscription-Key` | `400` · `400` |
| 4 · MCP anahtar referansı + refresh | `200`, yanıt `"headerConfigurationKeys":{"X-API-Key":"Tracon:McpSecrets:DemoKey"}`; yakalayıcı `POST /mcp X-API-Key= dogrulama-degeri`; `GET /api/mcp-servers` içinde `dogrulama-degeri` sayısı `0` |
| 5 · webhook anahtar referansı + `test` | `200`; yakalayıcı `POST /hook X-API-Key= dogrulama-degeri X-Tracon-Event= test.ping`; teslim `Delivered 200` |
| 6 · karar 6 (`X-Tenant: plain-190`, haritalar gönderilmeden) | audit `mcp:m1` → `plain-190` `0`; `webhook:w1` → `0`; `mcp:m1` → `X-Tenant` `1`; iki haritanın anahtar adları audit'te maskesiz; saklı harita korundu |
| case 4 · kiracı dışı ad (`default` → `globex`) | `400`, "'Tracon:McpSecrets:globex:Key' is outside the key space of tenant 'default'. 'headerConfigurationKeys[X-API-Key]' may only reference…" |
| case 5 · `Authorization` iki alandan / OAuth ile | `400` · `400` |
| case 9 · `X-Tracon-Signature` anahtar haritasında | `400` |
| case 6 · form gövdesiyle `PUT` (`mcp.tsx` gövdesi, `curl`) | `200`; `sqlite3` iki haritayı değişmeden gösterdi |
| case 7 · `sqlite3` ile düz `X-API-Key: eski-190` satırı + refresh | yakalayıcı `POST /mcp2 X-API-Key= eski-190`; log "MCP server 'm2' stores the credential header 'X-API-Key' in plain headers, in the clear. Move it to headerConfigurationKeys."; `eski-190` logda yok |
| case 13 · (a) yalnız yeni alan (b) + `headers:{"X-Team":"t1"}` | (a) `400` "…Remove 'X-API-Key' from headers: send 'headers' in the same save…"; (b) `200`, yakalayıcı `/mcp2 dogrulama-degeri`, yeni uyarı yok |
| gerçek `run` (echo, `support`) | `200`; yakalayıcı `POST /hook X-API-Key= dogrulama-degeri X-Tenant= plain-190 X-Tracon-Event= run.completed`; teslim `Delivered 200` |
| iki koşumun logu | `dogrulama-degeri`, `eski-190`, `plain-190` geçiş sayısı `0` |

MCP bağlantıları yakalayıcıya ulaştıktan sonra "could not connect" ile düştü —
yakalayıcı MCP konuşmaz; başlığın teli geçtiği ölçüldü. Gerçek MCP sunucusuyla uçtan
uca yol `CredentialHeaderDeliveryTests` (loopback `MapMcp`) ile kanıtlı.

## Plandan Sapmalar
| # | Plan | Gerçekleşen | Gerekçe |
|---|---|---|---|
| 1 | Önkoşul: maskeleme `kusur-giderme`'si ayrı commit | `3d79f8bc` (plan commit'i) içinde indi; okuma ve denetim tarafı ikisi de vardı | Başlangıç 4 ölçtü: `HeaderValueMask` + üç test yeşil. 190.6'nın "denetim değer düşürme" işi gerekmedi |
| 2 | Açık Soru 4 önerisi A (`DiagnosticId` + `UrlFormat`) | **B + `UrlFormat`**: yalnız mesaj, `DiagnosticId` yok | Ölçüldü: `TRC9001` ile Tracon.Core'un STJ context'i 30 hatayla kırıldı; üretilen kod yalnız `CS0612/CS0618` bastırır. Tüketici de kırılırdı (K-869) |
| 3 | `maf-api-kesfi` ile `AdditionalHeaders` + OAuth ölçümü | ilspy ile `ModelContextProtocol.Core` 2.2.0 decompile | Soru imza değil davranıştı; reflection davranışı göstermez. Bulgu devir notunda |
| 4 | 190.4: yalnız boş/CR-LF değer düşer | Ek: transport'un **ekleyemeyeceği** başlık (içerik başlığı) da düşer | Decompile, SDK'nın bu durumda **değeri** exception mesajına koyduğunu ve bağlantı yolunun logladığını gösterdi — plansız bir log sızıntısı yolu |
| 5 | Webhook ayrılmış ad yalnız yeni alanda `400` | Aynı; ek olarak iki harita birlikte `MaxExtraHeaders`'a sayılır (AS 2 = A) ve teslimde anahtar kaynaklı başlık önce eklenir | Limit düz başlığı keser, kimliği asla |
| 6 | TRC0201 mesajı (AS 7 = A: `DescribeSecretTarget`) | Hedef `McpServerDefinition.Headers["X-Api-Key"]` olur; genel mesaja "(for a request header, in 'HeaderConfigurationKeys')" eklendi | Hedef metnine tavsiye gömmek mesajın tırnaklarını bozuyordu |
| 7 | Sözleşme: iki sınıfa birer test | Webhook'a ikinci test: `Header_names_that_differ_only_in_case_do_not_fail_the_save` | Planın hata modu tablosu harf tekrarını `WebhookStoreContract`'a bağlıyordu; `SqlWebhookStore.SerializeHeaders`'taki `ToDictionary` `ArgumentException` → `500` kusuru böylece sözleşmede kilitli |
| 8 | `MigrationParityTests` iki tabloyu kapsar | Kapsar; ayrıca `OUTPUT` regex'i çok satırlı listeyi okuyacak şekilde düzeltildi ve bir "OUTPUT bulundu" testi eklendi | Eski regex yalnız ilk satırı okuyordu; MCP/webhook listeleri çok satırlıdır, test boşuna geçerdi |
| 9 | Webhook `Describe` yalnız yeni alanın adlarını ekler | Düz `headers`'ın **adları** da eklendi | MCP denetimi zaten adları taşıyor; iki yüzey aynı biçimde |
| 10 | — | `mcp.tsx:584` yorumu düzeltildi ("headers typed back from this form" artık yanlış) | Silme onayı kararı (§175.3) değişmedi |
| 11 | Göç tarifi `production.md`'de | `## Upgrading: credential headers` başlığı; `[Obsolete]` `UrlFormat`'ı oraya bağlanır | `ObsoleteMessagesTests` başlık slug'ını kilitler |
| 12 | Düşen testler önce (uygulama sırası 2) | Testler kodla birlikte yazıldı; önce-kırmızı ayrıca koşulmadı | Mevcut dört test (maske, denetim) yeni kural yüzünden kırmızıya döndü ve yeniden yazıldı (`X-Session-Id`, `X-Tenant`); yeni testlerin tabanda düşeceği alan yokluğundan açıktır (alan bağlanmıyordu) |

## Bu Fazda Verilen Kararlar
K-868 (kimlik başlığı yalnız anahtar adıyla; kategori: güvenlik, kalıcı-veri) · K-869
(`[Obsolete]` biçimi ve `Authorization` çakışması; public-api) · K-870 (`PUT` koruma
semantiği; public-api). Açık Sorular: 1 A · 2 A · 3 A · 4 **B** (sapma 2) · 5 A · 6
ölçüldü → B · 7 A · 8 A · 9 A (F-290) · 10 A · 11 B.

Yerel tercihler (K-* değil):

- Yardımcı adları `CredentialHeaderNames` (Core/Egress), `CredentialHeaderSaveRules`
  (AspNetCore/Endpoints), `McpHeaderBuilder` (Mcp/Internal), `ObsoleteMessages` (Abstractions).
- Parmak izi biçimi: mevcut dizeye `|` + sıralı `ad=anahtarAdı`; değer girmez.
- Anahtar kaynaklı başlık, aynı adlı düz başlığı **anahtar boş çözülse de** ezer —
  bildirim operatörün niyetidir, düz değer bayat kimlik olabilir.
- `CredentialHeaderSaveRules`'ın `400` başlıkları: `Credential header stored in the
  clear` · `Header name invalid` · `Configuration key missing` · `Reserved header` ·
  `Duplicate header`; webhook uçta hepsi mevcut `Subscription invalid` başlığıyla döner.
- Başlık adı geçersizse `detail` adı **yankılamaz** (CR/LF taşıyabilir).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 — Açık Soru 4'ün önerisi (A) ölçümle B'ye döndü (Sapma 2); plan metni değişmedi |
| Düzeltme turu sayısı | 3 — (1) `ic-dongu`: `ShippedDocumentationSelfContainmentTests` (`///` içinde `K-*`/🚨/`phase 190`); (2) `dokuman-bakim.py`: `///` içinde `1.0.0` bağımlılık damgası sanıldı; (3) denetim bulguları (🔴1 + 🟡1–5 + 🟢1) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 1 — 🟡3: koruma kuralı, eski satırdaki düz `Authorization` + OAuth'u kaydedilebilir kıldı (denetimde bulundu, kapatıldı); 🟡4 muafiyetin geniş ilk hâli agent `metadata`'sında değer sızdırırdı (commit edilmeden kapatıldı) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

`faz-denetcisi`, taze bağlam, taban `cd985bd7`, çalışma ağacı (2026-09-24).

| # | Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|---|
| 🔴1 | Sözleşme tiplerinin `<summary>`'sinde `<see cref="HeaderConfigurationKeys"/>` → OpenAPI/istemci/site metninde `IReadOnlyDictionary<…>? McpServerRequest.HeaderConfigurationKeys` (K-517) | 🔴 | gerçek | düzeltildi — dört dosyada `<c>HeaderConfigurationKeys</c>`; OpenAPI ve iki istemci yeniden üretildi |
| 🟡1 | Kaydetmede kiracı dışı / önek dışı ad için HTTP testi yok | 🟡 | gerçek | düzeltildi — `TenantScopedConfigurationKeyTests.Header_key_outside_the_callers_tenant_is_rejected` (5 satır) + `…_under_the_callers_tenant_is_accepted` |
| 🟡2 | Webhook teslimindeki yeni hata yolları test edilmemiş | 🟡 | gerçek | düzeltildi — `WebhookCredentialHeaderDeliveryTests` (9 test: boş/CR-LF değer, `configuration` `null`, anahtar düz başlığı ezer, eski düz kimlik uyarısı, düz CR-LF, içerik başlığı, limit) |
| 🟡3 | Korunan düz `Authorization` + formdan OAuth açma `200` alıyor; SDK Bearer yazmaz → OAuth sessizce çalışmaz | 🟡 | gerçek (fazın regresyonu) | düzeltildi — etkin düz haritada `Authorization` + `oauthEnabled` → `400`; `CredentialHeaderSaveTests.Oauth_is_refused_while_the_kept_plain_headers_carry_authorization`. Plan "kullanıcıya sorulur" diyordu: K-869'un çakışma kuralının (etkin harita) doğrudan uzantısı olarak uygulandı; kural `CHANGELOG` ve `production.md`'de |
| 🟡4 | Denetim muafiyeti `…ConfigurationKeys` sonekli her nesneye uygulanıyor → agent `metadata`'sında değer düz yazılır | 🟡 | gerçek | düzeltildi — ad tam `headerConfigurationKeys` + değer `:` taşımalı; `AuditSecretFilterTests.Only_the_header_map_with_key_paths_is_exempt` |
| 🟡5 | `TraconMcpSecurityOptions`/`TraconWebhookOptions` XML'i yeni davranışla çelişiyor | 🟡 | gerçek | düzeltildi |
| 🟡6 | `kapi.py tarama` kırmızı: üç migration ankrajsız | 🟡 | gerçek (beklenen) | commit sonrası `scripts/applied-migrations.json` `sourceCommits` ile ankrajlandı |
| 🟢1 | Webhook anahtar haritasındaki içerik başlığı logsuz düşüyor | 🟢 | gerçek | düzeltildi (uyarı + test) |
| 🟢2 | Anahtar alanına yazılan değer `400`/teslim `error`'ında yankılanır | 🟢 | gerçek (faz öncesi) | F-291 olarak devredildi |
| 🟢3 | `ObsoleteMessagesTests` `DiagnosticId` taraması AspNetCore'u kapsamıyor | 🟢 | gerçek | gerekçelendi — iki öznitelik aynı sabitleri kullanır; Core.UnitTests AspNetCore'a referans vermez |

## Sonraki Faza Devir Notu

**Sıradaki faz: [191](../../191-TEK-DERLEME-ZINCIRI.md)** — Faz 190'la teknik bağı yok.

Devralınan sözleşmeler:

- `McpServerDefinition.HeaderConfigurationKeys` / `WebhookSubscription.HeaderConfigurationKeys`
  (başlık adı → anahtar ADI). Kaydetmede ve çözmede `ConfigurationKeyGuard.RequireTenantKey`.
  Üçüncü taraf store alanı taşımalı ve `SaveAsync` dönüşünde de döndürmelidir (iki sözleşme testi).
- `PUT` semantiği: haritalar yok/`null` → saklı, `{}` → temizle (K-870). Diğer alanlar tam değiştirilir.
- Tek sınıflandırıcı: `CredentialHeaderNames` (Core); `AuditSecretFilter` onu okur.
- Başlıkları tek okuyan: `McpHeaderBuilder` (MCP), `WebhookDeliveryJobHandler.AddCredentialHeaders` (webhook).

🚨 Tuzaklar:

- **SDK ölçümü (`ModelContextProtocol.Core` 2.2.0, ilspy):** `ClientOAuthProvider.SendAsync`
  Bearer'ı yalnız istekte `Authorization` yoksa yazar; `401` sonrası tekrar isteği Bearer'la
  kurar. Düz `Authorization` + OAuth bu yüzden kaydetmede `400`'dür (🟡3). `CopyAdditionalHeaders`
  eklenemeyen başlıkta **değeri** `InvalidOperationException` mesajına koyar; `McpHeaderBuilder.CanSend`
  böyle başlığı transport'a hiç vermez. SDK yükseltmesinde iki davranış yeniden ölçülür.
- `[Obsolete]`'e `DiagnosticId` verilmez (K-869; STJ üreteci yalnız `CS0612/0618` bastırır).
- `///` içindeki her `x.y.z` bağımlılık damgası kapısına girer; Tracon sürümü XML'de "first stable release" yazılır.
- `CredentialHeaderSaveRules.Validate` bağlanan (`Ordinal`) haritayı yargılar; saklamadan önce
  `Copy(..., OrdinalIgnoreCase)`. Sıra değişirse harf tekrarı kontrolü kör olur.

Açık iş:

- **F-290** — `AuthorizationConfigurationKey`'in GA'da kaldırılması (veri taşıma + `mcp.tsx` formunun yeni alana geçmesi). Formun geçişi GA'dan bağımsız erken yapılabilir.
- **F-291** — anahtar alanına yazılan değerin hata metninde yankılanması (faz öncesi, dört yüzey).
- `MT-MCP-074` 👤 tarayıcıdan form tıklaması koşulmadı (gövde `curl` ile ölçüldü); `MT-MCP-075`, `MT-SEC-210` ➜ CI.
- Site yayını (`scripts/site-deploy.sh`, `faz-tamamlama` Adım 10) bakımcı onayı bekler.
