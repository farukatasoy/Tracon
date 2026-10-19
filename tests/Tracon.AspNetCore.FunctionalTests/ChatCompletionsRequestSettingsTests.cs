using System.Net;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A request setting the agent definition owns is refused on
/// <c>/v1/chat/completions</c>, as <c>/v1/responses</c> already refuses it (F-581).
/// </summary>
/// <remarks>
/// <para>
/// Measured before the fix (2026-10-19): the endpoint read only <c>model</c>,
/// <c>metadata.entity_id</c>, <c>messages</c> and <c>stream</c>. A client that
/// sent <c>temperature</c>, <c>tools</c>, <c>response_format</c> or <c>n: 3</c>
/// got <c>200</c> and an answer that ignored them: no JSON, one choice, no
/// client tool. <c>/v1/responses</c> refused the same settings with <c>400</c>
/// through Microsoft Agent Framework's default (<c>RejectRequestSettings</c>),
/// and MAF's own Chat Completions host refuses them too.
/// </para>
/// <para>
/// A field that asks for the default (<c>n: 1</c>, a <c>text</c> response
/// format, <c>logprobs: false</c>, no tools) changes nothing and is accepted. So
/// is a field that carries no setting the agent uses (<c>user</c>,
/// <c>stream_options</c>, <c>store</c>).
/// </para>
/// </remarks>
public sealed class ChatCompletionsRequestSettingsTests
{
    private const string AgentName = "settings-agent";
    private static readonly Uri ChatCompletions = new("/tracon/v1/chat/completions", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);
    private static readonly Uri Conversations = new("/tracon/v1/conversations", UriKind.Relative);

    [Theory]
    [InlineData("temperature", "0.2")]
    [InlineData("top_p", "0.9")]
    [InlineData("max_tokens", "50")]
    [InlineData("max_completion_tokens", "50")]
    [InlineData("frequency_penalty", "0.5")]
    [InlineData("presence_penalty", "0.5")]
    [InlineData("seed", "7")]
    [InlineData("stop", "[\"END\"]")]
    [InlineData("logit_bias", "{\"50256\":-100}")]
    [InlineData("reasoning_effort", "\"low\"")]
    [InlineData("response_format", "{\"type\":\"json_object\"}")]
    [InlineData("tools", "[{\"type\":\"function\",\"function\":{\"name\":\"f\",\"parameters\":{}}}]")]
    [InlineData("tool_choice", "\"auto\"")]
    [InlineData("functions", "[{\"name\":\"f\",\"parameters\":{}}]")]
    [InlineData("function_call", "\"auto\"")]
    [InlineData("n", "3")]
    [InlineData("logprobs", "true")]
    [InlineData("top_logprobs", "2")]
    [InlineData("modalities", "[\"text\",\"audio\"]")]
    [InlineData("audio", "{\"voice\":\"alloy\",\"format\":\"mp3\"}")]
    [InlineData("web_search_options", "{}")]
    public async Task A_setting_the_agent_owns_is_refused_by_name(string field, string value)
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostAsync(host, ChatCompletions, $$$"""
            {"model":"{{{AgentName}}}","messages":[{"role":"user","content":"hi"}],"{{{field}}}":{{{value}}}}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var error = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("error");
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain(field);
        error.GetProperty("type").GetString().ShouldBe("invalid_request_error");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_refused_setting_is_named_at_once()
    {
        await using var host = await StartAsync(Provider());

        using var response = await PostAsync(host, ChatCompletions, $$$"""
            {"model":"{{{AgentName}}}","messages":[{"role":"user","content":"hi"}],"temperature":0.1,"tools":[{"type":"function","function":{"name":"f"}}]}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var message = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("error").GetProperty("message").GetString()!;
        message.ShouldContain("temperature");
        message.ShouldContain("tools");
    }

    [Fact]
    public async Task Defaults_and_fields_without_an_agent_setting_are_accepted()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostAsync(host, ChatCompletions, $$$"""
            {"model":"{{{AgentName}}}","messages":[{"role":"user","content":"hi"}],
             "n":1,"response_format":{"type":"text"},"logprobs":false,"tools":[],"modalities":["text"],
             "temperature":null,"user":"u-1","store":false,"stream":true,"stream_options":{"include_usage":true}}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_responses_endpoint_refuses_the_same_kind_of_setting()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostAsync(host, Responses, $$$"""
            {"model":"{{{AgentName}}}","input":"hi","temperature":0.2}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("error").GetProperty("message").GetString()
            .ShouldNotBeNull().ShouldContain("temperature");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_conversation_created_with_initial_items_is_refused_not_emptied()
    {
        await using var host = await StartAsync(Provider());

        using var refused = await PostAsync(host, Conversations, """
            {"items":[{"type":"message","role":"user","content":"hi"}]}
            """);
        using var empty = await PostAsync(host, Conversations, """
            {"items":[],"metadata":{"topic":"orders"}}
            """);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(refused)).GetProperty("error").GetProperty("message").GetString()
            .ShouldNotBeNull().ShouldContain("items");
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static FakeModelProvider Provider() => new FakeModelProvider("settings").RespondsWith("ok");

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgent(TestData.Definition(AgentName) with { Model = new ModelBinding { Provider = "settings", Model = "settings-1" } }));

    private static Task<HttpResponseMessage> PostAsync(TraconTestHost host, Uri uri, string json)
        => host.Client.PostAsync(
            uri,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
}
