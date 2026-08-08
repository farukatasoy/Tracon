using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AgentPrism.Generators;

/// <summary>
/// <see cref="Location"/>'un onbelleklenebilir (deger esitligi tasiyan) izdusumu.
/// </summary>
/// <remarks>
/// <see cref="Location"/> bir <see cref="SyntaxTree"/>'ye bagli olabilir ve
/// <see cref="IIncrementalGenerator"/> modellerinde tutulmamalidir; bu tip
/// gereken uc bilgiyi (dosya yolu, metin araligi, satir araligi) kopyalar ve
/// gerektiginde <see cref="ToLocation"/> ile yeniden bir <see cref="Location"/>
/// uretir.
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
