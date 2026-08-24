namespace AgentPrism.Package.Tests.Infrastructure;

/// <summary>The result of running a subprocess.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Combined => StandardOutput + Environment.NewLine + StandardError;
}
