using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowInboxHarness;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>GET /api/workflows/requests</c>: the tenant-wide list of the human requests the
/// tenant's workflow runs wait on.
/// </summary>
/// <remarks>
/// <para>
/// Every case drives the real endpoint over the real engine: the list is built from rows the engine
/// writes when a run starts waiting, from the answer the respond endpoint marks, and from the
/// prompt the run's event holds, and none of the three is visible to a test of one part alone.
/// </para>
/// <para>
/// The graphs are registered in code and keep the default response mode, so an answered run stays
/// <see cref="RunStatus.AwaitingInput"/> and its request stays in the list, marked answered.
/// </para>
/// </remarks>
public sealed class WorkflowRequestInboxHttpTests
{
    [Fact]
    public async Task The_list_names_what_the_waiting_runs_wait_on_and_when_each_request_expires()
    {
        await using var host = await StartAsync();

        var (timedRun, timedRequest) = await RunToAwaitAsync(host, Timed);
        var (openRun, openRequest) = await RunToAwaitAsync(host, Open);

        var items = await ListAsync(host);

        items.Count.ShouldBe(2);

        var timed = items.Single(item => string.Equals(item.GetProperty("requestId").GetString(), timedRequest, StringComparison.Ordinal));
        var open = items.Single(item => string.Equals(item.GetProperty("requestId").GetString(), openRequest, StringComparison.Ordinal));

        timed.GetProperty("runId").GetGuid().ShouldBe(timedRun);
        timed.GetProperty("workflowName").GetString().ShouldBe(Timed);
        timed.GetProperty("sessionId").GetString().ShouldNotBeNullOrWhiteSpace();
        timed.GetProperty("portId").GetString().ShouldBe(Port);
        timed.GetProperty("prompt").GetString().ShouldBe("Approve: the draft");
        timed.GetProperty("form").GetString().ShouldBe("Boolean");
        timed.GetProperty("answered").GetBoolean().ShouldBeFalse();
        timed.GetProperty("lastResponseRunId").ValueKind.ShouldBe(JsonValueKind.Null);

        // The time is the request's own time plus the workflow's limit.
        var requestedAt = timed.GetProperty("requestedAt").GetDateTimeOffset();

        timed.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(requestedAt + Limit);

        open.GetProperty("runId").GetGuid().ShouldBe(openRun);
        open.GetProperty("expiresAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_requests_of_a_run_carry_the_same_facts_as_the_list()
    {
        await using var host = await StartAsync();

        var (runId, _) = await RunToAwaitAsync(host, Timed);
        var listed = (await ListAsync(host)).ShouldHaveSingleItem();

        using var response = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/runs/{runId}/requests", UriKind.Relative));
        var perRun = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ShouldHaveSingleItem();

        perRun.GetRawText().ShouldBe(listed.GetRawText());
    }

    [Fact]
    public async Task An_answer_marks_the_request_answered_and_the_state_filter_follows()
    {
        await using var host = await StartAsync();

        var (answeredRun, answeredRequest) = await RunToAwaitAsync(host, Timed);
        await RunToAwaitAsync(host, Timed);

        var responseRun = await AnswerAsync(host, answeredRun, answeredRequest);

        // An answered run stays AwaitingInput, so its request is still in the list.
        (await ListAsync(host, "state=all")).Count.ShouldBe(2);

        var pending = await ListAsync(host);
        var answered = (await ListAsync(host, "state=answered")).ShouldHaveSingleItem();

        pending.ShouldHaveSingleItem().GetProperty("answered").GetBoolean().ShouldBeFalse();
        string.Equals(pending[0].GetProperty("requestId").GetString(), answeredRequest, StringComparison.Ordinal).ShouldBeFalse();
        answered.GetProperty("requestId").GetString().ShouldBe(answeredRequest);
        answered.GetProperty("answered").GetBoolean().ShouldBeTrue();
        answered.GetProperty("lastResponseRunId").GetGuid().ShouldBe(responseRun);

        // The default state is pending.
        (await ListAsync(host, "state=pending")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_filtered_page_is_never_short_because_the_filter_runs_before_the_page()
    {
        await using var host = await StartAsync();

        var waiting = new List<(Guid RunId, string RequestId)>();

        for (var i = 0; i < 7; i++)
        {
            waiting.Add(await RunToAwaitAsync(host, Timed));
        }

        // Two answered: filtering after the page would hand back a short page.
        await AnswerAsync(host, waiting[0].RunId, waiting[0].RequestId);
        await AnswerAsync(host, waiting[2].RunId, waiting[2].RequestId);

        var sizes = new List<int>();
        var seen = new List<string>();

        foreach (var skip in new[] { 0, 2, 4 })
        {
            var page = await ListAsync(host, $"state=pending&skip={skip}&take=2");

            sizes.Add(page.Count);
            seen.AddRange(page.Select(static item => item.GetProperty("requestId").GetString()!));
        }

        sizes.ShouldBe([2, 2, 1]);
        seen.Distinct(StringComparer.Ordinal).Count().ShouldBe(5);
        seen.ShouldNotContain(waiting[0].RequestId, StringComparer.Ordinal);
        seen.ShouldNotContain(waiting[2].RequestId, StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_list_is_oldest_first()
    {
        await using var host = await StartAsync();

        var first = await RunToAwaitAsync(host, Timed);
        var second = await RunToAwaitAsync(host, Timed);
        var third = await RunToAwaitAsync(host, Timed);

        (await ListAsync(host)).Select(static item => item.GetProperty("requestId").GetString())
            .ShouldBe([first.RequestId, second.RequestId, third.RequestId]);
    }

    [Theory]
    [InlineData("take=0", 1)]
    [InlineData("take=-4", 1)]
    [InlineData("take=100000", 3)]
    [InlineData("skip=-9&take=100", 3)]
    [InlineData("skip=2&take=100", 1)]
    [InlineData("skip=99", 0)]
    public async Task A_page_outside_the_range_is_cut_to_it(string query, int expected)
    {
        await using var host = await StartAsync();

        for (var i = 0; i < 3; i++)
        {
            await RunToAwaitAsync(host, Timed);
        }

        (await ListAsync(host, query)).Count.ShouldBe(expected);
    }

    [Fact]
    public async Task The_list_can_be_narrowed_to_one_workflow()
    {
        await using var host = await StartAsync();

        await RunToAwaitAsync(host, Timed);
        var other = await RunToAwaitAsync(host, Other);

        var narrowed = (await ListAsync(host, $"workflowName={Other}")).ShouldHaveSingleItem();

        narrowed.GetProperty("runId").GetGuid().ShouldBe(other.RunId);
        (await ListAsync(host, "workflowName=nothing")).ShouldBeEmpty();
        (await ListAsync(host, "workflowName=")).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("waiting")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("pending,answered")]
    [InlineData("pending,all")]
    public async Task A_state_that_is_not_one_of_the_three_names_is_refused(string state)
    {
        await using var host = await StartAsync();

        // Only a name is a state. Enum.TryParse alone would take '1' as 'answered' and a
        // comma-separated list as a combination, and the answer would not be what was asked.
        using var response = await SendListAsync(host, $"state={Uri.EscapeDataString(state)}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("Answered")]
    [InlineData("ALL")]
    public async Task The_three_names_are_accepted_in_any_letter_case(string state)
    {
        await using var host = await StartAsync();

        using var response = await SendListAsync(host, $"state={state}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_tenant_sees_only_its_own_requests()
    {
        await using var host = await StartAsync(tenancy: true);

        var alpha = await RunToAwaitAsync(host, Timed, tenant: "alpha");
        var beta = await RunToAwaitAsync(host, Timed, tenant: "beta");

        var forAlpha = (await ListAsync(host, tenant: "alpha")).ShouldHaveSingleItem();
        var forBeta = (await ListAsync(host, tenant: "beta")).ShouldHaveSingleItem();

        forAlpha.GetProperty("runId").GetGuid().ShouldBe(alpha.RunId);
        forBeta.GetProperty("runId").GetGuid().ShouldBe(beta.RunId);
        (await ListAsync(host, tenant: "gamma")).ShouldBeEmpty();

        // Answering alpha's request marks alpha's row only, though both tenants use the same session ids.
        await AnswerAsync(host, alpha.RunId, alpha.RequestId, tenant: "alpha");

        (await ListAsync(host, "state=answered", tenant: "alpha")).ShouldHaveSingleItem();
        (await ListAsync(host, "state=answered", tenant: "beta")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_handler_that_refuses_reading_runs_rejects_the_list_with_403_and_never_filters_it()
    {
        var handler = new SwitchableHandler();
        await using var host = await StartAsync(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IRunAuthorizationHandler>(handler)));

        for (var i = 0; i < 3; i++)
        {
            await RunToAwaitAsync(host, Timed);
        }

        (await ListAsync(host)).Count.ShouldBe(3);

        handler.Deny = true;

        using var denied = await SendListAsync(host);

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Not a short page of what the caller may see: nothing at all.
        (await denied.Content.ReadAsStringAsync()).ShouldNotContain(Timed);

        handler.Requests.Any(static request => request.Access == RunAccess.Read && request.RunId is null).ShouldBeTrue();
    }

    [Fact]
    public async Task An_installation_without_the_engine_answers_501()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(InboxUri);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Theory]
    [InlineData("requests")]
    [InlineData("Requests")]
    public async Task A_stored_workflow_cannot_take_the_name_of_the_list_route(string name)
    {
        await using var host = await StartAsync(configureTracon: static builder => builder
            .AddAgent(TestData.Definition("manager"))
            .AddAgent(TestData.Definition("worker")));

        using var saved = await SavePlannedAsync(host, name, requestTimeout: null);

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await saved.Content.ReadAsStringAsync()).ShouldContain("reserved");
    }

    [Fact]
    public async Task A_save_carries_the_request_timeout_and_refuses_one_outside_the_range()
    {
        await using var host = await StartAsync(configureTracon: static builder => builder
            .AddAgent(TestData.Definition("manager"))
            .AddAgent(TestData.Definition("worker")));

        using var tooShort = await SavePlannedAsync(host, "planned", TimeSpan.FromSeconds(30));
        using var tooLong = await SavePlannedAsync(host, "planned", TimeSpan.FromDays(400));

        tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooShort.Content.ReadAsStringAsync()).ShouldContain("requestTimeout");
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var saved = await SavePlannedAsync(host, "planned", TimeSpan.FromHours(6));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(saved)).GetProperty("requestTimeout").GetString().ShouldBe("06:00:00");

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/workflows/planned", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("requestTimeout").GetString().ShouldBe("06:00:00");

        // A save that leaves it out removes the limit, like every other field of a full replace.
        using var cleared = await SavePlannedAsync(host, "planned", requestTimeout: null);

        (await TraconTestHost.ReadJsonAsync(cleared)).GetProperty("requestTimeout").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_stored_workflow_gives_its_limit_to_the_requests_of_its_runs()
    {
        await using var host = await StartAsync(configureTracon: static builder => builder
            .AddAgent(TestData.Definition("manager"))
            .AddAgent(TestData.Definition("worker")));

        using var saved = await SavePlannedAsync(host, "planned", TimeSpan.FromDays(2));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        await RunToAwaitAsync(host, "planned");

        var item = (await ListAsync(host)).ShouldHaveSingleItem();

        item.GetProperty("workflowName").GetString().ShouldBe("planned");
        item.GetProperty("form").GetString().ShouldBe("PlanReview");
        item.GetProperty("expiresAt").GetDateTimeOffset()
            .ShouldBe(item.GetProperty("requestedAt").GetDateTimeOffset() + TimeSpan.FromDays(2));
    }

    [Fact]
    public async Task An_answer_still_works_when_the_inbox_cannot_be_marked()
    {
        await using var host = await StartAsync(configureTracon: static builder => builder.Services.ThrowFrom<IWorkflowRequestStore>(
            nameof(IWorkflowRequestStore.MarkAnsweredAsync), static () => new InvalidOperationException("the inbox is unreachable")));

        var (runId, requestId) = await RunToAwaitAsync(host, Timed);
        var responseRun = await AnswerAsync(host, runId, requestId);

        // The answer started its run and finished it; only the mark is missing, and it is logged.
        (await host.WaitForTerminalRunAsync(responseRun)).Status.ShouldBe(RunStatus.Completed);
        (await ListAsync(host, "state=pending")).ShouldHaveSingleItem().GetProperty("answered").GetBoolean().ShouldBeFalse();
        host.Logs.Entries.ShouldContain(static line => line.Contains("could not be marked in the workflow request inbox", StringComparison.Ordinal));
    }

    private sealed class SwitchableHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();
        private readonly List<RunAuthorizationRequest> _requests = [];

        public bool Deny { get; set; }

        public IReadOnlyList<RunAuthorizationRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _requests.Add(request);
            }

            return ValueTask.FromResult(
                Deny && request.Access == RunAccess.Read
                    ? RunAuthorizationResult.Deny("reading runs is refused")
                    : RunAuthorizationResult.Allow());
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());
    }
}
