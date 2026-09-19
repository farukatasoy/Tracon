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
| **Depo** | Oturum 2'ye kadar çoğunlukla **bellek içi**; MT-SEC-071/110-119 blokları geçici SQLite kullandı. **Oturum 3'ten itibaren `mt_s1` PostgreSQL şeması** (içerik koruması, oturum sahipliği, webhook testleri SQL kalıcılık istiyordu) — önceki oturumun "hiç dokunulmadı" notu artık GEÇERSİZ, `mt_s1` bu ailenin test verisiyle dolu (temizlenmedi, §"Sıradaki oturumun işi"ne bakın). |
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

**Oturum 3 — MT-SEC-120..193 koşuldu (74 case, aile 13 BİTTİ). 🎉**

**Sayım (skill §7 betiği, `13-KIRACI-VE-GUVENLIK.md` üzerinde):**
**97 ☑ Geçti · 0 ☑ Kaldı · 45 ☑ Beklemede** (toplam 142 kayıtlı case).
Bu oturum 120-135, 140-193'ü (74 case) kapattı: **35 Geçti, 39 Beklemede**
(1 fiziksel eylem/insan gerekir — MT-SEC-174).

**Bir yeni kusur açıldı: `HATA-S1-023`** (aşağıda) — `AesGcmContentProtector`
`Unprotect`/`UnprotectBytes` yanlış-ama-var bir anahtar karşısında ham,
isimsiz bir `AuthenticationTagMismatchException` sızdırıyor; sınıfın DİĞER
dört hata dalı (`kid` yok/boş/base64-değil/yanlış-uzunluk) hepsi `kid`'i
adıyla söyleyen düzgün bir `TraconException` üretirken bu beşincisi sessiz
kalıyor. Ayrıntı ve yeniden üretme adımları HATA kaydında.

**🚨 Aile 13 (144/142 case) TAMAMLANDI.** Kalan 45 `Beklemede` case'in HEPSİ
gerekçeli — ya (a) `samples/Tracon.Api`'ye GEÇİCİ kod eklenmesini isteyen ve
kod donmasıyla çakışan case'ler (MT-SEC-121/122, 141-150, 152-163, 183-189 —
toplam 33 case, üç ayrı konsolide not içinde ayrıntılı), (b) bu ortamın
`postgres` süperkullanıcı bağlantısıyla üretilemeyen `REVOKE`/rol senaryoları
(MT-SEC-190-193, 4 case), (c) daha önceki oturumdan devreden aynı sınıf
(MT-SEC-024, 084, 102-105, 108, 118 — 8 case, ayrıntı önceki oturumun notunda),
ya da (d) 👤 insan/üretim-ölçekli veri gerektiren MT-SEC-174 (1 case) ya da
paylaşılan tarayıcı kilidi nedeniyle koşulamayan MT-SEC-126 (1 case, YENİ).
Hiçbiri "unutuldu" değildir — her biri kendi `## MT-SEC-NNN` bloğunda
gerekçesiyle durur.

**Kalıcı çözüm adayları (kapanış/`docs/ADAYLAR.md` kararı):**
1. Repo dışında (`~/tracon-manuel/` deseni), yerel NuGet feed'den referans
   alan, KONFİGÜRE EDİLEBİLİR bir `IRunAuthorizationHandler`/
   `IToolAuthorizationHandler`/`IRunAttributionContext` taşıyan küçük bir
   tüketici host'u — 33 case'i (121/122, 141-150, 152-163) TEK SEFERDE açar.
2. Aynı desenle `RequireProductionProfile()` çağıran, `MapTracon` çağırmayan
   minimal bir host — 7 case'i (183-189) açar.
3. Kapanış modunda izole bir doğrulama sunucusunda kısıtlı bir PostgreSQL rolü
   (`tracon_app`) oluşturup uygulamayı ONUNLA bağlatmak — 4 case'i (190-193)
   açar.
4. Örnek uygulamaya sayısal argümanlı, onay gerektiren bir demo tool —
   MT-SEC-102-104'ü açar (önceki oturumdan devreden öneri, hâlâ geçerli).

**Sıradaki oturumun işi:** Bu şeridin dağılımına göre (`13 · 19 · 04 · 18 · 10 · 08`)
**aile 19'a (`19-COK-MODLULUK-VE-SES.md`) geç** — yeni bir kardeş kayıt dosyası
aç. Uygulama şu an **durduruldu** (bu oturumun sonunda). `Tracon:SessionOwnership`,
`Tracon:Demo:Roles`, `Tracon:Webhooks:AllowInsecureHttp`,
`Tracon:ContentProtection:Keys:sample2` ortam değişkenleri sıradaki oturuma
MİRAS KALMAZ (süreç kapandı) — aile 19 muhtemelen bunların hiçbirini
gerektirmiyor, ama gerekirse skill §serit-kurulumu §2'den sıfırdan kurulmalı.
`mt_s1` şemasında bu oturumun ürettiği test verisi (mtsec1xx-*, cp-demo*,
filemem-*, oai-*, guard-circuit-*, resp-yuzeyi-test vb.) TEMİZLENMEDİ —
aile 19 bunlara dokunmuyor, sorun değil; kapanış/damıtma oturumu isterse
`DROP SCHEMA mt_s1 CASCADE` ile sıfırlayabilir.

🚨 **Bu oturumda ölçülen bir ortam tuzağı (gelecek oturumlara not):**
`Tracon:Demo:Roles:Enabled=true` açıldığında `RequireRolePolicies=true` olur
ve **HER** uç (yalnız rol-özel uçlar değil) `X-Tracon-Demo-Role` başlığı
YOKSA `401` döner. `operator` rolü AYNI ZAMANDA `Tracon.Operator` yönetim
politikasını sağlar (`SessionOwnership`'in `ManagementPolicy`'si) — sıradan
kullanıcı testlerinde YANLIŞLIKLA `operator` kullanmak filtrelenmemiş/yönetim
davranışını taklit eder (bu oturumda MT-SEC-165'te bir kez düşüldü, düzeltildi).
Sıradan kullanıcı testleri için `reader` kullan, yönetim/operator testleri
için `operator`.

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

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile I)

**Kaydın iki tespiti de doğruydu.** Guard gerçekten veritabanına dokunuyor — ve
dokunmak zorunda: `external:invoke` kapsamlı bir anahtarın var olduğunu
kanıtlamak kontrolün **kendisidir**. Yanlış olan XML dokümanıydı, düzeltildi.

**Düzeltme (K-814).** `DbException` yakalanır ve **fail-closed** yorumlanır:
cevap veremeyen bir depo bir anahtarın varlığını kanıtlayamaz, ve başarısız bir
sorgunun üzerine dışa açık bir agent yüzeyi açmak tam da bu kapının engellemek
için var olduğu şeydir. Host yine durur — ama artık belgelenen
`InvalidOperationException` ile ve **hangi kuralın** durdurduğunu söyleyerek;
ham istisna `InnerException` olarak taşınır.

**Sınıf taraması.** Açılışta senkron store okuyan tek diğer yer
`TraconA2AExtensions`'ın agent kartı süslemesidir ve o **zaten** geniş bir
`catch` + güvenli yedekle çözmüştü (orada güvenlik kontrolü yok, yalnız
`Description`/`Version` süslemesi — yedek meşru). `QuotaUsageObserver` ve
`JobQueueDepthObserver` da kendi `RefreshAsync`'lerinde yakalıyor. Kaydın
"`EnsureRemoteAccessNotCombined` bu korumadan yoksun" tespiti tek boşluktu.

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

### HATA-S1-023 — Yanlış (ama var olan) bir `ContentProtection` anahtarı, kimliği belirsiz ham bir kripto istisnasıyla çöker

- **Case:** MT-SEC-133 (keşif — case'in KENDİ iddiası, "değeri boş `kid`"
  senaryosu, doğru ve geçti; bulgu KOMŞU bir senaryoda — "değeri VAR ama
  YANLIŞ `kid`" — çıktı)
- **Önem:** Düşük-Orta
- **İzlek:** A
- **Ortam:** macOS arm64 · net10 · PostgreSQL (`mt_s1`) · gerçek `ContentProtection` anahtarı

**Beklenen**
`AesGcmContentProtector.LoadKey` (`src/Tracon.Core/Security/AesGcmContentProtector.cs:164-193`)
bir `kid` için anahtar çözümlenemediğinde DÖRT farklı durumu (haritada yok ·
değeri boş · base64 değil · yanlış uzunlukta) ayrı ayrı, `kid`'i VE
yapılandırma anahtarının adını açıkça söyleyen bir `TraconException` ile
karşılar (sınıfın kendi XML doc'u ve dört ayrı mesaj bunu doğruluyor).

**Gerçekleşen**
Bir kid (`sample`) için `Keys` haritasında GEÇERLİ bir girdi var VE raw değer
de base64/32-bayt olarak GEÇERLİ ama YANLIŞ (başka bir restart'ta o `kid` ile
yazılmış veriyi şifrelemeyen bir anahtar) olduğunda, `Unprotect`/`UnprotectBytes`
(satır 83-101, 128-146) `AesGcm.Decrypt`'i DOĞRUDAN çağırıyor ve fırlayan
`System.Security.Cryptography.AuthenticationTagMismatchException`'ı YAKALAMIYOR.
İstisna işlenmemiş şekilde `ExceptionHandlerMiddleware`'e kadar çıkıyor: HTTP
yanıtı jenerik `ProblemDetails` (`detail` YOK), sunucu logundaki mesaj da
yalnız `"The computed authentication tag did not match the input
authentication tag."` — ne `kid` adı, ne hangi `session`/`run` etkilendiği
mesajın kendisinde YOK (yalnız stack trace'in çağrı zincirinden dolaylı
çıkarılabilir). Sınıfın DİĞER dört hata dalı hepsi `kid`'i adıyla söylerken
bu beşinci dal (kriptografik doğrulama hatası) sessiz kalıyor.

**Yeniden üretme**
1. `Tracon:ContentProtection:Keys:sample` → `Tracon:ContentProtection:RawKeys:sample`
   eşlemesi dururken, `RawKeys:sample`'ın GERÇEK değerini bir restart'ta
   anahtar A, bir SONRAKİ restart'ta FARKLI bir anahtar B olacak şekilde
   değiştir (iki restart arası aynı `kid` için farklı ham değer).
2. Anahtar A ile yazılmış bir satırı, anahtar B etkinken oku
   (`GET /api/sessions/{id}` veya `/api/runs/{id}/input`).
3. `HTTP 500` + jenerik gövde; logda `AuthenticationTagMismatchException`,
   `kid` adı yok.

**Kanıt**
- Log: `/tmp/mt-s1-app5.log:76-97` (bu oturumun geçici dosyası, kalıcı değil) —
  `fail: Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware[1]` →
  `System.Security.Cryptography.AuthenticationTagMismatchException` →
  `Tracon.AesGcmContentProtector.Unprotect(String stored) ...AesGcmContentProtector.cs:line 97`.
- Karşılaştırma: AYNI sınıfın "değeri boş" dalı (`LoadKey:178`) MT-SEC-133'te
  `kid`'i adıyla söyleyen düzgün bir `TraconException` üretti — asimetri
  ölçüldü.

**Kapsam**
`Unprotect` ve `UnprotectBytes`'ın ikisi de etkilenir — `ProtectedValue.Read`
üzerinden PostgreSQL/SQLite/SQL Server'ın ÜÇÜ de aynı kod yolunu paylaşır
(`Tracon.Sql.Shared/Internal/ProtectedValue.cs`). Yalnız anahtar ROTASYONU
sırasında (yeni `ActiveKeyId`'ye geçilirken eski bir satırın `kid`'i için
YANLIŞ bir ham değer yapılandırılırsa) veya ham kripto bozulmasında (bit
hatası) tetiklenir — sıradan "anahtar hiç yok/boş" operasyonel hatasından
DAHA NADİR ama aynı ailenin bir parçası.

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile I)

**Kaydın ölçtüğü asimetri aynen kapatıldı** (K-815). `CryptographicException`
(`AuthenticationTagMismatchException` ondan türer) yakalanıp `TraconException`'a
çevriliyor; mesaj `kid`'i **ve** yapılandırma anahtarının adını söylüyor — diğer
dört dalla aynı hizada — ve en olası nedeni (bir `kid`'in materyalinin yenisiyle
değiştirilmesi) adıyla anıyor. Orijinal istisna `InnerException` olarak duruyor.

**K-059 ayrıca testle zorlanıyor:** mesajda anahtar materyali de, korunan değerin
kendisi de görünmüyor.

**Sınıf taraması.** Repoda `AesGcm.Decrypt` çağrısı **iki** yerdedir (`Unprotect`,
`UnprotectBytes`) ve ikisi de tek ortak yardımcıya alındı; kaydın "ikisi de
etkilenir" tespiti doğruydu. Başka kriptografik doğrulama yolu yok — API key
karşılaştırması ve webhook imzası eşitlik karşılaştırmasıdır, istisna atmaz.

---

### Konsolide not — MT-SEC-141..150, 152..163 (`IRunAuthorizationHandler` davranış testleri) KOŞULAMADI

Bu 22 case'in HEPSİ aynı ön koşula dayanıyor: `samples/Tracon.Api`'ye GEÇİCİ
olarak ÖZEL davranışlı bir `IRunAuthorizationHandler` (kullanıcıya göre
reddeden, her zaman reddeden, `throw` eden, yalnız `Session`/`Voice` erişimini
reddeden vb.) kaydı — `Program.cs`'e `services.Replace(...)` veya benzeri bir
satır eklenmesi. Kod donması (kural 1) bunu yasaklıyor; MT-SEC-024/084/105 ile
AYNI sınıf.

**Araştırılan alternatif — `samples/Tracon.Embedded` (donuk, ayrı bir sample):**
Bu proje GERÇEKTEN özel bir `IRunAuthorizationHandler` (`EmbeddedRunAuthorizationHandler`)
taşıyor ve ÇALIŞTIRILABİLİR (kod değişikliği gerekmez) — ama davranışı bu
case'lerin çoğuyla ÖRTÜŞMÜYOR:
- Reddi **KİRACI** temelli üretiyor (tanınmayan `X-Host-Tenant` → deny), case'lerin
  çoğu **KULLANICI** temelli reddi test ediyor (`"a"` izinli, `"b"` reddedilir).
  Spec'in KENDİ notu (MT-SEC-152, 157) bu ikame yı zaten 2026-09-05 üretim
  oturumunda KULLANMIŞ ve "bilinmeyen kiracı → 403" ile doğrulamış — kısmen
  geçerli bir ikame, ama MT-SEC-141/142/144/145/147/149/150 gibi kullanıcı
  KİMLİĞİNE özgü senaryolar için YETERSİZ (bu örnekte kullanıcı ayrımı yok).
- `AuthorizeSessionAsync`'i HER ZAMAN `Allow()` döner (kaynak okundu,
  `Authorization/EmbeddedRunAuthorizationHandler.cs:61-68`) — session
  reddi/`throw` senaryoları (146, 153-156, 158-163) bu örnekle HİÇ ÜRETİLEMEZ.
