using System.Globalization;
using System.Runtime.CompilerServices;

namespace Tracon.Tests.Common;

/// <summary>
/// Records every faulted task the host lets go without observing it, so a test run
/// can fail on it.
/// </summary>
/// <remarks>
/// <para>
/// .NET does not crash the process on <see cref="TaskScheduler.UnobservedTaskException"/>;
/// the exception vanishes. In a product that is a lost failure: a background step
/// that threw and nobody awaited. A passing run said nothing about it until this
/// guard, because only two tests listened and each listened for one marker.
/// </para>
/// <para>
/// The event fires on the finalizer thread when the collector reclaims the task, so
/// it cannot be tied to the test that dropped it. The guard reports once, for the
/// whole run, with the exceptions themselves:
/// <see cref="UnobservedTaskPipeline"/> forces the collection while the runtime is
/// still fully alive, and a <c>ProcessExit</c> handler prints the report and turns
/// the exit code red.
/// </para>
/// <para>
/// 🚨 Three shapes were tried before this one. An assembly fixture that throws in
/// <c>DisposeAsync</c> reports "Test Assembly Cleanup Failure" once for EVERY test of
/// the assembly (42 tests, 42 failures); a leak in a 10 000-test assembly would bury
/// the one message that matters. A forced collection inside <c>ProcessExit</c> never
/// saw the leak (the finalizer thread is gone by then). An exception thrown from the
/// pipeline's <c>StopAsync</c> is swallowed and the run stays green.
/// </para>
/// <para>
/// Linked into every test project by <c>tests/Directory.Build.props</c>. A test that
/// provokes an unobserved fault on purpose claims it with <see cref="Take"/>.
/// </para>
/// </remarks>
internal static class UnobservedTaskGuard
{
    /// <summary>The exit code of the Microsoft Testing Platform for "tests failed".</summary>
    private const int TestsFailedExitCode = 2;

    private static readonly Lock Gate = new();
    private static readonly List<Exception> Seen = [];
    private static IReadOnlyList<Exception> _leaked = [];

    /// <summary>Subscribes before the first test of the assembly runs.</summary>
    [ModuleInitializer]
    internal static void Install()
    {
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => ReportAtExit();
    }

    /// <summary>
    /// Removes and returns the recorded exceptions that <paramref name="match"/> accepts.
    /// </summary>
    /// <param name="match">Selects the exceptions the caller claims.</param>
    internal static IReadOnlyList<Exception> Take(Func<Exception, bool> match)
    {
        lock (Gate)
        {
            var taken = Seen.Where(match).ToList();
            Seen.RemoveAll(taken.Contains);

            return taken;
        }
    }

    /// <summary>Forces the collections that make a dropped task report.</summary>
    internal static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// Collects once more at the end of the run and keeps what is left for the exit report.
    /// </summary>
    internal static void SettleAtEndOfRun()
    {
        ForceCollection();
        _leaked = Take(static _ => true);
    }

    private static void ReportAtExit()
    {
        var left = _leaked;

        if (left.Count == 0)
        {
            return;
        }

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{left.Count} faulted task(s) were dropped without being observed during this test run:{Environment.NewLine}" +
            $"{string.Join(Environment.NewLine, left.Select(static exception => exception.ToString()))}"));

        if (Environment.ExitCode == 0)
        {
            Environment.ExitCode = TestsFailedExitCode;
        }
    }

    private static void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        lock (Gate)
        {
            Seen.AddRange(args.Exception.Flatten().InnerExceptions);
        }
    }
}
