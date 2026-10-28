namespace Tracon.Package.Tests.Infrastructure;

/// <summary>Builds and starts a project that <c>dotnet new tracon-api</c> generated.</summary>
/// <remarks>
/// The template tests that RUN the generated host (<see cref="TemplateRunTests"/>,
/// <see cref="FirstAgentPathTests"/>) share this one build-and-start path, so the
/// readiness handshake and the output lookup are not copied per test.
/// </remarks>
internal static class GeneratedHost
{
    /// <summary>The line ASP.NET Core writes once the host accepts connections.</summary>
    private const string ReadyLine = "Now listening on";

    /// <summary>Builds the project in <paramref name="directory"/> in Release and returns its main assembly.</summary>
    /// <param name="directory">The generated project's directory.</param>
    /// <param name="projectName">The project name, which is also the assembly name.</param>
    /// <returns>The path of <c>{projectName}.dll</c> in the build output.</returns>
    public static async Task<string> BuildAsync(string directory, string projectName)
    {
        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", directory, TimeSpan.FromMinutes(5));
        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);

        return Directory.EnumerateFiles(directory, $"{projectName}.dll", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains(Path.Combine("bin", "Release"), StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'{projectName}.dll' was not found in the build output.{Environment.NewLine}{buildResult.Combined}");
    }

    /// <summary>Starts the built host on a loopback port and waits until it accepts connections.</summary>
    /// <param name="assemblyPath">The assembly <see cref="BuildAsync"/> returned.</param>
    /// <param name="directory">The generated project's directory, the host's working directory.</param>
    /// <param name="port">The loopback port the host listens on.</param>
    /// <param name="environment">Extra environment variables, such as provider settings.</param>
    /// <returns>The running host. Disposing it kills the process tree.</returns>
    public static Task<ManagedProcess> StartAsync(
        string assemblyPath,
        string directory,
        int port,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
        };

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                variables[key] = value;
            }
        }

        return ProcessRunner.StartAsync(
            "dotnet",
            $"\"{assemblyPath}\"",
            ReadyLine,
            TimeSpan.FromSeconds(30),
            directory,
            variables);
    }
}
