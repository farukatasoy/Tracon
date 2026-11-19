using System.ClientModel.Primitives;
using OpenAI;

namespace Tracon;

/// <summary>
/// The pipeline of an OpenAI client and the address it was built for, taken together.
/// </summary>
/// <remarks>
/// A holder that talks to the service over the protocol level (the image generator, the audio transport) used to keep both
/// from the day it was built. It now asks for the pair when a call starts, so a key or an address that changed in the
/// meantime is the one the call uses, and the two always belong to one client.
/// </remarks>
/// <param name="Pipeline">The client's pipeline.</param>
/// <param name="Endpoint">The API base, with a trailing slash.</param>
internal readonly record struct OpenAIPipelineTarget(ClientPipeline Pipeline, Uri Endpoint)
{
    /// <summary>Creates the pair of a client and its address.</summary>
    /// <param name="client">The OpenAI client.</param>
    /// <param name="endpoint">The API base the client was built with. <c>OpenAIClient.Endpoint</c> would give it, but the package marks that member evaluation-only, so the factory that built the client passes it.</param>
    /// <returns>The pair.</returns>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    internal static OpenAIPipelineTarget Of(OpenAIClient client, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(endpoint);

        // A relative path resolves against a base that ends in a slash, or it replaces the last segment.
        return new(client.Pipeline, endpoint.AbsoluteUri.EndsWith('/') ? endpoint : new Uri(endpoint.AbsoluteUri + "/"));
    }
}
