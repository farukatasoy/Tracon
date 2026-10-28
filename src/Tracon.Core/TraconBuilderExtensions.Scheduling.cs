using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

public static partial class TraconBuilderExtensions
{
    /// <summary>
    /// Adds an <see cref="IJobHandler"/> extension point under a handler key.
    /// </summary>
    /// <typeparam name="THandler">The handler type to add.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="handlerKey">
    /// The key jobs are dispatched by. 1-128 characters: lowercase ASCII
    /// letters, digits, <c>.</c>, <c>_</c>, or <c>-</c>, starting with a
    /// letter or digit. Pick a namespace prefix of your own
    /// (<c>contoso.nightly-report</c>).
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// <para>
    /// The worker dispatches by exact, ordinal key match, so the order of
    /// registrations does not decide which handler runs, and no handler can
    /// shadow another.
    /// </para>
    /// <para>
    /// The handler is registered <strong>scoped</strong> and resolved from a
    /// fresh scope for every execution, so it may take scoped dependencies in
    /// its constructor. Registering two handlers under the same key, or using
    /// a key inside Tracon's own <c>tracon.</c> namespace, stops the
    /// host from starting.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddJobHandler&lt;NightlyReportJobHandler&gt;("contoso.nightly-report");
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="handlerKey"/> is empty, malformed, or inside the
    /// <see cref="JobHandlerKeys.ReservedPrefix"/> namespace.
    /// </exception>
    public static ITraconBuilder AddJobHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this ITraconBuilder builder,
        string handlerKey)
        where THandler : class, IJobHandler
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerKey);

        if (!JobHandlerKeys.IsValidKey(handlerKey))
        {
            throw new ArgumentException(
                $"'{handlerKey}' is not a valid job handler key. A key must be 1-128 characters: " +
                "lowercase ASCII letters, digits, '.', '_', or '-', starting with a letter or digit.",
                nameof(handlerKey));
        }

        if (JobHandlerKeys.IsReserved(handlerKey))
        {
            throw new ArgumentException(
                $"The job handler key '{handlerKey}' is reserved: the '{JobHandlerKeys.ReservedPrefix}' " +
                "namespace belongs to Tracon's own handlers. Pick a key of your own " +
                "(for example 'contoso.nightly-report').",
                nameof(handlerKey));
        }

        builder.Services.TryAddScoped<THandler>();
        builder.Services.AddSingleton(new JobHandlerRegistration(handlerKey, typeof(THandler), BuiltIn: false));

        return builder;
    }

    /// <summary>
    /// Sets batch and scheduled run settings from code.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">Settings modifier. When not given, only the defaults/configuration apply.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The job queue and scheduling stores are already registered by
    /// <c>AddTracon()</c>; this method only changes the settings (e.g.
    /// turning off this process's background worker with
    /// <c>o.RunWorker = false</c>). <c>PostConfigure</c> is used, so the value
    /// given from code always wins over the value coming from the configuration file.
    /// <example>
    /// <code>
    /// // A web instance that serves requests and leaves the jobs to a worker process.
    /// builder.AddTracon()
    ///        .UseScheduling(o => o.RunWorker = false);
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder UseScheduling(
        this ITraconBuilder builder,
        Action<TraconSchedulingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is not null)
        {
            builder.Services.PostConfigure(configure);
        }

        return builder;
    }
}
