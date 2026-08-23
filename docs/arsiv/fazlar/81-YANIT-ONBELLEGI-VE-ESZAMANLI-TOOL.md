# Faz 81 — Yanıt Önbelleği ve Eşzamanlı Tool Çağrısı

> **Durum:** ✅ Tamamlandı (2026-08-22)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-45**, **F-134** — Dalga 13 Küme B
> **Önkoşul:** [Faz 62](62-MODEL-YEDEK-ZINCIRI-VE-ON-UCUS-DENETIMI.md) — halka sırası kuralı ve `ModelBinding` bayrak emsali oradan gelir
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`
> **🚨 Plan sonrası sürüm değişimi:** Plan MAF 1.16.0 · MEAI 10.8.3'e karşı yazıldı; depo 2026-08-21'de **MAF 1.18.0 · MEAI 10.9.0 · MCP 2.2.0**'a yükseldi (K-543, K-544). İki etkisi vardır ve ikisi de §81.5 ile §81.1'dedir: eşzamanlı tool bayrağının **ikinci bir evi** doğdu, ve MEAI 10.9.0 kendi yönlendirme/yedek istemcilerini getirdi (`RoutingChatClient` ailesi) — bu faz onları kullanmaz, ama halka sırası kararı verilirken bilinmelidir
> **Yeni paket:** Yok — `Microsoft.Extensions.Caching.Abstractions` 10.0.10 `AgentPrism.Core`'un grafiğinde **zaten var** (üç TFM'de de, `Microsoft.Extensions.AI` üzerinden; ölçüldü 2026-08-21) · **Migration:** Yok — `ModelBinding` `jsonb` sütununa bütün olarak serileşir
> **Public API:** Büyüyor — iki `ModelBinding` alanı, bir `sealed record`, bir `public class`. `PublicAPI.Shipped.txt` dosyalarının toplamı **16 satır** (yalnız başlık satırları; ölçüldü) → Faz 7'den önce eklemek ucuzdur, sonra bir sürüm kararıdır
> **Tüketici yüzeyi:** `docs-site/` → `guides/model-providers.md` (önbellek), `guides/reliability.md` (önbelleğin devre kesici ve yedek zincirle ilişkisi), `concepts/tools.md` §"Authorization and timeout" kardeşi (eşzamanlı çağrı), `capabilities.md` §"Agent design and model control" tablosuna iki satır
> · sevk edilen: `ModelBinding` XML dokümanı (yeni alanlar), `src/AgentPrism.Core/README.md`. `api/` ve `http-api/schema-modelbinding.md` **üretilir** — orada iş XML dokümanıdır
> **Manuel test alanı:** [`docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md`](../../manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md) (`MYU`) — ikisi de `ModelBinding` + `ModelProviderRegistry` yüzeyidir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/81-YANIT-ONBELLEGI-VE-ESZAMANLI-TOOL.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz model boru hattına **iki opt-in ergonomi anahtarı** ekler. İkisi de aynı yere takılır — `ModelProviderRegistry`'nin kurduğu zincir — ve ikisi de varsayılan kapalıdır. Kazanan, deterministik iş yükü koşturan tüketicidir: aynı soru iki kez sorulduğunda ikinci kez ödemez, ve bir turda birbirinden bağımsız üç tool çağrıldığında üçünü sırayla beklemez.

## Bitiş Ölçütleri (DoD)

- [x] Aynı istem iki kez sorulduğunda ikinci `run` modele **çıkmaz**; `usage` token'ları sıfır ve `chat` span'i yok — `samples/AgentPrism.Api` üzerinde gerçek bir OpenAI çağrısıyla ölçüldü: ikinci `run`'ın `usage` alanı `null` döndü (K-557 — `0` değil `null`, "ölçülmedi" anlamında), birinci `run`'ın `usage.totalTokens`'ı `244` idi. Bu ölçüm İLK denemede `null` DEĞİL `244` (ikinci run BİRİNCİYLE AYNI) döndürdü — gerçek bir kusurdu, `StripUsage(...)` ile düzeltildi ve `ResponseCacheUsageTests`'e kilitlendi (K-557)
- [x] Farklı tool kümesine sahip iki agent aynı önbellek kaydını **paylaşmaz** — düşen bir testle önce kanıtlandı (`ResponseCacheKeyTests.Different_tool_set_does_not_share_a_cache_entry`), sonra geçti; ayrıca `MT-MYU-011` ile gerçek OpenAI çağrısıyla elle doğrulandı
- [x] Başka kiracının önbelleklenmiş yanıtı görünmez — 🚨 **plandan sapma**: `TenantIsolationContract<TStore>` bir **store** sözleşmesidir, `AgentPrismResponseCachingChatClient` bir `IChatClient` dekoratörüdür ve o tabana uymaz. Bunun yerine `ResponseCacheKeyTests.Different_tenant_does_not_share_a_cache_entry` (birim) + `ResponseCachePipelineTests.Response_cache_never_leaks_across_tenants` (fonksiyonel, gerçek kiracı başlığıyla HTTP üzerinden) + `MT-MYU-012` (gerçek OpenAI çağrısıyla elle) — üçü de yeşil
- [x] Önbellek kaydı `Lifetime` sonunda geçersizleşir; akışlı ve akışsız yolun **ikisinde de** — `ResponseCacheLifetimeTests.Non_streaming_write_applies_the_configured_lifetime` ve `Streaming_write_ALSO_applies_the_configured_lifetime` (`CoalesceStreamingUpdates = false` ile zorlanan gerçek akışlı yazma yolu)
- [x] `ResponseCache.Enabled` açıkken `IDistributedCache` yoksa `POST /api/agents/validate` net bir hata döner ve mesaj eksik kaydı adıyla söyler — 🚨 **plandan sapma**: kod `compilation_error` değil `invalid_setting`/`model.providerSettings` döndürür; sebep ve gerekçe K-556'da. `ResponseCachePipelineTests.Enabling_response_cache_without_a_registered_store_fails_validation_naming_the_missing_registration` bunu doğru koda karşı doğrular
- [x] `ResponseCache` `null` ve `AllowConcurrentToolCalls` `false` iken boru hattı bugünküyle **birebir aynı** (K1) — `ResponseCachePipelineTests.Disabled_response_cache_calls_the_model_every_time` + `ConcurrentToolInvocationTests.Flag_off_preserves_todays_sequential_guarantee`; ayrıca tüm mevcut test paketi (Faz 80 sonrası) hiçbir regresyon göstermeden geçti
- [x] `AllowConcurrentToolCalls` açıkken üç eşzamanlı tool'un kaydı, metriği ve yetkilendirme kararı doğru çağrıya bağlanır — çakışma `Barrier` ile garanti edildi: `ConcurrentToolInvocationTests.Three_concurrent_tool_calls_are_all_recorded_correctly_under_a_guaranteed_overlap` (kayıt + metrik — `AgentPrismToolUsage.Report` her çağrıya farklı miktar bağlar), `Authorization_denial_under_concurrency_is_bound_to_the_correct_call` (yetkilendirme) ve `One_tool_timing_out_does_not_stop_its_siblings_from_completing` (bir tool'un kendi zaman aşımı yalnız kendisini etkiler). 🚨 Bu testleri yazarken ayrı bir tuzak ölçüldü: senkron/bloklayan bir tool gövdesi `Task.WhenAll` dağıtımını sessizce sıralı çalıştırır (`docs/hafiza/test-altyapisi.md`); gövdeler `Task.Run` ile sarılarak düzeltildi. "Eşzamanlı alt agent çağrılarında bütçe sayacı kayar" satırı gerekçelendi: `AgentRunBudget.cs` bu fazda değişmedi, mevcut `Interlocked`/CAS güvenliği önceki fazdan miras (bkz. Denetim Bulguları #1)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `build` (frontend dahil), `pack` (17 paket, beklenenle uyuşuyor) ve `format` tam çözümde TEK seferde yeşil. `test` tam çözümde İKİ kez koşuldu (denetim öncesi ve denetim sonrası); her ikisinde de yalnız kaynak-çekişmeli teardown/zamanlama sorunları görüldü, gerçek bir test asla kırmızı olmadı: `AgentPrism.Ui.E2ETests`'teki tek bir test (`Runs_screen_lists_only_roots_by_default`) ve `AgentPrism.AspNetCore.FunctionalTests`'in tamamı (ilk koşumda) — üçü de İZOLE koşulduğunda tam yeşil (614/614 üç kez, E2E testi 1/1, ikinci tam koşumda zaten kendiliğinden yeşildi). İkinci tam koşumda AYRICA `AgentPrism.PostgreSql.IntegrationTests` her testi "Test Assembly Cleanup Failure" ile ikiletti (`Passed: 1118, Failed: 1118, Total: 2236`) — kanıt SqlServer'ın konteyner teardown'ı hemen ÖNCESİNDE bitmişti ve Docker daemon'ı meşguldü: `PostgresFixture.DisposeAsync()`'in Testcontainers/`Docker.DotNet` konteyner silme çağrısı `TaskCanceledException` aldı, testlerin KENDİSİ (1118/1118) zaten geçmişti. İzole koşumda 1118/1118, 26 sn, temiz — regresyon değil, uzun oturumda art arda Docker tabanlı paket koşumlarının ürettiği yeni bir kaynak-çekişme deseni (`docs/hafiza/test-altyapisi.md`'nin bilinen desenine ek bir örnek)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — yukarıdaki satır ve `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md` `MT-MYU-010`
- [x] `secret` taraması boş döndü — bu fazın dokunduğu dosyalarda; repodaki önceden var olan yerel test `Password=`/`sk-` literalleri bu fazdan bağımsızdır (Faz 79/80 emsaliyle aynı kapsam)
- [x] Manuel kabul case'leri `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md` içine eklendi (`MT-MYU-010`…`014`); `010`/`011`/`012` gerçek OpenAI çağrısıyla elle koşuldu, `013` mantık olarak doğrulandı (otomatik testle), `014` otomasyonun zaten garantili kanıtladığını not eder
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bulgular ve sonuçları aşağıdaki "Denetim Bulguları" bölümündedir
- [x] `docs-site/` güncellendi (`guides/model-providers.md` — iki yeni bölüm + tablo satırları, `guides/reliability.md` — devre kesici/önbellek ilişkisi, `concepts/tools.md` — eşzamanlı tool çağrısı alt bölümü, `capabilities.md` — iki tablo satırı, `guides/observability.md` — yeni sayaç/tag'ler); `npm run check` (dördü birden: `check:content` · `build` · `check:links` · `check:weight`) temiz — 1012 sayfa, 130 640 bağlantı, en ağır sayfa 50 884 B/57 000 B tavan
- [x] `tuketici-dokuman-senkronu` koşuldu — `build-agent-map.mjs` yeniden üretildi (harita 8453 B/10 240 B, `llms.txt` 16 375 B/20 480 B), `dokuman-bakim.py --site-denetle` iki kural tetikledi ve ikisi de hedefiyle eşleşti

### Doğrulama komutları

```bash
# Ayni istemi iki kez sor; ikinci yanit onbellekten gelmeli.
curl -s -X POST http://localhost:5081/agentprism/api/agents/cached/run \
  -H 'Content-Type: application/json' -d '{"message":"What is 2+2?"}' | jq '.usage'
