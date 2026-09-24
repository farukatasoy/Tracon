using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Hosting;

/// <summary>
/// Every option that becomes the period of a background timer must fail at
/// startup when the timer cannot take it (F-278).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PeriodicTimer"/> accepts one millisecond to about 49.7 days.
/// Each Tracon background service creates its timer inside
/// <c>ExecuteAsync</c>, and .NET's default
/// <c>BackgroundServiceExceptionBehavior</c> is <c>StopHost</c>. Measured on
/// the sample app: <c>Tracon__Approvals__ScanInterval=00:00:00</c> logged
/// "Application started" and then stopped the host with
/// <c>ArgumentOutOfRangeException (Parameter 'period')</c> — approval
/// expiration is on by default, so a default setup was one bad value away.
/// </para>
/// <para>
/// The tests go through the real registration path (<c>AddTracon</c> →
/// <see cref="IStartupValidator"/>), which is what the host runs. A validator
/// that exists but is not registered with <c>ValidateOnStart</c> stays red.
/// </para>
/// </remarks>
public sealed class TimerPeriodOptionValidationTests
{
    [Theory]
    [MemberData(nameof(RejectedPeriods))]
    public void Approval_expiration_scan_interval_is_checked_at_startup(string value)
        => ShouldFailAtStartup("ScanInterval", ("Tracon:Approvals:ScanInterval", value));

    [Fact]
    public void Approval_scan_interval_is_not_checked_while_expiration_is_off()
        => ShouldStart(
            ("Tracon:Approvals:ExpirationEnabled", "false"),
            ("Tracon:Approvals:ScanInterval", "00:00:00"));

    [Theory]
    [MemberData(nameof(RejectedPeriods))]
    public void Canary_scan_interval_is_checked_at_startup_while_auto_rollback_is_on(string value)
        => ShouldFailAtStartup(
            "ScanInterval",
            ("Tracon:Canary:AutoRollbackEnabled", "true"),
            ("Tracon:Canary:ScanInterval", value));

    [Fact]
    public void Canary_scan_interval_is_not_checked_while_auto_rollback_is_off()
        => ShouldStart(("Tracon:Canary:ScanInterval", "00:00:00"));

    [Theory]
    [MemberData(nameof(RejectedPeriods))]
    public void Run_reconciliation_scan_interval_is_checked_at_startup(string value)
        => ShouldFailAtStartup(
            "ScanInterval",
            ("Tracon:RunReconciliation:Enabled", "true"),
            ("Tracon:RunReconciliation:ScanInterval", value));

    [Theory]
    [MemberData(nameof(RejectedPeriods))]
    public void Run_heartbeat_interval_is_checked_at_startup(string value)
        => ShouldFailAtStartup(
            "HeartbeatInterval",
            ("Tracon:RunReconciliation:Enabled", "true"),
            ("Tracon:RunReconciliation:HeartbeatInterval", value),
            // Keeps the unrelated "threshold >= heartbeat" rule quiet.
            ("Tracon:RunReconciliation:OrphanThreshold", "60.00:00:00"));

    [Fact]
    public void Run_reconciliation_timers_are_not_checked_beyond_zero_while_it_is_off()
        => ShouldStart(
            ("Tracon:RunReconciliation:ScanInterval", "50.00:00:00"),
            ("Tracon:RunReconciliation:HeartbeatInterval", "00:00:00.0005"));

    [Theory]
    [MemberData(nameof(RejectedPeriods))]
    public void Job_poll_interval_is_checked_at_startup(string value)
        => ShouldFailAtStartup("PollInterval", ("Tracon:Scheduling:PollInterval", value));

    [Theory]
    [InlineData("00:00:00.0010")]   // renewal every 0.5 ms
    [InlineData("100.00:00:00")]    // renewal every 50 days
    public void Job_lease_duration_whose_renewal_timer_cannot_run_is_checked_at_startup(string value)
        => ShouldFailAtStartup("LeaseDuration", ("Tracon:Scheduling:LeaseDuration", value));

    [Fact]
    public void Job_timers_are_not_checked_beyond_zero_in_a_process_without_a_worker()
        => ShouldStart(
            ("Tracon:Scheduling:RunWorker", "false"),
            ("Tracon:Scheduling:PollInterval", "50.00:00:00"),
            ("Tracon:Scheduling:LeaseDuration", "100.00:00:00"));

    [Fact]
    public void Singleton_lease_duration_whose_renewal_timer_cannot_run_is_checked_at_startup()
        => ShouldFailAtStartup(
            "LeaseDuration",
            ("Tracon:SingletonExecution:Enabled", "true"),
            // Renewal runs at a third of the lease: every 50 days.
            ("Tracon:SingletonExecution:LeaseDuration", "150.00:00:00"));

    [Theory]
    [InlineData("00:00:00.0005")]
    [InlineData("50.00:00:00")]
    public void Provider_health_background_interval_is_checked_at_startup(string value)
        => ShouldFailAtStartup("BackgroundInterval", ("Tracon:Health:BackgroundInterval", value));

    [Fact]
    public void Retention_batch_delay_longer_than_a_delay_can_wait_is_checked_at_startup()
        => ShouldFailAtStartup("BatchDelay", ("Tracon:Retention:BatchDelay", "50.00:00:00"));

    [Fact]
    public void Retention_batch_delay_of_zero_still_means_no_pause()
        => ShouldStart(("Tracon:Retention:BatchDelay", "00:00:00"));

    [Theory]
    [MemberData(nameof(AcceptedPeriods))]
    public void Every_period_the_timer_takes_starts(string value)
        => ShouldStart(
            ("Tracon:Approvals:ScanInterval", value),
            ("Tracon:Canary:AutoRollbackEnabled", "true"),
            ("Tracon:Canary:ScanInterval", value),
            ("Tracon:RunReconciliation:Enabled", "true"),
            ("Tracon:RunReconciliation:ScanInterval", value),
            ("Tracon:RunReconciliation:HeartbeatInterval", value),
            ("Tracon:RunReconciliation:OrphanThreshold", "60.00:00:00"),
            ("Tracon:Scheduling:PollInterval", value),
            ("Tracon:Health:BackgroundInterval", value));

    [Theory]
    [MemberData(nameof(RejectedPeriods))]
    public void Every_rejected_period_is_one_the_timer_itself_refuses(string value)
    {
        // The rule mirrors the runtime's range. If the runtime widens it, this
        // test says so instead of the validator silently refusing a good value.
        var period = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Should.Throw<ArgumentOutOfRangeException>(() => new PeriodicTimer(period, TimeProvider.System).Dispose());
        TimerPeriod.IsValid(period).ShouldBeFalse();
    }

    public static TheoryData<string> RejectedPeriods =>
    [
        "00:00:00",
        "-00:00:01",
        "00:00:00.0005",
        "50.00:00:00",
    ];

    public static TheoryData<string> AcceptedPeriods =>
    [
        "00:00:00.0010",
        "00:00:30",
        "49.00:00:00",
    ];

    private static void ShouldFailAtStartup(string optionName, params (string Key, string Value)[] settings)
    {
        using var provider = Build(settings);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(optionName);
    }

    private static void ShouldStart(params (string Key, string Value)[] settings)
    {
        using var provider = Build(settings);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider Build((string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        return services.BuildServiceProvider();
    }
}
