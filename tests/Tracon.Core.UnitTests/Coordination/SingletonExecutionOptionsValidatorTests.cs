using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Coordination;

/// <summary>
/// Verifies fail-fast validation for <see cref="SingletonExecutionOptions"/>.
/// </summary>
/// <remarks>
/// The lease minimum exists because <see cref="SingletonGuard"/> renews at one
/// third of <see cref="SingletonExecutionOptions.LeaseDuration"/> but never
/// faster than <see cref="SingletonGuard.MinimumRenewInterval"/>. Below the
/// minimum the renewal lands ON or AFTER the expiry, and a second instance
/// takes the lease over while the owner is still alive and working.
/// </remarks>
public sealed class SingletonExecutionOptionsValidatorTests
{
    [Fact]
    public void A_lease_shorter_than_the_minimum_is_rejected()
    {
        var result = Validate(new SingletonExecutionOptions
        {
            Enabled = true,
            LeaseDuration = TimeSpan.FromSeconds(1),
        });

        result.Succeeded.ShouldBeFalse();
        result.Failures!.ShouldContain(
            failure => failure.Contains(nameof(SingletonExecutionOptions.LeaseDuration), StringComparison.Ordinal));
    }

    [Fact]
    public void The_shortest_accepted_lease_is_accepted()
    {
        var result = Validate(new SingletonExecutionOptions
        {
            Enabled = true,
            LeaseDuration = SingletonGuard.MinimumLeaseDuration,
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void The_default_lease_is_accepted()
    {
        Validate(new SingletonExecutionOptions { Enabled = true }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_short_lease_is_not_rejected_while_selection_is_disabled()
    {
        // 🚨 The renewal loop never runs while selection is off, so a short
        // lease cannot flap. Only the runtime hazard is gated on Enabled; the
        // zero check below stays unconditional - that value is nonsense either way.
        var result = Validate(new SingletonExecutionOptions
        {
            Enabled = false,
            LeaseDuration = TimeSpan.FromSeconds(1),
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_lease_of_zero_is_rejected_even_while_selection_is_disabled()
    {
        var result = Validate(new SingletonExecutionOptions
        {
            Enabled = false,
            LeaseDuration = TimeSpan.Zero,
        });

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Renewal_stays_strictly_inside_the_shortest_accepted_lease()
    {
        // 🚨 The gate that keeps the two constants in sync: lowering the
        // validator minimum without lowering the renewal floor brings back the
        // takeover-while-alive defect this test class documents.
        foreach (var lease in new[]
        {
            SingletonGuard.MinimumLeaseDuration,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(10),
        })
        {
            SingletonGuard.ComputeRenewInterval(lease).ShouldBeLessThan(lease, $"lease={lease}");
        }
    }

    private static ValidateOptionsResult Validate(SingletonExecutionOptions options)
        => new SingletonExecutionOptionsValidator().Validate(name: null, options);
}
