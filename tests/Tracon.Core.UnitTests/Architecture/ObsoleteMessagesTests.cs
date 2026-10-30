using System.Reflection;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// No obsoletion carries a diagnostic identifier. removed
/// the last <c>[Obsolete]</c> member, so the guard is empty today; it stays so
/// the next deprecation meets the rule before a consumer does.
/// </summary>
public sealed class ObsoleteMessagesTests
{
    /// <summary>
    /// 🚨 A <c>DiagnosticId</c> escapes the <c>CS0618</c> suppression the
    /// System.Text.Json source generator writes into its output: Tracon.Core's
    /// own serializer context failed to build with one (measured),
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
}
