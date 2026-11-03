using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconTenancyOptions"/> during application startup.
/// </summary>
/// <remarks>
/// Written by hand for the same reason as <c>TraconOptionsValidator</c>: no reflection-based validation.
/// </remarks>
internal sealed class TraconTenancyOptionsValidator : IValidateOptions<TraconTenancyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconTenancyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        for (var i = 0; i < options.AllowedTenants.Count; i++)
        {
            // The entry is the operator's text, and an entry no request can match is a mistake worth stopping for:
            // a tenant that is on the list in the operator's mind and refused on every request. A request carries
            // at most the length HttpTenantContext accepts, so a longer entry is as unreachable as one with a
            // character outside the alphabet.
            if (!HttpTenantContext.IsValidTenantId(options.AllowedTenants[i]))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconTenancyOptions)}.{nameof(TraconTenancyOptions.AllowedTenants)}[{i}] may contain only " +
                    "ASCII letters, digits, '_', '.' and '-', and at most 64 characters. The tenant identifier is the key " +
                    "every store compares, and a relational engine may treat two other spellings as one tenant.");
            }
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
