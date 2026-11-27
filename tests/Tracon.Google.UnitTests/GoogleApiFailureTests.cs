namespace Tracon.Google.UnitTests;

/// <summary>
/// The one rule that tells the status name Gemini gives (<c>UNAVAILABLE</c>) from the HTTP reason phrase the SDK
/// writes in its place when the error body is empty (<c>Forbidden</c>).
/// </summary>
/// <remarks>
/// The chat and image error translation (as a provider error code) and the speech failure text (in brackets after the
/// status) read the name through the same helper, so the two cannot disagree about what a name is.
/// </remarks>
public sealed class GoogleApiFailureTests
{
    [Theory]
    [InlineData("UNAVAILABLE")]
    [InlineData("RESOURCE_EXHAUSTED")]
    [InlineData("INVALID_ARGUMENT")]
    public void A_status_name_of_upper_case_letters_and_underscores_is_kept(string name)
        => GoogleApiFailure.StatusName(name).ShouldBe(name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Forbidden")]
    [InlineData("Service Unavailable")]
    [InlineData("unavailable")]
    [InlineData("HTTP_503")]
    [InlineData("UNAVAILABLE ")]
    public void Anything_else_is_not_a_status_name(string? text)
        => GoogleApiFailure.StatusName(text).ShouldBeNull();

    [Fact]
    public void A_name_of_forty_characters_is_kept_and_one_of_forty_one_is_not()
    {
        GoogleApiFailure.StatusName(new string('A', 40)).ShouldBe(new string('A', 40));
        GoogleApiFailure.StatusName(new string('A', 41)).ShouldBeNull();
    }
}
