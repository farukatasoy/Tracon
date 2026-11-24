using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A process that starts the job worker says so once. The worker is on by default, so a host that connects to a shared
/// SQL database without a <c>Tracon:Scheduling</c> section joins that database's queue; the line is where an operator
/// reads it.
/// </summary>
public sealed class JobWorkerStartLogTests
{
    private const string Fragment = "The job worker runs in this process";

    [Fact]
    public async Task A_host_with_the_default_scheduling_settings_logs_one_line_that_names_the_defaults_and_the_way_out()
    {
        await using var host = await TraconTestHost.StartAsync();

        await WaitUntil.TrueAsync(() => StartLines(host).Count > 0, "the worker to say it started");

        var line = StartLines(host).ShouldHaveSingleItem();

        line.ShouldStartWith("Information ", Case.Sensitive);
        line.ShouldContain("every lane", Case.Sensitive);
        line.ShouldContain("at most 2 at a time", Case.Sensitive);
        line.ShouldContain("every 00:00:10", Case.Sensitive);
        line.ShouldContain("Tracon:Scheduling:RunWorker", Case.Sensitive);
    }

    [Fact]
    public async Task The_lanes_of_a_dedicated_worker_are_named()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static tracon => tracon.UseScheduling(static options => options.Lanes = ["media", "default"]));

        await WaitUntil.TrueAsync(() => StartLines(host).Count > 0, "the worker to say it started");

        StartLines(host).ShouldHaveSingleItem().ShouldContain("the lanes media, default", Case.Sensitive);
    }

    /// <summary>
    /// With the worker off nothing is built and nothing is said: the idle setup makes no background call, and a line
    /// that claims a worker runs would be false.
    /// </summary>
    [Fact]
    public async Task A_host_that_keeps_the_worker_off_logs_nothing_about_it()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static tracon => tracon.UseScheduling(static options => options.RunWorker = false));

        var worker = host.Services.GetServices<IHostedService>().OfType<GatedLoop<JobWorkerBackgroundService>>().Single();

        worker.Loop.ShouldBeNull();
        StartLines(host).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_host_with_the_whole_subsystem_off_logs_nothing_about_it_either()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static tracon => tracon.UseScheduling(static options => options.Enabled = false));

        StartLines(host).ShouldBeEmpty();
    }

    private static List<string> StartLines(TraconTestHost host)
        => [.. host.Logs.Entries.Where(static entry => entry.Contains(Fragment, StringComparison.Ordinal))];
}
