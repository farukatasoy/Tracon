namespace Tracon;

/// <summary>Whether a provider failure should move to the next fallback link.</summary>
/// <remarks>
/// Three-valued rather than <see langword="bool"/> on purpose: the built-in
/// rules are a closed, positive set (an unrecognized failure does not retry
/// by default — see <see cref="IProviderRetryClassifier"/>). A
/// <see langword="bool"/> contract would force a consumer's classifier to
/// take a side on every exception it does not recognize, silently breaking
/// that guarantee the moment a custom classifier is registered.
/// </remarks>
public enum ProviderRetryDecision
{
    /// <summary>No opinion; the built-in rules decide.</summary>
    Unknown = 0,

    /// <summary>Try the next fallback link.</summary>
    Retry = 1,

    /// <summary>Do not switch providers; surface the error.</summary>
    DoNotRetry = 2,
}
