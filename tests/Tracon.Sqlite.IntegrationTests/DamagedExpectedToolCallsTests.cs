using System.Text.Json;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A damaged <c>expected_tool_calls</c> cell. SQLite and SQL Server hold the column
/// as plain text, so a hand edit or a writer that changed the format can leave text that is not the
/// array the store writes (PostgreSQL's <c>jsonb</c> refuses it on write, so only the text providers can
/// be put in this state).
/// </summary>
/// <remarks>
/// The listing of a suite's cases must still work, the damaged case must not turn into one that asserts
/// nothing (the <c>toolCallArgsMatch</c> check passes a case with no expected call), and the cases next
/// to it must not change.
/// </remarks>
public sealed class DamagedExpectedToolCallsTests(SqliteSchemaFixture schema) : IClassFixture<SqliteSchemaFixture>
{
    [Fact]
    public async Task A_damaged_cell_makes_that_case_fail_and_leaves_the_listing_and_the_other_cases_alone()
    {
        await schema.ResetAsync();

        var evals = schema.Context.Evals;
        var suite = await evals.SaveSuiteAsync(new EvalSuite
        {
            TenantId = schema.Tenant.TenantId,
            Name = "damaged-cells",
            AgentName = "support",
            Checks = JsonDocument.Parse("""[{"kind":"toolCallArgsMatch"}]""").RootElement.Clone(),
        });

        var saved = await evals.ReplaceCasesAsync(
            suite.Id,
            [
                new EvalCase
                {
                    SuiteId = suite.Id,
                    Seq = 0,
                    Query = "intact",
                    ExpectedToolCalls = [new EvalExpectedToolCall { Name = "get_order_status" }],
                },
                new EvalCase
                {
                    SuiteId = suite.Id,
                    Seq = 1,
                    Query = "damaged",
                    ExpectedToolCalls = [new EvalExpectedToolCall { Name = "get_order_status" }],
                },
            ]);

        await schema.Context.ExecuteAsync(
            $"UPDATE {schema.Context.TablePrefix}eval_cases SET expected_tool_calls = '{{\"calls\": \"not the array\"}}' " +
            $"WHERE id = '{saved[1].Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant()}';");

        var listed = await evals.ListCasesAsync(suite.Id);

        listed.Count.ShouldBe(2);
        listed[0].ExpectedToolCalls.Select(static call => call.Name).ShouldBe(["get_order_status"]);
        listed[1].ExpectedToolCalls.Select(static call => call.Name).ShouldBe([ExpectedToolCallsCodec.UnreadableName]);

        // The list a client receives still serializes: the damaged case carries no default JsonElement.
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(listed));

        wire.RootElement[1].GetProperty("ExpectedToolCalls")[0].GetProperty("Name").GetString()
            .ShouldBe(ExpectedToolCallsCodec.UnreadableName);
    }
}
