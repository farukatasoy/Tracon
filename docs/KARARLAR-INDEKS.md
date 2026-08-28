# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 558 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (88 / 646 kalem)

| K | Satır | Karar |
|---|---|---|
| K-559 | 605 | `IContentProtector` varsayılanı `NullContentProtector`, `AddAgentPrism()` içinde `TryAddSingleton` ile kaydedilir; `AddContentProtection(...)` `Replace` ile değiştirir (Faz 82, `IAuditLog`/`InMemoryAuditLog` deseninin aynısı) |
| K-560 | 606 | `SqlStoreContext.ContentProtector` `IContentProtector?` (nullable); `NullContentProtector.Instance`'a varsayılan DEĞERİ YOKTUR (Faz 82) |
| K-561 | 607 | `AgentPrismContentProtectionOptions.Keys` bir kid'i anahtarın KENDİSİNE değil, yapılandırma anahtarının ADINA eşler (K-059 örüntüsünün tekrarı, Faz 82) |
| K-562 | 608 | Zarf biçimi metin sütunları için JSON (`$apEnc`/`kid`/`n`/`c`), ikili sütun için sabit başlıklı biçim (`APEB` + sürüm + kid uzunluğu + kid + nonce + şifreli+etiket) — tek yerde tanımlı (Faz 82) |
| K-563 | 609 | `AgentPrismContentProtectionOptionsValidator` yalnız YAPISAL doğrulama yapar (`ActiveKeyId` set ve `Keys`'te var); anahtarın gerçek değerini `IConfiguration` üzerinden ÇÖZMEZ (Faz 82) |
| K-564 | 610 | `AgentPrism.Client` NSwag ile üretilir; üretilen kod repoya commit edilir ve davranışsal bir kapıyla (`ClientCoverageTests`) izlenir, byte-diff kapısı DEĞİL (Faz 83, kullanıcı kararı) 👤 |
| K-565 | 611 | `AgentPrism.Client` `AgentPrism.Abstractions`'ı REFERANSLAMAZ; DTO'lar OpenAPI belgesinden üretilir, sunucu tipleriyle aynı şekle sahip ama FARKLI CLR tipidir (Faz 83) |
| K-566 | 612 | `AgentPrism.Client` ve `AgentPrism.Cli` public API takibinin (`PublicApiAnalyzers`) DIŞINDADIR (Faz 83, Açık Soru 2 karar A) |
| K-567 | 613 | `AgentPrism.Client`'ın AOT sözünden VAZGEÇİLDİ: `AgentPrismAotCompatible=false` (Faz 83, ölçülen risk gerçekleşti) |
| K-568 | 614 | `IMigrationApplier` arayüzü `AgentPrism.Abstractions`'a eklendi: linked-source `MigrationRunner` tipinin üç sağlayıcı derlemesinde belirsiz olması sorununu çözer (Faz 83) |
| K-569 | 615 | `AddAgentPrismClient` `IServiceCollection` döner, `IHttpClientBuilder` DEĞİL; `IHttpClientFactory` kullanılmaz (Faz 83, plandan sapma) |
| K-570 | 616 | Üretim öncesi OpenAPI belgesinin bir kopyasında her şema `additionalProperties: false` ile kapatılır (Faz 83, ölçülen kusur) |
| K-571 | 617 | Enum'lar için `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` TİP DÜZEYİNDE her `enum` bildirimine eklenir; `JsonSourceGenerationOptions.Converters` listesi KULLANILMAZ (Faz 83, ölçülen STJ davranışı) |
| K-572 | 618 | `agentprism health` `/api/models/health`'i okur; `MapAgentPrism` içinde jenerik bir `/health` ucu YOKTUR (Faz 83) |
| K-573 | 619 | OpenAPI belgesi VARSAYILAN KAPALI uçları da tarif eder; `GET /api/diagnostics` belgeye girdi ama davranışı değişmedi (Faz 84, §84.3) |
| K-574 | 620 | `packages/` kök dizini dil sınırı kapısının (`SourceLanguageTests`) kapsamına girdi; `PackagedReadmePattern` regex'i `src/`'nin yanına `packages/`'ı da aldı (Faz 84, §84.7, denetim 🟡 bulgusu) |
| K-575 | 621 | `src/AgentPrism.UI/frontend/package.json`, `@agentprism/client`'a `file:../../../packages/agentprism-client` ile bağlanır; npm registry'den kurulmayı BEKLEMEZ (Faz 84, §84.8) |
| K-576 | 622 | npm yayın işi (`ci.yml`) NuGet'in `--skip-duplicate`'ine karşılık `npm view` ile elle idempotency kontrolü yapar; ikisi de AYNI `v*` git tag'inden türer, ayrı bir npm sürüm şeması YOKTUR (Faz 84, §84.11) |
| K-577 | 623 | Konsol göçü TAM göçtür; üretilen istemcinin üzerine adlandırılmış bir cephe (facade) katmanı EKLENMEDİ (Faz 84, planın kendi kaçış merdiveni kullanılmadı) |
| K-578 | 624 | OpenAPI üretecinin iki sistemik kusuru (eksik `required`, `number\|string` karışımı) VE paylaşılan-şema nullable sızıntısı frontend'de `Fix<T,K>` tek yardımcı tipiyle düzeltilir; sunucu şeması DEĞİŞTİRİLMEZ (Faz 84, §84.6, Açık Soru 4 karar A) |
| K-579 | 625 | `Microsoft.AspNetCore.Mvc.Testing` yalnız GERÇEK giriş noktalı örnek uygulamaları test eden projelerde kullanılır; kütüphane testleri `TestHost` kalır (Faz 85) |
| K-580 | 626 | SQL-tabanlı `jsonb`/`json` yükü taşıyan her ARA tip (`AgentDefinitionPayload` gibi), kaynak tipe (`AgentDefinition`) yeni alan eklendiğinde ELLE senkronize edilmelidir; derleyici bunu zorlamaz (Faz 86, ölçülen kusur) |
| K-581 | 627 | `CompiledAgentCache`'i atlayan bir çağıran, `IAgentCatalog.ResolveAsync`'in UYGULADIĞI `IAgentDecorator` zincirini de ELLE uygulamak zorundadır; yeni `AgentDecoratorPipeline.Apply` bunu tek bir yerde toplar (Faz 86, ölçülen kusur) |
| K-582 | 628 | `AgentPrismOptions.MaxParameterValueLength` tek, üst-düzey bir sınırdır; parametre başına ayrı bir sınır YOKTUR (Faz 86, Açık Soru 2 kapatıldı, öneri A) |
| K-583 | 629 | Kesilen bir işi sürdürmek üçüncü, ayrı bir işlemdir (`JobKind.RunContinuation`, `runs.continued_from_run_id`); `Replay` (K-315) ve `ApprovalResume` (K-368) ile KARIŞTIRILMAZ (Faz 87, kullanıcı kararı) 👤 |
| K-584 | 630 | `Destructive`/`External` etkili bir tool taşıyan koşu varsayılan olarak devam ETMEZ; gevşetme bir AYARLA değil, tool'un kendi `SafeToRepeat` bildirimiyle yapılır (Faz 87, kullanıcı kararı) 👤 |
| K-585 | 631 | Devam koşusunun eşleşmeme politikası `RecordedToolPlayback`'in (internal) kurucusuna bir `ToolPlaybackMismatchPolicy` parametresi olarak eklendi; `ReplayToolMode`'a dördüncü bir üye veya ikinci bir sarmalayıcı tipi AÇILMADI (Faz 87, Açık Soru 1, öneri A) |
| K-586 | 632 | Skill veya çağrılabilir alt-agent kullanan bir agent DEVAM ETTİRİLEMEZ; bu Faz 87'nin planında YOKTU, uygulama sırasında ölçülüp keşfedilen bir sınırdır |
| K-587 | 633 | Workflow düğüm retry'ı yalnız `AddWorkflowFunction` ile kaydedilen fonksiyon düğümlerini kapsar; retry döngüsü düğümün KENDİ çağrısının içinde kalır ve `MaxSuperSteps` sayacını ETKİLEMEZ — ÖLÇÜLDÜ (Faz 87, Açık Soru 2 kapatıldı, öneri B) |
| K-588 | 634 | MEAI görsel üretim yüzeyi deneysel kaldığı sürece `MEAI001` bastırması yalnız görsel kayıt ve çağrı sınırlarında DAR tutulur (Faz 88, plan sapması) |
| K-589 | 635 | Birden çok image provider kaydı, `AgentPrismImageOptions.Provider` adına göre KEYED çözülür; isimsiz kayıt yalnız özel consumer fallback'idir (Faz 88, denetim öncesi tasarım bulgusu) |
| K-590 | 636 | Görsel ek deposu yalnız doğrulanabilir baytı kalıcılaştırır; `HostedFileContent` fail-closed reddedilir (Faz 88, plan sapması) |
| K-591 | 637 | Google image adapter, `WIDTHxHEIGHT` isteklerini tahminden çevirmek yerine REDDEDER (Faz 88, plan sapması) |
| K-592 | 638 | Tool çıktısı boyut sınırı UTF-8 bayt biriminde ölçülür; token veya karakter DEĞİL (Faz 89, kullanıcı kararı) 👤 |
| K-593 | 639 | Kırpılan tool çıktısı `{"truncated","omittedBytes","content"}` alanlı bir JSON zarfına sarılır; zarf `Utf8JsonWriter` + `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` ile üretilir (Faz 89, kullanıcı kararı + plan sapması) 👤 |
| K-594 | 640 | `TruncatingAIFunction` yalnız `string` ve `JsonElement` sonuçları ölçer/kırpar; başka bir ham CLR nesnesi HİÇ dokunulmadan geçer (Faz 89, bağımsız denetim bulgusu, plan Açık Soru 1 kapatıldı) |
| K-595 | 641 | `TruncatingAIFunction`, zarfın asla sınırı AŞMAYACAĞINI kurucuda GARANTİ eder: `maxOutputBytes`, `MinimumEnvelopeBytes`'ın (bugün 57 B, `omittedBytes` için `int.MaxValue` worst-case ile hesaplanır) altındaysa `ArgumentOutOfRangeException` atar (Faz 89, bağımsız denetim bulgusu) |
| K-596 | 642 | `McpResourceTrimming` `AgentPrism.Mcp`'den `AgentPrism.Core`'a `TextTrimming` adıyla TAŞINDI (kopyalanmadı) ve `public` yapıldı (Faz 89, plan) |
| K-597 | 643 | Arşivlemek TAŞIMAK değil DAMITMAKTIR: kapanan fazın dokümanı tam metniyle değil, sabit boyutlu bir kayıtla arşivlenir (Faz 90) (kullanıcı kararı) 👤 |
| K-598 | 644 | Tam metin git geçmişinde yaşar ve çözülebilirliği HER denetimde kanıtlanır; `git log --follow` sözleşme DEĞİLDİR (Faz 90) |
| K-599 | 645 | `HARIC` muafiyeti KORUNUR; muaf tutulan her ağaç KENDİ bütçesini alır (Faz 90) (kullanıcı kararı) 👤 |
| K-600 | 646 | `KARARLAR.md` satırı 450 bayta indirilir; kesilen gerekçe `KARARLAR-GECMISI.md`'ye ÖNCE taşınır, satır SONRA kısaltılır (Faz 90) |
| K-601 | 647 | Public yüzey erişilebilirlik ölçütüyle daraltıldı: yaprak olup başka public imzada geçmeyen 96 tip `internal` yapıldı (Faz 96) |
| K-602 | 648 | Tek sürüm hattı: paketlenen 19 projenin hepsi `1.0.0-preview.N` olarak çıkar (Faz 97) (kullanıcı kararı) 👤 |
| K-603 | 649 | `PublicAPI.Shipped.txt` preview hattı boyunca boş kalır; 618 tipin `Unshipped` → `Shipped` dolumu `1.0.0` GA'ya ertelendi (Faz 97) (kullanıcı kararı) 👤 |
| K-604 | 650 | Yayın işleri (`publish`, `npm-publish`) yayın provası kapısına (`release-dryrun`) bağlandı; kapı her push'ta (PR dahil) koşar (Faz 97) |
| K-605 | 651 | Yeni paket `AgentPrism.Testing.Contracts.Xunit`: `IRunStore` ve 32 diğer store sözleşmesi xunit.v3 test taban sınıfı olarak sevk edilir; ad alanı `AgentPrism.Testing.Contracts.Storage` (Faz 98) (kullanıcı kararı — paket adı ve ad alanı) 👤 |
| K-606 | 652 | `ApiKeyGenerator`/`GeneratedApiKey` `AgentPrism.Core`'dan `AgentPrism.Abstractions`'a taşındı (Faz 98) |
| K-607 | 653 | `IRunStore.AppendEventAsync` yinelenen `Sequence`'i REDDEDER (dört implementasyonda: bellek içi + üç SQL sağlayıcı) (Faz 98) (kullanıcı kararı) 👤 |
| K-608 | 654 | `StartRunAsync`'in dönüş değeri dört sağlayıcıda da COALESCE uygulanmış kaydı taşır (Faz 98) |
| K-609 | 655 | AgentPrism, `IModelProvider.CreateChatClient`'ın döndürdüğü `IChatClient`'ı HİÇBİR ZAMAN dispose etmez; ömür sağlayıcınındır (Faz 99) (kullanıcı kararı) 👤 |
| K-610 | 656 | `ContractCoverage`'ın her çağrısı bir sözleşme AİLESİ adı alır; aile adı almayan aşırı yükleme YOKTUR (Faz 99) |
| K-611 | 657 | Sözleşme suite'inde isteğe bağlı davranış ATLANAN senaryo değil, AYRI bir opt-in sınıftır (Faz 99) |
| K-612 | 658 | Uygulanmış migration baytları değiştirilebilir checksum manifestine değil Git kaynak commit'lerine sabitlenir (Faz 99, F-151) |
| K-613 | 659 | Yargıç model istemcisi ayrı `CreateSetupChatClientAsync` üyesiyle kurulur (Faz 100) |
| K-614 | 660 | `IToolRegistry` public kalır; tanınmayan implementation `IVerifiedToolRegistry` marker'ı taşımadığı için startup'ta reddedilir; `AllowUnverifiedToolRegistry` bilerek yapan için opt-out'tur (Faz 102) |
| K-615 | 661 | Generator kendi ürettiği `JsonSerializerContext`'i kullanmaz; complex tool sonucu için tool sahibi kendi `[JsonSerializable]` context'ini `AgentPrismToolAttribute.JsonSerializerContext`'e verir, aksi hâlde derleme `APG0008` ile durur (Faz 102) |
| K-616 | 662 | `CustomToolContract` yalnız implementer'ın TEK BAŞINA sağlayabileceği isim/metadata/eşzamanlı çağrı sözleşmesini taşır; tenant/timeout/envelope/guard davranışı registry-DI sınırını geçtiği için pakete Core bağımlılığı EKLENMEDEN Core fonksiyonel testlerinde ve sample run'ında kanıtlanır (Faz 102) |
| K-617 | 663 | Content guard'ın normalize edemediği tool sonucu için fail-closed yolu, guard PATTERN eşleşmesinden TAMAMEN bağımsız, koşulsuz bir değiştirme olarak yeniden yazıldı (Faz 102, bağımsız denetim bulgusu — kapanmadan kapatılan 🔴) |
| K-618 | 664 | BYOK iki ayrı public interface'e bölündü: `IModelProvider.CreateChatClient(binding)` yalnız setup credential, yeni `ITenantCredentialModelProvider.CreateChatClient(binding, credential)` yalnız tenant credential alır; capability yok ise registry provider'ı hiç çağırmadan `provider_credential_unsupported` ile fail-closed olur (Faz 103) (kullanıcı kararı) 👤 |
| K-619 | 665 | Provider construction hatası normalizasyonunda AgentPrism'in KENDİ validation hatası, internal ve tek başına inşa edilemez (`InternalsVisibleTo` ile korunan) bir işaretçi tipiyle "foreign" sayılmaktan çıkarıldı (Faz 103) |
| K-620 | 666 | `AgentPrismJudgeException` kaldırıldı; stable judge error code'ları (`judge_failed`/`judge_timeout`/`judge_contract`) internal sabitlere taşındı; `IAgentPrismBuilder.AddRunJudge` `AddAgentSource` deseniyle üç overload (generic/instance/factory) olarak eklendi (Faz 103) (kullanıcı kararı) 👤 |
| K-621 | 667 | `JudgeTimeout` cooperative cancellation değil GERÇEK wait cutoff'tur; token'ı yok sayan judge gövdesi timeout'ta öldürülmez, arkada tamamlanır ve geç sonuç sessizce atılır (skor/summary/metric yazmaz, unobserved exception üretmez) (Faz 103) (kullanıcı kararı) 👤 |
| K-622 | 668 | `kapi.py yayin` extension sample'larını (+ Native AOT smoke) izole `NUGET_PACKAGES` cache'i ve tek exact packed version ile doğrulayan `release_extension_samples.py`'a bağlandı; AOT publish restore+publish TEK komutta birleştirildi (Faz 103) |
| K-623 | 669 | Kiracı yalıtımı UYGULAMA KATMANINDA tek hat kalır; veritabanı RLS'i (Row Level Security) eklenmez (Faz 104) 👤 |
| K-624 | 670 | `Production` ortamında kalıcı olmayan store BAŞLANGIÇTA UYARIR; hata fırlatılmaz ve susturma seçeneği eklenmez (Faz 104) 👤 |
| K-625 | 671 | Üç SQL sağlayıcısına `Options.DataSource` alanı eklendi; AgentPrism kendi kurduğu `NpgsqlDataSource`/`SqlServerDataSource`/`SqliteDataSource`'u artık PUBLIC bir DI servisi olarak kaydetmez; `DataSource` ve `ConnectionString` birlikte verilirse başlangıç hatası (sessiz öncelik yok) (Faz 110) |
| K-626 | 672 | `runs_v1` okuma sözleşmesi görünümü üç sağlayıcıda `EnableReadViews` ile isteğe bağlı yayımlandı; yayımlanan sürüm sütun kaybetmez/yeniden adlandırmaz/daraltmaz, kırıcı değişiklik `runs_v2` olarak açılır; görünüm bir kiracı sınırı DEĞİLDİR (Faz 111) |
| K-627 | 673 | `RunErrorClass.BudgetExceeded` kaldırıldı; sayısal değer `9` KALICI OLARAK EMEKLİ, asla yeniden kullanılmaz 👤 |
| K-628 | 674 | İstemci taraflı tool (`AddClientTool`) taşıyan bir agent, `POST /replay` ile HİÇBİR `toolMode`'da (`ReplayTools`/`LiveTools`/`NoTools` üçü de) yeniden oynatılamaz; ret `RunReplayService.PrepareAsync` içinde, run hiç başlamadan, agent TANIMININ tool listesi taranarak verilir (Faz 112) 👤 |
| K-629 | 675 | Sağlayıcı retry kararı için `IProviderRetryClassifier` (üç durumlu: `Unknown`/`Retry`/`DoNotRetry`) yeni public seam olarak `AgentPrism.Abstractions/Providers/` altında açıldı; `DefaultRunErrorClassifier` `internal sealed partial` → `public sealed partial`; `ErrorFingerprint`'in iç hesabı internal kalıp yeni public `RunErrorFingerprint.Compute(string?)` facade'ı eklendi (Faz 113, F-149) |
| K-630 | 676 | Çalıştırma-içi bütçe kesmesi (`AgentPrismRunBudgetExceededException`, `run_budget_exceeded`) yeni bir `RunErrorClass` üyesi AÇMADAN mevcut `QuotaExceeded` (`4`)'e eşlenir; `9` K-627'nin kararıyla kalıcı emekli kalır (Faz 114, F-166) 👤 |
| K-631 | 677 | `ModelProviderRegistry`'nin kurucusu `IRunPricingResolver?` yerine `IServiceProvider?` alır; `RunBudgetChatClient` fiyat çözümleyiciyi boru hattı KURULUM anında (`BuildPipeline` içinde) GEÇ (lazy) çözer (Faz 114, F-166) |
| K-632 | 678 | `AgentRunBudget`'ın maliyet sayacı `decimal` değil, `long` sabit noktalı nano-birim (`1/1_000_000_000`) + `Interlocked.Add`'tir; `RunRecordingAgent.CompleteAsync`'in run sonu `scope.Budget?.RecordUsage(...)` satırı KALDIRILDI — bütçe muhasebesinin %100'ünü artık `RunBudgetChatClient` taşır (Faz 114, F-166) |
| K-633 | 679 | `AgentPrism.Client`'ın üretilmiş `System.Text.Json.JsonElement`/`ChatRole`-tipli alanları geriye dönük UYUMSUZ biçimde düzeltildi (`scripts/nswag-postprocess-client.py`); `AgentPrismPublicApiTrackingEnabled=false` olduğu için derleyici bunu işaretlemedi (Faz 115, eval CLI komutu sırasında bulunan önceden var olan kusur) |
| K-634 | 680 | Performans tahsis kapısı yalnız TAHSİS EDİLEN BAYTI karşılaştırır (sıfır tolerans), süreyi bilgi olarak kaydeder ama hiçbir şeyi kırmaz; beşinci bağımsız bir kapı DEĞİL, `kapi.py kapanis`'in üç sıcak yol dosyasından biri değiştiğinde koşturduğu koşullu bir adımdır (Faz 116, F-67) 👤 |
| K-635 | 681 | BenchmarkDotNet 0.15.8 yalnız `bench/AgentPrism.Benchmarks` (`IsPackable=false`) için eklendi; K-007'nin "yeni paket gerekçe ister" barı gerçek restore ile ölçülerek karşılandı (Faz 116, F-67) |
| K-636 | 682 | `ModelContextProtocol.Extensions.Tasks` 2.2.0 yalnız `AgentPrism.AspNetCore`'a eklendi; net yeni geçişli paket sıfırdır, `AgentPrism.Mcp` (istemci) bu paketi görmez (Faz 117, F-167) |
| K-637 | 683 | MCP task kimliği AgentPrism'in run kimliğidir; SDK'nın `IMcpTaskStore.CreateTaskAsync()`'i çağrının hangi agent/kiracı olduğunu bilmediği için bu eşleşme kayıt-öncesi bir `AsyncLocal` sağlayıcı filtresiyle (`McpTaskRunProvisioningFilter`) kurulur; arka plan yürütmesi kendi `AmbientTenantScope` sarmalını taşır; kiracı-oblivious SDK-içi `tasks/cancel` müdahalesi kabul edilen, kapatılamayan bir sınır olarak belgelenir (Faz 117, F-167) (kullanıcı kararı) 👤 |
| K-638 | 684 | Yargıç başına retry checkpoint'i yeni bir tablo/migration AÇMADAN mevcut `run_scores` satırlarından okur; bu, K-621'in "sonraki adım" sütununun sorduğu soruya (F-152 timeout modeline yeni bir katman ekler mi) HAYIR cevabıdır (Faz 118, F-152) (kullanıcı kararı) 👤 |
| K-639 | 685 | Kiracı sağlayıcı bağlantısında (`BYOK`) `provider` adı, karşılaştırıcı değiştirilerek değil DEĞER NORMALLEŞTİRİLEREK case duyarsız yapılır; kural `TenantProviderBinding.NormalizeProviderName` olarak public'tir ve store hem yazarken hem sorgularken uygular (Yayın denetimi, BL-006) |
| K-640 | 686 | Yabancı (AgentPrism dışı) bir exception'ın mesajı hiçbir zaman kalıcı alana veya dışa açık yanıta yazılmaz; kural `AgentPrism.SafeErrorText` olarak `AgentPrism.Abstractions`'ta public'tir (Faz 119, BL-027/BL-037) |
| K-641 | 687 | `IJobHandler`'ın at-least-once yürütme sözleşmesi (aynı job'ın süzülmemiş item listesiyle yeniden çağrılabileceği) `AgentPrism.Abstractions`'ın XML dokümanına yazılır ve bir contract testiyle (`JobHandlerContract`, `AgentPrism.Testing.Contracts.Xunit`) kilitlenir; `IIdempotencyStore`'u job dispatch loop'una bağlamak gereksiz ikinci bir mekanizma olacağı için REDDEDİLİR (Faz 120, BL-041) |
| K-642 | 688 | `IAgentDecorator.Order`'ın sayısal yönü DEĞİŞMEZ (düşük değer dışta sarar); yanlış olan XML dokümanıydı ve düzeltildi. Sıralama sözleşmesi taşıyan her public üyenin yönünü SÖZCÜKLE belirtmesi `OrderingContractDocumentationTests` ile kalıcı kapıya bağlandı (BL-034) |
| K-643 | 689 | Seam sözleşme standardının dört boyutu (DI lifetime, tenant mode, delivery guarantee, guarantee limit) iki ayrı mekanizmayla kilitlenir: ilk üçü `SeamContractDocumentationTests`'in küçülen taban çizgisiyle, dördüncüsü `OrderingContractDocumentationTests` deseniyle ayrı TheoryData satırlarıyla (Faz 121, BL-003 kulvar 3) |
| K-644 | 690 | `IAuditLog`'un null-tenant sözleşmesi (boşsa çağıranın AMBIENT tenant'ına düşer, "her kiracı" değil) `ITenantContext` enjeksiyonuyla GERÇEK davranışa dönüştürüldü — `InMemoryAuditLog` öncesinde tüm kiracıları tarıyordu, `SqlAuditLog` sessizce boş dönüyordu; ikisi de `SqlRunStore`'un deseniyle hizalandı (Faz 121, BL-046) |
| K-645 | 691 | 60 tekil-registrasyon seam'ine dedicated `Add*()`/`Use*()` metodu EKLENMEZ; `IAgentPrismBuilder.Services`'in XML dokümanına bağlı bir metin kapısı sözleşmesi yeterli sayıldı (Faz 122, BL-008/BL-019 kulvar 2) |
| K-646 | 692 | Tenant credential'la üretilen `IChatClient`, dört sevk edilen adaptörün (Anthropic, Azure, Google, OpenAI) hepsinde (credential, model, `ProviderSettings`) başına önbelleğe alınır; paylaşılan anahtar `AgentPrism.Core.TenantChatClientCacheKey` olarak eklendi 👤 |
