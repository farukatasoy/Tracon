using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// The caller of an A2A request, held where the code that finishes the turn after the request is over can read it.
/// </summary>
/// <remarks>
/// <para>
/// The A2A library runs a streamed turn on its own once the caller has gone: it keeps the model call going and saves the
/// session when the model answers, with a request that no longer exists. What the save asks of the application (the tenant,
/// the caller's isolation key, the user who owns a new conversation) was answered from <c>HttpContext</c> until then, and a
/// strict store refused the save for want of a key, so a finished turn was lost.
/// </para>
/// <para>
/// The answer is bound where the library cannot lose it: <see cref="Bind"/> wraps the result of the endpoint, and the wrapper
/// opens three ambient scopes in the body that executes the result, which is the body the library's turn starts under.
/// An ambient value flows down into everything started inside it, including the library's own task. A scope opened in a
/// helper that returns before the result runs would be gone by then.
/// </para>
/// </remarks>
internal static class A2ACallerScope
{
    private static readonly AsyncLocal<string?> IsolationKey = new();

    /// <summary>Gets the isolation key of the caller whose request started the turn that is running, or <see langword="null"/> outside one.</summary>
    public static string? CurrentIsolationKey => IsolationKey.Value;

    /// <summary>Wraps the answer to an A2A request so that the turn it starts keeps the caller after the request has ended.</summary>
    /// <param name="answer">The answer the endpoint produced.</param>
    /// <param name="httpContext">The request.</param>
    /// <returns>An answer that runs <paramref name="answer"/> with the caller's tenant, attribution and isolation key bound.</returns>
    public static IResult Bind(IResult answer, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(httpContext);

        var services = httpContext.RequestServices;

        // A tenant a custom ITenantContext names outside the alphabet is not bound: it is the context's defect to report,
        // and a scope that threw here would turn it into a failure of every A2A call.
        string? tenantId = null;

        if (services.GetService<ITenantContext>()?.TenantId is { } tenant && AmbientTenantScope.TryNormalize(tenant, out var canonical))
        {
            tenantId = canonical;
        }

        // An attribution the run start refuses (a user over 200 characters, too many labels) is left to the request:
        // a scope would hide it from that refusal, which reads the attribution from the same place.
        var attribution = services.GetService<IRunAttributionContext>();
        var bindAttribution = RunAttributionGate.Validate(attribution) is null;
        var (userId, labels) = bindAttribution ? RunAttributionReader.Read(attribution) : (null, null);

        var isolationKey = services.GetService<AgentIsolationKeyProvider>() is TraconCallerIsolationKeyProvider provider
            ? provider.ResolveFor(httpContext)
            : null;

        return new BoundResult(answer, tenantId, bindAttribution, userId, labels, isolationKey);
    }

    private static Restore Begin(string? isolationKey)
    {
        var previous = IsolationKey.Value;
        IsolationKey.Value = isolationKey;

        return new Restore(previous);
    }

    private sealed class BoundResult(
        IResult answer,
        string? tenantId,
        bool bindAttribution,
        string? userId,
        IReadOnlyDictionary<string, string>? labels,
        string? isolationKey) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            // 🚨 The scopes are opened HERE, in the body that executes the answer, and kept open until it is done. The library
            // starts a streamed turn when this answer is first read, so its task is created under these values; an async
            // helper that opened them would have restored them before then.
            using var tenant = tenantId is null ? null : AmbientTenantScope.Begin(tenantId);
            using var attribution = bindAttribution ? AmbientRunAttributionScope.Begin(userId, labels) : null;
            using var caller = Begin(isolationKey);

            await answer.ExecuteAsync(httpContext).ConfigureAwait(false);
        }
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => IsolationKey.Value = previous;
    }
}