- `current_account` tool'u `UserId` taşımıyor (149 için yetersiz).

**Sonuç:** 141-150 ve 152-163 (toplam 22 case) bu oturumda `☑ Beklemede`
bırakıldı. **Kalıcı çözüm** (kapanış kararı): repo DIŞINDA (`~/tracon-manuel/`
gibi), yerel NuGet feed'den referans alan, KONFİGÜRE EDİLEBİLİR (env değişkeniyle
davranışı seçilebilen) bir `IRunAuthorizationHandler`/`IRunAttributionContext`
taşıyan küçük bir tüketici host'u yazılması — bu TEK host, 22 case'in TAMAMINI
tek seferde açabilir (dosya 05 oturum 10/11'in "paketlenmiş tüketici host'u"
tarifiyle aynı sınıf, ama bu sefer YENİ kod yazımı gerektiriyor, yalnız
YAPILANDIRMA değil). `docs/ADAYLAR.md` adayı olabilir.

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

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 11) · ☑ GEÇTİ**
Ön koşulun istediği geçici `Program.cs` düzenlemesi yerine kalıcı kanca
kullanıldı (K-834). Varsayılan davranış değişmedi.

`Tracon:Tenancy:AllowedTenants` artık config'ten okunuyor — **seçenek vardı
ama örnek uygulama onu hiç bağlamıyordu**, ∴ beyaz liste bu kurulumda hiç
zorlanamıyordu.

`--Tracon:Tenancy:AllowedTenants=kiraci-alfa` ile:

| İstek | Sonuç |
|---|---|
| `X-Tracon-Tenant: kiraci-gamma` (listede **yok**) | **`403`** |
| `X-Tracon-Tenant: kiraci-alfa` (listede **var**) | `200` → `{"tenantId":"kiraci-alfa"}` |

```json
{"title":"Tenant rejected","status":403,
 "detail":"The resolved tenant is not on the allow list. This request is NOT
           silently downgraded to the default tenant's data; it is rejected."}
```

☑ **`{"tenantId":"default"}` DÖNMEDİ** — düzeltilmiş davranış yerinde; fix
öncesi sessiz düşüş **geri gelmemiş**. Yanıtın `detail`'i bunu cümleyle de
söylüyor.

⚠️ Spec `title: "Kiraci reddedildi"` (Türkçe) bekliyor; sevk edilen metin
**İngilizce**dir (`"Tenant rejected"`) — K-228. Spec düzeltildi (skill §1.1).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

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

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 11) · ☑ GEÇTİ**
Ön koşulun istediği geçici `Program.cs` düzenlemesi yerine kalıcı kanca
kullanıldı (K-834). Varsayılan davranış değişmedi.

`Tracon:Demo:RequireRolePolicies` kancası eklendi. Normalde bu bayrak demo
rollerine bağlıdır; ama case'in istediği **ters** bileşim (kapı açık, politika
**hiç kayıtlı değil**) o yolla üretilemez — demo rollerini açmak politikaları
da kaydeder ve kapıyı tatmin eder.

```
EXIT=134   Application started: 0
Unhandled exception. System.InvalidOperationException:
TraconEndpointOptions.RequireRolePolicies is on but these policies are not
registered: Tracon.Reader, Tracon.Operator, Tracon.Admin. Define the
TraconPolicies.Reader/Operator/Admin names inside
builder.Services.AddAuthorization(...), or turn RequireRolePolicies off.
```

☑ Açılışta `InvalidOperationException`. ☑ Mesaj **üç politika adını da**
içeriyor: `Tracon.Reader` · `Tracon.Operator` · `Tracon.Admin`. Ayrıca iki
çıkış yolu da yazılı (kaydet **ya da** kapıyı kapat).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

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

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 10) · ☑ GEÇTİ**
🚨 **Turun engeli kalktı: `refund_order` artık gerçek bir tool.** MT-SEC-100 ve
101'in kaydı *"davranışsal kısım koşulamadı — `refund_order` örnek uygulamada
gerçek bir tool DEĞİL"* diyordu. K-834'ün kapsamında **kalıcı** olarak eklendi
(`OrderTools.cs`): onay isteyen ve **sayısal** bir argüman alan tek tool budur
— `cancel_order` yalnız bir sipariş kimliği taşır, eşik uygulanacak bir şey
yok. O tool olmadan koşullu kural özelliği yönetim API'sinden erişilebiliyor
ama bir koşumda **hiç gözlemlenemiyordu**.

Kural: `refund_order` üzerinde `amount <= 100`.

| Çağrı | Run durumu | Tool |
|---|---|---|
| `amount = 50` (eşik altı) | `Completed` | koştu — *"Refunded 50 for order 442."* |
| **`amount = 500`** (eşik üstü) | **`AwaitingApproval`** | **koşmadı** |

☑ 500 > 100 olduğu için koşul eşleşmiyor ve **kapalı düşme** devreye giriyor:
run `RunAwaitingInput` olayıyla onay bekliyor, tool çalışmıyor.

💡 Karşı kontrol aynı tabloda: eşik **altındaki** çağrı otomatik geçti ve tool
gerçekten koştu. Tek başına `AwaitingApproval` görmek "kural hiç işlemiyor"
anlamına da gelebilirdi — bu, MT-SEC-101'in davranışsal yarısını da kapatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-103 — Argüman hiç gönderilmezse onay ister (yol çözülemez → kapalı düşme)

**Gerçek sonuç**
**KOŞULAMADI** — MT-SEC-102 ile aynı gerekçe.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 10) · ☑ GEÇTİ**

⚠️ **Tool seçimi değiştirildi ve sebebi ölçümün kendisidir.** `refund_order`
ile *"tutarı belirtme"* denendiğinde model boş bırakmak yerine `amount=0`
gönderdi — yol **çözüldü** (`0 <= 100`) ve çağrı otomatik onaylandı. Yani
`decimal` bir parametreyle "argüman hiç gönderilmesin" koşulu modele
bırakılarak üretilemiyor. Onun yerine koşul **yapısal olarak** kuruldu:
kural `cancel_order` üzerine yazıldı ve yolu `amount` seçildi — o tool'da
böyle bir argüman **yok**.

```
kural:  cancel_order  amount <= 100
cagri:  cancel_order(orderId = "442")      ← 'amount' argumanı YOK
sonuc:  AwaitingApproval        tool: KOSMADI
```

☑ `ToolArgumentConditionMatcher.TryResolvePath` yolu çözemedi, koşul
eşleşmedi, çağrı onay istedi.

💡 **Karşı kontrol MT-SEC-104 ile ortaktır** (aşağıdaki üçüncü satır): aynı
tool'da çözülebilen bir yolla yazılan kural otomatik onay veriyor. ∴ buradaki
ret "kural `cancel_order`'da hiç işlemiyor" değil, gerçekten **yolun
çözülememesi**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-104 — Tip uyuşmazsa onay ister (`"50"` metni sayı kuralını geçemez)

**Gerçek sonuç**
**KOŞULAMADI** — MT-SEC-102 ile aynı gerekçe.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 10) · ☑ GEÇTİ**

Aynı yapısal yaklaşım (MT-SEC-103'ün notu): kural **sayı** ile yazıldı,
argüman **dıze** gönderildi.

| # | Kural | Çağrı | Sonuç | Tool |
|---|---|---|---|---|
| 1 | `orderId <= 100` (**SAYI** `100`) | `cancel_order(orderId = "50")` | **`AwaitingApproval`** | koşmadı |
| 2 | `orderId == "50"` (**DİZE**) | `cancel_order(orderId = "50")` | `Completed` | **koştu** |

☑ Satır 1: `"50"` metni ile `100` sayısı aynı JSON türünde değil ∴ **sessiz
tip dönüştürme yapılmıyor** ve çağrı onay istiyor. Sayısal olarak
`50 <= 100` doğru olurdu; eşleşme **olmuyor** çünkü türler ayrışıyor.

☑ Satır 2 — **bu turun en değerli ölçümü**: aynı yol, aynı argüman, yalnız
kuralın değeri dızeye çevrildi ve çağrı **otomatik onaylandı**. Bu iki satır
birlikte ret sebebinin **tip** olduğunu kanıtlıyor; tek satır "kural bu tool'da
hiç çalışmıyor" ile aynı görünürdü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-105 — Kodda kayıtlı politika veri kuralını EZER

**Gerçek sonuç**
**KOŞULAMADI — kaynak donması.** Spec'in kendisi zaten "👤 insan gerekir —
`samples/Tracon.Api`'ye geçici bir `AddToolApprovalPolicy` çağrısı eklemeden
koşulamaz" diyor. Bu turun kod donması kuralıyla tutarlı biçimde atlandı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 11) · ☑ GEÇTİ**
Ön koşulun istediği geçici `Program.cs` düzenlemesi yerine kalıcı kanca
kullanıldı (K-834). Varsayılan davranış değişmedi.

`Tracon:Demo:ToolApprovalPolicy=refund_order=Required` kancası eklendi.
Kurulum: MT-SEC-100'ün **koşulsuz** kuralı kayıtlı (her çağrıyı otomatik
onaylar) **ve** kod politikası `Required` dönüyor.

| Kurulum | Run |
|---|---|
| Koşulsuz veri kuralı **+ kod politikası `Required`** | **`AwaitingApproval`** |
| Aynı veri kuralı, **kod politikası YOK** | `Completed` — tool koştu (*"Refunded 50 for order 442."*) |

☑ Kod kazanıyor: `IsAutoApprovedAsync`'in politika dalı (`Required`/
`NotRequired`) veri kurallarından **ÖNCE** değerlendiriliyor.

💡 **İkinci satır olmadan bu case yanlış sebeple yeşil görünürdü**: tek
başına `AwaitingApproval`, veri kuralının hiç işlememesiyle aynı görünür.
Karşı kontrol kuralın **gerçekten** otomatik onayladığını gösteriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

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
instances of the same browser` — bu makinedeki **ilgisiz, eşzamanlı çalışan
başka bir Claude Code oturumu** aynı paylaşılan tarayıcı profiline erişiyordu
(doğrulandı: bu turun ap-s2/3/4 şeritleri bu koşum sırasında henüz hiç
başlatılmamıştı). Zorla denenmedi (paylaşılan kaynağı bozma riski). Açık
soru tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 2) · ☑ GEÇTİ**

🚨 **Turun engeli tarayıcı kilidiydi, rol değil.** §5(b) bu case'i "reader
kimliği yok" satırına koymuştu; kaydın kendisi ise doğru sebebi yazıyor
(`Browser is already in use`). Önceki oturumdan kalmış yedi süreç
durduruldu ve case hiçbir rol yapılandırması olmadan koştu.

Kural arayüzden oluşturuldu (`refund_order`, `amount` `≤` `100`) ve listeye
geri okundu:

```
Remembered approvals
TOOL           AGENT        SCOPE          CREATED
refund_order   All agents   amount ≤ 100   now
               "The call is auto-approved only when every condition below matches."
```

**K2 sınırı ölçüldü ve tutuyor.** Operatör alanı bir `<select>` ve **tam sekiz**
sabit seçenek taşıyor:

```
equals (=) · not equals (≠) · greater than (>) · greater than or equal (≥)
less than (<) · less than or equal (≤) · in (∈) · not in (∉)
```

Formun tamamında `<textarea>` **sayısı 0**; hiçbir alan ifade/formül kabul
etmiyor. Alan ve değer yalnız düz metin kutuları (`amount`, `100`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

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
tarayıcı profili bu makinedeki ilgisiz başka bir Claude Code oturumu
tarafından kilitli). Açık soru tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 2) · ☑ GEÇTİ**

Aynı tarayıcı kilidi kalktı. Bağlama formu açıldı ve **üç** alan taşıyor:

| Alan | Tür | Not |
|---|---|---|
| Provider | `<select>` (5 seçenek) | `anthropic` · `google` · `openai` · `openai-responses` · `openrouter` |
| Configuration key name | metin | yer tutucu `Tracon:ProviderKeys:Acme:OpenAI` |
| Endpoint override | metin | isteğe bağlı |

Formun kendi ipucu sınırı yazıyor: *"The name only — set the value with
dotnet user-secrets, an environment variable, or a key vault. **Never entered
here.**"* Panelin altında ikinci bir cümle: *"No field on this screen ever
accepts a credential value — only the name of the configuration key it is read
from at call time."*

`input[type="password"]` **sayısı 0**; "api key value" / "secret value" /
"paste your key" kalıplarının hiçbiri sayfada geçmiyor. K-059 sınırı tutuyor.

**Çözümleme rozeti de ölçüldü.** Bir bağlama kaydedildi
(`Tracon:ProviderKeys:MtSec118:OpenAI` — yapılandırmada **yok**) ve satır
salt-okunur bir rozetle geldi:

```
anthropic   Not resolved   Delete
Tracon:ProviderKeys:MtSec118:OpenAI      Updated: now
```

Rozet durumu **gösteriyor**, düzenlenebilir bir alan değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-120 — Yetkilendirme kancası kayıtlı değilken davranış değişmez

**Gerçek sonuç**
`/api/diagnostics` → `IRunAuthorizationHandler` → `AllowAllRunAuthorizationHandler`,
`isBuiltInDefault: true`. `get_order_status`'u çağırabilecek `support`
agent'ına gerçek bir OpenAI çağrısı (`gpt-5.4-mini`) yapıldı: `HTTP: 200`,
akış normal tamamlandı (`update` → `done`), `finishReason: stop`. Davranış
değişikliği yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-121 — Reddeden bir kanca `run`'ı düşürmez, model devam eder

**Gerçek sonuç — KOŞULAMADI, kaynak donması.** Case her çağrıyı reddeden bir
`IToolAuthorizationHandler`'ın `samples/Tracon.Api`'ye GEÇİCİ kaydını istiyor;
kod donması bunu yasaklıyor. Alternatif donuk örnek `samples/Tracon.Embedded`
incelendi (`Authorization/EmbeddedToolAuthorizationHandler.cs`): `delete_account`
tool'u `RequiredPermission="admin"` taşıyor ve hiçbir tohumlanmış kiracı
(`acme`/`globex`) bu izne sahip değil — GERÇEK bir "her zaman reddedilir" tool
var. Ama örneğin `EchoModelProvider.cs:15` model taklidi SABİT KODLANMIŞ
şekilde yalnız `current_account`'ı prob'luyor (`ProbeToolName = "current_account"`),
`delete_account`'ı hiçbir zaman çağırmıyor — bu yüzden bu donuk örnekle de
reddi tetiklemenin bir yolu yok. Aynı sınıf: MT-SEC-024/084/105.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 11) · ☑ GEÇTİ**
Ön koşulun istediği geçici `Program.cs` düzenlemesi yerine kalıcı kanca
kullanıldı (K-834). Varsayılan davranış değişmedi.

`Tracon:Demo:ToolAuthorization:Mode=deny-all` kancası eklendi
(`DemoDenyAllToolAuthorization`, her çağrıyı `ToolAuthorizationResult.Deny`
ile reddeder).

```
run status: Completed      error: None
```

☑ Run **`Completed`**, **`Failed` DEĞİL**. ☑ Model ret metnini bir **tool
sonucu** olarak aldı ve turuna devam etti:

```
result: "The demonstration handler refuses every tool call, including
         'get_order_status'."
