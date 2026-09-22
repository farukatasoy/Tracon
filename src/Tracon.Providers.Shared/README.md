# Tracon.Providers.Shared — shared source

> This is **not** a NuGet package and has **no** `.csproj` file of its own.
> The `.cs` files here are compiled directly into each model provider adapter
> via `<Compile Include="..." />`.

## Why not a package

The four provider adapters carried the same health check, model provider and
registration body as hand-made copies. A fix had to be written four times, and
one copy could miss it. A fifth package would add a dependency the consumer
never uses directly and a release burden on every version. Shared source removes
the copies and leaves the consumer's dependency graph unchanged. The same
mechanism as `Tracon.Sql.Shared`.

## Who compiles it

| Package | How |
|-------|-------|
| `Tracon.OpenAI` | `<Compile Include="../Tracon.Providers.Shared/**/*.cs" LinkBase="Shared" />` |
| `Tracon.Anthropic` | same |
| `Tracon.Google` | same |
| `Tracon.Azure` | same |

## What is here

| File | Holds | Stays in each package |
|---|---|---|
| `ProviderHealthCheckCore.cs` | The `GET` model-list call, timeout, failure mapping, body parsing | Endpoint, auth headers, body shape |
| `ModelProviderCore.cs` | Known-model set, BYOK factory and client caches, tenant endpoint guard rule, configuration diagnostic | `BuildCredentialFactory` (each SDK takes different options) |
| `ProviderRegistrationCore.cs` | Options + validator registration, "already registered" check, configuration readers, `Models` binding | Chat client factory registration, `AddModelProvider` |

## Rules

- **Every type stays `internal`.** A public type here would be a different
  CLR type in each of the four assemblies: a consumer that references two
  providers would get `CS0433`, and each package's `PublicAPI.Unshipped.txt`
  would have to list it. An internal type causes neither. Measured: the
  `Microsoft.AspNetCore.OpenApi` XML comment generator emits only public
  members, so the duplicate internal `<member>` ids across the four XML
  documentation files do not break `/openapi/v1.json` (the failure the
  sample application works around for the public `Tracon.Sql.Shared` types).
- **A file belongs here only when all four providers use it.** OpenAI-only
  code (live/sideband, OpenAI-compatible providers) stays in `Tracon.OpenAI`.
- **No conditional compilation symbols.** The same text compiles the same way
  in all four packages; a provider difference is a delegate or a parameter.
- **No reference to a provider SDK namespace** (`OpenAI`, `Anthropic`,
  `Google.GenAI`, `Azure.*`). Anything SDK-specific stays in the package.
- Model catalog data (`*ModelCatalog.cs`) stays in each package.

## Tests

`tests/Shared/Providers/` holds the tests of this tree. The four provider test
projects link them, so each assembly's copy is tested.
