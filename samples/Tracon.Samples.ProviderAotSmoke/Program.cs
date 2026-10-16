using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracon;
using Tracon.Samples.ProviderAotSmoke;

// Native AOT smoke for the provider packages. Each package that the site lists
// as "Native AOT: Yes" is called here through its real request and response
// path against a loopback server, and the publish step must emit no trim or
// AOT warning (the repository's TreatWarningsAsErrors turns one into a failure).
// The meta-package smoke (ExtensionAotSmoke) proves the graph; this one proves
// the providers.
using var server = new FakeProviderServer();
var baseAddress = server.BaseAddress;

var builder = Host.CreateApplicationBuilder(args);
builder.AddTracon()
    .UseOpenAI("smoke-key", o => o.Endpoint = new Uri(baseAddress, "v1/"))
    .UseAzureOpenAI(baseAddress, "smoke-key")
    .UseVoice(o =>
    {
        o.ApiKey = "smoke-key";
        o.Endpoint = baseAddress;
        o.DefaultVoiceId = "voice-1";
    });

using var host = builder.Build();
await host.StartAsync();

var failures = new List<string>();

foreach (var providerName in new[] { OpenAIProviderNames.ChatCompletions, AzureOpenAIProviderNames.AzureOpenAI })
{
    var provider = host.Services.GetServices<IModelProvider>().Single(p => string.Equals(p.Name, providerName, StringComparison.Ordinal));
    using var chat = provider.CreateChatClient(new ModelBinding { Provider = providerName, Model = "smoke-model" });

    var reply = await chat.GetResponseAsync("ping");
    Check(failures, $"{providerName} reply", reply.Text, FakeProviderServer.Answer);

    var streamed = new StringBuilder();

    await foreach (var update in chat.GetStreamingResponseAsync("ping"))
    {
        streamed.Append(update.Text);
    }

    Check(failures, $"{providerName} stream", streamed.ToString(), FakeProviderServer.Answer);
}

var synthesizer = host.Services.GetRequiredService<ISpeechSynthesizer>();
var audio = await synthesizer.SynthesizeAsync(new SpeechRequest { Text = "hello" });
Check(failures, "voice audio", audio.Data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
    FakeProviderServer.ExpectedAudio.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));

var voices = await synthesizer.ListVoicesAsync();
Check(failures, "voice list", voices.Count == 1 ? voices[0].VoiceId : "none", "voice-1");

await host.StopAsync();

// Each provider must have reached its own path, not another's.
RequireSeen(failures, server, "/v1/chat/completions");
RequireSeen(failures, server, "/openai/v1/chat/completions");
RequireSeen(failures, server, "/v1/text-to-speech/");
RequireSeen(failures, server, "/v2/voices");

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("provider AOT smoke passed: openai azure-openai voice");
return 0;

static void Check(List<string> failures, string what, string actual, string expected)
{
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
        failures.Add($"{what}: expected '{expected}', got '{actual}'");
    }
}

static void RequireSeen(List<string> failures, FakeProviderServer server, string pathPrefix)
{
    lock (server.Requests)
    {
        if (!server.Requests.Any(r => r.StartsWith(pathPrefix, StringComparison.Ordinal)))
        {
            failures.Add($"no request reached '{pathPrefix}'");
        }
    }
}
