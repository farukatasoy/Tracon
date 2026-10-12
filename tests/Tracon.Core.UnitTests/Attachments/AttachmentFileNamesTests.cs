using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Attachments;

/// <summary>
/// The file name of an attachment Tracon writes follows the DETECTED type (F-336).
/// </summary>
/// <remarks>
/// Four write sites had four rules: the speech endpoint wrote <c>.mp3</c> for any
/// audio and the conversation driver wrote <c>.bin</c>. The first test reuses the
/// guard's own signature list, so a type added to the guard without an extension
/// here fails.
/// </remarks>
public sealed class AttachmentFileNamesTests
{
    [Theory]
    [MemberData(nameof(AttachmentTypeGuardTests.KnownSignatures), MemberType = typeof(AttachmentTypeGuardTests))]
    public void Every_type_the_guard_detects_has_its_own_extension(byte[] data, string expectedMediaType)
    {
        var mediaType = new AttachmentTypeGuard(Options.Create(new TraconOptions())).Validate(data).MediaType;

        mediaType.ShouldBe(expectedMediaType);
        string.Equals(AttachmentFileNames.ExtensionFor(mediaType!), "bin", StringComparison.Ordinal)
            .ShouldBeFalse($"'{mediaType}' has no file extension.");
    }

    [Fact]
    public void The_name_carries_the_prefix_the_UTC_time_and_the_extension()
    {
        var createdAt = new DateTimeOffset(2026, 10, 12, 17, 30, 5, TimeSpan.FromHours(3)).AddTicks(1234567);

        AttachmentFileNames.For("speech", "audio/ogg", createdAt).ShouldBe("speech-20261012-143005-1234567.ogg");
    }

    [Fact]
    public void An_unknown_type_falls_back_to_bin()
        => AttachmentFileNames.ExtensionFor("application/x-unknown").ShouldBe("bin");
}
