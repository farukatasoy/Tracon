namespace Tracon;

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
/// is read from at run time, the same indirection Tracon uses elsewhere
/// for provider credentials. The example below shows both: the
/// <c>ContentProtection</c> section names <c>ContentProtectionKeys:2026-08</c>,
/// and that key holds the actual value — set through
/// <c>dotnet user-secrets</c>, never committed to <c>appsettings.json</c>.
/// </para>
/// <example>
/// <code language="json">
/// "Tracon": { "ContentProtection": {
///   "Enabled": true,
///   "ActiveKeyId": "2026-08",
///   "Keys": { "2026-08": "ContentProtectionKeys:2026-08" }
/// }},
/// "ContentProtectionKeys": { "2026-08": "&lt;32-byte base64 key&gt;" }
/// </code>
/// </example>
/// </remarks>
public sealed class TraconContentProtectionOptions
{
    /// <summary>The configuration section this type binds to.</summary>
    public const string SectionName = "Tracon:ContentProtection";

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
    /// <para>
    /// An entry stays here for as long as any stored value still carries its
    /// key id — removing it makes those values unreadable
    /// (<see cref="TraconException"/>, naming the missing key id).
    /// </para>
    /// <para>
    /// An entry with an empty name stops the application at startup with an
    /// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> that names
    /// the key id, also while <see cref="Enabled"/> is <see langword="false"/>: an old
    /// row is still decrypted through this map after protection is turned off.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> Keys { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the set of columns protection applies to. Defaults to all of them.</summary>
    /// <remarks>
    /// <para>
    /// In configuration, a list of <see cref="ProtectedColumn"/> names
    /// (<c>Columns:0 = "RunInput"</c>, matched case-insensitively). A list that is
    /// present replaces the default.
    /// </para>
    /// <para>
    /// An entry that is not a <see cref="ProtectedColumn"/> name — a misspelling, an
    /// empty entry, an undefined number — stops the application at startup with an
    /// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> that
    /// names the entry as written, whether <see cref="Enabled"/> is on or off. Such
    /// an entry used to be skipped, and the column the operator meant stayed in plain
    /// text. An undefined value added in code stops the application too; code that
    /// rewrites the set after binding replaces the configured entries.
    /// </para>
    /// </remarks>
    public ISet<ProtectedColumn> Columns { get; } = new HashSet<ProtectedColumn>(Enum.GetValues<ProtectedColumn>());

    /// <summary>
    /// The <see cref="Columns"/> entries the binder could not read, keyed by the
    /// undefined value it bound in each one's place.
    /// </summary>
    /// <remarks>
    /// For the startup message only: the failure itself comes from an undefined
    /// member of <see cref="Columns"/>, so code that rewrites the set clears it.
    /// </remarks>
    internal Dictionary<ProtectedColumn, string> UnreadableColumns { get; } = [];
}
