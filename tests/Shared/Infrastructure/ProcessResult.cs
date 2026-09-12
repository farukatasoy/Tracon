namespace Tracon.Tests.Common;

/// <summary>The result of running a subprocess to completion.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Combined => StandardOutput + Environment.NewLine + StandardError;
}