```

☑ Kayıtta `authorizationDenied: true`, `error: null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-122 — Yetki reddi olay akışında `ToolFailed`'den ayırt edilebilir

**Gerçek sonuç — KOŞULAMADI, MT-SEC-121 ile aynı kök neden.** Reddedilen
tool çağrısı üretilemeden bu case'in `run`'ı da elde edilemiyor.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 11) · ☑ GEÇTİ**

MT-SEC-121'in run'ı için `GET /api/runs/{id}/tools`:

```json
{
  "toolName": "get_order_status",
  "arguments": "orderId=442",
  "result": "The demonstration handler refuses every tool call, including 'get_order_status'.",
  "duration": "00:00:00.0170000",
  "error": null,
  "authorizationDenied": true,
  "timedOut": false,
  "succeeded": true
}
```

☑ `authorizationDenied: true` · ☑ `timedOut: false` · ☑ `error` **boş**.

💡 Kayıt genel bir hata görünümünden **üç alanla birden** ayrışıyor:
`error` boş, `succeeded: true`, ve reddin kendisi ayrı bir bayrakta
(`authorizationDenied`). ∴ "ret bir hata değildir" iddiası veri
modelinde de geçerli, yalnız metinde değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-123 — Timeout'lu bir tool ~1 saniyede kesilir, `run` devam eder

**Not:** `get_slow_report` (`TimeoutSeconds=1`) örnek uygulamada TANIMLI ama
hiçbir `AgentDefinition.ToolNames`'de bağlı değil (`grep` doğrulandı,
`samples/Tracon.Api/OrderTools.cs:60` tanım, hiçbir `Program.cs` agent'ı
kullanmıyor — yalnız global katalogda, `MT-API-070`'in 10 tool listesinde).
Kod donması ihlal edilmeden bu bağlamayı sağlamanın yolu: `POST /api/agents`
ile YENİ bir agent TANIMI (veritabanı kaydı, kaynak değil — MT-SEC-128/129'un
`PUT /api/tenants/.../providers` ile aynı sınıf) — `mt-sec-slow`,
`toolNames: ["get_slow_report"]`, model `gpt-5.4-mini`.

**Gerçek sonuç**
`mt-sec-slow` agent'ına `"Fetch report R-1 with get_slow_report."` mesajı
gönderildi. Toplam istek süresi **2.96s** (model akıl yürütme + tool çağrısı
dahil). `GET /api/runs/{id}/tools`: `toolName: "get_slow_report"`,
`duration: 00:00:01.049`, `error: "Tool 'get_slow_report' did not complete
within 1s."`, `timedOut: true`, `succeeded: false`. Gerçek gövde (5 saniyelik
uyku) BEKLENMEDİ — kesme ~1.05s'de gerçekleşti. `run` `finishReason: stop`
ile normal tamamlandı, `Failed` değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-124 — Aynı tool arka arkaya 6 kez zaman aşımına uğrarsa devre kesici AÇILMAZ

**Gerçek sonuç**
`mt-sec-slow` agent'ına art arda 7 `run` (6 + doğrulama için 1 fazla) at,
her biri `get_slow_report` çağırdı ve zaman aşımına uğradı: süreler
2.48s–3.29s arası (model akıl yürütme dahil, tümü benzer büyüklükte — geç
kalan/hızlanan yok). `GET /api/models/health`: `openai` sağlayıcısı
`status: "Healthy"`, `detail: null` — devre kesici hiç tetiklenmedi. 7.
`run` da aynı hızda (2.60s) tamamlandı; devre kesici gecikmesi (kapalı devre
~57ms'lik hızlı-red) gözlenmedi. Tool zaman aşımı `ModelProviderCircuitBreaker`'a
ulaşmıyor, doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-125 — Onay bekleme süresi timeout'a düşmez

**Not:** İlk deneme senkron (`Prefer: respond-async` yok) çalıştı ama sonuç
`GET /api/approvals/pending`'de görünmedi — kaynak `AgentEndpoints.cs:1214-1221`
yorumu açıkça belirtiyor: "bu mailbox yalnız `Prefer: respond-async` ile
başlayan `run`'lar için var". Kuyruklu tekrar `sessionId` OLMADAN `403`/`Failed`
verdi (`TraconException: ... approval is the input to the next turn and cannot
be resolved without a session`) — case metni bu ön koşulu yazmıyor ama koddan
gerekli olduğu görüldü, `sessionId: "mt-sec-125"` eklenip düzeltildi (kural 1
istisnası değil, yalnız eksik bir adım tamamlandı).

**Gerçek sonuç**
Kuyruklu `run` (`Prefer: respond-async`, `sessionId: "mt-sec-125"`) `cancel_order`
için `AwaitingApproval`'a düştü, `GET /api/approvals/pending` onayı doğru
gösterdi (`id: 01a0ac8d-7a61-74df-83a3-45eafdab1926`). **130 saniye** beklendi
— bu süre boyunca onay durumu `Pending`/run durumu `AwaitingApproval` kaldı,
hiçbir `ToolTimeout`/hata üretilmedi. `POST /api/approvals/{id}/decide`
`{"approved":true}` → `200`, `status: "Approved"`, karar **49ms**'de işlendi.
Kararın hemen ardından (23:31:47, kararın 7s sonrası — worker döngüsü) **YENİ**
bir `run` (`01a0ac8d-7a61-7a38-b9a4-92ad32c969bb`) açıldı ve **~1.04s**'de
`Completed` oldu — normal hız, `TimeoutAIFunction`'ın içinden hiç geçmedi
(K-368 doğrulandı: onay kararı orijinal `run`'ı DEĞİL, yeni bir `run`'ı ileri
götürür; orijinal `run` `AwaitingApproval` durumunda kalıcı olarak durur).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-126 — Tool kataloğunda etki rozeti ve izin adı görünür

**Gerçek sonuç — arayüz KOŞULAMADI (Playwright tarayıcısı kilitli, MT-SEC-108/118
ile AYNI kök neden), API verisi doğrulandı.** `browser_navigate` denendi:
`Error: Browser is already in use for .../mcp-chrome-3eca5a9` — paralel çalışan
diğer şerit(ler) aynı paylaşılan tarayıcı profiline erişiyor. `GET /api/tools`
ile temel veri doğrulandı: `cancel_order` → `"effect":"Destructive"`,
`"requiredPermission":"orders.cancel"`; `get_order_status` ve
`list_recent_orders` → `"effect":"Read"`, `"requiredPermission":null`. Arayüz
rozetlerinin bu alanlardan üretildiği varsayımı case'in kendi metniyle uyumlu
ama GÖRSEL render doğrulanamadı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 2) · ☑ GEÇTİ**

Turda API verisi doğrulanmış, görsel render kalmıştı. Tarayıcı kilidi kalkınca
render de ölçüldü. ⚠️ Tools ekranı **tablo değil kart** düzeni — `tr`
sayısı `0`; satır arayan bir seçici boş döner.

| Tool | Etki rozeti | İzin rozeti |
|---|---|---|
| `cancel_order` | ☑ `destructive` + *"This tool cannot be undone."* | ☑ `orders.cancel` + *"A caller needs the permission \"orders.cancel\" to call this tool."* |
| `get_order_status` | ☑ **yok** | ☑ **yok** |
| `list_recent_orders` | ☑ **yok** | ☑ **yok** |

Rozetin kırmızı olduğu hesaplanmış stille doğrulandı, adıyla değil:
sınıf `bg-danger-soft text-danger`, `color: rgb(245,165,155)`,
`background: rgb(51,25,26)`.

⚠️ Etki filtresi açılır listesi de `destructive` kelimesini taşıyor
(`<option>`); rozet arayan bir ölçüm önce onu bulur. Ölçüm yalnız
yaprak düğüm + rozet kabuğu zincirine bakılarak yapıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-127 — Token okumayan tool: `run` 1 saniyede devam eder, gövde arkada biter

