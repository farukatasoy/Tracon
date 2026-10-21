using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Setup of conversation memory.</summary>
public static class TraconBuilderConversationMemoryExtensions
{
    /// <summary>
    /// Sets up conversation memory: an agent whose definition turns on
    /// <see cref="MemorySettings.EnableConversationMemory"/> finds what the same user said in earlier
    /// sessions with that agent and gets it as context.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">Sets <see cref="TraconConversationMemoryOptions.VectorDimensions"/> and, if you want, the collection name.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Register a <c>Microsoft.Extensions.VectorData.VectorStore</c> in the service collection. The
    /// connector package you choose and its embedding model live inside that store; Tracon takes
    /// the store from the container when it compiles an agent that asks for memory, and the build
    /// stops with the missing registration's name if there is none.
    /// </para>
    /// <para>
    /// A record is scoped by tenant, agent and user, and carries its session. A run with no user, or no
    /// session, neither reads nor writes memory. The store is the host's data: Tracon deletes the records of
    /// a session when the session goes (an erasure of its data subject, <c>DELETE /api/sessions/{id}</c>,
    /// or its retention period), but cannot delete what the store keeps for its own reasons, such as backups.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseConversationMemory(options =&gt; options.VectorDimensions = 1536);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseConversationMemory(
        this ITraconBuilder builder,
        Action<TraconConversationMemoryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddOptions<TraconConversationMemoryOptions>().Configure(configure).ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconConversationMemoryOptions>, TraconConversationMemoryOptionsValidator>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISubjectContentEraser, ConversationMemoryEraser>());

        return builder;
    }
}
