using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host with no vector store answers a knowledge upload with 501, and the answer
/// names the package that supplies the store.
/// </summary>
/// <remarks>
/// <c>UsePostgreSql()</c> alone told a consumer nothing once the meta package stopped
/// carrying the store: the call does not exist without the <c>Tracon.PostgreSql</c>
/// reference. No HTTP test drove this path before, so the text could say anything.
/// </remarks>
public sealed class KnowledgeNotSupportedTests
{
    private static readonly Uri UploadUri = new("/tracon/api/knowledge/docs/documents", UriKind.Relative);

    [Fact]
    public async Task Upload_without_a_vector_store_answers_501_and_names_the_package()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            UploadUri,
            new { sourceId = "handbook", text = "Refunds take five days." },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented, body);
        body.ShouldContain("Tracon.PostgreSql");
        body.ShouldContain("UsePostgreSql()");
    }
}
