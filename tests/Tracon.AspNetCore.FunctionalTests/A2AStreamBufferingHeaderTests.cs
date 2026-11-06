using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every stream Tracon serves tells a reverse proxy not to buffer it. The A2A streaming answer is written by the A2A
/// library, which does not set the header; the endpoint adds it.
/// </summary>
public sealed class A2AStreamBufferingHeaderTests
{
    private const string AgentName = "a2a-header";

    [Theory]
    [InlineData("SendStreamingMessage", "text/event-stream")]
    [InlineData("SendMessage", "application/json")]
    public async Task An_a2a_answer_asks_a_proxy_not_to_buffer_it(string method, string mediaType)
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder =>
            {
                builder.AddAgent(TestData.Definition(AgentName));
                builder.UseA2A(static options => options.ExposedAgents.Add(AgentName));
            },
            configureAfterMap: static app => app.MapTraconA2A());

        var message = System.Text.Json.JsonSerializer.SerializeToElement(
            new A2A.SendMessageRequest
            {
                Message = new A2A.Message
                {
                    Role = A2A.Role.User,
                    Parts = [A2A.Part.FromText("hello")],
                    MessageId = Guid.NewGuid().ToString(),
                },
            },
            A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/a2a/{AgentName}")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = message }),
        };

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe(mediaType);
        response.Headers.GetValues("X-Accel-Buffering").ShouldHaveSingleItem().ShouldBe("no");
    }
}
