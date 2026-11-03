using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Turns a value the SQL store refused — a text over its column, a number out of its range — into a <c>400</c>
/// <c>ProblemDetails</c> instead of the <c>503</c> that says the store could not answer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StoreFailure"/> counts every provider exception as "the store did not answer", and
/// <see cref="StoreUnavailableProblemMiddleware"/> makes a <c>503</c> of it. For a refused value that is the wrong
/// answer twice: the store DID answer, and a client that retries a <c>503</c> sends the same value again and gets the same
/// refusal. This middleware sits inside the <c>503</c> one, so the exception reaches it first; whatever it does not
/// recognize, it leaves alone.
/// </para>
/// <para>
/// <strong>The body says nothing about the value.</strong> The provider's message names the table, the column and, on
/// SQL Server, the value that was cut — which is the caller's data and can be a secret that was pasted into the wrong
/// field. Neither the message nor the exception is forwarded or logged; the response carries a fixed text and the log
/// carries the kind of refusal. A caller who needs the field named gets that from validating the field, not from reading
/// the provider's message.
/// </para>
/// <para>
/// Only the exception that was caught is classified, not the exceptions inside it: a <see cref="TraconException"/> is
/// already Tracon's own answer. The mapping applies only to endpoints tagged <c>Tracon</c> and only while the response has
/// not started, the same shape as <see cref="StoreUnavailableProblemMiddleware"/>.
/// </para>
/// </remarks>
internal static class StoreDataFaultProblemMiddleware
{
    /// <summary>The title used when a text did not fit the column that holds it.</summary>
    internal const string TextTooLongTitle = "Value too long for the store";

    /// <summary>The title used when a number did not fit the column that holds it.</summary>
    internal const string NumberOutOfRangeTitle = "Number out of range for the store";

    /// <summary>The detail used when a text did not fit the column that holds it.</summary>
    internal const string TextTooLongDetail =
        "A value in the request is longer than the persistence store accepts. Sending the same request again " +
        "gives the same answer; shorten the value.";

    /// <summary>The detail used when a number did not fit the column that holds it.</summary>
    internal const string NumberOutOfRangeDetail =
        "A number in the request is outside the range the persistence store accepts. Sending the same request " +
        "again gives the same answer; use a smaller number.";

    public static async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            !httpContext.Response.HasStarted &&
            IsTraconEndpoint(httpContext) &&
            TryClassify(httpContext, exception, out var fault))
        {
            // 🚨 No exception object and no message: the provider's text carries the value that was refused.
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(StoreDataFaultProblemMiddleware).FullName!)
                .LogWarning(
                    "The persistence store refused a value ({Fault}, {ExceptionType}) on {Endpoint}. Responding {StatusCode}.",
                    fault,
                    exception.GetType().Name,
                    httpContext.GetEndpoint()?.DisplayName,
                    StatusCodes.Status400BadRequest);

            var (title, detail) = fault == StoreDataFault.TextTooLong
                ? (TextTooLongTitle, TextTooLongDetail)
                : (NumberOutOfRangeTitle, NumberOutOfRangeDetail);

            await TypedResults.Problem(
                    title: title,
                    detail: detail,
                    statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(httpContext)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Asks the active SQL provider, if there is one, whether it refused a value.</summary>
    /// <remarks>Persistence in memory registers no classifier: it has no column to overflow.</remarks>
    private static bool TryClassify(HttpContext httpContext, Exception exception, out StoreDataFault fault)
    {
        var classifier = httpContext.RequestServices.GetService<IStoreDataFaultClassifier>();

        if (classifier is null)
        {
            fault = default;
            return false;
        }

        return classifier.TryClassify(exception, out fault);
    }

    private static bool IsTraconEndpoint(HttpContext httpContext)
        => httpContext.GetEndpoint()?.Metadata.GetMetadata<ITagsMetadata>() is { } tags &&
           tags.Tags.Contains("Tracon", StringComparer.Ordinal);
}
