namespace Tracon.Testing;

/// <summary>Assertions on a recorded run.</summary>
/// <remarks>
/// Each unmet assertion throws <see cref="TraconAssertionException"/> and
/// writes the **expected and actual** value in its message. Streaming runs are
/// covered too: <c>run_events</c> fills in on the streaming path as well, no
/// separate type is needed.
/// </remarks>
public sealed class RunAssertions
{
    internal RunAssertions(
        RunRecord record,
        IReadOnlyList<RunEvent> events,
        IReadOnlyList<ToolInvocationRecord> toolInvocations)
    {
        Record = record;
        Events = events;
        ToolInvocations = toolInvocations;
    }

    /// <summary>The run record.</summary>
    public RunRecord Record { get; }

    /// <summary>The run's event stream, in sequence order.</summary>
    public IReadOnlyList<RunEvent> Events { get; }

    /// <summary>Tool calls made during the run.</summary>
    public IReadOnlyList<ToolInvocationRecord> ToolInvocations { get; }

    /// <summary>Asserts that the run completed successfully.</summary>
    /// <returns>The chain, for continued assertions.</returns>
    /// <exception cref="TraconAssertionException">The status is not <see cref="RunStatus.Completed"/>.</exception>
    public RunAssertions ShouldHaveCompleted()
    {
        if (Record.Status != RunStatus.Completed)
        {
            throw new TraconAssertionException(
                $"Expected the run status to be 'Completed' but found '{Record.Status}'.");
        }

        return this;
    }

    /// <summary>Asserts that the run ended with a failure.</summary>
    /// <returns>The chain, for continued assertions.</returns>
    /// <exception cref="TraconAssertionException">The status is not <see cref="RunStatus.Failed"/>.</exception>
    public RunAssertions ShouldHaveFailed()
    {
        if (Record.Status != RunStatus.Failed)
        {
            throw new TraconAssertionException(
                $"Expected the run status to be 'Failed' but found '{Record.Status}'.");
        }

        return this;
    }

    /// <summary>Asserts that the run ended with a specific error type.</summary>
    /// <param name="errorType">The value expected to match <see cref="TraconException.ErrorType"/>.</param>
    /// <returns>The chain, for continued assertions.</returns>
    /// <exception cref="TraconAssertionException">
    /// The status is not <see cref="RunStatus.Failed"/>, or the error type does not match.
    /// </exception>
    public RunAssertions ShouldHaveFailedWith(string errorType)
    {
        ShouldHaveFailed();

        var actual = Record.Error?.Type;

        if (!string.Equals(actual, errorType, StringComparison.Ordinal))
        {
            throw new TraconAssertionException(
                $"Expected the error type to be '{errorType}' but found '{actual}'.");
        }

        return this;
    }

    /// <summary>Asserts that a specific tool was called.</summary>
    /// <param name="toolName">Tool name.</param>
    /// <param name="times">If given, the exact number of calls expected.</param>
    /// <returns>The chain, for continued assertions.</returns>
    /// <exception cref="TraconAssertionException">The tool was never called, or the count does not match.</exception>
    public RunAssertions ShouldHaveCalledTool(string toolName, int? times = null)
    {
        var count = ToolInvocations.Count(invocation => string.Equals(invocation.ToolName, toolName, StringComparison.Ordinal));

        if (count == 0)
        {
            throw new TraconAssertionException(
                $"Expected tool '{toolName}' to be called at least once but it was never called.");
        }

        if (times is { } expected && count != expected)
        {
            throw new TraconAssertionException(
                $"Expected tool '{toolName}' to be called {expected} time(s) but it was called {count} time(s).");
        }

        return this;
    }

    /// <summary>Asserts that a specific tool was never called.</summary>
    /// <param name="toolName">Tool name.</param>
    /// <returns>The chain, for continued assertions.</returns>
    /// <exception cref="TraconAssertionException">The tool was called at least once.</exception>
    public RunAssertions ShouldNotHaveCalledTool(string toolName)
    {
        var count = ToolInvocations.Count(invocation => string.Equals(invocation.ToolName, toolName, StringComparison.Ordinal));

        if (count > 0)
        {
            throw new TraconAssertionException(
                $"Expected tool '{toolName}' to never be called but it was called {count} time(s).");
        }

        return this;
    }

    /// <summary>Asserts that the run's generated text contains a specific substring.</summary>
    /// <param name="text">Substring to look for.</param>
    /// <returns>The chain, for continued assertions.</returns>
    /// <exception cref="TraconAssertionException">The substring was not found.</exception>
    /// <remarks>
    /// If a <see cref="RunEventType.MessageCompleted"/> exists (non-streaming
    /// run) it is used; otherwise (a streaming run, e.g. the HTTP <c>/run</c>
    /// endpoint) the <see cref="RunEventType.MessageDelta"/> chunks are
    /// concatenated. The two are never both present in the same run — also
    /// summing the chunks on the non-streaming path would DUPLICATE the text.
    /// </remarks>
    public RunAssertions ShouldHaveOutputContaining(string text)
    {
        var completed = Events
            .Where(static runEvent => runEvent.Type == RunEventType.MessageCompleted)
            .Select(static runEvent => runEvent.Text ?? string.Empty)
            .ToList();

        var output = completed.Count > 0
            ? string.Concat(completed)
            : string.Concat(Events
                .Where(static runEvent => runEvent.Type == RunEventType.MessageDelta)
                .Select(static runEvent => runEvent.Text ?? string.Empty));

        if (!output.Contains(text, StringComparison.Ordinal))
        {
            throw new TraconAssertionException(
                $"Expected the output to contain '{text}' but found: '{output}'.");
        }

        return this;
    }
}
