using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Tracon;

/// <summary>
/// An immutable snapshot of a single tenant's discovered MCP tools.
/// </summary>
internal sealed class McpTenantTools
{
    /// <summary>The binding reported for a name this snapshot does not serve.</summary>
    public const string AbsentBinding = "absent";

    /// <summary>The binding reported for a served tool whose connection is not known.</summary>
    public const string UnboundBinding = "unbound";

    private readonly Dictionary<string, AIFunctionDeclaration> _tools;
    private readonly Dictionary<string, string> _sources;
    private readonly Dictionary<string, string> _bindings;
    private readonly string[] _serverPrefixes;

    private McpTenantTools(
        Dictionary<string, AIFunctionDeclaration> tools,
        Dictionary<string, string> sources,
        Dictionary<string, string> bindings,
        IReadOnlyList<ToolDescriptor> descriptors,
        string[] serverPrefixes)
    {
        _tools = tools;
        _sources = sources;
        _bindings = bindings;
        Descriptors = descriptors;
        _serverPrefixes = serverPrefixes;
    }

    /// <summary>The empty set.</summary>
    public static McpTenantTools Empty { get; } = new([], [], [], [], []);

    /// <summary>The tool descriptors shown in the UI; sorted by name.</summary>
    public IReadOnlyList<ToolDescriptor> Descriptors { get; }

    /// <summary>Gets the tool with the given name.</summary>
    /// <param name="name">The tool name.</param>
    /// <param name="tool">The tool found.</param>
    /// <returns><see langword="true"/> if the tool is registered.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool)
        => _tools.TryGetValue(name, out tool);

    /// <summary>Gets the source fingerprint of the tool with the given name.</summary>
    /// <param name="name">The tool name.</param>
    /// <returns>
    /// The fingerprint of the endpoint the tool in this snapshot came from, or
    /// <see langword="null"/> when the snapshot holds no such tool.
    /// </returns>
    /// <remarks>
    /// Read from the same snapshot as the tool itself, so the source a rule is
    /// judged against is the one the tool object was discovered from, not a
    /// connection a refresh in progress has already replaced.
    /// </remarks>
    public string? GetSourceFingerprint(string name)
    {
        if (_sources.TryGetValue(name, out var fingerprint))
        {
            return fingerprint;
        }

        // Not in this snapshot, but named like a tool of one of the tenant's
        // servers: the server's last refresh failed and dropped its tools, while
        // a compiled agent may still hold one bound to the live connection. Its
        // source is unknown, not absent - so no rule approves it.
        foreach (var prefix in _serverPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return ToolSourceFingerprint.Unresolved;
            }
        }

