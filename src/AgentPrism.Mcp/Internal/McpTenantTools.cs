using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// An immutable snapshot of a single tenant's discovered MCP tools.
/// </summary>
internal sealed class McpTenantTools
{
    private readonly Dictionary<string, AIFunctionDeclaration> _tools;

    private McpTenantTools(Dictionary<string, AIFunctionDeclaration> tools, IReadOnlyList<ToolDescriptor> descriptors)
    {
        _tools = tools;
        Descriptors = descriptors;
    }

    /// <summary>The empty set.</summary>
    public static McpTenantTools Empty { get; } = new([], []);

    /// <summary>The tool descriptors shown in the UI; sorted by name.</summary>
    public IReadOnlyList<ToolDescriptor> Descriptors { get; }

    /// <summary>Gets the tool with the given name.</summary>
    /// <param name="name">The tool name.</param>
    /// <param name="tool">The tool found.</param>
    /// <returns><see langword="true"/> if the tool is registered.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool)
        => _tools.TryGetValue(name, out tool);

    /// <summary>Builds an immutable set from registrations.</summary>
    /// <param name="registrations">The discovered tool registrations.</param>
    /// <param name="logger">The logger name collisions are reported to.</param>
    /// <param name="authorizationHandler">The authorization policy applied before every call.</param>
    /// <param name="validator">The argument validation policy applied before every call.</param>
    /// <param name="defaultTimeout">The timeout applied when a tool's own registration sets none.</param>
    /// <param name="attribution">The run attribution context, or <see langword="null"/> when none is registered.</param>
    /// <param name="authorizingLogger">The logger passed to every <see cref="AuthorizingAIFunction"/> instance.</param>
    /// <param name="timeoutLogger">The logger passed to every <see cref="TimeoutAIFunction"/> instance.</param>
    /// <param name="validatingLogger">The logger passed to every <see cref="ValidatingAIFunction"/> instance.</param>
    /// <param name="defaultMaxOutputBytes">
    /// The output byte limit applied when a tool's own registration sets
    /// none, or <see langword="null"/> for unlimited.
    /// </param>
    /// <returns>The set.</returns>
    /// <remarks>
    /// A name collision is <strong>not an error</strong>. In the code
    /// registry a collision is caught at compile time, and throwing there is
    /// correct; in MCP the name comes from a remote server, and two servers
    /// producing the same name must not crash AgentPrism. The second
    /// registration is skipped and a warning is logged.
    /// </remarks>
    public static McpTenantTools Create(
        IReadOnlyList<AgentPrismToolRegistration> registrations,
        ILogger logger,
        IToolAuthorizationHandler authorizationHandler,
        IToolArgumentsValidator validator,
        TimeSpan defaultTimeout,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> authorizingLogger,
        ILogger<TimeoutAIFunction> timeoutLogger,
        ILogger<ValidatingAIFunction> validatingLogger,
        int? defaultMaxOutputBytes = null)
    {
        var tools = new Dictionary<string, AIFunctionDeclaration>(registrations.Count, StringComparer.Ordinal);
        var descriptors = new List<ToolDescriptor>(registrations.Count);

        foreach (var registration in registrations)
        {
            var name = registration.Function.Name;

            // Wrapping goes through ToolWrapperChain.Compose — the same
            // composition point ToolRegistry uses (docs/127, 127.1). MCP
            // tools do not go through that registry, so this call site is
            // still separate, but the wrapper CHAIN itself is no longer
            // built by hand here: a layer added to Compose reaches this path
            // automatically. MCP tools are always real AIFunctions
            // (McpClientTool : AIFunction); Compose's approval-without-body
            // guard only ever fires for a misconfigured direct
            // AgentPrismToolRegistration registration, same as in ToolRegistry.
            //
            // An MCP tool's definition lives on a remote server the moment it is
            // read; it never carries its own effect classification, so it
            // defaults to External — the most cautious class, not Read. This
            // promotion is the one difference from ToolRegistry's own effect
            // handling, and it is a CALL-SITE decision made before Compose is
            // ever invoked (docs/127, 127.1).
            var effect = registration.Effect == ToolEffect.Read ? ToolEffect.External : registration.Effect;

            var descriptor = new ToolDescriptor
            {
                Name = name,
                Description = registration.Function.Description,
                JsonSchema = registration.Function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Undefined
                    ? null
                    : registration.Function.JsonSchema.GetRawText(),
                RequiresApproval = registration.RequiresApproval,
                Source = registration.Source,
                RunsOnClient = registration.Function is not AIFunction,
                Effect = effect,
                RequiredPermission = registration.RequiredPermission,
                Timeout = registration.Timeout,
                MaxOutputBytes = registration.MaxOutputBytes,
            };

            var function = ToolWrapperChain.Compose(
                registration,
                descriptor,
                authorizationHandler,
                validator,
                defaultTimeout,
                defaultMaxOutputBytes,
                attribution,
                authorizingLogger,
                timeoutLogger,
                validatingLogger);

            if (!tools.TryAdd(name, function))
            {
                logger.LogWarning(
                    "MCP tool name '{ToolName}' was produced more than once; the second registration was skipped. " +
                    "Choose server names that are distinguishable from one another.",
                    name);

                continue;
            }

            descriptors.Add(descriptor);
        }

        descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return new McpTenantTools(tools, descriptors);
    }
}
