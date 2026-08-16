using System.Text.Json;
using AgentPrism.Google.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism.Google.UnitTests;

/// <summary>
/// Verifies that the API key does not appear in any output AgentPrism exposes externally.
/// </summary>
/// <remarks>
/// Protected boundary: the key goes only to the Google client's <c>x-goog-api-key</c>
/// header. The catalog, provider registry, settings object, validation messages,
/// exception messages, client metadata, and log lines must <strong>never, under any
/// condition,</strong> carry the key.
/// </remarks>
public sealed class SecretLeakTests
{
    private const string Secret = "very-secret-key-TEST-9f3a2b";

    [Fact]
    public void Provider_registry_output_has_no_key()
    {
        using var provider = BuildProvider();

        JsonSerializer.Serialize(
            provider.GetRequiredService<IModelProviderRegistry>().List(),
            JsonOptions).ShouldNotContain(Secret);
    }

    [Fact]
    public void Model_catalog_output_has_no_key()
    {
        using var provider = BuildProvider();

        foreach (var modelProvider in provider.GetServices<IModelProvider>())
        {
            JsonSerializer.Serialize(modelProvider.Models, JsonOptions).ShouldNotContain(Secret);
            Text(modelProvider).ShouldNotContain(Secret);
        }
    }

    [Fact]
    public void Settings_object_does_not_define_its_own_ToString()
    {
        // The settings object MUST NOT be a record (K-035): the compiler-generated
        // ToString would print every property and expose the key in the first log line.
        typeof(GoogleProviderOptions).GetMethod(nameof(ToString), Type.EmptyTypes)!
            .DeclaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public void Validation_messages_do_not_contain_the_key()
    {
        var result = new GoogleProviderOptionsValidator().Validate(
            name: null,
            new GoogleProviderOptions
            {
                ApiKey = Secret,
                Endpoint = new Uri("/v1", UriKind.Relative),
                Timeout = TimeSpan.Zero,
            });

        result.Failed.ShouldBeTrue();
        string.Join('\n', result.Failures!).ShouldNotContain(Secret);
    }

    [Fact]
    public void Compile_error_message_does_not_contain_the_key()
    {
        using var factory = new GoogleChatClientFactory(new GoogleProviderOptions { ApiKey = Secret });

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = GoogleProviderNames.Google, Model = "  " }));

        exception.ToString().ShouldNotContain(Secret);
    }

    [Fact]
    public void Client_metadata_does_not_contain_the_key()
    {
        using var provider = BuildProvider();

        using var chatClient = provider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(TestData.Binding());

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        (metadata.ProviderUri?.ToString() ?? string.Empty).ShouldNotContain(Secret);
        (metadata.DefaultModelId ?? string.Empty).ShouldNotContain(Secret);
        Text(chatClient).ShouldNotContain(Secret);
    }

    [Fact]
    public void Provider_setup_and_client_production_do_not_log_the_key()
    {
        using var loggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));
        services.AddAgentPrism().UseGoogle(Secret, options =>
        {
            options.DefaultModel = TestData.Model;
            options.Models.Add(new ModelDescriptor { Name = TestData.Model });
        });

        using var serviceProvider = services.BuildServiceProvider();

        using var chatClient = serviceProvider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(TestData.Binding("model-not-in-catalog"));

        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    [Fact]
    public void Key_is_not_logged_when_settings_are_validated()
    {
        using var loggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));
        services.AddAgentPrism().UseGoogle(Secret);

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value.ApiKey.ShouldBe(Secret);
        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static string Text(object value) => value.ToString() ?? string.Empty;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseGoogle(Secret);

        return services.BuildServiceProvider();
    }
}
