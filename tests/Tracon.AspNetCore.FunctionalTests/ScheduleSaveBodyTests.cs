using System.Net;
using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>payload</c> of a schedule is optional: left out, <c>null</c> and <c>[]</c> all save a schedule with no payload, and
/// it reads back as the empty array. A schedule that leaves out its time zone runs in UTC.
/// </summary>
/// <remarks>
/// The generated clients mark <c>payload</c> optional and nullable, and write the documented default of
/// <c>timeZone</c> and <c>enabled</c>, so a caller that sets only the handler key and the target sends a body the
/// server must save as a schedule that runs. SQLite stands in for the SQL stores; the store contract tests cover
/// the other two.
/// </remarks>
public sealed class ScheduleSaveBodyTests
{
    public static TheoryData<string, string, string> Payloads => new()
    {
        { "memory", """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici"}""", "[]" },
        { "memory", """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici","payload":null}""", "[]" },
        { "memory", """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici","payload":[]}""", "[]" },
        { "memory", """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici","payload":["a","b"]}""", """["a","b"]""" },
        { "sqlite", """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici"}""", "[]" },
        { "sqlite", """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici","payload":null}""", "[]" },
        { "sqlite", """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici","payload":[]}""", "[]" },
        { "sqlite", """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici","payload":["a","b"]}""", """["a","b"]""" },
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task A_schedule_is_saved_and_reads_back_with_the_payload_it_was_given_or_none(string store, string body, string expectedPayload)
    {
        using var database = new TempSqliteDatabase("schedule-body");
        await using var host = await StartAsync(store, database);

        using var saved = await host.Client.PutAsync(ScheduleUri, Json(body));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(saved)).GetProperty("payload").GetRawText().ShouldBe(expectedPayload);

        using var read = await host.Client.GetAsync(ScheduleUri);

        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("payload").GetRawText().ShouldBe(expectedPayload);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task A_schedule_that_leaves_out_its_time_zone_and_enabled_flag_runs_in_utc_and_is_enabled(string store)
    {
        using var database = new TempSqliteDatabase("schedule-body");
        await using var host = await StartAsync(store, database);

        using var saved = await host.Client.PutAsync(
            ScheduleUri,
            Json("""{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici","cron":"0 3 * * *"}"""));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var schedule = await TraconTestHost.ReadJsonAsync(saved);

        schedule.GetProperty("timeZone").GetString().ShouldBe("UTC");
        schedule.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        schedule.GetProperty("nextRunAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    private static Uri ScheduleUri { get; } = new("/tracon/api/schedules/body-schedule", UriKind.Relative);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static Task<TraconTestHost> StartAsync(string store, TempSqliteDatabase database)
        => TraconTestHost.StartAsync(builder =>
        {
            if (string.Equals(store, "sqlite", StringComparison.Ordinal))
            {
                builder.UseSqlite(database.ConnectionString);
            }
        });
}
