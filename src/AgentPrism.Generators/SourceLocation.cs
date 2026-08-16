using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AgentPrism.Generators;

/// <summary>A cacheable (value-equality) projection of <see cref="Location"/>.</summary>
/// <remarks>
/// <see cref="Location"/> can be tied to a <see cref="SyntaxTree"/> and must not be
/// held in <see cref="IIncrementalGenerator"/> models; this type copies the three
/// pieces of information needed (file path, text span, line span) and reconstructs
/// a <see cref="Location"/> via <see cref="ToLocation"/> when required.
/// </remarks>
internal readonly record struct SourceLocation(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static SourceLocation From(Location location)
    {
        var lineSpan = location.GetLineSpan();
        return new SourceLocation(lineSpan.Path ?? string.Empty, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}
