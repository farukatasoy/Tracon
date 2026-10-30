namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:RateLimit</c> really reaches <see cref="TraconRateLimitOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </remarks>
public sealed class TraconRateLimitOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        // An enum name matches case-insensitively.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:RateLimit:Enabled"] = "true",
            ["Tracon:RateLimit:PermitLimit"] = "30",
            ["Tracon:RateLimit:Window"] = "00:00:30",
            ["Tracon:RateLimit:Partition"] = "global",
            ["Tracon:RateLimit:Scope"] = "deployment",
        };

        var options = SectionBinding.Bind<TraconRateLimitOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.PermitLimit.ShouldBe(30);
        options.Window.ShouldBe(TimeSpan.FromSeconds(30));
        options.Partition.ShouldBe(RateLimitPartitionKind.Global);
        options.Scope.ShouldBe(RateLimitScope.Deployment);

        // QueueLimit cannot share a configuration with Scope = Deployment (the validator refuses the pair),
        // so the next test covers it with Scope = Instance.
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:RateLimit", options, nameof(TraconRateLimitOptions.QueueLimit));
    }

    [Fact]
    public void The_queue_limit_is_bound_while_the_scope_is_instance()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:RateLimit:QueueLimit"] = "4",
            ["Tracon:RateLimit:Scope"] = "instance",
        };

        var options = SectionBinding.Bind<TraconRateLimitOptions>(values);

        options.QueueLimit.ShouldBe(4);
        options.Scope.ShouldBe(RateLimitScope.Instance);
    }

    [Fact]
    public void A_queue_limit_with_the_deployment_scope_stops_the_application_at_start()
    {
        // 🚨 A database counter has no waiting line. Accepting the pair would give the operator a limit that
        // behaves differently from the settings they wrote.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:RateLimit:Enabled"] = "true",
            ["Tracon:RateLimit:QueueLimit"] = "4",
            ["Tracon:RateLimit:Scope"] = "Deployment",
        };

        var failure = SectionBinding.FailsAtStartup(values);

        failure.Failures.ShouldContain(
            message => message.Contains(nameof(TraconRateLimitOptions.QueueLimit), StringComparison.Ordinal)
                && message.Contains(nameof(RateLimitScope.Deployment), StringComparison.Ordinal));
    }

    [Fact]
    public void The_deployment_scope_without_a_queue_starts_cleanly()
    {
        SectionBinding.StartsCleanly(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:RateLimit:Enabled"] = "true",
            ["Tracon:RateLimit:Scope"] = "Deployment",
        });
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconRateLimitOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.PermitLimit.ShouldBe(60);
        options.Window.ShouldBe(TimeSpan.FromMinutes(1));
        options.QueueLimit.ShouldBe(0);
        options.Partition.ShouldBe(RateLimitPartitionKind.Tenant);
        options.Scope.ShouldBe(RateLimitScope.Instance);
    }
}
