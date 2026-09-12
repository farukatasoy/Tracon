using System.Text.Json;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// An in-memory <see cref="IStatePreflightReader"/> that hands back exactly
/// the rows a test set up.
/// </summary>
/// <remarks>
/// The reader itself is measured against a real database in
/// <c>Tracon.Sqlite.IntegrationTests</c>; this fake exists so the
/// INTERPRETATION in <see cref="StatePreflight"/> can be driven through
/// states a real database is awkward to be pushed into (a generation from
/// the future, a payload that is not JSON).
/// </remarks>
internal sealed class FakeStatePreflightReader : IStatePreflightReader
{
    private readonly Dictionary<StatePreflightTarget, List<StateSample>> _samples = new()
    {
        [StatePreflightTarget.Sessions] = [],
        [StatePreflightTarget.WorkflowCheckpoints] = [],
    };

    public string ProviderName => "Fake";

    /// <summary>The sample sizes each <see cref="SampleAsync"/> call was made with.</summary>
    public List<int> RequestedSampleSizes { get; } = [];

    public FakeStatePreflightReader Add(StatePreflightTarget target, StateSample sample)
    {
        _samples[target].Add(sample);

        return this;
    }

    public ValueTask<IReadOnlyList<StateGenerationTally>> TallyAsync(
        StatePreflightTarget target,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StateGenerationTally> tallies =
        [
            .. _samples[target]
                .GroupBy(static sample => sample.SchemaGeneration)
                .Select(static group => new StateGenerationTally
                {
                    SchemaGeneration = group.Key,
                    RecordCount = group.Count(),
                })
                .OrderBy(static tally => tally.SchemaGeneration ?? int.MinValue),
        ];

        return ValueTask.FromResult(tallies);
    }

    public ValueTask<IReadOnlyList<StateSample>> SampleAsync(
        StatePreflightTarget target,
        int perGeneration,
        CancellationToken cancellationToken = default)
    {
        RequestedSampleSizes.Add(perGeneration);

        IReadOnlyList<StateSample> samples = perGeneration <= 0
            ? []
            : [.. _samples[target].GroupBy(static sample => sample.SchemaGeneration).SelectMany(group => group.Take(perGeneration))];

        return ValueTask.FromResult(samples);
    }

    /// <summary>Builds a sample carrying an arbitrary JSON payload.</summary>
    public static StateSample Sample(string id, int? generation, string stateJson, string? mafVersion = null) => new()
    {
        Id = id,
        SchemaGeneration = generation,
        MafVersion = mafVersion,
        State = JsonDocument.Parse(stateJson).RootElement.Clone(),
    };

    /// <summary>Builds a sample whose stored text did not parse as JSON at all.</summary>
    public static StateSample UnparsableSample(string id, int? generation) => new()
    {
        Id = id,
        SchemaGeneration = generation,
        State = default,
    };
}
