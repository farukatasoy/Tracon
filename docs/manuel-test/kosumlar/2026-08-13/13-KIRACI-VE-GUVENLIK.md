# 13 — Kiracı ve Güvenlik (`SEC`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../13-KIRACI-VE-GUVENLIK.md`](../../13-KIRACI-VE-GUVENLIK.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-SEC-001 — Loopback'ten doğru token ile istek geçer

**Gerçek sonuç**
HTTP: 200.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-002 — Loopback dışından (LAN adresi) istek, `AllowRemoteAccess` kapalı → `403`

**Gerçek sonuç**
HTTP: 403, title: Uzak erisim kapali, detail AllowRemoteAccess ayarini acin... ile devam ediyor. Dogru token olmasina ragmen reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-003 — Authorization başlığı yok, `AuthToken` tanımlı → `401` + `WWW-Authenticate`

**Gerçek sonuç**
HTTP: 401. WWW-Authenticate: Bearer basligi var. Govde title: Kimlik dogrulanamadi, detail Gecerli bir Authorization: Bearer <token> basligi gerekiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-004 — Yanlış bearer token → `401`, gövde token hakkında bilgi vermez

**Gerçek sonuç**
HTTP: 401. Govde MT-SEC-003 ile birebir ayni (title/detail) - yanlis token hakkinda ipucu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-005 — `Bearer` şeması olmayan bir Authorization başlığı → `401`

**Gerçek sonuç**
HTTP: 401 - Basic sema reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-006 — `Bearer ` öneki var ama değer boş → `401`

**Gerçek sonuç**
HTTP: 401.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Meta ve kabuk istisnaları

`app.MapAgentPrism` üç ayrı grup kurar: `/api/meta` (hiçbir filtre),
arayüz kabuğu + MCP OAuth geri dönüşü (loopback + policy VAR, bearer YOK) ve
geri kalan her şey (üç katman da var). Bu bölüm ayrımı somutlaştırır.

---

## MT-SEC-010 — `/api/meta`, loopback dışından VE Authorization başlıksız yine `200` döner

**Gerçek sonuç**
HTTP: 200 - loopback disindan, Authorization basliksiz bile /api/meta erisilebilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-011 — `/api/meta` yanıtı `AuthToken` DEĞERİNİ hiçbir alanda taşımaz

**Gerçek sonuç**
requiresBearerToken, allowRemoteAccess, requiresAuthorizationPolicy alanlarinin ucu var. manuel-test-token-2026 dizgisi govdenin hicbir yerinde gecmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-012 — Arayüz kabuğu bearer token'dan MUAFTIR ama loopback'ten muaf DEĞİLDİR

**Gerçek sonuç**
loopback: 200, lan: 403 - kabuk bearer token'dan muaf ama loopback'ten muaf degil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Kiracılık çözümlemesi (`HttpTenantContext`, Faz 41)

**Bu bölümün ön koşulu.** Aşağıdaki `dotnet user-secrets` komutları uygulanır
(kod değişikliği GEREKMEZ — `Program.cs` bu anahtarları zaten okur):

```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Tenancy:Enabled" "true"
dotnet user-secrets set "AgentPrism:Tenancy:AllowHeaderResolution" "true"
# ClaimType KASITLI olarak verilmez: claim ayarlıysa baslik hic okunmaz.
```
Uygulamayı yeniden başlatın.

---

## MT-SEC-020 — `UseTenancy` hiç çağrılmamışken her istek varsayılan kiracıya düşer

**Gerçek sonuç**
{"tenantId":"default"} - tenancy hic ayarlanmamisken varsayilan kiraciya dusuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-021 — `AllowHeaderResolution` açıkken `X-AgentPrism-Tenant` başlığı kiracıyı belirler

**Gerçek sonuç**
{"tenantId":"kiraci-alfa"}

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-022 — `AllowHeaderResolution` KAPALIYKEN aynı başlık yok sayılır

**Gerçek sonuç**
{"tenantId":"default"} - AllowHeaderResolution kapaliyken X-AgentPrism-Tenant basligi hic okunmadi, zincir varsayilana dustu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-023 — Biçimsiz kiracı kimliği başlıkta gönderilirse sessizce reddedilir (hataya düşmez)

**Gerçek sonuç**
HTTP: 200, tenantId: default - IsValidTenantId gecersiz degeri reddetti, zincir varsayilana dustu, hata verilmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-024 — `AllowedTenants` beyaz listesi doluyken listede olmayan bir değer 403 ile reddedilir

