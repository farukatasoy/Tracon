using System.Text.RegularExpressions;

namespace AgentPrism.Generators.UnitTests.Examples;

/// <summary>
/// Wraps one extracted C# block in the declarations a shipped example assumes
/// but never states - the surrounding application a consumer would already have.
/// </summary>
/// <remarks>
/// <para>
/// The variable placeholders are measured, not guessed: across the 45 C#
/// blocks (49 total minus 4 JSON configuration fragments), <c>builder</c> (an
/// <see cref="IHostApplicationBuilder"/>) is the free name in 36 and <c>app</c>
/// (an <see cref="IEndpointRouteBuilder"/>) in 6; the rest recur once to five
/// times each. A placeholder is only emitted when the block does not already
/// declare that name itself - one block writes its own <c>var builder = ...</c>,
/// and emitting the placeholder unconditionally would double-declare it (CS0128).
/// </para>
/// <para>
/// A handful of blocks name a type that exists only to illustrate an extension
/// point (<c>OnPremiseModelProvider</c>, <c>OrderTools</c>, <c>IOrderGateway</c>,
/// <c>IOrderRepository</c>, <c>NightlyReportJobHandler</c>, <c>GitAgentSource</c>,
/// <c>ResponseQualityJudge</c>, <c>AuditingAgentDecorator</c>,
/// <c>OrderDeskAuthorization</c>, <c>LoggingEvaluatorFactory</c>): "a
/// provider/tool/service/repository/handler/source/judge/decorator/authorization you wrote yourself". Those get a
/// minimal stub here for the same reason - a real consumer would have written
/// one, and the doc text stays untouched.
/// </para>
/// <para>
/// One block IS a type declaration (a worked <c>[AgentPrismTool]</c> class), not
/// a statement; it is placed at namespace scope directly, alongside the stub
/// types above, instead of inside a method body.
/// </para>
/// </remarks>
internal static class ExamplePrelude
{
    private static readonly (string Type, string Name, string Value)[] Placeholders =
    [
        ("IHostApplicationBuilder", "builder", "null!"),
        ("IEndpointRouteBuilder", "app", "null!"),
        ("IAgentPrismBuilder", "agentPrism", "null!"),
        ("string", "apiKey", "\"api-key\""),
        ("string", "connectionString", "\"Data Source=agentprism.db\""),
        ("IConfiguration", "configuration", "null!"),
        ("AIFunction", "refundTool", "null!"),
        ("Uri", "endpoint", "null!"),
        ("SpeechAudio", "audio", "null!"),
        ("SpeechRequest", "request", "null!"),
        ("decimal?", "price", "null"),
        ("string[]", "args", "[]"),
    ];

