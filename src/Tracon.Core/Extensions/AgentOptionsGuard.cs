using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The member table that decides which Microsoft Agent Framework options an
/// <see cref="IAgentExtension"/> may change, and the check that enforces it.
/// </summary>
/// <remarks>
/// <para>
/// The compiler hands an extension the very options object it builds the agent from.
/// A member is <see cref="Rule.Protected"/> when the definition or one of Tracon's
/// tenant and security decisions sets it: the tool chain, the tenant's chat history,
/// the working-directory skill scan, file access, the model parameters, the loop and the
/// sub-agent tree. A member is also protected when it changes one of those indirectly, or when
/// Microsoft Agent Framework puts its effect under a tool loop that Tracon's pipeline moves.
/// The check reads those members before an extension runs and after it; a
/// changed one fails the compilation. It never writes the old value back, because a
/// silent fix would hide the mistake.
/// </para>
/// <para>
/// This table is the single list. A test reflects over every settable member of the
/// types below and fails on one that has no row, so a new Microsoft Agent Framework
/// member must be classified before the build is green
/// (<c>AgentOptionsGuardTests.Every_member_is_classified</c>). The reflection lives in
/// the test; this class reads members by name and is AOT safe.
/// </para>
/// <para>
/// A reference is compared by identity, a sequence element by element by identity, a
/// value by equality. A nested options object that is protected is compared member by
/// member, because an in-place change would pass a reference check.
/// </para>
/// </remarks>
// MAAI001: HarnessAgentOptions members, LoopAgentOptions, BackgroundAgentsProviderOptions and
// FileAccessProviderOptions are marked "evaluation purposes only". The suppression covers this
// table only, the same rationale as AgentDefinitionCompiler.Agents.cs. Rationale:
// docs/KARARLAR.md, decision K-020.
#pragma warning disable MAAI001
internal static class AgentOptionsGuard
{
    /// <summary>What an extension may do with a member.</summary>
    internal enum Rule
    {
        /// <summary>The extension may change the member.</summary>
        Free,

        /// <summary>The member must come back unchanged.</summary>
        Protected,

        /// <summary>The providers that were there stay in place, in order; the extension may add after them.</summary>
        AppendOnly,
    }

    /// <summary>The rows of <see cref="ChatOptions"/>.</summary>
    internal static OptionRules<ChatOptions> Chat { get; } = new OptionRules<ChatOptions>()
        .Protect(nameof(ChatOptions.Tools), static options => Sequence(options.Tools))
        .Protect(nameof(ChatOptions.Instructions), static options => options.Instructions)
        .Protect(nameof(ChatOptions.ResponseFormat), static options => Reference(options.ResponseFormat))
        .Protect(nameof(ChatOptions.ModelId), static options => options.ModelId)
        .Protect(nameof(ChatOptions.Temperature), static options => options.Temperature)
        .Protect(nameof(ChatOptions.TopP), static options => options.TopP)
        .Protect(nameof(ChatOptions.MaxOutputTokens), static options => options.MaxOutputTokens)

        // A conversation id says the service keeps the history, and the agent then leaves
        // ChatHistoryProvider (the tenant's history) unused. The definition never sets one.
        .Protect(nameof(ChatOptions.ConversationId), static options => options.ConversationId)
        .Protect(nameof(ChatOptions.Reasoning), static options => options.Reasoning is { } reasoning
            ? new object?[] { reasoning.Effort, reasoning.Output }
            : null)

        // The provider SDKs build the request from the raw representation first and fill only what
        // it leaves empty, so it wins over the model, temperature, token limit and reasoning above.
        // The Anthropic and Google rings also skip the definition's ProviderSettings when it is set.
        .Protect(nameof(ChatOptions.RawRepresentationFactory), static options => Reference(options.RawRepresentationFactory))
        .Free(
            nameof(ChatOptions.AdditionalProperties),
            nameof(ChatOptions.AllowBackgroundResponses),
            nameof(ChatOptions.AllowMultipleToolCalls),
            nameof(ChatOptions.ContinuationToken),
            nameof(ChatOptions.FrequencyPenalty),
            nameof(ChatOptions.PresencePenalty),
            nameof(ChatOptions.Seed),
            nameof(ChatOptions.StopSequences),
            nameof(ChatOptions.ToolMode),
            nameof(ChatOptions.TopK));

