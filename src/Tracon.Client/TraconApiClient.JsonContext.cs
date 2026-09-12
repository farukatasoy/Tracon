using System.Text.Json;

namespace Tracon.Client.Generated;

// Wires the generated client to TraconClientJsonContext.g.cs (produced by
// scripts/generate-client-json-context.py) instead of the reflection-based
// JsonSerializer default. UpdateJsonSerializerSettings is a partial method
// hook the generator itself declares - implementing it here, in a hand file,
// keeps the AOT wiring stable across every `dotnet nswag run` regeneration.
// Rationale: docs/arsiv/fazlar/83-TIPLI-ISTEMCI-VE-CLI.md, section 83.6.
/// <summary>The generated typed client for Tracon's management API. See the package README for setup.</summary>
public partial class TraconApiClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.TypeInfoResolver = TraconClientJsonContext.Default;
    }
}
