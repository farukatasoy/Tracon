using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.OpenAIShared.Tests;

/// <summary>
/// Builds the OpenAI package's chat client for <see cref="StrictResponseSchemaWireTests"/> through the factory a
/// registered provider uses.
/// </summary>
internal static class StrictWireHarness
{
    /// <summary>The two API surfaces <c>UseOpenAI()</c> registers.</summary>
    public static IReadOnlyList<WireSurface> Surfaces { get; } = [WireSurface.ChatCompletions, WireSurface.Responses];

    public static IChatClient CreateClient(Uri serverBase, WireSurface surface, bool? strictSetting)
    {
        var factory = new OpenAIChatClientFactory(new OpenAIProviderOptions
        {
            ApiKey = "test-api-key-1234567890",
            Endpoint = new Uri(serverBase, "v1/"),
        });

        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (strictSetting is { } strict)
        {
            settings[OpenAIProviderNames.StrictJsonSchemaSetting] = JsonSerializer.SerializeToElement(strict);
        }

        var binding = new ModelBinding
        {
            Provider = surface == WireSurface.Responses ? OpenAIProviderNames.Responses : OpenAIProviderNames.ChatCompletions,
            Model = "gpt-4o-mini",
            ProviderSettings = settings,
        };

        return factory.CreateChatClient(
            binding,
            surface == WireSurface.Responses ? OpenAIApiSurface.Responses : OpenAIApiSurface.ChatCompletions);
    }
}
