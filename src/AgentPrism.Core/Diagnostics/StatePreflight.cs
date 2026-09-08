using System.Reflection;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Answers "can this build still read the state already in the database"
/// WITHOUT writing anything and without starting the application.
/// </summary>
/// <remarks>
/// <para>
/// The question is normally answered in production, after the upgrade. This
/// class moves it BEFORE the upgrade: it counts the stored schema generations
/// across every tenant, compares them against what the running code writes,
/// and tries to decode a small sample.
/// </para>
/// <para>
/// <strong>Nothing here writes.</strong> It reads through
/// <see cref="IStatePreflightReader"/>, which issues only <c>SELECT</c>
/// statements, and it never touches <see cref="ISessionStore"/> — restoring a
/// session through the normal path would stamp and save.
/// </para>
/// <para>
/// The decode probe builds a bare <see cref="ChatClientAgent"/> over a chat
/// client that refuses every call. Microsoft Agent Framework's
/// <c>DeserializeSessionAsync</c> never invokes the chat client, so a
/// preflight needs no model provider, no API key and no agent definition —
/// which is what lets it run from the CLI, against a database whose
/// application is not even up.
/// </para>
/// </remarks>
public sealed class StatePreflight
{
    /// <summary>The Microsoft Agent Framework version this process runs.</summary>
    private static readonly string CurrentMafVersion = AssemblyVersionText.Read(typeof(AIAgent).Assembly);

    private readonly IStatePreflightReader _reader;

    /// <summary>Creates a new preflight over a read-only reader.</summary>
    /// <param name="reader">The read-only source of stored state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public StatePreflight(IStatePreflightReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    /// <summary>Runs the preflight.</summary>
    /// <param name="samplePerGeneration">
    /// How many rows of EACH stored generation to decode. <c>0</c> counts
    /// generations and decodes nothing.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="samplePerGeneration"/> is negative.</exception>
    public async ValueTask<StatePreflightReport> RunAsync(
        int samplePerGeneration = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(samplePerGeneration);

        var sessions = await CountAsync(StatePreflightTarget.Sessions, StateSchemaGenerations.Session, cancellationToken)
            .ConfigureAwait(false);
        var checkpoints = await CountAsync(StatePreflightTarget.WorkflowCheckpoints, StateSchemaGenerations.WorkflowCheckpoint, cancellationToken)
            .ConfigureAwait(false);

        var failures = new List<StateSampleFailure>();
        var decoded = 0;
        var structureOnly = 0;

        var sessionSamples = await _reader
            .SampleAsync(StatePreflightTarget.Sessions, samplePerGeneration, cancellationToken)
            .ConfigureAwait(false);

        // Built once and reused: constructing it per row would measure agent
        // construction, not the stored payload. Built from the rows that
        // actually came back rather than from the requested sample size —
        // IStatePreflightReader is public, and a third-party implementation
        // that returns rows for a size of 0 must still get a defined answer
        // instead of a NullReferenceException.
        var probe = sessionSamples.Count > 0 ? CreateProbeAgent() : null;

        foreach (var sample in sessionSamples)
        {
            var outcome = await DecodeSessionAsync(probe!, sample, cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case null:
                    decoded++;
                    break;
                case { Reason: null }:
                    structureOnly++;
                    break;
                default:
                    failures.Add(outcome.ToFailure(StatePreflightTarget.Sessions, sample));
                    break;
            }
        }

        var checkpointSamples = await _reader
            .SampleAsync(StatePreflightTarget.WorkflowCheckpoints, samplePerGeneration, cancellationToken)
            .ConfigureAwait(false);

        foreach (var sample in checkpointSamples)
        {
            // A checkpoint payload is Microsoft Agent Framework's own opaque
            // blob and has no decoder outside a running workflow, so the only
            // honest check here is that the stored text is still a JSON
            // document. The report says so rather than implying more.
            if (sample.State.ValueKind is JsonValueKind.Undefined)
            {
                failures.Add(new StateSampleFailure
                {
                    Target = StatePreflightTarget.WorkflowCheckpoints,
                    Id = sample.Id,
                    SchemaGeneration = sample.SchemaGeneration,
                    RecordedMafVersion = sample.MafVersion,
                    Reason = "the stored state is not a JSON document",
                });
            }
            else
            {
                structureOnly++;
            }
        }

        return new StatePreflightReport
        {
            ProviderName = _reader.ProviderName,
            Sessions = sessions,
            Checkpoints = checkpoints,
            SamplePerGeneration = samplePerGeneration,
            DecodedSampleCount = decoded,
            StructureOnlySampleCount = structureOnly,
            SampleFailures = failures,
            RunningMafVersion = CurrentMafVersion,
        };
    }

