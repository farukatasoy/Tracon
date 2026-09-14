using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tracon.Capacity;

namespace Tracon.CapacityDriver;

/// <summary>Drives the three HTTP shapes against a real host over loopback TCP.</summary>
/// <remarks>
/// <para>
/// 🚨 Every path here is the path a consumer's own client would take: a real
/// socket, a real status code, a real body. There is no in-process test server
/// and no shortcut into the host's services - the whole point of the separate
/// process is that no measurement can accidentally skip the HTTP or SQL layer.
/// </para>
/// </remarks>
public sealed class ScenarioClient
{
    private readonly HttpClient _client;
    private readonly CellSpec _spec;

    /// <summary>Creates a client bound to one cell's host and workload.</summary>
    /// <param name="client">The HTTP client, already pointed at the host.</param>
    /// <param name="spec">The cell under measurement.</param>
    public ScenarioClient(HttpClient client, CellSpec spec)
    {
        _client = client;
        _spec = spec;
    }

    private Uri RunUri => new($"{_spec.BaseAddress}/api/agents/{_spec.Agent}/run", UriKind.Absolute);

    /// <summary>Runs one request in the given scenario.</summary>
    /// <param name="scenario">One of the three scenario names.</param>
    /// <param name="tenant">The tenant the request belongs to.</param>
    /// <param name="correlation">The request's unique correlation value.</param>
    /// <param name="warmup">Whether this request belongs to the warm-up.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the request did.</returns>
    public async Task<RequestSample> ExecuteAsync(
        string scenario,
        string tenant,
        string correlation,
        bool warmup,
        CancellationToken cancellationToken)
    {
        var sample = new RequestSample
        {
            CellId = _spec.CellId,
            Scenario = scenario,
            Tenant = tenant,
            Correlation = correlation,
            Warmup = warmup,
            DispatchedUtc = DateTimeOffset.UtcNow,
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_spec.RequestTimeoutSeconds));

        var started = Stopwatch.GetTimestamp();

        try
        {
            switch (scenario)
            {
                case CapacityScenario.Buffered:
                    await BufferedAsync(sample, tenant, correlation, started, timeout.Token).ConfigureAwait(false);
                    break;
                case CapacityScenario.Streaming:
                    await StreamingAsync(sample, tenant, correlation, started, timeout.Token).ConfigureAwait(false);
                    break;
                case CapacityScenario.Queued:
                    await QueuedAsync(sample, tenant, correlation, started, timeout.Token).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The per-request budget expired. 🚨 This is NOT treated as a server
            // cancellation: the run may still be advancing, and the drain phase
            // follows it to a terminal status before the cell is summarised.
            sample.Outcome = RequestOutcome.TimedOut;
            sample.Error = "request timeout";
        }
        catch (OperationCanceledException)
        {
            sample.Outcome = RequestOutcome.Failed;
            sample.Error = "cell cancelled";
        }
        catch (HttpRequestException ex)
        {
            sample.Outcome = RequestOutcome.Failed;
            sample.Error = Redactor.Scrub(ex.Message);
        }
        catch (JsonException ex)
        {
            sample.Outcome = RequestOutcome.Failed;
            sample.Error = "response parse: " + Redactor.Scrub(ex.Message);
        }
        catch (IOException ex)
        {
            sample.Outcome = RequestOutcome.Failed;
            sample.Error = "transport: " + Redactor.Scrub(ex.Message);
        }

        sample.TotalMilliseconds ??= Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return sample;
    }

    private HttpRequestMessage CreateRunRequest(string tenant, string correlation)
    {
        var body = JsonSerializer.Serialize(
            new { message = CapacityPayload.RequestMessage(correlation, _spec.Workload.RequestBytes) });

        var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(CapacityContract.TenantHeader, tenant);
        return request;
    }

