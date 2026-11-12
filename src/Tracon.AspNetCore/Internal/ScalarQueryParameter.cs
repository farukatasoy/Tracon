using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Tracon;

/// <summary>
/// Names the parameter and the form it takes when a number, flag, date or GUID in the query cannot be read, for every
/// endpoint of the group it is added to.
/// </summary>
/// <remarks>
/// <para>
/// The request delegate generator does not throw for a nullable parameter that fails to parse, in any environment: it marks
/// the request <c>400</c> and goes on, so no exception reaches the parameter middleware and the answer was a bare <c>400</c>
/// with no body. <c>?startedAfter=yesterday</c> told the caller nothing about which parameter was wrong. The filter is the
/// sibling of <see cref="EnumQueryParameter"/>, which does the same for enums, and it uses the same signal: the status is
/// already <c>400</c> and the response has not begun.
/// </para>
/// <para>
/// The check is one endpoint filter factory on the group, so an endpoint added tomorrow is covered without its author
/// knowing the binder's rule: the factory reads the handler's own signature once, when the endpoint is built, and an
/// endpoint with no such parameter pays nothing at run time. The filter speaks only when the binder already failed. It
/// reads the text as the binder does (the invariant culture), and a text it can read but the binder refused is left to the
/// bare <c>400</c>.
/// </para>
/// </remarks>
internal static class ScalarQueryParameter
{
    private const string IsoDateTimeExample = "2026-11-12T08:00:00Z";

    /// <summary>The filter factory to add to a route group with <c>AddEndpointFilterFactory</c>.</summary>
    /// <param name="context">What the framework knows about the handler.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>The pipeline, with the check in front when the handler has such a parameter.</returns>
    public static EndpointFilterDelegate Filter(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        List<(Type Type, string Name)>? scalars = null;

        foreach (var parameter in context.MethodInfo.GetParameters())
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

            if (!IsScalar(type) || ComesFromElsewhere(parameter))
            {
                continue;
            }

            var name = parameter.GetCustomAttribute<FromQueryAttribute>()?.Name
                ?? parameter.GetCustomAttribute<FromRouteAttribute>()?.Name
                ?? parameter.Name;

            if (name is { Length: > 0 })
            {
                (scalars ??= []).Add((type, name));
            }
        }

        if (scalars is null)
        {
            return next;
        }

        var checks = scalars.ToArray();

        return invocationContext =>
        {
            var httpContext = invocationContext.HttpContext;

            if (httpContext.Response.StatusCode == StatusCodes.Status400BadRequest && !httpContext.Response.HasStarted)
            {
                foreach (var (type, name) in checks)
                {
                    if (EnumQueryParameter.RawText(httpContext, name) is { Length: > 0 } text && !CanRead(type, text))
                    {
                        return ValueTask.FromResult<object?>(OpenAIRefusalShape.Refuse(
                            httpContext,
                            JsonBindingProblemMiddleware.ParameterProblemTitle,
                            $"'{name}' must be {Describe(type)}.",
                            StatusCodes.Status400BadRequest));
                    }
                }
            }

            return next(invocationContext);
        };
    }

    private static bool ComesFromElsewhere(ParameterInfo parameter)
        => parameter.IsDefined(typeof(FromServicesAttribute), inherit: false)
           || parameter.IsDefined(typeof(FromBodyAttribute), inherit: false)
           || parameter.IsDefined(typeof(FromHeaderAttribute), inherit: false)
           || parameter.IsDefined(typeof(FromFormAttribute), inherit: false);

    private static bool IsScalar(Type type)
        => type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
           || type == typeof(double) || type == typeof(float) || type == typeof(decimal)
           || type == typeof(bool) || type == typeof(Guid)
           || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(TimeOnly)
           || type == typeof(TimeSpan);

    private static bool CanRead(Type type, string text)
    {
        var culture = CultureInfo.InvariantCulture;

        if (type == typeof(int))
        {
            return int.TryParse(text, culture, out _);
        }

        if (type == typeof(long))
        {
            return long.TryParse(text, culture, out _);
        }

        if (type == typeof(short))
        {
            return short.TryParse(text, culture, out _);
        }

        if (type == typeof(byte))
        {
            return byte.TryParse(text, culture, out _);
        }

        if (type == typeof(double))
        {
            return double.TryParse(text, culture, out _);
        }

        if (type == typeof(float))
        {
            return float.TryParse(text, culture, out _);
        }

        if (type == typeof(decimal))
        {
            return decimal.TryParse(text, culture, out _);
        }

        if (type == typeof(bool))
        {
            return bool.TryParse(text, out _);
        }

        if (type == typeof(Guid))
        {
            return Guid.TryParse(text, out _);
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.TryParse(text, culture, out _);
        }

        if (type == typeof(DateTime))
        {
            return DateTime.TryParse(text, culture, out _);
        }

        if (type == typeof(DateOnly))
        {
            return DateOnly.TryParse(text, culture, out _);
        }

        if (type == typeof(TimeOnly))
        {
            return TimeOnly.TryParse(text, culture, out _);
        }

        return TimeSpan.TryParse(text, culture, out _);
    }

    private static string Describe(Type type)
    {
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
        {
            return "a whole number";
        }

        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            return "a number";
        }

        if (type == typeof(bool))
        {
            return "true or false";
        }

        if (type == typeof(Guid))
        {
            return "a GUID";
        }

        if (type == typeof(DateTimeOffset) || type == typeof(DateTime))
        {
            return $"a date and time in ISO 8601 format, for example {IsoDateTimeExample}";
        }

        if (type == typeof(DateOnly))
        {
            return "a date in the form yyyy-MM-dd";
        }

        return type == typeof(TimeOnly) ? "a time of day such as 08:30:00" : "a duration such as 01:30:00";
    }
}
