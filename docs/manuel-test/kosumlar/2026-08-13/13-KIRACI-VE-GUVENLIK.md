# 13 — Kiracı ve Güvenlik (`SEC`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../13-KIRACI-VE-GUVENLIK.md`](../../13-KIRACI-VE-GUVENLIK.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/13-KIRACI-VE-GUVENLIK.md
> ```

---

## Temiz geçen case'ler (51)

| Case | Durum | Başlık |
|---|---|---|
| MT-SEC-001 | ☑ | Loopback'ten doğru token ile istek geçer |
| MT-SEC-002 | ☑ | Loopback dışından (LAN adresi) istek, `AllowRemoteAccess` kapalı → `403` |
| MT-SEC-003 | ☑ | Authorization başlığı yok, `AuthToken` tanımlı → `401` + `WWW-Authenticate` |
| MT-SEC-004 | ☑ | Yanlış bearer token → `401`, gövde token hakkında bilgi vermez |
| MT-SEC-005 | ☑ | `Bearer` şeması olmayan bir Authorization başlığı → `401` |
| MT-SEC-006 | ☑ | `Bearer ` öneki var ama değer boş → `401` |
| MT-SEC-010 | ☑ | `/api/meta`, loopback dışından VE Authorization başlıksız yine `200` döner |
| MT-SEC-011 | ☑ | `/api/meta` yanıtı `AuthToken` DEĞERİNİ hiçbir alanda taşımaz |
| MT-SEC-012 | ☑ | Arayüz kabuğu bearer token'dan MUAFTIR ama loopback'ten muaf DEĞİLDİR |
| MT-SEC-020 | ☑ | `UseTenancy` hiç çağrılmamışken her istek varsayılan kiracıya düşer |
| MT-SEC-021 | ☑ | `AllowHeaderResolution` açıkken `X-Tracon-Tenant` başlığı kiracıyı belirler |
| MT-SEC-022 | ☑ | `AllowHeaderResolution` KAPALIYKEN aynı başlık yok sayılır |
| MT-SEC-023 | ☑ | Biçimsiz kiracı kimliği başlıkta gönderilirse sessizce reddedilir (hataya düşmez) |
| MT-SEC-030 | ☑ | Kiracı A'da `FIX-AGENT-01` oluşturma |
| MT-SEC-031 | ☑ | Kiracı B'de AYNI adla oluşturma çakışmaz (ayrı satır) |
| MT-SEC-032 | ☑ | Kiracı A'nın listesi yalnız kendi agent'ını gösterir |
| MT-SEC-033 | ☑ | Kiracı A bir çalıştırma başlatır (`FIX-PROMPT-02`) |
| MT-SEC-034 | ☑ | Kiracı A kendi çalıştırmasını görebilir |
| MT-SEC-035 | ☑ | Kiracı B aynı `runId`'yi `404` ile görür (403 DEĞİL) |
| MT-SEC-036 | ☑ | Kiracı B kendi `manuel-destek` kopyasını siler; Kiracı A'nınki etkilenmez |
| MT-SEC-040 | ☑ | `PUT /api/tenants/{slug}` yeni bir kiracı kaydı oluşturur |
| MT-SEC-041 | ☑ | Aynı slug'a ikinci `PUT` günceller (upsert) |
| MT-SEC-042 | ☑ | Geçersiz biçimli slug → `400` |
| MT-SEC-043 | ☑ | `GET /api/tenants` kayıtlı kiracıları listeler |
| MT-SEC-044 | ☑ | `DELETE /api/tenants/{slug}` yalnız KAYDI siler, kiracının verisi kalır |
| MT-SEC-045 | ☑ | Var olmayan slug'ı silmeye çalışmak → `404` |
| MT-SEC-050 | ☑ | `POST /api/api-keys` yeni anahtar üretir, ham değer `ap_` ile başlar |
| MT-SEC-051 | ☑ | `GET /api/api-keys` listesi ham değer ve özet TAŞIMAZ |
| MT-SEC-052 | ☑ | `name` boş → `400` |
| MT-SEC-053 | ☑ | Boş `scopes` dizisi → `400` |
| MT-SEC-055 | ☑ | Üretilen anahtar, kapsamı yeten bir uçta Bearer olarak çalışır |
| MT-SEC-056 | ☑ | Aynı anahtar, kapsam DIŞI bir uçta `403 Kapsam yetersiz` alır |
| MT-SEC-057 | ☑ | `DELETE /api/api-keys/{id}` iptal eder; sonra o anahtarla istek `401` alır |
| MT-SEC-058 | ☑ | Var olmayan veya zaten iptal edilmiş `id`'yi tekrar iptal etmek → `404` |
| MT-SEC-059 | ☑ | Süresi geçmiş anahtar `401` alır |
| MT-SEC-060 | ☑ | `X-Tracon-Tenant` başlığı, anahtarın kiracısıyla ÇELİŞİRSE `403` |
| MT-SEC-061 | ☑ | Başlık HİÇ verilmezse kiracı doğrudan anahtardan çözülür |
| MT-SEC-062 | ☑ | `apikey.create`/`apikey.revoke` denetim izine düşer, ham değer YAZILMAZ |
| MT-SEC-070 | ☑ | `external:invoke` anahtarı YOKKEN `AllowRemoteAccess = true` yapılırsa uygulama AÇILMAZ |
| MT-SEC-071 | ☑ | Önce `external:invoke` anahtarı üretilir, SONRA `AllowRemoteAccess = true` başarıyla açılır |
| MT-SEC-080 | ☑ | Hiçbir rol testi kurulmadan (varsayılan): Admin gerektiren uç bile rol kontrolüne takılmaz |
| MT-SEC-081 | ☑ | Yalnız `reader` rolüyle Admin ucu `403` alır |
| MT-SEC-082 | ☑ | `admin` rolüyle aynı istek `201` alır |
| MT-SEC-083 | ☑ | `operator` rolü çalıştırma başlatabilir ama Admin ucuna erişemez |
| MT-SEC-084 | ☑ | `RequireRolePolicies = true` + hiçbir policy kayıtlı değilken uygulama AÇILMAZ |
| MT-SEC-085 | ☑ | `/api/meta`'nın `roles` alanı: hiçbir policy kayıtlı değilken hepsi `true` |
| MT-SEC-090 | ☑ | `agent.create` → `agent.update` → `agent.delete` sırası izlenebilir |
| MT-SEC-091 | ☑ | `authToken`/`apiKey` gibi sır adlı bir alan varsa değeri `"***"` olur |
| MT-SEC-092 | ☑ | Çoğul `tokens` içeren bir alan (örn. `maxOutputTokens`) REDAKTE EDİLMEZ |
| MT-SEC-093 | ☑ | Kimlik doğrulaması yokken `actor` her zaman `null`'dur |
| MT-SEC-094 | ☑ | `limit` parametresi dönen kayıt sayısını sınırlar |

## Ayrıntı taşıyan case'ler (3)

## MT-SEC-024 — `AllowedTenants` beyaz listesi doluyken listede olmayan bir değer 403 ile reddedilir

**Gerçek sonuç**
`HTTP: 403`, `title: "Kiraci reddedildi"`, `detail: "Cozulen kiraci izin verilenler listesinde degil. Bu istek varsayilan kiracinin verisine SESSIZCE dusurulmez; reddedilir."` — `{"tenantId":"default"}` DÖNMEDİ. K-393 öncesi kusurun düzeltmesi doğru çalışıyor (düzeltilmiş davranış gözlendi). Geçici `options.AllowedTenants.Add("kiraci-alfa")` satırı `Program.cs`'e eklenip test koşuldu, sonra kaldırılıp yeniden derlendi (`git diff` temiz, iz bırakmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Kiracı izolasyonu (veri sınırı)

**Ön koşul (tüm bölüm)** — §3'ün ön koşulu (Tenancy açık, header çözümü açık)
uygulanmış olmalı; ek olarak `AllowedTenants` boş bırakılır (whitelist yok).

---

## MT-SEC-054 — Bilinmeyen kapsam değeri → `400` (kapalı liste)

**Gerçek sonuç**
**KALDI - HATA-S2-006 (Orta).** Beklenen HTTP 400 yerine HTTP 500 (genel ProblemDetails, 'An error occurred while processing your request.') dondu. Kok neden: CreateAsync (ApiKeyEndpoints.cs:57-64) [FromBody] ApiKeyCreateRequest ile OTOMATIK minimal-API govde baglama kullaniyor; bilinmeyen bir ApiKeyScope dizgisi System.Text.Json'in JsonStringEnumConverter'inda bir JsonException firlatir ve bu istisna handler govdesine HIC ULASMADAN once, framework'un kendi govde-baglama asamasinda olusur. Diger uclar (orn. /api/agents/validate, /v1/chat/completions) govdeyi ELLE JsonSerializer.Deserialize + try/catch (JsonException) ile okuyup temiz 400 'Govde cozumlenemedi:' uretiyor; bu uc ise otomatik baglamaya guveniyor ve app.UseExceptionHandler() (Program.cs:682, ozellestirilmemis) istisnayi genel 500 ProblemDetails'a ceviriyor. Kapsam: [FromBody] kullanan diger 10 dosya da (ApprovalEndpoints, EvalEndpoints, ExperimentEndpoints, RetentionEndpoints, QuotaEndpoints, RunEndpoints, SchedulingEndpoints, SkillScriptGrantEndpoints, WebhookEndpoints, WorkflowEndpoints) potansiyel olarak ayni deseni tasiyabilir - ayrintili dogrulanmadi, yalniz bu case olculdu.

---

**Yeniden koşum (Aile G, 2026-08-14).** DÜZELTİLDİ — **HTTP 400**:
`{"title":"Gecersiz istek govdesi","detail":"The JSON value could not be converted to Tracon.ApiKeyScope. Path: $.scopes[0]..."}`.
Kök neden düzeltmesi tek endpoint'e özel bir yama DEĞİL, kütüphane çapında bir
yeniden tasarımdır: `ApiKeyEndpoints.CreateAsync` artık `[FromBody]` otomatik
baglamasi yerine `RequestBodyBinding.ReadAsync<T>` (yeni,
`Tracon.AspNetCore/Internal/RequestBodyBinding.cs`) ile govdeyi elle okur —
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

## MT-SEC-095 — Denetim izinde silme/düzeltme ucu YOKTUR

**Gerçek sonuç**
HTTP: 405 (Method Not Allowed).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bölüm sonu — temizlik

Bu dosyadaki case'ler bittiğinde:

1. `samples/Tracon.Api/RoleTestAuthHandler.cs` (varsa) silinir.
2. `Program.cs`'e eklenen `AddAuthentication`/`AddAuthorization`,
   `UseAuthentication`/`UseAuthorization`, `options.AllowRemoteAccess = true;`
   ve `options.RequireRolePolicies = true;` satırları geri alınır.
3. `git diff samples/Tracon.Api/Program.cs` çalıştırılıp değişiklik
   KALMADIĞI doğrulanır.
4. `dotnet user-secrets list` ile `Tracon:Tenancy:*` girdileri temizlenir
   (isteğe bağlı — sonraki dosya zaten kendi reset yordamını uygular).

---
