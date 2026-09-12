using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// Verifies the <c>list_voices</c> tool's output text: no Turkish, and the
/// voice's gender surfaces when the provider reports it.
/// </summary>
public sealed class ListVoicesToolTests
{
    [Fact]
    public async Task No_voices_yields_an_English_message()
    {
        var result = await InvokeAsync([]);

        result.ShouldBe("No voices available.");
    }

    [Fact]
    public async Task Gender_attribute_is_shown_when_reported()
    {
        var result = await InvokeAsync(
        [
            new VoiceDescriptor
            {
                VoiceId = "v1",
                Name = "Amy",
                Category = "premade",
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [VoiceAttributeNames.Gender] = "female",
                },
            },
        ]);

        result.ShouldBe("Amy (v1) — premade — female");
    }

    [Fact]
    public async Task Missing_gender_attribute_omits_the_trailing_segment()
    {
        var result = await InvokeAsync(
        [
            new VoiceDescriptor { VoiceId = "v1", Name = "Amy" },
        ]);

        result.ShouldBe("Amy (v1)");
    }

    [Fact]
    public async Task Overflow_line_is_in_English()
    {
        var voices = Enumerable.Range(0, ListVoicesTool.MaxListedVoices + 3)
            .Select(index => new VoiceDescriptor { VoiceId = $"v{index}", Name = $"Voice {index:D3}" })
            .ToList();

        var result = await InvokeAsync(voices);

        result.ShouldNotBeNull();
        result.ShouldContain("… and 3 more voices.");
    }

    private static async ValueTask<string?> InvokeAsync(IReadOnlyList<VoiceDescriptor> voices)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer>(new FakeSpeechSynthesizer(voices));

        await using var provider = services.BuildServiceProvider();
        var tool = new ListVoicesTool(provider);

        var arguments = new AIFunctionArguments(
            new Dictionary<string, object?>(StringComparer.Ordinal),
            StringComparer.Ordinal)
        {
            Services = provider,
        };

        return (string?)await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    private sealed class FakeSpeechSynthesizer(IReadOnlyList<VoiceDescriptor> voices) : ISpeechSynthesizer
    {
        public string ProviderName => "fake";

        public int MaxCharactersPerRequest => 1000;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(voices);
    }
}
