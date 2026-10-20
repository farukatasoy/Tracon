using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Remembers the registered name of a hosted tool instance, so the run recorder can name the
/// call a response carries.
/// </summary>
/// <remarks>
/// <para>
/// A provider's response names a kind of call, not the tool: a web search call carries a call
/// identifier and the queries. The tool Tracon registered under a name is the one instance the model
/// call was given in <see cref="ChatOptions.Tools"/>, so the instance is the key. The table does not keep
/// a tool alive.
/// </para>
/// <para>
/// A tool the table does not know (the harness's own web search, a tool an agent extension added) has no
/// registered name, and the recorder writes its calls as content events, as before.
/// </para>
/// </remarks>
internal static class HostedToolIdentity
{
    private static readonly ConditionalWeakTable<AITool, string> Names = new();

    /// <summary>Remembers the name of a tool instance.</summary>
    /// <param name="tool">The instance.</param>
    /// <param name="name">The registered name.</param>
    /// <returns>
    /// <see langword="false"/> when the instance is already registered under another name. The same
    /// name again is not a conflict: a factory may return a shared instance for one registration.
    /// </returns>
    public static bool TryRegister(AITool tool, string name)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Atomic: two first compiles of one shared instance can arrive together.
        return Names.TryAdd(tool, name)
            || (Names.TryGetValue(tool, out var existing) && string.Equals(existing, name, StringComparison.Ordinal));
    }

    /// <summary>Finds the registered name of the tool a model call was given for a kind of call.</summary>
    /// <param name="options">The options of the model call.</param>
    /// <param name="kind">The kind of call the response carries.</param>
    /// <returns>The registered name, or <see langword="null"/> when the options carry no registered tool of the kind.</returns>
    public static string? FindName(ChatOptions? options, HostedToolKind kind)
    {
        if (options?.Tools is not { Count: > 0 } tools)
        {
            return null;
        }

        foreach (var tool in tools)
        {
            if (HostedToolRegistry.IsOfKind(tool, kind) && Names.TryGetValue(tool, out var name))
            {
                return name;
            }
        }

        return null;
    }
}
