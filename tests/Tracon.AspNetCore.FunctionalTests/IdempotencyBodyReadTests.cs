using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The <c>Idempotency-Key</c> filter reads a request body only when the endpoint will read it too: a JSON body, which the
/// JSON body limit already bounds.
/// </summary>
/// <remarks>
/// <para>
/// The filter fingerprints the body, so it copies the whole body into memory. The JSON limit answers 413 only for a JSON
/// content type, so a body that declared none was copied at any length the server accepts (30 MB on Kestrel, about five
/// times over: measured 152 MB of allocation for a 29 MB body) just to be ignored, or refused with 415, by the endpoint.
/// A body that declares another content type never reaches the filter: the routing answers 415 first.
/// </para>
/// <para>
/// A counting stream sits under every layer of the pipeline, so what the tests count is what the server pulled from the
/// request, whoever pulled it.
/// </para>
/// </remarks>
public sealed class IdempotencyBodyReadTests
{
    private const int BodyBytes = 256 * 1024;

    [Theory]
    [InlineData("/tracon/api/agents/kod-agent/run", null, false)]
    [InlineData("/tracon/api/agents/kod-agent/run", "text/plain", false)]
    [InlineData("/tracon/v1/chat/completions", null, false)]
    [InlineData("/tracon/v1/responses", null, false)]
    [InlineData("/tracon/api/workflows/probe/run", null, true)]
    [InlineData("/tracon/api/workflows/runs/6d7cd6e1-6d0d-4a39-8ae3-0f2b9d4f1f01/resume", null, true)]
    [InlineData("/tracon/api/workflows/runs/6d7cd6e1-6d0d-4a39-8ae3-0f2b9d4f1f01/respond", null, true)]
    public async Task A_keyed_request_without_a_JSON_content_type_has_none_of_its_body_read(string route, string? contentType, bool respondAsync)
    {
        var read = new BodyReadCounter();
        await using var host = await StartCountingAsync(read);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(route, UriKind.Relative))
        {
            Content = new ByteArrayContent(new byte[BodyBytes]),
        };

        request.Content.Headers.ContentType = contentType is null ? null : new MediaTypeHeaderValue(contentType);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        if (respondAsync)
        {
            request.Headers.Add("Prefer", "respond-async");
        }

        using var reply = await host.Client.SendAsync(request);

        read.Bytes.ShouldBe(0, "a body the endpoint refuses or ignores is not copied into memory to fingerprint it");

        if (!respondAsync)
        {
            reply.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        }
    }

    [Fact]
    public async Task A_keyed_JSON_request_is_read_once_and_in_full()
    {
        var read = new BodyReadCounter();
        await using var host = await StartCountingAsync(read);

        var json = $$"""{"message":"{{new string('a', BodyBytes - """{"message":""}""".Length)}}"}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var reply = await host.Client.SendAsync(request);

        reply.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Bytes.ShouldBe(json.Length, "the filter fingerprints the body and the endpoint reads it from the buffer, not from the wire again");
    }

    public static TheoryData<string, bool> StreamingFlagBodies => new()
    {
        { "{\"message\":\"hi\",\"stream\":true}", true },
        { "{\"stream\":true,\"message\":\"hi\"}", true },
        { "{\"message\":\"hi\",\"stream\":false}", false },
        { "{\"stream\":true,\"stream\":false,\"message\":\"hi\"}", false },
        { "{\"stream\":false,\"stream\":true,\"message\":\"hi\"}", true },
        { "{\"str\\u0065am\":true,\"message\":\"hi\"}", true },
        { "{\"nested\":{\"stream\":true,\"deeper\":[1,{\"stream\":true}]},\"message\":\"hi\"}", false },
        { "{\"stream\":\"true\",\"message\":\"hi\"}", false },
        { "[{\"stream\":true}]", false },
        { "{\"stream\":true,\"message\":", false },
        { "{\"stream\":true,\"message\":\"hi\"} trailing", false },
    };

    [Theory]
    [MemberData(nameof(StreamingFlagBodies))]
    public async Task The_streaming_flag_is_read_from_the_top_level_of_a_JSON_object_only(string json, bool refusedAsStreaming)
    {
        // The flag is read from the buffered body without parsing it into a document; these are the readings a document
        // gave: the last of two equal names wins, an escaped name counts, a nested property does not, and a body that is
        // not valid JSON is the endpoint's to refuse, not the filter's.
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var reply = await host.Client.SendAsync(request);
        var title = (await TraconTestHost.ReadJsonAsync(reply)).TryGetProperty("title", out var value) ? value.GetString() : null;

        if (refusedAsStreaming)
        {
            reply.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            title.ShouldBe("Idempotency-Key not supported on streaming requests");
        }
        else
        {
            title.ShouldNotBe("Idempotency-Key not supported on streaming requests", StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task A_keyed_request_with_no_body_at_all_is_still_fingerprinted_by_its_route()
    {
        // Workflow run endpoints take an optional body; the key must keep working when there is none.
        await using var host = await TraconTestHost.StartAsync();

        foreach (var key in new[] { "k1", "k2" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/workflows/probe/run", UriKind.Relative));
            request.Headers.Add("Idempotency-Key", key);
            request.Headers.Add("Prefer", "respond-async");

            using var reply = await host.Client.SendAsync(request);

            // No workflow engine is registered in this host; the filter has already let the request through to say so.
            reply.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        }
    }

    private static Task<TraconTestHost> StartCountingAsync(BodyReadCounter read)
        => TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureApp: app => app.Use((context, next) =>
            {
                context.Request.Body = new CountingStream(context.Request.Body, read);

                return next(context);
            }));

    private sealed class BodyReadCounter
    {
        public long Bytes { get; set; }
    }

    private sealed class CountingStream(Stream inner, BodyReadCounter counter) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            counter.Bytes += read;

            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            counter.Bytes += read;

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
