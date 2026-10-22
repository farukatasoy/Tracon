using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs (K-269): the package's Tracon.Testing.TraconTestHost and
// this project's own TraconTestHost share a name.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

// MAAI001: AgentFileStore and FileAccessProvider are marked "evaluation purposes only" - the same
// rationale as AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 234 (F-628): the file tools of a session workspace are governed like any other tool: the quota refuses a write
/// and the run goes on, the timeout ends a call that hangs, and a store that fails ends one call and not the run.
/// </summary>
/// <remarks>
/// A write needs a person's approval, so each write here goes through the real approval flow and the limit is met at the
/// moment the approved call runs.
/// </remarks>
public sealed class SessionWorkspaceGovernanceTests
{
    private const string ProviderName = "governance-files-model";

    [Fact]
    public async Task A_write_past_the_file_limit_is_refused_after_the_approval_and_the_model_reads_why_and_the_run_completes()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_write", new { fileName = "big.md", content = new string('a', 40) })
            .RespondsWith("Understood.")
            .EchoesUserMessage();

        await using var host = await StartAsync(provider, configureServices: static services =>
            services.Configure<TraconWorkspaceOptions>(static options =>
            {
                options.MaxFileBytes = 20;
                options.MaxSessionBytes = 100_000;
                options.MaxTenantBytes = 1_000_000;
            }));

        await WriteAsync(host, "session-1");

