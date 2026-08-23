# Faz 65 — Kiracı Sağlayıcı Anahtarları (BYOK)

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-40**, **F-119**
> **Önkoşul:** [Faz 41](41-KIRACI-YALITIMININ-ZORLANMASI.md) — kiracı yalıtımının zemini · [Faz 53](53-KIRACI-API-ANAHTARLARI.md) — kiracı yönetim yüzeyi ve kapsam modeli · [Faz 8](08-SAGLAYICI-GENISLEMESI.md) — sağlayıcı katmanı
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.OpenAI`, `AgentPrism.Anthropic`, `AgentPrism.Google`, `AgentPrism.Azure`, `AgentPrism.Sql.Shared`, `AgentPrism.PostgreSql`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (yeni `tenant_provider_bindings` tablosu). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor ve bir arayüz imzası genişliyor** — `IModelProvider.CreateChatClient`. 🚨 Arayüze metot/parametre eklemek yayından **sonra** en pahalı değişikliktir; `PublicAPI.Shipped.txt` bugün **boş** olduğu için **şimdi bedava**
> **Site etkisi:** `guides/model-providers.md`, `concepts/governance.md`, `reference/configuration.md`
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Sağlayıcı anahtarı bugün **globaldir**. Bütün kiracılar aynı anahtarı, aynı kotayı ve aynı **faturayı** paylaşır. Çok kiracılı bir SaaS için bu kabul edilemez: bir kiracının aşırı kullanımı diğerinin hizmetini durdurur ve maliyet kiracıya yansıtılamaz. - **F-40** — Kiracı başına sağlayıcı anahtarı.

## Bitiş Ölçütleri (DoD)

- [x] Bağlama yokken bugünkü davranış **birebir** korunur — sync `CreateChatClient` yolu değişmedi; `ModelProviderRegistryTenantCredentialTests.Tenant_with_no_binding_falls_back_to_the_global_credential` ve `The_sync_overload_never_resolves_a_tenant_credential_...`
- [x] Kiracının bağlaması varken çağrı **onun** anahtarıyla gider; iki kiracı iki farklı anahtar kullanır (sözleşme testi, dört koşum) — `TenantProviderBindingStoreContract` bellek içi + 3 SQL sağlayıcısında (1050+ test dahil toplam koşum); gerçek örnek uygulamada `acme` kiracısı için ayrı bir `AgentPrism:ProviderKeys:Acme:OpenAI` bağlaması doğrulandı (Adım 2)
- [x] Anahtar **değeri** hiçbir yerde saklanmaz: veritabanı, log, `span`, API yanıtı — dördü de testle kapatıldı — `TenantProviderEndpointTests.Response_never_carries_a_credential_value`, `*ModelProviderCredentialTests.The_key_value_never_appears_in_client_metadata` (OpenAI), doğrudan `psql` ile veritabanı satırı okundu (Adım 2)
- [x] Önek dışındaki bir yapılandırma adı hem kayıtta hem çözümlemede reddedilir — `TenantProviderCredentialResolverTests` (kayıt + çözümleme iki ayrı test), `TenantProviderEndpointTests.Name_outside_the_allowed_prefix_is_rejected`
- [x] Ad var ama değer yoksa çağrı global anahtara **düşmez**; hata anlaşılırdır — `ModelProviderRegistryTenantCredentialTests.A_binding_that_exists_but_resolves_to_no_value_does_not_fall_back_silently`
- [x] Dört sağlayıcı paketi de credential'ı uygular; dördü de test edilir — `{OpenAI,Anthropic,Google,Azure}ModelProviderCredentialTests`
- [~] Bağlama yazımı denetim izine **mutasyondan sonra** yazılır (planın "önce" ifadesinden sapma) — `AuditRecorder.WriteAsync` (hata yutan, standart) deseni kullanıldı; K-089/K-370'in "önce + engelleyici" deseni yalnız GERİ ALINAMAZ eylemler içindir (script çalıştırma, onay kararı), bir yapılandırma yazımı bu sınıfa girmiyor — `ApiKeyEndpoints`/`RetentionEndpoints` ile aynı, kurulu desen
- [x] Arayüzde değer girme alanı **yoktur** — `tenant-provider-panel.tsx`; form yalnız sağlayıcı, yapılandırma anahtarı ADI, uç adresi alır
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format`, hepsi 0 uyarı (Adım 1)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — Adım 2, gerçek PostgreSQL'e karşı: önek reddi `400`, bağlama `resolved:false`→veritabanı satırı, egress reddi `400`→izinli `201`, denetim izi zincirlenmiş hash ile doğrulandı
- [x] `secret` taraması boş döndü — yalnız önceden var olan doküman örnekleri eşleşti, yeni kod sıfır eşleşme
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi (MT-SEC-109..119); otomatikleştirilebilenler Adım 2'de gerçek koşumla doğrulandı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"
- [x] `docs-site/` güncellendi (`guides/model-providers.md`, `concepts/governance.md`, `reference/configuration.md`); `npm run build` + `check-links.mjs` temiz — 947 sayfa, 117830 iç bağlantı, hiçbiri kırık (denetim sonrası tekrar koşuldu)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — 165,4 KB → 169,4 KB gzip (+4,0 KB), bütçe 250 KB
- [x] Egress politikası tanımsız kiracıda hiçbir davranış değişmez — `Egress_policy_allows_a_listed_provider` ve varsayılan `null` testleri
- [x] İzinsiz sağlayıcıya işaret eden agent tanımı **kaydetme anında** reddedilir — `AgentDefinitionCompilerEgressTests`; gerçek koşumda `POST /api/agents` üzerinden doğrulandı (Adım 2) — beklenenden de erken, `run` beklemeden
- [x] İzinsiz sağlayıcı için anahtar kaydı `400` döner — `Binding_a_provider_the_egress_policy_does_not_allow_is_rejected`
- [x] **(denetim sonrası eklendi, K-470)** Bir fallback tetiklendiğinde de kiracının kendi credential'ı ve egress kısıtı uygulanır — `A_triggered_fallback_resolves_its_own_tenant_credential_through_the_async_entry_point`, `Egress_policy_rejects_a_fallback_provider_not_in_the_allowed_list`
- [x] **(denetim sonrası eklendi, K-471)** Kiracı credential'ı gömülü bir agent asla önbelleğe girmez; bağlama rotate/silinse dahi önbellek eski credential'ı hiç sızdırmaz — `DefinitionStoreAgentSourceTenantCredentialTests` (uçtan uca, gerçek zincir), `HasTenantProviderOverride_*` (izole)
- [x] **(denetim sonrası eklendi)** Eşzamanlı iki `PUT` aynı bağlamayı bozmaz — gerçek PostgreSQL'e karşı `TenantProviderBindingConcurrencyTests.Racing_upserts_to_the_same_binding_leave_exactly_one_consistent_row` (20 eşzamanlı yazıcı)

