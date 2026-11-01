#pragma warning disable IDE1006 // The pages write these names in lower case; a script reads a global by its spelling.
#pragma warning disable CA1822, CA1024, MA0016

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;

namespace Tracon.Generators.UnitTests.SiteExamples;

/// <summary>
/// The names a documentation snippet uses without declaring them: the application a consumer already has around the
/// lines a page shows.
/// </summary>
/// <remarks>
/// A page says <c>builder.AddWorkflow(...)</c> and <c>builder.Services.AddSingleton(...)</c> and means the same
/// <c>builder</c>, so <see cref="DocsBuilder"/> is both a host application builder and a Tracon builder. Every other
/// name is the one a page uses and does not define; a snippet that declares one of its own shadows it.
/// </remarks>
public sealed class SiteGlobals
{
    public DocsBuilder builder { get; } = new();

    public IEndpointRouteBuilder app { get; } = null!;

    public ITraconBuilder tracon { get; } = new DocsBuilder();

    public IConfiguration configuration { get; } = null!;

    public IServiceCollection services { get; } = new ServiceCollection();

    public IServiceProvider provider { get; } = null!;

    public AIAgent agent { get; } = null!;

    public string apiKey { get; } = "api-key";

    public string connectionString { get; } = "Data Source=tracon.db";

    public string[] args { get; } = [];
}

/// <summary>A host application builder that is also a Tracon builder; it builds nothing.</summary>
public sealed class DocsBuilder : IHostApplicationBuilder, ITraconBuilder
{
    public IDictionary<object, object> Properties { get; } = new Dictionary<object, object>();

    public IConfigurationManager Configuration { get; } = null!;

    public IHostEnvironment Environment { get; } = null!;

    public ILoggingBuilder Logging { get; } = null!;

    public IMetricsBuilder Metrics { get; } = null!;

    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>What a page calls after configuring the builder; the harness never runs it.</summary>
    public WebApplication Build() => throw new NotSupportedException();

    public void ConfigureContainer<TContainerBuilder>(
        IServiceProviderFactory<TContainerBuilder> factory,
        Action<TContainerBuilder>? configure = null)
        where TContainerBuilder : notnull
        => throw new NotSupportedException();
}
