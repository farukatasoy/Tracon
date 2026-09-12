using Tracon.Voice.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// Validates the <c>speak</c> tool's attachment write path.
/// </summary>
/// <remarks>
/// The three tests in this class guard Phase 28's most expensive findings:
/// writing the session id to the attachment (G1), the tool itself calling the
/// type check (G2), and recognizing a real MP3 byte sequence (G3).
/// </remarks>
public sealed class SpeakToolTests
{
    /// <summary>A real MPEG frame header that carries NO ID3 tag.</summary>
    /// <remarks>
    /// 0xFF 0xF3 = 11-bit sync + MPEG2 + Layer III. This byte sequence was
    /// deliberately chosen because the old allow-list recognized only three
    /// fixed values.
    /// </remarks>
    private static readonly byte[] FrameSyncMp3 = [0xFF, 0xF3, 0x48, 0xC4, 0x00, 0x00];

    [Fact]
    public async Task Produced_audio_attachment_is_written_with_the_SESSION_id()
    {
        // 🚨 G1. The retention policy treats an attachment written without a
        // session as ORPHANED and deletes it after the cutoff date; the audio
        // would disappear while the session is still alive.
        var store = new RecordingAttachmentStore();
        var services = BuildServices(store, FrameSyncMp3);

        var scope = new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            TenantId = "tenant-1",
            SessionId = "session-42",
            AgentName = "voice-assistant",
        };

        TraconRunContext.SetCurrent(scope);

        try
        {
            _ = await InvokeAsync(services, "hello");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        var saved = store.Saved.ShouldHaveSingleItem();
        saved.SessionId.ShouldBe("session-42");
        saved.RunId.ShouldBe(scope.RunId);
        saved.TenantId.ShouldBe("tenant-1");
    }

    [Fact]
    public async Task Headerless_content_is_NOT_written_to_the_attachment_store()
    {
        // 🚨 G2. IAttachmentStore.SaveAsync performs no validation; validation
        // lives at the HTTP layer. The tool guard MUST call it ITSELF.
        var store = new RecordingAttachmentStore();

        // Raw PCM: matches no magic byte.
        var services = BuildServices(store, [0x01, 0x02, 0x03, 0x04, 0x05]);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await InvokeAsync(services, "hello"));

        exception.Message.ShouldContain("magic byte");
        store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Frame_synced_MP3_is_recognized_and_saved()
    {
        // 🚨 G3. The allow-list used to recognize only ID3/FFFB/FFF3; real
        // output can carry a different valid frame header.
        var store = new RecordingAttachmentStore();
        var services = BuildServices(store, FrameSyncMp3);

        var result = await InvokeAsync(services, "hello");

        store.Saved.ShouldHaveSingleItem().MediaType.ShouldBe("audio/mpeg");
        result.ShouldNotBeNull();
        result.ToString().ShouldNotBeNull().ShouldContain("attachmentId=");
    }

    [Fact]
    public async Task Result_returns_the_attachment_id_NOT_the_raw_audio()
    {
        var store = new RecordingAttachmentStore();
        var services = BuildServices(store, FrameSyncMp3);

        var result = (await InvokeAsync(services, "hello"))?.ToString();

        result.ShouldNotBeNull();
        result.Contains(store.Saved[0].Id.ToString(), StringComparison.Ordinal).ShouldBeTrue();

        // If the raw audio landed in the result, the context window would fill up with base64.
        result.Contains("audio/mpeg;base64", StringComparison.Ordinal).ShouldBeFalse();
        result.Length.ShouldBeLessThan(200);
    }

    [Fact]
    public async Task Text_is_NOT_truncated_when_over_the_character_limit_it_errors()
    {
        var store = new RecordingAttachmentStore();
        var services = BuildServices(store, FrameSyncMp3, options => options.MaxCharactersPerRequest = 5);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await InvokeAsync(services, "this text is longer than five characters"));

        exception.Message.ShouldContain("the limit is 5");
        store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public void Tool_schema_requires_text()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var schema = new SpeakTool(services).JsonSchema.GetRawText();

        schema.ShouldContain("\"text\"");
        schema.ShouldContain("\"required\"");
    }

    private static async ValueTask<object?> InvokeAsync(ServiceProvider services, string text)
    {
        var tool = new SpeakTool(services);

        var arguments = new AIFunctionArguments(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["text"] = text },
            StringComparer.Ordinal)
        {
            Services = services,
        };

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    private static ServiceProvider BuildServices(
        IAttachmentStore store,
        byte[] audioBytes,
        Action<VoiceOptions>? configure = null)
    {
        var voiceOptions = new VoiceOptions { ApiKey = "k", DefaultVoiceId = "voice-1" };
        configure?.Invoke(voiceOptions);

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(audioBytes));

        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<ITenantContext>(new FixedTenantContext("tenant-1"));
        services.AddSingleton(Options.Create(voiceOptions));
        services.AddSingleton(Options.Create(new TraconOptions()));
        services.AddSingleton<AttachmentTypeGuard>();
        services.AddSingleton<VoicePricing>();
        services.AddSingleton<ISpeechSynthesizer>(
            new ElevenLabsSpeechClient(voiceOptions, new HttpClient(handler)));

        return services.BuildServiceProvider();
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    /// <summary>Store that keeps saved attachments in memory.</summary>
    private sealed class RecordingAttachmentStore : IAttachmentStore
    {
        public List<AttachmentDescriptor> Saved { get; } = [];

        public ValueTask<AttachmentDescriptor> SaveAsync(
            AttachmentContent content,
            CancellationToken cancellationToken = default)
        {
            var descriptor = new AttachmentDescriptor
            {
                Id = Guid.NewGuid(),
                TenantId = content.TenantId,
                SessionId = content.SessionId,
                RunId = content.RunId,
                FileName = content.FileName,
                MediaType = content.MediaType,
                ByteSize = content.Data.Length,
                Sha256 = "FAKE",
                CreatedBy = content.CreatedBy,
                CreatedAt = DateTimeOffset.UnixEpoch,
            };

            Saved.Add(descriptor);

            return ValueTask.FromResult(descriptor);
        }

        public ValueTask<AttachmentDescriptor?> GetAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Saved.Find(entry => entry.Id == id));

        public ValueTask<Stream?> OpenReadAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream?>(null);

        public ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(
            AttachmentQuery query,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AttachmentDescriptor>>(Saved);

        public ValueTask<bool> DeleteAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);

        public ValueTask<int> DeleteBySessionAsync(
            string tenantId,
            string sessionId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);
    }
}
