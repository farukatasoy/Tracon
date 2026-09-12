namespace Tracon.Samples.CustomTool;

/// <summary>Registers the compile-time generated order preview tool.</summary>
public static class OrderPreviewToolRegistration
{
    /// <summary>Adds every generated tool from this sample assembly.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    public static ITraconBuilder AddOrderPreviewTools(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddGeneratedTools();
    }
}
