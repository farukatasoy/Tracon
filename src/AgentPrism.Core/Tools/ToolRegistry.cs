using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// The tool registry created from <see cref="AgentPrismToolRegistration"/> entries.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, AIFunctionDeclaration> _tools;
    private readonly List<ToolDescriptor> _descriptors;

    /// <summary>Initializes a new registry from registrations.</summary>
    /// <param name="registrations">The tool registrations.</param>
    /// <param name="authorizationHandler">The authorization policy applied before every server-side call.</param>
    /// <param name="optionsMonitor">Supplies the installation's default tool timeout.</param>
    /// <param name="attribution">The run attribution context, or <see langword="null"/> when none is registered.</param>
    /// <param name="authorizingLogger">The logger passed to every <see cref="AuthorizingAIFunction"/> instance.</param>
    /// <param name="timeoutLogger">The logger passed to every <see cref="TimeoutAIFunction"/> instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">
    /// The same name is registered more than once, or a client-side tool
    /// (one whose body is not an <see cref="AIFunction"/>) is registered
    /// with <c>requiresApproval: true</c>.
    /// </exception>
    public ToolRegistry(
        IEnumerable<AgentPrismToolRegistration> registrations,
        IToolAuthorizationHandler authorizationHandler,
        IOptionsMonitor<AgentPrismOptions> optionsMonitor,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> authorizingLogger,
        ILogger<TimeoutAIFunction> timeoutLogger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(authorizationHandler);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(authorizingLogger);
        ArgumentNullException.ThrowIfNull(timeoutLogger);

        _tools = new Dictionary<string, AIFunctionDeclaration>(StringComparer.Ordinal);
        _descriptors = [];

        var defaultTimeout = optionsMonitor.CurrentValue.Tools.DefaultTimeout;

        foreach (var registration in registrations)
        {
            var name = registration.Function.Name;

            // Every wrapper is applied here, not in the compiler. The registry is
            // the only place that enforces the "an agent can only refer to a registered
            // tool" rule. Enforcing wrapping here prevents another code path from bypassing it.
            //
            // Composition order (docs/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md, 69.1):
            // Authorizing (outermost) -> Timeout -> ApprovalRequired (innermost) -> real function.
            // Authorization runs before anything else: asking for approval or waiting
            // out a timeout for a call the caller could never make is backwards.
            // Timeout sits OUTSIDE approval: ApprovalRequiredAIFunction never blocks on
            // the human decision within one call (K-368 — the decision resumes as a NEW
            // run), so this ordering only ever bounds the tool's own execution.
            AIFunctionDeclaration function;

            if (registration.Function is AIFunction invocable)
            {
                AIFunction wrapped = registration.RequiresApproval
                    ? new ApprovalRequiredAIFunction(invocable)
                    : invocable;

                wrapped = new TimeoutAIFunction(wrapped, registration.Timeout ?? defaultTimeout, timeoutLogger);

                function = new AuthorizingAIFunction(
                    wrapped,
                    authorizationHandler,
                    registration.Effect,
                    registration.RequiredPermission,
                    attribution,
                    authorizingLogger);
            }
            else if (registration.RequiresApproval)
            {
                throw new AgentPrismException(
                    $"Tool '{name}' cannot require approval: it runs on the client and has no " +
                    "server-side body to defer. Approval and client-side tools are separate mechanisms.");
            }
            else
            {
                // Declaration-only (client-side) tool: the server never invokes it,
                // so there is no execution to authorize or bound with a timeout.
                function = registration.Function;
            }

            if (!_tools.TryAdd(name, function))
            {
                throw new AgentPrismException(
                    $"More than one tool is registered with name '{name}'. Tool names must be unique.");
            }

            _descriptors.Add(new ToolDescriptor
            {
                Name = name,
                Description = registration.Function.Description,
                JsonSchema = registration.Function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Undefined
                    ? null
                    : registration.Function.JsonSchema.GetRawText(),
                RequiresApproval = registration.RequiresApproval,
                Source = registration.Source,
                RunsOnClient = registration.Function is not AIFunction,
                Effect = registration.Effect,
                RequiredPermission = registration.RequiredPermission,
                Timeout = registration.Timeout,
                SafeToRepeat = registration.SafeToRepeat,
            });
        }

        _descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolDescriptor> List() => _descriptors;

    /// <inheritdoc />
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _tools.TryGetValue(name, out tool);
    }
}
