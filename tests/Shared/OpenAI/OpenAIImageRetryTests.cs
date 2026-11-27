using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using Tracon.Tests.Common;

namespace Tracon.OpenAIShared.Tests;

/// <summary>
/// An image call is billed, and the client sends it again on a failure it considers worth repeating. The ceiling of the call
/// (<c>tracon.maxRetries</c> among the options' additional properties) lowers how often, for the image call alone.
/// </summary>
/// <remarks>
/// <para>
/// The generator runs over an <see cref="OpenAIClient"/> whose transport answers from a script and counts the requests that
/// reach it, so what is asserted is the number of requests a server would see. The wait between two tries runs on a
/// <see cref="ManualClock"/>: nothing here waits for a backoff or measures one.
/// </para>
/// <para>
/// Linked into <c>Tracon.OpenAI.UnitTests</c> and <c>Tracon.Azure.UnitTests</c>: the generator and the policy are compiled
/// into both packages, and the Azure package gives its client the same policy with no count of its own.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
public sealed class OpenAIImageRetryTests
{
    private const string Answer = """{"created":1,"data":[{"b64_json":"aGk="}]}""";

    private static readonly Uri Endpoint = new("http://provider.invalid/v1/");

    [Theory]
    [InlineData(null, 4)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(10, 4)]
    public async Task A_failing_image_call_is_sent_once_and_then_as_often_as_its_ceiling_allows(int? ceiling, int expectedRequests)
    {
        var clock = new ManualClock();
        var transport = new Script((503, "{}"));
        var generator = Generator(transport, clock, clientMaxRetries: null);

        await Should.ThrowAsync<ClientResultException>(() => clock.AdvanceUntilAsync(Generate(generator, ceiling)));

        transport.Calls.ShouldBe(expectedRequests);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A_ceiling_of_zero_sends_the_call_once_whatever_the_status_the_client_would_have_repeated(int status)
    {
        var clock = new ManualClock();
        var transport = new Script((status, "{}"));

        await Should.ThrowAsync<ClientResultException>(
            () => clock.AdvanceUntilAsync(Generate(Generator(transport, clock, clientMaxRetries: null), ceiling: 0)));

        transport.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_ceiling_above_the_count_of_the_client_changes_nothing()
    {
        var clock = new ManualClock();
        var transport = new Script((503, "{}"));

        await Should.ThrowAsync<ClientResultException>(
            () => clock.AdvanceUntilAsync(Generate(Generator(transport, clock, clientMaxRetries: 1), ceiling: 5)));

        transport.Calls.ShouldBe(2, "the client's own count of one retry is the lower of the two");
    }

    [Fact]
    public async Task A_call_that_succeeds_within_its_ceiling_returns_the_image()
    {
        var clock = new ManualClock();
        var transport = new Script((503, "{}"), (200, Answer));

        var call = Generate(Generator(transport, clock, clientMaxRetries: null), ceiling: 1);
        await clock.AdvanceUntilAsync(call);

        (await call).Contents.OfType<DataContent>().ShouldHaveSingleItem();
        transport.Calls.ShouldBe(2, "the first try failed and the one retry the ceiling allows answered");
    }

    [Fact]
    public async Task A_call_with_no_ceiling_keeps_the_count_of_the_client()
    {
        var clock = new ManualClock();
        var transport = new Script((503, "{}"));

        await Should.ThrowAsync<ClientResultException>(
            () => clock.AdvanceUntilAsync(Generate(Generator(transport, clock, clientMaxRetries: 2), ceiling: null)));

        transport.Calls.ShouldBe(3, "one try and the client's two retries");
    }

    [Fact]
    public async Task A_chat_call_on_the_same_client_keeps_the_count_of_the_client_after_an_image_call_was_capped()
    {
        var clock = new ManualClock();
        var transport = new Script((503, "{}"));
        var client = Client(transport, clock, clientMaxRetries: null);
        var generator = new OpenAIPipelineImageGenerator(client, Endpoint, "gpt-image-1", "openai");

        await Should.ThrowAsync<ClientResultException>(() => clock.AdvanceUntilAsync(Generate(generator, ceiling: 0)));

        transport.Calls.ShouldBe(1, "the image call is capped");

        // The chat clients of the same OpenAI client send through the same pipeline: a message with no ceiling.
        var message = client.Pipeline.CreateMessage(new Uri(Endpoint, "chat/completions"), "POST");
        await clock.AdvanceUntilAsync(client.Pipeline.SendAsync(message).AsTask());

        transport.Calls.ShouldBe(1 + 4, "a chat call is still sent the first time and the client's three retries");
    }

    [Fact]
    public async Task An_edit_is_capped_like_a_generation_and_goes_to_the_edit_path()
    {
        var clock = new ManualClock();
        var transport = new Script((503, "{}"));
        var generator = Generator(transport, clock, clientMaxRetries: null);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        await Should.ThrowAsync<ClientResultException>(
            () => clock.AdvanceUntilAsync(
                generator.GenerateAsync(
                    new ImageGenerationRequest("a red kite", [new DataContent(png, "image/png")]),
                    Options(ceiling: 1),
                    TestContext.Current.CancellationToken)));

        transport.Calls.ShouldBe(2);
        transport.Paths.ShouldAllBe(path => path.EndsWith("/images/edits", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_status_the_client_does_not_repeat_is_sent_once_with_or_without_a_ceiling()
    {
        var clock = new ManualClock();
        var transport = new Script((400, """{"error":{"code":"invalid_size","message":"bad"}}"""));

        await Should.ThrowAsync<ClientResultException>(
            () => clock.AdvanceUntilAsync(Generate(Generator(transport, clock, clientMaxRetries: null), ceiling: 5)));

        transport.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task A_ceiling_out_of_range_is_refused_before_the_request_is_sent(int ceiling)
    {
        var transport = new Script((200, Answer));
        var generator = Generator(transport, new ManualClock(), clientMaxRetries: null);

        var failure = await Should.ThrowAsync<TraconException>(() => Generate(generator, ceiling));

        failure.Message.ShouldContain(ImageProviderSettingKeys.MaxRetriesProperty, Case.Sensitive);
        failure.Message.ShouldNotContain(ceiling.ToString(System.Globalization.CultureInfo.InvariantCulture), Case.Sensitive);
        transport.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("two")]
    [InlineData(1.5)]
    [InlineData(true)]
    public async Task A_ceiling_that_is_not_a_whole_number_is_refused_and_the_message_names_no_value(object value)
    {
        var transport = new Script((200, Answer));
        var generator = Generator(transport, new ManualClock(), clientMaxRetries: null);

        var failure = await Should.ThrowAsync<TraconException>(
            () => generator.GenerateAsync(
                new ImageGenerationRequest("a red kite"),
                new ImageGenerationOptions { AdditionalProperties = new AdditionalPropertiesDictionary { [ImageProviderSettingKeys.MaxRetriesProperty] = value } },
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(ImageProviderSettingKeys.MaxRetriesProperty, Case.Sensitive);
        failure.Message.ShouldNotContain(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, Case.Sensitive);
        transport.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_ceiling_that_arrives_as_a_json_number_is_read()
    {
        var clock = new ManualClock();
        var transport = new Script((503, "{}"));
        var generator = Generator(transport, clock, clientMaxRetries: null);

        await Should.ThrowAsync<ClientResultException>(
            () => clock.AdvanceUntilAsync(
                generator.GenerateAsync(
                    new ImageGenerationRequest("a red kite"),
                    new ImageGenerationOptions
                    {
                        AdditionalProperties = new AdditionalPropertiesDictionary
                        {
                            [ImageProviderSettingKeys.MaxRetriesProperty] = JsonSerializer.SerializeToElement(1),
                        },
                    },
                    TestContext.Current.CancellationToken)));

        transport.Calls.ShouldBe(2);
    }

    [Fact]
    public void The_bound_of_the_options_is_the_bound_of_the_policy()
    {
        // The options type that carries the ceiling lives in Tracon.Core and the policy that applies it in the shared source:
        // two numbers that must be one.
        ImageProviderSettingKeys.MaximumMaxRetries.ShouldBe(OpenAIBoundedRetryPolicy.MaximumMaxRetries);
    }

    private static OpenAIPipelineImageGenerator Generator(Script transport, TimeProvider clock, int? clientMaxRetries)
        => new(Client(transport, clock, clientMaxRetries), Endpoint, "gpt-image-1", "openai");

    private static OpenAIClient Client(Script transport, TimeProvider clock, int? clientMaxRetries)
        => new(
            new ApiKeyCredential("test-api-key-1234567890"),
            new OpenAIClientOptions
            {
                Endpoint = Endpoint,
                RetryPolicy = new OpenAIBoundedRetryPolicy(clock, clientMaxRetries),
                Transport = new HttpClientPipelineTransport(new HttpClient(transport)),
            });

    private static Task<ImageGenerationResponse> Generate(OpenAIPipelineImageGenerator generator, int? ceiling)
        => generator.GenerateAsync(new ImageGenerationRequest("a red kite"), Options(ceiling), TestContext.Current.CancellationToken);

    private static ImageGenerationOptions Options(int? ceiling)
        => new()
        {
            AdditionalProperties = ceiling is { } value
                ? new AdditionalPropertiesDictionary { [ImageProviderSettingKeys.MaxRetriesProperty] = value }
                : null,
        };

    /// <summary>A transport that answers from a list, repeating the last answer when the list runs out.</summary>
    private sealed class Script(params (int Status, string Body)[] answers) : HttpMessageHandler
    {
        private readonly List<string> _paths = [];
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<string> Paths
        {
            get
            {
                lock (_paths)
                {
                    return [.. _paths];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_paths)
            {
                _paths.Add(request.RequestUri!.AbsolutePath);
            }

            var (status, body) = answers[Math.Min(Interlocked.Increment(ref _calls), answers.Length) - 1];

            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        }
    }
}
#pragma warning restore MEAI001
