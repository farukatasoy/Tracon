# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`KARARLAR-INDEKS-REDDEDILEN.md`](KARARLAR-INDEKS-REDDEDILEN.md). En eski 176 karar: [`KARARLAR-INDEKS-ARSIV.md`](KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (150 / 326 kalem)

| K | Satır | Karar |
|---|---|---|
| K-177 | 222 | SQL Server upsert'lerinde `MERGE` KULLANILMAZ |
| K-178 | 223 | Migration numaraları sağlayıcı başına bağımsızdır |
| K-179 | 224 | Şema adı kuralı iki sağlayıcıda AYNIDIR |
| K-180 | 225 | SQL Server'da yoğun yazılan tablolarda birincil anahtar NONCLUSTERED, kümelenmiş indeks zaman sütununda |
| K-181 | 226 | `AgentPrism.SqlServer` AOT uyumlu olarak İŞARETLENMEZ |
| K-182 | 227 | Diziler SQL Server'a JSON metni olarak taşınır |
| K-183 | 228 | İki kalıcılık sağlayıcısı aynı anda kaydedilirse açılışta UYARI loglanır |
| K-184 | 229 | SQL Server benzersiz indekste NULL'ları EŞİT sayar; `COALESCE`'li ifade indeksi gerekmez |
| K-185 | 230 | `AgentPrism` meta paketi `AgentPrism.SqlServer`'ı İÇERMEZ |
| K-186 | 231 | SQL Server sözleşme testleri `azure-sql-edge` (arm64) ile doğrulandı; gerçek `mssql/server` hâlâ koşturulamadı 👤 |
| K-187 | 232 | `SqlServerQueries`'teki tüm `@@ROWCOUNT` referansları `@@` önekini kaybetmişti |
| K-188 | 233 | `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnızca İLK sonuç kümesine bakıyordu; SQL Server'ın iki dallı upsert deseni ikinci kümeye yazabiliyor |
| K-189 | 234 | `SqlWebhookStore.ReadSubscription` diziyi `Dialect.ReadTextArray` yerine doğrudan `reader.GetFieldValue<string[]>` ile okuyordu |
| K-190 | 235 | SQLite'ta şema yerine tablo öneki; `SqlQueriesBase.Schema` bu değeri taşır |
| K-191 | 236 | SQLite'ta uuid BÜYÜK harfle yazılır; `SqliteDialect.AddUuid` özellikle EZİLMEZ |
| K-192 | 237 | SQLite migration kilidi sidecar dosya kilididir, `BEGIN IMMEDIATE` tüm migration süresince açık TUTULMAZ |
| K-193 | 238 | SQLite'ta indeks adları VERİTABANI GENELİNDE tektir; migration DDL'indeki her indeks de tablo önekiyle EZİLİR |
| K-194 | 239 | SQLite upsert deseni PostgreSQL ile BİREBİR aynıdır: tek ifadelik `INSERT ... ON CONFLICT ... RETURNING` |
| K-195 | 240 | `DbHelpers.ToGuid`/`ToBoolean` eklendi: `ExecuteScalarAsync` sonucunun CLR tipi sağlayıcıya göre değişir |
| K-196 | 241 | `AgentPrism.Sqlite` AOT uyumlu olarak İŞARETLENMEZ (ölçülmedi) |
| K-197 | 242 | `SQLitePCLRaw.*` paketleri 2.1.12'ye sabitlendi (K-007 deseni) |
| K-198 | 243 | Saklama SQL'i tek tabloyla üretilir, saglayıcı başına kopyalanmaz |
| K-199 | 244 | `run_events` partition'ı açılmadı (K-063 ölçümle kapandı) |
| K-200 | 245 | Parti silme her sağlayıcıda farklı teknik kullanır |
| K-201 | 246 | `MaxRows` var ama uygulanmıyor (ertelendi → kapandı K-258) |
| K-202 | 247 | Saklama zamanlaması Faz 17'nin kuyruğunu yeniden kullanır |
| K-203 | 248 | `sessions`/`conversations` ayrı hedeftir |
| K-204 | 249 | Anthropic ve Google için RESMİ SDK'lar kullanıldı, topluluk paketleri değil 👤 |
| K-205 | 250 | `Google.GenAI`'ın geçişli ağırlığı bilerek kabul edildi ve tek pakette izole edildi 👤 |
| K-206 | 251 | İçerik filtresi tespiti `AgentPrism.Core`'da ortak dekoratördür, sağlayıcı paketlerinde değil 👤 |
| K-207 | 252 | Paket adı `AgentPrism.Google`, sağlayıcı adı `google` 👤 |
| K-208 | 253 | `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır 👤 |
| K-209 | 254 | `AgentPrism` meta paketi Anthropic ve Google sağlayıcılarını İÇERMEZ |
| K-210 | 255 | Sağlayıcı adı `azure-openai`; kimlik fabrikası tüketiciden gelir, `Azure.Identity` alınmadı 👤 |
| K-211 | 256 | `azure-openai` hiçbir `ProviderSettings` anahtarı sunmaz; `AzureChatExtensions` çalışma anında kırıktır |
| K-212 | 257 | Azure AI Foundry ertelendi; gerekçe sürüm uyumu değil, 37 geçişli paket ve doğrulanamazlık 👤 |
| K-213 | 258 | Azure'ın Responses yüzeyi desteklenmiyor |
| K-214 | 259 | `KARARLAR-INDEKS.md`'den tarih sütunu kaldırıldı |
| K-215 | 260 | Ses sözleşmeleri `AgentPrism.Abstractions`'ta yaşar; ElevenLabs bir uygulamadır |
| K-216 | 261 | `AgentPrism.Voice` hiçbir NuGet paketi almaz; ham `HttpClient` kullanılır |
| K-217 | 262 | `AgentRunScope.SessionId` eklendi; oturumsuz yazılan ek saklama tarafından silinir |
| K-218 | 263 | Tool bağımlılıkları KURULUM anında alınır; `AIFunctionArguments.Services` MAF boru hattında boştur |
| K-219 | 264 | `tool_invocations` beş ölçüm sütunu taşır; ses maliyeti token maliyetiyle toplanmaz |
| K-220 | 265 | `POST /api/voice/speak` operatör eylemidir ve `tool_invocations`'a yazmaz |
| K-221 | 266 | Ses API anahtarı düz `ApiKey`'dir; K-059 yalnız veritabanı içindir |
| K-222 | 267 | Konuşma katmanı Seçenek A ile ve `AgentPrism.Core`'da |
| K-223 | 268 | `MapAgentPrism` `UseWebSockets()`'i koşullu olarak kendisi kurar |
| K-224 | 269 | WebSocket bearer token'ı alt protokolde taşınır, sorgu dizesinde kabul edilmez |
| K-225 | 270 | `PersistAudio` yalnız agent'ın ürettiği sesi saklar; kullanıcının sesi hiç saklanmaz 👤 |
| K-226 | 271 | Artımlı (geçici) transkript yok; çözüm tek atımlıdır 👤 |
| K-227 | 272 | Ses dakikası bir kota birimi değildir 👤 |
| K-228 | 273 | i18n kütüphanesi alınmadı; `lib/i18n.tsx` elle yazıldı |
| K-229 | 274 | `t` fonksiyonu modül düzeyindedir ve kimliği hiç değişmez |
| K-230 | 275 | Dil tercihi `localStorage`'da; token `sessionStorage`'da kalır (K-047) |
| K-231 | 276 | Varsayılan dil tarayıcıdan gelir 👤 |
| K-232 | 277 | Sunucu yanıtları çevrilmez; API sözleşmesi tek dillidir |
| K-233 | 278 | Rozet metni küçük harf, süzgeç/başlık metni büyük harf: iki ayrı anahtar kümesi |
| K-234 | 279 | Dil başına ses eşlemesi istemcide tutulur; protokol zaten taşıyordu |
| K-235 | 280 | Konuşma çözümlemesine dil kodu gönderilmez |
| K-236 | 281 | `--ap-subtle` ve `--ap-muted` WCAG AA'ya göre düzeltildi |
| K-237 | 282 | Klavye kısayolu metin alanında tetiklenmez; `Ctrl+Enter` yereldir |
| K-238 | 283 | Komut paleti istemci tarafında arar |
| K-239 | 284 | `run_scores`: `author` NULL'ı sağlayıcı bazlı işlenir |
| K-240 | 285 | `ScoredRuns`/`PositiveRate` iki yolla hesaplanır |
| K-241 | 286 | `InMemoryRunStore`'a isteğe bağlı `IRunScoreStore` eklendi |
| K-242 | 287 | `FeedbackControl` ikili puan gösterir, yıldız YAZILMADI |
| K-243 | 288 | Her çalıştırma (kök VE alt) kendi `CancellationTokenSource`'unu üretir; defter ağaç cascade'ini kendi mantığıyla uygular, akan `CancellationToken`'ın doğal yayılımına GÜVENMEZ |
| K-244 | 289 | `IRunCancellationRegistry` varsayılan AÇIK kaydedilir; ayrı bir `Use...()` çağrısı yok |
| K-245 | 290 | `WorkflowRunner` aynı deftere kendi kök kaydını yazar; `ExecuteAsync`'in zaten kurduğu `timeout`+istek `CancellationTokenSource` birleşimi (`linked`) yeniden kullanılır |
| K-246 | 291 | İptal isteği `run.cancel` eylemiyle denetim izine yazılır |
| K-247 | 292 | K-183'ün isareti `AgentPrism.Sql.Shared`'daki internal `SqlPersistenceRegistration`'dan `AgentPrism.Abstractions`'daki public `SqlPersistenceRegistrationMarker`'a taşındı |
| K-248 | 293 | `MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular; ayrı bir adaptör sınıfı yok |
| K-249 | 294 | `UseOpenAICompatible()` hiçbir `ConfigurationDiagnostic` bildirmez; `UseOpenAI()`'nin sabit `AgentPrism:Providers:OpenAI` bölümü yalnız KENDİSİ için geçerlidir |
| K-250 | 295 | `AgentPrismDiagnosticsReport` genel bir Healthy/Degraded/Unhealthy alanı TASIMAZ; üç durumlu karar yalnız `AgentPrism.AspNetCore.AgentPrismHealthCheck` içindedir |
| K-251 | 296 | `AddAgentPrismHealthChecks()` `AddAgentPrism()`'in önceden çağrıldığını KAYIT ANINDA denetlemez |
| K-252 | 297 | Döngü denetimi `AgentCallGraph.ValidateDetailed`'e taşındı, yeni dosya yok |
| K-253 | 298 | `AgentPrismValidationOptions.McpTimeout` Core'a eklendi, AspNetCore'a değil |
| K-254 | 299 | Kota ölçerine `quota.metric` eklendi 👤 |
| K-255 | 300 | Kota ölçeri `usage`+`limit` için AYRI iki `ObservableGauge`'dur 👤 |
| K-256 | 301 | `QuotaUsageObserver` senkron kapılı önbellektir, zamanlayıcı değil |
| K-257 | 302 | Kota ölçeri yalnız KAYITLI kiracıları tarar |
| K-258 | 303 | `MaxRows` sıra, silme adımından çıkarılarak uygulandı (K-201 kapandı) |
| K-259 | 304 | Saklama korelasyonları BARE hedef adı değil, TAM NİTELENDİRİLMİŞ ad kullanır (Faz 25 hatası düzeltildi) |
| K-260 | 305 | `MaxRows` kiracı genelinde uygulanır, kiracı başına DEĞİL (Faz 36 planının Açık Soru 3'ünden sapma) |
| K-261 | 306 | `eval_case_results`/`workflow_checkpoints` için `MaxRows` eşiği İLİŞKİLİ tablo üzerinden hesaplanır (Açık Soru 2 çözüldü) |
| K-262 | 307 | Şablonda `IncludeSymbols=false` zorunlu |
| K-263 | 308 | Şablonda `TargetFrameworks` boşaltılır |
| K-264 | 309 | Şablon içeriği `<None Pack>` ile paketlenir |
| K-265 | 310 | Şablon paket sürümü varsayılanı kayan `*-*` |
| K-266 | 311 | Üretilen `OrderTools.cs` `using AgentPrism;` taşır |
| K-267 | 312 | `ModelBinding.ResponseFormat` yetenek denetimi yalnız `Json`/`JsonSchema` kiplerinde çalışır |
| K-268 | 313 | `TemplateFixture` sablon testleri icin tam cozum yerine `AgentPrism.src.slnf` (yalniz `src/` paketlerini listeleyen bir cozum filtresi) paketler |
| K-269 | 314 | `AgentPrism.Testing.FakeModelProvider` modele ozel, BIR KEZ tuketilen bir yanit kuyrugu tutar; mesaj gecmisi taranarak "hangi tool zaten cagrildi" cikarilmaz |
| K-270 | 315 | `AgentPrism.Testing` yalniz `net10.0` hedefler (cogul `TargetFrameworks` ozelligiyle ezilerek) |
| K-271 | 316 | `tests/AgentPrism.Core.UnitTests/Fakes/FakeModelProvider.cs` SILINMEDI (plandan sapma) |
| K-272 | 317 | Uç etiketleri TEK `.WithTags("AgentPrism", "<Alan>")` çağrısıyla verilir |
| K-273 | 318 | SSE/ikili yanıtlar `Produces<T>` ile tiple bildirilir; `responseType: null` içerik tipini tamamen düşürür |
| K-274 | 319 | Aynı statü koduna birden fazla `.Produces` çağrısı yapılmaz; çoklu içerik tipi TEK çağrıya `additionalContentTypes` ile yazılır |
| K-275 | 320 | On bir ham `Task<IResult>` ucunun tamamı yol B (`.Produces`/`.ProducesProblem` üstverisi) ile belgelendi; hiçbiri yol A'ya (`Results<...>` imza değişikliği) taşınmadı |
| K-276 | 321 | `docs/openapi/agentprism.json` üretim kaynağı `AgentPrism.AspNetCore.FunctionalTests`'tir, `samples/AgentPrism.Api` DEĞİL |
| K-277 | 322 | Bellek içi depolar `ITenantContext` alır; kiracı süzgeci artık isteğe bağlı değildir |
| K-278 | 323 | `sessions` birincil anahtarı `(tenant_id, id)`; oturum kimliği kiracı içinde benzersizdir |
| K-279 | 324 | Saklama veri düzlemi kiracıya kilitlidir; `IRetentionStore`'un dört metodu `tenantId` alır 👤 |
| K-280 | 325 | Çalıştırmanın alt yazmaları ambient kiracıyla süzülmez; `[TenantAgnostic]` ile gerekçesi yazılır |
| K-281 | 326 | `TenantAgnosticAttribute` `AgentPrism.Sql.Shared` içinde ve `internal`'dir |
| K-282 | 327 | Kiracı yalıtımı iki depo örneğiyle değil, değiştirilebilir tek bir kiracı bağlamıyla sınanır |
| K-283 | 328 | Görünmeyen bir oturum YOK sayılır; "başkasının oturumu" reddi kaldırıldı |
| K-284 | 329 | Tek yürütücü seçimi bir kira TABLOSUYLA yapılır, oturum kilidiyle değil |
| K-285 | 330 | `SingletonGuard` `public`tir; "internal yardımcı" planı uygulanamadı |
| K-286 | 331 | Kira süresi varsayılanı 60 sn, yenileme aralığı `LeaseDuration/3` (en az 1 sn taban); gerçek devralma ölçüldü |
| K-287 | 332 | Saat kayması: `expires_at` uygulama saatiyle hesaplanır, veritabanı saatiyle değil |
| K-288 | 333 | `/api/agents/{name}/run` `Idempotency-Key` varsa akışsız (JSON) çalışır; plandan sapma (kullanıcı kararı) 👤 |
| K-289 | 334 | `AgentPrismIdempotencyOptions` `AgentPrism.Core`'dadır, plandaki gibi `AgentPrism.AspNetCore`'da değil |
| K-290 | 335 | `idempotency_keys` için ayrı bir `Retention: TimeSpan` alanı yerine standart `RetentionTargets`/`AgentPrismRetentionOptions` üçlüsü kullanıldı |
| K-291 | 336 | Idempotency desteği varsayılan AÇIKTIR (`Enabled = true`); K1'in "varsayılan kapalı" kuralının bilinçli bir yorumu |
| K-292 | 337 | Ham govde, filtreden ÖNCE `MapAgentPrism` içine eklenen koşullu bir ara yazılımla tamponlanır |
| K-293 | 338 | `RunError` sınıf/parmak izini doğrudan taşır; ayrı bir arama tablosu açılmadı |
| K-294 | 339 | Sınıflandırma TEK bir noktada, `RunRecordingAgent.CompleteAsync` içinde, `error` `null` değilse çalışır |
| K-295 | 340 | `ByErrorClass` ayrı bir depo metodu değil, `GetStatisticsAsync`'in genişletilmiş sonucu; `/api/stats/errors` o sonucun dar bir dilimi |
| K-296 | 341 | Hata sınıflandırıcının SDK istisna adları örnek uygulamada gerçek bir OpenAI hatasıyla ölçüldü; `ClientResultException` eksikti |
| K-297 | 342 | `quota_exceeded` sınıfı otomatik sınıflandırıcı için YAPISAL olarak ulaşılamazdır; taksonomide kalır ama örnek uygulamada uçtan uca gösterilemedi |
| K-298 | 343 | Parmak izi normalleştirmesi tırnak içi metni SİLMEZ (Açık Soru 3 → C) |
| K-299 | 344 | Sınıf başına en sık üç küme, pencere fonksiyonlarıyla (`ROW_NUMBER()`/`COUNT() OVER`) tek geçişte hesaplanır — bu desenin kod tabanındaki İLK kullanımı |
| K-300 | 345 | Terfi sorgusu `run_events`'teki `RunStarted.Text`'ten okunur; plan taslağının "run_events zaten kullanıcı girdisini taşır" iddiası yanlıştı ve düzeltildi (kullanıcı kararı) 👤 |
| K-301 | 346 | Çok turluluk, "bu oturumda DAHA ÖNCE başlamış başka bir çalıştırma var mı" sorusuyla belirlenir; tam konuşma geçmişi okunmaz |
| K-302 | 347 | `AddCaseAsync`'in `seq`/`source_run_id` eşzamanlılığı `ON CONFLICT`/`MERGE` değil, düz `INSERT` + `SqlDialect.IsUniqueViolation` yakalama + yeniden deneme ile çözülür |
| K-303 | 348 | "Olumsuz puan" otomatik terfi tetikleyicisi olarak `Binary` için `Value == 0`, `Stars` için `Value <= 2` (5 üzerinden) tanımlandı |
| K-304 | 349 | Kuyruğa alınan (`Prefer: respond-async`) bir çalıştırmanın `runs` satırı, işçinin gerçek yürütme satırıyla AYNI birincil anahtarı paylaşır; `IRunStore.StartRunAsync` bu yüzden bir UPSERT'tir |
| K-305 | 350 | Kuyruğa alınan bir çalıştırmada `JobRecord.Id` ile `RunRecord.Id` bilinçli olarak AYNI değeri taşır |
| K-306 | 351 | `AgentPrismAsyncRunOptions.MaxAttempts` varsayılanı `1`'dir (kullanıcı kararı) 👤 |
| K-307 | 352 | `AgentPrismAsyncRunOptions.Enabled` varsayılanı `true`'dur (kullanıcı kararı) 👤 |
| K-308 | 353 | Çalıştırmanın girdisi AYRI bir `run_inputs` tablosunda ve `json` sütununda saklanır; `runs`'a sütun EKLENMEZ |
| K-309 | 354 | Varsayılan tool modu `ReplayTools`; kayıtlı sonucu olmayan bir çağrı yeniden oynatmayı DURDURUR ve `422` döner |
| K-310 | 355 | `LiveTools` `Admin` rolü ister ve onay gerektiren bir tool taşıyan agent bu modda çalıştırılamaz (`409`) |
| K-311 | 356 | Dallanma öğeleri KOPYALAR; işaretçi zinciri reddedildi |
| K-312 | 357 | `conversations.parent_conversation_id` yabancı anahtar TAŞIMAZ |
| K-313 | 358 | Konusma dallandırma yalnız SQL sağlayıcısı açıkken çalışır; bellek içi kurulumda uç `501` döner (kullanıcı kararı) 👤 |
| K-314 | 359 | Kod kaynaklı agent'lar model bindirmesi ve `NoTools`/`ReplayTools` ile oynatılamaz; `400` döner (kullanıcı kararı) 👤 |
| K-315 | 360 | Yeniden oynatma OTURUMSUZDUR |
| K-316 | 361 | Yeniden oynatma yalnız SENKRON ve akışsızdır; kuyruğa alma bu fazın kapsamı dışındadır |
| K-317 | 362 | `azure-sql-edge` artık güvenilir bir yerel doğrulama ikamesidir; hazır-olma denetimi `sqlcmd` yerine ADO.NET ile yazılmalıdır (kullanıcı kararı) 👤 |
| K-318 | 363 | SQL Server migration'larında `ALTER TABLE ADD` ile eklenen sütunu AYNI toplu işlemde `CREATE INDEX`'te kullanmak "Invalid column name" verir; dört migration dosyası etkiliydi |
| K-319 | 364 | `EvalCaseResult.Scores` ayarlanmamışken (varsayılan `JsonValueKind.Undefined`) SQL Server'a `"[]"` yazılır, `"null"` DEĞİL — `ISJSON` kısıtı bare `null`'ı reddeder |
| K-320 | 365 | Model çağrı boru hattının TAMAMINI `ModelProviderRegistry` kurar; `IModelProvider` HAM istemci döndürür (kullanıcı kararı) 👤 |
| K-321 | 366 | İçerik guard'ı `IChatClient` katmanındadır ve tool çağrı döngüsünün İÇİNDEDİR |
| K-322 | 367 | Devre kesici içerik engellemesini ardışık hata SAYMAZ |
| K-323 | 368 | Yeni bir genişleme noktasının "varsayılan kapalı" kapısı KAYITTIR, bir `Enabled` bayrağı değildir (kullanıcı kararı) 👤 |
| K-324 | 369 | `422` yalnızca AKIŞSIZ çalıştırma dalında dönebilir (kullanıcı kararı) 👤 |
| K-325 | 370 | Engellenen veya maskelenen içerik HİÇBİR yere yazılmaz |
| K-326 | 371 | `RunErrorClass.ContentBlocked` `ContentFiltered`'dan AYRIDIR |
