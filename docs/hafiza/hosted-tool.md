# Hosted Tool Tuzakları (web arama · kod yorumlayıcı · dosya arama)

> `AddHostedTool`, sağlayıcı desteği (`IHostedToolSupport`), ücret halkası ve çağrı kaydı (Faz 226, F-603).
> Tool onayı/yetkisi: [tool-onay-ve-yetkilendirme](tool-onay-ve-yetkilendirme.md). Halka sırası:
> [model-boru-hatti](model-boru-hatti.md). Olay sözleşmesi: [run-olay-sozlesmesi](run-olay-sozlesmesi.md).
> Maliyet terimi: [olcum-kota-ve-secenekler](olcum-kota-ve-secenekler.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır; yalnız bu alana dokunurken okunur.

## Parçalar

`HostedToolRegistry` (kayıtlar, `FindProblems`, `Create`, `Fingerprint`) · `HostedToolIdentity` (örnek → ad) ·
`HostedToolSupportCheck` (sağlayıcı desteği; `SamplingSupportCheck`'in kardeşi) · `HostedToolContent` (içerik
tipi → tür) · `HostedToolUsageChatClient` (ücret halkası) · `HostedCallLedger` (çağrı kimliği → ad, run başına) ·
`RunRecordingAgent.WriteHostedOrUnrecognizedContentAsync` (kayıt). Kayıt defteri `IToolRegistry`'nin DIŞINDADIR:
o sözleşme `AIFunctionDeclaration` döner ve MCP kaydı onu sarar (`McpToolRegistry`). Derleyici `ResolveTools`'ta
ikisini de okur, **hosted önce** (bir MCP sunucusunun sonradan yayınladığı aynı ad kodda kayıtlı tool'u gölgelemesin).

## Tuzaklar

- **🚨 Sağlayıcı yanıtı tool ADI taşımaz; ad modele verilen `ChatOptions.Tools` örneğinden okunur** (K-1044). Fabrika
  her derlemede yeni örnek üretir ve `HostedToolIdentity` (ConditionalWeakTable) örneği ada bağlar; halka adı çağrı
  kimliğiyle ledger'a yazar, kaydedici ledger'dan okur. İçeriğe etiket koymak (`AdditionalProperties`) çalışmaz:
  içerik oturum geçmişine serileştirilir. Aynı örneği iki adla döndüren fabrika derleme hatasıdır.
- **🚨 Dosya arama içeriği tipsizdir ve plan yanlış okumuştu** (2026-10-21, tel + decompile, MEAI.OpenAI 10.11):
  `file_search_call` taban `ToolCallContent` (ham temsil **null**) ve AYNI mesajda/güncellemede taban
  `ToolResultContent` (ham `FileSearchCallResponseItem`) olur. Plan "ham tipi çağrıda" diyordu; testlerin sahte
  içeriği öyle kurulsaydı yeşil kalırdı. Çağrı yalnız aynı kimlikli dosya arama sonucuyla eşleşince dosya aramasıdır
  (`HostedToolContent.FileSearchCallIds`); bilgisayar çağrısı gibi tabanlar kendi ham öğesini taşır. Kapı:
  `HostedToolWireTests.OpenAI_responses_items_become_the_registered_tools_calls_and_are_billed`.
- **🚨 Akışta bir çağrı BİRÇOK içeriktir** (gerçek `samples/Tracon.Api` akışlı koşusunda bulundu; birim ve fonksiyonel
  testler yakalamadı): OpenAI web aramasını başlarken ve biterken, kod yorumlayıcıyı **kod parçası başına** aynı çağrı
  kimliğiyle verir. Halka yanıt başına `Seen` ile bir kez sayar; kaydedici `HostedCallLedger.TryClaimWrite` ile bir
  kez yazar. Kapı: `HostedToolRunTests.A_call_a_stream_shows_in_several_contents_is_one_call`.
- **Halka yanıt önbelleğinin İÇİNDEDİR** (iç döngü, FIC'in altı, OTel'in hemen üstü). Dışında bir isabet başka run'a
  ilk run'ın aramasını faturalardı; aynı çağrı kimliğinin `ReportToolMeasurement` ile DEĞİŞTİRİLMESİ yalnız aynı
  run'daki tekrarı korur. Mutasyonla ölçüldü: halkayı önbellekten önce kurmak
  `A_cache_hit_in_another_run_is_not_billed…`'i kırar.
- **Fiyatsız tür `null` kalır, `0` olmaz** (K-1047, K-482): kalem listelenir, run `UnsummedItemCount` sayar, halka
  başına bir uyarı yazılır. Plan "ücret `0`" demişti; `0` bilinmeyeni bedava gösterirdi.
- **Birim çağrı içeriğidir.** Kod yorumlayıcı konteyner başına faturalanır ve yanıt konteyner adlandırmaz: tutar
  `IsEstimated`. Anthropic `UsageDetails.AdditionalCounts["WebSearchRequests"]` sağlayıcının faturasıdır: içerikten
  büyükse fark kimliksiz öğe olur; küçükse sayaç geri alınmaz ve fark loglanır.
- **Önbellek anahtarları fabrikanın ürettiğini hashler** (`HostedToolDescription`, K-811 deseni): kiracı vector store'unu
  değiştirirse sonraki run görür. Bedeli fabrikanın HER run'da çalışmasıdır (anahtar her çözümlemede hesaplanır); fabrika
  ucuz ve yan etkisiz olmalıdır. `CompiledAgentCacheKeyCoverageTests` sınıflandırması bu yüzden vardır.
  🚨 İki önbellek, aynı açıklama: derlenmiş agent VE yanıt önbelleği (`TraconResponseCachingChatClient.ToolKey`). Yanıt
  önbelleği tool'u yalnız adıyla anahtarlarsa her dosya arama `file_search` olur ve iki vector store bir cevabı paylaşır
  (denetim buldu). Sağlayıcı SDK nesnesi (`Filters`, `UserLocation`) `ToString`'te tip adı verir: böyle bir değer
  KARŞILAŞTIRILAMAZ ("opak"); derlenmiş agent her run'da yeniden derlenir (`BypassCache`) ve yanıt önbelleği o tool için
  asla isabet vermez. Sessizce aynı anahtar vermek, kiracının daralttığı alan listesini eski agent'a bırakırdı.
- **Önbellek isabeti hosted çağrıyı `tracon.content` yazar, tool olayı değil, ve ücret yazmaz**: halka önbelleğin
  içindedir, ledger boş kalır. Run aramadı; yanıtın taşıdığı içerik kaydedilir (`A_response_cache_hit_replays…`).
- **Otomatik run devamı hosted çağrıda DURMAZ** (`RunReconciliationService.FindUnsafeToolAsync`): kaydedilen hosted çağrının
  tool kaydı artık vardır ve `IToolRegistry`'de bulunmaz; "güvenle tekrarlanamaz" sayılıyordu. Sağlayıcı çağrıyı cevaplanmış bir
  turun içinde koşturdu, devam onu tekrar koşturmaz (`A_hosted_tool_call_in_the_interrupted_run…`, mutasyonla ölçüldü).
- **Tamponlu yanıtta sağlayıcının sayısı İLK bilinir**: Anthropic'in faturaladığından fazla görünen arama ücretsiz listelenir;
  akışta sayı çağrılardan sonra gelir, yalnız artış eklenir (sayaç geri almaz).
- **Replay hosted tool'u koşturmaz**: dönüştürücü yalnız `AIFunction`'a ulaşır. `ReplayTools` ve `NoTools` hosted adları
  `ToolNames`'ten düşürür (`RunReplayService.ApplyOverrides`); `LiveTools` bırakır.
- **Çakışmalar**: tür başına tek tool (yanıt türü adlandırır, tool'u değil); harness `EnableWebSearch` + kayıtlı web
  arama = model iki tool alırdı; `ToolMode = required:<hosted>` reddedilir (sağlayıcı seçimi modele bırakır). Ad
  function tool'larla tektir: host başlangıcında `ToolRegistrationValidationService` reddeder.
- **Fabrika `HostedToolContext` alır, `IConfiguration` almaz.** Plan örneği `context.Configuration` yazıyordu; tip üç
  alan taşır (`TenantId`, `Provider`, `Services`). Kimlik `secret` değildir; kimlik bilgisi hosted tool'a girmez (K-059).
- **🚨 Nullable enum alanı üç yerde kırar** (`ToolDescriptor.HostedKind`, denetim buldu): (1) OpenAPI şeması AÇIKLAMASIZ çıkar ve `OpenApiDescriptionCoverageTests` tabanı yalnız küçüldüğü için yeni kayıt kırmızıdır: enum tipine `[Description]` ver. (2) NSwag, bileşenin `enum` listesinde `null` VE özelliğin `oneOf [null, $ref]` sarmalı birlikte olunca enum yerine ÖZELLİĞİN adıyla boş bir sınıf yazar (`public HostedKind? HostedKind`); .NET istemci değeri okuyamaz (`JsonException`), sekiz eski özellik de böyleydi. Çözüm `nswag-prepare-document.py`'de (`strip_null_from_enums`, yalnız atılan kopyada); kapı `HostedToolClientTests` + `nswag_prepare_document_test.py`. `ClientCoverageTests` yöntem ADINI görür, okunabilirliği görmez. (3) Konsolun `i18n` "bilerek aynı" listesine kısayol olarak girmez: çevir.
- **Sahte sunucu tuzakları**: Anthropic SDK 12.58 `usage.server_tool_use` için `web_fetch_requests` ister
  (`AnthropicInvalidDataException: 'web_fetch_requests' cannot be absent`); OpenAI sahtesi `ResponsesOutputBeforeMessage`
  ile gerçek öğe şekillerini döndürür ve SDK ayrıştırır.

## Tel tablosu (2026-10-20/21, `HostedToolWireTests` + gerçek koşum)

| Tür | `openai` (Chat) | `openai-responses` | `anthropic` | `google` |
|---|---|---|---|---|
| web arama | ✗ (`web_search_options`, arama dışı modelde `400`) | ✓ `web_search` | ✓ `web_search_20250305` | ✓ |
| kod yorumlayıcı | ✗ (hatasız düşer) | ✓ `code_interpreter` | ✓ `code_execution_20250825` | ✓ |
| dosya arama | ✗ (hatasız düşer) | ✓ `file_search` + `vector_store_ids` | ✗ | ✗ (`Retrieval` kaynaksız; ölçülmedi) |

`azure-openai` Chat Completions kurar: hiçbiri. Google için yerel sahte sunucu yoktur; satırı gerçek koşum taşır.
`ModelDescriptor` düzeyinde ret ayrı bir adaydır (F-712; örnekleme alanları için aynı sınıf Faz 252'de `RejectedSamplingFields` oldu): yüzey `✓` iken model aracı reddedebilir.
