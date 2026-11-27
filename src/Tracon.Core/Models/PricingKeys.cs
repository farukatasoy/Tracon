namespace Tracon;

/// <summary>
/// The keys <c>Tracon:Pricing:{provider}:{model}</c> reads, and the keys people write there that it does not.
/// </summary>
/// <remarks>
/// <para>
/// The section is bound by hand, so this class is the single source of truth for the five short keys: the binder reads them
/// through these names and the startup check compares every other key of a model with them. A key added to the binder and
/// not to this list would be named as unread on every host that sets it.
/// </para>
/// <para>
/// Keys compare without case, as the configuration system itself does.
/// </para>
/// </remarks>
internal static class PricingKeys
{
    /// <summary>The cost per million input tokens.</summary>
    public const string Input = "Input";

    /// <summary>The cost per million output tokens.</summary>
    public const string Output = "Output";

    /// <summary>The cost per million input tokens served from the prompt cache.</summary>
    public const string CachedInput = "CachedInput";

    /// <summary>The cost per million input tokens written into the prompt cache with the short time to live.</summary>
    public const string CacheWrite = "CacheWrite";

    /// <summary>The cost per million input tokens written into the prompt cache with the long time to live.</summary>
    public const string CacheWriteLong = "CacheWriteLong";

    // A key people write for a rate the binder does read, mapped to the key it reads. Names a provider's own price list uses
    // ("cache read") and the code property names of ModelPriceOverride, which are what a host copies from the reference.
    private static readonly Dictionary<string, string> LikelyMatches = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CacheRead"] = CachedInput,
        ["CacheReads"] = CachedInput,
        ["CacheReadCostPerMillionTokens"] = CachedInput,
        ["CachedInputCostPerMillionTokens"] = CachedInput,
        ["InputCostPerMillionTokens"] = Input,
        ["OutputCostPerMillionTokens"] = Output,
        ["CacheWriteCostPerMillionTokens"] = CacheWrite,
        ["CacheWriteLongCostPerMillionTokens"] = CacheWriteLong,
    };

    /// <summary>Gets the keys the binder reads, in the order the reference lists them.</summary>
    public static IReadOnlyList<string> Known { get; } = [Input, Output, CachedInput, CacheWrite, CacheWriteLong];

    /// <summary>Tells whether the binder reads a key.</summary>
    /// <param name="key">The key under a model, without its path.</param>
    /// <returns><see langword="true"/> when the key is one of <see cref="Known"/>, whatever its case.</returns>
    public static bool IsKnown(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (var known in Known)
        {
            if (string.Equals(known, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the key the binder reads for a rate that a key it does not read most likely meant.</summary>
    /// <param name="key">The key under a model, without its path.</param>
    /// <returns>The key to write instead, or <see langword="null"/> when the key matches nothing known.</returns>
    public static string? LikelyMatch(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return LikelyMatches.GetValueOrDefault(key);
    }
}

/// <summary>A key under a model of <c>Tracon:Pricing</c> that the binder does not read.</summary>
/// <param name="Path">The full configuration path of the key, as written.</param>
/// <param name="Key">The last segment of the path.</param>
internal sealed record UnrecognizedPricingKey(string Path, string Key);
