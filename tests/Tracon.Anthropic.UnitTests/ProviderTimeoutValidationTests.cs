namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// The provider's request timeout must fail at startup when an HTTP stack cannot
/// take it.
/// </summary>
/// <remarks>
/// The value reaches the SDK's HTTP stack and the health check's
/// <see cref="CancellationTokenSource"/>. Past <see cref="int.MaxValue"/>
/// milliseconds (about 24.8 days) one refuses it at the first request and another
/// wraps it into a negative value; the validator refused only zero and negative
/// values.
/// </remarks>
public sealed class ProviderTimeoutValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25 * 24 * 60 * 60)]
    public void A_timeout_an_http_stack_cannot_take_fails(int seconds)
        => Failures(TimeSpan.FromSeconds(seconds)).ShouldContain(failure => failure.Contains("Timeout", StringComparison.Ordinal));

    [Fact]
    public void The_longest_timeout_every_http_stack_takes_passes()
        => Failures(TimeSpan.FromDays(24)).ShouldNotContain(failure => failure.Contains("Timeout", StringComparison.Ordinal));

    private static IEnumerable<string> Failures(TimeSpan timeout)
        => new AnthropicProviderOptionsValidator().Validate(name: null, new AnthropicProviderOptions { Timeout = timeout }).Failures ?? [];
}
