using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A factory agent whose model calls do not match what it declared loses something: the model rings
/// (a client from outside the registry), the tenant's key, the egress policy and the cost (a model
/// it did not declare, or a client other than <see cref="AgentFactoryContext.ChatClient"/>). The
/// catalog says so, once per agent name.
/// </summary>
/// <remarks>
/// <para>
/// This is a DI and catalog test on purpose. The warning reads the agent the factory
/// returned, and only the real <c>AddTracon()</c> registration builds the registry
/// client, the code source and the logger the same way a consumer's host does.
/// </para>
/// <para>
/// Failure modes asked: cancellation (no new path), concurrency (one warning from
/// parallel resolves), empty input (an agent with no chat client is unknown, not
/// bypassed), another tenant (the warning is per name, not per tenant), subsystem
/// failure (an agent whose <c>GetService</c> throws still resolves).
/// </para>
/// </remarks>
public sealed class FactoryAgentDiagnosticsTests
{
    private const string SourceCategory = "Warning Tracon.CodeAgentSource ";

    [Fact]
    public async Task A_factory_agent_with_a_client_from_outside_the_registry_warns_once_per_name()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(
            "outside-agent",
            static _ => OutsideRegistryClient().AsAIAgent(instructions: "Reply briefly.", name: "outside-agent")));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("outside-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await catalog.ResolveAsync("outside-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var warning = Warnings(host).ShouldHaveSingleItem();
        warning.ShouldContain("outside-agent");
        warning.ShouldContain(nameof(IModelProviderRegistry));
    }

    [Fact]
    public async Task Each_factory_agent_name_gets_its_own_warning()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent("first-agent", static _ => OutsideRegistryClient().AsAIAgent(name: "first-agent"))
            .AddAgent("second-agent", static _ => OutsideRegistryClient().AsAIAgent(name: "second-agent")));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        await catalog.ResolveAsync("first-agent", culture: null, TestContext.Current.CancellationToken);
        await catalog.ResolveAsync("second-agent", culture: null, TestContext.Current.CancellationToken);
        await catalog.ResolveAsync("first-agent", culture: null, TestContext.Current.CancellationToken);

        var warnings = Warnings(host);
        warnings.Count.ShouldBe(2);
        warnings.ShouldContain(static line => line.Contains("first-agent", StringComparison.Ordinal));
        warnings.ShouldContain(static line => line.Contains("second-agent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Parallel_first_resolves_of_one_name_warn_once()
    {
        // A code agent resolves without a real await, so Task.WhenAll alone would run the
        // calls one after the other. Each factory holds its callers at a barrier on dedicated
        // threads, so all of them reach the warning check together, and a slow logger keeps
        // the writer inside the check long enough for the others to arrive. That catches a
        // check that logs first and records the name afterwards. It cannot prove that the
        // single TryAdd call is atomic; the dictionary type gives that guarantee.
        const int names = 40;
        const int callers = 8;
        var barriers = Enumerable.Range(0, names).ToDictionary(static index => $"outside-{index}", static _ => new Barrier(callers), StringComparer.Ordinal);

        try
        {
            await using var host = await TraconTestHost.StartAsync(
                builder =>
                {
                    foreach (var (name, barrier) in barriers)
                    {
                        builder.AddAgent(name, _ =>
                        {
                            var agent = OutsideRegistryClient().AsAIAgent(name: name);
                            barrier.SignalAndWait(TimeSpan.FromSeconds(30)).ShouldBeTrue();

                            return agent;
                        });
                    }
                },
                configureServices: static services => services.AddSingleton<ILoggerProvider, SlowWarningLoggerProvider>());

            var catalog = host.Services.GetRequiredService<IAgentCatalog>();

            foreach (var name in barriers.Keys)
            {
                await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Factory.StartNew(
                    () => catalog.ResolveAsync(name, culture: null, TestContext.Current.CancellationToken).AsTask(),
                    TestContext.Current.CancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap()));
            }

            var warnings = Warnings(host);

            warnings.Count.ShouldBe(names);

            foreach (var name in barriers.Keys)
            {
                warnings.Count(line => line.Contains($"'{name}'", StringComparison.Ordinal)).ShouldBe(1, name);
            }
        }
        finally
        {
            foreach (var barrier in barriers.Values)
            {
                barrier.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_factory_without_a_model_that_calls_a_model_is_named_once()
    {
        // The agent builds its client from the registry with the synchronous call: no tenant key, no egress
        // check, no model in the run record. It declared nothing, so Tracon says what the declaration buys.
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(
            "registry-agent",
            static services => services.GetRequiredService<IModelProviderRegistry>()
                .CreateChatClient(TestData.Model())
                .AsAIAgent(instructions: "Reply briefly.", name: "registry-agent")));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("registry-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await catalog.ResolveAsync("registry-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var warning = Warnings(host).ShouldHaveSingleItem();
        warning.ShouldContain("registry-agent");
        warning.ShouldContain("declares none");
        warning.ShouldContain("AddAgent(name, model, factory)");
    }

    [Fact]
    public async Task A_factory_without_a_model_wrapped_in_agent_middleware_is_still_named()
    {
        // The detection must walk through an AIAgentBuilder wrapper; a consumer who adds middleware
        // around a registry-built agent is told the same thing, and not that the client is outside.
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(
            "wrapped-agent",
            static services => services.GetRequiredService<IModelProviderRegistry>()
                .CreateChatClient(TestData.Model())
                .AsAIAgent(name: "wrapped-agent")
                .AsBuilder()
                .Use(static (messages, session, options, next, cancellationToken) => next(messages, session, options, cancellationToken))
                .Build()));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("wrapped-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var warning = Warnings(host).ShouldHaveSingleItem();
        warning.ShouldContain("declares none");
        warning.ShouldNotContain("did not come from");
    }

    [Fact]
    public async Task Parallel_first_resolves_of_a_factory_without_a_model_warn_once()
    {
        const int names = 20;
        const int callers = 8;
        var barriers = Enumerable.Range(0, names).ToDictionary(static index => $"registry-{index}", static _ => new Barrier(callers), StringComparer.Ordinal);

        try
        {
            await using var host = await TraconTestHost.StartAsync(
                builder =>
                {
                    foreach (var (name, barrier) in barriers)
                    {
                        builder.AddAgent(name, services =>
                        {
                            var agent = services.GetRequiredService<IModelProviderRegistry>()
                                .CreateChatClient(TestData.Model())
                                .AsAIAgent(name: name);
                            barrier.SignalAndWait(TimeSpan.FromSeconds(30)).ShouldBeTrue();

                            return agent;
                        });
                    }
                },
                configureServices: static services => services.AddSingleton<ILoggerProvider, SlowWarningLoggerProvider>());

            var catalog = host.Services.GetRequiredService<IAgentCatalog>();

            foreach (var name in barriers.Keys)
            {
                await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Factory.StartNew(
                    () => catalog.ResolveAsync(name, culture: null, TestContext.Current.CancellationToken).AsTask(),
                    TestContext.Current.CancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap()));
            }

            var warnings = Warnings(host);

            warnings.Count.ShouldBe(names);

            foreach (var name in barriers.Keys)
            {
                warnings.Count(line => line.Contains($"'{name}'", StringComparison.Ordinal)).ShouldBe(1, name);
            }
        }
        finally
        {
            foreach (var barrier in barriers.Values)
            {
                barrier.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_factory_that_declares_its_model_and_uses_the_context_client_does_not_warn()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(
            "declared-agent",
            TestData.Model(),
            static context => context.ChatClient.AsAIAgent(instructions: "Reply briefly.", name: context.AgentName)));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("declared-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        Warnings(host).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_declared_agent_wrapped_in_agent_middleware_still_holds_the_context_client()
    {
        // The identity of the registry's outermost ring is the proof, and middleware around the agent
        // or a wrapper around the client leaves it where it was.
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(
            "declared-wrapped-agent",
            TestData.Model(),
            static context => context.ChatClient
                .AsBuilder()
                .Use(static (messages, options, next, cancellationToken) => next(messages, options, cancellationToken))
                .Build()
                .AsAIAgent(name: context.AgentName)
                .AsBuilder()
                .Use(static (messages, session, options, next, cancellationToken) => next(messages, session, options, cancellationToken))
                .Build()));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("declared-wrapped-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        Warnings(host).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_factory_that_declares_its_model_but_builds_another_registry_client_is_named_once()
    {
        // The cost would be written to the declared model while another client answered.
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(
            "mismatched-agent",
            TestData.Model(),
            static context => context.Services.GetRequiredService<IModelProviderRegistry>()
                .CreateChatClient(context.Model)
                .AsAIAgent(name: context.AgentName)));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("mismatched-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await catalog.ResolveAsync("mismatched-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var warning = Warnings(host).ShouldHaveSingleItem();
        warning.ShouldContain("mismatched-agent");
        warning.ShouldContain("AgentFactoryContext.ChatClient");
    }

    [Fact]
    public async Task A_factory_that_declares_its_model_but_uses_a_client_from_outside_the_registry_is_named_once()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(
            "declared-outside-agent",
            TestData.Model(),
            static context => OutsideRegistryClient().AsAIAgent(name: context.AgentName)));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("declared-outside-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var warning = Warnings(host).ShouldHaveSingleItem();
        warning.ShouldContain("declared-outside-agent");
        warning.ShouldContain("AgentFactoryContext.ChatClient");
    }

    [Fact]
    public async Task A_declared_agent_that_exposes_no_chat_client_does_not_warn()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(
                "declared-clientless-agent",
                TestData.Model(),
                static (AgentFactoryContext _) => new ClientlessAgent(throwsOnGetService: false)));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("declared-clientless-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        Warnings(host).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_factory_agent_that_exposes_no_chat_client_does_not_warn()
    {
        // Unknown is not bypassed: an agent that wraps another runtime has no
        // IChatClient to inspect, and a warning there would be noise.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent("plain-agent", static _ => new ClientlessAgent(throwsOnGetService: false)));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("plain-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        Warnings(host).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_whose_GetService_throws_still_resolves_and_does_not_warn()
    {
        // Observability never breaks function: the check is a courtesy to the
        // consumer, not a reason to fail their resolve.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent("broken-agent", static _ => new ClientlessAgent(throwsOnGetService: true)));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("broken-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        Warnings(host).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_definition_backed_code_agent_does_not_warn()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition("declared-agent")));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        (await catalog.ResolveAsync("declared-agent", culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        Warnings(host).ShouldBeEmpty();
    }

    private static IChatClient OutsideRegistryClient()
        => new FakeModelProvider("echo").EchoesUserMessage().CreateChatClient(TestData.Model());

    private static List<string> Warnings(TraconTestHost host)
        => [.. host.Logs.Entries.Where(static line => line.StartsWith(SourceCategory, StringComparison.Ordinal))];

    private sealed class SlowWarningLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SlowWarningLogger();

        public void Dispose()
        {
            // Nothing to release.
        }

        private sealed class SlowWarningLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    Thread.Sleep(5);
                }
            }
        }
    }

    private sealed class ClientlessAgent(bool throwsOnGetService) : AIAgent
    {
        // Throws only for the chat client lookup. Other services (agent metadata)
        // are read by Tracon's own decorators, which are not what this test probes.
        public override object? GetService(Type serviceType, object? serviceKey = null)
            => throwsOnGetService && serviceType == typeof(IChatClient)
                ? throw new InvalidOperationException("The chat client is not available on this agent.")
                : base.GetService(serviceType, serviceKey);

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            System.Text.Json.JsonElement serializedState,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "done")));

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new AgentResponseUpdate(ChatRole.Assistant, "done");
        }
    }
}
