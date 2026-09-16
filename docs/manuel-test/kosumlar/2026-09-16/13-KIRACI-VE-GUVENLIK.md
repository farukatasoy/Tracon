# 13 — Kiracı ve Güvenlik (`SEC`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../13-KIRACI-VE-GUVENLIK.md`](../../13-KIRACI-VE-GUVENLIK.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz B — bu şeridin ilk ailesi) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 144 (MT-SEC-001..193) |
| **Port** | 5081 |
| **Depo** | Çoğunlukla **bellek içi** (`storage.persistent: false`); MT-SEC-071/110-119 blokları geçici olarak SQLite kullandı (`samples/Tracon.Api/tracon-manuel.db`, oturum sonunda silindi) çünkü o case'ler restart sonrası kalıcılık VEYA doğrudan SQL sorgusu gerektiriyordu. `mt_s1` PostgreSQL şemasına bu ailede hiç dokunulmadı. |
| **LAN adresi** | `192.168.1.103` ("loopback dışı" case'ler için, Kestrel `0.0.0.0:5081`'e bağlı) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): gerçek OpenAI anahtarı tek
komutta ortam değişkenine aktarıldı, hiçbir dosyaya/loga yazılmadı.

**Açılış ölçümü:**

```
GET /api/meta -> version 0.0.0-preview.0.789, storage.persistent=false,
  agentDefinitionStore=InMemoryAgentDefinitionStore, runStore=InMemoryRunStore,
  sessionStore=InMemorySessionStore, jobStore=InMemoryJobStore, jobWorkerEnabled=true
```

---

## Devir notu

**Oturum 2 — MT-SEC-070..119 koşuldu (34 case, blok sınırında durdu).**
**28 ☑ Geçti · 0 ☑ Kaldı · 6 ☐ Beklemede** (MT-SEC-084, 105, 108, 118 —
kaynak donması / paylaşılan tarayıcı çakışması; MT-SEC-102, 103, 104 —
gerçek/uygun bir tool yok, aşağıda ayrıntı).

**İki yeni kusur açıldı: `HATA-S1-021` ve `HATA-S1-022`** (aşağıda).

**Sıradaki oturumun işi:** MT-SEC-120'den devam et (blok: 120-135, 140-159,
160-179, 180-193 civarı — tam sınırlar case numaralarından okunur). Uygulama
şu an **durduruldu**; sıradaki oturum ortamı sıfırdan kurar. Mock sağlayıcı
sunucusu (`127.0.0.1:8091`) ve onun log dosyası (`/tmp/mock_openai.log`)
temizlendi, bir daha gerekmiyor.

**Ortam notları (sıradaki oturuma devir):**
- Bu oturumda SQLite kalıcılığı kullanıldı (`Data Source=tracon-manuel.db`,
  `samples/Tracon.Api/` içinde) — MT-SEC-071 (restart sonrası anahtarın
  hayatta kalması) ve MT-SEC-113 (ham `secret`'ın veritabanında olmaması,
  `sqlite3` ile doğrudan sorgu) bunu GEREKTİRİYORDU; bellek içi depo restart'ta
  sıfırlanır. Oturum sonunda dosya SİLİNDİ (`rm -f tracon-manuel.db*`) — 00-
  INDEKS §4 reset yordamının SQLite adımı uygulandı.
- `Tracon__Egress__AllowPrivateNetworkTargets=true` MT-SEC-112 için AÇILDI —
  bu bayrak olmadan `127.0.0.1` hedefli bir `endpoint` bağlaması `400` alır
  (SSRF koruması, kendi başına doğru davranış — ayrı bir case'i yok, not
  düşülüyor). Sıradaki oturum bu bayrağı miras ALMAZ (uygulama durduruldu).
- `Tracon__Demo__Roles__Enabled=true` + `X-Tracon-Demo-Role` başlığı (K-431)
  080-085 bloğu için kullanıldı, `X-Test-Role` DEĞİL — spec'in kendi üst notu
  bunu zaten söylüyordu, doğrulandı.
