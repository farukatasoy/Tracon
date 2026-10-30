namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The classification the span store filter uses when <c>RecordSensitiveData</c> is off.
/// </summary>
public sealed class SensitiveSpanAttributesTests
{
    [Theory]
    [InlineData("gen_ai.input.messages")]
    [InlineData("gen_ai.output.messages")]
    [InlineData("gen_ai.system_instructions")]
    [InlineData("gen_ai.tool.call.arguments")]
    [InlineData("gen_ai.tool.call.result")]
    [InlineData("executor.input")]
    [InlineData("executor.output")]
    [InlineData("message.content")]
    [InlineData("error.message")]
    [InlineData("gen_ai.prompt")]
    [InlineData("gen_ai.completion")]
    public void A_content_key_is_dropped(string key)
        => SensitiveSpanAttributes.IsContent(key).ShouldBeTrue();

    [Theory]
    [InlineData("gen_ai.usage.input_tokens")]
    [InlineData("gen_ai.usage.output_tokens")]
    [InlineData("gen_ai.request.model")]
    [InlineData("gen_ai.operation.name")]
    [InlineData("gen_ai.tool.definitions")]
    [InlineData("gen_ai.tool.name")]
    [InlineData("tracon.run.id")]
    [InlineData("tracon.tenant.id")]
    public void A_cost_or_metadata_key_is_kept(string key)
    {
        // 🚨 A filter on the words "input" and "output" would drop the token counts
        // above, and the cost view is built from them.
        SensitiveSpanAttributes.IsContent(key).ShouldBeFalse();
    }

    [Fact]
    public void The_comparison_ignores_case()
        => SensitiveSpanAttributes.IsContent("GEN_AI.SYSTEM_INSTRUCTIONS").ShouldBeTrue();
}
