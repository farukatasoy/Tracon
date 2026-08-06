namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>Bir alt surec calistirmasinin sonucu.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Combined => StandardOutput + Environment.NewLine + StandardError;
}
