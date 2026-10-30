using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Extensions;

/// <summary>
/// The order the documentation gives for chat client rings: in one slot the first ring
/// added is the innermost, across the extensions in the order the definition names them; a Turn ring
/// is outside every ModelCall ring; and both compile paths place them.
/// </summary>
public sealed class ChatClientRingOrderTests
{
    [Fact]
    public async Task In_one_slot_the_first_ring_added_is_the_innermost_across_extensions()
    {
        var entries = new List<string>();
        var compiler = Compiler(
            new DelegateAgentExtension("first", context =>
            {
                context.UseChatClient(ChatClientSlot.ModelCall, inner => new TagRing(inner, "first-a", entries));
                context.UseChatClient(ChatClientSlot.ModelCall, inner => new TagRing(inner, "first-b", entries));
            }, null),
            new DelegateAgentExtension("second", context =>
                context.UseChatClient(ChatClientSlot.ModelCall, inner => new TagRing(inner, "second-a", entries)), null));

        var agent = compiler.Compile(Named("first", "second"));
        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        // Outermost first: the ring the last extension added last is entered first.
        entries.ShouldBe(["second-a", "first-b", "first-a"]);
    }

    [Fact]
    public async Task The_order_follows_the_definition_not_the_registration()
    {
        var entries = new List<string>();
        var compiler = Compiler(
            new DelegateAgentExtension("first", context => context.UseChatClient(ChatClientSlot.Turn, inner => new TagRing(inner, "first", entries)), null),
            new DelegateAgentExtension("second", context => context.UseChatClient(ChatClientSlot.Turn, inner => new TagRing(inner, "second", entries)), null));

        var agent = await compiler.CompileAsync(Named("second", "first"), TestContext.Current.CancellationToken);
        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        entries.ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task A_Turn_ring_is_outside_every_ModelCall_ring_whatever_order_they_were_added_in()
    {
        var entries = new List<string>();
        var compiler = Compiler(new DelegateAgentExtension("both", context =>
        {
            context.UseChatClient(ChatClientSlot.ModelCall, inner => new TagRing(inner, "model-call", entries));
            context.UseChatClient(ChatClientSlot.Turn, inner => new TagRing(inner, "turn", entries));
            context.UseChatClient(ChatClientSlot.ModelCall, inner => new TagRing(inner, "model-call-2", entries));
        }, null));

        var agent = compiler.Compile(Named("both"));
        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        entries.ShouldBe(["turn", "model-call-2", "model-call"]);
    }

    /// <summary>
    /// The synchronous path builds a fallback link through its own resolver. That resolver
    /// must pass the rings too, or an agent compiled with <c>Compile</c> runs its fallback model
    /// without them.
    /// </summary>
    [Fact]
    public async Task The_synchronous_compile_path_gives_a_fallback_link_the_same_rings()
    {
        var entries = new List<string>();
        var built = 0;
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(
                new FakeModelProvider(new UnavailableChatClient(), name: "primary"),
                new FakeModelProvider(name: "fallback")),
            TestData.Registry(),
            agentExtensions:
            [
                new DelegateAgentExtension("ring", context => context.UseChatClient(ChatClientSlot.ModelCall, inner =>
                    new TagRing(inner, $"ring-{Interlocked.Increment(ref built)}", entries)), null),
            ]);

        var agent = compiler.Compile(Named("ring") with
        {
            Model = new ModelBinding
            {
                Provider = "primary",
                Model = "fake-model",
                Fallbacks = [new ModelFallback { Provider = "fallback", Model = "fake-model" }],
            },
        });

        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        built.ShouldBe(2);
        entries.ShouldBe(["ring-1", "ring-2"]);
    }

    [Fact]
    public void The_registry_hands_the_compiler_the_rings_per_slot_in_the_order_they_were_added()
    {
        var registry = new AgentExtensionRegistry(
        [
            new DelegateAgentExtension("one", static context =>
            {
                context.UseChatClient(ChatClientSlot.Turn, static inner => inner);
                context.UseChatClient(ChatClientSlot.ModelCall, static inner => inner);
            }, null),
            new DelegateAgentExtension("two", static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => inner), null),
            new DelegateAgentExtension("none", static _ => { }, null),
        ]);

        var configuration = registry.Configure(
            Named("two", "none", "one"), "tenant-a", NoServices.Instance, new ChatClientAgentOptions(), harnessOptions: null);

        configuration.Rings.IsEmpty.ShouldBeFalse();
        configuration.Rings.AgentName.ShouldBe("test-agent");
        configuration.Rings.Extensions.ShouldBe(["two", "one"]);
        configuration.Wrappers.ShouldBeEmpty();
    }

    [Fact]
    public void An_agent_whose_extensions_add_no_ring_gets_the_empty_set()
    {
        var registry = new AgentExtensionRegistry([new DelegateAgentExtension("none", static _ => { }, null)]);

        var configuration = registry.Configure(Named("none"), "tenant-a", NoServices.Instance, new ChatClientAgentOptions(), harnessOptions: null);

        configuration.Rings.ShouldBeSameAs(ChatClientRings.Empty);
    }

    [Fact]
    public void A_null_ring_is_refused()
    {
        var context = new AgentExtensionContext(Named(), "tenant-a", NoServices.Instance, new ChatClientAgentOptions(), harnessAgentOptions: null);

        Should.Throw<ArgumentNullException>(() => context.UseChatClient(ChatClientSlot.ModelCall, null!));
    }

    [Fact]
    public void A_slot_that_is_not_defined_is_refused()
    {
        var context = new AgentExtensionContext(Named(), "tenant-a", NoServices.Instance, new ChatClientAgentOptions(), harnessAgentOptions: null);

        Should.Throw<ArgumentOutOfRangeException>(() => context.UseChatClient((ChatClientSlot)7, static inner => inner))
            .ParamName.ShouldBe("slot");
    }

    private static AgentDefinition Named(params string[] extensionNames)
        => TestData.Definition() with { ExtensionNames = extensionNames };

    private static AgentDefinitionCompiler Compiler(params IAgentExtension[] extensions)
        => new(TestData.Providers(new FakeModelProvider()), TestData.Registry(), agentExtensions: extensions);

    /// <summary>A provider client that fails as an unavailable upstream does, so the fallback chain moves on.</summary>
    private sealed class UnavailableChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("HTTP 503 (Service Unavailable)");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("HTTP 503 (Service Unavailable)");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake owns nothing.
        }
    }

    private sealed class TagRing(IChatClient inner, string tag, List<string> entries) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            entries.Add(tag);
            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            entries.Add(tag);

            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }
    }
}
