# Model Boru Hatti Tuzaklari (dekorator · devre kesici · istisna siniflandirma)

> `ModelProviderRegistry.CreateChatClient` ile kurulan `IChatClient` zinciri:
> dekorator yazimi, `RawRepresentation`, devre kesici ve baglanti hatasi
> siniflandirmasi. SDK'ya ozgu tuzaklar icin
> [`openai-saglayici.md`](openai-saglayici.md).
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
- **Engelleme kararı devre kesiciye hata olarak GITMEZ.** `CircuitBreakingChatClient` `AgentPrismContentBlockedException`'i ayrica ayiklar (K-322); guard dongunun icinde, devre kesici disinda oldugu icin bu ayiklama zorunludur.
- **🚨 Gercek bir baglanti hatasi tek bir istisna DEGIL, IC ICE bir ZINCIRDIR; en disi asla tahmin ettigin tip degildir.** Olculdu: dinlenmeyen bir porta baglanmak `AggregateException` → `ClientResultException` → `HttpRequestException` → `SocketException` uretir (4 deneme boyunca tekrarlanir). Kural: siniflandirici ZINCIRIN TAMAMINI gezmelidir (`AggregateException.InnerExceptions` + `InnerException`, ozyinelemeli). Olcumun tamami: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Sinif adi eslemesi TEK BASINA yetmez — mesaj metni sinif kararini DEGISTIRIR.** `ClientResultException` gercek bir HTTP yaniti ALINDIYSA `"HTTP {kod} (...)"` bicimindedir; hic yanit alinamadiysa (baglanti reddi) HTTP onekini TASIMAZ. Bu yuzden durum-metni denetimi ONCE, tip-tabanli "baglanti hatasi" varsayimi SONRA calismalidir — aksi hâlde bir 401/403 yanlislikla yeniden denenir. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Bir istisna siniflandiricisi yazarken `InnerException`'i KONTROL ETMEDEN "tip eslesmedi → retry yok" deme.** `FallbackRetryClassifier`'in ilk hâli yalniz en distaki `AggregateException`'a bakti; canli bir kesintide ILK `FailureThreshold` istegin HEPSI kullaniciya ciplak hata olarak dustu. Duzeltme: `Flatten(exception)` (kendisi + `InnerException` zinciri + her `AggregateException` kolu) uzerinde gez, HER adimda ayni kurali uygula. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 MEAI 10.9.0 kendi yedek zincirini getirdi; bizimki Faz 62'dendir ve DAHA GENISTIR.** Yukseltme olcumu (2026-08-21): `RoutingChatClient` (soyut) · `FailoverChatClient` · `OrderedFailoverChatClient` · `SemanticRoutingChatClient` · `RoutingContext` · `FailoverChatClientAttempt` eklendi. AgentPrism'inki yalniz "sirayla dene" degildir — devre kesici, on ucus denetimi, hata siniflandirmasi ve atif kaydiyla birlesiktir. **Onun uzerine gecmek bir KARAR isidir, bir yukseltme isi degil**; oneriden once `ModelBinding.Fallbacks`'in sozlesmesini ve K-320'nin halka sirasini oku.

