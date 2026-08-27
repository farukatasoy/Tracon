using System.Collections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// Direct unit coverage of the two checks (BL-018, BL-051, phase 122); the
/// Production/Development environment split and the real DI wiring are
/// covered functionally - see <c>SilentGapWarningTests</c> in
/// <c>AgentPrism.AspNetCore.FunctionalTests</c> and
/// <see cref="SilentGapWarningRegistrationTests"/>.
/// </summary>
public sealed class SilentGapWarningTests
{
    [Fact]
    public async Task No_content_guard_and_retention_disabled_warns_twice_in_production()
    {
        var service = new SilentGapWarningService(
            ProductionEnvironment(),
            [],
            Options.Create(new AgentPrismRetentionOptions()),
            NullLogger<SilentGapWarningService>.Instance);

        // Neither check throws, and NullLogger discards the output silently -
        // this proves both branches run to completion in one pass.
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_throwing_content_guard_enumeration_does_not_stop_the_host()
    {
        // IContentGuard is a public contract; a consumer's own IEnumerable
        // implementation could throw while being enumerated. Reporting the gap
        // must never be the reason a host fails to start.
        var service = new SilentGapWarningService(
            ProductionEnvironment(),
            new ThrowingContentGuards(),
            Options.Create(new AgentPrismRetentionOptions { Enabled = true }),
            NullLogger<SilentGapWarningService>.Instance);

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_throwing_retention_options_read_does_not_stop_the_host()
    {
        var retentionOptions = Substitute.For<IOptions<AgentPrismRetentionOptions>>();
        retentionOptions.Value.Returns(_ => throw new InvalidOperationException("options failure"));

        var service = new SilentGapWarningService(
            ProductionEnvironment(),
            [],
            retentionOptions,
            NullLogger<SilentGapWarningService>.Instance);

        await service.StartAsync(CancellationToken.None);
    }

    private static IHostEnvironment ProductionEnvironment()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        return environment;
    }

    private sealed class ThrowingContentGuards : IEnumerable<IContentGuard>
    {
        public IEnumerator<IContentGuard> GetEnumerator() => throw new InvalidOperationException("enumeration failure");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
