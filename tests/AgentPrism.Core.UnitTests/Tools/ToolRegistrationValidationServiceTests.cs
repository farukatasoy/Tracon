using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace AgentPrism.Core.UnitTests.Tools;

public sealed class ToolRegistrationValidationServiceTests
{
    [Fact]
    public async Task Unverified_registry_fails_startup_by_default()
    {
        var service = Create(new ForeignRegistry());

        var exception = await Should.ThrowAsync<AgentPrismException>(async () => await service.StartAsync(CancellationToken.None));

        exception.Message.ShouldContain("Authorizing");
        exception.Message.ShouldContain("Truncating");
    }

    [Fact]
    public async Task Opt_in_allows_an_unverified_registry()
    {
        var options = new AgentPrismOptions();
        options.Tools.AllowUnverifiedToolRegistry = true;

        await Create(new ForeignRegistry(), options: options).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Invalid_per_tool_timeout_fails_startup()
    {
        var registration = new AgentPrismToolRegistration(
            AIFunctionFactory.Create((Func<string>)(() => "ok"), "status", "Gets status."),
            timeout: TimeSpan.Zero);
        var service = Create(new VerifiedRegistry(), [registration]);

        await Should.ThrowAsync<AgentPrismException>(async () => await service.StartAsync(CancellationToken.None));
    }

    private static ToolRegistrationValidationService Create(
        IToolRegistry registry,
        IEnumerable<AgentPrismToolRegistration>? registrations = null,
        AgentPrismOptions? options = null)
        => new(
            registry,
            registrations ?? [],
            Options.Create(options ?? new AgentPrismOptions()),
            NullLogger<ToolRegistrationValidationService>.Instance);

    private class ForeignRegistry : IToolRegistry
    {
        public IReadOnlyList<ToolDescriptor> List() => [];

        public bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool)
        {
            tool = null;
            return false;
        }
    }

    private sealed class VerifiedRegistry : ForeignRegistry, IVerifiedToolRegistry
    {
    }
}
