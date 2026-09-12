namespace Tracon.Samples.CustomJobHandler;

/// <summary>Registers the sample nightly-report job handler.</summary>
public static class NightlyReportJobHandlerRegistrationExtensions
{
    /// <summary>The key this sample's jobs are dispatched by.</summary>
    /// <remarks>
    /// A consumer picks its own namespace prefix. The <c>tracon.</c>
    /// prefix is reserved for the handlers Tracon itself ships, and using
    /// it stops the host from starting.
    /// </remarks>
    public const string HandlerKey = "samples.nightly-report";

    /// <summary>Registers the <see cref="NightlyReportJobHandler"/> under <see cref="HandlerKey"/>.</summary>
    /// <param name="builder">The Tracon builder.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// This call may come before or after <c>AddTracon()</c>: dispatch is
    /// an exact key match, so registration order does not change which handler
    /// runs.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder AddNightlyReportJobHandler(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddJobHandler<NightlyReportJobHandler>(HandlerKey);
        return builder;
    }
}
