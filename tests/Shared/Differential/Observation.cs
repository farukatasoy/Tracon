using System.Text;

namespace Tracon.Tests.Differential;

/// <summary>
/// What one step of a sequence returned, in a form two implementations can be compared on: a returned value, a
/// returned list, or the kind of failure.
/// </summary>
/// <remarks>
/// Both the model and the driver build their observations from plain text of their own. A store record is rendered
/// into the same text a model record is, so the comparison never needs the model to produce a store type.
/// </remarks>
internal sealed class Observation
{
    private Observation(string text, IReadOnlyList<string>? items, IReadOnlyList<TieSegment>? layout)
    {
        Text = text;
        Items = items;
        Layout = layout;
    }

    /// <summary>Gets the text of a single value, or the heading of a list.</summary>
    public string Text { get; }

    /// <summary>Gets the rendered items of a list the driver read, in the order the store returned them.</summary>
    public IReadOnlyList<string>? Items { get; }

    /// <summary>Gets the shape of a list the model expects, when the order between equal sort keys is open.</summary>
    public IReadOnlyList<TieSegment>? Layout { get; }

    /// <summary>Creates the observation of a single value.</summary>
    /// <param name="text">The rendered value.</param>
    /// <returns>The observation.</returns>
    public static Observation Of(string text) => new(text, null, null);

    /// <summary>Creates the observation of a list whose order is fully defined.</summary>
    /// <param name="heading">The text that precedes the items.</param>
    /// <param name="items">The rendered items in order.</param>
    /// <returns>The observation.</returns>
    public static Observation List(string heading, IReadOnlyList<string> items) => new(heading, items, null);

    /// <summary>Creates the observation of a list in which items with an equal sort key may come in any order.</summary>
    /// <param name="heading">The text that precedes the items.</param>
    /// <param name="layout">The groups of equal sort key, in order, with how many of each the page holds.</param>
    /// <returns>The observation.</returns>
    public static Observation Tied(string heading, IReadOnlyList<TieSegment> layout) => new(heading, null, layout);

    /// <summary>Creates the observation of a call that failed.</summary>
    /// <param name="exception">The exception the call threw.</param>
    /// <returns>The observation.</returns>
    public static Observation Failure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // The kind of failure is part of the contract; the text is not. A provider's own exception type is not
        // comparable, so any type the model does not name stays visible under its full name.
        return exception switch
        {
            OperationCanceledException => Of("throws OperationCanceled"),
            ArgumentException => Of("throws ArgumentException"),
            TraconException => Of("throws TraconException"),
            _ => Of("throws " + exception.GetType().FullName + ": " + FirstLine(exception.Message)),
        };
    }

    /// <summary>Creates the observation the model gives for a rejected argument.</summary>
    /// <returns>The observation.</returns>
    public static Observation Rejected() => Of("throws ArgumentException");

    /// <summary>Compares what the model expects with what the store returned.</summary>
    /// <param name="expected">The model's observation.</param>
    /// <param name="actual">The store's observation.</param>
    /// <returns>An empty string when they agree; otherwise the reason they do not.</returns>
    public static string Compare(Observation expected, Observation actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (!string.Equals(expected.Text, actual.Text, StringComparison.Ordinal))
        {
            return "the value differs";
        }

        if (expected.Layout is { } layout)
        {
            return CompareTied(layout, actual.Items ?? []);
        }

        var expectedItems = expected.Items ?? [];
        var actualItems = actual.Items ?? [];

        if (expectedItems.Count != actualItems.Count)
        {
            return $"the list has {actualItems.Count} items, the model expects {expectedItems.Count}";
        }

        for (var index = 0; index < expectedItems.Count; index++)
        {
            if (!string.Equals(expectedItems[index], actualItems[index], StringComparison.Ordinal))
            {
                return $"item {index} differs";
            }
        }

        return string.Empty;
    }

    /// <summary>Renders the observation for a failure message.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder(Text);

        if (Items is not null)
        {
            foreach (var item in Items)
            {
                builder.Append("\n      - ").Append(item);
            }
        }

        if (Layout is not null)
        {
            foreach (var segment in Layout)
            {
                builder.Append("\n      - ").Append(segment.Count).Append(" of {").Append(string.Join(" ; ", segment.Members)).Append('}');
            }
        }

        return builder.ToString();
    }

    private static string CompareTied(IReadOnlyList<TieSegment> layout, IReadOnlyList<string> actual)
    {
        var expectedCount = layout.Sum(static segment => segment.Count);

        if (actual.Count != expectedCount)
        {
            return $"the list has {actual.Count} items, the model expects {expectedCount}";
        }

        var position = 0;

        foreach (var segment in layout)
        {
            var taken = actual.Skip(position).Take(segment.Count).ToList();
            position += segment.Count;

            if (taken.Distinct(StringComparer.Ordinal).Count() != taken.Count)
            {
                return "an item is listed twice within one group of equal sort key";
            }

            foreach (var item in taken)
            {
                if (!segment.Members.Contains(item))
                {
                    return $"item at position {position - segment.Count + taken.IndexOf(item)} is outside the group of equal sort key it should come from";
                }
            }
        }

        return string.Empty;
    }

    private static string FirstLine(string message)
    {
        var newline = message.IndexOf('\n', StringComparison.Ordinal);

        return newline < 0 ? message : message[..newline].TrimEnd('\r');
    }
}

/// <summary>A group of items that share a sort key, and how many of them the page under comparison holds.</summary>
/// <param name="Members">The rendered items that share the key.</param>
/// <param name="Count">How many of them fall inside the page.</param>
internal sealed record TieSegment(IReadOnlySet<string> Members, int Count);

/// <summary>Builds the expected page of a sorted list in which items with an equal sort key may come in any order.</summary>
internal static class TiedPage
{
    /// <summary>Cuts a page out of a list that is sorted, but whose equal keys are unordered.</summary>
    /// <param name="heading">The text that precedes the items.</param>
    /// <param name="sorted">The whole list sorted by the key; each item carries its rendering.</param>
    /// <param name="skip">The number of items to skip.</param>
    /// <param name="take">The number of items to take.</param>
    /// <returns>The expected observation.</returns>
    public static Observation Cut(string heading, IReadOnlyList<(long Key, string Rendered)> sorted, int skip, int take)
    {
        ArgumentNullException.ThrowIfNull(sorted);

        var start = Math.Clamp(skip, 0, sorted.Count);
        var count = Math.Clamp(take, 0, sorted.Count - start);
        var segments = new List<TieSegment>();
        var index = 0;

        while (index < sorted.Count)
        {
            var end = index;

            while (end < sorted.Count && sorted[end].Key == sorted[index].Key)
            {
                end++;
            }

            var from = Math.Max(index, start);
            var to = Math.Min(end, start + count);

            if (to > from)
            {
                segments.Add(new TieSegment(
                    new HashSet<string>(sorted.Skip(index).Take(end - index).Select(static pair => pair.Rendered), StringComparer.Ordinal),
                    to - from));
            }

            index = end;
        }

        return Observation.Tied(heading, segments);
    }
}
