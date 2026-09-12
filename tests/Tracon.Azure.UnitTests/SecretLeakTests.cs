using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.Azure.UnitTests.Infrastructure;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// Verifies that the API key and the resource endpoint do not appear in any
/// output Tracon exposes externally.
/// </summary>
/// <remarks>
/// Protected boundary: the key only goes to the Azure client's <c>api-key</c>
/// header. The catalog, provider registry, options object, validation
/// messages, exception messages, and log lines must <strong>never, under
/// any condition,</strong> carry the key. The resource endpoint is not a
/// secret, but it exposes an organization's topology; it is not written
/// into validation messages.
/// </remarks>
public sealed class SecretLeakTests
{
    private const string Secret = "very-secret-azure-key-TEST-9f3a2b";

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
    public void Options_type_does_not_define_its_own_ToString()
    {
        // The options type must NOT be a record (K-035): the compiler-generated
        // ToString would print all properties and expose the key in the first log line.
        typeof(AzureOpenAIProviderOptions).GetMethod(nameof(ToString), Type.EmptyTypes)!
            .DeclaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public void Validation_messages_contain_neither_the_key_nor_the_endpoint()
    {
        var result = new AzureOpenAIProviderOptionsValidator().Validate(
            name: null,
            new AzureOpenAIProviderOptions
            {
                ApiKey = Secret,
                Endpoint = new Uri("/openai", UriKind.Relative),
                Timeout = TimeSpan.Zero,
            });

        result.Failed.ShouldBeTrue();

        var text = string.Join('\n', result.Failures!);
        text.ShouldNotContain(Secret);
        text.ShouldNotContain("/openai");
    }

    [Fact]
    public void Compile_error_message_does_not_contain_the_key()
    {
        var factory = new AzureOpenAIChatClientFactory(
            new AzureOpenAIProviderOptions { Endpoint = TestData.Endpoint, ApiKey = Secret });

        var exception = Should.Throw<TraconException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = AzureOpenAIProviderNames.AzureOpenAI, Model = "  " }));

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
    public void Provider_setup_and_client_creation_do_not_log_the_key()
    {
        using var loggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));
        services.AddTracon().UseAzureOpenAI(TestData.Endpoint, Secret, options =>
        {
            options.DefaultDeployment = TestData.Deployment;
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment });
        });

        using var serviceProvider = services.BuildServiceProvider();

        using var chatClient = serviceProvider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(TestData.Binding("not-in-catalog-deployment"));

        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    [Fact]
    public void Key_is_not_logged_when_options_are_validated()
    {
        using var loggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));
        services.AddTracon().UseAzureOpenAI(TestData.Endpoint, Secret);

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value.ApiKey.ShouldBe(Secret);
        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static string Text(object value) => value.ToString() ?? string.Empty;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseAzureOpenAI(TestData.Endpoint, Secret);

        return services.BuildServiceProvider();
    }
}
