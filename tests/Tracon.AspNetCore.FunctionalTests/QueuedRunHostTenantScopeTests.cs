using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A queued run's tool reads the application's own tenant, and it is the tenant of the run.
/// </summary>
/// <remarks>
/// <para>
/// The job worker opens Tracon's own tenant scope for the job, and the application's own scope (the current tenant of
/// its multi-tenant framework) stays as it was: a tool that reads the application's tenant-filtered data read no
/// tenant. <see cref="IHostTenantScope"/> is the worker's call into the application; these cases run the whole path:
/// the HTTP request, the queue, the worker and the tool.
/// </para>
/// <para>
/// Two runs of two tenants are held at the same moment by a barrier the tools wait on, so a scope that leaked
/// across jobs shows as a wrong tenant and not as a lucky order.
/// </para>
/// </remarks>
public sealed class QueuedRunHostTenantScopeTests
{
    private const string ProviderName = "host-scope-provider";
    private const string ModelName = "host-scope-model";
    private const string ToolName = "read_host_tenant";

    [Fact]
    public async Task A_queued_runs_tool_sees_the_hosts_tenant_scope_of_its_own_tenant_while_two_tenants_run_at_once()
    {
        var seen = new ConcurrentBag<(string? Run, string? Host)>();
        var barrier = new TwoAtOnce();

        await using var host = await StartAsync(seen, barrier, registerScope: true);

        await QueueAsync(host, "tenant-a");
        await QueueAsync(host, "tenant-b");

        await WaitUntil.TrueAsync(() => seen.Count >= 2, "both tools to read the host's tenant");

        seen.OrderBy(static pair => pair.Run, StringComparer.Ordinal)
            .ShouldBe([("tenant-a", "tenant-a"), ("tenant-b", "tenant-b")]);
    }

    [Fact]
    public async Task Without_a_registration_the_tool_sees_no_host_tenant_as_before()
    {
        var seen = new ConcurrentBag<(string? Run, string? Host)>();
        var barrier = new TwoAtOnce();

        await using var host = await StartAsync(seen, barrier, registerScope: false);

        await QueueAsync(host, "tenant-a");
        await QueueAsync(host, "tenant-b");

        await WaitUntil.TrueAsync(() => seen.Count >= 2, "both tools to run");

        seen.Select(static pair => pair.Host).ShouldAllBe(static tenant => tenant == null);
    }

    private static async Task<TraconTestHost> StartAsync(
        ConcurrentBag<(string? Run, string? Host)> seen,
        TwoAtOnce barrier,
        bool registerScope)
        => await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(
                    new FakeModelProvider(ProviderName)
                        .WithModel(new ModelDescriptor { Name = ModelName })
                        .CallsTool(ToolName)
                        .CallsTool(ToolName)
                        .EchoesLastToolResult());

                builder.AddTool(
                    (Func<CancellationToken, Task<string>>)(async cancellationToken =>
                    {
                        seen.Add((TraconRunContext.Current?.TenantId, FrameworkTenant.Current));

                        await barrier.ArriveAsync(cancellationToken).ConfigureAwait(false);

                        return "read";
                    }),
                    name: ToolName,
                    description: "Reads the tenant the application's own framework holds.");

                builder.AddAgent(new AgentDefinition
                {
                    Name = "scope-agent",
                    Instructions = "Read it.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    ToolNames = [ToolName],
                });

                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.UseScheduling(static options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                    options.MaxConcurrentJobs = 4;
                });
            },
            configureServices: services =>
            {
                if (registerScope)
                {
                    services.AddSingleton<IHostTenantScope, FrameworkTenantScope>();
                }
            });

    private static async Task QueueAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/agents/scope-agent/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "start" }),
        };

        request.Headers.Add("Prefer", "respond-async");
        request.Headers.Add("X-Tracon-Tenant", tenant);

        using var accepted = await host.Client.SendAsync(request);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
    }

    /// <summary>Lets the first two arrivals through together: both tools are running when either returns.</summary>
    private sealed class TwoAtOnce
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) >= 2)
            {
                _both.TrySetResult();
            }

            await _both.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The current tenant of the application's own multi-tenant framework: an accessor over one <c>AsyncLocal</c>.</summary>
    private static class FrameworkTenant
    {
        private static readonly AsyncLocal<string?> Holder = new();

        public static string? Current => Holder.Value;

        public static IDisposable Change(string tenantId)
        {
            var previous = Holder.Value;

            Holder.Value = tenantId;

            return new Restore(previous);
        }

        private sealed class Restore(string? previous) : IDisposable
        {
            public void Dispose() => Holder.Value = previous;
        }
    }

    /// <summary>What the application registers: Tracon's tenant of the job becomes the framework's current tenant.</summary>
    private sealed class FrameworkTenantScope : IHostTenantScope
    {
        public IDisposable? Begin(string tenantId) => FrameworkTenant.Change(tenantId);
    }
}
