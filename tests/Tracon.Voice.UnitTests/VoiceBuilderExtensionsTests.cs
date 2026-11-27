using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// Verifies that the <c>UseVoice(...)</c> registration actually leaves behind
/// resolvable services.
/// </summary>
/// <remarks>
/// 🚨 This class's first test is a regression test for a real defect found in
/// the sample app: a tool failed at call time with
/// <c>No service for type 'IOptions&lt;VoiceOptions&gt;'</c>.
/// Tools resolve DI at CALL time; a missing registration shows up neither at
/// compile time nor during setup.
/// </remarks>
public sealed class VoiceBuilderExtensionsTests
{
    [Fact]
    public void Settings_can_be_resolved_at_call_time()
    {
        using var provider = BuildProvider(options =>
        {
            options.ApiKey = "k";
            options.DefaultVoiceId = "voice-1";
        });

        provider.GetService<IOptions<VoiceOptions>>().ShouldNotBeNull()
            .Value.DefaultVoiceId.ShouldBe("voice-1");
    }

    [Fact]
    public void ALL_of_the_tools_dependencies_can_be_resolved()
    {
        // Every service the tool bodies need is listed here. If one were
        // missing, the error would only show up on a real tool call.
        using var provider = BuildProvider(options => options.ApiKey = "k");

        provider.GetService<ISpeechSynthesizer>().ShouldNotBeNull();
        provider.GetService<ISpeechTranscriber>().ShouldNotBeNull();
        provider.GetService<IVoiceHealthCheck>().ShouldNotBeNull();
        provider.GetService<IVoicePricingReader>().ShouldNotBeNull();
        provider.GetService<IOptions<VoiceOptions>>().ShouldNotBeNull();
        provider.GetService<AttachmentTypeGuard>().ShouldNotBeNull();
        provider.GetService<IAttachmentStore>().ShouldNotBeNull();
        provider.GetService<ITenantContext>().ShouldNotBeNull();
    }

    [Fact]
    public void Three_tools_are_registered()
    {
        using var provider = BuildProvider(options => options.ApiKey = "k");

        var registry = provider.GetRequiredService<IToolRegistry>();

        registry.TryGet("speak", out _).ShouldBeTrue();
        registry.TryGet("transcribe", out _).ShouldBeTrue();
        registry.TryGet("list_voices", out _).ShouldBeTrue();
    }

    [Fact]
    public void Approval_setting_applies_to_production_tools_not_to_listing()
    {
        using var provider = BuildProvider(options =>
        {
            options.ApiKey = "k";
            options.RequireApproval = true;
        });

        var descriptors = provider.GetRequiredService<IToolRegistry>().List();

        Find(descriptors, "speak").RequiresApproval.ShouldBeTrue();
        Find(descriptors, "transcribe").RequiresApproval.ShouldBeTrue();

        // Listing produces no charge and has no external effect.
        Find(descriptors, "list_voices").RequiresApproval.ShouldBeFalse();
    }

    [Fact]
    public void Consumers_own_registration_is_PRESERVED()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer, CustomSynthesizer>();
        services.AddTracon().UseVoice(options => options.ApiKey = "k");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISpeechSynthesizer>().ShouldBeOfType<CustomSynthesizer>();
    }

    [Fact]
    public void The_same_instance_also_binds_to_all_three_interfaces()
    {
        // The concurrency limit must be kept in a SINGLE counter; separate
        // instances would allow double the limit.
        using var provider = BuildProvider(options => options.ApiKey = "k");

        var synthesizer = provider.GetRequiredService<ISpeechSynthesizer>();
        var transcriber = provider.GetRequiredService<ISpeechTranscriber>();
        var health = provider.GetRequiredService<IVoiceHealthCheck>();

        ReferenceEquals(synthesizer, transcriber).ShouldBeTrue();
        ReferenceEquals(synthesizer, health).ShouldBeTrue();
    }

    [Fact]
    public void The_repeat_limit_is_off_by_default_and_the_one_in_configuration_reaches_the_built_in_client()
    {
        using var unset = BuildProvider(options => options.ApiKey = "k");
        using var configured = BuildFromConfiguration(("Tracon:Voice:ApiKey", "k"), ("Tracon:Voice:MaxRetries", "3"));

        unset.GetRequiredService<IOptions<VoiceOptions>>().Value.MaxRetries.ShouldBe(0, "a failed request is not repeated unless asked");
        ((ElevenLabsSpeechClient)unset.GetRequiredService<ISpeechSynthesizer>()).MaxRetries.ShouldBe(0);

        configured.GetRequiredService<IOptions<VoiceOptions>>().Value.MaxRetries.ShouldBe(3);
        ((ElevenLabsSpeechClient)configured.GetRequiredService<ISpeechSynthesizer>()).MaxRetries.ShouldBe(3);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("11")]
    public void A_repeat_limit_out_of_range_stops_the_host_at_startup_and_names_the_key(string value)
    {
        using var provider = BuildFromConfiguration(("Tracon:Voice:ApiKey", "k"), ("Tracon:Voice:MaxRetries", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("MaxRetries", Case.Sensitive);
    }

    private static ServiceProvider BuildFromConfiguration(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value, StringComparer.Ordinal))
            .Build();

        var services = new ServiceCollection();
        services.AddTracon().UseVoice(configuration.GetSection(VoiceOptions.SectionName));

        return services.BuildServiceProvider();
    }

    private static ToolDescriptor Find(IReadOnlyList<ToolDescriptor> descriptors, string name)
        => descriptors.Single(descriptor => string.Equals(descriptor.Name, name, StringComparison.Ordinal));

    private static ServiceProvider BuildProvider(Action<VoiceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddTracon().UseVoice(configure);

        return services.BuildServiceProvider();
    }

    private sealed class CustomSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "mine";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }
}
