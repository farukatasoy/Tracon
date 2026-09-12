namespace Tracon.Cli.FunctionalTests.Infrastructure;

/// <summary>
/// Invokes <see cref="Program.Main"/> in-process, capturing stdout/stderr.
/// </summary>
/// <remarks>
/// <c>Console.SetOut</c>/<c>SetError</c> are process-wide, so every test that
/// uses this MUST be in <see cref="CliCollection"/> - it forces the tests
/// that call it to run one at a time, the way they would if
/// <c>tracon</c> ran as a genuinely separate process each time.
/// </remarks>
internal static class CliRunner
{
    public static async Task<CliResult> RunAsync(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;

        using var outWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        Console.SetOut(outWriter);
        Console.SetError(errorWriter);

        try
        {
            var exitCode = await Program.Main(args).ConfigureAwait(false);
            return new CliResult(exitCode, outWriter.ToString(), errorWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Combined => StandardOutput + StandardError;
}

/// <summary>Serializes every test that calls <see cref="CliRunner"/> (process-wide console redirection).</summary>
[CollectionDefinition(nameof(CliTestGroup), DisableParallelization = true)]
public sealed class CliTestGroup;