    /// <summary>The rows of <see cref="BackgroundAgentsProviderOptions"/>; the sub-agent tree owns all of it.</summary>
    internal static OptionRules<BackgroundAgentsProviderOptions> BackgroundAgentsOptions { get; } = new OptionRules<BackgroundAgentsProviderOptions>()
        .Protect(nameof(BackgroundAgentsProviderOptions.AgentListBuilder), static options => Reference(options.AgentListBuilder))
        .Protect(nameof(BackgroundAgentsProviderOptions.Instructions), static options => options.Instructions)
        .Protect(nameof(BackgroundAgentsProviderOptions.WaitTimeout), static options => options.WaitTimeout);

    /// <summary>The rows of <see cref="LoopAgentOptions"/>; the definition's loop owns all of it.</summary>
    internal static OptionRules<LoopAgentOptions> LoopOptions { get; } = new OptionRules<LoopAgentOptions>()
        .Protect(nameof(LoopAgentOptions.ExcludeOnBehalfOfMessages), static options => options.ExcludeOnBehalfOfMessages)
        .Protect(nameof(LoopAgentOptions.FreshContextPerIteration), static options => options.FreshContextPerIteration)
        .Protect(nameof(LoopAgentOptions.MaxIterations), static options => options.MaxIterations)
        .Protect(nameof(LoopAgentOptions.NonStreamingReturnsLastResponseOnly), static options => options.NonStreamingReturnsLastResponseOnly)
        .Protect(nameof(LoopAgentOptions.OnBehalfOfAuthorName), static options => options.OnBehalfOfAuthorName)
        .Protect(nameof(LoopAgentOptions.SessionCreatedCallback), static options => Reference(options.SessionCreatedCallback));

    /// <summary>The rows of <see cref="FileAccessProviderOptions"/>; file access is closed.</summary>
    internal static OptionRules<FileAccessProviderOptions> FileAccessOptions { get; } = new OptionRules<FileAccessProviderOptions>()
        .Protect(nameof(FileAccessProviderOptions.DisableReadOnlyToolApproval), static options => options.DisableReadOnlyToolApproval)
        .Protect(nameof(FileAccessProviderOptions.DisableWriteToolApproval), static options => options.DisableWriteToolApproval)
        .Protect(nameof(FileAccessProviderOptions.DisableWriteTools), static options => options.DisableWriteTools)
        .Protect(nameof(FileAccessProviderOptions.Instructions), static options => options.Instructions);

    /// <summary>The rows of <see cref="ChatClientAgentOptions"/>.</summary>
    internal static OptionRules<ChatClientAgentOptions> ChatAgent { get; } = new OptionRules<ChatClientAgentOptions>()
        .Protect(nameof(ChatClientAgentOptions.Id), static options => options.Id)
        .Protect(nameof(ChatClientAgentOptions.Name), static options => options.Name)
        .Protect(nameof(ChatClientAgentOptions.Description), static options => options.Description)
        .ProtectNested(nameof(ChatClientAgentOptions.ChatOptions), static options => options.ChatOptions, Chat)
        .Protect(nameof(ChatClientAgentOptions.ChatHistoryProvider), static options => Reference(options.ChatHistoryProvider))
        .AppendOnly(nameof(ChatClientAgentOptions.AIContextProviders), static options => options.AIContextProviders)

        // The approval gate. Binding keeps an approval tied to the request a human was shown, and
        // invocable-function bypassing re-injects stored calls as pre-approved responses.
        .Protect(nameof(ChatClientAgentOptions.DisableApprovalResponseBinding), static options => options.DisableApprovalResponseBinding)
        .Protect(nameof(ChatClientAgentOptions.EnableInvocableFunctionBypassing), static options => options.EnableInvocableFunctionBypassing)

        // MAF's default chat client stack holds the approval binding and the per-call history
        // writer. Using the client as is skips the whole stack: a forged approval runs the tool,
        // and with per-call persistence on, the tenant's history stays empty (measured).
        .Protect(nameof(ChatClientAgentOptions.UseProvidedChatClientAsIs), static options => options.UseProvidedChatClientAsIs)

        // ModelBinding.AllowConcurrentToolCalls sets the tool loop Tracon builds (K-553). MAF finds
        // that same loop and turns it on when this member is set, over the definition (measured).
        .Protect(nameof(ChatClientAgentOptions.AllowConcurrentInvocation), static options => options.AllowConcurrentInvocation)

