using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Tracon.Tests.Common;

/// <summary>
/// One call that reached a <see cref="FaultProxy{T}"/>.
/// </summary>
/// <param name="Method">The name of the interface method.</param>
/// <param name="Index">The 1-based position of the call among all calls the plan has seen.</param>
/// <param name="MethodIndex">The 1-based position of the call among the calls of the same method.</param>
/// <param name="Arguments">The arguments of the call.</param>
/// <param name="Token">The first <see cref="CancellationToken"/> argument of the call, or <see langword="default"/>.</param>
internal readonly record struct FaultCall(
    string Method,
    int Index,
    int MethodIndex,
    IReadOnlyList<object?> Arguments,
    CancellationToken Token);

/// <summary>
/// What a test wants done to the calls that pass through one or more
/// <see cref="FaultProxy{T}"/> instances: count them, run a hook just before one is
/// forwarded, make one fail, and make the proxy behave like a store that honors its
/// token.
/// </summary>
/// <remarks>
/// <para>
/// One plan can be shared by the proxies of several interfaces (store, sink, model),
/// so <see cref="FaultCall.Index"/> counts every awaited call of a run in the order
/// it happened. A first pass with no rule measures how many await points a run has;
/// a second pass per index cancels the run at exactly that point.
/// </para>
/// <para>
/// Test-only; linked into the test projects that need it, never shipped.
/// </para>
/// </remarks>
internal sealed class FaultPlan
{
    private readonly ConcurrentDictionary<string, int> _methodCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _log = new();
    private int _total;

    /// <summary>
    /// Gets or sets a hook that runs for every call just before it is forwarded. A test
    /// cancels the run's token here to cancel at exactly the call it names.
    /// </summary>
    public Action<FaultCall>? OnCall { get; set; }

    /// <summary>
    /// Gets or sets a rule that returns the exception to fail the call with, or
    /// <see langword="null"/> to forward it.
    /// </summary>
    public Func<FaultCall, Exception?>? Fail { get; set; }

    /// <summary>
    /// Gets or sets whether a call whose token is already canceled throws
    /// <see cref="OperationCanceledException"/> instead of being forwarded, as a SQL
    /// store does.
    /// </summary>
    public bool HonorCancellation { get; set; }

    /// <summary>Gets how many calls the plan has seen.</summary>
    public int Total => Volatile.Read(ref _total);

    /// <summary>Gets the names of the calls in the order they arrived.</summary>
    public IReadOnlyList<string> Calls => [.. _log];

    /// <summary>Gets how many calls of one method the plan has seen.</summary>
    /// <param name="method">The interface method name.</param>
    /// <returns>The count; zero for a method that was never called.</returns>
    public int CountOf(string method) => _methodCounts.TryGetValue(method, out var count) ? count : 0;

    /// <summary>
    /// Gets or sets a rule that makes a call wait until its token is canceled, as a store behind a network that
    /// drops packets does. The call then ends with <see cref="OperationCanceledException"/>.
    /// </summary>
    public Func<FaultCall, bool>? Hang { get; set; }

    /// <summary>
    /// Counts a call a hand-written fake receives and applies the plan to it: the hook runs, then the call throws,
    /// waits for its token, or returns.
    /// </summary>
    /// <param name="method">The name of the method.</param>
    /// <param name="cancellationToken">The token of the call.</param>
    /// <param name="arguments">The arguments of the call, for the rules.</param>
    /// <returns>The completion task.</returns>
    public async ValueTask EnterAsync(string method, CancellationToken cancellationToken = default, params object?[] arguments)
    {
        var call = Next(method, [.. arguments, cancellationToken]);
        var fault = Decide(call);

        if (fault is not null)
        {
            throw fault;
        }

        if (Hang?.Invoke(call) == true)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); // delay: simulated
        }
    }

    internal Exception? Decide(FaultCall call)
    {
        OnCall?.Invoke(call);

        var fault = Fail?.Invoke(call);

        if (fault is null && HonorCancellation && call.Token.IsCancellationRequested)
        {
            fault = new OperationCanceledException(call.Token);
        }

        return fault;
    }

    internal FaultCall Next(string method, object?[] arguments)
    {
        var index = Interlocked.Increment(ref _total);
        var methodIndex = _methodCounts.AddOrUpdate(method, 1, static (_, current) => current + 1);
        var token = arguments.OfType<CancellationToken>().FirstOrDefault();

        _log.Enqueue(method);

        return new FaultCall(method, index, methodIndex, arguments, token);
    }
}

