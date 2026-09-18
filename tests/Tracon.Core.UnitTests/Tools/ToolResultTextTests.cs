using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Tools;

public sealed class ToolResultTextTests
{
    [Fact]
    public void Json_element_returns_raw_json()
    {
        using var document = JsonDocument.Parse("""{"comment":"guard me"}""");

        ToolResultText.TryGetText(document.RootElement, out var text).ShouldBeTrue();

        text.ShouldBe("""{"comment":"guard me"}""");
    }

    [Fact]
    public void Complex_clr_value_is_not_inspectable()
    {
        ToolResultText.TryGetText(new { Comment = "guard me" }, out var text).ShouldBeFalse();

        text.ShouldBeNull();
    }

    [Fact]
    public void Protocol_content_returns_the_text_a_provider_adapter_sends()
    {
        // 🚨 This used to answer false, and five callers branch on it: the
        // output budget skipped an MCP result, the run event and the tool
        // invocation record stored nothing for it, replay lost it, and the
        // content guards swapped it for a fixed sentence — for the one input
        // class ("text a remote MCP tool returned") they exist to read.
        ToolResultText.TryGetText(new TextContent("remote answer"), out var text).ShouldBeTrue();

        text.ShouldNotBeNull().ShouldContain("remote answer", Case.Sensitive);
    }

    [Fact]
    public void A_multi_block_protocol_result_is_inspectable_too()
    {
        // An MCP tool answering with more than one block returns an
        // AIContent[], which is not itself an AIContent.
        var blocks = new AIContent[] { new TextContent("first"), new TextContent("second") };

        ToolResultText.TryGetText(blocks, out var text).ShouldBeTrue();

        text.ShouldNotBeNull().ShouldContain("second", Case.Sensitive);
    }

    [Fact]
    public void Only_a_protocol_result_keeps_its_own_shape()
    {
        ToolResultText.IsProtocolResult(new TextContent("remote answer")).ShouldBeTrue();
        ToolResultText.IsProtocolResult(new AIContent[] { new TextContent("first") }).ShouldBeTrue();

        ToolResultText.IsProtocolResult("plain text").ShouldBeFalse();
        ToolResultText.IsProtocolResult(null).ShouldBeFalse();
    }

    [Fact]
    public void Foreign_exception_message_is_not_exposed()
    {
        ToolFailureText.Get(new InvalidOperationException("Server=secret")).ShouldBe("Tool failed with InvalidOperationException.");
    }

    [Fact]
    public void Tracon_exception_message_is_preserved()
    {
        ToolFailureText.Get(new TraconException("The request timed out.")).ShouldBe("The request timed out.");
    }
}
