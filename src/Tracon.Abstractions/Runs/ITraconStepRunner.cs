namespace Tracon;

/// <summary>
/// Runs a deterministic step of the host's own code as a Tracon run, so what the step spends is recorded and counted like a run's.
/// </summary>
/// <remarks>
/// <para>
/// An agent run and a workflow run record themselves. A step of a content pipeline that draws a cover image or voices a script
/// without any model deciding anything has no run of its own, so its cost would be in no run, no statistic and no quota. This
/// interface opens one: a run row of kind <see cref="RunKind.Step"/> that is open while the step's code runs. Inside it,
/// <see cref="IImageGenerationService"/> and <see cref="ISpeechSynthesisService"/> record what they spend as the run's non-token
/// items, and an agent run started with <see cref="TraconRunOptions.ParentRunId"/> set to the step's run attaches below it.
/// </para>
/// <para>
/// <strong>A delegate and not a scope object.</strong> The run's identity is ambient (<c>TraconRunContext.Current</c> in Tracon's
/// core package), and an ambient value set inside a method that returns an object does not reach the caller. The step therefore
/// receives control here and runs inside.
/// </para>
/// <para>
/// <strong>No gate is asked.</strong> <c>IRunAuthorizationHandler</c> and the tenant's quota decide whether a run may start for a
/// caller; a step is the host's own code with no caller of its own. A host that starts steps for a user asks
/// <c>RunAdmission.CheckAsync</c> first. What the step spends is still written to the quota counters.
/// </para>
/// <para>
/// <strong>Model tokens belong to agent runs.</strong> A step's row carries no token usage of its own: a model call a step makes
/// belongs to an agent run started with the step as its parent, which writes its tokens and cost and is counted in the step's tree.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered by <c>AddTracon()</c> with <c>TryAdd</c>. Call this interface; do not
/// implement or replace it: the tenant check of the parent run, the recording and the quota entry run inside it.
/// </para>
/// <para>
/// <strong>Tenant behavior — AMBIENT tenant.</strong> The run belongs to the tenant of the current <c>ITenantContext</c>, and a
/// parent run must belong to it as well.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. The step runs once, in this process; nothing retries it and nothing
/// resumes it after a restart. A run whose process stops is closed by run reconciliation, like any other.
/// </para>
/// <para>
/// <strong>Recording does not break the step.</strong> A run store that cannot write, or cannot be read to check the ids the step
/// names, is logged and the step runs without a record, the rule every run follows. With <c>Tracon:RunRecording:Enabled</c> off the step runs and nothing is recorded.
/// </para>
/// </remarks>
/// <example>
/// A host endpoint that renders an episode without a model call and records what it spent:
/// <code>
/// app.MapPost("/episodes", async (
///     ITraconStepRunner steps,
///     IImageGenerationService images,
///     ISpeechSynthesisService speech,
///     CancellationToken cancellationToken) =>
/// {
///     var stepRunId = Guid.CreateVersion7();
///
///     await steps.RunAsync(
///         new TraconStepOptions { Name = "podcast-render", RunId = stepRunId },
///         async stepToken =>
///         {
///             var cover = await images.GenerateAsync(
///                 new TraconImageRequest { Prompt = "A lighthouse at dawn" }, stepToken);
///             var voice = await speech.SynthesizeAsync(
///                 new SpeechRequest { Text = "Welcome to the show." }, cancellationToken: stepToken);
///
///             await File.WriteAllBytesAsync("cover.png", cover.Images[0].Data.ToArray(), stepToken);
///             await File.WriteAllBytesAsync("welcome.mp3", voice.Audio.Data.ToArray(), stepToken);
///         },
///         cancellationToken);
///
///     return Results.Ok(stepRunId);
/// });
/// </code>
/// </example>
public interface ITraconStepRunner
{
    /// <summary>Runs a step that returns a value as a run.</summary>
    /// <typeparam name="T">The type of the step's result.</typeparam>
    /// <param name="options">The run's name, and optionally its id and its parent.</param>
    /// <param name="work">
    /// The step. Its token is canceled by <paramref name="cancellationToken"/> and by <c>POST /api/runs/{id}/cancel</c>.
    /// </param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>What the step returned.</returns>
    /// <exception cref="ArgumentException">The name is blank, longer than 200 characters or surrounded by whitespace.</exception>
    /// <exception cref="TraconException">
    /// <see cref="TraconStepOptions.RunId"/> already names a run, or <see cref="TraconStepOptions.ParentRunId"/> names no run of
    /// this tenant. Nothing is written and the step does not run.
    /// </exception>
    /// <remarks>
    /// An exception the step throws closes the run as <c>Failed</c> (or <c>Canceled</c> for a cancellation) and is thrown on
    /// unchanged.
    /// </remarks>
    ValueTask<T> RunAsync<T>(
        TraconStepOptions options,
        Func<CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken);

    /// <summary>Runs a step that returns nothing as a run.</summary>
    /// <param name="options">The run's name, and optionally its id and its parent.</param>
    /// <param name="work">The step; its token is the same as the one of the overload that returns a value.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes when the step has run and the run is closed.</returns>
    /// <exception cref="ArgumentException">The name is blank, longer than 200 characters or surrounded by whitespace.</exception>
    /// <exception cref="TraconException">
    /// <see cref="TraconStepOptions.RunId"/> already names a run, or <see cref="TraconStepOptions.ParentRunId"/> names no run of
    /// this tenant. Nothing is written and the step does not run.
    /// </exception>
    ValueTask RunAsync(
        TraconStepOptions options,
        Func<CancellationToken, ValueTask> work,
        CancellationToken cancellationToken);
}

/// <summary>Describes the run a step is recorded as.</summary>
public sealed record TraconStepOptions
{
    /// <summary>Gets the name the run is listed under (its <see cref="RunRecord.AgentName"/>), 1 to 200 characters.</summary>
    /// <remarks>
    /// A stable name per kind of step (<c>podcast-render</c>, <c>catalog-cover</c>): the run list filters by it and the statistics
    /// group by it, so a name with an identifier in it gives one group per run.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>Gets the run id the caller chose, or <see langword="null"/> for a new one.</summary>
    /// <remarks>
    /// It must be an id no run has, such as <c>Guid.CreateVersion7()</c>, and one your own code made: an id a client sent is an id a
    /// client chose. An id that already names a run of this tenant is refused before anything is written. An id a run of another
    /// tenant holds is refused by the run store, which never gives a row to another tenant: the other run is left as it was, the
    /// refusal is logged, and the step runs without a record. Two processes that start the same new id at the same moment are not
    /// told apart.
    /// </remarks>
    public Guid? RunId { get; init; }

    /// <summary>Gets the run this step is part of, or <see langword="null"/> for a step that stands alone.</summary>
    /// <remarks>
    /// The parent must be a run of the current tenant. The step joins the parent's tree: its cost is counted in the root run's
    /// tree totals, and the quota entry is written under the root's name.
    /// The state of the parent is not asked. A parent that has already ended, whether it completed, failed or was canceled,
    /// accepts the step, and the step's cost is added to the totals of the tree as it is for a parent that is still running; the
    /// ended parent's own status does not change. Only the parent's existence and its tenant are checked, so a step that must not
    /// join a run that is over has to be refused by the host before it asks for the run to be the parent.
    /// </remarks>
    public Guid? ParentRunId { get; init; }
}
