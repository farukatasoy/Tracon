using System.Reflection;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// No obsoletion carries a diagnostic identifier or a hand-written help address
/// (phase 190).
/// </summary>
public sealed class ObsoleteMessagesTests
{
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

        // The site address is declared once for C# and this package cannot see it.
        attribute.UrlFormat.ShouldBeNull();
    }
}
