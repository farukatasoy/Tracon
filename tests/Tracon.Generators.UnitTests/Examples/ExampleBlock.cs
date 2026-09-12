namespace Tracon.Generators.UnitTests.Examples;

/// <summary>One <c>&lt;example&gt;</c> block read out of a shipped XML doc comment.</summary>
/// <param name="RelativeSourcePath">Repository-relative path of the file the block came from.</param>
/// <param name="LineNumber">1-based line of the opening <c>&lt;example&gt;</c> tag.</param>
/// <param name="Code">The decoded text between <c>&lt;code&gt;</c> and <c>&lt;/code&gt;</c>.</param>
/// <param name="Language">
/// The <c>&lt;code&gt;</c> tag's <c>language</c> attribute, or <c>"csharp"</c> when the
/// tag carries none. A <c>"json"</c> block is a configuration fragment, not C#,
/// and is validated as JSON instead of compiled.
/// </param>
internal sealed record ExampleBlock(string RelativeSourcePath, int LineNumber, string Code, string Language)
{
    /// <summary>Human-readable identity used as the compilation theory's key and failure label.</summary>
    public string Origin => $"{RelativeSourcePath}:{LineNumber}";

    /// <summary>The package the block ships in - the second segment of <c>src/&lt;package&gt;/...</c>.</summary>
    public string Package => RelativeSourcePath.Split('/')[1];
}
