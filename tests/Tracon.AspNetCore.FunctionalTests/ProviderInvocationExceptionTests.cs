using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A model call that fails throws a public type that carries the status the provider answered with and says whether
/// sending the same call again can change the answer.
/// </summary>
/// <remarks>
/// <para>
/// A caller that retries (a job queue, a pipeline step) used to read the status out of the message text. The real
/// OpenAI client is driven against a local server through the registry's whole pipeline, so the type that comes out is the
/// one a caller catches, and the status is the one on the wire.
/// </para>
/// <para>
/// The retry count is set to zero so the test sends one request and never waits for a backoff. The default count and the
/// limit have their own tests against a scripted transport.
/// </para>
/// </remarks>
public sealed class ProviderInvocationExceptionTests
{
    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    public async Task A_failed_call_says_its_status_and_whether_a_retry_can_help(int status, bool transient)
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionStatusCode = status;

        var failure = await FailAsync(server);

        failure.StatusCode.ShouldBe(status);
        failure.IsTransient.ShouldBe(transient);
        failure.Provider.ShouldBe(OpenAIProviderNames.ChatCompletions);
        failure.ErrorType.ShouldBe(TraconErrorTypes.UpstreamError);
        server.ChatCompletionCallCount.ShouldBe(1, "the retry count is zero, so the call is sent once");
    }

    [Fact]
    public async Task The_message_carries_neither_the_providers_words_nor_the_key_and_no_text_of_the_failure_carries_the_key()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionStatusCode = 429;
        server.ChatCompletionErrorHasOpenAiBody = true;

        var failure = await FailAsync(server);

        // The message is the sentence Tracon chose, the provider and the status. The body the provider wrote is not in it;
        // the inner exception and ToString() are log material, and the key of the call is replaced in both.
        failure.Message.ShouldContain("429");
        failure.Message.ShouldNotContain("The request was rejected.");
        failure.Message.ShouldNotContain("invalid_request_error");
        failure.ToString().ShouldNotContain("probe-key-do-not-leak");
        failure.InnerException.ShouldNotBeNull().ToString().ShouldNotContain("probe-key-do-not-leak");
    }

    [Fact]
    public async Task A_caller_can_catch_it_as_a_tracon_exception()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionStatusCode = 503;

        var failure = await FailAsync(server);

        failure.ShouldBeAssignableTo<TraconException>();
    }

    [Fact]
    public async Task A_compatible_server_that_asks_for_no_key_is_called_once_with_a_retry_limit_of_zero()
    {
        // The keyless compatible server (Ollama, LM Studio) is built along another path than a keyed provider; a limit
        // that only the keyed path carried would send this call four times.
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ChatCompletionStatusCode = 503;

        var failure = await FailAsync(server, compatibleName: "local-llm");

        failure.Provider.ShouldBe("local-llm");
        server.ChatCompletionCallCount.ShouldBe(1);
    }

    private static async Task<ProviderInvocationException> FailAsync(FakeOpenAiCompatibleServer server, string? compatibleName = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var tracon = services.AddTracon();

        if (compatibleName is null)
        {
            tracon.UseOpenAI("probe-key-do-not-leak", options =>
            {
                options.Endpoint = server.BaseAddress;
                options.MaxRetries = 0;
            });
        }
        else
        {
            tracon.UseOpenAICompatible(compatibleName, options =>
            {
                options.Endpoint = server.BaseAddress;
                options.MaxRetries = 0;
            });
        }

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IModelProviderRegistry>();

        using var client = await registry.CreateChatClientAsync(
            new ModelBinding { Provider = compatibleName ?? OpenAIProviderNames.ChatCompletions, Model = "fake-local-model" },
            TestContext.Current.CancellationToken);

        return await Should.ThrowAsync<ProviderInvocationException>(async () =>
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken));
    }
}
