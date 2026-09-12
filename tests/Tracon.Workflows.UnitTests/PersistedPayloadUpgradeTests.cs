using System.Text.Json;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Phase 126: the compatibility check for persisted checkpoint state.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the session-state fixture in <c>Tracon.Core.UnitTests</c>, this
/// test cannot exercise a full <c>ResumeStreamingAsync</c> against the
/// captured fixture: Microsoft Agent Framework derives executor identity
/// from <c>AIAgent.Id</c>, generated fresh and RANDOM per process
/// (<c>docs/hafiza/workflows.md</c>). A checkpoint resumed in a different
/// process than the one that wrote it always fails with
/// <c>InvalidDataException</c> ("not compatible with the workflow") for
/// that reason alone, independent of whether the STATE payload itself is
/// still readable — resuming this repo's own fixture from a fresh test run
/// would always look broken, for the wrong reason.
/// </para>
/// <para>
/// What this test verifies instead: the state <see cref="JsonElement"/>
/// captured from a real run still parses, still carries the
/// <c>$type</c>-discriminator-first invariant Microsoft Agent Framework
/// requires (see <c>WorkflowCheckpointRecord</c>'s remarks), and round-trips
/// byte-for-byte through Tracon's own persistence layer
/// (<see cref="InMemoryWorkflowCheckpointStore"/>) without being
/// reinterpreted or reordered. If a future Microsoft Agent Framework
/// Workflows version changes the checkpoint SHAPE in a way that breaks this
/// invariant, this is where it is caught.
/// </para>
/// </remarks>
public sealed class PersistedPayloadUpgradeTests
{
    [Fact]
    public async Task Todays_code_reads_and_round_trips_a_checkpoint_fixture_written_by_todays_MAF()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "workflow-checkpoint-1.18.0.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var capturedState = document.RootElement;

        // The $type discriminator invariant K-027/K-121 depend on: wherever
        // it appears (here, inside the first edge of the graph), it must
        // still be the first property of ITS OWN object, or Microsoft Agent
        // Framework's own deserializer rejects the payload outright.
        var firstEdge = capturedState.GetProperty("workflow").GetProperty("edges")
            .EnumerateObject().First().Value[0];

        firstEdge.EnumerateObject().First().Name.ShouldBe("$type");

        var store = new InMemoryWorkflowCheckpointStore();

        var written = await store.CreateAsync(new WorkflowCheckpointRecord
        {
            Id = TraconId.NewId(),
            TenantId = "tenant-a",
            SessionId = "s-fixture",
            CheckpointId = "c-fixture",
            CreatedAt = DateTimeOffset.UtcNow,
            State = capturedState,
        });

        var read = await store.ReadAsync("tenant-a", "s-fixture", "c-fixture");

        read.ShouldNotBeNull();
        read.Value.GetRawText().ShouldBe(capturedState.GetRawText());
        written.State.GetRawText().ShouldBe(capturedState.GetRawText());
    }
}
