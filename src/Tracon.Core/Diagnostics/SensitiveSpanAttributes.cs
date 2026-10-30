namespace Tracon;

/// <summary>
/// Classifies span attribute keys that carry what a user, a model or a tool said, so the span store can leave them
/// out while <see cref="TraconObservabilityOptions.RecordSensitiveData"/> is off.
/// </summary>
/// <remarks>
/// <para>
/// The source of a span is not Tracon's to decide: the Microsoft.Extensions.AI rings read their own switch (the
/// <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c> environment variable) and the Microsoft Agent Framework
/// workflow telemetry has its own option. Whatever they write reaches Tracon's listener. This class is the one place
/// that says which of those keys are content, and the listener applies it before anything is buffered.
/// </para>
/// <para>
/// 🚨 A word filter on <c>input</c> and <c>output</c> is wrong: <c>gen_ai.usage.input_tokens</c> and
/// <c>gen_ai.usage.output_tokens</c> would go, and the cost view is built from them. The rule is the three words the
/// semantic convention uses for text (<c>message</c>, <c>prompt</c>, <c>completion</c>) plus the full names of the keys
/// that carry text without one of them. The list is proven complete by a test that drives the real rings with
/// sensitive data on, not by this list.
/// </para>
/// </remarks>
internal static class SensitiveSpanAttributes
{
    private static readonly string[] ContentWords = ["message", "prompt", "completion"];

    private static readonly string[] ContentKeys =
    [
        "gen_ai.system_instructions",
        "gen_ai.tool.call.arguments",
        "gen_ai.tool.call.result",
        "executor.input",
        "executor.output",
    ];

    /// <summary>Returns whether a span attribute key carries content that can hold personal data.</summary>
    /// <param name="key">The attribute key.</param>
    /// <returns><see langword="true"/> when the store must not keep the value while sensitive data is off.</returns>
    public static bool IsContent(string key)
    {
        foreach (var word in ContentWords)
        {
            if (key.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var name in ContentKeys)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
