using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>Where the text of a store column comes from, and what keeps it inside the column.</summary>
internal enum ColumnOrigin
{
    /// <summary>A client chooses it and Tracon refuses a value over the limit at the input.</summary>
    Client,

    /// <summary>It is copied from another column that is limited at its own input.</summary>
    Follows,

    /// <summary>A client chooses it, and another rule already keeps it inside the column.</summary>
    Elsewhere,

    /// <summary>Tracon makes it (a hash, a counter, a fixed word), so its width is known.</summary>
    Generated,

    /// <summary>The operator chooses it, in code or in configuration, not a client of the API.</summary>
    Operator,

    /// <summary>It is the name of the authenticated caller, taken from the identity and not from a request field.</summary>
    Principal,

    /// <summary>A model provider, a tool, an MCP server or the workflow engine chose it.</summary>
    External,
}

/// <summary>One text column of the SQL Server schema, and the rule that keeps a value inside it.</summary>
/// <param name="Column">The column as <c>table.column</c>, or <c>*.column</c> for a column every table that has it treats alike.</param>
/// <param name="Origin">Where the text comes from.</param>
/// <param name="Limit">For <see cref="ColumnOrigin.Client"/>: the longest value the input accepts, at most the column's width.</param>
/// <param name="Note">For every other origin: the reason no input needs a limit of its own.</param>
internal sealed record ClientTextColumn(string Column, ColumnOrigin Origin, int Limit, string Note)
{
    /// <summary>Gets the route parameters whose value is stored in this column.</summary>
    public IReadOnlyList<string> Route { get; init; } = [];

    /// <summary>Gets the request body properties whose value is stored in this column.</summary>
    public IReadOnlyList<(Type Type, string Property)> Body { get; init; } = [];

    /// <summary>Gets the form file fields whose file name is stored in this column.</summary>
    public IReadOnlyList<string> Form { get; init; } = [];
}

/// <summary>
/// The text a client chooses and the width of the column that stores it, in one table; and the two checks that read it.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server holds an identifier in <c>nvarchar(200)</c> and refuses a longer value, while the other stores keep it. A
/// client that sent 201 characters got a truncation error, a <c>503</c> or a truncated row on one store and a quiet
/// <c>200</c> on another. The limit is therefore the narrowest engine's, on every store, as it is for an idempotency key
/// and a session identifier: a value over it is a <c>400</c> that names the field, before anything is written.
/// </para>
/// <para>
/// The table lists <strong>every</strong> narrow text column of the SQL Server schema and says where its text comes from.
/// A column a client fills names the inputs that carry the text, and those inputs are checked from this one list: a route
/// parameter by <see cref="RouteFilter"/> and a body property by <see cref="Guard"/>, so an endpoint added tomorrow that
/// reads one of these types is covered without its author knowing the rule. A column nothing in a request fills says
/// so, with the reason. A migration that adds a text column without a row here fails
/// <c>ClientTextLimitsTests</c>, so the question "who chooses this text, and how long can it be" is asked once for each
/// column and not left to the first client that sends too much.
/// </para>
/// </remarks>
internal static class ClientTextLimits
{
    /// <summary>The width of the narrowest column that holds an identifier or a name: <c>nvarchar(200)</c>.</summary>
    public const int Identifier = 200;

    /// <summary>The width of a column that holds a configuration name, a path or an address: <c>nvarchar(400)</c>.</summary>
    public const int Setting = 400;

    /// <summary>The width of a column that holds a URL: <c>nvarchar(2000)</c>.</summary>
    public const int Address = 2000;

    private static readonly Dictionary<Type, (string Property, int Limit)[]> BodyLimits = BuildBodyLimits();

    private static readonly Dictionary<string, int> RouteLimits = BuildRouteLimits();

    private static readonly Dictionary<string, int> FormLimits = BuildFormLimits();

    /// <summary>Gets every text column of the schema.</summary>
    public static IReadOnlyList<ClientTextColumn> Columns { get; } = ClientTextColumns.All;

