using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// The tool registry created from <see cref="AgentPrismToolRegistration"/> entries.
/// </summary>
internal sealed class ToolRegistry : IToolRegistry, IVerifiedToolRegistry
{
    private readonly Dictionary<string, AIFunctionDeclaration> _tools;
    private readonly List<ToolDescriptor> _descriptors;

    /// <summary>Builds the registry from the final service-provider state.</summary>
    /// <param name="provider">The root service provider.</param>
    /// <returns>The assembled tool registry.</returns>
    /// <remarks>
    /// Image generation is a two-key feature: the image option must be enabled
    /// and an <see cref="IImageGenerator"/> must be registered. Resolving this
    /// here preserves the default-off rule: merely referencing a provider package does not expose
    /// a paid tool to agent definitions.
    /// </remarks>
    // MEAI 10.9.0 marks IImageGenerator experimental (MEAI001), although it is
    // the framework's only image-generation abstraction. The use is limited to
    // registry gating; provider adapters and the tool own the call path.
#pragma warning disable MEAI001
    internal static ToolRegistry Create(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var registrations = provider.GetServices<AgentPrismToolRegistration>().ToList();
        var images = provider.GetRequiredService<IOptions<AgentPrismImageOptions>>().Value;

        if (images.Enabled)
        {
            var generator = provider.GetRequiredService<ImageGeneratorResolver>().Resolve();

            _ = generator;
            registrations.Add(new AgentPrismToolRegistration(
                new GenerateImageTool(provider),
                effect: ToolEffect.External));
        }

        return new ToolRegistry(
            registrations,
            provider.GetRequiredService<IToolAuthorizationHandler>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
            provider.GetService<IRunAttributionContext>(),
            provider.GetRequiredService<ILogger<AuthorizingAIFunction>>(),
            provider.GetRequiredService<ILogger<TimeoutAIFunction>>());
    }
#pragma warning restore MEAI001

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
        var defaultMaxOutputBytes = optionsMonitor.CurrentValue.Tools.DefaultMaxOutputBytes;

        foreach (var registration in registrations)
        {
            var name = registration.Function.Name;

            // Every wrapper is applied here, not in the compiler. The registry is
            // the only place that enforces the "an agent can only refer to a registered
            // tool" rule. Enforcing wrapping here prevents another code path from bypassing it.
            //
            // Composition order (docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md, 69.1;
            // docs/arsiv/fazlar/89-TOOL-CIKTISI-BOYUT-SINIRI.md, 89.3):
            // Authorizing (outermost) -> Timeout -> ApprovalRequired -> Truncating (innermost) -> real function.
            // Authorization runs before anything else: asking for approval or waiting
            // out a timeout for a call the caller could never make is backwards.
            // Timeout sits OUTSIDE approval: ApprovalRequiredAIFunction never blocks on
            // the human decision within one call (K-368 — the decision resumes as a NEW
            // run), so this ordering only ever bounds the tool's own execution.
            // Truncating sits directly around the real function, INSIDE approval: it
            // must see only the tool's own output, never the pending-approval signal
            // ApprovalRequiredAIFunction produces instead of running the body.
            AIFunctionDeclaration function;

            if (registration.Function is AIFunction invocable)
            {
                var effectiveMaxOutputBytes = registration.MaxOutputBytes ?? defaultMaxOutputBytes;

                AIFunction wrapped = effectiveMaxOutputBytes is { } maxOutputBytes
                    ? new TruncatingAIFunction(invocable, maxOutputBytes)
                    : invocable;

                wrapped = registration.RequiresApproval
                    ? new ApprovalRequiredAIFunction(wrapped)
                    : wrapped;

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
                MaxOutputBytes = registration.MaxOutputBytes,
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
