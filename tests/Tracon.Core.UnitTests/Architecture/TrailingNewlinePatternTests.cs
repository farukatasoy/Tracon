namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// A name that ends in a line feed is not a valid name, for every whole-value validator in Abstractions and Core
/// (phase 260, F-756).
/// </summary>
/// <remarks>
/// <para>
/// The pattern of each of these validators was written <c>^…$</c>, and .NET's <c>$</c> also matches before a
/// trailing <c>\n</c>. <see cref="AnchoredPatternTests"/> keeps the SHAPE from coming back; these cases prove the
/// BEHAVIOR of the five validators this package can reach. The tenant id (AspNetCore) is covered over HTTP in
/// <c>TenantValueFormatTests</c> and the MCP name in <c>McpToolNamingTests</c>.
/// </para>
/// <para>
/// Each case pairs the bad value with the same value without the line feed, so a validator that rejects
/// everything cannot pass.
/// </para>
/// </remarks>
public sealed class TrailingNewlinePatternTests
{
    [Theory]
    [InlineData("contoso.preview")]
    [InlineData("a")]
    public void A_custom_event_type_is_valid_without_the_line_feed_and_not_with_it(string type)
    {
        RunEventCustomTypes.IsValidType(type).ShouldBeTrue();
        RunEventCustomTypes.IsValidType(type + "\n").ShouldBeFalse();
        RunEventCustomTypes.IsValidType(type + "\r\n").ShouldBeFalse();
    }

    [Theory]
    [InlineData("my-handler")]
    [InlineData("a")]
    public void A_job_handler_key_is_valid_without_the_line_feed_and_not_with_it(string key)
    {
        JobHandlerKeys.IsValidKey(key).ShouldBeTrue();
        JobHandlerKeys.IsValidKey(key + "\n").ShouldBeFalse();
        JobHandlerKeys.IsValidKey(key + "\r\n").ShouldBeFalse();
    }

    [Theory]
    [InlineData("media")]
    [InlineData("a")]
    public void A_job_lane_name_is_valid_without_the_line_feed_and_not_with_it(string name)
    {
        JobLanes.IsValidName(name).ShouldBeTrue();
        JobLanes.IsValidName(name + "\n").ShouldBeFalse();
        JobLanes.IsValidName(name + "\r\n").ShouldBeFalse();
    }

    [Theory]
    [InlineData("docs")]
    [InlineData("Product_Manuals-2026")]
    public void A_knowledge_collection_name_is_accepted_without_the_line_feed_and_refused_with_it(string collection)
    {
        Should.NotThrow(() => KnowledgeIngestionService.RequireValidCollectionName(collection));
        Should.Throw<ArgumentException>(() => KnowledgeIngestionService.RequireValidCollectionName(collection + "\n"));
        Should.Throw<ArgumentException>(() => KnowledgeIngestionService.RequireValidCollectionName(collection + "\r\n"));
    }
}
