using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Reads the <c>required</c> members that a consumer can see out of C# source.
/// </summary>
/// <remarks>
/// <para>
/// Why a source scan and not reflection: <c>RequiredMemberAttribute</c> sits on
/// the compiled member, but this test project references only
/// <c>Tracon.Core</c>; the members live in seven packages. Reading the source
/// also needs no build order (the other architecture gates do the same).
/// </para>
/// <para>
/// A member counts when it is <c>public</c> or <c>protected</c> and every type
/// around it is visible outside the assembly. Visibility is decided over all
/// parts of a <c>partial</c> type: <c>public</c> on one part is enough, because
/// the compiler merges the modifiers.
/// </para>
/// </remarks>
internal static class RequiredMemberScanner
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>
    /// The sorted, distinct <c>Namespace.Type.Member</c> lines of every
    /// visible <c>required</c> member in <paramref name="sources"/>. Generic
    /// arity is not part of a type name (<c>Page&lt;T&gt;</c> is <c>Page</c>), the
    /// same form the release gate reads from the PublicAPI files.
    /// </summary>
    public static IReadOnlyList<string> Scan(IEnumerable<string> sources)
    {
        var roots = sources.Select(static source => CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot()).ToList();

        var declaredVisible = new Dictionary<string, bool>(StringComparer.Ordinal);
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var type in roots.SelectMany(static root => root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()))
        {
            var name = FullNameOf(type);
            parents[name] = type.Parent is BaseTypeDeclarationSyntax outer ? FullNameOf(outer) : null;
            declaredVisible[name] = declaredVisible.GetValueOrDefault(name) || DeclaresVisibility(type);
        }

        var visible = new Dictionary<string, bool>(StringComparer.Ordinal);

        bool IsVisible(string name)
        {
            if (visible.TryGetValue(name, out var known))
            {
                return known;
            }

            var result = declaredVisible.GetValueOrDefault(name)
                && (parents.GetValueOrDefault(name) is not { } parent || IsVisible(parent));
            visible[name] = result;
            return result;
        }

        var lines = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var member in roots.SelectMany(static root => root.DescendantNodes().OfType<MemberDeclarationSyntax>()))
        {
            if (member.Parent is not BaseTypeDeclarationSyntax owner || !IsRequiredAndReachable(member))
            {
                continue;
            }

            var typeName = FullNameOf(owner);

            if (!IsVisible(typeName))
            {
                continue;
            }

            foreach (var name in MemberNames(member))
            {
                lines.Add($"{typeName}.{name}");
            }
        }

        return [.. lines];
    }

    private static bool IsRequiredAndReachable(MemberDeclarationSyntax member)
    {
        var modifiers = member switch
        {
            PropertyDeclarationSyntax property => property.Modifiers,
            FieldDeclarationSyntax field => field.Modifiers,
            _ => default,
        };

        return modifiers.Any(SyntaxKind.RequiredKeyword)
            && (modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword))
            && !modifiers.Any(SyntaxKind.PrivateKeyword);
    }

    private static IEnumerable<string> MemberNames(MemberDeclarationSyntax member) => member switch
    {
        PropertyDeclarationSyntax property => [property.Identifier.ValueText],
        FieldDeclarationSyntax field => field.Declaration.Variables.Select(static variable => variable.Identifier.ValueText),
        _ => [],
    };

    /// <summary>
    /// Whether this declaration alone lets the type out of its assembly. A
    /// nested type inside an interface is public without a modifier.
    /// </summary>
    private static bool DeclaresVisibility(BaseTypeDeclarationSyntax type)
    {
        if (type.Modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return true;
        }

        return type.Parent switch
        {
            InterfaceDeclarationSyntax => !HasAccessibilityModifier(type),
            BaseTypeDeclarationSyntax => type.Modifiers.Any(SyntaxKind.ProtectedKeyword)
                && !type.Modifiers.Any(SyntaxKind.PrivateKeyword),
            _ => false,
        };
    }

    private static bool HasAccessibilityModifier(BaseTypeDeclarationSyntax type) =>
        type.Modifiers.Any(static modifier => modifier.Kind()
            is SyntaxKind.PublicKeyword or SyntaxKind.InternalKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.PrivateKeyword);

    private static string FullNameOf(BaseTypeDeclarationSyntax type)
    {
        var parts = new List<string>();

        for (SyntaxNode? node = type; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax declaration:
                    parts.Add(declaration.Identifier.ValueText);
                    break;
                case BaseNamespaceDeclarationSyntax @namespace:
                    parts.Add(@namespace.Name.ToString());
                    break;
            }
        }

        parts.Reverse();
        return string.Join('.', parts);
    }
}
