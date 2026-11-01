using System.Security.Cryptography;
using System.Text;

namespace Tracon.Generators.UnitTests.SiteExamples;

/// <summary>One fenced C# block of a hand-written documentation page.</summary>
/// <param name="Page">The page, relative to the documentation root (<c>guides/embedding.md</c>).</param>
/// <param name="Line">1-based line of the opening fence.</param>
/// <param name="Code">The text between the fences.</param>
internal sealed record SiteBlock(string Page, int Line, string Code)
{
    /// <summary>
    /// What names the block in the exception list: the page and a digest of the text. A line number would shift with
    /// every paragraph added above the block; a digest changes only when the block does, and a block that was edited
    /// has to be looked at again.
    /// </summary>
    public string Id => $"{Page}#{Digest}";

    private string Digest
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Code.ReplaceLineEndings("\n").Trim())))[..8].ToLowerInvariant();
}
