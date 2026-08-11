namespace AgentPrism;

// MAAI001: Microsoft.Agents.AI.AgentFileStore "evaluation purposes only"
// olarak isaretli. Bastirma gerekcesi AgentDefinitionCompiler'daki ile aynidir.
#pragma warning disable MAAI001

/// <summary>
/// Paylasilan bir <see cref="Microsoft.Agents.AI.AgentFileStore"/> uzerinde,
/// bir kiraciya ozel yalitilmis bir alt agac gibi davranan sarmalayici.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Microsoft.Agents.AI.AgentFileStore"/> varsayilan olarak surec genelinde
/// paylasilan TEK bir depodur (<c>InMemoryAgentFileStore</c>, <c>TryAddSingleton</c>).
/// Bu sarmalayici olmadan, bir kiracinin <c>EnableTextSearch</c> acik bir agent'i kok
/// <c>"/"</c> dizininden yaptigi recursive aramada BASKA bir kiracinin
/// <c>EnableFileMemory</c> ile yazdigi dosyalari gorebilirdi — kiraci yalitiminin
/// kirilmasi demektir.
/// </para>
/// <para>
/// Her yol cagrida <c>/{tenantId}/...</c> onekiyle ic depoya yonlendirilir; disariya
/// donen yol/ad alanlarindan da ayni onek soyulur. Sonuc: sarmalanan tarafta hem
/// <see cref="AgentDefinitionCompiler"/>'in yazma (<c>FileMemoryProvider</c>) hem de
/// okuma (<c>TextSearchProvider</c>) tarafi, sanki kendi ozel kok dizinleriymis gibi
/// <c>"/"</c> ile calisir; ic depodaki gercek onek kod cagiran tarafa hic sizmaz.
/// </para>
/// </remarks>
internal sealed class TenantPrefixingAgentFileStore : Microsoft.Agents.AI.AgentFileStore
{
    private readonly Microsoft.Agents.AI.AgentFileStore _inner;
    private readonly string _prefix;

    /// <summary>Yeni bir kiraci-yalitimli sarmalayici olusturur.</summary>
    /// <param name="inner">Sarmalanan paylasilan depo.</param>
    /// <param name="tenantId">Yalitilacak kiracinin kimligi.</param>
    public TenantPrefixingAgentFileStore(Microsoft.Agents.AI.AgentFileStore inner, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        _inner = inner;
        _prefix = tenantId;
    }

    /// <inheritdoc />
    public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        => _inner.CreateDirectoryAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        => _inner.FileExistsAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override async Task<IReadOnlyList<Microsoft.Agents.AI.FileStoreEntry>> ListChildrenAsync(
        string directory, CancellationToken cancellationToken = default)
    {
        var entries = await _inner.ListChildrenAsync(Rewrite(directory), cancellationToken).ConfigureAwait(false);

        return entries.Select(entry => new Microsoft.Agents.AI.FileStoreEntry(StripPrefix(entry.Name), entry.Type))
            .ToList();
    }

    /// <inheritdoc />
    public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(Rewrite(path), cancellationToken);

    /// <inheritdoc />
    public override async Task<IReadOnlyList<Microsoft.Agents.AI.FileSearchResult>> SearchAsync(
        string directory,
        string regexPattern,
        string? globPattern = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var matches = await _inner
            .SearchAsync(Rewrite(directory), regexPattern, globPattern, recursive, cancellationToken)
            .ConfigureAwait(false);

        return matches.Select(match => new Microsoft.Agents.AI.FileSearchResult
        {
            FileName = StripPrefix(match.FileName),
            MatchingLines = match.MatchingLines,
            Snippet = match.Snippet,
        }).ToList();
    }

    /// <inheritdoc />
    public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
        => _inner.WriteAsync(Rewrite(path), content, cancellationToken);

    private string Rewrite(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Ic depo (orn. InMemoryAgentFileStore) yolun GORECELI olmasini ve
        // '/' ile BASLAMAMASINI sart kosar (NormalizeRelativePath). "" ve "/"
        // cagiranin kendi kok dizinini ("ben") temsil eder.
        if (path.Length == 0 || string.Equals(path, "/", StringComparison.Ordinal))
        {
            return _prefix;
        }

        var relative = path[0] == '/' ? path[1..] : path;

        return _prefix + "/" + relative;
    }

    private string StripPrefix(string value)
    {
        if (string.Equals(value, _prefix, StringComparison.Ordinal))
        {
            return "/";
        }

        return value.StartsWith(_prefix + "/", StringComparison.Ordinal)
            ? value[_prefix.Length..]
            : value;
    }
}
#pragma warning restore MAAI001