**Gerçek sonuç**
`HTTP: 403`, `title: "Kiraci reddedildi"`, `detail: "Cozulen kiraci izin verilenler listesinde degil. Bu istek varsayilan kiracinin verisine SESSIZCE dusurulmez; reddedilir."` — `{"tenantId":"default"}` DÖNMEDİ. K-393 öncesi kusurun düzeltmesi doğru çalışıyor (düzeltilmiş davranış gözlendi). Geçici `options.AllowedTenants.Add("kiraci-alfa")` satırı `Program.cs`'e eklenip test koşuldu, sonra kaldırılıp yeniden derlendi (`git diff` temiz, iz bırakmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Kiracı izolasyonu (veri sınırı)

**Ön koşul (tüm bölüm)** — §3'ün ön koşulu (Tenancy açık, header çözümü açık)
uygulanmış olmalı; ek olarak `AllowedTenants` boş bırakılır (whitelist yok).

---

## MT-SEC-030 — Kiracı A'da `FIX-AGENT-01` oluşturma

**Gerçek sonuç**
HTTP: 201.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-031 — Kiracı B'de AYNI adla oluşturma çakışmaz (ayrı satır)

**Gerçek sonuç**
HTTP: 201 (409 DEGIL) - iki farkli kiracida ayni ad serbest birakildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-032 — Kiracı A'nın listesi yalnız kendi agent'ını gösterir

**Gerçek sonuç**
kiraci-alfa listesinde manuel-destek var (tek kopya, kendi kiracisinin surumu); kiraci-beta'nin ayri satiri sizmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-033 — Kiracı A bir çalıştırma başlatır (`FIX-PROMPT-02`)

