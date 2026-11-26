using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A step the host opens in its own endpoint is a run an operator reads over HTTP: what the media services spent inside it
/// is on the run, in its items and in the tree it joins.
/// </summary>
/// <remarks>
/// The cases go through the host's own endpoint and read the answers back through the management API, because a case that
/// asked the store would pass for a record the API does not show, and a case that called the runner outside a request would
/// not prove that the request's tenant reaches the run.
/// </remarks>
#pragma warning disable MEAI001
public sealed class StepRunMediaTests
{
    private const string StepName = "cover-and-narration";
    private const string ImageProvider = "openai";
    private const string TenantHeader = "X-Tracon-Tenant";

    /// <summary>Eleven characters at 1000 USD a million.</summary>
    private const decimal SpeechCost = 0.011m;

    private const decimal ImageCost = 0.04m;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_speech_and_the_picture_a_step_makes_are_items_of_its_run_and_their_sum_is_its_cost()
    {
        var images = new CountingImages();
        await using var host = await StartAsync(images);
        var runId = Guid.NewGuid();

        using var produced = await host.Client.PostAsync(Produce(runId), content: null, Token);

        produced.StatusCode.ShouldBe(HttpStatusCode.OK, await produced.Content.ReadAsStringAsync(Token));

        var run = await ReadAsync(host, $"/tracon/api/runs/{runId}");

        run.GetProperty("kind").GetString().ShouldBe("Step");
        run.GetProperty("agentName").GetString().ShouldBe(StepName);
        run.GetProperty("status").GetString().ShouldBe("Completed");
        run.GetProperty("nonTokenCost").GetProperty("amount").GetDecimal().ShouldBe(SpeechCost + ImageCost);

        var items = (await ReadAsync(host, $"/tracon/api/runs/{runId}/usage")).EnumerateArray()
            .Select(static item => item.GetProperty("usage"))
            .ToList();

        items.Select(static usage => (usage.GetProperty("unit").GetString(), usage.GetProperty("cost").GetDecimal()))
            .ShouldBe([(ToolUsageUnits.Characters, SpeechCost), (ToolUsageUnits.Images, ImageCost)]);

        var listed = (await ReadAsync(host, "/tracon/api/runs")).EnumerateArray()
            .Select(static record => record.GetProperty("id").GetGuid());

        listed.ShouldContain(runId);
    }

    [Fact]
    public async Task A_step_attached_to_an_agent_run_adds_what_it_spent_to_that_runs_tree()
    {
        await using var host = await StartAsync(new CountingImages());

        using var answered = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/writer/run", UriKind.Relative),
            new { message = "write it" },
            Token);

        answered.StatusCode.ShouldBe(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync(Token));

        var parentId = (await ReadAsync(host, "/tracon/api/runs?agentName=writer")).EnumerateArray()
            .ShouldHaveSingleItem()
            .GetProperty("id")
            .GetGuid();
        var stepId = Guid.NewGuid();

        using var produced = await host.Client.PostAsync(Produce(stepId, parentId), content: null, Token);

        produced.StatusCode.ShouldBe(HttpStatusCode.OK, await produced.Content.ReadAsStringAsync(Token));

        var parent = await ReadAsync(host, $"/tracon/api/runs/{parentId}");

        parent.GetProperty("treeNonTokenCost").GetProperty("amount").GetDecimal().ShouldBe(SpeechCost + ImageCost);

        var tree = (await ReadAsync(host, $"/tracon/api/runs/{parentId}/tree")).EnumerateArray()
            .Select(static record => record.GetProperty("id").GetGuid());

