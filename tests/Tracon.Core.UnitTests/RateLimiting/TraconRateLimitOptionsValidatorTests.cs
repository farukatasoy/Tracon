using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.RateLimiting;

/// <summary>Verifies the start-up validation of <see cref="TraconRateLimitOptions"/>.</summary>
public sealed class TraconRateLimitOptionsValidatorTests
{
    [Fact]
    public void A_queue_with_the_deployment_scope_is_refused()
    {
        var result = Validate(new TraconRateLimitOptions { Scope = RateLimitScope.Deployment, QueueLimit = 1 });

        result.Failed.ShouldBeTrue();
        result.Failures!.ShouldContain(
            failure => failure.Contains(nameof(TraconRateLimitOptions.QueueLimit), StringComparison.Ordinal));
    }

    [Fact]
    public void A_queue_with_the_instance_scope_is_accepted()
    {
        Validate(new TraconRateLimitOptions { Scope = RateLimitScope.Instance, QueueLimit = 10 }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void The_deployment_scope_without_a_queue_is_accepted()
    {
        Validate(new TraconRateLimitOptions { Scope = RateLimitScope.Deployment }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void The_default_options_are_accepted()
    {
        Validate(new TraconRateLimitOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_permit_limit_or_window_that_is_not_positive_keeps_its_old_meaning_and_is_not_refused()
    {
        // The filter has always read a value below one as one permit and a window that is not positive as one
        // minute. A new failure here would stop an application that runs today.
        Validate(new TraconRateLimitOptions { Scope = RateLimitScope.Deployment, PermitLimit = 0, Window = TimeSpan.Zero })
            .Succeeded.ShouldBeTrue();
    }

    private static ValidateOptionsResult Validate(TraconRateLimitOptions options)
        => new TraconRateLimitOptionsValidator().Validate(Options.DefaultName, options);
}
