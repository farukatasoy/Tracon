using System.Reflection;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every obsoletion points at a section of the production guide that exists,
/// and none of them carries a diagnostic identifier (phase 190).
/// </summary>
/// <remarks>
/// The help address ships inside the package and reaches the consumer as the
/// link of a <c>CS0618</c> warning. The anchor is the section heading's slug,
/// so a renamed heading breaks it without any build noticing.
/// </remarks>
public sealed class ObsoleteMessagesTests
{
    [Fact]
    public void The_help_address_names_a_heading_of_the_production_guide()
    {
        var anchor = ObsoleteMessages.UrlFormat[(ObsoleteMessages.UrlFormat.IndexOf('#', StringComparison.Ordinal) + 1)..];
        var guide = Path.Combine(CapabilityEntryPoints.RepositoryRoot, "docs-site", "src", "content", "docs", "guides", "production.md");

        var slugs = File.ReadAllLines(guide)
            .Where(static line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(static line => Slug(line[3..]))
            .ToList();

        slugs.ShouldContain(slug => string.Equals(slug, anchor, StringComparison.Ordinal));
        ObsoleteMessages.UrlFormat.ShouldStartWith("https://tracon.dev/guides/production/#");
    }

    /// <summary>
    /// 🚨 A <c>DiagnosticId</c> escapes the <c>CS0618</c> suppression the
    /// System.Text.Json source generator writes into its output: Tracon.Core's
    /// own serializer context failed to build with one (measured, phase 190),
    /// and so would a consumer's.
    /// </summary>
    [Fact]
    public void No_obsolete_member_carries_a_diagnostic_id()
    {
        var offenders = new[] { typeof(McpServerDefinition).Assembly, typeof(TraconOptions).Assembly }
            .SelectMany(static assembly => assembly.GetTypes())
            .SelectMany(static type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Cast<MemberInfo>()
                .Append(type))
            .Where(static member => member.GetCustomAttribute<ObsoleteAttribute>() is { DiagnosticId: not null })
            .Select(static member => $"{member.DeclaringType?.Name}.{member.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void The_deprecated_field_carries_the_shared_message_and_address()
    {
        var obsolete = typeof(McpServerDefinition)
            .GetProperty(nameof(McpServerDefinition.HeaderConfigurationKeys))
            .ShouldNotBeNull();

        obsolete.GetCustomAttribute<ObsoleteAttribute>().ShouldBeNull("the replacement is not deprecated");

        var attribute = typeof(McpServerDefinition)
            .GetProperty("AuthorizationConfigurationKey")
            .ShouldNotBeNull()
            .GetCustomAttribute<ObsoleteAttribute>()
            .ShouldNotBeNull();

        attribute.Message.ShouldBe(ObsoleteMessages.AuthorizationConfigurationKey);
        attribute.UrlFormat.ShouldBe(ObsoleteMessages.UrlFormat);
    }

    /// <summary>The heading slug the site generator produces for plain text headings.</summary>
    private static string Slug(string heading)
        => Regex.Replace(heading.Trim().ToLowerInvariant(), "[^a-z0-9 -]", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1))
            .Replace(' ', '-');
}
