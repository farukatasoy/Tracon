using System.Text.Json;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Phase 156: the read-only state preflight.
/// </summary>
/// <remarks>
/// <see cref="A_session_written_by_a_fully_wired_agent_decodes_through_the_bare_probe"/>
/// is the load-bearing one. The preflight decodes with a bare
/// <c>ChatClientAgent</c> over a chat client that refuses every call,
/// because the CLI holds no model provider credentials — if that bare agent
/// could NOT read what a fully wired Tracon agent writes, every clean
/// database would report a false failure.
/// </remarks>
public sealed class StatePreflightTests
{
    [Fact]
    public async Task A_session_written_by_a_fully_wired_agent_decodes_through_the_bare_probe()
    {
        var state = await WriteRealSessionStateAsync();

        var report = await new StatePreflight(
                new FakeStatePreflightReader().Add(
                    StatePreflightTarget.Sessions,
                    new StateSample { Id = "s1", SchemaGeneration = 1, State = state }))
            .RunAsync(samplePerGeneration: 5);

        report.SampleFailures.ShouldBeEmpty();
        report.DecodedSampleCount.ShouldBe(1, "the probe agent has to genuinely deserialize, not skip");
        report.IsClean.ShouldBeTrue();
    }

    [Fact]
    public async Task A_generation_from_the_future_is_reported_with_its_row_count()
    {
        var reader = new FakeStatePreflightReader()
            .Add(StatePreflightTarget.Sessions, FakeStatePreflightReader.Sample("ok", 1, "{}"))
            .Add(StatePreflightTarget.Sessions, FakeStatePreflightReader.Sample("future-a", 99, "{}"))
            .Add(StatePreflightTarget.Sessions, FakeStatePreflightReader.Sample("future-b", 99, "{}"));

        var report = await new StatePreflight(reader).RunAsync(samplePerGeneration: 5);

        report.HasUnreadableGeneration.ShouldBeTrue();
        report.UnreadableRecordCount.ShouldBe(2);
        report.IsClean.ShouldBeFalse();
        report.Sessions.Single(static generation => generation.SchemaGeneration == 99)
            .ReadableByThisBuild.ShouldBeFalse();
        report.Sessions.Single(static generation => generation.SchemaGeneration == 1)
            .ReadableByThisBuild.ShouldBeTrue();
    }

