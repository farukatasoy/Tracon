namespace Tracon;

/// <summary>Resolves <see cref="RunJudgeSet"/> while the host starts.</summary>
internal sealed class RunJudgeValidationService(RunJudgeSet judgeSet) : ITraconStartupCheck
{
    /// <inheritdoc />
    public StartupCheckKind Kind => StartupCheckKind.Gate;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = judgeSet;
        return Task.CompletedTask;
    }
}