        // MAF puts these two layers under the tool loop it builds, so they act on every model call.
        // Tracon's tool loop is inside the model client, so MAF builds none and the layers land above
        // it (measured): an injected message never reaches the running turn, and the history is
        // written once per turn, not once per model call.
        .Protect(nameof(ChatClientAgentOptions.EnableMessageInjection), static options => options.EnableMessageInjection)
        .Protect(nameof(ChatClientAgentOptions.RequirePerServiceCallChatHistoryPersistence), static options => options.RequirePerServiceCallChatHistoryPersistence)
        .Free(
            nameof(ChatClientAgentOptions.ClearOnChatHistoryProviderConflict),
            nameof(ChatClientAgentOptions.DisableApprovalNotRequiredFunctionBypassing),
            nameof(ChatClientAgentOptions.ThrowOnChatHistoryProviderConflict),
            nameof(ChatClientAgentOptions.WarnOnChatHistoryProviderConflict));

    /// <summary>The rows of <see cref="HarnessAgentOptions"/>.</summary>
    internal static OptionRules<HarnessAgentOptions> Harness { get; } = new OptionRules<HarnessAgentOptions>()
        .Protect(nameof(HarnessAgentOptions.Id), static options => options.Id)
        .Protect(nameof(HarnessAgentOptions.Name), static options => options.Name)
        .Protect(nameof(HarnessAgentOptions.Description), static options => options.Description)
        .ProtectNested(nameof(HarnessAgentOptions.ChatOptions), static options => options.ChatOptions, Chat)
        .Protect(nameof(HarnessAgentOptions.ChatHistoryProvider), static options => Reference(options.ChatHistoryProvider))
        .AppendOnly(nameof(HarnessAgentOptions.AIContextProviders), static options => options.AIContextProviders)

        // The definition's HarnessSettings, Compaction and Memory.
        .Protect(nameof(HarnessAgentOptions.HarnessInstructions), static options => options.HarnessInstructions)
        .Protect(nameof(HarnessAgentOptions.MaxContextWindowTokens), static options => options.MaxContextWindowTokens)
        .Protect(nameof(HarnessAgentOptions.MaxOutputTokens), static options => options.MaxOutputTokens)
        .Protect(nameof(HarnessAgentOptions.MaximumIterationsPerRequest), static options => options.MaximumIterationsPerRequest)
        .Protect(nameof(HarnessAgentOptions.DisableCompaction), static options => options.DisableCompaction)
        .Protect(nameof(HarnessAgentOptions.CompactionStrategy), static options => Reference(options.CompactionStrategy))
        .Protect(nameof(HarnessAgentOptions.DisableTodoProvider), static options => options.DisableTodoProvider)
        .Protect(nameof(HarnessAgentOptions.DisableAgentModeProvider), static options => options.DisableAgentModeProvider)
        .Protect(nameof(HarnessAgentOptions.DisableToolAutoApproval), static options => options.DisableToolAutoApproval)

        // The approval gate: the harness's own auto-approval layer sits inside Tracon's, so a rule that
        // approves everything runs a tool before Tracon's gate sees the request (measured).
        .Protect(nameof(HarnessAgentOptions.ToolApprovalAgentOptions), static options => Reference(options.ToolApprovalAgentOptions))
        .Protect(nameof(HarnessAgentOptions.DisableApprovalResponseBinding), static options => options.DisableApprovalResponseBinding)

        // K-062 (file access), K-892 (working-directory scan, file memory, paid web search), F-578.
        .Protect(nameof(HarnessAgentOptions.DisableFileMemory), static options => options.DisableFileMemory)
        .Protect(nameof(HarnessAgentOptions.FileMemoryStore), static options => Reference(options.FileMemoryStore))
        .Protect(nameof(HarnessAgentOptions.FileAccessStore), static options => Reference(options.FileAccessStore))
        .ProtectNested(nameof(HarnessAgentOptions.FileAccessProviderOptions), static options => options.FileAccessProviderOptions, FileAccessOptions)
        .Protect(nameof(HarnessAgentOptions.DisableWebSearch), static options => options.DisableWebSearch)
        .Protect(nameof(HarnessAgentOptions.DisableAgentSkillsProvider), static options => options.DisableAgentSkillsProvider)
        .Protect(nameof(HarnessAgentOptions.AgentSkillsSource), static options => Reference(options.AgentSkillsSource))

