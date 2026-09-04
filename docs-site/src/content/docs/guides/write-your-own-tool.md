---
title: Write your own tool
description: Build a safe custom tool without bypassing AgentPrism's authorization, approval, timeout, and output boundaries.
---

Use `[AgentPrismTool]` with `AddGeneratedTools()` for a custom tool. It is the AOT-safe
path. Tool instances are singletons and can run concurrently for different tenants and
runs. Keep no mutable run state in fields.

```csharp
using System.ComponentModel;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(OrderReceipt))]
internal partial class OrderToolJsonContext : JsonSerializerContext;

public sealed record OrderReceipt(string OrderId, string Status);

public static class OrderTools
{
    [AgentPrismTool(
        "submit_order",
        "Submits an order to the fulfillment system.",
        Effect = ToolEffect.External,
        RequiredPermission = "orders.submit",
        RequiresApproval = true,
        SafeToRepeat = true,
        TimeoutSeconds = 30,
        MaxOutputBytes = 4096,
        JsonSerializerContext = typeof(OrderToolJsonContext))]
    public static Task<OrderReceipt> SubmitOrderAsync(
        [Description("The identifier of the order to submit.")] string orderId,
        CancellationToken cancellationToken)
        => Task.FromResult(new OrderReceipt(orderId, "submitted"));
}
```

`[Description]` (`System.ComponentModel.DescriptionAttribute`) reaches the generated
JSON Schema as the parameter's `description` — the strongest signal the model has for
filling in that argument correctly. A parameter without one still compiles; the
generator reports it as a warning (`APG0009`).

A standard `System.ComponentModel.DataAnnotations` attribute reaches the schema as a
constraint the model sees before it ever calls the tool:

```csharp
[AgentPrismTool("search_orders_by_text", "Searches orders by free text.")]
public static string[] SearchOrdersByText(
    [Description("The search text.")] [MinLength(1)] [MaxLength(200)] string query,
    [Description("The maximum number of results.")] [Range(1, 100)] int limit = 20,
    [Description("Order codes to match, each 3 letters and 4 digits.")]
    [RegularExpression(@"^[A-Z]{3}-\d{4}$")]
    string[] orderCodes)
    => Array.Empty<string>();
```

| Attribute | Applies to | Schema key(s) |
|---|---|---|
| `[Range(min, max)]` | `int`/`double`/other numeric types (scalar or array element) | `minimum`, `maximum` |
| `[MinLength(n)]` / `[MaxLength(n)]` | `string` | `minLength` / `maxLength` |
| `[MinLength(n)]` / `[MaxLength(n)]` | an array | `minItems` / `maxItems` — the array's own length, never its elements' |
| `[StringLength(max, MinimumLength = min)]` | `string` | `minLength` + `maxLength` |
| `[RegularExpression(pattern)]` | `string` | `pattern`, copied as-is — .NET regex syntax is not translated to ECMA-262 |

When two attributes set the same key (`[MinLength(2)]` together with
`[StringLength(10, MinimumLength = 3)]`), the narrower bound wins. A constraint
attribute that does not apply to its parameter's type or shape — `[Range]` on a
`string`, a length constraint on a `bool`, `[Range(typeof(decimal), "0", "1")]` (its
`Type`-based overload gives no compile-time constant) — is left out of the schema and
reported as a warning (`APG0010`); it never blocks generation.

The schema is the only place this constraint is enforced. Binding rejects a type
mismatch or a missing required field, but nothing else checks `minimum`/`maxLength`/
`pattern` at run time — a validator that wants to enforce them reads
`tool.JsonSchema` inside `IToolArgumentsValidator` (below).

**What the generator can express:** a parameter's scalar type (primitive types,
`string`, `Guid`, `DateTime`/`DateTimeOffset`, `enum`), an array of these,
`description`, whether it is required, the constraints above, and a supported
**object** — a public record or class with a single public constructor, up to 3
nested object levels deep:

```csharp
public sealed record Criterion([Description("The criterion's name.")] string Name, [Description("The weight, 1-5.")] [Range(1, 5)] int Weight);
public sealed record Rubric([Description("The rubric's title.")] string Title, [Description("Its scoring criteria.")] IReadOnlyList<Criterion> Criteria);

// Every type the parameter's graph reaches needs its own [JsonSerializable] on
// the SAME context - Rubric nests Criterion, so both are declared here.
[JsonSerializable(typeof(Rubric))]
[JsonSerializable(typeof(Criterion))]
internal partial class ScoringJsonContext : JsonSerializerContext;

[AgentPrismTool("score_submission", "Scores a submission against a rubric.", JsonSerializerContext = typeof(ScoringJsonContext))]
public static string ScoreSubmission([Description("The rubric to score against.")] Rubric rubric)
    => $"Scored against '{rubric.Title}'.";
```

