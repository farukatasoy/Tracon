using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

public static partial class TraconBuilderExtensions
{
    /// <summary>Registers a custom run judge as a singleton.</summary>
    /// <typeparam name="TJudge">The judge implementation type.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Repeating this overload for the same implementation type has no effect.
    /// The container creates and owns the singleton. The judge must be
    /// thread-safe because evaluations can overlap, including when a timed-out
    /// call finishes after a retry starts.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddRunJudge&lt;ResponseQualityJudge&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddRunJudge<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJudge>(this ITraconBuilder builder)
        where TJudge : class, IRunJudge
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRunJudge, TJudge>());
        return builder;
    }

    /// <summary>Registers a configured run-judge instance as a singleton.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="judge">The judge instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The caller owns the instance and any resources it holds. Different
    /// configured instances of the same CLR type are preserved. Their
    /// <see cref="IRunJudge.Name"/> values must still be unique.
    /// </remarks>
    public static ITraconBuilder AddRunJudge(this ITraconBuilder builder, IRunJudge judge)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(judge);

        builder.Services.AddSingleton(judge);
        return builder;
    }

    /// <summary>Registers a run-judge factory as a singleton.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="factory">The factory that creates the judge.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The container owns the produced singleton. The factory must not capture
    /// a scoped dependency because the result outlives that scope. Different
    /// factories and configured results are preserved.
    /// </remarks>
    public static ITraconBuilder AddRunJudge(this ITraconBuilder builder, Func<IServiceProvider, IRunJudge> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(factory);
        return builder;
    }

    /// <summary>
    /// Registers a custom eval check. Eval suites can reference it by
    /// this <paramref name="kind"/> name in their <c>checks</c> field.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="kind">The check type name. Must not collide with a built-in type (for example <c>nonEmpty</c>).</param>
    /// <param name="check">A code-written check that does not call the model.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// An <c>EvalCheck</c> produced with
    /// <c>Microsoft.Agents.AI.FunctionEvaluator.Create(...)</c> is expected.
    /// Same rationale as tools: custom logic is only defined in code, and a
    /// free-form expression cannot be written from the interface.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddEvalCheck("mentionsOrderId", FunctionEvaluator.Create(
    ///            "mentionsOrderId",
    ///            response => response.Contains("order", StringComparison.OrdinalIgnoreCase)));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddEvalCheck(this ITraconBuilder builder, string kind, Microsoft.Agents.AI.EvalCheck check)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(check);

        builder.Services.AddSingleton(new TraconEvalCheckRegistration(kind, check));
        return builder;
    }

    /// <summary>
    /// Registers a code-defined loop stop criterion with no description. A
    /// definition's <see cref="LoopSettings.Criteria"/> can then reference it by
    /// this <paramref name="kind"/> name.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="kind">
    /// The criterion name. It must not be one of the built-in kinds
    /// (<c>completionMarker</c>, <c>todoCompletion</c>, <c>aiJudge</c>,
    /// <c>backgroundTaskCompletion</c>, <c>agentReview</c>); shadowing one is rejected, so the same
    /// definition cannot mean different things in two applications.
    /// </param>
    /// <param name="evaluator">
    /// The criterion. One instance serves every agent and every run that names
    /// the kind, so it must be thread-safe and keep no state of one run in its fields.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The same registration as the overload that takes a description; the
    /// console then shows the kind name alone.
    /// </remarks>
#pragma warning disable MAAI001 // See the rationale on the overload that takes a description.
    public static ITraconBuilder AddLoopEvaluator(this ITraconBuilder builder, string kind, Microsoft.Agents.AI.LoopEvaluator evaluator)
        => AddLoopEvaluator(builder, kind, evaluator, description: null);
