using System.Net;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Every decision that reads a provider failure reads the same graph, whatever a writer is shown of it.
/// </summary>
/// <remarks>
/// <para>
/// What a writer reaches through <see cref="Exception.InnerException"/> is a masked view of the provider client's
/// exception: type name, message and stack as text, no graph behind it. A decision cannot be made from that. These cases
/// are built so that the only evidence is something the view cannot carry: a status that lives in a property and
/// not in the text, a cancellation that is a frame type inside an aggregate, a timeout that is a frame type whose
/// message says nothing of it. Each case first shows that the view alone decides differently, and then that the exception
/// Tracon throws decides as the raw one does.
/// </para>
/// </remarks>
public sealed class ProviderFailureGraphTests
{
    private const string Value = "canary-graph-unit-value-4b19e7a0";

    public static TheoryData<string, Func<Exception>> Cases => new()
    {
        // The status is HttpRequestException.StatusCode; the message has no status text at all.
        { "status only in the property (429)", () => new HttpRequestException("transport said no", inner: null, HttpStatusCode.TooManyRequests) },
        { "status only in the property (404)", () => new HttpRequestException("transport said no", inner: null, HttpStatusCode.NotFound) },
        { "status only in the property (401)", () => new HttpRequestException("transport said no", inner: null, HttpStatusCode.Unauthorized) },
        { "status only in the property, behind an aggregate", () => new AggregateException("Retry failed", new HttpRequestException("transport said no", inner: null, HttpStatusCode.ServiceUnavailable)) },

        // The cancellation is a frame type inside an aggregate.
        { "cancellation only inside an aggregate", () => new AggregateException("Retry failed", new OperationCanceledException()) },

        // The timeout is a TimeoutException frame whose message does not say so.
        { "timeout only as a TimeoutException frame", () => new HttpRequestException("transport down", new TimeoutException("x")) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_exception_tagged_at_the_provider_boundary_decides_as_the_raw_exception_does(string label, Func<Exception> build)
    {
        _ = label;

        var raw = build();
        var mask = ProviderSecretMask.ForValue(Value);
        var tagged = new ForeignProviderInvocationException(raw, mask);

        Decide(tagged).ShouldBe(Decide(raw));
        CircuitBreakingChatClient.CountsAgainstProvider(tagged)
            .ShouldBe(CircuitBreakingChatClient.CountsAgainstProvider(new ForeignProviderInvocationException(raw, ProviderSecretMask.Empty)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_normalized_exception_decides_as_the_raw_exception_does(string label, Func<Exception> build)
    {
        _ = label;

        var raw = build();
        var normalized = ProviderInvocationException.UpstreamFailure(raw, "openai", ProviderSecretMask.ForValue(Value));
        var unmasked = ProviderInvocationException.UpstreamFailure(raw, "openai", ProviderSecretMask.Empty);

        Decide(normalized).ShouldBe(Decide(unmasked));
        FinalFailure.IsFinal(normalized).ShouldBe(FinalFailure.IsFinal(unmasked));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_view_alone_would_decide_differently_so_the_cases_prove_the_walk(string label, Func<Exception> build)
    {
        var raw = build();
        var view = new ForeignProviderInvocationException(raw, ProviderSecretMask.ForValue(Value)).InnerException!;

        // If a decision stopped at the inner exception it would read this, and it would not agree.
        view.ShouldBeOfType<MaskedFailureView>();
        Decide(view).ShouldNotBe(Decide(raw), $"the case '{label}' does not need the raw graph, so it proves nothing about the walk");
    }

    [Fact]
    public void A_cancellation_inside_the_tagged_exception_is_still_a_cancellation_and_a_status_property_still_vetoes()
    {
        var mask = ProviderSecretMask.ForValue(Value);

        FallbackRetryClassifier.IsCancellation(new ForeignProviderInvocationException(new AggregateException(new OperationCanceledException()), mask)).ShouldBeTrue();
        FallbackRetryClassifier.Classify(new ForeignProviderInvocationException(new AggregateException(new OperationCanceledException()), mask)).ShouldBe(FallbackSkipReason.None);
        FallbackRetryClassifier.Classify(new ForeignProviderInvocationException(new HttpRequestException("no", inner: null, HttpStatusCode.NotFound), mask)).ShouldBe(FallbackSkipReason.None);
        FallbackRetryClassifier.Classify(new ForeignProviderInvocationException(new HttpRequestException("no", inner: null, HttpStatusCode.BadGateway), mask)).ShouldBe(FallbackSkipReason.HttpError);
        FallbackRetryClassifier.Classify(new ForeignProviderInvocationException(new HttpRequestException("no", new TimeoutException("x")), mask)).ShouldBe(FallbackSkipReason.Timeout);
    }

    [Fact]
    public void An_auth_status_behind_a_server_error_frame_still_wins_through_the_tagged_exception()
    {
        // The outer frame reads 503; the 401 is in the property of a frame behind it. The veto walks every frame.
        var raw = new HttpRequestException("HTTP 503 upstream", new HttpRequestException("auth", inner: null, HttpStatusCode.Unauthorized));
        var tagged = new ForeignProviderInvocationException(raw, ProviderSecretMask.ForValue(Value));

        FallbackRetryClassifier.Classify(tagged).ShouldBe(FallbackRetryClassifier.Classify(raw));
        CircuitBreakingChatClient.CountsAgainstProvider(tagged).ShouldBeTrue();
    }

    [Fact]
    public void The_frames_of_a_tagged_graph_are_the_raw_frames_in_order_and_never_the_view()
    {
        var leaf = new InvalidOperationException("leaf");
        var branch = new HttpRequestException("branch", leaf);
        var other = new TimeoutException("other");
        var raw = new AggregateException("top", branch, other);

        var tagged = new ForeignProviderInvocationException(raw, ProviderSecretMask.ForValue(Value));
        var normalized = ProviderInvocationException.UpstreamFailure(raw, "openai", ProviderSecretMask.ForValue(Value));

        ProviderFailureGraph.Frames(tagged).ShouldBe([tagged, raw, branch, leaf, other]);
        ProviderFailureGraph.Frames(normalized).ShouldBe([normalized, raw, branch, leaf, other]);
        ProviderFailureGraph.Frames(tagged).ShouldNotContain(frame => frame is MaskedFailureView);
    }

    [Fact]
    public void A_plain_exception_is_walked_by_its_inner_exception()
    {
        var leaf = new InvalidOperationException("leaf");
        var middle = new InvalidOperationException("middle", leaf);
        var top = new TraconException("top", middle);

        ProviderFailureGraph.Frames(top).ShouldBe([top, middle, leaf]);
        ProviderFailureGraph.Children(leaf).ShouldBeEmpty();
    }

    [Fact]
    public void The_foreign_exception_is_found_behind_a_public_wrapper()
    {
        var tagged = new ForeignProviderInvocationException(new InvalidOperationException("x"), ProviderSecretMask.ForValue(Value));
        var wrapper = new TraconProviderUnavailableException("All providers failed.", tagged);

        ForeignProviderInvocationException.Find(wrapper).ShouldBeSameAs(tagged);
        ForeignProviderInvocationException.Find(new InvalidOperationException("none")).ShouldBeNull();
    }

    [Fact]
    public void The_decision_view_of_a_tagged_exception_has_the_masked_text_and_the_raw_inner_exception()
    {
        var raw = new HttpRequestException($"HTTP 401 Bearer {Value}", new InvalidOperationException($"deep {Value}"), HttpStatusCode.Unauthorized);
        var tagged = new ForeignProviderInvocationException(raw, ProviderSecretMask.ForValue(Value));

        var decision = ProviderFailureGraph.DecisionView(tagged);

        decision.ShouldBeOfType<ForeignProviderInvocationException>();
        decision.InnerException.ShouldBeSameAs(raw);
        decision.Message.ShouldNotContain(Value, Case.Sensitive);
        decision.ToString().ShouldNotContain(Value, Case.Sensitive);
        ((ForeignProviderInvocationException)decision).Original.ShouldBeSameAs(raw);

        // The decision view is not what a writer gets: the exception that travels up is untouched.
        tagged.InnerException.ShouldBeOfType<MaskedFailureView>();
    }

    [Fact]
    public void The_decision_view_of_a_normalized_exception_keeps_its_identity_and_status()
    {
        var raw = new HttpRequestException($"Bearer {Value}", inner: null, HttpStatusCode.Forbidden);
        var normalized = ProviderInvocationException.UpstreamFailure(raw, "openai", ProviderSecretMask.ForValue(Value));

        var decision = ProviderFailureGraph.DecisionView(normalized).ShouldBeOfType<ProviderInvocationException>();

        decision.ErrorType.ShouldBe(normalized.ErrorType);
        decision.StatusCode.ShouldBe(403);
        decision.Message.ShouldBe(normalized.Message);
        decision.InnerException.ShouldBeSameAs(raw);
        decision.ToString().ShouldNotContain(Value, Case.Sensitive);
    }

    [Fact]
    public void The_decision_view_of_any_other_exception_is_that_exception()
    {
        var other = new TraconProviderUnavailableException("open");

        ProviderFailureGraph.DecisionView(other).ShouldBeSameAs(other);
    }

    private static Decision Decide(Exception exception)
        => new(
            ProviderHttpStatus.Read(exception),
            ProviderHttpStatus.IsPermanentFailure(exception),
            FallbackRetryClassifier.Classify(exception),
            FallbackRetryClassifier.IsCancellation(exception),
            FallbackRetryClassifier.IsTimeout(exception));

    private sealed record Decision(
        int? Status,
        bool Permanent,
        FallbackSkipReason Reason,
        bool Cancellation,
        bool Timeout);
}
