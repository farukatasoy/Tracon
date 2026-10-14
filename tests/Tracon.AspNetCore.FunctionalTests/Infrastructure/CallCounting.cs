using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Counts the calls to one member of an already registered service, while every
/// member keeps the real implementation's behavior.
/// </summary>
/// <remarks>
/// The sibling of <see cref="FaultInjection"/>: a test that must prove a code path
/// does NOT touch a store (a gate) needs the number of calls, not a failure.
/// </remarks>
internal static class CallCounting
{
    /// <summary>
    /// Replaces the last registration of <typeparamref name="TService"/> with one that
    /// counts the calls to <paramref name="memberName"/>.
    /// </summary>
    /// <returns>The counter the proxy increments.</returns>
    public static CallCounter CountCalls<TService>(this IServiceCollection services, string memberName)
        where TService : class
    {
        var counter = new CallCounter();
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(TService));

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => CallCountingProxy<TService>.Wrap(
                FaultInjection.Resolve<TService>(provider, descriptor), memberName, counter),
            descriptor.Lifetime));

        return counter;
    }
}

/// <summary>The number of calls a <see cref="CallCounting"/> proxy has seen.</summary>
internal sealed class CallCounter
{
    private int _count;

    /// <summary>Gets the number of calls so far.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Adds one call.</summary>
    public void Increment() => Interlocked.Increment(ref _count);
}

/// <summary>The forwarding proxy behind <see cref="CallCounting.CountCalls{TService}"/>.</summary>
/// <typeparam name="TService">The proxied interface.</typeparam>
#pragma warning disable CA1852 // DispatchProxy derives a runtime type from this class; it cannot be sealed.
internal class CallCountingProxy<TService> : DispatchProxy
#pragma warning restore CA1852
    where TService : class
{
    private TService _inner = null!;
    private string _memberName = null!;
    private CallCounter _counter = null!;

    public static TService Wrap(TService inner, string memberName, CallCounter counter)
    {
        var proxy = Create<TService, CallCountingProxy<TService>>();
        var self = (CallCountingProxy<TService>)(object)proxy;

        self._inner = inner;
        self._memberName = memberName;
        self._counter = counter;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (string.Equals(targetMethod.Name, _memberName, StringComparison.Ordinal))
        {
            _counter.Increment();
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}
