using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Records which members of a registered service a run calls, and forwards every call unchanged.
/// </summary>
/// <remarks>
/// The store interfaces carry some three hundred members between them and a plain run touches a few dozen.
/// Knowing which ones lets a failure be injected where it can matter instead of three hundred times where nothing
/// calls.
/// </remarks>
internal static class CallRecording
{
    /// <summary>Replaces the last registration of <paramref name="serviceType"/> with a recording proxy.</summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="serviceType">The registered interface.</param>
    /// <param name="calls">Where the member names are collected, keyed by service and member.</param>
    public static void Record(IServiceCollection services, Type serviceType, ConcurrentDictionary<(Type Service, string Member), int> calls)
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == serviceType);
        var wrap = typeof(CallRecordingProxy<>).MakeGenericType(serviceType).GetMethod(nameof(CallRecordingProxy<object>.Wrap))!;

        services.Add(ServiceDescriptor.Describe(
            serviceType,
            provider =>
            {
                var inner = descriptor.ImplementationInstance
                    ?? descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

                return wrap.Invoke(null, [inner, calls])!;
            },
            descriptor.Lifetime));
    }
}

/// <summary>The forwarding proxy behind <see cref="CallRecording"/>.</summary>
/// <typeparam name="TService">The proxied interface.</typeparam>
#pragma warning disable CA1852 // DispatchProxy derives a runtime type from this class; it cannot be sealed.
internal class CallRecordingProxy<TService> : DispatchProxy
#pragma warning restore CA1852
    where TService : class
{
    private TService _inner = null!;
    private ConcurrentDictionary<(Type Service, string Member), int> _calls = null!;

    public static TService Wrap(TService inner, ConcurrentDictionary<(Type Service, string Member), int> calls)
    {
        var proxy = Create<TService, CallRecordingProxy<TService>>();
        var self = (CallRecordingProxy<TService>)(object)proxy;

        self._inner = inner;
        self._calls = calls;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        _calls.AddOrUpdate((typeof(TService), targetMethod.Name), 1, static (_, count) => count + 1);

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
