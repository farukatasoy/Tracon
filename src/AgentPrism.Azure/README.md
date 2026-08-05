# AgentPrism.Azure

AgentPrism icin Azure OpenAI saglayici adaptoru.

```csharp
builder.AddAgentPrism()
       .UseAzureOpenAI(new Uri("https://benim-kaynagim.openai.azure.com/"), apiKey, o =>
       {
           o.DefaultDeployment = "uretim-gpt";
       });
```

Kaydedilen saglayici adi: **`azure-openai`** (`AzureOpenAIProviderNames.AzureOpenAI`).

Uretilen her `IChatClient` `AgentPrism.OpenAI` ile ayni boru hattindan gecer:
`UseFunctionInvocation()` tool cagri dongusunu Microsoft Agent Framework'e birakir,
`UseOpenTelemetry()` span'leri `AgentPrism` kaynagi altinda uretir. Devre kesici ve
icerik filtresi tespiti `ModelProviderRegistry` duzeyindedir; bu paket ikisini de
ek kod yazmadan alir.

## 🚨 Deployment ≠ model

Azure'da cagrilan sey **model adi degil, deployment adidir**. Deployment adini
Azure kaynagini kuran kisi secer; ayni model iki kaynakta iki farkli adla
konuslandirilmis olabilir.

```csharp
Model = new ModelBinding
{
    Provider = AzureOpenAIProviderNames.AzureOpenAI,
    Model = "uretim-gpt",     // DEPLOYMENT adi. "gpt-5.4-mini" DEGIL.
}
```

Ad istegin yoluna girer:

```
POST {endpoint}/openai/deployments/{deployment}/chat/completions?api-version=2024-10-21
```

Bu yuzden yanlis bir ad, model bulunamadi hatasi degil **HTTP 404** verir.
Alan bos birakildiginda hata mesaji da bunu soyler.

## Kimlik dogrulama

Iki yol vardir. Ikisi de verildiginde **yonetilen kimlik kazanir**.

### API anahtari

```csharp
.UseAzureOpenAI(endpoint, apiKey)
```

Anahtar bir sirdir; `appsettings.json`'a **yazilmaz**, `dotnet user-secrets`
kullanilir.

### Microsoft Entra (yonetilen kimlik)

AgentPrism'in "sir saklamama" durusuyla en iyi ortusen yoldur: API anahtari hic
yoktur.

```csharp
// Tuketicinin projesinde:
//   <PackageReference Include="Azure.Identity" Version="..." />
.UseAzureOpenAI(o =>
{
    o.Endpoint = new Uri("https://benim-kaynagim.openai.azure.com/");
    o.CredentialFactory = static () => new DefaultAzureCredential();
    o.DefaultDeployment = "uretim-gpt";
})
```

🚨 **`Azure.Identity` bu paketin bagimliligi DEGILDIR.** Paket yalnizca
`Azure.Core` soyutlamasina (`TokenCredential`) baglanir; kimligi tuketici secer ve
yonetilen kimlik kullanmayan tuketici `Azure.Identity` zincirini hic almaz.

Egemen bulutlar icin token kapsami degisir:

```csharp
o.Audience = "https://cognitiveservices.azure.us/.default";   // Azure Government
```

Varsayilan: `https://cognitiveservices.azure.com/.default`.

## Saglayiciya ozgu ayarlar — yoktur

`ModelBinding.ProviderSettings` icinde bu saglayici **hicbir anahtar
desteklemez**. Tanimli bir anahtar geldiginde derleme acik bir hatayla durur.

Sebep olculdur (2026-08-05): Azure'un sohbet istegine ek alan yazan tek yol
`Azure.AI.OpenAI.Chat.AzureChatExtensions`'tir (`AddDataSource`,
`SetNewMaxCompletionTokensPropertyEnabled`, `GetDataSources`) ve bu uzantilarin
**tamami** kullandigimiz OpenAI SDK surumuyle calisma aninda
`MissingMethodException` verir. Calismayan bir ayari sunmak, hic sunmamaktan
kotudur. Ayrinti: `docs/KARARLAR.md`, karar K-211.

`max_completion_tokens` alani zaten dogru gonderilir — OpenAI SDK'si bu adi
kendisi kullanir, Azure uzantisina gerek yoktur (olculdu).

## Kullanilan SDK

Resmi [`Azure.AI.OpenAI`](https://www.nuget.org/packages/Azure.AI.OpenAI) paketi
(sahip: Microsoft, MIT). Paket OpenAI SDK'sinin ustune yalnizca **yonlendirmeyi**
ekler: deployment yolu, `api-version` sorgu parametresi ve `api-key` / Entra
kimligi. Mesaj eslemesi, akis, tool cagrisi ve kullanim sayaclari OpenAI SDK'sinin
kendi kodundan gelir.

Geciseli bagimliliklar: `Azure.Core`, `OpenAI`, `System.ClientModel`,
`System.Memory.Data`, `Microsoft.Bcl.AsyncInterfaces`. Paket AOT uyumludur
(`IsAotCompatible=true`, sifir uyari).

### Desteklenmeyen yuzeyler

- **Responses API.** `AzureOpenAIClient.GetResponsesClient()` Azure'a ozgu bir
  istemci **dondurmez**; OpenAI'in taban sinifini doner ve Azure'un yol/`api-version`
  sekline uydugu dogrulanamaz. Bu paket yalnizca Chat Completions kullanir.
- **Azure OpenAI On Your Data** (`AddDataSource`) — yukaridaki calisma ani kirilmasi.
- **Azure AI Foundry Agents.** Ayri bir yetenektir (`IAgentSource`, `IModelProvider`
  degil) ve ayri bir pakete birakildi; bkz. `docs/27-AZURE-FOUNDRY.md`.

## Saglik denetimi

`GET {endpoint}/openai/models?api-version=2024-10-21` ucuna gider, **ucret
uretmez**. Kimlik dogrulama `api-key` basligi veya Entra `Bearer` token'i ile
yapilir. Hata detayi HTTP durum kodu ve kisa nedenle sinirlidir; API anahtari ve
kaynak adresi **sizmaz**.

```
GET /agentprism/api/models/health/azure-openai
```

🚨 Donen liste **model** listesidir, deployment listesi degildir. Denetimin
kanitladigi sey sudur: adres dogru, kimlik gecerli, kaynak ayakta. Deployment
adinin dogrulugu ilk gercek cagrida anlasilir.

## Model katalogu

AgentPrism yerlesik model listesi tasimaz (karar K-032). Katalog tamamen
yapilandirmadan gelir ve **bir dogrulama listesi degildir** — burada olmayan bir
deployment adi da kullanilabilir. Girdilerin `Name` alani **deployment adidir**.

```json
{
  "AgentPrism": {
    "Providers": {
      "AzureOpenAI": {
        "Endpoint": "https://benim-kaynagim.openai.azure.com/",
        "ApiKey": "",
        "DefaultDeployment": "uretim-gpt",
        "Models": [
          { "Name": "uretim-gpt", "DisplayName": "Uretim (gpt-5.4-mini)",
            "ContextWindowTokens": 128000,
            "InputCostPerMillionTokens": 0.15, "OutputCostPerMillionTokens": 0.6 }
        ]
      }
    }
  }
}
```

`ApiKey` **asla** bu dosyaya yazilmaz — `dotnet user-secrets` kullanilir.
`CredentialFactory` bir delegate'tir ve yapilandirmadan okunmaz; kodda verilir.
