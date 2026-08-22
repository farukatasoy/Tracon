# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 476 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (115 / 591 kalem)

| K | Satır | Karar |
|---|---|---|
| K-477 | 523 | `SqlPersistenceDiagnosticsSnapshot.PendingMigrations`'a yeni alan eklenmedi; çekirdek-dışı bir setin bekleyen adı `"{set}:{ad}"` önekiyle yazılır (Faz 67, açık soru 2'nin kapanışı) |
| K-478 | 524 | Çalıştırma kimliği `IRunAttributionContext`'ten gelir; istek GÖVDESİNDEN asla alınmaz (Faz 68) |
| K-479 | 525 | Etiketler ayrı tabloya değil `runs.labels` JSON sütununa yazılır (Faz 68, açık soru 1, kullanıcı kararı) 👤 |
| K-480 | 526 | Sınır aşımı KIRPILMAZ, REDDEDİLİR; gürültülü sınır HTTP'de (`400`), sessiz düşürme kayıt yolundadır (Faz 68) |
| K-481 | 527 | Kullanıcı kimliği ve etiketler METRİK etiketi OLMAZ; yalnız sorgu boyutudur (Faz 68) |
| K-482 | 528 | Token kırılımı toplamların İÇİNDE sayılır; bildirilmeyen sayaç `null` kalır, `0` OLMAZ (Faz 68) |
| K-483 | 529 | Cache fiyatı ÇIKARMALI hesaplanır ve HER maliyet toplamının ÜÇÜNCÜ terimidir; tanımsız oran `Unknown`'a DÜŞÜRMEZ (Faz 68, açık soru 2/3, kullanıcı kararı) 👤 |
| K-484 | 530 | `UsageDetails`'in ses sayaçları için `MEAI001` bastırması TEK dosyada (`UsageBreakdown`) toplandı (Faz 68) |
| K-485 | 531 | `/api/stats`'a `groupBy` EKLENMEDİ; `byUser`/`byLabel` her zaman döner (Faz 68, plandan sapma) |
| K-486 | 532 | Attribution UPSERT'te `COALESCE` ile KORUNUR, üzerine yazılmaz (Faz 68) |
| K-487 | 533 | Tool sarmalama sırası Authorizing (dış) → Timeout → ApprovalRequired (iç) → gerçek fonksiyon; MCP yolunda AYNI mantık TEKRARLANIR (Faz 69) |
| K-488 | 534 | Yetki reddi İSTİSNA fırlatmaz, normal sonuç döner; kanca hata fırlatırsa fail-closed (Faz 69) |
| K-489 | 535 | `RunErrorClass.ToolTimeout` yeni değer (13); `AgentPrismToolTimeoutException` stabil kimliği `tool_timeout` (Faz 69) |
| K-490 | 536 | MAF `FunctionInvokingChatClient`'ın onay kısa devresi `AITool.GetService<T>()` pipeline'ı üzerinden çalışır; `ApprovalRequiredAIFunction.InvokeCoreAsync`'i DOĞRUDAN çağırmak defer ETMEZ, gerçek gövdeyi çalıştırır (ÖLÇÜLDÜ, Faz 69) |
| K-491 | 537 | Tool yürütme varsayılan timeout'u 30 saniye; ÖLÇÜLMEDİ, ilk gerçek koşumdan sonra gözden geçirilecek (Faz 69, F-114, açık soru 5) |
| K-492 | 538 | `RunEventType.ReasoningDelta` değeri 23, plandaki 22 DEĞİL (Faz 70, F-115) |
| K-493 | 539 | `IRunEventSink` fan-out'u depo başarısından TAM bağımsız; `RunEventWriter.CompleteAsync` artık `IsDisabled` iken erken dönmez (Faz 70, F-115, Açık Sorular 1/2/4) |
| K-494 | 540 | Fonksiyon düğümü yalnız `Sequential`'da desteklenir; `WorkflowDefinition.Nodes` `AgentNames` ile karşılıklı dışlanır (Faz 71, F-116) |
| K-495 | 541 | Karışık zincirde agent düğümü `AIAgentBinding` değil `WorkflowAgentStepExecutor` (bir `FunctionExecutor` alt sınıfı) olarak bağlanır (Faz 71, F-116) |
| K-496 | 542 | Fonksiyon adı kaydetme anında da doğrulanır (agent adının aksine, yalnız derleme anında); kayıt süreç ömrü boyunca sabittir (Faz 71, F-116) |
| K-497 | 543 | Kod düğümünün kendi zaman aşımı bu fazda ele alınmadı; Faz 69'un tool timeout sözleşmesi tek aday olarak bırakıldı (Faz 71, F-116, Açık Soru 2, ÇÖZÜLMEDİ) |
| K-498 | 544 | Kontrol noktasından devam sözleşmesi ÖLÇÜLDÜ: EN SON kontrol noktasından sürdürme kod düğümünü YENİDEN ÇAĞIRMAZ, DAHA ERKEN bir kontrol noktasından sürdürme ÇAĞIRIR — `AddWorkflowFunction` işleyicisi bu yüzden İDEMPOTENT olmak ZORUNDADIR (Faz 71, F-116, planın "en riskli hata modu") |
| K-499 | 545 | Talimat sözlüğü için migration YAZILMADI (Faz 72, F-117); plan yanlıştı |
| K-500 | 546 | Kültür sürümün İÇİNDEDİR; dil başına ayrı sürüm hattı açılmadı (Faz 72, F-117, plan Açık Soru 1, Seçenek A, kullanıcı kararı) 👤 |
| K-501 | 547 | `SpeechAlignment` KARAKTER bazlıdır, kelime bazlı DEĞİL (Faz 72, F-118) |
| K-502 | 548 | Akışlı sentez + `IncludeTimestamps` kombinasyonu istisna ile REDDEDİLİR, sessizce yok sayılmaz (Faz 72, F-118, K1) |
| K-503 | 549 | Alt agent çağrıları ebeveynin `culture`'ını MİRAS ALMAZ (Faz 72, F-117, denetimde bulundu) |
| K-504 | 550 | `speak` tool şeması `includeTimestamps` ALMAZ; yalnız `POST /api/voice/speak` (operatör HTTP yolu) destekler (Faz 72, F-118, plan sapması) |
| K-505 | 551 | Yetenek haritası `capabilities.md`'den ÜRETİLİR, commit edilir ve `dotnet pack` onu okur; Node zinciri `dotnet build`'e bağlanmaz (Faz 73, F-120, plan Açık Soru 4) |
| K-506 | 552 | `AgentPrism.Usage` tanıları `Warning`'dir; `Info` `dotnet build` çıktısına DÜŞMEZ (Faz 73, F-120, kullanıcı kararı, ölçümle) 👤 |
| K-507 | 553 | Harita sürüm işareti İÇERİK REVİZYONUDUR, paket sürümü değil (Faz 73, F-120, plan sapması) |
| K-508 | 554 | `APG0302` sarmalayıcıyı değil, DEKORATÖRSÜZ VE FABRİKASIZ sarmalayıcıyı bildirir (Faz 73, F-120, plan Açık Soru 3) |
| K-509 | 555 | `CapabilityCoverageTests` kapsamı TÜM kayıt giriş noktalarıdır, yalnız `Use*`/`Map*` değil (Faz 73, F-120, kullanıcı kararı) 👤 |
| K-510 | 556 | Yerel referans dosyası PROJE başına yazılır, depo köküne değil (Faz 74, F-121, denetim bulgusu 3) |
| K-511 | 557 | `docs/openapi/agentprism.json` `AgentPrism.AspNetCore` paketine KAYNAĞINDAN girer; K-039 yeniden açılmaz (Faz 74, F-121) |
| K-512 | 558 | `CapabilityExampleTests` DÖRT iddia taşır; yabancı üye listesi yalnız Microsoft üyelerini içerir (Faz 74, F-121) |
| K-513 | 559 | Bir `<example>`'ın doğruluğunu yalnız DERLEME kanıtlar; metin denetimi yapısal olarak yetersizdir (Faz 74, F-121, denetim bulgusu 4) |
| K-514 | 560 | Sevk edilen dokümantasyon KENDİ KENDİNE YETER: pakete giren bir metin yalnız tüketicinin elindeki şeylere gönderme yapar (Faz 75, F-126) 👤 |
| K-515 | 561 | Sevk edilen dokümanı denetleyen cırcır, satırı değil BLOĞU okur (Faz 75) |
| K-516 | 562 | Site üreteçleri artık ONARMAZ, HATA VERİR (Faz 75, F-126) |
| K-517 | 563 | `<see cref>` paketlenen OpenAPI belgesinde TAM İMZA olarak render edilir; sözleşme tiplerinde `<c>ÜyeAdı</c>` yazılır (Faz 75) |
| K-518 | 564 | Kök `README.md` İngilizce'dir (Faz 75) 👤 |
| K-519 | 565 | Doküman sitesinin rengi KAPALI bir token kümesidir ve kontrast derleme anında hesaplanır (Faz 76) |
| K-520 | 566 | Figürler (ekran görüntüsü ve diyagram) temadan BAĞIMSIZDIR; iki temada da açık plaka üzerinde durur (Faz 76) |
| K-521 | 567 | Kenar çubuğu `src/sidebar.mjs`'te tek kaynaktır; üç tüketici onu okur (Faz 76) |
| K-522 | 568 | Tüketici doküman standardı faz dokümanından ayrıştırılıp `tuketici-dokuman-senkronu` skill'ine taşındı (kullanıcı kararı) 👤 |
| K-523 | 569 | Kapanmış faz dokümanları `docs/arsiv/fazlar/` altında yaşar; `docs/ 👤 |
| K-524 | 570 | `MIMARI.md` §7 "Güvenlik Modeli" kendi dosyasına ayrıldı |
| K-525 | 571 | Kiracı verisi tutan cache anahtarı TİPLİ olur; elle birleştirilen string anahtar yasaktır |
| K-526 | 572 | Denetim yükü `AuditPayload` ile kurulur; geri alınamaz eylem `AuditRecorder` kullanamaz |
| K-527 | 573 | Yürütülebilir yüzeyi genişleten ayar yapılandırmadan okunmaz |
| K-528 | 574 | `external:invoke` kapsamı istek anında zorlanır; anahtarın var olması yetmez |
| K-529 | 575 | Giden ağ hedefi TEK bir muhafızdan geçer; üç yüzey de kendi kopyasını taşımaz |
| K-530 | 576 | `AgentPrism:Egress:AllowPrivateNetworkTargets` varsayılanı KAPALI; üç yüzeyde de özel ağ reddedilir (kullanıcı kararı) 👤 |
| K-531 | 577 | Sağlayıcı istemcisine muhafız YALNIZ kiracı `Endpoint` override'ı varken takılır (kullanıcı kararı) 👤 |
| K-532 | 578 | Sağlayıcı SDK'larının muhafız kancası ÖLÇÜLDÜ; plan tahmini üçte ikisinde yanlıştı |
| K-533 | 579 | `secret` çözen HER yapılandırma anahtarı bir önek allow-list'ine bağlıdır |
| K-534 | 580 | MCP önek ayarı `Abstractions`'ta yaşar (`AgentPrismMcpSecurityOptions`), `AgentPrism.Mcp`'de değil |
| K-535 | 581 | Webhook ek başlıkları AgentPrism'in kendi başlık adlarını taşıyamaz |
| K-536 | 582 | Giden ağ muhafızı ORTAM PROXY'sini kullanmaz (`UseProxy = false`) |
| K-537 | 583 | `llms.txt` yetenek haritasından AYRI bütçelenir: 10 240 B ve 20 480 B (Faz 78, F-135, ölçümle) |
| K-538 | 584 | `APG0402` yalnız yerel referans dosyası GERÇEKTEN yazılırken öter (Faz 78, F-135, denetim 🔴 #1, kullanıcı kararı) 👤 |
| K-539 | 585 | Karar defterinin §2 tablosu YAPISAL olarak denetlenir: yinelenen numara · tabloyu kesen boş satır · sıra dışı numara |
| K-540 | 586 | Migration çakışmasının İKİ şekli vardır ve ikisi de geçicidir: unique ihlali VE deadlock |
| K-541 | 587 | Terminal `AwaitingApproval` durumu, ona bağlı kayıtlar GÖRÜNÜR olduktan sonra yayınlanır; sıra `AgentPrismRunOptions.BeforePendingApprovalIsPublished` kancasıyla zorlanır |
| K-542 | 588 | Ürün dokümantasyonu `agentprism.doayen.web.tr` alt alan adında kendi sunucumuzda yayınlanır; `base` KALICI olarak `/` oldu ve adres tek dosyada (`docs-site/site.config.mjs`) bildirilir 👤 |
| K-543 | 589 | Bağımlılık sürüklenmesini Dependabot fark eder; beş sabit gerekçeli `ignore` listesindedir 👤 |
| K-544 | 590 | MEAI 10.9.0, `Microsoft.Extensions.*` tabanını 10.0.11'e ZORLAR; "yalnız MEAI'yi yükselt" diye bir seçenek yoktur |
| K-545 | 591 | Geçici çakışma yeniden denemesi migration'ın BOOTSTRAP deyimlerini de kapsar; "bu yalnızca kurulum" muafiyeti yoktur |
| K-546 | 592 | `AgentPrismToolAttribute.cs`'in `OrderTools` örneği artık `static` DEĞİL; tool metodu yine `static` |
| K-547 | 593 | `Azure.Identity` yalnızca test projesine (`AgentPrism.Generators.UnitTests`) `PackageReference` olarak eklendi; `AgentPrism.Azure`'un bağımlılık grafiği DEĞİŞMEDİ |
| K-548 | 594 | `SITE_KURALLARI` kural başına eşleşir; `--site-denetle` `git` hatasında artık çıkış kodu 1 verir (Faz 80, kullanıcı kararı) 👤 |
| K-549 | 595 | `kirik_baglantilar()` site-mutlak (`/...`) bağlantıları slug haritasıyla çözer; üretilen `api/`, `http-api/` ve `openapi/` hem kaynak hem hedef olarak hariç (Faz 80) |
| K-550 | 596 | Doküman bakım testleri `scripts/dokuman_bakim_test.py`dır (ALT ÇİZGİ); planın önerdiği `dokuman-bakim_test.py` (TİRE) DEĞİL (Faz 80, plandan sapma) |
| K-551 | 597 | Yanıt önbelleği anahtarı `record struct CacheKeyScope` (taban anahtar, kiracı, sağlayıcı, sıralanmış tool adları) üzerinden hesaplanır; MEAI'nin varsayılan `GetCacheKey`'ine güvenilmez (Faz 81, F-45, ölçümle) |
| K-552 | 598 | Yanıt önbelleği halkası tool çağrı döngüsünün İÇİNDE, telemetrinin VE içerik güvencesinin DIŞINDA durur (Faz 81, F-45, kullanıcı kararı) 👤 |
| K-553 | 599 | `AllowConcurrentToolCalls`, `ModelProviderRegistry`'nin kurduğu `FunctionInvokingChatClient` örneğine bağlanır; MEAI 1.18.0'ın getirdiği `ChatClientAgentOptions.AllowConcurrentInvocation`'a DEĞİL (Faz 81, F-134) |
| K-554 | 600 | `AgentPrismResponseCachingChatClient` `internal sealed`dır; planın taslağı `public` diyordu (Faz 81, plandan sapma) |
| K-555 | 601 | Yanıt önbelleğinin okuma/yazma hatası `run`'ı DURDURMAZ; loglanır ve isabet/ıska sessizce devam eder (Faz 81, plan metninde açık değildi, repo-geneli ilkeden türetildi) |
| K-556 | 602 | `POST /api/agents/validate`'de eksik `IDistributedCache` `invalid_setting`/`model.providerSettings` olarak görünür; planın beklediği `compilation_error` DEĞİL (Faz 81, plandan sapma, ölçümle) |
| K-557 | 603 | 🚨 Önbellek isabeti hem `ChatResponse.Usage`'ı hem her mesajın `UsageContent`'ini SIYIRIR; aksi hâlde bir `run` kaydı hiç faturalanmamış token'ı ikinci kez sayar (Faz 81, gerçek `samples/AgentPrism.Api` koşumunda ölçülen kusur) |
| K-558 | 604 | `SqlConversationBranchStore` içerik korumasına dokunmaz: dallanma `conversation_items.item`'i BAYT BAYT kopyalar, zarf DOKUNULMADAN kalır (Faz 82, plandan sapma) |
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
