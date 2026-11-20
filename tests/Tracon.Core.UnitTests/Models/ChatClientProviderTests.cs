using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// <c>AddChatClientProvider</c>: a chat client the host already has becomes a model provider.
/// </summary>
/// <remarks>
/// The registry places the content guard directly above the client a provider returns, so the tool-call loop that
/// Tracon adds sees every turn. A client that carries its own loop runs the tool results beneath the guard. The
/// helper refuses such a client; a documentation sentence could not.
/// </remarks>
public sealed class ChatClientProviderTests
{
    private const string ProviderName = "bring-your-own";

    private static readonly ModelBinding Binding = new() { Provider = ProviderName, Model = "own-model" };

    [Fact]
    public void A_registered_client_is_served_under_its_name_with_its_catalog()
    {
        var client = new FakeChatClient();
        var models = new[] { new ModelDescriptor { Name = "own-model", SupportsTools = false } };

        using var provider = Build(builder => builder.AddChatClientProvider(ProviderName, client, models));

        var registered = provider.GetServices<IModelProvider>().ShouldHaveSingleItem();

        registered.Name.ShouldBe(ProviderName);
        registered.Models.ShouldHaveSingleItem().Name.ShouldBe("own-model");
        registered.CreateChatClient(Binding).ShouldBeSameAs(client);
        registered.ShouldNotBeAssignableTo<ITenantCredentialModelProvider>("a tenant's own key does not reach a client the host built");
    }

    [Fact]
    public void A_registered_client_without_a_catalog_has_an_empty_one()
    {
        using var provider = Build(builder => builder.AddChatClientProvider(ProviderName, new FakeChatClient()));

        provider.GetServices<IModelProvider>().ShouldHaveSingleItem().Models.ShouldBeEmpty();
    }

    [Fact]
    public void A_client_that_runs_the_tool_call_loop_is_refused_when_it_is_registered()
    {
        var withLoop = new FakeChatClient().AsBuilder().UseFunctionInvocation().Build();
        var services = new ServiceCollection();
        var builder = services.AddTracon();

        var thrown = Should.Throw<TraconException>(() => builder.AddChatClientProvider(ProviderName, withLoop));

        thrown.Message.ShouldContain(ProviderName);
        thrown.Message.ShouldContain("UseFunctionInvocation", Case.Sensitive);
    }

    [Fact]
    public void A_client_wrapped_in_other_layers_is_still_refused_when_a_loop_is_inside()
    {
        var withLoop = new FakeChatClient().AsBuilder().UseFunctionInvocation().UseLogging(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).Build();
        var builder = new ServiceCollection().AddTracon();

        Should.Throw<TraconException>(() => builder.AddChatClientProvider(ProviderName, withLoop));
    }

    [Fact]
    public void A_factory_that_returns_a_client_with_the_loop_is_refused_when_the_client_is_built()
    {
        var withLoop = new FakeChatClient().AsBuilder().UseFunctionInvocation().Build();

        using var provider = Build(builder => builder.AddChatClientProvider(ProviderName, static (_, _) => throw new InvalidOperationException("unused")));

        // The factory runs when the client is built, not when the provider is registered.
        Should.Throw<InvalidOperationException>(() => provider.GetServices<IModelProvider>().Single().CreateChatClient(Binding));

        using var refusing = Build(builder => builder.AddChatClientProvider(ProviderName, (_, _) => withLoop));

        Should.Throw<TraconException>(() => refusing.GetServices<IModelProvider>().Single().CreateChatClient(Binding))
            .Message.ShouldContain(ProviderName);
    }

    [Fact]
    public void The_factory_receives_the_services_of_the_host_and_the_binding()
    {
        var client = new FakeChatClient();
        ModelBinding? seen = null;
        IServiceProvider? seenServices = null;

        using var provider = Build(builder => builder.AddChatClientProvider(
            ProviderName,
            (services, binding) =>
            {
                seen = binding;
                seenServices = services;

                return client;
            }));

        provider.GetServices<IModelProvider>().Single().CreateChatClient(Binding).ShouldBeSameAs(client);

        seen.ShouldBe(Binding);
        seenServices.ShouldNotBeNull().GetService<IModelProviderRegistry>().ShouldNotBeNull();
    }

    [Fact]
    public void A_blank_name_a_missing_client_or_a_missing_factory_is_refused_at_registration()
    {
        var builder = new ServiceCollection().AddTracon();

        Should.Throw<ArgumentException>(() => builder.AddChatClientProvider(" ", new FakeChatClient()));
        Should.Throw<ArgumentNullException>(() => builder.AddChatClientProvider(ProviderName, (IChatClient)null!));
        Should.Throw<ArgumentNullException>(() => builder.AddChatClientProvider(ProviderName, (Func<IServiceProvider, ModelBinding, IChatClient>)null!));
    }

    private static ServiceProvider Build(Action<ITraconBuilder> configure)
    {
        var services = new ServiceCollection();

        configure(services.AddTracon());

        return services.BuildServiceProvider();
    }
}