### Doğrulama komutları

```bash
# Deger yalniz user-secrets'ta yasar
dotnet user-secrets set "AgentPrism:ProviderKeys:Acme:OpenAI" "sk-..." \
  --project samples/AgentPrism.Api

# Baglama yaz - yalniz AD
curl -s -X PUT http://localhost:5081/agentprism/api/tenants/acme/providers/openai \
  -H 'Content-Type: application/json' \
  -d '{"apiKeyConfigurationName":"AgentPrism:ProviderKeys:Acme:OpenAI"}'

# Deger DONMEZ, yalniz cozumleme durumu doner
curl -s http://localhost:5081/agentprism/api/tenants/acme/providers

# Onek disindaki ad reddedilir
curl -s -o /dev/null -w '%{http_code}\n' -X PUT \
  http://localhost:5081/agentprism/api/tenants/acme/providers/openai \
  -H 'Content-Type: application/json' \
  -d '{"apiKeyConfigurationName":"ConnectionStrings:Default"}'
```

---

## Plandan Sapmalar

- **🚨 Plan `IModelProviderRegistry.CreateChatClient`in imzasının sabit kaldığını
  varsayıyordu; ölçüldü ki bu doğru ama YETERSİZDİ.** Kiracı `store`'ları
  (`ITenantProviderBindingStore`, `ITenantEgressPolicyStore`) zaten async
  (`ValueTask`); ama `AgentDefinitionCompiler.Compile`/`CompiledAgentCache.GetOrAdd`
  TAMAMEN senkron. Çözüm K-466'da: senkron üçlü DEĞİŞTİRİLMEDİ, yanına PARALEL
  `CreateChatClientAsync`/`CompileAsync`/`GetOrAddAsync` eklendi. Gerçek `run`
  yolu (`DefinitionStoreAgentSource`/`CodeAgentSource`), `AgentDefinitionValidator`
  ve `RunReplayService` async yola taşındı; sync yol (credential her zaman
  `null`) yalnız geriye dönük uyumluluk için kalır.
