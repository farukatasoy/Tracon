using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// An agent's own model client whose provider, tenant credential and egress check are already
/// resolved, and whose pipeline is built only after the agent's extensions have added their rings.
/// </summary>
/// <remarks>
/// <para>
/// The compiler resolves every piece of tenant state before it builds the agent: the
/// asynchronous store reads happen here, and <see cref="Build"/> is synchronous. The extensions
/// run between the two, so a refused provider is refused before any extension code runs.
/// </para>
/// <para>
/// A registry that is not <see cref="ModelProviderRegistry"/> builds its client in one step
/// through <see cref="IModelProviderRegistry"/>. It cannot place a ring, so <see cref="Build"/>
/// refuses rings for it instead of building the agent without them.
/// </para>
/// </remarks>
// K-874: BuildAgent and below build no client; they only take one.
internal sealed class PendingChatClient
{
    private readonly Func<ChatClientRings, IChatClient>? _build;
    private readonly IChatClient? _built;

    private PendingChatClient(Func<ChatClientRings, IChatClient>? build, IChatClient? built)
    {
        _build = build;
        _built = built;
    }

    /// <summary>Wraps a pipeline <see cref="ModelProviderRegistry"/> builds once the rings are known.</summary>
    /// <param name="build">Builds the pipeline with the given rings.</param>
    /// <returns>The pending client.</returns>
    public static PendingChatClient Deferred(Func<ChatClientRings, IChatClient> build) => new(build, built: null);

    /// <summary>Wraps a client that another <see cref="IModelProviderRegistry"/> has already built.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The pending client.</returns>
    public static PendingChatClient Built(IChatClient client) => new(build: null, client);

    /// <summary>Builds the client with the rings of the agent's extensions.</summary>
    /// <param name="rings">The rings.</param>
    /// <returns>The agent's model client.</returns>
    /// <exception cref="TraconCompilationException">
    /// A ring failed, or the client came from a registry that cannot place the rings.
    /// </exception>
    public IChatClient Build(ChatClientRings rings)
    {
        if (_build is not null)
        {
            return _build(rings);
        }

        if (!rings.IsEmpty)
        {
            throw new TraconCompilationException(
                $"Agent '{rings.AgentName}' names the agent extension " +
                $"{string.Join(", ", rings.Extensions.Select(static name => $"'{name}'"))}, which adds a chat client ring, " +
                $"but the registered {nameof(IModelProviderRegistry)} is not Tracon's own and cannot place the ring. " +
                $"Remove the registration that replaces {nameof(IModelProviderRegistry)}, or remove the extension from the definition.")
            {
                AgentName = rings.AgentName,
            };
        }

        return _built!;
    }
}
