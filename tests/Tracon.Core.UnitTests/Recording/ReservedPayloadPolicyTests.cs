using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Which reserved <see cref="RunEventType.Custom"/> types keep their payload with
/// <c>RecordToolPayloads</c> off. The exemption is a list, not "every reserved type": a
/// notice that carries provider text or workflow data is an observability detail like any
/// tool payload.
/// </summary>
public sealed class ReservedPayloadPolicyTests
{
    private const string Payload = "{\"k\":1}";

    [Theory]
    [InlineData(RunEventCustomTypes.QuotaThreshold)]
    [InlineData(RunEventCustomTypes.OutputTruncated)]
    [InlineData(RunEventCustomTypes.OutputFiltered)]
    [InlineData(RunEventCustomTypes.Context)]
    public async Task A_notice_whose_payload_is_the_function_keeps_it_with_payloads_off(string customType)
    {
        var writer = await CreateStartedWriterAsync(recordToolPayloads: false);

        var runEvent = await writer.AppendReservedAsync(Draft(customType));

        runEvent.Payload.ShouldBe(Payload);
    }

    [Theory]
    [InlineData(RunEventCustomTypes.Content)]
    [InlineData(RunEventCustomTypes.Citation)]
    [InlineData(RunEventCustomTypes.WorkflowEvent)]
    [InlineData("tracon.a-reserved-type-added-later")]
    public async Task Any_other_reserved_type_loses_its_payload_with_payloads_off_but_keeps_its_text(string customType)
    {
        var writer = await CreateStartedWriterAsync(recordToolPayloads: false);

        var runEvent = await writer.AppendReservedAsync(Draft(customType));

        runEvent.Payload.ShouldBeNull();
        runEvent.Text.ShouldBe("summary");
        runEvent.CustomType.ShouldBe(customType);
    }

    [Theory]
    [InlineData(RunEventCustomTypes.Content)]
    [InlineData(RunEventCustomTypes.Citation)]
    [InlineData(RunEventCustomTypes.WorkflowEvent)]
    public async Task The_content_notices_keep_their_payload_with_payloads_on(string customType)
    {
        var writer = await CreateStartedWriterAsync(recordToolPayloads: true);

        var runEvent = await writer.AppendReservedAsync(Draft(customType));

        runEvent.Payload.ShouldBe(Payload);
    }

    [Theory]
    [InlineData(RunEventCustomTypes.Content)]
    [InlineData(RunEventCustomTypes.Citation)]
    [InlineData(RunEventCustomTypes.Context)]
    [InlineData(RunEventCustomTypes.WorkflowEvent)]
    public async Task A_consumer_cannot_write_a_content_notice_through_the_public_path(string customType)
    {
        var writer = await CreateStartedWriterAsync(recordToolPayloads: true);

        await Should.ThrowAsync<ArgumentException>(
            async () => await writer.AppendAsync(Draft(customType)));
    }

    private static RunEventDraft Draft(string customType)
        => new(RunEventType.Custom) { CustomType = customType, Text = "summary", Payload = Payload };

    private static async Task<RunEventWriter> CreateStartedWriterAsync(bool recordToolPayloads)
    {
        var store = new InMemoryRunStore();
        var options = new TraconRunRecordingOptions { RecordToolPayloads = recordToolPayloads };
        var writer = new RunEventWriter(store, options, NullLogger.Instance, TraconId.NewId(), metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "test-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "hello");

        return writer;
    }
}