        tree.ShouldContain(stepId);
    }

    [Fact]
    public async Task Canceling_the_run_over_HTTP_cancels_the_steps_work_and_closes_the_run_as_canceled()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await StartAsync(new CountingImages(), entered);
        var runId = Guid.NewGuid();

        var waiting = host.Client.PostAsync(new Uri($"/wait?runId={runId}", UriKind.Relative), content: null, Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        using var canceled = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/cancel", UriKind.Relative), content: null, Token);

        canceled.IsSuccessStatusCode.ShouldBeTrue(await canceled.Content.ReadAsStringAsync(Token));

        using var ended = await waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);

        (await ended.Content.ReadAsStringAsync(Token)).ShouldContain("canceled", Case.Sensitive, "the step's own token was canceled");
        (await ReadAsync(host, $"/tracon/api/runs/{runId}")).GetProperty("status").GetString().ShouldBe("Canceled");
    }

    [Fact]
    public async Task A_tenant_that_does_not_allow_the_image_provider_is_refused_inside_a_step_and_nothing_is_measured_for_the_picture()
    {
        var images = new CountingImages();
        await using var host = await StartAsync(images);

        await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", ["test-voice"], Token);

        var runId = Guid.NewGuid();

        using var refused = await host.Client.PostAsync(Produce(runId), content: null, Token);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync(Token));
        images.Calls.ShouldBe(0, "the prompt did not leave for a provider the tenant does not allow");

        (await ReadAsync(host, $"/tracon/api/runs/{runId}")).GetProperty("status").GetString().ShouldBe("Failed");
        (await ReadAsync(host, $"/tracon/api/runs/{runId}/usage")).EnumerateArray()
            .ShouldHaveSingleItem("the speech before the refusal was spent and stays recorded")
            .GetProperty("usage").GetProperty("unit").GetString().ShouldBe(ToolUsageUnits.Characters);
    }

    [Fact]
    public async Task A_provider_failure_inside_a_step_is_masked_and_leaves_no_item()
    {
        const string Key = "sk-test-0123456789-never-shown";
        await using var host = await StartAsync(new CountingImages { Failure = $"401 for key {Key}" }, openAIKey: Key);
        var runId = Guid.NewGuid();

        using var failed = await host.Client.PostAsync(Produce(runId), content: null, Token);

        var answer = await failed.Content.ReadAsStringAsync(Token);

        failed.StatusCode.ShouldBe(HttpStatusCode.BadGateway, answer);
        answer.ShouldNotContain(Key);

        var run = await ReadAsync(host, $"/tracon/api/runs/{runId}");

        run.GetProperty("status").GetString().ShouldBe("Failed");
        run.ToString().ShouldNotContain(Key);
        (await ReadAsync(host, $"/tracon/api/runs/{runId}/usage")).EnumerateArray()
            .ShouldHaveSingleItem("only the speech that was billed")
            .GetProperty("usage").GetProperty("unit").GetString().ShouldBe(ToolUsageUnits.Characters);
    }

    [Fact]
    public async Task A_run_of_another_tenant_is_no_parent_and_its_tree_does_not_move()
    {
        await using var host = await StartAsync(new CountingImages(), tenancy: true);
        var parentId = Guid.NewGuid();

        using var first = await SendAsync(host, HttpMethod.Post, Produce(parentId), "tenant-a");

        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Token));

        var intruderId = Guid.NewGuid();

        using var attached = await SendAsync(host, HttpMethod.Post, Produce(intruderId, parentId), "tenant-b");

        attached.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await attached.Content.ReadAsStringAsync(Token));

        using var missing = await SendAsync(
            host, HttpMethod.Get, new Uri($"/tracon/api/runs/{intruderId}", UriKind.Relative), "tenant-b");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, "a refused step opens no run");

        using var parent = await SendAsync(
            host, HttpMethod.Get, new Uri($"/tracon/api/runs/{parentId}", UriKind.Relative), "tenant-a");

        (await TraconTestHost.ReadJsonAsync(parent)).GetProperty("treeNonTokenCost").GetProperty("amount").GetDecimal()
            .ShouldBe(SpeechCost + ImageCost);
    }

    private static Uri Produce(Guid runId, Guid? parentRunId = null)
        => new(
            parentRunId is { } parent ? $"/produce?runId={runId}&parentRunId={parent}" : $"/produce?runId={runId}",
            UriKind.Relative);

    private static async Task<JsonElement> ReadAsync(TraconTestHost host, string path)
    {
        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static async Task<HttpResponseMessage> SendAsync(TraconTestHost host, HttpMethod method, Uri uri, string tenant)
    {
        using var request = new HttpRequestMessage(method, uri);

        request.Headers.Add(TenantHeader, tenant);

        return await host.Client.SendAsync(request, Token);
    }

    private static Task<TraconTestHost> StartAsync(
        CountingImages images,
        TaskCompletionSource? entered = null,
        bool tenancy = false,
        string? openAIKey = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                if (openAIKey is not null)
                {
                    builder.UseOpenAI(openAIKey);
                }

                builder.AddAgent(new AgentDefinition
                {
                    Name = "writer",
                    Description = "Writes.",
                    Instructions = "Write.",
                    Model = new ModelBinding { Provider = "echo", Model = "m1" },
                    Origin = AgentDefinitionOrigin.Code,
                });

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }
            },
            configureServices: services =>
            {
                services.AddSingleton<ISpeechSynthesizer>(new StubVoiceProvider());
                services.AddKeyedSingleton<IImageGenerator>(ImageProvider, images);
                services.Configure<TraconImageOptions>(static options =>
                {
                    options.Provider = ImageProvider;
                    options.Providers[ImageProvider] = new TraconImageProviderOptions { Model = "image-1" };
                });
                services.Configure<TraconOptions>(static options =>
                {
                    options.Pricing.Currency = "USD";
                    options.Pricing.Voice["test-voice"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["voice-m1"] = new VoicePriceOverride { PerMillionCharacters = 1000m },
                    };
                    options.Pricing.Images[ImageProvider] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["image-1"] = new ImagePriceOverride { PerImage = ImageCost },
                    };
                });
            },
            configureApp: app =>
            {
                app.MapPost("/produce", static async (
                    Guid runId,
                    Guid? parentRunId,
                    ITraconStepRunner steps,
                    ISpeechSynthesisService speech,
                    IImageGenerationService pictures,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        await steps.RunAsync(
                            new TraconStepOptions { Name = StepName, RunId = runId, ParentRunId = parentRunId },
                            async token =>
                            {
                                await speech.SynthesizeAsync(
                                    new SpeechRequest { Text = "Hello there", ModelId = "voice-m1" },
                                    cancellationToken: token);
                                await pictures.GenerateAsync(new TraconImageRequest { Prompt = "a lighthouse" }, token);
                            },
                            cancellationToken);

                        return Results.Ok();
                    }
                    catch (ProviderInvocationException exception)
                    {
                        return Results.Text(exception.ToString(), statusCode: StatusCodes.Status502BadGateway);
                    }
                    catch (TraconTenantProviderRefusedException exception)
                    {
                        return Results.Text(exception.Message, statusCode: StatusCodes.Status403Forbidden);
                    }
                    catch (TraconException exception)
                    {
                        return Results.Text(exception.Message, statusCode: StatusCodes.Status400BadRequest);
                    }
                });

                app.MapPost("/wait", async (Guid runId, ITraconStepRunner steps, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        await steps.RunAsync(
                            new TraconStepOptions { Name = StepName, RunId = runId },
                            async token =>
                            {
                                entered?.TrySetResult();
                                await Task.Delay(Timeout.InfiniteTimeSpan, token); // delay: simulated
                            },
                            cancellationToken);

                        return Results.Text("finished");
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return Results.Text("canceled");
                    }
                });
            });

    private sealed class CountingImages : IImageGenerator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        /// <summary>Gets the message of the provider failure every call ends with; <see langword="null"/> answers.</summary>
        public string? Failure { get; init; }

        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            if (Failure is { } failure)
            {
                throw new HttpRequestException(failure, inner: null, HttpStatusCode.Unauthorized);
            }

            byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            byte[] image = [.. signature, .. new byte[8]];

            return Task.FromResult(new ImageGenerationResponse([new DataContent(image, "image/png")]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
#pragma warning restore MEAI001
