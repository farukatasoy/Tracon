using System.Net.Http.Json;
using System.Text.Json;
using Tracon.Cli.FunctionalTests.Infrastructure;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Cli.FunctionalTests;

/// <summary>
/// <c>tracon eval --baseline</c> against a real, listening host (Phase 153):
/// the relative gate that catches a slide an absolute threshold cannot see.
/// </summary>
[Collection(nameof(CliTestGroup))]
public sealed class EvalBaselineGateTests
{
    private const string AgentName = "baseline-agent";
    private const string SuiteName = "baseline-suite";

    [Fact]
    public async Task A_regression_against_the_previous_run_exits_3_and_names_the_broken_case()
    {
        await using var host = await StartHostAsync();
        var caseIds = await CreateSuiteAsync(host, ("hello", "never echoed"));
        var suiteId = await SuiteIdAsync(host);

        // The baseline says this case used to pass; the run about to happen
        // fails it. Written through the store because the shipped HTTP surface
        // assigns a fresh case id on every edit, which would make an edited
        // case an Added/Removed pair rather than a regression.
        await SyntheticRunAsync(host, suiteId, (caseIds[0], true));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--baseline", "previous", "--max-regressions", "0",
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(3);
        result.StandardOutput.ShouldContain("1 regressed");
        result.StandardError.ShouldContain($"regressed: case {caseIds[0]}");
    }

    [Fact]
    public async Task No_regression_against_the_previous_run_exits_0()
    {
        await using var host = await StartHostAsync();
        var caseIds = await CreateSuiteAsync(host, ("hello", "hello"));
        var suiteId = await SuiteIdAsync(host);

        await SyntheticRunAsync(host, suiteId, (caseIds[0], true));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--baseline", "previous", "--max-regressions", "0",
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("0 regressed");
    }

    [Fact]
    public async Task A_case_added_to_the_suite_is_not_a_regression()
    {
        await using var host = await StartHostAsync();
        var caseIds = await CreateSuiteAsync(host, ("hello", "hello"), ("world", "never echoed"));
        var suiteId = await SuiteIdAsync(host);

        // The baseline only ever measured the first case. The second one fails
        // now, but it is new - a gate that calls that a regression cries wolf
        // every time someone extends a suite.
        await SyntheticRunAsync(host, suiteId, (caseIds[0], true));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--baseline", "previous", "--max-regressions", "0",
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("0 regressed");
        result.StandardOutput.ShouldContain("1 added");
    }

    [Fact]
    public async Task An_explicit_baseline_run_id_is_accepted()
    {
        await using var host = await StartHostAsync();
        var caseIds = await CreateSuiteAsync(host, ("hello", "never echoed"));
        var suiteId = await SuiteIdAsync(host);
        var baseline = await SyntheticRunAsync(host, suiteId, (caseIds[0], true));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--baseline", baseline.ToString(), "--max-regressions", "0",
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(3);
        result.StandardOutput.ShouldContain($"vs baseline {baseline}");
    }

    [Fact]
    public async Task A_suites_first_run_has_nothing_to_compare_and_does_not_fail_the_gate()
    {
        // 🚨 Failing here would teach the team to delete the flag rather than
        // fix a regression: a brand new suite has no history by definition.
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, ("hello", "never echoed"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--baseline", "previous", "--max-regressions", "0",
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(0);
        result.StandardError.ShouldContain("No earlier completed run");
    }

    [Fact]
    public async Task A_baseline_whose_details_retention_removed_exits_4_not_3()
    {
        // 4 and 3 are different faults with different fixes. Folding "the
        // history is gone" into "a case broke" would send someone hunting for a
        // regression that was never measured.
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, ("hello", "hello"));
        var suiteId = await SuiteIdAsync(host);
        var trimmed = await TrimmedRunAsync(host, suiteId);

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--baseline", trimmed.ToString(), "--max-regressions", "0",
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(4);
        result.StandardError.ShouldContain("409");
    }

    [Fact]
    public async Task Json_output_stays_a_single_parseable_document_with_a_baseline()
    {
        // 🚨 Under --json stdout is machine input. The baseline summary is
        // useful to a human but must not join the document.
        await using var host = await StartHostAsync();
        var caseIds = await CreateSuiteAsync(host, ("hello", "never echoed"));
        var suiteId = await SuiteIdAsync(host);
        await SyntheticRunAsync(host, suiteId, (caseIds[0], true));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName, "--json",
            "--baseline", "previous", "--max-regressions", "0",
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(3);
        Should.NotThrow(() => JsonDocument.Parse(result.StandardOutput).Dispose());
        result.StandardError.ShouldContain("1 regressed");
    }

