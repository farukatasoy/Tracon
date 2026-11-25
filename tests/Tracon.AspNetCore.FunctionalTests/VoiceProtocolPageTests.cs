using System.Text.Json;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The protocol page of the site names every frame, every field and every handshake answer the protocol has.
/// </summary>
/// <remarks>
/// The page is hand-written, so a frame added to the code and left off the page is a defect nothing else would see. The names are
/// read from the same place the snapshot reads them (the protocol's own constants and the frame types), not from a copy.
/// </remarks>
public sealed class VoiceProtocolPageTests
{
    private const string PagePath = "docs-site/src/content/docs/reference/voice-conversation-protocol.md";
    private const string SchemaPath = "docs-site/public/schemas/voice-conversation-frames.schema.json";

    [Fact]
    public void The_page_names_every_frame_and_every_field_of_the_protocol()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot.Path, PagePath));

        foreach (var name in VoiceFramesSnapshotTests.Names().Where(static name => name.StartsWith("client-", StringComparison.Ordinal) || name.StartsWith("server-", StringComparison.Ordinal)))
        {
            var word = name[(name.IndexOf(' ', StringComparison.Ordinal) + 1)..];

            // `type` is the discriminator the page explains once; the others are named in a table cell.
            if (string.Equals(word, "type", StringComparison.Ordinal))
            {
                continue;
            }

            page.ShouldContain($"`{word}`", Case.Sensitive, $"The protocol page does not name '{word}' ({name}).");
        }
    }

    [Fact]
    public void The_page_lists_every_handshake_answer_the_endpoint_can_give_and_the_audio_formats()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot.Path, PagePath));

        foreach (var status in new[] { "101", "400", "401", "403", "404", "429", "501", "503" })
        {
            page.ShouldContain($"| `{status}` |", Case.Sensitive, $"The handshake table lacks {status}.");
        }

        page.ShouldContain(VoiceConversationProtocol.SubProtocol, Case.Sensitive);
        page.ShouldContain(VoiceConversationProtocol.TokenSubProtocolPrefix, Case.Sensitive);
        page.ShouldContain(VoiceAudioFormats.WebmOpus, Case.Sensitive);
        page.ShouldContain(VoiceAudioFormats.Pcm16, Case.Sensitive);
    }

    [Fact]
    public void The_page_points_at_the_schema_by_the_address_the_schema_declares()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot.Path, PagePath));
        var id = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Path, SchemaPath))).RootElement.GetProperty("$id").GetString()!;

        page.ShouldContain(new Uri(id).AbsolutePath, Case.Sensitive);
    }
}
