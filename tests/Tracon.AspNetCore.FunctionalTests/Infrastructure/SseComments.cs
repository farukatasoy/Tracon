using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Reads a Server-Sent Events stream line by line, for a test that follows the keep-alive comments a stream writes
/// between its frames.
/// </summary>
/// <remarks>
/// <see cref="SseReader"/> skips comments. The stream that polls a run writes one comment for every poll that found nothing
/// new, which makes the comment the visible tick of the poll: a test that moves the host's clock reads the next tick.
/// </remarks>
internal static class SseComments
{
    /// <summary>The comment a run's event stream writes when a poll found no new event.</summary>
    public const string Waiting = ": waiting";

    /// <summary>Reads lines until one equals <paramref name="line"/>.</summary>
    /// <param name="reader">The reader of the response body.</param>
    /// <param name="line">The line to wait for, as it is written.</param>
    /// <returns>The lines read before it, in order.</returns>
    public static Task<List<string>> ReadUntilLineAsync(StreamReader reader, string line)
        => ReadUntilAsync(reader, candidate => string.Equals(candidate, line, StringComparison.Ordinal), $"the line '{line}'");

    /// <summary>Reads lines until one satisfies <paramref name="match"/>.</summary>
    /// <param name="reader">The reader of the response body.</param>
    /// <param name="match">Whether the line is the one waited for.</param>
    /// <param name="description">What the wait is for, used in the failure message.</param>
    /// <returns>The lines read before it, in order.</returns>
    /// <exception cref="TimeoutException">The stream gave no such line within the failure bound.</exception>
    public static async Task<List<string>> ReadUntilAsync(StreamReader reader, Func<string, bool> match, string description)
    {
        var before = new List<string>();
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        bound.CancelAfter(WaitUntil.DefaultTimeout); // cancel: bound

        try
        {
            while (await reader.ReadLineAsync(bound.Token) is { } line)
            {
                if (match(line))
                {
                    return before;
                }

                before.Add(line);
            }
        }
        catch (OperationCanceledException exception) when (bound.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The stream gave {description} within {WaitUntil.DefaultTimeout}. Lines read: {string.Join(" | ", before)}", exception);
        }

        throw new TimeoutException($"The stream ended before {description}. Lines read: {string.Join(" | ", before)}");
    }
}
