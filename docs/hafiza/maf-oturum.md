# MAF Oturum ve Gecmis Saglayicisi Tuzaklari

> `AgentSessionStore`, `ChatHistoryProvider`, `AgentSessionStateBag` ve OpenAI
> Responses depolama yolu. Diger MAF tipleri (tool, harness, workflow, MCP,
> paket envanteri) icin: [`maf-api.md`](maf-api.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: `maf-api.md` 15.827/16.000 B'ye ulasmisti (%1 bosluk).
> Oturum/gecmis ekseni kendi basina tutarli ve MAF tip envanterinden BAGIMSIZ
> buyuyor. K-214 merdiveni: gercek bolunme.

## Oturum ve durum

- **`AgentSessionStore` soyut sınıf** (2026-08-01): `SaveSessionAsync` / `GetSessionAsync` / `DeleteSessionAsync`.
- **Çok kiracılılık için MAF'ta hazır yapı var** (2026-08-01): `IsolationKeyScopedAgentSessionStore` + `SessionIsolationKeyProvider`. Sıfırdan yazmaya gerek yok.
- **`AgentSessionStateBag`, `SerializeSessionAsync` çıktısına dahildir** (2026-08-02): oturuma yazılan her şey (kimlik damgası, konuşma kimliği) oturumla birlikte kalıcılaşır. Doğrulandı: `/sessions` çıktısında `stateBag` altında görünüyor.
- **`StateBag.SetValue`/`TryGetValue` AOT tanısı üretmiyor** (2026-08-02): kaynak üreteciyle kurulmuş `JsonSerializerOptions` geçildiğinde `IL2026` çıkmıyor. `AgentPrismCoreJsonContext` bunun için var.

- **🚨 Bir yarışı İLK yazım için kapatmak, SONRAKİ yazımları kapatmaz** (2026-08-31, K-648): HATA-004 `TryCreateAsync` ile ilk kaydı atomik yaptı; sonraki her kayıt koşulsuz `SaveAsync`'ten geçmeye devam etti ve var olan bir oturumda **eşzamanlı iki tur da sessizce başarılı** oldu, biri üzerine yazıldı. Çözüm `TryUpdateAsync` + `sessions.version`. İki tuzak: (1) `AuditingSessionStore` yeni üyeyi **iletmezse** arayüzün atomik olmayan varsayılan gövdesini miras alır ve düzeltmeyi sessizce iptal eder — kendi yorumu bu tuzağı `TryCreateAsync` için zaten anlatıyordu; (2) koşulsuz `SaveAsync` sürümü **ilerletmeli**, gelen kayıttan almamalı, yoksa eski sürümü elinde tutan bir yazar hâlâ eşleşir. Bir "yarış kapatıldı" cümlesi okuduğunda sor: **hangi yazım için?**

## `ChatHistoryProvider`

- **`ChatHistoryProvider` örneği tüm oturumlarda paylaşılır** (2026-08-01): oturuma özgü hiçbir durum alan olarak tutulamaz. Veritabanı anahtarı `ProviderSessionState<T>` ile `AgentSession` içinde saklanır. MAF dokümanının açık uyarısı.
- **`ChatHistoryProvider`'ın parametresiz ctor'u yok** (2026-08-02): `protected ChatHistoryProvider(Func<...>?, Func<...>?, Func<...>?)`. Üç filtreyi de (`null` geçerek) vermek gerekir.
- **`ChatClientAgentOptions` ve `HarnessAgentOptions` ikisinde de `ChatHistoryProvider` var** (2026-08-02): derleyici ikisine de aynı örneği koyar. `agent.GetService<ChatHistoryProvider>()` ile geri okunamaz — bağlandığını doğrulamak için gerçek bir çalıştırma yapıp veritabanına bak.
- **`ChatHistoryProvider.InvokingAsync` public** (2026-08-02): `InvokingContext` kurucusu da public (`MAAI001` işaretli). Oturum geçmişini okumanın tek public yolu; sağlayıcı bu çağrıda yalnız okur. `ProvideChatHistoryAsync` protected olduğu için kullanılamaz.
- **`InMemoryChatHistoryProvider` durumu oturumda tutar** (2026-08-02): `GetMessages(AgentSession)` imzası bunu gösteriyor. Tek örneğin tüm oturumlarca paylaşılması güvenli; `AddAgentPrism()` bu yüzden açıkça kaydediyor (K-037).
- **🚨 Responses API + `ChatHistoryProvider` = calisma ani hatasi** (K-030): `AsIChatClient(ResponsesClient, model)` sunucu tarafi `storage`'i acik birakir, `ChatClientAgent` `Only ConversationId or ChatHistoryProvider...` atar. **Yalniz `UsePostgreSql()` acikken** gorulur. Cozum `AsIChatClientWithStoredOutputDisabled(model)`.

## OpenAI Responses ve depolama

- **MAF `Hosting.OpenAI` depolaması `TryAddSingleton`** (2026-08-01): bellek içi `IConversationStorage`/`IAgentConversationIndex`/`IResponsesService` kayıtları `TryAdd`'dir. Kendi implementasyonumuzu `AddOpenAIResponses()` çağrısından **önce** kaydedersek bizimki kazanır; sıra bozulursa kalıcılık sessizce devre dışı kalır (`StorageOverrideTests` korur).
- **`OpenAIResponses` public ve tam yolu veriyor** (2026-08-02): `ToAgentRunRequest` / `GetSessionStoreId` / `CreateResponseId` / `WriteResponse` / `WriteResponseStreamAsync`. Sonuncusu **hazır SSE çerçeveleri** üretir (`event:` + `data:` + boş satır) — yeniden çerçeveleme bozar, `SseWriter.WriteRawAsync` ile olduğu gibi yazılır.
- **`GetSessionStoreId` = `conversation ?? previous_response_id ?? null`** (2026-08-02): ölçüldü. `OpenAIResponsesRunRequest` agent adı **taşımaz**; gövdeden kendimiz okuruz.
- **🚨 MAF'in OpenAI `storage` arayuzleri `internal`** (2026-08-02): `IConversationStorage`, `IAgentConversationStore` vb. disaridan uygulanamaz — kendi kalicilik katmanini MAF'in depolama noktasina takamazsin. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
