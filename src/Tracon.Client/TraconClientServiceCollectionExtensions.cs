using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.Client.Generated;

namespace Tracon.Client;

/// <summary>Extensions that register the typed management client with dependency injection.</summary>
public static class TraconClientServiceCollectionExtensions
{
    /// <summary>Registers <see cref="TraconApiClient"/> as a singleton.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">The settings the client is built from.</param>
    /// <returns>The service collection, for further configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="TraconClientOptions.BaseAddress"/> was not set.</exception>
    /// <remarks>
    /// <c>IHttpClientFactory</c> is not used because it would add
    /// <c>Microsoft.Extensions.Http</c> to the dependency graph for a package
    /// that otherwise takes none. The <see cref="HttpClient"/> is created once
    /// and reused for the application's lifetime, which is the pattern
    /// Microsoft recommends when not going through the factory.
    /// <example>
    /// <code>
    /// builder.Services.AddTraconClient(options =>
    /// {
    ///     options.BaseAddress = new Uri("https://example.com/tracon/");
    ///     options.Token = builder.Configuration["Tracon:Token"];
    /// });
    /// </code>
    /// </example>
    /// </remarks>
    public static IServiceCollection AddTraconClient(
        this IServiceCollection services,
        Action<TraconClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TraconClientOptions();
        configure(options);

        if (options.BaseAddress is null)
        {
            throw new ArgumentException(
                $"{nameof(TraconClientOptions.BaseAddress)} must be set to the application root " +
                "plus the MapTracon prefix, for example https://example.com/tracon/.",
                nameof(configure));
        }

        var baseAddress = options.BaseAddress.AbsoluteUri.EndsWith('/')
            ? options.BaseAddress
            : new Uri(options.BaseAddress.AbsoluteUri + "/", UriKind.Absolute);
        var token = options.Token;

        services.TryAddSingleton(_ =>
        {
            var httpClient = new HttpClient { BaseAddress = baseAddress };

            if (!string.IsNullOrEmpty(token))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return new TraconApiClient(httpClient);
        });

        return services;
    }
}
