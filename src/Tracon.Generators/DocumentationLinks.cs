namespace Tracon.Generators;

/// <summary>
/// The published documentation addresses that analyzer diagnostics point consumers at.
/// </summary>
/// <remarks>
/// <para>
/// These addresses ship inside the package: they reach the consumer as the help link of
/// an analyzer diagnostic, which the IDE turns into a clickable target. A stale value
/// here sends a consumer to a page that does not exist.
/// </para>
/// <para>
/// The site declares the same address in <c>docs-site/site.config.mjs</c>, and the two
/// declarations cannot be one file because one is C# and the other is JavaScript. They
/// are held together by <c>DiagnosticIntegrityTests</c>, which reads that file and fails
/// when the value below no longer matches it. This is the same arrangement
/// <c>ShippedDocumentationSelfContainmentTests</c> uses for the internal-history pattern.
/// </para>
/// </remarks>
internal static class DocumentationLinks
{
    /// <summary>The published site, with its trailing slash.</summary>
    private const string Site = "https://tracon.dev/";

    /// <summary>
    /// The capability map, up to and including the anchor separator. Every analyzer
    /// diagnostic is about a capability boundary, so all of them link into this page and
    /// append the section they concern.
    /// </summary>
    public const string CapabilityMap = Site + "capabilities/#";
}