    private static readonly Regex SelfDeclaredVariablePattern = new(
        @"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex TypeDeclarationPattern = new(
        @"^(?:(?:public|internal|private|protected|static|sealed|abstract|partial)\s+)*(?:class|struct|record|interface|enum)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.AI;
        using Microsoft.Extensions.AI.Evaluation;
        using Microsoft.Extensions.AI.Evaluation.Quality;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Agents.AI;
        using Microsoft.Agents.AI.Workflows;
        using Azure.Identity;
        using AgentPrism;
        using AgentPrism.Client;
        using AgentPrism.Testing;
        """;

    private const string StubTypes = """
        // Stands in for "a provider you wrote yourself" - the shipped example
        // names it to show the extension point, not to ship the type.
        internal sealed class OnPremiseModelProvider(Uri endpoint) : IModelProvider
        {
            public string Name => "on-premise";

            public IReadOnlyList<ModelDescriptor> Models { get; } = [];

            public IChatClient CreateChatClient(ModelBinding binding) => null!;
        }

        // Stands in for "your own DI service", named in one <example> only.
        internal interface IOrderGateway;

        internal sealed class OrderGateway : IOrderGateway;

        // Stands in for "your own scoped repository", named in one <example> only.
        internal interface IOrderRepository
        {
            Task<string> GetAsync(string orderId);
        }

        // Stands in for "your own scheduled job", named in one <example> only.
        internal sealed class NightlyReportJobHandler : IJobHandler
        {
            public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        }

        // Stands in for "your own content guard", named in one <example> only.
        internal sealed class CustomerNameGuard : IContentGuard
        {
            public string Name => "customer-name";

            public ValueTask<ContentGuardResult> InspectAsync(ContentGuardContext context, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ContentGuardResult.Allow);
        }

        // Stands in for "your own agent source", named in one <example> only.
        internal sealed class GitAgentSource : IAgentSource
        {
            public string Name => "git";

            public int Priority => 0;

            public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default) => default;

            public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default) => default;
        }

        // Stands in for "your own run/session authorization handler", named in
        // one <example> only.
        internal sealed class OrderDeskAuthorization : IRunAuthorizationHandler
        {
            public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default) => default;

            public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default) => default;
        }

        // Stands in for "your own run judge", named in one <example> only.
        internal sealed class ResponseQualityJudge : IRunJudge
        {
            public string Name => "response-quality";

            public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default) => default;
        }

        // Stands in for "your own eval-evaluator factory", named in two <example> blocks.
        internal sealed class LoggingEvaluatorFactory : IEvalEvaluatorFactory
        {
            public IAgentEvaluator Create(IReadOnlyList<EvalCheck> checks) => new LocalEvaluator([.. checks]);
        }

        // Stands in for "your own agent decorator", named in two <example> blocks.
        internal sealed class AuditingAgentDecorator(IAuditLog auditLog) : IAgentDecorator
        {
            public int Order => 5;

            public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor) => agent;
        }
        """;

    /// <summary>The running "order status" tool shown across several unrelated examples.</summary>
    /// <remarks>
    /// Not <c>static</c>: a static class cannot be a generic type argument, and
    /// <c>AddToolsFrom&lt;OrderTools&gt;()</c> (a different block) names this same
    /// type that way. The tool method itself stays <c>static</c> - APG0007
    /// requires that regardless of the container.
    /// </remarks>
    private const string OrderToolsStub = """
        internal class OrderTools
        {
            [AgentPrismTool("get_order_status", "Returns an order's shipping status.")]
            public static string GetOrderStatus(string orderId) => "shipped";
        }
        """;

    public static string Wrap(string code)
    {
        if (TypeDeclarationPattern.IsMatch(code.TrimStart()))
        {
            // The block IS the declaration (a worked [AgentPrismTool] class):
            // placed directly at namespace scope, no surrounding method or
            // OrderTools stub - the block supplies that type itself.
            return $$"""
                {{Usings}}

                namespace AgentPrism.Generators.UnitTests.Examples.Harness;

                {{StubTypes}}

                {{code}}
                """;
        }

        var selfDeclared = SelfDeclaredVariablePattern.Matches(code)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var declarations = string.Join(
            '\n',
            Placeholders
                .Where(placeholder => !selfDeclared.Contains(placeholder.Name))
                .Select(placeholder => $"        {placeholder.Type} {placeholder.Name} = {placeholder.Value};"));

        // AddToolsFrom<OrderTools>() and the [AgentPrismTool] example both name
        // "OrderTools"; only the latter declares it, so every other block gets
        // this stub instead of redeclaring what that one block already supplies.
        var orderTools = code.Contains("class OrderTools", StringComparison.Ordinal) ? string.Empty : OrderToolsStub;

        return $$"""
            {{Usings}}

            namespace AgentPrism.Generators.UnitTests.Examples.Harness;

            {{StubTypes}}

            {{orderTools}}

            internal static class Harness
            {
                private static void Body()
                {
                    // Stands in for "a workflow graph you built", named in one <example> only.
                    static Workflow BuildTriageGraph(IServiceProvider provider) => null!;

            {{declarations}}

            {{Indent(code)}}
                }
            }
            """;
    }

    private static string Indent(string code)
        => string.Join('\n', code.Split('\n').Select(line => line.Length == 0 ? line : "        " + line));
}
