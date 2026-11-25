using System.Reflection;
using System.Text;
using Tracon.Tests.Common;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Pins the frame names, the frame fields and the handshake values of the voice conversation protocol to
/// <c>docs/contracts/voice-frames.txt</c>.
/// </summary>
/// <remarks>
/// A client switches on the <c>type</c> of a frame and reads its fields, so removing or renaming one is a breaking change in 1.x;
/// a new one is not. The names are read from the protocol's own constants and from the properties of the two frame types, not
/// from a copy, so a name added to the code and left out of the file is red.
/// </remarks>
public sealed class VoiceFramesSnapshotTests
{
    [Fact]
    public void The_snapshot_matches_the_frames_the_protocol_declares()
    {
        ContractSnapshot.Verify(
            "docs/contracts/voice-frames.txt",
            "tests/Tracon.AspNetCore.FunctionalTests/VoiceFramesSnapshotTests.cs",
            "The frames, fields and handshake values of the voice conversation WebSocket",
            Names());
    }

    [Fact]
    public void The_scan_finds_the_names_it_is_known_to_find()
    {
        // A scan that finds nothing would keep the snapshot test green by leaving the group out.
        var names = Names();

        names.ShouldContain("client-frame start", StringComparer.Ordinal);
        names.ShouldContain("server-frame ready", StringComparer.Ordinal);
        names.ShouldContain("server-frame ended", StringComparer.Ordinal);
        names.ShouldContain("client-field voiceId", StringComparer.Ordinal);
        names.ShouldContain("server-field attachmentId", StringComparer.Ordinal);
        names.ShouldContain("subprotocol tracon.voice.v1", StringComparer.Ordinal);
        names.ShouldContain("input-format pcm16", StringComparer.Ordinal);
    }

    /// <summary>The names of the protocol, one line each.</summary>
    internal static List<string> Names()
    {
        var names = new List<string>();

        foreach (var constant in Constants(typeof(VoiceConversationProtocol)))
        {
            var value = (string)constant.GetRawConstantValue()!;

            if (constant.Name.StartsWith("Client", StringComparison.Ordinal))
            {
                names.Add($"client-frame {value}");
            }
            else if (constant.Name.StartsWith("Server", StringComparison.Ordinal))
            {
                names.Add($"server-frame {value}");
            }
            else if (string.Equals(constant.Name, nameof(VoiceConversationProtocol.SubProtocol), StringComparison.Ordinal))
            {
                names.Add($"subprotocol {value}");
            }
            else if (string.Equals(constant.Name, nameof(VoiceConversationProtocol.TokenSubProtocolPrefix), StringComparison.Ordinal))
            {
                names.Add($"subprotocol-prefix {value}");
            }
            else if (string.Equals(constant.Name, nameof(VoiceConversationProtocol.EndedByAgent), StringComparison.Ordinal)
                || string.Equals(constant.Name, nameof(VoiceConversationProtocol.EndedByDuration), StringComparison.Ordinal))
            {
                names.Add($"ended-reason {value}");
            }
            else
            {
                throw new InvalidOperationException($"The protocol constant '{constant.Name}' has no line in the snapshot. Name its kind here.");
            }
        }

        names.AddRange(Constants(typeof(VoiceAudioFormats)).Select(static constant => $"input-format {constant.GetRawConstantValue()}"));
        names.AddRange(Fields(typeof(VoiceClientMessage)).Select(static field => $"client-field {field}"));
        names.AddRange(Fields(typeof(VoiceServerMessage)).Select(static field => $"server-field {field}"));

        return names;
    }

    /// <summary>The wire names (camelCase) of the properties of a frame type.</summary>
    internal static IReadOnlyList<string> Fields(Type frame)
        => [.. frame
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => CamelCase(property.Name))];

    private static IEnumerable<FieldInfo> Constants(Type type)
        => type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(static field => field.IsLiteral && field.FieldType == typeof(string));

    private static string CamelCase(string name)
    {
        var builder = new StringBuilder(name);
        builder[0] = char.ToLowerInvariant(builder[0]);

        return builder.ToString();
    }
}
