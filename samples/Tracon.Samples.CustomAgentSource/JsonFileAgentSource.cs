using System.Text.Json;
using Microsoft.Agents.AI;

namespace Tracon.Samples.CustomAgentSource;

/// <summary>
/// A definition-backed <see cref="IAgentSource"/> written against the published
/// Tracon.Core package alone. Every <see cref="AgentDefinition"/> lives as one
/// JSON file in a directory — standing in for a Git repository, an object store, or
/// any other place a real third party keeps definitions outside Tracon's own
/// database.
/// </summary>
/// <remarks>
/// <para>
/// This is not a production source — it does no caching of its own and re-reads the
/// directory on every call, which the source contract explicitly allows (a source
/// that serves many definitions should add its own cache; see the "write your own
/// agent source" guide). It exists to prove the extension point is complete from
/// outside the repository: everything here comes from published packages, and the
/// sample's test project runs Tracon's official <c>AgentSourceContract</c> suite
/// against it.
/// </para>
/// <para>
/// It deliberately exercises the parts of the contract a real source has to get
/// right: <see cref="ListAsync"/> and <see cref="ResolveAsync"/> read the SAME
/// directory the SAME way, so they can never disagree about which names exist (the
/// filename is not treated as the agent's identity — only the JSON content's
/// <c>Name</c> field is, exactly as the management database does it); compilation and
/// caching go through <see cref="AgentDefinitionCompiler.CompileCachedAsync"/>, the
/// one high-level path that gets the skill/callable-agent/shared-instructions
/// fingerprint and the tenant BYOK cache bypass right without the source re-deriving
/// either.
/// </para>
/// </remarks>
public sealed class JsonFileAgentSource : IAgentSource
{
    /// <summary>The name this source registers under.</summary>
    public const string SourceName = "json-file";

    private readonly string _directory;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly CompiledAgentCache _cache;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a source that reads definitions from <c>options.Directory</c>.</summary>
    /// <param name="options">
    /// This source's own configuration. Registered by hand
    /// (<c>services.AddSingleton(new JsonFileAgentSourceOptions { ... })</c>) before
    /// <c>AddAgentSource&lt;JsonFileAgentSource&gt;()</c> runs.
    /// </param>
    /// <param name="compiler">The compiler, resolved through DI.</param>
    /// <param name="cache">The compiled-agent cache, also resolved through DI.</param>
    /// <param name="tenantContext">
    /// The tenant context. This source itself is NOT tenant-aware — every tenant sees the
    /// same directory — but the ambient tenant still has to enter the compile cache key:
    /// <see cref="AgentDefinitionCompiler"/> can bind a tenant-dependent tool (for example
    /// semantic search) into a definition at compile time, and without the tenant in the
    /// key, a second tenant that resolves the SAME agent name would silently get back the
    /// FIRST tenant's compiled instance. See <see cref="CompiledAgentCache"/>'s remarks.
    /// </param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><c>options.Directory</c> is empty.</exception>
    public JsonFileAgentSource(JsonFileAgentSourceOptions options, AgentDefinitionCompiler compiler, CompiledAgentCache cache, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            throw new ArgumentException($"{nameof(options)}.{nameof(options.Directory)} must not be empty.", nameof(options));
        }

        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _directory = options.Directory;
        _compiler = compiler;
        _cache = cache;
        _tenantContext = tenantContext;
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
    public async ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptors = new List<AgentDescriptor>();

        await foreach (var definition in ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            descriptors.Add(ToDescriptor(definition));
        }

        descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return descriptors;
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        var definition = await FindDefinitionAsync(agentName, cancellationToken).ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        return await _compiler
            .CompileCachedAsync(definition, _cache, _tenantContext.TenantId, culture, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<AgentDefinition?> FindDefinitionAsync(string agentName, CancellationToken cancellationToken)
    {
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
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
                .DeserializeAsync(stream, JsonFileAgentSourceJsonContext.Default.AgentDefinition, cancellationToken)
                .ConfigureAwait(false);

            if (definition is not null)
            {
                yield return Normalize(definition);
            }
        }
    }

    /// <summary>
    /// Fills in the collections a hand-written JSON file can leave absent.
    /// </summary>
    /// <remarks>
    /// <see cref="AgentDefinition"/>'s <c>ToolNames</c>, <c>SkillNames</c>,
    /// <c>CallableAgentNames</c>, <c>McpResourceUris</c>, and <c>Parameters</c> — and
    /// <see cref="ModelBinding"/>'s <c>ProviderSettings</c> and <c>Fallbacks</c> — default
    /// to an empty collection in C#. That default initializer does not run for a property
    /// the JSON payload never mentions: <c>System.Text.Json</c>'s source generator has to
    /// validate the <see langword="required"/> members (<c>Name</c>, <c>Model</c>), and to
    /// do that it builds the object WITHOUT running the constructor, then sets only the
    /// properties present in the JSON — every absent property (required or not) is left at
    /// the CLR default, <see langword="null"/> for a reference type, not its C# initializer.
    /// <see cref="AgentDefinitionCompiler"/> assumes the management database's own
    /// invariant (never null, only possibly empty) and does not defend against a null
    /// collection, so a source reading raw JSON has to restore it itself. A source backed
    /// by a database — like Tracon's own — does not need this: the row it reads
    /// already came from a save path that filled every column in.
    /// </remarks>
    private static AgentDefinition Normalize(AgentDefinition definition)
        => definition with
        {
            ToolNames = definition.ToolNames ?? [],
            SkillNames = definition.SkillNames ?? [],
            CallableAgentNames = definition.CallableAgentNames ?? [],
            McpResourceUris = definition.McpResourceUris ?? [],
            Parameters = definition.Parameters ?? [],
            Model = definition.Model with
            {
                ProviderSettings = definition.Model.ProviderSettings ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
                Fallbacks = definition.Model.Fallbacks ?? [],
            },
        };

    private static AgentDescriptor ToDescriptor(AgentDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Custom,
            SourceName = SourceName,
            Version = definition.Version,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = definition.SkillNames,
            CallableAgentNames = definition.CallableAgentNames,
            UsesHarness = definition.Harness is not null,
            UpdatedAt = definition.UpdatedAt,
        };
}
