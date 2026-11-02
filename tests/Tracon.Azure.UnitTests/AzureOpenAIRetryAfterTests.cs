using System.ClientModel.Primitives;
using System.Net;
using Microsoft.Extensions.AI;
using Tracon.Azure.UnitTests.Infrastructure;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// A <c>Retry-After</c> longer than a minute does not hold an Azure OpenAI call, and one that is not a delay does not
/// crash it.
/// </summary>
/// <remarks>
/// The client library obeys the header without a ceiling: a day-long value held the call for the day, and a date in the
/// next century made its timer throw. The package gives the client a retry policy that ignores such a header and uses the
/// library's ordinary backoff instead. The end to end behavior of a run is in the functional tests; this holds the wiring
/// of this package's own client.
/// </remarks>
public sealed class AzureOpenAIRetryAfterTests
{
    private const string CompletionBody = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"production-gpt",
         "choices":[{"index":0,"message":{"role":"assistant","content":"pong"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    [Theory]
    [InlineData("86400")]
    [InlineData("2147483647")]
    [InlineData("Fri, 31 Dec 9999 23:59:59 GMT")]
    public async Task A_retry_after_beyond_a_minute_is_retried_at_the_ordinary_backoff(string retryAfter)
    {
        var handler = new TooManyRequestsOnce(retryAfter);
        var client = AzureOpenAIChatClientFactory.CreateClient(
            TestData.Options(),
            transport: new HttpClientPipelineTransport(new HttpClient(handler)));
        using var chatClient = AzureOpenAIChatClientFactory.FromClient(client).CreateChatClient(TestData.Binding());

        // The library's first backoff is under a second; a call that obeyed the header would not return before this limit.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(30));

        var response = await chatClient.GetResponseAsync("ping", cancellationToken: bound.Token);

        response.Text.ShouldBe("pong");
        handler.Calls.ShouldBe(2, "one 429 and the retry that answered");
    }

    private sealed class TooManyRequestsOnce(string retryAfter) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                var rejected = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{\"error\":{\"message\":\"slow down\"}}", System.Text.Encoding.UTF8, "application/json"),
                };
                rejected.Headers.TryAddWithoutValidation("Retry-After", retryAfter);

                return Task.FromResult(rejected);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CompletionBody, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
