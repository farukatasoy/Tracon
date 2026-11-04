using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// The secrets a deployment passes to the host as environment variables (provider keys, the connection string, the
/// chain key, content protection key material, an OAuth client secret) are not in what a skill script can read of its
/// environment or print of it, on success or on failure.
/// </summary>
/// <remarks>
/// <para>
/// This is how most deployments carry a secret: <c>Anthropic__ApiKey</c> and <c>ConnectionStrings__Tracon</c> are
/// variables of the host's own process. The runner clears the environment of the child and gives it the names the
/// operator allows. The scripts here dump the whole environment every way a shell can (<c>env</c>, <c>printenv</c>,
/// <c>set</c>, <c>export -p</c>, <c>declare -x</c>), once with the script succeeding and once failing with the dump on
/// standard error, and the result is searched whole, serialized, the way it reaches the model and the run record.
/// </para>
/// <para>
/// A variable the operator DID allow reaches the script, and the test says so: a search that finds none of the
/// others proves nothing if the script could not have read any.
/// </para>
/// </remarks>
public sealed class SkillScriptEnvironmentCanaryTests : IDisposable
{
    private const string Allowed = "TRACON_CANARY_ALLOWED_VALUE";
    private const string AllowedValue = "canary-env-allowed-3d71b9e0";

    private static readonly string? BashPath = FindBash();

    // Name and the stem of its value. The names are the shapes a deployment really uses for its configuration.
    private static readonly (string Name, string Stem)[] Secrets =
    [
        ("Anthropic__ApiKey", "canary-env-anthropic-key"),
        ("Tracon__Providers__AzureOpenAI__ApiKey", "canary-env-azure-key"),
        ("Tracon__Providers__Google__ApiKey", "canary-env-google-key"),
        ("Tracon__Providers__OpenAI__ApiKey", "canary-env-openai-key"),
        ("Tracon__Voice__ApiKey", "canary-env-elevenlabs-key"),
        ("ConnectionStrings__Tracon", "canary-env-connection-password"),
        ("Tracon__AuditChainKeys__2026", "canary-env-chain-key"),
        ("ContentProtectionKeys__2026", "canary-env-protection-key"),
        ("Tracon__McpSecrets__OAuthClient", "canary-env-oauth-client-secret"),
        ("Tracon__AuthToken", "canary-env-static-token"),
        ("canary_lowercase_secret", "canary-env-lowercase-secret"),
    ];

    private readonly string _root = Directory.CreateTempSubdirectory("tracon-env-canary-").FullName;

    [Theory]
    [InlineData("env")]
    [InlineData("printenv")]
    [InlineData("set")]
    [InlineData("export -p")]
    [InlineData("declare -x")]
    public async Task A_script_that_prints_its_environment_prints_no_secret_of_the_host(string dump)
    {
        Assert.SkipWhen(BashPath is null, "bash not found.");

        using var seeded = Seed();

        var succeeded = await RunAsync(WriteScript("dump-ok.sh", dump));
        var failed = await RunAsync(WriteScript("dump-fail.sh", $"{dump} 1>&2\nexit 3"));

        succeeded.Succeeded.ShouldBeTrue();
        failed.Succeeded.ShouldBeFalse("the failing script has to fail, or the failure path was not driven");

        // The allowed variable reaches the script, so the dump was read by a process that could see the environment.
        succeeded.StandardOutput.ShouldContain(AllowedValue, Case.Sensitive);
        failed.StandardError.ShouldContain(AllowedValue, Case.Sensitive);

        var everything = JsonSerializer.Serialize(succeeded) + "\n" + JsonSerializer.Serialize(failed) + "\n" + succeeded.StandardOutput + failed.StandardError;

        foreach (var (name, stem) in Secrets)
        {
            everything.ShouldNotContain(stem, Case.Sensitive, $"the value of {name} reached the script's output");
            everything.ShouldNotContain(name, Case.Sensitive, $"the name {name} reached the script's output, so the variable was in the child's environment");
        }
    }

    [Fact]
    public async Task A_script_that_reads_the_variables_by_name_gets_nothing()
    {
        Assert.SkipWhen(BashPath is null, "bash not found.");

        using var seeded = Seed();

        var reads = string.Join('\n', Secrets.Select(static secret => $"echo \"[${{{secret.Name}}}]\""));
        var result = await RunAsync(WriteScript("read-by-name.sh", reads));

        result.Succeeded.ShouldBeTrue();

        foreach (var (name, stem) in Secrets)
        {
            result.StandardOutput.ShouldNotContain(stem, Case.Sensitive, $"the script read {name}");
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cleanup failure must not change the test result.
        }
    }

    private static string? FindBash()
    {
        foreach (var candidate in new[] { "/bin/bash", "/usr/bin/bash" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static EnvironmentSeed Seed()
    {
        var seed = new EnvironmentSeed();

        foreach (var (name, stem) in Secrets)
        {
            seed.Set(name, stem + "-a1b2c3d4");
        }

        seed.Set(Allowed, AllowedValue);

        return seed;
    }

    private string WriteScript(string name, string body)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, body + Environment.NewLine);

        return path;
    }

    private static async Task<SkillScriptExecutionResult> RunAsync(string scriptPath)
    {
        var traconOptions = new TraconOptions();
        var options = traconOptions.Skills.Scripts;
        options.Enabled = true;
        options.PlatformIsolationAcknowledged = true;
        options.Interpreters["sh"] = BashPath!;
        options.EnvironmentAllowList.Add(Allowed);

        var content = await File.ReadAllTextAsync(scriptPath);

        return await new SkillScriptProcessRunner(Options.Create(traconOptions)).ExecuteAsync(
            new SkillScriptExecutionRequest
            {
                TenantId = "default",
                SkillName = "test",
                ScriptName = Path.GetFileNameWithoutExtension(scriptPath),
                Origin = SkillScriptOrigin.File,
                Extension = "sh",
                Interpreter = BashPath!,
                Content = content,
                ContentSha256 = new string('0', 64),
                SourcePath = scriptPath,
                Timeout = options.Timeout,
                MaxOutputBytes = options.MaxOutputBytes,
            },
            CancellationToken.None);
    }

    /// <summary>Sets process environment variables and puts every one back as it was.</summary>
    private sealed class EnvironmentSeed : IDisposable
    {
        private readonly List<(string Name, string? Previous)> _previous = [];

        public void Set(string name, string value)
        {
            _previous.Add((name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _previous)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }
}
