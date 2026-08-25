using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Normalizes foreign provider failures at the outer model-call boundary.</summary>
internal static class ProviderFailureNormalizer
{
    internal const string UpstreamErrorType = "upstream_error";
    internal const string CredentialUnsupportedErrorType = "provider_credential_unsupported";
    internal const string UpstreamMessage = "The model provider request failed.";
    internal const string CredentialUnsupportedMessage = "The model provider does not support tenant credentials.";

    internal static bool ShouldNormalize(Exception exception)
        => exception is not OperationCanceledException && !IsKnownSafe(exception);

    internal static void Log(
        ILoggerFactory? loggerFactory,
        ModelBinding binding,
        Exception exception,
        string operation)
    {
        loggerFactory?
            .CreateLogger("AgentPrism.ModelProvider")
            .LogError(
                exception,
                "Model provider {Provider} failed during {Operation} for model {Model}.",
                binding.Provider,
                operation,
                binding.Model);
    }

    private static bool IsKnownSafe(Exception exception)
        => exception is AgentPrismContentFilteredException
            or AgentPrismContentBlockedException
            or AgentPrismCompilationException
            or AgentPrismProviderUnavailableException
            or AgentPrismToolTimeoutException
            or AgentPrismSessionConflictException
            or AgentPrismExternalCallException
            or ReplayToolMismatchException
            or AgentPrismAgentSourceException
            or JobRetryException
            or ProviderInvocationException
            // Internal and unforgeable (AgentPrism.Abstractions grants AgentPrism.Core
            // sole InternalsVisibleTo access): thrown only by AgentPrism's own
            // ModelProviderSettings.Validate, never by a foreign provider.
            or ProviderSettingsValidationException;
}

/// <summary>Marks an exception that crossed the raw provider boundary.</summary>
internal sealed class ForeignProviderInvocationException(Exception original) : Exception(original.Message, original)
{
    internal Exception Original { get; } = original;
}

/// <summary>Tags only failures thrown by the provider client itself.</summary>
internal sealed class ProviderFailureTaggingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ProviderFailureNormalizer.ShouldNormalize(exception))
        {
            throw new ForeignProviderInvocationException(exception);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<ChatResponseUpdate> enumerator;

        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception exception) when (ProviderFailureNormalizer.ShouldNormalize(exception))
        {
            throw new ForeignProviderInvocationException(exception);
        }

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (ProviderFailureNormalizer.ShouldNormalize(exception))
                {
                    throw new ForeignProviderInvocationException(exception);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
}

/// <summary>Internal stable provider failure carried to public protocol adapters.</summary>
internal sealed class ProviderInvocationException : AgentPrismException
{
    private ProviderInvocationException(string errorType, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorType = errorType;
    }

    public override string ErrorType { get; }

    internal static ProviderInvocationException UpstreamFailure(Exception exception)
        => new(ProviderFailureNormalizer.UpstreamErrorType, ProviderFailureNormalizer.UpstreamMessage, exception);

    internal static ProviderInvocationException CredentialUnsupported(string providerName)
        => new(
            ProviderFailureNormalizer.CredentialUnsupportedErrorType,
            $"{ProviderFailureNormalizer.CredentialUnsupportedMessage} Provider: '{providerName}'.");
}

/// <summary>Preserves internal retry semantics, then masks the final provider failure.</summary>
internal sealed class ProviderFailureNormalizingChatClient(
    ModelBinding binding,
    IChatClient inner,
    ILoggerFactory? loggerFactory)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (ForeignProviderInvocationException exception)
        {
            ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "invocation");
            throw ProviderInvocationException.UpstreamFailure(exception.Original);
        }
        catch (AgentPrismProviderUnavailableException exception)
            when (FindForeignFailure(exception) is { } foreign)
        {
            ProviderFailureNormalizer.Log(loggerFactory, binding, foreign.Original, "invocation");
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<ChatResponseUpdate> enumerator;

        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (ForeignProviderInvocationException exception)
        {
            ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "streaming invocation");
            throw ProviderInvocationException.UpstreamFailure(exception.Original);
        }

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (ForeignProviderInvocationException exception)
                {
                    ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "streaming invocation");
                    throw ProviderInvocationException.UpstreamFailure(exception.Original);
                }
                catch (AgentPrismProviderUnavailableException exception)
                    when (FindForeignFailure(exception) is { } foreign)
                {
                    ProviderFailureNormalizer.Log(loggerFactory, binding, foreign.Original, "streaming invocation");
                    throw;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }

    private static ForeignProviderInvocationException? FindForeignFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is ForeignProviderInvocationException foreign)
            {
                return foreign;
            }
        }

        return null;
    }
}
