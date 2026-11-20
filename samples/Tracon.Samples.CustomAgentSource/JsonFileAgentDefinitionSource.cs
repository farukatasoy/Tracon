using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Tracon.Samples.CustomAgentSource;

/// <summary>
/// An <see cref="IAgentDefinitionSource"/> written against the published Tracon.Abstractions
/// package alone. Every <see cref="AgentDefinition"/> lives as one JSON file in a directory —
/// standing in for a Git repository, an object store, or any other place a real third party
/// keeps definitions outside Tracon's own database.
/// </summary>
/// <remarks>
/// <para>
/// The source returns the definition and nothing else. Tracon compiles it, so everything that
/// belongs to a compiled agent comes for free: parameter values (a definition that declares
/// <c>parameters</c> accepts them on a run, from a queue, a batch, a replay and a workflow step),
/// the compiled-agent cache, the tenant key, the catalog descriptor and the run record. A source
/// that builds its own <c>AIAgent</c> (<see cref="IAgentSource"/>) has to do each of those itself,
/// and cannot receive a parameter value at all.
/// </para>
/// <para>
/// This is not a production source — it does no caching of its own and re-reads the directory on
/// every call, which the contract explicitly allows (a source that serves many definitions should
/// add its own cache; see the "write your own agent source" guide). It exists to prove the
/// extension point is complete from outside the repository: everything here comes from published
/// packages, and the sample's test project runs Tracon's official
/// <c>AgentDefinitionSourceContract</c> suite against it.
/// </para>
/// <para>
/// It deliberately exercises the part of the contract a real source has to get right:
/// <see cref="ListAsync"/> and <see cref="GetAsync"/> read the SAME directory the SAME way, so they
/// can never disagree about which names exist (the filename is not treated as the agent's identity
/// — only the JSON content's <c>Name</c> field is, exactly as the management database does it).
/// </para>
/// </remarks>
public sealed class JsonFileAgentDefinitionSource : IAgentDefinitionSource
{
    /// <summary>The name this source registers under.</summary>
    public const string SourceName = "json-file";

    private readonly string _directory;

    /// <summary>Creates a source that reads definitions from <c>options.Directory</c>.</summary>
    /// <param name="options">
    /// This source's own configuration. Registered by hand
    /// (<c>services.AddSingleton(new JsonFileAgentDefinitionSourceOptions { ... })</c>) before
    /// <c>AddAgentDefinitionSource&lt;JsonFileAgentDefinitionSource&gt;()</c> runs.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><c>options.Directory</c> is empty.</exception>
    public JsonFileAgentDefinitionSource(JsonFileAgentDefinitionSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            throw new ArgumentException($"{nameof(options)}.{nameof(options.Directory)} must not be empty.", nameof(options));
        }

        _directory = options.Directory;
    }

    /// <inheritdoc />
    public string Name => SourceName;

    /// <inheritdoc />
    /// <remarks>
    /// Runs after the built-in database source. See the "write your own agent source"
    /// guide for how to choose a value — <c>0</c> and <c>100</c> are not reserved, they
    /// are just where the built-in sources sit.
    /// </remarks>
    public int Priority => AgentSourcePriority.Database + 1;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = new List<AgentDefinition>();

        await foreach (var definition in ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            definitions.Add(definition);
        }

        definitions.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return definitions;
    }

    /// <inheritdoc />
    public async ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        await foreach (var definition in ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(definition.Name, agentName, StringComparison.Ordinal))
            {
                return definition;
            }
        }

        return null;
    }

    private async IAsyncEnumerable<AgentDefinition> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(path);
            var definition = await JsonSerializer
                .DeserializeAsync(stream, JsonFileAgentDefinitionSourceJsonContext.Default.AgentDefinition, cancellationToken)
                .ConfigureAwait(false);

            if (definition is not null)
            {
                yield return definition;
            }
        }
    }
}