        var told = ToolResults(provider);
        told.ShouldStartWith("Workspace quota reached");
        told.ShouldContain("20 bytes");
        (await host.Services.GetRequiredService<AgentFileStore>()
            .ReadAsync(SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/big.md")).ShouldBeNull();
    }

    [Fact]
    public async Task A_write_past_the_session_limit_is_refused_even_though_the_file_fits_and_the_run_completes()
    {
        // 2 500 bytes fit the file limit of 3 000; only the session rule can refuse this write, because the 1 KiB the row
        // costs counts as well.
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_write", new { fileName = "notes.md", content = new string('a', 2500) })
            .RespondsWith("Understood.")
            .EchoesUserMessage();

        await using var host = await StartAsync(provider, configureServices: static services =>
            services.Configure<TraconWorkspaceOptions>(static options =>
            {
                options.MaxFileBytes = 3000;
                options.MaxSessionBytes = 3000;
                options.MaxTenantBytes = 1_000_000;
            }));

        await WriteAsync(host, "session-1");

        var told = ToolResults(provider);
        told.ShouldStartWith("Workspace quota reached");
        told.ShouldContain("files of this session");
        told.ShouldContain("3000 bytes");
        (await host.Services.GetRequiredService<AgentFileStore>()
            .ReadAsync(SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/notes.md")).ShouldBeNull();
    }

    [Fact]
    public async Task A_write_past_the_tenant_limit_is_refused_when_another_session_has_used_it_up()
    {
        // Session 1 writes and fits. Session 2 would fit its own limit, and passes the tenant's.
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_write", new { fileName = "notes.md", content = new string('a', 2000) })
            .RespondsWith("Saved.")
            .CallsTool("file_access_write", new { fileName = "notes.md", content = new string('b', 2000) })
            .RespondsWith("Understood.")
            .EchoesUserMessage();

        await using var host = await StartAsync(provider, configureServices: static services =>
            services.Configure<TraconWorkspaceOptions>(static options =>
            {
                options.MaxFileBytes = 4000;
                options.MaxSessionBytes = 4000;
                options.MaxTenantBytes = 4000;
            }));
        var store = host.Services.GetRequiredService<AgentFileStore>();

        await WriteAsync(host, "session-1");
        await WriteAsync(host, "session-2");

        (await store.ReadAsync(SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/notes.md")).ShouldNotBeNull();
        (await store.ReadAsync(SessionWorkspacePaths.AgentRoot("default", "session-2", "drafter") + "/notes.md")).ShouldBeNull();
        provider.Requests[^1].Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>()
            .Select(static result => result.Result?.ToString())
            .Any(static text => text is not null && text.Contains("workspaces of this tenant are full", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_call_that_hangs_ends_at_the_tool_timeout_and_the_run_goes_on()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_read", new { fileName = "notes.md" })
            .RespondsWith("Gave up.")
            .EchoesUserMessage();

        await using var host = await StartAsync(
            provider,
            new WorkspaceSettings { Enabled = true, DisableReadApproval = true },
            configureTracon: static builder => builder.Configure(static options => options.Tools.DefaultTimeout = TimeSpan.FromMilliseconds(200)),
            configureServices: static services => services.AddSingleton<AgentFileStore>(new HangingReadStore()));

        var started = DateTimeOffset.UtcNow;
        var runId = await SessionWorkspaceApprovalTests.QueueAsync(host, "session-1");

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");
        (DateTimeOffset.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(30));
        provider.Requests[^1].Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>()
            .Select(static result => result.Result?.ToString())
            .Contains("Tool 'file_access_read' did not complete within 200ms.", StringComparer.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task A_store_that_fails_ends_the_call_and_not_the_run_and_the_model_is_told_nothing_about_the_store()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_read", new { fileName = "notes.md" })
            .RespondsWith("Could not read it.")
            .EchoesUserMessage();

        await using var host = await StartAsync(
            provider,
            new WorkspaceSettings { Enabled = true, DisableReadApproval = true },
            configureServices: static services => services.AddSingleton<AgentFileStore>(new FailingStore()));

        var runId = await SessionWorkspaceApprovalTests.QueueAsync(host, "session-1");

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        var told = string.Join(
            "\n",
            provider.Requests[^1].Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().Select(static result => result.Result?.ToString()));
        told.ShouldNotContain("connection string", customMessage: "an exception of the store is not shown to the model");
        told.ShouldNotContain("db.internal.example");
    }

    [Fact]
    public async Task A_file_tool_is_denied_by_the_authorization_handler_with_the_effect_of_what_it_does()
    {
        var provider = new FakeModelProvider(ProviderName)
            .CallsTool("file_access_ls", new { })
            .RespondsWith("done")
            .EchoesUserMessage();
        var handler = new RecordingDenyingHandler();

        await using var host = await StartAsync(
            provider,
            new WorkspaceSettings { Enabled = true, DisableReadApproval = true },
            configureServices: services => services.AddSingleton<IToolAuthorizationHandler>(handler));
        await host.Services.GetRequiredService<AgentFileStore>()
            .WriteAsync(SessionWorkspacePaths.AgentRoot("default", "session-1", "drafter") + "/notes.md", "alpha");

        var runId = await SessionWorkspaceApprovalTests.QueueAsync(host, "session-1");

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");
        handler.Seen.ShouldBe([("file_access_ls", ToolEffect.Read)]);
        string.Join(
            "\n",
            provider.Requests[^1].Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().Select(static result => result.Result?.ToString()))
            .ShouldNotContain("notes.md");
    }

    /// <summary>Queues a run that calls the write tool, approves the call, and waits for the run that carries it out.</summary>
    private static async Task WriteAsync(TraconTestHost host, string sessionId)
    {
        var originalRunId = await SessionWorkspaceApprovalTests.QueueAsync(host, sessionId);
        await host.WaitForRunStatusAsync(originalRunId, "AwaitingApproval");
        await SessionWorkspaceApprovalTests.DecideAsync(host, await SessionWorkspaceApprovalTests.PendingAsync(host), approved: true);
        await SessionWorkspaceApprovalTests.WaitForResumedRunAsync(host, sessionId, originalRunId);
    }

    /// <summary>The text of every tool result the model had read when its last request was made.</summary>
    private static string ToolResults(FakeModelProvider provider)
        => string.Join(
            "\n",
            provider.Requests[^1].Messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().Select(static result => result.Result?.ToString()));

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        WorkspaceSettings? workspace = null,
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .AddAgent(new AgentDefinition
                    {
                        Name = "drafter",
                        Instructions = "Work with files.",
                        Model = new ModelBinding { Provider = ProviderName, Model = "files-1" },
                        Workspace = workspace ?? new WorkspaceSettings { Enabled = true, DisableReadApproval = true },
                        Origin = AgentDefinitionOrigin.Code,
                    });
                configureTracon?.Invoke(builder);
            },
            configureServices: services =>
            {
                services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
                configureServices?.Invoke(services);
            });

    private sealed class RecordingDenyingHandler : IToolAuthorizationHandler
    {
        public List<(string Tool, ToolEffect Effect)> Seen { get; } = [];

        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Seen.Add((request.ToolName, request.Effect));

            return new(ToolAuthorizationResult.Deny("no files today"));
        }
    }

    /// <summary>A store that waits for ever on a read, until the caller gives up.</summary>
    private sealed class HangingReadStore : AgentFileStore
    {
        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override async Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated

            return null;
        }

        public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileStoreEntry>>([]);

        public override Task<IReadOnlyList<FileSearchResult>> SearchAsync(
            string directory,
            string regexPattern,
            string? globPattern = null,
            bool recursive = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileSearchResult>>([]);
    }

    /// <summary>A store whose every call fails with a message that names the host of the database.</summary>
    private sealed class FailingStore : AgentFileStore
    {
        private static InvalidOperationException Failure() => new("connection string for db.internal.example refused");

        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => throw Failure();

        public override Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default) => throw Failure();

        public override Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => throw Failure();

        public override Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) => throw Failure();

        public override Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw Failure();

        public override Task<IReadOnlyList<FileStoreEntry>> ListChildrenAsync(string directory, CancellationToken cancellationToken = default) => throw Failure();

        public override Task<IReadOnlyList<FileSearchResult>> SearchAsync(
            string directory,
            string regexPattern,
            string? globPattern = null,
            bool recursive = false,
            CancellationToken cancellationToken = default)
            => throw Failure();
    }
}
