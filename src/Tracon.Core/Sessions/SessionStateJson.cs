using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// The serializer options of the session state that the in-memory chat history keeps in the session's state bag.
/// </summary>
/// <remarks>
/// <para>
/// The state bag writes each value with the options it was set with. The history provider's default options come from the
/// Agent Framework, whose metadata does not cover <see cref="AgentRequestMessageSourceAttribution"/>: the source
/// information its memory providers add to a message. On a runtime that may use reflection the serializer falls back to it
/// and nothing shows; under Native AOT writing the session threw <see cref="NotSupportedException"/> on the first run of an
/// agent with conversation memory (found by the AOT smoke of the release rehearsal, the first run of it after the Agent
/// Framework 1.28 upgrade).
/// </para>
/// <para>
/// These options are the Agent Framework's own with this assembly's source-generated context behind them, so every type
/// the framework resolves keeps its metadata and the one it left out resolves from the context.
/// </para>
/// </remarks>
internal static class SessionStateJson
{
    /// <summary>Gets the options the in-memory chat history provider writes its state with.</summary>
    internal static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var framework = AgentAbstractionsJsonUtilities.DefaultOptions;
        var options = new JsonSerializerOptions(framework)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(framework.TypeInfoResolver!, TraconCoreJsonContext.Default),
        };

        options.MakeReadOnly();

        return options;
    }
}
