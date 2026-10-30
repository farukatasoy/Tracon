using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The tool registry created from <see cref="TraconToolRegistration"/> entries.
/// </summary>
internal sealed class ToolRegistry : IToolRegistry, IVerifiedToolRegistry
{
    // The provider whose registry this thread is building, or null. Thread-static: the container resolves a
    // singleton factory on the thread that asked, and another thread waits on the singleton lock.
    [ThreadStatic]
    private static IServiceProvider? _buildingFor;

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

        // 🚨 A tool factory (AddTool(provider => ...)) runs inside GetServices below. One that asks for
        // IToolRegistry, or for a service that needs it, resolves this same singleton again before it exists;
        // the container does not see through a factory delegate, so it recurses until the process dies
        // with a stack overflow. The flag turns that into an error that names the cause.
        if (ReferenceEquals(_buildingFor, provider))
        {
            throw new TraconException(
                "A tool factory registered with AddTool asked for IToolRegistry, or for a service that depends on it, " +
                "while the tool registry was being built from the result of that factory. A factory builds a tool; " +
                "take the dependencies the tool needs, not the registry.");
        }

        var previous = _buildingFor;
        _buildingFor = provider;

        try
        {
            return CreateCore(provider);
        }
        finally
        {
            _buildingFor = previous;
        }
    }

    private static ToolRegistry CreateCore(IServiceProvider provider)
    {
        var registrations = provider.GetServices<TraconToolRegistration>().ToList();
        var images = provider.GetRequiredService<IOptions<TraconImageOptions>>().Value;

        if (images.Enabled)
        {
            var generator = provider.GetRequiredService<ImageGeneratorResolver>().Resolve();

            _ = generator;
            // 🚨 An explicit timeout, NOT the installation default. The generic
            // 30s default is meant for a tool that queries something; a measured
            // gpt-image-1 request routinely needs 30-35s and was being cut off
            // just before it succeeded.
            registrations.Add(new TraconToolRegistration(new GenerateImageTool(provider))
            {
                Effect = ToolEffect.External,
                Timeout = images.Timeout,
            });
        }

        return new ToolRegistry(
            registrations,
            provider.GetRequiredService<IToolAuthorizationHandler>(),
            provider.GetRequiredService<TenantToolPolicyGate>(),
            provider.GetRequiredService<IToolArgumentsValidator>(),
            provider.GetRequiredService<IOptionsMonitor<TraconOptions>>(),
            provider.GetService<IRunAttributionContext>(),
            provider.GetRequiredService<ILogger<AuthorizingAIFunction>>(),
            provider.GetRequiredService<ILogger<TimeoutAIFunction>>(),
            provider.GetRequiredService<ILogger<ValidatingAIFunction>>());
    }
#pragma warning restore MEAI001

    /// <summary>Initializes a new registry from registrations.</summary>
    /// <param name="registrations">The tool registrations.</param>
    /// <param name="authorizationHandler">The authorization policy applied before every server-side call.</param>
    /// <param name="policyGate">The tenant tool policy applied before <paramref name="authorizationHandler"/>.</param>
    /// <param name="validator">The argument validation policy applied before every server-side call.</param>
    /// <param name="optionsMonitor">Supplies the installation's default tool timeout.</param>
    /// <param name="attribution">The run attribution context, or <see langword="null"/> when none is registered.</param>
    /// <param name="authorizingLogger">The logger passed to every <see cref="AuthorizingAIFunction"/> instance.</param>
    /// <param name="timeoutLogger">The logger passed to every <see cref="TimeoutAIFunction"/> instance.</param>
    /// <param name="validatingLogger">The logger passed to every <see cref="ValidatingAIFunction"/> instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// The same name is registered more than once, or a client-side tool
    /// (one whose body is not an <see cref="AIFunction"/>) is registered
    /// with <c>RequiresApproval = true</c>.
    /// </exception>
    public ToolRegistry(
        IEnumerable<TraconToolRegistration> registrations,
        IToolAuthorizationHandler authorizationHandler,
        TenantToolPolicyGate policyGate,
        IToolArgumentsValidator validator,
        IOptionsMonitor<TraconOptions> optionsMonitor,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> authorizingLogger,
        ILogger<TimeoutAIFunction> timeoutLogger,
        ILogger<ValidatingAIFunction> validatingLogger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(authorizationHandler);
        ArgumentNullException.ThrowIfNull(policyGate);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(authorizingLogger);
        ArgumentNullException.ThrowIfNull(timeoutLogger);
        ArgumentNullException.ThrowIfNull(validatingLogger);

        _tools = new Dictionary<string, AIFunctionDeclaration>(StringComparer.Ordinal);
        _descriptors = [];

        var defaultTimeout = optionsMonitor.CurrentValue.Tools.DefaultTimeout;
        var defaultMaxOutputBytes = optionsMonitor.CurrentValue.Tools.DefaultMaxOutputBytes;

        foreach (var registration in registrations)
        {
            var name = registration.Function.Name;

            // The registry is the only place that enforces the "an agent can
            // only refer to a registered tool" rule; every wrapper comes from
            // ToolWrapperChain.Compose, the single composition point shared
            // with the MCP tenant tool set — a call site
            // building its own chain by hand could silently miss a layer.
            var descriptor = new ToolDescriptor
            {
                Name = name,
                Description = registration.Function.Description,
                JsonSchema = registration.Function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Undefined
                    ? null
                    : registration.Function.JsonSchema.GetRawText(),
                RequiresApproval = RequiresApproval(registration),
                Source = registration.Source,
                RunsOnClient = registration.Function is not AIFunction,
                Effect = registration.Effect,
                RequiredPermission = registration.RequiredPermission,
                Timeout = registration.Timeout,
                SafeToRepeat = registration.SafeToRepeat,
                MaxOutputBytes = registration.MaxOutputBytes,
            };

            var function = ToolWrapperChain.Compose(
                registration,
                descriptor,
                authorizationHandler,
                policyGate,
                validator,
                defaultTimeout,
                defaultMaxOutputBytes,
                attribution,
                authorizingLogger,
                timeoutLogger,
                validatingLogger);

            if (!_tools.TryAdd(name, function))
            {
                throw new TraconException(
                    $"More than one tool is registered with name '{name}'. Tool names must be unique.");
            }

            _descriptors.Add(descriptor);
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

    /// <summary>Whether the tool asks for approval before it runs.</summary>
    /// <param name="registration">The registration.</param>
    /// <returns><see langword="true"/> when the registration says so or the tool is already wrapped for approval.</returns>
    /// <remarks>
    /// <see cref="FunctionInvokingChatClient"/> finds an <see cref="ApprovalRequiredAIFunction"/> through every
    /// wrapper, so a tool the consumer or the framework wrapped asks for approval whatever the registration says.
    /// Microsoft Agent Framework wraps by default (<c>DockerShellExecutor.AsAIFunction()</c>,
    /// <c>FileAccessProvider</c>). The descriptor said "no approval" for such a tool, and the external-surface
    /// guard and replay, which read the descriptor, let it through (MAF-KAPSAMA §0 #45).
    /// </remarks>
    private static bool RequiresApproval(TraconToolRegistration registration)
        => registration.RequiresApproval
            || registration.Function.GetService<ApprovalRequiredAIFunction>() is not null;
}
