namespace Tracon.Testing.Contracts.Internal;

/// <summary>
/// Asks a store again until it gives the answer a test is waiting for.
/// </summary>
/// <remarks>
/// <para>
/// A lease that was written with a short duration ends on the store's own clock (a database server's, for a store that
/// shares its database), which a test cannot move or read. The test does not sleep for a guess of how long that takes: the
/// answer that proves the lease is over is the one it waits for, and the question asked again is free of side effects while
/// the answer is still "no".
/// </para>
/// <para>
/// The limit only ends a wait that already failed. It is counted in questions and not in time, so the helper reads no clock
/// of its own, and a store that never answers is reported with what was waited for.
/// </para>
/// </remarks>
internal static class UntilAnswered
{
    private const int MaxQuestions = 1_500;

    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(20);

    /// <summary>Asks until <paramref name="accept"/> takes the answer, and returns it.</summary>
    /// <typeparam name="T">What the store answers.</typeparam>
    /// <param name="ask">The question; it must not change the store while the answer is not the one waited for.</param>
    /// <param name="accept">Whether the answer is the one the test waits for.</param>
    /// <param name="what">What the test waits for, for the message of a wait that failed.</param>
    /// <returns>The first answer <paramref name="accept"/> took.</returns>
    /// <exception cref="TimeoutException">No answer was taken within the limit.</exception>
    public static async ValueTask<T> AskAsync<T>(Func<ValueTask<T>> ask, Func<T, bool> accept, string what)
    {
        for (var asked = 0; asked < MaxQuestions; asked++)
        {
            var answer = await ask().ConfigureAwait(false);

            if (accept(answer))
            {
                return answer;
            }

            await Task.Delay(Pause, TestContext.Current.CancellationToken).ConfigureAwait(false); // delay: poll
        }

        throw new TimeoutException($"The store never answered as the test waited for: {what}.");
    }
}
