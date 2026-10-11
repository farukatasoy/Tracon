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
    public static IServiceCollection ThrowFrom<TService>(
        this IServiceCollection services,
        string memberName,
        Func<Exception> fault,
        int times = int.MaxValue)
        where TService : class
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(TService));

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => FaultInjectingProxy<TService>.Wrap(Resolve<TService>(provider, descriptor), memberName, fault, times),
            descriptor.Lifetime));

        return services;
    }

    private static TService Resolve<TService>(IServiceProvider provider, ServiceDescriptor descriptor)
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

    public static TService Wrap(TService inner, string memberName, Func<Exception> fault, int times)
    {
        var proxy = Create<TService, FaultInjectingProxy<TService>>();
        var self = (FaultInjectingProxy<TService>)(object)proxy;

        self._inner = inner;
        self._memberName = memberName;
        self._fault = fault;
        self._remaining = times;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (string.Equals(targetMethod.Name, _memberName, StringComparison.Ordinal)
            && Interlocked.Decrement(ref _remaining) >= 0)
        {
            throw _fault();
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
