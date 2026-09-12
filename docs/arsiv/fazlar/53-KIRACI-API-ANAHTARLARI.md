# Faz 53 — Kiracı Bazlı API Anahtarları ve Kapsamlar

> **Durum:** ✅ Tamamlandı (2026-08-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-56**
> **Önkoşul:** [Faz 41](41-KIRACI-YALITIMININ-ZORLANMASI.md) — kiracı yalıtımı zemini · [Faz 50](50-DISA-ACILAN-AGENT-YUZEYI.md) — bu fazı **acil** kılan dış yüzey
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.AspNetCore`, `Tracon.Sql.Shared`, `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** gerekli — numara uygulama anında alınır (üç sağlayıcı için ayrı)
> **Public API:** büyüyor — yeni tipler ve bir depo arayüzü. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/53-KIRACI-API-ANAHTARLARI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'e gelen kimlik doğrulaması bugün **tek statik bearer token**'dır. Bu token'ı bilen herkes bütün kiracıların bütün uçlarına erişir. Anahtar döndürme, iptal, kapsam daraltma ve kiracıya bağlama yolu yoktur.

## Plandan Sapmalar

- **`IApiKeyStore` imzası planın taslağından farklı.** `ListAsync` ve `RevokeAsync`
  taslakta kiracı parametresi taşımıyordu; diğer tüm kiracı-parametreli depolarla
  (`IWebhookStore.ListSubscriptionsAsync(tenantId, ...)` gibi) tutarlılık için
  ikisine de `string tenantId` eklendi. `CreateAsync` zaten `ApiKeyDraft.TenantId`
  üzerinden kiracıyı taşıyordu, değişmedi.
- **`HasActiveScopeAsync` plana hiç yazılmamış yeni bir arayüz üyesi.**
  `ExternalSurfaceGuard.EnsureRemoteAccessNotCombined`'ın "sistemde en az bir
  `external:invoke` anahtarı var mı" sorusunu tenant-agnostik sormasının tek
  yolu buydu — mevcut `ListAsync`/`FindByHashAsync` bunu karşılamıyordu. K-361
  gerekçesi yerine `IApiKeyStore.cs` içindeki XML doküman ve `[TenantAgnostic]`
  gerekçesi kalıcı kayıttır.
- **Kapsam (`RequireApiKeyScope`) denetimi tam yüzeye değil, DoD'nin adlandırdığı
  uçlara uygulandı** (K-360). Tam taksonomi bilinçli olarak ertelendi.
- **Bearer katmanının davranışı bilerek değişti** (K-359): `Authorization`
  başlığı sunulduğunda `AuthToken` tanımsız olsa bile artık doğrulanır. Plan
  metni "ikisi de tanımsızsa davranış aynıdır" diyordu; bu yalnız **başlık
  YOKKEN** geçerlidir — başlık varken sessiz geçiş kaldırıldı.
- **`docs/openapi/tracon.json` değişti** (yeni `/api/api-keys` uçları);
  Faz 53'ün DoD'sinde bahsedilmiyordu ama diğer her yeni uç ucu gibi otomatik
  snapshot testine girdi ve `TRACON_OPENAPI_REFRESH=1` ile tazelendi.

## Bu Fazda Verilen Kararlar

K-356, K-357, K-358, K-359, K-360, K-361 — `docs/KARARLAR.md`.

## Bitiş Ölçütleri (DoD)

- [x] `POST /api/api-keys` ham anahtarı bir kez döner — `Olusturma_ham_degeri_bir_kez_dondurur`, ayrıca `samples/Tracon.Api` ile elle doğrulandı
- [x] Geçerli bir API anahtarıyla yapılan istek, `X-Tracon-Tenant` başlığı olmadan doğru kiracıyı çözer — `Kiraci_basliktan_degil_anahtardan_cozulur`
- [x] Başlık anahtarın kiracısından farklı bir kiracı söylerse `403` döner — `Baslik_anahtarin_kiracisindan_farkliysa_403_alir`
- [x] Süresi geçmiş / iptal edilmiş anahtar `401` alır — `Suresi_gecmis_anahtar_401_alir`, `Iptal_edilen_anahtar_401_alir`
- [x] `runs:read` kapsamlı anahtar `POST /api/agents/{name}/run` çağırınca `403` alır — `Yetersiz_kapsamli_anahtar_403_alir`
- [x] `AllowRemoteAccess` + `external:invoke` anahtarıyla MCP/A2A yüzeyi açılır; anahtar yokken bugünkü red korunur — `AllowRemoteAccess_acikken_external_invoke_anahtari_yoksa_MCP_acilamaz` / `...anahtariyla_MCP_acilir`
- [x] Statik token'lı bugünkü kurulum hiç değişmeden çalışmaya devam eder — mevcut 431 fonksiyonel test (artı yeni 20'si) yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build/test/pack/format` hepsi temiz (bu oturumda ölçüldü)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü
- [x] `en.ts`/`tr.ts` eksiksiz (i18n.test.ts 16/16); bundle payı ölçüldü: **161.5 KB gzip / 250 KB bütçe**

### Doğrulama komutları — gerçek çıktı (2026-08-08, `samples/Tracon.Api`, loopback)

```
POST /tracon/api/api-keys {"name":"ci","scopes":["RunsRead"]}
→ {"record":{"id":"019fe119-...","tenantId":"default","name":"ci","keyPrefix":"ap_default_2",
   "scopes":["RunsRead"],...,"isActive":true},
   "plaintextKey":"ap_default_2zKqRNjJ6MVXcwtTxPKuXkouy85tReMOhGAgYr9MGzo"}

