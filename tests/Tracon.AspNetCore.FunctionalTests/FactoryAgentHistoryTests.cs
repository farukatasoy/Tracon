using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the console and the OpenAI-compatible Conversations endpoint show for a
/// factory agent, with and without Tracon's <see cref="ChatHistoryProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// The "any MAF feature" guide tells a factory author to hand the agent the
/// <see cref="ChatHistoryProvider"/> from DI. This class is the measurement behind
/// that sentence, taken on both storage modes (2026-10-19, Microsoft.Agents.AI 1.x):
/// the turn shows on both surfaces whenever the agent uses the DI provider. Without
/// it the turn shows only on the in-memory default, whose provider happens to match
/// MAF's own; on SQL storage both surfaces are empty, because they read through the
/// DI provider and the agent wrote somewhere else.
/// </para>
/// <para>
/// The third case of the theory and the second test pin MAF's default on purpose. If a
/// MAF upgrade changes it, the guide's two sentences about the history provider are
/// stale and these are the signal to change the guide.
/// </para>
/// </remarks>
public sealed class FactoryAgentHistoryTests
{
    private static readonly Uri Conversations = new("/tracon/v1/conversations", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_factory_agent_shows_its_turn_on_both_surfaces(bool sqlStorage, bool passDiProvider)
    {
        using var database = new TempSqliteDatabase("factory-history-visible");

        await using var host = await StartAsync(database, sqlStorage, passDiProvider);

        var conversationId = await RunAsync(host, "hello factory");

        var items = await ItemTextsAsync(host, conversationId);
        items.ShouldContain("hello factory", StringComparer.Ordinal);
        items.ShouldContain("Echo: hello factory", StringComparer.Ordinal);

        var consoleMessages = await ConsoleMessagesAsync(host, conversationId);
        consoleMessages.ShouldContain(static message => message.Contains("hello factory", StringComparison.Ordinal));
        consoleMessages.ShouldContain(static message => message.Contains("Echo: hello factory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_factory_agent_without_the_DI_history_provider_is_invisible_on_SQL_storage()
    {
        using var database = new TempSqliteDatabase("factory-history-default");

        await using var host = await StartAsync(database, sqlStorage: true, passDiProvider: false);

        var conversationId = await RunAsync(host, "hello default");

        (await ItemTextsAsync(host, conversationId)).ShouldBeEmpty();
        (await ConsoleMessagesAsync(host, conversationId)).ShouldBeEmpty();
    }

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase database, bool sqlStorage, bool passDiProvider)
        => TraconTestHost.StartAsync(builder =>
        {
            if (sqlStorage)
            {
                builder.UseSqlite(database.ConnectionString);
            }

            builder.AddAgent(
                "history-agent",
                services => services.GetRequiredService<IModelProviderRegistry>()
                    .CreateChatClient(TestData.Model())
                    .AsAIAgent(new ChatClientAgentOptions
                    {
                        Name = "history-agent",
                        ChatOptions = new ChatOptions { Instructions = "Reply briefly." },
                        ChatHistoryProvider = passDiProvider ? services.GetService<ChatHistoryProvider>() : null,
                    }));
        });

    private static async Task<string> RunAsync(TraconTestHost host, string message)
    {
        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }, TestContext.Current.CancellationToken))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var run = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "history-agent", conversation = conversationId, input = message },
            TestContext.Current.CancellationToken);

        run.EnsureSuccessStatusCode();

        return conversationId;
    }

    private static async Task<List<string>> ItemTextsAsync(TraconTestHost host, string conversationId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}/items", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await TraconTestHost.ReadJsonAsync(response);

        return
        [
            .. json.GetProperty("data").EnumerateArray()
                .SelectMany(static item => item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                    ? content.EnumerateArray()
                    : [])
                .Select(static part => part.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty),
        ];
    }

    private static async Task<List<string>> ConsoleMessagesAsync(TraconTestHost host, string conversationId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/sessions/{conversationId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await TraconTestHost.ReadJsonAsync(response);

        return json.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array
            ? [.. messages.EnumerateArray().Select(static message => message.GetRawText())]
            : [];
    }
}
