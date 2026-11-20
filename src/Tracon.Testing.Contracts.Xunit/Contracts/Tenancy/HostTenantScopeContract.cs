namespace Tracon.Testing.Contracts.Tenancy;

/// <summary>Behavior tests for an <see cref="IHostTenantScope"/> implementation.</summary>
/// <remarks>
/// <para>
/// The worker calls <see cref="IHostTenantScope.Begin"/> from its own method body for every job and disposes the
/// result when the job ends, so an implementation has to do three things that look alike and are not: make its tenant
/// the current one for the work that follows, put the previous one back when it is disposed, and keep two jobs of two
/// tenants that run at the same moment apart. This suite checks them against the host's own reading of its current
/// tenant.
/// </para>
/// <para>
/// Derive one class for the implementation. <see cref="ReadHostTenant"/> reads what the host's code would read inside a
/// job: the current-tenant accessor of the framework the implementation opens a scope in.
/// </para>
/// </remarks>
public abstract class HostTenantScopeContract
{
    /// <summary>Creates the scope under test. Called once for each test.</summary>
    /// <returns>The implementation.</returns>
    protected abstract IHostTenantScope CreateScope();

    /// <summary>Reads the tenant the host's own code sees right now.</summary>
    /// <returns>The host's current tenant, in the form the host uses; <see langword="null"/> when it has none.</returns>
    /// <remarks>
    /// The tests compare it with <see cref="TenantKey"/>, so a host that keys its tenants differently overrides that
    /// method to map an identifier Tracon hands over to the key the host reads back.
    /// </remarks>
    protected abstract string? ReadHostTenant();

    /// <summary>Maps a tenant identifier that Tracon hands over to what <see cref="ReadHostTenant"/> returns for it.</summary>
    /// <param name="tenantId">The identifier given to <see cref="IHostTenantScope.Begin"/>.</param>
    /// <returns>The value the host reads back; the identifier itself by default.</returns>
    protected virtual string TenantKey(string tenantId) => tenantId;

    [Fact]
    public void Begin_makes_the_tenant_current_and_dispose_puts_the_previous_one_back()
    {
        var scope = CreateScope();
        var before = ReadHostTenant();

        using (scope.Begin("contract-tenant-a"))
        {
            ReadHostTenant().ShouldBe(TenantKey("contract-tenant-a"));
        }

        ReadHostTenant().ShouldBe(before);
    }

    [Fact]
    public void A_scope_opened_inside_another_restores_the_outer_tenant_when_it_ends()
    {
        var scope = CreateScope();

        using (scope.Begin("contract-tenant-a"))
        {
            using (scope.Begin("contract-tenant-b"))
            {
                ReadHostTenant().ShouldBe(TenantKey("contract-tenant-b"));
            }

            ReadHostTenant().ShouldBe(TenantKey("contract-tenant-a"));
        }
    }

    [Fact]
    public void Disposing_a_scope_twice_does_no_harm()
    {
        var scope = CreateScope();
        var before = ReadHostTenant();
        var opened = scope.Begin("contract-tenant-a");

        opened?.Dispose();
        opened?.Dispose();

        ReadHostTenant().ShouldBe(before);
    }

    [Fact]
    public async Task Two_flows_of_two_tenants_open_at_the_same_moment_each_see_their_own()
    {
        var scope = CreateScope();
        var bothOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = 0;

        async Task<string?> RunAsync(string tenantId)
        {
            // Written in this flow's own body, the way the worker writes it for a job.
            using var host = scope.Begin(tenantId);

            if (Interlocked.Increment(ref opened) == 2)
            {
                bothOpen.TrySetResult();
            }

            await bothOpen.Task.ConfigureAwait(false);

            return ReadHostTenant();
        }

        var first = Task.Run(() => RunAsync("contract-tenant-a"));
        var second = Task.Run(() => RunAsync("contract-tenant-b"));

        (await first.ConfigureAwait(false)).ShouldBe(TenantKey("contract-tenant-a"));
        (await second.ConfigureAwait(false)).ShouldBe(TenantKey("contract-tenant-b"));
    }

    [Fact]
    public async Task A_scope_that_ends_in_one_flow_does_not_end_the_scope_of_another()
    {
        var scope = CreateScope();
        var firstEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            using (scope.Begin("contract-tenant-a"))
            {
                await secondOpen.Task.ConfigureAwait(false);
            }

            firstEnded.TrySetResult();
        });

        var second = Task.Run(async () =>
        {
            using var host = scope.Begin("contract-tenant-b");

            secondOpen.TrySetResult();
            await firstEnded.Task.ConfigureAwait(false);

            return ReadHostTenant();
        });

        await first.ConfigureAwait(false);

        (await second.ConfigureAwait(false)).ShouldBe(TenantKey("contract-tenant-b"));
    }
}
