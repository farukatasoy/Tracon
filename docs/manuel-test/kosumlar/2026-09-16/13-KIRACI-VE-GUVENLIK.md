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
| **Depo** | **Bellek içi** (`storage.persistent: false`) — bu turda `mt_s1` PostgreSQL şemasına hiç dokunulmadı (bu ailenin cases'i bunu gerektirmedi) |
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

**Oturum 1 — MT-SEC-001..062 koşuldu (40 case, blok sınırında durdu).**
**39 ☑ Geçti · 0 ☑ Kaldı · 1 ☐ Beklemede (MT-SEC-024, kaynak donması engelliyor).**

**Sıradaki oturumun işi:** MT-SEC-070'ten devam et (blok: 070-071, 080-085,
090-095, 100-109, 110-119 civarı — tam blok sınırları case numaralarından
okunur, bkz. DEVIR.md "bölme noktası onluk case bloğu" kuralı). Uygulama şu an
**durduruldu** (oturum sonu); sıradaki oturum ortamı sıfırdan kurar (skill §3
adım 3-4).

**Ortam notları (sıradaki oturuma devir):**
- Kestrel bu ailenin TAMAMI için `0.0.0.0:5081`'e bağlı tutuldu (loopback +
  LAN aynı anda). `$LANIP=192.168.1.103`.
- Bu oturumda kalıcılık sağlayıcısı hiç açılmadı (hepsi bellek içi) —
  `Tracon:Tenancy:*` bayrakları dışında ekstra yapılandırma gerekmedi.
- `Tracon:Tenancy:Enabled`/`AllowHeaderResolution` bu oturumda üç kez
  aç/kapa edildi (MT-SEC-020 kapalı → MT-SEC-021/023 açık → MT-SEC-022 yalnız
  header kapalı → MT-SEC-030+ tekrar açık). Oturum sonunda uygulama
  DURDURULDU; sıradaki oturum MT-SEC-070/071 için `external:invoke` API
  anahtarı akışını sıfırdan kuracak (kendi ön koşulu var, tenancy'den
  bağımsız).

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

## Açık sorular / fiziksel eylem tablosu

| Case | Neden | Ne gerekiyor |
|---|---|---|
| `MT-SEC-024` | Case metni `samples/Tracon.Api/Program.cs`'e geçici bir satır eklenmesini istiyor (`options.AllowedTenants.Add(...)`); kod donması bunu yasaklıyor | Kullanıcı kararı: (a) kapanış modunda mı koşulsun, (b) repo dışı bir tüketici host'uyla mı (bkz. `DEVIR.md` "donuk `samples/` gerektiren case'ler" tarifi — dosya 05'te kanıtlandı) koşulsun, yoksa (c) kalıcı olarak `⏭` mü sayılsın |

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
