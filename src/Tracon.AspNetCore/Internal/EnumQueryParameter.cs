using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tracon;

/// <summary>
/// Refuses a number that names no member of an enum query parameter.
/// </summary>
/// <remarks>
/// The binder parses an enum parameter with <c>Enum.TryParse</c>, which accepts any integer, so <c>?bucket=99</c> binds
/// to a value that is in no member. Text that names no member never reaches a handler (the binder rejects it and the
/// parameter middleware answers 400); the number is the same mistake spelled differently. A handler whose value feeds a
/// <c>switch</c> that throws on the unknown member must call this before it uses the value, or the request ends in a 500.
/// </remarks>
internal static class EnumQueryParameter
{
    /// <summary>The 400 problem for an undefined value, or <see langword="null"/> when the value is absent or defined.</summary>
    /// <typeparam name="TEnum">The enum type of the parameter.</typeparam>
    /// <param name="name">The parameter name as the caller wrote it.</param>
    /// <param name="value">The bound value.</param>
    /// <returns>The problem to return, or <see langword="null"/> to go on.</returns>
    public static ProblemHttpResult? Refuse<TEnum>(string name, TEnum? value)
        where TEnum : struct, Enum
        => value is { } actual && !Enum.IsDefined(actual)
            ? TypedResults.Problem(
                title: JsonBindingProblemMiddleware.ParameterProblemTitle,
                detail: $"'{name}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.",
                statusCode: StatusCodes.Status400BadRequest)
            : null;
}
