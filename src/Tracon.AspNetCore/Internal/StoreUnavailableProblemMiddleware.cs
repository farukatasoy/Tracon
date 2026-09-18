using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Turns a persistence failure — the provider's own <see cref="DbException"/> —
/// into a <c>503</c> <c>ProblemDetails</c> that says WHY the store could not
/// answer, instead of letting a raw provider exception reach the consumer's
/// global handler as a bodyless <c>500</c>.
/// </summary>
/// <remarks>
/// <para>
/// The information was never missing, only unreachable: while a write returned
/// its opaque <c>500</c>, the same application answered <c>/health</c> with
/// <c>503 Unhealthy</c> and <c>/api/diagnostics</c> with
/// <c>migrationsUpToDate: false</c> and the pending-migration count. This
/// middleware asks <see cref="ISqlPersistenceDiagnostics"/> the same question
/// the diagnostics endpoint asks, and puts the answer in the response.
/// </para>
/// <para>
/// <strong>The two causes are told apart, deliberately.</strong> A schema that
/// is not current stays broken until an operator applies the migrations; a
/// database that cannot be reached is usually transient and worth retrying.
/// A client that cannot tell them apart either retries forever or gives up on
/// a condition that would have cleared.
/// </para>
/// <para>
/// <strong>What is never written:</strong> the schema name and the statement
/// text. That is the defensible half of the old opacity and it is kept —
/// The provider's exception message carries both on every provider, so the
/// message is not forwarded; it is logged instead.
/// </para>
/// <para>
/// Tracon is a library and cannot depend on whether the consumer called
/// <c>AddProblemDetails()</c> or <c>UseExceptionHandler()</c>, so the mapping is
/// attached from <see cref="TraconEndpointRouteBuilderExtensions.MapTracon"/>
/// and applies only to endpoints tagged <c>Tracon</c> — the same shape as
/// <see cref="JsonBindingProblemMiddleware"/>.
/// </para>
/// </remarks>
internal static class StoreUnavailableProblemMiddleware
{
    /// <summary>The title used when the store is reachable but its schema is behind.</summary>
    internal const string SchemaProblemTitle = "Database schema is not current";

    /// <summary>The title used when the store could not be reached at all.</summary>
    internal const string UnreachableProblemTitle = "Persistence store unavailable";

    public static async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (DbException exception) when (
            !httpContext.Response.HasStarted &&
            IsTraconEndpoint(httpContext))
        {
            // 🚨 The provider's message names the schema and the statement. It
            // goes to the log, where the operator already looks for the detail
            // of a masked error, and never to the response.
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(StoreUnavailableProblemMiddleware).FullName!)
                .LogError(
                    exception,
                    "A Tracon endpoint could not reach the persistence store. Responding {StatusCode}.",
                    StatusCodes.Status503ServiceUnavailable);

            var (title, detail) = await DescribeAsync(httpContext).ConfigureAwait(false);

            await TypedResults.Problem(
                    title: title,
                    detail: detail,
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                .ExecuteAsync(httpContext)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Asks the active SQL provider which of the two causes this is.</summary>
    /// <remarks>
    /// The probe itself can fail — the connection that just failed is the one it
    /// would use — so a failure here is not an error: it means the store could
    /// not be reached, which is the answer the caller needs anyway.
    /// </remarks>
    private static async ValueTask<(string Title, string Detail)> DescribeAsync(HttpContext httpContext)
    {
        var diagnostics = httpContext.RequestServices.GetService<ISqlPersistenceDiagnostics>();

        if (diagnostics is not null)
        {
            try
            {
                var snapshot = await diagnostics
                    .GetSnapshotAsync(httpContext.RequestAborted)
                    .ConfigureAwait(false);

                if (snapshot.CanConnect && snapshot.PendingMigrations.Count > 0)
                {
                    return (
                        SchemaProblemTitle,
                        $"The persistence store is reachable but {snapshot.PendingMigrations.Count} migration(s) " +
                        "have not been applied, so the table this request needs does not exist yet. Apply the " +
                        "migrations with the 'tracon migrate' command, or start the application with " +
                        "AutoApplyMigrations enabled. Retrying this request will not help until then.");
                }
            }
            catch (DbException)
            {
                // Falls through to the unreachable answer below.
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
        }

        return (
            UnreachableProblemTitle,
            "The persistence store did not answer this request. This is usually transient; retry. " +
            "'/api/diagnostics' and the health endpoint report the store's current state.");
    }

    private static bool IsTraconEndpoint(HttpContext httpContext)
        => httpContext.GetEndpoint()?.Metadata.GetMetadata<ITagsMetadata>() is { } tags &&
           tags.Tags.Contains("Tracon", StringComparer.Ordinal);
}
