using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Tests.Common;

/// <summary>
/// Every place the HTTP surface takes a date or a time from a caller, driven with the instants a date type breaks on.
/// </summary>
/// <remarks>
/// <para>
/// The same sweep runs against every persistence the surface can sit on. The rule it checks is the one a request is
/// held to everywhere: <strong>no input makes the server fail</strong>. A range that no store can answer is a
/// <c>4xx</c>; any other instant is answered. The instants are chosen because a store's own date type ends there:
/// SQL Server's legacy <c>datetime</c> starts in 1753 and its <c>datetime2</c> stops at the end of 9999, PostgreSQL
/// reads the first and the last instant of <see cref="DateTimeOffset"/> as its infinities, and a <see cref="DateTime"/>
/// cannot hold a time 24 hours before the first of January of year 1.
/// </para>
/// <para>
/// The inventory is the list of <c>date-time</c> inputs of the published OpenAPI document, written out by hand: a new
/// input is one more row in <see cref="Requests"/>, and <c>DateInputInventoryTests</c> fails when the document names
/// an input this class does not drive.
/// </para>
/// </remarks>
internal static class DateInputSweep
{
    /// <summary>The instants every date input is driven with.</summary>
    public static readonly IReadOnlyList<string> Instants =
    [
        "0001-01-01T00:00:00Z",
        "0001-01-01T00:00:00.0000001Z",
        "0001-01-01T14:00:00+14:00",
        "0441-05-05T00:00:00Z",
        "1752-12-31T23:59:59Z",
        "1753-01-01T00:00:00Z",
        "1899-12-31T23:59:59Z",
        "1900-01-01T00:00:00Z",
        "2026-01-01T00:00:00+14:00",
        "2026-01-01T00:00:00-14:00",
        "9999-12-31T00:00:00Z",
        "9999-12-31T23:59:59.9999999Z",
        "9999-12-31T23:59:59.9999999+14:00",
    ];

    /// <summary>The ranges (from, to) driven into every endpoint that takes two ends.</summary>
    public static readonly IReadOnlyList<(string From, string To)> Ranges =
    [
        ("0001-01-01T00:00:00Z", "0001-01-02T00:00:00Z"),
        ("0441-05-04T00:00:00Z", "0441-05-05T00:00:00Z"),
        ("1752-12-31T20:00:00Z", "1753-01-01T04:00:00Z"),
        ("1899-12-25T00:00:00Z", "1900-01-08T00:00:00Z"),
        ("9999-12-30T00:00:00Z", "9999-12-31T23:59:59.9999999Z"),
        ("0001-01-01T00:00:00Z", "9999-12-31T23:59:59.9999999Z"),
        ("9999-12-31T23:59:59.9999999Z", "0001-01-01T00:00:00Z"),
    ];

    /// <summary>
    /// The inputs of the surface: the verb, the path, and the name of the query parameter or body member that holds a date.
    /// </summary>
    public static readonly IReadOnlyList<DateInput> Inputs =
    [
        new("GET", "/runs", "startedAfter", null),
        new("GET", "/stats", "startedAfter", null),
        new("GET", "/tools/usage", "startedAfter", null),
        new("GET", "/stats/timeseries", "from", "to"),
        new("GET", "/evaluation/scores/summary", "from", "to"),
        new("GET", "/audit", "after", "before"),
        new("GET", "/audit/verify", "after", "before"),
        new("POST", "/api-keys", "expiresAt", null),
        new("POST", "/skill-script-grants", "expiresAt", null),
        new("POST", "/definitions/import", "exportedAt", null),
    ];

    /// <summary>
    /// The numbers of days a retention policy keeps rows for: one that fits, and the ones that put the cutoff before the first
    /// instant a date can hold or past the length of a <see cref="TimeSpan"/>.
    /// </summary>
    public static readonly IReadOnlyList<int> RetentionDays = [1, 739_000, 739_500, 3_652_059, 10_675_199, 10_675_200, int.MaxValue];

