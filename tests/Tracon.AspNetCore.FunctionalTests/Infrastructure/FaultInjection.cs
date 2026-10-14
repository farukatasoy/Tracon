using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Makes one member of an already registered service throw, while every other
/// member keeps the real implementation's behavior.
/// </summary>
/// <remarks>
/// A store interface carries a dozen members; a hand-written fake for one failing
/// write would have to reimplement the rest. The proxy forwards every call to the
/// service the application registered and throws only from the named member, so
/// the test exercises the real store everywhere else.
/// </remarks>
internal static class FaultInjection
{
    /// <summary>
    /// Replaces the last registration of <typeparamref name="TService"/> with one that
    /// throws from <paramref name="memberName"/> the first <paramref name="times"/> calls.
    /// </summary>
    /// <remarks>
    /// <paramref name="when"/> narrows the calls that count: only a call whose arguments it
    /// accepts throws (and uses up one of <paramref name="times"/>); every other call goes to the
    /// real service.
    /// </remarks>
    public static IServiceCollection ThrowFrom<TService>(
        this IServiceCollection services,
        string memberName,
        Func<Exception> fault,
        int times = int.MaxValue,
        Func<object?[], bool>? when = null)
        where TService : class
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(TService));

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => FaultInjectingProxy<TService>.Wrap(Resolve<TService>(provider, descriptor), memberName, fault, times, when: when),
            descriptor.Lifetime));

        return services;
    }

    /// <summary>
    /// Replaces the last registration of <typeparamref name="TService"/> with one whose
    /// <paramref name="memberName"/> runs the real call and THEN throws, the first
    /// <paramref name="times"/> calls: a write the store committed but reported as failed
    /// (a timeout after the commit).
    /// </summary>
    /// <remarks>Only members that return <see cref="ValueTask"/> or <see cref="Task"/>.</remarks>
    public static IServiceCollection ThrowAfter<TService>(
        this IServiceCollection services,
        string memberName,
        Func<Exception> fault,
        int times = int.MaxValue)
        where TService : class
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(TService));

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => FaultInjectingProxy<TService>.Wrap(Resolve<TService>(provider, descriptor), memberName, fault, times, afterInner: true),
            descriptor.Lifetime));

        return services;
    }

    /// <summary>Builds the service a replaced registration described.</summary>
    internal static TService Resolve<TService>(IServiceProvider provider, ServiceDescriptor descriptor)
        where TService : class
        => (TService)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
}

/// <summary>The forwarding proxy behind <see cref="FaultInjection.ThrowFrom{TService}"/>.</summary>
/// <typeparam name="TService">The proxied interface.</typeparam>
#pragma warning disable CA1852 // DispatchProxy derives a runtime type from this class; it cannot be sealed.
internal class FaultInjectingProxy<TService> : DispatchProxy
#pragma warning restore CA1852
    where TService : class
{
    private TService _inner = null!;
    private string _memberName = null!;
    private Func<Exception> _fault = null!;
    private int _remaining;
    private bool _afterInner;
    private Func<object?[], bool>? _when;

    public static TService Wrap(
        TService inner,
        string memberName,
        Func<Exception> fault,
        int times,
        bool afterInner = false,
        Func<object?[], bool>? when = null)
    {
        var proxy = Create<TService, FaultInjectingProxy<TService>>();
        var self = (FaultInjectingProxy<TService>)(object)proxy;

        self._inner = inner;
        self._memberName = memberName;
        self._fault = fault;
        self._remaining = times;
        self._afterInner = afterInner;
        self._when = when;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (string.Equals(targetMethod.Name, _memberName, StringComparison.Ordinal)
            && (_when is null || _when(args ?? []))
            && Interlocked.Decrement(ref _remaining) >= 0)
        {
            if (!_afterInner)
            {
                throw _fault();
            }

            var committed = targetMethod.Invoke(_inner, args) switch
            {
                ValueTask valueTask => ThrowAfterAsync(valueTask.AsTask()),
                Task task => ThrowAfterAsync(task),
                _ => throw new NotSupportedException($"'{_memberName}' returns no task; ThrowAfter cannot wrap it."),
            };

            // A proxy returns object: the caller's ValueTask is the boxed wrapper of this task.
            return targetMethod.ReturnType == typeof(ValueTask) ? new ValueTask(committed) : committed;
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

    private async Task ThrowAfterAsync(Task inner)
    {
        await inner.ConfigureAwait(false);

        throw _fault();
    }
}
