using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The package family alignment check in isolation: which assemblies it reads,
/// what it treats as aligned, and what its refusal says. That the host
/// actually runs it, and stops before any other service starts, is proven at
/// the host boundary in <c>PackageFamilyAlignmentHostTests</c>.
/// </summary>
public sealed class PackageFamilyAlignmentTests
{
    private const string Current = "1.0.0-preview.3";
    private const string Previous = "1.0.0-preview.2";

    /// <summary>
    /// The family list is fixed in code, so a new library package would be
    /// invisible to the check until someone remembered to add it. The package
    /// set is read from <c>src/*/*.csproj</c> by the helper the release facts
    /// use: every library package except the HTTP client.
    /// </summary>
    [Fact]
    public void The_family_is_every_library_package_except_the_HTTP_client()
    {
        var expected = PackableProjects.Ids()
            .Where(id => PackableProjects.ProfileOf(id) == PackageProfile.Library)
            .Where(id => !string.Equals(id, "Tracon.Client", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        PackageFamilyAlignment.FamilyAssemblyNames.ShouldBe(expected);
    }

    [Fact]
    public void One_version_is_aligned()
    {
        PackageFamilyAlignment.Describe(
        [
            new("Tracon.Core", Current),
            new("Tracon.Abstractions", Current),
            new("Tracon.AspNetCore", Current),
        ]).ShouldBeNull();
    }

    [Fact]
    public void Only_the_core_loaded_is_aligned()
    {
        PackageFamilyAlignment.Describe([new("Tracon.Core", Current)]).ShouldBeNull();
    }

    [Fact]
    public void Nothing_loaded_is_aligned()
    {
        PackageFamilyAlignment.Describe([]).ShouldBeNull();
    }

    [Fact]
    public void Two_versions_are_refused_with_every_assembly_on_its_own_line_ordered_by_name()
    {
        var message = PackageFamilyAlignment.Describe(
        [
            new("Tracon.Core", Current),
            new("Tracon.AspNetCore", Previous),
            new("Tracon.Abstractions", Current),
        ]);

        message.ShouldNotBeNull();
        Lines(message).ShouldBe(
        [
            $"Tracon.Abstractions {Current}",
            $"Tracon.AspNetCore {Previous}",
            $"Tracon.Core {Current}",
        ]);
        message.ShouldContain("Reference every Tracon package at the same version");
    }

    /// <summary>
    /// Open question 1 (A): the same assembly name loaded twice, from two load
    /// contexts, at two versions. A plug-in carrying its own Tracon is not a
    /// supported graph, so it is refused like any other mix.
    /// </summary>
    [Fact]
    public void The_same_assembly_at_two_versions_is_refused_and_both_are_listed()
    {
        var message = PackageFamilyAlignment.Describe(
        [
            new("Tracon.Core", Current),
            new("Tracon.Core", Previous),
        ]);

        message.ShouldNotBeNull();
        Lines(message).ShouldBe([$"Tracon.Core {Previous}", $"Tracon.Core {Current}"]);
    }

    [Fact]
    public void The_same_assembly_loaded_twice_at_one_version_is_aligned()
    {
        PackageFamilyAlignment.Describe(
        [
            new("Tracon.Core", Current),
            new("Tracon.Core", Current),
        ]).ShouldBeNull();
    }

    /// <summary>Open question 2 (A): a version nobody can read never counts as aligned.</summary>
    [Fact]
    public void An_unknown_version_is_refused_even_when_it_is_the_only_one()
    {
        var message = PackageFamilyAlignment.Describe(
        [
            new("Tracon.Core", PackageFamilyAlignment.UnknownVersion),
            new("Tracon.Abstractions", PackageFamilyAlignment.UnknownVersion),
        ]);

        message.ShouldNotBeNull();
        Lines(message).ShouldBe(["Tracon.Abstractions unknown", "Tracon.Core unknown"]);
    }

    [Fact]
    public void Assemblies_outside_the_family_are_ignored_even_with_a_Tracon_prefix()
    {
        // This test assembly is named Tracon.Core.UnitTests: a prefix match would
        // put it in the family and report its version as a mix.
        var loaded = PackageFamilyAlignment.ReadLoaded(
            [typeof(PackageFamilyAlignmentTests).Assembly, typeof(object).Assembly, typeof(PackageFamilyAlignment).Assembly],
            static assembly => assembly == typeof(PackageFamilyAlignment).Assembly ? Current : Previous);

        loaded.ShouldBe([new LoadedFamilyAssembly("Tracon.Core", Current)]);
    }

    /// <summary>
    /// The real reader against the real process: the assemblies this test host
    /// loaded come from one build, so the check must find them and find them
    /// aligned. A false refusal here would stop every Tracon host.
    /// </summary>
    [Fact]
    public void The_family_loaded_by_this_test_process_is_found_and_aligned()
    {
        _ = typeof(TraconException).FullName; // Tracon.Abstractions
        var loaded = new LoadedPackageFamily().Read();

        var names = loaded.Select(assembly => assembly.Name).ToHashSet(StringComparer.Ordinal);

        names.Contains("Tracon.Core").ShouldBeTrue(string.Join(", ", names));
        names.Contains("Tracon.Abstractions").ShouldBeTrue(string.Join(", ", names));
        names.Contains("Tracon.Core.UnitTests").ShouldBeFalse(string.Join(", ", names));
        loaded.ShouldAllBe(assembly => !string.Equals(assembly.Version, PackageFamilyAlignment.UnknownVersion, StringComparison.Ordinal));
        PackageFamilyAlignment.Describe(loaded).ShouldBeNull();
    }

    /// <summary>
    /// The sibling helper the reader calls is missing on a mixed graph (a newer
    /// abstractions package that moved it): the read fails, the version becomes
    /// unknown, and the check still refuses with the assembly listed - it does
    /// not throw the reader's exception at the host.
    /// </summary>
    [Fact]
    public void A_reader_that_cannot_resolve_its_helper_fails_closed()
    {
        var loaded = PackageFamilyAlignment.ReadLoaded(
            [typeof(PackageFamilyAlignment).Assembly],
            static _ => throw new MissingMethodException("Tracon.AssemblyVersionText", "Read"));

        loaded.ShouldBe([new LoadedFamilyAssembly("Tracon.Core", PackageFamilyAlignment.UnknownVersion)]);

        var message = PackageFamilyAlignment.Describe(loaded);
        message.ShouldNotBeNull();
        Lines(message).ShouldBe(["Tracon.Core unknown"]);
    }

    [Fact]
    public void A_blank_version_is_unknown()
    {
        PackageFamilyAlignment.ReadLoaded([typeof(PackageFamilyAlignment).Assembly], static _ => " ")
            .ShouldBe([new LoadedFamilyAssembly("Tracon.Core", PackageFamilyAlignment.UnknownVersion)]);
    }

    [Fact]
    public void AddTracon_registers_the_check_once_even_when_called_twice()
    {
        var services = new ServiceCollection();
        services.AddTracon();
        services.AddTracon();

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(PackageFamilyAlignmentService)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(LoadedPackageFamily)).ShouldBe(1);
    }

    [Fact]
    public async Task A_mixed_family_stops_the_service_with_a_Tracon_exception()
    {
        var service = new PackageFamilyAlignmentService(new FixedFamily(
            new("Tracon.Core", Current),
            new("Tracon.AspNetCore", Previous)));

        var exception = await Should.ThrowAsync<TraconException>(() => service.StartingAsync(CancellationToken.None));

        Lines(exception.Message).ShouldBe([$"Tracon.AspNetCore {Previous}", $"Tracon.Core {Current}"]);
    }

    [Fact]
    public async Task An_aligned_family_starts_even_when_the_start_is_cancelled()
    {
        var service = new PackageFamilyAlignmentService(new FixedFamily(
            new("Tracon.Core", Current),
            new("Tracon.Abstractions", Current)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await service.StartingAsync(cancelled.Token);
        await service.StartAsync(cancelled.Token);
        await service.StartedAsync(cancelled.Token);
        await service.StoppingAsync(cancelled.Token);
        await service.StopAsync(cancelled.Token);
        await service.StoppedAsync(cancelled.Token);
    }

    private static readonly Regex AssemblyLine = new(
        @"^ {4}(?<line>\S+ \S+)\r?$",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static List<string> Lines(string message)
        => AssemblyLine.Matches(message).Select(match => match.Groups["line"].Value).ToList();

    private sealed class FixedFamily(params LoadedFamilyAssembly[] assemblies) : LoadedPackageFamily
    {
        internal override IReadOnlyList<LoadedFamilyAssembly> Read() => assemblies;
    }
}