        // The sub-agent tree budget, the loop record and the span store.
        .Protect(nameof(HarnessAgentOptions.BackgroundAgents), static options => Sequence(options.BackgroundAgents))
        .ProtectNested(nameof(HarnessAgentOptions.BackgroundAgentsProviderOptions), static options => options.BackgroundAgentsProviderOptions, BackgroundAgentsOptions)
        .Protect(nameof(HarnessAgentOptions.LoopEvaluators), static options => Sequence(options.LoopEvaluators))
        .ProtectNested(nameof(HarnessAgentOptions.LoopAgentOptions), static options => options.LoopAgentOptions, LoopOptions)
        .Protect(nameof(HarnessAgentOptions.DisableOpenTelemetry), static options => options.DisableOpenTelemetry)
        .Protect(nameof(HarnessAgentOptions.OpenTelemetrySourceName), static options => options.OpenTelemetrySourceName)
        .Free(
            nameof(HarnessAgentOptions.AgentModeProviderOptions),
            nameof(HarnessAgentOptions.DisableApprovalNotRequiredFunctionBypassing));

    /// <summary>Reads the protected members of a chat agent's options.</summary>
    /// <param name="options">The options an extension is about to change.</param>
    /// <returns>The reading to compare the options with afterwards.</returns>
    internal static OptionsCheckpoint Take(ChatClientAgentOptions options)
    {
        var before = ChatAgent.Capture(options);
        return new OptionsCheckpoint(() => ChatAgent.Changes(before, options));
    }

    /// <summary>Reads the protected members of a harness agent's options.</summary>
    /// <param name="options">The options an extension is about to change.</param>
    /// <returns>The reading to compare the options with afterwards.</returns>
    internal static OptionsCheckpoint Take(HarnessAgentOptions options)
    {
        var before = Harness.Capture(options);
        return new OptionsCheckpoint(() => Harness.Changes(before, options));
    }

    /// <summary>Wraps a reference so it is compared by identity.</summary>
    private static ReferenceToken? Reference(object? value) => value is null ? null : new ReferenceToken(value);

    /// <summary>Wraps a sequence so it is compared element by element, by identity.</summary>
    private static SequenceToken? Sequence<T>(IEnumerable<T>? items)
        where T : class
        => items is null ? null : new SequenceToken([.. items.Select(static item => (object)item)]);

    /// <summary>A member's reading taken before an extension runs, and the check against the options after it.</summary>
    internal sealed class OptionsCheckpoint(Func<IReadOnlyList<string>> changes)
    {
        /// <summary>Gets the members that differ from the reading, as <c>Type.Member</c> paths.</summary>
        /// <returns>The paths of the changed protected members; empty when none changed.</returns>
        public IReadOnlyList<string> Changes() => changes();
    }