    /// <summary>Counts one table's generations and marks each readable or not.</summary>
    /// <param name="target">The table to count.</param>
    /// <param name="currentGeneration">The generation this build writes for that table.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The counts.</returns>
    private async ValueTask<IReadOnlyList<StateGenerationCount>> CountAsync(
        StatePreflightTarget target,
        int currentGeneration,
        CancellationToken cancellationToken)
    {
        var tallies = await _reader.TallyAsync(target, cancellationToken).ConfigureAwait(false);

        return [.. tallies.Select(tally => new StateGenerationCount
        {
            Target = target,
            SchemaGeneration = tally.SchemaGeneration,
            RecordCount = tally.RecordCount,
            // An unstamped row predates stamping and therefore cannot come
            // from the future. This mirrors the null-tolerant comparison the
            // restore paths themselves make.
            ReadableByThisBuild = tally.SchemaGeneration is not int generation || generation <= currentGeneration,
        })];
    }

    /// <summary>Tries to decode one sampled session.</summary>
    /// <param name="probe">The bare probe agent.</param>
    /// <param name="sample">The sampled row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="null"/> when the payload decoded; an outcome carrying a
    /// <see langword="null"/> reason when the payload could only be checked
    /// structurally; an outcome carrying a reason when it failed.
    /// </returns>
    private static async ValueTask<DecodeOutcome?> DecodeSessionAsync(
        AIAgent probe,
        StateSample sample,
        CancellationToken cancellationToken)
    {
        if (sample.State.ValueKind is JsonValueKind.Undefined)
        {
            return new DecodeOutcome("the stored state is not a JSON document");
        }

        if (ContentProtectionEnvelope.IsProtected(sample.State))
        {
            // Encrypted at rest and this process holds no key. Reporting this
            // as a failure would be a false alarm: the application that DOES
            // hold the key reads the row fine.
            return DecodeOutcome.StructureOnly;
        }

        if (sample.SchemaGeneration is int generation && generation > StateSchemaGenerations.Session)
        {
            // Already reported by the generation count; decoding it would only
            // produce a second, less precise message about the same row.
            return DecodeOutcome.StructureOnly;
        }

        try
        {
            _ = await probe.DeserializeSessionAsync(sample.State, jsonSerializerOptions: null, cancellationToken)
                .ConfigureAwait(false);

            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return new DecodeOutcome(
                $"the stored state could not be deserialized by Microsoft Agent Framework {CurrentMafVersion} ({ex.GetType().Name})");
        }
    }

    /// <summary>Builds the agent the decode probe reads through.</summary>
    /// <returns>An agent whose chat client refuses every call.</returns>
    private static ChatClientAgent CreateProbeAgent() =>
        new ChatClientAgent(new RefusingChatClient(), new ChatClientAgentOptions { Name = "agentprism-state-preflight" });

    /// <summary>The result of one decode attempt.</summary>
    /// <param name="Reason">The failure reason, or <see langword="null"/> when the row was only checked structurally.</param>
    private sealed record DecodeOutcome(string? Reason)
    {
        /// <summary>The row was read, but not decoded.</summary>
        public static DecodeOutcome StructureOnly { get; } = new((string?)null);

        /// <summary>Turns this outcome into a report entry.</summary>
        /// <param name="target">The table the row belongs to.</param>
        /// <param name="sample">The sampled row.</param>
        /// <returns>The report entry.</returns>
        public StateSampleFailure ToFailure(StatePreflightTarget target, StateSample sample) => new()
        {
            Target = target,
            Id = sample.Id,
            SchemaGeneration = sample.SchemaGeneration,
            RecordedMafVersion = sample.MafVersion,
            Reason = Reason!,
        };
    }

    /// <summary>
    /// A chat client that exists only to satisfy <see cref="ChatClientAgent"/>'s
    /// constructor. Deserializing a session never calls it.
    /// </summary>
    /// <remarks>
    /// It throws rather than returning an empty response on purpose: if a
    /// future Microsoft Agent Framework version ever started calling the chat
    /// client from the deserialize path, a silent stub would turn that into a
    /// wrong "readable" verdict, while this one turns it into a loud failure.
    /// </remarks>
    private sealed class RefusingChatClient : IChatClient
    {
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The state preflight never runs an agent; it only reads stored state.");

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The state preflight never runs an agent; it only reads stored state.");

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
