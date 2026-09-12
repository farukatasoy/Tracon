using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Providers;

/// <summary>
/// Verifies the <see cref="IProviderRetryClassifier"/> seam added to
/// <see cref="FallbackChatClient"/> in Phase 113 (F-149): a consumer's
/// decision wins, <see cref="ProviderRetryDecision.Unknown"/> defers to the
/// built-in rules, a buried cancellation can never be turned into a retry,
/// and a classifier that throws does not break the model-call path.
/// </summary>
public sealed class ProviderRetryClassifierSeamTests
{
    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "hi")];

    [Fact]
    public async Task Unknown_decision_defers_to_the_built_in_rules()
    {
        // The built-in rules retry an HTTP 5xx; a classifier that never has an
        // opinion must not change that.
        using var chatClient = RegistryWithFallback(
            primaryMessage: "HTTP 500 (internal_server_error)",
            classifier: new StubProviderRetryClassifier(ProviderRetryDecision.Unknown));

        var response = await chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("fallback answer");
    }

    [Fact]
    public async Task DoNotRetry_overrides_a_built_in_retryable_failure()
    {
        // Built in alone would retry HTTP 500; the consumer says no.
        using var chatClient = RegistryWithFallback(
            primaryMessage: "HTTP 500 (internal_server_error)",
            classifier: new StubProviderRetryClassifier(ProviderRetryDecision.DoNotRetry));

        var exception = await Should.ThrowAsync<TraconException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        exception.ErrorType.ShouldBe("upstream_error");
    }

    [Fact]
    public async Task Retry_overrides_a_built_in_non_retryable_failure()
    {
        // An SDK-specific failure the built-in closed set does not recognize
        // (and therefore would NOT retry) - the consumer knows better.
        var primary = new FakeModelProvider(
            new FakeChatClient(_ => throw new InvalidOperationException("Acme.Sdk.ThrottledException")),
            name: "primary");
        var fallback = new FakeModelProvider(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer"))),
            name: "fallback");

        using var chatClient = new ModelProviderRegistry(
            [primary, fallback],
            retryClassifier: new StubProviderRetryClassifier(ProviderRetryDecision.Retry))
            .CreateChatClient(Binding());

        var response = await chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("fallback answer");
    }

    [Fact]
    public async Task A_bare_cancellation_never_reaches_the_classifier()
    {
        using var cts = new CancellationTokenSource();
        var spy = new StubProviderRetryClassifier(ProviderRetryDecision.Retry);

        using var chatClient = RegistryWithFallback(
            primaryFactory: _ => throw new OperationCanceledException(cts.Token),
            classifier: spy);

        await Should.ThrowAsync<OperationCanceledException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        spy.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_cancellation_wrapped_in_another_exception_cannot_be_turned_into_a_retry()
    {
        // Even a classifier that ALWAYS says Retry must not be able to leak
        // a cancellation into a fallback attempt: the check runs BEFORE the
        // seam and cannot be overridden (F-149's "the seam cannot leak a
        // cancellation into a retry").
        var wrapped = new AggregateException("wrapped", new OperationCanceledException());
        var spy = new StubProviderRetryClassifier(ProviderRetryDecision.Retry);

        var primary = new FakeModelProvider(new FakeChatClient(_ => throw wrapped), name: "primary");
        var fallback = new FakeModelProvider(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "should never be reached"))),
            name: "fallback");

        using var chatClient = new ModelProviderRegistry([primary, fallback], retryClassifier: spy)
            .CreateChatClient(Binding());

        // A wrapped cancellation is a NON-retryable failure (same as an
        // authentication error): it propagates through ProviderFailureNormalizingChatClient
        // as the usual public-safe "upstream_error", not a fallback-chain-exhausted error -
        // the fallback link is never even attempted.
        var exception = await Should.ThrowAsync<TraconException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));

        exception.ErrorType.ShouldBe("upstream_error");
        spy.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_throwing_classifier_falls_back_to_the_built_in_rules_and_the_run_does_not_break()
    {
        // Built-in alone retries HTTP 500; the consumer's classifier is
        // broken (throws on every call). The observability/extension
        // function must not break the model-call path it decorates.
        using var chatClient = RegistryWithFallback(
            primaryMessage: "HTTP 500 (internal_server_error)",
            classifier: new ThrowingProviderRetryClassifier());

        var response = await chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("fallback answer");
    }

    [Fact]
    public async Task A_throwing_classifier_still_lands_on_the_built_in_non_retryable_decision()
    {
        // A throwing classifier is not a silent "always retry": for a
        // failure the built-in rules never retry (auth), it must still
        // surface instead of masking it.
        using var chatClient = RegistryWithFallback(
            primaryMessage: "HTTP 401 (invalid_api_key)",
            classifier: new ThrowingProviderRetryClassifier());

        await Should.ThrowAsync<TraconException>(
            () => chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task No_classifier_registered_preserves_todays_built_in_behavior()
    {
        using var chatClient = RegistryWithFallback(primaryMessage: "HTTP 500 (internal_server_error)", classifier: null);

        var response = await chatClient.GetResponseAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("fallback answer");
    }

    private static IChatClient RegistryWithFallback(
        string primaryMessage,
        IProviderRetryClassifier? classifier)
        => RegistryWithFallback(_ => throw new InvalidOperationException(primaryMessage), classifier);

    private static IChatClient RegistryWithFallback(
        Func<IEnumerable<ChatMessage>, ChatResponse> primaryFactory,
        IProviderRetryClassifier? classifier)
    {
        var primary = new FakeModelProvider(new FakeChatClient(primaryFactory), name: "primary");
        var fallback = new FakeModelProvider(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "fallback answer"))),
            name: "fallback");

        return new ModelProviderRegistry(
            [primary, fallback],
            retryClassifier: classifier,
            loggerFactory: NullLoggerFactory.Instance)
            .CreateChatClient(Binding());
    }

    private static ModelBinding Binding()
        => new()
        {
            Provider = "primary",
            Model = "primary-model",
            Fallbacks = [new ModelFallback { Provider = "fallback", Model = "fallback-model" }],
        };

    private sealed class StubProviderRetryClassifier(ProviderRetryDecision decision) : IProviderRetryClassifier
    {
        public int CallCount { get; private set; }

        public ProviderRetryDecision Classify(Exception exception)
        {
            CallCount++;
            return decision;
        }
    }

    private sealed class ThrowingProviderRetryClassifier : IProviderRetryClassifier
    {
        public ProviderRetryDecision Classify(Exception exception) => throw new InvalidOperationException("classifier is broken");
    }
}
