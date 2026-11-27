using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Tracon.Google.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Google.UnitTests;

/// <summary>
/// A Gemini answer that is an HTTP error reaches the model pipeline with its status, so the fallback chain, the
/// circuit breaker and <see cref="ProviderInvocationException"/> decide from it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Google.GenAI.ApiException</c> derives from <see cref="HttpRequestException"/> but declares its own
/// <c>StatusCode</c> with <c>new</c>, so the status the base class reports stays empty. Core reads the base
/// property and then the text of the message; a JSON error body makes the SDK write the service's own sentence
/// as the message, and that sentence has no status in it. A <c>503</c> read as a failure that never got an answer,
/// and so did a <c>400</c>.
/// </para>
/// <para>
/// The wire tests drive the real adapter against a local server, so the exception each one throws is the one a
/// run meets. The decorator tests script the inner client for the cases a server cannot produce on demand.
/// </para>
/// </remarks>
public sealed class GoogleStatusChatClientTests
{
    private const string ServiceSentence = "The service gave this specific sentence.";

    private const string StreamedAnswer =
        "data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"ok\"}]},\"finishReason\":\"STOP\"}]}\r\n\r\n";

    public static TheoryData<int, string, string, bool> Answers() => new()
    {
        { 400, "INVALID_ARGUMENT", "Bad Request", false },
        { 404, "NOT_FOUND", "Not Found", false },
        { 429, "RESOURCE_EXHAUSTED", "Too Many Requests", true },
        { 500, "INTERNAL", "Internal Server Error", true },
        { 503, "UNAVAILABLE", "Service Unavailable", true },
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task A_json_error_answer_to_a_buffered_call_carries_the_status_the_service_gave(int status, string name, string reason, bool transient)
    {
        await using var server = StubHttpServer.Respond(status, reason, ErrorBody(status, name));
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(TestData.Binding());

        var failure = await Should.ThrowAsync<Exception>(
            async () => await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken));

        AssertRead(failure, status, name, transient);
    }

