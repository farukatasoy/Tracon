using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every hosted service the packages ship is named here and has a kind. A new one fails this test until
/// somebody decides which kind it is, and a tick loop also names the test file that drives it.
/// </summary>
/// <remarks>
/// <para>
/// A hosted service runs outside any request, so nothing else exercises its failure: an exception in a loop
/// ends the work for the life of the process (and, with the framework default, stops the host), a loop with no
/// wait between failures spins a core, and a stop that never returns holds the shutdown. The kinds say what a
/// service owes:
/// </para>
/// <list type="bullet">
/// <item><description><c>TickLoop</c> - work on a timer. Survives a failing call, calls again only on the next
/// tick, and stops in bounded time. Proven in the file the entry names.</description></item>
/// <item><description><c>Lifecycle</c> - acts when the host stops (drain, close sessions). Bounded, and one
/// failing item does not stop the rest.</description></item>
/// <item><description><c>StartupGate</c> - checks the configuration at start and refuses to start on purpose.</description></item>
/// <item><description><c>StartupAdvisory</c> - writes a log line at start. It must never stop the host.</description></item>
/// </list>
/// <para>
/// The scan reads the package assemblies from the test output folder, so a service in a package this project
/// does not reference by code is still found. The list is closed in both directions: a service that disappears
/// fails too, so the list cannot go stale.
/// </para>
/// </remarks>
public sealed class HostedServiceInventoryTests
{
    private static readonly Dictionary<string, (Kind Kind, string? LoopTest)> Known = new(StringComparer.Ordinal)
    {
        ["Tracon.JobWorkerBackgroundService"] = (Kind.TickLoop, "JobWorkerBackgroundServiceTests.cs"),
        ["Tracon.RunHeartbeatWriter"] = (Kind.TickLoop, "HostedTickLoopTests.cs"),
        ["Tracon.RunReconciliationService"] = (Kind.TickLoop, "HostedTickLoopTests.cs"),
        ["Tracon.CanaryEvaluationService"] = (Kind.TickLoop, "HostedTickLoopTests.cs"),
        ["Tracon.ApprovalExpirationService"] = (Kind.TickLoop, "HostedTickLoopTests.cs"),
        ["Tracon.ModelProviderHealthBackgroundService"] = (Kind.TickLoop, "HostedTickLoopTests.cs"),
        ["Tracon.JobQueueDepthObserver"] = (Kind.TickLoop, "HostedTickLoopTests.cs"),
        ["Tracon.QuotaUsageObserver"] = (Kind.TickLoop, "HostedTickLoopTests.cs"),
        ["Tracon.WorkflowRequestExpirationService"] = (Kind.TickLoop, "WorkflowRequestExpirationLoopTests.cs"),
        ["Tracon.McpDiscoveryService"] = (Kind.TickLoop, "McpDiscoveryLoopTests.cs"),

        ["Tracon.TraconDrainService"] = (Kind.Lifecycle, "TraconDrainServiceTests.cs"),
        ["Tracon.LiveVoiceShutdownService"] = (Kind.Lifecycle, "LiveVoiceShutdownFaultTests.cs"),

        ["Tracon.ToolRegistrationValidationService"] = (Kind.StartupGate, null),
        ["Tracon.JobHandlerRegistryValidator"] = (Kind.StartupGate, null),
        ["Tracon.AgentSourceValidationService"] = (Kind.StartupGate, null),
        ["Tracon.AuditChainKeyStartupValidator"] = (Kind.StartupGate, null),
        ["Tracon.ProductionProfileValidator"] = (Kind.StartupGate, null),
        ["Tracon.RequiredBindingValidator"] = (Kind.StartupGate, null),
        ["Tracon.PackageFamilyAlignmentService"] = (Kind.StartupGate, null),
        ["Tracon.RunJudgeValidationService"] = (Kind.StartupGate, null),
        ["Tracon.MigrationHostedService"] = (Kind.StartupGate, null),

        ["Tracon.UnpricedModelWarningService"] = (Kind.StartupAdvisory, null),
        ["Tracon.NonPersistentStorageWarningService"] = (Kind.StartupAdvisory, null),
        ["Tracon.SilentGapWarningService"] = (Kind.StartupAdvisory, null),
        ["Tracon.PreservedStoreRegistrationWarningService"] = (Kind.StartupAdvisory, null),
        ["Tracon.OpenAISpeechDefaultWarning"] = (Kind.StartupAdvisory, null),
    };

    private enum Kind
    {
        TickLoop,
        Lifecycle,
        StartupGate,
        StartupAdvisory,
    }

    [Fact]
    public void Every_hosted_service_of_the_packages_has_a_kind_and_the_list_has_no_stale_entry()
    {
        var actual = FindHostedServices();

        // The scan reads files; if it read none, an empty list would pass the "no unknown" check below.
        actual.Count.ShouldBeGreaterThan(20, "the package assemblies must have been scanned");

        var unknown = actual.Except(Known.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var gone = Known.Keys.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "A hosted service was added. Decide its kind and add it to Known: a TickLoop names the test file that " +
            "makes its call fail and hang, and proves it neither dies, spins, nor holds the stop.");
        gone.ShouldBeEmpty("A known hosted service is no longer in the packages; remove it from Known.");
    }

    [Fact]
    public void Every_tick_loop_and_lifecycle_service_names_a_test_file_that_mentions_it()
    {
        var testFiles = Directory
            .EnumerateFiles(Path.Combine(RepoRoot.Path, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var problems = new List<string>();

        foreach (var (name, (kind, loopTest)) in Known)
        {
            if (kind is Kind.StartupGate or Kind.StartupAdvisory)
            {
                loopTest.ShouldBeNull($"{name} is a start-up service; it has no loop test to name");

                continue;
            }

            var typeName = name[(name.LastIndexOf('.') + 1)..];
            var match = testFiles.Where(path => string.Equals(Path.GetFileName(path), loopTest, StringComparison.Ordinal)).ToList();

            if (match.Count != 1)
            {
                problems.Add($"{name}: expected one test file named '{loopTest}', found {match.Count}");

                continue;
            }

            if (!File.ReadAllText(match[0]).Contains(typeName, StringComparison.Ordinal))
            {
                problems.Add($"{name}: '{loopTest}' never mentions '{typeName}'");
            }
        }

        problems.ShouldBeEmpty();
    }

    private static List<string> FindHostedServices()
    {
        var services = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Tracon.*.dll"))
        {
            var file = Path.GetFileNameWithoutExtension(path);

            if (file.EndsWith("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            Assembly assembly;

            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException)
            {
                continue;
            }

            foreach (var type in TypesOf(assembly))
            {
                if (type is { IsAbstract: false, IsInterface: false } && typeof(IHostedService).IsAssignableFrom(type))
                {
                    services.Add(type.FullName!);
                }
            }
        }

        return [.. services];
    }

    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
