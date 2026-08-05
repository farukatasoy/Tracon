# AgentPrism.Google

AgentPrism icin Google Gemini saglayici adaptoru.

```csharp
builder.AddAgentPrism()
       .UseGoogle(apiKey, o => o.DefaultModel = "gemini-3.6-flash");
```

Kaydedilen saglayici adi: **`google`** (`GoogleProviderNames.Google`).

```csharp
Model = new ModelBinding
{
    Provider = GoogleProviderNames.Google,
    Model = "gemini-3.6-flash",
}
```

Ad `gemini` degil `google`'dir: ayni paket ileride Vertex AI'yi de kapsayabilir ve
model ailesinin adina kilitlenmemelidir.

Uretilen her `IChatClient` `AgentPrism.OpenAI` ile ayni boru hattindan gecer:
`UseFunctionInvocation()` tool cagri dongusunu Microsoft Agent Framework'e birakir,
`UseOpenTelemetry()` span'leri `AgentPrism` kaynagi altinda uretir. Devre kesici ve
icerik filtresi tespiti `ModelProviderRegistry` duzeyindedir.

## Kullanilan SDK ve bagimlilik agirligi

Resmi [`Google.GenAI`](https://www.nuget.org/packages/Google.GenAI) paketi (sahip:
Google LLC, Apache-2.0). Paket kendi `AsIChatClient` adaptorunu tasir.

🚨 **Bu paket digerlerinden agirdir.** `Google.Apis.Auth` uzerinden `Newtonsoft.Json`,
`System.Management` ve `System.CodeDom` gecisli olarak gelir. Agirlik bilerek kabul
edildi ve bu paketin icinde izole tutuldu: Gemini kullanmayan bir tuketici hicbirini
almaz. Diger AgentPrism paketleri bu bagimliliklardan etkilenmez.

Paket AOT uyumludur (`IsAotCompatible=true`, sifir uyari).

## Saglayiciya ozgu ayarlar

`ModelBinding.ProviderSettings` sozlugu ile agent basina verilir. **Bu listede
olmayan bir anahtar sessizce yok sayilmaz** — derleme hatasi verir ve mesaj
desteklenen anahtarlari yazar.

| Anahtar | Tip | Aciklama |
|---|---|---|
| `google.safety.harassment` | metin | Taciz esigi |
| `google.safety.hateSpeech` | metin | Nefret soylemi esigi |
| `google.safety.sexuallyExplicit` | metin | Cinsel icerik esigi |
| `google.safety.dangerousContent` | metin | Tehlikeli icerik esigi |
| `google.safety.civicIntegrity` | metin | Sivil butunluk esigi |
| `google.thinking.budgetTokens` | tam sayi | Dusunme butcesi, `[-1, 65535]` |
| `google.thinking.includeThoughts` | mantiksal | Dusunme ozeti yanitta dondurulsun mu |

Gecerli esik degerleri: `BLOCK_LOW_AND_ABOVE`, `BLOCK_MEDIUM_AND_ABOVE`,
`BLOCK_ONLY_HIGH`, `BLOCK_NONE`, `OFF`. Taninmayan bir deger derleme hatasi verir
ve gecerli degerleri listeler.

```csharp
ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
{
    ["google.safety.harassment"] = JsonSerializer.SerializeToElement("BLOCK_ONLY_HIGH"),
    ["google.thinking.budgetTokens"] = JsonSerializer.SerializeToElement(512),
}
```

## Guvenlik filtresi bos yanit uretir

🚨 Gemini'nin guvenlik filtresi devreye girdiginde yanit **bos** gelir ve bitis
sebebi `content_filter` olur. AgentPrism bunu "basarili ama bos" saymaz: calistirma
`Failed` durumuyla ve `RunError.Type = "content_filtered"` ile kaydedilir. Sessiz bos
yanit, hata ayiklamasi en zor durumdur.

Tespit `AgentPrism.Core` icindeki ortak dekoratordedir; model metin urettikten sonra
kesildiyse (kismi cevap) hata atilmaz — elde kullanilabilir bir cevap vardir.

## Bilinen davranis farklari

- **Model adlari hizli eskir.** Olculdu (2026-08-05): `gemini-2.5-flash` cagrisi
  *"This model is no longer available to new users"* dondu. Katalog yapilandirmadan
  gelir (karar K-032) ve bir dogrulama listesi degildir.
- **Model listesi kaynak yolu tasir.** Saglik ucu `models/gemini-3.6-flash` yerine
  `gemini-3.6-flash` doner; onek temizlenir.
- **Sistem mesaji ayri bir alandir** (`systemInstruction`); donusumu SDK yapar.
- **Akista kullanim sayaclari sonda gelir.**

## Saglik denetimi

`GET {endpoint}/{apiVersion}/models` ucuna gider, **ucret uretmez**. Kimlik
dogrulamasi `x-goog-api-key` basligi ile yapilir — anahtar sorgu dizesine bilerek
konmaz, cunku sorgu dizeleri vekil sunucu ve erisim gunluklerine duz metin yazilir.

```
GET /agentprism/api/models/health/google
```

## Model katalogu

```json
{
  "AgentPrism": {
    "Providers": {
      "Google": {
        "ApiKey": "",
        "DefaultModel": "gemini-3.6-flash",
        "Models": [
          { "Name": "gemini-3.6-flash", "DisplayName": "Gemini 3.6 Flash",
            "ContextWindowTokens": 1048576, "MaxOutputTokens": 65536,
            "SupportsReasoning": true }
        ]
      }
    }
  }
}
```

`ApiKey` **asla** bu dosyaya yazilmaz — `dotnet user-secrets` kullanilir.
