using System.Diagnostics;
using System.Text;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// The password in the connection string a command-line tool is given reaches no output of the tool when the tool
/// connects: not standard output, not standard error, in text or in JSON, whether the string came on the command line or
/// from the environment.
/// </summary>
/// <remarks>
/// <para>
/// The other canary of the tool runs its connection commands (<c>migrate</c>, <c>migrate status</c>, <c>state-check</c>)
/// against an address nothing listens on, so only the driver's failure text is read. This one runs them against a real
/// container, with the password of a login of that server, where the tool succeeds: a connection that is made is the
/// proof that the string was used, and the output of a success is a text of its own.
/// </para>
/// <para>
/// The tool runs as a process of its own, started from the build output of the tool's project, so what is read is what an
/// operator's terminal receives: no console redirection stands between the tool and the test, and the password is in the
/// environment of the child process only when the case says so.
/// </para>
/// </remarks>
public abstract class CliConnectedSecretCanaryAcceptanceContract
{
    private const string PasswordStem = "canary-cli-connected-password";

    /// <summary>Gets the <c>--provider</c> value of the server under test.</summary>
    protected abstract string Provider { get; }

    /// <summary>Gets a schema on the class's server, owned by a login with the given password.</summary>
    /// <param name="password">The password of the login.</param>
    /// <returns>The database the tool connects to.</returns>
    protected abstract Task<TwoNodeDatabase> NewDatabaseAsync(string password);

    /// <summary>
    /// The tool connects, migrates, reports and checks, with the connection string on the command line and then from the
    /// environment, and the password is in none of what it printed.
    /// </summary>
    /// <remarks>
    /// One login and one schema serve both halves: the tool has no schema option, so it uses the default one, and a second
    /// login would find that schema owned by the first.
    /// </remarks>
    [Fact]
    public async Task The_connection_password_is_in_no_output_of_a_command_that_connects()
    {
        var database = await NewDatabaseAsync($"{PasswordStem}-3f8a1c64");
        var connection = database.ConnectionString;

        connection.ShouldContain(PasswordStem, Case.Sensitive, "the connection string does not carry the canary password, so there is nothing to search for");

        var outputs = new StringBuilder();

        async Task<CliProcessResult> Run(bool fromEnvironment, params string[] words)
        {
            var args = fromEnvironment
                ? [.. words, "--provider", Provider]
                : (string[])[.. words, "--provider", Provider, "--connection", connection];

            var plain = await CliProcess.RunAsync(args, fromEnvironment ? connection : null);
            var json = await CliProcess.RunAsync([.. args, "--json"], fromEnvironment ? connection : null);

            outputs.AppendLine((fromEnvironment ? "environment: " : "command line: ") + string.Join(' ', words) + "\n" + plain.Combined + "\n" + json.Combined);
            TestContext.Current.SendDiagnosticMessage($"cli {Provider} {(fromEnvironment ? "env" : "arg")} '{string.Join(' ', words)}': exit {plain.ExitCode}, stdout '{plain.StandardOutput.Trim().ReplaceLineEndings(" | ")[..Math.Min(120, plain.StandardOutput.Trim().Length)]}'");

            return plain;
        }

        foreach (var fromEnvironment in (bool[])[false, true])
        {
            var status = await Run(fromEnvironment, "migrate", "status");
            status.ExitCode.ShouldBe(0, "migrate status did not connect: " + status.Combined);

            var migrated = await Run(fromEnvironment, "migrate");
            migrated.ExitCode.ShouldBe(0, "migrate did not connect: " + migrated.Combined);
            migrated.StandardOutput.ShouldEndWith(" applied" + Environment.NewLine, Case.Sensitive);

            if (!fromEnvironment)
            {
                // The first run on a new schema applies migrations; the second has nothing left to apply.
                migrated.StandardOutput.ShouldNotStartWith("0 applied", Case.Sensitive, "the schema was not new, so the later steps prove nothing");
            }
            else
            {
                migrated.StandardOutput.ShouldStartWith("0 applied", Case.Sensitive);
            }

            var after = await Run(fromEnvironment, "migrate", "status");
            after.ExitCode.ShouldBe(0, after.Combined);
            after.StandardOutput.ShouldStartWith("0 pending", Case.Sensitive);

            var checkedState = await Run(fromEnvironment, "state-check");
            checkedState.ExitCode.ShouldBe(0, "state-check did not connect: " + checkedState.Combined);
            checkedState.StandardOutput.ShouldContain("Provider:", Case.Sensitive);
        }

        SecretHits.Find(
                [new("standard output and standard error", outputs.ToString())],
                [new("connection password", PasswordStem)])
            .ShouldBeEmpty();
    }
}

/// <summary>What a command-line tool process answered with.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="StandardOutput">Everything written to standard output.</param>
/// <param name="StandardError">Everything written to standard error.</param>
internal sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Combined => StandardOutput + StandardError;
}

/// <summary>Runs the command-line tool as a process, from the build output of its project.</summary>
internal static class CliProcess
{
    /// <summary>Runs the tool.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="connectionFromEnvironment">The value of the connection variable of the child process; <see langword="null"/> leaves it unset.</param>
    /// <returns>The exit code and what the tool wrote.</returns>
    public static async Task<CliProcessResult> RunAsync(string[] args, string? connectionFromEnvironment)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Name;
        var assembly = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Tracon.Cli", configuration, "Tracon.Cli.dll"));

        File.Exists(assembly).ShouldBeTrue($"the tool is not built at {assembly}");

        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        info.ArgumentList.Add(assembly);

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment.Remove("TRACON_CONNECTION");
        info.Environment.Remove("TRACON_TOKEN");

        if (connectionFromEnvironment is not null)
        {
            info.Environment["TRACON_CONNECTION"] = connectionFromEnvironment;
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("the tool did not start");
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);

        return new CliProcessResult(process.ExitCode, await output, await error);
    }
}