Binding deserializes the whole object in one call through this same context — never
through reflection — so every type in the graph must be declared on it or the build
fails with `APG0011`, naming the type that is missing. A graph deeper than 3 nested
object levels, or one that reaches itself again through its own members, fails to
build with `APG0012` instead of risking a schema the model's own error rate rises
against once it gets this deep.

🚨 **Put the attribute directly on the parameter, never with an explicit
`[property: ...]` target.** `[Description]`/`[Range]`/etc. on a positional record's
parameter (`Rubric(... int Weight)`) reach the generator only when the attribute has
no explicit target — `[property: Description(...)]` moves it to the generated
property instead, which the generator never reads, and the member silently loses its
description or constraint instead of failing to build.

🚨 **Do not give the context a non-default naming policy.** Binding deserializes the
whole object through `JsonSerializerContext.Default`, so a
`[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]`
on the context changes what JSON KEY binding expects (`camelCase`) while the schema
still advertises the member's declared name (`PascalCase`, unchanged) — a model that
sends exactly what the schema shows leaves the member silently unset instead of
throwing. Leave the context's naming policy at its default until the schema side
picks one too.

For any other type — a `Dictionary<,>`, a tuple, a type with more than one public
constructor — register the tool by hand instead:

```csharp
public sealed record OrderFilter(string Status, int MinAmount);

[JsonSerializable(typeof(OrderFilter))]
internal partial class OrderFilterJsonContext : JsonSerializerContext;

agentPrism.Services.AddSingleton<AgentPrismToolRegistration>(provider =>
    new(AIFunctionFactory.Create(
        (OrderFilter filter) => SearchOrders(filter),
        "search_orders",
        "Searches orders matching a filter.",
        OrderFilterJsonContext.Default.Options)));
```

The source generator preserves all this metadata. For a complex result, declare its
`JsonSerializerContext` in your own source as shown above; Roslyn does not let one
source generator feed a context to another in the same compilation. The context makes
the result canonical JSON, so content guards, the output limit, run records, and the
model inspect the same data. Return `AIContent` only for the existing attachment
contract; attachments are not inline output and are not subject to `MaxOutputBytes`.

Do not resolve dependencies from `AIFunctionArguments.Services`: MAF supplies an empty
provider. Resolve a singleton dependency when you register an `AIFunction`, as shown
above.

```csharp
var repository = provider.GetRequiredService<IOrderRepository>();
agentPrism.AddTool(AIFunctionFactory.Create(
    (string orderId) => repository.Find(orderId), "get_order", "Fetches an order."));
```

`ToolRegistrationOptions` contains `RequiresApproval`, `Effect`,
`RequiredPermission`, `Timeout`, `SafeToRepeat`, `MaxOutputBytes`, and `Source`.
Code-defined tools normally leave `Source` unset.

Do not replace `IToolRegistry`. It is AgentPrism's immutable startup snapshot and owns
authorization, timeout, approval, and output truncation. Startup rejects an unverified
replacement unless `Tools.AllowUnverifiedToolRegistry` is explicitly enabled.

Timeout is not a forced abort: the model stops waiting, but a tool body can still have
started an external side effect. Make external calls idempotent. AgentPrism records and
streams controlled error text, but arguments and successful results can be persisted;
never return a secret.

## Call pipeline order

Every registration — code-defined or MCP-sourced — passes through the same layers, in
the same fixed order, before your method body ever runs:

```mermaid
flowchart LR
    accTitle: Tool call pipeline order
    accDescr: A tool call passes through authorization, argument validation, timeout, approval, and output truncation, in that fixed order, before the real method body runs.
    A["Authorization<br/>(can this caller call it at all)"] --> B["Argument validation<br/>(are these arguments acceptable)"]
    B --> C["Timeout<br/>(bounds the call)"]
    C --> D["Approval<br/>(does a person need to say yes)"]
    D --> E["Output truncation"]
    E --> F["your method body"]
```

Authorization runs first: asking a person to approve, waiting out a timeout, or
validating arguments for a call the caller could never make at all is backwards.
Argument validation runs next, still before the timeout and the approval wait — a
malformed call should not consume a timeout budget or wait on a human decision. The
scope `AddScopedTool` opens (below) surrounds only the innermost box, `your method
body`: nothing outside it ever resolves a scoped dependency.

## Scoped dependencies

A singleton dependency is resolved once, at registration. When the dependency has to
be fresh **per call** — a repository, a `DbContext` — use `AddScopedTool` instead of
`AddTool`. Its shape is identical; the difference is one word, and the word means
exactly this: every call opens its own dependency-injection scope, exposes it through
`AIFunctionArguments.Services`, and closes it as soon as the call ends.

