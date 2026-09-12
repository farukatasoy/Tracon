# Tracon.Azure

Azure OpenAI provider adapter for Tracon.

## Image generation

After `UseAzureOpenAI(...)`, call `UseAzureOpenAIImages(...)` to use the same
authenticated Azure client for image generation. `Model` is the Azure image deployment
name, not a public model name.

```csharp
tracon.UseAzureOpenAIImages(options =>
{
    options.Enabled = true;
    options.Model = "image-deployment";
});
```

The generator key is `azure-openai`. Configure prices under
`Tracon:Pricing:Images:azure-openai` when cost reporting is required.

```csharp
builder.AddTracon()
       .UseAzureOpenAI(new Uri("https://my-resource.openai.azure.com/"), apiKey, o =>
       {
           o.DefaultDeployment = "production-gpt";
       });
```

Registered provider name: **`azure-openai`** (`AzureOpenAIProviderNames.AzureOpenAI`).

Every `IChatClient` produced goes through the same pipeline as `Tracon.OpenAI`:
`UseFunctionInvocation()` leaves the tool-call loop to the Microsoft Agent Framework,
`UseOpenTelemetry()` produces spans under the `Tracon` source. Circuit breaker and
content filter detection live at the `ModelProviderRegistry` level; this package gets
both without any extra code.

## Deployment is not the model

What gets called in Azure is **the deployment name, not the model name**. The
deployment name is chosen by whoever provisioned the Azure resource; the same model
can be deployed under two different names in two different resources.

```csharp
Model = new ModelBinding
{
    Provider = AzureOpenAIProviderNames.AzureOpenAI,
    Model = "production-gpt",     // DEPLOYMENT name. NOT "gpt-5.4-mini".
}
```

The name goes into the request path:

```
POST {endpoint}/openai/deployments/{deployment}/chat/completions?api-version=2024-10-21
```

This is why a wrong name produces **HTTP 404**, not a "model not found" error.
When the field is left empty, the error message says so too.

## Authentication

There are two ways. If both are provided, **managed identity wins**.

### API key

```csharp
.UseAzureOpenAI(endpoint, apiKey)
```

The key is a secret; it is **never** written to `appsettings.json` — use
`dotnet user-secrets`.

### Microsoft Entra (managed identity)

The path that best matches Tracon's "no secrets stored" stance: there is no API
key at all.

```csharp
// In the consumer's project:
//   <PackageReference Include="Azure.Identity" Version="..." />
.UseAzureOpenAI(o =>
{
    o.Endpoint = new Uri("https://my-resource.openai.azure.com/");
    o.CredentialFactory = static () => new DefaultAzureCredential();
    o.DefaultDeployment = "production-gpt";
})
```

**`Azure.Identity` is NOT a dependency of this package.** The package only binds to
the `Azure.Core` abstraction (`TokenCredential`); the consumer chooses the credential,
and a consumer that doesn't use managed identity never pulls in the `Azure.Identity`
chain at all.

Token scope changes for sovereign clouds:

```csharp
o.Audience = "https://cognitiveservices.azure.us/.default";   // Azure Government
```

Default: `https://cognitiveservices.azure.com/.default`.

## Provider-specific settings — none

This provider supports **no keys at all** in `ModelBinding.ProviderSettings`. If a
defined key is present, the build stops with a clear error.

The reason is measured: the only way to write extra fields onto an
Azure chat request is `Azure.AI.OpenAI.Chat.AzureChatExtensions` (`AddDataSource`,
`SetNewMaxCompletionTokensPropertyEnabled`, `GetDataSources`), and **all** of these
extensions throw `MissingMethodException` at runtime against the OpenAI SDK version
we use. Offering a setting that doesn't work is worse than not offering it at all.

The `max_completion_tokens` field is already sent correctly — the OpenAI SDK uses
this name itself, no Azure extension is needed (measured).

## SDK used

The official [`Azure.AI.OpenAI`](https://www.nuget.org/packages/Azure.AI.OpenAI)
package (owner: Microsoft, MIT). The package adds only **routing** on top of the
OpenAI SDK: the deployment path, the `api-version` query parameter, and
`api-key` / Entra credentials. Message mapping, streaming, tool calling, and usage
counters all come from the OpenAI SDK's own code.

Transitive dependencies: `Azure.Core`, `OpenAI`, `System.ClientModel`,
`System.Memory.Data`, `Microsoft.Bcl.AsyncInterfaces`. The package is AOT-compatible
(`IsAotCompatible=true`, zero warnings).

### Unsupported surfaces

- **Responses API.** `AzureOpenAIClient.GetResponsesClient()` does **not** return an
  Azure-specific client; it returns OpenAI's base class, and there is no confirmation
  it conforms to Azure's path/`api-version` shape. This package only uses Chat
  Completions.
- **Azure OpenAI On Your Data** (`AddDataSource`) — the runtime breakage described
  above.
- **Azure AI Foundry Agents.** This is a separate capability (`IAgentSource`, not
  `IModelProvider`) and has been left to a separate package; see

## Health check

Hits `GET {endpoint}/openai/models?api-version=2024-10-21`, which **produces no
charge**. Authentication is done with the `api-key` header or an Entra `Bearer`
token. Error detail is limited to the HTTP status code and a short reason; the API
key and resource address are **never leaked**.

```
GET /tracon/api/models/health/azure-openai
```

The list returned is a **model** list, not a deployment list. What the health
check proves is: the address is correct, the credential is valid, the resource is
up. Whether the deployment name is correct is only known on the first real call.

## Model catalog

Tracon does not ship a built-in model list: model names change faster than a
NuGet release. The catalog comes
entirely from configuration and **is not a validation list** — a deployment name not
listed here can still be used. The `Name` field of each entry is **the deployment
name**.

```json
{
  "Tracon": {
    "Providers": {
      "AzureOpenAI": {
        "Endpoint": "https://my-resource.openai.azure.com/",
        "ApiKey": "",
        "DefaultDeployment": "production-gpt",
        "Models": [
          { "Name": "production-gpt", "DisplayName": "Production (gpt-5.4-mini)",
            "ContextWindowTokens": 128000,
            "InputCostPerMillionTokens": 0.15, "OutputCostPerMillionTokens": 0.6 }
        ]
      }
    }
  }
}
```

`ApiKey` is **never** written to this file — use `dotnet user-secrets`.
`CredentialFactory` is a delegate and is not read from configuration; it is
supplied in code.

## Links

- Guide: <https://tracon.dev/guides/model-providers/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
