using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Marks a named speech provider that serves a Microsoft.Extensions.AI client.</summary>
/// <param name="Registration">The registration the provider is keyed under.</param>
internal sealed record SpeechClientBridgeCheck(SpeechProviderRegistration Registration);

/// <summary>
/// Options type with no settings: it exists so the host runs <see cref="SpeechClientBridgeValidator"/>
/// at startup (<c>ValidateOnStart</c>).
/// </summary>
internal sealed class SpeechClientBridgeOptions;

/// <summary>
/// Builds every bridged speech provider when the host starts, so a missing client or a missing
/// provider name stops the host instead of failing the first call.
/// </summary>
/// <remarks>
/// <para>
/// The factory of a named provider runs at the first resolution, and the first resolution is a
/// tenant's request. A failure there is a <c>502</c> or a <c>500</c> on a path that has nothing
/// to do with the cause. Only providers registered with a client binding are built: a provider
/// from a factory the consumer wrote keeps its lazy creation.
/// </para>
/// <para>
/// A provider that is built here is the one the container keeps, so the check costs no second
/// instance.
/// </para>
/// </remarks>
internal sealed class SpeechClientBridgeValidator : IValidateOptions<SpeechClientBridgeOptions>
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<SpeechClientBridgeCheck> _checks;

    public SpeechClientBridgeValidator(IServiceProvider services, IEnumerable<SpeechClientBridgeCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(checks);

        _services = services;
        _checks = [.. checks];
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SpeechClientBridgeOptions options)
    {
        List<string>? failures = null;

        foreach (var check in _checks)
        {
            try
            {
                _ = check.Registration.Kind == SpeechProviderKind.Transcriber
                    ? (object)_services.GetRequiredKeyedService<ISpeechTranscriber>(check.Registration)
                    : _services.GetRequiredKeyedService<ISpeechSynthesizer>(check.Registration);
            }
            catch (TraconException exception)
            {
                (failures ??= []).Add(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                // The client's own factory threw: the container's text names the cause.
                (failures ??= []).Add(
                    $"The speech {SpeechProviderRegistration.Noun(check.Registration.Kind)} " +
                    $"'{check.Registration.Name}' could not be built: {exception.Message}");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Wires <see cref="SpeechClientBridgeValidator"/> into the host.</summary>
internal static class SpeechClientBridgeValidation
{
    /// <summary>Records a bridged provider for the startup check.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registration">The provider's registration.</param>
    public static void Register(IServiceCollection services, SpeechProviderRegistration registration)
    {
        services.AddSingleton(new SpeechClientBridgeCheck(registration));

        // Once per host: the validator reads every record, and a second start-up hook would only repeat it.
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(IValidateOptions<SpeechClientBridgeOptions>)))
        {
            return;
        }

        services.AddSingleton<IValidateOptions<SpeechClientBridgeOptions>, SpeechClientBridgeValidator>();
        services.AddOptions<SpeechClientBridgeOptions>().ValidateOnStart();
    }
}
