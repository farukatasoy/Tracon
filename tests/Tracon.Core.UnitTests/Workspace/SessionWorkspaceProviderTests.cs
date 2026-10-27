using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Workspace;

/// <summary>
/// What the provider offers a run and how each file tool runs: only to a run that has a session, through the Tracon
/// tool chain, and with the approval the definition allows.
/// </summary>
/// <remarks>
/// The provider is driven the way the framework drives it, with the run's identity set in
/// <see cref="TraconRunContext"/>, and each tool is invoked as the function-invoking client invokes it. Each test sets and
/// clears that scope in its own body (it is an <c>AsyncLocal</c>).
/// </remarks>
// MAAI001: the context provider contexts and the file store are marked "evaluation purposes only" - see K-020.
#pragma warning disable MAAI001
public sealed class SessionWorkspaceProviderTests : IDisposable
{
    private static readonly string[] AllTools =
    [
        "file_access_delete",
        "file_access_grep",
        "file_access_ls",
        "file_access_read",
        "file_access_read_lines",
        "file_access_replace",
        "file_access_replace_lines",
        "file_access_write",
    ];

    private readonly InMemoryAgentFileStore _raw = new();
    private readonly AIAgent _agent = new ChatClientAgent(new FakeChatClient());

    public void Dispose() => TraconRunContext.SetCurrent(null);

