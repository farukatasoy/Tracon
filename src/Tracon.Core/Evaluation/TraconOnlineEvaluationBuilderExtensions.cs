using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Chain extensions that turn on the built-in model-based judge.</summary>
/// <remarks>
/// This is an extension method, not a member of <see cref="ITraconBuilder"/>:
/// adding a member to the interface is a breaking change after release, adding
/// an extension method is not.
/// </remarks>
public static class TraconOnlineEvaluationBuilderExtensions
{
    /// <summary>
    /// Registers Tracon's built-in model-based <see cref="IRunJudge"/>
    /// implementation (<see cref="ModelRunJudge"/>).
    /// </summary>
    /// <param name="builder">Configuration chain.</param>
    /// <param name="configure">The judge's model, criteria, and instructions.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>
    /// or
    /// <paramref name="configure"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This call ALONE scores nothing. The actual gate for online evaluation
    /// is <c>Tracon:OnlineEvaluation:Enabled</c> AND
    /// <c>Tracon:OnlineEvaluation:SampleRate</c> (the no-surprises rule: no run is sampled
    /// unless both are on). This extension only registers which model is USED
    /// as the judge.
    /// </para>
    /// <para>
    /// Because <see cref="ModelBinding"/> is a nested type, it is set up in
    /// code rather than from a configuration section - the same pattern as
    /// other provider bindings (e.g. <c>AgentDefinition.Model</c>).
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddModelRunJudge(options =>
    ///        {
    ///            options.Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" };
    ///            options.Criteria.Add("Does the response directly answer the question?");
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddModelRunJudge(
        this ITraconBuilder builder,
        Action<ModelRunJudgeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.AddRunJudge<ModelRunJudge>();
        builder.Services.Configure(configure);

        return builder;
    }

    /// <summary>
    /// Registers a Microsoft.Extensions.AI <see cref="IEvaluator"/> as a
    /// Tracon run judge.
    /// </summary>
    /// <param name="builder">Configuration chain.</param>
    /// <param name="name">The judge name, matching <c>[A-Za-z0-9._-]{1,64}</c>.</param>
    /// <param name="evaluator">The evaluator to grade sampled runs with.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="evaluator"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a legal judge name.</exception>
    /// <remarks>
    /// <para>
    /// The calibrated evaluator catalog lives in the
    /// <c>Microsoft.Extensions.AI.Evaluation.Quality</c> package, which
    /// Tracon deliberately does not reference: add it yourself when you
    /// want the catalog, and a consumer who does not use it carries no extra
    /// dependency.
    /// </para>
    /// <para>
    /// Every metric the evaluator reports becomes its own score row, named
    /// <c>{name}.{metric}</c>. The prefix is what keeps two evaluators that
    /// report the same metric from overwriting each other, because a score name
    /// is part of the stored uniqueness key.
    /// </para>
    /// <para>
    /// The evaluator calls a model on every sampled run, so evaluation cost
    /// grows with the number of registered evaluators. The model is the judge
    /// model, set here or by <see cref="AddModelRunJudge"/>; registering an
    /// evaluator judge without one fails the judge at call time.
    /// </para>
    /// <para>
    /// Like <see cref="AddModelRunJudge"/>, this call alone scores nothing:
    /// <c>Tracon:OnlineEvaluation:Enabled</c> and its sample rate are still
    /// the gate.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddEvaluatorJudge(
    ///            "relevance",
    ///            new RelevanceEvaluator(),
    ///            options =&gt; options.Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddEvaluatorJudge(
        this ITraconBuilder builder,
        string name,
        IEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(evaluator);

        // 🚨 Checked HERE and not only by the startup gate: registration is
        // where the caller can see which of several calls was wrong.
        if (!RunScoreRules.IsValidName(name))
        {
            throw new ArgumentException(RunScoreRules.NameDescription, nameof(name));
        }

        builder.AddRunJudge(provider => new EvaluatorRunJudge(
            name,
            evaluator,
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IOptionsMonitor<ModelRunJudgeOptions>>(),
            provider.GetService<ILogger<EvaluatorRunJudge>>()));

        return builder;
    }

    /// <summary>
    /// Registers a Microsoft.Extensions.AI <see cref="IEvaluator"/> as a
    /// Tracon run judge and configures the shared judge model.
    /// </summary>
    /// <param name="builder">Configuration chain.</param>
    /// <param name="name">The judge name, matching <c>[A-Za-z0-9._-]{1,64}</c>.</param>
    /// <param name="evaluator">The evaluator to grade sampled runs with.</param>
    /// <param name="configure">The judge model the evaluator calls.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>, <paramref name="evaluator"/> or
    /// <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a legal judge name.</exception>
    /// <remarks>
    /// The judge model is <strong>shared</strong> with every other judge:
    /// this overload writes the same <see cref="ModelRunJudgeOptions"/> that
    /// <see cref="AddModelRunJudge"/> writes. Use it to run evaluator judges
    /// without also turning on the built-in model judge. Calling it more than
    /// once applies each configuration in order, as options configuration
    /// always does.
    /// </remarks>
    public static ITraconBuilder AddEvaluatorJudge(
        this ITraconBuilder builder,
        string name,
        IEvaluator evaluator,
        Action<ModelRunJudgeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.AddEvaluatorJudge(name, evaluator);
        builder.Services.Configure(configure);

        return builder;
    }

    /// <summary>
    /// Registers the factory that builds the <see cref="IAgentEvaluator"/> an
    /// eval suite is graded with.
    /// </summary>
    /// <typeparam name="TFactory">The factory implementation type.</typeparam>
    /// <param name="builder">Configuration chain.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The consumer's registration wins: the built-in factory is registered
    /// with <c>TryAdd</c>, so this one replaces it. Registering none leaves
    /// MAF's <see cref="LocalEvaluator"/>, built over the suite's own checks.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddEvalEvaluatorFactory&lt;LoggingEvaluatorFactory&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddEvalEvaluatorFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>(
        this ITraconBuilder builder)
        where TFactory : class, IEvalEvaluatorFactory
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IEvalEvaluatorFactory, TFactory>();

        return builder;
    }

    /// <summary>Registers a configured eval-evaluator factory instance.</summary>
    /// <param name="builder">Configuration chain.</param>
    /// <param name="factory">The factory instance. The caller owns it.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The consumer's registration wins over the built-in one.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddEvalEvaluatorFactory(new LoggingEvaluatorFactory());
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddEvalEvaluatorFactory(
        this ITraconBuilder builder,
        IEvalEvaluatorFactory factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(factory);

        return builder;
    }
}
