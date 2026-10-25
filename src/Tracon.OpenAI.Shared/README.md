# Tracon.OpenAI.Shared — shared source

> This is **not** a NuGet package and has **no** `.csproj` file of its own.
> The `.cs` files here compile directly into the two packages built on the
> OpenAI client library, through `<Compile Include="..." />`.

## Why not `Tracon.Providers.Shared`

`Tracon.Providers.Shared` compiles into all four provider adapters, and its
rules forbid a reference to a provider SDK. The code here needs the OpenAI
client library (`OpenAIClient.Pipeline`), which only two packages reference.

## Who compiles it

| Package | How |
|-------|-------|
| `Tracon.OpenAI` | `<Compile Include="../Tracon.OpenAI.Shared/**/*.cs" LinkBase="OpenAIShared" />` |
| `Tracon.Azure` | same |

## What is here

| File | Holds |
|---|---|
| `OpenAIPipelineImageGenerator.cs` | The `IImageGenerator` both packages register, written over the client's pipeline instead of `ImageClient`, so a host publishes with Native AOT |
| `OpenAIResponseSchema.cs` | The response schema as the Microsoft.Extensions.AI OpenAI client sends it: a copy of the library's `internal` rewrite, plus the lists that `IResponseSchemaSupport` returns. `StrictResponseSchemaWireTests` compares the copy with the real request body |
| `OpenAIStrictResponseSchemaChatClient.cs` | The decorator behind `openai.strictJsonSchema` and `azure-openai.strictJsonSchema`: `strict: true` on the response format, `strict: false` on each function declaration |

## Rules

- **Every type stays `internal`** — the same reason as `Tracon.Providers.Shared`:
  a public type would be a different CLR type in each assembly.
- **A file belongs here only when both packages use it.**
- **No conditional compilation symbols.** A package difference is a parameter.
