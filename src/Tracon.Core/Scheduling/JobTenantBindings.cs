namespace Tracon;

/// <summary>
/// What the job worker needs to know about tenants, in one place: the ceiling a tenant may hold in the queue and the
/// scope the host opens for a tenant's job.
/// </summary>
/// <remarks>
/// The worker's constructor is already wide, and both parts are optional registrations the container answers with
/// <see langword="null"/> when nobody made one. Taking them together keeps the constructor from growing a parameter for
/// each tenant concern.
/// </remarks>
/// <param name="CapSource">Names the per-tenant ceiling; <see langword="null"/> leases in plain age order.</param>
/// <param name="HostScope">Opens the host's own tenant scope around a job; <see langword="null"/> opens none.</param>
internal sealed record JobTenantBindings(IJobTenantCapSource? CapSource, IHostTenantScope? HostScope)
{
    /// <summary>
    /// Builds the bindings, taking the ceiling from the workflow engine's source or from the scheduling settings,
    /// whichever is on.
    /// </summary>
    /// <param name="capSource">The source another package registered (the workflow engine's); <see langword="null"/> when none is.</param>
    /// <param name="hostScope">The host's tenant scope; <see langword="null"/> when the host registered none.</param>
    /// <param name="options">The scheduling settings, which may name a ceiling of their own.</param>
    /// <returns>The bindings the worker is built with.</returns>
    /// <exception cref="InvalidOperationException">Both the source and the settings name a ceiling.</exception>
    /// <remarks>
    /// <para>
    /// One lease request carries ONE ceiling: a set of handler keys and the most jobs of them a tenant may hold. The
    /// workflow engine's ceiling and the one in the settings would each need the request to themselves, so a host that
    /// turns both on is told at startup and not left with one of them silently dropped.
    /// </para>
    /// <para>
    /// The settings' ceiling is NOT a registered <see cref="IJobTenantCapSource"/>. A container keeps the first
    /// registration of a service, and the core's registrations run before the workflow package's: a core source in the
    /// container would hide the engine's.
    /// </para>
    /// </remarks>
    public static JobTenantBindings Create(IJobTenantCapSource? capSource, IHostTenantScope? hostScope, TraconSchedulingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxLeasedPerTenant is not { } ceiling || options.TenantCapHandlerKeys.Count == 0)
        {
            return new JobTenantBindings(capSource, hostScope);
        }

        if (capSource?.Current is not null)
        {
            throw new InvalidOperationException(
                $"A per-tenant lease ceiling is set twice: {nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxLeasedPerTenant)} " +
                $"(Tracon:Scheduling:{nameof(TraconSchedulingOptions.MaxLeasedPerTenant)}) and the workflow engine's " +
                "Tracon:Workflows:MaxConcurrentRunsPerTenant. A lease request carries one ceiling. Turn one of them off.");
        }

        return new JobTenantBindings(
            new SettingsTenantCapSource(new JobTenantCap { HandlerKeys = [.. options.TenantCapHandlerKeys], MaxLeasedPerTenant = ceiling }),
            hostScope);
    }

    /// <summary>The ceiling the scheduling settings name, fixed when the host starts.</summary>
    private sealed class SettingsTenantCapSource(JobTenantCap cap) : IJobTenantCapSource
    {
        /// <inheritdoc />
        public JobTenantCap? Current => cap;
    }
}
