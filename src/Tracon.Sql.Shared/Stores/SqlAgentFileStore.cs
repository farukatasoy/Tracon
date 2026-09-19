using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// The persistent, PostgreSQL-backed implementation of <see cref="AgentFileStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>FileMemoryProvider</c> and <c>TextSearchProvider</c> resolve to this type
/// when it is registered, without code changes.
/// </para>
/// <para>
/// The tenant is injected directly from <see cref="ITenantContext"/>. The
/// interface has no parameter for the agent name; it is read from the current
/// run's ambient scope (<see cref="TraconRunContext"/>). Because of this,
/// the file store can only be used within a run — calling it outside
/// <c>RunRecordingAgent</c> throws an explicit error.
/// </para>
/// </remarks>
#pragma warning disable MAAI001 // AgentFileStore and its derivatives are "evaluation purposes only" — same rationale as AgentDefinitionCompiler.
internal sealed class SqlAgentFileStore : AgentFileStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new persistent file store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlAgentFileStore(
        SqlStoreContext context,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    /// <summary>The gateway for provider-specific behaviors.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public override async Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectAgentFile);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        DbHelpers.Add(command, "path", NormalizePath(path));

        return await DbHelpers
            .ReadSingleAsync(command, reader => ProtectedValue.Read(_context, reader.GetString(0))!, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var now = DateTimeOffset.UtcNow;
        var command = CreateCommand(_sql.UpsertAgentFile);
        DbHelpers.Add(command, "id", TraconId.NewId(now));
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        DbHelpers.Add(command, "path", NormalizePath(path));
        DbHelpers.Add(command, "content", ProtectedValue.Write(_context, ProtectedColumn.AgentFileContent, content)!);
        Dialect.AddTimestamp(command, "now", now);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.DeleteAgentFile);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        DbHelpers.Add(command, "path", NormalizePath(path));

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public override async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectAgentFile);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        DbHelpers.Add(command, "path", NormalizePath(path));

        return await DbHelpers
            .ReadSingleAsync(command, static reader => reader.GetString(0), cancellationToken)
            .ConfigureAwait(false) is not null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Directories are not stored as separate rows; the path hierarchy is derived
    /// from the paths of stored files (see migration 0006 comment). "Creating" a
    /// directory is therefore a no-op with no persistent effect.
    /// </remarks>
    public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Files UNDER the directory are narrowed by a prefix filter in SQL; cost is
    /// driven by the row count under this directory, not by the store's total
    /// file count.
    /// </remarks>
    public override async Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var prefix = NormalizeDirectory(directory);
        var files = await LoadFilteredAsync(
            prefix,
            deepLike: null,
            nameLike: null,
            regexPattern: null,
            cancellationToken).ConfigureAwait(false);

        var children = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var (path, _) in files)
        {
            var remainder = path[prefix.Length..];
            var slash = remainder.IndexOf('/');
            var name = slash < 0 ? remainder : remainder[..slash];

            if (name.Length == 0)
            {
                continue;
            }

            children[name] = slash >= 0;
        }

        return children
            .Select(static pair => new FileStoreEntry(pair.Key, pair.Value ? "directory" : "file"))
            .OrderBy(static entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The prefix, depth limit (
    /// <paramref name="recursive"/>
    /// ), and
    /// <paramref name="globPattern"/>
    /// are pushed down to SQL; <c>LoadAllAsync</c> is no longer called. PostgreSQL
    /// additionally pushes
    /// <paramref name="regexPattern"/>
    /// down as a pre-filter via the <c>~</c> operator — the final match is still ALWAYS
    /// done here with .NET <see cref="Regex"/>, so behavior does not change. If the
    /// pattern sent to the server is invalid in PostgreSQL's ARE syntax
    /// (e.g.NET-specific named groups), <see cref="SqlDialect.IsInvalidRegexError"/>
    /// catches this and the query is re-run WITHOUT the pre-filter.
    /// </remarks>
    public override async Task<IReadOnlyList<FileSearchResult>> SearchAsync(
        string directory,
        string regexPattern,
        string? globPattern = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var prefix = NormalizeDirectory(directory);
        var deepLike = recursive ? null : EscapeLikeLiteral(prefix) + "%/%";
        var nameLike = string.IsNullOrEmpty(globPattern) ? null : EscapeLikeLiteral(prefix) + TranslateGlobToLike(globPattern);

        List<(string Path, string Content)> files;

        // 🚨 With content protection on, the server-side `~` prefilter would
        // compare its pattern against CIPHERTEXT and never match; sending it
        // anyway would make every search pay for a round trip that always
        // falls back (decision 82.3). Skip it outright instead of relying on
        // IsInvalidRegexError, which only catches a pattern PostgreSQL's ARE
        // syntax rejects, not one that is merely comparing against the wrong bytes.
        if (_context.ProtectedColumns.Contains(ProtectedColumn.AgentFileContent))
        {
            files = await LoadFilteredAsync(prefix, deepLike, nameLike, regexPattern: null, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            try
            {
                files = await LoadFilteredAsync(prefix, deepLike, nameLike, regexPattern, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DbException ex) when (Dialect.IsInvalidRegexError(ex))
            {
                files = await LoadFilteredAsync(prefix, deepLike, nameLike, regexPattern: null, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var regex = new Regex(regexPattern, RegexOptions.None, TimeSpan.FromSeconds(2));
        var results = new List<FileSearchResult>();

        foreach (var (path, content) in files)
        {
            var matches = CollectMatches(regex, content);

            if (matches.Count > 0)
            {
                results.Add(new FileSearchResult
                {
                    FileName = path,
                    MatchingLines = matches,
                    Snippet = matches[0].Line,
                });
            }
        }

        return results;
    }

    private static List<FileSearchMatch> CollectMatches(Regex regex, string content)
    {
        var lines = content.Split('\n');
        var matches = new List<FileSearchMatch>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (regex.IsMatch(lines[i]))
            {
                matches.Add(new FileSearchMatch { Line = lines[i], LineNumber = i + 1 });
            }
        }

        return matches;
    }

    /// <summary>
    /// Reads files narrowed by a prefix, an optional depth limit, an optional
    /// glob filter, and an optional regex pre-filter (effective only on
    /// PostgreSQL). <paramref name="deepLike"/> and <paramref name="nameLike"/>
    /// are built by the caller as full LIKE patterns that INCLUDE the prefix
    /// (see <see cref="SearchAsync"/>).
    /// </summary>
    private async ValueTask<List<(string Path, string Content)>> LoadFilteredAsync(
        string prefix,
        string? deepLike,
        string? nameLike,
        string? regexPattern,
        CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectAgentFilesFiltered);
        DbHelpers.AddTenant(command, _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        Dialect.AddText(command, "prefix_like", EscapeLikeLiteral(prefix) + "%");
        Dialect.AddText(command, "prefix_deep_like", deepLike);
        Dialect.AddText(command, "name_like", nameLike);
        Dialect.AddText(command, "regex_pattern", regexPattern);

        return await DbHelpers.ReadListAsync(
            command,
            reader => (reader.GetString(0), ProtectedValue.Read(_context, reader.GetString(1))!),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Escapes a string so it matches literally in a <c>LIKE</c> pattern.</summary>
    private static string EscapeLikeLiteral(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (ch is '\\' or '%' or '_')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Translates a glob pattern (<c>*</c>/<c>?</c>) into a <c>LIKE ... ESCAPE '\'</c> pattern.
    /// </summary>
    /// <remarks>
    /// <c>*</c> matches ANY sequence of characters, including <c>/</c>, just as
    /// in the original .NET regex-based matching (it can cross directory
    /// boundaries); <c>%</c> in <c>LIKE</c> carries the same behavior.
    /// </remarks>
    private static string TranslateGlobToLike(string globPattern)
    {
        var builder = new StringBuilder(globPattern.Length);

        foreach (var ch in globPattern)
        {
            if (ch == '*')
            {
                builder.Append('%');
            }
            else if (ch == '?')
            {
                builder.Append('_');
            }
            else if (ch is '\\' or '%' or '_')
            {
                builder.Append('\\').Append(ch);
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string RequireAgentName()
        => TraconRunContext.Current?.AgentName
           ?? throw new TraconException(
               "Persistent file memory can only be used within an active run.");

    private static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var trimmed = path.Trim();
        var withLeadingSlash = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
        return withLeadingSlash.Length > 1 ? withLeadingSlash.TrimEnd('/') : withLeadingSlash;
    }

    private static string NormalizeDirectory(string directory)
    {
        var normalized = NormalizePath(directory);
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);
}
#pragma warning restore MAAI001
