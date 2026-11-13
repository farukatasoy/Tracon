using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Client.Generated;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The generated client reads the data subject export: the document it returns, and the problem it raises when no
/// resolver is registered.
/// </summary>
/// <remarks>
/// The export was the one answer of the document with a 200 and no body schema, so the typed method returned nothing and
/// the document the caller asked for could only be read through a hand-made request. It is also the one operation of the
/// document that names a response header, <c>X-Tracon-Unresolved-Conversations</c>: the count of sessions whose
/// conversation the export could not look up, which tells a caller whether the document is complete. The method therefore
/// returns the response wrapper (the status, the headers and the document), and no other method does.
/// </remarks>
public sealed class GeneratedClientDataSubjectExportTests
{
    [Fact]
    public async Task The_export_returns_the_document_as_a_json_element_keyed_by_target_table()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IDataSubjectResolver>(new EmptyScopeResolver()));

        var response = await CreateClient(host).TraconExportDataSubjectAsync("subject");

        response.StatusCode.ShouldBe(200);
        response.Result.ValueKind.ShouldBe(JsonValueKind.Object);
        response.Result.EnumerateObject().Any().ShouldBeTrue("the document holds a key for every target table, an empty array when nothing matches");
    }

    [Fact]
    public async Task The_export_hands_the_caller_the_header_that_counts_the_conversations_it_could_not_look_up()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IDataSubjectResolver>(new EmptyScopeResolver()));

        var response = await CreateClient(host).TraconExportDataSubjectAsync("subject");

        // Written on every 200: "0" says every conversation was looked up, so an absent header is not a clean export.
        response.Headers.ShouldContainKey("X-Tracon-Unresolved-Conversations");
        response.Headers["X-Tracon-Unresolved-Conversations"].ShouldHaveSingleItem().ShouldBe("0");
    }

    [Fact]
    public async Task Without_a_resolver_the_export_is_a_conflict_problem_the_client_raises_typed()
    {
        await using var host = await TraconTestHost.StartAsync();

        var failure = await Should.ThrowAsync<TraconApiException<ProblemDetails>>(
            async () => await CreateClient(host).TraconExportDataSubjectAsync("subject"));

        failure.StatusCode.ShouldBe(409);
        failure.Result.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    private static TraconApiClient CreateClient(TraconTestHost host)
    {
        host.Client.BaseAddress = new Uri(host.Client.BaseAddress!, "tracon/");

        return new TraconApiClient(host.Client);
    }

    private sealed class EmptyScopeResolver : IDataSubjectResolver
    {
        public ValueTask<DataSubjectScope> ResolveAsync(string subjectId, string tenantId, CancellationToken cancellationToken = default)
            => new(new DataSubjectScope { SessionIds = [], RunIds = [], ConversationIds = [] });
    }
}
