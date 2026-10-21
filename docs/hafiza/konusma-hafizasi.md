# Konuşma Hafızası Tuzakları

> `UseConversationMemory`, `MemorySettings.EnableConversationMemory`, kapsam sarmalayıcısı
> (`ConversationMemoryScopeProvider`), silme kancası (`ISubjectContentEraser`) ve
> tüketicinin `VectorStore` bağlayıcısı. Faz 233 (F-627).
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnızca bu alana dokunurken
> okunur. Yeni not buraya eklenir, `MEMORY.md`'ye değil.

## Kapsam

- **🚨 MAF `ChatHistoryMemoryProvider` kapsamı OTURUM DURUMUNDA dondurur** (ölçüldü, MAF 1.25 decompile): `State(StorageScope, SearchScope)` ilk çağrıda `ProviderSessionState<State>` ile `AgentSession.StateBag`'e yazılır. Oturum sahipliği kapalıyken iki kullanıcı aynı `sessionId`'yi kullanabilir; ilk kullanıcının kapsamı ikincinin okuma ve yazmasına da uygulanırdı. Sağlayıcı `sealed`'dır, kapsam sarmalayıcıyla çözülür: `ConversationMemoryScopeProvider` içe `session: null` verir ve kapsamı `TraconRunContext.Current`'ten **her çağrıda** kurar. `StateKeys` boştur (K-1066). Kapı: `Two_users_sharing_one_session_id_each_write_under_their_own_scope`.
- **Kapsam alanları:** `ApplicationId` = kiracı (normalize), `AgentId` = agent adı, `UserId` = run'ın kullanıcısı. `SessionId` yalnız **yazma** kapsamındadır; arama kapsamında boştur, yoksa kullanıcı oturumlar arası hatırlanmazdı.
- **Kullanıcısız ya da oturumsuz run hafızayı ne okur ne yazar.** HTTP `run` ucu `sessionId` verilmezse oturum AÇMAZ (ölçüldü: `A_run_that_names_no_session_is_not_remembered`). Run'ın kiracısı agent'ın derlendiği kiracıdan farklıysa da atlanır ve uyarı loglanır.
- **`AIContextProvider.InvokingAsync(AIContext)` çağrılır, `MessageAIContextProvider.InvokingAsync` değil** (`ChatClientAgent` decompile): sarmalayıcı `AIContextProvider`'dan türer ve içe aynı aşırı yükü verir.
- **🚨 Workflow adımındaki agent hafıza yazar, ama oturum satırı olmayan bir kimlikle** (ölçüldü, F-643): adımın run'ı workflow'un oturum kimliğini taşır (`WorkflowSessionId.NewId()`, `sessions` satırı yoktur — F-332). Oturum silme ve `sessions` saklama hedefi bu kayda ulaşmaz; veri sahibi silme yalnız çözümleyici o kimliği `SessionIds`'te adlarsa siler. Çare adaydadır (`runs.session_id` genişletmesi ya da adımda kapatma).
- **Alt agent ve kuyruklu run kullanıcıyı taşır** (ölçüldü): `A_sub_agent_remembers_for_the_user_and_session_of_the_run_that_called_it` · `A_queued_run_remembers_for_the_user_who_queued_it`. Alt agent'ın oturumu kök oturumdur (`AgentRunScope.SessionId`).
- **Saklanan:** yalnız `External` kullanıcı mesajı ve asistan metni. Tool çağrısı, tool sonucu ve başka sağlayıcının eklediği mesaj saklanmaz (hafıza kendini geri yazıp büyümesin). Agent'ın "bilmiyorum" cevabı da saklanır ve sonraki aramada dönebilir; site bunu söyler.

## Hata davranışı

