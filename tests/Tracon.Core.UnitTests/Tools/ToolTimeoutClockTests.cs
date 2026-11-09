using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// The timeout of a tool is waited out on the host's <see cref="TimeProvider"/>, whichever way the chain around the tool
/// was composed: by the registry, by the registry's factory from the container, by the built-in tool governance or by the
/// chain composer the MCP tool set uses.
/// </summary>
/// <remarks>
/// A chain that quietly kept the system's clock would make every host-level test of a tool timeout wait the real time out,
/// and a host that registers its own clock (a test host, a simulation) would see tools time out on another time than its
/// runs do. Each test calls a tool that never ends, reads the timer the call armed and moves the clock to it.
/// </remarks>
public sealed class ToolTimeoutClockTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(7);

    [Fact]
    public async Task The_chain_composer_arms_the_timeout_on_the_clock_it_is_given()
    {
        var clock = new ManualClock();
        var registration = HangingRegistration("slow_tool", Limit);

        var tool = ToolWrapperChain.Compose(
            registration,
            Descriptor("slow_tool"),
            new AllowAllToolAuthorizationHandler(),
            TestData.OpenToolPolicy(),
            NoOpToolArgumentsValidator.Instance,
            defaultTimeout: TimeSpan.FromSeconds(30),
            defaultMaxOutputBytes: null,
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance,
            clock);

        await TimeOutAsync(tool, clock, Limit);
    }

    [Fact]
    public async Task The_registry_arms_the_timeout_of_a_tool_without_its_own_on_the_default_and_on_the_clock_it_is_given()
    {
        var clock = new ManualClock();
        var registration = new TraconToolRegistration(HangingFunction("slow_tool"));

        var registry = new ToolRegistry(
            [registration],
            new AllowAllToolAuthorizationHandler(),
            TestData.OpenToolPolicy(),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance,
            clock);

        registry.TryGet("slow_tool", out var tool).ShouldBeTrue();

        await TimeOutAsync(tool!, clock, new TraconOptions().Tools.DefaultTimeout);
    }

    [Fact]
    public async Task The_registry_the_container_builds_takes_the_clock_the_host_registered()
    {
        var clock = new ManualClock();
        await using var provider = ContainerWith(clock, HangingRegistration("slow_tool", Limit));

        provider.GetRequiredService<IToolRegistry>().TryGet("slow_tool", out var tool).ShouldBeTrue();

        await TimeOutAsync(tool!, clock, Limit);
    }

    [Fact]
    public async Task The_built_in_governance_the_container_builds_takes_the_clock_the_host_registered()
    {
        var clock = new ManualClock();
        await using var provider = ContainerWith(clock);

        var tool = provider.GetRequiredService<BuiltInToolGovernance>()
            .Govern(HangingFunction("file_access_read"), ToolEffect.Read, requiresApproval: false);

        await TimeOutAsync(tool, clock, new TraconOptions().Tools.DefaultTimeout);
    }

    [Fact]
    public async Task A_host_that_registers_no_clock_gets_the_systems_and_a_call_that_ends_in_time_returns_its_result()
    {
        // The system's clock is the default: nothing is registered, and the chain still builds and still runs the tool.
        await using var provider = ContainerWith(clock: null, new TraconToolRegistration(AIFunctionFactory.Create(() => "in time", "quick_tool")));

        provider.GetRequiredService<IToolRegistry>().TryGet("quick_tool", out var tool).ShouldBeTrue();

        var result = await ((AIFunction)tool!).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result?.ToString().ShouldNotBeNull().ShouldContain("in time");
    }

    /// <summary>
    /// Calls the tool, which never ends, reads the timer the call armed and moves the clock to it: the call ends with the
    /// sentence the model reads, and the clock moved by the limit and by nothing else.
    /// </summary>
    private static async Task TimeOutAsync(AIFunctionDeclaration tool, ManualClock clock, TimeSpan limit)
    {
        var invocation = ((AIFunction)tool).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)).AsTask();

        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the call to arm its timeout on the clock");

        clock.Pending.ShouldBe([limit], "the timer of the call is the tool's limit, on the clock of the host");

        await clock.AdvanceUntilAsync(invocation, within: limit);

        (await invocation)?.ToString().ShouldNotBeNull().ShouldContain("did not complete within");
        clock.Steps.ShouldBe([limit]);
    }

    private static ServiceProvider ContainerWith(ManualClock? clock, params TraconToolRegistration[] registrations)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IToolAuthorizationHandler>(new AllowAllToolAuthorizationHandler());
        services.AddSingleton(TestData.OpenToolPolicy());
        services.AddSingleton<IToolArgumentsValidator>(NoOpToolArgumentsValidator.Instance);
        services.AddSingleton(TestData.DefaultOptionsMonitor());
        services.AddSingleton<IOptions<TraconImageOptions>>(Options.Create(new TraconImageOptions()));

        foreach (var registration in registrations)
        {
            services.AddSingleton(registration);
        }

        services.AddSingleton<IToolRegistry>(ToolRegistry.Create);
        services.AddSingleton(BuiltInToolGovernance.Create);

        if (clock is not null)
        {
            services.AddSingleton<TimeProvider>(clock);
        }

        return services.BuildServiceProvider();
    }

    private static TraconToolRegistration HangingRegistration(string name, TimeSpan timeout)
        => new(HangingFunction(name)) { Timeout = timeout };

    private static AIFunction HangingFunction(string name)
        => AIFunctionFactory.Create(
            async () =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None); // delay: simulated

                return "too late";
            },
            name);

    private static ToolDescriptor Descriptor(string name) => new() { Name = name, Effect = ToolEffect.Read };
}
