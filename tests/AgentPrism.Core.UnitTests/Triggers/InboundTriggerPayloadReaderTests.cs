using System.Text.Json;

namespace AgentPrism.Core.UnitTests.Triggers;

/// <summary>Verifies <see cref="InboundTriggerPayloadReader"/> (section 66.3).</summary>
public sealed class InboundTriggerPayloadReaderTests
{
    [Fact]
    public void WholeBody_mode_returns_the_raw_body_text()
    {
        using var document = JsonDocument.Parse("""{"event":{"text":"hello"}}""");

        InboundTriggerPayloadReader
            .TryExtractMessage(document.RootElement, InboundTriggerPayloadMode.WholeBody, null, out var message)
            .ShouldBeTrue();

        message.ShouldBe("""{"event":{"text":"hello"}}""");
    }

    [Fact]
    public void Path_mode_extracts_a_nested_string_field()
    {
        using var document = JsonDocument.Parse("""{"event":{"text":"hello"}}""");

        InboundTriggerPayloadReader
            .TryExtractMessage(document.RootElement, InboundTriggerPayloadMode.Path, "event.text", out var message)
            .ShouldBeTrue();

        message.ShouldBe("hello");
    }

    [Fact]
    public void Path_mode_extracts_a_non_string_leaf_as_raw_json()
    {
        using var document = JsonDocument.Parse("""{"event":{"count":3}}""");

        InboundTriggerPayloadReader
            .TryExtractMessage(document.RootElement, InboundTriggerPayloadMode.Path, "event.count", out var message)
            .ShouldBeTrue();

        message.ShouldBe("3");
    }

    [Fact]
    public void Path_mode_fails_when_a_segment_does_not_exist()
    {
        using var document = JsonDocument.Parse("""{"event":{"text":"hello"}}""");

        InboundTriggerPayloadReader
            .TryExtractMessage(document.RootElement, InboundTriggerPayloadMode.Path, "event.missing", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Path_mode_fails_when_a_middle_segment_is_not_an_object()
    {
        using var document = JsonDocument.Parse("""{"event":"hello"}""");

        InboundTriggerPayloadReader
            .TryExtractMessage(document.RootElement, InboundTriggerPayloadMode.Path, "event.text", out _)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Path_mode_fails_when_the_path_is_empty(string? path)
    {
        using var document = JsonDocument.Parse("""{"event":{"text":"hello"}}""");

        InboundTriggerPayloadReader
            .TryExtractMessage(document.RootElement, InboundTriggerPayloadMode.Path, path, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Path_mode_fails_when_the_path_exceeds_the_shared_segment_limit()
    {
        // The SAME limit ToolArgumentConditionMatcher enforces (phase 63):
        // one path language, one limit.
        var path = string.Join('.', Enumerable.Repeat("a", ToolArgumentConditionLimits.MaxPathSegments + 1));
        using var document = JsonDocument.Parse("{}");

        InboundTriggerPayloadReader
            .TryExtractMessage(document.RootElement, InboundTriggerPayloadMode.Path, path, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Path_mode_fails_when_the_path_exceeds_the_shared_length_limit()
    {
        var path = new string('a', ToolArgumentConditionLimits.MaxPathLength + 1);
        using var document = JsonDocument.Parse("{}");

        InboundTriggerPayloadReader
            .TryExtractMessage(document.RootElement, InboundTriggerPayloadMode.Path, path, out _)
            .ShouldBeFalse();
    }
}
