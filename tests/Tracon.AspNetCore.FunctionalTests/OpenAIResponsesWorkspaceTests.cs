using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs: the package's Tracon.Testing.TraconTestHost and
// this project's own TraconTestHost share a name.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

// MAAI001: the file tools of a session workspace are marked "evaluation purposes only" - the same
// rationale as AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A call to the Responses endpoint that chains with <c>previous_response_id</c> and names no conversation, for an agent that
/// keeps files for its session.
/// </summary>
/// <remarks>
/// <para>
/// The session of a chained call is the one the previous response saved, but the new response saves its own, so the session id
/// changes on every turn. The files of a session workspace sit under the session id: the second turn reads the first turn's
/// files, and every later turn starts from an empty directory while the files of the turns before stay under the id of an
/// earlier response. A client that chains calls is told so instead of losing its files without a word.
/// </para>
/// <para>
/// Each question crosses the HTTP boundary (the body names the chain), the DI boundary (the file store is the host's
/// registration) and the store boundary, and both compile paths of an agent (the plain agent and the harness agent) carry the
/// workspace, so a unit test would answer neither.
/// </para>
/// </remarks>
public sealed class OpenAIResponsesWorkspaceTests
{
    private const string ProviderName = "files-model";
    private const string AgentName = "drafter";

    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_that_chains_without_a_conversation_is_refused_and_a_conversation_keeps_the_workspace(bool harness)
    {
        await using var host = await StartAsync(harness);

        string firstId;

        using (var first = await PostAsync(host, new { model = AgentName, input = "first turn" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            firstId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("id").GetString()!;
        }

        using (var chained = await PostAsync(host, new { model = AgentName, input = "second turn", previous_response_id = firstId }))
        {
            chained.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var error = (await TraconTestHost.ReadJsonAsync(chained)).GetProperty("error");

            error.GetProperty("type").GetString().ShouldBe("invalid_request_error");
            error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("conversation");
        }

        using (var streamed = await PostAsync(host, new { model = AgentName, input = "second turn", previous_response_id = firstId, stream = true }))
        {
            streamed.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a stream that is refused is refused before it starts");
        }

        // The way to keep the files is the conversation: the first response id names it.
        using var kept = await PostAsync(host, new { model = AgentName, input = "second turn", conversation = firstId });

        kept.StatusCode.ShouldBe(HttpStatusCode.OK);

        var runs = host.Services.GetRequiredService<IRunStore>();

        (await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .Count.ShouldBe(2, "the refused calls started no run");
    }

    [Fact]
    public async Task An_agent_with_no_workspace_chains_as_it_always_did()
    {
        await using var host = await StartAsync(harness: false, workspace: false);

        string firstId;

        using (var first = await PostAsync(host, new { model = AgentName, input = "first turn" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            firstId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("id").GetString()!;
        }

        using var chained = await PostAsync(host, new { model = AgentName, input = "second turn", previous_response_id = firstId });

        chained.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> PostAsync(TraconTestHost host, object body)
        => host.Client.PostAsJsonAsync(Responses, body, TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(bool harness, bool workspace = true)
    {
        var provider = new FakeModelProvider(ProviderName).ForModel("files-1", static cfg => cfg.EchoesUserMessage());

        return TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Work with files.",
                Model = new ModelBinding { Provider = ProviderName, Model = "files-1" },
                Workspace = workspace ? new WorkspaceSettings { Enabled = true, DisableReadApproval = true } : null,
                Harness = harness ? new HarnessSettings() : null,
                Origin = AgentDefinitionOrigin.Code,
            });
        });
    }
}
