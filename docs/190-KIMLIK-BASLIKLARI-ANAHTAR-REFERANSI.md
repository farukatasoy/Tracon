# Faz 190 — Kimlik Taşıyan Başlıklar İçin Yapılandırma Anahtarı Referansı

> **Durum:** 📋 Planlandı (2026-09-23)
> **Plan onayı:** Bakımcı, 2026-09-23 (engelleyici kararlar sohbette alındı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-272**
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

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman.
2. Kararlar — dosyanın tamamını **okuma**:
   ```bash
   grep -n '^| \*\*K-\(059\|852\|535\|779\|603\) ' docs/KARARLAR.md
   grep -n "K-855" docs/KARARLAR.md   # ilk etiketli satır — yeni K biçimi
   ```
   **K-059** (kayıt değeri değil anahtar adını taşır) · **K-852** (ad kaydın kiracı
   alanındadır; kaydetme ve çözmede denetlenir) · **K-535** (webhook ek başlığı Tracon
   başlık adını taşıyamaz) · **K-779** (denetim `before`/`after` kalıcı düz metin) ·
   **K-603** (`Shipped` GA'ya kadar boş).
3. [Faz 187](187-KIRICI-DEGISIKLIK-KAPISI.md) devir notu — `CHANGELOG` ve `### Deprecated`
   biçimi; `yayin --kuru` bu fazın notlarını onunla denetler:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/187-KIRICI-DEGISIKLIK-KAPISI.md
   ```
4. Önkoşul kapısı — maskelemenin **davranışını** ölç:
   ```bash
   git log --oneline -i --grep=mask bb9953e3..HEAD
   grep -rn "sends_the_mask_back_is_rejected\|audit_trail_records_header_names" tests/Tracon.AspNetCore.FunctionalTests
   python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*GovernanceEndpointTests" "*WebhookEndpointTests"
   ```
   Adlar turun kolunda görüldü (2026-09-23, commit'siz); değişirse commit'in
   testlerini koş. Okuma testi yok/kırmızı → **başlama**, kullanıcıya sor. Denetim
   testi yok → o iş bu fazındır (190.6). Faz 185–189 aynı dosyalara dokunur: her
   `dosya:satır`'ı yeniden ölç.
5. Alan hafızası (`docs/hafiza/`): `http-uc-guvenlik-ve-sozlesme.md` (yeni
   `*ConfigurationKey` iki yolda `RequireTenantKey` alır) · `secenek-baglama-ve-gizlilik.md`
   (filtre ada bakar) · `sql-migration.md` · `sql-paylasilan-sorgu-uretimi.md` ·
   `nswag-istemci-uretimi.md`

---

## Amaç

MCP server'ı veya webhook alıcısı `X-API-Key`, `Ocp-Apim-Subscription-Key` veya
`Cookie` ister. Tracon yalnız `Authorization` için anahtar referansı sunar; diğer
başlıklar `Headers`'ta DB'ye düz yazılır (K-059 ihlali). Kazanan: API anahtarlı
hedef kullanan operatör; değer `user-secrets`'ta kalır.

- **F-272** — MCP ve webhook kaydına anahtar adı haritası; düz kimlik adı `400`;
  eski alan `[Obsolete]`.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `McpServerDefinition.cs:76-82` · `WebhookTypes.cs:129-136` | "Secret yazma" kuralı yalnız XML'de; "shown in the UI" yanlış (`mcp.tsx`'te yalnız `:564` yorumu) |
| `GovernanceEndpoints.cs:804-898` | `Validate` başlığa bakmaz; `Headers["Authorization"]` + OAuth geçer |
| `GovernanceEndpoints.cs:272` · `WebhookEndpoints.cs:215` · `mcp.tsx:36-48`, `:164-168` · `webhook-panel.tsx:287-297` | `headers` yoksa saklı başlık silinir; iki form `headers`'sız tam PUT yapar |
| `McpTransportFactory.cs:139-178` · `McpOAuthAuthorizationCoordinator.cs:212` | Anahtardan çözülen tek başlık `Authorization`; birleştirme iki kopya |
| `WebhookDeliveryJobHandler.cs:202-232` | Ek başlık aynen gider; anahtar kaynaklı başlık yok |
| `AuditingMcpServerStore.cs:112-113` · `AuditSecretFilter.cs:24-31`, `:148-189` | MCP denetimi kaydın tamamını yazar, filtre ada bakar: `X-Tenant`, `Cookie`, `Ocp-Apim-Subscription-Key` değeri `audit_log`'a düz girer (K-779; okunarak çıkarıldı) |
| `SqlWebhookStore.cs:231` · `McpTransportFactory.cs:145` | `X-A` + `x-a` kopyada `ArgumentException` (webhook kaydı, MCP bağlantı). **Okunarak çıkarıldı** — düşen test ölçer |
| `src/Tracon.Mcp/README.md:40` · `external-agents.md:53`, `:58` | Örnek `Tracon:Mcp:…` kullanır; önek `Tracon:McpSecrets:` (`TraconMcpSecurityOptions.cs:42`); örnek `400` alır |

> Kanıtlar 2026-09-23 tarihinde `bb9953e3` üzerinde doğrulandı.

**Sınıf taraması.** `grep -rn "Dictionary<string, string>?\? Headers" src` → beş tip;
dördü sınıfta. `IdempotencyResponse` (`IdempotencyTypes.cs:65`) server yanıtıdır, dışarıda.

---

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

## 190.1 — Veri modeli

İmza "Planlanan Public API"dedir. 🚨 JSON bağlama sözlüğü `Ordinal` karşılaştırıcıyla
kurar (`Headers` varsayılanı da `Ordinal`, `McpServerDefinition.cs:81-82`);
doğrulama harf duyarsızlığını kendisi uygular.

**Sütun** `header_configuration_keys`, `headers` biçiminde: PostgreSQL
`jsonb NOT NULL DEFAULT '{}'::jsonb`; SQL Server `nvarchar(max) NOT NULL` + `N'{}'` +
`ISJSON`; SQLite `TEXT NOT NULL DEFAULT '{}'` + `json_valid`. `DEFAULT` üçünde aynı
(`hafiza/sqlite.md`); SQLite `ADD COLUMN` + `CHECK` **ölçülmeli**. Eski satır `{}` okunur.

🚨 **Sütun her listenin SONUNA eklenir.** `ReadServer` (`SqlApprovalAndMcpStores.cs:273-293`,
sıra 0-16) ve `ReadSubscription` (`SqlWebhookStore.cs:249-263`, 0-10) sıra numarasıyla
okur; yeni sıra MCP 17, webhook 11. Yerler: paylaşılan `SqlQueriesBase.cs:952-956`,
`:1019-1022`; her lehçenin `Upsert*` `INSERT`/`UPDATE`'i (ör. `PostgresQueries.cs:963-986`);
**yalnız SQL Server:** elle yazılmış `mcpServerOutput` (`SqlServerQueries.cs:1071-1077`,
kullanım `:1095`, `:1100`) ve `webhookSubscriptionOutput` (`:1468`, `:1482`, `:1487`).
Unutulursa `SaveAsync` (`SqlApprovalAndMcpStores.cs:202`, `SqlWebhookStore.cs:97`)
17 sütun okur, `ReadServer` atar — yalnız SQL Server'da. `MigrationParityTests` bunu
skor tablosu için (`OUTPUT` dahil) denetler; iki tablo eklenir.

**Store.** Bellek içi `with` ile kopyalar (`InMemoryApprovalAndMcpStores.cs:138`,
`InMemoryWebhookStore.cs:89`); alan kendiliğinden taşınır.

**Sözleşme.** `McpServerStoreContract` (`ToolApprovalRuleStoreContract.cs:216`; başlık
testi yok) ve `WebhookStoreContract` (`:72` var) round-trip alır ve `SaveAsync`'in
**döndürdüğü** kaydı da doğrular (yalnız `GetAsync` `OUTPUT` kaymasını kaçırır).
Alanı düşüren üçüncü taraf store kimliksiz bağlanır; test onu yakalar.

## 190.2 — Paylaşılan sınıflandırıcı

`Tracon.Core`'da tek internal kaynak (öneri `Egress/CredentialHeaderNames.cs`).
IVT `Tracon.AspNetCore` ve `Tracon.Mcp`'yi kapsar (`AssemblyInfo.cs:6`, `:14`).
`SecretKeyFragments` ve `Normalize` buraya taşınır; `AuditSecretFilter` aynı listeyi okur.
Kural: normalleştirilmiş ad bir parçayı içerir (`tokens` istisnası korunur) · ad
`Cookie` · ad `-key` ile biter (harf duyarsız). `NonValueKeySuffixes` girmez: kayıt
özelliği adları içindir.

| Ad | Sonuç |
|---|---|
| `Authorization`, `Proxy-Authorization`, `X-API-Key`, `api_key`, `Private-Token`, `X-Auth-Token` | kimlik (parça) |
| `Ocp-Apim-Subscription-Key`, `X-Functions-Key`, `X-Access-Key` · `Cookie` | kimlik |
| `X-Idempotency-Key` | kimlik — bilinen yanlış pozitif |
| `X-Tenant`, `X-Server`, `X-Max-Tokens` | değil |
| `X-Auth`, `X-Signature`, `X-Session-Id` | değil — bilinen kalıntı |

## 190.3 — Kaydetme kuralları

İki PUT aynı sırayı izler; mevcut denetimler (ad, adres, SSRF, K-852, olaylar) önce koşar.

1. **Koruma (karar 5).** `headers` veya `headerConfigurationKeys` yok ya da `null` →
   saklı değer; `{}` temizler. Form iki alanı göndermez, `"***"`'ü geri yazamaz;
   arayüz kodu değişmez. MCP PUT önceki kaydı okumaz → `servers.GetAsync(tenant, name, ct)`
   eklenir; webhook zaten okur (`WebhookEndpoints.cs:201`).
2. **Kimlik benzeri ad (karar 3).** Yalnız **istekte gelen** `headers` yargılanır
   (Açık Soru 1). `400` metni (İngilizce, K-232) adı ve `headerConfigurationKeys`'i
   adlandırır; değeri yankılamaz.
3. **Girdi.** Başlık adı RFC 9110 `token`; boş, boşluk, `:`, CR/LF → `400`. 🚨
   Sarmalayıcı (`GovernanceEndpoints.cs:907-917`) boş anahtar adını geçirir; sözlük
   girdisi onu ayrıca reddeder. Ad `RequireTenantKey`'den geçer (`Tracon:McpSecrets:`,
   `Tracon:WebhookSecrets:` — `TraconWebhookOptions.cs:53`); alan adı
   `headerConfigurationKeys[X-API-Key]`. Webhook ayrılmış ad (`WebhookSigner.IsReservedHeader`) → `400`.
4. **Tekillik.** Birleşik `headers` ∪ `headerConfigurationKeys` adları harf duyarsız
   tekil; `ArgumentException` yolu kapanır. Metin "remove '<ad>' from headers" der
   (göç tarifi, 190.7). Düz `headers` için yeni `400`; `CHANGELOG`'a girer.
5. **`Authorization` çakışması (karar 2).** `headerConfigurationKeys["Authorization"]`
   + dolu `authorizationConfigurationKey` veya `oauthEnabled` → `400`. Mevcut kural
   (`GovernanceEndpoints.cs:889-896`) kalır. **Birleşik** durumda ölçülür: form
   `authorizationConfigurationKey` yazar, saklı kayıt yeni alanda `Authorization` taşır → `400`.

🚨 **Koruma yan etkisi.** Formdan yalnız `endpoint`'i değişen kayıt korunan başlıkları
ve çözülen değerleri **yeni host'a** gönderir; operatör onları görmez (bugün form PUT'u
siliyordu). K-852 yüzünden ayrıcalık yükseltmesi değil, güvenlik sürprizidir:
`.WithDescription` ve `security.md` söyler; uyarı logu Açık Soru 10.

## 190.4 — Çözme: tek başlık birleştirici

**MCP.** İki kopya (`BuildHeaders` — `McpShortLivedConnection.cs:48`, `McpConnection.cs:119`
kullanır — ve koordinatör) tek internal yardımcıya (öneri `Internal/McpHeaderBuilder.cs`)
iner; kopya kalırsa OAuth yolu çözülen başlığı kaçırır.

- `Headers` `OrdinalIgnoreCase` sözlüğe **indeksleyiciyle** kopyalanır; harf tekrarı
  atmaz, son değer kazanır, uyarı.
- Kimlik benzeri düz ad → uyarı, başlık **gider** (karar 7). OAuth açıkken düz
  `Authorization` dahil: bugün `McpTransportFactory.cs:147-155` `Headers`'ı aynen döner,
  koordinatör aynen kopyalar; bu faz yalnız uyarı ekler. `:147-155` yorumu ("Validate
  already rejects this combination") düzeltilir.
- **Doğrulanmadı — `maf-api-kesfi` ile ölçülmeli:** `HttpClientTransportOptions.AdditionalHeaders`
  `Authorization` taşırken `ClientOAuthOptions` hangi değeri gönderir. Sonuç devir
  notuna; değişiklik gerekirse kullanıcıya sorulur.
- Her anahtar `McpKeySpace.Require` (`McpKeySpace.cs:39-45`); ihlal `TraconException`,
  `McpConnection.cs:157-162` "could not connect" loglar — bugünkü
  `AuthorizationConfigurationKey` davranışı.
- Anahtar kaynaklı `Authorization` OAuth açıkken yazılmaz, uyarı (bugünkü eski alan
  yolu gibi; kaydetme reddeder, yalnız üçüncü taraf store'da olur). Ad hem düz hem
  anahtar kaynaklıysa anahtar kazanır.

**Parmak izi.** `ComputeFingerprint` (`McpConnection.cs:82-88`) bugün başlık
**değerini** içerir (bellekte, `McpToolCatalog.cs:236`). Yeni alan sıralı `ad=anahtarAdı`
ile girer; çözülen değer girmez. `server.Headers`'ı yalnız yardımcı ve `ComputeFingerprint` okur.

**Webhook.** `ExecuteAsync` her anahtarı teslimden önce `RequireTenantKey` ile denetler;
ihlalde `DropAsync` (`WebhookDeliveryJobHandler.cs:83-107` deseni). `SendAsync` çözülen
başlığı ekler. `AddExtraHeaders` kimlik benzeri düz ad için uyarır ve gönderir.
`MaxExtraHeaders` (20, `TraconWebhookOptions.cs:64`): Açık Soru 2. `configuration`
`null` (`:31`) → başlık gitmez, uyarı.

İki yolda: değer boş veya CR/LF → başlık gitmez, uyarı. 🚨 Uyarı başlık ve kayıt
**adını** taşır; değeri **asla**.

## 190.5 — `AuthorizationConfigurationKey` → `[Obsolete]`

İki tipte `[Obsolete]`; mesaj yeni alanı ve `1.0.0`'da kaldırmayı söyler (Açık Soru 4).
Kayıt, çözme, parmak izi, SQL sütunu değişmez.

🚨 **Repodaki ilk `[Obsolete]`** (`grep -rn "\[Obsolete" src` → 0);
`TreatWarningsAsErrors=true`, `WarningsNotAsErrors` boş (`Directory.Build.props:23-24`).
Her iç kullanım `CS0618` → kırmızı. Kullanım (`grep -rn "AuthorizationConfigurationKey" src tests --include=*.cs`):

| Yer | İşlem |
|---|---|
| `GovernanceEndpoints.cs:271`, `:860`, `:889` · `McpTransportFactory.cs:157` · `McpConnection.cs:85` · `SqlApprovalAndMcpStores.cs:191`, `:282` · `ToolApprovalRuleStoreContract.cs:247`, `:276` · `EgressGuardTests.cs:160` | Dar `#pragma warning disable CS0618` + gerekçe |
| `UsageAnalyzerTests.cs:154`, `:207` · `TemplateAgentsFileTests.cs:602` | Kaynak metni: `ShouldCompileCleanly`, paket testi, `:154`'ün `ShouldHaveSingleItem`'ı `CS0618`'i görebilir — **ölçülmeli**; görürse literal yeni alana taşınır (TRC0201 iç sözlüğü tarar, `TraconUsageAnalyzer.cs:208-210`; `:162` mesajı değişir) |
| `TraconCoreJsonContext` (`AuditingMcpServerStore.cs:113`) | Üretilen kodun bastırması **ölçülmeli** |
| `Tracon.Client` | OpenAPI `deprecated` üretirse istemci de `[Obsolete]` — Açık Soru 6 |

Form (`mcp.tsx:352-364`) eski alanı yazar; `tsc` hata saymaz, frontend'de eslint yok (ölçüldü).

## 190.6 — Denetim izi ve okuma yüzeyi

- **Karar 6 bu fazın DoD'sidir.** Maskeleme turu `AuditingMcpServerStore.Serialize`'da
  değeri maskeler (kolda `src/Tracon.Core/Security/HeaderValueMask.cs`; `main`'de yok).
  Başlangıç 4 inmediğini ölçerse bu faz `Headers` değerini düşürür, adı bırakır. DoD
  sınıflandırıcı dışı `X-Tenant: plain-190` ile ölçer.
- Webhook `Describe` (`WebhookEndpoints.cs:394-424`) bugün başlık yazmaz; yeni alanın
  **adları** eklenir.
- `HeaderConfigurationKeys` ad haritasıdır (K-059). Filtre iç nesneye iner ve
  `"Authorization": "Tracon:McpSecrets:…"`'i `"***"` yapar (`AuditSecretFilter.cs:97-114`): Açık Soru 5.
- GET `headerConfigurationKeys`'i **maskelemez**; test sabitler.
- Düzeltilecek metin: `GovernanceEndpoints.cs:227-228`, `McpServerDefinition.cs:44`,
  `:77-79`, `GovernanceContracts.cs:46-49`, `WebhookTypes.cs:129-133` (maskeleme turu
  değiştirdiyse yalnız yeni alan eklenir).

## 190.7 — Tüketici yüzeyi

| Yüzey | İş |
|---|---|
| XML | Dört alan + `<example>` (MCP `X-Api-Key` → `Tracon:McpSecrets:acme:SearchKey`; webhook eşi) |
| `.WithDescription` | Koruma (`null`/`{}`/`endpoint` yan etkisi), kimlik adı reddi |
| `Tracon.Mcp/README.md` | Authentication yeni alanla; önek düzelir |
| `docs-site` | `security.md` tablosu (`:223-226`) + "What is stored in the clear" (`:240`): kalıntı, `endpoint` yan etkisi. `production.md` önek tablosu (`:401-404`) + yeni `## Upgrading:` (`400`'ler, `[Obsolete]`, göç tarifi). `external-agents.md` örneği. `governance.md` § Webhooks (`:657`) |
| `capabilities.md` | Satır 99, 198 |
| İstemciler | `tracon.json` → `Tracon.Client` (NSwag + `scripts/nswag-postprocess-client.py`) → `@tracon/client` (`npm run generate`); `server-types.ts:110`, `:184` `Fix<…>` |
| `CHANGELOG.md` | `Added` · `Deprecated` (`1.0.0`'da kalkar) · `Security` (kimlik adı `400`, harf tekrarı `400`, koruma + `endpoint` yan etkisi) |
| TRC0201 | Açık Soru 7 |

**Göç tarifi (`production.md`).** Düz `X-API-Key`'li eski kayıt tek PUT'la taşınır:
`headers` credential adı **olmadan** (diğer düz başlıklarla) +
`headerConfigurationKeys: {"X-API-Key": "Tracon:McpSecrets:<tenant>:…"}`. Yalnız yeni
alanı göndermek `400` alır: `headers` `null` → saklı `X-API-Key` korunur → tekillik
bozulur. `headers: {}` diğer düz başlıkları da siler; GET onları `"***"` gösterir,
operatör değerleri bilmelidir (Açık Soru 11).

## 190.8 — Açılacak kararlar (numara yok)

K-855 ve sonrası; **kategori etiketi zorunlu**: `*(kategori: <değer>)*`, değer
`public-api` · `güvenlik` (kiracı sınırı dahil) · `kalıcı-veri` (migration dahil) ·
`geri-dönüşü-pahalı`; birden çoksa virgülle.

| Taslak | Etiket |
|---|---|
| Kimlik başlığı yalnız anahtar ADIYLA (`HeaderConfigurationKeys`); K-852 iki yolda; düz kimlik adı `400`; eski satır gider + uyarı (K-059 genişler) | güvenlik/kiracı (ek: kalıcı veri) |
| `AuthorizationConfigurationKey` `[Obsolete]`, `1.0.0`'da kalkar; iki alandan aynı başlık `400`. Faz 188/189 `[Obsolete]`'suz kaldırır; bu üye farklıdır: kalıcı sütun, form yazar, HTTP istemcisi gönderir. 188/189 kuralı yalnız-kod imzalarda kalır | `public-api` |
| PUT MCP/webhook: iki alan eksik/`null` → saklı değer; `{}` temizler | `public-api` |

Yerel tercihler (yardımcı adı, uyarı metni, parmak izi biçimi) "Bu Fazda Verilen Kararlar"a.

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

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// Tracon.Abstractions — McpServerDefinition, WebhookSubscription (sealed record)
public IReadOnlyDictionary<string, string> HeaderConfigurationKeys { get; init; }
    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

// Tracon.AspNetCore — McpServerRequest, WebhookSaveRequest (sealed record)
public IReadOnlyDictionary<string, string>? HeaderConfigurationKeys { get; init; }  // null → saklı değer

// McpServerDefinition, McpServerRequest — biçim: Açık Soru 4
[Obsolete("Use HeaderConfigurationKeys[\"Authorization\"]. Removed in 1.0.0.")]
public string? AuthorizationConfigurationKey { get; init; }

// Tracon.Testing.Contracts.Xunit — McpServerStoreContract, WebhookStoreContract
[Fact] public async Task Header_configuration_keys_are_preserved();
```

Her tip: **bugün eklemek/değiştirmek ucuz (pre-1.0, `Shipped` boş) / GA'dan sonra kırıcı.**

| Tip | Değişiklik | GA'dan sonra kırıcı olan |
|---|---|---|
| `McpServerDefinition` | alan + `[Obsolete]` | kaldırma |
| `WebhookSubscription` | alan | kaldırma |
| `McpServerRequest` | alan + `[Obsolete]` + `null` anlamı | HTTP anlam değişikliği |
| `WebhookSaveRequest` | alan + `null` anlamı | aynı |
| iki sözleşme sınıfı | yeni test | üçüncü taraf store'da yeni kırmızı |

### HTTP `endpoint`'leri

Yeni uç yok.

| Metot | Yol | Rol | Ne değişir |
|---|---|---|---|
| `PUT` | `/api/mcp-servers/{name}` | Admin + `AgentsAdmin` | Yeni alan; kimlik adı, çakışma, tekrar → `400`; `null` korur |
| `PUT` | `/api/webhooks/{name}` | Admin + `PlatformAdmin` | Aynı + ayrılmış ad `400` |
| `GET` | `/api/mcp-servers` | Reader + `AgentsRead` | Anahtar adlarını döner (maskesiz) |
| `GET` | `/api/webhooks`, `/api/webhooks/{name}` | Admin + `PlatformRead` | Aynı |

### Arayüz payı

Form değişmez: 0 KB. Açık Soru 3 kabul edilirse gzip pay `npm run build` sonrası
**ölçülmeli** (`wwwroot/assets/` bugün yalnız `.br`). Yeni metin çıkarsa `en.ts` + `tr.ts` (K-228).

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/  Mcp/McpServerDefinition.cs, Webhooks/WebhookTypes.cs, PublicAPI.Unshipped.txt
src/Tracon.AspNetCore/    Contracts/GovernanceContracts.cs, Endpoints/{Governance,Webhook}Endpoints.cs, PublicAPI.Unshipped.txt
src/Tracon.Core/          Egress/CredentialHeaderNames.cs (yeni), Audit/AuditSecretFilter.cs,
                          Audit/AuditingMcpServerStore.cs (maskeleme yapmadıysa), Webhooks/WebhookDeliveryJobHandler.cs
src/Tracon.Mcp/           Internal/McpHeaderBuilder.cs (yeni), Internal/McpTransportFactory.cs,
                          Internal/McpConnection.cs, McpOAuthAuthorizationCoordinator.cs, README.md
src/Tracon.Sql.Shared/    Internal/SqlQueriesBase.cs, Stores/{SqlApprovalAndMcpStores,SqlWebhookStore}.cs
src/Tracon.PostgreSql/    Internal/PostgresQueries.cs, Migrations/NNNN_header_configuration_keys.sql
src/Tracon.SqlServer/     Internal/SqlServerQueries.cs (Upsert + iki OUTPUT listesi), Migrations/NNNN_…sql
src/Tracon.Sqlite/        Internal/SqliteQueries.cs, Migrations/NNNN_…sql
src/Tracon.Testing.Contracts.Xunit/  Contracts/{ToolApprovalRuleStoreContract,WebhookStoreContract}.cs, PublicAPI.Unshipped.txt
üretilir: src/Tracon.Client/Generated/TraconApiClient.g.cs, packages/tracon-client/src/schema.ts, docs/openapi/tracon.json
src/Tracon.UI/frontend/src/lib/server-types.ts
tests/  Tracon.Core.UnitTests/…, Tracon.Mcp.UnitTests/…, Tracon.AspNetCore.FunctionalTests/…,
        Tracon.Sql.Shared.UnitTests/MigrationParityTests.cs
docs-site/src/content/docs/{getting-started/security.md, guides/production.md, guides/external-agents.md, concepts/governance.md, capabilities.md}
CHANGELOG.md
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

Test adları önerisidir: `CHSave` = `CredentialHeaderSaveTests`, `Keep` = `HeaderPreservationTests`, `Build` = `McpHeaderBuilderTests`, `WhDel` = `WebhookCredentialHeaderDeliveryTests`.

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| Sınıflandırıcı kimlik adını kaçırır / `X-Max-Tokens`'ı yakalar | Birim | `CredentialHeaderNamesTests` |
| Sınıflandırıcı ile filtre ayrı liste okur | Birim (mimari) | aynı sınıf |
| Düz `X-API-Key`/`Cookie`/`Ocp-Apim-Subscription-Key` `200` alır | Fonksiyonel (HTTP) | `CHSave` (MCP + webhook) |
| `400` değeri yankılar | Fonksiyonel | `CHSave` |
| **Boş girdi:** anahtar adı `""` geçer | Fonksiyonel | `CHSave` |
| **Bozuk girdi:** adda boşluk, `:`, CR/LF | Birim + Fonksiyonel | `CHSave` |
| Harf tekrarı: webhook SQL `500`, MCP bağlanamaz | Fonksiyonel + Sözleşme (SQL) | `CHSave`, `WebhookStoreContract` |
| **Başka kiracı:** yabancı ad kaydetmede geçer | Fonksiyonel (kiracı) | `TenantScopedConfigurationKeyTests` |
| **Başka kiracı:** store'daki yabancı ad çözülür | Fonksiyonel (depo → bağlantı/teslim) | `CredentialHeaderResolveGuardTests` (MCP bağlanmaz, webhook `Dropped`) |
| `Authorization` iki alandan / OAuth + yeni alan geçer | Fonksiyonel | `CHSave` |
| Çakışma yalnız istekte ölçülür | Fonksiyonel | `Keep` |
| Form gövdesiyle (`mcp.tsx:36-48`) PUT saklı başlığı siler | Fonksiyonel (HTTP) + Manuel 👤 | `Keep` |
| `{}` temizlemez | Fonksiyonel | `Keep` |
| `endpoint` değişen PUT başlığı yeni host'a gönderir, belgelenmez | Fonksiyonel (koruma → teslim) + doküman | `Keep`; `.WithDescription`, `security.md` |
| Göç: yalnız yeni alanlı PUT'un `400` metni çözümü söylemez | Fonksiyonel | `Keep` |
| **Eşzamanlılık:** koruma okuması ile yazma arasındaki PUT kaybolur | — (kabul; bugün de son yazan kazanır) | yok — Riskler |
| Sütun kaybolur / sıra kayar | Sözleşme (bellek içi + üç SQL) | round-trip: `SaveAsync` dönüşü + `GetAsync` |
| SQL Server `OUTPUT` sütunu atlar | Birim + Sözleşme (SQL Server) | `MigrationParityTests` + round-trip |
| Lehçe metinleri ayrışır | Birim | `Tracon.Sql.Shared.UnitTests` |
| Migration öncesi satır okunamaz | Entegrasyon (üç SQL) | migration testi (`{}`) |
| Çözülen başlık MCP'ye gitmez | Fonksiyonel (loopback, `AllowPrivateNetworkTargets`) | `McpCredentialHeaderDeliveryTests` |
| OAuth yolu çözülen başlığı kaçırır (kopya kalır) | Birim + mimari tarama | `Build`; `src/Tracon.Mcp`'de `server.Headers` yalnız yardımcıda ve `McpConnection.ComputeFingerprint`'te |
| Çözülen başlık webhook alıcısına gitmez | Fonksiyonel (teslim) | `WhDel` |
| **Alt sistem hatası:** değer boş, `configuration` `null`, CR/LF | Birim (MCP) + Fonksiyonel (webhook) | `Build`, `WhDel` |
| Eski düz kimlik başlığı (OAuth'ta `Authorization` dahil) gitmez / uyarı değeri loglar | Birim (sahte logger) | `Build`, `WhDel` |
| Parmak izi değer içerir / ad değişince yenilenmez | Birim | `Build` |
| GET yeni alanı maskeler | Fonksiyonel | `CHSave` |
| Denetim düz değer yazar (`X-Tenant: plain-190`); anahtar adları kaybolur | Fonksiyonel (HTTP → `/api/audit`) + Birim (filtre) | `CredentialHeaderAuditTests`, `AuditSecretFilterTests` |
| Yeni `Headers`'lı tip kuralı atlar | Birim (mimari, `SecretBearingTypeTests` emsali) | `HeaderBearingTypeTests` |
| `CS0618` derlemeyi kırar | Paket + derleme kapısı | `kapi.py`, `TemplateAgentsFileTests`, `UsageAnalyzerTests` |
| TRC0201 yeni alandaki adı `secret` sayar | Birim (analyzer) | `UsageAnalyzerTests` |
| **İptal:** önceki-kayıt okuması `CancellationToken` taşımaz | Derleme (CA2016 — **ölçülmeli**) + `faz-denetim` | — |
| Eski alan yolu kırılır | Fonksiyonel | mevcut `GovernanceEndpointTests`, `EgressGuardTests`, `TenantScopedConfigurationKeyTests` |

Sözleşme testi `src/Tracon.Testing.Contracts.Xunit/Contracts/`'tadır; bellek içi + üç
SQL'de koşar. Test literali `secret` desenine uymaz (`.agents/ortak/kapilar.md:62-67`).

---

## Manuel Kabul Case'leri

> Kapanışta iki alan dosyasına eklenir; numara kapanışta. `$APU`/`$APB`:
> `18-MCP-VE-A2A.md:106-110`. "Kurulum" = aşağıdaki komut 1-2. 👤 = insan gerekir.

| # | Ön koşul | Adımlar | Beklenen |
|---|---|---|---|
| 1 | Temiz | Komut 3 | `400`; `detail` yeni alanı adlandırır; değer yok |
| 2 | Temiz | Komut 3, `headers` `{"Cookie":"a=b"}`, sonra `{"Ocp-Apim-Subscription-Key":"x"}` | İkisi `400` |
| 3 | Kurulum | Komut 4 | Yakalayıcı `X-API-Key: dogrulama-degeri`; GET adı gösterir, değeri değil |
| 4 | Çok kiracılık, çağıran `acme` | yeni alan `{"X-API-Key":"Tracon:McpSecrets:globex:Key"}` | `400`; `headerConfigurationKeys[X-API-Key]` ve `Tracon:McpSecrets:acme:` adlanır |
| 5 | Temiz | Eski alan + yeni `Authorization`; ayrı kayıtta `oauthEnabled` + yeni `Authorization` | İkisi `400` |
| 6 | Case 3 | 👤 `/tracon/mcp` formundan `m1`'i kaydet | `200`; ad hâlâ var; refresh sonrası değer gider |
| 7 | SQLite; `sqlite3` ile düz `X-API-Key`'li satır | Refresh | Başlık gider; uyarı adı taşır, değeri taşımaz |
| 8 | Kurulum | Webhook PUT `headers:{"Authorization":"Bearer x"}`; sonra komut 5 | İlki `400`; ikincisi değeri iletir |
| 9 | Temiz | Webhook yeni alan `{"X-Tracon-Signature":"Tracon:WebhookSecrets:DemoKey"}` | `400` (ayrılmış) |
| 10 | Kurulum + komut 6'nın PUT'ları (`X-Tenant: plain-190`) | `GET /api/audit/mcp:m1`, `/api/audit/webhook:w1` | `plain-190` yok; MCP'de `X-Tenant` adı var; anahtar adları Açık Soru 5'e göre |
| 11 | MT-SEC-091 | Yeniden yaz: `headers.Authorization` artık `400`; filtre kanıtı agent metadata veya eski satırla | Maskeleme turu değiştirdiyse yalnız `400` adımı |
| 12 | Webhook satırı kiracı dışı anahtar adıyla | Test olayı | `Dropped`; `error` alanı ve öneki adlandırır |
| 13 | `sqlite3` ile `m2`: `headers` `{"X-API-Key":"eski","X-Team":"t1"}`, endpoint yakalayıcı | (a) PUT endpoint + yeni alan `X-API-Key`; (b) aynı + `headers:{"X-Team":"t1"}`; refresh | (a) `400`, "remove 'X-API-Key' from headers"; (b) `200`, yakalayıcı `dogrulama-degeri`, uyarı yok |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> sorular plan yazılmadan **önce** soruldu (Bağlayıcı kararlar).

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Korunan eski `Headers` yeniden yargılanır mı? | A: Yalnız istekteki · B: Birleşik | **A** — B'de form eski satırı kaydedemez |
| 2 | Webhook yeni alanı `MaxExtraHeaders`'a sayılır mı? | A: Evet, kaydetmede `400` · B: Teslimde kes | **A** — B kimliği sessizce düşürür |
| 3 | Liste "Yetki" sütunu (`mcp.tsx:517-530`) yeni alanı gösterir mi? | A: İki alandan `Authorization` adı · B: Dokunma | **A** — B'de server "yok" görünür |
| 4 | `[Obsolete]` biçimi | A: `DiagnosticId` + `UrlFormat` · B: Yalnız mesaj | **A** — tek uyarı bastırılır; `TRC` aralığıyla çakışma **ölçülmeli** |
| 5 | Denetimde anahtar adları redakte edilir mi? | A: `…configurationkeys` alt ağacı ad haritası · B: Filtre aynen | **A** — B tanıyı siler (`secenek-baglama-ve-gizlilik.md:23`) |
| 6 | `[Obsolete]` OpenAPI `deprecated` üretir mi? | Ölç. Üretmezse A: dönüştürücü · B: XML cümlesi | **B** — yeni dönüştürücü yok |
| 7 | TRC0201 sözlük literali için yeni alanı adlandırsın mı? | A: `DescribeSecretTarget` (`TraconUsageAnalyzer.cs:586-602`) · B: Dokunma | **A** — bugünkü tavsiye uygulanamaz |
| 8 | Webhook anahtar değeri boşsa | A: Başlık gitmez, uyarı · B: Teslim düşer | **A** — `ResolveSecret` (`WebhookDeliveryJobHandler.cs:381-406`) gibi |
| 9 | GA kaldırma (veri taşıma + form) nerede izlenir? | A: ADAYLAR kalemi · B: Bu faz | **A** — B form kaydını çakıştırır |
| 10 | Host değişirken korunan kimlik başlığı için uyarı logu? | A: Evet · B: Yalnız doküman | **A** — ucuz; karar 5 değişmez |
| 11 | Göçte `headers` `null` iken yeni alandaki ad korunan düz addan otomatik düşsün mü? | A: Düşer · B: `400` + tarif | **B** — A örtük silme ekler |

---

## Bitiş Ölçütleri (DoD)

- [ ] Komut 3 → `400`; `detail` `headerConfigurationKeys` içerir, `v` içermez; webhook eşi aynı
- [ ] Komut 4 → yakalayıcı değeri alır; GET adı döner, değeri dönmez
- [ ] Form gövdeli PUT iki alanı korur, `{}` temizler (`HeaderPreservationTests` + case 6); `endpoint` yan etkisi `.WithDescription` ve `security.md`'de
- [ ] Karar 6: komut 6 → `mcp:m1` ve `webhook:w1` denetiminde `plain-190` sayısı `0`; `mcp:m1`'de `X-Tenant` var
- [ ] Göç tarifi `production.md`'de; case 13 geçti
- [ ] Kiracı dışı anahtar adı: MCP bağlanmaz, webhook `Dropped` (`CredentialHeaderResolveGuardTests`)
- [ ] Round-trip bellek içi + üç SQL'de yeşil, `SaveAsync` dönüşünü doğrular; `MigrationParityTests` iki tabloyu kapsar
- [ ] `grep -rn "server\.Headers" src/Tracon.Mcp` → yalnız yardımcı ve `McpConnection.ComputeFingerprint`
- [ ] `HeaderBearingTypeTests` yeşil; sınıflandırıcı ve filtre tek listeyi okur
- [ ] `grep -rn "CS0618" src tests` sayısı kapanışta; her `pragma` gerekçeli
- [ ] `AdditionalHeaders` + OAuth ölçümü devir notunda
- [ ] `CHANGELOG.md` `Added`/`Deprecated`/`Security`; `python3 scripts/kapi.py yayin --kuru` yeşil
- [ ] Dört doğrulama kapısı sıfır uyarı verir: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı: komut 1-6 ve bir agent `run`'ının `run.completed` webhook'u yakalayıcıya `X-API-Key` değeriyle ulaştı
- [ ] `secret` taraması boş döndü: `python3 scripts/kapi.py tarama`
- [ ] Manuel kabul case'leri `docs/manuel-test/18-MCP-VE-A2A.md` ve `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `npm run check:links` temiz
- [ ] Arayüze dokunulduysa (Açık Soru 3) `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü

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

## Riskler

| Risk | Önlem |
|------|-------|
| `-key` yanlış pozitif (`X-Idempotency-Key`) | Metin yeni alanı söyler; değer yapılandırmaya taşınır |
| Sınıflandırıcı dışı adlar DB'de düz | Okuma maskeli, denetim değersiz; `security.md` "What is stored in the clear" |
| İlk `[Obsolete]` `CS0618` dalgası | 190.5 envanteri; adım 7 erken kapı; `pragma` gerekçeli ve sayılı |
| Koruma kayıp güncelleme penceresi | Bugün de "son yazan kazanır"; kilit yok; `.WithDescription` söyler |
| Form `endpoint` değişimi kimlik başlığını yeni host'a gönderir | K-852 adı kiracıya bağlar; doküman; Açık Soru 10 |
| Göç diğer düz değerleri yeniden ister (GET `"***"`) | `production.md` tarifi; `400` metni; Açık Soru 11 |
| Eski satır uyarısı her bağlantıda tekrarlar | Kapanışta sıklık ölçülür; gerekirse tek seferlik |
| Değer değişince MCP bağlantısı yenilenmez (parmak izi adları taşır) | Eski alanla bugün aynı; `external-agents.md` refresh'i söyler |
| SDK'nın `AdditionalHeaders` + OAuth davranışı bilinmiyor | Adım 6'da önce `maf-api-kesfi` |
| GA'da form yeni alana geçmek zorunda | Açık Soru 9 → ADAYLAR; devir notunda 🚨 |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: arşivdeki her
> `- [ ]` satırı `tamamlanmis_faz_isaretsiz_kutular()` kapısında ayrıca hata
> sayılır ve bulgunun kaynağı bulanıklaşır.
>
> `dokuman-bakim.py --denetle` 14. kapısı (`surec_olcumu_bulgulari`) bu tabloyu
> **eşik 167**'den itibaren her kapanmış fazda arar. Boş bir değer hücresi
> kırmızıdır; `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir
> **karar** arar. Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez
> (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
