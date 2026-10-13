using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Which failures count against a provider's circuit (F-354, F-387).
/// </summary>
/// <remarks>
/// The circuit of the setup-time credential is shared by every tenant, so a
/// failure that says nothing about the provider must not count.
/// </remarks>
public sealed class CircuitBreakerFailureCountingTests
{
    [Fact]
    public void A_cancellation_raised_above_the_provider_client_does_not_count()
    {
        // 🚨 F-387. A tool's own timeout is a TaskCanceledException nobody asked
        // for; the function loop rethrows it inside the circuit's ring, and it
        // counted against the provider every tenant shares.
        CircuitBreakingChatClient.CountsAgainstProvider(new TaskCanceledException("the tool's own call timed out"))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task A_cancellation_the_provider_client_raised_itself_counts()
    {
        using var client = new ProviderFailureTaggingChatClient(new TimingOutChatClient());

        // Caught by hand: Should.ThrowAsync rebuilds a canceled task's exception
        // as a new TaskCanceledException and drops the one that was thrown.
        var cancellation = await CatchAsync(client, TestContext.Current.CancellationToken);

        CircuitBreakingChatClient.CountsAgainstProvider(cancellation).ShouldBeTrue();
    }

    [Fact]
    public async Task A_cancellation_the_caller_requested_is_not_marked_as_the_providers()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        using var client = new ProviderFailureTaggingChatClient(new TimingOutChatClient());

        var cancellation = await CatchAsync(client, source.Token);

        ProviderFailureTaggingChatClient.IsProviderCancellation(cancellation).ShouldBeFalse();
    }

    [Fact]
    public void A_permanent_4xx_with_an_empty_body_does_not_count()
    {
        // F-387: System.ClientModel writes "Status: 400" without OpenAI's body.
        var failure = new ForeignProviderInvocationException(new InvalidOperationException("Service request failed.\nStatus: 400 (Bad Request)\n"));

        CircuitBreakingChatClient.CountsAgainstProvider(failure).ShouldBeFalse();
    }

    private static async Task<OperationCanceledException> CatchAsync(ProviderFailureTaggingChatClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException cancellation)
        {
            return cancellation;
        }

        throw new ShouldAssertException("The call was expected to be canceled.");
    }

    /// <summary>A provider client whose own deadline fires: it throws a cancellation the caller did not request.</summary>
    private sealed class TimingOutChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
