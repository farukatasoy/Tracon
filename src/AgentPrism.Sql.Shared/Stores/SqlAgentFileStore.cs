using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;

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
internal sealed class SqlAgentFileStore : AgentFileStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir kalici dosya belleği olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
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

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public override async Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectAgentFile);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        DbHelpers.Add(command, "path", NormalizePath(path));

        return await DbHelpers
            .ReadSingleAsync(command, static reader => reader.GetString(0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var now = DateTimeOffset.UtcNow;
        var command = CreateCommand(_sql.UpsertAgentFile);
        DbHelpers.Add(command, "id", AgentPrismId.NewId(now));
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        DbHelpers.Add(command, "path", NormalizePath(path));
        DbHelpers.Add(command, "content", content);
        Dialect.AddTimestamp(command, "now", now);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.DeleteAgentFile);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        DbHelpers.Add(command, "path", NormalizePath(path));

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public override async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectAgentFile);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        DbHelpers.Add(command, "path", NormalizePath(path));

        return await DbHelpers
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
    /// <remarks>
    /// Faz 51 (Is A): dizinin ALTINDAKI dosyalar SQL'de onek suzgeciyle daraltilir;
    /// depodaki toplam dosya sayisindan degil, yalniz bu dizinin altindaki satir
    /// sayisindan etkilenir. Bkz. <c>docs/51-VEKTOR-BELLEK-VE-RAG.md</c>.
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
    /// Faz 51 (Is A): onek, derinlik siniri (<paramref name="recursive"/>) ve
    /// <paramref name="globPattern"/> SQL'e iner; <c>LoadAllAsync</c> artik
    /// cagrilmaz. PostgreSQL ayrica <paramref name="regexPattern"/>'i <c>~</c>
    /// operatoruyle on suzgec olarak indirir — nihai eslesme yine de HER ZAMAN
    /// .NET <see cref="Regex"/> ile burada yapilir, davranis degismez. Sunucuya
    /// gonderilen desen PostgreSQL'in ARE sozdiziminde gecersizse (ornegin .NET'e
    /// ozgu adlandirilmis gruplar), <see cref="SqlDialect.IsInvalidRegexError"/>
    /// bunu yakalar ve sorgu on suzgec OLMADAN yeniden calisir.
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
    /// Onek, istege bagli derinlik siniri, istege bagli glob suzgeci ve
    /// (yalniz PostgreSQL'de etkili) istege bagli regex on suzgeciyle daraltilmis
    /// dosyalari okur (Faz 51, Is A). <paramref name="deepLike"/> ve
    /// <paramref name="nameLike"/> cagiran tarafca ONEKI ICEREN tam LIKE
    /// desenleri olarak kurulur (bkz. <see cref="SearchAsync"/>).
    /// </summary>
    private async ValueTask<List<(string Path, string Content)>> LoadFilteredAsync(
        string prefix,
        string? deepLike,
        string? nameLike,
        string? regexPattern,
        CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectAgentFilesFiltered);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", RequireAgentName());
        Dialect.AddText(command, "prefix_like", EscapeLikeLiteral(prefix) + "%");
        Dialect.AddText(command, "prefix_deep_like", deepLike);
        Dialect.AddText(command, "name_like", nameLike);
        Dialect.AddText(command, "regex_pattern", regexPattern);

        return await DbHelpers.ReadListAsync(
            command,
            static reader => (reader.GetString(0), reader.GetString(1)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bir metni <c>LIKE</c> deseninde harfi harfine eslesecek sekilde kacislar.</summary>
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
    /// Bir glob desenini (<c>*</c>/<c>?</c>) <c>LIKE ... ESCAPE '\'</c> desenine cevirir.
    /// </summary>
    /// <remarks>
    /// <c>*</c> orijinal .NET regex tabanli eslemede oldugu gibi <c>/</c> dahil
    /// HERHANGI bir karakter dizisiyle eslesir (dizin sinirini asabilir); <c>LIKE</c>
    /// icindeki <c>%</c> ayni davranisi tasir.
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

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);
}
#pragma warning restore MAAI001
