namespace Tracon;

/// <summary>
/// Forces <see cref="JobHandlerRegistry"/> to be built while the host starts.
/// </summary>
/// <remarks>
/// A duplicate or reserved handler key is a configuration mistake, and a
/// configuration mistake must break the host's start, not the worker's first
/// tick — a worker error is logged and swallowed (background errors must not
/// break functionality), so without this the mistake would surface as jobs
/// quietly running the wrong handler, or as one log line nobody reads.
/// </remarks>
internal sealed class JobHandlerRegistryValidator(JobHandlerRegistry registry) : ITraconStartupCheck
{
    /// <inheritdoc />
    public StartupCheckKind Kind => StartupCheckKind.Gate;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolving the registry is the validation: JobHandlerRegistry.Create
        // has already thrown by the time this constructor ran.
        _ = registry;

        return Task.CompletedTask;
    }
}
