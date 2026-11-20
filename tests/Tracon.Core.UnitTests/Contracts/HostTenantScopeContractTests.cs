using System.Reflection;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Tenancy;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// Runs the published <see cref="HostTenantScopeContract"/> against a scope written the way a multi-tenant
/// framework's current-tenant accessor is: one <c>AsyncLocal</c> that a scope sets and puts back.
/// </summary>
public sealed class AsyncLocalHostTenantScopeContractTests : HostTenantScopeContract
{
    protected override IHostTenantScope CreateScope() => new AsyncLocalHostTenantScope();

    protected override string? ReadHostTenant() => AsyncLocalHostTenantScope.Current;

    /// <summary>The host side of the contract: an accessor and the scope that changes it.</summary>
    private sealed class AsyncLocalHostTenantScope : IHostTenantScope
    {
        private static readonly AsyncLocal<string?> Accessor = new();

        public static string? Current => Accessor.Value;

        public IDisposable? Begin(string tenantId)
        {
            var previous = Accessor.Value;

            Accessor.Value = tenantId;

            return new Restore(previous);
        }

        private sealed class Restore(string? previous) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Accessor.Value = previous;
                }
            }
        }
    }
}

/// <summary>Every host tenant scope contract the package ships has a derived test in this assembly.</summary>
public sealed class HostTenantScopeContractCoverageTests
{
    [Fact]
    public void Every_host_tenant_scope_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.TenancyContracts).ShouldBeEmpty();
}
