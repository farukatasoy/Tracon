using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconRateLimitOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// The only rule is the one a database counter cannot honour: it has no waiting line, so
/// <see cref="TraconRateLimitOptions.QueueLimit"/> above zero together with
/// <see cref="RateLimitScope.Deployment"/> is refused. Ignoring the queue without a word would give
/// the operator a limit that behaves differently from the one the settings describe.
/// A <see cref="TraconRateLimitOptions.PermitLimit"/> or <see cref="TraconRateLimitOptions.Window"/>
/// that is not positive keeps its long-standing meaning (one permit, one minute) and is not refused.
/// </remarks>
internal sealed class TraconRateLimitOptionsValidator : IValidateOptions<TraconRateLimitOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Scope == RateLimitScope.Deployment && options.QueueLimit > 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconRateLimitOptions)}.{nameof(TraconRateLimitOptions.QueueLimit)} must be 0 while " +
                $"{nameof(TraconRateLimitOptions.Scope)} is {nameof(RateLimitScope.Deployment)}: the counter store " +
                $"has no waiting line, so a request over the limit gets 429 at once. Actual value: {options.QueueLimit}.");
        }

        return ValidateOptionsResult.Success;
    }
}
