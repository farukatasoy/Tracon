using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// What a hosted tool puts on the wire, as text that is the same for the same tool and different for a tool that
/// differs, so a cache can tell whether it still holds the right answer.
/// </summary>
/// <param name="Text">The description. Never shown; it is hashed into a cache key.</param>
/// <param name="Opaque">
/// <see langword="true"/> when the tool holds a value this description cannot compare (a provider SDK object in
/// <c>AdditionalProperties</c>, an input of a type it does not know). Two such tools can differ and look the same.
/// </param>
/// <remarks>
/// <para>
/// Two caches ask: the compiled-agent cache (a tenant that replaces its vector store, or narrows the domains of a
/// web search, must reach the next run) and the response cache (an answer found in one vector store is not the answer
/// from another). Both read the thing that is embedded, not a proxy for it.
/// </para>
/// <para>
/// An opaque tool is not guessed at: <c>ToString()</c> of a provider SDK object is its type name, which would hash two
/// different filters to one key. The compiled-agent cache is bypassed for an agent that selects one, and the response
/// cache gives it a key that never repeats.
/// </para>
/// </remarks>
internal readonly record struct HostedToolDescription(string Text, bool Opaque)
{
    /// <summary>Gets whether <paramref name="tool"/> is one of the hosted tool types the adapters map.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns><see langword="true"/> for a web search, a code interpreter or a file search.</returns>
    public static bool IsHosted(AITool tool)
        => tool is HostedWebSearchTool or HostedCodeInterpreterTool or HostedFileSearchTool;

    /// <summary>Describes a hosted tool.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>The description.</returns>
    public static HostedToolDescription Of(AITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var text = new StringBuilder(tool.GetType().Name);
        var opaque = false;

        foreach (var (key, value) in tool.AdditionalProperties.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append(';').Append(key).Append('=');
            Append(text, value, ref opaque);
        }

        IList<AIContent>? inputs = tool switch
        {
            HostedFileSearchTool search => search.Inputs,
            HostedCodeInterpreterTool code => code.Inputs,
            _ => null,
        };

        foreach (var input in inputs ?? [])
        {
            text.Append(';');

            switch (input)
            {
                case HostedVectorStoreContent store:
                    text.Append("vector-store=").Append(store.VectorStoreId);
                    break;

                case HostedFileContent file:
                    text.Append("file=").Append(file.FileId);
                    break;

                case DataContent data:
                    text.Append("data=").Append(data.MediaType).Append(':').Append(Convert.ToHexString(SHA256.HashData(data.Data.Span)));
                    break;

                default:
                    text.Append(input.GetType().Name);
                    opaque = true;
                    break;
            }
        }

        if (tool is HostedFileSearchTool { MaximumResultCount: { } maximum })
        {
            text.Append(";max=").Append(maximum.ToString(CultureInfo.InvariantCulture));
        }

        return new HostedToolDescription(text.ToString(), opaque);
    }

    // A value is compared by what it says, only when it says it in a type whose text is its content: text, numbers,
    // booleans, enums, and lists and dictionaries of those. Anything else is opaque.
    private static void Append(StringBuilder text, object? value, ref bool opaque)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                break;

            case string or bool or char or Enum:
                text.Append(value.GetType().Name).Append(':').Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;

            case IConvertible when value.GetType().IsPrimitive || value is decimal:
                text.Append(value.GetType().Name).Append(':').Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;

            case IDictionary dictionary:
                text.Append('{');

                foreach (var key in dictionary.Keys.Cast<object>().OrderBy(static key => Convert.ToString(key, CultureInfo.InvariantCulture), StringComparer.Ordinal))
                {
                    text.Append(Convert.ToString(key, CultureInfo.InvariantCulture)).Append('=');
                    Append(text, dictionary[key], ref opaque);
                    text.Append(',');
                }

                text.Append('}');
                break;

            case IEnumerable items:
                text.Append('[');

                foreach (var item in items)
                {
                    Append(text, item, ref opaque);
                    text.Append(',');
                }

                text.Append(']');
                break;

            default:
                text.Append(value.GetType().FullName);
                opaque = true;
                break;
        }
    }
}
