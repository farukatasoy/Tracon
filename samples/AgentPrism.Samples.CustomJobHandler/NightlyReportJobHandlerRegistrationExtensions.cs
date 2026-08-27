namespace AgentPrism.Samples.CustomJobHandler;

/// <summary>Registers the sample nightly-report job handler.</summary>
public static class NightlyReportJobHandlerRegistrationExtensions
{
    /// <summary>Registers one singleton <see cref="NightlyReportJobHandler"/>.</summary>
    /// <param name="builder">The AgentPrism builder.</param>
    /// <returns>The builder.</returns>
    public static IAgentPrismBuilder AddNightlyReportJobHandler(this IAgentPrismBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddJobHandler<NightlyReportJobHandler>();
        return builder;
    }
}
