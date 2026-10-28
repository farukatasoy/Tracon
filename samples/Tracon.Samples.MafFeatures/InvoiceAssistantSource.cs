using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.MafFeatures;

/// <summary>
/// A custom agent source that builds its agent in code and still gets the tenant key,
/// the egress policy, and run cost.
/// </summary>
/// <remarks>
/// <para>
/// A factory registered with <c>AddAgent(name, factory)</c> declares no model and builds its
/// client without the tenant. <c>AddAgent(name, model, factory)</c> gets the tenant key, the
/// egress policy, and cost without a source, so write a source when the agents do not
/// live in code: a repository, or another runtime. A source gets all three because
/// <see cref="ResolveAsync"/> is asynchronous and <see cref="ListAsync"/> publishes the model,
/// which the catalog hands to run recording.
/// </para>
/// <para>
/// This source does not use the definition compiler, so it owns the agent's whole shape.
/// It is not tenant-aware: every tenant sees the same agent, and the tenant's own key is
/// applied by the registry when <see cref="ResolveAsync"/> asks for the chat client.
/// </para>
/// </remarks>
/// <param name="models">The registry that builds the chat client.</param>
/// <param name="tools">The registry that returns the wrapped tool.</param>
/// <param name="history">Tracon's chat history provider, or <see langword="null"/> when none is registered.</param>
/// <param name="model">The model the agent talks to.</param>
public sealed class InvoiceAssistantSource(
    IModelProviderRegistry models,
    IToolRegistry tools,
    ChatHistoryProvider? history,
    ModelBinding model) : IAgentSource
{
    /// <summary>The name the agent is served under.</summary>
    public const string AgentName = "invoice-assistant";

    /// <summary>The name this source registers under.</summary>
    public const string SourceName = "invoice-assistant-source";

    /// <inheritdoc />
    public string Name => SourceName;

    /// <inheritdoc />
    public int Priority => AgentSourcePriority.Database + 1;

    // The model named here is what run recording prices. Leave it out and the
    // run is recorded with no model and no cost.
    private AgentDescriptor Descriptor => new()
    {
        Name = AgentName,
        Description = "A custom-source agent that answers questions about invoices.",
        Origin = AgentDefinitionOrigin.Custom,
        SourceName = SourceName,
        Model = model,
        ToolNames = [SearchOrdersToolRegistration.ToolName],
    };

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask<IReadOnlyList<AgentDescriptor>>([Descriptor]);
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(agentName, AgentName, StringComparison.Ordinal))
        {
            return null;
        }

        // The asynchronous call applies the tenant's own provider key and the tenant's
        // egress policy, on top of the model rings.
        var chatClient = await models.CreateChatClientAsync(model, cancellationToken).ConfigureAwait(false);

        if (!tools.TryGet(SearchOrdersToolRegistration.ToolName, out var searchOrders))
        {
            throw new InvalidOperationException($"The tool '{SearchOrdersToolRegistration.ToolName}' is not registered.");
        }

        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = AgentName,
            ChatOptions = new ChatOptions
            {
                Instructions = "Answer questions about invoices. Look orders up with the search tool.",
                Tools = [searchOrders],
            },
            ChatHistoryProvider = history,
        });
    }
}

/// <summary>Registration helper for <see cref="InvoiceAssistantSource"/>.</summary>
public static class InvoiceAssistantRegistration
{
    /// <summary>Adds the custom source to the Tracon chain.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="model">The model the agent talks to.</param>
    /// <returns>The same chain.</returns>
    public static ITraconBuilder AddInvoiceAssistant(this ITraconBuilder builder, ModelBinding model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);

        // The factory overload: the source needs the model binding, which does not
        // belong in the container as its own singleton.
        return builder.AddAgentSource(services => new InvoiceAssistantSource(
            services.GetRequiredService<IModelProviderRegistry>(),
            services.GetRequiredService<IToolRegistry>(),
            services.GetService<ChatHistoryProvider>(),
            model));
    }
}
