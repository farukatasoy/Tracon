using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Contracts.Tools;

/// <summary>Behavior tests for a code-defined custom tool registration.</summary>
public abstract class CustomToolContract : IAsyncLifetime
{
    /// <summary>The registration under test.</summary>
    protected AgentPrismToolRegistration Registration { get; private set; } = null!;

    /// <summary>Creates the custom tool registration under test.</summary>
    protected abstract ValueTask<AgentPrismToolRegistration> CreateRegistrationAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Registration = await CreateRegistrationAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    [Fact]
    public void Name_is_stable_and_valid()
    {
        Registration.Function.Name.ShouldNotBeNullOrWhiteSpace();
        Registration.Function.Name.Length.ShouldBeLessThanOrEqualTo(64);
        Registration.Function.Name.All(static value => char.IsAsciiLetterOrDigit(value) || value is '_' or '-').ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_server_calls_complete()
    {
        if (Registration.Function is not AIFunction function)
        {
            return;
        }

        var calls = new Task<object?>[8];

        for (var index = 0; index < calls.Length; index++)
        {
            calls[index] = function.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), CancellationToken.None).AsTask();
        }

        await Task.WhenAll(calls).ConfigureAwait(false);
    }

    [Fact]
    public void Metadata_is_self_consistent()
    {
        (Registration.RequiresApproval && Registration.Function is not AIFunction).ShouldBeFalse();
        (Registration.Timeout is null || Registration.Timeout > TimeSpan.Zero).ShouldBeTrue();
    }
}
