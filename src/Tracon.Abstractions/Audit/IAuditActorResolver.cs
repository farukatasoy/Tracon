namespace Tracon;

/// <summary>Resolves the actor (the who) of the current call.</summary>
/// <remarks>
/// <para>
/// The default implementation reads the user of the HTTP request. It returns
/// <see langword="null"/> when there is no authentication or the actor cannot be
/// resolved; that state is not hidden, and the user interface shows "unknown".
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins. The default
/// implementation is safe as a singleton because it does not capture any
/// per-request state itself — it reads <see cref="Resolve"/>'s caller on each
/// call from <c>AuditActorContext.Current</c>, an <see cref="System.Threading.AsyncLocal{T}"/>
/// ambient scope that <c>Tracon.AspNetCore</c> populates per request (the
/// same mechanism as <see cref="System.Diagnostics.Activity.Current"/>). A
/// replacement implementation that instead captured a scoped dependency in
/// its constructor would be a captive dependency.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> This interface
/// answers "which user", never "which tenant"; it carries no tenant
/// parameter and applies no tenant filtering of its own.
/// </para>
/// </remarks>
public interface IAuditActorResolver
{
    /// <summary>Resolves the current actor.</summary>
    /// <returns>The actor id, or <see langword="null"/> when it cannot be resolved.</returns>
    string? Resolve();
}
