using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Reports, once while the host starts, every store contract where a storage
/// provider found the consuming application's own registration and left it in
/// place.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the OPPOSITE used to happen silently.
/// <c>UsePostgreSql</c> overwrote a consumer's <c>ITenantStore</c> with no log,
/// no warning and no startup failure, so the only symptom was a later request
/// reading from the wrong store. Keeping the consumer's registration is the
/// correct behavior and it is now what happens — but a composition decision
/// that changes which database a run is recorded in must be visible.
/// </para>
/// <para>
/// It never throws. Registering your own store and then calling
/// <c>UsePostgreSql</c> is a supported, documented combination
/// (<c>guides/write-your-own-store</c>): the provider still supplies the other
/// ~33 contracts. The message states what was kept, not that anything is wrong.
/// </para>
/// </remarks>
/// <param name="defaults">The marks a storage provider wrote while composing.</param>
/// <param name="logger">The logger the warnings are written to.</param>
internal sealed partial class PreservedStoreRegistrationWarningService(
    TraconDefaultRegistrations defaults,
    ILogger<PreservedStoreRegistrationWarningService> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var contract in defaults.Preserved)
        {
            RegistrationKept(logger, contract.Name);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "{Contract} stays bound to this application's own registration. A storage provider " +
                  "did not overwrite it, so that store does not use the provider's database. Remove your " +
                  "registration if the provider's store is what you meant to use.")]
    private static partial void RegistrationKept(ILogger logger, string contract);
}