- **Egress uçları planın öngörmediği bir 5. metotla büyüdü: `DELETE
  /api/tenants/{tenantId}/egress`.** Plan yalnız GET+PUT öngörüyordu (65.6
  tablosu). Gerçek örnek uygulama koşumunda (Adım 2) ölçüldü: PUT ile boş dizi
  yazmak "hiçbir sağlayıcıya izin yok" demektir, "kısıtsız" DEĞİLDİR — bir
  yöneticinin bir politikayı tamamen GERİ ALMASININ hiçbir HTTP yolu yoktu.
  `ITenantEgressPolicyStore.DeleteAsync` zaten vardı ve test edilmişti; yalnız
  HTTP yüzeyi eksikti.
- **`AgentDefinitionValidator.CheckModel` özel adı `CheckModelAsync`'e taşındı**
  (async yola geçişin bir parçası); dışa dönük davranış (mesaj kodları, sıra)
  değişmedi.
- **`AuditSecretFilter` alan ADINA bakan blanket-redaction'ı** `apiKeyConfigurationName`
  alanını `***` yaptı (değer güvenli olsa da). `TenantProviderEndpoints.DescribeForAudit`
  bu yüzden denetim payload'ında alanı `configKeyName` diye yazar — HTTP
  sözleşmesindeki alan adı (`apiKeyConfigurationName`) değişmedi, yalnız denetim
  özeti farklı adlandırıldı. Ayrıntı: `docs/hafiza/cekirdek-calistirma.md`.
- **Google GenAI SDK'sinin `ChatClientMetadata.ProviderUri`'si özel `Endpoint`'i
  yansıtmadığı ölçüldü** (diğer üç sağlayıcı doğru yansıtır); `GoogleModelProviderCredentialTests`
  bu yüzden uç nokta yerine yalnız istemcinin üretildiğini doğrular — plan bu
  farkı öngörmüyordu.
- **E2E (Playwright) doğrulaması ilk denemede yapılamadı**, ortamda tarayıcı
  ikilikleri kurulu değildi ve `npx playwright install chromium` ağ üzerinden
  zaman aşımına uğradı. `faz-denetim` kapanışında (Adım 1) tarayıcılar kurulu
  hâlde bulundu; tam `AgentPrism.Ui.E2ETests` seti koşuldu — **55/55 geçti**
  (bu fazın kendi paneli için ayrı bir case eklenmedi; mevcut `settings.tsx`
  kapsamı yeterliydi, bkz. Adım 7 site senkronu).
- **🟢 Bağımsız bir gözlem, faz kapsamı DIŞINDA:** tam çözüm koşumunda
  `Runs_button_on_session_page_navigates_to_filtered_list` (Faz 65'in
  dokunmadığı bir dosya, `UiTests.cs`, oturum/`run` ekranları) izolasyonda
  3/3 geçerken paralel yükte 3 denemeden 2'sinde bir zamanlama yarışıyla
  düştü (`tbody tr` sayısı düğme etiketiyle eşleşmeden okunuyor). Bu fazın
  BYOK/egress kodu bu ekrana hiç dokunmuyor; kök neden tarayıcı/`Docker`
  kaynak çekişmesidir, kod kusuru değil. `docs/ADAYLAR.md`'ye devredildi.

## Bu Fazda Verilen Kararlar

