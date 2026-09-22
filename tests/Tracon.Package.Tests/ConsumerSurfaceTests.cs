using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The antithesis of Phase 96's <c>internal</c> narrowing: for every type
/// pulled behind <c>internal</c> because "the consumer reaches it through a
/// public interface instead", this proves both halves of that claim from a
/// real <c>PackageReference</c> consumer - the interface resolves, the
/// implementation type does not.
/// </summary>
/// <remarks>
/// Measured cases: <c>Tracon.IRunStore</c> stays public, its default
/// implementation <c>Tracon.InMemoryRunStore</c> became <c>internal</c>
/// (Phase 96, item 96.6); <c>Tracon.IMigrationApplier</c> stays public, the
/// SQL providers' <c>Tracon.MigrationRunner</c> became <c>internal</c>
/// (Phase 182) - it had been public in all three provider packages under the
/// same name. Neither <see cref="ConsumerRunTests"/> nor
/// <see cref="LocalReferenceTests"/> covers this: they prove the SDK-level
/// consumer surface still works, not that a narrowed type actually stopped
/// being visible.
/// </remarks>
public sealed class ConsumerSurfaceTests(TemplateFixture fixture)
{
    [Theory]
    [InlineData("InMemoryRunStore")]
    [InlineData("MigrationRunner")]
    public async Task Internal_type_is_not_visible_to_a_package_consumer(string typeName)
    {
        using var dir = new TempDirectory();

        await SurfaceProbeProject.WriteAsync(fixture.Version, dir.Path, $$"""
            using Tracon;

            {{typeName}}? narrowed = null;
            Console.WriteLine(narrowed);
            """);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(2));

        buildResult.ExitCode.ShouldNotBe(0, buildResult.Combined);
        buildResult.Combined.ShouldContain("CS0122");
        buildResult.Combined.ShouldContain(typeName);
    }

    [Theory]
    [InlineData("IRunStore")]
    [InlineData("IMigrationApplier")]
    public async Task Corresponding_interface_is_visible_to_a_package_consumer(string interfaceName)
    {
        using var dir = new TempDirectory();

        await SurfaceProbeProject.WriteAsync(fixture.Version, dir.Path, $$"""
            using Tracon;

            {{interfaceName}}? seam = null;
            Console.WriteLine(seam is null);
            """);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(2));

        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);
    }
}
