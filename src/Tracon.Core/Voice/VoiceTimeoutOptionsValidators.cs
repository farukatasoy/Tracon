using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates the timeouts of <see cref="VoiceLiveOptions"/> when the application starts.</summary>
/// <remarks>
/// Each delegation arms <see cref="VoiceLiveOptions.DelegationTimeout"/> with
/// <c>CancelAfter</c>, which refuses a negative value or more than about 49.7 days.
/// Out of range, the first delegation threw inside the sideband pump and ended the
/// whole session with an error. Nothing validated the options before.
/// </remarks>
internal sealed class VoiceLiveOptionsValidator : IValidateOptions<VoiceLiveOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, VoiceLiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return TimerPeriod.IsValid(options.DelegationTimeout)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(VoiceLiveOptions)}.{nameof(VoiceLiveOptions.DelegationTimeout)}",
                options.DelegationTimeout));
    }
}

/// <summary>Validates the timeouts of <see cref="VoiceConversationOptions"/> when the application starts.</summary>
/// <remarks>
/// A connection waits for its next frame with <c>CancelAfter</c> over the
/// shorter of <see cref="VoiceConversationOptions.IdleTimeout"/> and the time left of
/// <see cref="VoiceConversationOptions.MaxConnectionDuration"/>; a negative value, or
/// both longer than about 49.7 days, threw on every connection. Nothing validated the
/// options before.
/// </remarks>
internal sealed class VoiceConversationOptionsValidator : IValidateOptions<VoiceConversationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, VoiceConversationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (!TimerPeriod.IsValid(options.IdleTimeout))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(VoiceConversationOptions)}.{nameof(VoiceConversationOptions.IdleTimeout)}",
                options.IdleTimeout));
        }

        if (!TimerPeriod.IsValid(options.MaxConnectionDuration))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(VoiceConversationOptions)}.{nameof(VoiceConversationOptions.MaxConnectionDuration)}",
                options.MaxConnectionDuration));
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
