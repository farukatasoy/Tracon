using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// A consumer that touches the whole public registration, builder and option surface of the packed packages builds without a
/// warning under every strictness switch.
/// </summary>
/// <remarks>
/// <para>
/// The consumer is written by reflection over the shipped assemblies (<c>SurfaceGenerator/Program.cs</c>), not by hand: every
/// extension method of a Tracon assembly is called with arguments built from its signature, every public type with settable
/// members is constructed with all of them set, and every public interface is implemented. Nothing in it is run. The
/// strict build then reports what no consumer can fix in their own code: a diagnostic of an analyzer on a public shape, a
/// nullable annotation that makes the natural call a warning, an obsolete member, an experimental type in a signature, a
/// call the trim analyzer refuses on a package that promises Native AOT.
/// </para>
/// <para>
/// The Native AOT publish reaches every generated method from <c>Main</c> through a branch the compiler cannot decide, so
/// the trimmer and the ahead-of-time compiler look at the bodies behind the whole surface and not only at the call sites.
/// </para>
/// </remarks>
public sealed class PublicSurfaceConsumerTests(TemplateFixture fixture)
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    private static readonly Regex WarningLine = new(
        @"^.*: warning [A-Za-z]+\d+:",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// The whole surface compiles without a diagnostic. The pinned language version is the lowest the packages accept:
    /// the generated code uses no syntax newer than it, so a project on that version is one more reader of every package.
    /// </summary>
    /// <param name="kindName">The name of the <see cref="SurfaceKind"/>: the packages.</param>
    /// <param name="languageVersion">The <c>LangVersion</c> the project pins, or <see langword="null"/>.</param>
    [Theory]
    [InlineData("Aot", null)]
    [InlineData("Full", null)]
    [InlineData("Full", "9")]
    public async Task A_consumer_that_touches_the_whole_public_surface_builds_without_a_warning(string kindName, string? languageVersion)
    {
        var kind = Enum.Parse<SurfaceKind>(kindName);
        var surface = await PublicSurfaceProject.GenerateAsync(fixture.Version, kind);
        using var directory = new TempDirectory();

        await PublicSurfaceProject.WriteConsumerAsync(fixture.Version, directory.Path, kind, surface, languageVersion);

        var build = await ProcessRunner.RunAsync("dotnet", "build -c Release", directory.Path, BuildTimeout);

        build.ExitCode.ShouldBe(0, build.Combined);
        WarningLine.IsMatch(build.Combined).ShouldBeFalse(build.Combined);
    }

    /// <summary>
    /// Negative control: the members the AOT surface leaves out because they need unreferenced or dynamic code are a warning
    /// to call, and the consumer says so. Without this the clean AOT build above could mean the analyzers are not running.
    /// </summary>
    [Fact]
    public async Task A_call_of_a_member_that_needs_dynamic_code_is_a_diagnostic_in_the_AOT_consumer()
    {
        var surface = await PublicSurfaceProject.GenerateAsync(fixture.Version, SurfaceKind.AotWithContractCalls);
        using var directory = new TempDirectory();

        await PublicSurfaceProject.WriteConsumerAsync(fixture.Version, directory.Path, SurfaceKind.AotWithContractCalls, surface);

        var build = await ProcessRunner.RunAsync("dotnet", "build -c Release", directory.Path, BuildTimeout);

        build.ExitCode.ShouldNotBe(0, build.Combined);
        build.Combined.ShouldContain("IL2026", Case.Sensitive, build.Combined);
        build.Combined.ShouldContain("IL3050", Case.Sensitive, build.Combined);
    }

    /// <summary>
    /// The packages that promise Native AOT publish, with a diagnostic as an error, from a program that reaches the whole
    /// surface. A call-site check cannot see what the package does inside a method: this one does.
    /// </summary>
    [Fact]
    public async Task The_packages_that_promise_Native_AOT_publish_with_the_whole_surface_reachable()
    {
        var surface = await PublicSurfaceProject.GenerateAsync(fixture.Version, SurfaceKind.Aot);
        using var directory = new TempDirectory();

        await PublicSurfaceProject.WriteConsumerAsync(fixture.Version, directory.Path, SurfaceKind.Aot, surface);

        var publishDirectory = Path.Combine(directory.Path, "publish");

        // Restore and publish run as one command: a separate restore does not add the runtime-specific ILCompiler package.
        var publish = await ProcessRunner.RunAsync(
            "dotnet",
            $"publish -c Release -r {RuntimeInformation.RuntimeIdentifier} -p:PublishAot=true -o \"{publishDirectory}\"",
            directory.Path,
            BuildTimeout);

        publish.ExitCode.ShouldBe(0, publish.Combined);
        WarningLine.IsMatch(publish.Combined).ShouldBeFalse(publish.Combined);

        var executable = Path.Combine(publishDirectory, OperatingSystem.IsWindows() ? "Consumer.exe" : "Consumer");
        File.Exists(executable).ShouldBeTrue(publish.Combined);

        var run = await ProcessRunner.RunAsync(executable, string.Empty, directory.Path, TimeSpan.FromMinutes(1));

        run.ExitCode.ShouldBe(0, run.Combined);
    }

    /// <summary>
    /// What the generator could not write is a short, named list, and the surface it did write does not shrink. A member
    /// dropped for a new reason, or a package that silently stops being read, shows here before it shows as a missing check.
    /// </summary>
    [Fact]
    public async Task The_surface_the_generator_could_not_write_is_the_recorded_list_and_the_surface_it_wrote_does_not_shrink()
    {
        var aot = await PublicSurfaceProject.GenerateAsync(fixture.Version, SurfaceKind.Aot);
        var full = await PublicSurfaceProject.GenerateAsync(fixture.Version, SurfaceKind.Full);

        // The members marked as needing unreferenced or dynamic code: the warning on a call is their contract.
        aot.Lines.Where(static line => line.StartsWith("skipped", StringComparison.Ordinal)).ShouldBe(
            [
                "skipped entry: Tracon.TraconBuilderExtensions.AddTool(ITraconBuilder, Delegate, String, String, Action`1): marked as needing unreferenced or dynamic code: the warning on a call is the contract",
                "skipped entry: Tracon.TraconBuilderExtensions.AddToolsFrom(ITraconBuilder): marked as needing unreferenced or dynamic code: the warning on a call is the contract",
                "skipped entry: Tracon.TraconBuilderExtensions.AddToolsFrom(ITraconBuilder, Type): marked as needing unreferenced or dynamic code: the warning on a call is the contract",
            ],
            ignoreOrder: true);

        full.Lines.Where(static line => line.StartsWith("skipped", StringComparison.Ordinal)).ShouldBeEmpty();

        // Microsoft Agent Framework marks the loop types for evaluation only. A Tracon signature that names one hands
        // that diagnostic to the consumer, who has to suppress it; the guides say so for these four and for no other.
        // A new line here is a new signature of that kind.
        foreach (var surface in new[] { aot, full })
        {
            surface.Lines.Where(static line => line.StartsWith("exposed", StringComparison.Ordinal)).ShouldBe(
                [
                    "exposed MAAI001: Tracon.TraconBuilderExtensions.AddLoopEvaluator(ITraconBuilder, String, Func`2)",
                    "exposed MAAI001: Tracon.TraconBuilderExtensions.AddLoopEvaluator(ITraconBuilder, String, Func`2, String)",
                    "exposed MAAI001: Tracon.TraconBuilderExtensions.AddLoopEvaluator(ITraconBuilder, String, LoopEvaluator)",
                    "exposed MAAI001: Tracon.TraconBuilderExtensions.AddLoopEvaluator(ITraconBuilder, String, LoopEvaluator, String)",
                ],
                ignoreOrder: true);
        }

        // Floors, measured when the surface was first generated; a number below one is a package or a signature the
        // generator stopped seeing.
        aot.Counts["entry-aot"].ShouldBeGreaterThanOrEqualTo(86);
        aot.Counts["options-types"].ShouldBeGreaterThanOrEqualTo(288);
        aot.Counts["stub-interface"].ShouldBeGreaterThanOrEqualTo(87);
        full.Counts["entry-full"].ShouldBeGreaterThanOrEqualTo(131);
        full.Counts["options-types"].ShouldBeGreaterThanOrEqualTo(581);
        full.Counts["stub-interface"].ShouldBeGreaterThanOrEqualTo(87);
    }

    /// <summary>
    /// The command-line tool is not a library: it carries no compile asset, so no project can reference it and it has no
    /// public surface to build against. Should it ever carry one, it joins the generated surface.
    /// </summary>
    [Fact]
    public void The_command_line_tool_package_carries_no_compile_asset()
    {
        var package = Path.Combine(RepoPaths.PackageReleaseDirectory, $"Tracon.Cli.{fixture.Version}.nupkg");

        using var archive = ZipFile.OpenRead(package);
        var entries = archive.Entries.Select(static entry => entry.FullName).ToList();

        entries.ShouldNotContain(static entry => entry.StartsWith("lib/", StringComparison.Ordinal));
        entries.ShouldContain(static entry => entry.StartsWith("tools/", StringComparison.Ordinal));
    }
}
