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
    public void Attachment_is_not_inline_tool_text()
    {
        ToolResultText.TryGetText(new TextContent("attachment"), out var text).ShouldBeFalse();

        text.ShouldBeNull();
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