**Gerçek sonuç**
HTTP: 200. Not: Idempotency-Key basligi yokken run ucu varsayilan olarak akisli (SSE) yanit veriyor (sistem geneli tutarli davranis, dosya 07'de de gozlendi) - runId event: run cercevesinden okundu: 019ffb05-7e0d-7ade-a93a-5b240cd21758.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-034 — Kiracı A kendi çalıştırmasını görebilir

**Gerçek sonuç**
HTTP: 200.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-035 — Kiracı B aynı `runId`'yi `404` ile görür (403 DEĞİL)

**Gerçek sonuç**
HTTP: 404 (403 DEGIL) - kiraci-beta'ya calistirmanin var oldugu bilgisi bile sizmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-036 — Kiracı B kendi `manuel-destek` kopyasını siler; Kiracı A'nınki etkilenmez

**Gerçek sonuç**
Adim 1: HTTP 204. Adim 2: HTTP 200 - kiraci-alfa'nin kopyasi hala var, kiraci-beta'nin silmesi yalniz kendi satirini etkiledi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Kiracı kayıt yönetimi (`GovernanceEndpoints.MapTenants`, Faz 9)

Bu bölüm §3/§4'ten BAĞIMSIZDIR: `ITenantStore` kaydı `UseTenancy` açık
olmasa bile çalışır (kayıt zorunlu değildir, yalnız arayüz için bir isim/açıklama
kaynağıdır).

---

## MT-SEC-040 — `PUT /api/tenants/{slug}` yeni bir kiracı kaydı oluşturur

**Gerçek sonuç**
HTTP: 200. Govde slug: kiraci-alfa, displayName: Alfa Musterisi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-041 — Aynı slug'a ikinci `PUT` günceller (upsert)

**Gerçek sonuç**
HTTP: 200, displayName guncellendi (Alfa Musterisi (guncel)), ayni id (019ffb04...) - ikinci satir olusmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-042 — Geçersiz biçimli slug → `400`

**Gerçek sonuç**
HTTP: 400, title: Kiraci anahtari gecersiz, detail beklenen metinle birebir eslesiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-043 — `GET /api/tenants` kayıtlı kiracıları listeler

**Gerçek sonuç**
kiraci-alfa listede var.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-044 — `DELETE /api/tenants/{slug}` yalnız KAYDI siler, kiracının verisi kalır

**Gerçek sonuç**
Adim 1: HTTP 204. Adim 2: HTTP 200 - kiraci kaydi silinmesi calisma anindaki agent cozumlemesini etkilemedi (ITenantStore kaydi yalniz isim/aciklama kaynagi, zorunlu degil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-045 — Var olmayan slug'ı silmeye çalışmak → `404`

**Gerçek sonuç**
HTTP: 404, title: Kiraci bulunamadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — API anahtarları (Faz 53)

Reset yordamı yeniden uygulanır (§3-5'in geçici ayarları temizlenir); bu
bölüm `UseTenancy` AÇIK OLMADAN başlar (MT-SEC-060/061 kendi ön koşulunu
ayrıca belirtir).

---

## MT-SEC-050 — `POST /api/api-keys` yeni anahtar üretir, ham değer `ap_` ile başlar

**Gerçek sonuç**
HTTP: 200. plaintextKey ap_ ile basliyor (ap_default_RPaDJLnwS0...). record.keyPrefix (ap_default_R, 12 karakter) plaintextKey'in ilk 12 karakteriyle ayni. record.name: manuel-okuma, record.scopes: [RunsRead].

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-051 — `GET /api/api-keys` listesi ham değer ve özet TAŞIMAZ

**Gerçek sonuç**
Yanit govdesinde plaintextKey (ap_default_RPaDJ...) hicbir yerde gecmiyor. Alanlar yalniz id, tenantId, name, keyPrefix, scopes, expiresAt, revokedAt, lastUsedAt, createdAt, isActive - keyHash yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-052 — `name` boş → `400`

**Gerçek sonuç**
HTTP: 400, detail: 'name' bos olamaz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-053 — Boş `scopes` dizisi → `400`

**Gerçek sonuç**
HTTP: 400, detail: En az bir kapsam ('scopes') secilmelidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-054 — Bilinmeyen kapsam değeri → `400` (kapalı liste)

**Gerçek sonuç**
**KALDI - HATA-S2-006 (Orta).** Beklenen HTTP 400 yerine HTTP 500 (genel ProblemDetails, 'An error occurred while processing your request.') dondu. Kok neden: CreateAsync (ApiKeyEndpoints.cs:57-64) [FromBody] ApiKeyCreateRequest ile OTOMATIK minimal-API govde baglama kullaniyor; bilinmeyen bir ApiKeyScope dizgisi System.Text.Json'in JsonStringEnumConverter'inda bir JsonException firlatir ve bu istisna handler govdesine HIC ULASMADAN once, framework'un kendi govde-baglama asamasinda olusur. Diger uclar (orn. /api/agents/validate, /v1/chat/completions) govdeyi ELLE JsonSerializer.Deserialize + try/catch (JsonException) ile okuyup temiz 400 'Govde cozumlenemedi:' uretiyor; bu uc ise otomatik baglamaya guveniyor ve app.UseExceptionHandler() (Program.cs:682, ozellestirilmemis) istisnayi genel 500 ProblemDetails'a ceviriyor. Kapsam: [FromBody] kullanan diger 10 dosya da (ApprovalEndpoints, EvalEndpoints, ExperimentEndpoints, RetentionEndpoints, QuotaEndpoints, RunEndpoints, SchedulingEndpoints, SkillScriptGrantEndpoints, WebhookEndpoints, WorkflowEndpoints) potansiyel olarak ayni deseni tasiyabilir - ayrintili dogrulanmadi, yalniz bu case olculdu.

---

**Yeniden koşum (Aile G, 2026-08-14).** DÜZELTİLDİ — **HTTP 400**:
`{"title":"Gecersiz istek govdesi","detail":"The JSON value could not be converted to AgentPrism.ApiKeyScope. Path: $.scopes[0]..."}`.
Kök neden düzeltmesi tek endpoint'e özel bir yama DEĞİL, kütüphane çapında bir
yeniden tasarımdır: `ApiKeyEndpoints.CreateAsync` artık `[FromBody]` otomatik
baglamasi yerine `RequestBodyBinding.ReadAsync<T>` (yeni,
`AgentPrism.AspNetCore/Internal/RequestBodyBinding.cs`) ile govdeyi elle okur —
`AgentEndpoints`'in zaten kullandığı desenle aynı. Bu koşumda tahmin edilen 10
dosyanın TAMAMI (ve tahminin KAÇIRDIĞI, implicit binding kullanan
`GovernanceEndpoints`, `AgentEndpoints.RollbackAsync`, `.../run`,
`SkillEndpoints`, `SessionEndpoints`, `KnowledgeEndpoints` ×2, `VoiceEndpoints`,
`GovernanceEndpoints` tenants/mcp-prompts uçları) aynı desene taşındı — ayrıntı
`KAPANIS-PLANI.md` §6 Aile G. Ayrıca kütüphane çapında bir savunma katmanı
(`JsonBindingProblemMiddleware`) eklendi: elle okumayı unutan gelecekteki bir
uç için, yalnız `Development` ortamında (framework'ün `ThrowOnBadRequest`
bayrağı yalnız orada açık) 500'ü 400'e çevirir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-055 — Üretilen anahtar, kapsamı yeten bir uçta Bearer olarak çalışır

**Gerçek sonuç**
HTTP: 200.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-056 — Aynı anahtar, kapsam DIŞI bir uçta `403 Kapsam yetersiz` alır

**Gerçek sonuç**
HTTP: 403, title: Kapsam yetersiz, detail: Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-057 — `DELETE /api/api-keys/{id}` iptal eder; sonra o anahtarla istek `401` alır

**Gerçek sonuç**
Adim 1: HTTP 204. Adim 2: HTTP 401 - satir silinmedi, revokedAt yazildi, sonraki istek reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-058 — Var olmayan veya zaten iptal edilmiş `id`'yi tekrar iptal etmek → `404`

**Gerçek sonuç**
HTTP: 404 - ikinci iptal 'bulunamadi' gibi goruldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-059 — Süresi geçmiş anahtar `401` alır

**Gerçek sonuç**
6 saniye sonra HTTP: 401 - suresi gecmis anahtar reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-060 — `X-AgentPrism-Tenant` başlığı, anahtarın kiracısıyla ÇELİŞİRSE `403`

**Gerçek sonuç**
HTTP: 403, title: Kiraci uyusmuyor, detail: 'X-AgentPrism-Tenant' basligi API anahtarinin baglandigi kiraciyi EZEMEZ...

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-061 — Başlık HİÇ verilmezse kiracı doğrudan anahtardan çözülür

**Gerçek sonuç**
{"tenantId":"kiraci-alfa"} - baslik verilmeden kiraci dogrudan anahtardan cozuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-062 — `apikey.create`/`apikey.revoke` denetim izine düşer, ham değer YAZILMAZ

**Gerçek sonuç**
Iki kayit dondu: apikey.create ve apikey.revoke. create kaydinin after alani yalniz name, keyPrefix, scopes tasiyor - ham deger veya ozet yok. revoke kaydinin before/after alanlari null.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Uzak erişimin API anahtarıyla koşullanması (Faz 50 × 53, `ExternalSurfaceGuard`)

Örnek uygulama `.UseMcpServer(...)` VE `.UseA2A(...)` çağırır ve
`app.MapAgentPrismMcpServer(); app.MapAgentPrismA2A();` ile bunları açar
(`Program.cs:98-99,717-718`). Bu, `AllowRemoteAccess = true` yapıldığı anda
`ExternalSurfaceGuard.EnsureRemoteAccessNotCombined`'in **açılışta** devreye
girdiği anlamına gelir — sıra önemlidir.

---

## MT-SEC-070 — `external:invoke` anahtarı YOKKEN `AllowRemoteAccess = true` yapılırsa uygulama AÇILMAZ

**Gerçek sonuç**
**Dokuman duzeltmesi uygulandi (yukaridaki not).** Gecici kod degisikligi yerine `AgentPrism__Ui__AllowRemoteAccess=true` ortam degiskeniyle baslatildi ('external:invoke' kapsamli hicbir anahtar yokken). Surec aciliste `Unhandled exception: System.InvalidOperationException: AllowRemoteAccess acikken MCP disa acilamaz: sistemde 'external:invoke' kapsamli, suresi gecmemis ve iptal edilmemis bir API anahtari yok...` ile COKTU (ExternalSurfaceGuard.cs:144). Sync/aciliste calisan denetim dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-071 — Önce `external:invoke` anahtarı üretilir, SONRA `AllowRemoteAccess = true` başarıyla açılır

**Gerçek sonuç**
SQLite'a gecici olarak gecildi (anahtarin yeniden baslatma boyunca hayatta kalmasi icin - bellek ici depoda case dogal olarak test edilemez, anahtar da fixture'lar gibi silinirdi). Adim 1: HTTP 200, ExternalInvoke kapsamli anahtar uretildi. Adim 2: `AgentPrism__Ui__AllowRemoteAccess=true` ile 0.0.0.0'a baglanarak yeniden baslatildi, surec COKMEDI (basariyla acildi). Adim 3: LAN adresinden dogru token ile istek HTTP 200 dondu - MT-SEC-002'nin 403'u burada alinmadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Bu bölümden sonra temizlik.** `options.AllowRemoteAccess = true;` satırı
> Program.cs'ten kaldırılır ve reset yordamı yeniden uygulanır — §8 bu
> varsayımla başlar.

---

# 8 — Rol tabanlı yetkilendirme (`AgentPrismPolicies`, Faz 6)

AgentPrism rol/kullanıcı SAKLAMAZ; `AgentPrismPolicies.Reader/Operator/Admin`
yalnızca policy ADLARIdır ve tüketicinin kendi `AddAuthorization` çağrısında
tanımlanmadıkça (`AgentPrismRolePolicies.Resolve`) hiçbir şey yapmazlar
(`RoleEndpointConventionBuilderExtensions.cs:25-34`). Örnek uygulama hiçbir
rol policy'si veya kimlik doğrulama şeması TANIMLAMAZ (`grep -rn
"AddAuthorization\|AddAuthentication" samples/AgentPrism.Api/Program.cs`
boş döner) — bu yüzden MT-SEC-071'den sonrasını çalıştırmak için GEÇİCİ bir
test kimlik doğrulama şeması eklenir.

**Bu bölümün ön koşulu — geçici kod (test bitince İKİSİ de kaldırılır).**

1. Yeni dosya `samples/AgentPrism.Api/RoleTestAuthHandler.cs`:
   ```csharp
   // GECICI TEST DOSYASI — yalniz MT-SEC-08x rol testleri icindir. Test
   // bitince bu dosyayi silin.
   using System.Security.Claims;
   using System.Text.Encodings.Web;
   using Microsoft.AspNetCore.Authentication;
   using Microsoft.Extensions.Options;

   namespace AgentPrism.Api;

   public sealed class RoleTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
   {
       public const string SchemeName = "RoleTest";

       public RoleTestAuthHandler(
           IOptionsMonitor<AuthenticationSchemeOptions> options,
           ILoggerFactory logger,
           UrlEncoder encoder)
           : base(options, logger, encoder)
       {
       }

       protected override Task<AuthenticateResult> HandleAuthenticateAsync()
       {
           var role = Request.Headers["X-Test-Role"].ToString();

           if (string.IsNullOrEmpty(role))
           {
               return Task.FromResult(AuthenticateResult.NoResult());
           }

           var roleClaims = role switch
           {
               "reader" => new[] { "agentprism-reader" },
               "operator" => new[] { "agentprism-reader", "agentprism-operator" },
               "admin" => new[] { "agentprism-reader", "agentprism-operator", "agentprism-admin" },
               _ => Array.Empty<string>(),
           };

           var claims = roleClaims
               .Select(r => new Claim(ClaimTypes.Role, r))
               .Append(new Claim(ClaimTypes.NameIdentifier, "manuel-test-kullanici"));

           var identity = new ClaimsIdentity(claims, SchemeName);
           var principal = new ClaimsPrincipal(identity);

           return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
       }
   }
   ```

2. `samples/AgentPrism.Api/Program.cs`'in en üstündeki `using` bloğuna
   (satır 61 civarı) şu satırı ekle:
   ```csharp
   using Microsoft.AspNetCore.Authentication;
   ```

3. `var app = builder.Build();` satırının (satır ~680) HEMEN ÜSTÜNE:
   ```csharp
   builder.Services.AddAuthentication(RoleTestAuthHandler.SchemeName)
       .AddScheme<AuthenticationSchemeOptions, RoleTestAuthHandler>(RoleTestAuthHandler.SchemeName, null);

   builder.Services.AddAuthorization(options =>
   {
       options.AddPolicy(AgentPrismPolicies.Reader,
           p => p.RequireRole("agentprism-reader", "agentprism-operator", "agentprism-admin"));
       options.AddPolicy(AgentPrismPolicies.Operator,
           p => p.RequireRole("agentprism-operator", "agentprism-admin"));
       options.AddPolicy(AgentPrismPolicies.Admin,
           p => p.RequireRole("agentprism-admin"));
   });
   ```

4. `var app = builder.Build();` satırının HEMEN ALTINA (`app.UseExceptionHandler();`'dan önce):
   ```csharp
   app.UseAuthentication();
   app.UseAuthorization();
   ```

5. Yeniden başlat: `dotnet run`. Rol seçimi artık her istekte
   `X-Test-Role: reader|operator|admin` başlığıyla yapılır; başlık
   verilmezse istek kimliksiz kalır (üç katmanlı korumadan geçer ama
   `AuthenticateResult.NoResult()` nedeniyle hiçbir rolü karşılamaz).

---

## MT-SEC-080 — Hiçbir rol testi kurulmadan (varsayılan): Admin gerektiren uç bile rol kontrolüne takılmaz

**Gerçek sonuç**
HTTP: 201 - hicbir AgentPrism.* policy'si kayitli olmadigindan RequireRole hicbir sey eklemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-081 — Yalnız `reader` rolüyle Admin ucu `403` alır

**Gerçek sonuç**
HTTP: 403.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-082 — `admin` rolüyle aynı istek `201` alır

**Gerçek sonuç**
HTTP: 201.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-083 — `operator` rolü çalıştırma başlatabilir ama Admin ucuna erişemez

**Gerçek sonuç**
Adim 1: HTTP 200. Adim 2: HTTP 403 (404 DEGIL) - rol denetimi handler'dan once calisti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-084 — `RequireRolePolicies = true` + hiçbir policy kayıtlı değilken uygulama AÇILMAZ

**Gerçek sonuç**
Gecici olarak §8'in AddAuthentication/AddAuthorization + RoleTestAuthHandler.cs kurulumu geri alinip, MapAgentPrism lambda'sina `options.RequireRolePolicies = true;` eklendi, yeniden derlendi. `dotnet run` aciliste `Unhandled exception: System.InvalidOperationException: AgentPrismEndpointOptions.RequireRolePolicies acik ama su policy'ler kayitli degil: AgentPrism.Reader, AgentPrism.Operator, AgentPrism.Admin...` ile COKTU (AgentPrismRolePolicies.cs:82). Uc policy adi da mesajda gecti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-085 — `/api/meta`'nın `roles` alanı: hiçbir policy kayıtlı değilken hepsi `true`

**Gerçek sonuç**
{'canRead': True, 'canOperate': True, 'canAdminister': True} - hicbir policy kayitli degilken hepsi true. RequireRolePolicies satiri kaldirilip yeniden derlendi (git diff temiz), RoleTestAuthHandler.cs silindi, sade ornek uygulamayla dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Denetim izi (`IAuditLog`, Faz 9)

---

## MT-SEC-090 — `agent.create` → `agent.update` → `agent.delete` sırası izlenebilir

**Gerçek sonuç**
Uc kayit dondu, en yeniden eskiye: [agent.delete, agent.update, agent.create]. Her kaydin entity alani agent:manuel-audit.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-091 — `authToken`/`apiKey` gibi sır adlı bir alan varsa değeri `"***"` olur

**Gerçek sonuç**
after JSON'unda "Authorization":"***" gorunuyor - ham deger (cok-gizli-deger) govdenin hicbir yerinde yok. Not: ayni kayitta authorizationConfigurationKey, oauthClientSecretConfigurationKey, oauthAuthorizationMode alanlari da *** olarak redakte edilmis (asiri-redaksiyon, alan adinda key/secret/authorization fragmani geciyor olabilir) - bu sizinti degil tam tersi yonde bir gozlem, case'in kendi iddiasini etkilemiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-092 — Çoğul `tokens` içeren bir alan (örn. `maxOutputTokens`) REDAKTE EDİLMEZ

**Gerçek sonuç**
after.model.maxOutputTokens: 512 - gercek sayisal degeriyle gorunuyor, *** degil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-093 — Kimlik doğrulaması yokken `actor` her zaman `null`'dur

**Gerçek sonuç**
actor: None (null).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-094 — `limit` parametresi dönen kayıt sayısını sınırlar

**Gerçek sonuç**
limit=1 ile 1 kayit dondu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SEC-095 — Denetim izinde silme/düzeltme ucu YOKTUR

**Gerçek sonuç**
HTTP: 405 (Method Not Allowed).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bölüm sonu — temizlik

Bu dosyadaki case'ler bittiğinde:

1. `samples/AgentPrism.Api/RoleTestAuthHandler.cs` (varsa) silinir.
2. `Program.cs`'e eklenen `AddAuthentication`/`AddAuthorization`,
   `UseAuthentication`/`UseAuthorization`, `options.AllowRemoteAccess = true;`
   ve `options.RequireRolePolicies = true;` satırları geri alınır.
3. `git diff samples/AgentPrism.Api/Program.cs` çalıştırılıp değişiklik
   KALMADIĞI doğrulanır.
4. `dotnet user-secrets list` ile `AgentPrism:Tenancy:*` girdileri temizlenir
   (isteğe bağlı — sonraki dosya zaten kendi reset yordamını uygular).

---
