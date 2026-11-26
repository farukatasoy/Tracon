using System.Net;
using AGUI.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The first sentence of a conversation is the host's, written to the thread's history and streamed back with no model call
/// and no run (<see cref="TraconAGUIOptions.ResolveGreeting"/>).
/// </summary>
/// <remarks>
/// <para>
/// A greeting the front end only draws is a sentence the model and the evaluation never saw. The claims here are about what
/// is <em>recorded</em>: the history holds the greeting once, the agent's next turn reads it, no run row exists for it and the
/// model was not called, and a request the run gates turn away never reaches the host's code. Each crosses the HTTP boundary,
/// the session store and the run recorder, so they are functional tests.
/// </para>
/// </remarks>
public sealed class AGUIGreetingTests
{
    private const string Agent = "greeter";
    private const string Provider = "greet-model";
    private const string Greeting = "Hello Ada! Are you ready to start?";

    // Text only the host's own failure knows: nothing Tracon writes about a failure may contain it.
    private const string Canary = "CANARY-GREETING-FAILURE-7731";

    [Fact]
    public async Task An_opening_request_streams_the_greeting_and_writes_it_to_the_history_with_no_run_and_no_model_call()
    {
        var model = new FakeModelProvider(Provider).RespondsWith("Reply.");

        await using var host = await StartAsync(model, static (_, _) => ValueTask.FromResult<string?>(Greeting));

        var response = await host.RunAsync(Agent, Opening("t-open"));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Types.ShouldBe(["RUN_STARTED", "TEXT_MESSAGE_START", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_END", "RUN_FINISHED"]);
        response.Text.ShouldBe(Greeting);
        response.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();

        model.Requests.ShouldBeEmpty("a greeting is the host's sentence: no model was called");

        var history = await ReadHistoryAsync(host, "t-open");

        history.ShouldNotBeNull().ShouldHaveSingleItem().Text.ShouldBe(Greeting);
        history[0].Role.ShouldBe(Microsoft.Extensions.AI.ChatRole.Assistant);

        using var runs = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);

        (await runs.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("[]", "no run exists for a greeting");
    }

    [Fact]
    public async Task The_next_turn_reads_the_greeting_from_the_history_and_the_transcript_starts_with_it()
    {
        var model = new FakeModelProvider(Provider).RespondsWith("Yes, let us begin.");

        await using var host = await StartAsync(model, static (_, _) => ValueTask.FromResult<string?>(Greeting));

        (await host.RunAsync(Agent, Opening("t-next"))).Status.ShouldBe(HttpStatusCode.OK);

        var turn = await host.RunAsync(Agent, AGUIInputs.User("t-next", "Yes, ready.", run: "run-2"));

        turn.Status.ShouldBe(HttpStatusCode.OK, turn.Body);

        model.Requests.ShouldHaveSingleItem().Messages
            .ShouldContain(static message => message.Role == Microsoft.Extensions.AI.ChatRole.Assistant && message.Text == Greeting, "the model knows it has greeted");

        var history = await ReadHistoryAsync(host, "t-next");

        history.ShouldNotBeNull().Select(static message => message.Text).ShouldBe([Greeting, "Yes, ready.", "Yes, let us begin."]);
    }

    [Fact]
    public async Task The_greeting_is_said_once_a_second_opening_and_a_thread_with_history_say_nothing_and_do_not_call_the_resolver()
    {
        var calls = 0;
        var model = new FakeModelProvider(Provider).RespondsWith("Hi.");

        await using var host = await StartAsync(
            model,
            (_, _) =>
            {
                Interlocked.Increment(ref calls);

                return ValueTask.FromResult<string?>(Greeting);
            });

        (await host.RunAsync(Agent, Opening("t-once"))).Text.ShouldBe(Greeting);

        var second = await host.RunAsync(Agent, Opening("t-once", run: "run-2"));

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Types.ShouldBe(["RUN_STARTED", "RUN_FINISHED"]);

        // A thread that already has a conversation is not greeted either.
        (await host.RunAsync(Agent, AGUIInputs.User("t-talked", "hello"))).Status.ShouldBe(HttpStatusCode.OK);

        var late = await host.RunAsync(Agent, Opening("t-talked"));

        late.Types.ShouldBe(["RUN_STARTED", "RUN_FINISHED"]);

        calls.ShouldBe(1, "the resolver was asked for the one thread that had no history");

        (await ReadHistoryAsync(host, "t-once")).ShouldNotBeNull().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_resolver_is_given_the_agent_the_thread_and_the_caller()
    {
        AGUIParameterRequest? seen = null;

        await using var host = await StartAsync(
            new FakeModelProvider(Provider).RespondsWith("Hi."),
            (request, _) =>
            {
                seen = request;

                return ValueTask.FromResult<string?>(Greeting);
            });

        (await host.RunAsync(Agent, Opening("t-input"))).Status.ShouldBe(HttpStatusCode.OK);

        var request = seen.ShouldNotBeNull();

        request.AgentName.ShouldBe(Agent);
        request.ThreadId.ShouldBe("t-input");
        request.User.ShouldNotBeNull();
        request.ForwardedParameters.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_resolver_that_says_nothing_writes_nothing(string? text)
    {
        await using var host = await StartAsync(
            new FakeModelProvider(Provider).RespondsWith("Hi."),
            (_, _) => ValueTask.FromResult(text));

        var response = await host.RunAsync(Agent, Opening("t-blank"));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Types.ShouldBe(["RUN_STARTED", "RUN_FINISHED"]);

        ((await ReadHistoryAsync(host, "t-blank")) ?? []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_resolver_a_request_with_no_input_is_still_a_400()
    {
        await using var host = await AGUITestHost.StartAsync(builder =>
        {
            builder
                .AddModelProvider(new FakeModelProvider(Provider).RespondsWith("Hi."))
                .AddAgent(TestAgents.Definition(Agent, Provider));
        });

        var response = await host.RunAsync(Agent, Opening("t-none"));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
    }

    [Fact]
    public async Task A_caller_the_run_gates_turn_away_never_reaches_the_resolver()
    {
        var calls = 0;

        await using var host = await AGUITestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(new FakeModelProvider(Provider).RespondsWith("Hi."))
                    .AddAgent(TestAgents.Definition(Agent, Provider));
            },
            configureServices: static services => services.AddSingleton<IRunAuthorizationHandler>(new DenyAllHandler()),
            configureAGUI: options => options.ResolveGreeting = (_, _) =>
            {
                Interlocked.Increment(ref calls);

                return ValueTask.FromResult<string?>(Greeting);
            });

        var response = await host.RunAsync(Agent, Opening("t-denied"));

        response.Status.ShouldBe(HttpStatusCode.Forbidden, response.Body);
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_agent_that_does_not_exist_is_a_404_and_the_resolver_is_not_called()
    {
        var calls = 0;

        await using var host = await StartAsync(
            new FakeModelProvider(Provider).RespondsWith("Hi."),
            (_, _) =>
            {
                Interlocked.Increment(ref calls);

                return ValueTask.FromResult<string?>(Greeting);
            });

        var response = await host.RunAsync("no-such-agent", Opening("t-missing"));

        response.Status.ShouldBe(HttpStatusCode.NotFound, response.Body);
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_resolver_that_throws_ends_the_request_with_a_500_and_writes_nothing_and_says_nothing_of_the_failure()
    {
        await using var host = await StartAsync(
            new FakeModelProvider(Provider).RespondsWith("Hi."),
            static (_, _) => throw new InvalidOperationException(Canary));

        var response = await host.RunAsync(Agent, Opening("t-throws"));

        response.Status.ShouldBe(HttpStatusCode.InternalServerError, response.Body);
        response.Problem.GetProperty("errorType").GetString().ShouldBe("greeting_resolution_failed");
        response.Body.ShouldNotContain(Canary, Case.Sensitive);

        ((await ReadHistoryAsync(host, "t-throws")) ?? []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_opening_requests_that_overlap_write_the_greeting_once()
    {
        // The first request's resolver is held while the second runs from start to end. When it is released, the thread already
        // has a greeting, and the held request must say nothing: the history is read again before it writes.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        await using var host = await StartAsync(
            new FakeModelProvider(Provider).RespondsWith("Hi."),
            async (_, _) =>
            {
                var call = Interlocked.Increment(ref calls);

                if (call == 1)
                {
                    entered.TrySetResult();
                    await release.Task.ConfigureAwait(false);

                    return "first greeting";
                }

                return "second greeting";
            });

        var held = host.RunAsync(Agent, Opening("t-overlap", run: "run-held"));

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var winner = await host.RunAsync(Agent, Opening("t-overlap", run: "run-fast"));

        winner.Text.ShouldBe("second greeting");

        release.TrySetResult();

        var late = await held;

        late.Status.ShouldBe(HttpStatusCode.OK, late.Body);
        late.Types.ShouldBe(["RUN_STARTED", "RUN_FINISHED"], "the thread was greeted while this request waited for the host's code");

        (await ReadHistoryAsync(host, "t-overlap")).ShouldNotBeNull().ShouldHaveSingleItem().Text.ShouldBe("second greeting");
    }

    [Fact]
    public async Task A_message_of_images_alone_is_new_input_and_not_an_opening_request()
    {
        var calls = 0;

        await using var host = await StartAsync(
            new FakeModelProvider(Provider).RespondsWith("Hi."),
            (_, _) =>
            {
                Interlocked.Increment(ref calls);

                return ValueTask.FromResult<string?>(Greeting);
            });

        const string Body = """
            {"threadId":"t-image","runId":"run-1","messages":[{"id":"m1","role":"user","content":[{"type":"image","source":{"type":"url","value":"https://example.test/a.png","mimeType":"image/png"}}]}],"tools":[],"context":[]}
            """;

        var withResolver = await host.PostAsync(Agent, Body);

        withResolver.Status.ShouldBe(HttpStatusCode.BadRequest, withResolver.Body);
        withResolver.Problem.GetProperty("title").GetString().ShouldBe("Empty request", "the answer a host with no resolver gives to the same body");
        calls.ShouldBe(0);
        ((await ReadHistoryAsync(host, "t-image")) ?? []).ShouldBeEmpty("the person's message was not answered with a greeting");
    }

    [Fact]
    public async Task What_the_front_end_sends_as_parameters_reaches_the_resolver_and_no_run()
    {
        IReadOnlyDictionary<string, string>? seen = null;

        await using var host = await StartAsync(
            new FakeModelProvider(Provider).RespondsWith("Hi."),
            (request, _) =>
            {
                seen = request.ForwardedParameters;

                return ValueTask.FromResult<string?>(Greeting);
            });

        var input = Opening("t-forwarded");
        input.ForwardedProperties = System.Text.Json.JsonDocument.Parse("""{"parameters":{"topic":"fractions"}}""").RootElement.Clone();

        (await host.RunAsync(Agent, input)).Status.ShouldBe(HttpStatusCode.OK);

        seen.ShouldNotBeNull().ShouldContainKeyAndValue("topic", "fractions");
    }

    [Fact]
    public async Task A_tenant_whose_quota_is_spent_is_not_greeted_and_the_resolver_is_not_called()
    {
        var calls = 0;

        await using var host = await StartAsync(
            new FakeModelProvider(Provider).RespondsWith("Hi."),
            (_, _) =>
            {
                Interlocked.Increment(ref calls);

                return ValueTask.FromResult<string?>(Greeting);
            });

        await host.Services.GetRequiredService<IQuotaStore>().SaveAsync(
            new QuotaDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                AgentName = Agent,
                Period = QuotaPeriod.Daily,
                MaxRuns = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        (await host.RunAsync(Agent, AGUIInputs.User("t-quota-spent", "hello"))).Status.ShouldBe(HttpStatusCode.OK);

        var refused = await host.RunAsync(Agent, Opening("t-quota-open"));

        refused.Status.ShouldBe(HttpStatusCode.TooManyRequests, refused.Body);
        calls.ShouldBe(0);
        ((await ReadHistoryAsync(host, "t-quota-open")) ?? []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_users_thread_is_not_greeted_and_the_resolver_is_not_called()
    {
        var identity = new SwitchableIdentity();
        var calls = 0;

        await using var host = await AGUITestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider(Provider).RespondsWith("Hi."))
                .AddAgent(TestAgents.Definition(Agent, Provider)),
            configureServices: services =>
            {
                services.Configure<TraconSessionOwnershipOptions>(options =>
                {
                    options.Enabled = true;
                    options.RequireAuthenticatedOwner = true;
                });
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(identity));
            },
            configureAGUI: options => options.ResolveGreeting = (_, _) =>
            {
                Interlocked.Increment(ref calls);

                return ValueTask.FromResult<string?>(Greeting);
            });

        identity.UserId = "a";
        (await host.RunAsync(Agent, AGUIInputs.User("t-owned-open", "hello"))).Status.ShouldBe(HttpStatusCode.OK);

        identity.UserId = "b";
        var refused = await host.RunAsync(Agent, Opening("t-owned-open", run: "run-b"));

        refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body);
        calls.ShouldBe(0, "the owner's thread is not the other user's to open");
    }

    [Fact]
    public async Task A_history_that_cannot_be_written_answers_a_failure_and_no_run_and_no_greeting()
    {
        await using var host = await AGUITestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(new FakeModelProvider(Provider).RespondsWith("Hi."))
                    .AddAgent(TestAgents.Definition(Agent, Provider));
                builder.Services.ThrowFrom<ISessionStore>(
                    nameof(ISessionStore.TryCreateAsync),
                    static () => new InvalidOperationException(Canary));
            },
            configureAGUI: options => options.ResolveGreeting = static (_, _) => ValueTask.FromResult<string?>(Greeting));

        var response = await host.RunAsync(Agent, Opening("t-unwritable"));

        response.Status.ShouldBe(HttpStatusCode.BadGateway, response.Body);
        response.Body.ShouldNotContain(Canary, Case.Sensitive);
        response.Types.ShouldBeEmpty("the failure is a plain answer before any stream opens");
    }

    private static Task<AGUITestHost> StartAsync(
        FakeModelProvider model,
        Func<AGUIParameterRequest, CancellationToken, ValueTask<string?>> resolveGreeting)
        => AGUITestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(model)
                    .AddAgent(TestAgents.Definition(Agent, Provider));
            },
            configureAGUI: options => options.ResolveGreeting = resolveGreeting);

    private static RunAgentInput Opening(string thread, string run = "run-1")
        => AGUIInputs.With(thread, run, messages: []);

    private static async Task<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>?> ReadHistoryAsync(AGUITestHost host, string thread)
    {
        var agent = await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(Agent, cancellationToken: TestContext.Current.CancellationToken);

        return await host.Services.GetRequiredService<AgentSessionManager>()
            .ReadHistoryAsync(agent.ShouldNotBeNull(), thread, cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed class SwitchableIdentity : IRunAttributionContext
    {
        public string? UserId { get; set; }

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class DenyAllHandler : IRunAuthorizationHandler
    {
        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Deny("not for you"));

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Deny("not for you"));
    }
}
