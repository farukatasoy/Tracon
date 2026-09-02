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

- **🚨 `sessions.schema_version` Faz 1'den beri vardı ve HEP doluydu — Faz 126 planı bunu bilmeden "NULL = damgasız satır" tasarımı önerdi** (2026-09-01, K-649): `faz-uygulama` Adım 1'in "grep'le ölç" kuralı `SqlSessionStore.cs`'i okuyunca sütunun `0001_initial.sql`'den beri `NOT NULL` olduğu ortaya çıktı. `state_schema_version` (yeniden adlandırılmış hâli) bu yüzden `SessionRecord`'da `int` — hiçbir zaman `null` değil. Yeni MAF-sürüm sözleşmesi için gerçekten eksik olan eksen `StateMafVersion` (`string?`) idi. Yeni bir "damga/versiyon" sütunu eklerken önce `grep -n "schema_version\|CurrentSchemaVersion" src/AgentPrism.Sql.Shared/` ile GERÇEKTEN eksik olanı ölç.
- **Damgalama/doğrulama sorumluluğu SQL store'da DEĞİL, `AgentSessionManager`'da olmalı** (2026-09-01, K-649): `SqlSessionStore` artık `StateSchemaVersion`/`StateMafVersion`'ı `AgentName`/`TenantId` gibi düz veri olarak taşır, kendi başına hesaplamaz/doğrulamaz. Yalnız `AgentSessionManager` hem "şimdi çalışan MAF sürümü" (reflection: `typeof(AIAgent).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()`, `MetaEndpoints.ReadVersion()` ile aynı desen) hem "kayıtlı sürüm" bilgisine aynı anda sahip; "kayıtlı ≠ bugünkü" karşılaştırması ve tanımlı hata mesajı orada üretilir. Bu sayede `InMemorySessionStore`'a HİÇ dokunmak gerekmedi — `record with {...}` zaten her alanı taşıyordu.
- **🚨 Bir yarışı İLK yazım için kapatmak, SONRAKİ yazımları kapatmaz** (2026-08-31, K-648): HATA-004 `TryCreateAsync` ile ilk kaydı atomik yaptı; sonraki her kayıt koşulsuz `SaveAsync`'ten geçmeye devam etti ve var olan bir oturumda **eşzamanlı iki tur da sessizce başarılı** oldu, biri üzerine yazıldı. Çözüm `TryUpdateAsync` + `sessions.version`. İki tuzak: (1) `AuditingSessionStore` yeni üyeyi **iletmezse** arayüzün atomik olmayan varsayılan gövdesini miras alır ve düzeltmeyi sessizce iptal eder — kendi yorumu bu tuzağı `TryCreateAsync` için zaten anlatıyordu; (2) koşulsuz `SaveAsync` sürümü **ilerletmeli**, gelen kayıttan almamalı, yoksa eski sürümü elinde tutan bir yazar hâlâ eşleşir. Bir "yarış kapatıldı" cümlesi okuduğunda sor: **hangi yazım için?**

## `ChatHistoryProvider`

