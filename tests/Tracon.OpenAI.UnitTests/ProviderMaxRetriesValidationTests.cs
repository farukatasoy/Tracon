namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The number of retries the OpenAI client makes is a small, bounded count, and a value outside the bound fails at
/// startup instead of becoming a wait.
/// </summary>
public sealed class ProviderMaxRetriesValidationTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(1000)]
    public void A_count_outside_zero_to_ten_fails(int maxRetries)
        => Failures(maxRetries).ShouldContain(failure => failure.Contains("MaxRetries", StringComparison.Ordinal) && failure.Contains("0 to 10", StringComparison.Ordinal));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(10)]
    public void A_count_inside_the_bound_and_no_count_pass(int? maxRetries)
        => Failures(maxRetries).ShouldNotContain(failure => failure.Contains("MaxRetries", StringComparison.Ordinal));

    private static IEnumerable<string> Failures(int? maxRetries)
        => new OpenAIProviderOptionsValidator().Validate(name: null, new OpenAIProviderOptions { MaxRetries = maxRetries }).Failures ?? [];
}