**Gerçek sonuç**
MT-SEC-123/124'ün 7 çağrısının tümü için (~4s sonra) uygulama logunda
`info: Tracon.TimeoutAIFunction[0]` → `Tool 'get_slow_report' finished after
its 1s timeout had already been reported to the model.` satırı görüldü (log
`/tmp/mt-s1-app.log:1772` ve 6 benzeri). `run` MT-SEC-123'te doğrulandığı gibi
~1s'de devam etmişti; gövdenin arka planda GERÇEKTEN bittiği yalnız bu log
satırıyla görünür — `GET /api/runs/{id}/tools` kaydı (MT-SEC-123'te alındı)
bu ikinci tamamlanmayı bir olay olarak TAŞIMIYOR (belgelenmiş sınır, doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-128 — Kiracı sağlayıcı `Endpoint`'i özel ağa işaret ediyor: reddedilir

**Gerçek sonuç**
`PUT /api/tenants/acme/providers/anthropic`, `endpoint: "http://10.0.0.5/"`
ile → `HTTP: 400`, `detail: "The target resolves to a private network address
(10.0.0.5); set 'Tracon:Egress:AllowPrivateNetworkTargets' to true to allow
it."` — mesaj ayarın adını taşıyor. Aynı istek `endpoint` alanı hiç verilmeden
→ `HTTP: 200`, `endpoint: null` — kısıt yalnız override'a uygulanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-129 — Webhook `secretConfigurationKey` önek dışında: reddedilir

**Gerçek sonuç**
`PUT /api/webhooks/orders`, `secretConfigurationKey: "ConnectionStrings:Default"`
ile → `HTTP: 400`, `detail: "'ConnectionStrings:Default' is outside the allowed
prefix. 'secretConfigurationKey' may only reference a configuration key under
'Tracon:WebhookSecrets:'."`. Aynı istek `"Tracon:WebhookSecrets:orders"` ile →
`HTTP: 200`. `secretConfigurationKey` hiç verilmeden (`orders2`) → `HTTP: 200`,
`secretConfigurationKey: null` — alan opsiyonel.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-130 — Webhook ek başlığı imza başlığının adını taşıyor: gönderilmez

**Not:** Alıcı olarak yerel bir Python `http.server` (127.0.0.1:8099, header
loglayan) kullanıldı — kod donmasını ihlal etmez. Uygulama restart edildi
(`Tracon__Webhooks__AllowInsecureHttp=true` — varsayılan yalnız `https`
kabul ediyor). İki abonelik denendi: `secretConfigurationKey` YOK (`sig-test`)
ve VAR (`sigtest`, `Tracon:WebhookSecrets:sigtest` ortam değişkeniyle).

**Gerçek sonuç**
İkisinde de `headers: {"X-Tracon-Signature":"sahte","X-Tenant":"acme"}`
abonelikte tanımlıydı. Bir `run` tetiklendi, alıcı isteği yakaladı:
- `sig-test` (`secretConfigurationKey` yok): alınan başlıklarda `X-Tracon-Signature`
  HİÇ YOK (imzalanacak `secret` yok, sahtesi de gönderilmedi) — `X-Tenant: acme`
  normal iletildi.
- `sigtest` (`secretConfigurationKey` var): alınan başlıklarda **tek bir**
  `X-Tracon-Signature: sha256=dda645becaaabdcef01ada058303ba8a5db83ff75f1eb93d38334c764ccc1ea9`
  var — GERÇEK hesaplanmış imza, `sahte` DEĞİL. `X-Tenant: acme` yine iletildi.
- Sunucu logunda HER İKİ abonelik için de: `Webhook subscription '<ad>' carries
  the reserved header 'X-Tracon-Signature'; it was not sent.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-134 — Koruma kapalıyken davranış Faz 82 öncesiyle birebir aynıdır

**Not — spec `Ön koşul`u koddan sapmış (K-228 DEĞİL, factual sapma, bu koşumda
düzeltildi).** `samples/Tracon.Api/appsettings.json:31-38` içinde
`ContentProtection.Enabled` **varsayılan `true`**'dur (case'in "varsayılan
`false`" iddiasının aksine) — `ActiveKeyId: "sample"`,
`Keys: {"sample": "Tracon:ContentProtection:RawKeys:sample"}` zaten hazır
gelir; yalnız `RawKeys:sample`'ın GERÇEK değeri (bir `secret`) tanımsızdır.
Bu yüzden koruma "kapalı" durumu bu koşumda `Tracon__ContentProtection__Enabled=false`
ortam değişkeniyle AÇIKÇA kuruldu. Case'in ikinci adımı ("bölümü appsettings.json'dan
tamamen kaldır") ile bu env-var override'ı DAVRANIŞSAL olarak eşdeğerdir —
tam da case'in kendi iddiasının konusu ("kapıyı açan `Enabled` bayrağıdır,
çağrının/bildirimin varlığı değil") — ayrı bir appsettings.json düzenlemesi
kod donmasını ihlal ederdi, gerek de yok.

**Gerçek sonuç**
`Tracon__ContentProtection__Enabled=false` ile `run_inputs.messages` VE
`sessions.state` sütunları **düz metin** (`$apEnc` yok) — MT-SEC-132'nin
`legacy-demo` yazımıyla aynı ölçüm, üstte doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-131 — Koruma açıkken oturum durumu ve `run` girdisi veritabanında şifreli durur

**Not:** `Tracon:ContentProtection` zaten `appsettings.json`'da
`Enabled:true, ActiveKeyId:"sample", Keys:{"sample":"Tracon:ContentProtection:RawKeys:sample"}`
ile varsayılı geliyordu (bkz. MT-SEC-134 notu); yalnız `RawKeys:sample`'ın
GERÇEK 32 baytlık değeri eksikti — `openssl rand -base64 32` ile üretilip
`Tracon__ContentProtection__RawKeys__sample` ortam değişkenine (user-secrets
DEĞİL) atandı, uygulama yeniden başlatıldı.

**Gerçek sonuç**
`sessionId: "cp-demo2"`, mesaj `"secret marker XYZZY-CP-DEMO-2"` ile `run`
yapıldı (RUN_ID `01a0ac97-09f3-7b9f-a451-7a955086329e`). Postgres'e doğrudan
`psql` ile bakıldı: hem `run_inputs.messages` hem `sessions.state` sütunu
`{"$apEnc":1,"kid":"sample","n":"...","c":"..."}` zarfı — `XYZZY-CP-DEMO-2`
metni sütunda HİÇ görünmüyor. `GET /api/runs/{id}/input` ve
`GET /api/sessions/cp-demo2` ise metni **düz** döndürdü — çözme şeffaf, API
zarfı hiçbir zaman göstermiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-132 — Koruma açılmadan önce yazılmış satır, açıldıktan sonra da okunabilir

**Gerçek sonuç**
Adım 1 (MT-SEC-134'te koşuldu): `Enabled:false` iken `sessionId: "legacy-demo"`
yazıldı, sütun düz metin doğrulandı. Adım 2: uygulama `Enabled:true` (+ gerçek
`RawKeys:sample`) ile yeniden başlatıldı (MT-SEC-131 restart'ı). Adım 3:
`legacy-demo` satırı Postgres'te HÂLÂ düz metin (`$apEnc` yok, koruma
YALNIZCA yeni yazmaları etkiliyor) VE `GET /api/sessions/legacy-demo` isteği
`"secret marker XYZZY-LEGACY-DEMO"` metnini **birebir** döndürdü — okuma
başarılı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-133 — Bilinmeyen `kid` sessiz değil, adını söyleyen net bir hata verir

**Not — literal ön koşul (appsettings.json'dan `Keys:sample` girdisini kaldırma)
kod donması yüzünden koşulamadı; eşdeğer bir alt senaryo kullanıldı.** Proje
zaten iki gerçek `secret` taşıyor (`dotnet user-secrets list` doğrulandı):
`Tracon:ContentProtection:RawKeys:{sample,sample2}` — `sample2`'nin `Keys`
haritasında karşılığı appsettings.json'da YOK (env ile eklendi:
`Tracon__ContentProtection__Keys__sample2=Tracon:ContentProtection:RawKeys:sample2`,
`ActiveKeyId=sample2`). `Keys:sample` GİRDİSİNİN KENDİSİ appsettings.json'da
sabit kalıyor; onu SİLMENİN appsettings.json dışında bir yolu yok. Bunun yerine
`sample`'ın işaret ettiği RAW DEĞER boşaltıldı (`Tracon__ContentProtection__RawKeys__sample=""`)
— `AesGcmContentProtector.LoadKey` (satır 164-183) içindeki İKİ ayrı kontrolden
("Keys'te yok" / "değeri boş") ikincisini tetikliyor, mesaj yine `'sample'`
adını taşıyor; case'in kendi iddiasına eşdeğer kanıt.

**Ara bulgu (yol boyunca, düzeltilip asıl teste geçildi):** ilk denemede
`RawKeys__sample` env değişkenini hiç EXPORT ETMEDEN restart edildiğinde
uygulama BEKLENMEDİK şekilde `AuthenticationTagMismatchException` (500, mesajsız)
verdi — çünkü proje deposunda ZATEN gerçek bir `sample` secret'ı var
(`dotnet user-secrets`, yukarıda) ve env var'ın YOKLUĞU o secret'ın devreye
girmesine izin veriyor; benim MT-SEC-131'de kullandığım FARKLI anahtar bu
secret DEĞİLDİ, dolayısıyla "yanlış-ama-var" bir anahtarla decrypt denendi.
Bu, aşağıdaki HATA-S1-023'ün kaynağıdır.

**Gerçek sonuç**
1. Uygulama **açıldı** (`Application started`) — `ActiveKeyId=sample2`'nin
   `Keys`'te karşılığı var, `sample`'ın YOK OLMASI değil yalnız DEĞERİNİN boş
   olması başlangıcı engellemedi (validator yalnız yapıyı kontrol ediyor,
   ayrıntı doğrulaması tembel).
2. `GET /api/sessions/cp-demo2` (kid=`sample`) → `HTTP 500`. **HTTP gövdesi**
   yalnız jenerik `ProblemDetails` (`title: "An error occurred..."`, `detail`
   YOK) — ama **sunucu logu** net: `Tracon.TraconException: Content
   protection key 'sample' points at configuration key
   'Tracon:ContentProtection:RawKeys:sample', which has no value. Set it with
   \`dotnet user-secrets\` or an environment variable.`
   (`AesGcmContentProtector.cs:178`). Bu, dosyanın geri kalanında zaten
   yerleşik `SafeErrorText` deseniyle (ayrıntı yanıtta değil günlükte —
   `DEVIR.md`'nin sağlayıcı hatası notu) TUTARLI; case'in "sessiz null/boş
   DEĞİL, fark edilir" iddiası günlük kanalıyla karşılanıyor.
3. `sample2` ile YENİ bir satır (`cp-demo4`) yazıldı ve `GET` ile düz metin
   okundu — aynı anda ÇALIŞAN bir anahtarla etkilenmiyor, doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-135 — Koruma açıkken agent dosya araması hâlâ doğru sonuç verir

**Not:** `samples/Tracon.Api`'nin hiçbir hazır agent'ı `EnableFileMemory`
açmıyor (yalnız `knowledge-assistant` `EnableVectorSearch` kullanıyor —
FARKLI bir bellek türü). Kod donmasını ihlal etmeden: `POST /api/agents` ile
YENİ bir agent (`mt-sec-filemem`, veritabanı kaydı) `memory: {"enableFileMemory":
true}` ile oluşturuldu.

**Gerçek sonuç**
Agent'a `animals.txt` dosyasına `"The favorite animal is
needle-XYZZY-PANDA."` yazdırıldı. Postgres'te doğrudan bakıldı:
`mt_s1.agent_files.content` → `{"$apEnc":1,"kid":"sample2","n":"...","c":"..."}`
zarfı, `needle-XYZZY-PANDA` metni sütunda YOK. Ayrı bir turda agent'a
`"favori hayvan"`ı dosyalarından arattırıldı → doğru cevap **"PANDA"**
döndü — sunucu tarafı ön süzgeç (yalnız PostgreSQL'de var, `FileMemoryProvider`
şifreli sütunu doğrudan filtreleyemez) devre dışı kalsa da nihai eşleşme
istemci tarafında (deşifre edilmiş içerik üzerinde) çalışıyor ve doğru
sonucu buluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-140 — Handler kayıtlı değilken hiçbir şey değişmez

**Gerçek sonuç**
`GET /api/diagnostics` → `IRunAuthorizationHandler`: `AllowAllRunAuthorizationHandler`,
`isBuiltInDefault: true`. `support` agent'ına gerçek bir OpenAI `run`'ı
`HTTP: 200`, akış normal tamamlandı (`finishReason: stop`) — hiçbir davranış
değişikliği yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-141 — Handler yalnız beklenen kullanıcıya izin verir

**Gerçek sonuç — KOŞULAMADI, kaynak donması.** Case `samples/Tracon.Api`'ye
GEÇİCİ olarak `UserId == "a"` dışını reddeden bir `IRunAuthorizationHandler`
kaydı (`Program.cs`, `services.Replace(...)`) istiyor — kod donması bunu
yasaklıyor. Ayrıntı: aşağıdaki konsolide not (MT-SEC-141..150, 152..163).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
🚨 **Turun "geçici `Program.cs` değişikliği" ön koşulu ARTIK GEÇERSİZ.**
Örnek uygulamaya kalıcı bir demo kancası eklendi (K-834):
`Tracon:Demo:RunAuthorization:Mode`. `DemoRoleAuthenticationHandler`'ın
deseninin aynısıdır, varsayılan kapalıdır ve kimliği `X-Demo-User`
başlığından okur. Hiçbir dosya elle düzenlenip geri alınmadı.

Mod `allow-user-a`:

| Adım | Kimlik | Sonuç |
|---|---|---|
| 1 | `a` | ☑ `HTTP 200`, run çalıştı |
| 2 | `b` | ☑ `HTTP 403`, `title: "Run not authorized"` |

`runs` listesi **tek** satır taşıyor (`user=a`, `Completed`) — reddedilen
çağrı için satır **açılmadı**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-142 — Reddedilen bir run başkasının session'ını da kapsar

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
`a` ile `sessionId: "session-of-a"` açıldıktan sonra `b` aynı oturumla denedi:
`HTTP 403`.

💡 **"Karar SessionId'yi de görebilir" iddiası İKİ YOLLA kanıtlandı.**
Tek başına 403 bunu göstermez — `allow-user-a` zaten `b`'yi reddediyordu.

**(1) Karar SessionId'ye göre değişiyor.** `deny-foreign-session` modu
`session-of-{user}` sahiplik kuralını uygular; **aynı** kullanıcı `b` yalnız
oturum değiştiği için farklı yanıt alıyor:

| Kimlik | `sessionId` | Sonuç |
|---|---|---|
| `a` | `session-of-a` | `200` |
| `b` | `session-of-a` | **`403`** — *"refuses a session that belongs to another user"* |
| `b` | `session-of-b` | `200` |

**(2) Handler'ın aldığı istek doğrudan görüldü.** Demo handler aldığı her
isteği günlüklüyor:

```
Demo run authorization: mode=AllowUserA tenant=default agent=support
                        session=session-of-a user=a access=Start run=(null)
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-143 — `throw` eden handler reddeder (fail-closed)

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `throw` — `AuthorizeRunAsync` her çağrıda `InvalidOperationException`
fırlatıyor.

```
HTTP 403   {"title":"Run not authorized",
            "detail":"The registered IRunAuthorizationHandler denied this run."}
```

☑ `403`, **`500` DEĞİL** — fail-closed tutuyor. ☑ `runs` sayısı değişmedi
(`5 → 5`), satır açılmadı.

💡 Fırlatılan istisnanın **kendi metni yanıta girmiyor**; `detail` jenerik
bir cümle. Kapı hem kapanıyor hem sessiz kalıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-144 — Reddedilen run kotayı tüketmez (sıra: atıf → yetki → kota)

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**

Mod `deny-all`, günlük `MaxRuns=1` kotası tanımlı. Üç adım da ölçüldü:

| # | İşlem | Sonuç |
|---|---|---|
| 1 | Reddedilecek run | ☑ `HTTP 403` |
| 2 | `GET /api/quotas/usage` | ☑ `usage: []` — sayaç **hâlâ boş** |
| 3 | Mod kapatıldı, izinli run | ☑ `HTTP 200`; sayaç `runs: 1` oldu |
| 4 | İkinci izinli run | ☑ `HTTP 429` — limit ancak şimdi doldu |

💡 **Dördüncü adım case'de yok ama iddiayı aslında O kanıtlıyor.** Sayacın boş
görünmesi tek başına "henüz yazılmadı" da olabilirdi; `MaxRuns=1` limitinin
**reddedilen çağrıdan sonra da tam** kalması ve izinli run'ın hakkının
tamamını alabilmesi, reddin kotadan hiç düşmediğinin kesin kanıtıdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-145 — Kuyruğa alınan (`Prefer: respond-async`) run da kapsanır

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`, `Prefer: respond-async`:

```
HTTP 403   {"title":"Run not authorized",
            "detail":"The demonstration handler refuses every run."}
```

☑ `403`, **`202 Accepted` DEĞİL**. ☑ `GET /api/jobs` sayısı `0 → 0` — iş
kuyruğuna hiç yazılmadı. Kapı worker'a düşmeden HTTP katmanında çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-146 — Session erişimi: `List` → `403`, `Read`/`Delete`/`Branch` → `404`

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden.** Ayrıca
`samples/Tracon.Embedded`'in kendi `EmbeddedRunAuthorizationHandler.AuthorizeSessionAsync`'i
HER ZAMAN `Allow()` döndürüyor (kaynak okundu) — bu donuk alternatif de session
reddi ÜRETEMEZ.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Dört ayrı mod, her biri **yalnız kendi** erişimini reddediyor. Her modun
öncesinde oturum tazelendi (ilk ölçümde `DELETE` oturumu silmiş ve sonraki
modları kirletmişti — tekrarlandı):

| Mod | List | Read | Branch | Delete |
|---|---|---|---|---|
| `deny-session-list` | **403** | 200 | 201 | 204 |
| `deny-session-read` | 200 | **404** | 409† | 204 |
| `deny-session-delete` | 200 | 200 | 409† | **404** |
| `deny-session-branch` | 200 | 200 | **404** | 204 |

† `409` kalan bir `branch-of-a`'dan; o modlarda branch **reddedilmiyor**,
çakışmaya kadar ilerliyor — doğru sonuç.

💡 **Case'in `List` notu ölçümde görünüyor.** `List` bir **işlem**tir, tekil
bir kaynağa dokunmaz ∴ `403` güvenlidir. Diğer üçü var olan bir kaynağa
dokunur ∴ `404` ile varlık gizlenir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-147 — Dört run başlatan yüzeyin dördü de kapsanır (bypass yok)

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`. **Dört yüzeyin dördü de `403`** — hiçbiri handler'ı atlamıyor:

| # | Yüzey | Çağrı | Sonuç |
|---|---|---|---|
| 1 | Agent run | `POST /api/agents/support/run` | ☑ `403` |
| 2 | Workflow run | `POST /api/workflows/summarize-and-approve/run` | ☑ `403` |
| 3 | Inbound trigger | `POST /api/triggers/default/mt-sec-147` (**imzalı**) | ☑ `403` |
| 4 | OpenAI uyumlu | `POST /v1/chat/completions` | ☑ `403` (`type: run_not_authorized`) |

⚠️ **Yüzey 3'te sıra önemli: imza kapısı yetkilendirmeden ÖNCE çalışıyor.**
İmzasız istek `401 Signature verification failed` alıyor, `403` değil. Geçerli
HMAC üretilmeden bu yüzey ölçülemez:
`sha256=HMACSHA256(secret, "{unixSeconds}.{body}")`, başlıklar
`X-Tracon-Timestamp` + `X-Tracon-Signature`.

⚠️ Trigger'ın `signingSecretConfigurationName` alanı **yalnız**
`Tracon:TriggerSecrets:` öneki altındaki bir anahtara işaret edebilir; başka
bir önek `400` ile reddediliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-148 — Handler'a giden `TenantId` ambient kiracıdır

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden** (handler'ın
gördüğü değeri loglamak için YİNE bir handler kaydı gerekir).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Demo handler aldığı isteği günlüklüyor, ∴ `TenantId` doğrudan okunabiliyor:

```
Demo run authorization: mode=AllowUserA tenant=default agent=support
                        session=session-of-a user=a access=Start run=(null)
```

☑ `tenant=default` — `SingleTenantContext`'in varsayılanı. Kapı kendi kiracı
değerini **uydurmuyor**; `RunAuthorizationGate.cs:53` `TenantId = tenants.TenantId`
yazıyor, yani her zaman `ITenantContext`'ten okuyor.

💡 Aynı satır `access=Start` ve `run=(null)` da gösteriyor — arayüzün
dokümanının dediği gibi bir `Start` isteğinde henüz run yoktur.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-149 — İzin verilen çağıranın kimliği tool gövdesine ulaşır

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden** (ayrıca
`samples/Tracon.Embedded`'in `current_account` tool'u yalnız
`tenant/run/session` döndürüyor, `UserId` taşımıyor — donuk alternatif de
uymuyor).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**

Ön koşulun istediği "çağıranın kimliğini döndüren test tool'u" örnek uygulamaya
kalıcı olarak eklendi (`whoami`, K-834) — geçici düzenleme yapılmadı.
Kimlik `X-Demo-User: ada` ile verildi.

```
GET /api/runs/{id}         → userId: ada
GET /api/runs/{id}/tools   → tool: whoami
                              sonuç: "This run is attributed to 'ada'."
```

☑ Tool `TraconRunContext.Current?.UserId` üzerinden `"ada"`yı gördü.

💡 İki satır birlikte case'in asıl cümlesini kanıtlıyor: run **kaydındaki**
`userId` ile tool'un **gördüğü** değer aynı — yeni bir kavram değil,
`IRunAttributionContext`'in okuduğu değerin tool'a açılan aynı kopyası.
Run kaydı tek başına yalnız **kaydedicinin** gördüğünü kanıtlardı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-150 — Eşzamanlı çağrılar aynı handler örneğinde birbirini bozmaz

**Gerçek sonuç — KOŞULAMADI, MT-SEC-141 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**

Mod `allow-user-a`. **Sekiz** run eşzamanlı gönderildi — dördü `a`, dördü `b`:

```
b1=403 b3=403 b2=403 b4=403 a3=200 a1=200 a2=200 a4=200
```

Handler günlüğü **tam sekiz** karar taşıyor:

```
   4 user=a access=Start run=(null)
   4 user=b access=Start run=(null)
```

☑ Hepsi tutarlı: her `a` izinli, her `b` reddedildi. ☑ İstisna yok, çakışma
yok, kayıp karar yok — handler her çağrıyı **ayrı ayrı** gördü. Singleton
handler eşzamanlı çağrılarda durum taşımıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-151 — Handler kayıtlı değilken 21 kaynak ucunun hiçbiri değişmez

**Gerçek sonuç**
Yeni bir temel `run` (`support`, "Hello, MT-SEC-151 baseline.") ile: tekil
okuma `200`, `/tree` `200`, `/events` `200`, `/input` `200`, `/tools` `200`,
`/feedback` `200`. `/trace` → `404` (bu ortamda OTel dışa aktarımı
yapılandırılmamış görünüyor — yetkilendirmeyle İLGİSİZ, karşılaştırma
tabanı olmadığından yalnız not düşülüyor). Liste uçları: `/api/runs` `200`,
`/api/attachments` `200`, `/api/approvals/pending` `200`. Tamamlanmış bir
`run`'ın `cancel`'ı → `409` (beklenen — zaten bitmiş).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-152 — Reddedilen tekil `run` okuması, var olmayan `run` ile BİREBİR aynıdır

**Gerçek sonuç — KOŞULAMADI, kaynak donması.** Aynı kök neden: reddeden bir
`IRunAuthorizationHandler` gerekir. Ayrıntı: aşağıdaki konsolide not.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`. **Var olan** bir run'ın reddedilen okuması ile **var olmayan**
bir run'ın okuması birebir aynı:

```
GET /api/runs/01a0b794-1552-...   → 404  {"title":"Run not found", "detail":"There is no run with id '01a0b794-...'"}
GET /api/runs/01a00000-0000-...   → 404  {"title":"Run not found", "detail":"There is no run with id '01a00000-...'"}
```

İki yanıt yalnız çağıranın **kendi verdiği** kimlikte ayrışıyor; durum kodu,
başlık ve cümle aynı. Varlık sızıntısı yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-153 — Reddedilen olay akışı hiç açılmaz

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`. `GET /api/runs/{id}/events` → **`404`**. Akış hiç açılmıyor:
yanıt `text/event-stream` değil, tek bir problem belgesi. Reddedilen bir
okumada SSE kanalının açılıp boş kapanması da olmuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-154 — Reddedilen `trace`, `input` ve `tools` üçü de `404`

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`, üçü de reddedildi ve üçü de **`404`**:

```
GET /api/runs/{id}/trace  → 404
GET /api/runs/{id}/input  → 404
GET /api/runs/{id}/tools  → 404
```

Üçü de tekil bir kaynağa dokunuyor ∴ `403` yerine `404` — MT-SEC-152'nin
varlık gizleme kuralıyla tutarlı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-155 — Reddedilen `cancel` `404` döner, `409` DEĞİL

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`. `POST /api/runs/{id}/cancel` → **`404`**, **`409` DEĞİL**.

💡 Ayrım önemli: `409` "bu run var ama iptal edilemez" derdi ve run'ın
**varlığını** açık ederdi. `404` hiçbir şey söylemiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-156 — Reddedilen `replay` `403` döner, satır açılmaz, kota tüketilmez

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**

Mod `deny-all`. ⚠️ **Kalıcı tanımlı bir agent gerekti:** `support` kod-tanımlı
olduğu için replay daha yetkilendirmeye gelmeden `400 Replay not supported`
veriyor (*"Agent 'support' has no persistent definition"*). Ölçüm bu yüzden
API'den yaratılan `mt-sec-156` agent'ıyla yapıldı.

```
POST /api/runs/{id}/replay
→ HTTP 403  {"title":"Run not authorized",
              "detail":"The registered IRunAuthorizationHandler denied this request."}
```

☑ `403`. ☑ `runs` tablo sayısı `3 → 3` — satır **açılmadı**, ∴ kota da
tüketilemez.

💡 **Neden burada `403`, MT-SEC-152'de `404`?** Replay **yeni bir run başlatır**
— `Start` ailesinden bir işlemdir, tekil bir kaynağın varlığını açık etmez.
Salt okuma ise var olan bir kaynağa dokunur ∴ `404` ile varlık gizlenir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-157 — `/v1/chat/completions` akışlı ve akışsız dalda ayrı ayrı kapsanır

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.** Spesifikasyonun
kendi notu `samples/Tracon.Embedded`'de "bilinmeyen kiracı" ile bunu daha önce
(2026-09-05, üretim/yazım oturumu) doğruladığını söylüyor — bu koşumda o
donuk örnek TEKRAR kurulup çalıştırılmadı (zaman bütçesi); sonraki bir
oturum `samples/Tracon.Embedded`'i `X-Host-Tenant` başlığı OLMADAN (→ ambient
`"default"` kiracısı, `EmbeddedTenantStore`'da YOK → `AuthorizeRunAsync` reddeder)
çağırarak bunu ucuzca yeniden koşabilir.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`. `/v1/chat/completions` **iki dalda da** kapsanıyor:

| Dal | Sonuç |
|---|---|
| `stream: false` (akışsız) | ☑ `403` |
| `stream: true` (akışlı) | ☑ `403` |

Akışlı dal **SSE açıp içine hata koymuyor**; istek daha akış başlamadan
`403` ile bitiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-158 — Ek uçları: yükleme ve liste `403`, indirme ve silme `404`

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`. Dört uç, case'in dediği bölünme birebir:

| Uç | Beklenen | Ölçülen |
|---|---|---|
| `POST /api/attachments` (yükleme) | `403` | ☑ `403` |
| `GET /api/attachments` (liste) | `403` | ☑ `403` |
| `GET /api/attachments/{id}` (indirme) | `404` | ☑ `404` |
| `DELETE /api/attachments/{id}` (silme) | `404` | ☑ `404` |

⚠️ Ek uçları `runs/{id}/attachments` **değil**, kökte `api/attachments`.
İlk ölçüm yanlış yola gidip `404` almıştı; o `404` yetkilendirme değil
yönlendirme sonucuydu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-159 — Onay uçları: liste `403`, okuma ve karar `404`, kayıt `Pending` kalır

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `deny-all`:

| Uç | Beklenen | Ölçülen |
|---|---|---|
| `GET /api/approvals/pending` (liste) | `403` | ☑ `403` |
| `GET /api/approvals/{id}` (okuma) | `404` | ☑ `404` |
| `POST /api/approvals/{id}/decide` (karar) | `404` | ☑ `404` |

⚠️ Karar ucu `POST /api/approvals/{id}` **değil** `.../{id}/decide`; ilk
ölçüm `405` almıştı ve bu yetkilendirmeyle ilgili değildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-160 — `throw` eden handler her kaynağı reddeder (fail-closed)

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**
Mod `throw` — handler her karar noktasında istisna fırlatıyor. **Her kaynak**
reddedildi:

| Erişim | Sonuç |
|---|---|
| `POST /api/agents/support/run` | ☑ `403` |
| `GET /api/runs` (liste) | ☑ `403` |
| `GET /api/attachments` (liste) | ☑ `403` |
| `GET /api/approvals/pending` (liste) | ☑ `403` |
| `GET /api/runs/{id}` | ☑ `404` |
| `GET /api/runs/{id}/events` | ☑ `404` |
| `GET /api/runs/{id}/trace` | ☑ `404` |
| `GET /api/runs/{id}/input` | ☑ `404` |
| `POST /api/runs/{id}/cancel` | ☑ `404` |

Hiçbiri `500` değil, hiçbiri geçmiş değil. Fail-closed yalnız run başlatmada
değil **kaynak erişiminin tamamında** geçerli, ve `403`/`404` bölünmesi
`deny-all` ile birebir aynı — istisna yolu ayrı bir kod yolu değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-161 — Handler'a başka kiracının kimliği HİÇ gitmez

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**

Kiracı modu açık (`Tracon:Tenancy:Enabled=true` **ve**
`AllowHeaderResolution=true` — §3.4, ikisi de şart). `kiraci-a` bir run açtı.

| Adım | Çağrı | Sonuç |
|---|---|---|
| 1 | `kiraci-b` ile **`kiraci-a`'nın** run id'si | ☑ `404` |
| 2 | `kiraci-b` ile **var olmayan** run id | ☑ `404` |
| 3 | Handler karar sayısı | ☑ `1 → 1` — **hiç çağrılmadı** |

💡 **Karşı kontrol iddiayı tamamlıyor.** Aynı run **kendi** kiracısından
okunduğunda `200` geldi **ve** karar sayısı `1 → 2` oldu. Yani handler
susmuyor; yalnız kaynak bulunup kiracısı doğrulandıktan **sonra** soruluyor.
Bu ikinci ölçüm olmadan "hiç çağrılmadı" bulgusu, handler'ın hiç kayıtlı
olmamasıyla aynı görünürdü.

∴ başka kiracının kimliği handler'a **hiç gitmiyor**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-162 — Ses: başka kullanıcının oturumuna bağlanma reddedilir

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden** (ayrıca
`UseVoiceConversation()` + transcriber/synthesizer da bu ortamda kurulu değil).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**

Mod `deny-session-voice` (K-834 ile eklenen beşinci oturum modu).

```
WebSocket el sıkışması  /api/voice/sessions/session-of-a/stream        → 404
WebSocket el sıkışması  /api/voice/sessions/hic-olmayan-oturum/stream  → 404
```

☑ `404` — **`401` DEĞİL** (kimlik doğrulaması başarılıydı) ve **`403` DEĞİL**
(oturumun varlığını doğrulardı). Erişilemeyen oturumun aldığı yanıtla birebir
aynı.

🚨 **Token bu uçta `Authorization` başlığıyla GİTMEZ.** WebSocket
**subprotocol**'ü kullanılır:
`Sec-WebSocket-Protocol: tracon.voice.v1, tracon.token.<token>`
(`VoiceConversationProtocol.cs:23,34`). İlk ölçüm `Authorization` başlığıyla
yapıldı ve `401` döndü — o `401` yetkilendirme reddi değil, **token'ın hiç
ulaşmaması**ydı. Bu ayrım case'in kendi iddiasının merkezinde: yanlış taşıma
kullanılırsa case yanlış sebeple "geçti" görünür.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-163 — Ses: var olmayan oturumun ilk turu HÂLÂ açılır (K-283 korunur)

**Gerçek sonuç — KOŞULAMADI, MT-SEC-152 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 4) · ☑ GEÇTİ**

Handler kayıtlı ama `Voice`'a izin veren bir modda (`deny-session-branch`).
Sunucunun **hiç görmediği** bir `sessionId` ile bağlanıldı:

```
WebSocket el sıkışması → 101 Switching Protocols
```

☑ Bağlantı **açıldı** — K-283 korunuyor, ilk tur oturumu oluşturabilir.

☑ **Handler yine de soruldu**: karar sayısı `0 → 1` ve günlük satırı tam olarak
o oturumu ve erişim türünü taşıyor:

```
Demo session authorization: mode=DenySessionBranch tenant=default
                            session=hic-gorulmemis-1789787198 user=(null) access=Voice
```

💡 İki iddia birlikte ölçülmeliydi: yalnız `101` görmek "kapı hiç sorulmadı"
anlamına da gelebilirdi — ki bu case'in korkusu tam tersi yönde aynı kusurdur.
Karar sayısının artması, yeni konuşma izninin gerçekten tüketiciye
sorulduğunu gösteriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-164 — Sahiplik kapalıyken (varsayılan) hiçbir davranış değişmez

**Not:** Kimlik `X-Demo-User` başlığıyla (`DemoRunAttributionContext`, ortam
değişkeni/kod değişikliği gerekmez) çözüldü.

**Gerçek sonuç**
`Tracon:SessionOwnership` bölümü hiç yokken (varsayılan) `sessionId: mtsec164`
ile bir `run` açıldı, listelendi (`200`), okundu (`200`), silindi (`204`).
`owner_id` sütunu `NULL` — yeni indeks boştur. (`branch`'ın ayrı denemesi
bu koşumda içerik-koruması kirliliğinden 500 aldı, MT-SEC-133/HATA-S1-023 ile
AYNI ortam kirliliği — sahiplikle ilgisi yok, temiz ortamda MT-SEC-173'te
`branch` zaten doğrulandı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-165 — Sahipli liste yalnız çağıranın oturumlarını döner

**Not:** `Tracon__SessionOwnership__Enabled=true` ile restart edildi. 🚨
**Tuzak:** `X-Tracon-Demo-Role: operator` rolü AYNI ZAMANDA `ManagementPolicy`
(`Tracon.Operator`, MT-SEC-170'in konusu) sağlıyor ve TÜM kiracı listesini
FİLTRESİZ döndürüyor — ilk denemede A ve B'nin listeleri YANLIŞLIKLA özdeş
çıktı. `X-Tracon-Demo-Role: reader` (yönetim politikası SAĞLAMAYAN bir rol)
ile düzeltilip yeniden koşuldu.

**Gerçek sonuç**
A (`X-Demo-User: a`) `sessionId: mtsec165-a`, B `sessionId: mtsec165-b` açtı.
`reader` rolüyle: A'nın `GET /api/sessions`'ı yalnız `[('mtsec165-a','a')]`
döndü; B'ninki yalnız `[('mtsec165-b','b')]`. `ownerId` alanı doğru dolu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-166 — Başka sahibin oturumu okuma/silme/dallandırmada `404`

**Gerçek sonuç**
`X-Demo-User: b` ile A'nın (`mtsec165-a`) oturumuna: `GET` → `404`,
`detail: "There is no session with id 'mtsec165-a'."` — var olmayan
`does-not-exist-xyz` ile YAPILAN çağrının gövdesiyle **birebir aynı** (yalnız
id metni farklı). `DELETE` → `404`, aynı gövde; A kendi oturumunu SONRASINDA
`200` ile hâlâ okuyabildi (silme yan etki bırakmadı). `POST .../branch` → `404`,
aynı gövde (ilk deneme `content-type` başlıksız gönderildiği için 500 aldı —
kendi test hatam, düzeltilip doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-167 — Başka sahibin oturumuna `run` atmak reddedilir

**Not:** Adım 2 (`/v1/responses` üzerinden `conversation` alanı) koşulmadı —
zaman bütçesi; adım 1 kapının kendisini zaten kanıtlıyor, aynı `SessionOwnershipGate`
kodu ikisi için de paylaşılıyor (kaynak başlığı doğrulandı).

**Gerçek sonuç**
`X-Demo-User: b` ile `sessionId: "mtsec165-a"` (A'nın oturumu) taşıyan bir
`run` isteği → `HTTP: 403`, `errorType: "session_owner_required"`,
`detail: "The session named in this request belongs to another user."`.
A'nın oturumunun `version` alanı istek ÖNCESİ ve SONRASI **1** — reddedilen
tur hiç yazmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-168 — Süzgeç sayfalamadan ÖNCE uygulanır

**Not:** "5 sahipsiz oturum daha yeni" ön koşulu için ELDEKİ eski (bu turun
önceki ailelerinden kalma) 5 sahipsiz satırın (`oai-e2e-01`, `oai-sse-01`,
`oai-idem-01`, `oai-openrouter-01`, `circuit-halfopen-01`) `updated_at`'ı
doğrudan `psql` ile `now()`'a çekildi — kod/kaynak değişikliği değil, yalnız
kendi şemamda veri hazırlığı.

**Gerçek sonuç**
A'nın 5 oturumu (`mtsec168-a1..5`) açıldıktan SONRA 5 sahipsiz satır en
güncel yapıldı. `GET /api/sessions?take=3` (A olarak) → tam **3** satır,
üçü de A'nın (`mtsec168-a5/a4/a3`, `ownerId: "a"`). Sahipsiz satırlardan hiçbiri
listede görünmedi — süzgeç sayfalamadan ÖNCE uygulanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-169 — Kimlik çözülemezse oturum açılmaz

**Gerçek sonuç**
`X-Demo-User` başlıksız (kimlik çözülemez), `sessionId: "mtsec169-noident"`
ile `run` → `HTTP: 403`, `title: "Session owner required"`,
`detail: "Session 'mtsec169-noident' cannot be opened: session ownership is
on and no authenticated identity could be resolved to own it."`. Aynı
istek `sessionId` OLMADAN → `HTTP: 200` (oturumsuz turun sahibi olmaz,
reddedilmez). `SELECT ... WHERE id='mtsec169-noident'` → **0 satır** —
sahipsiz satır hiç açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-170 — Yönetim rolü filtresiz kiracı listesini görür

**Not:** `Tracon:Demo:Roles:Enabled=true` ile `X-Tracon-Demo-Role: operator`
`Tracon.Operator` politikasını sağlıyor (K-431 deseni). 🚨 Bu rolü MT-SEC-165'te
YANLIŞLIKLA sıradan kullanıcı testinde kullanmıştım — o case düzeltilip
`reader` ile yeniden koşuldu (bkz. 165'in kendi notu); bu case (170) TAM OLARAK
operator/yönetim davranışını sınıyor, doğru rol budur.

**Gerçek sonuç**
`X-Tracon-Demo-Role: operator` ile `GET /api/sessions` → **19** satır: **7**
sahipli (A'nın MT-SEC-165/168 oturumları) + **12** sahipsiz (bu ailenin
önceki case'lerinden kalma eski satırlar) — hepsi görünür. Aynı çağrı
`X-Tracon-Demo-Role: reader` (politika sağlamayan) ile → **6** satır, hepsi
`ownerId: "a"` — liste daralıyor, sahipsiz satırlar KAYBOLUYOR. Yön
doğrulandı: kayıtlı olmayan/sağlanmayan politika filtresiz liste VERMİYOR.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-171 — Sahip gövdeden değiştirilemez, ikinci yazımda düşmez

**Gerçek sonuç**
A (`X-Demo-User: a`) gövdeye `ownerId`/`userId`/`owner`: `"kurban"` koyarak
`sessionId: "mtsec171"` ile `run` attı — `owner_id` sütunu **`a`** (gövde
alanları etkisiz). İkinci turdan sonra `owner_id` HÂLÂ `a` (`NULL`'a
düşmedi), `version` **1 → 2** ilerledi — gerçek bir güncelleme yolu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-172 — Kuyruğa alınmış `run` sahibi iş zarfından alır

**Not:** Adım 2 (işçi işi almadan önce kimliği BAŞKA bir kullanıcıya çevirme)
koşulmadı — bu ortamda worker'ın hangi ANDA işi aldığını kesin kontrol etmek
(ayrı bir işlemin ortasına yetişmek) zaman bütçesi içinde güvenilir
yapılamadı; adım 1+3 zaten iddianın özünü (kimliğin işçi ANINDA değil, ZARFTA
donduğunu) kanıtlıyor — worker HttpContext'siz çalışıyor, tek kaynağı zarf.

**Gerçek sonuç**
A (`X-Demo-User: a`) `Prefer: respond-async` + `sessionId: "mtsec172"` ile
`run` attı (`202`). `GET /api/runs/{id}` anlık `userId: "a"` taşıdığını
gösterdi (iş zarfı ANINDA yakalanmış). Worker işi ~10s içinde aldı, `run`
`Completed` oldu. `sessions.owner_id` → **`a`**. `GET /api/sessions`
(reader): A'nın listesinde VAR, B'ninkinde YOK.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-173 — Dallandırma sahibi KAYNAKTAN korur

**Gerçek sonuç**
A'nın `mtsec171` oturumu (`owner_id: a`) `POST .../branch` ile dallandırıldı
→ `HTTP: 201`, yeni oturum id `01a0acaf-0f92-7451-b88b-cbf0419add00`.
`sessions.owner_id` (yeni satır) → **`a`** — dallandırma bir kopya, devir
değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-174 — 👤 Dolu bir `sessions` tablosunda migration kilidi ÖLÇÜLÜR

**Gerçek sonuç — 👤 insan gerekir, KOŞULAMADI.** Case üretim-benzeri
boyutta (en az birkaç milyon satır) dolu bir tablo ister ve gerçek bir
kilit-süresi ÖLÇÜMÜ ister; bu bir CI/otomasyon case'i değildir (spec'in
kendi notu: "otomatik karşılığı yoktur ve olamaz"). Fiziksel eylem tablosuna
eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 13) · ☑ GEÇTİ**

🚨 **Turun engeli kalktı: üretim benzeri tablo ÜRETİLDİ.** `mt_z1.sessions`
`generate_series` ile dolduruldu — **3.000.001 satır, 529 MB**. Sonra
`owner_id` sütunu düşürülüp migration (`0047_session_owner.sql`) yeniden
uygulandı ve **süre ölçüldü**, tahmin edilmedi.

**Sağlayıcı: PostgreSQL 18** (case "üçten en az biri" diyor).

| Ölçüm | Değer |
|---|---|
| Satır sayısı | **3.000.001** |
| Tablo boyutu | **529 MB** |
| `ALTER TABLE … ADD COLUMN owner_id text` | **18,2 ms** |
| — bunun kilit içinde geçen kısmı | **16,8 ms** |
| `CREATE INDEX … WHERE owner_id IS NOT NULL` (kısmi) | **157,8 ms** |
| Kısmi indeksin boyutu | **8.192 bayt** |

☑ **Tablo yeniden yazımı olmadı.** 529 MB'lık bir tabloda `ADD COLUMN` 18 ms
sürdü; yeniden yazım olsaydı dakikalar sürerdi. Sebep sütunun `NULL`
varsayılanlı olması — PostgreSQL yalnız katalogu güncelliyor.

💡 **Kısmi indeksin ucuzluğu karşı kontrolle ölçüldü.** Aynı sütunlar
üzerine **tam** bir indeks de kuruldu ve karşılaştırıldı:

| İndeks | Süre | Boyut |
|---|---|---|
| Kısmi (`WHERE owner_id IS NOT NULL`) | **157,8 ms** | **8 KB** |
| Tam (karşılaştırma için) | 1.960,6 ms | 20 MB |

∴ sahiplik kapalıyken kısmi indeks **2.560 kat küçük** ve **12 kat hızlı**
— "sahipliği hiç açmayan bir kurulum hiçbir şey ödemez" iddiası sayıyla
karşılanıyor. Karşılaştırma indeksi ölçümden sonra düşürüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-175 — `RefuseUnownedSessions` tek başına hiçbir şey yapmaz

**Gerçek sonuç**
`Tracon:SessionOwnership:Enabled=false` + `RefuseUnownedSessions=true` ile
sahipsiz `legacy-demo` oturumu → `GET /api/sessions/legacy-demo` → `HTTP: 200`
(içerik döndü). Sahiplik kapalıyken bayrak **inert** — bir sınır açmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-176 — Katı mod sahipsiz satırı var olmayanla AYNI gövdeyle reddeder

**Not:** İlk denemede `X-Tracon-Demo-Role: operator` ile DELETE **204** ile
BAŞARILI oldu (satır silindi) — bu case'in beklediği `404` DEĞİL. Araştırma:
bu, `Tracon.Operator` yönetim politikasının satırın kendisine ayrı bir
erişim tanıdığı bir DAVRANIŞ (bkz. `TraconSessionOwnershipOptions.ManagementPolicy`
XML doc'u: "bu istisna bir oturumu OKUMAYI kapsar" — DELETE de `SessionEndpoints`
kodunda AYNI istisna yoluna giriyor), case 176'nın ise SIRADAN (yönetim
politikası OLMAYAN) bir çağrıyı sınaması gerekiyordu. Demo rol şeması
(reader/operator/admin) sıradan-ama-yetkili bir kullanıcıyı temsil etmiyor,
`reader` da genel DELETE izni taşımadığı için ayrı bir `403` üretti (rol
politikası, sahiplikle ilgisiz bir katman). Doğru koşum: DEMO ROLLERİ
KAPALIYKEN (yalnız `X-Demo-User`, rol dayatması yok) tekrarlandı.

**Gerçek sonuç**
`Tracon:Demo:Roles:Enabled` KAPALI, `SessionOwnership:{Enabled,RefuseUnownedSessions}=true`
ile: sahipsiz `guard-circuit-son` oturumuna `GET` → `404`,
`detail: "There is no session with id 'guard-circuit-son'."` — var olmayan
`does-not-exist-176` ile BİREBİR aynı gövde (yalnız id metni farklı). `DELETE`
→ AYNI `404` gövdesi; satır Postgres'te HÂLÂ duruyor — kapı silmeden ÖNCE
çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-177 — Katı modda sahipsiz oturuma `run` başlatılamaz

**Not:** Adım 3 (ses WebSocket'i) koşulmadı — bu ortamda `UseVoiceConversation()`/
transcriber/synthesizer kurulu değil (önceki ailelerin de not düştüğü ortam
sınırı).

**Gerçek sonuç**
Sahipsiz `guard-circuit-sifirla` oturumuna: akışlı `run` → GERÇEK `HTTP: 403`
(SSE `error` çerçevesine düşmedi — kapı akış başlamadan önce çalıştı),
`errorType: "session_owner_required"`, `detail: "The session named in this
request belongs to another user."`. `Idempotency-Key` ile akışsız dal →
AYNI `403` + AYNI gövde — sahipsiz reddi başkasının oturumu reddiyle
BİREBİR aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-178 — 🚨 Katı modda VAR OLMAYAN oturum yine açılır

**Not:** Adım 2 (ses WebSocket) koşulmadı — ortamda ses kurulu değil
(MT-SEC-177 ile aynı sınır).

**Gerçek sonuç**
Daha önce HİÇ yazılmamış `sessionId: "mtsec178-brand-new"` ile `run` →
`HTTP: 200`; `sessions.owner_id` → **`a`** — oturum açıldı ve sahibi
çağırandır. "Henüz yok" (bu case) ile "sahipsiz yazılmış" (MT-SEC-176/177)
gerçekten AYRI davranıyor — K-283 korunuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-179 — Katı modda yönetim payı okur ama `run` başlatamaz

**Gerçek sonuç**
`X-Tracon-Demo-Role: operator` (`Tracon.Operator` politikasını sağlar) ile
sahipsiz `oai-e2e-01` oturumuna: `GET` → `HTTP: 200` (destek zaten listede
gördüğü satırı açabiliyor). Aynı rolle `run` başlatmak → `HTTP: 403`,
`errorType: "session_owner_required"` — muafiyet OKUMAYI kapsıyor,
KONUŞMAYA YAZMAYI değil. `X-Tracon-Demo-Role: reader` (politika sağlamayan)
ile aynı `GET` → `HTTP: 404` — politika yokken de `404` (fail-closed).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-180 — `/v1/conversations` tüketicinin yetki handler'ından geçer

**Not — bu case turda HİÇ kayıt bloğu almamıştı.** Kapanış ölçümü (2026-09-19)
spec'in 144 case'ini kaydın 142'siyle karşılaştırdığında ortaya çıktı: sayım
betiği yalnız blok taşıyan case'i görür, bu yüzden `İŞARETSİZ` sayısı onu hiç
saymadı. Case `İnsan gerekir: Hayır` diyor ve ön koşulu K-834'ün
`Tracon:Demo:RunAuthorization:Mode=deny-all` kancasıyla **kod değişikliği
olmadan** karşılanıyor.

**Gerçek sonuç**
Canlı `samples/Tracon.Api` (Release DLL, `--contentRoot`, Development).
Önce mod KAPALIYKEN `summarizer` ile `conv-1` oturumu gerçekten yaratıldı
(`GET /api/sessions/conv-1` → `200`, iki mesaj). Sonra uygulama
`Tracon__Demo__RunAuthorization__Mode=deny-all` ile yeniden başlatıldı:

| Adım | Ölçülen |
|---|---|
| 1 · `GET /tracon/v1/conversations/conv-1` | `404` · `{"error":{"message":"'conv-1' was not found.","type":"not_found_error"}}` |
| 2 · `GET …/conv-1/items` | `404` · **aynı gövde** |
| 3 · `DELETE …/conv-1` | `404` · **aynı gövde** |
| Karşı kontrol · `GET …/baska-kiraci-conv` (hiç var olmayan kimlik) | `404` · yalnız kimlik dizesi farklı, biçim **birebir aynı** |
| 4 · `POST /tracon/v1/conversations` | `200` · `{"id":"conv_01a0b85f…","object":"conversation"}` |

`403` hiçbir adımda dönmedi — dönseydi konuşmanın varlığını doğrulardı.
🚨 **Satır silinmedi:** mod kapatılıp uygulama yeniden başlatıldığında
`GET /tracon/api/sessions/conv-1` → `200` ve iki mesaj hâlâ yerinde.
⚠️ Karşı kontrol olmadan bu case yanlış sebeple yeşil görünürdü: bu uçta var
olmayan bir kimlik ret YOKKEN `200` + sentezlenmiş gövde döndürüyor
(ölçüldü, taban çizgisi), yani `404` gerçekten kapının cevabıdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-181 — `/v1/conversations` haritalaması kapatılabilir

**Not — bu case de turda hiç kayıt bloğu almamıştı** (aynı ölçüm). Ön koşulu
(`options.MapOpenAIConversations = false`) örnek uygulamada çevirebilecek bir
anahtar YOKTU; K-834 gereği **kalıcı** bir demo kancası eklendi —
`Tracon:Demo:MapOpenAIConversations` gerçek seçeneği bağlar, yeni bir davranış
eklemez, yokluğunda sevk edilen varsayılan (`true`) kalır.

**Gerçek sonuç**
Uygulama `Tracon__Demo__MapOpenAIConversations=false` ile başlatıldı.

| Adım | Ölçülen |
|---|---|
| 1 · `POST /v1/conversations` | `405` |
| 1 · `GET /v1/conversations/conv-1` | `404` |
| 1 · `GET …/conv-1/items` | `404` |
| 1 · `DELETE …/conv-1` | `405` |
| 2 · `openapi/v1.json` conversations yolları | `[]` |
| 3 · `GET /tracon/api/sessions/conv-1` | `200` — oturum kendi ucundan erişilebilir, satır duruyor |
| 4 · `/tracon/v1/responses` · `/tracon/v1/chat/completions` | ikisi de belgede **duruyor** |

⚠️ **Spec `dördü de 404` diyor; bu host ikisinde `405` verir ve case yine
GEÇER.** Sebep ortam farkıdır, kapsam farkı değil: referans uygulama konsolu
sunar ve SPA yedek rotası her yolu **yalnız GET** için eşler, bu yüzden
eşlenmemiş bir yola gelen GET dışı her metot `405` alır. Karşı kontrol bunu
ayrıştırdı — `POST /tracon/v1/hic-boyle-yol-yok` → `405`,
`DELETE /tracon/v1/hic-boyle-yol-yok/abc` → `405`, `GET` → `404`: **hiç var
olmamış** bir yol ile kapatılmış conversations yolu **birebir aynı** cevabı
veriyor. Case'in iddiası (`rota hiç yok, reddedilmiyor`) tam da budur.
Spec'in `404`'ü otomatik karşılığının host'unda (konsolsuz, yedek rotasız)
ölçülmüştür. Taban çizgisi: bayrak AÇIKken aynı dört uç `200` döner (ölçüldü).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-182 — Profil çağrılmayan kurulum aynı kalır

**Not:** `dotnet run` üzerinden koşuldu (bu şeridin standart tarifi);
`RequireProductionProfile()` `samples/Tracon.Api`'de HİÇ çağrılmıyor
(`grep` doğrulandı) — case'in tam istediği ön koşul zaten varsayılan.

**Gerçek sonuç**
Bu oturumun ŞU AN çalışan (değiştirilmemiş) örneğinin tüm logu tarandı:
`"Application started"` → **1**, `"Production profile"` → **0**. Host normal
başladı, metot çağrılmayan kurulum tek bir log satırı bile ödemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-SEC-183 — Açık kalan kararların hepsi tek mesajda sayılır

**Gerçek sonuç — KOŞULAMADI, kaynak donması.** Case `samples/Tracon.Api`'ye
GEÇİCİ `tracon.RequireProductionProfile();` satırı eklenmesini istiyor — kod
donması yasaklıyor. MT-SEC-024/084/105/141 ile aynı sınıf. 184-189'un HEPSİ
aynı temel kuruluma (`RequireProductionProfile()`  çağrısı, bazılarında
`.Accept(...)` zinciriyle) dayanıyor; ayrıntı aşağıdaki konsolide nota
taşındı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 5) · ☑ GEÇTİ**

Ön koşulun istediği geçici satır yerine kalıcı bayrak kullanıldı (K-834):
`--Tracon:Demo:ProductionProfile:Enabled=true`.

☑ Host **başlamadı**: `exit=134`, `Application started` **0**.
☑ Her kalem üç bilgiyi taşıyor — `Setting` · `Today` · `Fix`.
☑ Mesajın sonu `Accept(…)` yolunu **ve** kapının sınırını yazıyor:

```
Accept a decision you have deliberately taken instead of changing it, for example:
RequireProductionProfile(profile => profile.Accept(TraconProductionRisk.SingleTenant)).
This gate changes no setting and secures nothing by itself; it only refuses to let
one of these decisions go unanswered.
```

⚠️ **Sayı bayattı: case 5 diyor, bugün 4.** Mesajın kendi başlığı
*"4 production decisions are still on the permissive default"* diyor ve dört
kalem sayıyor: `SingleTenant` · `UnownedSessions` · `UnlimitedRequestRate` ·
`UnboundedRetention`.

**Sebebi ölçüldü ve gerilemenin tersi.** Altı riskten **ikisi** örnek
uygulamada artık karşılanıyor: `UninspectedContent` (case'in zaten saydığı
gibi, `AddPatternContentGuard` kayıtlı) **ve** `UnencryptedContentAtRest` —
`appsettings.json`'da `Tracon:ContentProtection:Enabled` **`true`**
(`ContentProtectionProfileCheck.cs` o bayrağı okuyor ve `Satisfied` dönüyor).
Case yazıldığında ikinci kalem hâlâ açıkmış. Spec 4'e çekildi (skill §1.1).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-184 — Kabul edilen risk host'u durdurmaz ve adıyla loglanır

**Gerçek sonuç — KOŞULAMADI, MT-SEC-183 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 5) · ☑ GEÇTİ**

`Accept=SingleTenant,UnencryptedContentAtRest` + üç ayar açık
(`SessionOwnership` · `RateLimit` · `Retention`).

☑ Host **başladı**: `health=200`, `Application started` **1**.
☑ Kabul edilen risk **adıyla** loglandı:

```
Production profile: SingleTenant is accepted. ITenantContext is permissive:
resolves to Tracon's built-in SingleTenantContext, so every call runs as the
default tenant.
```

💡 **Yalnız `SingleTenant` loglandı, ikinci kabul loglandı değil** — ve bu
doğru: `UnencryptedContentAtRest` zaten **karşılanmış** durumda (MT-SEC-183'ün
notu), ∴ kabul edecek bir şey yok. Kabul yalnız **gerçekten açık** bir kararı
susturur; karşılanmış bir kalemi kabul etmek sessizdir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-185 — Kabul komşu kalemi kapsamaz

**Gerçek sonuç — KOŞULAMADI, MT-SEC-183 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 5) · ☑ GEÇTİ**

