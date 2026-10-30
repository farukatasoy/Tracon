using System.Text.Json;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// How a suspended call's result is read back out of a session history: it comes back as the string
/// the tool wrapper wrote, or as the JSON a store made of it, and only a result that says it is a request counts.
/// </summary>
public sealed class ToolInputPayloadTests
{
    private const string Request = """{"status":"input_required","requestId":"r1","replayCallId":"c2","fingerprint":"F","requestedSchema":{"type":"object"}}""";

    [Fact]
    public void The_string_the_wrapper_wrote_is_read()
        => ToolInputResolver.ReadPayload(Request).ShouldNotBeNull().GetProperty("requestId").GetString().ShouldBe("r1");

    [Fact]
    public void The_same_text_as_a_json_string_is_read()
        => ToolInputResolver.ReadPayload(JsonSerializer.SerializeToElement(Request)).ShouldNotBeNull().GetProperty("requestId").GetString().ShouldBe("r1");

    [Fact]
    public void The_same_text_as_a_json_object_is_read()
    {
        using var document = JsonDocument.Parse(Request);

        ToolInputResolver.ReadPayload(document.RootElement.Clone()).ShouldNotBeNull().GetProperty("requestId").GetString().ShouldBe("r1");
    }

    [Theory]
    [InlineData("""{"status":"ok"}""")]
    [InlineData("""{"requestId":"r1"}""")]
    [InlineData("""{"status":1}""")]
    [InlineData("""["input_required"]""")]
    [InlineData("hello")]
    [InlineData("")]
    public void A_result_that_does_not_say_it_is_a_request_is_not_one(string result)
        => ToolInputResolver.ReadPayload(result).ShouldBeNull();

    [Fact]
    public void A_result_of_another_shape_is_not_a_request()
    {
        ToolInputResolver.ReadPayload(null).ShouldBeNull();
        ToolInputResolver.ReadPayload(42).ShouldBeNull();
        ToolInputResolver.ReadPayload(JsonSerializer.SerializeToElement(42)).ShouldBeNull();
    }
}
