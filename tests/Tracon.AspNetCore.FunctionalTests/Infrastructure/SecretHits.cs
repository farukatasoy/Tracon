using System.Text;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The search of a canary run: every secret against every output, in the shapes a secret takes on its way out.
/// </summary>
/// <remarks>
/// A secret is searched by its stem (the part before the random suffix), not only by the whole value, so a truncated or
/// partly masked copy is found as well. A stem is also searched URL-escaped and as the Base64 of its bytes, which are
/// the shapes an address and an Authorization scheme give it.
/// </remarks>
internal static class SecretHits
{
    /// <summary>Searches the outputs for the secrets.</summary>
    /// <param name="outputs">Output name and its text.</param>
    /// <param name="secrets">Secret name and the stem to look for.</param>
    /// <returns>One line per hit, with the text around it; empty when nothing was found.</returns>
    public static List<string> Find(IEnumerable<KeyValuePair<string, string>> outputs, IEnumerable<KeyValuePair<string, string>> secrets)
    {
        var found = new List<string>();
        var shapes = secrets
            .SelectMany(secret => Shapes(secret.Value).Select(shape => (Secret: secret.Key, shape.Name, shape.Text)))
            .ToList();

        foreach (var (output, text) in outputs)
        {
            foreach (var (secret, shape, needle) in shapes)
            {
                var at = text.IndexOf(needle, StringComparison.Ordinal);

                if (at >= 0)
                {
                    var from = Math.Max(0, at - 300);
                    var around = text.Substring(from, Math.Min(text.Length - from, 700)).ReplaceLineEndings(" ");

                    found.Add($"the {secret} ({shape}) is in the {output}: ...{around}...");
                }
            }
        }

        return found;
    }

    /// <summary>Tells whether the bytes hold a stem as UTF-8 or as UTF-16.</summary>
    /// <param name="bytes">The bytes, for example every file of a database.</param>
    /// <param name="stem">The stem.</param>
    /// <returns>The encodings it was found in; empty when it was not found.</returns>
    public static List<string> FindInBytes(byte[] bytes, string stem)
    {
        var found = new List<string>();

        foreach (var (name, needle) in (ValueTuple<string, byte[]>[])[("UTF-8", Encoding.UTF8.GetBytes(stem)), ("UTF-16", Encoding.Unicode.GetBytes(stem))])
        {
            if (bytes.AsSpan().IndexOf(needle) >= 0)
            {
                found.Add(name);
            }
        }

        return found;
    }

    private static IEnumerable<(string Name, string Text)> Shapes(string stem)
    {
        yield return ("as is", stem);
        yield return ("URL-escaped", Uri.EscapeDataString(stem));

        // Base64 of the part that is a whole number of three-byte groups, so it is a prefix of the Base64 of any longer value.
        var aligned = Encoding.UTF8.GetBytes(stem).AsSpan(0, stem.Length / 3 * 3);

        yield return ("Base64", Convert.ToBase64String(aligned));
    }
}
