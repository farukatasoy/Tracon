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
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../../../.agents/ortak/test-seviyeleri.md).

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

## Gerçekleşen Public API
```csharp
// Tracon.Abstractions
public sealed record McpServerDefinition
{
    [Obsolete("Use HeaderConfigurationKeys[\"Authorization\"] instead. AuthorizationConfigurationKey is removed in 1.0.0.",
        UrlFormat = "https://tracon.dev/guides/production/#upgrading-credential-headers")]
    public string? AuthorizationConfigurationKey { get; init; }
    public IReadOnlyDictionary<string, string> HeaderConfigurationKeys { get; init; }   // varsayılan: OrdinalIgnoreCase, boş
}
public sealed record WebhookSubscription
{
    public IReadOnlyDictionary<string, string> HeaderConfigurationKeys { get; init; }   // aynı
}

// Tracon.AspNetCore
public sealed record McpServerRequest
{
    [Obsolete(/* aynı mesaj ve UrlFormat */)] public string? AuthorizationConfigurationKey { get; init; }
    public IReadOnlyDictionary<string, string>? HeaderConfigurationKeys { get; init; }  // null → saklı değer
}
public sealed record WebhookSaveRequest
{
    public IReadOnlyDictionary<string, string>? HeaderConfigurationKeys { get; init; }  // null → saklı değer
}

// Tracon.Testing.Contracts.Xunit
McpServerStoreContract.Header_configuration_keys_are_preserved()
WebhookStoreContract.Header_configuration_keys_are_preserved()
WebhookStoreContract.Header_names_that_differ_only_in_case_do_not_fail_the_save()
```

HTTP: yeni uç yok. `PUT /api/mcp-servers/{name}` ve `PUT /api/webhooks/{name}`'in
anlamı değişti (`null` koruma, yeni `400`'ler); iki `GET` yeni alanı maskesiz döner.
`Headers` (her iki istek tipinde) artık "yok/`null` → saklı değer" anlamındadır.

## Dosya Listesi (gerçekleşen)
```
Yeni:
src/Tracon.Abstractions/ObsoleteMessages.cs
src/Tracon.Core/Egress/CredentialHeaderNames.cs
src/Tracon.AspNetCore/Endpoints/CredentialHeaderSaveRules.cs
src/Tracon.Mcp/Internal/McpHeaderBuilder.cs
src/Tracon.PostgreSql/Migrations/0054_header_configuration_keys.sql
src/Tracon.SqlServer/Migrations/0042_header_configuration_keys.sql
src/Tracon.Sqlite/Migrations/0041_header_configuration_keys.sql
tests/Tracon.AspNetCore.FunctionalTests/{CredentialHeaderSaveTests,HeaderPreservationTests,
    CredentialHeaderDeliveryTests,CredentialHeaderAuditTests,HeaderBearingTypeTests}.cs
tests/Tracon.Core.UnitTests/Egress/CredentialHeaderNamesTests.cs
tests/Tracon.Core.UnitTests/Architecture/ObsoleteMessagesTests.cs
tests/Tracon.Mcp.UnitTests/McpHeaderBuilderTests.cs
tests/Tracon.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/HeaderConfigurationKeysMigrationTests.cs

Değişen (kod):
src/Tracon.Abstractions/{Mcp/McpServerDefinition,Webhooks/WebhookTypes}.cs, PublicAPI.Unshipped.txt
src/Tracon.AspNetCore/{Contracts/GovernanceContracts,Endpoints/GovernanceEndpoints,Endpoints/WebhookEndpoints}.cs, PublicAPI.Unshipped.txt
src/Tracon.Core/{Audit/AuditSecretFilter,Webhooks/WebhookDeliveryJobHandler}.cs
src/Tracon.Mcp/{Internal/McpConnection,Internal/McpTransportFactory,McpOAuthAuthorizationCoordinator}.cs, README.md
src/Tracon.Sql.Shared/{Internal/SqlQueriesBase,Stores/SqlApprovalAndMcpStores,Stores/SqlWebhookStore}.cs
src/Tracon.{PostgreSql,SqlServer,Sqlite}/Internal/*Queries.cs
src/Tracon.Testing.Contracts.Xunit/Contracts/{ToolApprovalRuleStoreContract,WebhookStoreContract}.cs, PublicAPI.Unshipped.txt
src/Tracon.Generators/{TraconUsageAnalyzer,UsageDiagnostics}.cs
src/Tracon.UI/frontend/src/{lib/server-types.ts,screens/mcp.tsx}
Üretilen: docs/openapi/tracon.json, src/Tracon.Client/Generated/TraconApiClient.g.cs, packages/tracon-client/src/schema.ts
Testler (güncellenen): GovernanceEndpointTests, WebhookEndpointTests, EgressGuardTests, AuditSecretFilterTests,
    UsageAnalyzerTests, MigrationParityTests, sql-text-baseline.{postgres,sqlserver,sqlite}.txt
Doküman: CHANGELOG.md; docs-site: getting-started/security.md, guides/production.md,
    guides/external-agents.md, concepts/governance.md, capabilities.md
```

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