    [Theory]
    [MemberData(nameof(Answers))]
    public async Task A_json_error_answer_to_a_streamed_call_carries_the_status_the_service_gave(int status, string name, string reason, bool transient)
    {
        await using var server = StubHttpServer.Respond(status, reason, ErrorBody(status, name));
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(TestData.Binding());

        var failure = await Should.ThrowAsync<Exception>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            {
                _ = update;
            }
        });

        AssertRead(failure, status, name, transient);
    }

    [Fact]
    public async Task A_stream_that_the_service_cuts_with_an_error_event_carries_the_status_of_the_event()
    {
        // The service can open a stream with 200 and put the failure in an event: the SDK raises the same error type for it.
        var body = StreamedAnswer + "data: " + ErrorBody(503, "UNAVAILABLE") + "\r\n\r\n";

        await using var server = StubHttpServer.Respond(200, "OK", body);
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(TestData.Binding());
        var updates = 0;

        var failure = await Should.ThrowAsync<Exception>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            {
                updates++;
                _ = update;
            }
        });

        updates.ShouldBeGreaterThan(0, "the text before the failure was delivered");
        AssertRead(failure, 503, "UNAVAILABLE", transient: true);
    }

    [Fact]
    public async Task An_empty_error_body_still_carries_its_status()
    {
        await using var server = StubHttpServer.Respond(503, "Service Unavailable", string.Empty);
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(TestData.Binding());

        var failure = await Should.ThrowAsync<Exception>(
            async () => await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken));

        var http = failure.ShouldBeOfType<HttpRequestException>();

        http.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        ProviderHttpStatus.Read(failure).ShouldBe(503);
    }

    [Fact]
    public async Task The_translated_failure_keeps_the_sentence_and_the_original_error_as_its_inner_exception()
    {
        var original = new ServerError(ServiceSentence, 503, "UNAVAILABLE");
        using var client = new GoogleStatusChatClient(new Throwing(original));

        var failure = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken));

        failure.GetType().ShouldBe(typeof(HttpRequestException));
        failure.Message.ShouldBe(ServiceSentence);
        failure.InnerException.ShouldBeSameAs(original);
        failure.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task An_error_in_the_middle_of_a_stream_carries_its_status_after_the_updates_that_came_before_it()
    {
        using var client = new GoogleStatusChatClient(new Throwing(new ServerError(ServiceSentence, 503, "UNAVAILABLE"), updatesBeforeFailure: 2));
        var seen = new List<string>();

        var failure = await Should.ThrowAsync<HttpRequestException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            {
                seen.Add(update.Text);
            }
        });

        seen.ShouldBe(["update 1", "update 2"]);
        failure.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_status_name_that_is_an_http_reason_phrase_is_not_taken_for_a_provider_code()
    {
        // With an empty body the SDK puts the reason phrase where the service's status name would be.
        using var client = new GoogleStatusChatClient(new Throwing(new ClientError("Request failed with status code 403: Forbidden", 403, "Forbidden")));

        var failure = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken));

        failure.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ProviderErrorCodes.Read(failure).ShouldBeNull();
    }

    [Fact]
    public async Task An_error_without_a_status_in_the_http_range_is_left_as_it_was_thrown()
    {
        foreach (var original in new ApiException[]
        {
            new("The SDK raised this with no status."),
            new ClientError("Out of range, low.", 99),
            new ServerError("Out of range, high.", 600),
        })
        {
            using var client = new GoogleStatusChatClient(new Throwing(original));

            var failure = await Should.ThrowAsync<ApiException>(
                async () => await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken));

            failure.ShouldBeSameAs(original);
        }
    }

    [Fact]
    public async Task A_failure_that_is_not_an_api_error_passes_through_as_the_same_instance()
    {
        foreach (var original in new Exception[]
        {
            new InvalidOperationException("Not the service's answer."),
            new HttpRequestException("A transport failure.", inner: null, HttpStatusCode.BadGateway),
            new GoogleFunctionCallException("MALFORMED_FUNCTION_CALL"),
        })
        {
            using var client = new GoogleStatusChatClient(new Throwing(original));

            var failure = await Should.ThrowAsync<Exception>(
                async () => await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken));

            failure.ShouldBeSameAs(original);
        }
    }

    [Fact]
    public async Task A_cancellation_passes_through_untouched_in_both_call_shapes()
    {
        // Should.ThrowAsync builds a new exception for a cancellation, so the instance is caught by hand.
        var original = new OperationCanceledException("The caller stopped.");
        using var client = new GoogleStatusChatClient(new Throwing(original));

        (await CaptureAsync(async () => await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken)))
            .ShouldBeSameAs(original);

        (await CaptureAsync(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            {
                _ = update;
            }
        })).ShouldBeSameAs(original);
    }

    [Fact]
    public async Task A_call_that_succeeds_passes_through_in_both_call_shapes()
    {
        using var client = new GoogleStatusChatClient(new Throwing(failure: null, updatesBeforeFailure: 2));

        (await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken)).Text.ShouldBe("answer");

        var seen = new List<string>();

        await foreach (var update in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
        {
            seen.Add(update.Text);
        }

        seen.ShouldBe(["update 1", "update 2"]);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        return null;
    }

    private static string ErrorBody(int code, string status)
        => JsonSerializer.Serialize(new { error = new { code, message = ServiceSentence, status } });

    private static void AssertRead(Exception failure, int status, string name, bool transient)
    {
        var http = failure.ShouldBeOfType<HttpRequestException>();

        http.StatusCode.ShouldBe((HttpStatusCode)status);
        (http.InnerException as ApiException).ShouldNotBeNull().Status.ShouldBe(name);

        ProviderHttpStatus.Read(failure).ShouldBe(status);
        FallbackRetryClassifier.IsRetryable(failure).ShouldBe(transient, $"{status} is {(transient ? "worth another try" : "permanent")}");

        // A permanent 4xx other than 401 and 403 does not count against the credential every tenant shares.
        CircuitBreakingChatClient.CountsAgainstProvider(new ForeignProviderInvocationException(failure, ProviderSecretMask.Empty))
            .ShouldBe(transient);

        var invocation = ProviderInvocationException.UpstreamFailure(failure, GoogleProviderNames.Google, ProviderSecretMask.Empty);

        invocation.StatusCode.ShouldBe(status);
        invocation.IsTransient.ShouldBe(transient);
        invocation.ProviderErrorCode.ShouldBe(name);
        invocation.Message.ShouldNotContain(ServiceSentence, Case.Sensitive, "the service's own words stay out of the message");
    }

    /// <summary>An inner client that fails with the exception it is given, after the updates it is asked to send first.</summary>
    private sealed class Throwing(Exception? failure, int updatesBeforeFailure = 0) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => failure is null
                ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")))
                : Task.FromException<ChatResponse>(failure);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            for (var index = 1; index <= updatesBeforeFailure; index++)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, $"update {index}");
            }

            if (failure is not null)
            {
                throw failure;
            }
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The scripted client has nothing to release.
        }
    }
}