- **MAF sağlayıcısı arama ve yazma hatasını YUTAR ve loglar** (`Failed to search…` / `Failed to add messages…`, `LogLevel.Error`); run hafızasız sürer. İptal yutulmaz. Kullanıcı hatayı yalnız günlükte görür: gerçek `run` yapan faz günlüğe bakmalıdır.
- **🚨 Microsoft.SemanticKernel.Connectors.* hattı MEVD 10.10 ile ÇALIŞMAZ** (2026-10-22, ölçüldü): 11 bağlayıcının (PgVector, InMemory, SqliteVec, SqlServer, Qdrant, Redis, MongoDB, AzureAISearch, CosmosNoSql, Weaviate, Pinecone; 1.74.0/1.74.1-preview) hepsi `VectorSearchOptions<T>.get_OldFilter()` çağırır. O üye `Microsoft.Extensions.VectorData.Abstractions` 10.1.0'da vardı, 10.7.0'da yok. Bağlayıcı tabanı `10.1.0` yazar, NuGet uyarı vermez, MAF 10.10.0'ı getirir. Belirti: arama `MissingMethodException`, günlükte `ChatHistoryMemoryProvider` hatası, **yanıt hafızasız**. Yazma çalışır. Çalışan hat: `CommunityToolkit.VectorData.*` 1.0 (PgVector, InMemory — MEVD 10.8.2). Her yeni bağlayıcıda gerçek bir arama koştur: `ConversationMemoryConnectorTests` kalıbı.
- **Bağlayıcı AOT'u kendi sonucudur:** `CommunityToolkit.VectorData.InMemory` `IL2104`/`IL3053` verir. AOT smoke (`Tracon.Samples.ExtensionAotSmoke`) bu yüzden `tests/Shared/VectorStore/InMemoryVectorStore.cs` çiftini bağlar; çift, taban `GetCollection<TKey,TRecord>`'ın `RequiresDynamicCode` ve `RequiresUnreferencedCode` işaretini taşımak ZORUNDADIR (`IL3051`/`IL2046`). Tracon + MAF sağlayıcısı + MEVD soyutlaması: 0 yayın uyarısı, çalışır.

## Silme

- **Kopya `ISubjectContentEraser`'dır** (`Tracon.Abstractions/Privacy`, `internal`; `Target = "conversation_memory"`). `UseConversationMemory` `ConversationMemoryEraser`'ı `TryAddEnumerable` ile kaydeder; bağımlılığı `IServiceProvider`'dır ve `VectorStore` ile seçenekleri **ilk işte** çözer (SQL depoları `ActivatorUtilities` ile `VectorStore` kaydından önce kurulabilir). Depo yoksa her yöntem "yok" döner.
- **🚨 Her süzgeç kiracıyı da adlar** (oturum kimliği yalnız kiracı içinde tekildir). Test: `It_never_deletes_the_records_of_another_tenant_that_carry_the_same_session_id`.
- **Sıra:** veri sahibi silmede SQL satırları → dış içerik (ek baytları) → **hafıza** → denetim yazımı → commit; hata hepsini geri alır. Oturum silmede (`AgentSessionManager.DeleteSessionAsync`) önce kopya, sonra satır, yalnız **var olan** oturum için (kopya silinemezse oturum yerinde kalır, tekrar çağrı bitirir). Saklama süresinde (`SqlRetentionStore`, `sessions` hedefi) toplu okuma → kiracı kiracı kopya → anahtarla satır; arşiv kipinde sink'ten sonra, satırdan önce. `NullDataSubjectStore` da erasers'ı çağırır (bellek içi oturum + vektör deposu).
- **Sayfalama:** süzgeçli okuma `OrderBy(Key)` + `Skip` ile; silme sayfa başı 200 anahtar ve süzgeç boşalana dek. Depo silmeyi bildirip kaydı döndürürse döngü yerine hata verir.
- **Dışa aktarma** `content`, `role`, `agent`, `sessionId`, `createdAt` yazar; vektör ve kullanıcı kimliği yazmaz.

## Test altyapısı

- `tests/Shared/VectorStore/InMemoryVectorStore.cs` süzgeci ÇALIŞTIRIR (`Expression.Compile`), kelime kesişimiyle sıralar, `FailOn(işlem)`, `IgnoreDeletes` ve iptali bilir. Gerçek bağlayıcı çevirisini kanıtlamaz: o `ConversationMemoryConnectorTests` (bellek içi) ve örnek uygulama (PgVector) işidir.
- Sahte model senaryosu (`CallsTool(...).EchoesLastToolResult()`) sağlayıcı başına **bir kez** tüketilir: `OnDemand` testi hafızayı doğrudan tohumlar ve tek tur koşar.
- `kapi.py test` DERLEMEZ; süzgeç sınıf adı + önceden derlenmiş ikili ister. OpenAPI özeti `///` metninden gelir ve derlemeden sonra yenilenir: `<see cref>` özette "bool X.Y" diye çıkar, `<c>` kullan.
