using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Onceden yanit semasi bildirmeyen on bir <c>Task&lt;IResult&gt;</c> ucunun artik
/// en az bir sema bildirdigini dogrular (Faz 40, bolum 40.2).
/// </summary>
/// <remarks>
/// Faz 40 oncesi olculen durum: bu on bir uc <c>responses</c> altinda hicbir
/// giris tasimiyordu (bkz. <c>docs/40-OPENAPI-YAYINI.md</c>, "Bugun ne calismiyor").
/// </remarks>
public sealed class OpenApiResponseSchemaTests
{
    /// <summary>Faz 40 oncesi hicbir yanit semasi bildirmeyen on bir ucun operationId'leri.</summary>
    public static TheoryData<string> HamTaskIResultOperationIds { get; } = new()
    {
        "AgentPrismRunAgent",
        "AgentPrismDownloadAttachment",
        "AgentPrismRunWorkflow",
        "AgentPrismResumeWorkflow",
        "AgentPrismRespondWorkflowRequest",
        "AgentPrismOpenAIResponses",
        "AgentPrismOpenAIChatCompletions",
        "AgentPrismOpenAICreateConversation",
        "AgentPrismOpenAIGetConversation",
        "AgentPrismOpenAIDeleteConversation",
        "AgentPrismOpenAIListConversationItems",
    };

    [Fact]
    public async Task Belgedeki_her_uc_en_az_bir_yanit_semasi_bildirir()
    {
        var document = await FetchDocumentAsync();

        var missing = new List<string>();

        foreach (var (path, method, operation) in OpenApiTestHelpers.EnumerateOperations(document))
        {
            if (!operation.TryGetProperty("responses", out var responses) ||
                !responses.EnumerateObject().Any())
            {
                missing.Add($"{method.ToUpperInvariant()} {path}");
            }
        }

        missing.ShouldBeEmpty(customMessage: $"Yanit semasi olmayan uclar: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(HamTaskIResultOperationIds))]
    public async Task Onceden_sema_uretmeyen_uc_artik_en_az_bir_sema_bildirir(string operationId)
    {
        var document = await FetchDocumentAsync();

        var operation = OpenApiTestHelpers.FindByOperationId(document, operationId);

        var responses = operation.GetProperty("responses");

        responses.EnumerateObject().Count().ShouldBeGreaterThan(
            0, customMessage: $"'{operationId}' hala hicbir yanit semasi bildirmiyor.");
    }

    [Fact]
    public async Task Agent_run_ucu_hem_SSE_hem_ProblemDetails_hatalarini_bildirir()
    {
        var document = await FetchDocumentAsync();

        var operation = OpenApiTestHelpers.FindByOperationId(document, "AgentPrismRunAgent");
        var responses = operation.GetProperty("responses");

        responses.GetProperty("200").GetProperty("content").TryGetProperty("text/event-stream", out _)
            .ShouldBeTrue("Basari yaniti SSE olarak bildirilmeli.");

        foreach (var status in new[] { "400", "404", "429" })
        {
            responses.TryGetProperty(status, out var problemResponse).ShouldBeTrue(
                $"'{status}' yaniti bildirilmemis.");

            problemResponse.GetProperty("content").TryGetProperty("application/problem+json", out _)
                .ShouldBeTrue($"'{status}' yaniti ProblemDetails olarak bildirilmemis.");
        }
    }

    [Fact]
    public async Task Responses_ucu_hem_JSON_hem_SSE_basari_yanitini_bildirir()
    {
        var document = await FetchDocumentAsync();

        var operation = OpenApiTestHelpers.FindByOperationId(document, "AgentPrismOpenAIResponses");
        var content = operation.GetProperty("responses").GetProperty("200").GetProperty("content");

        content.TryGetProperty("application/json", out _)
            .ShouldBeTrue("Akissiz JSON yaniti bildirilmemis.");

        content.TryGetProperty("text/event-stream", out _)
            .ShouldBeTrue("Akisli SSE yaniti bildirilmemis.");
    }

    private static async Task<System.Text.Json.JsonElement> FetchDocumentAsync()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        return await AgentPrismTestHost.ReadJsonAsync(response);
    }
}
