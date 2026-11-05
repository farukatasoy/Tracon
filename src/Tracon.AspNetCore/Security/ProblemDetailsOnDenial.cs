using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Extensions that make a refused role or policy check answer with a <c>ProblemDetails</c> body.
/// </summary>
public static class TraconProblemDetailsOnDenialBuilderExtensions
{
    /// <summary>
    /// Makes a <c>401</c> or <c>403</c> that the ASP.NET Core authorization middleware writes for a Tracon
    /// endpoint carry an <c>application/problem+json</c> body.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Without this call the answer to a refused role or policy is empty: the status code and, when the
    /// authentication scheme sets one, the <c>WWW-Authenticate</c> header, but no body and no content type.
    /// Tracon's own refusals (a missing or wrong token, a missing API key scope, a foreign tenant) always carry a
    /// body, so the same API otherwise speaks two shapes.
    /// </para>
    /// <para>
    /// The call wraps the authorization middleware's result handler and changes nothing else. The default handler
    /// runs first, so the status code, the <c>WWW-Authenticate</c> header, a cookie scheme's redirect and the
    /// scheme's challenge events are unchanged. Afterwards, when the answer is a <c>401</c> or <c>403</c> with
    /// nothing written yet, the wrapper writes the body. It never turns a refusal into anything else: a failure
    /// while writing the body is logged and the status stays.
    /// </para>
    /// <para>
    /// The body is the same for every caller and every tenant: a fixed title and a fixed sentence. It names no
    /// role, policy, tenant or user, and does not echo the credential.
    /// </para>
    /// <para>
    /// Only Tracon's management API, AG-UI, MCP server, A2A and voice endpoints are touched. The browser pages
    /// (the console shell and the OAuth callback) and the consumer's own endpoints answer as before. The
    /// OpenAI-compatible endpoints keep their own error shape, <c>{"error":{"message":…,"type":…}}</c>, which is what
    /// the OpenAI SDKs parse.
    /// </para>
    /// <para>
    /// Order does not matter: the call may come before or after <c>AddAuthorization()</c>. A handler the consumer
    /// registered for <see cref="IAuthorizationMiddlewareResultHandler"/> with <c>AddSingleton</c> or <c>Replace</c> wins and
    /// is left alone; one added with <c>TryAddSingleton</c> after this call finds a registration and is skipped. Calling it
    /// twice is the same as calling it once.
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services.AddTracon()
    ///     .UseProblemDetailsOnDenial();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseProblemDetailsOnDenial(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var registered = services.LastOrDefault(static descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));

        // Only ASP.NET Core's own handler, or no handler yet, is wrapped. Anything else is the consumer's choice
        // (or an earlier call of this method) and stays.
        if (registered is null)
        {
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(Wrap);
        }
        else if (registered.ImplementationType == typeof(AuthorizationMiddlewareResultHandler))
        {
            services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler>(Wrap));
        }

        return builder;
    }

    private static ProblemDetailsOnDenialHandler Wrap(IServiceProvider provider)
        => new(
            new AuthorizationMiddlewareResultHandler(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("Tracon.ProblemDetailsOnDenial"));
}

/// <summary>
/// Endpoint metadata that marks a Tracon endpoint as one whose refused role or policy check may get a body.
/// </summary>
/// <remarks>
/// Put on the route groups Tracon maps, so the authorization handler (one per process) writes a body only for
/// Tracon's own endpoints and leaves the consumer's endpoints as they were.
/// </remarks>
internal sealed class ProblemDetailsOnDenialMarker
{
    /// <summary>The only instance; the marker carries no data.</summary>
    public static readonly ProblemDetailsOnDenialMarker Instance = new();

    private ProblemDetailsOnDenialMarker()
    {
    }
}

/// <summary>
/// Endpoint metadata that makes a refusal on an OpenAI-compatible endpoint use the OpenAI error shape.
/// </summary>
/// <remarks>
/// The OpenAI-compatible endpoints answer every error as <c>{"error":{"message":…,"type":…}}</c>, which is what the OpenAI
/// SDKs parse and what the published document lists for their <c>403</c>. A refused role is the one error that reaches
/// them before their own code, so it follows the same rule.
/// </remarks>
internal sealed class OpenAIDenialShapeMarker
{
    /// <summary>The only instance; the marker carries no data.</summary>
    public static readonly OpenAIDenialShapeMarker Instance = new();

    private OpenAIDenialShapeMarker()
    {
    }
}

/// <summary>
/// Wraps an authorization result handler and adds a <c>ProblemDetails</c> body to the <c>401</c> or <c>403</c> it wrote.
/// </summary>
internal sealed class ProblemDetailsOnDenialHandler(IAuthorizationMiddlewareResultHandler inner, ILogger logger)
    : IAuthorizationMiddlewareResultHandler
{
    internal const string UnauthorizedTitle = "Authentication required";

    internal const string UnauthorizedDetail = "Valid credentials are required for this endpoint.";

    internal const string ForbiddenTitle = "Access denied";

    internal const string ForbiddenDetail = "The caller is not allowed to perform this operation.";

    internal const string OpenAIUnauthorizedType = "authentication_error";

    internal const string OpenAIForbiddenType = "permission_error";

    /// <inheritdoc />
    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        // An allowed request goes straight through: the wrapper must not be a step between the caller and the endpoint.
        return authorizeResult.Succeeded
            ? inner.HandleAsync(next, context, policy, authorizeResult)
            : HandleRefusalAsync(next, context, policy, authorizeResult);
    }

    private async Task HandleRefusalAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        // The default handler first: the scheme writes its own header, redirect or event. An exception from here
        // (no authentication scheme is configured, for one) is the consumer's setup problem and goes up unchanged.
        await inner.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);

        var endpoint = context.GetEndpoint();

        if (endpoint?.Metadata.GetMetadata<ProblemDetailsOnDenialMarker>() is null)
        {
            return;
        }

        var response = context.Response;

        if (response.HasStarted
            || response.StatusCode is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
            || response.ContentLength is > 0
            || response.ContentType is not null
            || context.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        var status = response.StatusCode;

        try
        {
            var unauthorized = status == StatusCodes.Status401Unauthorized;

            IResult answer = endpoint.Metadata.GetMetadata<OpenAIDenialShapeMarker>() is not null
                ? OpenAICompatSupport.Error(
                    status,
                    unauthorized ? UnauthorizedDetail : ForbiddenDetail,
                    unauthorized ? OpenAIUnauthorizedType : OpenAIForbiddenType)
                : unauthorized
                    ? TypedResults.Problem(title: UnauthorizedTitle, detail: UnauthorizedDetail, statusCode: status)
                    : TypedResults.Problem(title: ForbiddenTitle, detail: ForbiddenDetail, statusCode: status);

            await answer.ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller left while the refusal was being written; there is nobody to answer.
        }
        catch (Exception exception)
        {
            // The refusal stands. A body that could not be written must never turn into a success or a failure of the server.
            logger.LogWarning(
                exception,
                "The {Status} refusal of {Path} could not get a problem body; the status is unchanged.",
                status,
                context.Request.Path.Value);
        }
    }
}
