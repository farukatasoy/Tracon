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

The package calls the resource's **v1 API** (`{endpoint}/openai/v1/`), the OpenAI
wire protocol Azure serves next to its classic API. The deployment name travels in
the request body's `model` field:

```
POST {endpoint}/openai/v1/chat/completions
{ "model": "production-gpt", ... }
```

A wrong name comes back as an Azure error naming the deployment, not as a
"model not found" error. When the field is left empty, the error message says so.

The resource must support the v1 API. Give the resource address
(`https://my-resource.openai.azure.com/`); an address that already ends in
`/openai/v1` is used as it is, and a gateway path in front of the resource is kept.

## Authentication

There are two ways. If both are provided, **managed identity wins**.

### API key

```csharp
.UseAzureOpenAI(endpoint, apiKey)
```

The key is sent as `Authorization: Bearer <key>`.

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

The token is sent only over **HTTPS**. An Entra credential with a plain `http://`
resource address is refused at the first chat call, and the health check reports
unhealthy without asking the credential for a token.

## Provider-specific settings

This provider supports **one key** in `ModelBinding.ProviderSettings`:
`azure-openai.strictJsonSchema` (boolean, off by default). With it on, the request
carries `strict: true` on the response format of an agent whose `ResponseFormat` is a
JSON schema. It reaches the response format only; the agent's tools stay non-strict.
Strict mode needs a deployment of a model that supports structured outputs. Any other
key stops the build with a clear error.

The client rewrites a response schema on every request, strict or not, and
`POST /api/agents/validate` returns the schema that goes on the wire under
`responseSchemas`.

No other setting is offered, and the reason is measured: the Azure-only request fields
(`AddDataSource` for Azure OpenAI On Your Data, and the like) were written through the
Azure SDK's request extensions, and **all** of them threw `MissingMethodException` at
runtime against the OpenAI SDK version in use. The package now talks to the v1 API
through the plain OpenAI client, which has no counterpart for them. Offering a setting
that doesn't work is worse than not offering it at all.

The `max_completion_tokens` field is already sent correctly — the OpenAI SDK uses
this name itself (measured).

## SDK used

The official [`OpenAI`](https://www.nuget.org/packages/OpenAI) package, pointed at the
resource's v1 endpoint. The Azure-specific
[`Azure.AI.OpenAI`](https://www.nuget.org/packages/Azure.AI.OpenAI) package is **not**
used: its stable line stopped at 2.1.0 (December 2024). Message mapping, streaming,
tool calling, and usage counters all come from the OpenAI SDK's own code.

Direct dependencies: `OpenAI`, `Azure.Core` (carried in the public API as
`TokenCredential`), `Microsoft.Extensions.AI.OpenAI`.

The package is Native AOT-compatible: a Native AOT app that calls it through a real
chat request with an API key (plain and streamed) publishes with no trim or AOT warning and runs
(`samples/Tracon.Samples.ProviderAotSmoke`, checked on every release rehearsal).

### Unsupported surfaces

- **Responses API.** This package only uses Chat Completions (and images). The v1
  API serves a Responses surface, but it has not been measured here.
- **Azure OpenAI On Your Data** (`AddDataSource`) — no counterpart on the plain
  OpenAI client (see above).
- **Azure AI Foundry Agents.** This is a separate capability (`IAgentSource`, not
  `IModelProvider`) and has been left to a separate package; see

## Health check

Hits `GET {endpoint}/openai/v1/models`, which **produces no
charge**. Authentication matches the chat call: `Authorization: Bearer` with the key,
or an Entra token (HTTPS only; over plain HTTP the check reports unhealthy without
asking the credential for a token). Error detail is limited to the HTTP status code and a short reason; the API
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
