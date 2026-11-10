using A2A;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Drives the A2A endpoint with the official A2A .NET SDK's own card resolver and client, the pair another agent uses
/// to find and call a Tracon agent.
/// </summary>
/// <remarks>
/// The raw tests build the JSON-RPC body with the SDK's serializer and read the answer by hand. The SDK client also
/// decides what the card means (which interface to call, whether streaming is allowed) and how an error code maps to
/// an exception, so those decisions are checked here with the SDK making them.
/// </remarks>
public sealed class A2ASdkDifferentialTests
{
    private const string Agent = "kod-agent";

    [Fact]
    public async Task The_SDK_card_resolver_reads_the_published_card()
    {
        await using var host = await StartAsync();

        var card = await Resolver(host).GetAgentCardAsync(TestContext.Current.CancellationToken);

        card.Name.ShouldBe(Agent);
        card.Description.ShouldNotBeNullOrWhiteSpace();
        card.Version.ShouldNotBeNullOrWhiteSpace();
        card.DefaultInputModes.Any(static mode => string.Equals(mode, "text/plain", StringComparison.Ordinal)).ShouldBeTrue();
        card.DefaultOutputModes.Any(static mode => string.Equals(mode, "text/plain", StringComparison.Ordinal)).ShouldBeTrue();
        card.Capabilities.Streaming.ShouldNotBe(true, "the server declares it does not stream");
        card.SupportedInterfaces.ShouldNotBeNull().ShouldNotBeEmpty();
        card.SupportedInterfaces[0].ProtocolBinding.ShouldBe(ProtocolBindingNames.JsonRpc);
    }

    [Fact]
    public async Task The_SDK_client_sends_a_message_and_reads_the_agent_reply()
    {
        await using var host = await StartAsync();
        using var client = Client(host);

        var response = await client.SendMessageAsync(UserMessage("hello a2a"), TestContext.Current.CancellationToken);

        response.PayloadCase.ShouldBe(SendMessageResponseCase.Message, "the server answers with a message, not a task");

        var reply = response.Message.ShouldNotBeNull();
        reply.Role.ShouldBe(Role.Agent);
        reply.Parts.ShouldNotBeEmpty();
        reply.Parts[0].Text.ShouldNotBeNull().ShouldContain("hello a2a", Case.Sensitive);
        reply.MessageId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_SDK_extension_helper_round_trips_text_in_and_text_out()
    {
        await using var host = await StartAsync();
        using var client = Client(host);

        var response = await client.SendMessageAsync("shortcut text", Role.User, Guid.NewGuid().ToString("N"), TestContext.Current.CancellationToken);

        response.Message.ShouldNotBeNull().Parts[0].Text.ShouldNotBeNull().ShouldContain("shortcut text", Case.Sensitive);
    }

    [Fact]
    public async Task A_context_id_carries_a_second_message_into_the_same_conversation()
    {
        var provider = new FakeModelProvider("a2a-sdk-model").RespondsWith("first reply", "second reply");
        await using var host = await StartAsync(provider);
        using var client = Client(host);

        var first = await client.SendMessageAsync(UserMessage("one", contextId: "ctx-sdk"), TestContext.Current.CancellationToken);
        var second = await client.SendMessageAsync(UserMessage("two", contextId: "ctx-sdk"), TestContext.Current.CancellationToken);

        first.Message.ShouldNotBeNull().ContextId.ShouldBe("ctx-sdk");
        second.Message.ShouldNotBeNull().ContextId.ShouldBe("ctx-sdk");
        second.Message.Parts[0].Text.ShouldBe("second reply");
    }

    [Fact]
    public async Task Streaming_from_a_server_that_declares_no_streaming_ends_in_an_SDK_exception_or_a_single_message_and_never_hangs()
    {
        await using var host = await StartAsync();
        using var client = Client(host);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20)); // cancel: bound

        var events = new List<StreamResponse>();
        Exception? failure = null;

        try
        {
            await foreach (var item in client.SendStreamingMessageAsync(UserMessage("stream me"), deadline.Token))
            {
                events.Add(item);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = exception;
        }

        deadline.IsCancellationRequested.ShouldBeFalse("the call ended on its own; it did not wait for the deadline");
        (failure is not null || events.Count > 0).ShouldBeTrue("the SDK got either an error it can read or at least one event");

        if (failure is A2AException a2a)
        {
            a2a.ErrorCode.ShouldBe(A2AErrorCode.UnsupportedOperation);
        }
    }

    [Fact]
    public async Task Getting_or_canceling_a_task_that_does_not_exist_is_a_TaskNotFound_the_SDK_raises()
    {
        await using var host = await StartAsync();
        using var client = Client(host);

        var get = await Should.ThrowAsync<A2AException>(
            async () => await client.GetTaskAsync(new GetTaskRequest { Id = "no-such-task" }, TestContext.Current.CancellationToken));
        var cancel = await Should.ThrowAsync<A2AException>(
            async () => await client.CancelTaskAsync(new CancelTaskRequest { Id = "no-such-task" }, TestContext.Current.CancellationToken));

        get.ErrorCode.ShouldBe(A2AErrorCode.TaskNotFound);
        cancel.ErrorCode.ShouldBe(A2AErrorCode.TaskNotFound);
    }

    [Fact]
    public async Task A_message_to_an_agent_that_is_not_exposed_is_refused_before_the_SDK_can_resolve_a_card()
    {
        await using var host = await StartAsync();

        var resolver = new A2ACardResolver(
            new Uri(host.Client.BaseAddress!, "/tracon/a2a/not-exposed/"),
            host.Client);

        await Should.ThrowAsync<Exception>(async () => await resolver.GetAgentCardAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_SDK_factory_builds_a_client_from_the_published_card()
    {
        await using var host = await StartAsync();

        var card = await Resolver(host).GetAgentCardAsync(TestContext.Current.CancellationToken);

        // A consumer that follows the card instead of hard-coding the URL: the card's interface must be one the
        // SDK can turn into a client and call.
        var client = A2AClientFactory.Create(card, host.Client);

        var response = await client.SendMessageAsync(UserMessage("via the factory"), TestContext.Current.CancellationToken);

        response.Message.ShouldNotBeNull().Parts[0].Text.ShouldNotBeNull().ShouldContain("via the factory", Case.Sensitive);
    }

    private static SendMessageRequest UserMessage(string text, string? contextId = null)
        => new()
        {
            Message = new Message
            {
                Role = Role.User,
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = contextId,
                Parts = [Part.FromText(text)],
            },
        };

    private static Task<TraconTestHost> StartAsync(FakeModelProvider? provider = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                if (provider is not null)
                {
                    builder.AddModelProvider(provider);
                    builder.AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = provider.Name, Model = provider.Name + "-1" } });
                }
                else
                {
                    builder.AddAgent(TestData.Definition());
                }

                builder.UseA2A(static options => options.ExposedAgents.Add(Agent));
            },
            configureAfterMap: static app => app.MapTraconA2A());

    private static A2ACardResolver Resolver(TraconTestHost host)
        => new(new Uri(host.Client.BaseAddress!, "/tracon/a2a/kod-agent/"), host.Client);

    private static A2AClient Client(TraconTestHost host)
        => new(new Uri(host.Client.BaseAddress!, "/tracon/a2a/kod-agent"), host.Client);
}
