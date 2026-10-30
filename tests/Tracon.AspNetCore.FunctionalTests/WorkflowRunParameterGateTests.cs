using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The workflow run endpoint refuses bad parameter values before the stream
/// opens, with the SAME body an agent run gives.
/// </summary>
/// <remarks>
/// Once the SSE headers are sent the status can no longer change, so
/// a missing value that only surfaced inside the stream would reach the caller
/// as a <c>200</c> with an error frame. The body must also match
/// <c>POST /api/agents/{name}/run</c>: a client that already handles
/// <c>missingParameters</c> must not learn a second shape.
/// </remarks>
public sealed class WorkflowRunParameterGateTests
{
    [Fact]
    public async Task A_missing_required_value_is_a_400_with_the_agent_run_body_and_no_model_call()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/podcast/run",
            new WorkflowRunHttpRequest { Message = "Episode 12" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("missingParameters").EnumerateArray().Select(static item => item.GetString())
            .ShouldBe(["tone"]);
        body.GetProperty("unknownParameters").GetArrayLength().ShouldBe(0);
        body.GetProperty("tooLongParameters").GetArrayLength().ShouldBe(0);
        provider.Requests.ShouldBeEmpty();

        // No run row at all: the refusal came before the stream.
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false }))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task An_undeclared_name_is_refused_and_named()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/podcast/run",
            new WorkflowRunHttpRequest
            {
                Message = "Episode 12",
                Parameters = WorkflowParameterBindingTests.Values(("tone", "formal"), ("tonne", "typo")),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("unknownParameters").EnumerateArray().Select(static item => item.GetString())
            .ShouldBe(["tonne"]);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_value_over_the_length_limit_is_refused()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        var tooLong = new string('x', host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>()
            .Value.MaxParameterValueLength + 1);

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/podcast/run",
            new WorkflowRunHttpRequest
            {
                Message = "Episode 12",
                Parameters = WorkflowParameterBindingTests.Values(("tone", tooLong)),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("tooLongParameters").EnumerateArray().Select(static item => item.GetString())
            .ShouldBe(["tone"]);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_object_is_the_same_as_no_values()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/podcast/run",
            new WorkflowRunHttpRequest
            {
                Message = "Episode 12",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("missingParameters").EnumerateArray().Select(static item => item.GetString())
            .ShouldBe(["tone"]);
    }

    [Fact]
    public async Task A_value_at_exactly_the_limit_is_accepted()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        var atLimit = new string(
            'x',
            host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>().Value.MaxParameterValueLength);

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", atLimit)),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
    }

    [Fact]
    public async Task A_code_defined_workflow_takes_no_parameters_and_says_so()
    {
        // A workflow registered in code carries no definition, hence no schema:
        // every value is unknown. No separate rule is needed.
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            static builder => builder.AddWorkflow(
                "coded",
                static services => Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder.BuildSequential(
                    "coded",
                    [services.GetWorkflowAgent("coded", "writer"), services.GetWorkflowAgent("coded", "editor")])));

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/coded/run",
            new WorkflowRunHttpRequest
            {
                Message = "Episode 12",
                Parameters = WorkflowParameterBindingTests.Values(("tone", "formal")),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("unknownParameters").EnumerateArray().Select(static item => item.GetString())
            .ShouldBe(["tone"]);
    }

    [Fact]
    public async Task The_saved_definition_and_the_listing_carry_the_schema()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        await WorkflowParameterBindingTests.SaveWorkflowAsync(host);

        using var read = await host.Client.GetAsync("/tracon/api/workflows/podcast");
        var definition = await TraconTestHost.ReadJsonAsync(read);

        definition.GetProperty("parameters").EnumerateArray()
            .Select(static item => item.GetProperty("name").GetString())
            .ShouldBe(["tone", "voice"]);

        using var list = await host.Client.GetAsync("/tracon/api/workflows");
        var listed = (await TraconTestHost.ReadJsonAsync(list)).EnumerateArray()
            .Single(static item => string.Equals(item.GetProperty("name").GetString(), "podcast", StringComparison.Ordinal));

        listed.GetProperty("parameters").EnumerateArray()
            .Select(static item => item.GetProperty("name").GetString())
            .ShouldBe(["tone", "voice"]);
    }

    [Fact]
    public async Task An_invalid_schema_name_is_refused_at_save_time()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        using var response = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/bad",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                AgentNames = ["writer"],
                Parameters =
                [
                    new AgentParameter { Name = "9lives", Kind = AgentParameterKind.Text },
                    new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text },
                    new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("'9lives'", Case.Sensitive);
        detail.ShouldContain("declared more than once", Case.Sensitive);
    }

    [Fact]
    public async Task Saving_a_participants_missing_parameter_is_NOT_refused_at_save_time()
    {
        // The participants live in the agent catalog, which can change after the
        // workflow is saved; coverage is checked when a run starts, not here.
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(provider);

        using var response = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/podcast",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                AgentNames = ["writer", "editor"],
                Parameters = [],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
