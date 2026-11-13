using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A text a client chooses and a store keeps in a column is refused over the column's limit with a <c>400</c> that names
/// the field, the same on every store and before anything is written.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server holds an identifier in <c>nvarchar(200)</c>; the other stores keep any length. The limit is the narrowest
/// engine's on all of them, as for an idempotency key and a session identifier. Each test sends one more character than
/// the limit and then the limit itself, so the edge is pinned from both sides.
/// </para>
/// <para>
/// The inputs here are one of each kind the table in <c>ClientTextLimits</c> lists: a name in the path of a <c>PUT</c>, a
/// property of a body type, a property of a type nested in a body, and the file name of a form upload. The in-memory stores
/// stand in for all of them because the check runs before a store is asked.
/// </para>
/// </remarks>
public sealed class ClientTextLengthHttpTests
{
    private const string Prefix = "/tracon/api/";

    [Theory]
    [InlineData("agents/{0}", "name")]
    [InlineData("evals/{0}", "name")]
    [InlineData("experiments/{0}", "name")]
    [InlineData("mcp-servers/{0}", "name")]
    [InlineData("schedules/{0}", "name")]
    [InlineData("skills/{0}", "name")]
    [InlineData("triggers/{0}", "name")]
    [InlineData("webhooks/{0}", "name")]
    [InlineData("workflows/{0}", "name")]
    [InlineData("retention/{0}", "target")]
    [InlineData("tenants/acme/providers/{0}", "provider")]
    public async Task A_name_in_the_path_of_a_PUT_over_200_characters_is_a_400_that_names_the_parameter(string route, string parameter)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var tooLong = await PutAsync(host, route, new string('n', 201));
        var problem = await ReadProblemAsync(tooLong);

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, problem.ToString());
        problem.GetProperty("title").GetString().ShouldBe("Value too long");
        Detail(problem).ShouldContain($"'{parameter}'");
        Detail(problem).ShouldContain("200 characters");

        // The name that fits goes on to the handler, which has its own opinion of an empty body: not this one.
        using var fits = await PutAsync(host, route, new string('n', 200));
        var answer = fits.StatusCode == HttpStatusCode.BadRequest ? await ReadProblemAsync(fits) : default;

        if (answer.ValueKind == JsonValueKind.Object)
        {
            string.Equals(answer.GetProperty("title").GetString(), "Value too long", StringComparison.Ordinal).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task A_name_over_the_limit_in_the_path_of_a_GET_or_a_DELETE_is_still_a_name_no_row_has()
    {
        await using var host = await TraconTestHost.StartAsync();
        var name = new string('n', 201);

        using var read = await host.Client.GetAsync(new Uri($"{Prefix}agents/{name}", UriKind.Relative));
        using var delete = await host.Client.DeleteAsync(new Uri($"{Prefix}webhooks/{name}", UriKind.Relative));

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_name_in_the_body_of_a_POST_over_200_characters_is_a_400_that_names_the_property_and_nothing_is_created()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var tooLong = await host.Client.PostAsJsonAsync(
            new Uri($"{Prefix}api-keys", UriKind.Relative),
            new { name = new string('k', 201), scopes = new[] { "RunsRead" } });
        var problem = await ReadProblemAsync(tooLong);

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, problem.ToString());
        problem.GetProperty("title").GetString().ShouldBe("Invalid request body");
        Detail(problem).ShouldContain("'name'");
        Detail(problem).ShouldContain("200 characters");

        using var listed = await host.Client.GetAsync(new Uri($"{Prefix}api-keys", UriKind.Relative));
        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(0);

        using var fits = await host.Client.PostAsJsonAsync(
            new Uri($"{Prefix}api-keys", UriKind.Relative),
            new { name = new string('k', 200), scopes = new[] { "RunsRead" } });

        fits.StatusCode.ShouldBe(HttpStatusCode.OK, await fits.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_address_over_2000_characters_in_the_body_of_a_PUT_is_a_400_that_names_the_property()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var tooLong = await host.Client.PutAsJsonAsync(
            new Uri($"{Prefix}webhooks/limit-check", UriKind.Relative),
            CredentialHeaderSaveTests.WebhookBody(url: "https://example.com/" + new string('a', 1981)));
        var problem = await ReadProblemAsync(tooLong);

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, problem.ToString());
        Detail(problem).ShouldContain("'url'");
        Detail(problem).ShouldContain("2000 characters");

        using var fits = await host.Client.PutAsJsonAsync(
            new Uri($"{Prefix}webhooks/limit-check", UriKind.Relative),
            CredentialHeaderSaveTests.WebhookBody(url: "https://example.com/" + new string('a', 1980)));

        // The address is long and the limit is not the reason the handler may still refuse it.
        if (fits.StatusCode == HttpStatusCode.BadRequest)
        {
            Detail(await ReadProblemAsync(fits)).ShouldNotContain("2000 characters");
        }
    }

    [Fact]
    public async Task A_model_name_over_200_characters_nested_in_an_agent_definition_is_a_400_that_names_the_property()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var tooLong = await host.Client.PostAsJsonAsync(
            new Uri($"{Prefix}agents", UriKind.Relative),
            new AgentDefinitionRequest
            {
                Name = "limit-agent",
                Instructions = "Answer.",
                Model = new ModelBinding { Provider = "any", Model = new string('m', 201) },
            });
        var problem = await ReadProblemAsync(tooLong);

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, problem.ToString());
        Detail(problem).ShouldContain("'model'");
        Detail(problem).ShouldContain("200 characters");

        using var absent = await host.Client.GetAsync(new Uri($"{Prefix}agents/limit-agent", UriKind.Relative));
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_resource_name_over_200_characters_nested_in_a_skill_is_a_400_that_names_the_property()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var tooLong = await host.Client.PutAsJsonAsync(
            new Uri($"{Prefix}skills/limit-skill", UriKind.Relative),
            new AgentSkillRequest
            {
                Name = "limit-skill",
                Description = "A skill for the limit check.",
                Instructions = "Do nothing.",
                Resources = [new AgentSkillResourceDefinition { Name = new string('r', 201), Content = "x" }],
            });
        var problem = await ReadProblemAsync(tooLong);

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, problem.ToString());
        Detail(problem).ShouldContain("'name'");
        Detail(problem).ShouldContain("200 characters");
    }

    [Fact]
    public async Task A_configuration_name_over_400_characters_in_a_tenant_binding_is_a_400_that_names_the_property()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var tooLong = await host.Client.PutAsJsonAsync(
            new Uri($"{Prefix}tenants/acme/providers/openai", UriKind.Relative),
            new { apiKeyConfigurationName = "Tracon:ProviderKeys:acme:" + new string('k', 400) });
        var problem = await ReadProblemAsync(tooLong);

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, problem.ToString());
        Detail(problem).ShouldContain("'apiKeyConfigurationName'");
        Detail(problem).ShouldContain("400 characters");
    }

    [Fact]
    public async Task A_file_name_over_400_characters_in_an_upload_is_a_400_that_names_the_field()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var tooLong = await UploadAsync(host, new string('f', 397) + ".png");
        var problem = await ReadProblemAsync(tooLong);

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, problem.ToString());
        problem.GetProperty("title").GetString().ShouldBe("Value too long");
        Detail(problem).ShouldContain("'file'");
        Detail(problem).ShouldContain("400 characters");

        using var fits = await UploadAsync(host, new string('f', 396) + ".png");

        fits.StatusCode.ShouldBe(HttpStatusCode.Created, await fits.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PutAsync(TraconTestHost host, string route, string name)
        => host.Client.PutAsJsonAsync(new Uri(Prefix + string.Format(System.Globalization.CultureInfo.InvariantCulture, route, name), UriKind.Relative), new { });

    private static Task<HttpResponseMessage> UploadAsync(TraconTestHost host, string fileName)
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var content = new MultipartFormDataContent { { new ByteArrayContent([.. signature, .. new byte[8]]), "file", fileName } };

        return host.Client.PostAsync(new Uri($"{Prefix}attachments", UriKind.Relative), content);
    }

    private static string Detail(JsonElement problem) => problem.GetProperty("detail").GetString() ?? string.Empty;

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
