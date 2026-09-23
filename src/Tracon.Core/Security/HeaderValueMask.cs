namespace Tracon;

/// <summary>
/// Hides the values of the extra request headers an operator stores on an MCP
/// server or a webhook subscription, wherever such a record leaves the
/// process: an HTTP response or the audit trail.
/// </summary>
/// <remarks>
/// <para>
/// A header value is free text. Nothing stops an operator from typing an API
/// key into it, and a read that returned it would hand that key to every
/// caller allowed to list the records — who could then call the target
/// directly. The mask therefore covers EVERY header, not only names that
/// look like credentials: a name heuristic misses <c>X-Auth</c> and
/// <c>X-Session-Id</c>. Only the names stay readable.
/// </para>
/// <para>
/// The mask is a presentation rule only. The store keeps the real value, and
/// the transport sends it. A save that sends the mask back is rejected
/// (<see cref="FindMaskedHeader"/>), because it would overwrite the real
/// value with the mask.
/// </para>
/// </remarks>
internal static class HeaderValueMask
{
    /// <summary>The fixed text every stored header value is replaced with.</summary>
    public const string Value = "***";

    /// <summary>Returns a copy of <paramref name="headers"/> with every value replaced by <see cref="Value"/>.</summary>
    /// <param name="headers">The stored headers.</param>
    /// <returns>The masked copy; the same instance when there is nothing to mask.</returns>
    public static IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (headers.Count == 0)
        {
            return headers;
        }

        var masked = new Dictionary<string, string>(headers.Count, StringComparer.Ordinal);

        foreach (var name in headers.Keys)
        {
            masked[name] = Value;
        }

        return masked;
    }

    /// <summary>Finds a header whose value is the mask itself.</summary>
    /// <param name="headers">The headers of a save request, or <see langword="null"/>.</param>
    /// <returns>The first such header name, or <see langword="null"/> when there is none.</returns>
    public static string? FindMaskedHeader(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return null;
        }

        foreach (var (name, value) in headers)
        {
            if (string.Equals(value, Value, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Builds the rejection text for a save that sent the mask back.</summary>
    /// <param name="headerName">The header that carries the mask.</param>
    /// <returns>The problem detail.</returns>
    public static string DescribeRejection(string headerName)
        => $"Header '{headerName}' carries the mask '{Value}'. A read never returns a stored header " +
           "value, so a client that reads, edits and saves a record must send the real value of " +
           "every header again.";
}
