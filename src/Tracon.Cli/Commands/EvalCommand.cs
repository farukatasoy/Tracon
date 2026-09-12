using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Tracon.Client;
using Tracon.Client.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Cli.Commands;

/// <summary>
/// Triggers an eval suite over HTTP, polls it to completion, and turns the
/// result into an exit code a CI pipeline can gate on.
/// </summary>
/// <remarks>
/// <para>
/// The eval run is processed in the background: "trigger and exit" would
/// never actually measure anything, so polling is not an optional
/// convenience, it is what makes this a gate.
/// </para>
/// <para>
/// Types that also exist in Tracon.Abstractions (<c>EvalRun</c>,
/// <c>EvalRunStatus</c>, ...) are always written with the
/// <c>Tracon.Client.Generated</c> prefix in this file: this file's own
/// namespace nests inside "Tracon", so the compiler resolves an
/// unqualified name to the Abstractions type FIRST, before it ever considers
/// a using-alias - an unqualified reference here would silently bind to the
/// wrong assembly's type instead of failing to build.
/// </para>
/// </remarks>
internal static class EvalCommand
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    // How far back --baseline previous looks. Deep enough to step over a burst
    // of failed or cancelled runs, shallow enough to stay one request.
    private const int PreviousRunSearchWindow = 50;
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var url = CliArgs.RequireOption(args, "--url");
        var suiteName = CliArgs.RequireOption(args, "--suite");
        var token = CliArgs.GetOption(args, "--token") ?? Environment.GetEnvironmentVariable("TRACON_TOKEN");
        var asJson = CliArgs.HasFlag(args, "--json");
        var agentVersion = ParseOptionalInt(args, "--agent-version");
        var minPassRate = ParseOptionalDouble(args, "--min-pass-rate");
        var maxFailures = ParseOptionalInt(args, "--max-failures");
        var baseline = CliArgs.GetOption(args, "--baseline");
        var maxRegressions = ParseOptionalInt(args, "--max-regressions");
        var timeout = ParseOptionalSeconds(args, "--timeout") ?? DefaultTimeout;
        var pollInterval = ParseOptionalSeconds(args, "--poll-interval") ?? DefaultPollInterval;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseAddress))
        {
            throw new CliArgumentException("'--url' must be an absolute URL, for example http://localhost:5080/tracon.");
        }

        if (minPassRate is < 0 or > 1)
        {
            throw new CliArgumentException("'--min-pass-rate' must be between 0 and 1.");
        }

        if (maxFailures < 0)
        {
            throw new CliArgumentException("'--max-failures' must not be negative.");
        }

        if (maxRegressions < 0)
        {
            throw new CliArgumentException("'--max-regressions' must not be negative.");
        }

        // Silently ignoring the relative gate would be the worst outcome: the
        // pipeline would read a green exit code as "no regressions" while
        // nothing was ever compared.
        if (maxRegressions is not null && baseline is null)
        {
            throw new CliArgumentException("'--max-regressions' needs '--baseline <runId|previous>'; without a baseline there is nothing to compare against.");
        }

        Guid? explicitBaseline = null;

        if (baseline is not null && !string.Equals(baseline, "previous", StringComparison.Ordinal))
        {
            if (!Guid.TryParse(baseline, out var parsedBaseline))
            {
                throw new CliArgumentException("'--baseline' must be an eval run id, or the word 'previous'.");
            }

            explicitBaseline = parsedBaseline;
        }

        var services = new ServiceCollection();
        services.AddTraconClient(options =>
        {
            options.BaseAddress = baseAddress;
            options.Token = token;
        });

        // await using var x = ...; puts ConfigureAwait(false) out of reach for
        // the compiler-generated dispose call (MA0004); declaring the variable
        // first and wrapping usage in `await using (x.ConfigureAwait(false))`
        // keeps the concrete type usable. docs/hafiza/build-ve-analyzer.md.
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var client = provider.GetRequiredService<TraconApiClient>();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            Tracon.Client.Generated.EvalRun triggered;
            try
            {
                triggered = await client.TraconTriggerEvalRunAsync(
                    suiteName,
                    new Tracon.Client.Generated.EvalRunTriggerRequest { AgentVersion = agentVersion },
                    timeoutSource.Token).ConfigureAwait(false);
            }
            catch (TraconApiException ex) when (ex.StatusCode == 403)
            {
                // The trigger call is the ONLY step that needs RunsWrite (115.3): a
                // caller whose key carries only EvalsRead lands here, and the
                // response body is never echoed (K-059) so the scope name is stated
                // from the endpoint's own contract instead.
                Console.Error.WriteLine("Request failed: HTTP 403 (missing the 'RunsWrite' API key scope).");
                return 2;
            }
            catch (TraconApiException ex) when (ex.StatusCode == 404)
            {
                Console.Error.WriteLine($"Request failed: HTTP 404 (suite '{suiteName}' not found for this tenant).");
                return 2;
            }
            catch (TraconApiException ex)
            {
                Console.Error.WriteLine($"Request failed: HTTP {ex.StatusCode}.");
                return 2;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"Timed out after {timeout.TotalSeconds:0} s triggering suite '{suiteName}'.");
                return 2;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Canceled.");
                return 2;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Connection failed: {ex.Message}");
                return 2;
            }

            Tracon.Client.Generated.EvalRunDetailResponse detail;
            try
            {
                detail = await PollUntilTerminalAsync(client, triggered.Id, pollInterval, timeoutSource.Token)
                    .ConfigureAwait(false);
            }
            catch (TraconApiException ex) when (ex.StatusCode == 403)
            {
                // Polling is the ONLY step that needs EvalsRead: a key that carries
                // RunsWrite (enough to trigger) but not EvalsRead fails here instead.
                Console.Error.WriteLine("Request failed: HTTP 403 (missing the 'EvalsRead' API key scope).");
                return 2;
            }
            catch (TraconApiException ex)
            {
                Console.Error.WriteLine($"Request failed: HTTP {ex.StatusCode}.");
                return 2;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"Timed out after {timeout.TotalSeconds:0} s waiting for suite '{suiteName}' to finish.");
                return 2;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Canceled.");
                return 2;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Connection failed: {ex.Message}");
                return 2;
            }

            Print(detail, asJson);

            if (detail.Run.Status is Tracon.Client.Generated.EvalRunStatus.Failed
                or Tracon.Client.Generated.EvalRunStatus.Cancelled)
            {
                Console.Error.WriteLine($"Suite '{suiteName}' did not finish running (status: {detail.Run.Status}).");
                return 2;
            }

            if (!PassesThreshold(detail.Run, minPassRate, maxFailures))
            {
                return 3;
            }

            if (baseline is null)
            {
                return 0;
            }

            return await ApplyBaselineGateAsync(
                    client,
                    suiteName,
                    detail.Run,
                    explicitBaseline,
                    maxRegressions,
                    asJson,
                    timeoutSource.Token)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Judges the finished run against a baseline run of the same suite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The absolute gate cannot see a slide: with <c>--min-pass-rate 0.85</c>
    /// set, a drop from 95% to 90% passes. This gate compares case by case
    /// instead, and it separates the two things a pipeline must never confuse -
    /// "cases broke" (exit 3) and "the two runs could not be compared at all"
    /// (exit 4).
    /// </para>
    /// <para>
    /// With <c>--baseline</c> but no <c>--max-regressions</c>, the comparison is
    /// reported and never fails the build: the ceiling is what turns a report
    /// into a gate.
    /// </para>
    /// </remarks>
    private static async Task<int> ApplyBaselineGateAsync(
        TraconApiClient client,
        string suiteName,
        Tracon.Client.Generated.EvalRun run,
        Guid? explicitBaseline,
        int? maxRegressions,
        bool asJson,
        CancellationToken cancellationToken)
    {
        Guid baselineRunId;

        if (explicitBaseline is { } given)
        {
            baselineRunId = given;
        }
        else
        {
            var previous = await FindPreviousCompletedRunAsync(client, suiteName, run.Id, cancellationToken)
                .ConfigureAwait(false);

            if (previous is null)
            {
                // 🚨 Not a failure. A brand new suite has no history, and
                // failing its first CI run would teach the team to delete the
                // flag rather than to fix a regression.
                Console.Error.WriteLine(
                    $"No earlier completed run of suite '{suiteName}' to compare against; the relative gate was skipped.");
                return 0;
            }

            baselineRunId = previous.Value;
        }

        Tracon.Client.Generated.EvalRunDiff diff;

        try
        {
            diff = await client.TraconDiffEvalRunsAsync(run.Id, baselineRunId, skip: null, take: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TraconApiException ex) when (ex.StatusCode is 400 or 404 or 409)
        {
            // 4, not 3: "could not compare" is a different fault with a
            // different fix than "cases broke", and a pipeline that treats them
            // alike hides a lost history behind a familiar red. Only the three
            // codes the endpoint uses to REFUSE a comparison land here; an
            // auth or server fault is a 2 like every other transport problem.
            Console.Error.WriteLine(
                $"Could not compare run {run.Id} against baseline {baselineRunId}: HTTP {ex.StatusCode}.");
            return 4;
        }
        catch (TraconApiException ex)
        {
            Console.Error.WriteLine($"Request failed: HTTP {ex.StatusCode}.");
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled.");
            return 2;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Connection failed: {ex.Message}");
            return 2;
        }

        // 🚨 Under --json, stdout is ONE machine-readable document and nothing
        // else may join it; a second, human-shaped line there turns `| jq` into
        // a parse error. The summary still reaches the operator, on stderr.
        var summary =
            $"vs baseline {baselineRunId}: {diff.RegressedCount} regressed, {diff.FixedCount} fixed, " +
            $"{diff.AddedCount} added, {diff.RemovedCount} removed.";

        if (asJson)
        {
            Console.Error.WriteLine(summary);
        }
        else
        {
            Console.WriteLine(summary);
        }

        foreach (var entry in diff.Cases.Where(static entry => entry.Kind == Tracon.Client.Generated.EvalCaseDiffKind.Regressed))
        {
            Console.Error.WriteLine(
                $"  regressed: case {entry.CaseId}" +
                (string.IsNullOrEmpty(entry.CandidateFailureReason) ? string.Empty : $": {entry.CandidateFailureReason}"));
        }

        return maxRegressions is { } limit && diff.RegressedCount > limit ? 3 : 0;
    }

    /// <summary>
    /// Finds the newest completed run of the suite other than the one just
    /// made, which is what <c>--baseline previous</c> means.
    /// </summary>
    private static async Task<Guid?> FindPreviousCompletedRunAsync(
        TraconApiClient client,
        string suiteName,
        Guid currentRunId,
        CancellationToken cancellationToken)
    {
        // The list endpoint answers newest first, so the first completed entry
        // that is not the run we just made is the one wanted.
        var runs = await client.TraconListEvalRunsAsync(suiteName, skip: null, take: PreviousRunSearchWindow, cancellationToken)
            .ConfigureAwait(false);

        foreach (var candidate in runs)
        {
            if (candidate.Id != currentRunId &&
                candidate.Status == Tracon.Client.Generated.EvalRunStatus.Completed)
            {
                return candidate.Id;
            }
        }

        return null;
    }

    private static async Task<Tracon.Client.Generated.EvalRunDetailResponse> PollUntilTerminalAsync(
        TraconApiClient client, Guid runId, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        while (true)
        {
            var detail = await client.TraconGetEvalRunAsync(runId, cancellationToken).ConfigureAwait(false);

            if (detail.Run.Status is Tracon.Client.Generated.EvalRunStatus.Completed
                or Tracon.Client.Generated.EvalRunStatus.Failed
                or Tracon.Client.Generated.EvalRunStatus.Cancelled)
            {
                return detail;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A run with no threshold given always passes. An empty suite
    /// (<c>Total == 0</c>) fails a given <c>--min-pass-rate</c> rather than
    /// dividing by zero into a false "100% passed".
    /// </summary>
    private static bool PassesThreshold(Tracon.Client.Generated.EvalRun run, double? minPassRate, int? maxFailures)
    {
        var passesRate = minPassRate is not { } rate || (run.Total > 0 && (double)run.Passed / run.Total >= rate);
        var passesFailures = maxFailures is not { } max || run.Failed <= max;

        return passesRate && passesFailures;
    }

    private static void Print(Tracon.Client.Generated.EvalRunDetailResponse detail, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(detail, PrettyJson));
            return;
        }

        var run = detail.Run;
        var duration = run.CompletedAt is { } completedAt
            ? string.Create(CultureInfo.InvariantCulture, $" in {(completedAt - run.StartedAt).TotalSeconds:0.0} s")
            : string.Empty;

        Console.WriteLine($"{run.Status}: {run.Passed}/{run.Total} passed{duration}.");

        foreach (var result in detail.Results.Where(static result => !result.Passed))
        {
            var reason = string.IsNullOrEmpty(result.FailureReason) ? string.Empty : $": {result.FailureReason}";
            Console.WriteLine($"  FAILED case {result.CaseId}{reason}");
        }
    }

    private static int? ParseOptionalInt(IReadOnlyList<string> args, string name)
    {
        var raw = CliArgs.GetOption(args, name);

        if (raw is null)
        {
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new CliArgumentException($"'{name}' must be an integer, got '{raw}'.");
        }

        return value;
    }

    private static double? ParseOptionalDouble(IReadOnlyList<string> args, string name)
    {
        var raw = CliArgs.GetOption(args, name);

        if (raw is null)
        {
            return null;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new CliArgumentException($"'{name}' must be a number, got '{raw}'.");
        }

        return value;
    }

    private static TimeSpan? ParseOptionalSeconds(IReadOnlyList<string> args, string name)
    {
        var seconds = ParseOptionalDouble(args, name);

        if (seconds is null)
        {
            return null;
        }

        if (seconds <= 0)
        {
            throw new CliArgumentException($"'{name}' must be a positive number of seconds.");
        }

        return TimeSpan.FromSeconds(seconds.Value);
    }
}
