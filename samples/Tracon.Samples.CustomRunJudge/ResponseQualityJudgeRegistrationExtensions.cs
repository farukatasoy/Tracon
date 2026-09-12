namespace Tracon.Samples.CustomRunJudge;

/// <summary>Registers the sample response-quality judge.</summary>
public static class ResponseQualityJudgeRegistrationExtensions
{
    /// <summary>Registers one singleton <see cref="ResponseQualityJudge"/>.</summary>
    /// <param name="builder">The Tracon builder.</param>
    /// <returns>The builder.</returns>
    public static ITraconBuilder AddResponseQualityJudge(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddRunJudge<ResponseQualityJudge>();
    }
}
