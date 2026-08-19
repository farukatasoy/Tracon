using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// A lazy <see cref="AIAgent"/> wrapper for connecting a catalog agent to the
/// A2A server.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddA2AServer(services, AIAgent agent, ...)</c> requires an OBJECT
/// INSTANCE, and this call must be made BEFORE the <see cref="IServiceProvider"/>
/// is built (<c>Build()</c>, inside <c>UseA2A()</c>) (section 50.5) — but the
/// real catalog agent can only be resolved through a built container. This
/// class reconciles the two timings with the same "lazy resolution" pattern
/// used by <see cref="CallableAgentResolver"/>: <see cref="AttachServices"/> is
/// called ONCE by <c>MapAgentPrismA2A()</c> (after the application is
/// <c>Build()</c>-ed), while the actual resolution happens on EVERY call.
/// </para>
/// <para>
/// DIFFERS from <see cref="ChildAgentInvoker"/>: that models one agent calling
/// ANOTHER agent and expects an ambient PARENT scope (depth, budget, tenant).
/// This class ALWAYS starts a new ROOT run — an external caller has no such
/// parent scope.
/// </para>
/// </remarks>
internal sealed class ExternalAgentProxy : AIAgent
{
    private readonly string _agentName;
    private IServiceProvider? _services;

    /// <summary>Creates a new lazily-resolved proxy.</summary>
    /// <param name="agentName">Name of the target agent in the catalog.</param>
    public ExternalAgentProxy(string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        _agentName = agentName;
    }

    /// <inheritdoc />
    public override string Name => _agentName;

    /// <summary>
    /// Budget template copied for each call. The object itself is NOT shared;
    /// each <c>RunCoreAsync</c> call produces its own <see cref="AgentRunBudget"/>
    /// instance.
    /// </summary>
    public AgentRunBudget BudgetTemplate { get; set; } = new() { MaxDepth = 1 };

    /// <summary>
    /// Attaches the root service provider after the application is <c>Build()</c>-ed.
    /// </summary>
    /// <param name="services">The application's service provider.</param>
    /// <remarks>
    /// Called ONCE per agent by <c>MapAgentPrismA2A()</c>. There is no call path
    /// before this point because the A2A server has not yet been connected to
    /// HTTP.
    /// </remarks>
    internal void AttachServices(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent
            .DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent
            .SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var runOptions = CreateRunOptions();

        var response = await agent.RunAsync(messages, session, runOptions, cancellationToken).ConfigureAwait(false);

        RefuseIfApprovalPending(ChildRunApproval.Describe(response.Messages));

        await ExternalCallAudit.WriteAsync(_services!, "a2a", _agentName, runOptions.RunId!.Value, cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var runOptions = CreateRunOptions();
        string? pendingApproval = null;

        await foreach (var update in agent.RunStreamingAsync(messages, session, runOptions, cancellationToken).ConfigureAwait(false))
        {
            pendingApproval ??= ChildRunApproval.Describe(update.Contents);

            yield return update;
        }

        RefuseIfApprovalPending(pendingApproval);

        await ExternalCallAudit.WriteAsync(_services!, "a2a", _agentName, runOptions.RunId!.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private void RefuseIfApprovalPending(string? pending)
    {
        if (pending is null)
        {
            return;
        }

        // Defense layer: the startup check (ExternalSurfaceGuard) already
        // prevents exposing an agent carrying a tool that requires approval,
        // but the definition can be updated AFTERWARD to add an approval-
        // requiring tool. This is the runtime enforcement of the same K-103
        // boundary.
        throw new AgentPrismExternalCallException(
            $"Agent '{_agentName}' could not complete: tool '{pending}' requires user approval. " +
            "An externally-invoked agent cannot respond to an approval request.")
        {
            AgentName = _agentName,
            Protocol = "a2a",
        };
    }

    private AgentPrismRunOptions CreateRunOptions()
        => new()
        {
            RunId = AgentPrismId.NewId(),

            // A NEW budget with the same template values. Sharing the object
            // itself would let all A2A calls, for the app's lifetime, consume a
            // single budget; each external call must be the root of its own tree.
            Budget = new AgentRunBudget
            {
                MaxDepth = BudgetTemplate.MaxDepth,
                MaxTotalTokens = BudgetTemplate.MaxTotalTokens,
                MaxTotalRuns = BudgetTemplate.MaxTotalRuns,
            },
        };

    private async ValueTask<AIAgent> ResolveAsync(CancellationToken cancellationToken)
    {
        var services = _services ?? throw new InvalidOperationException(
            $"The A2A proxy for '{_agentName}' is not attached to a service provider. " +
            "The call to MapAgentPrismA2A() may be missing.");

        var catalog = services.GetRequiredService<IAgentCatalog>();
        var agent = await catalog.ResolveAsync(_agentName, culture: null, cancellationToken).ConfigureAwait(false);

        return agent ?? throw new AgentPrismExternalCallException($"There is no agent named '{_agentName}' in the catalog.")
        {
            AgentName = _agentName,
            Protocol = "a2a",
        };
    }
}