- **`ChatHistoryProvider` örneği tüm oturumlarda paylaşılır** (2026-08-01): oturuma özgü hiçbir durum alan olarak tutulamaz. Veritabanı anahtarı `ProviderSessionState<T>` ile `AgentSession` içinde saklanır. MAF dokümanının açık uyarısı.
- **`ChatHistoryProvider`'ın parametresiz ctor'u yok** (2026-08-02): `protected ChatHistoryProvider(Func<...>?, Func<...>?, Func<...>?)`. Üç filtreyi de (`null` geçerek) vermek gerekir.
- **`ChatClientAgentOptions` ve `HarnessAgentOptions` ikisinde de `ChatHistoryProvider` var** (2026-08-02): derleyici ikisine de aynı örneği koyar. `agent.GetService<ChatHistoryProvider>()` ile geri okunamaz — bağlandığını doğrulamak için gerçek bir çalıştırma yapıp veritabanına bak.
- **`ChatHistoryProvider.InvokingAsync` public** (2026-08-02): `InvokingContext` kurucusu da public (`MAAI001` işaretli). Oturum geçmişini okumanın tek public yolu; sağlayıcı bu çağrıda yalnız okur. `ProvideChatHistoryAsync` protected olduğu için kullanılamaz.
- **`InMemoryChatHistoryProvider` durumu oturumda tutar** (2026-08-02): `GetMessages(AgentSession)` imzası bunu gösteriyor. Tek örneğin tüm oturumlarca paylaşılması güvenli; `AddAgentPrism()` bu yüzden açıkça kaydediyor (K-037).
- **🚨 Responses API + `ChatHistoryProvider` = calisma ani hatasi** (K-030): `AsIChatClient(ResponsesClient, model)` sunucu tarafi `storage`'i acik birakir, `ChatClientAgent` `Only ConversationId or ChatHistoryProvider...` atar. **Yalniz `UsePostgreSql()` acikken** gorulur. Cozum `AsIChatClientWithStoredOutputDisabled(model)`.
- **🚨 `session: null` ile yapılan bir çağrı, `ChatHistoryProvider` KURULUYSA
  KAYDI ATLAMAZ — çerçeve kendi geçici bir `AgentSession` açar ve o oturuma
  yazar** (2026-09-02, Faz 134, `Microsoft.Agents.AI` 1.18.0'a karşı küçük bir
  konsol probuyla ölçüldü: özel bir `ChatHistoryProvider` alt sınıfı,
  `session: null` ile `agent.RunAsync(...)` çağrıldığında `ProvideChatHistoryAsync`/
  `StoreChatHistoryAsync`'in YİNE `session=non-null` ile çağrıldığını gösterdi
  — çerçeve arka planda taze bir `AgentSession` üretip veriyor). Sonuç:
  "`session: null` geç, kalıcılığı atla" varsayımı YANLIŞTIR; gerçek etki
  "gerçek çağıranın oturumuna YAZMA, bunun yerine bir kerelik, hiç geri
  okunmayan bir oturuma yaz" — bu, AgentPrism'in zaten gerçek oturumsuz
  çalıştırmalarda (bkz. `RunRecordingAgent.RunCoreAsync`'in `session = null`
  varsayılanı) SQL-destekli bir `ChatHistoryProvider` kuruluyken sergilediği
  AYNI kabul edilmiş davranıştır — `conversations`/`conversation_items`
  tablosunda sahipsiz ama zamanla saklama politikasınca temizlenen bir satır
  üretir (`RetentionTargets.Conversations`, `updated_at < @cutoff`, `sessions`
  varlığından bağımsız). `StructuredResponseValidatingAgent`'ın onarım turu
  bunu bilerek kullanır (bkz. `docs/hafiza/cekirdek-calistirma.md`).

## OpenAI Responses ve depolama

- **MAF `Hosting.OpenAI` depolaması `TryAddSingleton`** (2026-08-01): bellek içi `IConversationStorage`/`IAgentConversationIndex`/`IResponsesService` kayıtları `TryAdd`'dir. Kendi implementasyonumuzu `AddOpenAIResponses()` çağrısından **önce** kaydedersek bizimki kazanır; sıra bozulursa kalıcılık sessizce devre dışı kalır (`StorageOverrideTests` korur).
- **`OpenAIResponses` public ve tam yolu veriyor** (2026-08-02): `ToAgentRunRequest` / `GetSessionStoreId` / `CreateResponseId` / `WriteResponse` / `WriteResponseStreamAsync`. Sonuncusu **hazır SSE çerçeveleri** üretir (`event:` + `data:` + boş satır) — yeniden çerçeveleme bozar, `SseWriter.WriteRawAsync` ile olduğu gibi yazılır.
- **`GetSessionStoreId` = `conversation ?? previous_response_id ?? null`** (2026-08-02): ölçüldü. `OpenAIResponsesRunRequest` agent adı **taşımaz**; gövdeden kendimiz okuruz.
- **🚨 MAF'in OpenAI `storage` arayuzleri `internal`** (2026-08-02): `IConversationStorage`, `IAgentConversationStore` vb. disaridan uygulanamaz — kendi kalicilik katmanini MAF'in depolama noktasina takamazsin. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
