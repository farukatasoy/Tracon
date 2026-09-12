namespace Tracon;

/// <summary>
/// Splits a delegation's output into appends the provider will accept, and caps how
/// many are sent.
/// </summary>
/// <remarks>
/// <para>
/// Two ceilings apply, for two different reasons. The character ceiling is the
/// provider's own per-append limit: text over it is <strong>split</strong>, never
/// silently dropped, because dropping half a sentence produces an answer that is
/// wrong rather than short. The append-count ceiling bounds how much one delegation
/// may push into a session that the provider, not Tracon, is driving.
/// </para>
/// <para>
/// The character ceiling is counted in characters even where the provider
/// states its limit in tokens. Tracon ships no tokenizer and must not take one
/// on; the provider implementation converts the limit once, conservatively, and
/// documents the ratio in a single place.
/// </para>
/// </remarks>
internal sealed class LiveVoiceAppendBudget
{
    private readonly int _maxCharacters;
    private readonly int _maxAppends;

    /// <summary>Creates a budget.</summary>
    /// <param name="maxCharacters">The provider's per-append character ceiling.</param>
    /// <param name="maxAppends">How many appends one delegation may send.</param>
    public LiveVoiceAppendBudget(int maxCharacters, int maxAppends)
    {
        _maxCharacters = Math.Max(16, maxCharacters);
        _maxAppends = Math.Max(1, maxAppends);
    }

    /// <summary>Gets how many appends have been taken from the budget.</summary>
    public int Sent { get; private set; }

    /// <summary>Gets whether the append ceiling has been reached.</summary>
    public bool IsExhausted => Sent >= _maxAppends;

    /// <summary>Splits text into pieces the provider will accept and charges the budget.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The pieces to send; empty when the text is blank or the budget is spent.</returns>
    /// <remarks>
    /// Splitting prefers the last whitespace inside the ceiling, so a piece ends on a
    /// word boundary wherever one exists. A single word longer than the ceiling is cut
    /// at the ceiling — the alternative is sending something the provider rejects.
    /// </remarks>
    public IReadOnlyList<string> Take(string? text)
    {
        if (text is not { Length: > 0 })
        {
            return [];
        }

        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return [];
        }

        var pieces = new List<string>();
        var index = 0;

        while (index < trimmed.Length && Sent < _maxAppends)
        {
            var remaining = trimmed.Length - index;

            if (remaining <= _maxCharacters)
            {
                pieces.Add(trimmed[index..]);
                Sent++;

                break;
            }

            var window = trimmed.AsSpan(index, _maxCharacters);
            var breakAt = window.LastIndexOf(' ');
            var length = breakAt > 0 ? breakAt : _maxCharacters;

            var piece = trimmed.Substring(index, length).TrimEnd();

            if (piece.Length > 0)
            {
                pieces.Add(piece);
                Sent++;
            }

            index += length;

            while (index < trimmed.Length && trimmed[index] == ' ')
            {
                index++;
            }
        }

        return pieces;
    }
}
