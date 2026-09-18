# HTTP Uc ve Istek Yolu Tuzaklari

> Minimal API uc tanimi, model baglama, filtre sirasi, WebSocket, kimlik
> dogrulama muafiyeti ve kaydetme ucunda adres denetimi. Servis KAYDI ve DI
> yasam dongusu icin: [`aspnetcore-di.md`](aspnetcore-di.md). JSON
> serilestirme icin: [`aspnetcore-json.md`](aspnetcore-json.md). Kiraci
> onceligi, CORS, egress DNS ve OpenAPI/enum-sozlesme tuzaklari icin:
> [`http-uc-guvenlik-ve-sozlesme.md`](http-uc-guvenlik-ve-sozlesme.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: `aspnetcore-di.md` 15.297/16.000 B'ye ulasmisti (%4
> bosluk) ve dosya adiyla icerigi ayrismisti -- 34 maddenin 23'u DI degil HTTP
> ucuyla ilgiliydi. K-214 merdiveni: gercek bolunme.

- **`launchSettings.json`, `ASPNETCORE_URLS` ortam değişkenini ezer** (2026-08-02): `dotnet run` ile örnek API her zaman 5080'de açılır. Manuel doğrulamada portu varsayma, logdan oku.
- **`WithTags` `Microsoft.AspNetCore.Http` namespace'inde** (2026-08-02): `OpenApiRouteHandlerBuilderExtensions.WithTags<TBuilder>`. `Microsoft.AspNetCore.Builder` yeterli değil; `RouteGroupBuilder` üzerinde `CS1061` verir.
- **`Microsoft.AspNetCore.OpenApi` 10.0.10 CVE'li paket çekiyor** (2026-08-02): `Microsoft.OpenApi` 2.0.0 → `NU1903` build'i kırar. `Microsoft.OpenApi` 2.11.0 temiz; K-007'nin öngördüğü tek bilinçli `PackageReference` ile zorlanır.
- **🚨 `Request.ContentLength` parçalı aktarımda `null`** (2026-08-02): ölçüldü — `HttpClient.PostAsJsonAsync` chunked gönderdiğinde sunucuda `ContentLength` gelmiyor ve `if (ContentLength is > 0)` koşulu gövdeyi sessizce düşürüyordu. İsteğe bağlı gövdelerde ham metni oku, uzunluk başlığına güvenme.
- **`ASP0016`: tek parametresi `HttpContext` olan ve `Task<T>` dönen rota işleyicisi `RequestDelegate` sayılır** (2026-08-02): dönen değer sessizce atılır. Yanıtı doğrudan yaz ve `Task` döndür.
- **`MapGet` HEAD isteğine 405 döner** (2026-08-02): statik varlıklar için `MapMethods(pattern, ["GET","HEAD"], ...)` kullan; ters vekiller ve sağlık denetimleri HEAD ile yoklar.
- **🚨 `WebApplicationBuilder` user-secrets'ı yalnız `Development` ortamında yükler** (2026-08-02, Faz 8): `dotnet run` varsayılan olarak `ASPNETCORE_ENVIRONMENT` ayarlanmamışsa `Production` görünüyor (launchSettings kullanılmazsa) ve `secret`'lar sessizce boş kalıyor — hata vermez, sadece `ApiKey` boş gelir. Elle doğrulamada `ASPNETCORE_ENVIRONMENT=Development dotnet run` gerekir.
- **🚨 Minimal API'de DI'da kayıtlı olmayan bir tip `endpoint` parametresi olursa TÜM `endpoint`'ler kırılır** (2026-08-02, Faz 11): `TimeProvider` `endpoint` imzasına eklendiğinde `RequestDelegateFactory.InferMetadata` patladı ve 136 fonksiyonel testin 121'i düştü — hata mesajı kırılan `endpoint`'le ilgisizdi. Ayrıca gövde ve sorgu parametrelerini `[FromBody]` / `[FromQuery]` / `[FromRoute]` ile açıkça işaretle.
- **🚨 Minimal API'de `IFormFile` parametresi ucu OTOMATİK anti-forgery metadata ekler** (2026-08-02, Faz 14): Olculdu — `UseAntiforgery()` cagrilmayan (bearer token korumali) bir API'de `POST /api/attachments` her istekte "middleware not found" ile `500` veriyordu. `.DisableAntiforgery()` acikca eklenmeli; form govdesi alan HERHANGI bir uc icin gecerli tuzak.
- **🚨 Minimal API'de KAYITLI OLMAYAN (veya nullable/opsiyonel) bir servis parametresi GÖVDE sanılır ve TÜM uçları kırar** (2026-08-02 Faz 9 · 2026-08-05 Faz 28): ASP.NET Core kaynak çıkarımını `IServiceProviderIsService`'e sorar; tip kayıtlı değilse parametreyi gövdeden bağlamaya çalışır ve `MapTracon`'in TAMAMI `InvalidOperationException: Body was inferred but the method does not allow inferred body parameters` ile çöker. Etki tek uçla sınırlı DEĞİLDİR — `RouteEndpointDataSource` tüm uçları TEK bir DFA matcher'da birleştirir (ölçüldü: 112/119 ve 252/261 test aynı anda düştü, semptom hedeften kopuk göründü). Çözüm: kayıtlı olmayabilecek HER servis parametresini `[FromServices]` ile açıkça işaretle. Özellik isteğe bağlı bir paketle geliyorsa (`Tracon.Voice` kurulu değilse) bu NORMALDİR ve uç `501` dönmelidir. İki vakanın anlatısı: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Uc grubuna eklenen filtre `IDisposable` ise grup onu bertaraf etmez** (2026-08-03, Faz 21): `TraconRateLimitFilter` bir `PartitionedRateLimiter` tutar; uygulama omru boyunca yasar ve `MapTracon` tek ornek kurar. Her istekte yeni bir sinirlayici kurulsaydi pencere hicbir zaman dolmazdi.
- **Bir HTTP uc grubunu bearer token denetiminden muaf tutmak icin `TraconEndpointFilter(options, requireBearerToken: false)` ile AYRI bir `MapGroup` kur** (2026-08-04, Faz 22): OAuth `/oauth/callback` gibi bir uc saglayicinin yonlendirdigi tarayicidan gelir, bearer token tasiyamaz ama `/api/meta` gibi tamamen acik da olamaz (loopback + policy gecerli kalmali). Desen arayuz kabugununkiyle (`MapUi`) ayni: kendi `MapGroup` + `requireBearerToken: false` + istege bagli `RequireAuthorization(policy)`. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Kestrel `IHttpWebSocketFeature` SAGLAMAZ; `UseWebSockets()` sart** (2026-08-05, Faz 29): onu `WebSocketMiddleware` kurar ve hata yalnizca ilk WebSocket denemesinde gorunur. Tuketiciden ayrica cagri istemek `MapTracon`'in tek giris noktasi olma kuralini (K1) bozar; `MapTracon` ara yazilimi **kosullu** kurar (ilgili servis kayitliyken + `endpoints is IApplicationBuilder`, K-223). Ikinci bir ornek zararsizdir. `TestServer` yukseltmeyi taklit eder (`CreateWebSocketClient()`). Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Tarayici bir WebSocket el sikismasina `Authorization` basligi EKLEYEMEZ** (2026-08-05, Faz 29): K-046'nin arayuz kabugu icin tespit ettigi kisitin aynisi. Token `Sec-WebSocket-Protocol` alt protokolunde tasinir (`tracon.token.<token>`) ve uc onu sabit zamanda kendisi dogrular (K-224); sorgu dizesi gunluklere yazildigi icin KABUL EDILMEZ. Uc bu yuzden DORDUNCU bir `MapGroup`'tadir (`requireBearerToken: false`) — kabuk ve OAuth geri donusuyle ayni desen.
- **🚨 Tipli (`[FromBody]`) bir parametre govdeyi `IEndpointFilter.InvokeAsync` CAGRILMADAN ONCE tuketir** (2026-08-07, Faz 43): minimal API tipli parametreleri filtre zincirinden ONCE baglar; filtrenin icinde `Request.EnableBuffering()` cagirmak COK GECTIR, akis zaten geri sarilamaz durumdadir. Cozum `MapVoiceConversation`'in `app.UseWebSockets()` deseniyle AYNI: `endpoints is IApplicationBuilder` kontroluyle `MapTracon` icine, KOSULLU (yalniz ilgili baslik varken) `EnableBuffering()` cagiran bir `app.Use(...)` eklenir — boylece filtre govdeyi HER ZAMAN `Position = 0`'dan guvenle yeniden okuyabilir, baglama ONCE mi SONRA mi oldugundan bagimsiz.
- **PUT `/api/agents/{name}` var olmayan bir agent icin 404 doner; olusturma POST `/api/agents`'tir** (2026-08-03, Faz 18): PUT yalniz GUNCELLEME icindir. Surum karsilastirma senaryosu kurarken bu sirayla karsilasildi.
- **🚨 `GET /api/agents/{name}` bir KOD agent'ının `definition`'ını yalnız
  `IAgentDefinitionStore`'a (veritabanı) bakarak dolduruyordu; `CodeAgentRegistration`
  singleton'ı hiç sorulmuyordu** (2026-09-06, kusur bildirimi): declarative
  `AddAgent(AgentDefinition)` ile kaydedilen bir agent'ın `Instructions`'ı
  bellekte (`CodeAgentRegistration.Definition`) gerçekten vardı ama panelde HİÇ
  görünmüyordu — `definition` her zaman `null` dönüyordu. Düzeltme
  `AgentEndpoints.GetAgentAsync`'e `IEnumerable<CodeAgentRegistration>` enjekte
  edip DB `null` dönünce ONA bakan bir fallback ekledi; factory tabanlı
  (`AddAgent(name, factory)`) bir agent için hâlâ `AgentDefinition` yok, ama
  somut tipi `ChatClientAgent` ise `factoryInstructions` alanına best-effort
  okunuyor (factory'yi ÇAĞIRMAK gerekiyor — `catalog.ResolveAsync` DEĞİL, çünkü
  o decorator zinciriyle sarar ve `ChatClientAgent` tip kontrolünü kırar;
  `registration.Factory(httpContext.RequestServices)` DOĞRUDAN çağrılır, hata
  yutulup loglanır — bir GET'i asla 500'e çevirmez).
  **Kalan, BİLEREK dokunulmayan boşluk:** aynı kök neden (`AgentParameterGate`
  de `definitionStore.GetAsync` — yalnız DB — kullanıyor, bkz. kendi XML
  yorumu "a code-defined agent... has no schema at all") declarative bir kod
  agent'ının `Parameters` şemasını `/run` ve `/estimate`'te HİÇ görmüyor. Bu
  düzeltme Playground'un artık `definition.parameters`'ı GÖRMESİNİ sağladığı
  için (aynı `GetAgentAsync` yanıtından okunuyor), declarative + parametreli
  BİR agent varsa panel alanları GÖSTERİR ama sunucu her değeri "unknown
  parameter" ile REDDEDER — pre-existing, dokümante, ayrı bir boşluk;
  `RunAsync`'in parametreli dalı `CompiledAgentCache`'i ve decorator zincirini
  BİLEREK bypass ettiği için (bkz. `RunAsync` içindeki 🚨 yorumu) buraya
  dokunmak çok daha büyük bir değişiklik ister. Bugüne kadar hiçbir
  test/örnek declarative kod agent'ını `Parameters` ile birleştirmedi; biri
  birleştirirse önce burası kırılır.
