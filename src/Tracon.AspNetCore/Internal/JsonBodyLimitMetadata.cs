namespace Tracon;

/// <summary>
/// The longest JSON request body the endpoints under the group accept, carried on the group so the
/// request path reads it from the endpoint and never from the options object.
/// </summary>
/// <param name="MaxBytes">The limit in bytes.</param>
/// <param name="OpenAIPathPrefix">
/// The path prefix of the OpenAI-compatible routes (the mapped prefix and <c>/v1/</c>). Their refusal is the error
/// envelope an OpenAI client reads; every other route answers a problem document. Taken from the mapped prefix and not
/// from a substring of the route, because a consumer's own prefix may contain <c>/v1/</c>.
/// </param>
internal sealed record JsonBodyLimitMetadata(long MaxBytes, string OpenAIPathPrefix);
