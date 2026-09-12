using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>Converts a <see cref="FunctionCallContent.Arguments"/> dictionary into the read-only shape <see cref="ToolApprovalContext"/> needs.</summary>
internal static class FunctionCallArguments
{
    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Returns <paramref name="arguments"/> as a read-only dictionary, avoiding a copy when
    /// the concrete instance already implements <see cref="IReadOnlyDictionary{TKey,TValue}"/>
    /// — the shape <c>Dictionary&lt;string,object?&gt;</c> (what Microsoft Agent Framework's real
    /// function-call parsing produces) always does.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ToReadOnly(IDictionary<string, object?>? arguments)
        => arguments switch
        {
            null => Empty,
            IReadOnlyDictionary<string, object?> readOnly => readOnly,
            _ => new Dictionary<string, object?>(arguments, StringComparer.Ordinal),
        };
}