    private async Task BufferedAsync(
        RequestSample sample,
        string tenant,
        string correlation,
        long started,
        CancellationToken cancellationToken)
    {
        using var request = CreateRunRequest(tenant, correlation);

        // The idempotency header is what makes this scenario buffered (Phase 43):
        // its write cost belongs to this scenario's numbers, not to a footnote.
        request.Headers.TryAddWithoutValidation("Idempotency-Key", correlation);

        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        sample.HeadersMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        sample.StatusCode = (int)response.StatusCode;

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        sample.FirstContentMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        sample.TotalMilliseconds = sample.FirstContentMilliseconds;

        if (!response.IsSuccessStatusCode)
        {
            sample.Outcome = RequestOutcome.Rejected;
            sample.Error = Redactor.Scrub(Truncate(payload));
            return;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("runId", out var runId))
        {
            sample.RunId = runId.GetString();
        }

        sample.ContentMatched = AnswerMatches(ExtractAnswerText(root), correlation);
        sample.Outcome = RequestOutcome.Accepted;
    }

    private async Task StreamingAsync(
        RequestSample sample,
        string tenant,
        string correlation,
        long started,
        CancellationToken cancellationToken)
    {
        using var request = CreateRunRequest(tenant, correlation);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        sample.HeadersMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        sample.StatusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            sample.Outcome = RequestOutcome.Rejected;
            sample.Error = Redactor.Scrub(Truncate(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
            return;
        }

        var reader = new SseFrameReader(started);
        var assembled = new StringBuilder();
        var frames = 0;
        double? previous = null;
        double maximumGap = 0;
        string? failure = null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var frame in reader.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            frames++;

            if (previous is { } last)
            {
                maximumGap = Math.Max(maximumGap, frame.ElapsedMilliseconds - last);
            }

            previous = frame.ElapsedMilliseconds;

            switch (frame.EventName)
            {
                case "run":
                    sample.RunId = ReadProperty(frame.Data, "runId");
                    break;
                case "update":
                    sample.FirstContentMilliseconds ??= frame.ElapsedMilliseconds;
                    AppendUpdateText(assembled, frame.Data);
                    break;
                case "error":
                    failure = Redactor.Scrub(Truncate(frame.Data));
                    break;
                default:
                    break;
            }
        }

        sample.Frames = frames;
        sample.MaximumFrameGapMilliseconds = frames > 1 ? maximumGap : null;
        sample.TotalMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (reader.TruncatedFrame)
        {
            sample.Outcome = RequestOutcome.Failed;
            sample.Error = "stream ended mid-frame";
            return;
        }

        if (failure is not null)
        {
            sample.Outcome = RequestOutcome.Failed;
            sample.Error = failure;
            return;
        }

        sample.ContentMatched = AnswerMatches(assembled.ToString(), correlation);
        sample.Outcome = RequestOutcome.Accepted;
    }

    private async Task QueuedAsync(
        RequestSample sample,
        string tenant,
        string correlation,
        long started,
        CancellationToken cancellationToken)
    {
        using var request = CreateRunRequest(tenant, correlation);
        request.Headers.TryAddWithoutValidation("Prefer", "respond-async");

        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        sample.HeadersMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        sample.StatusCode = (int)response.StatusCode;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            sample.Outcome = response.IsSuccessStatusCode ? RequestOutcome.Failed : RequestOutcome.Rejected;
            sample.Error = Redactor.Scrub(Truncate(body));
            return;
        }

        using (var accepted = JsonDocument.Parse(body))
        {
            sample.RunId = accepted.RootElement.GetProperty("runId").GetString();
        }