GET /tracon/api/agents  (Authorization: Bearer <RunsRead anahtarı>)
→ 403  (agents:read kapsamı yok — doğru davranış)

GET /tracon/api/agents  (Authorization: Bearer <AgentsRead anahtarı>)
→ 200

POST /tracon/api/agents/asistan/run  (Authorization: Bearer <RunsRead anahtarı>)
→ 403  (runs:write kapsamı yok)

DELETE /tracon/api/api-keys/{id} → 204; sonraki istekte aynı anahtar → 401
GET /tracon/api/api-keys → liste, hiçbir kayıtta plaintextKey/hash yok
grep raw-key /tmp/tracon-api.log → 0 eşleşme
```

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IApiKeyStore` (yukarıdaki imza) — her zaman kayıtlı (`TryAddSingleton`/`Replace`), K-018
  "birinci sınıf" örneği.
- `RequireApiKeyScope(ApiKeyScope)` uzantısı (`ApiKeyScopeRequirement`, internal) — yeni bir
  uca kapsam eklemek isteyen kod bunu kullanır, yeni bir mekanizma icat etmez.
- `ApiKeyRequestContext.Get(HttpContext)` — bir isteğin API anahtarıyla mı dogrulandigini
  soran her kod (ör. denetim izi zenginleştirmesi) bunu okur.

**Bilinen tuzaklar (🚨):**
1. **SQL Server sözleşme testleri bu makinede koşturulamadı** — `SqlServerFixture`
   gerçek `mcr.microsoft.com/mssql/server` kullanıyor ve bu Apple Silicon + Rosetta
   kombinasyonunda önceden bilinen bir sınırlamadır (K-317,
   `docs/hafiza/sql-server-yerel-test.md`). PostgreSQL (902/902) ve SQLite (469/469)
   sözleşmeleri **gerçek** çalıştırmayla doğrulandı; SQL Server sorgu metni aynı
   dosyadaki (var olan, ölçülmüş) desenlerle birebir yazıldı ama gerçek bir SQL
   Server üzerinde hiç çalıştırılmadı. Bir sonraki oturum (uygun makinede veya CI'da)
   `azure-sql-edge` ikamesiyle veya gerçek `mssql/server` ile `ApiKeyStoreContract`'ı
   çalıştırıp bu notu kapatmalı.
2. **`InMemoryApiKeyStore.RevokeAsync` ilk yazımda "zaten iptal edilmiş" durumunu
   kontrol etmiyordu** — SQL sürümü `WHERE revoked_at IS NULL` ile doğruydu, bellek
   içi sürüm eksikti. `ApiKeyStoreContract` (gerçek PostgreSQL container'ında
   koşturulunca) bunu yakaladı; birim testleri (InMemory, container'sız) YAKALAMADI.
   Ders: bir sözleşme testini yalnız bellek içi uygulamada koşturmak yetmez,
   **gerçek** bir sağlayıcıda da koşmalı — Faz 6/12/15/16/18/20/21/48 listesine
   eklenecek yeni bir örnek.
3. **Kapsam denetimi tam yüzeye uygulanmadı** (K-360). Webhook, kota, retention,
   skill, workflow yönetimi gibi Admin/Operator uçları API anahtarıyla erişilebilir
   ama scope'tan etkilenmez (yalnız rol politikasından geçer — statik token ile
   aynı zemin, GÜVENLİK AÇIĞI değil, kasıtlı kapsam sınırlaması).
4. **Rate limit / bütçe anahtar başına yok** (Açık Soru 1, B seçildi) — kota
   altyapısı (Faz 21) bir anahtara değil kiracıya bağlıdır.

**Yarım kalan / ertelenen işler:**
1. SQL Server üzerinde gerçek doğrulama (yukarıdaki tuzak 1).
2. Kapsam taksonomisinin geri kalan uçlara genişletilmesi (K-360'ın reopen koşulu).
3. Anahtar başına hız sınırı/bütçe (Açık Soru 1, seçenek B — ayrı aday kalemi).

**Sıradaki faz:** `docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md`'de Faz 53 sonrası sıradaki
kalem — plan dokümanı henüz yazılmadıysa `faz-planlama` skill'i ile başlanır.
