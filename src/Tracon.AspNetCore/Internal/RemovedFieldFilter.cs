using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// Endpoint filter that refuses a JSON body that still carries a field the API
/// removed, and names what replaces it.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 System.Text.Json drops a field it does not know, without a word. A client
/// written for the removed field would therefore save its record with
/// the value silently gone, and learn about it from a runtime failure far from
/// the cause (for a credential: a <c>401</c> on the first call). The filter turns that
/// silence into a <c>400</c> at the save. It is a deliberate exception: every
/// other unknown field is still ignored, and the rule never becomes
/// "reject unknown fields" for the whole API.
/// </para>
/// <para>
/// The endpoints bind their body by hand (<see cref="RequestBodyBinding"/>), so
/// the filter buffers the request, looks at the TOP-LEVEL property names only,
/// and rewinds the stream for the binder. The name is matched case-insensitively
/// because the binder does the same: <c>AuthorizationConfigurationKey</c> was
/// accepted before the field was removed. A body that is not valid JSON or not an
/// object passes through untouched, so the binder gives its usual answer. The
/// filter is added after the authorization requirements: an unauthorized
/// caller gets <c>401</c> or <c>403</c>, never a hint about the contract.
/// </para>
/// </remarks>
internal sealed class RemovedFieldFilter : IEndpointFilter
{
    /// <summary>The <c>title</c> of the refusal.</summary>
    internal const string ProblemTitle = "Removed field";

    private readonly string _fieldName;
    private readonly string _replacement;

    /// <summary>Creates a filter for one removed field.</summary>
    /// <param name="fieldName">The wire name of the removed field, for example <c>authorizationConfigurationKey</c>.</param>
    /// <param name="replacement">What to send instead, as the sentence names it, for example <c>headerConfigurationKeys["Authorization"]</c>.</param>
    /// <exception cref="ArgumentException">A parameter is <see langword="null"/> or white space.</exception>
    public RemovedFieldFilter(string fieldName, string replacement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacement);

        _fieldName = fieldName;
        _replacement = replacement;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.HttpContext.Request;

        if (request.HasJsonContentType() &&
            await CarriesFieldAsync(request, context.HttpContext.RequestAborted).ConfigureAwait(false))
        {
            return Results.Problem(
                title: ProblemTitle,
                detail: $"'{_fieldName}' was removed in 1.0. Use {_replacement}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context).ConfigureAwait(false);
    }

    private async ValueTask<bool> CarriesFieldAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();

        try
        {
            using var document = await JsonDocument
                .ParseAsync(request.Body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, _fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            // Not JSON: the binder reports it with the contract's own error.
            return false;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
