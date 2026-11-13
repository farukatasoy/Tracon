using Microsoft.Extensions.AI;
using Tracon.Google.UnitTests.Infrastructure;

#pragma warning disable MEAI001
namespace Tracon.Google.UnitTests;

/// <summary>
/// What the Gemini image adapter does with the options a caller sets, and the refusals that happen before the network.
/// </summary>
/// <remarks>
/// The answers are the shape <c>generateContent</c> gives for an image model: one part with inline bytes and the MIME
/// type the model chose.
/// </remarks>
public sealed class GoogleImageGeneratorTests
{
    private const string Model = "gemini-test-image";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public async Task A_media_type_is_refused_before_a_request_leaves_because_the_api_key_mode_rejects_every_value_of_it()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(Png, "image/png"));
        var generator = new GoogleImageGenerator(GoogleSpeechTestData.CreateClient(handler));

        var refusal = await Should.ThrowAsync<TraconException>(async () => await generator.GenerateAsync(
            new ImageGenerationRequest("a lighthouse"),
            new ImageGenerationOptions { ModelId = Model, MediaType = "image/jpeg" },
            TestContext.Current.CancellationToken));

        refusal.Message.ShouldContain("media type");
        refusal.Message.ShouldContain("mediaType", Case.Insensitive, "the message names the setting to leave out");
        handler.Requests.ShouldBeEmpty("the SDK would send the request and the service would answer with an error about outputMimeType");
    }

    [Fact]
    public async Task A_request_without_a_media_type_sends_no_image_config_and_returns_the_type_the_model_chose()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(Png, "image/png"));
        var generator = new GoogleImageGenerator(GoogleSpeechTestData.CreateClient(handler));

        var response = await generator.GenerateAsync(
            new ImageGenerationRequest("a lighthouse"),
            new ImageGenerationOptions { ModelId = Model },
            TestContext.Current.CancellationToken);

        var generation = handler.Requests.ShouldHaveSingleItem().Json.GetProperty("generationConfig");

        generation.GetProperty("responseModalities").EnumerateArray().Select(static m => m.GetString()).ShouldBe(["IMAGE"]);
        generation.TryGetProperty("imageConfig", out _).ShouldBeFalse();

        var image = response.Contents.OfType<DataContent>().ShouldHaveSingleItem();

        image.MediaType.ShouldBe("image/png");
        image.Data.ToArray().ShouldBe(Png);
    }
}
