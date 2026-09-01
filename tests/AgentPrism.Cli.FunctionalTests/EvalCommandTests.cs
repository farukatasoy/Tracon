using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.Cli.Commands;
using AgentPrism.Cli.FunctionalTests.Infrastructure;
using AgentPrism.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Cli.FunctionalTests;

/// <summary>
/// <c>agentprism eval</c> against a real, listening AgentPrism host
/// (docs/arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md): triggers a suite through
/// <c>AgentPrism.Client</c>, polls it to completion through the background job
/// worker, and applies the CLI's own threshold gate.
/// </summary>
[Collection(nameof(CliTestGroup))]
public sealed class EvalCommandTests
{
    private const string AgentName = "eval-agent";
    private const string SuiteName = "eval-suite";

    /// <summary>Starts a host with a fast job worker and an echoing agent, ready to run a suite.</summary>
    private static Task<RealHttpHost> StartHostAsync(string agentName = AgentName)
        => RealHttpHost.StartAsync(
            configureServices: static services => services.UseScheduling(options =>
            {
                // The server's own job poll interval (default 10 s) would make
                // every test wait that long for the queued run to be picked up;
                // shortening it here is a test-only concern and has nothing to
                // do with the CLI's own --poll-interval.
                options.PollInterval = TimeSpan.FromMilliseconds(50);
            }),
            configureAgentPrism: builder => builder
                .AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage())
                .AddAgent(new AgentDefinition
                {
                    Name = agentName,
                    Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
                    Origin = AgentDefinitionOrigin.Code,
                }));

    /// <returns>The saved cases' identifiers, in the given order.</returns>
    private static async Task<IReadOnlyList<Guid>> CreateSuiteAsync(
        RealHttpHost host, string suiteName, string agentName, params (string Query, string? ExpectedOutput)[] cases)
    {
        using var httpClient = new HttpClient { BaseAddress = host.BaseAddress };

        using var suiteResponse = await httpClient.PutAsJsonAsync(
            $"api/evals/{suiteName}",
            new
            {
                agentName,
                checks = new object[] { new { kind = "containsExpected" } },
            });
        suiteResponse.EnsureSuccessStatusCode();

        using var casesResponse = await httpClient.PutAsJsonAsync(
            $"api/evals/{suiteName}/cases",
            cases.Select(c => new { query = c.Query, expectedOutput = c.ExpectedOutput }).ToArray());
        casesResponse.EnsureSuccessStatusCode();

        var saved = await casesResponse.Content.ReadFromJsonAsync<JsonElement>();
        return [.. saved.EnumerateArray().Select(static c => c.GetProperty("id").GetGuid())];
    }

    private static async Task<string> CreateApiKeyAsync(RealHttpHost host, string name, params string[] scopes)
    {
        using var httpClient = new HttpClient { BaseAddress = host.BaseAddress };

        using var response = await httpClient.PostAsJsonAsync(
            "api/api-keys",
            new { name, scopes });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("plaintextKey").GetString()!;
    }

    [Fact]
    public async Task A_suite_that_passes_its_threshold_exits_0()
    {
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "hello"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--min-pass-rate", "1.0", "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("1/1 passed");
    }

    [Fact]
    public async Task A_suite_that_misses_its_threshold_exits_3_and_identifies_the_failing_case()
    {
        // EvalCaseResult carries no case NAME, only CaseId (Guid) - deliberately,
        // so a later-edited or deleted case still leaves an intelligible past
        // result (EvalCaseResult.cs remarks). The CLI's own output promise is
        // therefore the case's identifier, not a human name; asserting the
        // actual Guid appears (not just the literal "FAILED case" substring)
        // is what makes this a real test of that promise.
        await using var host = await StartHostAsync();
        var caseIds = await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "this text never appears in the echo"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--min-pass-rate", "1.0", "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(3);
        result.StandardOutput.ShouldContain("0/1 passed");
        result.StandardOutput.ShouldContain($"FAILED case {caseIds[0]}");
    }

    [Fact]
    public async Task Max_failures_tolerates_one_failing_case()
    {
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "this text never appears in the echo"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--max-failures", "1", "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Both_thresholds_given_requires_both_to_hold()
    {
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "this text never appears in the echo"));

        // --max-failures alone would pass (0/1 <= 5); --min-pass-rate alone would
        // fail (0.0 < 1.0). Given together, AND (not OR) must decide the outcome.
        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--min-pass-rate", "1.0", "--max-failures", "5", "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(3);
    }

    [Fact]
    public async Task No_threshold_given_always_exits_0()
    {
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "this text never appears in the echo"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task A_suite_with_no_cases_cannot_be_run_at_all()
    {
        // The server itself refuses to queue a run for a case-less suite
        // (EvalEndpoints.TriggerRunAsync, 400 "has no cases") - a Total==0
        // COMPLETED run can therefore never come back through the normal
        // trigger path today. This is exit 2 ("could not run"), not 3 ("ran
        // but missed the gate"): the run never started. PassesThreshold's
        // Total==0 guard (115.3, open question 3) stays as defense for any
        // EvalRunDetailResponse a future server version might still produce
        // with Total==0, but this test documents the path actually reachable now.
        await using var host = await StartHostAsync();

        using var httpClient = new HttpClient { BaseAddress = host.BaseAddress };
        using var suiteResponse = await httpClient.PutAsJsonAsync(
            $"api/evals/{SuiteName}",
            new { agentName = AgentName, checks = new object[] { new { kind = "nonEmpty" } } });
        suiteResponse.EnsureSuccessStatusCode();

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName, "--min-pass-rate", "1.0");

        result.ExitCode.ShouldBe(2);
        result.Combined.ShouldContain("400");
    }

    [Fact]
    public async Task A_nonexistent_suite_exits_2_and_names_the_suite_without_a_server_body()
    {
        await using var host = await StartHostAsync();

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", "no-such-suite");

        result.ExitCode.ShouldBe(2);
        result.Combined.ShouldContain("no-such-suite");
    }

    [Fact]
    public async Task A_run_that_ends_Failed_exits_2_never_3()
    {
        // The suite's agent does not exist: EvalJobHandler.RunSuiteAsync
        // throws (AgentPrismException, "No agent named ... exists") and the
        // outer catch marks the run EvalRunStatus.Failed rather than
        // Completed - an infrastructure problem, not a quality signal, so it
        // must never be reported the same way as a missed --min-pass-rate.
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, SuiteName, "no-such-agent", ("hello", "hello"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(2);
        result.Combined.ShouldContain("Failed");
    }

    [Fact]
    public async Task A_server_that_is_not_running_fails_fast_instead_of_hanging()
    {
        var result = await CliRunner.RunAsync(
            "eval", "--url", "http://127.0.0.1:1/agentprism/", "--suite", "any-suite");

        result.ExitCode.ShouldBe(2);
    }

    [Fact]
    public async Task Timeout_expires_while_the_run_stays_pending()
    {
        // RunWorker: false keeps the job Pending forever, exercising the
        // "polling never sees a terminal state" branch deterministically.
        await using var host = await RealHttpHost.StartAsync(
            configureServices: static services => services.UseScheduling(options => options.RunWorker = false),
            configureAgentPrism: builder => builder
                .AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage())
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
                    Origin = AgentDefinitionOrigin.Code,
                }));
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "hello"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--timeout", "1", "--poll-interval", "0.1");

        result.ExitCode.ShouldBe(2);
        result.Combined.ShouldContain("Timed out");
    }

    [Fact]
    public async Task An_EvalsRead_only_key_cannot_trigger_and_the_message_names_RunsWrite()
    {
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "hello"));
        var key = await CreateApiKeyAsync(host, "reader", "EvalsRead");

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName, "--token", key);

        result.ExitCode.ShouldBe(2);
        result.Combined.ShouldContain("RunsWrite");
    }

    [Fact]
    public async Task A_RunsWrite_only_key_can_trigger_but_not_poll_and_the_message_names_EvalsRead()
    {
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "hello"));
        var key = await CreateApiKeyAsync(host, "writer", "RunsWrite");

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName, "--token", key,
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(2);
        result.Combined.ShouldContain("EvalsRead");
    }

    [Fact]
    public async Task Another_tenants_suite_is_read_as_not_found()
    {
        await using var host = await StartHostAsync();

        // Seeded directly under a tenant no request ever resolves to (host has
        // no tenancy configuration, so every HTTP request resolves to
        // "default") - the same isolation shape as a real cross-tenant probe,
        // without needing to also stand up header-based tenancy for the test.
        var evalStore = host.Services.GetRequiredService<IEvalStore>();
        var suite = await evalStore.SaveSuiteAsync(new EvalSuite
        {
            TenantId = "tenant-a",
            Name = SuiteName,
            AgentName = AgentName,
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement,
        });
        await evalStore.ReplaceCasesAsync(suite.Id, [new EvalCase { SuiteId = suite.Id, Seq = 0, Query = "hello" }]);

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName);

        result.ExitCode.ShouldBe(2);
        result.Combined.ShouldContain(SuiteName);
    }

    [Fact]
    public async Task Json_flag_produces_parseable_JSON_regardless_of_the_threshold_outcome()
    {
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "this text never appears in the echo"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--min-pass-rate", "1.0", "--poll-interval", "0.1", "--timeout", "20", "--json");

        result.ExitCode.ShouldBe(3);
        Should.NotThrow(() => JsonDocument.Parse(result.StandardOutput));
    }

    [Fact]
    public async Task An_out_of_range_min_pass_rate_is_a_usage_error()
    {
        var result = await CliRunner.RunAsync(
            "eval", "--url", "http://localhost:1/agentprism/", "--suite", "any-suite", "--min-pass-rate", "1.5");

        result.ExitCode.ShouldBe(1);
        result.Combined.ShouldContain("--min-pass-rate");
    }

    [Fact]
    public async Task A_negative_max_failures_is_a_usage_error()
    {
        var result = await CliRunner.RunAsync(
            "eval", "--url", "http://localhost:1/agentprism/", "--suite", "any-suite", "--max-failures", "-1");

        result.ExitCode.ShouldBe(1);
        result.Combined.ShouldContain("--max-failures");
    }

    [Fact]
    public async Task External_cancellation_breaks_the_poll_loop_immediately_instead_of_waiting_out_the_timeout()
    {
        // Simulates Ctrl+C (Program.cs wires CancelKeyPress into the same
        // token) without needing a real signal: the run stays Pending forever
        // (RunWorker: false), and the token is canceled long before the
        // generous --timeout would ever fire on its own.
        await using var host = await RealHttpHost.StartAsync(
            configureServices: static services => services.UseScheduling(options => options.RunWorker = false),
            configureAgentPrism: builder => builder
                .AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage())
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
                    Origin = AgentDefinitionOrigin.Code,
                }));
        await CreateSuiteAsync(host, SuiteName, AgentName, ("hello", "hello"));

        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var exitCode = await EvalCommand.RunAsync(
            [
                "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
                "--timeout", "300", "--poll-interval", "0.1",
            ],
            cancellation.Token);
        stopwatch.Stop();

        exitCode.ShouldBe(2);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }
}
