using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tracon;

/// <summary>
/// The configuration values a binder found and could not turn into the type of the
/// option they belong to, kept for one options instance.
/// </summary>
/// <remarks>
/// <para>
/// A binder that skipped such a value left the shipped default in place without a
/// word: <c>DefaultMaxOutputBytes = "10MB"</c> kept the output unbounded. The binder
/// now records the key, the text and what it expected here, and
/// <see cref="UnreadableConfigurationValidator{TOptions}"/> refuses to start the host.
/// The <c>Configure&lt;T&gt;</c> lambdas have no logger, so the validator is the only
/// channel that reaches the operator.
/// </para>
/// <para>
/// The report lives in a side table keyed by the options instance, not in a member
/// of each options type. Eight of the bound option types live in
/// <c>Tracon.Abstractions</c>, which cannot see an <c>internal</c> type of Core; a table
/// keeps every options type unchanged and public API does not grow. A
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> entry dies with its options
/// instance. One report serves a whole <c>TraconOptions</c> tree: the nested option
/// objects have no registration of their own, so the root owns their entries.
/// </para>
/// <para>
/// An entry counts only while <c>stillBound</c> says the options still hold what the
/// binder left: code that assigns the option after binding wins. Binding runs
/// on one thread while the options are built, and validators run after it.
/// </para>
/// </remarks>
internal sealed class UnreadableConfigurationReport
{
    private const int MaximumQuotedText = 64;

    private static readonly ConditionalWeakTable<object, UnreadableConfigurationReport> Reports = new();

    private readonly List<Entry> _entries = [];

    /// <summary>Gets the report of an options instance, creating it when the instance has none.</summary>
    /// <param name="options">The options instance the binder fills.</param>
    /// <returns>The instance's report.</returns>
    internal static UnreadableConfigurationReport For(object options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Reports.GetValue(options, static _ => new UnreadableConfigurationReport());
    }

    /// <summary>Gets the report of an options instance when one exists.</summary>
    /// <param name="options">The options instance.</param>
    /// <param name="report">The report, or <see langword="null"/> when the binder recorded nothing.</param>
    /// <returns>Whether a report exists.</returns>
    internal static bool TryGet(object options, [NotNullWhen(true)] out UnreadableConfigurationReport? report)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Reports.TryGetValue(options, out report);
    }

    /// <summary>Records a value that could not be read, replacing an earlier record of the same key.</summary>
    /// <param name="path">The full configuration key.</param>
    /// <param name="text">The text the operator wrote.</param>
    /// <param name="expected">What the key expects, completing "which is not …".</param>
    /// <param name="stillBound">Whether the option still holds what the binder left.</param>
    internal void Record(string path, string text, string expected, Func<bool> stillBound)
        => Add(
            path,
            $"{path} is {Quote(text)}, which is not {expected}. Write a valid value, or remove the key to keep the default.",
            stillBound);

    /// <summary>Records a failure with a message the caller wrote.</summary>
    /// <param name="path">The full configuration key.</param>
    /// <param name="message">The complete message.</param>
    /// <param name="stillBound">Whether the option still holds what the binder left.</param>
    internal void RecordMessage(string path, string message, Func<bool> stillBound)
        => Add(path, message, stillBound);

    /// <summary>
    /// Forgets an earlier record of a key and of the keys below it, after the key read
    /// cleanly or held nothing.
    /// </summary>
    /// <param name="path">The full configuration key.</param>
    internal void Clear(string path)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _entries.RemoveAll(entry => IsAtOrBelow(entry.Path, path));
    }

    /// <summary>Gets the messages of the records whose option still holds what the binder left.</summary>
    /// <returns>The messages in the order the binder read the keys; empty when all is readable.</returns>
    internal List<string> Failures()
    {
        var failures = new List<string>();

        foreach (var entry in _entries)
        {
            if (entry.StillBound())
            {
                failures.Add(entry.Message);
            }
        }

        return failures;
    }

    /// <summary>Quotes the text of a key for a message: control characters and a long tail are cut.</summary>
    /// <param name="text">The text the operator wrote.</param>
    /// <returns>The text between single quotes.</returns>
    internal static string Quote(string text)
    {
        var builder = new StringBuilder(Math.Min(text.Length, MaximumQuotedText) + 3);
        builder.Append('\'');

        foreach (var character in text.AsSpan(0, Math.Min(text.Length, MaximumQuotedText)))
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        if (text.Length > MaximumQuotedText)
        {
            builder.Append('…');
        }

        return builder.Append('\'').ToString();
    }

    private static bool IsAtOrBelow(string candidate, string path)
        => candidate.Length >= path.Length
            && candidate.StartsWith(path, StringComparison.Ordinal)
            && (candidate.Length == path.Length || candidate[path.Length] == ':');

    private void Add(string path, string message, Func<bool> stillBound)
    {
        Clear(path);
        _entries.Add(new Entry(path, message, stillBound));
    }

    private readonly record struct Entry(string Path, string Message, Func<bool> StillBound);
}
