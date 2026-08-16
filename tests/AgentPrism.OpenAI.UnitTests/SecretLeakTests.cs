using System.Text.Json;
using AgentPrism.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// Verifies that the API key never appears in any output AgentPrism hands out.
/// </summary>
/// <remarks>
/// <para>
/// The boundary these tests protect: the key goes only into the OpenAI client's
/// authentication header. The catalog, the provider registry, the options object,
/// validation messages, exception messages, client metadata, and log lines must
/// <strong>never</strong> carry the key.
/// </para>
/// <para>
/// Npgsql masks the password in its own connection string; the OpenAI client offers
/// no such guarantee. That is why the boundary is protected here, with tests.
/// </para>
/// </remarks>
public sealed class SecretLeakTests
{
    private const string Secret = "very-secret-key-TEST-9f3a2b";

    [Fact]
    public void Provider_registry_output_carries_no_key()
    {
        using var provider = BuildProvider();

        var json = JsonSerializer.Serialize(
            provider.GetRequiredService<IModelProviderRegistry>().List(),
            JsonOptions);

        json.ShouldNotContain(Secret);
    }

    [Fact]
    public void Model_catalog_output_carries_no_key()
    {
        using var provider = BuildProvider();

        foreach (var modelProvider in provider.GetServices<IModelProvider>())
        {
            JsonSerializer.Serialize(modelProvider.Models, JsonOptions).ShouldNotContain(Secret);
            Text(modelProvider).ShouldNotContain(Secret);
        }
    }

    [Fact]
    public void Options_object_does_not_define_its_own_ToString()
    {
        // The options object must NOT be a record: the compiler-generated ToString
        // writes every property and would expose the key in the very first log line.
        // The default object.ToString writes only the type name.
        typeof(OpenAIProviderOptions).GetMethod(nameof(ToString), Type.EmptyTypes)!
            .DeclaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public void Validation_messages_contain_no_key()
    {
        var validator = new OpenAIProviderOptionsValidator();

        var result = validator.Validate(
            name: null,
            new OpenAIProviderOptions
            {
                ApiKey = Secret,
                Endpoint = new Uri("/v1", UriKind.Relative),
                Timeout = TimeSpan.Zero,
            });

        result.Failed.ShouldBeTrue();
        string.Join('\n', result.Failures!).ShouldNotContain(Secret);
    }

    [Fact]
    public void Build_error_message_contains_no_key()
    {
        var factory = new OpenAIChatClientFactory(new OpenAIProviderOptions { ApiKey = Secret });

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "  " },
            OpenAIApiSurface.ChatCompletions));

        exception.ToString().ShouldNotContain(Secret);
    }

    [Fact]
    public void Client_metadata_contains_no_key()
    {
        // The phase 6 telemetry writes this metadata to span tags.
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
        services.AddAgentPrism().UseOpenAI(Secret, options => options.DefaultModel = "gpt-4o-mini");

        using var serviceProvider = services.BuildServiceProvider();

        using var chatClient = serviceProvider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(TestData.Binding("model-not-in-the-catalog"));

        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    [Fact]
    public void Key_is_not_logged_when_options_are_validated()
    {
        using var loggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));
        services.AddAgentPrism().UseOpenAI(Secret);

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value.ApiKey.ShouldBe(Secret);
        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static string Text(object value) => value.ToString() ?? string.Empty;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(Secret);

        return services.BuildServiceProvider();
    }
}
