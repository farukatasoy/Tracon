using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The workflows screens: the graph and human-in-the-loop requests.
/// </summary>
public sealed class WorkflowTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Workflow_graph_renders()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows");

        await Expect(session.Page.GetByText("summarize-and-translate")).ToBeVisibleAsync();
        await session.Page.GetByText("summarize-and-translate").ClickAsync();

        var graph = session.Page.GetByTestId("workflow-graph");

        await Expect(graph).ToBeVisibleAsync();

        // The built-in pattern adds an output node that the user did not
        // write: two agents + OutputMessages. The graph is rendered from the
        // COMPILED workflow, not the definition, so that node also appears.
        var nodes = session.Page.GetByTestId("workflow-node");

        // A retrying check, not a one-shot count: the nodes render after the
        // graph container does (Phase 184).
        await Expect(nodes.Nth(2), "The compiled graph shows fewer than three nodes.").ToBeVisibleAsync();
    }

    [Fact]
    public async Task Mermaid_text_can_be_copied()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await session.Page.GotoAsync($"{host.UiAddress}/workflows/summarize-and-translate");

        await Expect(session.Page.GetByTestId("workflow-graph")).ToBeVisibleAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Copy" }).First.ClickAsync();

        var copied = await session.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // The copied text is produced by Microsoft Agent Framework; the UI
        // does not render it, it only passes it through (bundle budget, K-002).
        copied.ShouldContain("flowchart", Case.Sensitive);
    }

    /// <summary>
    /// Saving a function-node workflow from the console keeps its chain.
    /// </summary>
    /// <remarks>
    /// The editor used to open this workflow with an EMPTY participant list and a
    /// disabled Save. Adding an agent enabled Save, and the full-replace PUT
    /// (K-725) replaced the chain with that agent: the function node was gone,
    /// with no warning. Measured on the sample app on 2026-10-10. Since phase 195 the
    /// editor edits the chain; this case still pins that a change to something else
    /// (the description) sends the chain back whole.
    /// </remarks>
    [Fact]
    public async Task Editing_a_function_node_workflow_keeps_its_chain()
    {
        await using var host = await UiHost.StartAsync(configureTracon: static tracon => tracon
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "word-count",
                static _ => static (messages, _, _) => ValueTask.FromResult(messages),
                "Passes the messages on unchanged."));
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        using (var created = await api.PutAsJsonAsync("api/workflows/mixed-chain", new
        {
            description = "Answers, then counts words.",
            kind = "Sequential",
            nodes = new[]
            {
                new { name = "support", kind = "Agent" },
                new { name = "word-count", kind = "Function" },
            },
        }))
        {
            created.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/mixed-chain/edit");

        var chain = session.Page.GetByTestId("workflow-participants");

        await Expect(chain).ToContainTextAsync("support");
        await Expect(chain).ToContainTextAsync("word-count");

        await session.Page.GetByLabel("Description", new() { Exact = true }).FillAsync("Edited in the console.");
        await session.Page.GetByTestId("workflow-save").ClickAsync();

        await Expect(session.Page).ToHaveURLAsync($"{host.UiAddress}/workflows/mixed-chain");

        var saved = await api.GetFromJsonAsync<JsonElement>("api/workflows/mixed-chain");

        saved.GetProperty("description").GetString().ShouldBe("Edited in the console.");
        saved.GetProperty("agentNames").GetArrayLength().ShouldBe(0);

        var nodes = saved.GetProperty("nodes");

        nodes.GetArrayLength().ShouldBe(2);
        nodes[0].GetProperty("name").GetString().ShouldBe("support");
        nodes[1].GetProperty("name").GetString().ShouldBe("word-count");
        nodes[1].GetProperty("kind").GetString().ShouldBe("Function");
    }

    /// <summary>
    /// Builds a chain with a function that takes settings, saves it and runs it - all in the console.
    /// </summary>
    /// <remarks>
    /// F-292 + F-295. The whole path crosses every layer the unit tests stand apart from: the
    /// function list from <c>GET /api/workflows/functions</c>, the node list the editor sends, the
    /// strict read of the settings on save, and the settings the function receives when the run
    /// compiles the graph.
    /// </remarks>
    [Fact]
    public async Task A_chain_built_in_the_console_with_function_settings_is_saved_and_runs()
    {
        await using var host = await UiHost.StartAsync(configureTracon: static tracon => tracon
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, SpeakSettings>(
                "speak",
                static _ => static (_, settings, _, _) => ValueTask.FromResult(
                    new List<ChatMessage>([new ChatMessage(ChatRole.User, $"spoken by {settings.Voice} at {settings.LoudnessLufs} LUFS")])),
                static options => options.Description = "Reads the text aloud."));
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/new");

        await session.Page.GetByTestId("workflow-name").FillAsync("narrated");
        await session.Page.GetByTestId("workflow-add-agent").SelectOptionAsync("support");
        await session.Page.GetByTestId("workflow-add-function").SelectOptionAsync("speak");

        var field = session.Page.GetByTestId("workflow-settings-speak");

        await Expect(session.Page.GetByTestId("workflow-settings-fields-speak")).ToContainTextAsync("voice (string, required)");

        await field.FillAsync("""{"voice":"alloy","loudnessLufs":-14}""");
        await session.Page.GetByTestId("workflow-save").ClickAsync();

        await Expect(session.Page).ToHaveURLAsync($"{host.UiAddress}/workflows/narrated");

        // Stored in the canonical form for a chain that holds a function: nodes, with agentNames empty.
        var saved = await api.GetFromJsonAsync<JsonElement>("api/workflows/narrated");

        saved.GetProperty("agentNames").GetArrayLength().ShouldBe(0);

        var nodes = saved.GetProperty("nodes");

        nodes.GetArrayLength().ShouldBe(2);
        nodes[0].GetProperty("kind").GetString().ShouldBe("Agent");
        nodes[0].GetProperty("settings").ValueKind.ShouldBe(JsonValueKind.Null);
        nodes[1].GetProperty("settings").GetProperty("voice").GetString().ShouldBe("alloy");
        nodes[1].GetProperty("settings").GetProperty("loudnessLufs").GetDouble().ShouldBe(-14);

        // And it runs: the function got the settings the operator typed.
        await session.Page.GetByTestId("workflow-message").FillAsync("hello");
        await session.Page.GetByTestId("workflow-run").ClickAsync();

        await Expect(session.Page.GetByTestId("workflow-output"))
            .ToContainTextAsync("spoken by alloy at -14 LUFS", new() { UseInnerText = true });
    }

    /// <summary>
    /// Settings the function's type cannot hold are refused on save, and the console shows the server's answer.
    /// </summary>
    [Fact]
    public async Task A_misspelled_settings_member_is_refused_and_the_servers_answer_is_shown()
    {
        await using var host = await UiHost.StartAsync(configureTracon: static tracon => tracon
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, SpeakSettings>(
                "speak",
                static _ => static (messages, _, _, _) => ValueTask.FromResult(messages)));
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/new");

        await session.Page.GetByTestId("workflow-name").FillAsync("narrated");
        await session.Page.GetByTestId("workflow-add-agent").SelectOptionAsync("support");
        await session.Page.GetByTestId("workflow-add-function").SelectOptionAsync("speak");
        await session.Page.GetByTestId("workflow-settings-speak").FillAsync("""{"voise":"alloy"}""");
        await session.Page.GetByTestId("workflow-save").ClickAsync();

        // The answer names the node and the member; the console shows it as the server wrote it.
        await Expect(session.Page.GetByText("Function node 'speak' has invalid settings").First).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("'voise'").First).ToBeVisibleAsync();

        // Refused means not stored, and the editor is still there to fix it.
        (await api.GetAsync("api/workflows/narrated")).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);

        await session.Page.GetByTestId("workflow-settings-speak").FillAsync("""{"voice":"alloy"}""");
        await session.Page.GetByTestId("workflow-save").ClickAsync();

        await Expect(session.Page).ToHaveURLAsync($"{host.UiAddress}/workflows/narrated");
    }

    /// <summary>
    /// A step timeout typed in the console is saved on its node and stops the run there.
    /// </summary>
    /// <remarks>
    /// Phase 196 (F-303, F-405). The field is offered only where the chain is saved as a node
    /// list - a chain with a function step - since a step timeout never changes the form
    /// (K-901). The value crosses the editor's seconds-to-<c>hh:mm:ss</c> conversion, the save,
    /// the stored node, and the wrapper that cancels the function when the run compiles.
    /// </remarks>
    [Fact]
    public async Task A_step_timeout_typed_in_the_console_is_saved_on_its_node_and_stops_the_run()
    {
        await using var host = await UiHost.StartAsync(configureTracon: static tracon => tracon
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "slow",
                static _ => static async (messages, _, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

                    return messages;
                }));
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/new");

        await session.Page.GetByTestId("workflow-name").FillAsync("bounded");
        await session.Page.GetByTestId("workflow-add-agent").SelectOptionAsync("support");

        // An agent-only chain is saved as an agent list, which has no node to keep a timeout on.
        await Expect(session.Page.GetByTestId("workflow-timeout-support")).ToHaveCountAsync(0);

        await session.Page.GetByTestId("workflow-add-function").SelectOptionAsync("slow");
        await session.Page.GetByTestId("workflow-timeout-slow").FillAsync("1");
        await session.Page.GetByTestId("workflow-save").ClickAsync();

        await Expect(session.Page).ToHaveURLAsync($"{host.UiAddress}/workflows/bounded");

        var saved = await api.GetFromJsonAsync<JsonElement>("api/workflows/bounded");

        saved.GetProperty("nodes")[1].GetProperty("timeout").GetString().ShouldBe("00:00:01");

        await session.Page.GetByTestId("workflow-message").FillAsync("hello");
        await session.Page.GetByTestId("workflow-run").ClickAsync();

        var runs = await WaitUntil.ValueAsync(
            async () => (await api.GetFromJsonAsync<JsonElement>("api/runs?agentName=bounded")).EnumerateArray().ToList(),
            static found => found.Count == 1 && !string.Equals(found[0].GetProperty("status").GetString(), "Running", StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(30));

        runs[0].GetProperty("status").GetString().ShouldBe("Failed");
        runs[0].GetProperty("error").GetProperty("type").GetString().ShouldBe("workflow_function_timeout");
    }

    internal sealed record SpeakSettings
    {
        public required string Voice { get; init; }

        public double LoudnessLufs { get; init; } = -16;
    }

    [Fact]
    public async Task Pending_request_card_can_be_answered()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow");

        await Expect(session.Page.GetByTestId("workflow-graph")).ToBeVisibleAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("publish the report");
        await session.Page.GetByTestId("workflow-run").ClickAsync();

        // The run pauses waiting for a human response; the card appears then.
        var card = session.Page.GetByTestId("workflow-pending-request");

        await Expect(card).ToBeVisibleAsync();

        await Expect(card).ToContainTextAsync("publish the report", new() { UseInnerText = true });

        await session.Page.GetByTestId("workflow-approve").ClickAsync();

        // The response opens a NEW run and produces the graph output.
        var output = session.Page.GetByTestId("workflow-output");

        await Expect(output).ToBeVisibleAsync();

        await Expect(output).ToContainTextAsync("approved", new() { UseInnerText = true });
    }

    /// <summary>
    /// A tool that waits for approval inside a workflow is answered from the same card (phase 230).
    /// </summary>
    /// <remarks>
    /// The card names the tool and what the approval presenter says about the call, takes a reason the
    /// agent reads, and can save the approval as a rule. The rule is checked through the API: the card is only
    /// proven if the rule it asked for exists.
    /// </remarks>
    [Fact]
    public async Task A_tool_approval_card_approves_with_a_reason_and_remembers_the_rule()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/tool-approval-flow");

        await Expect(session.Page.GetByTestId("workflow-graph")).ToBeVisibleAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("cancel order 7");
        await session.Page.GetByTestId("workflow-run").ClickAsync();

        var card = session.Page.GetByTestId("workflow-pending-request");

        await Expect(card).ToBeVisibleAsync();
        await Expect(card).ToContainTextAsync("tool approval", new() { UseInnerText = true });
        await Expect(card).ToContainTextAsync("cancel_order", new() { UseInnerText = true });
        await Expect(card).ToContainTextAsync("Cancel order ORD-7 for Jordan Rivera.", new() { UseInnerText = true });

        await session.Page.GetByTestId("workflow-tool-reason").FillAsync("verified with the customer");
        await session.Page.GetByTestId("workflow-remember").CheckAsync();
        await session.Page.GetByTestId("workflow-approve").ClickAsync();

        // The answer opens a NEW run and the agent finishes the tool call.
        await Expect(session.Page.GetByTestId("workflow-output"))
            .ToContainTextAsync("has been canceled", new() { UseInnerText = true });

        var rules = await api.GetFromJsonAsync<JsonElement>("api/approvals/rules");
        var rule = rules.EnumerateArray().ShouldHaveSingleItem();

        rule.GetProperty("toolName").GetString().ShouldBe("cancel_order");
        rule.GetProperty("agentName").GetString().ShouldBe("approval-agent");
    }

    /// <summary>
    /// A run started outside the console is answered from its own run page.
    /// </summary>
    /// <remarks>
    /// F-335: the run page sent the operator to "the workflow screen", but that
    /// screen only showed a request for a run it had started itself. A run
    /// started over HTTP, by a job or in another tab had no card anywhere, so
    /// the console could not answer it at all.
    /// </remarks>
    [Fact]
    public async Task Pending_request_of_a_run_started_elsewhere_opens_from_its_run_page()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        var runId = await StartApprovalRunAsync(api);

        await session.Page.GotoAsync($"{host.UiAddress}/runs/{runId}");
        await session.Page.GetByRole(AriaRole.Link, new() { Name = "workflow screen", Exact = true }).ClickAsync();

        var card = session.Page.GetByTestId("workflow-pending-request");

        await Expect(card).ToBeVisibleAsync();
        await Expect(card).ToContainTextAsync("publish the report", new() { UseInnerText = true });

        await session.Page.GetByTestId("workflow-approve").ClickAsync();

        var output = session.Page.GetByTestId("workflow-output");

        await Expect(output).ToContainTextAsync("approved", new() { UseInnerText = true });

        // The answer opened a NEW run (K-130), and the address follows it, so a
        // reload shows the run the screen is now about — not the answered one.
        await Expect(session.Page).ToHaveURLAsync(new Regex(
            $@"/workflows/approval-flow\?runId=(?!{runId})[0-9a-f-]{{36}}$",
            RegexOptions.None,
            TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// The address alone is enough to reach the request: a bookmark, a reload or
    /// a link pasted into a ticket.
    /// </summary>
    [Fact]
    public async Task Pending_request_opens_from_a_link_that_names_its_run()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        var runId = await StartApprovalRunAsync(api);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow?runId={runId}");

        var card = session.Page.GetByTestId("workflow-pending-request");

        await Expect(card).ToBeVisibleAsync();
        await Expect(card).ToContainTextAsync("publish the report", new() { UseInnerText = true });
    }

    /// <summary>
    /// A run named in the address that this screen cannot answer gets a note,
    /// never a card.
    /// </summary>
    /// <remarks>
    /// The request list endpoint answers for any workflow run, so a card for
    /// another workflow's run would send the answer to a graph the operator is
    /// not looking at.
    /// </remarks>
    [Fact]
    public async Task A_run_this_screen_cannot_answer_shows_a_note_instead_of_a_card()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        var waiting = await StartApprovalRunAsync(api);
        var completed = await ApproveAsync(api, waiting);

        var notice = session.Page.GetByTestId("workflow-run-notice");
        var card = session.Page.GetByTestId("workflow-pending-request");

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/summarize-and-translate?runId={waiting}");

        await Expect(notice).ToContainTextAsync("not a run of this workflow", new() { UseInnerText = true });
        await Expect(card).ToHaveCountAsync(0);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow?runId={completed}");

        await Expect(notice).ToContainTextAsync("not waiting for input", new() { UseInnerText = true });
        await Expect(card).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Pending_run_appears_with_distinct_status_in_Runs_list()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow");
        await Expect(session.Page.GetByTestId("workflow-graph")).ToBeVisibleAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("publish the report");
        await session.Page.GetByTestId("workflow-run").ClickAsync();
        await Expect(session.Page.GetByTestId("workflow-pending-request")).ToBeVisibleAsync();

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // Neither completed nor failed: it has its own status.
        //
        // 🚨 Exact is required: the status FILTER also has a hidden
        // <option>Awaiting input</option>, and a substring match would find
        // that first and wait for it to be visible. The badge is lowercase,
        // the option is capitalized; Exact tells them apart.
        await Expect(session.Page
            .GetByText("awaiting input", new() { Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Starts approval-flow over HTTP — outside the console — and returns its run id.
    /// </summary>
    /// <remarks>
    /// The stream closes when the run stops at the request port, so reading it
    /// to the end is the wait: the run is <c>AwaitingInput</c> afterwards.
    /// </remarks>
    private static async Task<string> StartApprovalRunAsync(HttpClient api)
    {
        using var response = await api.PostAsJsonAsync(
            "api/workflows/approval-flow/run",
            new { message = "publish the report" });

        response.EnsureSuccessStatusCode();

        return ReadRunId(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Approves the run's request over HTTP and returns the run that continues it.</summary>
    /// <summary>
    /// A workflow rolled back in the console runs the old content (phase 200).
    /// </summary>
    /// <remarks>
    /// The whole path crosses every layer the unit tests stand apart from: the history list, the
    /// rollback call, the stored current definition, and the next run's compile and stamp.
    /// </remarks>
    [Fact]
    public async Task A_workflow_rolled_back_in_the_console_runs_the_old_content()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        foreach (var (description, agents) in new[]
        {
            ("one agent", new[] { "support" }),
            ("two agents", new[] { "support", "router" }),
        })
        {
            using var saved = await api.PutAsJsonAsync(
                "api/workflows/rollbackable",
                new { kind = "Sequential", description, agentNames = agents });

            saved.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/rollbackable");

        await Expect(session.Page.GetByTestId("version-checkbox-1")).ToBeVisibleAsync();
        await Expect(session.Page.GetByTestId("version-checkbox-2")).ToBeVisibleAsync();

        // The current version offers no rollback; the old one does.
        await Expect(session.Page.GetByTestId("version-rollback-2")).ToHaveCountAsync(0);
        await session.Page.GetByTestId("version-rollback-1").ClickAsync();

        // The rollback is a NEW version, and the screen shows it.
        await Expect(session.Page.GetByTestId("version-checkbox-3")).ToBeVisibleAsync();

        var current = await api.GetFromJsonAsync<JsonElement>("api/workflows/rollbackable");

        current.GetProperty("version").GetInt32().ShouldBe(3);
        current.GetProperty("description").GetString().ShouldBe("one agent");
        current.GetProperty("agentNames").GetArrayLength().ShouldBe(1);

        // The next run is the old chain, stamped with the rollback's version.
        using var run = await api.PostAsJsonAsync("api/workflows/rollbackable/run", new { message = "hello" });

        run.EnsureSuccessStatusCode();

        var runId = ReadRunId(await run.Content.ReadAsStringAsync());
        var tree = await api.GetFromJsonAsync<JsonElement>($"api/runs/{runId}/tree");
        var rows = tree.EnumerateArray().ToList();

        rows.Count(static row => string.Equals(row.GetProperty("kind").GetString(), "Agent", StringComparison.Ordinal)).ShouldBe(1);
        rows.Single(static row => string.Equals(row.GetProperty("kind").GetString(), "Workflow", StringComparison.Ordinal))
            .GetProperty("agentVersion").GetInt32().ShouldBe(3);
    }

    /// <summary>
    /// A step pinned in the editor is saved in the node form and runs the pinned version of its agent (phase 200).
    /// </summary>
    [Fact]
    public async Task A_step_pinned_in_the_console_is_saved_as_a_node_and_runs_the_pinned_version()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        // A stored agent with two versions: only a stored agent keeps versions.
        foreach (var (create, instructions) in new[] { (true, "first text"), (false, "second text") })
        {
            using var request = new HttpRequestMessage(
                create ? HttpMethod.Post : HttpMethod.Put,
                create ? "api/agents" : "api/agents/scribe")
            {
                Content = JsonContent.Create(new
                {
                    name = "scribe",
                    instructions,
                    model = new { provider = ScriptedModels.ProviderName, model = ScriptedModels.Default },
                }),
            };

            using var response = await api.SendAsync(request);

            response.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/new");

        await session.Page.GetByTestId("workflow-name").FillAsync("pinned");
        await session.Page.GetByTestId("workflow-add-agent").SelectOptionAsync("scribe");

        // A code agent has no versions to choose from; a stored one does.
        await Expect(session.Page.GetByTestId("workflow-version-scribe")).ToBeVisibleAsync();
        await session.Page.GetByTestId("workflow-version-scribe").SelectOptionAsync("1");
        await session.Page.GetByTestId("workflow-save").ClickAsync();

        await Expect(session.Page).ToHaveURLAsync($"{host.UiAddress}/workflows/pinned");

        // One pinned agent step is written as a node list: the agent list has nowhere to keep the pin.
        var saved = await api.GetFromJsonAsync<JsonElement>("api/workflows/pinned");

        saved.GetProperty("agentNames").GetArrayLength().ShouldBe(0);
        saved.GetProperty("nodes")[0].GetProperty("name").GetString().ShouldBe("scribe");
        saved.GetProperty("nodes")[0].GetProperty("agentVersion").GetInt32().ShouldBe(1);

        using var run = await api.PostAsJsonAsync("api/workflows/pinned/run", new { message = "hello" });

        run.EnsureSuccessStatusCode();

        var tree = await api.GetFromJsonAsync<JsonElement>($"api/runs/{ReadRunId(await run.Content.ReadAsStringAsync())}/tree");

        tree.EnumerateArray()
            .Single(static row => string.Equals(row.GetProperty("kind").GetString(), "Agent", StringComparison.Ordinal))
            .GetProperty("agentVersion").GetInt32().ShouldBe(1, "the agent is at version 2; the step runs the version it pins");
    }

    /// <summary>A code agent offers no version choice: it keeps no history to pin.</summary>
    [Fact]
    public async Task An_agent_defined_in_code_offers_no_version_choice_in_the_editor()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/new");

        await session.Page.GetByTestId("workflow-name").FillAsync("plain");
        await session.Page.GetByTestId("workflow-add-agent").SelectOptionAsync("support");

        await Expect(session.Page.GetByTestId("workflow-step")).ToHaveCountAsync(1);
        await Expect(session.Page.GetByTestId("workflow-version-support")).ToHaveCountAsync(0);
    }

    private static async Task<string> ApproveAsync(HttpClient api, string runId)
    {
        var requests = await api.GetFromJsonAsync<JsonElement>($"api/workflows/runs/{runId}/requests");
        var requestId = requests[0].GetProperty("requestId").GetString();

        using var response = await api.PostAsJsonAsync(
            $"api/workflows/runs/{runId}/respond",
            new { requestId, approved = true });

        response.EnsureSuccessStatusCode();

        return ReadRunId(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Reads the run id from a workflow stream's first frame, <c>event: run</c>.</summary>
    private static string ReadRunId(string stream)
    {
        var frame = stream
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .First(candidate => candidate.Contains("event: run\n", StringComparison.Ordinal));
        var data = frame
            .Split('\n')
            .First(line => line.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];

        using var document = JsonDocument.Parse(data);

        return document.RootElement.GetProperty("runId").GetString()
            ?? throw new InvalidOperationException("The run frame carries no run id.");
    }
}
