using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenAI;

namespace Tracon;

// 🚨 Why the protocol level (measured 2026-10-19, versions below).
//
// OpenAI 2.14.0 was built against System.ClientModel 1.15.0. System.ClientModel 1.16.0 renamed
// AsyncStreamingClientResult<T> to AsyncStreamingResult<T>, and a host that also uses Tracon.Azure
// resolves 1.16.0 (Azure.Core 1.63.0 and 1.64.0 require it). The member signatures of AudioClient and
// ImageClient still name the old type, so Native AOT cannot compile a host that merely constructs
// one: "Failed to load type 'System.ClientModel.AsyncStreamingClientResult`1'". A ten-line program
// reproduced it (1.16.0 fails; the same program with 1.15.0 publishes clean), and a JIT host only
// notices when it runs one of the streaming members. Re-measure on the next OpenAI or
// System.ClientModel upgrade: if the repro publishes clean, AudioClient is usable again.
//
/// <summary>
/// Posts to the OpenAI audio endpoints through the shared client's own pipeline, without
/// <c>AudioClient</c>.
/// </summary>
/// <remarks>
/// <para>
/// A host that constructs an <c>AudioClient</c> cannot be published with Native AOT while the
/// OpenAI client library and the <c>System.ClientModel</c> a host resolves disagree on a type name
/// (the package's Native AOT promise is proved by a publish, not by a flag). The pipeline is the same one
/// every sub-client uses, so authentication, the organisation header, retries, the network timeout
/// and the transport the client was built with all apply. Only the request and the response are
/// written here, and both are small: a JSON body and a multipart form.
/// </para>
/// </remarks>
internal sealed class OpenAIAudioTransport
{
    /// <summary>The most bytes one streamed chunk holds.</summary>
    private const int StreamChunkSize = 16 * 1024;

    private readonly ClientPipeline _pipeline;
    private readonly Uri _endpoint;

    /// <summary>Creates a transport over a client's pipeline.</summary>
    /// <param name="client">The shared OpenAI client.</param>
    /// <param name="endpoint">
    /// The API base the client was built with. <c>OpenAIClient.Endpoint</c> would give it, but the
    /// package marks that member evaluation-only, so the factory that built the client passes it.
    /// </param>
    public OpenAIAudioTransport(OpenAIClient client, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(endpoint);

        _pipeline = client.Pipeline;

        // A relative path resolves against a base that ends in a slash, or it replaces the last segment.
        _endpoint = endpoint.AbsoluteUri.EndsWith('/') ? endpoint : new Uri(endpoint.AbsoluteUri + "/");
    }

    /// <summary>Posts a body and returns the response content.</summary>
    /// <param name="path">The path under the API base, such as <c>audio/speech</c>.</param>
    /// <param name="content">The body.</param>
    /// <param name="contentType">The <c>Content-Type</c> of the body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The buffered response content.</returns>
    /// <exception cref="ClientResultException">The service answered with an error status.</exception>
    public async Task<BinaryData> PostAsync(
        string path,
        BinaryContent content,
        string contentType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var message = _pipeline.CreateMessage();

        message.Apply(new RequestOptions { CancellationToken = cancellationToken });

        var request = message.Request;
        request.Method = "POST";
        request.Uri = new Uri(_endpoint, path);
        request.Headers.Set("Content-Type", contentType);
        request.Content = content;

        try
        {
            await _pipeline.SendAsync(message).ConfigureAwait(false);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
        {
            // Measured: a call cancelled while the transport reads the request surfaces as an
            // ObjectDisposedException from the transport's own continuation. The caller asked for the
            // stop; the cause it needs is the cancellation.
            throw new OperationCanceledException(cancellationToken);
        }

        var response = message.Response
            ?? throw new TraconException("The OpenAI client returned no response.");

        return response.IsError
            ? throw Failure(response, await ClientResultException.CreateAsync(response).ConfigureAwait(false))
            : response.Content;
    }

    /// <summary>Posts a body and yields the response content as it arrives.</summary>
    /// <param name="path">The path under the API base, such as <c>audio/speech</c>.</param>
    /// <param name="content">The body.</param>
    /// <param name="contentType">The <c>Content-Type</c> of the body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chunks, in order, as the service sends them.</returns>
    /// <exception cref="TraconException">The service answered with an error status.</exception>
    /// <remarks>
    /// The speech endpoint sends the audio with chunked transfer encoding by default, so playback can
    /// begin before the file is complete. This is the raw stream, not the server-sent events the
    /// <c>stream_format</c> field offers: every speech model serves it, <c>tts-1</c> included.
    /// An error answer is read whole first, so its machine code can be named.
    /// </remarks>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> PostStreamingAsync(
        string path,
        BinaryContent content,
        string contentType,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var message = _pipeline.CreateMessage();

        // The option, not the message property: Apply copies the option's BufferResponse (true by
        // default) onto the message, so a flag set on the message first is overwritten (measured).
        message.Apply(new RequestOptions { CancellationToken = cancellationToken, BufferResponse = false });

        var request = message.Request;
        request.Method = "POST";
        request.Uri = new Uri(_endpoint, path);
        request.Headers.Set("Content-Type", contentType);
        request.Content = content;

        try
        {
            await _pipeline.SendAsync(message).ConfigureAwait(false);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var response = message.Response
            ?? throw new TraconException("The OpenAI client returned no response.");

        if (response.IsError)
        {
            await response.BufferContentAsync(cancellationToken).ConfigureAwait(false);

            throw Failure(response, await ClientResultException.CreateAsync(response).ConfigureAwait(false));
        }

        var body = response.ContentStream
            ?? throw new TraconException("The OpenAI speech endpoint returned no audio stream.");

        var buffer = new byte[StreamChunkSize];

        while (true)
        {
            int read;

            try
            {
                read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (read == 0)
            {
                yield break;
            }

            // A copy: the buffer is reused for the next read, and the caller keeps the chunk.
            yield return buffer.AsMemory(0, read).ToArray();
        }
    }

    /// <summary>Describes a failed call without the service's text.</summary>
    /// <param name="response">The error response.</param>
    /// <param name="inner">The client's own exception, kept for the log.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// The status and the machine code (<c>model_not_found</c>, <c>insufficient_quota</c>) are enough to act on
    /// and safe to print. The service's message is not: it can quote the key it refused. It stays in the inner
    /// exception, which an endpoint never prints.
    /// </remarks>
    private static TraconException Failure(PipelineResponse response, ClientResultException inner)
    {
        var code = ErrorCode(response);

        return new TraconException(
            $"OpenAI speech request failed with HTTP {response.Status}{(code is null ? string.Empty : $" ({code})")}.",
            inner);
    }

    private static string? ErrorCode(PipelineResponse response)
    {
        try
        {
            using var document = JsonDocument.Parse(response.Content);

            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("code", out var code)
                   && code.ValueKind == JsonValueKind.String
                   && code.GetString() is { Length: > 0 and <= 64 } text
                   && text.All(static c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-' or '.')
                ? text
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // The body was not buffered: there is no code to read.
            return null;
        }
    }
}
