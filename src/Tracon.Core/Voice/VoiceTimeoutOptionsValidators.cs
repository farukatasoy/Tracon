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

        // Both are compared with the age of a session, so a value of zero or less ends every session at its first check.
        AddWhenNotPositive(options.MaxSessionDuration, nameof(VoiceLiveOptions), nameof(VoiceLiveOptions.MaxSessionDuration), ref failures);
        AddWhenNotPositive(options.PendingSessionTimeout, nameof(VoiceLiveOptions), nameof(VoiceLiveOptions.PendingSessionTimeout), ref failures);

        // A limit of zero refuses the work; a negative one is not a count and is read as one that refuses it too.
        AddWhenNegative(options.MaxAppendsPerDelegation, nameof(VoiceLiveOptions), nameof(VoiceLiveOptions.MaxAppendsPerDelegation), ref failures);
        AddWhenNegative(options.MaxConcurrentDelegations, nameof(VoiceLiveOptions), nameof(VoiceLiveOptions.MaxConcurrentDelegations), ref failures);
        AddWhenNegative(options.MaxConcurrentSessionsPerTenant, nameof(VoiceLiveOptions), nameof(VoiceLiveOptions.MaxConcurrentSessionsPerTenant), ref failures);
        AddWhenNegative(options.MaxLedgerEntriesPerDelegation, nameof(VoiceLiveOptions), nameof(VoiceLiveOptions.MaxLedgerEntriesPerDelegation), ref failures);
        AddWhenNegative(options.MaxTranscriptLedgerCharacters, nameof(VoiceLiveOptions), nameof(VoiceLiveOptions.MaxTranscriptLedgerCharacters), ref failures);

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static void AddWhenNotPositive(TimeSpan value, string typeName, string memberName, ref List<string>? failures)
    {
        if (value <= TimeSpan.Zero)
        {
            (failures ??= []).Add($"{typeName}.{memberName} must be greater than zero. Actual value: {value}.");
        }
    }

    internal static void AddWhenNegative(int value, string typeName, string memberName, ref List<string>? failures)
    {
        if (value < 0)
        {
            (failures ??= []).Add($"{typeName}.{memberName} must not be negative (0 refuses the work). Actual value: {value}.");
        }
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

        // The sample rate goes into the header of the audio the transcription provider receives.
        if (options.InputSampleRate < 1)
        {
            (failures ??= []).Add(
                $"{nameof(VoiceConversationOptions)}.{nameof(VoiceConversationOptions.InputSampleRate)} " +
                $"must be at least 1. Actual value: {options.InputSampleRate}.");
        }

        // Compared with the length of an utterance: zero or less closes every utterance when it opens.
        VoiceLiveOptionsValidator.AddWhenNotPositive(
            options.MaxUtteranceDuration, nameof(VoiceConversationOptions), nameof(VoiceConversationOptions.MaxUtteranceDuration), ref failures);

        VoiceLiveOptionsValidator.AddWhenNegative(
            options.MaxConcurrentConnectionsPerTenant, nameof(VoiceConversationOptions), nameof(VoiceConversationOptions.MaxConcurrentConnectionsPerTenant), ref failures);
        VoiceLiveOptionsValidator.AddWhenNegative(
            options.MaxSpokenCharactersPerTurn, nameof(VoiceConversationOptions), nameof(VoiceConversationOptions.MaxSpokenCharactersPerTurn), ref failures);
        VoiceLiveOptionsValidator.AddWhenNegative(
            options.MaxUtteranceBytes, nameof(VoiceConversationOptions), nameof(VoiceConversationOptions.MaxUtteranceBytes), ref failures);

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
