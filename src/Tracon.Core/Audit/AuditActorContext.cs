using System.Security.Claims;

namespace Tracon;

/// <summary>
/// An ambient context that carries the user of the current call.
/// </summary>
/// <remarks>
/// <para>
/// <c>Tracon.AspNetCore</c> writes <c>HttpContext.User</c> to <see cref="Current"/>
/// at the start of every protected request, after security checks pass. The value is
/// held in <see cref="AsyncLocal{T}"/>, so code later in the same request's async call
/// chain, such as a store decorator, can read it. This is the same mechanism as <c>Activity.Current</c>.
/// </para>
/// <para>
/// This design lets <c>Tracon.Core</c> read the actor without adding a dependency
/// on <c>IHttpContextAccessor</c> or ASP.NET Core. <see cref="ClaimsPrincipal"/> is in
/// the base .NET library, not specific to ASP.NET Core.
/// </para>
/// </remarks>
internal static class AuditActorContext
{
    private static readonly AsyncLocal<ClaimsPrincipal?> CurrentHolder = new();

    /// <summary>The user of the current call, or <see langword="null"/> when it is not set.</summary>
    public static ClaimsPrincipal? Current
    {
        get => CurrentHolder.Value;
        set => CurrentHolder.Value = value;
    }
}
