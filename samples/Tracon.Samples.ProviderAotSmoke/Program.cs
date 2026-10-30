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
    .UseOpenAISpeech(o =>
    {
        o.SynthesisModel = "tts-smoke";
        o.TranscriptionModel = "stt-smoke";
    })
    .UseOpenAIImages(o => o.Model = "image-smoke")
    .UseAzureOpenAI(baseAddress, "smoke-key")
    .UseAzureOpenAIImages()
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

// OpenAI speech through the Microsoft.Extensions.AI bridge: both directions reach
// their own endpoint, and the bridged provider answers under the name "openai".
var speech = host.Services.GetRequiredService<ISpeechProviderResolver>();

var bridgedSynthesizer = await speech.GetSynthesizerAsync("openai");
var bridgedAudio = await bridgedSynthesizer.SynthesizeAsync(new SpeechRequest { Text = "hello" });
Check(failures, "openai speech audio", bridgedAudio.Data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
    FakeProviderServer.ExpectedAudio.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
Check(failures, "openai speech model", bridgedAudio.ModelId ?? "none", "tts-smoke");

var bridgedTranscriber = await speech.GetTranscriberAsync("openai");
using (var recording = new MemoryStream(FakeProviderServer.ExpectedAudio.ToArray()))
{
    var transcript = await bridgedTranscriber.TranscribeAsync(recording, "audio/mpeg");
    Check(failures, "openai transcript", transcript.Text, FakeProviderServer.Transcript);
}

// Image generation through each provider's own path. The generator is the one the
// generate_image tool resolves; a host that only registers it must still publish.
#pragma warning disable MEAI001 // Microsoft.Extensions.AI marks its image generator experimental
foreach (var providerName in new[] { OpenAIProviderNames.ChatCompletions, AzureOpenAIProviderNames.AzureOpenAI })
{
    var generator = host.Services.GetRequiredKeyedService<IImageGenerator>(providerName);
    var images = await generator.GenerateAsync(new ImageGenerationRequest("a lighthouse"), new ImageGenerationOptions { Count = 1 });
    var image = images.Contents.OfType<DataContent>().SingleOrDefault();
    Check(failures, $"{providerName} image", image is null ? "none" : $"{image.MediaType}:{image.Data.Length}",
        $"image/png:{FakeProviderServer.ExpectedImage.Length}");
}
#pragma warning restore MEAI001

await host.StopAsync();

// Each provider must have reached its own path, not another's.
RequireSeen(failures, server, "/v1/chat/completions");
RequireSeen(failures, server, "/openai/v1/chat/completions");
RequireSeen(failures, server, "/v1/text-to-speech/");
RequireSeen(failures, server, "/v2/voices");
RequireSeen(failures, server, "/v1/audio/speech");
RequireSeen(failures, server, "/v1/audio/transcriptions");
RequireSeen(failures, server, "/v1/images/generations");
RequireSeen(failures, server, "/openai/v1/images/generations");

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("provider AOT smoke passed: openai azure-openai voice openai-speech images");
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
