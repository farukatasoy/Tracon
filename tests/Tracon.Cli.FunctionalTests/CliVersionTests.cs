using System.Reflection;
using Tracon.Cli.FunctionalTests.Infrastructure;

namespace Tracon.Cli.FunctionalTests;

[Collection(nameof(CliTestGroup))]
public sealed class CliVersionTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public async Task Version_option_prints_the_package_version(string option)
    {
        var expected = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion
            .Split('+', 2)[0];

        var result = await CliRunner.RunAsync(option);

        result.ExitCode.ShouldBe(0);
        result.StandardError.ShouldBeEmpty();
        result.StandardOutput.Trim().ShouldBe(expected);
    }
}
