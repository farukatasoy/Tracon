using Microsoft.Extensions.Hosting;

namespace AgentPrism;

/// <summary>Resolves <see cref="RunJudgeSet"/> while the host starts.</summary>
internal sealed class RunJudgeValidationService(RunJudgeSet judgeSet) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = judgeSet;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
