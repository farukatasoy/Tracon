using System.ClientModel;
using System.Drawing;
using Microsoft.Extensions.AI;
using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The OpenAI image generator is written over the client's pipeline, not
/// <c>ImageClient</c> (Native AOT, F-584). These tests pin the request and the
/// response mapping that <c>ImageClient.AsIImageGenerator()</c> used to provide.
/// </summary>
#pragma warning disable MEAI001
public sealed class OpenAIImageWireTests
{
    private const string Answer =
        """{"created":1,"data":[{"b64_json":"aGk="}],"output_format":"webp","usage":{"input_tokens":5,"output_tokens":10,"total_tokens":15,"input_tokens_details":{"image_tokens":1,"text_tokens":4}}}""";

    [Fact]
    public async Task A_generation_posts_the_model_count_size_and_format_and_maps_the_answer()
    {
        await using var server = StubHttpServer.Respond(200, "OK", Answer);
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        var response = await generator.GenerateAsync(
            new ImageGenerationRequest("a red kite"),
            new ImageGenerationOptions { Count = 2, ImageSize = new Size(1024, 1536), MediaType = "image/webp" },
            TestContext.Current.CancellationToken);

        var head = await server.RequestHead;
        head.ShouldStartWith("POST /v1/images/generations HTTP/1.1");
        head.ShouldContain($"Authorization: Bearer {TestData.ApiKey}");

        var body = await server.RequestBody;
        body.ShouldContain("\"model\":\"gpt-image-1\"");
        body.ShouldContain("\"prompt\":\"a red kite\"");
        body.ShouldContain("\"n\":2");
        body.ShouldContain("\"size\":\"1024x1536\"");
        body.ShouldContain("\"output_format\":\"webp\"");

        // gpt-image-1 rejects response_format: it is sent only when the caller asks for one.
        body.ShouldNotContain("response_format");

        var image = response.Contents.OfType<DataContent>().ShouldHaveSingleItem();
        image.MediaType.ShouldBe("image/webp");
        image.Data.ToArray().ShouldBe("hi"u8.ToArray());

        response.Usage.ShouldNotBeNull();
        response.Usage.InputTokenCount.ShouldBe(5);
        response.Usage.OutputTokenCount.ShouldBe(10);
        response.Usage.TotalTokenCount.ShouldBe(15);
        response.Usage.AdditionalCounts!["InputTokenDetails.ImageTokenCount"].ShouldBe(1);
    }

    [Fact]
    public async Task An_error_answer_throws_the_client_library_exception()
    {
        await using var server = StubHttpServer.Respond(400, "Bad Request", """{"error":{"code":"invalid_size","message":"bad"}}""");
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        var exception = await Should.ThrowAsync<ClientResultException>(
            () => generator.GenerateAsync(new ImageGenerationRequest("x"), cancellationToken: TestContext.Current.CancellationToken));

        exception.Status.ShouldBe(400);
    }

    [Fact]
    public async Task A_cancelled_caller_stops_the_call()
    {
        await using var server = StubHttpServer.NeverRespond();
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(
            () => generator.GenerateAsync(new ImageGenerationRequest("x"), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void The_metadata_names_the_provider_and_the_model()
    {
        using var generator = new OpenAIChatClientFactory(TestData.Options()).CreateImageGenerator("gpt-image-1");

        var metadata = generator.GetService<ImageGeneratorMetadata>().ShouldNotBeNull();
        metadata.ProviderName.ShouldBe(OpenAIProviderNames.ChatCompletions);
        metadata.DefaultModelId.ShouldBe("gpt-image-1");
    }

    private static OpenAIChatClientFactory Factory(StubHttpServer server)
        => new(TestData.Options(options => options.Endpoint = new Uri(server.BaseAddress, "v1/")));
}
#pragma warning restore MEAI001
