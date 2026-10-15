using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconWorkflowOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// <para>
/// Every workflow run arms <see cref="TraconWorkflowOptions.RunTimeout"/> as
/// its deadline with a <see cref="CancellationTokenSource"/>, which refuses a
/// negative value or more than about 49.7 days. Out of range, every run threw when it
/// started - long after the host did. Zero cancelled every run at once. The value is
/// checked only while workflows are on; nothing arms it otherwise.
/// </para>
/// <para>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </para>
/// <para>
/// <strong>A workflow registered in code is checked against the ceiling here</strong>
/// (<see cref="WorkflowRegistrationOptions.RunTimeout"/>, F-537). A stored definition meets the ceiling
/// at its save, an HTTP call someone can answer; a registration is written at build time and meets the
/// host's settings only at startup, where a value the deployment cannot honour should stop the host
/// instead of cutting every run in silence.
/// </para>
/// </remarks>
/// <param name="codeWorkflows">The workflows registered in code; none when the validator is built by hand.</param>
internal sealed class TraconWorkflowOptionsValidator(IEnumerable<CodeWorkflowRegistration>? codeWorkflows = null) : IValidateOptions<TraconWorkflowOptions>
{
    /// <summary>
    /// The largest continuation count accepted. A queued run's job gets one lease
    /// per continuation, and a chain this long is a loop, not a recovery.
    /// </summary>
    private const int MaxResumeAttemptsLimit = 100;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Enabled && !TimerPeriod.IsValid(options.RunTimeout))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.RunTimeout)}",
                options.RunTimeout,
                $" when {nameof(TraconWorkflowOptions.Enabled)} is on"));
        }

        // Checked whether or not the engine is on: the cap guards a definition SAVE,
        // which does not need the engine.
        if (options.MaxNodeSettingsLength < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.MaxNodeSettingsLength)} must be at least 1 " +
                $"(bytes of a node's settings JSON). Actual value: {options.MaxNodeSettingsLength}.");
        }

        // F-405: checked whether or not the engine is on, like the settings cap: the
        // ceiling guards a definition SAVE. A ceiling below the installation's own
        // run timeout would let a definition shorten nothing it could not already,
        // and the save would refuse a value the installation runs every day.
        if (options.MaxRunTimeout is { } ceiling)
        {
            if (!TimerPeriod.IsValid(ceiling))
            {
                return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                    $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.MaxRunTimeout)}",
                    ceiling));
            }

            if (ceiling < options.RunTimeout)
            {
                return ValidateOptionsResult.Fail(
                    $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.MaxRunTimeout)} ({ceiling}) must not be " +
                    $"below {nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.RunTimeout)} ({options.RunTimeout}): " +
                    "it is the longest run timeout a workflow definition may set. Leave it unset to let a definition " +
                    "only shorten the run timeout.");
            }
        }

        // F-537: the ceiling a workflow registered in code is held to. Only while the engine is on:
        // nothing arms a run timeout otherwise.
        if (options.Enabled && codeWorkflows is not null)
        {
            var allowed = WorkflowDefinitionValidator.RunTimeoutCeiling(options.RunTimeout, options.MaxRunTimeout);
            List<string>? failures = null;

            foreach (var registration in codeWorkflows)
            {
                if (registration.RunTimeout is { } requested && requested > allowed)
                {
                    (failures ??= []).Add(
                        $"Workflow '{registration.Name}' sets {nameof(WorkflowRegistrationOptions)}.{nameof(WorkflowRegistrationOptions.RunTimeout)} " +
                        $"to {requested}, above the longest run timeout a workflow may set ({allowed}). Lower it" +
                        (options.MaxRunTimeout is null
                            ? ", or set 'Tracon:Workflows:MaxRunTimeout' above it: while that is unset a workflow may only shorten " +
                              "'Tracon:Workflows:RunTimeout', because a running workflow holds a slot every tenant shares."
                            : $", or raise 'Tracon:Workflows:MaxRunTimeout' ({options.MaxRunTimeout})."));
                }
            }

            if (failures is not null)
            {
                return ValidateOptionsResult.Fail(failures);
            }
        }

        // F-321: recovery resumes from checkpoints, so it cannot work without
        // them. Refused at startup rather than discovered when the first job is
        // leased again and finds nothing to resume from.
        if (options.ResumeInterruptedRuns && !options.EnableCheckpointing)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.ResumeInterruptedRuns)} needs " +
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.EnableCheckpointing)}: an interrupted " +
                "run is resumed from its latest checkpoint. Turn checkpointing on, or turn recovery off.");
        }

        // Phase 209: the expiry scan runs on a PeriodicTimer, which refuses a period outside
        // one millisecond to about 49.7 days from inside a hosted service, where the default
        // StopHost turns it into a stopped host (F-278). Only while the engine is on.
        if (options.Enabled && !TimerPeriod.IsValid(options.RequestExpirationScanInterval))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.RequestExpirationScanInterval)}",
                options.RequestExpirationScanInterval,
                $" when {nameof(TraconWorkflowOptions.Enabled)} is on"));
        }

        if (options.MaxRequestExpirationsPerScan < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.MaxRequestExpirationsPerScan)} must be at " +
                $"least 1. Actual value: {options.MaxRequestExpirationsPerScan}.");
        }

        if (options.MaxResumeAttempts is < 0 or > MaxResumeAttemptsLimit)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.MaxResumeAttempts)} must be from 0 to " +
                $"{MaxResumeAttemptsLimit}. Actual value: {options.MaxResumeAttempts}.");
        }

        return ValidateOptionsResult.Success;
    }
}
