namespace Tracon;

/// <summary>
/// Decides whether a header or property NAME looks like it carries a
/// credential. The one list both the audit secret filter and the save rules
/// for MCP server and webhook headers read.
/// </summary>
/// <remarks>
/// <para>
/// A stored record's plain <c>Headers</c> go to the database as written. A
/// header whose name looks like a credential therefore has to be declared by
/// the NAME of the configuration key its value is read from
/// (<c>HeaderConfigurationKeys</c>), and a save that puts one into
/// the plain headers is rejected.
/// </para>
/// <para>
/// The rule is a name heuristic and has two known gaps: <c>X-Idempotency-Key</c>
/// is classified as a credential (false positive, the value moves to
/// configuration), and <c>X-Auth</c>, <c>X-Signature</c> or <c>X-Session-Id</c>
/// are not (false negatives). A read masks every header value anyway, and the
/// audit trail records header names only.
/// </para>
/// </remarks>
internal static class CredentialHeaderNames
{
    /// <summary>
    /// The fragments whose presence in a normalized name marks a secret.
    /// </summary>
    /// <remarks>
    /// Matched after <see cref="Normalize"/>: <c>x-api-key</c>, <c>api_key</c>
    /// and <c>API.KEY</c> all reduce to a name that contains <c>apikey</c>.
    /// </remarks>
    private static readonly string[] SecretFragments =
    [
        "apikey",
        "authorization",
        "token",
        "password",
        "secret",
    ];

    /// <summary>
    /// Reports whether a header name carries a credential: it contains one of
    /// the secret fragments, it is <c>Cookie</c>, or it ends with <c>-key</c>.
    /// </summary>
    /// <param name="headerName">The header name.</param>
    /// <returns><see langword="true"/> when the name looks like a credential.</returns>
    /// <remarks>
    /// <c>-key</c> catches the gateway subscription headers the fragments miss
    /// (<c>Ocp-Apim-Subscription-Key</c>, <c>X-Functions-Key</c>). It is a
    /// header rule only: the audit filter judges property names, and a
    /// property such as <c>idempotencyKey</c> is not a credential there.
    /// </remarks>
    public static bool IsCredential(string headerName)
    {
        ArgumentNullException.ThrowIfNull(headerName);

        return ContainsSecretFragment(Normalize(headerName))
            || string.Equals(headerName, "Cookie", StringComparison.OrdinalIgnoreCase)
            || headerName.EndsWith("-key", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reports whether a normalized name contains a secret fragment.</summary>
    /// <param name="normalizedName">A name already passed through <see cref="Normalize"/>.</param>
    /// <returns><see langword="true"/> when a fragment matches.</returns>
    public static bool ContainsSecretFragment(string normalizedName)
    {
        ArgumentNullException.ThrowIfNull(normalizedName);

        foreach (var fragment in SecretFragments)
        {
            if (!normalizedName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A singular "token" is an authentication value, such as authToken
            // or accessToken, while the plural is a count, such as
            // maxOutputTokens, maxContextWindowTokens, or totalTokens. Redacting
            // the latter would make every agent audit record needlessly empty.
            // This was observed in a real agent.create record from the /tracon sample.
            if (string.Equals(fragment, "token", StringComparison.Ordinal) &&
                normalizedName.Contains("tokens", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>Removes the characters that only separate words in a name.</summary>
    /// <param name="name">The header or property name.</param>
    /// <returns>The name with only its letters and digits.</returns>
    /// <remarks>
    /// Separators are stripped before matching. The fragments are written
    /// without them, so "apiKey" matched but "x-api-key", "xi-api-key" and
    /// "api_key" did not - the separator broke the "apikey" run apart and a
    /// live provider key reached audit_log.after in clear text.
    /// </remarks>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var needsWork = false;

        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character))
            {
                needsWork = true;
                break;
            }
        }

        if (!needsWork)
        {
            return name;
        }

        return string.Create(name.Length, name, static (span, source) =>
        {
            var length = 0;

            foreach (var character in source)
            {
                if (char.IsLetterOrDigit(character))
                {
                    span[length++] = character;
                }
            }

            span[length..].Fill(' ');
        }).TrimEnd();
    }

    /// <summary>
    /// Reports whether a string is a valid HTTP field name: a non-empty RFC 9110
    /// <c>token</c>. Spaces, <c>:</c>, CR and LF are all outside it.
    /// </summary>
    /// <param name="headerName">The candidate name.</param>
    /// <returns><see langword="true"/> when the name is a valid token.</returns>
    public static bool IsValidName(string? headerName)
    {
        if (string.IsNullOrEmpty(headerName))
        {
            return false;
        }

        foreach (var character in headerName)
        {
            if (!IsTokenCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTokenCharacter(char character)
        => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
