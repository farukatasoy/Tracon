using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The configuration the documentation shows for <c>AddContentProtection</c> encrypts: a key id and a key name, and no
/// other setting. A reader who copies it must not end up with user messages in the clear.
/// </summary>
/// <remarks>
/// The tests run a real host on a real SQLite file and read the file's bytes. A host that is only registered shows the
/// protector in the container and the setting in the options; what it did to the data is in the file.
/// </remarks>
public sealed class ContentProtectionDocExampleTests
{
    private const string AgentName = "example-agent";
    private const string KeyId = "2026-08";
    private const string KeyName = "ContentProtectionKeys:2026-08";
    private const string Message = "example-user-message-7c1d52aa";
    private const string EnvelopeMarker = "$apEnc";

    private static readonly string KeyBase64 = Convert.ToBase64String(Encoding.ASCII.GetBytes("example-cp-key-material-01234567"));

    [Fact]
    public async Task The_example_in_the_documentation_encrypts_what_the_user_wrote()
    {
        using var database = new TempSqliteDatabase("content-protection-example");

        await RunOneMessageAsync(database, builder => builder.AddContentProtection(static options =>
        {
            options.ActiveKeyId = KeyId;
            options.Keys[KeyId] = KeyName;
        }));

        var bytes = database.ReadAllBytes();

        SecretHits.FindInBytes(bytes, AgentName).ShouldNotBeEmpty("the run is not in the database, so the scan below proves nothing");
        SecretHits.FindInBytes(bytes, EnvelopeMarker).ShouldNotBeEmpty("no column is protected: the call registered the protector and left protection off");
        SecretHits.FindInBytes(bytes, Message).ShouldBeEmpty("the user's message is in the database as plain text");
    }

    [Fact]
    public async Task The_delegate_can_still_turn_protection_off()
    {
        using var database = new TempSqliteDatabase("content-protection-example-off");

        await RunOneMessageAsync(database, builder => builder.AddContentProtection(static options =>
        {
            options.ActiveKeyId = KeyId;
            options.Keys[KeyId] = KeyName;
            options.Enabled = false;
        }));

        var bytes = database.ReadAllBytes();

        SecretHits.FindInBytes(bytes, Message).ShouldNotBeEmpty("the run is not in the database, so the scan below proves nothing");
        SecretHits.FindInBytes(bytes, EnvelopeMarker).ShouldBeEmpty("a delegate that writes Enabled = false is the way to keep the keys and switch protection off");
    }

    [Fact]
    public async Task The_call_without_a_delegate_leaves_the_switch_to_configuration()
    {
        using var database = new TempSqliteDatabase("content-protection-example-config");

        await RunOneMessageAsync(database, static builder => builder.AddContentProtection());

        var bytes = database.ReadAllBytes();

        SecretHits.FindInBytes(bytes, Message).ShouldNotBeEmpty("the run is not in the database, so the scan below proves nothing");
        SecretHits.FindInBytes(bytes, EnvelopeMarker).ShouldBeEmpty("Tracon:ContentProtection:Enabled was not set, and the call has no settings to act on");
    }

    private static async Task RunOneMessageAsync(TempSqliteDatabase database, Func<ITraconBuilder, ITraconBuilder> addContentProtection)
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => addContentProtection(builder.UseSqlite(database.ConnectionString))
                .AddAgent(TestData.Definition(AgentName)),
            configureServices: static services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, KeyBase64)]).Build());
                services.Configure<TraconOptions>(static options => options.Observability.RecordSensitiveData = false);
            });

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new { message = Message, sessionId = "example-session" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
