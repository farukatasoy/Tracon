using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Refuses to start the host while a configuration value of the options is unreadable.
/// </summary>
/// <typeparam name="TOptions">The options type.</typeparam>
/// <remarks>
/// <para>
/// The binders record each text they could not read in the options' own
/// <see cref="UnreadableConfigurationReport"/>; this validator turns the records that
/// still hold into one failure list, so the operator sees every unreadable key of the
/// options at once. It owns no rule about the value itself - an out-of-range number is
/// the options' own validator's - and it reports only keys the binder read.
/// </para>
/// <para>
/// It is registered with <c>ValidateOnStart</c> by
/// <see cref="UnreadableConfigurationValidator.Add"/>. A validator without that
/// registration runs at the first <c>.Value</c> read, after the first provider call.
/// </para>
/// </remarks>
internal sealed class UnreadableConfigurationValidator<TOptions> : IValidateOptions<TOptions>
    where TOptions : class
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!UnreadableConfigurationReport.TryGet(options, out var report))
        {
            return ValidateOptionsResult.Success;
        }

        var failures = report.Failures();

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Registers <see cref="UnreadableConfigurationValidator{TOptions}"/>.</summary>
internal static class UnreadableConfigurationValidator
{
    /// <summary>
    /// Registers the check for an options type whose binder reads scalars: the options
    /// are validated at start and the check runs once, however often the caller registers.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="services">The service collection.</param>
    internal static void Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        IServiceCollection services)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TOptions>().ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TOptions>, UnreadableConfigurationValidator<TOptions>>());
    }
}
