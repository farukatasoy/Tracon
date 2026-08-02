using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentFileStore"/>'un PostgreSQL destekli, kalici uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// Faz 13'ten devir: <c>FileMemoryProvider</c> ve <c>TextSearchProvider</c> bu
/// tip kayitli oldugunda kod degismeden kalici belleğe doner
/// (bkz. <c>docs/KARARLAR.md</c>, K-110).
/// </para>
/// <para>
/// Kiraci <see cref="ITenantContext"/>'ten dogrudan enjekte edilir. Agent adi
/// icin arayuzde bir parametre yoktur; suren calistirmanin ambient kapsamindan
/// (<see cref="AgentPrismRunContext"/>) okunur. Bu yuzden dosya belleği yalnizca
/// bir calistirma icinde kullanilabilir — <c>RunRecordingAgent</c> disinda
/// cagrilirsa acik bir hata verir.
/// </para>
/// </remarks>
#pragma warning disable MAAI001 // AgentFileStore ve turevleri "evaluation purposes only" — gerekce AgentDefinitionCompiler'daki ile ayni.
public sealed class PostgresAgentFileStore : AgentFileStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir kalici dosya belleği olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresAgentFileStore(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public override async Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectAgentFile);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);
        command.Parameters.AddWithValue("agent_name", RequireAgentName());
        command.Parameters.AddWithValue("path", NormalizePath(path));

        return await NpgsqlHelpers
            .ReadSingleAsync(command, static reader => reader.GetString(0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var now = DateTimeOffset.UtcNow;
        var command = CreateCommand(_sql.UpsertAgentFile);
        command.Parameters.AddWithValue("id", AgentPrismId.NewId(now));
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);
        command.Parameters.AddWithValue("agent_name", RequireAgentName());
        command.Parameters.AddWithValue("path", NormalizePath(path));
        command.Parameters.AddWithValue("content", content);
        command.Parameters.AddWithValue("now", now.UtcDateTime);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.DeleteAgentFile);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);
        command.Parameters.AddWithValue("agent_name", RequireAgentName());
        command.Parameters.AddWithValue("path", NormalizePath(path));

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public override async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectAgentFile);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);
        command.Parameters.AddWithValue("agent_name", RequireAgentName());
        command.Parameters.AddWithValue("path", NormalizePath(path));

        return await NpgsqlHelpers
            .ReadSingleAsync(command, static reader => reader.GetString(0), cancellationToken)
            .ConfigureAwait(false) is not null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Dizinler ayri satir olarak tutulmaz; yol hiyerarsisi kayitli dosyalarin
    /// yolundan turetilir (bkz. migration 0006 yorumu). Bir dizini "olusturmak"
    /// bu yuzden hicbir kalici etkisi olmayan bir no-op'tur.
    /// </remarks>
    public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public override async Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var prefix = NormalizeDirectory(directory);
        var files = await LoadAllAsync(cancellationToken).ConfigureAwait(false);

        var children = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var (path, _) in files)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

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
    public override async Task<IReadOnlyList<FileSearchResult>> SearchAsync(
        string directory,
        string regexPattern,
        string? globPattern = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var prefix = NormalizeDirectory(directory);
        var files = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var regex = new Regex(regexPattern, RegexOptions.None, TimeSpan.FromSeconds(2));

        var results = new List<FileSearchResult>();

        foreach (var (path, content) in files)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = path[prefix.Length..];

            if (!recursive && remainder.Contains('/', StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(globPattern) && !MatchesGlob(remainder, globPattern))
            {
                continue;
            }

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

    private static bool MatchesGlob(string name, string globPattern)
    {
        var pattern = "^" + Regex.Escape(globPattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(name, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    private async ValueTask<List<(string Path, string Content)>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectAgentFiles);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);
        command.Parameters.AddWithValue("agent_name", RequireAgentName());

        return await NpgsqlHelpers.ReadListAsync(
            command,
            static reader => (reader.GetString(0), reader.GetString(1)),
            cancellationToken).ConfigureAwait(false);
    }

    private static string RequireAgentName()
        => AgentPrismRunContext.Current?.AgentName
           ?? throw new AgentPrismException(
               "Kalici dosya belleği yalnizca suren bir calistirma icinde kullanilabilir.");

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

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;
        return command;
    }
}
#pragma warning restore MAAI001
