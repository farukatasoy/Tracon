using System.Net;
using System.Text;
using Tracon.Client.Generated;

namespace Tracon.Client.UnitTests;

/// <summary>
/// A queued workflow run (<c>Prefer: respond-async</c>) is answered with
/// <c>202 Accepted</c>, and the generated client hands its body over typed.
/// </summary>
/// <remarks>
/// The generated method's success type is the stream's text, so a <c>202</c> arrives,
/// like a queued agent run's, as <see cref="TraconApiException{TResult}"/> carrying
/// <see cref="AcceptedRunResponse"/>. A client generated from a document without the
/// <c>202</c> would surface the body as untyped text and lose the run id.
/// </remarks>
public sealed class TraconApiClientQueuedWorkflowTests
{
    private const string RunId = "01a1340e-ad31-75e4-b64b-e3d0a26ab0a1";

    [Fact]
    public async Task A_queued_workflow_run_surfaces_its_202_body_as_the_accepted_run()
    {
        using var http = new HttpClient(new AcceptedHandler()) { BaseAddress = new Uri("https://example.test/tracon/") };
        var client = new TraconApiClient(http);

        var accepted = await Should.ThrowAsync<TraconApiException<AcceptedRunResponse>>(
            () => client.TraconRunWorkflowAsync("podcast", new WorkflowRunHttpRequest { Message = "Episode 12" }));

        accepted.StatusCode.ShouldBe(202);
        accepted.Result.RunId.ShouldBe(Guid.Parse(RunId));
        accepted.Result.EventsLocation.ShouldBe($"/tracon/api/runs/{RunId}/events");
    }

    [Fact]
    public async Task A_queued_resume_and_a_queued_response_surface_the_same_body()
    {
        using var http = new HttpClient(new AcceptedHandler()) { BaseAddress = new Uri("https://example.test/tracon/") };
        var client = new TraconApiClient(http);

        var resumed = await Should.ThrowAsync<TraconApiException<AcceptedRunResponse>>(
            () => client.TraconResumeWorkflowAsync(Guid.Parse(RunId), new WorkflowResumeHttpRequest()));
        var answered = await Should.ThrowAsync<TraconApiException<AcceptedRunResponse>>(
            () => client.TraconRespondWorkflowRequestAsync(Guid.Parse(RunId), new WorkflowRespondHttpRequest { RequestId = "gate", Approved = true }));

        resumed.Result.JobId.ShouldBe(Guid.Parse(RunId));
        answered.Result.JobId.ShouldBe(Guid.Parse(RunId));
    }

    private sealed class AcceptedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = $$"""{"runId":"{{RunId}}","jobId":"{{RunId}}","location":"/tracon/api/runs/{{RunId}}","eventsLocation":"/tracon/api/runs/{{RunId}}/events"}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