curl -s -X POST http://localhost:5081/agentprism/api/agents/cached/run \
  -H 'Content-Type: application/json' -d '{"message":"What is 2+2?"}' | jq '.usage'

# IDistributedCache kayitli degilken tanim dogrulamasi net hata dondurmeli.
curl -s -X POST http://localhost:5081/agentprism/api/agents/validate \
  -H 'Content-Type: application/json' \
  -d '{"name":"cached","model":{"provider":"openai","model":"gpt-5.4-mini","responseCache":{"enabled":true}}}' | jq '.messages'

# Onbellek isabet/iska sayaci.
dotnet-counters monitor --counters AgentPrism -p <pid>
```

---

## Plandan Sapmalar

1. **`AgentPrismResponseCachingChatClient` `internal sealed`dır, planın taslağı `public` diyordu.** Ölçüldü: bu sınıfla aynı kategorideki (`ModelProviderRegistry.BuildPipeline`'ın kurduğu, tüketicinin asla doğrudan örneklemediği saf halka) her sınıf (`FallbackChatClient`, `ContentFilterDetectingChatClient`, `CircuitBreakingChatClient`, `ProviderConcurrencyLimitingChatClient`) `internal sealed`dır. K-554.
2. **`AllowConcurrentToolCalls`, `ModelProviderRegistry`'nin kurduğu `FunctionInvokingChatClient` örneğine bağlanır**, planın öngörmediği (plan yazıldıktan SONRA MAF 1.18.0 ile ortaya çıkan) `ChatClientAgentOptions.AllowConcurrentInvocation`'a değil — 81.5'in kendi notunun işaret ettiği açık soru, uygulama sırasında karara bağlandı. K-553.
3. **`POST /api/agents/validate`'de eksik `IDistributedCache` `invalid_setting`/`model.providerSettings` olarak görünür, planın beklediği `compilation_error` DEĞİL.** `AgentDefinitionValidator.CheckModelAsync` zaten `CreateChatClientAsync`'i doğrudan çağırıp `AgentPrismException`'ı bu şekilde yakalıyor (Faz 62'den beri) ve `HasError` doğruysa `compilation_error`'ın kaynağı `CheckStructureAsync`'i hiç çalıştırmıyor. Yeni kod yolu icat edilmedi. K-556.
4. **Kiracı yalıtımı `TenantIsolationContract<TStore>` ile değil, özel testlerle kanıtlandı.** O sözleşme bir **store** tabanıdır; `AgentPrismResponseCachingChatClient` bir `IChatClient` dekoratörüdür ve o tabana uymaz. Bunun yerine `ResponseCacheKeyTests`/`ResponseCachePipelineTests`'te özel testler yazıldı.
5. **🚨 Planın taslağı belirtmiyordu ama uygulama sırasında zorunlu hale geldi: önbellek isabeti `ChatResponse.Usage`'ı ve her `UsageContent`'i siyırır.** `samples/AgentPrism.Api` üzerinde gerçek bir OpenAI çağrısıyla ölçülen bir kusurdu — isabet orijinal çağrının token sayısını YENİDEN raporluyordu. K-557; ayrıntı `docs/hafiza/model-boru-hatti.md`.
6. **`AgentPrism.Testing.FakeModelProvider`'a `CallsTools(...)` eklendi** (paylaşılan test altyapısı, plan dışı ama gerekli): tek bir turda birden çok tool çağrısı üreten bir sahte adım script'lenemiyordu; gerçek eşzamanlı çağrı testleri bu olmadan yazılamazdı.
7. **Cache read/write hataları `run`'ı durdurmaz — planın "Planlanan Public API" taslağı bunu göstermiyordu ama risk tablosu talep ediyordu.** `ReadCacheAsync`/`ReadCacheStreamingAsync`/`WriteCacheAsync`/`WriteCacheStreamingAsync`'in dördü de hatayı yutar, loglar; `run` devam eder ("gözlemlenebilirlik işlevselliği bozmaz" kuralı, K-555).
8. **`samples/AgentPrism.Api`'ye kalıcı bir `cached-support` demo agent'ı ve `AddDistributedMemoryCache()` kaydı eklendi** — plan bunu istemiyordu ama `support`/`researcher`/`router`/`summarizer`/`translator` emsaliyle tutarlı: her fazın ergonomi kazanımı örnek uygulamada gösterilir.

## Bu Fazda Verilen Kararlar

K-551, K-552, K-553, K-554, K-555, K-556, K-557 — bkz. `docs/KARARLAR.md`.

## Denetim Bulguları

Bağımsız denetim taze bağlamlı ayrı bir agent tarafından koşuldu (2026-08-22).
Kendi kanıtını bağımsız üretti: `build`/`format`/`AgentPrism.Core.UnitTests`/
`AgentPrism.AspNetCore.FunctionalTests`'i izole yeniden koştu (hepsi yeşil),
kodu satır satır okudu (`ModelBinding.cs`, `ResponseCacheSettings.cs`,
`AgentPrismResponseCachingChatClient.cs` tam, `AgentDefinitionValidator.CheckModelAsync`
K-556 iddiasını doğrulamak için). **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | DoD satırı eşzamanlılığın "kaydı, metriği ve yetkilendirme kararı" doğru bağlandığını iddia ediyordu; tool **metriği** ve "bir tool iptal edilirken diğeri koşmaya devam eder" hiç test edilmemişti | **Düzeltildi.** `ConcurrentToolInvocationTests`'e `AgentPrismToolUsage.Report` ile üç farklı miktar raporlayan ve her `ToolInvocationRecord.Usage`'ı doğrulayan bir doğrulama eklendi; ayrı bir yeni test (`One_tool_timing_out_does_not_stop_its_siblings_from_completing`) tool_b'nin kendi 200ms zaman aşımının yalnız KENDİSİNİ etkilediğini, zaman aşımı olmayan tool_a/tool_c'nin normal tamamlandığını kanıtlıyor. "Eşzamanlı alt agent çağrılarında bütçe sayacı kayar" satırı **gerekçelendi**: `AgentRunBudget.cs` bu fazda HİÇ değişmedi (`Interlocked`/CAS tabanlı eşzamanlılık güvenliği önceki bir fazdan miras); Faz 81 yalnız `FunctionInvokingChatClient.AllowConcurrentInvocation`'ı açığa çıkarıyor, `AgentRunBudget`'a dokunmuyor — yeni bir risk yüzeyi değil |
| 2 | 🟡 | "`run` iptal edilirken önbelleğe yazma askıda kalır" hata modu hiç test edilmemişti; `catch (Exception ex) when (ex is not OperationCanceledException)` filtresinin iptali gerçekten YUTMADIĞI kanıtlanmamıştı | **Düzeltildi.** `ResponseCacheLifetimeTests.Cancellation_during_a_cache_write_propagates_as_a_cancellation_not_a_swallowed_failure` — `ThrowOnSet` bir `OperationCanceledException` taşıdığında `GetResponseAsync`'in onu YUTMADIĞINI, fırlattığını doğrudan kanıtlıyor |
| 3 | 🟡 | DoD satırı `TenantIsolationContract`'ın dört koşumda yeşil olduğunu iddia ediyordu; o sözleşme bir **store** tabanıdır ve bu fazda hiç değişmedi | **Gerekçelendi** (denetimden önce zaten yazılmıştı) — bkz. "Plandan Sapmalar" #4 ve yukarıdaki DoD satırı: `AgentPrismResponseCachingChatClient` bir `IChatClient` dekoratörüdür, `TStore` tabanına uymaz; kiracı yalıtımı özel testlerle (`ResponseCacheKeyTests`, `ResponseCachePipelineTests`, `MT-MYU-012`) kanıtlandı |
| 4 | 🟡 | İki hata modu yalnız istemci seviyesinde (birim) kanıtlanmıştı, HTTP/DI sınırından değil: (a) `IDistributedCache` hatası `run`'ı durdurmuyor, (b) fazın gerçek bulduğu kusur (K-557, isabetin `usage`'ı yeniden raporlaması) yalnız `AgentPrismResponseCachingChatClient` seviyesinde kilitliydi, kusurun GÖZLEMLENDİĞİ `GET /api/runs/{id}` sınırında değil | **Düzeltildi.** `ResponseCachePipelineTests`'e iki yeni fonksiyonel test eklendi: `A_cache_store_failure_does_not_fail_the_run` (gerçek DI kaydıyla, doğrudan örneklenmiş istemciye değil) ve `Second_identical_run_shows_null_usage_through_the_run_record` (`GET /api/runs/{id}`'nin `usage` alanının ikinci `run`'da `null` döndüğünü doğrular — düzeltme geçici olarak geri alınıp bu testin GERÇEKTEN kırmızıya düştüğü doğrulandı) |

**🟢 aday listesine devredildi:**

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `FakeModelProvider.CallsTools(...)` yeni bir sevk edilen public API ama fazın "Planlanan Public API" bölümünde yoktu | "Plandan Sapmalar" #6'ya not edildi — düşük risk, yalnız test altyapısı |
| 2 | Önbellek isabetinin `chat` span'i üretmediğini doğrudan bir `ActivityListener` ile kanıtlayan test yok (yalnız dolaylı kanıt var) | `docs/ADAYLAR.md`'ye **F-144** olarak eklendi |

## Sonraki Faza Devir Notu

- **Devralınan sözleşme:** `ModelProviderRegistry.BuildPipeline`'ın halka
  sırası artık (dıştan içe) içerik filtresi tespiti → yedek zinciri → devre
  kesici → ek çözme → `FunctionInvokingChatClient` (`AllowConcurrentToolCalls`
  burada ayarlanır) → **yanıt önbelleği halkası (YENİ, koşullu)** →
  OpenTelemetry → içerik guard'ı → eşzamanlılık sınırlayıcı → ham istemci.
  Yeni bir halka eklerken K-320'nin sorusuna artık ÜÇÜNCÜ bir boyut da eklenir:
  "önbellek isabetinin ONU görmesi gerekiyor mu?" — önbelleğin İÇİNDE bir halka
  (OTel, guard gibi) isabet eden çağrıları HİÇ görmez.
- **🚨 Bilinen tuzak (K-557):** bir önbellek dekoratörü yazan/değiştiren
  herkes "isabet maliyet yazmaz" iddiasını bir `chat` span'inin eksikliğine
  DEĞİL, dönen nesnenin kendisinden kullanım bilgisinin SIYRILMASINA
  dayandırmalıdır. Otomatik testler (sahte sağlayıcı varsayılan olarak kullanım
  bilgisi üretmez) bunu YAKALAMAZ; yalnız gerçek bir sağlayıcı çağrısı yakalar.
- **🚨 Bilinen tuzak (`docs/hafiza/test-altyapisi.md`):** `Barrier` tabanlı bir
  eşzamanlılık testinde tool gövdesi SENKRON/bloklayan bir çağrı yapıyorsa
  (`Barrier.SignalAndWait` gibi), `Task.Run` ile sarılmadığı sürece
  `Task.WhenAll` dağıtımı SESSİZCE sıralı çalışır — çakışma hiç gerçekleşmez,
  yalnız zaman aşımı kadar yavaşlar.
- **Yarım kalan iş:** yok — `docs-site/` senkronu ve `tuketici-dokuman-senkronu`
  bu kapanışta tamamlandı.
- **Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek.
