namespace Tracon;

/// <summary>
/// Constants for the configuration path of the providers that
/// <c>UseOpenAICompatible()</c> registers.
/// </summary>
/// <remarks>
/// The options shape is the same as <see cref="OpenAIProviderOptions"/>; this type only
/// carries the sub section path. Every named provider lives in its own sub section:
/// <c>Tracon:Providers:OpenAICompatible:{name}:*</c>.
/// Provider options live in their own sub section.
/// </remarks>
public static class OpenAICompatibleProviderOptions
{
    /// <summary>Gets the base configuration section path of the named providers.</summary>
    public const string SectionName = "Tracon:Providers:OpenAICompatible";
}
