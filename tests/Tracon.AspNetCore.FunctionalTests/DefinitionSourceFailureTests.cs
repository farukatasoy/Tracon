using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A failing or stalled <see cref="IAgentDefinitionSource"/> at the HTTP boundary.
/// </summary>
/// <remarks>
/// A source that throws must not leak its raw text into a response, and must not take the listing down for the
/// others. A source that stalls must not leave a run row behind when the caller gives up: the run endpoint reads
/// the definition's schema before it opens a run, and that read honors the caller's token.
/// </remarks>
public sealed class DefinitionSourceFailureTests
{
    private const string SecretMessage = "raw upstream failure with a connection string secret";
    private const string ProviderName = "failure-provider";
    private const string ModelName = "failure-model";

    [Fact]
    public async Task A_source_that_throws_on_lookup_answers_a_run_without_its_raw_text()
    {
        var provider = Provider();
        var source = new InMemoryDefinitionSource("failing-definitions")
        {
            BeforeRead = _ => throw new InvalidOperationException(SecretMessage),
        };

        await using var host = await StartAsync(provider, source);

        using var response = await host.Client.PostAsJsonAsync("/tracon/api/agents/any-agent/run", new AgentRunRequest { Message = "go" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString().ShouldNotBeNull();

        detail.ShouldNotContain(SecretMessage, Case.Sensitive);
        detail.ShouldContain("failing-definitions", Case.Sensitive);
        detail.ShouldContain(TraconAgentSourceException.SourceFailedErrorType, Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_listing_stays_available_when_a_definition_source_throws()
    {
        var provider = Provider();
        var source = new InMemoryDefinitionSource("failing-definitions")
        {
            BeforeRead = _ => throw new InvalidOperationException(SecretMessage),
        };

        await using var host = await StartAsync(provider, source, builder => builder.AddAgent(new AgentDefinition
        {
            Name = "healthy-agent",
            Instructions = "ok",
            Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
        }));

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Select(static agent => agent.GetProperty("name").GetString())
            .ShouldContain("healthy-agent", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_caller_that_gives_up_while_the_source_stalls_leaves_no_run_row()
    {
        var provider = Provider();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new InMemoryDefinitionSource("stalling-definitions")
        {
            BeforeRead = async cancellationToken =>
            {
                reading.TrySetResult();

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            },
        };

        await using var host = await StartAsync(provider, source);

        using var gaveUp = new CancellationTokenSource();
        var call = host.Client.PostAsJsonAsync(
            "/tracon/api/agents/any-agent/run",
            new AgentRunRequest { Message = "go" },
            gaveUp.Token);

        // The caller gives up only once the source is inside its stalled read, so the cancel lands on the lookup.
        await reading.Task.WaitAsync(TestContext.Current.CancellationToken);
        await gaveUp.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await call);

        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).ShouldBeEmpty();
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        InMemoryDefinitionSource source,
        Action<ITraconBuilder>? configure = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgentDefinitionSource(source);
                configure?.Invoke(builder);
            });
}