    /// <summary>The contract customization to add to the request-body options.</summary>
    /// <param name="typeInfo">The contract being built.</param>
    /// <remarks>
    /// A property over its limit throws <see cref="JsonException"/>, which every body reader already turns into the
    /// <c>400</c> problem of an invalid body. The message names the property and the limit, never the value.
    /// </remarks>
    public static void Guard(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (typeInfo.Kind != JsonTypeInfoKind.Object || !BodyLimits.TryGetValue(typeInfo.Type, out var limits))
        {
            return;
        }

        var guarded = new List<(JsonPropertyInfo Property, int Limit)>();

        foreach (var (name, limit) in limits)
        {
            // The member name when the contract knows its member; the written name otherwise (a contract a source
            // generator produced), whose case the naming policy changed.
            var property = typeInfo.Properties.FirstOrDefault(
                candidate => candidate.AttributeProvider is System.Reflection.PropertyInfo member
                    ? string.Equals(member.Name, name, StringComparison.Ordinal)
                    : string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

            if (property?.Get is not null)
            {
                guarded.Add((property, limit));
            }
        }

        if (guarded.Count == 0)
        {
            return;
        }

        var previous = typeInfo.OnDeserialized;

        typeInfo.OnDeserialized = instance =>
        {
            previous?.Invoke(instance);

            foreach (var (property, limit) in guarded)
            {
                if (property.Get!(instance) is string { } text && text.Length > limit)
                {
                    throw new JsonException(
                        $"The property '{property.Name}' can be at most {limit} characters; it is {text.Length} characters long.");
                }
            }
        };
    }

    /// <summary>
    /// The endpoint filter that refuses a route value over its limit, on a request that writes. Add it to the route group
    /// with <c>AddEndpointFilter</c>.
    /// </summary>
    /// <param name="context">The invocation.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>The <c>400</c> problem, or the result of the rest of the pipeline.</returns>
    /// <remarks>
    /// Only <c>PUT</c> creates a row from a name in the path. A <c>GET</c>, a <c>DELETE</c> and a <c>POST</c> name a row
    /// that exists, so a name no row can have is the <c>404</c> it always was.
    /// </remarks>
    public static ValueTask<object?> RouteFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.HttpContext.Request;

        if (HttpMethods.IsPut(request.Method))
        {
            foreach (var (name, value) in request.RouteValues)
            {
                if (RouteLimits.TryGetValue(name, out var limit)
                    && value is string { } text
                    && text.Length > limit)
                {
                    return ValueTask.FromResult<object?>(TooLong($"The path parameter '{name}'", limit, text.Length));
                }
            }
        }

        return next(context);
    }

    /// <summary>Checks the file name of a form file against the limit of its column.</summary>
    /// <param name="field">The form field that carries the file.</param>
    /// <param name="fileName">The file name the client sent, if any.</param>
    /// <returns>The <c>400</c> to answer with, or <see langword="null"/> when the name fits.</returns>
    /// <exception cref="InvalidOperationException">The field has no row in the table.</exception>
    public static ProblemHttpResult? CheckFileName(string field, string? fileName)
    {
        if (!FormLimits.TryGetValue(field, out var limit))
        {
            throw new InvalidOperationException($"The form field '{field}' has no row in the text column table.");
        }

        return fileName is { } name && name.Length > limit
            ? TooLong($"The file name of the '{field}' field", limit, name.Length)
            : null;
    }

    /// <summary>Gets the limit of a route parameter, or <see langword="null"/> when the table has none for it.</summary>
    /// <param name="parameter">The route parameter name.</param>
    /// <returns>The limit.</returns>
    public static int? RouteLimit(string parameter)
        => RouteLimits.TryGetValue(parameter, out var limit) ? limit : null;

    private static ProblemHttpResult TooLong(string subject, int limit, int length)
        => TypedResults.Problem(
            title: "Value too long",
            detail: $"{subject} can be at most {limit} characters; it is {length} characters long.",
            statusCode: StatusCodes.Status400BadRequest);

    private static Dictionary<Type, (string Property, int Limit)[]> BuildBodyLimits()
        => ClientTextColumns.All
            .Where(static column => column.Origin == ColumnOrigin.Client)
            .SelectMany(static column => column.Body.Select(body => (body.Type, body.Property, column.Limit)))
            .GroupBy(static entry => (entry.Type, entry.Property))
            .Select(static group => (group.Key.Type, group.Key.Property, Limit: group.Min(static entry => entry.Limit)))
            .GroupBy(static entry => entry.Type)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static entry => (entry.Property, entry.Limit)).ToArray());

    private static Dictionary<string, int> BuildRouteLimits()
        => ClientTextColumns.All
            .Where(static column => column.Origin == ColumnOrigin.Client)
            .SelectMany(static column => column.Route.Select(route => (Route: route, column.Limit)))
            .GroupBy(static entry => entry.Route, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Min(static entry => entry.Limit), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int> BuildFormLimits()
        => ClientTextColumns.All
            .Where(static column => column.Origin == ColumnOrigin.Client)
            .SelectMany(static column => column.Form.Select(field => (Field: field, column.Limit)))
            .GroupBy(static entry => entry.Field, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Min(static entry => entry.Limit), StringComparer.Ordinal);
}
