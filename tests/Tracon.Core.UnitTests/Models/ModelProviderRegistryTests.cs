using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Models;

/// <summary>Verifies the provider registry's resolution behavior by name.</summary>
public sealed class ModelProviderRegistryTests
{
    [Fact]
    public void Provider_is_resolved_by_name()
    {
        var provider = new FakeModelProvider(name: "first");
        var registry = new ModelProviderRegistry([provider, new FakeModelProvider(name: "second")]);

        registry.CreateChatClient(TestData.Binding(provider: "first"));

        provider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public void Provider_name_is_case_insensitive()
    {
        var provider = new FakeModelProvider(name: "openai");
        var registry = new ModelProviderRegistry([provider]);

        registry.CreateChatClient(TestData.Binding(provider: "OpenAI"));

        provider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public void Unknown_provider_gives_an_understandable_error()
    {
        var registry = new ModelProviderRegistry([new FakeModelProvider(name: "fake")]);

        var exception = Should.Throw<TraconException>(
            () => registry.CreateChatClient(TestData.Binding(provider: "no-such-provider")));

        exception.Message.ShouldContain("no-such-provider");
        // The error message must list the registered providers and say what to do.
        exception.Message.ShouldContain("fake");
        exception.Message.ShouldContain("UseOpenAI");
    }

    [Fact]
    public void Error_says_so_when_there_is_no_provider_at_all()
    {
        var registry = new ModelProviderRegistry([]);

        var exception = Should.Throw<TraconException>(
            () => registry.CreateChatClient(TestData.Binding()));

        exception.Message.ShouldContain("no provider is registered");
    }

    [Fact]
    public void Registering_the_same_name_twice_fails()
    {
        var exception = Should.Throw<TraconException>(() => new ModelProviderRegistry(
            [new FakeModelProvider(name: "openai"), new FakeModelProvider(name: "OPENAI")]));

        exception.Message.ShouldContain("openai");
    }

    [Fact]
    public void List_is_sorted_by_name()
    {
        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(name: "zeta"), new FakeModelProvider(name: "alpha")]);

        registry.List().Select(static descriptor => descriptor.Name).ShouldBe(["alpha", "zeta"]);
    }

    [Fact]
    public void List_carries_the_providers_models()
    {
        var registry = new ModelProviderRegistry([new FakeModelProvider()]);

        registry.List().ShouldHaveSingleItem().Models.ShouldHaveSingleItem().Name.ShouldBe("fake-model");
    }

    [Fact]
    public void Registry_sets_up_the_tool_loop_and_telemetry_pipeline()
    {
        // 🚨 Phase 48: these two links were moved here from inside four provider packages.
        // If they are lost, the agent never runs tool calls and no 'chat' span
        // is produced. A third-party provider also inherits them for free —
        // FakeModelProvider returns a raw client and the links are still there.
        using var chatClient = new ModelProviderRegistry([new FakeModelProvider()])
            .CreateChatClient(TestData.Binding());

        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldNotBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldNotBeNull();
    }

    [Fact]
    public void Null_binding_is_rejected()
    {
        var registry = new ModelProviderRegistry([new FakeModelProvider()]);

        Should.Throw<ArgumentNullException>(() => registry.CreateChatClient(null!));
    }
}
