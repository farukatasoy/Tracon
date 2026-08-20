# Model Saglayici Tuzaklari (OpenAI · Anthropic · Google · Azure)

> Tip adlari, yeniden deneme, model kimlikleri, hata sizintisi, saglayiciya ozgu ayarlar.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **🚨 Model adlarını bilgiden yazma, doğrula** (2026-08-02): Faz 3'te bilgiye dayanarak yazılan yerleşik katalog gerçek hesabın modellerinin hiçbirini içermiyordu; `gpt-4.1-mini` `HTTP 403 model_not_found` döndü. Gerçek liste `curl https://api.openai.com/v1/models -H "Authorization: Bearer $KEY"` ile alınır ve yalnızca `id` döner — context penceresi/fiyat yoktur. Katalog artık yapılandırmadan gelir (K-032).
- **OpenAI tip adları tahmin edilemez** (2026-08-02): `ResponsesClient` (`OpenAIResponseClient` **değil**), `OpenAIClientOptions.OrganizationId` (`Organization` değil), `NetworkTimeout` (`Timeout` değil). `GetResponsesClient()` model parametresi **almaz**; model `AsIChatClient(model)` tarafına geçer.
- **`OpenAIClient.Endpoint` bile `OPENAI001`** (2026-08-02): testte doğrulamak için bastırma gerekir. Bunun yerine `IChatClient.GetService(typeof(ChatClientMetadata)).ProviderUri` kullan.
- **🚨 `OpenAIClientOptions`/`System.ClientModel` pipeline'ı 5xx/408/429'u sessizce yeniden dener** (2026-08-02, Faz 8): Ölçüldü — sahte bir sunucu her istekte HTTP 500 döndüğünde, TEK bir `GetResponseAsync` çağrısı sunucuya **4 kez** ulaştı (1 ilk deneme + 3 otomatik yeniden deneme). Devre kesici gibi ham istek sayısına bağımlı testler `400` (yeniden denenmeyen bir istemci hatası) kullanmalı, `500` değil.
- **🚨 `HttpRequestException.Message` bağlantı hatalarında hedef adresi (host:port) gövdeye gömer** (2026-08-02, Faz 8): "`secret`/adres sızdırmaz" gereksinimi olan bir hata yolunda `.Message` kullanılamaz. `exception.HttpRequestError` (.NET 8+, enum kategori adı) adres taşımaz — `OpenAI/OpenAIProviderHealthCheck.cs` bunu kullanır.
- **OpenRouter model kimlikleri satıcı önekiyle gelir** (2026-08-02, Faz 8): `gpt-5.4-mini` değil `openai/gpt-5.4-mini`. Ölçüldü: `curl https://openrouter.ai/api/v1/models` ile doğrulanmadan model adı tahmin edilirse (K-032'nin aynı dersi) `model_not_found` benzeri bir hata alınır.
- **🚨 OpenRouter'ın kredi kontrolü `max_tokens`'i "en kötü durum" maliyeti sayar** (2026-08-02, Faz 8): Varsayılan `max_tokens` (65536, MAF/OpenAI istemcisinin kendi varsayılanı) düşük bakiyeli bir anahtarla gerçek bir `HTTP 402 (insufficient credits)` üretti — AgentPrism'in hatası değil, hesap kısıtı. `ModelBinding.MaxOutputTokens` ile makul bir üst sınır vermek çözer.
- **`ModelDescriptor` fiyat alanlarını faz 3'ten beri taşıyor** (`InputCostPerMillionTokens`, `OutputCostPerMillionTokens`) ve **hiçbir yerde okunmuyor**. Faz 20 maliyeti buradan çözecek.

## Anthropic ve Google (Faz 26, 2026-08-05)

- **🚨 Bir SDK'nin ham gosterimini kullanirken alanin uzerine yazilip yazilmadigini OLC.** `ChatOptions.RawRepresentationFactory` ile verilen nesneyi Anthropic adaptoru **oldugu gibi** kullanir; `model`/`max_tokens` uzerine YAZMAZ (yer tutucu model adiyla olculdu). Bu yuzden `AnthropicProviderSettingsChatClient` model adini ve token sinirini kendisi yazar. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`MessageCreateParams.Thinking` ve `.CacheControl` INIT-ONLY'dir.** Reflection `{ get; set; }` gosterir ama derleyici `CS8852` verir. Kosullu alan yazmak icin `MessageCreateParams.FromRawUnchecked(header, query, body)` kullanilir. `body` sozlugunde `messages` anahtari **bulunmali** (bos dizi yeter); yoksa SDK istemci tarafinda `'messages' cannot be absent` der. Adaptor gercek mesajlari onun uzerine yazar.
- **`null!` atamak "alani gonderme" demek DEGILDIR.** `Thinking = null!` yazmak govdeye `"thinking": null` koyar ve API `Input should be an object` ile reddeder. Ayarlanmayacak alan hic yazilmamalidir.
- **🚨 Anthropic'te `max_tokens` ZORUNLUDUR.** OpenAI'da atlanabilir; Anthropic'te atlanamaz. `AnthropicProviderOptions.DefaultMaxOutputTokens` (varsayilan 4096) bu yuzden var ve `null` olamaz.
- **Anthropic'te dusunme acikken `temperature` yalnizca 1 olabilir.** Baska bir deger `invalid_request_error` verir. Dusunme butcesi ayrica `max_tokens`'tan kucuk olmalidir.
- **Google'in "enum"lari enum DEGILDIR.** `HarmCategory`, `HarmBlockThreshold`, `FinishReason` string tasiyan struct'lardir; `Enum.GetNames` `ArgumentException` atar. `AllValues` ile listelenir, `Value` ile karsilastirilir; string'ten ortuk donusum operatoru vardir.
- **Gemini model adlari hizli eskir.** Olculdu: `gemini-2.5-flash` cagrisi *"This model is no longer available to new users"* dondu. K-032'nin somut bedeli; gercek liste `curl "https://generativelanguage.googleapis.com/v1beta/models" -H "x-goog-api-key: $KEY"` ile alinir.
- **Gemini model listesi kaynak yolu tasir.** `models/gemini-3.6-flash` doner; `ModelBinding.Model` oneksiz ad bekler, saglik denetimi onegi temizler.
- **Google.GenAI istemcisi `IDisposable`'dir** (`HttpClient` tasir). Fabrikanin da `IDisposable` olmasi gerekti; `OpenAIClient`'ta bu gerekmiyordu. Taban adres `HttpOptions.BaseUrl` ile verilir — `Client.setDefaultBaseUrl` **statiktir** ve surec genelinde durum degistirir, kullanilmaz.
- **Guvenlik filtresi bos yanit + `ChatFinishReason.ContentFilter` uretir.** Ikisi de (Anthropic refusal, Gemini SAFETY) ayni MEAI degerine eslenir; bu yuzden tespit tek bir Core dekoratorunde toplandi (K-206). Dekorator devre kesicinin **disindadir** — filtrelenmis yanit saglayici arizasi degildir.
- **Iki resmi SDK da `ChatOptions.RawRepresentationFactory` okur.** Metadata uye referanslarindan dogrulanabilir: `System.Reflection.Metadata` ile `MemberReferences` icinde `get_RawRepresentationFactory` aranir. Bu, gercek cagri yapmadan "kacis kapisi var mi?" sorusunu cevaplar.

## Azure OpenAI (Faz 27, 2026-08-05)

- **🚨 Bir SDK baska bir SDK'nin tipini genisletiyorsa, surum kaymasini CALISMA ANINDA olc** (K-211): `Azure.AI.OpenAI` 2.1.0 `OpenAI` 2.1.0'a karsi derlendi, biz 2.12.0 kullaniyoruz; NuGet cakismayi sessizce cozer ve `dotnet build` **sifir uyari** verir ama `AzureChatExtensions`'in istek tarafi metotlarinin TAMAMI `MissingMethodException` atar. Derleme yesilligi burada hicbir sey kanitlamaz. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Temel sohbet yolu ise saglam.** Olculdu: `POST {endpoint}/openai/deployments/{deployment}/chat/completions?api-version=2024-10-21`, `api-key` basligi, govdede `max_completion_tokens` (OpenAI 2.12.0 bu adi kendisi kullanir — Azure uzantisina gerek yok).
- **🚨 Azure'da `ModelBinding.Model` MODEL adi degil DEPLOYMENT adi tasir.** Ad istegin YOLUNA girer; yanlis ad `model_not_found` degil **HTTP 404** verir. Ayni model iki kaynakta iki farkli adla konuslandirilmis olabilir; adi kaynagi kuran kisi secer. Ayar adi bu yuzden `DefaultDeployment`, `DefaultModel` degil.
- **Yonetilen kimlik `Azure.Identity` GEREKTIRMEZ** (K-210): `AzureOpenAIClient(Uri, TokenCredential, ...)` kurucusu vardir ve `TokenCredential` `Azure.Core`'dadir; AgentPrism yalniz `Azure.Core`'a baglanir, `Func<TokenCredential>` tuketiciden gelir. Token `scope`'u `AzureOpenAIAudience.AzurePublicCloud` = `https://cognitiveservices.azure.com/.default` — TAM metindir, `/.default` EKLENMEZ. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`AzureOpenAIClientOptions.ServiceVersion` yalniz iki deger tasir** (`V2024_06_01`, `V2024_10_21`) ve parametresiz kurucu sonuncusunu secer. Yeni bir API surumu icin SDK yukseltilir; AgentPrism bu enum'u public yuzeyine almadi.
- **`AzureOpenAIClient.GetResponsesClient()` Azure'a ozgu DEGILDIR.** `OpenAI.OpenAIClient+TopLevelResponsesClient` doner (karsilastirma: `GetChatClient()` → `Azure.AI.OpenAI.Chat.AzureChatClient`). Responses yuzeyi bu yuzden desteklenmiyor (K-213).
- **`Microsoft.Agents.AI.Foundry` 1.5.0 MAF 1.16.0 ile YUKLENIR** (olculdu; tip yukleme hatasi yok) ama **37 gecisli paket** getirir — `Azure.AI.Projects`, `Azure.Storage.Blobs`, `Azure.Identity`, `Google.Protobuf`, `Microsoft.ML.Tokenizers`… Karsilastirma: `Google.GenAI` 11. Ertelendi (K-212). Kimligi `Azure.Core.TokenCredential` degil, `System.ClientModel`'in `AuthenticationTokenProvider`'idir.

## Sağlayıcı paketlerinin dosya haritası

- **OpenAI istemci kurulumu tek dosyada** (2026-08-02): `OpenAI/OpenAIChatClientFactory.cs`. `OpenAIClient` bir kez kurulur, iki sağlayıcı (`openai`, `openai-responses`) paylaşır. Boru hattı (`UseFunctionInvocation` + `UseOpenTelemetry`) da burada.
- **Adlandırılmış OpenAI uyumlu sağlayıcılar tek dosyada** (2026-08-02, Faz 8): `OpenAI/OpenAICompatibleProviderExtensions.cs`. `OpenAIChatClientFactory` ad başına `OpenAI/OpenAINamedChatClientFactoryCache.cs` içinde önbelleklenir; anahtarsız (yerel) örnekler sabit bir yer tutucu kimlikle kurulur.
- **Her sağlayıcı SDK'sı kendi paketinde izole** (2026-08-05, Faz 26/27): `Anthropic/AnthropicChatClientFactory.cs`, `Google/GoogleChatClientFactory.cs` ve `Azure/AzureOpenAIChatClientFactory.cs` — dördü de aynı boru hattını (`UseFunctionInvocation` + `UseOpenTelemetry`) kurar. Paketler birbirini görmez; kural `DependencyDirectionTests.AllowedReferences` ile korunur.
- **Saglayiciya ozgu ayarlar tek yardimcidan okunur** (Faz 26): `Abstractions/Agents/ModelProviderSettings.cs` dogrulama + tipli okuma yapar; her saglayici yalniz onek sabitini ve `*ProviderNames.SupportedSettings`'i yazar. Ayarlar `RawRepresentationFactory` ile gonderilir, her pakette bir `*ProviderSettingsChatClient` dekoratoru vardir — **`AgentPrism.Azure` haric** (K-211). Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `UseOpenAI()` sabit `AgentPrism:Providers:OpenAI` bolumune baglidir, `UseOpenAICompatible()` DEGILDIR** (2026-08-06, Faz 33, K-249): ikincisi ayarlari KODDA alir, sabit bir bolum yolu yoktur. `IModelProviderConfigurationDiagnostics` bu farki `configurationSectionKey: string?` ile ayirt eder; `UseOpenAICompatible()` `null` gecer ve hicbir `ConfigurationDiagnostic` uretmez — aksi hâlde yanlis anahtar adi gosterilirdi. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Model boru hatti (Faz 48, 2026-08-07)

- **🚨 Boru hattinin TAMAMINI `ModelProviderRegistry.CreateChatClient` kurar; `IModelProvider` HAM istemci dondurur** (K-320). Faz 48'e kadar dort saglayici fabrikasi `UseFunctionInvocation()` + `UseOpenTelemetry()` zincirini KENDI icinde kuruyordu ve defterin sardigi hicbir halka tool cagri dongusunun turlarini goremiyordu. Bugunku sira (distan ice):

  ```
  ContentFilterDetectingChatClient   (saglayici filtresi tespiti)
    -> devre kesici
      -> AttachmentResolvingChatClient
        -> FunctionInvokingChatClient  (MAF tool cagri dongusu)
          -> OpenTelemetry
            -> ContentGuardingChatClient
              -> saglayicinin HAM istemcisi
  ```

  Yeni bir halka eklerken tek soru sudur: **her model cagrisini gormesi gerekiyor mu?** Gerekiyorsa dongunun ICINE (guard gibi), agent turu basina bir kez yetiyorsa DISINA (devre kesici, ek cozme gibi) konur. Devre kesici bilerek disarida: iceri alinsaydi sayim granulerligi agent turundan gercek ag cagrisina kayar ve `FailureThreshold`'un anlami degisirdi.
- **🚨 Bir `IChatClient` dekoratoru, ic istemciden gelen nesneyi YERINDE DEGISTIRMEZ.** `ChatMessage`, `ChatResponseUpdate` ve `Contents` listeleri `Clone()` ile kopyalanir. Faz 48'de bir test bunu yakaladi: onceden kurulmus bir sahte istemci ayni cerceve orneklerini yeniden veriyordu ve yerinde maskeleme o ornekleri kalici olarak bozdu; bir sonraki test yanlis veriyle kostu. Onbellekleyen bir gercek istemci ayni davranisi uretir.
- **🚨 Bir mesajin metnini degistiren dekorator `RawRepresentation`'i DUSURMELIDIR** (`null` atar). Faz 26'da olculdu: Anthropic adaptoru `ChatOptions.RawRepresentationFactory` ile verilen ham nesnenin uzerine YAZMIYOR. Ham gosterim tasinirsa degisiklik sessizce etkisiz kalir ve eski metin aga cikar.
- **Engelleme kararı devre kesiciye hata olarak GITMEZ.** `CircuitBreakingChatClient` `AgentPrismContentBlockedException`'i ayrica ayiklar (K-322); guard dongunun icinde, devre kesici disinda oldugu icin bu ayiklama zorunludur.

## Model yedek zinciri hata siniflandirmasi (Faz 62, 2026-08-18)

- **🚨 Gercek bir baglanti hatasi tek bir istisna DEGIL, IC ICE bir ZINCIRDIR; en disi asla tahmin ettigin tip degildir.** Olculdu: dinlenmeyen bir porta baglanmak `AggregateException` → `ClientResultException` → `HttpRequestException` → `SocketException` uretir (4 deneme boyunca tekrarlanir). Kural: siniflandirici ZINCIRIN TAMAMINI gezmelidir (`AggregateException.InnerExceptions` + `InnerException`, ozyinelemeli). Olcumun tamami: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Sinif adi eslemesi TEK BASINA yetmez — mesaj metni sinif kararini DEGISTIRIR.** `ClientResultException` gercek bir HTTP yaniti ALINDIYSA `"HTTP {kod} (...)"` bicimindedir; hic yanit alinamadiysa (baglanti reddi) HTTP onekini TASIMAZ. Bu yuzden durum-metni denetimi ONCE, tip-tabanli "baglanti hatasi" varsayimi SONRA calismalidir — aksi hâlde bir 401/403 yanlislikla yeniden denenir. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Bir istisna siniflandiricisi yazarken `InnerException`'i KONTROL ETMEDEN "tip eslesmedi → retry yok" deme.** `FallbackRetryClassifier`'in ilk hâli yalniz en distaki `AggregateException`'a bakti; canli bir kesintide ILK `FailureThreshold` istegin HEPSI kullaniciya ciplak hata olarak dustu. Duzeltme: `Flatten(exception)` (kendisi + `InnerException` zinciri + her `AggregateException` kolu) uzerinde gez, HER adimda ayni kurali uygula. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Kiraci saglayici anahtarlari / BYOK (Faz 65, 2026-08-19)

- **🚨 Google SDK'si `ChatClientMetadata.ProviderUri`'de ozel `HttpOptions.BaseUrl`'i YANSITMAZ** — olculdu: hep sabit `generativelanguage.googleapis.com` doner; diger uc saglayici dogru yansitir. `GoogleModelProviderCredentialTests` bu yuzden yalniz istemcinin uretildigini dogrular.
- **Dort SDK istemcisi de kurulumda BIR KEZ insa edilip paylasilir** (olculdu); kiraci kimlik bilgisi onu kullanamaz. Paylasilan `ProviderCredentialClientCache<TFactory>` (`AgentPrism.Core`, `OpenAINamedChatClientFactoryCache`'in genellenmisi) her paket icin ayri fabrika uretir/onbellekler; `GoogleChatClientFactory` (`IDisposable`) hic elden cikarilmaz — sinirli, kabul edilmis sizinti.
