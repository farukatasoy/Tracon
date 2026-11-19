namespace Tracon;

/// <summary>
/// The chat client factory a provider was set up with, and, when the host named the configuration keys of the key and the
/// address, the clients that follow them.
/// </summary>
/// <remarks>
/// Everything that sends the setup key of one OpenAI options instance asks this object for the factory at the time of the
/// call: both API surfaces, the image and the speech clients, the health check. When no configuration key is named
/// <see cref="Live"/> is <see langword="null"/> and <see cref="Initial"/> is the factory for the life of the host.
/// </remarks>
internal sealed class OpenAISetup(OpenAIChatClientFactory initial, LiveSetup<OpenAIChatClientFactory>? live)
{
    /// <summary>Gets the factory the host started with.</summary>
    internal OpenAIChatClientFactory Initial { get; } = initial;

    /// <summary>Gets the live setup, or <see langword="null"/> when the key and the address are fixed for the life of the host.</summary>
    internal LiveSetup<OpenAIChatClientFactory>? Live { get; } = live;

    /// <summary>Returns the factory of the key and the address that are current now.</summary>
    /// <returns>The factory.</returns>
    internal OpenAIChatClientFactory Current() => Live is null ? Initial : Live.Current();
}
