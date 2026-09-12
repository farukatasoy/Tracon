using Microsoft.AspNetCore.Http;

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
internal sealed class DemoRunAttributionContext(IHttpContextAccessor accessor) : IRunAttributionContext
{
    /// <summary>The header carrying the demonstration user identity.</summary>
    public const string UserHeader = "X-Demo-User";

    /// <summary>The header carrying labels, as a comma-separated list of <c>key=value</c> pairs.</summary>
    public const string LabelHeader = "X-Demo-Labels";

    /// <inheritdoc />
    public string? UserId
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers[UserHeader].ToString();

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The limits on <see cref="RunLabels"/> are deliberately NOT enforced here.
    /// Tracon checks them itself before the run starts and answers 400; a
    /// sample that silently trimmed the header first would hide the very
    /// behaviour it exists to demonstrate.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Labels
    {
        get
        {
            var raw = accessor.HttpContext?.Request.Headers[LabelHeader].ToString();

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
