namespace Tracon;

/// <summary>
/// The text of every <see cref="ObsoleteAttribute"/> the packages carry.
/// </summary>
/// <remarks>
/// <para>
/// No obsoletion carries a <c>DiagnosticId</c>, so every one of them
/// surfaces as <c>CS0618</c>. Measured: the System.Text.Json source generator
/// suppresses <c>CS0612</c> and <c>CS0618</c> in the code it emits and nothing
/// else. With a custom identifier (<c>TRC9001</c> was tried), Tracon.Core's own
/// <c>JsonSerializerContext</c> failed to build with 30 errors, and every
/// consumer whose context includes the type would fail the same way under
/// <c>TreatWarningsAsErrors</c>.
/// </para>
/// <para>
/// No obsoletion carries a <c>UrlFormat</c> either. The published site address
/// is declared once for C# (<c>DocumentationLinks</c> in the analyzer package),
/// which this package cannot reference, and a second hand-written copy is what
/// the site's content check forbids. The message itself names the replacement
/// and the release that removes the member.
/// </para>
/// </remarks>
internal static class ObsoleteMessages
{
    /// <summary>The message of the <c>AuthorizationConfigurationKey</c> obsoletion.</summary>
    public const string AuthorizationConfigurationKey =
        "Use HeaderConfigurationKeys[\"Authorization\"] instead. AuthorizationConfigurationKey is removed in 1.0.0.";
}
