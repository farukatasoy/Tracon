using System.Net;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// The handler that keeps the Anthropic client library from waiting for a <c>Retry-After</c> longer than a minute.
/// </summary>
/// <remarks>
/// The library reads <c>Retry-After</c> and <c>Retry-After-Ms</c> from the response it gets back and waits for whatever
/// they say, so the handler takes out the ones that ask for too much and leaves every other header as it came. The end
/// to end behavior (a run against a provider that asks for a day) is in the functional tests.
/// </remarks>
public sealed class AnthropicRetryAfterHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Retry-After", "86400")]
    [InlineData("Retry-After", "61")]
    [InlineData("Retry-After", "2147483647")]
    [InlineData("Retry-After", "99999999999999999999")]
    [InlineData("Retry-After", "1e40")]
    [InlineData("Retry-After", "Fri, 31 Dec 9999 23:59:59 GMT")]
    [InlineData("Retry-After", "Tue, 03 Nov 2026 12:00:00 GMT")]
    [InlineData("Retry-After-Ms", "3600000")]
    [InlineData("Retry-After-Ms", "60001")]
    [InlineData("Retry-After-Ms", "1e40")]
    public async Task A_retry_header_that_asks_for_more_than_a_minute_is_removed(string header, string value)
    {
        using var response = await SendAsync(header, value);

        response.Headers.Contains(header).ShouldBeFalse($"{header}: {value} would have held the run");
    }

    [Theory]
    [InlineData("Retry-After", "0")]
    [InlineData("Retry-After", "2")]
    [InlineData("Retry-After", "60")]
    [InlineData("Retry-After", "1.5")]
    [InlineData("Retry-After", "-5")]
    [InlineData("Retry-After", "soon")]
    [InlineData("Retry-After", "Mon, 02 Nov 2026 12:00:30 GMT")]
    [InlineData("Retry-After", "Sun, 01 Nov 2026 12:00:00 GMT")]
    [InlineData("Retry-After-Ms", "250")]
    [InlineData("Retry-After-Ms", "60000")]
    [InlineData("Retry-After-Ms", "-1")]
    [InlineData("Retry-After-Ms", "Mon, 02 Nov 2026 12:00:30 GMT")]
    public async Task A_retry_header_within_a_minute_or_that_is_not_a_delay_is_left_alone(string header, string value)
    {
        using var response = await SendAsync(header, value);

        response.Headers.TryGetValues(header, out var values).ShouldBeTrue();
        values.ShouldBe([value]);
    }

    [Fact]
    public async Task The_header_that_is_fine_survives_beside_the_one_that_is_not()
    {
        using var response = await SendAsync(("Retry-After", "86400"), ("Retry-After-Ms", "500"));

        response.Headers.Contains("Retry-After").ShouldBeFalse();
        response.Headers.GetValues("Retry-After-Ms").ShouldBe(["500"]);
    }

    [Fact]
    public async Task A_response_without_the_headers_passes_through_untouched()
    {
        using var response = await SendAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.Contains("Retry-After").ShouldBeFalse();
    }

    private static Task<HttpResponseMessage> SendAsync(string header, string value) => SendAsync((header, value));

    private static async Task<HttpResponseMessage> SendAsync(params (string Name, string Value)[] headers)
    {
        using var handler = new AnthropicRetryAfterHandler(new ManualClock(Now))
        {
            InnerHandler = new Respond(headers),
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1/v1/messages"));

        return await invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class Respond((string Name, string Value)[] headers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return Task.FromResult(response);
        }
    }
}