    [Fact]
    public async Task Max_regressions_without_a_baseline_is_an_argument_error()
    {
        // Never a silent no-op: a pipeline would read the green exit code as
        // "no regressions" while nothing was ever compared.
        await using var host = await StartHostAsync();
        await CreateSuiteAsync(host, ("hello", "hello"));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--max-regressions", "0");

        result.ExitCode.ShouldBe(1);
        result.Combined.ShouldContain("--baseline");
    }

    [Fact]
    public async Task A_baseline_that_is_not_a_run_id_or_previous_is_an_argument_error()
    {
        await using var host = await StartHostAsync();

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--baseline", "yesterday");

        result.ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task The_absolute_gate_still_decides_first()
    {
        // --min-pass-rate failing is exit 3 whether or not a baseline is given;
        // the relative gate adds a check, it never relaxes one.
        await using var host = await StartHostAsync();
        var caseIds = await CreateSuiteAsync(host, ("hello", "never echoed"));
        var suiteId = await SuiteIdAsync(host);
        await SyntheticRunAsync(host, suiteId, (caseIds[0], false));

        var result = await CliRunner.RunAsync(
            "eval", "--url", host.BaseAddress.ToString(), "--suite", SuiteName,
            "--min-pass-rate", "1.0", "--baseline", "previous", "--max-regressions", "5",
            "--poll-interval", "0.1", "--timeout", "20");

        result.ExitCode.ShouldBe(3);
    }

    private static Task<RealHttpHost> StartHostAsync()
        => RealHttpHost.StartAsync(
            configureServices: static services => services.UseScheduling(options =>
                options.PollInterval = TimeSpan.FromMilliseconds(50)),
            configureTracon: static builder => builder
                .AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage())
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
                    Origin = AgentDefinitionOrigin.Code,
                }));

    private static async Task<IReadOnlyList<Guid>> CreateSuiteAsync(
        RealHttpHost host,
        params (string Query, string? ExpectedOutput)[] cases)
    {
        using var httpClient = new HttpClient { BaseAddress = host.BaseAddress };

        using var suiteResponse = await httpClient.PutAsJsonAsync(
            $"api/evals/{SuiteName}",
            new { agentName = AgentName, checks = new object[] { new { kind = "containsExpected" } } });
        suiteResponse.EnsureSuccessStatusCode();

        using var casesResponse = await httpClient.PutAsJsonAsync(
            $"api/evals/{SuiteName}/cases",
            cases.Select(static c => new { query = c.Query, expectedOutput = c.ExpectedOutput }).ToArray());
        casesResponse.EnsureSuccessStatusCode();

        var saved = await casesResponse.Content.ReadFromJsonAsync<JsonElement>();
        return [.. saved.EnumerateArray().Select(static c => c.GetProperty("id").GetGuid())];
    }

    private static async Task<Guid> SuiteIdAsync(RealHttpHost host)
    {
        var store = host.Services.GetRequiredService<IEvalStore>();
        var suite = await store.GetSuiteAsync("default", SuiteName);
        return suite!.Id;
    }

    /// <summary>Writes a completed run with the given outcomes, as an earlier CI run would have left it.</summary>
    private static async Task<Guid> SyntheticRunAsync(
        RealHttpHost host,
        Guid suiteId,
        params (Guid CaseId, bool Passed)[] results)
    {
        var store = host.Services.GetRequiredService<IEvalStore>();
        var run = await store.CreateRunAsync(NewRun(suiteId));

        foreach (var (caseId, passed) in results)
        {
            await store.RecordCaseResultAsync(new EvalCaseResult
            {
                Id = TraconId.NewId(),
                EvalRunId = run.Id,
                CaseId = caseId,
                RunId = TraconId.NewId(),
                Passed = passed,
                FailureReason = passed ? null : "check failed",
            });
        }

        var passedCount = results.Count(static result => result.Passed);
        await CompleteAsync(store, run.Id, results.Length, passedCount);
        return run.Id;
    }

    /// <summary>Writes a completed run that kept its summary but no per-case rows — what retention leaves behind.</summary>
    private static async Task<Guid> TrimmedRunAsync(RealHttpHost host, Guid suiteId)
    {
        var store = host.Services.GetRequiredService<IEvalStore>();
        var run = await store.CreateRunAsync(NewRun(suiteId));
        await CompleteAsync(store, run.Id, total: 3, passed: 3);
        return run.Id;
    }

    private static ValueTask CompleteAsync(IEvalStore store, Guid runId, int total, int passed)
        => store.CompleteRunAsync(new EvalRunCompletion
        {
            EvalRunId = runId,
            Status = EvalRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Total = total,
            Passed = passed,
            Failed = total - passed,
        });

    private static EvalRun NewRun(Guid suiteId) => new()
    {
        Id = TraconId.NewId(),
        TenantId = "default",
        SuiteId = suiteId,
        Status = EvalRunStatus.Pending,
        Total = 0,
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
    };
}