Bkz. `docs/KARARLAR.md`: **K-466** (senkron/async ikili yol), **K-467**
(egress+kimlik bilgisi çözümlemesi tek noktada, sıra), **K-468** (paylaşılan
`ProviderCredentialClientCache<TFactory>`), **K-469** (kiracı rotadan alınır,
ambiyans DEĞİL — `GovernanceEndpoints` deseni), **K-470** (bağımsız denetim
🔴 #1 — fallback zinciri kiracı/egress'ten habersizdi, düzeltildi), **K-471**
(bağımsız denetim 🔴 #2 — `CompiledAgentCache` kiracı credential'ını asla
saklamaz, bağlama varken tamamen atlar).

## Denetim Bulguları

> `faz-denetim` (taze bağlamlı bağımsız denetçi) koştu; bulgular ve kapanış
> sonuçları aşağıda.

### 🔴 Kapanmadan faz bitmeyecek olanlar — ikisi de kapandı

| # | Bulgu | Kanıt | Sonuç |
|---|---|---|---|
| 1 | Bir fallback zinciri (`ModelBinding.Fallbacks`) tetiklendiğinde `FallbackChatClient` kiracı/egress'ten habersiz senkron yolu (`CreateChatClient` metot grubu) çağırıyordu — fallback sağlayıcısı kiracının kendi credential'ını asla görmüyordu ve egress kısıtı fallback'e uygulanmıyordu | `FallbackChatClient.cs` (eski) `Func<ModelBinding, IChatClient> _buildClient` | **Düzeltildi (K-470).** `_buildClient` imzası `Func<ModelBinding, CancellationToken, ValueTask<IChatClient>>` oldu; `ResolveFallbackClientAsync` artık `ModelProviderRegistry.CreateChatClientAsync`'i çağırıyor. Kanıt: `A_triggered_fallback_resolves_its_own_tenant_credential_through_the_async_entry_point`, `Egress_policy_rejects_a_fallback_provider_not_in_the_allowed_list` (`ModelProviderRegistryTenantCredentialTests.cs`) + 16 önceden var olan `FallbackChatClientTests` hepsi geçiyor |
| 2 | `CompiledAgentCache` içine, kiracı credential'ı gömülü bir `AIAgent` **credential'dan habersiz bir anahtarla** yazılıyordu — bağlama sonradan rotate/silinse bile önbellekteki agent eski credential'ı sonsuza dek kullanmaya devam ederdi | `DefinitionStoreAgentSource.ResolveAsync`/`CodeAgentSource.ResolveAsync` (eski) — her ikisi de koşulsuz `_cache.GetOrAddAsync` çağırıyordu | **Düzeltildi (K-471).** `IModelProviderRegistry.HasTenantProviderOverrideAsync` (primary + her fallback için bağlama var mı) eklendi; `AgentDefinitionCompiler.UsesTenantProviderOverrideAsync` bunu deleger eder; her üç kaynak (`DefinitionStoreAgentSource.ResolveAsync`/`ResolveVersionAsync`, `CodeAgentSource.ResolveAsync`) bağlama varken önbelleği tamamen atlar. Kanıt: 3 izole birim testi (`HasTenantProviderOverride_*`) + uçtan uca `DefinitionStoreAgentSourceTenantCredentialTests` (3 test — gerçek `DefinitionStoreAgentSource → AgentDefinitionCompiler → CompiledAgentCache` zincirinde: bağlama eklenince önbellek büyümeden yeni credential kullanılıyor, bağlama silinince global credential'a dönüyor) |

