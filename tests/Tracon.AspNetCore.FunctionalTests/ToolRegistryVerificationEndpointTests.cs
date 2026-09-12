using System.Diagnostics.CodeAnalysis;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <see cref="ToolRegistrationValidationService"/>'s <see cref="IToolRegistry"/>
/// check, through a REAL <c>IHostedService</c> boot (<c>WebApplication.StartAsync</c>),
/// not a direct unit call — Manual Case 7/8/9 of Phase 102 (host-boundary claim).
/// </summary>
public sealed class ToolRegistryVerificationEndpointTests
{
    [Fact]
    public async Task An_unrecognized_IToolRegistry_fails_real_host_startup()
    {
        // 🚨 Registered in configureServices, which runs BEFORE AddTracon()'s
        // own TryAddSingleton<IToolRegistry> — the same order a third party's own
        // Program.cs would naturally produce.
        var exception = await Should.ThrowAsync<TraconException>(async () =>
            await TraconTestHost.StartAsync(
                static builder => builder.AddAgent(TestData.Definition()),
                configureServices: static services => services.AddSingleton<IToolRegistry, ForeignRegistry>()));

        exception.Message.ShouldContain("Authorizing");
        exception.Message.ShouldContain("Truncating");
    }

    [Fact]
    public async Task AllowUnverifiedToolRegistry_lets_real_host_startup_proceed()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder
                .AddAgent(TestData.Definition())
                .Configure(static options => options.Tools.AllowUnverifiedToolRegistry = true),
            configureServices: static services => services.AddSingleton<IToolRegistry, ForeignRegistry>());

        host.Services.GetRequiredService<IToolRegistry>().ShouldBeOfType<ForeignRegistry>();
    }

    private sealed class ForeignRegistry : IToolRegistry
    {
        public IReadOnlyList<ToolDescriptor> List() => [];

        public bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool)
        {
            tool = null;
            return false;
        }
    }
}
