using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Tracon;

/// <summary>
/// Refuses a number that names no member of an enum parameter, for every endpoint of the group it is added to.
/// </summary>
/// <remarks>
/// <para>
/// The binder parses an enum parameter with <c>Enum.TryParse</c>, which accepts any integer, so <c>?status=99</c> binds
/// to a value that is in no member. Text that names no member never reaches a handler (the binder rejects it and the
/// parameter middleware answers 400); the number is the same mistake spelled differently. A handler whose value feeds a
/// <c>switch</c> that throws on the unknown member ended in a 500, and one that filters on it answered 200 with an
/// empty page.
/// </para>
/// <para>
/// The check is one endpoint filter factory on the group, so an endpoint added tomorrow is covered without its author
/// knowing the binder's rule: the factory reads the handler's own signature once, when the endpoint is built, and an
/// endpoint with no enum parameter pays nothing at run time.
/// </para>
/// </remarks>
internal static class EnumQueryParameter
{
    /// <summary>The filter factory to add to a route group with <c>AddEndpointFilterFactory</c>.</summary>
    /// <param name="context">What the framework knows about the handler.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>The pipeline, with the check in front when the handler has an enum parameter.</returns>
    public static EndpointFilterDelegate Filter(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var parameters = context.MethodInfo.GetParameters();
        List<(int Index, Type Type, string Name)>? enums = null;

        for (var index = 0; index < parameters.Length; index++)
        {
            var type = Nullable.GetUnderlyingType(parameters[index].ParameterType) ?? parameters[index].ParameterType;

            if (!type.IsEnum)
            {
                continue;
            }

            var name = parameters[index].GetCustomAttribute<FromQueryAttribute>()?.Name ?? parameters[index].Name;

            if (name is { Length: > 0 })
            {
                (enums ??= []).Add((index, type, name));
            }
        }

        if (enums is null)
        {
            return next;
        }

        var checks = enums.ToArray();

        return invocationContext =>
        {
            foreach (var (index, type, name) in checks)
            {
                if (invocationContext.Arguments[index] is { } value && !IsMember(type, value))
                {
                    return ValueTask.FromResult<object?>(Problem(name, type));
                }
            }

            return next(invocationContext);
        };
    }

    private static bool IsMember(Type type, object value)
    {
        if (Enum.IsDefined(type, value))
        {
            return true;
        }

        // A flags enum names a combination by the members it is made of.
        if (!type.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            return false;
        }

        var all = 0UL;

        foreach (var member in Enum.GetValues(type))
        {
            all |= Convert.ToUInt64(member, System.Globalization.CultureInfo.InvariantCulture);
        }

        var bits = unchecked((ulong)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));

        return (bits & ~all) == 0;
    }

    private static ProblemHttpResult Problem(string name, Type type)
        => TypedResults.Problem(
            title: JsonBindingProblemMiddleware.ParameterProblemTitle,
            detail: $"'{name}' must be one of: {string.Join(", ", Enum.GetNames(type))}.",
            statusCode: StatusCodes.Status400BadRequest);
}
