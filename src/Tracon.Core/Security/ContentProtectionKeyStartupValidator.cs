using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Refuses to start the host when content protection is on and its active key has a value that cannot be a key.
/// </summary>
/// <remarks>
/// <para>
/// The key is read when the first value is written, and a recording write is best effort: a failure there is logged and
/// counted and the run goes on. A key of the wrong length therefore answered 200 for every run and left every protected
/// column empty, and the only trace was a counter. Reading the active key here moves the failure to the one moment an
/// operator is watching: the start. The failure names the configuration key and never carries a value.
/// </para>
/// <para>
/// Only the ACTIVE key is read, and only when it has a value. A key kept to read old rows can be missing for a while during
/// a rotation, and a host that refused to start for it would turn a rotation into an outage. A key with no value at all is
/// the shape of a host that runs on in-memory stores, where nothing calls the protector: the reference host and the OpenAPI
/// fuzz run ship the key name enabled and the value only with a SQL store. Only the protector Tracon ships is asked:
/// another <see cref="IContentProtector"/> decides for itself what a key is.
/// </para>
/// </remarks>
internal sealed class ContentProtectionKeyStartupValidator(IServiceProvider services) : ITraconStartupCheck
{
    /// <inheritdoc />
    public StartupCheckKind Kind => StartupCheckKind.Gate;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (services.GetService<IContentProtector>() is AesGcmContentProtector { IsEnabled: true } protector)
        {
            protector.ValidateActiveKey();
        }

        return Task.CompletedTask;
    }
}