    /// <summary>Gets every request of the sweep, as a verb, a path with its query, and a body.</summary>
    /// <returns>The requests.</returns>
    public static IEnumerable<SweepRequest> Requests()
    {
        // A number of days is a date once the server subtracts it from now: the policy is saved, then read through its
        // preview and run, which is where the cutoff is computed.
        foreach (var days in RetentionDays)
        {
            yield return new SweepRequest("PUT", "/retention/run_events", new Dictionary<string, object?>(StringComparer.Ordinal) { ["maxAgeDays"] = days, ["enabled"] = true });
            yield return new SweepRequest("GET", "/retention/preview", null);
            yield return new SweepRequest("POST", "/retention/run", null);
        }

        foreach (var input in Inputs)
        {
            foreach (var instant in Instants)
            {
                yield return Build(input, input.First, instant, null, null, string.Empty);

                if (input.Second is { } second)
                {
                    yield return Build(input, second, instant, null, null, string.Empty);
                }
            }

            if (input.Second is { } secondName)
            {
                foreach (var (from, to) in Ranges)
                {
                    foreach (var extra in ExtraQueryFor(input))
                    {
                        yield return Build(input, input.First, from, secondName, to, extra);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Writes the rows that make the queries read data from the edges too: a run that started in year 441 and one that
    /// started in the last hour of year 9999, and a score from before 1900.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <returns>A task that completes when the rows are written.</returns>
    public static async Task SeedEdgeRowsAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var runs = services.GetRequiredService<IRunStore>();
        var scores = services.GetRequiredService<IRunScoreStore>();
        var tenant = services.GetRequiredService<ITenantContext>().TenantId;

        foreach (var startedAt in new[] { "0441-05-05T10:30:00Z", "0001-01-01T00:10:00Z", "9999-12-31T23:30:00Z" })
        {
            var runId = TraconId.NewId();

            await runs.StartRunAsync(new RunStartInfo
            {
                RunId = runId,
                AgentName = "edge-agent",
                StartedAt = DateTimeOffset.Parse(startedAt, CultureInfo.InvariantCulture),
                TenantId = tenant,
            });

            await scores.UpsertAsync(new RunScore
            {
                TenantId = tenant,
                RunId = runId,
                Name = "helpfulness",
                Kind = RunScoreKind.Binary,
                Value = 1,
                Source = "human",
                Author = "operator@example",
                CreatedAt = DateTimeOffset.Parse(startedAt, CultureInfo.InvariantCulture),
            });
        }
    }

    /// <summary>Sends every request and returns the ones the server answered with a <c>5xx</c>.</summary>
    /// <param name="client">The client of the host under test.</param>
    /// <param name="apiPrefix">The path every endpoint sits under.</param>
    /// <returns>One line per failed request: status, verb, path and the start of the body.</returns>
    public static async Task<IReadOnlyList<string>> FindServerErrorsAsync(HttpClient client, string apiPrefix = "/tracon/api")
    {
        ArgumentNullException.ThrowIfNull(client);

        var failures = new List<string>();

        foreach (var request in Requests())
        {
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), new Uri(apiPrefix + request.PathAndQuery, UriKind.Relative));

            if (request.Body is { } body)
            {
                message.Content = JsonContent.Create(body);
            }

            try
            {
                using var response = await client.SendAsync(message);

                if ((int)response.StatusCode >= 500)
                {
                    var text = await response.Content.ReadAsStringAsync();
                    failures.Add($"{(int)response.StatusCode} {request.Method} {request.PathAndQuery} {text[..Math.Min(text.Length, 120)]}");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The in-process test server hands an exception nothing caught to the caller instead of answering 500.
                failures.Add($"unhandled {exception.GetType().Name} {request.Method} {request.PathAndQuery} {exception.Message}");
            }
        }

        return failures;
    }

    private static string[] ExtraQueryFor(DateInput input) => input.Path switch
    {
        "/stats/timeseries" => ["", "&bucket=Hour", "&bucket=Day"],
        "/evaluation/scores/summary" => ["", "&bucket=Hour", "&bucket=Day", "&bucket=Week"],
        _ => [""],
    };

    private static SweepRequest Build(DateInput input, string name, string value, string? secondName, string? secondValue, string extra)
    {
        if (string.Equals(input.Method, "GET", StringComparison.Ordinal))
        {
            var query = $"{name}={Uri.EscapeDataString(value)}";

            if (secondName is not null)
            {
                query += $"&{secondName}={Uri.EscapeDataString(secondValue!)}";
            }

            return new SweepRequest("GET", $"{input.Path}?{query}{extra}", null);
        }

        object body = input.Path switch
        {
            "/api-keys" => new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "edge", ["scopes"] = new[] { "RunsRead" }, [name] = value },
            "/skill-script-grants" => new Dictionary<string, object?>(StringComparer.Ordinal) { ["skillName"] = "edge-skill", [name] = value },
            "/definitions/import" => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["format"] = "tracon.definitions",
                ["formatVersion"] = 1,
                ["agents"] = Array.Empty<object>(),
                ["workflows"] = Array.Empty<object>(),
                [name] = value,
            },
            _ => throw new InvalidOperationException($"No body is known for {input.Path}."),
        };

        return new SweepRequest(input.Method, input.Path, body);
    }

    /// <summary>One input of the surface.</summary>
    /// <param name="Method">The verb.</param>
    /// <param name="Path">The path, without the prefix.</param>
    /// <param name="First">The first (or only) query parameter or body member that holds a date.</param>
    /// <param name="Second">The second query parameter, when the endpoint takes a range.</param>
    internal sealed record DateInput(string Method, string Path, string First, string? Second);

    /// <summary>One request of the sweep.</summary>
    /// <param name="Method">The verb.</param>
    /// <param name="PathAndQuery">The path and the query, without the prefix.</param>
    /// <param name="Body">The JSON body, or <see langword="null"/>.</param>
    internal sealed record SweepRequest(string Method, string PathAndQuery, object? Body);
}
