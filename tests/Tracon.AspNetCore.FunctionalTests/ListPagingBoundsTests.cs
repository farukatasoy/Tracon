using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A list endpoint answers a negative or a huge <c>skip</c> and <c>take</c> with a page, never with an error or with
/// every row.
/// </summary>
/// <remarks>
/// The job list and the eval run list passed both values to the store as they came. PostgreSQL refuses a negative
/// <c>OFFSET</c> (the host answered <c>500</c>), and a huge <c>take</c> returned every row. The other list endpoints
/// clamp to <c>1..200</c>; these two now do too.
/// </remarks>
public sealed class ListPagingBoundsTests
{
    [Theory]
    [InlineData("skip=-5&take=10")]
    [InlineData("skip=0&take=-1")]
    [InlineData("skip=0&take=0")]
    [InlineData("skip=-2147483648&take=-2147483648")]
    public async Task The_job_list_answers_a_negative_paging_value_with_a_page(string query)
    {
        await using var host = await TraconTestHost.StartAsync();
        await EnqueueAsync(host, 3);

        using var response = await host.Client.GetAsync(new Uri($"/tracon/api/jobs?{query}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task The_job_list_returns_at_most_two_hundred_rows_however_large_the_take()
    {
        await using var host = await TraconTestHost.StartAsync();
        await EnqueueAsync(host, 205);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/jobs?take=2000000000", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(200);
    }

    [Theory]
    [InlineData("skip=-5&take=10")]
    [InlineData("skip=0&take=-1")]
    [InlineData("skip=-2147483648&take=2147483647")]
    public async Task The_eval_run_list_answers_a_paging_value_out_of_range_with_a_page(string query)
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/evals/paging", UriKind.Relative),
                   new { agentName = "ozetleyici" }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var response = await host.Client.GetAsync(new Uri($"/tracon/api/evals/paging/runs?{query}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task EnqueueAsync(TraconTestHost host, int count)
    {
        var jobs = host.Services.GetRequiredService<IJobStore>();

        for (var index = 0; index < count; index++)
        {
            var now = DateTimeOffset.UtcNow;

            await jobs.EnqueueAsync(
                new JobRecord
                {
                    Id = TraconId.NewId(),
                    TenantId = "default",
                    HandlerKey = JobHandlerKeys.AgentBatch,
                    TargetName = "ozetleyici",
                    Status = JobStatus.Pending,
                    Payload = JsonDocument.Parse("""["input"]""").RootElement.Clone(),
                    ScheduledFor = now,
                    CreatedAt = now,
                },
                ["input"],
                TestContext.Current.CancellationToken);
        }
    }
}
