# Faz 218 — `AuthorizationConfigurationKey` Alanını Kaldırma

> **Durum:** 📋 Planlandı (2026-10-17)
> **Plan onayı:** onaylanmadı — uygulama başlamaz. Planlamada verilen kararlar (farukatasoy, 2026-10-17): eski alanı gönderen istek **`400` ile reddedilir** ve yerine geçen alanı adıyla söyler · veri **aynı migration'da taşınır ve sütun düşürülür** · iş freeze'den (`1.0.0-rc.1`) önce yapılır (UR-012 = A).
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-290**
> **Genellik:** MCP sunucusu kaydeden **her** tüketici: HTTP (`PUT /api/mcp-servers/{name}`), konsol formu ve kodla (`McpServerDefinition`) kayıt. İş tüketici kodunda kalamaz: alan, sütun ve form Tracon'dadır. İkinci kanıt: kaldırma 1.0'a duyuruldu (K-869, `CHANGELOG`, XML, `production.md`) ve UR-012 kırıcı kalemleri freeze'den önce ister.
> **Karşı görüş:** "`[Obsolete]` alan zararsızdır; kaldırmak veri taşıma ve kırıcı değişiklik getirir." Cevap: freeze'den sonra kaldırmak bir major ister. `[Obsolete]` üye 1.x boyunca donar ve iki yol, iki çakışma kuralı ve on bir `CS0618` bastırması kalıcı olur. Preview'da kaldırma kırıcı ama serbesttir; sürüm notunda adıyla yazılır.
> **Önkoşul:** Yok ([Faz 190](arsiv/fazlar/190-KIMLIK-BASLIKLARI-ANAHTAR-REFERANSI.md) `headerConfigurationKeys`'i ve `[Obsolete]`'u getirdi; kapandı)
> **Paketler:** `Tracon.Abstractions`, `.AspNetCore`, `.Mcp`, `.Sql.Shared` (kaynak paylaşımı), `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Testing.Contracts.Xunit`, `.Client` (üretilen), `.UI`, `@tracon/client` (üretilen)
> **Yeni paket:** Yok · **Migration:** gerekli — üç sağlayıcı; numara uygulama anında alınır (bugün son: PostgreSQL `0061`, SQL Server `0049`, SQLite `0048`)
> **Public API:** **küçülüyor** — iki public üye kalkar (`McpServerDefinition.AuthorizationConfigurationKey`, `McpServerRequest.AuthorizationConfigurationKey`); HTTP şemasından bir alan kalkar. `PublicAPI.Shipped.txt` boş: preview'da kırıcı değişiklik serbest, sürüm notunda adıyla (K-604 kapısı tipi görür)
> **Tüketici yüzeyi:** site: `guides/production.md` (`:404`, `:471-476` kaldırma paragrafı), `getting-started/security.md:229`, `reference/configuration.md:653`, MCP rehberi (`guides/external-agents.md` ya da MCP sayfası — uygulamada `grep -rn "authorizationConfigurationKey" docs-site/src/content/docs` ile), `http-api/` ve `api/` (üretilir), `reference/changelog` (üretilir)
> · sevk edilen: XML (`McpServerDefinition`, `McpServerRequest`), `src/Tracon.Mcp/README.md`, `CHANGELOG.md` `Removed`, `tracon.json` (OpenAPI), `Tracon.Client`, `@tracon/client`
> **Manuel test alanı:** `docs/manuel-test/18-MCP-VE-A2A.md` (`MT-MCP-086…`; mevcut `MT-MCP-005`, `012`, `048` yeniden yazılır)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.

1. Bu doküman
2. Kararlar — yalnız bu kalemleri grep'le:
   ```bash
   grep -n "^| \*\*K-059 \|^| \*\*K-869 \|^| \*\*K-870 \|^| \*\*K-178 \|^| \*\*K-604 " docs/KARARLAR.md
   ```
   **K-059** (`secret` yazılmaz; kayıtta yalnız anahtarın adı) · **K-869** (alan `[Obsolete]`, `1.0.0`'da kalkar; iki alandan ya da OAuth ile `Authorization` `400`) · **K-870** (`PUT`'ta yok/`null` harita saklı değeri korur, `{}` temizler) · **K-178** (migration numaraları sağlayıcı başına bağımsız) · **K-604** (kırıcı değişiklik kapısı)
