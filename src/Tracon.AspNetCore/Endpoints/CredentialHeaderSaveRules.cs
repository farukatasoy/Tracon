namespace Tracon;

/// <summary>
/// The save rules the MCP server and webhook subscription endpoints share for
/// their two header maps: plain <c>headers</c> and
/// <c>headerConfigurationKeys</c>.
/// </summary>
/// <remarks>
/// <para>
/// A plain header is stored in the clear, so a credential-looking name is
/// rejected and pointed at <c>headerConfigurationKeys</c>, which stores only
/// the NAME of the configuration key the value is read from. Every
/// such name goes through the tenant key space rule.
/// </para>
/// <para>
/// The two endpoints preserve a stored map when the request leaves it out
/// (<see langword="null"/>), so the uniqueness rule runs over the EFFECTIVE maps
/// — what will be stored — while the credential-name rule judges only the
/// headers the request sends: a stored row written before the rule existed
/// must stay saveable from a form that does not send its headers.
/// </para>
/// </remarks>
internal static class CredentialHeaderSaveRules
{
    /// <summary>The request field that carries the plain headers.</summary>
    public const string HeadersField = "headers";

    /// <summary>The request field that carries the configuration key names.</summary>
    public const string KeysField = "headerConfigurationKeys";

    /// <summary>A rejected save: the problem title and the detail.</summary>
    /// <param name="Title">The problem title.</param>
    /// <param name="Detail">The problem detail. Names headers; never carries a header value.</param>
    public sealed record Rejection(string Title, string Detail);

    /// <summary>Returns the map a save stores: the request's own, else the stored one, else empty.</summary>
    /// <param name="requested">The map the request sent, or <see langword="null"/> when it left the field out.</param>
    /// <param name="stored">The stored map, or <see langword="null"/> when there is no stored record.</param>
    /// <param name="comparer">The comparer an empty map is built with.</param>
    /// <returns>The effective map.</returns>
    public static IReadOnlyDictionary<string, string> Effective(
        IReadOnlyDictionary<string, string>? requested,
        IReadOnlyDictionary<string, string>? stored,
        StringComparer comparer)
        => requested ?? stored ?? new Dictionary<string, string>(comparer);

    /// <summary>Copies a validated map into one keyed with <paramref name="comparer"/>.</summary>
    /// <param name="map">The map, already checked by <see cref="Validate"/> for case-only duplicates.</param>
    /// <param name="comparer">The comparer of the stored map.</param>
    /// <returns>The copy.</returns>
    /// <remarks>
    /// Request binding builds a case-sensitive map; the stored
    /// <c>HeaderConfigurationKeys</c> is looked up case-insensitively, as HTTP
    /// header names are. Called after <see cref="Validate"/>, so the copy
    /// cannot fold two entries into one.
    /// </remarks>
    public static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> map, StringComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(map);

        var copy = new Dictionary<string, string>(map.Count, comparer);

        foreach (var (name, value) in map)
        {
            copy[name] = value;
        }