        return null;
    }

    // F-305 / K-880: a dropped list counts as absent by user decision. The catalog
    // keeps the last good list through a transient failure (F-309), so a blip reaches
    // this method as the same binding, not as absent.
    /// <summary>Gets the binding of the tool with the given name.</summary>
    /// <param name="name">The tool name.</param>
    /// <returns>
    /// The <see cref="McpConnection.Generation"/> of the connection the tool in this
    /// snapshot calls through, joined with a hash of the tool's own definition;
    /// <see cref="UnboundBinding"/> when the tool is served but its connection is not
    /// known; <see cref="AbsentBinding"/> when the snapshot holds no such tool.
    /// </returns>
    /// <remarks>
    /// <para>
    /// "Absent" covers a deleted or disabled server and a server whose tools were
    /// dropped after failed refreshes alike: a compiled agent keyed with a live binding
    /// is then not served, and the next resolve fails the way an uncached compile does.
    /// </para>
    /// <para>
    /// The definition hash covers what the server said about the tool (its
    /// description and input schema among the rest). A server can change it while the
    /// connection stays the same; the compiled agent holds the tool object that carries
    /// the old definition, so the hash makes the next resolve compile against the new
    /// one.
    /// </para>
    /// </remarks>
    public string GetBindingFingerprint(string name)
    {
        if (_bindings.TryGetValue(name, out var binding))
        {
            return binding;
        }

        return _tools.ContainsKey(name) ? UnboundBinding : AbsentBinding;
    }

    /// <summary>Builds an immutable set from registrations.</summary>
    /// <param name="registrations">The discovered tool registrations.</param>
    /// <param name="logger">The logger name collisions are reported to.</param>
    /// <param name="authorizationHandler">The authorization policy applied before every call.</param>
    /// <param name="policyGate">The tenant tool policy applied before <paramref name="authorizationHandler"/>.</param>
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
    /// <param name="sourceFingerprints">
    /// The source fingerprint of each registration (the endpoint of the server it
    /// was discovered from), or <see langword="null"/> when none is known. A
    /// registration missing from the map has no source, and no standing approval
    /// rule written for a source approves it.
    /// </param>
    /// <param name="serverNames">
    /// Every server registered for the tenant, reachable or not. A tool named like
    /// one of theirs but missing from the snapshot reports an unresolved source.
    /// </param>
    /// <param name="connectionGenerations">
    /// The <see cref="McpConnection.Generation"/> of the connection each registration
    /// calls through, or <see langword="null"/> when none is known. A served tool
    /// missing from the map reports <see cref="UnboundBinding"/>.
    /// </param>
    /// <returns>The set.</returns>
    /// <remarks>
    /// A name collision is <strong>not an error</strong>. In the code
    /// registry a collision is caught at compile time, and throwing there is
    /// correct; in MCP the name comes from a remote server, and two servers
    /// producing the same name must not crash Tracon. The second
    /// registration is skipped and a warning is logged.
    /// </remarks>
    public static McpTenantTools Create(
        IReadOnlyList<TraconToolRegistration> registrations,
        ILogger logger,
        IToolAuthorizationHandler authorizationHandler,
        TenantToolPolicyGate policyGate,
        IToolArgumentsValidator validator,
        TimeSpan defaultTimeout,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> authorizingLogger,
        ILogger<TimeoutAIFunction> timeoutLogger,
        ILogger<ValidatingAIFunction> validatingLogger,
        int? defaultMaxOutputBytes = null,
        IReadOnlyDictionary<TraconToolRegistration, string>? sourceFingerprints = null,
        IReadOnlyCollection<string>? serverNames = null,
        IReadOnlyDictionary<TraconToolRegistration, long>? connectionGenerations = null)
    {
        var tools = new Dictionary<string, AIFunctionDeclaration>(registrations.Count, StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(registrations.Count, StringComparer.Ordinal);
        var bindings = new Dictionary<string, string>(registrations.Count, StringComparer.Ordinal);
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
            // TraconToolRegistration registration, same as in ToolRegistry.
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
                policyGate,
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

            // Recorded only for the registration that WON the name above: the
            // skipped duplicate's server must not lend its endpoint to the tool
            // that is actually served under this name.
            if (sourceFingerprints is not null && sourceFingerprints.TryGetValue(registration, out var source))
            {
                sources[name] = source;
            }

            // Same rule: the connection behind the name is the winner's.
            if (connectionGenerations is not null && connectionGenerations.TryGetValue(registration, out var generation))
            {
                bindings[name] = string.Concat(
                    generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ":",
                    DefinitionHash(registration.Function));
            }

            descriptors.Add(descriptor);
        }

        descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        var prefixes = serverNames is null ? [] : serverNames.Select(static server => server + "_").ToArray();

        return new McpTenantTools(tools, sources, bindings, descriptors, prefixes);
    }

    // F-308: K-880 kept the remote definition out of the cache key until the cache had
    // a bound; CompiledAgentCache now holds one entry per definition slot, so a server
    // that edits a description on every refresh replaces an entry instead of adding one.
    /// <summary>Hashes the definition the tool object embeds, as the server sent it.</summary>
    /// <param name="function">The discovered tool.</param>
    /// <returns>The uppercase hex SHA-256 of the definition.</returns>
    /// <remarks>
    /// A remote tool hashes the server's whole protocol <see cref="Tool"/>, serialized
    /// with the MCP SDK's own source-generated options: the embedded thing itself, not a
    /// proxy for it. The generated <c>{server}_read_resource</c>
    /// tool hashes its name, description and schema; its body reads the resource list
    /// live, so nothing else of it is embedded.
    /// </remarks>
    private static string DefinitionHash(AIFunctionDeclaration function)
    {
        // The elicitation wrapper (Phase 235) is not part of what the server said: hash the tool inside it.
        var embedded = function is McpElicitingTool eliciting ? eliciting.Remote : function;

        var definition = embedded is McpClientTool remote
            ? JsonSerializer.SerializeToUtf8Bytes(remote.ProtocolTool, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(Tool)))
            : Encoding.UTF8.GetBytes(string.Join(
                '\n',
                function.Name,
                function.Description,
                function.JsonSchema.ValueKind == JsonValueKind.Undefined ? string.Empty : function.JsonSchema.GetRawText()));

        return Convert.ToHexString(SHA256.HashData(definition));
    }
}
