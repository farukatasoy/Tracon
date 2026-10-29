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
    ///        .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
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
        RegisterAuditChain(services, configurationSection);
        RegisterCoreInfrastructure(services);
        RegisterStorageAndJobs(services);
        RegisterCatalogAssembly(services);

        return new TraconBuilder(services);
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
        BindRunRecording(section.GetSection(nameof(TraconOptions.RunRecording)), options.RunRecording, options);
        BindObservability(section.GetSection(nameof(TraconOptions.Observability)), options.Observability, options);
        BindCircuitBreaker(section.GetSection(nameof(TraconOptions.CircuitBreaker)), options.CircuitBreaker, options);
        BindHealth(section.GetSection(nameof(TraconOptions.Health)), options.Health, options);
        BindAudit(section.GetSection(nameof(TraconOptions.Audit)), options.Audit);
        BindDeployment(section.GetSection(nameof(TraconOptions.Deployment)), options.Deployment, options);
        BindSkills(section.GetSection(nameof(TraconOptions.Skills)), options.Skills, options);
        BindAgentGraph(section.GetSection(nameof(TraconOptions.AgentGraph)), options.AgentGraph, options);
        BindAttachments(section.GetSection(nameof(TraconOptions.Attachments)), options.Attachments, options);
        BindPricing(section.GetSection(nameof(TraconOptions.Pricing)), options.Pricing, options);
        BindUtilityModel(section.GetSection(nameof(TraconOptions.UtilityModel)), options);
        BindValidation(section.GetSection(nameof(TraconOptions.Validation)), options.Validation, options);
        BindPreflight(section.GetSection(nameof(TraconOptions.Preflight)), options.Preflight, options);
        BindModelConcurrency(section.GetSection(nameof(TraconOptions.ModelConcurrency)), options.ModelConcurrency, options);
        BindTools(section.GetSection(nameof(TraconOptions.Tools)), options.Tools, options);
    }
}
