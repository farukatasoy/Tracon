namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// F-773 (phase 267): <c>Tracon:RunRecording:RecordCitationUrls</c> really reaches
/// <see cref="TraconRunRecordingOptions"/>.
/// </summary>
/// <remarks>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to the binder is
/// silently ignored, and this one decides whether a signed address reaches the stored record.
/// </remarks>
public sealed class TraconRunRecordingOptionsBindingTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void The_citation_url_key_is_bound(string value, bool expected)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:RunRecording:RecordCitationUrls"] = value,
        };

        SectionBinding.Bind<TraconOptions>(values).RunRecording.RecordCitationUrls.ShouldBe(expected);
    }

    [Fact]
    public void An_absent_key_leaves_the_address_unrecorded()
    {
        // The default is the safe one: host only.
        SectionBinding.Bind<TraconOptions>(SectionBinding.NoKeys).RunRecording.RecordCitationUrls.ShouldBeFalse();
    }
}
