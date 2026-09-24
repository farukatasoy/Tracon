namespace Tracon;

/// <summary>
/// The text and help address of every
/// <see cref="ObsoleteAttribute"/> the packages carry.
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
/// The help address points at the upgrade section of the production guide.
/// Its anchor is the section heading; <c>ObsoleteMessagesTests</c> fails when
/// the heading changes and the address does not.
/// </para>
/// </remarks>
internal static class ObsoleteMessages
{
    /// <summary>The help address of every obsoletion.</summary>
    public const string UrlFormat = "https://tracon.dev/guides/production/#upgrading-credential-headers";

    /// <summary>The message of the <c>AuthorizationConfigurationKey</c> obsoletion.</summary>
    public const string AuthorizationConfigurationKey =
        "Use HeaderConfigurationKeys[\"Authorization\"] instead. AuthorizationConfigurationKey is removed in 1.0.0.";
}
