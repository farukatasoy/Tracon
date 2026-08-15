namespace AgentPrism;

/// <summary>Resolves the actor (the who) of the current call.</summary>
/// <remarks>
/// The default implementation reads the user of the HTTP request. It returns
/// <see langword="null"/> when there is no authentication or the actor cannot be
/// resolved; that state is not hidden, and the user interface shows "unknown".
/// </remarks>
public interface IAuditActorResolver
{
    /// <summary>Resolves the current actor.</summary>
    /// <returns>The actor id, or <see langword="null"/> when it cannot be resolved.</returns>
    string? Resolve();
}
