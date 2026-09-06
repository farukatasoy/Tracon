using System.Text;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using AgentPrism.Client.Generated;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Proves that a caller can build a generated request DTO with only the
/// fields they care about and have the call succeed (F-197).
/// </summary>
/// <remarks>
/// NJsonSchema emits <c>= default!</c> for every property, including the
/// ones it declares as NON-nullable collections
/// (<c>ICollection&lt;T&gt;</c> with no <c>?</c>). The declared type promises
/// non-null and the value is <see langword="null"/> — so the compiler warns
/// nobody, the client serializes an explicit <c>"documents": null</c>, and
/// System.Text.Json OVERWRITES the server record's own <c>= []</c>
/// initializer with that null. Server code that reads the collection
/// unconditionally (<c>request.Documents.Count</c>,
/// <c>AgentEndpoints.RunAsync</c>) then throws
/// <see cref="NullReferenceException"/> — a 500 for a request the type
/// system said was valid.
/// <para>
/// This is invisible to <c>ClientCoverageTests</c> (which only proves a
/// method NAME exists) and to <c>dotnet build</c>. It is only visible when a
/// MINIMAL request crosses the real HTTP boundary, which is why this test
/// lives at the functional level.
/// </para>
/// </remarks>
public sealed class GeneratedClientCollectionDefaultTests
{
    [Fact]
    public async Task A_run_request_carrying_only_a_message_does_not_fail_on_an_unset_collection()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var client = CreateClient(host);

        // Deliberately NOT setting Approvals/ToolResults/AttachmentIds/Documents:
        // that is the whole point — a caller should not have to know which
        // collections the server happens to read unconditionally.
        var body = await client.AgentPrismRunAgentAsync(
            "kod-agent",
            new AgentPrism.Client.Generated.AgentRunRequest { Message = "merhaba" });

        body.ShouldContain("event: done");
    }

    [Fact]
    public void Every_non_nullable_collection_property_starts_empty_not_null()
    {
        var request = new AgentPrism.Client.Generated.AgentRunRequest();

        request.Approvals.ShouldNotBeNull();
        request.ToolResults.ShouldNotBeNull();
        request.AttachmentIds.ShouldNotBeNull();
        request.Documents.ShouldNotBeNull();

        // A property NSwag declared nullable keeps its null: the annotation is
        // the contract that says "not provided" is distinguishable from
        // "provided empty", and the fix must not erase it.
        request.Parameters.ShouldBeNull();
    }

    /// <summary>
    /// The same class of defect on the SERVER, which fixing the typed client
    /// cannot reach: a RAW HTTP caller (curl, another language, an OpenAI SDK)
    /// can still send an explicit <c>null</c> for a collection the contract
    /// declares non-nullable.
    /// </summary>
    /// <remarks>
    /// System.Text.Json OVERWRITES a record's <c>= []</c> initializer when the
    /// JSON carries an explicit null, so the property ends up null behind a
    /// non-nullable declaration. MEASURED before the fix (2026-09-06):
    /// <c>approvals</c>, <c>toolResults</c> and <c>attachmentIds</c> each
    /// returned <c>500</c> with a <see cref="NullReferenceException"/>, and
    /// <c>documents</c> returned <c>200</c> whose SSE body carried the same
    /// exception — a run that failed after the caller was told it started.
    /// The contract answer is <c>400</c>, not <c>500</c>: the body IS invalid
    /// (K-694).
    /// </remarks>
    [Theory]
    [InlineData("approvals")]
    [InlineData("toolResults")]
    [InlineData("attachmentIds")]
    [InlineData("documents")]
    public async Task An_explicit_null_for_a_non_nullable_collection_is_a_400_not_a_500(string property)
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var content = new StringContent(
            $$"""{"message":"merhaba","{{property}}":null}""",
            Encoding.UTF8,
            "application/json");

        var response = await host.Client.PostAsync("agentprism/api/agents/kod-agent/run", content);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain(property);
    }

    /// <summary>
    /// The guard must not turn a legitimately OMITTED collection into a 400 —
    /// the record's own initializer still has to win when the key is absent.
    /// </summary>
    [Fact]
    public async Task An_omitted_collection_still_uses_the_contract_default()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var content = new StringContent(
            """{"message":"merhaba"}""",
            Encoding.UTF8,
            "application/json");

        var response = await host.Client.PostAsync("agentprism/api/agents/kod-agent/run", content);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    /// <summary>A null for a property the contract DOES declare nullable stays legal.</summary>
    [Fact]
    public async Task An_explicit_null_for_a_nullable_property_is_still_accepted()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var content = new StringContent(
            """{"message":"merhaba","sessionId":null,"parameters":null}""",
            Encoding.UTF8,
            "application/json");

        var response = await host.Client.PostAsync("agentprism/api/agents/kod-agent/run", content);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    private static AgentPrismApiClient CreateClient(AgentPrismTestHost host)
    {
        host.Client.BaseAddress = new Uri(host.Client.BaseAddress!, "agentprism/");

        return new AgentPrismApiClient(host.Client);
    }
}