3. [`arsiv/fazlar/190-KIMLIK-BASLIKLARI-ANAHTAR-REFERANSI.md`](arsiv/fazlar/190-KIMLIK-BASLIKLARI-ANAHTAR-REFERANSI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/190-KIMLIK-BASLIKLARI-ANAHTAR-REFERANSI.md
   ```
4. Alan hafızası: [`hafiza/sql-migration.md`](hafiza/sql-migration.md) (migration kuralları) · [`hafiza/sqlite.md`](hafiza/sqlite.md) (`DROP COLUMN`, `json_each` gölgeleme tuzağı) · [`hafiza/sql-server-tuzaklari.md`](hafiza/sql-server-tuzaklari.md) (`OPENJSON`) · [`hafiza/mcp-a2a-sunucu.md`](hafiza/mcp-a2a-sunucu.md) · [`hafiza/nswag-istemci-uretimi.md`](hafiza/nswag-istemci-uretimi.md) (istemci yeniden üretimi) · [`hafiza/frontend.md`](hafiza/frontend.md)
5. Emsal migration'lar (sütun düşürme): PostgreSQL `0043_job_handler_key.sql`, SQL Server `0030_job_handler_key.sql`, SQLite `0030_job_handler_key.sql`; JSON sütunu: PostgreSQL `0054_header_configuration_keys.sql` ve eşleri

---

## Amaç

Faz 190 MCP sunucusunun `Authorization` başlığını yeni `headerConfigurationKeys`
haritasına taşıdı ve eski `AuthorizationConfigurationKey` alanını `[Obsolete]`
yaptı. Bu faz eski alanı her katmandan kaldırır: saklı veri yeni haritaya
taşınır, sütun düşer, public üyeler ve HTTP alanı kalkar, form yeni alanı yazar.
Freeze'den sonra 1.x tek bir kimlik yolu taşır.

- **F-290** — veri taşıma + sütun düşürme (üç sağlayıcı) · public üye ve HTTP alanını kaldırma · eski alanı gönderen isteğe `400` · konsol formunun geçişi · istemcilerin, sözleşme paketinin ve belgelerin güncellenmesi.

### Bugün ne var — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`McpServerDefinition.cs:82-83`](../src/Tracon.Abstractions/Mcp/McpServerDefinition.cs) | `[Obsolete(ObsoleteMessages.AuthorizationConfigurationKey)] public string? AuthorizationConfigurationKey` |
| [`GovernanceContracts.cs:48-49`](../src/Tracon.AspNetCore/Contracts/GovernanceContracts.cs) | `McpServerRequest` aynı alanı public taşır |
| [`ObsoleteMessages.cs:24-28`](../src/Tracon.Abstractions/ObsoleteMessages.cs) | `internal static class`; mesaj "removed in 1.0.0" der |
| [`GovernanceEndpoints.cs:310-312`](../src/Tracon.AspNetCore/Endpoints/GovernanceEndpoints.cs) · `:942-953` · `:1133-1140` | Kayıt alanı saklar; iki alanda `Authorization` → `400`; eski alan anahtar alanı kuralından geçer |
| [`McpHeaderBuilder.cs:53-56`](../src/Tracon.Mcp/Internal/McpHeaderBuilder.cs) · `:60-66` · `:182-215` | **Öncelik:** `HeaderConfigurationKeys` içindeki bir başlık eski değeri **her zaman** ezer (`headers.Remove(header)` → `:66`); OAuth açıkken eski alan hiç okunmaz (`:196-206`); iki alan da çalışma anında `keySpace.Require`'dan geçer (`:55`, `:214`) |
| [`McpConnection.cs:121-133`](../src/Tracon.Mcp/Internal/McpConnection.cs) | Bağlantı parmak izi eski alanı içerir; alan kalkınca her bağlantı yükseltmeden sonra **bir kez** yeniden kurulur |
| [`SqlApprovalAndMcpStores.cs:201-205`](../src/Tracon.Sql.Shared/Stores/SqlApprovalAndMcpStores.cs) · `:296-297` | Paylaşılan SQL deposu sütunu yazar ve okur (`GetNullableString(reader, 6)` — **okuma sırası sütun indeksine bağlıdır**) |
| `PostgresQueries.cs:1012-1024` · `SqlServerQueries.cs:1144-1174` · `SqliteQueries.cs:998` · `SqlQueriesBase.cs:1160` | Üç sağlayıcının sorgu metni sütunu anar |
| PostgreSQL `0002_observability.sql:119` · SQL Server `0001_initial.sql:390` · SQLite `0001_initial.sql:295` | Sütun tanımı (`text` / `nvarchar(200)` / `TEXT`); `header_configuration_keys` JSON (PostgreSQL `jsonb`, SQL Server `nvarchar` + `ISJSON`, SQLite `TEXT` + `json_valid`) |
| [`ToolApprovalRuleStoreContract.cs:289-290`](../src/Tracon.Testing.Contracts.Xunit/Contracts/ToolApprovalRuleStoreContract.cs) · `:353-354` | **Sevk edilen** sözleşme paketi eski alanı gidiş-dönüş sınar |
| [`mcp.tsx:41`](../src/Tracon.UI/frontend/src/screens/mcp.tsx) · `:65-66` · `:378-384` · `:425-427` | Form eski alanı yazar; OAuth açılınca temizler; özet eski alanı okur. Form yalnız **ekler** (`full-replace-editors.test.tsx` `CREATE_ONLY`) |
| `locales/{en,tr}/operations.ts` `mcp.authKey`, `mcp.authKeyHint` | Alanın etiketi ve ipucu |
| `git grep -n "pragma warning disable CS0618"` | 11 blok: 7 `src`, 2 sevk edilen sözleşme, 2 test |
| Testler | `GovernanceEndpointTests`, `EgressGuardTests:160`, `TenantScopedConfigurationKeyTests`, `CredentialHeaderSaveTests`, `McpHeaderBuilderTests:154`, `ConfigurationKeyGuardTests`, `AuditSecretFilterTests`, `ObsoleteMessagesTests`, `UsageAnalyzerTests:154,214` (`TRC0201` örneği alanı kullanır), `TemplateAgentsFileTests:602` |
| Site | `production.md:404`, `:471-476`; `security.md:229`; `configuration.md:653` |

> Kanıtlar 2026-10-17 tarihinde doğrulandı.

---

## 218.1 — Veri taşıma ve sütun düşürme (üç sağlayıcı)

Kural koddan türetilir ve **bugünkü çalışma anı davranışını birebir korur**
(`McpHeaderBuilder.cs:60-66`, `:196-206`). Her `mcp_servers` satırı için:

```mermaid
flowchart TD
    A{authorization_configuration_key dolu mu?} -- hayır --> Z[dokunma]
    A -- evet --> B{oauth_enabled?}
    B -- evet --> D[eski değeri at: bugün de okunmuyor]
    B -- hayır --> C{header_configuration_keys içinde<br/>'authorization' anahtarı var mı?<br/>büyük/küçük harfe bakmaz}
    C -- evet --> D2[eski değeri at: yeni harita bugün de ezer]
    C -- hayır --> E["header_configuration_keys + {'Authorization': eski değer}"]
    D --> F[sütunu düşür]
    D2 --> F
    E --> F
    Z --> F
```

| Sağlayıcı | Birleştirme | Anahtar araması | Sütun düşürme |
|---|---|---|---|
| PostgreSQL | `header_configuration_keys \|\| jsonb_build_object('Authorization', authorization_configuration_key)` | `EXISTS (SELECT 1 FROM jsonb_object_keys(header_configuration_keys) k WHERE lower(k) = 'authorization')` | `ALTER TABLE … DROP COLUMN IF EXISTS` (emsal `0043`) |
| SQL Server | `JSON_MODIFY(header_configuration_keys, '$.Authorization', authorization_configuration_key)` | `EXISTS (SELECT 1 FROM OPENJSON(header_configuration_keys) WHERE LOWER([key]) = 'authorization')` | `IF COL_LENGTH(…) IS NOT NULL ALTER TABLE … DROP COLUMN` (emsal `0030`) |
| SQLite | `json_set(header_configuration_keys, '$.Authorization', authorization_configuration_key)` | `EXISTS (SELECT 1 FROM json_each(header_configuration_keys) WHERE lower(key) = 'authorization')` | `ALTER TABLE … DROP COLUMN` (3.35+, emsal `0030`) |

- 🚨 SQLite `json_each`'in kendi sütunları (`key`, `value`, `id`) dış tabloyu gölgeler (`hafiza/sqlite.md`); alias'la yaz.
- Anahtar alanı kuralı değişmez: taşınan değer çalışma anında `keySpace.Require`'dan yine geçer (`:55`), yani taşıma güvenliği gevşetmez. Kurala aykırı eski bir satır bugün bağlantıda istisna verir; taşındıktan sonra da aynısını verir.
- Sütun **aynı migration'da** düşer (planlamada karar). Geri dönüş yoktur: migration'dan sonra `preview.4`'e dönen bir uygulama bu tabloyu okuyamaz. Sürüm notunda yazılır.
- Paylaşılan SQL okuyucusu sütunu **sıra indeksiyle** okur (`GetNullableString(reader, 6)`); sütun kalkınca sonraki indeksler kayar. Üç sağlayıcının `SELECT` listesi ve okuyucu birlikte değişir.

## 218.2 — Public üyeleri ve HTTP alanını kaldırma

- `McpServerDefinition.AuthorizationConfigurationKey` ve `McpServerRequest.AuthorizationConfigurationKey` kalkar; `ObsoleteMessages.AuthorizationConfigurationKey` sabiti kalkar (sınıf `internal`).
- `McpHeaderBuilder.AddLegacyAuthorization`, `McpConnection.ComputeFingerprint`'teki eski parça, `GovernanceEndpoints`'teki üç eski dal ve SQL deposunun iki satırı kalkar. 11 `CS0618` bastırmasının hepsi kalkar (`git grep -n "pragma warning disable CS0618"` → 0 hedeflenir; başka bir `[Obsolete]` üye kalırsa sayı onu gösterir).
- Sevk edilen `ToolApprovalRuleStoreContract`'ın iki eski case'i kalkar; `Authorization`'ı `headerConfigurationKeys` ile gidiş-dönüş sınayan case kalır (yoksa eklenir).
- `UsageAnalyzerTests` `TRC0201` örneği başka bir `*ConfigurationKey` üyesine geçer (analizör genel üye adına bakar; davranış değişmez — uygulamada ölçülür).
- OpenAPI (`tracon.json`), `Tracon.Client` ve `@tracon/client` yeniden üretilir; kırılma `CHANGELOG [Unreleased]` → `Removed` (tip adlarıyla; K-604 kapısı).

## 218.3 — Eski alanı gönderen istek `400` alır

System.Text.Json bilinmeyen alanı varsayılan olarak sessizce atar; eski bir
istemcinin kimlik başlığı böylece kaybolur ve MCP çağrısı `401` alır.
Planlamada karar: `PUT /api/mcp-servers/{name}` gövdesi
`authorizationConfigurationKey` taşırsa (değeri boş olsa da) `400` döner:

```
title: "Removed field"
detail: "'authorizationConfigurationKey' was removed in 1.0. Use headerConfigurationKeys[\"Authorization\"]."
```

Uygulama yolu Açık Soru 1'dedir. Kural yalnız bu alan içindir; başka bilinmeyen
alanlar bugünkü gibi yok sayılır.

## 218.4 — Konsol formu

- Form `authorizationConfigurationKey` yerine `headerConfigurationKeys: { Authorization: <anahtar> }` gönderir; alan boşsa haritayı **hiç göndermez** (K-870: yok → saklı değer korunur).
- OAuth açılınca form bu girdiyi temizler (bugünkü `:425-427` davranışı).
- `credentialSummary` yalnız haritayı okur.
- Etiket ve ipucu (`mcp.authKey`, `mcp.authKeyHint`) korunur; `en.ts` ve `tr.ts` değişmezse çeviri işi yoktur.
- Arayüz payı: değişiklik birkaç satırdır; bundle gzip KB **ölçülmeli** (bütçe 250 KB).

## 218.5 — Belgeler

- `production.md:471-476` "deprecated" paragrafı "removed in this release" anlatısına döner. `:404` ve `security.md:229` tablolarından eski alan çıkar. `configuration.md:653` cümlesi düzelir.
- `src/Tracon.Mcp/README.md` ve XML; `CHANGELOG` `Removed` + "Upgrading" bölümüne migration notu ve "sütun düşer, geri dönüş yok" cümlesi.
- MIMARI veya hafıza: yalnız davranış değişirse.

---

## Planlanan Public API

> Kalkan üyeler. Eklenen public üye yok.

```csharp
// Tracon.Abstractions — kalkar
public string? McpServerDefinition.AuthorizationConfigurationKey { get; init; }

// Tracon.AspNetCore — kalkar
public string? McpServerRequest.AuthorizationConfigurationKey { get; init; }
```

### HTTP `endpoint`'leri

| Metot | Yol | Değişiklik |
|---|---|---|
| `PUT` | `/api/mcp-servers/{name}` | İstek şemasından `authorizationConfigurationKey` kalkar; alanı taşıyan gövde `400` |
| `GET` | `/api/mcp-servers` · `/{name}` | Yanıt şemasından alan kalkar |

### Arayüz payı

`mcp.tsx` birkaç satır; ölçülmeli.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Mcp/McpServerDefinition.cs · ObsoleteMessages.cs · PublicAPI.Unshipped.txt
src/Tracon.AspNetCore/Contracts/GovernanceContracts.cs · Endpoints/GovernanceEndpoints.cs · PublicAPI.Unshipped.txt · tracon.json (üretilen)
src/Tracon.Mcp/Internal/McpHeaderBuilder.cs · Internal/McpConnection.cs · README.md
src/Tracon.Sql.Shared/Stores/SqlApprovalAndMcpStores.cs · Internal/SqlQueriesBase.cs
src/Tracon.PostgreSql/Internal/PostgresQueries.cs · Migrations/00NN_remove_authorization_configuration_key.sql
src/Tracon.SqlServer/Internal/SqlServerQueries.cs · Migrations/00NN_remove_authorization_configuration_key.sql
src/Tracon.Sqlite/Internal/SqliteQueries.cs · Migrations/00NN_remove_authorization_configuration_key.sql
src/Tracon.Testing.Contracts.Xunit/Contracts/ToolApprovalRuleStoreContract.cs
src/Tracon.Client/Generated/* (üretilen) · src/Tracon.UI/frontend/src/screens/mcp.tsx · @tracon/client schema (üretilen)
tests/… (aşağıdaki tablo) · docs-site/… · CHANGELOG.md · docs/manuel-test/18-MCP-VE-A2A.md
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| Eski değer yeni haritaya taşınmaz (bağlantı kimliksiz kalır) | Depo — üç sağlayıcı | migration testi: eski şemaya satır yaz, migration'ı koş, `HeaderConfigurationKeys["Authorization"]` = eski değer (her sağlayıcının entegrasyon projesi; emsal: migration testleri `hafiza/sql-migration.md`) |
| Yeni haritada `authorization` (her harf biçimi) varken eski değer üstüne yazılır | Depo — üç sağlayıcı | aynı test, `"authorization"` ve `"AUTHORIZATION"` case'leri: harita değişmez |
| OAuth açık satırda eski değer taşınır (bugün okunmuyordu, taşınınca da gönderilmez ama kayıt `400` kuralına aykırı olur) | Depo — üç sağlayıcı | OAuth case'i: harita değişmez |
| Haritadaki başka anahtarlar kaybolur | Depo — üç sağlayıcı | iki başlıklı harita + eski değer → üç anahtar |
| Boş/`NULL` eski değer haritaya boş `Authorization` ekler | Depo — üç sağlayıcı | `''` ve `NULL` case'leri: harita değişmez |
| Okuyucu sütun indeksi kayar, başka alan yanlış okunur | Sözleşme | `ToolApprovalRuleStoreContract` (MCP sunucu gidiş-dönüşü, bütün alanlar) — dört koşumda |
| Eski alanı gönderen istek sessizce kabul edilir | Fonksiyonel (HTTP) | `GovernanceEndpointTests`: gövdede alan → `400`, mesaj yerine geçeni adıyla söyler; değer `""` iken de `400`; alan yoksa kayıt çalışır |
| Başka bilinmeyen alan da reddedilmeye başlar | Fonksiyonel (HTTP) | gövdede rastgele bilinmeyen alan → `200` (bugünkü davranış) |
| Taşınan anahtar başka kiracının anahtar alanına kayar | Fonksiyonel | `TenantScopedConfigurationKeyTests`: taşınmış satır çalışma anında aynı `keySpace` kuralından geçer |
| Bağlantı parmak izi değişince bağlantı her istekte yeniden kurulur | Birim | `McpConnection` parmak izi kararlı (aynı tanım → aynı iz) |
| Form yeni alanı göndermez ya da boşken `{}` gönderip saklı başlıkları siler | Arayüz (Vitest) + E2E | `mcp.tsx` testi: dolu → `headerConfigurationKeys.Authorization`, boş → alan yok; `McpTests` E2E kayıt akışı |
| OpenAPI/istemci eski alanı taşır | Sözleşme | `OpenApiSnapshotTests` + istemci üretim kapısı |
| `secret` (anahtar adı değil değer) bir yere sızar | Birim | `AuditSecretFilterTests` güncellenir; yeni değer sızıntısı yok |
| Paketlenmiş tüketicide sözleşme kırılır | Paket | `kapi.py yayin --kuru` (sevk edilen sözleşme değişti — `faz-tamamlama` Adım 1 kuralı) |

Beş soru: iptal (migration transaction içinde; yarıda kalan migration journal'a yazılmaz) · eşzamanlılık (göç kilidi, mevcut) · boş/aşırı girdi (`''`, `NULL`, 200 karakter `nvarchar(200)`) · başka kiracı (satır kendi kiracısında taşınır; kiracılar arası birleştirme yok) · alt sistem hatası (migration düşerse uygulama açılmaz; bugünkü davranış).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| `MT-MCP-086` | Faz 217 sürümüyle (`preview.4` şeması) eski alanlı bir MCP sunucusu kayıtlı (SQLite, PostgreSQL) | Yeni sürümle başlat (migration koşar), `GET /api/mcp-servers/{ad}` | `headerConfigurationKeys.Authorization` eski anahtar adı; `authorizationConfigurationKey` yanıtta yok; MCP aracı çalışır |
| `MT-MCP-087` | Aynı | Haritasında zaten `authorization` olan bir satır | Harita değişmez; eski değer atılır |
| `MT-MCP-088` | Örnek uygulama | `PUT /api/mcp-servers/x` gövdesinde `authorizationConfigurationKey` | `400`, `detail` yerine geçeni adıyla söyler |
| `MT-MCP-089` 👤 | Konsol | MCP formunda anahtar adı girip kaydet; sonra OAuth'u açıp kapat | Kayıt `headerConfigurationKeys.Authorization` taşır; OAuth açılınca alan temizlenir |
| `MT-MCP-005`, `012`, `048` | — | Yeniden yazılır: eski alan yerine `headerConfigurationKeys["Authorization"]` | Aynı davranış, yeni alanla |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `400` kuralı nasıl uygulanır? | A: `PUT` uç filtresi gövdeyi tamponlar ve yalnız bu alan adını arar · B: `McpServerRequest` üzerinde `[JsonExtensionData]` ile bilinmeyen alanları toplayıp adı kontrol etmek · C: `[JsonUnmappedMemberHandling(Disallow)]` | **A.** B OpenAPI şemasına `additionalProperties` ekleyebilir (ölçülmeli); C her bilinmeyen alanı reddeder, karar bunu istemedi |
| 2 | Kural ne kadar yaşar? | A: kalıcı (1.x boyunca) · B: bir sürüm sonra kalkar | **A.** Eski istemci 1.x boyunca sessiz kimlik kaybı yaşamasın; bedel tek bir filtre |
| 3 | Taşıma, sağlayıcının eski kayıtlarında anahtar alanı kuralına aykırı bir değeri de taşısın mı? | A: taşı (çalışma anı bugünkü gibi reddeder) · B: taşıma, at ve uyar | **A.** Bugünkü davranışı korur; atmak sessiz veri kaybıdır |
| 4 | `TRC0201` analizör örneği hangi üyeye geçsin? | A: `OAuthClientSecretConfigurationKey` · B: `HeaderConfigurationKeys` sözlüğü (zaten ayrı testte) | **A** — tekil üye case'i kalır |

---

## Bitiş Ölçütleri (DoD)

- [ ] `git grep -n "AuthorizationConfigurationKey\|authorization_configuration_key\|authorizationConfigurationKey" -- src tests samples` yalnız migration dosyalarını ve `400` kuralının metnini/testini gösterir
- [ ] Üç sağlayıcının migration testi: taşıma, `authorization` çakışması (iki harf biçimi), OAuth, boş/`NULL`, başka anahtarlar korunur — yeşil
- [ ] `ToolApprovalRuleStoreContract` dört koşumda yeşil
- [ ] `PUT` eski alanla `400` (mesaj yerine geçeni anar); bilinmeyen başka alan `200`
- [ ] `git grep -n "pragma warning disable CS0618"` bu alan için 0
- [ ] OpenAPI, `Tracon.Client`, `@tracon/client` yeniden üretildi; `CHANGELOG [Unreleased]` `Removed` + "Upgrading" (sütun düşer, geri dönüş yok)
- [ ] Konsol formu yeni alanı yazar; Vitest + E2E yeşil; bundle payı ölçüldü
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `kapi.py yayin --kuru` yeşil (sevk edilen sözleşme değişti)
- [ ] `samples/Tracon.Api` ile gerçek `run`: eski şemadan yükseltilmiş SQLite veritabanında MCP aracı çağrısı — çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run check` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz (metin değişmediyse yazılır)

### Doğrulama komutları

```bash
# eski alan kaldı mı
git grep -n "AuthorizationConfigurationKey\|authorization_configuration_key\|authorizationConfigurationKey" -- src tests samples

# eski alanla kayıt
curl -s -o /dev/null -w "%{http_code}\n" -X PUT "$APU/api/mcp-servers/x" -H "$APB" \
  -H "content-type: application/json" \
  -d '{"endpoint":"https://example.test/mcp","transport":"StreamableHttp","authorizationConfigurationKey":"Tracon:McpSecrets:X"}'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Sütun düşer; geri dönüş yok | Sürüm notu ve "Upgrading" bölümü; manuel `MT-MCP-086` eski şemadan yükseltir |
| Okuyucu indeks kayması başka alanı bozar | Sözleşme testi bütün alanları gidiş-dönüş sınar; üç sağlayıcıda |
| Üçüncü taraf `IMcpServerStore` eski alanı okuyordu | Derleme hatası (alan kalktı); `CHANGELOG` `Removed`'da tip ve üye adıyla |
| Yükseltmeden sonra her MCP bağlantısı bir kez yeniden kurulur (parmak izi değişir) | Beklenen; sürüm notunda bir cümle |
| SQL Server `JSON_MODIFY` büyük/küçük harf ayrımı yapan JSON yolunda mevcut `"authorization"` anahtarını görmeyip ikinci bir anahtar ekler | Anahtar araması `OPENJSON` + `LOWER([key])` ile yapılır ve birleştirme yalnız anahtar yoksa koşar (tablo) |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Dört kategoriden birine giren karar `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer. Plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak.**

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
