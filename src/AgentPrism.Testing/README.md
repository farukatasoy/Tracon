# AgentPrism.Testing

Helpers that let a consumer building an agent on AgentPrism test their own
agent **without calling a real model**: a non-networked model provider, an
in-memory host fixture, and assertions on run records.

The package binds to **no test framework** (xunit, NUnit, MSTest, Shouldly,
FluentAssertions). It throws `AgentPrismAssertionException` when an assertion
fails; every framework counts that as a failure.

## Setup

```bash
dotnet add package AgentPrism.Testing
```

**The meta package (`AgentPrism`) does not reference this package.**
`AgentPrism.Testing` is referenced only from your test project, not from your
production application.

## Quick start

```csharp
[Fact]
public async Task Tool_is_called_when_order_status_is_asked()
{
    var provider = new FakeModelProvider()
        .CallsTool("get_order_status", new { orderId = "ORD-7" })
        .EchoesUserMessage();

    await using var host = await AgentPrismTestHost.StartAsync(options =>
    {
        options.ModelProvider = provider;
        options.ConfigureAgentPrism = builder => builder
            .AddToolsFrom(typeof(OrderTools))
            .AddAgent(new AgentDefinition
            {
                Name = "support",
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = provider.Name, Model = "fake-model" },
                ToolNames = ["get_order_status"],
                Origin = AgentDefinitionOrigin.Code,
            });
    });

    var run = await host.RunAsync("support", "Where is ORD-7?");

    run.ShouldHaveCompleted()
       .ShouldHaveCalledTool("get_order_status", times: 1)
       .ShouldHaveOutputContaining("Echo:");
}
```

## `FakeModelProvider`

Each model has its own **ordered response queue**. A call pops the next step
in the queue; once the queue is drained, every subsequent call returns the
default behavior (a fixed text, or the echo of the last user message via
`EchoesUserMessage()`). This is **lasting** for the provider's lifetime: it
does not infer "which tool was already called" by scanning message history.

```csharp
// Default queue: simple scenarios using a single model.
var provider = new FakeModelProvider()
    .RespondsWith("first response", "second response")
    .EchoesUserMessage();          // once the queue is drained

// Per-model queue: different models of the same provider (e.g. a router
// and the sub-agent it hands off to) must behave INDEPENDENTLY.
var routing = new FakeModelProvider()
    .ForModel("router-model", cfg => cfg
        .CallsTool("background_agents_start_task", new { agentName = "researcher" })
        .CallsTool("background_agents_wait_for_first_completion", new { taskIds = new[] { 1 } })
        .EchoesUserMessage())
    .ForModel("researcher-model", cfg => cfg
        .RespondsWith("research complete"));
```

## `AgentPrismTestHost`

Built on `WebApplication.CreateSlimBuilder()` + `UseTestServer()` —
`Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory<T>` is **not
used**, because it requires an entry-point assembly and locks the consumer
into a hosting model. In-memory stores are a first-class implementation
, so the host needs no database.

```csharp
await using var host = await AgentPrismTestHost.StartAsync(options =>
{
    options.ModelProvider = new FakeModelProvider().EchoesUserMessage();
    options.ConfigureAgentPrism = builder => builder.AddAgent(...);
});

// Raw HTTP access is also possible:
using var response = await host.Client.GetAsync("/agentprism/api/agents");
```

## Your tool must not expect a dependency from DI

`AIFunctionArguments.Services` is **empty** in MAF's run pipeline
(`Microsoft.Extensions.AI.EmptyServiceProvider`). If a tool needs a
dependency, that dependency is taken **at setup time**:

```csharp
// WRONG: resolving from arguments.Services inside the tool body returns
// null at run time.
public static class OrderTools
{
    [AgentPrismTool]
    public static string GetOrderStatus(string orderId, AIFunctionArguments arguments)
    {
        var repo = arguments.Services!.GetRequiredService<IOrderRepository>(); // null
        ...
    }
}

// CORRECT: the dependency is taken in the constructor, the tool is
// registered through a factory.
public sealed class OrderTools(IOrderRepository repository)
{
    [AgentPrismTool]
    public string GetOrderStatus(string orderId) => repository.Find(orderId);
}

services.AddSingleton(provider =>
    new AgentPrismToolRegistration(new OrderTools(provider.GetRequiredService<IOrderRepository>()), ...));
```

This trap was measured: an isolated probe program did not
prove the real pipeline. `AgentPrismTestHost` builds the real pipeline, not a
separate probe — so this failure shows up in tests the exact same way.

## `RunAssertions`

```csharp
run.ShouldHaveCompleted();
run.ShouldHaveCalledTool("refund_order");
run.ShouldHaveCalledTool("refund_order", times: 1);
run.ShouldNotHaveCalledTool("delete_account");
run.ShouldHaveFailedWith("content_filtered");
run.ShouldHaveOutputContaining("refunded");
```

Streaming runs are covered too: `run_events` fills in on the streaming path as
well, no separate type is needed.

## Dependencies

The package depends on `AgentPrism.Core` and `AgentPrism.AspNetCore`; the
in-memory host fixture needs the package that builds the endpoints.
`AgentPrism.AspNetCore` is the only package that carries prerelease MAF
packages; since `AgentPrism.Testing` depends on it, it inherits those
prerelease dependencies **transitively**. This is acceptable — the test
package is **not** in the production dependency graph.

The package takes **no test framework** dependency.

## Links

- Guide: <https://agentprism.doayen.web.tr/guides/testing/>
- Capability map: <https://agentprism.doayen.web.tr/capabilities/>
- API reference: <https://agentprism.doayen.web.tr/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://agentprism.doayen.web.tr/reference/licensing/>
