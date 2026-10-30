using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The <c>toolCallArgsMatch</c> check, from the case a client writes to the
/// result an eval run records.
/// </summary>
/// <remarks>
/// Every case goes through the real host: the cases endpoint, the eval job on the queue worker,
/// the agent run that calls the tool through <c>FakeModelProvider.CallsTool</c>, and the
/// result the run endpoint returns. A unit test of the comparison cannot show that the expected
/// calls survive the store, reach the evaluator and meet the arguments the model sent.
/// </remarks>
public sealed class EvalToolCallArgsTests
{
    private const string AgentName = "order-agent";
    private const string ToolName = "get_order_status";
    private const string Suite = "tool-call-suite";

    [Fact]
    public async Task A_call_with_the_expected_arguments_passes()
    {
        var result = await RunAsync(
            new { orderId = "ORD-1001" },
            """[{"name":"get_order_status","arguments":{"orderId":"ORD-1001"}}]""");

        result.GetProperty("passed").GetBoolean().ShouldBeTrue(result.GetRawText());
    }

    [Fact]
    public async Task A_call_with_another_argument_value_fails_and_the_reason_says_args_mismatch()
    {
        var result = await RunAsync(
            new { orderId = "ORD-1001" },
            """[{"name":"get_order_status","arguments":{"orderId":"ORD-9999"}}]""");

        result.GetProperty("passed").GetBoolean().ShouldBeFalse();

        var reason = result.GetProperty("failureReason").GetString().ShouldNotBeNull();

        reason.ShouldContain("tool_call_args_match", Case.Sensitive);
        reason.ShouldContain("get_order_status: args mismatch", Case.Sensitive);

        // The reason names the tool, never the argument values the model sent.
        reason.ShouldNotContain("ORD-1001", Case.Sensitive);
    }

    [Fact]
    public async Task A_number_the_model_sent_as_an_integer_matches_the_same_number_written_with_a_fraction()
    {
        var result = await RunAsync(
            new { orderId = "ORD-1001", quantity = 2 },
            """[{"name":"get_order_status","arguments":{"quantity":2.0}}]""");

        result.GetProperty("passed").GetBoolean().ShouldBeTrue(result.GetRawText());
    }

    [Fact]
    public async Task An_object_argument_matches_whatever_the_order_of_its_members()
    {
        // The shape a real provider hands over: a JsonElement, members in the model's own order.
        var address = JsonDocument.Parse("""{ "zip": "35000",  "city": "Izmir", "lines": ["a", "b"] }""").RootElement;

        var result = await RunAsync(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "ORD-1001", ["address"] = address },
            """[{"name":"get_order_status","arguments":{"address":{"city":"Izmir","lines":["a","b"],"zip":"35000"}}}]""");

        result.GetProperty("passed").GetBoolean().ShouldBeTrue(result.GetRawText());
    }

    [Fact]
    public async Task A_tool_the_model_did_not_call_fails()
    {
        var result = await RunAsync(
            new { orderId = "ORD-1001" },
            """[{"name":"cancel_order"}]""");

        result.GetProperty("passed").GetBoolean().ShouldBeFalse();
        result.GetProperty("failureReason").GetString().ShouldNotBeNull()
            .ShouldContain("cancel_order: not called", Case.Sensitive);
    }

    [Fact]
    public async Task An_expectation_without_arguments_needs_only_the_call()
    {
        var result = await RunAsync(new { orderId = "ORD-1001" }, """[{"name":"get_order_status"}]""");

        result.GetProperty("passed").GetBoolean().ShouldBeTrue(result.GetRawText());
    }

    [Fact]
    public async Task A_case_that_expects_no_tool_call_passes_the_check()
    {
        var result = await RunAsync(new { orderId = "ORD-1001" }, "[]");

        result.GetProperty("passed").GetBoolean().ShouldBeTrue(result.GetRawText());
    }

    [Fact]
    public async Task A_two_megabyte_argument_is_stored_and_listed_whole_because_the_endpoint_sets_no_size_limit_of_its_own()
    {
        // Measured for the plan's "huge JSON" question: a case's query, context and parameters have no
        // limit either, so the expected arguments get none; the host's request body limit applies.
        await using var host = await TraconTestHost.StartAsync();

        using (var suite = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}",
                   new EvalSuiteSaveRequest
                   {
                       AgentName = AgentName,
                       Checks = JsonDocument.Parse("""[{"kind":"toolCallArgsMatch"}]""").RootElement,
                   }))
        {
            suite.EnsureSuccessStatusCode();
        }

        var big = new string('x', 2 * 1024 * 1024);

        using (var saved = await host.Client.PutAsync(
                   new Uri($"/tracon/api/evals/{Suite}/cases", UriKind.Relative),
                   JsonContent.Create(new object[]
                   {
                       new
                       {
                           query = "q",
                           expectedToolCalls = new object[] { new { name = "t", arguments = new { blob = big } } },
                       },
                   })))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var listed = await host.Client.GetAsync(new Uri($"/tracon/api/evals/{Suite}/cases", UriKind.Relative));
        var cases = await TraconTestHost.ReadJsonAsync(listed);

        cases[0].GetProperty("expectedToolCalls")[0].GetProperty("arguments").GetProperty("blob").GetString()!.Length
            .ShouldBe(big.Length);
    }

    private static async Task<JsonElement> RunAsync(object modelArguments, string expectedToolCalls)
    {
        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                .AddModelProvider(new FakeModelProvider("order-provider")
                    .WithModel(new ModelDescriptor { Name = "order-model" })
                    .CallsTool(ToolName, modelArguments)
                    .RespondsWith("Your order has shipped."))
                .AddTool(AIFunctionFactory.Create(
                    static (string orderId) => $"{orderId}: shipped",
                    ToolName,
                    "Looks up the status of an order."))
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer order questions.",
                    Model = new ModelBinding { Provider = "order-provider", Model = "order-model" },
                    ToolNames = [ToolName],
                    Origin = AgentDefinitionOrigin.Code,
                });
                builder.UseScheduling(static options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                });
            });

        using (var suite = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/evals/{Suite}",
                   new EvalSuiteSaveRequest
                   {
                       AgentName = AgentName,
                       Checks = JsonDocument.Parse("""[{"kind":"toolCallArgsMatch"}]""").RootElement,
                   }))
        {
            suite.EnsureSuccessStatusCode();
        }

        using (var cases = await host.Client.PutAsync(
                   new Uri($"/tracon/api/evals/{Suite}/cases", UriKind.Relative),
                   new StringContent(
                       $$"""[{"query":"Where is order ORD-1001?","expectedToolCalls":{{expectedToolCalls}}}]""",
                       Encoding.UTF8,
                       "application/json")))
        {
            cases.StatusCode.ShouldBe(HttpStatusCode.OK, await cases.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync($"/tracon/api/evals/{Suite}/run", new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var evalRunId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        var detail = await WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync($"/tracon/api/evals/runs/{evalRunId}");

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetProperty("run").GetProperty("status").GetString() is "Completed" or "Failed",
            "the eval run to finish");

        return detail.GetProperty("results").EnumerateArray().ShouldHaveSingleItem();
    }
}
