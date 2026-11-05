using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tracon.Azure.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// The key set up at start is trimmed before a header is built from it, in the chat client and in the health check, and
/// a key that still holds a character an HTTP header cannot carry stops the provider with a failure that names the
/// option and does not repeat the key.
/// </summary>
/// <remarks>
/// A real socket, as in the other wire tests: the request head the server read is the proof of what was sent. The
/// key of a tenant is not part of this; it is masked where the text of its failure is shown.
/// </remarks>
public sealed class AzureOpenAISetupKeyTests
{
    private const string OptionName = "AzureOpenAIProviderOptions.ApiKey";

    private const string CompletionBody = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"production-gpt",
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
        var factory = new AzureOpenAIChatClientFactory(TestData.Options(options =>
        {
            options.ApiKey = around + TestData.ApiKey + around;
            options.Endpoint = server.BaseAddress;
        }));
        using var chatClient = factory.CreateChatClient(TestData.Binding());

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
        var failure = Should.Throw<TraconException>(() => new AzureOpenAIChatClientFactory(TestData.Options(options => options.ApiKey = configured)));

        failure.Message.ShouldStartWith(OptionName);
        failure.Message.ShouldNotContain("test-key-", Case.Sensitive);
        failure.Message.ShouldNotContain("0123456789", Case.Sensitive);
    }

    [Fact]
    public async Task The_health_check_sends_the_setup_key_without_the_white_space_around_it()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"gpt-test"}]}""");

        var health = await new AzureOpenAIProviderHealthCheck(
                AzureOpenAIProviderNames.AzureOpenAI,
                TestData.Options(options =>
                {
                    options.ApiKey = "\r\n" + TestData.ApiKey + "\n";
                    options.Endpoint = server.BaseAddress;
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

        var health = await new AzureOpenAIProviderHealthCheck(
                AzureOpenAIProviderNames.AzureOpenAI,
                TestData.Options(options =>
                {
                    options.ApiKey = "test-key-\0-0123456789";
                    options.Endpoint = server.BaseAddress;
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
        var validator = new AzureOpenAIProviderOptionsValidator();

        validator.Validate(name: null, TestData.Options(options => options.ApiKey = TestData.ApiKey + "\n")).Succeeded.ShouldBeTrue();

        var refused = validator.Validate(name: null, TestData.Options(options => options.ApiKey = "test-key-\0-0123456789"));

        refused.Failed.ShouldBeTrue();

        var text = string.Join('\n', refused.Failures!);

        text.ShouldContain(OptionName);
        text.ShouldNotContain("test-key-", Case.Sensitive);
        text.ShouldNotContain("0123456789", Case.Sensitive);
    }

    [Fact]
    public void A_credential_factory_wins_over_the_key_so_a_key_that_is_never_sent_is_not_judged()
    {
        var result = new AzureOpenAIProviderOptionsValidator().Validate(
            name: null,
            TestData.Options(options =>
            {
                options.ApiKey = "test-key-\0-0123456789";
                options.CredentialFactory = static () => new FakeTokenCredential();
            }));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void The_provider_reports_the_setup_key_as_it_is_sent()
    {
        var factory = new AzureOpenAIChatClientFactory(TestData.Options(options => options.ApiKey = "\r\n" + TestData.ApiKey + "\n"));

        // The trimmed value: a service that refuses a key quotes what it received.
        new AzureOpenAIModelProvider(AzureOpenAIProviderNames.AzureOpenAI, factory, []).SetupApiKeys.ShouldBe([TestData.ApiKey]);
    }

    [Fact]
    public void A_provider_set_up_with_a_credential_factory_reports_no_key_even_when_a_key_is_also_given()
    {
        // The managed credential wins and the key is never sent, so it is neither judged nor reported. Its token is
        // fetched while the client runs and is not a static secret.
        var factory = new AzureOpenAIChatClientFactory(TestData.Options(options =>
        {
            options.ApiKey = TestData.ApiKey;
            options.CredentialFactory = static () => new FakeTokenCredential();
        }));

        new AzureOpenAIModelProvider(AzureOpenAIProviderNames.AzureOpenAI, factory, []).SetupApiKeys.ShouldBeEmpty();
    }

    [Fact]
    public void A_provider_built_on_a_ready_made_client_reports_no_key()
    {
        var factory = AzureOpenAIChatClientFactory.FromClient(AzureOpenAIChatClientFactory.CreateClient(TestData.Options()));

        new AzureOpenAIModelProvider(AzureOpenAIProviderNames.AzureOpenAI, factory, []).SetupApiKeys.ShouldBeEmpty();
    }
}
