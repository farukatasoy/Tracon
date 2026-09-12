# Model Boru Hatti Tuzaklari (dekorator · devre kesici · istisna siniflandirma)

> `ModelProviderRegistry.CreateChatClient` ile kurulan `IChatClient` zinciri:
> dekorator yazimi, `RawRepresentation`, devre kesici ve baglanti hatasi
> siniflandirmasi. SDK'ya ozgu tuzaklar icin
> [`openai-saglayici.md`](openai-saglayici.md). `ContentGuard` fail-closed
> semantigi, DI dongusu ve saglayici/rol kaydi tuzaklari icin
> [`icerik-koruma-ve-saglayici-kayit.md`](icerik-koruma-ve-saglayici-kayit.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

## Halka sirasi (Faz 48)

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

## Tuzaklar

- **🚨 `OpenAIClientOptions`/`System.ClientModel` pipeline'ı 5xx/408/429'u sessizce yeniden dener** (2026-08-02, Faz 8): Ölçüldü — sahte bir sunucu her istekte HTTP 500 döndüğünde, TEK bir `GetResponseAsync` çağrısı sunucuya **4 kez** ulaştı (1 ilk deneme + 3 otomatik yeniden deneme). Devre kesici gibi ham istek sayısına bağımlı testler `400` (yeniden denenmeyen bir istemci hatası) kullanmalı, `500` değil.
- **🚨 `HttpRequestException.Message` bağlantı hatalarında hedef adresi (host:port) gövdeye gömer** (2026-08-02, Faz 8): "`secret`/adres sızdırmaz" gereksinimi olan bir hata yolunda `.Message` kullanılamaz. `exception.HttpRequestError` (.NET 8+, enum kategori adı) adres taşımaz — `OpenAI/OpenAIProviderHealthCheck.cs` bunu kullanır.
- **🚨 Boru hattinin TAMAMINI `ModelProviderRegistry.CreateChatClient` kurar; `IModelProvider` HAM istemci dondurur** (K-320). Faz 48'e kadar dort saglayici fabrikasi `UseFunctionInvocation()` + `UseOpenTelemetry()` zincirini KENDI icinde kuruyordu ve defterin sardigi hicbir halka tool cagri dongusunun turlarini goremiyordu. Bugunku sira (distan ice):
- **🚨 Bir `IChatClient` dekoratoru, ic istemciden gelen nesneyi YERINDE DEGISTIRMEZ.** `ChatMessage`, `ChatResponseUpdate` ve `Contents` listeleri `Clone()` ile kopyalanir. Faz 48'de bir test bunu yakaladi: onceden kurulmus bir sahte istemci ayni cerceve orneklerini yeniden veriyordu ve yerinde maskeleme o ornekleri kalici olarak bozdu; bir sonraki test yanlis veriyle kostu. Onbellekleyen bir gercek istemci ayni davranisi uretir.
- **🚨 Bir mesajin metnini degistiren dekorator `RawRepresentation`'i DUSURMELIDIR** (`null` atar). Faz 26'da olculdu: Anthropic adaptoru `ChatOptions.RawRepresentationFactory` ile verilen ham nesnenin uzerine YAZMIYOR. Ham gosterim tasinirsa degisiklik sessizce etkisiz kalir ve eski metin aga cikar.
- **Engelleme kararı devre kesiciye hata olarak GITMEZ.** `CircuitBreakingChatClient` `TraconContentBlockedException`'i ayrica ayiklar (K-322); guard dongunun icinde, devre kesici disinda oldugu icin bu ayiklama zorunludur.
- **🚨 Gercek bir baglanti hatasi tek bir istisna DEGIL, IC ICE bir ZINCIRDIR; en disi asla tahmin ettigin tip degildir.** Olculdu: dinlenmeyen bir porta baglanmak `AggregateException` → `ClientResultException` → `HttpRequestException` → `SocketException` uretir (4 deneme boyunca tekrarlanir). Kural: siniflandirici ZINCIRIN TAMAMINI gezmelidir (`AggregateException.InnerExceptions` + `InnerException`, ozyinelemeli). Olcumun tamami: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Sinif adi eslemesi TEK BASINA yetmez — mesaj metni sinif kararini DEGISTIRIR.** `ClientResultException` gercek bir HTTP yaniti ALINDIYSA `"HTTP {kod} (...)"` bicimindedir; hic yanit alinamadiysa (baglanti reddi) HTTP onekini TASIMAZ. Bu yuzden durum-metni denetimi ONCE, tip-tabanli "baglanti hatasi" varsayimi SONRA calismalidir — aksi hâlde bir 401/403 yanlislikla yeniden denenir. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Bir istisna siniflandiricisi yazarken `InnerException`'i KONTROL ETMEDEN "tip eslesmedi → retry yok" deme.** `FallbackRetryClassifier`'in ilk hâli yalniz en distaki `AggregateException`'a bakti; canli bir kesintide ILK `FailureThreshold` istegin HEPSI kullaniciya ciplak hata olarak dustu. Duzeltme: `Flatten(exception)` (kendisi + `InnerException` zinciri + her `AggregateException` kolu) uzerinde gez, HER adimda ayni kurali uygula. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Onbelleklenen bir yanit KENDI kullanim/maliyet bilgisini tasir; isabet onu OLDUGU GIBI geri verirse `run` faturalanmamis token'i IKINCI kez sayar** (2026-08-22, Faz 81, gercek `samples/Tracon.Api` kosumunda olculdu — otomatik testler sahte saglayicinin varsayilan olarak kullanim bilgisi URETMEMESI yuzunden yakalamadi). `DistributedCachingChatClient`'in `store`'a yazdigi `ChatResponse`, orijinal cagrinin `Usage` ozelligini VE her mesajin `UsageContent`'ini AYNEN tasir. Bir onbellek dekoratoru yazarken "isabet maliyet yazmaz" iddiasi bir `chat` span'inin EKSIKLIGINE guvenemez — donen nesnenin kendisinden KULLANIM BILGISI SIYRILMALIDIR (`TraconResponseCachingChatClient.StripUsage` deseni: `Usage = null` + mesaj/`update` icindeki `UsageContent` ogelerini filtrele). Sadece DONDURULEN nesnede yapilir, `store`'a yazilan bayt dizisi degismez.
- **🚨 MEAI 10.9.0 kendi yedek zincirini getirdi; bizimki Faz 62'dendir ve DAHA GENISTIR.** Yukseltme olcumu (2026-08-21): `RoutingChatClient` (soyut) · `FailoverChatClient` · `OrderedFailoverChatClient` · `SemanticRoutingChatClient` · `RoutingContext` · `FailoverChatClientAttempt` eklendi. Tracon'inki yalniz "sirayla dene" degildir — devre kesici, on ucus denetimi, hata siniflandirmasi ve atif kaydiyla birlesiktir. **Onun uzerine gecmek bir KARAR isidir, bir yukseltme isi degil**; oneriden once `ModelBinding.Fallbacks`'in sozlesmesini ve K-320'nin halka sirasini oku.


## `ChatOptions.ModelId` yedek bagliya SIZAR (Faz 113, canli kosumda bulundu)

- **🚨 `AgentDefinitionCompiler.BuildChatOptions`, `ChatOptions.ModelId`'yi
  BIRINCIL binding'in modeliyle DERLEME ANINDA sabitler ve o TEK nesne her
  yeniden deneme cagrisinda TEKRAR kullanilir.** `FallbackChatClient` bir
  yedek bagliya gecerken bu AYNI `options` nesnesini degistirmeden geciriyordu
  — yedegin KENDI `ModelFallback.Model` adi (bilerek birincilden FARKLI
  olabilir, bu tipin butun amaci budur) hic devreye girmiyordu. Gercek OpenAI
  anahtariyla `samples/Tracon.Api`'de olculdu: birincili olu bir porta
  isaret eden, modeli `flaky-model` olan bir agent, modeli `gpt-5.4-mini` olan
  gercek `openai` yedegine dustu ama giden istek yine `flaky-model` adini
  tasidi — sunucu `HTTP 404 (model_not_found)` dondurdu ve zincir TAMAMEN
  tuketildi (`FallbackRetryClassifier` gorunusunun disinda, MAF'in OpenAI
  adaptoru `ChatOptions.ModelId`'yi istemcinin KENDI SDK yapilandirmasi
  yerine giden HTTP govdesine yazar). Birim testleri bunu YAKALAMIYORDU:
  `FakeChatClient` `ChatOptions`'i tamamen yok sayiyor, yalniz `responder`
  fonksiyonuna bakiyordu — farkli `primaryModel`/`fallbackModel` adlariyla
  yazilmis testler bile gercek bir SDK'nin `ModelId` okumasini hic olcmuyordu.
  Duzeltme: `FallbackChatClient.OptionsForLink` her yedek cagrisi icin
  `options.Clone()` alip `ModelId`'yi o baglinin KENDI modeliyle degistirir;
  birincil cagri (`index == 0`) `options`'i degistirmeden kullanmaya devam
  eder. Regresyon: `FallbackChatClientTests.Fallback_link_is_called_with_its_own_ModelId_not_the_primarys`
  (`FakeChatClient.LastOptions.ModelId` gercek SDK yerine dogrudan olcer).
  **Kural: birden fazla FARKLI binding'e ait istemciyi cagiran her yeni kod
  yolu, o binding'e ozgu her alani (ModelId dahil) YENIDEN insa etmelidir —
  paylasilan bir `ChatOptions`/istek nesnesi taşımaz.**

## Dispose sahipligi ve cift sarmalamanin OLCULEN hasari (Faz 99)

- **Tracon `IModelProvider.CreateChatClient`'in donusunu hicbir zaman dispose
  etmez.** Calisma anı probuyla olculdu: derlenmis agent
  `Microsoft.Agents.AI.ChatClientAgent`'tir ve ne `IDisposable` ne
  `IAsyncDisposable` uygular; bir `run` sonrasi ham istemcinin dispose sayisi
  **0**; `CompiledAgentCache.Evict` yalniz `TryRemove` yapar. Boru hattinin
  tepesi ELLE dispose edilirse zincir ham istemciye iner (sayi 1) — ama eden
  yoktur. Saglayici donen nesnenin omrunu sahiplenir ve o nesne hic dispose
  edilmemeye dayanikli olmalidir (K-609).
- **🚨 Bir saglayici kendi `UseFunctionInvocation()` dongusunu kurarsa hasar
  yanit metninde de tool cagri sayisinda da GORUNMEZ.** Olculdu (tek tool'lu bir
  `run`, sayac tutan bir `IContentGuard` ile):
  ham istemci → guard 4 denetim: `Input:hi`, `Input:hi`, **`Input:42`**, `Output:done`;
  saglayici sarmalarsa → 3 denetim: `Input:hi`, `Output:42`, `Output:done`.
  Yanit ikisinde de `done`, tool ikisinde de bir kez calisti. Tek fark: dogru
  kurulumda tool sonucu modele **girerken** denetlenir; yanlis kurulumda ic
  dongu onu guard'in ALTINDAN modele besler — K-320'nin kapattigi
  prompt-injection yolu tam olarak budur. Regresyon:
  `tests/Tracon.Core.UnitTests/Models/PipelineOwnershipTests.cs` — iddia
  guard'in gordugu YONDUR, boru hattindaki tip sayisi degil.
