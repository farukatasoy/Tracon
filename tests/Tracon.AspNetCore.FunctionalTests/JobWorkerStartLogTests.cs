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

        StartLines(host).ShouldHaveSingleItem().ShouldContain("the lanes media, default, at most 2 at a time", Case.Sensitive);
    }

    /// <summary>
    /// A lane with a budget of its own is leased by its own pass and is not part of the shared pool: the line names
    /// both, the way a tick applies them, instead of saying "every lane" for a worker that does not lease every lane.
    /// </summary>
    [Fact]
    public async Task A_per_lane_budget_narrows_the_shared_pool_to_the_default_lane_and_is_named_on_its_own()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static tracon => tracon.UseScheduling(static options => options.MaxConcurrentJobsPerLane["media"] = 4));

        await WaitUntil.TrueAsync(() => StartLines(host).Count > 0, "the worker to say it started");

        var line = StartLines(host).ShouldHaveSingleItem();

        line.ShouldContain("the lanes default, at most 2 at a time; the lane media, at most 4 at a time", Case.Sensitive);
        line.ShouldNotContain("every lane", Case.Sensitive);
    }

    [Fact]
    public async Task A_worker_subscribed_to_no_lane_says_it_leases_from_none()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static tracon => tracon.UseScheduling(static options => options.Lanes = []));

        await WaitUntil.TrueAsync(() => StartLines(host).Count > 0, "the worker to say it started");

        var line = StartLines(host).ShouldHaveSingleItem();

        line.ShouldContain("no shared lane", Case.Sensitive);
        line.ShouldNotContain("every lane", Case.Sensitive);
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
