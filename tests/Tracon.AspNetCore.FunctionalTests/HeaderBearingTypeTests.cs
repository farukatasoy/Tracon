using System.Reflection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every stored type that carries free-form request headers also carries a
/// <c>HeaderConfigurationKeys</c> map, so a credential header always has a
/// place that stores only the key NAME (phase 190, K-059).
/// </summary>
/// <remarks>
/// Scans instead of listing, like Core's <c>SecretBearingTypeTests</c>: a new
/// type with a <c>Headers</c> dictionary cannot be added without either the
/// map or an entry in <see cref="Exempt"/> that says why it needs none.
/// </remarks>
public sealed class HeaderBearingTypeTests
{
    /// <summary>
    /// Types whose headers are not operator-supplied request headers.
    /// </summary>
    private static readonly string[] Exempt =
    [
        // A stored server RESPONSE, replayed for an idempotent retry: Tracon
        // wrote these headers itself, and no request is sent with them.
        nameof(IdempotencyResponse),
    ];

    [Fact]
    public void A_type_with_request_headers_also_carries_configuration_key_names()
    {
        var offenders = HeaderBearingTypes()
            .Where(static type => !Exempt.Contains(type.Name, StringComparer.Ordinal))
            .Where(static type => type.GetProperty("HeaderConfigurationKeys", BindingFlags.Public | BindingFlags.Instance) is null)
            .Select(static type => type.FullName)
            .ToList();

        offenders.ShouldBeEmpty(
            "These types store request headers in the clear but offer no key-name map for a credential header: " +
            string.Join(", ", offenders));
    }

    /// <summary>Proves the scan is not vacuous.</summary>
    [Fact]
    public void The_scan_finds_the_known_header_bearing_types()
    {
        var found = HeaderBearingTypes().Select(static type => type.Name).ToList();

        found.ShouldContain(static name => string.Equals(name, nameof(McpServerDefinition), StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, nameof(WebhookSubscription), StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, nameof(McpServerRequest), StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, nameof(WebhookSaveRequest), StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, nameof(IdempotencyResponse), StringComparison.Ordinal));
    }

    private static IEnumerable<Type> HeaderBearingTypes()
        => new[] { typeof(McpServerDefinition).Assembly, typeof(McpServerRequest).Assembly }
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type => type.GetProperty("Headers", BindingFlags.Public | BindingFlags.Instance) is { } property
                && typeof(IEnumerable<KeyValuePair<string, string>>).IsAssignableFrom(property.PropertyType))
            .Distinct();
}
