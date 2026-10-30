using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Evaluation;

/// <summary>
/// What <see cref="RunToCasePromoter"/> does with a recorded input an eval case
/// cannot carry.
/// </summary>
/// <remarks>
/// A case carries a query, a context and parameter values. Anything else in the
/// run's input - an attachment, a second turn - used to be dropped without a
/// word: the case then measured a different input than the run it stood for.
/// The HTTP mapping of the refusal is covered by <c>EvalCaseInputFidelityTests</c>.
/// </remarks>
public sealed class RunToCasePromoterTests
{
    private const string Tenant = "tenant";

    [Fact]
    public async Task A_run_whose_request_carried_an_attachment_is_refused()
    {
        var harness = await Harness.CreateAsync();

        var runId = await harness.SeedRunAsync(
            "What is in this picture?",
            [
                new ChatMessage(
                    ChatRole.User,
                    [new TextContent("What is in this picture?"), new UriContent(new Uri("https://example.test/tracon/api/attachments/0193"), "image/png")]),
            ]);

        var outcome = await harness.Promoter.PromoteAsync(Tenant, harness.Suite, runId, sourceKindOverride: null);

        outcome.Status.ShouldBe(RunPromotionStatus.UnsupportedInput);
        outcome.Detail.ShouldNotBeNull();
        outcome.Detail.ShouldContain("1 non-text content item(s)", Case.Sensitive);
        (await harness.Evals.ListCasesAsync(harness.Suite.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_plain_recorded_run_is_still_promoted()
    {
        // The control for the two refusals: the harness itself promotes.
        var harness = await Harness.CreateAsync();

        var runId = await harness.SeedRunAsync("Where is order 4182?", [new ChatMessage(ChatRole.User, "Where is order 4182?")]);

        var outcome = await harness.Promoter.PromoteAsync(Tenant, harness.Suite, runId, sourceKindOverride: null);

        outcome.Status.ShouldBe(RunPromotionStatus.Created);
        outcome.Case!.Query.ShouldBe("Where is order 4182?");
        outcome.Case.Context.ShouldBeNull();
    }

    /// <summary>
    /// A reference run stands for the tools it called, by name. The
    /// arguments of one run belong to that run; a case that demanded them would fail on the
    /// next run's own values.
    /// </summary>
    /// <returns>The test task.</returns>
    [Fact]
    public async Task A_reference_run_is_promoted_with_each_tool_it_called_once_and_without_arguments()
    {
        var harness = await Harness.CreateAsync();

        var runId = await harness.SeedRunAsync("Where is order 4182?", [new ChatMessage(ChatRole.User, "Where is order 4182?")]);

        foreach (var (name, arguments) in new[]
                 {
                     ("get_order_status", "orderId=4182"),
                     ("get_shipping_status", "orderId=4182"),
                     ("get_order_status", "orderId=4183"),
                 })
        {
            await harness.Runs.RecordToolInvocationAsync(new ToolInvocationRecord
            {
                Id = TraconId.NewId(),
                RunId = runId,
                ToolName = name,
                Arguments = arguments,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        var outcome = await harness.Promoter.PromoteAsync(Tenant, harness.Suite, runId, sourceKindOverride: EvalCaseSource.ReferenceRun);

        outcome.Status.ShouldBe(RunPromotionStatus.Created);

        var calls = outcome.Case!.ExpectedToolCalls;

        calls.Select(static call => call.Name).OrderBy(static name => name, StringComparer.Ordinal)
            .ShouldBe(["get_order_status", "get_shipping_status"]);
        calls.ShouldAllBe(static call => call.Arguments == null);
    }

    [Fact]
    public async Task A_failed_run_is_promoted_without_expected_tool_calls()
    {
        var harness = await Harness.CreateAsync();

        var runId = await harness.SeedRunAsync("Where is order 4182?", [new ChatMessage(ChatRole.User, "Where is order 4182?")]);

        await harness.Runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            ToolName = "get_order_status",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var outcome = await harness.Promoter.PromoteAsync(Tenant, harness.Suite, runId, sourceKindOverride: EvalCaseSource.FailedRun);

        outcome.Status.ShouldBe(RunPromotionStatus.Created);
        outcome.Case!.ExpectedToolCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_whose_input_carried_more_than_one_turn_is_refused()
    {
        // A client can send a whole conversation as one run's input (no
        // session, so the earlier-run check never sees it); its first user
        // message alone cannot repeat the run.
        var harness = await Harness.CreateAsync();

        var runId = await harness.SeedRunAsync(
            "What did I order?",
            [
                new ChatMessage(ChatRole.User, "What did I order?"),
                new ChatMessage(ChatRole.Assistant, "A blue kettle."),
                new ChatMessage(ChatRole.User, "When does it arrive?"),
            ]);

        var outcome = await harness.Promoter.PromoteAsync(Tenant, harness.Suite, runId, sourceKindOverride: null);

        outcome.Status.ShouldBe(RunPromotionStatus.UnsupportedInput);
        outcome.Detail.ShouldNotBeNull();
        outcome.Detail.ShouldContain("2 message(s) besides its documents and its request", Case.Sensitive);
        (await harness.Evals.ListCasesAsync(harness.Suite.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_document_whose_marker_a_content_guard_dropped_still_goes_to_the_context()
    {
        // A guard that masks a document's text builds a new TextContent without
        // the document channel marker (ContentGuardMessageMasker.WriteText).
        // The recorded block text survives; read as the request, it would have
        // made the document the case's query.
        var harness = await Harness.CreateAsync();

        var document = DocumentChannelMessageBuilder.Build(new AgentRunDocument { Name = "policy.md", Content = "Refunds within 30 days." });
        var unmarked = new ChatMessage(ChatRole.User, document.Text);

        var runId = await harness.SeedRunAsync(document.Text, [unmarked, new ChatMessage(ChatRole.User, "Summarize the attached policy.")]);

        var outcome = await harness.Promoter.PromoteAsync(Tenant, harness.Suite, runId, sourceKindOverride: null);

        outcome.Status.ShouldBe(RunPromotionStatus.Created);
        outcome.Case!.Query.ShouldBe("Summarize the attached policy.");
        outcome.Case.Context.ShouldBe(document.Text);
        outcome.InputSource.ShouldBe(RunPromotionInputSource.RecordedInput);
    }

    [Fact]
    public async Task A_run_that_sent_only_documents_has_no_query()
    {
        var harness = await Harness.CreateAsync();

        var document = DocumentChannelMessageBuilder.Build(new AgentRunDocument { Name = "policy.md", Content = "Refunds within 30 days." });

        var runId = await harness.SeedRunAsync(runStartedText: null, [document]);

        var outcome = await harness.Promoter.PromoteAsync(Tenant, harness.Suite, runId, sourceKindOverride: null);

        outcome.Status.ShouldBe(RunPromotionStatus.NoQuery);
    }

    [Fact]
    public async Task A_record_read_back_from_json_keeps_its_documents_apart_from_its_request()
    {
        // The SQL stores' shape, without a database: the marker values come back
        // as JsonElement (RunToCasePromotionSqlTests covers the real tables).
        var harness = await Harness.CreateAsync();

        List<ChatMessage> sent =
        [
            DocumentChannelMessageBuilder.Build(new AgentRunDocument { Name = "policy.md", Content = "Refunds within 30 days." }),
            DocumentChannelMessageBuilder.Build(new AgentRunDocument { Name = "faq.md", Content = "Shipping takes 2 days." }),
            new ChatMessage(ChatRole.User, "Summarize both."),
        ];

        var readBack = JsonSerializer.Deserialize<List<ChatMessage>>(
            JsonSerializer.Serialize(sent, AIJsonUtilities.DefaultOptions),
            AIJsonUtilities.DefaultOptions)!;

        var runId = await harness.SeedRunAsync("Summarize both.", readBack);

        var outcome = await harness.Promoter.PromoteAsync(Tenant, harness.Suite, runId, sourceKindOverride: null);

        outcome.Status.ShouldBe(RunPromotionStatus.Created);
        outcome.Case!.Query.ShouldBe("Summarize both.");
        outcome.Case.Context.ShouldBe(sent[0].Text + "\n" + sent[1].Text);
    }

    private sealed class Harness
    {
        private Harness(InMemoryRunStore runs, InMemoryEvalStore evals, InMemoryRunInputStore inputs, EvalSuite suite)
        {
            Runs = runs;
            Evals = evals;
            Inputs = inputs;
            Suite = suite;
            Promoter = new RunToCasePromoter(runs, new InMemoryRunScoreStore(), evals, inputs);
        }

        public InMemoryRunStore Runs { get; }

        public InMemoryEvalStore Evals { get; }

        public InMemoryRunInputStore Inputs { get; }

        public EvalSuite Suite { get; }

        public RunToCasePromoter Promoter { get; }

        public static async Task<Harness> CreateAsync()
        {
            var evals = new InMemoryEvalStore();
            var suite = await evals.SaveSuiteAsync(new EvalSuite
            {
                TenantId = Tenant,
                Name = "suite",
                AgentName = "agent",
                Checks = JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement.Clone(),
            });

            // The run store filters reads by the ambient tenant; it has to be
            // the promoting tenant or the run reads as missing.
            return new Harness(
                new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant)),
                evals,
                new InMemoryRunInputStore(),
                suite);
        }

        /// <summary>Writes a completed run the way the recording wrapper would: row, RunStarted, input.</summary>
        public async Task<Guid> SeedRunAsync(string? runStartedText, IReadOnlyList<ChatMessage> input)
        {
            var runId = TraconId.NewId();
            var now = DateTimeOffset.UtcNow;

            await Runs.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = "agent", StartedAt = now, TenantId = Tenant });
            await Runs.AppendEventAsync(new RunEvent
            {
                RunId = runId,
                Sequence = 0,
                Type = RunEventType.RunStarted,
                Timestamp = now,
                Text = runStartedText,
                TenantId = Tenant,
            });
            await Runs.CompleteRunAsync(new RunCompletion { RunId = runId, Status = RunStatus.Completed, CompletedAt = now });
            await Inputs.SaveAsync(new RunInputRecord { RunId = runId, TenantId = Tenant, Messages = input, CreatedAt = now });

            return runId;
        }
    }
}
