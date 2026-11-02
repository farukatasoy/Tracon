using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A JSON body longer than <see cref="TraconEndpointOptions.MaxJsonBodyBytes"/> is refused with 413 by every operation that
/// takes one, however the body is sent.
/// </summary>
/// <remarks>
/// <para>
/// Nothing bounded a body: a 32 MB message was read, parsed, recorded and echoed, about thirty times its size in memory at
/// once. The limit sits in front of the endpoints, so one that binds its body through the framework and one that reads it by
/// hand are held to it alike. The operations come from the published document, so an endpoint added later is probed without
/// anyone listing it.
/// </para>
/// <para>
/// The body is sent twice: with a length, which is refused before it is read, and chunked, which has no length and is
/// refused when the bytes passing the limit are counted.
/// </para>
/// </remarks>
public sealed class JsonBodyLimitTests
{
    private const long Limit = 4096;

    private static readonly string[] BodyMethods = ["post", "put", "patch"];

    [Fact]
    public async Task Every_operation_that_takes_a_JSON_body_refuses_one_over_the_limit_with_or_without_a_length()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(
            configureMoreEndpoints: static options => options.MaxJsonBodyBytes = Limit);

        using var documentResponse = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        documentResponse.EnsureSuccessStatusCode();

        var paths = (await TraconTestHost.ReadJsonAsync(documentResponse)).GetProperty("paths");
        var notRefused = new List<string>();
        var operations = 0;
        var oversized = $$"""{"padding":"{{new string('a', (int)Limit)}}"}""";

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in BodyMethods)
            {
                if (!path.Value.TryGetProperty(method, out var operation)
                    || !operation.TryGetProperty("requestBody", out var requestBody)
                    || !requestBody.GetProperty("content").TryGetProperty("application/json", out _))
                {
                    continue;
                }

                var uri = new Uri(RequestBodyContentTypeTests.FillRoute(path.Name, path.Value, operation), UriKind.Relative);
                operations++;

                foreach (var chunked in new[] { false, true })
                {
                    using var request = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri)
                    {
                        Content = Body(oversized, chunked),
                    };

                    if (chunked)
                    {
                        request.Headers.TransferEncodingChunked = true;
                    }

                    using var reply = await host.Client.SendAsync(request);

                    // A chunked body is counted as it is read, so an endpoint that answers 501 because its capability
                    // is not registered in this host never reads it and nothing is held. With a length, the refusal
                    // comes before the endpoint, so every operation answers 413.
                    var readNothing = chunked && reply.StatusCode == HttpStatusCode.NotImplemented;

                    if (reply.StatusCode != HttpStatusCode.RequestEntityTooLarge && !readNothing)
                    {
                        notRefused.Add($"{method.ToUpperInvariant()} {path.Name} ({(chunked ? "chunked" : "with a length")}) -> {(int)reply.StatusCode}");
                    }
                }
            }
        }

        operations.ShouldBeGreaterThan(30, "the document lists too few operations with a JSON body for the probe to mean anything");
        notRefused.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_body_at_the_limit_is_read_and_one_byte_over_is_not()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.MaxJsonBodyBytes = Limit);

        var atLimit = Padded((int)Limit);
        var overLimit = Padded((int)Limit + 1);

        using var accepted = await Post(host, atLimit, chunked: false);
        using var acceptedChunked = await Post(host, atLimit, chunked: true);
        using var refused = await Post(host, overLimit, chunked: false);
        using var refusedChunked = await Post(host, overLimit, chunked: true);

        // The body is not a valid agent definition, so it is the validation that answers, not the limit.
        accepted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        acceptedChunked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        refusedChunked.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task The_refusal_is_a_problem_document_and_names_the_limit()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.MaxJsonBodyBytes = Limit);

        using var reply = await Post(host, Padded((int)Limit + 1), chunked: false);

        reply.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await TraconTestHost.ReadJsonAsync(reply)).GetProperty("detail").GetString().ShouldNotBeNull()
            .ShouldContain(Limit.ToString(System.Globalization.CultureInfo.InvariantCulture), Case.Sensitive);
    }

    [Theory]
    [InlineData("/tracon/v1/chat/completions")]
    [InlineData("/tracon/v1/responses")]
    public async Task The_OpenAI_compatible_routes_refuse_in_their_own_error_shape(string route)
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.MaxJsonBodyBytes = Limit);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(route, UriKind.Relative))
        {
            Content = Body(Padded((int)Limit + 1), chunked: false),
        };

        using var reply = await host.Client.SendAsync(request);

        reply.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await TraconTestHost.ReadJsonAsync(reply)).GetProperty("error").GetProperty("message").GetString()
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_body_that_is_not_JSON_is_not_held_to_the_limit()
    {
        // An upload has its own limit; the JSON limit would refuse it for the wrong reason.
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.MaxJsonBodyBytes = Limit);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents", UriKind.Relative))
        {
            Content = new StringContent(new string('a', (int)Limit * 2), Encoding.UTF8, "text/plain"),
        };

        using var reply = await host.Client.SendAsync(request);

        reply.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task The_default_limit_is_eight_mebibytes()
    {
        TraconEndpointOptions.DefaultMaxJsonBodyBytes.ShouldBe(8L * 1024 * 1024);
        new TraconEndpointOptions().MaxJsonBodyBytes.ShouldBe(TraconEndpointOptions.DefaultMaxJsonBodyBytes);

        await using var host = await TraconTestHost.StartAsync();

        using var refused = await Post(host, Padded(9 * 1024 * 1024), chunked: false);
        using var accepted = await Post(host, Padded(1024 * 1024), chunked: false);

        refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        accepted.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_request_with_an_idempotency_key_still_reaches_its_endpoint_with_its_body()
    {
        // The idempotency filter reads the body to fingerprint it and rewinds it only when it can seek. A limit that
        // wrapped the buffer, not the other way round, left the endpoint an empty body: every request that carried the
        // header answered 400.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureEndpoints: static options => options.MaxJsonBodyBytes = Limit);

        foreach (var chunked in new[] { false, true })
        {
            using var accepted = await RunWithKeyAsync(host, RunBody((int)Limit), chunked);
            using var refused = await RunWithKeyAsync(host, RunBody((int)Limit + 1), chunked);

            accepted.StatusCode.ShouldBe(HttpStatusCode.OK, chunked ? "chunked, at the limit" : "with a length, at the limit");
            refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge, chunked ? "chunked" : "with a length");
        }

        // The same key with a different body is still a different request: the filter saw both bodies.
        var key = Guid.NewGuid().ToString();
        using var first = await RunWithKeyAsync(host, RunBody(200), chunked: false, key);
        using var second = await RunWithKeyAsync(host, RunBody(300), chunked: false, key);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_consumer_prefix_that_contains_v1_still_gets_a_problem_document_from_the_management_routes()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.AddTracon();

        await using var app = builder.Build();
        app.MapTracon("/api/v1/tracon", static options => options.MaxJsonBodyBytes = Limit);
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        using var management = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tracon/api/agents") { Content = Body(Padded((int)Limit + 1), chunked: false) };
        using var compatible = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tracon/v1/responses") { Content = Body(Padded((int)Limit + 1), chunked: false) };

        using var managementReply = await client.SendAsync(management, TestContext.Current.CancellationToken);
        using var compatibleReply = await client.SendAsync(compatible, TestContext.Current.CancellationToken);

        managementReply.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        managementReply.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        compatibleReply.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await TraconTestHost.ReadJsonAsync(compatibleReply)).TryGetProperty("error", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_real_server_refuses_an_oversized_body_with_or_without_a_length()
    {
        // The test server hands the endpoint a body stream the way no real server does. Kestrel wraps what a body read
        // throws and decides for itself how a chunked body is read, so the same two requests are sent to it.
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.AddTracon();

        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapTracon("/tracon", static options => options.MaxJsonBodyBytes = Limit);
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        var oversized = Padded((int)Limit + 1);
        var atLimit = Padded((int)Limit);

        foreach (var chunked in new[] { false, true })
        {
            using var refused = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents") { Content = Body(oversized, chunked) };
            using var accepted = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents") { Content = Body(atLimit, chunked) };

            if (chunked)
            {
                refused.Headers.TransferEncodingChunked = true;
                accepted.Headers.TransferEncodingChunked = true;
            }

            using var refusedReply = await client.SendAsync(refused, TestContext.Current.CancellationToken);
            using var acceptedReply = await client.SendAsync(accepted, TestContext.Current.CancellationToken);

            refusedReply.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge, chunked ? "chunked" : "with a length");
            acceptedReply.StatusCode.ShouldBe(HttpStatusCode.BadRequest, chunked ? "chunked, at the limit" : "with a length, at the limit");
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_limit_below_one_byte_is_refused_when_it_is_set(long value)
        => Should.Throw<ArgumentOutOfRangeException>(() => new TraconEndpointOptions().MaxJsonBodyBytes = value);

    private static string RunBody(int bytes)
    {
        const string Frame = """{"message":""}""";

        return $$"""{"message":"{{new string('a', bytes - Frame.Length)}}"}""";
    }

    private static async Task<HttpResponseMessage> RunWithKeyAsync(TraconTestHost host, string json, bool chunked, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/kod-agent/run", UriKind.Relative))
        {
            Content = Body(json, chunked),
        };

        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());

        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
        }

        return await host.Client.SendAsync(request);
    }

    private static string Padded(int bytes)
    {
        const string Frame = """{"padding":""}""";

        return $$"""{"padding":"{{new string('a', bytes - Frame.Length)}}"}""";
    }

    private static HttpContent Body(string json, bool chunked)
    {
        if (!chunked)
        {
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return content;
    }

    private static async Task<HttpResponseMessage> Post(TraconTestHost host, string json, bool chunked)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents", UriKind.Relative))
        {
            Content = Body(json, chunked),
        };

        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
        }

        return await host.Client.SendAsync(request);
    }
}
