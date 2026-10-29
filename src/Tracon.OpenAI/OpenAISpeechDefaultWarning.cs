using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// A <c>UseAsDefault</c> request that <c>UseOpenAISpeech</c> could not honour, because the unnamed
/// default was already registered.
/// </summary>
/// <param name="Interface">The default that stayed, as the service type is named in the log.</param>
/// <param name="ProviderName">The provider the request was for.</param>
internal sealed record OpenAISpeechDefaultRefused(string Interface, string ProviderName);

/// <summary>
/// Says once, while the host starts, that a <c>UseOpenAISpeech</c> call asked for the unnamed default
/// and did not get it.
/// </summary>
/// <remarks>
/// The default is registered with <c>TryAdd</c>, so the registration that comes first wins and
/// the later one does nothing; the consumer's own registration keeps winning. The rule stays as it
/// is. The service only makes the outcome visible: the options page says it, but a host that calls
/// <c>UseVoice</c> first then speaks with ElevenLabs while <c>UseAsDefault</c> says OpenAI. It reads
/// what the registration recorded and never touches a service, so it opens no connection.
/// </remarks>
internal sealed partial class OpenAISpeechDefaultWarning(
    IEnumerable<OpenAISpeechDefaultRefused> refused,
    ILogger<OpenAISpeechDefaultWarning> logger) : IHostedService
{
    /// <summary>
    /// Records that the default of <typeparamref name="TService"/> is taken, when it is. Call before
    /// the <c>TryAdd</c> of the default.
    /// </summary>
    /// <typeparam name="TService">The default's service type.</typeparam>
    /// <param name="services">The collection the default is about to be added to.</param>
    /// <param name="providerName">The provider the request is for.</param>
    public static void RecordWhenTaken<TService>(IServiceCollection services, string providerName)
        where TService : class
    {
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService))
        {
            return;
        }

        services.AddSingleton(new OpenAISpeechDefaultRefused(typeof(TService).Name, providerName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OpenAISpeechDefaultWarning>());
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        foreach (var item in refused)
        {
            DefaultNotApplied(logger, item.Interface, item.ProviderName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "UseOpenAISpeech asked for '{ProviderName}' to be the default {Interface}, but a default was already " +
                  "registered (UseVoice registers one) and the first registration stays. The speak and transcribe tools and " +
                  "the live conversation use that one, not '{ProviderName}'. Call UseOpenAISpeech before the call that " +
                  "registered the other default, or leave UseAsDefault off. This message is written once per call.")]
    private static partial void DefaultNotApplied(ILogger logger, string @interface, string providerName);
}
