using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Makes a host that registered Google speech or images without <c>UseGoogle</c> fail when it starts, not when the
/// first request reaches the speech or the image generator.
/// </summary>
/// <remarks>
/// <para>
/// Both calls share the authenticated client that <c>UseGoogle</c> registers, and they take it from the container when
/// something first resolves them. The order of the calls on the chain is free (<c>UseGoogle</c> may come after), so
/// nothing can be checked while the chain is built. The check runs with the host's start-up validation instead: the
/// container is complete then, and a missing provider is named in terms of the call to add, not as the name of an
/// internal type.
/// </para>
/// <para>
/// The marker is an options type with no members; only its validator carries the rule.
/// </para>
/// </remarks>
internal sealed class GoogleProviderRequirement
{
    /// <summary>Registers the requirement for the call named by <paramref name="callName"/>; one check serves every call.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="callName">The call that needs the provider, for the message.</param>
    public static void Require(IServiceCollection services, string callName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(callName);

        services.AddOptions<GoogleProviderRequirement>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GoogleProviderRequirement>, GoogleProviderRequirementValidator>());
        services.AddSingleton(new GoogleProviderCall(callName));
    }
}

/// <summary>A call that needs the Google provider, remembered for the message.</summary>
/// <param name="Name">The call, for example <c>UseGoogleSpeech</c>.</param>
internal sealed record GoogleProviderCall(string Name);

/// <summary>Refuses a host that registered Google speech or images and did not register the provider.</summary>
/// <param name="services">The container.</param>
internal sealed class GoogleProviderRequirementValidator(IServiceProvider services) : IValidateOptions<GoogleProviderRequirement>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GoogleProviderRequirement options)
    {
        if (services.GetService<GoogleModelProvider>() is not null)
        {
            return ValidateOptionsResult.Success;
        }

        var calls = string.Join(
            " and ",
            services.GetServices<GoogleProviderCall>().Select(static call => call.Name).Distinct(StringComparer.Ordinal));

        return ValidateOptionsResult.Fail(
            $"{calls} needs the Google provider, and UseGoogle(...) was not called. Add UseGoogle(...) to the same " +
            "AddTracon() chain (before or after this call); speech and images use the client it registers.");
    }
}
