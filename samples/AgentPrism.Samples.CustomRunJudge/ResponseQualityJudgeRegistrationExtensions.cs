namespace AgentPrism.Samples.CustomRunJudge;

/// <summary>Registers the sample response-quality judge.</summary>
public static class ResponseQualityJudgeRegistrationExtensions
{
    /// <summary>Registers one singleton <see cref="ResponseQualityJudge"/>.</summary>
    /// <param name="builder">The AgentPrism builder.</param>
    /// <returns>The builder.</returns>
    public static IAgentPrismBuilder AddResponseQualityJudge(this IAgentPrismBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddRunJudge<ResponseQualityJudge>();
    }
}
