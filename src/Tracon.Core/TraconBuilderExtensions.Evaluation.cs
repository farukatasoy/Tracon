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
    /// Registers a code-defined loop stop criterion. A definition's
    /// <see cref="LoopSettings.Criteria"/> can then reference it by this
    /// <paramref name="kind"/> name.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="kind">
    /// The criterion name. It must not be one of the built-in kinds
    /// (<c>completionMarker</c>, <c>todoCompletion</c>, <c>aiJudge</c>,
    /// <c>backgroundTaskCompletion</c>); shadowing one is rejected, so the same
    /// definition cannot mean different things in two applications.
    /// </param>
    /// <param name="evaluator">
    /// The criterion. <c>Microsoft.Agents.AI.DelegateLoopEvaluator</c> wraps a
    /// plain function; a subclass of <c>LoopEvaluator</c> covers anything larger.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Same rationale as tools and eval checks: a stop criterion whose logic is
    /// code is only ever defined in code. A definition arriving from the
    /// management API names the criterion, it never writes it.
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
    // suppression is narrowed to this one member instead of the file. Rationale:
    // docs/KARARLAR.md, decision K-020.
#pragma warning disable MAAI001
    public static ITraconBuilder AddLoopEvaluator(this ITraconBuilder builder, string kind, Microsoft.Agents.AI.LoopEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(evaluator);

        builder.Services.AddSingleton(new TraconLoopEvaluatorRegistration(kind, evaluator));
        return builder;
    }
#pragma warning restore MAAI001
}
