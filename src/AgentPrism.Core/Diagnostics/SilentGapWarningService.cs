using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Warns once, while the host starts, when a Production installation leaves an
/// extension point unregistered that silently disables a feature rather than
/// failing loudly.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="NonPersistentStorageWarningService"/>'s three rules: it
/// warns only in Production, it never throws, and it looks only at which
/// container registrations and configuration values are already in memory -
/// it opens no database connection.
/// </para>
/// <para>
/// No content guard and no retention policy are both deliberate, supported
/// defaults - a package upgrade must not start inspecting content or deleting
/// rows a consumer never asked for. That is why this is a warning and never an
/// exception: it reports the configuration, it does not report a broken setup.
/// </para>
/// <para>
/// <paramref name="environment"/> is optional for the same reason as the
/// storage warning: <c>AddAgentPrism()</c> is valid on a bare service
/// collection no host backs, and <see cref="IHostEnvironment"/> is registered
/// by the host, not by AgentPrism. Without it the environment is unknown, so
/// the service stays silent rather than guessing.
/// </para>
/// </remarks>
/// <param name="environment">The host environment, when a host provides one.</param>
/// <param name="contentGuards">The registered content guards.</param>
/// <param name="retentionOptions">The data retention settings.</param>
/// <param name="logger">The logger the warnings are written to.</param>
internal sealed class SilentGapWarningService(
    IHostEnvironment? environment,
    IEnumerable<IContentGuard> contentGuards,
    IOptions<AgentPrismRetentionOptions> retentionOptions,
    ILogger<SilentGapWarningService> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (environment is null || !environment.IsProduction())
        {
            return Task.CompletedTask;
        }

        WarnIfNoContentGuard();
        WarnIfRetentionDisabled();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private void WarnIfNoContentGuard()
    {
        bool anyRegistered;

        try
        {
            anyRegistered = contentGuards.Any();
        }
        catch (Exception exception)
        {
            // IContentGuard is a public contract, so this can run a consumer's
            // own constructor. Reporting the gap must never be the reason a
            // host fails to start.
            logger.LogDebug(
                exception,
                "The content guard registration check could not read the registered guards. No warning is reported.");

            return;
        }

        if (anyRegistered)
        {
            return;
        }

        logger.LogWarning(
            "AgentPrism is running in the Production environment with no IContentGuard registered: " +
            "no prompt or response is inspected before or after a model call. Register one with " +
            "AddPatternContentGuard() or AddContentGuard<T>() if content moderation is required. " +
            "No content guard is a supported mode; this message reports the configuration, it does " +
            "not report a broken setup.");
    }

    private void WarnIfRetentionDisabled()
    {
        bool enabled;

        try
        {
            enabled = retentionOptions.Value.Enabled;
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception,
                "The retention configuration check could not read AgentPrismRetentionOptions. No warning is reported.");

            return;
        }

        if (enabled)
        {
            return;
        }

        logger.LogWarning(
            "AgentPrism is running in the Production environment with data retention disabled " +
            "(AgentPrism:Retention:Enabled is false): rows accumulate indefinitely unless an " +
            "explicit database retention policy exists for every target you care about. Set " +
            "Enabled to true to apply the configuration-based defaults, or register a policy " +
            "through IRetentionPolicyStore. Unbounded retention is a supported mode; this message " +
            "reports the configuration, it does not report a broken setup.");
    }
}
