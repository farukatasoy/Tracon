namespace Tracon;

/// <summary>
/// Marks an endpoint whose request body is a form. The pipeline refuses a request that carries no form before the
/// endpoint's request delegate reads it.
/// </summary>
/// <remarks>
/// The request delegate the generator writes reads a form only when the request can carry a body. A request that cannot
/// (no body, or a length of zero) goes on to ask the request for its form, which throws
/// <see cref="InvalidOperationException"/> because there is no content type, and the client sees 500 for a mistake of its
/// own. <see cref="JsonBindingProblemMiddleware"/> answers it first, as it answers a body of the wrong type.
/// </remarks>
internal sealed class FormBodyMetadata
{
    private FormBodyMetadata()
    {
    }

    /// <summary>Gets the one instance; the marker carries nothing.</summary>
    public static FormBodyMetadata Instance { get; } = new();
}