#pragma warning restore MAAI001

    /// <summary>
    /// Registers a code-defined loop stop criterion. A definition's
    /// <see cref="LoopSettings.Criteria"/> can then reference it by this
    /// <paramref name="kind"/> name.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="kind">
    /// The criterion name. It must not be one of the built-in kinds
    /// (<c>completionMarker</c>, <c>todoCompletion</c>, <c>aiJudge</c>,
    /// <c>backgroundTaskCompletion</c>, <c>agentReview</c>); shadowing one is rejected, so the same
    /// definition cannot mean different things in two applications.
    /// </param>
    /// <param name="evaluator">
    /// The criterion. <c>Microsoft.Agents.AI.DelegateLoopEvaluator</c> wraps a
    /// plain function; a subclass of <c>LoopEvaluator</c> covers anything larger.
    /// One instance serves every agent and every run that names the kind, so it
    /// must be thread-safe and keep no state of one run in its fields.
    /// </param>
    /// <param name="description">
    /// What the criterion checks, in one sentence. The console shows it next to
    /// the kind and <c>GET /api/loop-criteria</c> returns it to every reader, so
    /// it must not carry a secret. <see langword="null"/> shows the kind alone.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Same rationale as tools and eval checks: a stop criterion whose logic is
    /// code is only ever defined in code. A definition arriving from the
    /// management API names the criterion, it never writes it. A criterion that
    /// needs an application service takes the factory overload instead.
    /// <example>
    /// The loop types are marked for evaluation only by Microsoft Agent
    /// Framework, so naming one in your own code needs the suppression below:
    /// <code>
    /// #pragma warning disable MAAI001
    /// builder.AddTracon()
    ///        .AddLoopEvaluator("hasCitations", new DelegateLoopEvaluator((context, ct) =>
    ///            new ValueTask&lt;LoopEvaluation&gt;(
    ///                context.LastResponse?.Text?.Contains("[1]", StringComparison.Ordinal) == true
    ///                    ? LoopEvaluation.Stop()
    ///                    : LoopEvaluation.Continue("Add a numbered citation for every claim."))));
    /// #pragma warning restore MAAI001
    /// </code>
    /// </example>
    /// </remarks>
    // MAAI001: Microsoft.Agents.AI.LoopEvaluator is marked "evaluation purposes
    // only". A registration surface cannot hide the type it registers, so the
    // suppression is narrowed to the registration members instead of the file.
    // Rationale: docs/KARARLAR.md, decision K-020.
    //
    // Four overloads instead of an optional description on two: RS0026 refuses
    // more than one overload with optional parameters (the AddTool pattern).