### 🟡 Aynı fazda kapanır veya gerekçelenir — ikisi de kapandı

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | §65.5'teki anlatı metni "mutasyondan **önce**" (K-089) diyordu ama gerçek kod (`ApiKeyEndpoints`/`RetentionEndpoints` ile aynı, kurulu desen) mutasyonu önce uygular, denetim kaydını sonra (hata yutan `AuditRecorder.WriteAsync`) yazar | **Düzeltildi — dokümanla kod artık aynı şeyi söylüyor.** §65.5 metni güncellendi: K-089/K-370'in "önce + engelleyici" deseni yalnız GERİ ALINAMAZ eylemler içindir (script çalıştırma, onay kararı); bir `configKeyName` yazımı bu sınıfa girmez. DoD'deki `[~]` satırı zaten bu gerekçeyi taşıyordu |
| 2 | Plan tablosunda adı geçen `TenantProviderConcurrencyTests` ve `TenantProviderTelemetryTests` hiç yazılmamıştı | **Kısmen yazıldı, kısmen gerekçelendi.** Concurrency: gerçek PostgreSQL'e karşı `TenantProviderBindingConcurrencyTests.Racing_upserts_to_the_same_binding_leave_exactly_one_consistent_row` eklendi (20 eşzamanlı `UpsertAsync`, `JobStoreConcurrencyTests` deseniyle) — `ON CONFLICT ... DO UPDATE` altında tek satır kalıyor, kaybolan güncelleme yok. Telemetry: yeni BYOK kodunun hiçbir yerinde `ILogger`/`Activity` çağrısı **yok** (`grep -rn "Log\|SetTag" src/AgentPrism.Core/Tenancy/ src/AgentPrism.AspNetCore/Endpoints/TenantProviderEndpoints.cs` boş döner) — sızacak bir log/span yolu yok; mevcut genel `AuditSecretFilterTests` + bu fazın kendi `Response_never_carries_a_credential_value`/`Saving_a_binding_writes_an_audit_entry_without_a_credential_value` testleri (functional) zaten API yanıtı ve denetim izi yollarını kapatıyor. Ayrı bir `TenantProviderTelemetryTests` sınıfı açmadım çünkü doğrulayacağı davranış yok |

**Temiz çıkan başlıklar:** 3.1 (DoD), 3.3 (test seviyesi), 3.5 (imza-gövde), 3.7 (repo kuralları), 3.8 (ürün yüzeyi)

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IModelProviderRegistry.CreateChatClient(ModelBinding)` (sync) davranışı
  **birebir korunur** — kiracı/egress çözümlemesi yapmaz, her zaman global
  kimlik bilgisini kullanır. Kiracıya duyarlı bir yol açan HER YENİ kod
  **`CreateChatClientAsync`'i çağırmalıdır**; aksi hâlde BYOK'u sessizce atlar.
- `IModelProvider.CreateChatClient(binding, credential = null)` — üçüncü taraf
  bir sağlayıcı `credential`'ı yok sayabilir (geriye dönük uyumlu davranır) ama
  o zaman o sağlayıcı için BYOK asla çalışmaz; dördü kendi paketinde uygular.
- Egress kontrolü kimlik bilgisi çözümlemesinden **önce** ve **tek** yerde
  (`ModelProviderRegistry.ResolveTenantCredentialAsync`) çalışır — yeni bir
  kısıtlama türü eklenirse buraya eklenmelidir, HTTP katmanına değil.

**Bilinen tuzaklar (🚨):**
- `docs/hafiza/cekirdek-calistirma.md` — senkron/async ikili yol deseni,
  `AuditSecretFilter` alan-adı gotcha'sı.
- `docs/hafiza/openai-saglayici.md` — Google `ProviderUri` kısıtı, 4 sağlayıcı
  istemcisinin kurulum anında tek kez inşa edilmesi.
- `docs/hafiza/sql-saglayicilari.md` — `OUTPUT` gerektirmeyen basitleştirilmiş
  iki dallı upsert deseni.

**Yarım kalan işler:**
- E2E (Playwright) görsel doğrulama yapılmadı (yukarı, "Plandan Sapmalar").
- `RunReplayService` async yola taşındı ama bir tekrar oynatımın (`replay`)
  ORİJİNAL çalıştırmanın kullandığı TAM O ANKİ kiracı kimlik bilgisiyle mi
  yoksa REPLAY ANINDA geçerli olan (değişmiş olabilir) bağlamayla mı gittiği
  ayrı test edilmedi — muhtemelen ikincisi (mevcut bağlama okunuyor), ama bu
  bilinçli bir tasarım kararı olarak kayda geçmedi.
- Egress politikası ve sağlayıcı bağlaması yönetim uçlarında eşzamanlılık testi
  (iki eşzamanlı `PUT`) yazılmadı; SQL Server'ın `UPDLOCK, SERIALIZABLE`
  deseni zaten kurulu ve başka tablolarda kanıtlanmış olduğu için düşük risk
  sayıldı, ama açık bir kontrat testi yok.

**Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek; bu faz bir önkoşul
belirlemiyor.
