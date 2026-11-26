using System.ClientModel;
using System.Drawing;
using Microsoft.Extensions.AI;
using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The OpenAI image generator is written over the client's pipeline, not
/// <c>ImageClient</c> (Native AOT). These tests pin the request and the
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
    public async Task The_model_of_the_call_replaces_the_one_the_generator_was_created_with()
    {
        await using var server = StubHttpServer.Respond(200, "OK", Answer);
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        await generator.GenerateAsync(
            new ImageGenerationRequest("a red kite"),
            new ImageGenerationOptions { ModelId = "gpt-image-1-mini" },
            TestContext.Current.CancellationToken);

        var body = await server.RequestBody;

        body.ShouldContain("\"model\":\"gpt-image-1-mini\"");
        body.ShouldNotContain("\"model\":\"gpt-image-1\"");
    }

    [Fact]
    public async Task A_call_that_names_no_model_uses_the_one_the_generator_was_created_with()
    {
        await using var server = StubHttpServer.Respond(200, "OK", Answer);
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        await generator.GenerateAsync(new ImageGenerationRequest("a red kite"), new ImageGenerationOptions { ModelId = "  " }, TestContext.Current.CancellationToken);

        (await server.RequestBody).ShouldContain("\"model\":\"gpt-image-1\"");
    }

    [Fact]
    public async Task The_quality_setting_is_sent_and_another_providers_quality_is_not()
    {
        await using var server = StubHttpServer.Respond(200, "OK", Answer);
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        await generator.GenerateAsync(
            new ImageGenerationRequest("a red kite"),
            new ImageGenerationOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["openai.quality"] = System.Text.Json.JsonSerializer.SerializeToElement("low"),
                    ["azure-openai.quality"] = System.Text.Json.JsonSerializer.SerializeToElement("high"),
                },
            },
            TestContext.Current.CancellationToken);

        var body = await server.RequestBody;

        body.ShouldContain("\"quality\":\"low\"");
        body.ShouldNotContain("high", Case.Sensitive);
    }

    [Fact]
    public async Task An_edit_posts_the_model_and_the_quality_of_the_call_in_its_form()
    {
        await using var server = StubHttpServer.Respond(200, "OK", Answer);
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        await generator.GenerateAsync(
            new ImageGenerationRequest("a red kite", [new DataContent(png, "image/png")]),
            new ImageGenerationOptions
            {
                ModelId = "gpt-image-1-mini",
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["openai.quality"] = System.Text.Json.JsonSerializer.SerializeToElement("medium"),
                    ["azure-openai.quality"] = System.Text.Json.JsonSerializer.SerializeToElement("high"),
                },
            },
            TestContext.Current.CancellationToken);

        (await server.RequestHead).ShouldStartWith("POST /v1/images/edits HTTP/1.1");

        var body = await server.RequestBody;

        body.ShouldContain("name=model", Case.Sensitive);
        body.ShouldContain("gpt-image-1-mini", Case.Sensitive);
        body.ShouldContain("name=quality", Case.Sensitive);
        body.ShouldContain("medium", Case.Sensitive);
        body.ShouldNotContain("high", Case.Sensitive);
    }

    [Fact]
    public async Task A_quality_that_is_not_text_is_refused_before_the_request_is_sent_and_the_message_names_no_value()
    {
        await using var server = StubHttpServer.Respond(200, "OK", Answer);
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        var failure = await Should.ThrowAsync<TraconException>(
            () => generator.GenerateAsync(
                new ImageGenerationRequest("a red kite"),
                new ImageGenerationOptions { AdditionalProperties = new AdditionalPropertiesDictionary { ["openai.quality"] = 4242 } },
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("openai.quality", Case.Sensitive);
        failure.Message.ShouldNotContain("4242", Case.Sensitive);
    }

    [Fact]
    public async Task An_error_answer_throws_the_client_library_exception()
    {
        await using var server = StubHttpServer.Respond(400, "Bad Request", """{"error":{"code":"invalid_size","message":"bad"}}""");
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        var exception = await Should.ThrowAsync<ClientResultException>(
            () => generator.GenerateAsync(new ImageGenerationRequest("x"), cancellationToken: TestContext.Current.CancellationToken));

        exception.Status.ShouldBe(400);

        // The client library's message carries the status and none of the body; the code the provider put in it travels beside it,
        // so the provider failure Tracon raises for it can name the code.
        exception.Data["tracon.provider-error-code"].ShouldBe("invalid_size");
        exception.Message.ShouldNotContain("invalid_size", Case.Sensitive);
    }

    [Theory]
    [InlineData("""{"error":{"code":null,"type":"image_generation_user_error","message":"x"}}""", "image_generation_user_error")]
    [InlineData("""{"error":{"code":"a sentence, not a code","message":"x"}}""", null)]
    [InlineData("""<html>502</html>""", null)]
    public async Task The_code_falls_back_to_the_type_and_a_body_with_none_adds_none(string body, string? expected)
    {
        await using var server = StubHttpServer.Respond(400, "Bad Request", body);
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");

        var exception = await Should.ThrowAsync<ClientResultException>(
            () => generator.GenerateAsync(new ImageGenerationRequest("x"), cancellationToken: TestContext.Current.CancellationToken));

        exception.Data["tracon.provider-error-code"].ShouldBe(expected);
    }

    [Fact]
    public async Task A_cancelled_caller_stops_the_call()
    {
        await using var server = StubHttpServer.NeverRespond();
        using var generator = Factory(server).CreateImageGenerator("gpt-image-1");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var call = generator.GenerateAsync(new ImageGenerationRequest("x"), cancellationToken: cancellation.Token);

        // The caller leaves once the server holds the request; the wait below only bounds a failure.
        await server.RequestHead.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => call.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken));
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
