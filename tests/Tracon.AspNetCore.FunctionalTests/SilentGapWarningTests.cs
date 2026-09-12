using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A Production installation that leaves content moderation or data retention
/// disabled says so on the server side (BL-018, BL-051, phase 122) - the same
/// silent-gap rationale as <see cref="NonPersistentStorageWarningTests"/>.
/// </summary>
/// <remarks>
/// These run at the functional level for the same reason as the storage
/// warning: the behavior crosses the DI and hosting boundary (which
/// registrations the container resolved, which environment the host reports).
/// </remarks>
public sealed class SilentGapWarningTests
{
    private const string ContentGuardFragment = "no IContentGuard registered";
    private const string RetentionFragment = "data retention disabled";

    [Fact]
    public async Task Production_with_no_content_guard_warns_exactly_once()
    {
        await using var host = await TraconTestHost.StartAsync(environment: Environments.Production);

        var matches = host.Logs.Entries
            .Where(entry => entry.Contains(ContentGuardFragment, StringComparison.Ordinal))
            .ToList();

        matches.Count.ShouldBe(1);
        matches[0].ShouldStartWith($"{LogLevel.Warning} {nameof(Tracon)}.{nameof(SilentGapWarningService)}");
    }

    [Fact]
    public async Task Development_with_no_content_guard_stays_quiet()
    {
        await using var host = await TraconTestHost.StartAsync(environment: Environments.Development);

        host.Logs.AllText.ShouldNotContain(ContentGuardFragment);
    }

    [Fact]
    public async Task A_registered_content_guard_stays_quiet_in_Production()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddPatternContentGuard(),
            environment: Environments.Production);

        host.Logs.AllText.ShouldNotContain(ContentGuardFragment);
    }

    [Fact]
    public async Task Production_with_retention_disabled_warns_exactly_once()
    {
        await using var host = await TraconTestHost.StartAsync(environment: Environments.Production);

        var matches = host.Logs.Entries
            .Where(entry => entry.Contains(RetentionFragment, StringComparison.Ordinal))
            .ToList();

        matches.Count.ShouldBe(1);
        matches[0].ShouldStartWith($"{LogLevel.Warning} {nameof(Tracon)}.{nameof(SilentGapWarningService)}");
    }

    [Fact]
    public async Task Development_with_retention_disabled_stays_quiet()
    {
        await using var host = await TraconTestHost.StartAsync(environment: Environments.Development);

        host.Logs.AllText.ShouldNotContain(RetentionFragment);
    }

    [Fact]
    public async Task Retention_enabled_stays_quiet_in_Production()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: services => services.Configure<TraconRetentionOptions>(options => options.Enabled = true),
            environment: Environments.Production);

        host.Logs.AllText.ShouldNotContain(RetentionFragment);
    }
}