        return copy;
    }

    /// <summary>Applies the header rules to a save.</summary>
    /// <param name="requestHeaders">The plain headers the request sent, or <see langword="null"/>.</param>
    /// <param name="requestKeys">The configuration key names the request sent, or <see langword="null"/>.</param>
    /// <param name="effectiveHeaders">The plain headers the save stores.</param>
    /// <param name="effectiveKeys">The configuration key names the save stores.</param>
    /// <param name="allowedPrefix">The configuration key prefix of the surface.</param>
    /// <param name="tenantId">The caller's tenant.</param>
    /// <param name="defaultTenantId">The installation's default tenant.</param>
    /// <param name="isReserved">A test for header names the surface sets itself, or <see langword="null"/>.</param>
    /// <returns>The rejection, or <see langword="null"/> when the save may go ahead.</returns>
    public static Rejection? Validate(
        IReadOnlyDictionary<string, string>? requestHeaders,
        IReadOnlyDictionary<string, string>? requestKeys,
        IReadOnlyDictionary<string, string> effectiveHeaders,
        IReadOnlyDictionary<string, string> effectiveKeys,
        string allowedPrefix,
        string tenantId,
        string defaultTenantId,
        Func<string, bool>? isReserved)
    {
        ArgumentNullException.ThrowIfNull(effectiveHeaders);
        ArgumentNullException.ThrowIfNull(effectiveKeys);

        if ((InvalidName(requestHeaders, HeadersField) ?? InvalidName(requestKeys, KeysField)) is { } invalidName)
        {
            return invalidName;
        }

        if (requestHeaders is not null)
        {
            foreach (var name in requestHeaders.Keys)
            {
                if (CredentialHeaderNames.IsCredential(name))
                {
                    return new Rejection(
                        "Credential header stored in the clear",
                        $"Header '{name}' looks like a credential, and '{HeadersField}' stores its value in " +
                        $"the database. Declare it in '{KeysField}' instead, as the name of the configuration " +
                        $"key its value is read from: {{\"{name}\": \"{ExampleKey(allowedPrefix, tenantId, defaultTenantId)}\"}}.");
                }
            }
        }

        if (requestKeys is not null)
        {
            foreach (var (name, keyName) in requestKeys)
            {
                var field = $"{KeysField}[{name}]";

                if (isReserved?.Invoke(name) == true)
                {
                    return new Rejection(
                        "Reserved header",
                        $"'{field}' names a header Tracon sets itself; choose another header name.");
                }

                // 🚨 The key guard lets an empty name through (the single-key
                // fields are optional), but an entry in this map with no key
                // name would send the header with nothing to resolve.
                if (string.IsNullOrWhiteSpace(keyName))
                {
                    return new Rejection(
                        "Configuration key missing",
                        $"'{field}' must name the configuration key the header's value is read from.");
                }

                try
                {
                    ConfigurationKeyGuard.RequireTenantKey(keyName, allowedPrefix, tenantId, defaultTenantId, field);
                }
                catch (TraconException exception)
                {
                    return new Rejection("Configuration key not allowed", exception.Message);
                }
            }
        }

        return Duplicate(effectiveHeaders, effectiveKeys);
    }

    private static Rejection? InvalidName(IReadOnlyDictionary<string, string>? map, string field)
    {
        if (map is null)
        {
            return null;
        }

        foreach (var name in map.Keys)
        {
            if (!CredentialHeaderNames.IsValidName(name))
            {
                // The name is not echoed as-is: it may carry a CR/LF, and the
                // detail travels back in a response body and into logs.
                return new Rejection(
                    "Header name invalid",
                    $"'{field}' carries a header name that is empty or contains a character HTTP does not " +
                    "allow in a header name (a space, ':', a control character such as CR or LF). " +
                    "Only letters, digits and !#$%&'*+-.^_`|~ are allowed.");
            }
        }

        return null;
    }

    /// <summary>Rejects a header name that appears twice across the two maps, compared case-insensitively.</summary>
    /// <remarks>
    /// HTTP header names are case-insensitive, but request binding builds a
    /// case-sensitive map. <c>X-A</c> and <c>x-a</c> used to reach the webhook
    /// store (which threw, a <c>500</c>) and the MCP transport (which failed to
    /// connect). The detail names the plain header to remove because that is
    /// the migration step: a header moving to <c>headerConfigurationKeys</c>
    /// must leave <c>headers</c> in the same save.
    /// </remarks>
    private static Rejection? Duplicate(
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> keys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in headers.Keys)
        {
            if (!seen.Add(name))
            {
                return new Rejection(
                    "Duplicate header",
                    $"Header '{name}' appears more than once in '{HeadersField}'; header names are " +
                    "case-insensitive. Keep one spelling.");
            }
        }

        var keyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in keys.Keys)
        {
            if (!keyNames.Add(name))
            {
                return new Rejection(
                    "Duplicate header",
                    $"Header '{name}' appears more than once in '{KeysField}'; header names are " +
                    "case-insensitive. Keep one spelling.");
            }

            if (seen.Contains(name))
            {
                return new Rejection(
                    "Duplicate header",
                    $"Header '{name}' is set in both '{HeadersField}' and '{KeysField}' (header names are " +
                    $"case-insensitive). Remove '{name}' " +
                    $"from {HeadersField}: send '{HeadersField}' in the same save, without '{name}' and with " +
                    "every other plain header. A save that leaves 'headers' out keeps the stored ones.");
            }
        }

        return null;
    }

    private static string ExampleKey(string allowedPrefix, string tenantId, string defaultTenantId)
        => string.Equals(tenantId, defaultTenantId, StringComparison.OrdinalIgnoreCase)
            ? $"{allowedPrefix}<KeyName>"
            : $"{allowedPrefix}{tenantId}:<KeyName>";
}
