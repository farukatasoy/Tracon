using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Makes one member of an already registered service wait before it runs, while every other member keeps the real
/// implementation's behavior.
/// </summary>
/// <remarks>
/// The sibling of <see cref="FaultInjection"/>: that one makes a call fail, this one makes it slow, so a test can
/// do something (drop the connection, start a second call) while a write is half done. When the wait ends the call
/// honours a cancelled token it was given, as a real store does.
/// </remarks>
internal static class HoldingProxy
{
    /// <summary>
    /// Replaces the last registration of <typeparamref name="TService"/> with one whose
    /// <paramref name="memberName"/> signals <paramref name="reached"/> and then waits for <paramref name="gate"/>,
    /// for every call <paramref name="when"/> accepts.
    /// </summary>
    /// <remarks>Only members that return <see cref="ValueTask{TResult}"/>.</remarks>
    public static IServiceCollection HoldBefore<TService>(
        this IServiceCollection services,
        string memberName,
        Func<object?[], bool> when,
        TaskCompletionSource reached,
        TaskCompletionSource gate)
        where TService : class
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(TService));

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => HoldingProxy<TService>.Wrap(
                FaultInjection.Resolve<TService>(provider, descriptor),
                memberName,
                when,
                reached,
                gate),
            descriptor.Lifetime));

        return services;
    }
}

#pragma warning disable CA1852 // DispatchProxy derives a runtime type from this class; it cannot be sealed.
internal class HoldingProxy<TService> : DispatchProxy
#pragma warning restore CA1852
    where TService : class
{
    private TService _inner = null!;
    private string _memberName = null!;
    private Func<object?[], bool> _when = null!;
    private TaskCompletionSource _reached = null!;
    private TaskCompletionSource _gate = null!;

    public static TService Wrap(
        TService inner,
        string memberName,
        Func<object?[], bool> when,
        TaskCompletionSource reached,
        TaskCompletionSource gate)
    {
        var proxy = Create<TService, HoldingProxy<TService>>();
        var self = (HoldingProxy<TService>)(object)proxy;

        self._inner = inner;
        self._memberName = memberName;
        self._when = when;
        self._reached = reached;
        self._gate = gate;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var arguments = args ?? [];

        if (!string.Equals(targetMethod.Name, _memberName, StringComparison.Ordinal) || !_when(arguments))
        {
            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }

        _reached.TrySetResult();

        var result = targetMethod.ReturnType.GetGenericArguments()[0];

        return typeof(HoldingProxy<TService>)
            .GetMethod(nameof(HoldAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(result)
            .Invoke(null, [_gate.Task, arguments, (Func<object?>)(() => targetMethod.Invoke(_inner, args))]);
    }

    private static async ValueTask<TResult> HoldAsync<TResult>(Task gate, object?[] arguments, Func<object?> invoke)
    {
        await gate.ConfigureAwait(false);

        // A real store honours the token it is handed; the in-memory one does not, so the proxy does it for the test.
        // Without this a write that used the caller's token would survive the caller dropping the connection here and
        // hide the very bug the test is about.
        foreach (var argument in arguments)
        {
            if (argument is CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
            }
        }

        return await (ValueTask<TResult>)invoke()!;
    }
}