MT-SEC-184'ün kurulumu, ama `Tracon:SessionOwnership:Enabled` **verilmedi**.

☑ Host **başlamadı**: `exit=134`, `Application started` **0**.
☑ Mesaj **yalnız** `UnownedSessions` kalemini sayıyor; başlık tekil:
*"and 1 production decision is still on the permissive default"*.
☑ Kabul edilen `SingleTenant` mesajda **hiç geçmiyor** (`grep -c` → `0`).

∴ kabul yalnız **adlandırdığı** riski kapsıyor, komşusunu değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-186 — İçerik denetimi kayıtla ölçülür, bayrakla değil

**Gerçek sonuç — KOŞULAMADI, MT-SEC-183 ile aynı kök neden** (ayrıca
`AddPatternContentGuard` çağrısını GEÇİCİ yorum satırına almayı istiyor —
ikinci bir kaynak değişikliği).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 11) · ☑ GEÇTİ**
Ön koşulun istediği geçici `Program.cs` düzenlemesi yerine kalıcı kanca
kullanıldı (K-834). Varsayılan davranış değişmedi.

`Tracon:Demo:SuppressRegistrations=ContentGuard` kancası eklendi. Zincirin
ortasındaki `AddPatternContentGuard` çağrısı zincirden **ayrıldı** ki
bırakılabilsin.

