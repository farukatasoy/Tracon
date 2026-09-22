using Microsoft.Extensions.Logging;
using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// Where the out-of-catalog log entry sends the operator (phase 181).
/// </summary>
/// <remarks>
/// Before phase 181 the entry named <c>Tracon:Providers:OpenAI:Models</c> for
/// every <see cref="OpenAIModelProvider"/>, including one registered with
/// <c>UseOpenAICompatible()</c>, whose catalog lives in the options it was given.
/// </remarks>
public sealed class OpenAIModelProviderCatalogLogTests
{
    private static readonly ModelDescriptor[] Catalog = [new() { Name = "gpt-in-catalog" }];

    [Fact]
    public void UseOpenAI_provider_points_at_its_configuration_section()
    {
        var log = LogForOutOfCatalogModel(OpenAIProviderOptions.SectionName);

        log.ShouldContain("Use the Tracon:Providers:OpenAI:Models setting");
    }

    [Fact]
    public void Compatible_provider_does_not_point_at_the_OpenAI_section()
    {
        var log = LogForOutOfCatalogModel(configurationSectionKey: null);

        log.ShouldContain("Use the OpenAIProviderOptions.Models setting");
        log.ShouldNotContain("Tracon:Providers:OpenAI");
    }

    private static string LogForOutOfCatalogModel(string? configurationSectionKey)
    {
        using var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));

        var provider = new OpenAIModelProvider(
            "compat-test",
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            Catalog,
            loggerFactory.CreateLogger<OpenAIModelProvider>(),
            healthCheckOptions: TestData.Options(),
            configurationSectionKey: configurationSectionKey);

        using var chatClient = provider.CreateChatClient(TestData.Binding("gpt-outside-catalog"));

        return loggerProvider.AllText;
    }
}
