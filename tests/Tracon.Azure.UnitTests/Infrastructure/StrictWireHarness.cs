using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.OpenAIShared.Tests;

/// <summary>
/// Builds the Azure package's chat client for <see cref="StrictResponseSchemaWireTests"/> through the factory a
/// registered provider uses.
/// </summary>
internal static class StrictWireHarness
{
    /// <summary>The one surface the Azure provider has: Chat Completions at the resource's v1 endpoint.</summary>
    public static IReadOnlyList<WireSurface> Surfaces { get; } = [WireSurface.ChatCompletions];

    public static IChatClient CreateClient(Uri serverBase, WireSurface surface, bool? strictSetting)
    {
        var factory = new AzureOpenAIChatClientFactory(new AzureOpenAIProviderOptions
        {
            ApiKey = "test-key-1234567890",
            Endpoint = serverBase,
        });

        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (strictSetting is { } strict)
        {
            settings[AzureOpenAIProviderNames.StrictJsonSchemaSetting] = JsonSerializer.SerializeToElement(strict);
        }

        return factory.CreateChatClient(new ModelBinding
        {
            Provider = AzureOpenAIProviderNames.AzureOpenAI,
            Model = "production-gpt",
            ProviderSettings = settings,
        });
    }
}