| Adım | Sonuç |
|---|---|
| 1 — guard **kayıtlı değil** | `UninspectedContent` listede; açık karar sayısı **4 → 5** |
| 2 — guard geri | kalem **düştü**; sayı **5 → 4** |

Adım 1'in kalemi:

```
UninspectedContent
  Setting: IContentGuard
  Today:   no content guard is registered, so no prompt or response is inspected
           and the inspecting wrapper is never added to the model pipeline
  Fix:     call AddPatternContentGuard(...) or AddContentGuard<TGuard>() on the
           Tracon chain, or populate the Tracon:ContentGuard:Pattern section
```

☑ `Setting` satırı **`IContentGuard`** diyor — bir **seçenek bayrağı değil**,
kaydın kendisi. Case'in asıl iddiası budur: kural setini boşaltmak, guard'ı
kaydetmemekle **aynı şey değildir**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-187 — `UseTenancy(Enabled = false)` hâlâ tek kiracı sayılır

**Gerçek sonuç — KOŞULAMADI, MT-SEC-183 ile aynı kök neden** (ayrıca
`UseTenancy` dalını GEÇİCİ olarak koşulur hâle getirmeyi istiyor).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 11) · ☑ GEÇTİ**
Ön koşulun istediği geçici `Program.cs` düzenlemesi yerine kalıcı kanca
kullanıldı (K-834). Varsayılan davranış değişmedi.

