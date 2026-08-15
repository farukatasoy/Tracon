using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>Chain extensions that turn on the built-in model-based judge - Phase 49.</summary>
/// <remarks>
/// This is an extension method, not a member of <see cref="IAgentPrismBuilder"/>:
/// adding a member to the interface is a breaking change after release, adding
/// an extension method is not.
/// </remarks>
public static class AgentPrismOnlineEvaluationBuilderExtensions
{
    /// <summary>
    /// Registers AgentPrism's built-in model-based <see cref="IRunJudge"/>
    /// implementation (<see cref="ModelRunJudge"/>).
    /// </summary>
    /// <param name="builder">Configuration chain.</param>
    /// <param name="configure">The judge's model, criteria, and instructions.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// 🚨 This call ALONE scores nothing. The actual gate for online evaluation
    /// is <c>AgentPrism:OnlineEvaluation:Enabled</c> AND
    /// <c>AgentPrism:OnlineEvaluation:SampleRate</c> (K1: no run is sampled
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
    /// builder.AddAgentPrism()
    ///        .AddModelRunJudge(options =>
    ///        {
    ///            options.Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" };
    ///            options.Criteria.Add("Does the response directly answer the question?");
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddModelRunJudge(
        this IAgentPrismBuilder builder,
        Action<ModelRunJudgeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRunJudge, ModelRunJudge>());
        builder.Services.Configure(configure);

        return builder;
    }
}
