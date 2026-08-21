# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 442 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (115 / 557 kalem)

| K | Satır | Karar |
|---|---|---|
| K-443 | 489 | `FallbackChatClient` devre kesicinin DIŞINDA, `ContentFilterDetectingChatClient`'ın İÇİNDE durur (K-320'nin yerleştirme kuralının Faz 62'ye uygulanışı) |
| K-444 | 490 | `ModelFallback` yalnız `Provider`+`Model` taşır; birincil bağlamanın diğer alanları (sıcaklık, `ProviderSettings`, ...) yedeğe DEVRETMEZ |
| K-445 | 491 | Sağlayıcı başına giden eşzamanlılık sınırı DOLDUĞUNDA isteği REDDETMEZ, kendi `CancellationToken`'ıyla sınırlı olarak BEKLER |
| K-446 | 492 | Ön uçuş reddi `400 Bad Request` döner, `413` DEĞİL |
| K-447 | 493 | `ModelFallbackUsed` olayı HEM `run_events`'e HEM kök span'in `agentprism.model.id` etiketine yazılır |
| K-448 | 494 | Ön uçuş token sayımı için `Microsoft.ML.Tokenizers` + `Microsoft.ML.Tokenizers.Data.O200kBase` AÇIK `PackageReference` ile eklendi (Faz 62 Açık Soru 1, seçenek A); `Microsoft.Bcl.Memory` CVE zorlamasıyla sabitlendi |
| K-449 | 495 | Yedek zincirinde retryable OLMAYAN bir hata (401/403, iptal) HANGİ HALKADA olursa olsun ANINDA ve SARMALANMADAN fırlatılır; zincir yalnız TÜM halkalar retryable hatayla tükendiğinde `AgentPrismProviderUnavailableException` ile "ilk hata" özetine sarılır |
| K-450 | 496 | `FallbackRetryClassifier.IsRetryable` istisnanın TAMAMINI (`InnerException` zinciri + her `AggregateException` kolu) gezer; yalnız en dıştaki istisnaya bakmaz |
| K-451 | 497 | Argüman-koşulu onay kuralı `POST /api/approvals/rules` YENİ bir uçtur; Faz 63 planı bunu yanlışlıkla "mevcut uç" sanıyordu (Faz 63, plandan sapma) |
| K-452 | 498 | `POST /api/approvals/rules` gövdesi `ArgumentsHash` alanı TAŞIMAZ; yalnız `toolName`/`agentName`/`argumentConditions` |
| K-453 | 499 | Plandaki `ToolApprovalDecision` adı `ToolApprovalPolicyDecision` olarak gerçekleşti (Faz 63, plandan sapma) |
| K-454 | 500 | `POST /api/approvals/rules`'ta "aynı kapsam + aynı koşul" çakışması, depoyu değiştirmeden ÜRETİLEN id ile DÖNEN id'yi karşılaştırarak tespit edilir |
| K-455 | 501 | `conditions_hash` kanonikleştirmesi sayısal normalizasyon YAPMAZ (`100` ile `100.0` farklı hash üretebilir); sıralama + `JsonElement.GetRawText()` yeterli sayıldı |
| K-456 | 502 | Denetim izi (`audit_log`) veri konusu silmesinin kapsamı dışındadır; silme yalnız İÇERİK verisinde (oturum, çalıştırma, konuşma, ek, puan, ses, çalıştırma girdisi) uygulanır (Faz 64, kullanıcı kararı) 👤 |
| K-457 | 503 | `DataSubjectScope`'a plandaki taslağın öngörmediği üçüncü bir alan (`ConversationIds`) eklendi (Faz 64, plandan sapma) |
| K-458 | 504 | `DocumentEmbeddings` (bilgi tabanı gömüleri) veri konusu silme/dışa aktarım kapsamının DIŞINDA bırakıldı (Faz 64, plandan sapma) |
| K-459 | 505 | Denetim zinciri "tenant'ın son yazılan satırı" sorgusu `ORDER BY created_at, id` YERİNE ayrı bir sıra sütunuyla (`chain_seq`: PostgreSQL `GENERATED ... AS IDENTITY`, SQL Server `SEQUENCE` + `DEFAULT NEXT VALUE FOR`, SQLite yerleşik `rowid`) bulunur |
| K-460 | 506 | `audit_log.before`/`after` PostgreSQL'de `jsonb`'den `json`'a değiştirildi (Faz 64, K-027'nin beşinci uygulaması) |
| K-461 | 507 | Denetim zinciri yazımında eşzamanlılık, oturum/advisory kilit YERİNE benzersiz dizin + yeniden deneme + rastgele gecikme (jitter) ile çözülür |
| K-462 | 508 | Veri konusu önizleme/silme AYNI SQL işlemi (transaction) üzerinden yürür: önizleme her zaman geri alınır (`ROLLBACK`), gerçek silme yalnız çağıranın denetim yazımı (`beforeCommitAsync`) başarılı olursa `COMMIT` edilir (Faz 64, K-370 emsali) |
| K-463 | 509 | `SessionConversationResolver` (Core) sıradan bir `AgentSession`'ın dahili sohbet geçmişini veri konusu kapsamına ekler; `DataSubjectScope.SessionIds` tek başına bunun için YETERSİZDİR (Faz 64, bağımsız denetim 🔴 #1) |
| K-464 | 510 | `DataSubjectTargetRegistry`'deki her `ArrayContains` çağrısı sütunu TAM NİTELİKLİ (`{table}.column`) verir, bare ad değil (Faz 64, bağımsız denetim sonrası bulunan gerçek kusur) |
| K-465 | 511 | `SqliteDialect.AddUuidArray` Guid'leri BÜYÜK harf metin olarak yazar (`System.Text.Json`'ın varsayılan küçük harf biçimi DEĞİL) (Faz 64, bağımsız denetim sonrası bulunan gerçek kusur) |
| K-466 | 512 | Kiracı kimlik bilgisi/egress çözümlemesi ASYNC bir ikinci yol olarak eklendi; mevcut SENKRON `AgentDefinitionCompiler.Compile`/`ModelProviderRegistry.CreateChatClient`/`CompiledAgentCache.GetOrAdd` üçlüsü DEĞİŞTİRİLMEDİ (Faz 65) |
| K-467 | 513 | Kiracı egress politikası kontrolü, kimlik bilgisi çözümlemesinden ÖNCE ve TEK bir noktada (`ModelProviderRegistry.CreateChatClientAsync`) çalışır — bu nokta hem gerçek `run` derlemesini hem `AgentDefinitionValidator`'ın ön-uçuş kontrolünü kapsar (Faz 65) |
| K-468 | 514 | Dört sağlayıcı paketinin (`OpenAI`/`Anthropic`/`Google`/`Azure`) kiracı-kimlik-bilgisi başına istemci önbelleği TEK bir paylaşılan `AgentPrism.Core.ProviderCredentialClientCache<TFactory>` sınıfıyla yapılır; dört ayrı kopya YAZILMADI (Faz 65) |
| K-469 | 515 | Kiracı sağlayıcı bağlama/egress uçları (`/api/tenants/{tenantId}/providers`, `/api/tenants/{tenantId}/egress`) kiracıyı AMBIYANS `ITenantContext`'ten değil, ROTA parametresinden alır — `ApiKeyEndpoints`'in aksine, `GovernanceEndpoints`'in `PUT /api/tenants/{slug}` deseniyle AYNI (Faz 65) |
| K-470 | 516 | `FallbackChatClient` bir fallback'i tetiklediğinde kiracı/egress'ten HABERSİZ senkron `CreateChatClient` metot grubunu değil, `ModelProviderRegistry.CreateChatClientAsync`'i çağırır (Faz 65, bağımsız denetim 🔴 #1) |
| K-471 | 517 | `CompiledAgentCache`, kiracı credential'ı gömülü bir `AIAgent`'ı credential'dan HABERSİZ bir anahtarla asla saklamaz; bağlama varken üç kaynak (`DefinitionStoreAgentSource`, `CodeAgentSource`) önbelleği TAMAMEN atlar (Faz 65, bağımsız denetim 🔴 #2) |
| K-472 | 518 | `InboundTriggerDispatcher`, "tetikleyici yok" ile "imza yanlış"ı TEK bir `InboundTriggerOutcome.Unauthorized`'a (HTTP `401`) birleştirir; ayrı bir `NotFound`/`404` yolu YOKTUR (Faz 66, bağımsız denetim 🔴 #1) |
| K-473 | 519 | `InboundTriggerDispatcher.ValidateAsync`, imza doğrulamasını hız sınırından ÖNCE çalıştırır (Faz 66, bağımsız denetim 🔴 #2) |
| K-474 | 520 | `InboundTriggerDispatcher.EnqueueAsync`, hedef agent/workflow'un GERÇEKTEN var olup olmadığını kabul anında DOĞRULAMAZ; kontrolü işleyiciye (`AgentRunJobHandler`/`WorkflowJobHandler`) bırakır (Faz 66, bağımsız denetim ile onaylandı) |
| K-475 | 521 | Migration ledger'ın `set_name`/`(set_name, id)` şema yükseltmesi numaralı bir migration DOSYASI değil, `SqlDialect.UpgradeMigrationsTableAsync` bootstrap adımıdır (Faz 67, plandan sapma) |
| K-476 | 522 | `IVectorSearchStore`, `EnableKnowledge = false` iken KAYITSIZ bırakılmaz; fabrikası `null` döner (Faz 67) |
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
