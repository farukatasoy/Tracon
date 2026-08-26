using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AgentPrism.Client;
using AgentPrism.Client.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Cli.Commands;

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
/// Types that also exist in AgentPrism.Abstractions (<c>EvalRun</c>,
/// <c>EvalRunStatus</c>, ...) are always written with the
/// <c>AgentPrism.Client.Generated</c> prefix in this file: this file's own
/// namespace nests inside "AgentPrism", so the compiler resolves an
/// unqualified name to the Abstractions type FIRST, before it ever considers
/// a using-alias - an unqualified reference here would silently bind to the
/// wrong assembly's type instead of failing to build.
/// </para>
/// </remarks>
internal static class EvalCommand
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var url = CliArgs.RequireOption(args, "--url");
        var suiteName = CliArgs.RequireOption(args, "--suite");
        var token = CliArgs.GetOption(args, "--token") ?? Environment.GetEnvironmentVariable("AGENTPRISM_TOKEN");
        var asJson = CliArgs.HasFlag(args, "--json");
        var agentVersion = ParseOptionalInt(args, "--agent-version");
        var minPassRate = ParseOptionalDouble(args, "--min-pass-rate");
        var maxFailures = ParseOptionalInt(args, "--max-failures");
        var timeout = ParseOptionalSeconds(args, "--timeout") ?? DefaultTimeout;
        var pollInterval = ParseOptionalSeconds(args, "--poll-interval") ?? DefaultPollInterval;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseAddress))
        {
            throw new CliArgumentException("'--url' must be an absolute URL, for example http://localhost:5080/agentprism.");
        }

        if (minPassRate is < 0 or > 1)
        {
            throw new CliArgumentException("'--min-pass-rate' must be between 0 and 1.");
        }

        if (maxFailures < 0)
        {
            throw new CliArgumentException("'--max-failures' must not be negative.");
        }

        var services = new ServiceCollection();
        services.AddAgentPrismClient(options =>
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
            var client = provider.GetRequiredService<AgentPrismApiClient>();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            AgentPrism.Client.Generated.EvalRun triggered;
            try
            {
                triggered = await client.AgentPrismTriggerEvalRunAsync(
                    suiteName,
                    new AgentPrism.Client.Generated.EvalRunTriggerRequest { AgentVersion = agentVersion },
                    timeoutSource.Token).ConfigureAwait(false);
            }
            catch (AgentPrismApiException ex) when (ex.StatusCode == 403)
            {
                // The trigger call is the ONLY step that needs RunsWrite (115.3): a
                // caller whose key carries only EvalsRead lands here, and the
                // response body is never echoed (K-059) so the scope name is stated
                // from the endpoint's own contract instead.
                Console.Error.WriteLine("Request failed: HTTP 403 (missing the 'RunsWrite' API key scope).");
                return 2;
            }
            catch (AgentPrismApiException ex) when (ex.StatusCode == 404)
            {
                Console.Error.WriteLine($"Request failed: HTTP 404 (suite '{suiteName}' not found for this tenant).");
                return 2;
            }
            catch (AgentPrismApiException ex)
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

            AgentPrism.Client.Generated.EvalRunDetailResponse detail;
            try
            {
                detail = await PollUntilTerminalAsync(client, triggered.Id, pollInterval, timeoutSource.Token)
                    .ConfigureAwait(false);
            }
            catch (AgentPrismApiException ex) when (ex.StatusCode == 403)
            {
                // Polling is the ONLY step that needs EvalsRead: a key that carries
                // RunsWrite (enough to trigger) but not EvalsRead fails here instead.
                Console.Error.WriteLine("Request failed: HTTP 403 (missing the 'EvalsRead' API key scope).");
                return 2;
            }
            catch (AgentPrismApiException ex)
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

            if (detail.Run.Status is AgentPrism.Client.Generated.EvalRunStatus.Failed
                or AgentPrism.Client.Generated.EvalRunStatus.Cancelled)
            {
                Console.Error.WriteLine($"Suite '{suiteName}' did not finish running (status: {detail.Run.Status}).");
                return 2;
            }

            return PassesThreshold(detail.Run, minPassRate, maxFailures) ? 0 : 3;
        }
    }

    private static async Task<AgentPrism.Client.Generated.EvalRunDetailResponse> PollUntilTerminalAsync(
        AgentPrismApiClient client, Guid runId, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        while (true)
        {
            var detail = await client.AgentPrismGetEvalRunAsync(runId, cancellationToken).ConfigureAwait(false);

            if (detail.Run.Status is AgentPrism.Client.Generated.EvalRunStatus.Completed
                or AgentPrism.Client.Generated.EvalRunStatus.Failed
                or AgentPrism.Client.Generated.EvalRunStatus.Cancelled)
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
    private static bool PassesThreshold(AgentPrism.Client.Generated.EvalRun run, double? minPassRate, int? maxFailures)
    {
        var passesRate = minPassRate is not { } rate || (run.Total > 0 && (double)run.Passed / run.Total >= rate);
        var passesFailures = maxFailures is not { } max || run.Failed <= max;

        return passesRate && passesFailures;
    }

    private static void Print(AgentPrism.Client.Generated.EvalRunDetailResponse detail, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(detail, PrettyJson));
            return;
        }

        var run = detail.Run;
        var duration = run.CompletedAt is { } completedAt
            ? $" in {(completedAt - run.StartedAt).TotalSeconds:0.0} s"
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