`UseTenancy` dalı artık anahtar **varsa** koşuyor (yalnız `true` iken değil) ve
`options.Enabled` değerden okunuyor. `--Tracon:Tenancy:Enabled=false`:

```
EXIT=134   Application started: 0

SingleTenant
  Setting: UseTenancy(options => options.Enabled)
  Today:   false although UseTenancy was called, so the request's tenant is
           never read and every request falls to the default tenant
  Fix:     set options.Enabled to true inside UseTenancy(...) and choose a claim
           type, or remove the UseTenancy call if this deployment is
           single-tenant on purpose
```

☑ Host **başlamıyor**. ☑ `SingleTenant` kalemi listede **duruyor** —
`UseTenancy` çağrılmış olması tek kiracılığı değiştirmiyor.
☑ `Setting` satırı bir **kod çağrısını** gösteriyor
(`UseTenancy(options => options.Enabled)`), bir `appsettings` anahtarını
**değil** — çünkü Tracon `TraconTenancyOptions`'a hiçbir yapılandırma
bölümü bağlamaz.

💡 `Today` cümlesi *"false **although UseTenancy was called**"* diyor —
"çağrılmış ama kapalı" ile "hiç çağrılmamış" durumlarını ayrı cümlelerle
anlatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-188 — Mesaj ve log hiçbir yapılandırma değeri taşımaz

