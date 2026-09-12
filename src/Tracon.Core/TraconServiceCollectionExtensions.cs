using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that register Tracon with dependency injection.</summary>
public static partial class TraconServiceCollectionExtensions
{
    /// <summary>
    /// Adds Tracon to the host application builder and reads settings from
    /// the <c>Tracon</c> section.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The configuration chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The one call every Tracon application starts with. On its own it
    /// runs with in-memory stores and needs no database; a provider and a store
    /// are added to the chain it returns.
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    ///
    /// builder.AddTracon()
    ///        .UseOpenAI(builder.Configuration["OpenAI:ApiKey"]!)
    ///        .UsePostgreSql(builder.Configuration.GetConnectionString("Tracon")!);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddTracon(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Services.AddTracon(builder.Configuration.GetSection(TraconOptions.SectionName));
    }

    /// <summary>Adds Tracon to the service collection.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configurationSection">Configuration section settings are read from.</param>
    /// <returns>The configuration chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// All services are registered with <c>TryAdd</c>. If you register your own
    /// implementation <em>before</em> this call, yours wins; Tracon does not overwrite it.
    /// </para>
    /// <para>
    /// When no additional configuration is done, Tracon runs with
    /// in-memory stores and requires no database.
    /// </para>
    /// </remarks>
    public static ITraconBuilder AddTracon(
        this IServiceCollection services,
        IConfiguration? configurationSection = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        RegisterOptions(services, configurationSection);
        RegisterCoreInfrastructure(services);
        RegisterStorageAndJobs(services);
        RegisterCatalogAssembly(services);

        return new TraconBuilder(services);
    }

    /// <summary>
    /// Adds an <see cref="IJobHandler"/> extension point under a handler key.
    /// </summary>
    /// <typeparam name="THandler">The handler type to add.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="handlerKey">
    /// The key jobs are dispatched by. 1-128 characters: lowercase ASCII
    /// letters, digits, <c>.</c>, <c>_</c>, or <c>-</c>, starting with a
    /// letter or digit. Pick a namespace prefix of your own
    /// (<c>contoso.nightly-report</c>).
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The worker dispatches by exact, ordinal key match, so
    /// <strong>this call may appear before or after <c>AddTracon()</c></strong>
    /// — registration order does not decide which handler runs, and no handler
    /// can shadow another.
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
    /// builder.Services.AddJobHandler&lt;NightlyReportJobHandler&gt;("contoso.nightly-report");
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="handlerKey"/> is empty, malformed, or inside the
    /// <see cref="JobHandlerKeys.ReservedPrefix"/> namespace.
    /// </exception>
    public static IServiceCollection AddJobHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services,
        string handlerKey)
        where THandler : class, IJobHandler
    {
        ArgumentNullException.ThrowIfNull(services);
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

        services.TryAddScoped<THandler>();
        services.AddSingleton(new JobHandlerRegistration(handlerKey, typeof(THandler), BuiltIn: false));

        return services;
    }

    /// <summary>
    /// Sets batch and scheduled run settings from code.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Settings modifier. When not given, only the defaults/configuration apply.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The job queue and scheduling stores are already registered by
    /// <c>AddTracon()</c>; this method only changes the settings (e.g.
    /// turning off this process's background worker with
    /// <c>o.RunWorker = false</c>). <c>PostConfigure</c> is used, so the value
    /// given from code always wins over the value coming from the configuration file.
    /// <example>
    /// <code>
    /// // A web instance that serves requests and leaves the jobs to a worker process.
    /// builder.Services.UseScheduling(o => o.RunWorker = false);
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection UseScheduling(
        this IServiceCollection services,
        Action<TraconSchedulingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        return services;
    }

    /// <summary>
    /// Manually binds a configuration section to the settings object.
    /// </summary>
    /// <remarks>
    /// This method must also be extended when a new setting is added. In
    /// exchange, Tracon.Core stays reflection-free and AOT-compatible.
    /// </remarks>
    private static void Bind(IConfiguration section, TraconOptions options)
    {
        BindCoreFields(section, options);

        // Every sub-section is responsible for ITS OWN existence check. An
        // early return causes later sections to never be read at all when the
        // first one is undefined; measured: Observability was being silently
        // ignored in a configuration where RunRecording was not written.
        BindRunRecording(section.GetSection(nameof(TraconOptions.RunRecording)), options.RunRecording);
        BindObservability(section.GetSection(nameof(TraconOptions.Observability)), options.Observability);
        BindCircuitBreaker(section.GetSection(nameof(TraconOptions.CircuitBreaker)), options.CircuitBreaker);
        BindHealth(section.GetSection(nameof(TraconOptions.Health)), options.Health);
        BindAudit(section.GetSection(nameof(TraconOptions.Audit)), options.Audit);
        BindSkills(section.GetSection(nameof(TraconOptions.Skills)), options.Skills);
        BindAgentGraph(section.GetSection(nameof(TraconOptions.AgentGraph)), options.AgentGraph);
        BindAttachments(section.GetSection(nameof(TraconOptions.Attachments)), options.Attachments);
        BindPricing(section.GetSection(nameof(TraconOptions.Pricing)), options.Pricing);
        options.UtilityModel = BindUtilityModel(section.GetSection(nameof(TraconOptions.UtilityModel)));
        BindValidation(section.GetSection(nameof(TraconOptions.Validation)), options.Validation);
        BindPreflight(section.GetSection(nameof(TraconOptions.Preflight)), options.Preflight);
        BindModelConcurrency(section.GetSection(nameof(TraconOptions.ModelConcurrency)), options.ModelConcurrency);
        BindTools(section.GetSection(nameof(TraconOptions.Tools)), options.Tools);
    }
}