    [Fact]
    public async Task A_row_from_the_future_is_not_ALSO_reported_as_a_decode_failure()
    {
        var reader = new FakeStatePreflightReader()
            .Add(StatePreflightTarget.Sessions, FakeStatePreflightReader.Sample("future", 99, """{"stateBag":"not an object"}"""));

        var report = await new StatePreflight(reader).RunAsync(samplePerGeneration: 5);

        // The generation count already names it, and precisely. A second,
        // vaguer message about the same row would make the operator hunt for
        // two problems where there is one.
        report.SampleFailures.ShouldBeEmpty();
        report.HasUnreadableGeneration.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unstamped_checkpoint_generation_counts_as_readable()
    {
        var reader = new FakeStatePreflightReader()
            .Add(StatePreflightTarget.WorkflowCheckpoints, FakeStatePreflightReader.Sample("old", null, "{}"));

        var report = await new StatePreflight(reader).RunAsync(samplePerGeneration: 5);

        // A row written before stamping existed cannot be from the future.
        // The resume path itself makes the same null-tolerant comparison.
        report.Checkpoints.Single().ReadableByThisBuild.ShouldBeTrue();
        report.HasUnreadableGeneration.ShouldBeFalse();
    }

    [Fact]
    public async Task A_checkpoint_is_counted_as_structure_only_never_as_decoded()
    {
        var reader = new FakeStatePreflightReader()
            .Add(StatePreflightTarget.WorkflowCheckpoints, FakeStatePreflightReader.Sample("c1", 1, """{"anything":1}"""));

        var report = await new StatePreflight(reader).RunAsync(samplePerGeneration: 5);

        // There is no decoder for a checkpoint payload outside a running
        // workflow, so claiming it was decoded would overstate the evidence.
        report.DecodedSampleCount.ShouldBe(0);
        report.StructureOnlySampleCount.ShouldBe(1);
        report.SampledCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_state_that_is_not_JSON_is_a_named_sample_failure()
    {
        var reader = new FakeStatePreflightReader()
            .Add(StatePreflightTarget.Sessions, FakeStatePreflightReader.UnparsableSample("broken", 1));

        var report = await new StatePreflight(reader).RunAsync(samplePerGeneration: 5);

        var failure = report.SampleFailures.ShouldHaveSingleItem();
        failure.Id.ShouldBe("broken");
        failure.Target.ShouldBe(StatePreflightTarget.Sessions);
        failure.Reason.ShouldContain("not a JSON document");
        report.IsClean.ShouldBeFalse();
    }

    [Fact]
    public async Task A_state_MAF_cannot_read_names_both_versions_instead_of_guessing()
    {
        var reader = new FakeStatePreflightReader()
            .Add(
                StatePreflightTarget.Sessions,
                FakeStatePreflightReader.Sample(
                    "corrupt",
                    1,
                    """{"stateBag":"this should be an object, not a string"}""",
                    mafVersion: "1.0.0-older"));

        var report = await new StatePreflight(reader).RunAsync(samplePerGeneration: 5);

        var failure = report.SampleFailures.ShouldHaveSingleItem();
        failure.RecordedMafVersion.ShouldBe("1.0.0-older");
        failure.Reason.ShouldContain(report.RunningMafVersion);
    }

    [Fact]
    public async Task An_encrypted_state_is_not_reported_as_unreadable()
    {
        // The CLI holds no content protection key, so it CANNOT decode a
        // protected row. Calling that a failure would raise a false alarm
        // about a row the application reads perfectly well.
        var reader = new FakeStatePreflightReader()
            .Add(
                StatePreflightTarget.Sessions,
                FakeStatePreflightReader.Sample("encrypted", 1, """{"$apEnc":1,"kid":"k1","n":"","c":""}"""));

        var report = await new StatePreflight(reader).RunAsync(samplePerGeneration: 5);

        report.SampleFailures.ShouldBeEmpty();
        report.DecodedSampleCount.ShouldBe(0);
        report.StructureOnlySampleCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_sample_size_of_zero_counts_generations_and_decodes_nothing()
    {
        var reader = new FakeStatePreflightReader()
            .Add(StatePreflightTarget.Sessions, FakeStatePreflightReader.UnparsableSample("broken", 1));

        var report = await new StatePreflight(reader).RunAsync(samplePerGeneration: 0);

        report.SampledCount.ShouldBe(0);
        report.SampleFailures.ShouldBeEmpty();
        report.Sessions.ShouldHaveSingleItem().RecordCount.ShouldBe(1);
        reader.RequestedSampleSizes.ShouldAllBe(static size => size == 0);
    }

    [Fact]
    public async Task An_empty_database_is_a_clean_report_not_an_error()
    {
        var report = await new StatePreflight(new FakeStatePreflightReader()).RunAsync(samplePerGeneration: 5);

        report.Sessions.ShouldBeEmpty();
        report.Checkpoints.ShouldBeEmpty();
        report.IsClean.ShouldBeTrue();
        report.SampledCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_negative_sample_size_is_rejected_rather_than_silently_clamped()
        => await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await new StatePreflight(new FakeStatePreflightReader()).RunAsync(samplePerGeneration: -1));

    /// <summary>
    /// Produces session state the way production does: a compiled agent with a
    /// chat history provider and a tool, two real turns, then
    /// <see cref="AgentSessionManager.SaveSessionAsync"/>.
    /// </summary>
    private static async Task<JsonElement> WriteRealSessionStateAsync()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);

        var agent = new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider(new FakeChatClient())),
                TestData.Registry(TestData.Tool("lookup")),
                chatHistoryProvider: new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions()))
            .Compile(TestData.Definition(toolNames: ["lookup"]));

        var session = await manager.GetOrCreateSessionAsync(agent, "real");
        await agent.RunAsync("What is the return policy?", session);
        await agent.RunAsync("And for electronics specifically?", session);
        await manager.SaveSessionAsync(agent, session);

        return (await store.GetAsync("real"))!.State;
    }
}
