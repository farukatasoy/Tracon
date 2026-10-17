using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates <see cref="VoiceLiveOptions"/> when the application starts.</summary>
/// <remarks>
/// <para>
/// Each delegation arms <see cref="VoiceLiveOptions.DelegationTimeout"/> with
/// <c>CancelAfter</c>, which refuses a negative value or more than about 49.7 days.
/// Out of range, the first delegation threw inside the sideband pump and ended the
/// whole session with an error. Nothing validated the options before.
/// </para>
/// <para>
/// The configuration binder refuses a <see cref="VoiceLiveOptions.DelegationMode"/> the
/// contract does not define, but a value assigned in code never passes the binder: the
/// provider threw on the first session and the endpoint answered <c>500</c>.
/// </para>
/// </remarks>
internal sealed class VoiceLiveOptionsValidator : IValidateOptions<VoiceLiveOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, VoiceLiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (!TimerPeriod.IsValid(options.DelegationTimeout))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(VoiceLiveOptions)}.{nameof(VoiceLiveOptions.DelegationTimeout)}",
                options.DelegationTimeout));
        }

        if (!Enum.IsDefined(options.DelegationMode))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceLiveOptions)}.{nameof(VoiceLiveOptions.DelegationMode)} is {(int)options.DelegationMode}, which is not a delegation mode. The only mode is '{nameof(LiveVoiceDelegationMode.Client)}'.");
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
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
