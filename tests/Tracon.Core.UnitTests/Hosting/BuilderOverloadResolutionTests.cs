using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Hosting;

/// <summary>
/// Which overload a lambda reaches. The factory overloads of <c>AddTool</c> sit beside
/// <c>AddTool(Delegate, ...)</c>, which wraps a method through reflection; <c>AddAgent</c> has three
/// factory shapes beside the definition form.
/// </summary>
/// <remarks>
/// <para>
/// measured this first (implementation order, step 1). A lambda with no parameter types has
/// no natural type, so it cannot reach <c>Delegate</c>; a lambda with typed parameters and a method group do
/// have one, and the compiler has to prefer the exact <c>Func</c>. If one of them reached the reflection
/// overload instead, the tool would be built once from the delegate's own signature, not from the
/// factory, and the compile would add trimming warnings on top of it. This class does not carry the
/// suppressions the snapshot test has, so a wrong choice cannot hide.
/// </para>
/// <para>
/// The proof is the registration the call writes. A factory overload writes a <em>factory</em> descriptor;
/// the reflection overload writes an <em>instance</em>.
/// </para>
/// </remarks>
public sealed class BuilderOverloadResolutionTests
{
    private static readonly ModelBinding Model = new() { Provider = "fake", Model = "fake" };

    [Fact]
    public void A_lambda_without_parameter_types_reaches_the_tool_factory()
    {
        var (services, builder) = Start();

        builder.AddTool(provider => Tool(provider));

        IsFactory(services).ShouldBeTrue();
    }

    [Fact]
    public void A_lambda_with_a_typed_parameter_reaches_the_tool_factory()
    {
        var (services, builder) = Start();

        builder.AddTool((IServiceProvider provider) => Tool(provider));

        IsFactory(services).ShouldBeTrue();
    }

    [Fact]
    public void A_static_lambda_reaches_the_tool_factory()
    {
        var (services, builder) = Start();

        builder.AddTool(static provider => Tool(provider));

        IsFactory(services).ShouldBeTrue();
    }

    [Fact]
    public void A_method_group_reaches_the_tool_factory()
    {
        var (services, builder) = Start();

        builder.AddTool(Tool);

        IsFactory(services).ShouldBeTrue();
    }

    [Fact]
    public void A_factory_that_returns_a_derived_function_type_reaches_the_tool_factory()
    {
        var (services, builder) = Start();

        builder.AddTool(provider => new DerivedTool(Tool(provider)));

        IsFactory(services).ShouldBeTrue();
    }

    [Fact]
    public void A_tool_factory_with_a_configure_callback_reaches_the_tool_factory()
    {
        var (services, builder) = Start();

        builder.AddTool(provider => Tool(provider), options => options.RequiresApproval = true);

        IsFactory(services).ShouldBeTrue();
    }

    [Fact]
    public void A_delegate_with_a_name_still_reaches_the_reflection_overload()
    {
        // The method form keeps its meaning: a delegate that is not a factory of tools.
        var (services, builder) = Start();

        builder.AddTool((Func<string>)(static () => "ok"), name: "delegate_tool");

        IsFactory(services).ShouldBeFalse();
    }

    [Fact]
    public void A_ready_tool_still_reaches_the_instance_overload()
    {
        var (services, builder) = Start();

        builder.AddTool(AIFunctionFactory.Create(static () => "ok", "ready_tool"));

        IsFactory(services).ShouldBeFalse();
    }

    [Fact]
    public void A_one_parameter_factory_with_a_model_declares_the_model_and_is_not_the_modelless_form()
    {
        var (services, builder) = Start();

        builder.AddAgent("synchronous", Model, context => context.ChatClient.AsAIAgent(name: context.AgentName));

        var registration = Registration(services);
        registration.Model.ShouldBe(Model);
        registration.ModelFactory.ShouldNotBeNull();
        registration.Factory.ShouldBeNull();
    }

