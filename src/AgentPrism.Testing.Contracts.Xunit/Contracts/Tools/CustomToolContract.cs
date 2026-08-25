using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Contracts.Tools;

/// <summary>Behavior tests for a code-defined custom tool registration.</summary>
public abstract class CustomToolContract : IAsyncLifetime
{
    /// <summary>The registration under test.</summary>
    protected AgentPrismToolRegistration Registration { get; private set; } = null!;

    /// <summary>Creates the custom tool registration under test.</summary>
    protected abstract ValueTask<AgentPrismToolRegistration> CreateRegistrationAsync();

    /// <summary>Gets the semantic result text expected from an argument-free invocation.</summary>
    protected abstract string ExpectedResultText { get; }

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

    /// <summary>
    /// Starts eight calls against the SAME singleton instance before awaiting
    /// any of them, and requires every one to run to completion.
    /// </summary>
    /// <remarks>
    /// This contract has no knowledge of what the tool's arguments or result
    /// mean, so it cannot assert that concurrent calls returned the RIGHT,
    /// non-corrupted value for each caller — only that none of them faulted
    /// (<see cref="TaskStatus.RanToCompletion"/>), which a naive
    /// non-thread-safe shared field (a counter, a cache, a mutable buffer)
    /// tends to violate under real concurrent access. A tool with per-call
    /// state worth corrupting is proven at the semantic level by its own
    /// tests — see <c>ConcurrentToolInvocationTests</c> in
    /// <c>AgentPrism.AspNetCore.FunctionalTests</c> for a body that
    /// deliberately holds a shared <c>Barrier</c> to guarantee genuine overlap.
    /// </remarks>
    [Fact]
    public async Task Concurrent_server_calls_complete()
    {
        var function = Registration.Function.ShouldBeAssignableTo<AIFunction>(
            "CustomToolContract verifies an invocable server tool. Declaration-only registrations need a separate contract.");

        var calls = new Task<object?>[8];

        for (var index = 0; index < calls.Length; index++)
        {
            calls[index] = function.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), CancellationToken.None).AsTask();
        }

        await Task.WhenAll(calls).ConfigureAwait(false);

        calls.ShouldAllBe(static call => call.Status == TaskStatus.RanToCompletion);
        calls.ShouldAllBe(call => string.Equals(
            call.Result == null ? null : call.Result.ToString(),
            ExpectedResultText,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Metadata_is_self_consistent()
    {
        (Registration.RequiresApproval && Registration.Function is not AIFunction).ShouldBeFalse();
        (Registration.Timeout is null || Registration.Timeout > TimeSpan.Zero).ShouldBeTrue();
    }
}
