using System.Diagnostics.CodeAnalysis;

namespace Tracon;

/// <summary>
/// Verifies that a configuration key name a stored record points at sits
/// under an allowed prefix, inside the key space of the record's tenant.
/// </summary>
/// <remarks>
/// <para>
/// A record never holds a secret <em>value</em>; it holds the <strong>name</strong>
/// of the configuration key the value is read from. That
/// alone is not enough. Without a prefix restriction, an administrator could
/// bind a record to an unrelated key such as <c>ConnectionStrings:Default</c>
/// — unable to read its value, but able to make Tracon send it somewhere.
/// </para>
/// <para>
/// The prefix alone is not enough either. It is one per installation, so
/// tenant A could name tenant B's key under it and point the record at an
/// address A controls. The key space is therefore split by tenant: a
/// non-default tenant names keys under <c>{prefix}{tenant}:</c>, and a flat
/// name directly under the prefix belongs to the default tenant. A
/// single-tenant installation keeps its flat names unchanged.
/// </para>
/// <para>
/// The rule is enforced twice on every surface: once where the record is
/// saved, and again where the value is resolved. A record written before the
/// rule existed must not silently read a key outside its tenant.
/// </para>
/// </remarks>
internal static class ConfigurationKeyGuard
{
    /// <summary>
    /// Throws unless a configuration key name sits under the allowed prefix
    /// and inside the key space of <paramref name="tenantId"/>.
    /// </summary>
    /// <param name="configurationKeyName">The configuration key name the record carries.</param>
    /// <param name="allowedPrefix">The only prefix a key name may start with.</param>
    /// <param name="tenantId">The tenant that owns the record.</param>
    /// <param name="defaultTenantId">
    /// The installation's default tenant (<see cref="TraconOptions.DefaultTenantId"/>).
    /// Only this tenant may use a flat name directly under the prefix.
    /// </param>
    /// <param name="fieldName">The name of the field being checked, as it appears to the caller.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="allowedPrefix"/>, <paramref name="tenantId"/> or
    /// <paramref name="defaultTenantId"/> is empty.
    /// </exception>
    /// <exception cref="TraconException">
    /// <paramref name="configurationKeyName"/> is empty, does not start with
    /// <paramref name="allowedPrefix"/>, or names a key outside the tenant's
    /// key space. The message names the field and the namespace to use, so
    /// the operator can see which field to correct and how.
    /// </exception>
    public static void RequireTenantKey(
        string? configurationKeyName,
        string allowedPrefix,
        string tenantId,
        string defaultTenantId,
        string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultTenantId);

        RequirePrefix(configurationKeyName, allowedPrefix, fieldName);

        var rest = configurationKeyName.AsSpan(allowedPrefix.Length);

        // Configuration keys are case-insensitive, and so is the tenant
        // identifier across the product: 'Tracon:ProviderKeys:Acme:OpenAI'
        // and 'tracon:providerkeys:acme:openai' read the same value.
        var underOwnSegment = rest.Length > tenantId.Length + 1
            && rest.StartsWith(tenantId, StringComparison.OrdinalIgnoreCase)
            && rest[tenantId.Length] == ':';

        if (underOwnSegment)
        {
            return;
        }

        var isDefaultTenant = string.Equals(tenantId, defaultTenantId, StringComparison.OrdinalIgnoreCase);

        // A flat name has no separator after the prefix. Every other tenant's
        // key carries one ('{prefix}{tenant}:...'), so a flat name can never
        // reach into another tenant's key space.
        if (isDefaultTenant && rest.Length > 0 && !rest.Contains(':'))
        {
            return;
        }

        var allowed = isDefaultTenant
            ? $"under '{allowedPrefix}{tenantId}:' or a flat name directly under '{allowedPrefix}'"
            : $"under '{allowedPrefix}{tenantId}:'";

        throw new TraconException(
            $"'{configurationKeyName}' is outside the key space of tenant '{tenantId}'. " +
            $"'{fieldName}' may only reference a configuration key {allowed}.");
    }

    private static void RequirePrefix(
        [NotNull] string? configurationKeyName,
        string allowedPrefix,
        string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedPrefix);

        if (string.IsNullOrWhiteSpace(configurationKeyName))
        {
            throw new TraconException($"'{fieldName}' cannot be empty.");
        }

        if (!configurationKeyName.StartsWith(allowedPrefix, StringComparison.Ordinal))
        {
            throw new TraconException(
                $"'{configurationKeyName}' is outside the allowed prefix. '{fieldName}' may only " +
                $"reference a configuration key under '{allowedPrefix}'.");
        }
    }
}
