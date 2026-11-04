using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tracon.Google.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Google.UnitTests;

/// <summary>
/// The key set up at start is trimmed before a header is built from it, in the chat client and in the health check, and
/// a key that still holds a character an HTTP header cannot carry stops the provider with a failure that names the
/// option and does not repeat the key.
/// </summary>
/// <remarks>
/// A real socket, as in the other wire tests: the request head the server read is the proof of what was sent. The
/// key of a tenant is not part of this; it is masked where the text of its failure is shown.
/// </remarks>
public sealed class GoogleSetupKeyTests
{
    private const string OptionName = "GoogleProviderOptions.ApiKey";

    private const string Candidate = """{"candidates":[{"content":{"role":"model","parts":[{"text":"ok"}]},"finishReason":"STOP"}]}""";

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    [InlineData(" ")]
    public async Task The_chat_client_sends_the_setup_key_without_the_white_space_around_it(string around)
    {
        await using var server = StubHttpServer.Respond(200, "OK", Candidate);
        using var factory = new GoogleChatClientFactory(TestData.Options(options =>
        {
            options.ApiKey = around + TestData.ApiKey + around;
            options.Endpoint = server.BaseAddress;
        }));
        using var chatClient = factory.CreateChatClient(TestData.Binding());

        await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        (await server.RequestHead).ShouldContain($"x-goog-api-key: {TestData.ApiKey}\r\n");
    }

    [Theory]
    [InlineData("test-key-\0-0123456789")]
    [InlineData("test-key-\n-0123456789")]
    [InlineData("test-key-\u00e9-0123456789")]
    [InlineData("test-key-,-0123456789")]
    public void The_factory_refuses_a_setup_key_a_header_cannot_carry_and_the_failure_does_not_repeat_it(string configured)
    {
        var failure = Should.Throw<TraconException>(() => new GoogleChatClientFactory(TestData.Options(options => options.ApiKey = configured)));

        failure.Message.ShouldStartWith(OptionName);
        failure.Message.ShouldNotContain("test-key-", Case.Sensitive);
        failure.Message.ShouldNotContain("0123456789", Case.Sensitive);
    }

    [Fact]
    public async Task The_health_check_sends_the_setup_key_without_the_white_space_around_it()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"models":[{"name":"models/gemini-test"}]}""");

        var health = await new GoogleProviderHealthCheck(
                GoogleProviderNames.Google,
                TestData.Options(options =>
                {
                    options.ApiKey = "\r\n" + TestData.ApiKey + "\n";
                    options.Endpoint = server.BaseAddress;
                }))
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);
        (await server.RequestHead).ShouldContain($"x-goog-api-key: {TestData.ApiKey}\r\n");
    }

    [Fact]
    public async Task The_health_check_reports_a_setup_key_a_header_cannot_carry_without_the_key_and_makes_no_call()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"models":[{"name":"models/gemini-test"}]}""");
        using var logs = new RecordingLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));

        var health = await new GoogleProviderHealthCheck(
                GoogleProviderNames.Google,
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
        var validator = new GoogleProviderOptionsValidator();

        validator.Validate(name: null, TestData.Options(options => options.ApiKey = TestData.ApiKey + "\n")).Succeeded.ShouldBeTrue();

        var refused = validator.Validate(name: null, TestData.Options(options => options.ApiKey = "test-key-\0-0123456789"));

        refused.Failed.ShouldBeTrue();

        var text = string.Join('\n', refused.Failures!);

        text.ShouldContain(OptionName);
        text.ShouldNotContain("test-key-", Case.Sensitive);
        text.ShouldNotContain("0123456789", Case.Sensitive);
    }
}
