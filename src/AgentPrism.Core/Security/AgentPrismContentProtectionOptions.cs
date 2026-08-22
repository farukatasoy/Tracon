namespace AgentPrism;

/// <summary>Settings for at-rest content protection.</summary>
/// <remarks>
/// <para>
/// Always registered, but <see cref="Enabled"/> defaults to
/// <see langword="false"/>: without <c>AddContentProtection(...)</c>,
/// nothing is encrypted and every store behaves exactly as it does today.
/// </para>
/// <para>
/// <see cref="Keys"/> never carries a key's raw material — it maps a key id
/// to the <strong>name</strong> of another configuration key the raw value
/// is read from at run time, the same indirection AgentPrism uses elsewhere
/// for provider credentials. The example below shows the two keys involved:
/// the second line is a name, not a value.
/// </para>
/// <example>
/// <code>
/// AgentPrism:ContentProtection:Enabled = true
/// AgentPrism:ContentProtection:ActiveKeyId = "2026-08"
/// AgentPrism:ContentProtection:Keys:2026-08 = "ContentProtectionKeys:2026-08"
/// ContentProtectionKeys:2026-08 = "&lt;32-byte base64 key&gt;"   // dotnet user-secrets
/// </code>
/// </example>
/// </remarks>
public sealed class AgentPrismContentProtectionOptions
{
    /// <summary>The configuration section this type binds to.</summary>
    public const string SectionName = "AgentPrism:ContentProtection";

    /// <summary>Gets or sets a value indicating whether the content protection ring is added to the pipeline.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the key id new writes are protected with.</summary>
    /// <remarks>Must be a key of <see cref="Keys"/> when <see cref="Enabled"/> is <see langword="true"/>.</remarks>
    public string? ActiveKeyId { get; set; }

    /// <summary>
    /// Gets the map from a key id to the name of the configuration key its raw
    /// material is read from.
    /// </summary>
    /// <remarks>
    /// An entry stays here for as long as any stored value still carries its
    /// key id — removing it makes those values unreadable
    /// (<see cref="AgentPrismException"/>, naming the missing key id).
    /// </remarks>
    public IDictionary<string, string> Keys { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the set of columns protection applies to. Defaults to all of them.</summary>
    public ISet<ProtectedColumn> Columns { get; } = new HashSet<ProtectedColumn>(Enum.GetValues<ProtectedColumn>());
}