#pragma warning disable MAAI001
    public static ITraconBuilder AddLoopEvaluator(
        this ITraconBuilder builder,
        string kind,
        Microsoft.Agents.AI.LoopEvaluator evaluator,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(evaluator);

        builder.Services.AddSingleton(new TraconLoopEvaluatorRegistration(kind, evaluator, description));
        return builder;
    }

    /// <summary>
    /// Registers a code-defined loop stop criterion that is created from the
    /// application's services. A definition's <see cref="LoopSettings.Criteria"/>
    /// can then reference it by this <paramref name="kind"/> name.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="kind">
    /// The criterion name. It must not be one of the built-in kinds
    /// (<c>completionMarker</c>, <c>todoCompletion</c>, <c>aiJudge</c>,
    /// <c>backgroundTaskCompletion</c>, <c>agentReview</c>); shadowing one, or registering the same
    /// name twice, is rejected before any factory runs.
    /// </param>
    /// <param name="factory">
    /// Creates the criterion from the application's root service provider. It
    /// runs once, on the first compilation of an agent whose loop names
    /// <paramref name="kind"/>, and its result serves every agent, every run and
    /// every tenant from then on.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The same registration as the overload that takes a description; the
    /// console then shows the kind name alone. That overload's remarks say when
    /// the factory runs and how a criterion reads the tenant.
    /// </remarks>
    public static ITraconBuilder AddLoopEvaluator(
        this ITraconBuilder builder,
        string kind,
        Func<IServiceProvider, Microsoft.Agents.AI.LoopEvaluator> factory)
        => AddLoopEvaluator(builder, kind, factory, description: null);

    /// <summary>
    /// Registers a code-defined loop stop criterion that is created from the
    /// application's services. A definition's <see cref="LoopSettings.Criteria"/>
    /// can then reference it by this <paramref name="kind"/> name.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="kind">
    /// The criterion name. It must not be one of the built-in kinds
    /// (<c>completionMarker</c>, <c>todoCompletion</c>, <c>aiJudge</c>,
    /// <c>backgroundTaskCompletion</c>, <c>agentReview</c>); shadowing one, or registering the same
    /// name twice, is rejected before any factory runs.
    /// </param>
    /// <param name="factory">
    /// Creates the criterion from the application's root service provider. It
    /// runs once, on the first compilation of an agent whose loop names
    /// <paramref name="kind"/>, and its result serves every agent, every run and
    /// every tenant from then on.
    /// </param>
    /// <param name="description">
    /// What the criterion checks, in one sentence. The console shows it next to
    /// the kind and <c>GET /api/loop-criteria</c> returns it to every reader, so
    /// it must not carry a secret. <see langword="null"/> shows the kind alone.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A criterion cannot take a service from <c>LoopContext</c>, which carries
    /// none; the factory is where it takes them. The factory runs at the first
    /// compilation rather than at setup, so a criterion may take the agent
    /// catalog (<see cref="IAgentCatalog"/>): at setup the catalog is still being
    /// built. When the factory throws, that compilation fails with a
    /// <see cref="TraconCompilationException"/> naming the kind, and the next
    /// compilation calls the factory again; a successful result is kept.
    /// </para>
    /// <para>
    /// The provider is the root one. Take singletons from it; a scoped service
    /// would outlive its scope. The tenant is not known when the factory runs:
    /// read <see cref="ITenantContext.TenantId"/> inside
    /// <c>EvaluateAsync</c>, never in the factory or the constructor, or every
    /// tenant would see the first tenant's value.
    /// </para>
    /// <para>
    /// <strong>A reviewer agent as the criterion.</strong> The criterion below
    /// computes a check in code, hands it to a reviewer agent from the catalog
    /// with the draft, and reads the reviewer's own JSON verdict. The reviewer
    /// runs as a child of the run being evaluated: it appears under that run in
    /// the run tree, spends from the same tree budget, and its tokens are its
    /// own run's usage. Per-run state, such as the round number, lives in
    /// <c>LoopContext.AdditionalProperties</c>, which belongs to one loop run.
    /// The draft is the last message of the turn: a harness turn can write a
    /// plan or an earlier version first, and <c>LastResponse.Text</c> joins
    /// every message of the turn.
    /// </para>
    /// <example>
    /// <code>
    /// #pragma warning disable MAAI001
    /// builder.AddTracon()
    ///        .AddLoopEvaluator(
    ///            "reviewed",
    ///            services =&gt;
    ///            {
    ///                var catalog = services.GetRequiredService&lt;IAgentCatalog&gt;();
    ///
    ///                return new DelegateLoopEvaluator(async (context, cancellationToken) =&gt;
    ///                {
    ///                    var messages = context.LastResponse?.Messages;
    ///                    var draft = messages is { Count: &gt; 0 } ? messages[messages.Count - 1].Text : string.Empty;
    ///                    var words = draft.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    ///                    var round = context.AdditionalProperties.TryGetValue("reviewed.round", out var r) ? (int)r! + 1 : 1;
    ///                    context.AdditionalProperties["reviewed.round"] = round;
    ///
    ///                    var scope = TraconRunContext.Current;
    ///                    if (scope?.Budget is { } budget &amp;&amp; (scope.Depth + 1 &gt; budget.MaxDepth || !budget.TryReserveRun()))
    ///                    {
    ///                        throw new InvalidOperationException("The run tree has no room for the reviewer.");
    ///                    }
    ///
    ///                    var reviewer = await catalog.ResolveAsync("reviewer", culture: null, cancellationToken)
    ///                        ?? throw new InvalidOperationException("No agent named 'reviewer'.");
    ///                    var options = scope is null ? null : new TraconRunOptions
    ///                    {
    ///                        ParentRunId = scope.RunId,
    ///                        RootRunId = scope.RootRunId,
    ///                        Depth = scope.Depth + 1,
    ///                        Budget = scope.Budget,
    ///                        SessionId = scope.SessionId,
    ///                    };
    ///
    ///                    var review = await reviewer.RunAsync(
    ///                        $"Round {round}. Word count: {words}. Review this draft:\n{draft}",
    ///                        session: null, options, cancellationToken);
    ///
    ///                    // The reviewer's ResponseFormat is a JSON schema: {"pass": bool, "feedback": string}.
    ///                    using var verdict = JsonDocument.Parse(review.Text);
    ///                    return verdict.RootElement.GetProperty("pass").GetBoolean()
    ///                        ? LoopEvaluation.Stop()
    ///                        : LoopEvaluation.Continue(verdict.RootElement.GetProperty("feedback").GetString() ?? "Revise the draft.");
    ///                });
    ///            },
    ///            description: "The reviewer agent approves the draft.");
    /// #pragma warning restore MAAI001
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddLoopEvaluator(
        this ITraconBuilder builder,
        string kind,
        Func<IServiceProvider, Microsoft.Agents.AI.LoopEvaluator> factory,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(new TraconLoopEvaluatorRegistration(kind, factory, description));
        return builder;
    }
#pragma warning restore MAAI001
}
