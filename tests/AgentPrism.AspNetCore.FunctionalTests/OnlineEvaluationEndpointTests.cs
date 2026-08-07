using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Cevrimici degerlendirme uclarinin testleri (Faz 49).</summary>
public sealed class OnlineEvaluationEndpointTests
{
    [Fact]
    public async Task Elle_puanlama_ornetlemeyi_atlar_ve_puan_yazar()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IRunJudge>(new StubJudge(77, "iyi")));

        var runId = await SeedScorableRunAsync(host);

        using var response = await host.Client.PostAsync(JudgeUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("kind").GetString().ShouldBe("Numeric");
        body[0].GetProperty("value").GetInt32().ShouldBe(77);
        body[0].GetProperty("source").GetString().ShouldBe("judge:stub");

        // Yazilan puan, insan geri bildirim ucunda da gorunur (ayni tablo).
        using var feedback = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{runId}/feedback", UriKind.Relative));
        (await AgentPrismTestHost.ReadJsonAsync(feedback)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Olmayan_calistirma_elle_puanlanmaya_calisilinca_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IRunJudge>(new StubJudge(77, "iyi")));

        using var response = await host.Client.PostAsync(JudgeUri(AgentPrismId.NewId()), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Kayitli_yargic_yoksa_bos_liste_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedScorableRunAsync(host);

        using var response = await host.Client.PostAsync(JudgeUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Ozet_ucu_bos_pencerede_sifir_ornek_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/evaluation/online", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("sampleCount").GetInt64().ShouldBe(0);
        body.GetProperty("belowThreshold").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Elle_puanlama_sonrasi_ozet_orneği_gosterir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IRunJudge>(new StubJudge(20, "kotu")));

        var runId = await SeedScorableRunAsync(host);
        await host.Client.PostAsync(JudgeUri(runId), content: null);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/evaluation/online", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("sampleCount").GetInt64().ShouldBe(1);
        body.GetProperty("averageScore").GetDouble().ShouldBe(20);
    }

    private static Uri JudgeUri(Guid runId) => new($"/agentprism/api/runs/{runId}/judge", UriKind.Relative);

    private static async Task<Guid> SeedScorableRunAsync(AgentPrismTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var runId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = now,
            TenantId = "default",
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = "default",
            Messages = [new ChatMessage(ChatRole.User, "soru")],
            CreatedAt = now,
        });

        await runs.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.MessageCompleted,
            Timestamp = now,
            Text = "cevap",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = now,
        });

        return runId;
    }

    private sealed class StubJudge(int score, string reason) : IRunJudge
    {
        public string Name => "stub";

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => new(new RunJudgment { Score = score, Reason = reason });
    }
}
