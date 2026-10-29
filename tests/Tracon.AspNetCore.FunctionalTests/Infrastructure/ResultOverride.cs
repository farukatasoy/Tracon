using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Makes one member of an already registered service answer a fixed value, while every
/// other member keeps the real implementation's behavior.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="FaultInjection"/> for a race a test cannot time: a store
/// that answers "no row" for a write the endpoint read a row for a moment ago.
/// </remarks>
internal static class ResultOverride
{
    /// <summary>
    /// Replaces the last registration of <typeparamref name="TService"/> with one whose
    /// <paramref name="memberName"/> returns <paramref name="result"/> without calling the real service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="memberName">The member to override.</param>
    /// <param name="result">The value the member returns (a <see cref="ValueTask{TResult}"/> is built around it).</param>
    /// <typeparam name="TService">The proxied interface.</typeparam>
    /// <typeparam name="TResult">The type the member's <see cref="ValueTask{TResult}"/> carries.</typeparam>
    /// <returns>The service collection.</returns>
    public static IServiceCollection ReturnFrom<TService, TResult>(
        this IServiceCollection services,
        string memberName,
        TResult result)
        where TService : class
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(TService));

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            provider => ResultOverridingProxy<TService>.Wrap(
                FaultInjection.Resolve<TService>(provider, descriptor), memberName, new ValueTask<TResult>(result)),
            descriptor.Lifetime));

        return services;
    }
}

/// <summary>The forwarding proxy behind <see cref="ResultOverride.ReturnFrom{TService, TResult}"/>.</summary>
/// <typeparam name="TService">The proxied interface.</typeparam>
#pragma warning disable CA1852 // DispatchProxy derives a runtime type from this class; it cannot be sealed.
internal class ResultOverridingProxy<TService> : DispatchProxy
#pragma warning restore CA1852
    where TService : class
{
    private TService _inner = null!;
    private string _memberName = null!;
    private object _result = null!;

    public static TService Wrap(TService inner, string memberName, object result)
    {
        var proxy = Create<TService, ResultOverridingProxy<TService>>();
        var self = (ResultOverridingProxy<TService>)(object)proxy;

        self._inner = inner;
        self._memberName = memberName;
        self._result = result;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (string.Equals(targetMethod.Name, _memberName, StringComparison.Ordinal))
        {
            return _result;
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
}
