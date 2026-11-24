using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Tracon;

/// <summary>
/// Keeps what the mappings of one surface (<c>MapTraconAGUI</c>, <c>MapTraconA2A</c>) share on the application: the patterns
/// that are taken, the endpoint name each one carries, and the objects the mappings of a surface must not each own.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core requires an endpoint name to be unique in the application and checks it at the first request that needs the
/// name, so a second mapping that reused a name broke every endpoint of the host, and the host started without a word. A
/// name is now made unique by the pattern it is mapped under, and a pattern that is mapped a second time is refused where it
/// is mapped, with a message that names it.
/// </para>
/// <para>
/// The state lives in <see cref="IApplicationBuilder.Properties"/>, as the access settings <c>MapTracon</c> stores do: the
/// application is already built when the endpoints are mapped, so a service cannot hold it. A route group has no such
/// dictionary; a surface that is mapped from one is refused earlier, where the shared access settings are read.
/// </para>
/// </remarks>
internal static class EndpointMappingRegistry
{
    private const string PatternsKeyPrefix = "Tracon.MappedPatterns.";
    private const string SharedKeyPrefix = "Tracon.SharedMappingObject.";

    /// <summary>Takes a pattern for a surface and gives back the suffix its endpoint names carry.</summary>
    /// <param name="endpoints">The routing builder the surface is mapped on.</param>
    /// <param name="surface">The name of the surface, for the message and for the key.</param>
    /// <param name="pattern">The pattern, as the caller gave it.</param>
    /// <param name="defaultPattern">The pattern whose endpoint names stay as they have always been.</param>
    /// <returns>
    /// An empty string for the default pattern, so its names do not change; otherwise an underscore and the pattern reduced to
    /// letters and digits, made unique among the patterns of the surface.
    /// </returns>
    /// <exception cref="InvalidOperationException">The pattern is already mapped for this surface.</exception>
    public static string Claim(IEndpointRouteBuilder endpoints, string surface, string pattern, string defaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPattern);

        var normalized = Normalize(pattern);

        if (endpoints is not IApplicationBuilder app)
        {
            return IsDefault(normalized, defaultPattern) ? string.Empty : Suffix(normalized);
        }

        var key = PatternsKeyPrefix + surface;

        if (!app.Properties.TryGetValue(key, out var stored) || stored is not Dictionary<string, (string Pattern, string Suffix)> claims)
        {
            claims = new Dictionary<string, (string Pattern, string Suffix)>(StringComparer.OrdinalIgnoreCase);
            app.Properties[key] = claims;
        }

        if (claims.TryGetValue(normalized, out var taken))
        {
            var already = string.Equals(taken.Pattern, normalized, StringComparison.Ordinal)
                ? $"{surface} is already mapped at '{taken.Pattern}'."
                : $"{surface} is already mapped at '{taken.Pattern}'; '{normalized}' is the same pattern.";

            throw new InvalidOperationException(
                already + " A pattern can be mapped once. Map the endpoint under another pattern, or remove the second call.");
        }

        var suffix = IsDefault(normalized, defaultPattern) ? string.Empty : Suffix(normalized);
        var unique = suffix;

        // Two patterns can read alike once their separators are gone ('/a/b' and '/a_b'); the second gets a number.
        for (var number = 2; claims.Values.Any(claim => string.Equals(claim.Suffix, unique, StringComparison.OrdinalIgnoreCase)); number++)
        {
            unique = string.Create(CultureInfo.InvariantCulture, $"{suffix}_{number}");
        }

        claims[normalized] = (normalized, unique);

        return unique;
    }

    /// <summary>
    /// Returns the object a surface keeps once for the whole application, creating it with the first mapping.
    /// </summary>
    /// <typeparam name="T">The object's type.</typeparam>
    /// <param name="endpoints">The routing builder the surface is mapped on.</param>
    /// <param name="name">What the object is, for the key.</param>
    /// <param name="create">Makes the object; may answer <see langword="null"/>, which is remembered as well.</param>
    /// <returns>The object, or <see langword="null"/> when <paramref name="create"/> answered so.</returns>
    public static T? Shared<T>(IEndpointRouteBuilder endpoints, string name, Func<T?> create)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(create);

        if (endpoints is not IApplicationBuilder app)
        {
            return create();
        }

        var key = SharedKeyPrefix + name;

        if (app.Properties.TryGetValue(key, out var stored))
        {
            return stored as T;
        }

        var created = create();
        app.Properties[key] = created;

        return created;
    }

    private static string Normalize(string pattern) => '/' + pattern.Trim().Trim('/');

    private static bool IsDefault(string normalized, string defaultPattern)
        => string.Equals(normalized, Normalize(defaultPattern), StringComparison.OrdinalIgnoreCase);

    private static string Suffix(string normalized)
    {
        var builder = new StringBuilder("_");
        var separated = true;

        foreach (var character in normalized)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                separated = false;
            }
            else if (!separated)
            {
                builder.Append('_');
                separated = true;
            }
        }

        // The root pattern has no letter to name it by.
        if (builder.Length == 1)
        {
            return "_root";
        }

        return builder[^1] == '_' ? builder.ToString(0, builder.Length - 1) : builder.ToString();
    }
}
