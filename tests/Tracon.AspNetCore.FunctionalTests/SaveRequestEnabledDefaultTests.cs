using System.Net;
using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A webhook subscription, an inbound trigger and a job schedule saved without <c>enabled</c> are saved paused. Each of
/// them starts work nobody asked for the moment it is saved, outward (a delivery to a URL, a run for a caller's event, a
/// job on a timetable), so the caller says <c>true</c> to start it. A quota stays on by default: a quota that is saved and
/// not enforced is the unsafe way to be wrong.
/// </summary>
/// <remarks>
/// The request bodies are raw JSON: the typed request would write the default itself. The retention policy takes the same
/// rule in <see cref="RetentionPolicyEnabledDefaultTests"/>.
/// </remarks>
public sealed class SaveRequestEnabledDefaultTests
{
    private const string Webhook = """{"url":"https://example.com/hook","events":["run.completed"]}""";
    private const string Trigger = """{"targetKind":"agent","targetName":"demo","signingSecretConfigurationName":"Tracon:TriggerSecrets:Slack"}""";
    private const string Schedule = """{"handlerKey":"tracon.agent-batch","targetName":"ozetleyici","cron":"0 3 * * *"}""";

    public static TheoryData<string, string> Resources => new()
    {
        { "/tracon/api/webhooks/orders", Webhook },
        { "/tracon/api/triggers/slack", Trigger },
        { "/tracon/api/schedules/nightly", Schedule },
    };

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task A_resource_saved_without_enabled_is_saved_paused_and_reads_back_paused(string path, string body)
    {
        await using var host = await TraconTestHost.StartAsync();

        var saved = await PutAsync(host, path, body);

        saved.GetProperty("enabled").GetBoolean().ShouldBeFalse("the request did not say the resource is on");

        using var read = await host.Client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task A_resource_saved_with_enabled_true_is_on(string path, string body)
    {
        await using var host = await TraconTestHost.StartAsync();

        var saved = await PutAsync(host, path, WithEnabled(body, "true"));

        saved.GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task A_resource_saved_with_enabled_false_stays_paused(string path, string body)
    {
        await using var host = await TraconTestHost.StartAsync();

        var saved = await PutAsync(host, path, WithEnabled(body, "false"));

        saved.GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_quota_saved_without_enabled_is_still_on()
    {
        await using var host = await TraconTestHost.StartAsync();

        var saved = await PutAsync(
            host,
            "/tracon/api/quotas",
            """{"period":"Daily","maxRuns":100}""");

        saved.GetProperty("enabled").GetBoolean().ShouldBeTrue("a quota that is not enforced is the unsafe way to be wrong");
    }

    private static string WithEnabled(string body, string value) => body[..^1] + $",\"enabled\":{value}}}";

    private static async Task<JsonElement> PutAsync(TraconTestHost host, string path, string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await host.Client.PutAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return await TraconTestHost.ReadJsonAsync(response);
    }
}
