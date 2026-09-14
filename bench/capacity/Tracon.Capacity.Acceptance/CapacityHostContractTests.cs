using System.Net;
using System.Text.Json;
using Tracon.Capacity;
using Tracon.CapacityDriver;

namespace Tracon.Capacity.Acceptance;

/// <summary>What the running host says about itself, read from the process rather than the profile.</summary>
public sealed class CapacityHostContractTests : IDisposable
{
    private readonly HttpClient _client = new();

    private HostProbe Probe => new(_client, AcceptanceEnvironment.BaseAddress);

    [Fact]
    public async Task The_host_reports_its_effective_scheduling_and_pool_settings()
    {
        var settings = await Probe.ReadSettingsAsync(CancellationToken.None);

        settings.Schema.ShouldBe(AcceptanceEnvironment.Schema);
        settings.ProcessId.ShouldBeGreaterThan(0);
        settings.MaxPoolSize.ShouldBeGreaterThan(0);

        // 🚨 The version the host actually loaded, not the one the profile
        // asked for. A mismatch would mean the measurement described other bytes.
        settings.TraconVersion.ShouldContain(AcceptanceEnvironment.PackageVersion.Split('+')[0]);
    }

    [Fact]
    public async Task The_counters_reset_so_a_warm_up_cannot_leak_into_a_measured_window()
    {
        await Probe.ResetAsync(CancellationToken.None);

        var telemetry = await Probe.ReadTelemetryAsync(CancellationToken.None);

        telemetry.ModelCalls.ShouldBe(0);
        telemetry.ModelTurns.ShouldBe(0);
        telemetry.ToolInvocations.ShouldBe(0);
    }

    [Fact]
    public async Task A_recording_failure_is_reported_rather_than_hidden_behind_a_green_run()
    {
        var telemetry = await Probe.ReadTelemetryAsync(CancellationToken.None);

        // Nothing has broken the store in this smoke, so the honest value is
        // zero - and the field exists so a real loss can never be silent.
        telemetry.RecordingFailures.ShouldBe(0);
    }

    [Fact]
    public async Task The_apparatus_surface_is_outside_Tracons_own_prefix()
    {
        var origin = new Uri(AcceptanceEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority);

        using var apparatus = await _client.GetAsync(new Uri(origin + CapacityContract.SettingsRoute));
        apparatus.StatusCode.ShouldBe(HttpStatusCode.OK);

        // And Tracon's own prefix does NOT serve it: the measurement surface is
        // never part of the product's HTTP contract.
        using var underPrefix = await _client.GetAsync(
            new Uri(AcceptanceEnvironment.BaseAddress + CapacityContract.SettingsRoute));
        underPrefix.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task The_execution_log_records_which_process_served_each_run()
    {
        var before = CapacityExecutionLog.ReadAll(AcceptanceEnvironment.ExecutionDirectory).Count;

        var correlation = "exec-" + Guid.NewGuid().ToString("N");
        var body = JsonSerializer.Serialize(new { message = CapacityPayload.RequestMessage(correlation, 256) });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(AcceptanceEnvironment.BaseAddress + "/api/agents/" + CapacityContract.AgentName + "/run"))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation(CapacityContract.TenantHeader, CapacityContract.TenantA);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", correlation);

        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var executions = CapacityExecutionLog.ReadAll(AcceptanceEnvironment.ExecutionDirectory);
        executions.Count.ShouldBeGreaterThan(before);

        var mine = executions.Single(e => string.Equals(e.Correlation, correlation, StringComparison.Ordinal));
        mine.Pid.ShouldBeGreaterThan(0);
        mine.Turns.ShouldBe(2);
        mine.ToolCalls.ShouldBe(1);
        mine.ModelMilliseconds.ShouldBeGreaterThan(0);
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