    /// <summary>The rows of one options type.</summary>
    /// <typeparam name="T">The options type.</typeparam>
    internal sealed class OptionRules<T>
        where T : class
    {
        private readonly List<Row> _rows = [];

        /// <summary>Gets every member that has a row.</summary>
        public IEnumerable<string> Names => _rows.Select(static row => row.Name);

        /// <summary>Gets the rule of a member, or <see langword="null"/> when the member has no row.</summary>
        /// <param name="name">The member name.</param>
        /// <returns>The rule, or <see langword="null"/>.</returns>
        public Rule? RuleOf(string name) => _rows.FirstOrDefault(row => string.Equals(row.Name, name, StringComparison.Ordinal))?.Rule;

        /// <summary>Gets the member names the check reads (protected and append-only), including nested ones.</summary>
        public IEnumerable<string> CheckedPaths => _rows.Where(static row => row.Read is not null).Select(static row => row.Name);

        /// <summary>Marks members the extension may change.</summary>
        /// <param name="names">The member names.</param>
        /// <returns>This table.</returns>
        public OptionRules<T> Free(params string[] names)
        {
            foreach (var name in names)
            {
                _rows.Add(new Row(name, Rule.Free, null, null));
            }

            return this;
        }

        /// <summary>Marks a member the extension must leave unchanged.</summary>
        /// <param name="name">The member name.</param>
        /// <param name="read">Reads the comparable form of the member.</param>
        /// <returns>This table.</returns>
        public OptionRules<T> Protect(string name, Func<T, object?> read)
        {
            _rows.Add(new Row(name, Rule.Protected, read, null));
            return this;
        }

        /// <summary>Marks a provider list the extension may only add to.</summary>
        /// <param name="name">The member name.</param>
        /// <param name="read">Reads the providers.</param>
        /// <returns>This table.</returns>
        public OptionRules<T> AppendOnly(string name, Func<T, IEnumerable<AIContextProvider>?> read)
        {
            _rows.Add(new Row(name, Rule.AppendOnly, options => Sequence(read(options)), null));
            return this;
        }

        /// <summary>
        /// Marks an options object the extension must leave unchanged, member by member:
        /// the reference itself and every protected member of <paramref name="nested"/>.
        /// </summary>
        /// <typeparam name="TNested">The nested options type.</typeparam>
        /// <param name="name">The member name.</param>
        /// <param name="get">Reads the nested object.</param>
        /// <param name="nested">The rows of the nested type; each of them must be protected.</param>
        /// <returns>This table.</returns>
        public OptionRules<T> ProtectNested<TNested>(string name, Func<T, TNested?> get, OptionRules<TNested> nested)
            where TNested : class
        {
            _rows.Add(new Row(name, Rule.Protected, options => Reference(get(options)), null));

            foreach (var row in nested._rows)
            {
                if (row.Read is not { } read)
                {
                    continue;
                }

                var inner = read;
                _rows.Add(new Row($"{name}.{row.Name}", Rule.Protected, options => get(options) is { } value ? inner(value) : null, name));
            }

            return this;
        }

        /// <summary>Gets the protected top-level members that an options object sets, other than the allowed ones.</summary>
        /// <param name="options">The options.</param>
        /// <param name="allowed">The protected members that may be set.</param>
        /// <returns>The member names, in row order.</returns>
        /// <remarks>
        /// A run's own options start empty, so a reading that is not empty is a member the caller set.
        /// An empty sequence counts as not set.
        /// </remarks>
        public IReadOnlyList<string> SetProtectedMembers(T options, params string[] allowed)
            => [.. _rows
                .Where(row => row is { Rule: Rule.Protected, Parent: null, Read: not null } && !allowed.Contains(row.Name, StringComparer.Ordinal))
                .Where(row => row.Read!(options) is { } reading && reading is not SequenceToken { Items.Length: 0 })
                .Select(static row => row.Name)];

        /// <summary>Reads every checked member.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The readings, in row order.</returns>
        public object?[] Capture(T options)
            => [.. _rows.Where(static row => row.Read is not null).Select(row => row.Read!(options))];

        /// <summary>Compares the options with an earlier reading.</summary>
        /// <param name="before">The reading from <see cref="Capture"/>.</param>
        /// <param name="options">The options after the extension ran.</param>
        /// <returns>The <c>Type.Member</c> path of every member that changed.</returns>
        public IReadOnlyList<string> Changes(object?[] before, T options)
        {
            var after = Capture(options);
            var changed = new List<string>();
            var changedParents = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;

            foreach (var row in _rows)
            {
                if (row.Read is null)
                {
                    continue;
                }

                var same = row.Rule == Rule.AppendOnly
                    ? IsPrefix(before[index], after[index])
                    : Same(before[index], after[index]);
                index++;

                if (same)
                {
                    continue;
                }

                // A replaced nested object already reports its parent; its members add no news.
                if (row.Parent is not null && changedParents.Contains(row.Parent))
                {
                    continue;
                }

                if (row.Parent is null)
                {
                    changedParents.Add(row.Name);
                }

                changed.Add($"{typeof(T).Name}.{row.Name}");
            }

            return changed;
        }

        private sealed record Row(string Name, Rule Rule, Func<T, object?>? Read, string? Parent);
    }

    private static bool Same(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return (left, right) switch
        {
            (ReferenceToken a, ReferenceToken b) => ReferenceEquals(a.Value, b.Value),
            (SequenceToken a, SequenceToken b) => a.Items.Length == b.Items.Length && a.Items.Zip(b.Items).All(static pair => ReferenceEquals(pair.First, pair.Second)),
            (object?[] a, object?[] b) => a.Length == b.Length && a.Zip(b).All(static pair => Same(pair.First, pair.Second)),
            _ => left.Equals(right),
        };
    }

    private static bool IsPrefix(object? before, object? after)
    {
        if (before is not SequenceToken earlier)
        {
            return true;
        }

        return after is SequenceToken later
               && later.Items.Length >= earlier.Items.Length
               && earlier.Items.Zip(later.Items).All(static pair => ReferenceEquals(pair.First, pair.Second));
    }

    private sealed record ReferenceToken(object Value);

    private sealed record SequenceToken(object[] Items);
}
#pragma warning restore MAAI001