        sample.FirstContentMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // 🚨 The subscriber attaches BEFORE the run can have finished, and the
        // stream's own first status decides whether that is true. A recorded
        // stream read after the fact proves nothing about live delivery, so a
        // run whose first observation is already terminal is marked
        // historical-only rather than counted as live.
        await ConsumeRecordedStreamAsync(sample, tenant, correlation, started, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConsumeRecordedStreamAsync(
        RequestSample sample,
        string tenant,
        string correlation,
        long started,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"{_spec.BaseAddress}/api/runs/{sample.RunId}/events", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation(CapacityContract.TenantHeader, tenant);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            sample.Outcome = RequestOutcome.Rejected;
            sample.Error = "event stream: HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            return;
        }

        var reader = new SseFrameReader(started);
        var frames = 0;
        long? lastSequence = null;
        var contiguous = true;
        var live = false;
        var sawTerminal = false;
        var assembled = new StringBuilder();
        double? previous = null;
        double maximumGap = 0;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var frame in reader.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            frames++;

            if (previous is { } last)
            {
                maximumGap = Math.Max(maximumGap, frame.ElapsedMilliseconds - last);
            }

            previous = frame.ElapsedMilliseconds;

            if (long.TryParse(frame.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence))
            {
                if (lastSequence is { } expected && sequence != expected + 1)
                {
                    contiguous = false;
                }

                lastSequence = sequence;
            }

            // A non-terminal frame proves the subscriber was attached while the
            // run was still producing: the run cannot have been finished at the
            // moment this frame was written.
            if (!sawTerminal)
            {
                live = true;
            }

            if (frame.EventName is "run.completed" or "run.failed")
            {
                sawTerminal = true;
                sample.TerminalStatus = frame.EventName;
            }

            AppendEventText(assembled, frame.Data);
        }

        sample.Frames = frames;
        sample.MaximumFrameGapMilliseconds = frames > 1 ? maximumGap : null;
        sample.LiveSubscription = live && !reader.TruncatedFrame;
        sample.TotalMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (reader.TruncatedFrame)
        {
            sample.Outcome = RequestOutcome.Failed;
            sample.Error = "recorded stream ended mid-frame";
            return;
        }

        if (!contiguous)
        {
            sample.Outcome = RequestOutcome.Failed;
            sample.Error = "recorded stream sequence was not contiguous";
            return;
        }

        sample.ContentMatched = AnswerMatches(assembled.ToString(), correlation);
        sample.Outcome = RequestOutcome.Accepted;
    }

    /// <summary>Asks for one tenant's run while carrying the OTHER tenant's header.</summary>
    /// <param name="runId">A run created by the first tenant.</param>
    /// <param name="foreignTenant">The tenant that must not be able to see it.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns><see langword="true"/> when the server refused; <see langword="false"/> when it answered.</returns>
    /// <remarks>
    /// 🚨 Run inside every cell, not only in the acceptance suite. Tenant
    /// isolation under real concurrency is a different claim from tenant
    /// isolation in a quiet test, and a cell that leaked is invalid however
    /// fast it was.
    /// </remarks>
    public async Task<bool> ProbeCrossTenantAsync(
        string runId,
        string foreignTenant,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"{_spec.BaseAddress}/api/runs/{runId}", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation(CapacityContract.TenantHeader, foreignTenant);

        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return !response.IsSuccessStatusCode;
    }

    private bool AnswerMatches(string? answer, string correlation)
    {
        if (string.IsNullOrEmpty(answer))
        {
            return false;
        }

        var expected = CapacityPayload.Answer(
            correlation,
            _spec.Workload.OutputChunks,
            _spec.Workload.OutputChunkCharacters);

        // The recorded stream carries the answer split across delta events and
        // may carry lifecycle text around it, so containment - not equality -
        // is the honest comparison. The correlation value makes it unique.
        return answer.Contains(expected, StringComparison.Ordinal);
    }

    private static string? ExtractAnswerText(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var response))
        {
            return null;
        }

        var builder = new StringBuilder();
        CollectText(response, builder);
        return builder.ToString();
    }

    private static void CollectText(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "text", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(property.Value.GetString());
                    }
                    else
                    {
                        CollectText(property.Value, builder);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectText(item, builder);
                }

                break;
            default:
                break;
        }
    }

    private static void AppendUpdateText(StringBuilder builder, string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            CollectText(document.RootElement, builder);
        }
        catch (JsonException)
        {
            // A frame the driver cannot parse is not silently dropped: the
            // assembled answer will not match, and the mismatch is reported.
        }
    }

    private static void AppendEventText(StringBuilder builder, string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);

            if (document.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }
        catch (JsonException)
        {
            // See AppendUpdateText.
        }
    }

    private static string? ReadProperty(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string text)
        => text.Length <= 400 ? text : text[..400];
}
