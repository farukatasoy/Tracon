using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tracon.Tests.Common;

/// <summary>
/// Proves that a multi-targeted test assembly ran on the runtime it was built for.
/// </summary>
/// <remarks>
/// Phase 183 runs a representative set of test projects on every framework the
/// packages ship. The whole point of the net8.0 leg is the net8.0 runtime: a leg
/// that rolled forward onto a newer runtime (<c>DOTNET_ROLL_FORWARD</c>, a
/// <c>RollForward</c> property) would pass every other test and prove nothing.
/// <c>tests/Directory.Build.props</c> links this file into every project of the set.
/// </remarks>
public sealed class RuntimeMatchesTargetFrameworkTests
{
    [Fact]
    public void The_test_assembly_runs_on_the_runtime_it_targets()
    {
        var target = typeof(RuntimeMatchesTargetFrameworkTests).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        target.ShouldNotBeNull("The test assembly carries no TargetFrameworkAttribute.");

        Environment.Version.Major.ShouldBe(
            new FrameworkName(target).Version.Major,
            $"Built for '{target}' but running on '{RuntimeInformation.FrameworkDescription}'.");
    }
}
