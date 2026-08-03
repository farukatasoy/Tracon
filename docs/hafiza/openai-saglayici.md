# OpenAI ve Uyumlu Saglayici Tuzaklari

> Tip adlari, yeniden deneme, model kimlikleri, hata sizintisi.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **🚨 Model adlarını bilgiden yazma, doğrula** (2026-08-02): Faz 3'te bilgiye dayanarak yazılan yerleşik katalog gerçek hesabın modellerinin hiçbirini içermiyordu; `gpt-4.1-mini` `HTTP 403 model_not_found` döndü. Gerçek liste `curl https://api.openai.com/v1/models -H "Authorization: Bearer $KEY"` ile alınır ve yalnızca `id` döner — context penceresi/fiyat yoktur. Katalog artık yapılandırmadan gelir (K-032).
- **OpenAI tip adları tahmin edilemez** (2026-08-02): `ResponsesClient` (`OpenAIResponseClient` **değil**), `OpenAIClientOptions.OrganizationId` (`Organization` değil), `NetworkTimeout` (`Timeout` değil). `GetResponsesClient()` model parametresi **almaz**; model `AsIChatClient(model)` tarafına geçer.
- **`OpenAIClient.Endpoint` bile `OPENAI001`** (2026-08-02): testte doğrulamak için bastırma gerekir. Bunun yerine `IChatClient.GetService(typeof(ChatClientMetadata)).ProviderUri` kullan.
- **🚨 `OpenAIClientOptions`/`System.ClientModel` pipeline'ı 5xx/408/429'u sessizce yeniden dener** (2026-08-02, Faz 8): Ölçüldü — sahte bir sunucu her istekte HTTP 500 döndüğünde, TEK bir `GetResponseAsync` çağrısı sunucuya **4 kez** ulaştı (1 ilk deneme + 3 otomatik yeniden deneme). Devre kesici gibi ham istek sayısına bağımlı testler `400` (yeniden denenmeyen bir istemci hatası) kullanmalı, `500` değil.
- **🚨 `HttpRequestException.Message` bağlantı hatalarında hedef adresi (host:port) gövdeye gömer** (2026-08-02, Faz 8): "sır/adres sızdırmaz" gereksinimi olan bir hata yolunda `.Message` kullanılamaz. `exception.HttpRequestError` (.NET 8+, enum kategori adı) adres taşımaz — `OpenAI/OpenAIProviderHealthCheck.cs` bunu kullanır.
- **OpenRouter model kimlikleri satıcı önekiyle gelir** (2026-08-02, Faz 8): `gpt-5.4-mini` değil `openai/gpt-5.4-mini`. Ölçüldü: `curl https://openrouter.ai/api/v1/models` ile doğrulanmadan model adı tahmin edilirse (K-032'nin aynı dersi) `model_not_found` benzeri bir hata alınır.
- **🚨 OpenRouter'ın kredi kontrolü `max_tokens`'i "en kötü durum" maliyeti sayar** (2026-08-02, Faz 8): Varsayılan `max_tokens` (65536, MAF/OpenAI istemcisinin kendi varsayılanı) düşük bakiyeli bir anahtarla gerçek bir `HTTP 402 (insufficient credits)` üretti — AgentPrism'in hatası değil, hesap kısıtı. `ModelBinding.MaxOutputTokens` ile makul bir üst sınır vermek çözer.
- **`ModelDescriptor` fiyat alanlarını faz 3'ten beri taşıyor** (`InputCostPerMillionTokens`, `OutputCostPerMillionTokens`) ve **hiçbir yerde okunmuyor**. Faz 20 maliyeti buradan çözecek.