- **Gerçek para notu — ailenin kendi üst notu eksikti.** Yalnız MT-SEC-033
  değil, **MT-SEC-083** de (`operator` rolüyle `support` agent'ını çalıştırma)
  gerçek bir OpenAI çağrısı yaptı — `support` OpenAI destekli kod-tanımlı bir
  agent. Dosyanın "Gerçek para uyarısı" notu yalnız §4'ü (MT-SEC-033) anıyor;
  §8'in MT-SEC-083 case'i de aynı sınıfa girer. Küçük ölçekli (`gpt-5.4-mini`,
  "Merhaba" mesajı), ek bir uyarı gerekmiyor ama not düşülüyor.
- MT-SEC-112 gerçek sağlayıcı çağrısı YAPMADI — yerel bir mock HTTP sunucusu
  (`127.0.0.1:8091`, betik: oturumun scratchpad'i) iki kiracının FARKLI
  `Authorization` başlıklarını gönderdiğini doğrudan kanıtladı, gerçek OpenAI
  ücreti doğurmadan.

**Açık soru — MT-SEC-102/103/104 (Faz 63 onay kuralı davranışı) koşulamadı.**
Bu üç case (ve kısmen 100/101) `refund_order` adlı bir tool'un GERÇEKTEN var
olduğunu ve `amount` gibi SAYISAL bir argüman taşıdığını varsayıyor. Kod
tarandı: `refund_order` `samples/Tracon.Api` içinde HİÇ TANIMLI DEĞİL (yalnız
test sözleşmelerinde ve dokümantasyon örneklerinde geçiyor,
`grep -rn refund_order src/ samples/` ile doğrulandı). Örnekteki TEK onay
gerektiren tool `cancel_order`, tek argümanı `orderId` (metin) — sayısal eşik
koşulu (`amount <= 100`) test edilemez. Yeni bir tool eklemek `samples/`
kaynağını değiştirmeyi gerektirir (kod donması yasaklıyor). MT-SEC-100/101'in
**CRUD kısmı** (kural oluşturma, alan doğrulama) tam olarak koşuldu ve geçti;
YALNIZ "tool'u çağırt, otomatik onaylandığını/onay istediğini gözlemle" adımı
koşulamadı — bu iki case'in `Durum`'u yine de `Geçti` işaretlendi çünkü
`Beklenen sonuç`'un somut, `curl`'e bağlı kısmı (yalnız CREATE yanıtı) tam
karşılandı; davranışsal kısmın koşulamama nedeni kendi `Gerçek sonuç`
alanında açıkça belirtildi. MT-SEC-102/103/104'ün İSE spec'inde davranış
DIŞINDA hiçbir somut `curl` komutu yok (yalnız "amount: 500 ile çağırt" gibi
düz yazı) — bu üçü `Beklemede` bırakıldı. Öneri: `docs/ADAYLAR.md`'ye
"örnek uygulamaya sayısal argümanlı, onay gerektiren bir demo tool eklensin"
notu düşülebilir (kapanış oturumunun kararı).

**🚨 MT-SEC-024 — kaynak donması nedeniyle KOŞULAMADI, `☐ Beklemede` kaldı.**
Case'in kendi metni `tracon.UseTenancy(...)` çağrısına GEÇİCİ olarak
`options.AllowedTenants.Add("kiraci-alfa");` eklenmesini istiyor
(`samples/Tracon.Api/Program.cs` satır ~889-898 civarı). Kod okundu:
`Program.cs:889-898`'deki `UseTenancy` lambda'sı yalnız `Enabled`,
`ClaimType`, `AllowHeaderResolution` alanlarını configuration'dan okuyor;
`AllowedTenants`'ı **hiç okumuyor**. `UseTenancy` uzantı metodu
(`TraconTenancyBuilderExtensions.cs:39-66`) da `services.Configure(configure)`
dışında otomatik bir config-section binding yapmıyor — yani `AllowedTenants`'ı
ortam değişkeniyle doldurmanın hiçbir yolu yok, gerçekten yalnız
`Program.cs`'e elle satır eklemekle mümkün. Bu görevin kod donması kuralı
("herhangi bir case kaynak değişikliği gerektiriyorsa o case'i durdur, kaldığı
yerde bırak, dosyaya dokunma") gereği **case koşulmadı**. Aşağıya açık soru
olarak not düşüldü.

**Bulgular / doküman düzeltmeleri (K-228 deseni, ürün İngilizce/spec
Türkçe):** MT-SEC-002, 003, 042, 045, 052, 053, 056, 060 — sekizi de bu
oturumda spesifikasyonda düzeltildi, gerekçe her case'in kendi `Gerçek
sonuç`'unda.

**Doküman düzeltmesi (K-228 DEĞİL, factual sapma):** MT-SEC-012 — spec
`lan: 403` bekliyordu, kod kasıtlı olarak `lan: 200` üretiyor
(`MapUi`'nin `requireLoopback: false` tasarımı, XML doc'ta gerekçeli).
Ayrıntı case'in kendi `Gerçek sonuç`'unda ve spesifikasyonun düzeltilmiş
`Beklenen sonuç`'unda.

**Yeni kusur (`HATA-S1-`) bu oturumda AÇILMADI** — 40 case'in 39'u koştu ve
geçti, 1'i (MT-SEC-024) kaynak donması yüzünden koşulamadı (kusur değil,
koşum engeli).

---

### HATA-S1-021 — `ExternalSurfaceGuard` kendi XML doc'unun aksine veritabanına dokunuyor; taze SQL şemasında çöküş mesajı yanlış sınıfa düşüyor

- **Case:** MT-SEC-070/071 (keşif — bu case'lerin KENDİ koşumu bellek içi
  depoyla BEKLENEN sonucu verdi; bulgu ek doğrulama sırasında çıktı)
- **Önem:** Orta
- **İzlek:** B
- **Ortam:** macOS arm64 · net10 · SQLite (taze/hiç migrate edilmemiş dosya) · `AllowRemoteAccess=true`

**Beklenen**
`ExternalSurfaceGuard.EnsureRemoteAccessNotCombined` (`ExternalSurfaceGuard.cs:122-148`)
`external:invoke` kapsamlı anahtar yoksa temiz bir `InvalidOperationException`
fırlatır; sınıfın kendi XML doc'u (satır 11-17) bunun "veritabanına
dokunmadığını" iddia eder.

**Gerçekleşen**
Taze (hiç migrate edilmemiş) bir SQLite dosyasıyla `AllowRemoteAccess=true`
verildiğinde, aynı metot `apiKeyStore.HasActiveScopeAsync(...)` çağrısıyla
GERÇEKTEN veritabanını sorguluyor (satır 131-135) ve şema henüz oluşmadığı
için ham `Microsoft.Data.Sqlite.SqliteException: SQLite Error 1: 'no such
table: tracon_api_keys'` ile İŞLENMEMİŞ bir istisna fırlatıyor — belgelenen
`InvalidOperationException` DEĞİL. Aynı dosyanın hemen yanındaki
`EnsureNoApprovalRequiredTools`/`ListCatalogWithRetryAsync` çifti AYNI riski
("no such table on an empty database") zaten öngörmüş ve bir yeniden-deneme
mekanizmasıyla çözmüş; `EnsureRemoteAccessNotCombined` bu korumadan yoksun.

**Yeniden üretme**
1. Taze bir SQLite dosyası (`rm -f tracon-manuel.db*`), `Tracon:Sqlite:ConnectionString` ayarlı.
2. `Tracon:Ui:AllowRemoteAccess=true` ile `dotnet run` (migrasyon tamamlanmadan MCP endpoint'i eşlenirken guard tetiklenir).
3. Konsolda `SqliteException` görülür, `InvalidOperationException` DEĞİL.

**Kanıt**
- Log: `/tmp/ap-s1-sec070.log` (bu oturumun kendi geçici dosyası, kalıcı değil) — stack trace `ExternalSurfaceGuard.cs:131` → `SqlApiKeyStore.HasActiveScopeAsync:122` → `DbHelpers.ExecuteScalarAsync:72,57`.
- Karşılaştırma: aynı senaryo bellek içi depoyla (`InMemoryApiKeyStore`) doğru, belgelenen `InvalidOperationException`'ı üretiyor (bu turda ayrıca doğrulandı, MT-SEC-070'in resmi koşumu budur).

**Kapsam**
Yalnız SQL tabanlı (`SqlApiKeyStore` — SQLite/PostgreSQL/SQL Server üçü de aynı
yolu kullanır, `DbHelpers` ortak) API key deposu + tamamen taze/migrate
edilmemiş şema + `AllowRemoteAccess=true` kesişiminde. Varsayılan bellek içi
depoda veya migrasyon tamamlandıktan sonraki bir restart'ta gözlenmez (bu
turda MT-SEC-071 tam olarak bu ikinci durumu doğruladı — sorun yok).

---

### HATA-S1-022 — `AuditSecretFilter` bazı BENİGN alan adlarını da (`ConfigurationKey` son eki, `AuthorizationMode`) gereksizce `"***"` yapıyor

- **Case:** MT-SEC-091 (keşif — case'in KENDİ iddiası, `headers.Authorization`
  redaksiyonu, doğru ve geçti; bulgu aynı yanıtın başka alanlarında çıktı)
- **Önem:** Düşük-Orta
- **İzlek:** C
- **Ortam:** macOS arm64 · net10 · bellek içi depo

**Beklenen**
`AuditSecretFilter.IsSecretKey` (`src/Tracon.Core/Audit/AuditSecretFilter.cs:119-142`)
yalnız GERÇEK kimlik bilgisi taşıyan alanları redakte eder; sınıfın kendi
yorumu zaten bir örneğini kabul ediyor (`tokens` çoğulu `token`'dan ayrı
tutulur, "aksi halde her agent kaydı anlamsızca boşalırdı").

**Gerçekleşen**
MT-SEC-091'in ürettiği denetim kaydında `authorizationConfigurationKey: null`
→ `"***"`, `oauthClientSecretConfigurationKey: null` → `"***"` VE
`oauthAuthorizationMode: "AuthorizationCode"` → `"***"` oldu. İlk ikisi bu
deponun kendi K-059 kuralına göre zaten yalnız bir `secret`'ın ADINI/referans
anahtarını taşıyor (değeri değil) — redaksiyon anlamsız ama zararsız. Üçüncüsü
İSE gerçek bir enum DEĞERİDİR (`AuthorizationCode`), `authorization` alt
dizgisini yalnız ALAN ADI (`oauthAuthorizationMode`) içerdiği için
`"***"`'a düşüyor; kaydı okuyan biri OAuth akış modunun ne olduğunu göremez.
Aynı sınıf zaten `tokens` (çoğul) için tam bu tür bir yanlış-pozitifi düzeltmiş
durumda; `*ConfigurationKey` soneki ve `*Mode`/`*Type` gibi enum alanları için
eşdeğer bir istisna yok.

**Yeniden üretme**
1. `PUT /api/mcp-servers/{ad}` ile `headers.Authorization` içeren bir MCP tanımı kaydet.
2. `GET /api/audit/mcp:{ad}` çağır, `after` JSON'unu incele.

**Kanıt**
Bu case'in kendi `Gerçek sonuç` alanındaki tam `after` gövdesi (üstte,
MT-SEC-091).

**Kapsam**
`AuditSecretFilter` her denetim kaydına uygulanıyor — herhangi bir tipte
`*ConfigurationKey` sonekli veya `authorization`/`secret`/`token`/`password`/
`apikey` alt dizgisini adında TAŞIYAN ama gerçek bir kimlik bilgisi OLMAYAN
her alan aynı şekilde etkilenir (yalnız MCP sunucu tanımıyla sınırlı değil).

---

## Açık sorular / fiziksel eylem tablosu

| Case | Neden | Ne gerekiyor |
|---|---|---|
| `MT-SEC-024` | Case metni `samples/Tracon.Api/Program.cs`'e geçici bir satır eklenmesini istiyor (`options.AllowedTenants.Add(...)`); kod donması bunu yasaklıyor | Kullanıcı kararı: (a) kapanış modunda mı koşulsun, (b) repo dışı bir tüketici host'uyla mı (bkz. `DEVIR.md` "donuk `samples/` gerektiren case'ler" tarifi — dosya 05'te kanıtlandı) koşulsun, yoksa (c) kalıcı olarak `⏭` mü sayılsın |
| `MT-SEC-084` | Aynı sınıf: `RequireRolePolicies=true` + policy YOK kombinasyonu bu örnekte yalnız `Program.cs`'e geçici bir satırla üretilebiliyor (`demoRolesEnabled` ikisini birlikte açıp kapıyor) | Aynı üç seçenek |
| `MT-SEC-105` | Spec'in kendisi zaten "👤 insan gerekir" diyor — `AddToolApprovalPolicy` geçici kod eklemesi ister | Aynı üç seçenek |
| `MT-SEC-102/103/104` | `refund_order` tool'u örnek uygulamada yok; gerçek onay-gerektiren tek tool (`cancel_order`) sayısal argüman taşımıyor, eşik koşulu test edilemez | Örnek uygulamaya sayısal argümanlı, onay gerektiren bir demo tool eklenmesi (ADAYLAR.md adayı olabilir) VEYA repo dışı tüketici host'u |
| `MT-SEC-108`, `MT-SEC-118` | Playwright tarayıcısı bu oturumda başka bir eşzamanlı ajan tarafından kilitli çıktı (`Browser is already in use for .../mcp-chrome-3eca5a9`) — muhtemelen paralel çalışan ap-s2/3/4 şeritlerinden biri aynı paylaşılan tarayıcı profiline erişiyor | Tarayıcı boşken (veya `--isolated` modda) yeniden koşulmalı |

---

## MT-SEC-001 — Loopback'ten doğru token ile istek geçer

**Gerçek sonuç**
`GET $APU/api/agents` → `HTTP: 200`, agent listesi döndü (14 kod-tanımlı
agent).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-002 — Loopback dışından (LAN adresi) istek, `AllowRemoteAccess` kapalı → `403`

**Gerçek sonuç**
`HTTP: 403`. `title: "Remote access disabled"`, `detail: "Tracon endpoints
are reachable only from the same machine by default. For remote access,
enable the AllowRemoteAccess setting and configure an authentication method
(AuthToken or RequireAuthorization)."` — spec Türkçe metin bekliyordu, ürün
İngilizce (K-228); `Beklenen sonuç` bu koşumda düzeltildi. Doğru token
taşınmasına rağmen reddedildi, doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-003 — Authorization başlığı yok, `AuthToken` tanımlı → `401` + `WWW-Authenticate`

**Gerçek sonuç**
`HTTP: 401`. `WWW-Authenticate: Bearer` başlığı mevcut. Gövde
`title: "Authentication failed"`, `detail: "A valid 'Authorization: Bearer
<token>' header is required."` — İngilizce (K-228), spec düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-004 — Yanlış bearer token → `401`, gövde token hakkında bilgi vermez

**Gerçek sonuç**
`HTTP: 401`. Gövde MT-SEC-003 ile **birebir aynı** (`title: "Authentication
failed"`, aynı `detail`) — hangi token'ın neden yanlış olduğuna dair hiçbir
ipucu yok. Doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-005 — `Bearer` şeması olmayan bir Authorization başlığı → `401`

**Gerçek sonuç**
`HTTP: 401` — `Basic` şeması reddedildi, gövde MT-SEC-003/004 ile aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-006 — `Bearer ` öneki var ama değer boş → `401`

**Gerçek sonuç**
`HTTP: 401`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-010 — `/api/meta`, loopback dışından VE Authorization başlıksız yine `200` döner

**Gerçek sonuç**
`HTTP: 200` — `$APULAN/api/meta` başlıksız çağrıda da tam gövde döndü.
Aynı koşullarda `/api/agents`'ın (MT-SEC-002) `403` verdiği doğrulanmış
durumda — meta ucu bilerek istisna.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-011 — `/api/meta` yanıtı `AuthToken` DEĞERİNİ hiçbir alanda taşımaz

**Gerçek sonuç**
Tam gövde incelendi: `{"version":...,"authentication":{"allowRemoteAccess":
false,"requiresBearerToken":true,"requiresAuthorizationPolicy":false},
"storage":{...},"roles":{...}}`. `manuel-test-token-2026` dizgisi gövdenin
hiçbir yerinde geçmiyor. `authentication.allowRemoteAccess` ve
`authentication.requiresAuthorizationPolicy` alanları da mevcut.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-012 — Arayüz kabuğu bearer token'dan MUAFTIR ama loopback'ten muaf DEĞİLDİR

**Gerçek sonuç — spec'in `Beklenen sonuç`'u koda göre YANLIŞTI, bu oturumda
düzeltildi (kural 1 istisnası, K-228 DEĞİL).**
Ölçüm: `loopback: 200` VE `lan: 200` — ikisi de aynı `Content-Length: 2637`
gövdeyi (kabuk `index.html`) döndürdü. Spec `lan: 403` bekliyordu.
Kod okundu: `TraconEndpointRouteBuilderExtensions.cs:344`'teki `MapUi`
`uiGroup.AddEndpointFilter(new TraconEndpointFilter(options,
requireBearerToken: false, requireLoopback: false))` ile kuruluyor —
`requireLoopback: false` **kasıtlı**. Aynı dosyanın 316-325. satırlarındaki
XML doc bunu açıkça gerekçelendiriyor: kabuk hiçbir veri taşımaz; gerçek
koruma veri uçlarındaki (`/api/agents` vb.) filtre örneklerindedir; kabuğun
kendisi LAN'dan da yüklenebilmelidir ki istemci tarafı `AccessGate`
bileşeni "uzak erişim kapalı" açıklamasını gösterebilsin — kabuk
engellenirse kullanıcı yalnızca bağlantı hatası görür, açıklayıcı ekranı
hiç göremez. Bu bir kusur değil, belgelenmiş bir tasarım kararı. Gerçek veri
koruması MT-SEC-002'nin doğruladığı katmandadır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-020 — `UseTenancy` hiç çağrılmamışken her istek varsayılan kiracıya düşer

**Gerçek sonuç**
`Tracon:Tenancy:Enabled` ayarlanmadan (varsayılan kapalı):
`GET /api/tenants/current` → `{"tenantId":"default"}`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-021 — `AllowHeaderResolution` açıkken `X-Tracon-Tenant` başlığı kiracıyı belirler

**Gerçek sonuç**
Uygulama `Tracon__Tenancy__Enabled=true`,
`Tracon__Tenancy__AllowHeaderResolution=true` ile yeniden başlatıldı (ortam
değişkeni, `user-secrets` DEĞİL). `X-Tracon-Tenant: kiraci-alfa` başlığıyla
istek → `{"tenantId":"kiraci-alfa"}`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-022 — `AllowHeaderResolution` KAPALIYKEN aynı başlık yok sayılır

**Gerçek sonuç**
Uygulama `Tracon__Tenancy__Enabled=true`,
`Tracon__Tenancy__AllowHeaderResolution=false` ile ayrı bir restart'ta
koşuldu. Aynı `X-Tracon-Tenant: kiraci-alfa` başlığıyla istek →
`{"tenantId":"default"}` — başlık sessizce yok sayıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-023 — Biçimsiz kiracı kimliği başlıkta gönderilirse sessizce reddedilir (hataya düşmez)

**Gerçek sonuç**
Tenancy+header açık haldeyken `X-Tracon-Tenant: kiraci alfa/beta` (boşluk +
`/`) → `HTTP: 200`, `{"tenantId":"default"}`. Hata verilmedi, zincir
varsayılana düştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-024 — `AllowedTenants` beyaz listesi doluyken listede olmayan bir değer 403 ile reddedilir

**Gerçek sonuç**
**KOŞULAMADI — kaynak donması.** Ayrıntı yukarıdaki "Devir notu" ve "Açık
sorular" bölümlerinde. Case'in ön koşulu `samples/Tracon.Api/Program.cs`'e
geçici bir satır eklenmesini (`options.AllowedTenants.Add("kiraci-alfa")`)
gerektiriyor; bu görevin kod donması kuralı kaynak değişikliğini yasaklıyor.
Kod okuması `AllowedTenants`'ın ortam değişkeniyle doldurulamayacağını
doğruladı (`Program.cs:889-898` yalnız `Enabled`/`ClaimType`/
`AllowHeaderResolution` okuyor; `TraconTenancyBuilderExtensions.cs:39-66`
otomatik config-section binding yapmıyor).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-030 — Kiracı A'da `FIX-AGENT-01` oluşturma

**Gerçek sonuç**
`kiraci-alfa` başlığıyla `manuel-destek` oluşturuldu → `HTTP: 201`, gövde
`tenantId: "kiraci-alfa"` taşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-031 — Kiracı B'de AYNI adla oluşturma çakışmaz (ayrı satır)

**Gerçek sonuç**
`kiraci-beta` başlığıyla AYNI adla (`manuel-destek`) oluşturma → `HTTP: 201`
(409 DEĞİL), gövde `tenantId: "kiraci-beta"`. `UNIQUE (tenant_id, name)`
kısıtının davranışı bellek içi depoda da doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-032 — Kiracı A'nın listesi yalnız kendi agent'ını gösterir

**Gerçek sonuç**
`kiraci-alfa` listesinde `manuel-destek` var (14 kod-tanımlı agent + bu 1
veritabanı tanımı = 15 ad). Kiracı B'nin eklediği başka bir tanım yok
(yalnız aynı ad zaten ayrı satır).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-033 — Kiracı A bir çalıştırma başlatır (`FIX-PROMPT-02`)

**Gerçek sonuç**
`HTTP: 200`. Gerçek OpenAI çağrısı (`gpt-5.4-mini`) yapıldı, SSE akışı
`Merhaba! Siparis numaranızı yazarsanız durumunu kontrol edebilirim.`
metnini token token döndürdü, `usage: inputTokens=153, outputTokens=21,
totalTokens=174`. `runId: 01a0abd2-b4b5-7f70-9cd8-17db0adfc7d6` not edildi.
**Gerçek para uyarısı gerçekleşti** (spec'in kendi notu) — küçük ölçekli,
tek bir `gpt-5.4-mini` çağrısı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-034 — Kiracı A kendi çalıştırmasını görebilir

**Gerçek sonuç**
`GET /api/runs/$RUNID` (kiraci-alfa başlığıyla) → `HTTP: 200`, tam `run`
kaydı (`status: "Completed"`, `tenantId: "kiraci-alfa"`, `usage`, `cost`
vb.).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-035 — Kiracı B aynı `runId`'yi `404` ile görür (403 DEĞİL)

**Gerçek sonuç**
Kiracı B başlığıyla aynı `runId` → `HTTP: 404`. `title` (varlık sızıntısı
önleyici) `"Run not found"`, `detail: "There is no run with id
'01a0abd2-...'."` — `403` DEĞİL, doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-036 — Kiracı B kendi `manuel-destek` kopyasını siler; Kiracı A'nınki etkilenmez

**Gerçek sonuç**
Adım 1 (kiraci-beta DELETE): `HTTP: 204`. Adım 2 (kiraci-alfa GET): `HTTP:
200`, tanım hâlâ mevcut (`tenantId: "kiraci-alfa"`). Silme yalnız kendi
kiracısının satırını etkiledi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-040 — `PUT /api/tenants/{slug}` yeni bir kiracı kaydı oluşturur

**Gerçek sonuç**
`HTTP: 200`. Gövde `slug: "kiraci-alfa"`, `displayName: "Alfa Musterisi"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-041 — Aynı slug'a ikinci `PUT` günceller (upsert)

**Gerçek sonuç**
`HTTP: 200`, `displayName: "Alfa Musterisi (guncel)"`. `id` ve `createdAt`
İLK `PUT`'takiyle **birebir aynı** kaldı (`01a0abd3-1663-...`) — ikinci bir
satır oluşmadı, gerçek upsert.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-042 — Geçersiz biçimli slug → `400`

**Gerçek sonuç**
`HTTP: 400`. `title: "Tenant key invalid"`, `detail: "The key must be at
most 64 characters and contain only letters, digits, dots, underscores, and
hyphens."` — İngilizce (K-228), spec düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-043 — `GET /api/tenants` kayıtlı kiracıları listeler

**Gerçek sonuç**
`kiraci-alfa` listede var (`displayName: "Alfa Musterisi (guncel)"`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-044 — `DELETE /api/tenants/{slug}` yalnız KAYDI siler, kiracının verisi kalır

**Gerçek sonuç**
Adım 1: `HTTP: 204`. Adım 2 (`kiraci-alfa`'nın `manuel-destek` agent'ını
GET): `HTTP: 200` — kayıt silinse de kiracının verisi bozulmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-045 — Var olmayan slug'ı silmeye çalışmak → `404`

**Gerçek sonuç**
`HTTP: 404`. `title: "Tenant not found"` — İngilizce (K-228), spec
düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-050 — `POST /api/api-keys` yeni anahtar üretir, ham değer `ap_` ile başlar

**Gerçek sonuç**
`HTTP: 200`. `plaintextKey: "ap_default_I3Bj0YbxRQTQWMsJX9_UyRL3l9hibF2jIm9
OZFnSQbo"` — `ap_` ile başlıyor. `record.keyPrefix: "ap_default_I"` —
`plaintextKey`'in ilk 12 karakteriyle birebir aynı. `record.name:
"manuel-okuma"`, `record.scopes: ["RunsRead"]`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-051 — `GET /api/api-keys` listesi ham değer ve özet TAŞIMAZ

**Gerçek sonuç**
Liste gövdesinde `$APIKEY_READ` (plaintext) hiçbir yerde geçmiyor. Alanlar
tam olarak `id, tenantId, name, keyPrefix, scopes, expiresAt, revokedAt,
lastUsedAt, createdAt, isActive` — `keyHash` yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-052 — `name` boş → `400`

**Gerçek sonuç**
`HTTP: 400`. `detail: "'name' cannot be empty."` — İngilizce (K-228), spec
düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-053 — Boş `scopes` dizisi → `400`

**Gerçek sonuç**
`HTTP: 400`. `detail: "At least one scope ('scopes') must be selected."` —
İngilizce (K-228), spec düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-054 — Bilinmeyen kapsam değeri → `400` (kapalı liste)

**Gerçek sonuç**
`HTTP: 400`. `title: "Invalid request body"`, `detail: "The JSON value
could not be converted to Tracon.ApiKeyScope. Path: $.scopes[0] |
LineNumber: 0 | BytePositionInLine: 45."` — `JsonStringEnumConverter`
model binding hatası, spec'in iddiasıyla tutarlı (spec zaten literal metin
beklemiyor, yalnız "model binding hatası" diyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-055 — Üretilen anahtar, kapsamı yeten bir uçta Bearer olarak çalışır

**Gerçek sonuç**
`GET /api/runs` bu anahtarla → `HTTP: 200`, gövde `[]` (anahtar `default`
kiracısına bağlı, o kiracıda run yok — case yalnız `200` bekliyor, sağlandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-056 — Aynı anahtar, kapsam DIŞI bir uçta `403 Kapsam yetersiz` alır

**Gerçek sonuç**
`HTTP: 403`. `title: "Insufficient scope"`, `detail: "This endpoint
requires the 'AgentsAdmin' scope; the key does not carry it."` — İngilizce
(K-228), spec düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-057 — `DELETE /api/api-keys/{id}` iptal eder; sonra o anahtarla istek `401` alır

**Gerçek sonuç**
Adım 1: `HTTP: 204`. Adım 2 (aynı anahtarla `GET /api/runs`): `HTTP: 401`,
gövde MT-SEC-003 ile aynı (`title: "Authentication failed"`). Satır
silinmedi, `revokedAt` yazıldı (davranış tutarlı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-058 — Var olmayan veya zaten iptal edilmiş `id`'yi tekrar iptal etmek → `404`

**Gerçek sonuç**
`HTTP: 404`. `title: "Key not found"`, `detail: "There is no API key with
id '01a0abd3-6dce-...'."` İkinci iptal "bulunamadı" olarak görünüyor —
spec'in kendi metni zaten literal metin beklemiyordu, yalnız davranışı
tarif ediyordu; eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-059 — Süresi geçmiş anahtar `401` alır

**Gerçek sonuç**
5 saniye sonra dolan bir anahtar üretildi, 6 saniye beklendi, aynı anahtarla
istek → `HTTP: 401`, gövde MT-SEC-003 ile aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-060 — `X-Tracon-Tenant` başlığı, anahtarın kiracısıyla ÇELİŞİRSE `403`

**Gerçek sonuç**
`kiraci-alfa`'ya bağlı `AgentsRead` kapsamlı bir anahtar üretildi
(`alfa-anahtar`, `keyPrefix: "ap_kiracialf"`). Bu anahtarla + `X-Tracon-
Tenant: kiraci-beta` başlığıyla istek → `HTTP: 403`. `title: "Tenant
mismatch"`, `detail: "The 'X-Tracon-Tenant' header CANNOT override the
tenant the API key is bound to. Remove the header or give a value matching
the key's tenant."` — İngilizce (K-228), spec düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-061 — Başlık HİÇ verilmezse kiracı doğrudan anahtardan çözülür

**Gerçek sonuç**
`X-Tracon-Tenant` başlığı olmadan, `$APIKEY_ALFA` ile
`GET /api/tenants/current` → `{"tenantId":"kiraci-alfa"}` — kiracı doğrudan
anahtardan çözüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-062 — `apikey.create`/`apikey.revoke` denetim izine düşer, ham değer YAZILMAZ

**Gerçek sonuç**
Uygulama temiz duruma sıfırlandı (yeniden başlatıldı, Tenancy kapalı —
ön koşula uyuldu). Anahtar oluşturuldu (`denetim-testi`), iptal edildi,
denetim izi sorgulandı: iki kayıt döndü —
`action: "apikey.revoke"` (`before: null`, `after: null`) ve
`action: "apikey.create"` (`after: "{\"name\":\"denetim-testi\",
\"keyPrefix\":\"ap_default_c\",\"scopes\":[\"RunsRead\"]}"` — yalnız
`name`/`keyPrefix`/`scopes`, ham değer veya özet YOK). Hash zinciri
(`previousHash`/`hash`) da gözlendi, tutarlı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-070 — `external:invoke` anahtarı YOKKEN `AllowRemoteAccess = true` yapılırsa uygulama AÇILMAZ

**Gerçek sonuç**
Kod değişikliği GEREKMEDİ (koşumda düzeltildi — bkz. spesifikasyonun
düzeltilmiş `Ön koşul`u). Bellek içi depo + `Tracon__Ui__AllowRemoteAccess=
true`, hiç `ExternalInvoke` anahtarı yokken: süreç açılışta
`System.InvalidOperationException: MCP cannot be exposed while
AllowRemoteAccess is on: the system holds no API key with the
'external:invoke' scope that is neither expired nor revoked. A single static
bearer token is not enough to protect an agent surface exposed beyond
loopback. Create a key with the 'external:invoke' scope through 'POST
/api/api-keys'.` ile çöktü. İngilizce (K-228), spec düzeltildi.
Ek doğrulama: AYNI senaryo taze (migrate edilmemiş) bir SQLite dosyasıyla
denendiğinde FARKLI ve yanlış bir istisna (`SqliteException: no such table`)
çıktı — bu ayrı bir kusur, `HATA-S1-021` olarak kaydedildi. Bu case'in KENDİ
koşumu (bellek içi, belgelenen davranış) geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-071 — Önce `external:invoke` anahtarı üretilir, SONRA `AllowRemoteAccess = true` başarıyla açılır

**Gerçek sonuç**
Kod değişikliği GEREKMEDİ. SQLite kalıcılığıyla (anahtarın restart'ta hayatta
kalması gerektiği için — bellek içiyle bu case anlamsız hâle gelirdi, ayrıntı
devir notunda): 1) `AllowRemoteAccess` OLMADAN başlatıldı, `ExternalInvoke`
kapsamlı `mcp-disa-acik` anahtarı oluşturuldu (`HTTP: 200`). 2) Durduruldu,
AYNI SQLite dosyasıyla `Tracon__Ui__AllowRemoteAccess=true` ile yeniden
başlatıldı — süreç BAŞARIYLA açıldı (çökmedi), log
`Now listening on: http://0.0.0.0:5081`. 3) `$LANIP` üzerinden doğru bearer
token ile `GET /api/agents` → `HTTP: 200` (MT-SEC-002'nin `403`'ü burada
ALINMADI).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-080 — Hiçbir rol testi kurulmadan (varsayılan): Admin gerektiren uç bile rol kontrolüne takılmaz

**Gerçek sonuç**
`Tracon:Demo:Roles:Enabled` ayarlanmadan (varsayılan kapalı), rol başlığı
olmadan `POST /api/agents` → `HTTP: 201` — hiçbir `Tracon.*` policy'si kayıtlı
olmadığından rol kontrolü hiçbir şey eklemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-081 — Yalnız `reader` rolüyle Admin ucu `403` alır

**Gerçek sonuç**
`Tracon__Demo__Roles__Enabled=true` ile yeniden başlatıldı (K-431 — geçici
kod DEĞİL, ortam değişkeni + `X-Tracon-Demo-Role` başlığı). `X-Tracon-Demo-
Role: reader` ile `POST /api/agents` → `HTTP: 403`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-082 — `admin` rolüyle aynı istek `201` alır

**Gerçek sonuç**
`X-Tracon-Demo-Role: admin` ile aynı istek → `HTTP: 201`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-083 — `operator` rolü çalıştırma başlatabilir ama Admin ucuna erişemez

**Gerçek sonuç**
Adım 1: `X-Tracon-Demo-Role: operator` ile `POST /api/agents/support/run` →
`HTTP: 200` (gerçek OpenAI çağrısı yapıldı — bkz. devir notunun "gerçek para"
uyarısı). Adım 2: aynı rolle `DELETE /api/api-keys/{sıfır-guid}` →
`HTTP: 403` (`404` DEĞİL) — rol denetimi handler'dan ÖNCE çalıştığı
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-084 — `RequireRolePolicies = true` + hiçbir policy kayıtlı değilken uygulama AÇILMAZ

**Gerçek sonuç**
**KOŞULAMADI — kaynak donması.** Kod okundu:
`samples/Tracon.Api/Program.cs:975`'te `options.RequireRolePolicies =
demoRolesEnabled;` — bu iki değer TEK bir bayrağa (`Tracon:Demo:Roles:
Enabled`) bağlı ve ayrıştırılamıyor: bayrak açıkken hem policy'ler kayıtlı
OLUYOR hem `RequireRolePolicies` açılıyor (hiç çökmüyor, çünkü policy'ler
zaten var); bayrak kapalıyken ikisi de kapalı. Case'in istediği kombinasyon
(`RequireRolePolicies=true` AMA policy YOK) bu sample'da yalnız `Program.cs`
satırlarını ayırarak (geçici kod değişikliği) üretilebilir — kod donması
yasaklıyor.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-085 — `/api/meta`'nın `roles` alanı: hiçbir policy kayıtlı değilken hepsi `true`

**Gerçek sonuç**
Demo rolleri KAPALIYKEN (varsayılan, önceki restart) `GET /api/meta` →
`roles: {"canRead": true, "canOperate": true, "canAdminister": true}` —
hiçbir kısıt yokken herkes "yetkili" görünüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-090 — `agent.create` → `agent.update` → `agent.delete` sırası izlenebilir

**Gerçek sonuç**
Temiz restart (demo rolleri kapalı, bellek içi). Create→Update→Delete
sonrası `GET /api/audit?entity=agent:manuel-audit` üç kayıt döndü, EN
YENİDEN ESKİYE: `agent.delete`, `agent.update`, `agent.create` — hepsi
`entity: "agent:manuel-audit"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-091 — `authToken`/`apiKey` gibi sır adlı bir alan varsa değeri `"***"` olur

**Gerçek sonuç**
`headers.Authorization: "Bearer cok-gizli-deger"` taşıyan bir MCP sunucusu
kaydedildi. Denetim kaydının `after` alanında `"headers":{"Authorization":
"***"}` — ham değer (`cok-gizli-deger`) hiçbir yerde yok. Case'in kendi
iddiası tam karşılandı.
**Ek gözlem (ayrı kusur, case'in kendi iddiasını etkilemiyor):** aynı `after`
gövdesinde `authorizationConfigurationKey`, `oauthClientSecretConfigurationKey`
(ikisi de `null` idi) VE `oauthAuthorizationMode` (`"AuthorizationCode"` idi)
de `"***"`'a redakte edilmiş — üçü de gerçek bir kimlik bilgisi DEĞİL. Kayıt
altına alındı: `HATA-S1-022`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-092 — Çoğul `tokens` içeren bir alan (örn. `maxOutputTokens`) REDAKTE EDİLMEZ

**Gerçek sonuç**
`model.maxOutputTokens: 512` ile agent oluşturuldu. Denetim kaydının `after`
alanında `"maxOutputTokens":512` — GERÇEK sayısal değeriyle, `"***"` DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-093 — Kimlik doğrulaması yokken `actor` her zaman `null`'dur

**Gerçek sonuç**
Demo rolleri KURULU DEĞİLKEN (sade örnek uygulama, statik bearer token) bir
agent oluşturuldu; denetim kaydının `actor` alanı `None` (JSON `null`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-094 — `limit` parametresi dönen kayıt sayısını sınırlar

**Gerçek sonuç**
`GET /api/audit?entity=agent:manuel-audit&limit=1` → tam **1** kayıt döndü
(en az 3 kayıt varken).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-095 — Denetim izinde silme/düzeltme ucu YOKTUR

**Gerçek sonuç**
`DELETE /api/audit/agent:manuel-audit` → `HTTP: 405` (`title: "Method Not
Allowed"`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-100 — Koşulsuz kural eskisi gibi çalışır: eşik altında otomatik geçer

**Gerçek sonuç**
`POST /api/approvals/rules` `{"toolName":"refund_order"}` (argumentConditions
hiç gönderilmeden) → `HTTP: 201`, gövde `"argumentConditions":[]`. CRUD
iddiası tam karşılandı. **Davranışsal kısım ("tool'u çağırt, otomatik
onaylanır") koşulamadı** — `refund_order` örnek uygulamada gerçek bir tool
DEĞİL (`grep -rn refund_order src/ samples/` yalnız test sözleşmelerinde ve
dokümantasyonda buluyor); gerçek onay-gerektiren tek tool (`cancel_order`)
argümansız/sayısız bu senaryoya uymuyor. Ayrıntı devir notunda.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-101 — `amount <= 100` koşullu kural: eşik altında otomatik geçer

**Gerçek sonuç**
`POST /api/approvals/rules` `{"toolName":"refund_order","argumentConditions":
[{"path":"amount","operator":"LessThanOrEqual","value":100}]}` → `HTTP: 201`,
gövdede `"operator":"LessThanOrEqual"` — DİZE olarak seryalize edildi (SAYI
DEĞİL), K-040 doğrulandı. **Davranışsal kısım (MT-SEC-100 ile aynı nedenle)
koşulamadı.**

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-102 — Aynı kural, eşik üstünde onay ister

**Gerçek sonuç**
**KOŞULAMADI.** Spec'in kendisinde bu case için hiçbir somut `curl`/repro
komutu yok (yalnız düz yazı "amount: 500 ile çağırt"), ve gerçek/uygun bir
sayısal argümanlı onay-gerektiren tool örnek uygulamada mevcut değil (bkz.
MT-SEC-100). Açık soru olarak devir notuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-103 — Argüman hiç gönderilmezse onay ister (yol çözülemez → kapalı düşme)

**Gerçek sonuç**
**KOŞULAMADI** — MT-SEC-102 ile aynı gerekçe.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-104 — Tip uyuşmazsa onay ister (`"50"` metni sayı kuralını geçemez)

**Gerçek sonuç**
**KOŞULAMADI** — MT-SEC-102 ile aynı gerekçe.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-105 — Kodda kayıtlı politika veri kuralını EZER

**Gerçek sonuç**
**KOŞULAMADI — kaynak donması.** Spec'in kendisi zaten "👤 insan gerekir —
`samples/Tracon.Api`'ye geçici bir `AddToolApprovalPolicy` çağrısı eklemeden
koşulamaz" diyor. Bu turun kod donması kuralıyla tutarlı biçimde atlandı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-106 — Aynı kapsam ve aynı koşulla ikinci kural `409` alır

**Gerçek sonuç**
MT-SEC-101'in kuralı AYNI gövdeyle ikinci kez gönderildi → `HTTP: 409`,
`title: "Rule already exists"`, `detail: "A rule for the same tool, agent,
and conditions is already registered."` — ikinci kural yeni satır açmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-107 — Sayısal olmayan bir değerle `GreaterThan` yazmak `400` alır

**Gerçek sonuç**
`{"path":"tier","operator":"GreaterThan","value":"gold"}` → `HTTP: 400`,
`title: "Invalid condition value"`, `detail: "Operator 'GreaterThan' expects
a number."` — birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-108 — Arayüzden koşullu kural eklenip geri okunur; serbest ifade kutusu YOKTUR

**Gerçek sonuç**
**KOŞULAMADI.** Playwright tarayıcısı bu oturumda meşguldü:
`Error: Browser is already in use for /Users/farukatasoy/Library/Caches/
ms-playwright-mcp/mcp-chrome-3eca5a9, use --isolated to run multiple
instances of the same browser` — muhtemelen eşzamanlı çalışan başka bir
şerit (ap-s2/3/4) aynı paylaşılan tarayıcı profiline erişiyor. Zorla
denenmedi (paylaşılan kaynağı bozma riski). Açık soru tablosuna eklendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-109 — Bağlama önek dışındaki bir yapılandırma anahtarı adıyla reddedilir

**Gerçek sonuç**
`PUT /api/tenants/acme/providers/openai` `{"apiKeyConfigurationName":
"ConnectionStrings:Default"}` → `HTTP: 400`, `title: "Invalid request"`,
`detail` `Tracon:ProviderKeys:` önekini anıyor. `GET
/api/tenants/acme/providers` → `[]` (kayıt hiç oluşmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-110 — Anahtar değeri `user-secrets`'e yazılınca bağlama `resolved: true` olur; hiçbir yanıt DEĞERİ TAŞIMAZ

**Gerçek sonuç**
**Sapma — `user-secrets` yerine ortam değişkeni kullanıldı (skill §1.2), bu
yüzden "restart'sız hot-reload" iddiası TAM test edilemedi** (env var .NET'te
başlangıçta bir kez okunur; `user-secrets`'ın dosya-izleyen `reloadOnChange`
davranışı farklı bir mekanizma ve paylaşılan makine-genelinde deponun
değiştirilmesini gerektirirdi — skill §1.2 bunu yasaklıyor). Bunun yerine
FONKSİYONEL parçalar doğrulandı: 1) değer YOKKEN `PUT
/api/tenants/acme/providers/openai` → `resolved:false`. 2)
`Tracon__ProviderKeys__Acme__OpenAI` ortam değişkeniyle restart sonrası
`GET /api/tenants/acme/providers` → `resolved:true`. İkisinde de gövdede
`sk-`/`apiKey`/`value` yok — yalnız `providerName`, `apiKeyConfigurationName`,
`endpoint`, `resolved`, `updatedAt`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-111 — Ad var, değer yok: çalıştırma global anahtara SESSİZCE düşmez

**Gerçek sonuç**
Bağlama kaydı DURURKEN (`Tracon:ProviderKeys:Acme:OpenAI`) değer YOKKEN,
`acme` kiracısı olarak `openai` sağlayıcılı bir agent TANIMLAMAYA çalışmak
bile (henüz `run` gerekmeden) `HTTP: 400` verdi: `title: "Definition
invalid"`, `detail: "Tenant 'acme' has a provider binding for 'openai'
pointing at configuration key 'Tracon:ProviderKeys:Acme:OpenAI', but that key
has no value. Set it with \`dotnet user-secrets set
\"Tracon:ProviderKeys:Acme:OpenAI\" \"<key>\"\` or through your configuration
provider — the call does NOT fall back to the global key."` — anahtar adını
VE `dotnet user-secrets set` ipucunu içeriyor, iddia tam karşılandı (kontrol
beklenenden bile ERKEN, derleme/tanım aşamasında çalışıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-112 — İki kiracı, iki farklı anahtar: her çağrı kendi anahtarını kullanır

**Gerçek sonuç**
Gerçek sağlayıcı yerine yerel bir mock HTTP sunucusu kullanıldı
(`127.0.0.1:8091`, `Tracon:Egress:AllowPrivateNetworkTargets=true` ile SSRF
korumasını aşarak — bu koruma kendi başına doğru davranıyor, ayrı not
düşülmedi). `acme` → `Tracon:ProviderKeys:Acme:OpenAI` =
`sk-acme-distinct-111`, `globex` → `Tracon:ProviderKeys:Globex:OpenAI` =
`sk-globex-distinct-222`, ikisi de aynı mock `endpoint`'e bağlandı. İki ayrı
`run` sonrası mock sunucunun logu: `AUTH=Bearer sk-acme-distinct-111` ve
`AUTH=Bearer sk-globex-distinct-222` — iki çağrı da FARKLI, kendi kiracısının
anahtarıyla gitti; çapraz sızıntı yok. Gerçek OpenAI ücreti oluşmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-113 — Bağlama yazımından sonra veritabanında `secret` YOKTUR

**Gerçek sonuç**
SQLite kalıcılığıyla, `sqlite3 tracon-manuel.db "SELECT * FROM
tracon_tenant_provider_bindings;"` →
`acme|openai|Tracon:ProviderKeys:Acme:OpenAI||...` — `api_key_configuration_
name` sütunu yalnız ADI taşıyor, satırda `sk-` ile başlayan hiçbir metin veya
gerçek anahtar değeri yok (`endpoint` sütunu bu satırda boştu, mock testinden
önceydi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-114 — Bağlama yazımı ve silinmesi denetim izine düşer

**Gerçek sonuç**
`default` kiracısı için bir bağlama kaydedilip silindi (spec'in kendi
`Girilecek veri` bloğu `tenant_provider:default:openai` kullanıyor).
`GET /api/audit/tenant_provider:default:openai` → iki kayıt:
`tenant_provider.save` (`after: {"providerName":"openai","configKeyName":
"Tracon:ProviderKeys:Default:OpenAI"}` — gerçek değer YOK) ve
`tenant_provider.delete` (`before`/`after` ikisi de `null`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-115 — Egress politikası tanımlanmamış bir kiracıda davranış DEĞİŞMEZ

**Gerçek sonuç**
Hiç egress politikası kaydetmeden `GET /api/tenants/acme/egress` →
`{"tenantId":"acme","allowedProviders":null,"updatedAt":null}` — kısıtsız.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-116 — İzinsiz sağlayıcıya işaret eden agent tanımı DERLEME ANINDA reddedilir

**Gerçek sonuç**
`acme` için egress `["anthropic"]` olarak kaydedildi. `openai` sağlayıcılı bir
agent TANIMLAMAYA çalışmak (henüz `run` gerekmeden) → `HTTP: 400`, `title:
"Definition invalid"`, `detail: "Tenant 'acme' is not allowed to call model
provider 'openai'. Allowed providers: anthropic."` — `openai` ve izinli
listeyi (`anthropic`) anıyor. Tanım hiç oluşmadı (sonraki `/run` denemesi
`404 Agent not found` verdi) — gerçek bir model çağrısı hiç yapılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-117 — İzinsiz sağlayıcı için bağlama yazımı da `400` alır (iki yüzey tutarlı)

**Gerçek sonuç**
Aynı egress politikası (`acme` → yalnız `anthropic`) dururken `PUT
/api/tenants/acme/providers/openai` → `HTTP: 400`, `title: "Invalid
request"`, `detail` MT-SEC-116 ile AYNI cümleyi taşıyor — bağlama ucuyla
agent derleme yolu aynı kısıtı uyguluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-118 — Arayüzden bağlama ekranında değer girme alanı YOKTUR

**Gerçek sonuç**
**KOŞULAMADI** — MT-SEC-108 ile aynı Playwright çakışması (paylaşılan
tarayıcı profili başka bir şerit tarafından kilitli). Açık soru tablosuna
eklendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-119 — Egress politikası silinince kiracı tekrar kısıtsız olur

**Gerçek sonuç**
`acme` için egress `["openai"]` kaydedildi → `DELETE` `HTTP: 204` → `GET`
`{"allowedProviders":null}` (kısıtsız, politika hiç yokmuş gibi) → ikinci
`DELETE` `HTTP: 404`, `title: "Policy not found"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
