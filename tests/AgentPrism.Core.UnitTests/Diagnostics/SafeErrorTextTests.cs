namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// Phase 119 (BL-027/BL-037): the shared rule every persistence/response sink
/// routes a caught exception through before writing it.
/// </summary>
public sealed class SafeErrorTextTests
{
    [Fact]
    public void An_AgentPrismException_own_message_is_preserved()
    {
        var exception = new AgentPrismContentBlockedException("The request was blocked by the 'pii' guard.");

        SafeErrorText.ForPersistence(exception, "abc12345").ShouldBe(exception.Message);
    }

    [Fact]
    public void A_foreign_exceptions_own_message_never_appears()
    {
        const string ProviderSecret = "https://internal-provider.local:8443/v1/chat?key=sk-abc123";

        var text = SafeErrorText.ForPersistence(new HttpRequestException(ProviderSecret), "abc12345");

        text.ShouldNotContain(ProviderSecret);
        text.ShouldContain(nameof(HttpRequestException));
    }

    [Fact]
    public void The_correlation_id_passed_in_is_embedded_in_the_safe_text()
    {
        var text = SafeErrorText.ForPersistence(new InvalidOperationException("boom"), "cafe1234");

        text.ShouldContain("cafe1234");
    }

    [Fact]
    public void A_bare_OperationCanceledException_is_treated_like_any_other_foreign_exception()
    {
        // SafeErrorText itself makes no special case for cancellation - every call site
        // already filters OperationCanceledException out before reaching here.
        var text = SafeErrorText.ForPersistence(new OperationCanceledException("cancelled by caller"), "abc12345");

        text.ShouldNotContain("cancelled by caller");
        text.ShouldContain(nameof(OperationCanceledException));
    }

    [Fact]
    public void NewCorrelationId_returns_a_non_empty_value()
        => SafeErrorText.NewCorrelationId().ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public void NewCorrelationId_does_not_repeat_across_consecutive_calls()
    {
        var first = SafeErrorText.NewCorrelationId();
        var second = SafeErrorText.NewCorrelationId();

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
    }
}
