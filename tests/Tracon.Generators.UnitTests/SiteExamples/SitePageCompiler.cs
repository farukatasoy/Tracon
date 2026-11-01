using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Tracon.Generators.UnitTests.Examples;

namespace Tracon.Generators.UnitTests.SiteExamples;

/// <summary>
/// Compiles the C# blocks of one documentation page the way a reader meets them: top to bottom, each block able to use
/// what the blocks above it defined.
/// </summary>
/// <remarks>
/// <para>
/// A page builds one example in steps. The first block declares <c>OrderTools</c>, the second registers it, the third
/// tests it. Each block is therefore a C# script submission chained to the one before, which is what makes a page
/// readable as code: statements, type declarations and the loose members of a class can sit in one block, a variable
/// declared in one block is in scope in the next, and a final expression needs no semicolon. Compiling each block alone
/// would report the second and third as broken for naming a type the page defined a screen earlier.
/// </para>
/// <para>
/// The names a page uses without defining them (<c>builder</c>, <c>app</c>, <c>tracon</c>) are the script's globals
/// (<see cref="SiteGlobals"/>). A block that starts with <c>.</c> continues a call chain; it is read as a continuation of
/// <c>tracon</c>. Errors belong to the block they are found in, so one broken block does not make its page's other
/// blocks look broken.
/// </para>
/// </remarks>
internal static class SitePageCompiler
{
    private static readonly ImmutableArray<MetadataReference> References =
        [.. AnalyzerTestHelper.References, MetadataReference.CreateFromFile(typeof(SiteGlobals).Assembly.Location)];

    private static readonly ImmutableArray<string> Usings =
    [
        .. ExamplePrelude.Usings
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("using ", StringComparison.Ordinal))
            .Select(static line => line["using ".Length..].TrimEnd(';')),
        "Xunit",
    ];

    /// <summary>Compiles the blocks of one page and returns the errors of each block that has any.</summary>
    /// <param name="blocks">The page's blocks, in page order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The error texts by block; a block that compiles is absent.</returns>
    public static IReadOnlyDictionary<SiteBlock, IReadOnlyList<string>> Compile(
        IReadOnlyList<SiteBlock> blocks,
        CancellationToken cancellationToken)
    {
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            usings: Usings);

        var parse = new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script);
        var errors = new Dictionary<SiteBlock, IReadOnlyList<string>>();
        CSharpCompilation? previous = null;
        var number = 0;

        foreach (var block in blocks)
        {
            number++;

            var compilation = CSharpCompilation.CreateScriptCompilation(
                "Block" + number.ToString(CultureInfo.InvariantCulture),
                CSharpSyntaxTree.ParseText(Prepare(block.Code), parse, cancellationToken: cancellationToken),
                References,
                options,
                previous,
                returnType: typeof(object),
                globalsType: typeof(SiteGlobals));

            var found = compilation.GetDiagnostics(cancellationToken)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(static diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}")
                .ToList();

            if (found.Count > 0)
            {
                errors[block] = found;

                // The compiler refuses a submission that follows one with errors, so the blocks below a broken one are
                // compiled without it. A name only the broken block defined is reported again below it; fixing the
                // block clears those, and the exception list says so (the entries become stale).
                continue;
            }

            previous = compilation;
        }

        return errors;
    }

    /// <summary>A block that starts with a dot continues a call chain on the Tracon builder.</summary>
    private static string Prepare(string code)
        => code.TrimStart().StartsWith('.') ? "tracon" + code.TrimStart() : code;
}
