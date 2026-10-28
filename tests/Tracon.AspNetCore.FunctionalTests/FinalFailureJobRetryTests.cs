using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A job whose failure carries a stable identity that a repeat cannot change fails on
/// that attempt, whatever attempts are left (F-429).
/// </summary>
/// <remarks>
/// <para>
/// The worker repeated every failure up to the job's attempt limit. An eval whose
/// judge model was cut off at its output limit, or a queued run whose model rejected
/// the request with a permanent 4xx, gets the same answer every time; each attempt
/// paid for the unfinished case's agent run and its judge again. The decision reads
/// the error's stable identity, never its text.
/// </para>
/// <para>
/// A consumer handler's own failure keeps its retries: only the identities Tracon
/// writes count, so a <c>404</c> a handler throws while it waits for a resource to
/// appear is still tried again.
/// </para>
/// </remarks>
public sealed class FinalFailureJobRetryTests
{
    private const string AgentName = "graded-agent";
    private const string SuiteName = "graded-suite";

    [Fact]
    public async Task An_eval_whose_judge_was_cut_off_at_its_output_limit_runs_its_case_once()
    {
        var agentModel = new FakeModelProvider("agent-model").EchoesUserMessage();
        var judgeModel = new FinishReasonModelProvider("judge-model");

        for (var turn = 0; turn < 3; turn++)
        {
            judgeModel.Text("""{"score":""", ChatFinishReason.Length, inputTokens: 10, outputTokens: 100);
        }

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                .AddModelProvider(agentModel)
                .AddModelProvider(judgeModel)
                .AddAgent(TestData.Definition(AgentName) with { Model = new ModelBinding { Provider = "agent-model", Model = "agent-1" } })
                .AddEvalEvaluatorFactory<JudgingEvaluatorFactory>();
                builder.UseScheduling(
                static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        (await host.Client.PutAsJsonAsync(
            new Uri($"/tracon/api/evals/{SuiteName}", UriKind.Relative),
            new EvalSuiteSaveRequest
            {
                AgentName = AgentName,
                Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
            })).EnsureSuccessStatusCode();
        (await host.Client.PutAsJsonAsync(
            new Uri($"/tracon/api/evals/{SuiteName}/cases", UriKind.Relative),
            new object[] { new { query = "question" } })).EnsureSuccessStatusCode();

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/evals/{SuiteName}/run", UriKind.Relative),
            new { });
        triggered.EnsureSuccessStatusCode();
        var jobId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("jobId").GetGuid();

        var job = await WaitForEndAsync(host, jobId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.Attempt.ShouldBe(1, "a judge cut off at its output limit is cut off again on every attempt");
        judgeModel.Calls.ShouldBe(1);
        agentModel.Requests.Count.ShouldBe(1, "each attempt paid for the unfinished case's agent run again");
    }

    [Fact]
    public async Task A_queued_run_whose_model_rejects_the_request_is_not_run_again()
    {
        var model = new RejectingModelProvider(HttpStatusCode.BadRequest);

        await using var host = await StartQueuedRunHostAsync(model);

        var runId = await QueueRunAsync(host);
        var job = await WaitForEndAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.Attempt.ShouldBe(1, "a permanent 4xx gets the same answer on every attempt");
        model.Calls.ShouldBe(1);
        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))
            .ShouldNotBeNull().Error.ShouldNotBeNull().Class.ShouldBe(RunErrorClass.ProviderError);
    }

    [Fact]
    public async Task A_queued_run_whose_model_is_rate_limited_is_run_again()
    {
        var model = new RejectingModelProvider(HttpStatusCode.TooManyRequests);

        await using var host = await StartQueuedRunHostAsync(model);

        var runId = await QueueRunAsync(host);
        var job = await WaitForEndAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.Attempt.ShouldBe(3, "a 429 is transient: the job keeps its attempts");
        model.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task A_consumer_handler_that_throws_its_own_404_keeps_its_retries()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                builder.AddJobHandler<NotFoundYetHandler>(NotFoundYetHandler.Key);
                builder.UseScheduling(
                static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenantId = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var now = DateTimeOffset.UtcNow;
        var queued = await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = tenantId,
                HandlerKey = NotFoundYetHandler.Key,
                TargetName = "resource",
                Status = JobStatus.Pending,
                Payload = JsonSerializer.SerializeToElement(new { }),
                MaxAttempts = 2,
                ScheduledFor = now,
                CreatedAt = now,
            },
            []);

        var job = await WaitForEndAsync(host, queued.Id);

        job.Status.ShouldBe(JobStatus.Failed);
        job.Attempt.ShouldBe(2, "a consumer's own failure is never read as one of Tracon's final identities");
    }

    private static Task<TraconTestHost> StartQueuedRunHostAsync(IModelProvider model)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                .AddModelProvider(model)
                .AddAgent(TestData.Definition(AgentName) with { Model = new ModelBinding { Provider = model.Name, Model = "rejecting-1" } });
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: static services =>
            {
                services.Configure<TraconAsyncRunOptions>(static options => options.MaxAttempts = 3);
            });

    private static async Task<Guid> QueueRunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();
    }

    private static async Task<JobRecord> WaitForEndAsync(TraconTestHost host, Guid jobId)
    {
        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenantId = host.Services.GetRequiredService<ITenantContext>().TenantId;

        var job = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(tenantId, jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the job to end");

        return job!;
    }

    /// <summary>Grades every item with a judge model that answers in JSON, through Tracon's own pipeline.</summary>
    private sealed class JudgingEvaluatorFactory(IModelProviderRegistry models) : IEvalEvaluatorFactory
    {
        public IAgentEvaluator Create(IReadOnlyList<EvalCheck> checks) => new JudgingEvaluator(models);
    }

    private sealed class JudgingEvaluator(IModelProviderRegistry models) : IAgentEvaluator
    {
        public string Name => "judging";

        public async Task<AgentEvaluationResults> EvaluateAsync(
            IReadOnlyList<EvalItem> items,
            string evalName = "Agent Framework Eval",
            CancellationToken cancellationToken = default)
        {
            var judge = models.CreateChatClient(new ModelBinding { Provider = "judge-model", Model = "judge-model-1" });

            foreach (var item in items)
            {
                _ = await judge.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, $"Grade: {item.Response}")],
                    new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
                    cancellationToken);
            }

            throw new InvalidOperationException("The judge script always stops at the output limit.");
        }
    }

    /// <summary>A model whose every call fails the way an SDK reports an HTTP error status.</summary>
    private sealed class RejectingModelProvider(HttpStatusCode status) : IModelProvider
    {
        private int _calls;

        public string Name => "rejecting";

        public int Calls => Volatile.Read(ref _calls);

        private void CountCall() => Interlocked.Increment(ref _calls);

        public HttpStatusCode Status { get; } = status;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "rejecting-1" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

        private sealed class Client(RejectingModelProvider owner) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                owner.CountCall();

                // The shape .NET writes: the status is on the property, and the
                // message has no "HTTP nnn" text.
                throw new HttpRequestException(
                    $"Response status code does not indicate success: {(int)owner.Status} ({owner.Status}).",
                    inner: null,
                    owner.Status);
            }

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // Nothing to release.
            }
        }

    }

    /// <summary>A consumer handler that waits for a resource that is not there yet.</summary>
    private sealed class NotFoundYetHandler : IJobHandler
    {
        public const string Key = "contoso.wait-for-resource";

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("The resource is not there yet.", inner: null, HttpStatusCode.NotFound);
    }
}