    [Fact]
    public void A_two_parameter_factory_with_a_model_declares_the_model()
    {
        var (services, builder) = Start();

        builder.AddAgent(
            "asynchronous",
            Model,
            async (context, _) =>
            {
                await Task.Yield();
                return context.ChatClient.AsAIAgent(name: context.AgentName);
            });

        var registration = Registration(services);
        registration.Model.ShouldBe(Model);
        registration.ModelFactory.ShouldNotBeNull();
        registration.Factory.ShouldBeNull();
    }

    [Fact]
    public void A_service_provider_factory_without_a_model_stays_the_modelless_form()
    {
        var (services, builder) = Start();

        builder.AddAgent("modelless", provider => Agent(provider));

        var registration = Registration(services);
        registration.Model.ShouldBeNull();
        registration.ModelFactory.ShouldBeNull();
        registration.Factory.ShouldNotBeNull();
    }

    [Fact]
    public void A_typed_service_provider_factory_without_a_model_stays_the_modelless_form()
    {
        var (services, builder) = Start();

        builder.AddAgent("typed-modelless", (IServiceProvider provider) => Agent(provider), "A description.");

        var registration = Registration(services);
        registration.Model.ShouldBeNull();
        registration.Factory.ShouldNotBeNull();
        registration.Description.ShouldBe("A description.");
    }

    [Fact]
    public void A_method_group_agent_factory_without_a_model_stays_the_modelless_form()
    {
        var (services, builder) = Start();

        builder.AddAgent("group-modelless", Agent);

        Registration(services).Factory.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_synchronous_model_overload_runs_the_factory_with_the_context_and_returns_its_agent()
    {
        var (services, builder) = Start();
        var expected = new ProbeAgent();
        AgentFactoryContext? seen = null;

        builder.AddAgent(
            "sync-result",
            Model,
            context =>
            {
                seen = context;
                return expected;
            });

        using var provider = services.BuildServiceProvider();
        var registration = Registration(services);
        var client = new ProbeChatClient();

        var built = await registration.BuildFactoryAgentAsync(
            provider,
            (_, _) => ValueTask.FromResult<IChatClient>(client),
            TestContext.Current.CancellationToken);

        built.Agent.ShouldBeSameAs(expected);
        built.DeclaredChatClient.ShouldBeSameAs(client);
        seen.ShouldNotBeNull();
        seen.AgentName.ShouldBe("sync-result");
        seen.Model.ShouldBe(Model);
        seen.ChatClient.ShouldBeSameAs(client);
        seen.Services.ShouldBeSameAs(provider);
    }

    [Fact]
    public async Task A_model_factory_that_returns_null_throws_a_named_error()
    {
        var (services, builder) = Start();

        builder.AddAgent("returns-null", Model, static (AgentFactoryContext _) => null!);

        using var provider = services.BuildServiceProvider();

        var exception = await Should.ThrowAsync<TraconException>(async () => await Registration(services)
            .BuildFactoryAgentAsync(provider, static (_, _) => ValueTask.FromResult<IChatClient>(new ProbeChatClient()), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("returns-null");
    }

    private static (ServiceCollection Services, ITraconBuilder Builder) Start()
    {
        var services = new ServiceCollection();
        return (services, new TraconBuilder(services));
    }

    private static bool IsFactory(ServiceCollection services)
    {
        var descriptor = services.ShouldHaveSingleItem();

        descriptor.ServiceType.ShouldBe(typeof(TraconToolRegistration));
        return descriptor.ImplementationFactory is not null && descriptor.ImplementationInstance is null;
    }

    private static CodeAgentRegistration Registration(ServiceCollection services)
        => (CodeAgentRegistration)services.ShouldHaveSingleItem().ImplementationInstance!;

    private static AIFunction Tool(IServiceProvider provider)
    {
        _ = provider;
        return AIFunctionFactory.Create(static () => "ok", "factory_tool");
    }

    private static AIAgent Agent(IServiceProvider provider)
    {
        _ = provider;
        return new ProbeAgent();
    }

    private sealed class DerivedTool(AIFunction inner) : DelegatingAIFunction(inner);

    private sealed class ProbeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ProbeAgent : AIAgent
    {
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
            => throw new NotSupportedException();

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
