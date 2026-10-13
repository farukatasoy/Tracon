using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Hosting;

/// <summary>
/// Every option that becomes a timeout must fail at startup when the timer behind
/// it cannot take the value (F-280).
/// </summary>
/// <remarks>
/// <para>
/// <c>CancellationTokenSource.CancelAfter</c>, its <see cref="TimeSpan"/>
/// constructor and <see cref="Task.Delay(TimeSpan)"/> accept -1 ms (infinite) and
/// 0 to about 49.7 days; any other value throws
/// <see cref="ArgumentOutOfRangeException"/> where the timeout is armed - in a run,
/// a request, a delivery attempt or a shutdown, long after the host started. The
/// validators already refused zero and negative values; none of them had an upper
/// bound. The worst site measured by the class scan: the skill script runner arms
/// its timeout after the interpreter process has started, so the exception skipped
/// the kill and the process kept running.
/// </para>
/// <para>
/// Each check uses <c>TimerPeriod.IsValid</c> (one millisecond to about 49.7 days).
/// None of these options documents zero or infinite as meaningful. The tests go
/// through <c>AddTracon</c> → <see cref="IStartupValidator"/>, the path the host
/// runs; the other packages' options have their own tests next to their code.
/// </para>
/// </remarks>
public sealed class TimeoutOptionValidationTests
{
    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Default_tool_timeout_is_checked_at_startup(string value)
        => ShouldFailAtStartup("DefaultTimeout", ("Tracon:Tools:DefaultTimeout", value));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Approval_presentation_timeout_is_checked_at_startup(string value)
        => ShouldFailAtStartup("ApprovalPresentationTimeout", ("Tracon:Tools:ApprovalPresentationTimeout", value));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Validation_mcp_timeout_is_checked_at_startup(string value)
        => ShouldFailAtStartup("McpTimeout", ("Tracon:Validation:McpTimeout", value));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Skill_script_timeout_is_checked_at_startup_while_scripts_are_on(string value)
        => ShouldFailAtStartup(
            "Timeout",
            ("Tracon:Skills:Scripts:Enabled", "true"),
            ("Tracon:Skills:Scripts:PlatformIsolationAcknowledged", "true"),
            ("Tracon:Skills:Scripts:Timeout", value));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Child_deadline_is_checked_at_startup(string value)
        => ShouldFailAtStartup(
            "ChildDeadline",
            ("Tracon:AgentGraph:ChildDeadline", value),
            ("Tracon:AgentGraph:WaitTimeout", "60.00:00:00"));

    [Fact]
    public void Child_wait_timeout_is_checked_at_startup()
        => ShouldFailAtStartup("WaitTimeout", ("Tracon:AgentGraph:WaitTimeout", "50.00:00:00"));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Drain_timeout_is_checked_at_startup(string value)
        => ShouldFailAtStartup("Timeout", ("Tracon:Drain:Timeout", value));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Webhook_delivery_timeout_is_checked_at_startup(string value)
        => ShouldFailAtStartup("Timeout", ("Tracon:Webhooks:Timeout", value));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Job_max_duration_is_checked_at_startup(string value)
        => ShouldFailAtStartup(
            "MaxDurationByHandlerKey",
            ("Tracon:Scheduling:MaxDurationByHandlerKey:tracon.workflow", value));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Image_timeout_is_checked_at_startup(string value)
        => ShouldFailAtStartup("Timeout", ("Tracon:Images:Timeout", value));

    [Theory]
    [MemberData(nameof(RejectedTimeouts))]
    public void Live_voice_delegation_timeout_is_checked_at_startup(string value)
    {
        using var provider = Build(
            static (builder, section) => builder.UseLiveVoice(section.GetSection("Voice:Live")),
            ("Tracon:Voice:Live:DelegationTimeout", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(VoiceLiveOptions.DelegationTimeout));
    }

    [Theory]
    [InlineData("IdleTimeout")]
    [InlineData("MaxConnectionDuration")]
    public void Voice_conversation_timeouts_are_checked_at_startup(string option)
    {
        using var provider = Build(
            static (builder, section) => builder.UseVoiceConversation(section.GetSection("Voice:Conversation")),
            ($"Tracon:Voice:Conversation:{option}", "50.00:00:00"));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(option);
    }

    [Theory]
    [MemberData(nameof(AcceptedTimeouts))]
    public void Every_timeout_the_timer_takes_starts(string value)
        => ShouldStart(
            ("Tracon:Tools:DefaultTimeout", value),
            ("Tracon:Tools:ApprovalPresentationTimeout", value),
            ("Tracon:Validation:McpTimeout", value),
            ("Tracon:Drain:Timeout", value),
            ("Tracon:Webhooks:Timeout", value),
            ("Tracon:Images:Timeout", value),
            ("Tracon:Scheduling:MaxDurationByHandlerKey:tracon.workflow", value));

    [Fact]
    public void The_upper_bound_is_the_one_the_timer_itself_refuses()
    {
        // The rule mirrors the runtime's range. If the runtime widens it, this test
        // says so instead of the validators silently refusing a good value.
        var tooLong = TimeSpan.FromMilliseconds(TimerPeriod.MaxMilliseconds + 1);

        Should.Throw<ArgumentOutOfRangeException>(() => new CancellationTokenSource(tooLong).Dispose());
        Should.NotThrow(() => new CancellationTokenSource(TimeSpan.FromMilliseconds(TimerPeriod.MaxMilliseconds)).Dispose());
        TimerPeriod.IsValid(tooLong).ShouldBeFalse();
    }

    public static TheoryData<string> RejectedTimeouts =>
    [
        "00:00:00",
        "-00:00:01",
        "50.00:00:00",
    ];

    public static TheoryData<string> AcceptedTimeouts =>
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
        => Build(static (_, _) => { }, settings);

    private static ServiceProvider Build(
        Action<ITraconBuilder, IConfigurationSection> configure,
        params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        var services = new ServiceCollection();
        var section = configuration.GetSection(TraconOptions.SectionName);
        configure(services.AddTracon(section), section);

        return services.BuildServiceProvider();
    }
}
