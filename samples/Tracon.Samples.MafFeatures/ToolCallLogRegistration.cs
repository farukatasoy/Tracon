using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.MafFeatures;

/// <summary>Registers <see cref="ToolCallLogDecorator"/> and the log it writes to.</summary>
public static class ToolCallLogRegistration
{
    /// <summary>Adds the decorator to the Tracon chain.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The same chain.</returns>
    public static ITraconBuilder AddToolCallLog(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ToolCallLog>();

        return builder.AddAgentDecorator<ToolCallLogDecorator>();
    }
}
