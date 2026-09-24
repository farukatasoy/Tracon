using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// The one implementation of the content fingerprints a script grant pins:
/// the hash of a single stored script and the fingerprint of a skill's whole
/// script set.
/// </summary>
/// <remarks>
/// <para>
/// A field enters the hash when it changes what runs. <c>Extension</c> picks the
/// interpreter and names the file, <c>Content</c> is the file, and
/// <c>ParametersSchema</c> builds the argument gate (a <see langword="null"/>
/// schema and an empty one are the same to the runner, so they hash the same).
/// <c>Description</c> only reaches the model and <c>Name</c> is the grant's key;
/// the name enters the set fingerprint instead.
/// </para>
/// <para>
/// Every field is written <c>&lt;UTF-8 byte length&gt;:&lt;value&gt;</c>, so no
/// value can be read as a field boundary, and each hash starts with its own
/// domain constant, so a script hash never equals a set fingerprint. SHA-256,
/// upper-case hexadecimal (64 characters).
/// </para>
/// </remarks>
internal static class SkillScriptHashing
{
    /// <summary>The domain constant of a single script's hash.</summary>
    internal const string ScriptDomain = "tracon.skill-script.v1";

    /// <summary>The domain constant of a script set's fingerprint.</summary>
    internal const string SetDomain = "tracon.skill-script-set.v1";

    /// <summary>The length of every hash: SHA-256 in hexadecimal.</summary>
    internal const int HashLength = 64;

    /// <summary>Hashes one script.</summary>
    /// <param name="extension">The file extension; a leading dot is ignored, the case is kept.</param>
    /// <param name="content">The script's source text.</param>
    /// <param name="parametersSchema">The argument schema; <see langword="null"/> hashes like an empty one.</param>
    /// <returns>The upper-case hexadecimal SHA-256.</returns>
    internal static string ComputeScriptHash(string? extension, string? content, string? parametersSchema)
    {
        var builder = new StringBuilder(ScriptDomain);

        AppendLengthPrefixed(builder, (extension ?? string.Empty).TrimStart('.'));
        AppendLengthPrefixed(builder, content);
        AppendLengthPrefixed(builder, parametersSchema);

        return Hash(builder);
    }

    /// <summary>Fingerprints a skill's whole script set.</summary>
    /// <param name="scripts">The scripts; their order does not matter.</param>
    /// <returns>The upper-case hexadecimal SHA-256.</returns>
    /// <remarks>
    /// Adding, removing, renaming, or changing any script changes the fingerprint.
    /// An empty set has a fingerprint of its own, so the first script added to a
    /// script-less skill changes it too.
    /// </remarks>
    internal static string ComputeSetHash(IReadOnlyList<AgentSkillScriptDefinition>? scripts)
    {
        scripts ??= [];

        var builder = new StringBuilder(SetDomain);

        AppendLengthPrefixed(builder, scripts.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var script in scripts.OrderBy(static script => script.Name, StringComparer.Ordinal))
        {
            AppendLengthPrefixed(builder, script.Name);
            AppendLengthPrefixed(builder, script.ContentHash);
        }

        return Hash(builder);
    }

    /// <summary>Reports whether <paramref name="value"/> has the shape of a hash these methods produce.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> for 64 hexadecimal characters, in either case.</returns>
    internal static bool IsWellFormed(string? value)
        => value is { Length: HashLength } && value.All(char.IsAsciiHexDigit);

    private static void AppendLengthPrefixed(StringBuilder builder, string? value)
    {
        value ??= string.Empty;

        builder.Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }

    private static string Hash(StringBuilder builder)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
}