    [Fact]
    public async Task A_run_with_no_session_is_offered_no_tool_and_no_instruction()
    {
        var provider = Provider();
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", session: null));

        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));

        (context.Tools ?? []).ShouldBeEmpty();
        context.Instructions.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_with_a_session_is_offered_the_eight_tools_and_the_instruction_about_the_workspace()
    {
        var provider = Provider();
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));

        context.Tools!.Select(static tool => tool.Name).Order(StringComparer.Ordinal).ShouldBe(AllTools);
        context.Instructions.ShouldNotBeNull().ShouldContain("Session workspace");
        context.Instructions.ShouldContain("deleted when the conversation is deleted", customMessage: "the default text of the framework says the files are shared across sessions and agents");
    }

    [Fact]
    public async Task The_tools_and_instructions_the_run_already_had_stay()
    {
        var provider = Provider();
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        var own = AIFunctionFactory.Create(() => "x", "own_tool");

        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext { Instructions = "Be brief.", Tools = [own] }));

        context.Tools!.Any(static tool => string.Equals(tool.Name, "own_tool", StringComparison.Ordinal)).ShouldBeTrue();
        context.Tools!.Count().ShouldBe(9);
        context.Instructions.ShouldNotBeNull().ShouldStartWith("Be brief.");
    }

    [Fact]
    public async Task A_read_only_workspace_is_offered_only_the_tools_that_read()
    {
        var provider = Provider(new WorkspaceSettings { Enabled = true, ReadOnly = true });
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));

        context.Tools!.Select(static tool => tool.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["file_access_grep", "file_access_ls", "file_access_read", "file_access_read_lines"]);
        context.Instructions.ShouldNotBeNull().ShouldContain("You cannot change them");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tool_that_changes_a_file_asks_for_approval_whatever_the_definition_says_and_a_tool_that_reads_asks_unless_the_definition_turns_it_off(bool disableReadApproval)
    {
        var provider = Provider(new WorkspaceSettings { Enabled = true, DisableReadApproval = disableReadApproval });
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));

        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));

        var asks = context.Tools!.ToDictionary(static tool => tool.Name, static tool => tool.GetService<ApprovalRequiredAIFunction>() is not null, StringComparer.Ordinal);

        foreach (var name in new[] { "file_access_write", "file_access_delete", "file_access_replace", "file_access_replace_lines" })
        {
            asks[name].ShouldBeTrue(name);
        }

        foreach (var name in new[] { "file_access_read", "file_access_read_lines", "file_access_ls", "file_access_grep" })
        {
            asks[name].ShouldBe(!disableReadApproval, name);
        }
    }

    [Fact]
    public async Task Each_tool_runs_through_the_tool_chain_with_the_effect_that_matches_what_it_does()
    {
        var handler = new RecordingHandler();
        var provider = Provider(handler: handler);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));

        foreach (var tool in context.Tools!.OfType<AIFunction>())
        {
            tool.GetService<AuthorizingAIFunction>().ShouldNotBeNull(tool.Name);
            tool.GetService<TimeoutAIFunction>().ShouldNotBeNull(tool.Name);
            tool.GetService<TruncatingAIFunction>().ShouldNotBeNull(tool.Name);
            tool.GetService<ExplainedFailureAIFunction>().ShouldNotBeNull(tool.Name);
            tool.ShouldBeOfType<CallRecordingAIFunction>(tool.Name);
        }

        await InvokeAsync(context, "file_access_write", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md", ["content"] = "alpha" });
        await InvokeAsync(context, "file_access_read", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md" });
        await InvokeAsync(context, "file_access_delete", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md" });

        handler.Seen.ShouldBe(
        [
            ("file_access_write", ToolEffect.Write),
            ("file_access_read", ToolEffect.Read),
            ("file_access_delete", ToolEffect.Destructive),
        ]);
    }

    [Fact]
    public async Task A_denied_call_changes_no_file()
    {
        var handler = new RecordingHandler { Deny = true };
        var provider = Provider(handler: handler);
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));

        var result = await InvokeAsync(context, "file_access_write", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md", ["content"] = "alpha" });

        result?.ToString().ShouldBe("no files today");
        (await _raw.ReadAsync(SessionWorkspacePaths.AgentRoot("acme", "session-1", "drafter") + "/a.md")).ShouldBeNull();
    }

    [Fact]
    public async Task A_call_that_passes_the_quota_is_explained_to_the_model_and_the_run_goes_on()
    {
        var provider = Provider(limits: WorkspaceTestSupport.Limits(file: 10));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));

        var result = await InvokeAsync(context, "file_access_write", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md", ["content"] = new string('a', 11) });

        result?.ToString().ShouldStartWith("Workspace quota reached");
    }

    [Fact]
    public async Task A_tool_that_hangs_ends_at_the_timeout_and_the_model_reads_why()
    {
        var provider = Provider(store: new HangingStore(), timeout: TimeSpan.FromMilliseconds(150));
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));
        var started = DateTimeOffset.UtcNow;

        // The bound is the test's own guard: without the timeout layer the store would wait for ever and the test would hang.
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await InvokeAsync(context, "file_access_read", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md" }, bound.Token);

        (DateTimeOffset.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(10));
        result?.ToString().ShouldNotBeNull().ShouldBe("Tool 'file_access_read' did not complete within 150ms.");
    }

    [Fact]
    public async Task A_cancelled_write_stores_nothing_and_is_not_reported_as_a_result()
    {
        var provider = Provider();
        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        var context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await InvokeAsync(
            context, "file_access_write", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md", ["content"] = "alpha" }, cancelled.Token));

        (await _raw.ReadAsync(SessionWorkspacePaths.AgentRoot("acme", "session-1", "drafter") + "/a.md")).ShouldBeNull();
    }

    [Fact]
    public async Task One_provider_serves_every_session_and_each_run_writes_below_its_own()
    {
        var provider = Provider();

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-1"));
        var first = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));
        await InvokeAsync(first, "file_access_write", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md", ["content"] = "one" });

        TraconRunContext.SetCurrent(WorkspaceTestSupport.Run("acme", "drafter", "session-2"));
        var second = await provider.InvokingAsync(new AIContextProvider.InvokingContext(_agent, session: null, new AIContext()));
        await InvokeAsync(second, "file_access_write", new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "a.md", ["content"] = "two" });

        second.Tools!.ShouldBe(first.Tools!, "the wrapped tools are built once");
        (await _raw.ReadAsync(SessionWorkspacePaths.AgentRoot("acme", "session-1", "drafter") + "/a.md")).ShouldBe("one");
        (await _raw.ReadAsync(SessionWorkspacePaths.AgentRoot("acme", "session-2", "drafter") + "/a.md")).ShouldBe("two");
    }

    [Theory]
    [InlineData("file_access_read", ToolEffect.Read)]
    [InlineData("file_access_read_lines", ToolEffect.Read)]
    [InlineData("file_access_ls", ToolEffect.Read)]
    [InlineData("file_access_grep", ToolEffect.Read)]
    [InlineData("file_access_write", ToolEffect.Write)]
    [InlineData("file_access_replace", ToolEffect.Write)]
    [InlineData("file_access_replace_lines", ToolEffect.Write)]
    [InlineData("file_access_delete", ToolEffect.Destructive)]
    [InlineData("file_access_a_tool_the_framework_adds_later", ToolEffect.Write)]
    public void A_tool_is_classified_by_what_it_does_and_one_this_code_does_not_know_is_treated_as_a_write(string name, ToolEffect expected)
        => SessionWorkspaceProvider.EffectOf(name).ShouldBe(expected);

    private SessionWorkspaceProvider Provider(
        WorkspaceSettings? settings = null,
        RecordingHandler? handler = null,
        Microsoft.Extensions.Options.IOptionsMonitor<TraconWorkspaceOptions>? limits = null,
        AgentFileStore? store = null,
        TimeSpan? timeout = null)
    {
        var options = new TraconOptions();

        if (timeout is { } value)
        {
            options.Tools.DefaultTimeout = value;
        }

        var governance = new BuiltInToolGovernance(
            handler ?? new RecordingHandler(),
            NoOpToolArgumentsValidator.Instance,
            new StaticOptionsMonitor<TraconOptions>(options),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        return new SessionWorkspaceProvider(
            WorkspaceTestSupport.Store(store ?? _raw, limits: limits ?? WorkspaceTestSupport.Limits(file: 1000, session: 10_000, tenant: 100_000)),
            settings ?? new WorkspaceSettings { Enabled = true, DisableReadApproval = true },
            governance);
    }

    private static async Task<object?> InvokeAsync(AIContext context, string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
        => await context.Tools!.OfType<AIFunction>().Single(function => string.Equals(function.Name, tool, StringComparison.Ordinal))
            .InvokeAsync(new AIFunctionArguments(arguments, StringComparer.Ordinal), cancellationToken);

    private sealed class RecordingHandler : IToolAuthorizationHandler
    {
        public bool Deny { get; init; }

        public List<(string Tool, ToolEffect Effect)> Seen { get; } = [];

        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Seen.Add((request.ToolName, request.Effect));

            return new(Deny ? ToolAuthorizationResult.Deny("no files today") : ToolAuthorizationResult.Allow());
        }
    }

    /// <summary>A store whose every read waits for ever, until the caller gives up.</summary>
    private sealed class HangingStore : AgentFileStore
    {
        public override Task WriteAsync(string path, string content, CancellationToken cancellationToken = default) => Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated

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
}
#pragma warning restore MAAI001
