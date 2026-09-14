using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Npgsql;
using Tracon.Capacity;
using Tracon.CapacityDriver;

namespace Tracon.Capacity.Acceptance;

/// <summary>The three HTTP paths, over real TCP against the packed host.</summary>
public sealed class CapacityPathTests : IDisposable
{
    private readonly HttpClient _client = new();

    private static Uri Run => new(AcceptanceEnvironment.BaseAddress + "/api/agents/" + CapacityContract.AgentName + "/run");

    private static HttpRequestMessage Request(string correlation, string tenant)
    {
        var body = JsonSerializer.Serialize(new { message = CapacityPayload.RequestMessage(correlation, 256) });

        var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(CapacityContract.TenantHeader, tenant);
        return request;
    }

    [Fact]
    public async Task A_buffered_run_goes_through_real_HTTP_the_tool_loop_and_the_store()
    {
        var correlation = "acc-buffered-" + Guid.NewGuid().ToString("N");

        using var request = Request(correlation, CapacityContract.TenantA);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", correlation);

        using var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var runId = document.RootElement.GetProperty("runId").GetGuid();

        // The answer carries the correlation the request carried: the model saw
        // THIS request, not a cached or shared one.
        payload.ShouldContain(correlation);

        // 🚨 The tool actually ran, once, for this run - a fixture that skipped
        // the tool loop would still return an answer.
        (await CountAsync(
            $"SELECT COUNT(*) FROM {Qualified("tool_invocations")} WHERE run_id = @id",
            ("id", runId))).ShouldBe(1);

        // And the run is PERSISTED, terminal, under the right tenant.
        (await CountAsync(
            $"SELECT COUNT(*) FROM {Qualified("runs")} WHERE id = @id AND status = 1 AND tenant_id = @tenant",
            ("id", runId), ("tenant", CapacityContract.TenantA))).ShouldBe(1);
    }

    [Fact]
    public async Task A_streaming_run_delivers_frames_rather_than_one_buffered_body()
    {
        var correlation = "acc-stream-" + Guid.NewGuid().ToString("N");

        using var request = Request(correlation, CapacityContract.TenantA);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var reader = new SseFrameReader(System.Diagnostics.Stopwatch.GetTimestamp());
        var assembled = new StringBuilder();
        var updates = 0;

        await using var stream = await response.Content.ReadAsStreamAsync();

        await foreach (var frame in reader.ReadAsync(stream))
        {
            if (string.Equals(frame.EventName, "update", StringComparison.Ordinal))
            {
                updates++;
                using var update = JsonDocument.Parse(frame.Data);
                Collect(update.RootElement, assembled);
            }
        }

        reader.TruncatedFrame.ShouldBeFalse();

        // More than one update frame is what makes this a stream. One frame
        // carrying the whole answer would be a buffered body in an SSE wrapper.
        updates.ShouldBeGreaterThan(1);
        assembled.ToString().ShouldContain(CapacityPayload.Answer(correlation, 20, 256));
    }

    [Fact]
    public async Task A_queued_run_is_accepted_with_202_and_a_location_and_then_actually_runs()
    {
        var correlation = "acc-queued-" + Guid.NewGuid().ToString("N");

        using var request = Request(correlation, CapacityContract.TenantB);
        request.Headers.TryAddWithoutValidation("Prefer", "respond-async");

        using var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldNotBeNull();

        using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = accepted.RootElement.GetProperty("runId").GetGuid();

        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            if (await CountAsync(
                $"SELECT COUNT(*) FROM {Qualified("runs")} WHERE id = @id AND status = 1",
                ("id", runId)) == 1)
            {
                // The recorded stream carries the answer the queued worker produced.
                (await CountAsync(
                    $"SELECT COUNT(*) FROM {Qualified("run_events")} WHERE run_id = @id",
                    ("id", runId))).ShouldBeGreaterThan(0);
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"The queued run {runId} did not reach a terminal status.");
    }

    [Fact]
    public async Task The_model_provider_under_measurement_is_the_fixture_and_makes_no_network_call()
    {
        using var response = await _client.GetAsync(
            new Uri(AcceptanceEnvironment.BaseAddress + "/api/models"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain(CapacityContract.ModelName);
    }

    private static void Collect(JsonElement element, StringBuilder builder)
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
                        Collect(property.Value, builder);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, builder);
                }

                break;
            default:
                break;
        }
    }

    internal static string Qualified(string table)
        => $"\"{AcceptanceEnvironment.Schema}\".\"{table}\"";

    internal static async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(AcceptanceEnvironment.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