```csharp
agentPrism.AddScopedTool(
    AIFunctionFactory.Create(
        async (string orderId, AIFunctionArguments arguments) =>
        {
            var writer = arguments.Services!.GetRequiredService<IOrderWriter>();
            return await writer.SubmitAsync(orderId);
        },
        "submit_order",
        "Submits an order."),
    options =>
    {
        options.RequiresApproval = true;
        options.Effect = ToolEffect.External;
        options.RequiredPermission = "orders.submit";
        options.Timeout = TimeSpan.FromSeconds(30);
        options.SafeToRepeat = true;
        options.MaxOutputBytes = 4096;
    });
```

`AIFunctionArguments.Services` is real inside a tool registered this way — the empty
provider MAF otherwise supplies is only ever seen by a plain `AddTool`/`AddToolsFrom`
registration. Two concurrent calls to the same scoped tool never share a scope. An
instance method marked `[AgentPrismTool]` still cannot be a tool (the source generator
rejects it at scan time) — the rejection message points here.

## Argument validation

Binding a call's JSON arguments already rejects a type mismatch, a missing `required`
field, or an invalid `enum` value, but it never rejects an extra field the schema does
not declare. Register `IToolArgumentsValidator` to add your own check, applied
uniformly before every server-side call — a code-defined tool and an MCP-sourced tool
share the same gate:

```csharp
public sealed class NoExtraFieldsValidator : IToolArgumentsValidator
{
    public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
        ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
        => arguments.Count > ExpectedFieldCount(tool)
            ? new(ToolArgumentsValidationResult.Invalid("Unexpected argument field."))
            : new(ToolArgumentsValidationResult.Valid);
}

services.AddSingleton<IToolArgumentsValidator, NoExtraFieldsValidator>();
```

There is no built-in JSON Schema validator — validation stays inside your own trust
boundary, the same stance [structured output](/guides/structured-output/) takes on the
response side. Nothing is registered by default, so an application that never adds a
validator keeps today's behavior exactly. A rejected call is recorded as `ToolFailed`,
never runs the real body, and the model receives your `Invalid` reason as its result —
never write an argument's actual value into that reason; a run event is persistent and
an argument can carry a secret. If your validator throws, the call is rejected
(fail-closed), the same way a throwing `IToolAuthorizationHandler` denies instead of
crashing the run.

Writing `NoExtraFieldsValidator` above does not prove it actually rejects a
malformed call — a validator that always returns `Valid` compiles just as
cleanly. `ToolArgumentValidationContract` fuzzes your validator against one of
your own tools:

```csharp
using AgentPrism.Testing.Contracts.Tools;
using Microsoft.Extensions.AI;

public sealed class NoExtraFieldsValidatorTests : ToolArgumentValidationContract
{
    protected override ValueTask<AIFunction> CreateToolAsync() => new(MyOrderTool);

    protected override ValueTask<IToolArgumentsValidator> CreateValidatorAsync()
        => new(new NoExtraFieldsValidator());
}
```

It reads `MyOrderTool`'s own JSON Schema and tries a missing required field, a
type mismatch, an out-of-range number, a pattern violation, an unrecognized
extra property, and a call crafted to fault a naive validator's own code —
every one must end up rejected (a thrown exception counts too; that is what
fail-closed means). A scenario your tool's schema does not exercise (no
numeric bound, no pattern) is skipped with an explicit reason instead of
passing silently. `ToolAuthorizationContract` does the same for
`IToolAuthorizationHandler`, except authorization has no schema to derive an
"invalid" call from — you supply `DeniedRequest` and `AllowedRequest`, two
calls your own policy decides oppositely, the same way `CustomToolContract`
below asks for the tool's expected result.

## Prove the registration

The `AgentPrism.Testing.Contracts.Xunit` package ships `CustomToolContract`.
Derive it in your test project to check the registration name, metadata, and concurrent
server-side invocation.

```csharp
using AgentPrism.Testing.Contracts;
using AgentPrism.Testing.Contracts.Tools;

public sealed class OrderToolTests : CustomToolContract
{
    protected override ValueTask<AgentPrismToolRegistration> CreateRegistrationAsync()
        => new(new AgentPrismToolRegistration(MyOrderTool));
}

[Fact]
public void All_tool_contracts_are_covered()
    => ContractCoverage.MissingDerivedTypes(
        typeof(OrderToolTests).Assembly,
        ContractCoverage.ToolContracts).ShouldBeEmpty();
```

## Read next

- [Tools, skills, and MCP](/concepts/tools/) — the governance model
- [Adding a tool](/getting-started/tools/) — the generated and reflection paths