- **🚨 Bir "tam değiştirme" ucunun girdi DTO'su, alan adı alan adı, yazdığı
  kayıtla karşılaştırılır — eksik her alan o uca her çağrıda SESSİZCE
  kaybolur** (2026-09-18, `HATA-S3-003` ve sınıf taraması). `PUT /api/evals
  /{name}/cases`'in `EvalCaseInput`'u `EvalCase`'in dört alanını taşımıyordu:
  `Id` (kimlik — `EvalRunDiffBuilder` iki koşumu bununla hizalar, yokluğunda
  değişmeyen bir case `Removed`+`Added` oluyor ve aynı penceredeki gerçek bir
  regresyon `--max-regressions` kapısını atlıyordu), `Parameters` (parametreli
  agent'ın case'leri her kayıttan sonra "zorunlu parametre eksik" ile düşüyordu)
  ve promosyon üçlüsü. Store katmanı `Id`'yi ve `Parameters`'ı zaten
  destekliyordu — kusur yalnız sözleşmedeydi. **Kontrol:** yeni bir `*Input`
  DTO'su yazarken hedef `record`'un alanlarını yan yana koy; taşınmayan her
  alan için "bunu kim doldurur ve tur bittiğinde hâlâ orada mı?" sorusunu
  yazılı yanıtla. Sunucunun kendi verisi (promosyon kaydı gibi) istemciden
  DEĞİL, **kimlikle** eşleşen mevcut kayıttan taşınır — istemci gönderebilseydi
  sahte köken uydurulabilirdi. K-801 · K-802.
- **Konsolun `GET → map → PUT` turu sözleşmenin ikinci yarısıdır** (aynı vaka):
  `eval-detail.tsx` case'leri okuyup girdi şekline çevirirken `id`'yi düşürüyordu,
  yani sunucu tarafı düzeltme tek başına kullanıcının gördüğü kusuru kapatmazdı.
  Bir uca alan eklerken `grep -rn "<uç yolu>" src/Tracon.UI/frontend/src/` ile
  turu kapatan ekranı da ara.
