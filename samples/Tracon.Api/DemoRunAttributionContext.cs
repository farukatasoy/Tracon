using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Tracon.Api;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> Resolves run attribution from request
/// headers so the per-user and per-label cost breakdowns can be exercised without
/// a real identity provider.
/// </summary>
/// <remarks>
/// <para>
/// A real application binds <see cref="IRunAttributionContext"/> to its OWN
/// identity pipeline — a claim on the authenticated principal, the subject of a
/// validated token, an API key's owner. A header is the client's DECLARATION, not
/// proof of identity, exactly as it is for the tenant header
/// (<c>HttpTenantContext</c>); it is acceptable here only because this whole
/// sample runs on loopback behind a demonstration authentication scheme.
/// </para>
/// <para>
/// What this class does illustrate faithfully is the boundary that matters: the
/// value comes from the SERVER's own resolution, never from the run request body.
/// A <c>userId</c> sent in the body of <c>POST /api/agents/{name}/run</c> is
/// ignored, because the body is not a source of attribution at all.
/// </para>
/// <para>
/// The class is a <strong>singleton</strong> and reads the request through
/// <see cref="IHttpContextAccessor"/> — a scoped registration would be a captive
/// dependency in the singleton services that consume it.
/// </para>
/// </remarks>
/// <param name="accessor">The accessor that resolves the current request.</param>
internal sealed class DemoRunAttributionContext(IHttpContextAccessor accessor, IConfiguration configuration) : IRunAttributionContext
{
    /// <summary>
    /// The configuration key that makes the demonstration identity pipeline fail. <c>throw</c>
    /// makes <see cref="UserId"/> throw on every HTTP request, the way a real identity provider
    /// that is down does; any other value, or none, leaves it working. For manual tests only.
    /// </summary>
    public const string ModeKey = "Tracon:Demo:Attribution:Mode";

    /// <summary>The header carrying the demonstration user identity.</summary>
    public const string UserHeader = "X-Demo-User";

    /// <summary>The header carrying labels, as a comma-separated list of <c>key=value</c> pairs.</summary>
    public const string LabelHeader = "X-Demo-Labels";

    /// <inheritdoc />
    /// <remarks>
    /// Outside a request there is no header to read: a queued run, an approval
    /// continuation or a run continuation runs on the background worker. Tracon
    /// replays the user the job recorded through
    /// <see cref="AmbientRunAttributionScope"/>, and a context that answers there
    /// must read it, or the run's sub-agents and tools run under no user.
    /// </remarks>
    public string? UserId
    {
        get
        {
            if (accessor.HttpContext is not { } httpContext)
            {
                return AmbientRunAttributionScope.CurrentUserId;
            }

            // A manual-test switch: Tracon must record the run with no attribution, never fail it,
            // when the identity pipeline throws.
            if (string.Equals(configuration[ModeKey], "throw", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The demonstration identity pipeline is down (Tracon:Demo:Attribution:Mode = throw).");
            }

            var value = httpContext.Request.Headers[UserHeader].ToString();

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The limits on <see cref="RunLabels"/> are deliberately NOT enforced here.
    /// Tracon checks them itself before the run starts and answers 400; a
    /// sample that silently trimmed the header first would hide the very
    /// behaviour it exists to demonstrate. Outside a request, the labels the job
    /// recorded come from <see cref="AmbientRunAttributionScope"/>, as the user
    /// does.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Labels
    {
        get
        {
            if (accessor.HttpContext is not { } httpContext)
            {
                return AmbientRunAttributionScope.CurrentLabels;
            }

            var raw = httpContext.Request.Headers[LabelHeader].ToString();

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var labels = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);

                if (separator > 0)
                {
                    labels[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
                }
            }

            return labels.Count == 0 ? null : labels;
        }
    }
}
