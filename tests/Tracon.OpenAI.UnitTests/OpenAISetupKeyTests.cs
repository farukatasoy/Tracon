using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The key set up at start is trimmed before a header is built from it, in the chat client and in the health check, and
/// a key that still holds a character an HTTP header cannot carry stops the provider with a failure that names the
/// option and does not repeat the key.
/// </summary>
/// <remarks>
/// A real socket, as in the other wire tests: the request head the server read is the proof of what was sent. The
/// key of a tenant is not part of this; it is masked where the text of its failure is shown.
/// </remarks>
public sealed class OpenAISetupKeyTests
{
    private const string OptionName = "OpenAIProviderOptions.ApiKey";

    private const string CompletionBody = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4o-mini",
         "choices":[{"index":0,"message":{"role":"assistant","content":"pong"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    [InlineData(" ")]
    public async Task The_chat_client_sends_the_setup_key_without_the_white_space_around_it(string around)
    {
        await using var server = StubHttpServer.Respond(200, "OK", CompletionBody);
        var factory = new OpenAIChatClientFactory(TestData.Options(options =>
        {
            options.ApiKey = around + TestData.ApiKey + around;
            options.Endpoint = server.BaseAddress;
        }));
        using var chatClient = factory.CreateChatClient(TestData.Binding(), OpenAIApiSurface.ChatCompletions);

        await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        (await server.RequestHead).ShouldContain($"Authorization: Bearer {TestData.ApiKey}\r\n");
    }

    [Theory]
    [InlineData("test-key-\0-0123456789")]
    [InlineData("test-key-\n-0123456789")]
    [InlineData("test-key-\u00e9-0123456789")]
    [InlineData("test-key-,-0123456789")]
    public void The_factory_refuses_a_setup_key_a_header_cannot_carry_and_the_failure_does_not_repeat_it(string configured)
    {
        var failure = Should.Throw<TraconException>(() => new OpenAIChatClientFactory(TestData.Options(options => options.ApiKey = configured)));

        failure.Message.ShouldStartWith(OptionName);
        failure.Message.ShouldNotContain("test-key-", Case.Sensitive);
        failure.Message.ShouldNotContain("0123456789", Case.Sensitive);
    }

    [Fact]
    public async Task The_health_check_sends_the_setup_key_without_the_white_space_around_it()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"gpt-test"}]}""");

        var health = await new OpenAIProviderHealthCheck(
                OpenAIProviderNames.ChatCompletions,
                TestData.Options(options =>
                {
                    options.ApiKey = "\r\n" + TestData.ApiKey + "\n";
                    options.Endpoint = new Uri(server.BaseAddress, "v1");
                }))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);
        (await server.RequestHead).ShouldContain($"Authorization: Bearer {TestData.ApiKey}\r\n");
    }

    [Fact]
    public async Task The_health_check_reports_a_setup_key_a_header_cannot_carry_without_the_key_and_makes_no_call()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"gpt-test"}]}""");
        using var logs = new RecordingLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));

        var health = await new OpenAIProviderHealthCheck(
                OpenAIProviderNames.ChatCompletions,
                TestData.Options(options =>
                {
                    options.ApiKey = "test-key-\0-0123456789";
                    options.Endpoint = new Uri(server.BaseAddress, "v1");
                }),
                loggers.CreateLogger("health"))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        health.Detail.ShouldNotBeNull().ShouldNotContain("0123456789", Case.Sensitive);
        logs.AllText.ShouldContain(OptionName);
        logs.AllText.ShouldNotContain("0123456789", Case.Sensitive);
        server.RequestHead.IsCompleted.ShouldBeFalse("the check called the provider with a key it could not send");
    }

    [Fact]
    public void The_options_validator_accepts_a_key_that_needs_only_a_trim_and_refuses_one_a_header_cannot_carry()
    {
        var validator = new OpenAIProviderOptionsValidator();

        validator.Validate(name: null, TestData.Options(options => options.ApiKey = TestData.ApiKey + "\n")).Succeeded.ShouldBeTrue();

        var refused = validator.Validate(name: null, TestData.Options(options => options.ApiKey = "test-key-\0-0123456789"));

        refused.Failed.ShouldBeTrue();

        var text = string.Join('\n', refused.Failures!);

        text.ShouldContain(OptionName);
        text.ShouldNotContain("test-key-", Case.Sensitive);
        text.ShouldNotContain("0123456789", Case.Sensitive);
    }

    [Fact]
    public void A_compatible_provider_without_a_key_is_valid_and_one_with_a_key_a_header_cannot_carry_is_not()
    {
        var validator = new OpenAIProviderOptionsValidator();
        var endpoint = new Uri("http://localhost:11434/v1");

        validator.Validate("local", new OpenAIProviderOptions { Endpoint = endpoint }).Succeeded.ShouldBeTrue();
        validator.Validate("local", new OpenAIProviderOptions { Endpoint = endpoint, ApiKey = "test-key-\0-0123456789" }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Both_surfaces_of_the_provider_report_the_setup_key_as_it_is_sent()
    {
        var factory = new OpenAIChatClientFactory(TestData.Options(options => options.ApiKey = "\r\n" + TestData.ApiKey + "\n"));

        foreach (var (name, surface) in new[] { (OpenAIProviderNames.ChatCompletions, OpenAIApiSurface.ChatCompletions), (OpenAIProviderNames.Responses, OpenAIApiSurface.Responses) })
        {
            // The trimmed value: a service that refuses a key quotes what it received.
            new OpenAIModelProvider(name, surface, factory, []).SetupApiKeys.ShouldBe([TestData.ApiKey]);
        }
    }

    [Fact]
    public void A_provider_built_on_a_ready_made_client_reports_no_key()
    {
        var factory = OpenAIChatClientFactory.FromClient(OpenAIChatClientFactory.CreateClient(TestData.Options()));

        new OpenAIModelProvider(OpenAIProviderNames.ChatCompletions, OpenAIApiSurface.ChatCompletions, factory, []).SetupApiKeys.ShouldBeEmpty();
    }

    [Fact]
    public void A_key_less_compatible_provider_reports_no_key()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddTracon().UseOpenAICompatible("ollama", options => options.Endpoint = new Uri("http://localhost:11434/v1"));

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IModelProvider>().Single(static candidate => string.Equals(candidate.Name, "ollama", StringComparison.Ordinal)).SetupApiKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Two_compatible_providers_each_report_their_own_key_and_not_the_others()
    {
        const string alphaKey = "alpha-key-0123456789";
        const string betaKey = "beta-key-9876543210";

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddTracon()
            .UseOpenAICompatible("alpha", options => { options.Endpoint = new Uri("https://alpha.example.com/v1"); options.ApiKey = alphaKey; })
            .UseOpenAICompatible("beta", options => { options.Endpoint = new Uri("https://beta.example.com/v1"); options.ApiKey = " " + betaKey + "\n"; });

        using var provider = services.BuildServiceProvider();
        var providers = provider.GetServices<IModelProvider>().ToDictionary(static candidate => candidate.Name, StringComparer.Ordinal);

        providers["alpha"].SetupApiKeys.ShouldBe([alphaKey]);
        providers["beta"].SetupApiKeys.ShouldBe([betaKey]);
    }
}
