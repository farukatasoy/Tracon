using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The three limits of <c>Tracon:InboundTriggers</c> must be positive, and a value that is not stops the host at start.
/// </summary>
/// <remarks>
/// <para>
/// Each of them used to accept zero and a negative number. A body limit of zero refused every request that has a body
/// (<c>413</c>), a tolerance of zero called every request stale (<c>401</c> for a caller whose signature was right), and a
/// rate below one was raised to one request per minute where it is read. In each case the setting looked applied and meant
/// something else. <see cref="ConfigurationPermutationTests"/> found them the day the section got a name.
/// </para>
/// <para>
/// The cases go through the container and <see cref="IStartupValidator"/>, from configuration and from code: a value
/// assigned in code never passes the binder.
/// </para>
/// </remarks>
public sealed class TraconInboundTriggerOptionsValidationTests
{
    private const string Section = TraconInboundTriggerOptions.SectionName;

    [Theory]
    [InlineData("TimestampTolerance", "00:00:00")]
    [InlineData("TimestampTolerance", "-00:00:01")]
    [InlineData("MaxBodyBytes", "0")]
    [InlineData("MaxBodyBytes", "-1")]
    [InlineData("MaxRequestsPerMinute", "0")]
    [InlineData("MaxRequestsPerMinute", "-1")]
    public void A_limit_of_zero_or_less_stops_the_host_and_is_named(string member, string text)
    {
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{Section}:{member}"] = text,
        });

        failure.Message.ShouldContain($"{nameof(TraconInboundTriggerOptions)}.{member}");
        failure.Message.ShouldContain("Actual value");
    }

    [Fact]
    public void A_limit_of_zero_assigned_in_code_stops_the_host()
    {
        var services = new ServiceCollection();
        services.AddTracon();
        services.Configure<TraconInboundTriggerOptions>(static options =>
        {
            options.TimestampTolerance = TimeSpan.Zero;
            options.MaxBodyBytes = 0;
            options.MaxRequestsPerMinute = 0;
        });

        using var provider = services.BuildServiceProvider();

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        failure.Failures.Count().ShouldBe(3, "all three limits are reported together");
    }

    [Theory]
    [InlineData("TimestampTolerance", "00:00:01")]
    [InlineData("MaxBodyBytes", "1")]
    [InlineData("MaxRequestsPerMinute", "1")]
    public void The_smallest_positive_limit_starts_the_host(string member, string text)
        => SectionBinding.StartsCleanly(new Dictionary<string, string?>(StringComparer.Ordinal) { [$"{Section}:{member}"] = text });

    [Fact]
    public void The_shipped_defaults_start_the_host()
        => SectionBinding.StartsCleanly(SectionBinding.NoKeys);
}