/// <summary>
/// A <see cref="DispatchProxy"/> that forwards every call of an interface to a real
/// implementation and lets a <see cref="FaultPlan"/> count, hook or fail it.
/// </summary>
/// <typeparam name="T">The interface to wrap.</typeparam>
/// <remarks>
/// The fault is returned the way the interface returns: a faulted
/// <see cref="Task"/> or <see cref="ValueTask"/> for an asynchronous method, a thrown
/// exception for a synchronous one. A method that returns an
/// <see cref="IAsyncEnumerable{T}"/> is counted and forwarded, never failed.
/// </remarks>
#pragma warning disable CA1852 // DispatchProxy derives the generated proxy from this type, so it cannot be sealed.
internal class FaultProxy<T> : DispatchProxy
#pragma warning restore CA1852
    where T : class
{
    private static readonly MethodInfo FromExceptionTask = typeof(Task)
        .GetMethods()
        .Single(static method => method is { Name: nameof(Task.FromException), IsGenericMethodDefinition: true });

    private T? _inner;
    private FaultPlan? _plan;

    /// <summary>Wraps an implementation.</summary>
    /// <param name="inner">The real implementation every call is forwarded to.</param>
    /// <param name="plan">The plan that counts, hooks and fails the calls.</param>
    /// <returns>The proxy, typed as the interface.</returns>
    public static T Create(T inner, FaultPlan plan)
    {
        var proxy = Create<T, FaultProxy<T>>();
        var self = (FaultProxy<T>)(object)proxy;
        self._inner = inner;
        self._plan = plan;

        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var arguments = args ?? [];
        var call = _plan!.Next(targetMethod.Name, arguments);

        var fault = _plan.Decide(call);

        var returnsStream = targetMethod.ReturnType.IsGenericType
            && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>);

        if (fault is not null && !returnsStream)
        {
            return Faulted(targetMethod.ReturnType, fault);
        }

        if (!returnsStream && _plan.Hang?.Invoke(call) == true)
        {
            return Hung(targetMethod.ReturnType, call.Token);
        }

        try
        {
            return targetMethod.Invoke(_inner, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();

            throw;
        }
    }

    private static Task<TResult> HangTask<TResult>(CancellationToken cancellationToken)
    {
        var source = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        cancellationToken.Register(static state => ((TaskCompletionSource<TResult>)state!).TrySetCanceled(), source);

        return source.Task;
    }

    private static object? Hung(Type returnType, CancellationToken cancellationToken)
    {
        if (returnType == typeof(Task))
        {
            return HangTask<bool>(cancellationToken);
        }

        if (returnType == typeof(ValueTask))
        {
            return new ValueTask(HangTask<bool>(cancellationToken));
        }

        var result = returnType.GetGenericArguments()[0];
        var task = typeof(FaultProxy<T>)
            .GetMethod(nameof(HangTask), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(result)
            .Invoke(null, [cancellationToken]);

        return returnType.GetGenericTypeDefinition() == typeof(Task<>) ? task : Activator.CreateInstance(returnType, task);
    }

    private static object? Faulted(Type returnType, Exception fault)
    {
        if (returnType == typeof(Task))
        {
            return Task.FromException(fault);
        }

        if (returnType == typeof(ValueTask))
        {
            return new ValueTask(Task.FromException(fault));
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return FromExceptionTask.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke(null, [fault]);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var result = returnType.GetGenericArguments()[0];
            var task = FromExceptionTask.MakeGenericMethod(result).Invoke(null, [fault]);

            return Activator.CreateInstance(returnType, task);
        }

        throw fault;
    }
}
