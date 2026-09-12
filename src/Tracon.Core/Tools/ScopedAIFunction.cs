using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Wraps an <see cref="AIFunction"/> so every call runs inside its own
/// dependency-injection scope.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework passes an empty provider as
/// <see cref="AIFunctionArguments.Services"/>: a tool cannot resolve
/// a dependency from it. This wrapper fills that gap for tools registered
/// through <c>AddScopedTool</c> — it opens a fresh
/// <see cref="IServiceScope"/> before the call and overwrites
/// <see cref="AIFunctionArguments.Services"/> with that scope's provider, so
/// the wrapped body (and anything it resolves) sees a real, call-scoped
/// container. The scope closes as soon as the call completes, fails, or is
/// canceled.
/// </para>
/// <para>
/// Installed as the innermost layer, directly around the real function: it
/// must own the scope for exactly the real call, nothing else in the
/// wrapper chain resolves a scoped dependency.
/// </para>
/// </remarks>
internal sealed class ScopedAIFunction(AIFunction innerFunction, IServiceScopeFactory scopeFactory)
    : DelegatingAIFunction(innerFunction)
{
    private readonly IServiceScopeFactory _scopeFactory =
        scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var scope = _scopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            arguments.Services = scope.ServiceProvider;

            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
    }
}