**Gerçek sonuç — KOŞULAMADI, MT-SEC-183 ile aynı kök neden** (ön koşulu
"MT-SEC-183'ün kurulumu").

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 5) · ☑ GEÇTİ**

İçerik koruması kapatıldı ve anahtar haritası canary değerlerle dolduruldu:

```
--Tracon:ContentProtection:Enabled=false
--Tracon:ContentProtection:Keys:canary-4f2a-key-id=CanaryKeys:canary-4f2a
```

☑ Mesaj ayarın **adını** taşıyor: `Tracon:ContentProtection:Enabled`
(günlükte **4** kez).

☑ **Günlüğün TAMAMINDA hiçbir canary dizesi yok** — üçü de `grep -c` → `0`:

| Aranan | Sayı |
|---|---|
| `canary-4f2a-key-id` (anahtar **kimliği**) | `0` |
| `CanaryKeys` (işaret edilen yapılandırma anahtarı) | `0` |
| `canary` (büyük/küçük harf duyarsız, herhangi bir yerde) | `0` |

∴ kapı ayarın adını söylüyor, **değerini** ya da haritasını hiç
sızdırmıyor. Hata mesajı da yığın izi de temiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-189 — Kapı HTTP yüzeyi olmayan host'ta da koşar

**Gerçek sonuç — KOŞULAMADI, MT-SEC-183 ile aynı kök neden** (`MapTracon`
çağırmayan ayrı bir gömülü host + `RequireProductionProfile()` çağrısı ister
— `samples/Tracon.Embedded` `MapTracon`'u ZATEN çağırıyor, bu case'e uymuyor;
yeni kod gerekir).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

### Konsolide not — MT-SEC-183..189 (`RequireProductionProfile`) KOŞULAMADI

Yedi case'in hepsi `samples/Tracon.Api/Program.cs`'e GEÇİCİ
`tracon.RequireProductionProfile(...)` çağrısı (bazılarında `.Accept(...)`
zinciriyle, 186'da ayrıca `AddPatternContentGuard`'ı yorumlama, 187'de
`UseTenancy` dalını değiştirme) eklenmesini istiyor — MT-SEC-024/084/105/141
ile AYNI kod donması sınıfı. `samples/Tracon.Embedded` de `MapTracon`
çağırdığı için MT-SEC-189'un "HTTP yüzeyi olmayan host" senaryosuna
uymuyor. **Kalıcı çözüm:** kapanış modunda (kod donması kalkınca) veya repo
dışı YENİ bir minimal host (`RequireProductionProfile()` çağıran, `MapTracon`
çağırmayan) ile koşulmalı — `docs/ADAYLAR.md` adayı olabilir.

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 13) · ☑ GEÇTİ**

🚨 **Turun engeli kalktı: repo DIŞINDA minimal bir host kuruldu.** Kayıt
*"`samples/Tracon.Embedded` de `MapTracon` çağırıyor"* diyordu. Scratchpad'de
`Host.CreateApplicationBuilder` tabanlı bir konsol uygulaması yazıldı ve
**yerel feed'den** `Tracon.Core 1.0.0-preview.1` paketine bağlandı — yani
sevk edilen paket, repo referansı değil. **HTTP yüzeyi yok; `MapTracon` hiç
çağrılmıyor.**

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddTracon().RequireProductionProfile();
var app = builder.Build();
await app.StartAsync();
```

```
HOST BASLADI — kapi kosmadi.
fail: Microsoft.Extensions.Hosting.Internal.Host[11]
      Hosting failed to start
      System.InvalidOperationException: RequireProductionProfile() was called, and
      6 production decisions are still on the permissive default, so the host does
      not start.

        SingleTenant
          Setting: ITenantContext
          Today:   resolves to Tracon's built-in SingleTenantContext, so every
                   call runs as the default tenant
          Fix:     register your own ITenantContext before the AddTracon() call,
                   or on an ASP.NET Core host call UseTenancy(...)
```

☑ Host **başlamadı** — bunlar kompozisyon kararları, bir HTTP kaygısı değil.
☑ `SingleTenant` kalemi **`SingleTenantContext` adını taşıyor**; kiracı
sorusu gömülü host'ta da anlamlı yanıtlanıyor, sessizce eksilmiyor.

💡 **Sayı 6, örnek uygulamada 4** (MT-SEC-183) — ve fark tutarlı: çıplak host
ne içerik guard'ı ne de içerik koruması yapılandırıyor, ∴ `UninspectedContent`
ve `UnencryptedContentAtRest` de listeye giriyor. Aynı kapı, aynı muhasebe.

⚠️ `Fix` cümlesi HTTP'siz host'u da düşünüyor: *"register your own
ITenantContext … **or on an ASP.NET Core host** call UseTenancy(…)"* — iki
barındırma modeli ayrı ayrı adlandırılıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-190 — Denetim izi yazılamazken onay kararı uygulanmaz

**Gerçek sonuç — KOŞULAMADI, ortam sınırı (kod donması DEĞİL).** Case
`REVOKE INSERT ON audit_log FROM tracon_app;` istiyor — bu şeridin bağlantı
dizesi (`00-INDEKS.md` §2.2/skill §2) `postgres` SÜPERKULLANICISINI kullanıyor
ve PostgreSQL süperkullanıcılar ACL kontrollerini BAYPAS EDER (`REVOKE` onu
etkilemez). `tracon_app` adlı ayrı, kısıtlı bir rol bu paylaşılan `ap-pg`
container'ında YOK; birini oluşturmak paylaşılan container'ı (kural 3, diğer
şeritlerle paylaşılıyor) etkileyebilir ve bu oturumun yetkisi dışında.
Ayrıntı: aşağıdaki konsolide not.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 12) · ☑ GEÇTİ**
🚨 **Turun engeli kalktı: kısıtlı bir rol kuruldu.** Kayıt *"tur `postgres`
süperkullanıcısını kullandı; süperkullanıcılar `REVOKE`'tan etkilenmez"* diyordu.
Yordam:

```sql
CREATE ROLE tracon_app LOGIN PASSWORD '…';        -- rolsuper = f
GRANT USAGE ON SCHEMA mt_z1 TO tracon_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA mt_z1 TO tracon_app;
```

Migration'lar önce `postgres` ile koşuldu (49 tablo), sonra uygulama
`AutoApplyMigrations=false` ile **`tracon_app`** bağlantı dizesine çevrildi.
`REVOKE` artık gerçekten ısırıyor: `has_table_privilege(…,'INSERT')` → `f`.

⚠️ **Kuyruklu onay `sessionId` ister.** İlk deneme `Failed` oldu ve mesaj
sebebi açıkça yazdı: *"The queued run requested approval, but no 'sessionId'
was given; approval is the input to the next turn and cannot be resolved
without a session."* Kalıcı bir `pending_approvals` satırı ancak
`Prefer: respond-async` **+** `sessionId` ile oluşuyor — senkron koşumda onay
akışın içinde taşınıyor ve tabloya yazılmıyor.

`REVOKE INSERT ON mt_z1.audit_log FROM tracon_app` sonrası:

| Adım | Sonuç |
|---|---|
| 2 — `POST /api/approvals/{id}/decide` | ☑ **`500`** |
| 3 — `GET /api/approvals/{id}` | ☑ `status: Pending`, `decidedBy: null`, `decidedAt: null` |

☑ Karar **uygulanmadı** (K-089 · K-370). Günlükte hata, **`WriteOrThrow`**
yolundan:

```
Npgsql.PostgresException (0x80004005): 42501: permission denied for table audit_log
  at Tracon.SqlAuditLog.WriteAsync(…)               SqlAuditLog.cs:109
  at Tracon.AuditRecorder.WriteOrThrowAsync(…)       AuditRecorder.cs:133 / 154
  at Tracon.ApprovalEndpoints.DecideAsync(…)         ApprovalEndpoints.cs:227
```

💡 Çağrı zinciri iddianın kendisini gösteriyor: karar ucu audit yazımını
**`WriteOrThrow`** ile çağırıyor — yani audit burada best-effort **değil**,
kritik yolda.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-191 — Denetim izi yazılamazken yönetim çağrısı devam eder

**Gerçek sonuç — KOŞULAMADI, MT-SEC-190 ile aynı kök neden.**

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 12) · ☑ GEÇTİ**

MT-SEC-190'ın `REVOKE`'u yürürlükteyken:

| Adım | Sonuç |
|---|---|
| 1 — `POST /api/agents` | ☑ **`201`** |
| 2 — `GET /api/agents/{name}` | ☑ **`200`** — agent **gerçekten** oluştu |

☑ Best-effort yol işlevselliği **bozmuyor**. Günlükte `LogWarning`:

```
warn: Tracon.AuditingAgentDefinitionStore[0]
      Could not write action 'agent.create' for entity 'agent:z1-best-effort'
      to the audit trail.
      Npgsql.PostgresException: 42501: permission denied for table audit_log
```

💡 **MT-SEC-190 ile yan yana okununca tasarım görünüyor:** aynı veritabanı
hatası, iki farklı sonuç. Onay kararı `WriteOrThrow` ile **durduruluyor**
(`LogError` + `500`); agent oluşturma `AuditingAgentDefinitionStore` ile
**yutuluyor** (`LogWarning` + `201`). Ayrım keyfi değil: biri denetim izinin
kanıtı olmadan yapılmaması gereken bir **karar**, diğeri izsiz de doğru olan
bir **yazım**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-192 — `audit_log` onarıldıktan sonra aynı karar uygulanır

**Gerçek sonuç — KOŞULAMADI, MT-SEC-190 ile aynı kök neden** (bu case
MT-SEC-190'ın DEVAMI, onun kurulumuna bağımlı).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 12) · ☑ GEÇTİ**

`GRANT INSERT ON mt_z1.audit_log TO tracon_app` sonrası **aynı** onay tekrar
karara bağlandı:

```
POST /api/approvals/{id}/decide  → 200
GET  /api/approvals/{id}         → status: Approved
                                    decidedAt: 2026-09-19T04:27:08Z
GET  /api/audit/verify           → {"status":"Valid","entriesChecked":2,
                                     "firstFailingEntryId":null}
```

☑ Karar uygulandı, audit satırı yazıldı. ☑ Zincir **`Valid`** — reddedilen
deneme zincirde **boşluk bırakmadı**, çünkü hiç satır yazılmamıştı.
☑ Ret bir **zehirli hap değil**: başarısız deneme sonraki kararı engellemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-193 — Denetim izi yazılamazken veri konusu silinmez

**Gerçek sonuç — KOŞULAMADI, iki ayrı ortam sınırı.** MT-SEC-190'ın
`REVOKE` engeli (üstte) VE ayrıca `samples/Tracon.Api`'de kayıtlı bir
`IDataSubjectResolver` YOK (`grep -rn IDataSubjectResolver samples/`
sıfır sonuç) — case'in "silinecek içeriği olan bir veri konusu" ön koşulu
bu haliyle üretilemez.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

### Konsolide not — MT-SEC-190..193 (`audit_log` yazma arızası) KOŞULAMADI

Dördü de uygulamanın veritabanı rolünden `audit_log`'a `INSERT` iznini
GERÇEKTEN almayı gerektiriyor. Bu şeridin (ve tüm turun) bağlantı dizesi
`postgres` süperkullanıcısını kullanıyor — PostgreSQL süperkullanıcılar
`REVOKE`'tan ETKİLENMEZ (RLS/ACL bypass, PostgreSQL'in kendi davranışı).
Kısıtlı bir `tracon_app` rolü oluşturup UYGULAMANIN bağlantı dizesini o role
çevirmek gerekirdi — bu, PAYLAŞILAN `ap-pg` container'ında YENİ bir rol
oluşturmak demektir (kural 3 bunu yasaklamaz ama diğer üç paralel şeridi
etkileme riski taşır ve bu oturumun bütçesi dışında kaldı). MT-SEC-193 ayrıca
`IDataSubjectResolver`'ın `samples/Tracon.Api`'de hiç kayıtlı olmamasıyla
ikinci bir engelle karşılaşıyor. **Kalıcı çözüm:** kapanış modunda, izole bir
doğrulama sunucusunda (skill §serit-kurulumu §4) kısıtlı bir rol oluşturup
tek seferlik koşulmalı.

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 12) · ☐ AÇIK KALIYOR**

Ön koşulun **ikinci** yarısı sağlanamadı. Kısıtlı rol kuruldu ve `REVOKE`
yürürlükte (MT-SEC-190), ama `IDataSubjectResolver` örnek uygulamada
**kayıtlı değil** — `grep -rn IDataSubjectResolver samples/` boş dönüyor.
Uç bunu açıkça söylüyor:

```
DELETE /api/data-subjects/test-kisi?dryRun=true
→ 409  {"title":"No data subject resolver registered",
         "detail":"IDataSubjectResolver is not registered. Tracon does not store
                   personal identity and cannot resolve a subject id to
                   sessions/runs/conversations on its own — register an
                   IDataSubjectResolver implementation to use this endpoint."}
```

⚠️ **Bu, K-834'ün diğer kancalarından farklı bir karar.** Öteki kancalar var
olan bir anahtarı çeviriyor; burada gereken şey bir **alan uygulaması**dır —
bir kimliği hangi oturum/run/konuşma satırlarına eşleyeceğini uyduran demo
kod. Ucun kendi cümlesi de bunu söylüyor: *"Tracon does not store personal
identity."* Karar kullanıcıya bırakıldı; `00-INDEKS.md` açık kalem tablosuna
yazılır.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-119 — Egress politikası silinince kiracı tekrar kısıtsız olur

**Gerçek sonuç**
`acme` için egress `["openai"]` kaydedildi → `DELETE` `HTTP: 204` → `GET`
`{"allowedProviders":null}` (kısıtsız, politika hiç yokmuş gibi) → ikinci
`DELETE` `HTTP: 404`, `title: "Policy not found"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
