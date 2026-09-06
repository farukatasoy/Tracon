using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using AgentPrism.Client.Generated;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Proves that the NSwag-generated client's SSE-returning methods actually
/// work against a real server (Phase 145, independent-audit finding).
/// </summary>
/// <remarks>
/// Before this phase's fix (<c>nswag-postprocess-client.py</c>'s fourth
/// rewrite pass), every generated method for a pure <c>text/event-stream</c>
/// 200 response read its body through the SAME JSON-deserializing helper
/// every other operation uses — which threw <see cref="AgentPrismApiException"/>
/// unconditionally, because a raw SSE frame is not a JSON string literal.
/// Nothing in the repository called these methods against a real host, so
/// the defect was invisible to <c>dotnet build</c> and to
/// <see cref="ClientCoverageTests"/> (which only checks that the method
/// exists, not that a call to it succeeds) — exactly the gap
/// <c>AgentPrismClient</c>'s <c>AgentPrismPublicApiTrackingEnabled=false</c>
/// leaves for a generated method's runtime behavior. This test is the
/// missing real-server call for the two methods this phase touches.
/// </remarks>
public sealed class GeneratedClientSseTests
{
    [Fact]
    public async Task AgentPrismRunAgentAsync_reads_the_SSE_body_as_plain_text()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        var body = await client.AgentPrismRunAgentAsync("kod-agent", RunRequest());

        body.ShouldContain("event: run");
        body.ShouldContain("event: done");
    }

    [Fact]
    public async Task AgentPrismStreamRunEventsAsync_reads_the_SSE_body_as_plain_text()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        await client.AgentPrismRunAgentAsync("kod-agent", RunRequest());

        var runs = await client.AgentPrismListRunsAsync();
        var runId = runs.Single().Id;

        var body = await client.AgentPrismStreamRunEventsAsync(runId);

        body.ShouldContain("event: run.started");
        body.ShouldContain("event: run.completed");
        body.ShouldNotContain("event: unknown");
    }

    private static AgentPrismApiClient CreateClient(AgentPrismTestHost host)
    {
        // The generated client's paths are relative to the MapAgentPrism prefix
        // (docs/hafiza/nswag-istemci-uretimi.md), which AgentPrismClientOptions.BaseAddress
        // normally carries; host.Client's own BaseAddress is bare, so it is set here instead.
        host.Client.BaseAddress = new Uri(host.Client.BaseAddress!, "agentprism/");

        return new AgentPrismApiClient(host.Client);
    }

    // The collections are no longer filled to work around a null default —
    // F-197 fixed that in nswag-postprocess-client.py's fifth pass, and
    // GeneratedClientCollectionDefaultTests is the test that proves a minimal
    // request works. Only Message is set here, which is also what a real
    // caller writes.
    // Fully qualified: this file also sees the server-side AgentPrism.AgentRunRequest
    // through the project's global usings, and the bare name is ambiguous (CS0104).
    private static AgentPrism.Client.Generated.AgentRunRequest RunRequest() => new()
    {
        Message = "merhaba",
    };
}
