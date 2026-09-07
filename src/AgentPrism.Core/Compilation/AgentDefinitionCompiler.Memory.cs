using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Memory provider production: file memory, todo tracking, text search, and
/// semantic (vector) search.
/// </summary>
// MAAI001: Microsoft.Agents.AI.AgentFileStore and the memory provider types
// are marked "evaluation purposes only". Memory setup is kept in a single
// file; only this file is updated if MAF changes these APIs. Rationale:
// docs/KARARLAR.md (same pattern as K-020).
#pragma warning disable MAAI001
public sealed partial class AgentDefinitionCompiler
{
    /// <summary>
    /// The minimum length a token must have for <see cref="BuildKeywordPattern"/>
    /// to count it as "meaningful" - excludes short filler words from the pattern.
    /// </summary>
    private const int MinKeywordLength = 3;

    /// <summary>Builds a list of <see cref="AIContextProvider"/> from a definition's memory settings.</summary>
    private List<AIContextProvider> CreateMemoryProviders(AgentDefinition definition)
    {
        var memory = definition.Memory;

        if (memory is null)
        {
            return [];
        }

        var providers = new List<AIContextProvider>();

        if (memory.EnableFileMemory)
        {
            providers.Add(new FileMemoryProvider(RequireFileStore(definition), stateInitializer: null, options: null));
        }

        if (memory.EnableTodo)
        {
            providers.Add(new TodoProvider(new TodoProviderOptions()));
        }

        if (CreateTextSearchProvider(definition) is { } textSearch)
        {
            providers.Add(textSearch);
        }

        return providers;
    }

    private TextSearchProvider? CreateTextSearchProvider(AgentDefinition definition)
    {
        if (definition.Memory is not { EnableTextSearch: true })
        {
            return null;
        }

        var fileStore = RequireFileStore(definition);

        return new TextSearchProvider(
            (query, cancellationToken) => SearchFileStoreAsync(fileStore, query, cancellationToken),
            new TextSearchProviderOptions(),
            _loggerFactory);
    }

    /// <summary>
    /// Adds the <c>search_knowledge</c> tool to <paramref name="tools"/> when
    /// <see cref="MemorySettings.EnableVectorSearch"/> is set.
    /// </summary>
    /// <exception cref="AgentPrismCompilationException">
    /// Semantic search is requested but <see cref="IVectorSearchStore"/>, the
    /// embedding generator, or the tenant cannot be resolved.
    /// </exception>
    private void AddVectorSearchTool(AgentDefinition definition, List<AITool> tools)
    {
        if (definition.Memory is not { EnableVectorSearch: true } memory)
        {
            return;
        }

        if (_vectorSearchStore is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants semantic search, but IVectorSearchStore is not " +
                "registered (today only PostgreSQL: UsePostgreSql()).")
            {
                AgentName = definition.Name,
            };
        }

        if (_embeddingGenerator is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants semantic search, but " +
                "IEmbeddingGenerator<string, Embedding<float>> is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var tenantId = _tenantContext?.TenantId ?? definition.TenantId
            ?? throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants semantic search, but the tenant could not be resolved.")
            {
                AgentName = definition.Name,
            };

        var collection = memory.VectorCollection is { Length: > 0 } named ? named : definition.Name;

        tools.Add(VectorSearchToolFactory.Create(
            _vectorSearchStore, _embeddingGenerator, tenantId, collection, _knowledgeMaxResults));
    }

    /// <remarks>
    /// Wraps with <see cref="TenantPrefixingAgentFileStore"/>: <see cref="_fileStore"/>
    /// is, by default, a SINGLE store shared process-wide. Without the wrapper,
    /// different tenants' file memory/text search would mix at the root "/"
    /// directory - a breach of tenant isolation.
    /// </remarks>
    private TenantPrefixingAgentFileStore RequireFileStore(AgentDefinition definition)
    {
        if (_fileStore is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants a memory provider, but the file store is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var tenantId = _tenantContext?.TenantId ?? definition.TenantId
            ?? throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' wants a memory provider, but the tenant could not be resolved.")
            {
                AgentName = definition.Name,
            };

        // 🚨 The agent name is part of the prefix as well (defect F-105): the
        // tenant boundary alone let one agent's private file be found by
        // another agent's text search inside the SAME tenant. The name is known
        // here, at compile time, and is already part of the CompiledAgentCache
        // key, so the boundary needs no ambient state.
        return new TenantPrefixingAgentFileStore(_fileStore, tenantId, definition.Name);
    }

    /// <summary>
    /// Maps <see cref="AgentFileStore.SearchAsync"/> results to
    /// <see cref="TextSearchProvider.TextSearchResult"/>.
    /// </summary>
    /// <remarks>
    /// Searches over whichever concrete <see cref="AgentFileStore"/> is
    /// registered (in-memory unless you register another); a persistent store
    /// falls through to persistent search with no code change here.
    /// </remarks>
    private static async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchFileStoreAsync(
        AgentFileStore fileStore,
        string query,
        CancellationToken cancellationToken)
    {
        var matches = await fileStore
            .SearchAsync("/", regexPattern: BuildKeywordPattern(query), globPattern: null, recursive: true, cancellationToken)
            .ConfigureAwait(false);

        return matches.Select(static match => new TextSearchProvider.TextSearchResult
        {
            Text = match.Snippet,
            SourceName = match.FileName,
            SourceLink = match.FileName,
        });
    }

    /// <summary>
    /// Converts the natural-language query <see cref="TextSearchProvider"/>
    /// has the model write into a regex pattern as expected by
    /// <see cref="AgentFileStore.SearchAsync"/>.
    /// </summary>
    /// <remarks>
    /// <c>SearchAsync</c> expects a REGEX (via <c>~</c> in
    /// PostgreSQL, via <see cref="Regex"/> elsewhere), but
    /// <see cref="TextSearchProvider"/> has the model write a natural-language
    /// query (e.g. "is there a record about FILE-7841?"). Running this raw
    /// text as-is as a regex almost never matches (spaces and punctuation
    /// impose narrow constraints in regex terms) - instead, the query is split
    /// on whitespace into meaningful tokens (excluding short filler words),
    /// each is escaped and joined with "OR"; a line matches if it contains ANY
    /// of these tokens (e.g. "FILE-7841"). If zero tokens remain (the whole
    /// query consists of short words), the raw query is escaped and used
    /// as-is - this never makes the behavior worse than it is today.
    /// </remarks>
    private static string BuildKeywordPattern(string query)
    {
        var tokens = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(static token => token.Length >= MinKeywordLength)
            .Select(Regex.Escape)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tokens.Length == 0 ? Regex.Escape(query) : string.Join('|', tokens);
    }
}
#pragma warning restore MAAI001
