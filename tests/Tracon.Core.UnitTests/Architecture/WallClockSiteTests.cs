using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every file in <c>src/</c> that reads the wall clock directly (<c>DateTimeOffset.UtcNow</c>, <c>DateTime.UtcNow</c>), and
/// every call that hands the audit recorder a <see langword="null"/> clock, is a known site with its count and the reason
/// it stays.
/// </summary>
/// <remarks>
/// <para>
/// A host registers one <see cref="TimeProvider"/>, and a test that moves it expects every stamp and every decision about
/// time to follow. A direct read of the wall clock does not: the stamp (or the age, or the wait) is the system's, whatever
/// the host registered, and it shows only to a test that moves the clock. The class scan found one such read that decided
/// something (a <c>Retry-After</c> measured against a different clock than the reset time it was subtracted from) and many
/// that stamp a row.
/// </para>
/// <para>
/// The gate does not read the code: it makes a NEW read fail until someone names the file, the count and why it stays, and
/// it fails when a known read goes away or a known file reads the clock more often, so the list cannot go stale and can
/// only get shorter. New code takes a <see cref="TimeProvider"/>. The shipped test-support packages
/// (<c>src/Tracon.Testing*</c>) are not scanned: they build sample data and are not product behavior.
/// </para>
/// </remarks>
public sealed partial class WallClockSiteTests
{
    private static readonly Dictionary<string, (int Count, string Reason)> KnownReads = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Abstractions/Security/ApiKeyRecord.cs"] = (1, "ApiKeyRecord.IsActive is documented as a display value only; authentication reads the registered clock (ApiKeyAuthenticator)"),
        ["src/Tracon.Abstractions/TraconId.cs"] = (1, "the time component of a new identifier"),
        ["src/Tracon.AspNetCore/A2A/ExternalAgentProxy.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs"] = (2, "the endpoint stamps what it writes and the time a queued job is scheduled for (the node's reading, as the job store documents); not read from the registered clock yet"),
        ["src/Tracon.AspNetCore/Endpoints/GovernanceEndpoints.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.AspNetCore/Endpoints/SchedulingEndpoints.cs"] = (3, "the endpoint stamps what it writes and the time a queued job is scheduled for (the node's reading, as the job store documents); not read from the registered clock yet"),
        ["src/Tracon.AspNetCore/Endpoints/SkillScriptGrantEndpoints.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.AspNetCore/Endpoints/TenantProviderEndpoints.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.AspNetCore/Endpoints/VoiceEndpoints.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs"] = (3, "the endpoint stamps what it writes and the time a queued job is scheduled for (the node's reading, as the job store documents); not read from the registered clock yet"),
        ["src/Tracon.AspNetCore/Internal/ToolApprovalDecisionRecorder.cs"] = (1, "RememberAsync stamps the standing rule; neither caller holds a clock in scope yet"),
        ["src/Tracon.AspNetCore/McpServer/CatalogToolCallHandler.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.AspNetCore/OpenAICompat/OpenAIChatCompletionsEndpoints.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.AspNetCore/OpenAICompat/OpenAICompatSupport.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.AspNetCore/OpenAICompat/OpenAIConversationsEndpoints.cs"] = (1, "the stamp an endpoint writes on a row or a response it builds; no decision compares it with another clock"),
        ["src/Tracon.Azure/AzureOpenAIProviderHealthCheck.cs"] = (2, "a latency or an age measured for a response or a health check; no decision reads it"),
        ["src/Tracon.Core/Audit/AuditRecorder.cs"] = (1, "WriteAsync stamps the audit entry; its call sites (about fifty) hand it no clock, so a clock parameter is the change that retires this read"),
        ["src/Tracon.Core/Evaluation/InMemoryEvalStore.cs"] = (3, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Core/Experiments/InMemoryExperimentStore.cs"] = (5, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Core/Images/ImageAttachmentWriter.cs"] = (1, "the stamp written on the record the class saves; no decision compares it with another clock"),
        ["src/Tracon.Core/Privacy/NullDataSubjectStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Core/Recording/NonTokenUsageReporter.cs"] = (1, "the stamp of a usage row written beside the run writer, which holds the registered clock but does not hand it over yet"),
        ["src/Tracon.Core/Replay/RecordedToolPlayback.cs"] = (1, "the stamp written on the record the class saves; no decision compares it with another clock"),
        ["src/Tracon.Core/Storage/InMemoryAgentDefinitionStore.cs"] = (2, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Core/Storage/InMemoryAgentSkillStore.cs"] = (2, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Core/Storage/InMemoryApprovalAndMcpStores.cs"] = (2, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Core/Storage/InMemoryAttachmentStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Core/Storage/InMemorySkillScriptGrantStore.cs"] = (2, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Core/Storage/InMemoryWorkflowStores.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.PostgreSql/Stores/PgVectorSearchStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Providers.Shared/ModelProviderCore.cs"] = (1, "a latency or an age measured for a response or a health check; no decision reads it"),
        ["src/Tracon.Providers.Shared/ProviderHealthCheckCore.cs"] = (1, "a latency or an age measured for a response or a health check; no decision reads it"),
        ["src/Tracon.Sql.Shared/Migrations/MigrationHostedService.cs"] = (1, "the migration runner: it runs before the host's clock exists and stamps what it applied"),
        ["src/Tracon.Sql.Shared/Migrations/MigrationRunner.cs"] = (1, "the migration runner: it runs before the host's clock exists and stamps what it applied"),
        ["src/Tracon.Sql.Shared/Stores/SqlAgentDefinitionStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlAgentFileStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlAgentSkillStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlApprovalAndMcpStores.cs"] = (2, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlAttachmentStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlChatHistoryProvider.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlConversationBranchStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlDataSubjectStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlEvalStore.cs"] = (2, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlExperimentStore.cs"] = (6, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlIdempotencyStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlSkillScriptGrantStore.cs"] = (2, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Sql.Shared/Stores/SqlWorkflowDefinitionStore.cs"] = (1, "the stamp a store writes on a row it saves (created, updated, completed, deleted); nothing compares it with another clock to decide"),
        ["src/Tracon.Voice/Internal/ElevenLabsSpeechClient.cs"] = (1, "a latency or an age measured for a response or a health check; no decision reads it"),
        ["src/Tracon.Voice/Internal/SynthesizerVoiceHealthCheck.cs"] = (1, "a latency or an age measured for a response or a health check; no decision reads it"),
        ["src/Tracon.Voice/Tools/SpeakTool.cs"] = (1, "a latency or an age measured for a response or a health check; no decision reads it"),
        ["src/Tracon.Workflows/Internal/TraconCheckpointStore.cs"] = (1, "the stamp written on the record the class saves; no decision compares it with another clock"),
    };

    private static readonly Dictionary<string, (int Count, string Reason)> KnownNullClocks = new(StringComparer.Ordinal)
    {
        ["src/Tracon.AspNetCore/Internal/ToolInputResolver.cs"] = (1, "the audit row of an answer to an input request; the resolver is static and its caller holds no clock in scope yet"),
        ["src/Tracon.Core/Compilation/CompiledAgentCache.cs"] = (1, "the constructor chain of the cache: it hands the clock on to the entry it builds"),
    };

    [Fact]
    public void Every_direct_wall_clock_read_is_a_known_site_with_its_count()
    {
        var actual = Scan(WallClockRead());

        AssertMatches(
            actual,
            KnownReads,
            "A new read of the wall clock was added. Take a TimeProvider (the constructor, a [FromServices] parameter, or the " +
            "request's services) and read the time from it. Only when no decision uses the value and no clock can reach the " +
            "code, add the file here with its count and the reason.");
    }

    [Fact]
    public void Every_null_clock_handed_to_the_audit_recorder_is_a_known_call()
    {
        var actual = Scan(NullClockArgument());

        AssertMatches(
            actual,
            KnownNullClocks,
            "A call hands the audit recorder a null clock, so the entry is stamped with the system clock whatever the host " +
            "registered. Pass the registered TimeProvider.");
    }

    [Theory]
    [InlineData("var now = DateTimeOffset.UtcNow;", 1)]
    [InlineData("return DateTime.UtcNow - start;", 1)]
    [InlineData("var a = DateTimeOffset.UtcNow; var b = DateTime.UtcNow;", 2)]
    [InlineData("var now = clock.GetUtcNow();", 0)]
    [InlineData("// DateTimeOffset.UtcNow is read here", 0)]
    [InlineData("/// <see cref=\"DateTimeOffset.UtcNow\"/>", 0)]
    public void The_scan_counts_a_direct_read_and_leaves_a_comment_alone(string source, int expected)
        => CountIn(source, WallClockRead()).ShouldBe(expected);

    [Theory]
    [InlineData("timeProvider: null,", 1)]
    [InlineData("timeProvider:null)", 1)]
    [InlineData("timeProvider: services.GetService<TimeProvider>(),", 0)]
    [InlineData("// timeProvider: null", 0)]
    public void The_scan_counts_a_null_clock_argument_and_leaves_a_comment_alone(string source, int expected)
        => CountIn(source, NullClockArgument()).ShouldBe(expected);

    [GeneratedRegex(
        @"\b(?:DateTimeOffset|DateTime)\.UtcNow\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex WallClockRead();

    [GeneratedRegex(
        @"\btimeProvider:\s*null\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex NullClockArgument();

    private static int CountIn(string source, Regex pattern)
        => source
            .Split('\n')
            .Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Sum(line => pattern.Count(line));

    private static Dictionary<string, int> Scan(Regex pattern)
    {
        var root = FindRepositoryRoot();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.StartsWith("src/Tracon.Testing", StringComparison.Ordinal))
            {
                continue;
            }

            var count = CountIn(File.ReadAllText(path), pattern);

            if (count > 0)
            {
                counts[relative] = count;
            }
        }

        return counts;
    }

    private static void AssertMatches(
        Dictionary<string, int> actual,
        Dictionary<string, (int Count, string Reason)> known,
        string newSiteAdvice)
    {
        var added = actual
            .Where(pair => !known.ContainsKey(pair.Key) || pair.Value > known[pair.Key].Count)
            .Select(pair => $"{pair.Key}: {pair.Value} (known: {(known.TryGetValue(pair.Key, out var entry) ? entry.Count : 0)})")
            .Order(StringComparer.Ordinal)
            .ToList();

        var gone = known
            .Where(pair => !actual.TryGetValue(pair.Key, out var count) || count < pair.Value.Count)
            .Select(pair => $"{pair.Key}: {(actual.TryGetValue(pair.Key, out var count) ? count : 0)} (known: {pair.Value.Count})")
            .Order(StringComparer.Ordinal)
            .ToList();

        added.ShouldBeEmpty(newSiteAdvice);
        gone.ShouldBeEmpty("A known site reads the clock less than the list says, or no longer reads it; lower the count or remove the file from the list.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
