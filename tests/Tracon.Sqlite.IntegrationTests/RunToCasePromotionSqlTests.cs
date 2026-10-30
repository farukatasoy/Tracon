using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Promoting a run to an eval case on a SQL provider.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The in-memory stores keep messages as objects; a SQL store keeps them as
/// JSON, and the document channel marker comes back as <see cref="JsonElement"/>
/// values (measured on this provider, 2026-10-12). A promotion that only works
/// on objects reads the document as the query on every SQL deployment - so the
/// read path is proven here, against the real <c>run_inputs</c> and
/// <c>eval_cases</c> tables.
/// </para>
/// </remarks>
public sealed class RunToCasePromotionSqlTests(SqliteSchemaFixture schema) : IClassFixture<SqliteSchemaFixture>
{
    [Fact]
    public async Task A_run_promoted_from_sql_keeps_its_whole_query_its_document_and_its_parameter_values()
    {
        await schema.ResetAsync();

        var tenant = schema.Tenant.TenantId;
        var context = schema.Context;
        var runId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;
        var query = string.Concat(Enumerable.Repeat("Summarize the attached policy for a new hire. ", 230));
        var document = DocumentChannelMessageBuilder.Build(new AgentRunDocument { Name = "policy.md", Content = "Refunds within 30 days." });

        await context.Runs.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = "billing-agent", StartedAt = now, TenantId = tenant });

        // What RunEventWriter writes for a query over MaxPayloadLength.
        await context.Runs.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.RunStarted,
            Timestamp = now,
            Text = string.Concat(query.AsSpan(0, 8 * 1024), "…[truncated]"),
            TenantId = tenant,
        });

        await context.Runs.CompleteRunAsync(new RunCompletion { RunId = runId, Status = RunStatus.Completed, CompletedAt = now });

        await context.RunInputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = tenant,
            Messages = [document, new ChatMessage(ChatRole.User, query)],
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" },
            CreatedAt = now,
        });

        var suite = await context.Evals.SaveSuiteAsync(new EvalSuite
        {
            TenantId = tenant,
            Name = "billing-suite",
            AgentName = "billing-agent",
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement.Clone(),
        });

        var promoter = new RunToCasePromoter(context.Runs, context.RunScores, context.Evals, context.RunInputs);

        var outcome = await promoter.PromoteAsync(tenant, suite, runId, sourceKindOverride: null);

        outcome.Status.ShouldBe(RunPromotionStatus.Created);

        // Read back from the table, not from the returned object: the context
        // and the values have to survive the eval_cases write too.
        var stored = (await context.Evals.ListCasesAsync(suite.Id)).ShouldHaveSingleItem();

        stored.Query.ShouldBe(query);
        stored.Context.ShouldBe(document.Text);
        stored.Parameters.ShouldNotBeNull();
        stored.Parameters["customer"].ShouldBe("Acme");
    }
}
