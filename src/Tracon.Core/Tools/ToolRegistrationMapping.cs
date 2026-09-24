using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Turns the settings a caller wrote into <see cref="ToolRegistrationOptions"/>
/// into a <see cref="TraconToolRegistration"/>.
/// </summary>
/// <remarks>
/// The eager and the scoped tool registration both go through here, so a
/// setting added to both types cannot be carried on one path and dropped on
/// the other. <c>ToolRegistrationParityTests</c> fails when a setting is
/// missing from this mapping.
/// </remarks>
internal static class ToolRegistrationMapping
{
    /// <summary>Creates a registration for <paramref name="function"/> with the settings in <paramref name="options"/>.</summary>
    /// <param name="function">The tool.</param>
    /// <param name="options">The settings the caller configured.</param>
    /// <returns>The registration.</returns>
    public static TraconToolRegistration FromOptions(AIFunctionDeclaration function, ToolRegistrationOptions options) =>
        new(function)
        {
            RequiresApproval = options.RequiresApproval,
            Source = options.Source,
            Effect = options.Effect,
            RequiredPermission = options.RequiredPermission,
            Timeout = options.Timeout,
            SafeToRepeat = options.SafeToRepeat,
            MaxOutputBytes = options.MaxOutputBytes,
        };
}
