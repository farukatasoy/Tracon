using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Checks a run's response against the agent's requested
/// <see cref="AgentResponseFormat"/> before letting it through.
/// </summary>
/// <remarks>
/// <para>
/// Installed as the <strong>innermost</strong> <see cref="IAgentDecorator"/>
/// (<c>Order = 30</c>, see <see cref="StructuredResponseValidatingAgentDecorator"/>) —
/// closer to the compiled agent than the tool-approval gate and run recording,
/// so a rejection propagates as an ordinary exception through both outer
/// wrappers exactly like every other run-ending error.
/// </para>
/// <para>
/// Runs only when all three hold: <see cref="AgentPrismStructuredResponseOptions.Enabled"/>
/// is <see langword="true"/>, the agent asked for <see cref="AgentResponseFormatKind.Json"/>
/// or <see cref="AgentResponseFormatKind.JsonSchema"/>, and the run actually
/// produced a response (it did not already end in error or cancellation). With
/// any of the three missing, this wrapper calls straight through and today's
/// behaviour does not change at all.
/// </para>
/// <para>
/// <strong>Well-formedness first.</strong> Before <see cref="IStructuredResponseValidator"/>
/// is ever called, the response must be non-empty and parse as JSON. AgentPrism
/// checks this itself — <c>System.Text.Json</c> is already a dependency, no
/// reflection is involved, and it closes the two failure modes a consumer
/// would otherwise have to check by hand in every validator. This is
/// deliberately NOT schema validation; see the interface's own remarks for
/// why AgentPrism ships no built-in schema validator.
/// </para>
/// <para>
/// <strong>Streaming.</strong> Content is forwarded to the caller as it
/// arrives — validation runs only after the last update, so a rejection can
/// still close the run as <see cref="RunStatus.Failed"/> but cannot un-send
/// what the client already received. A structured-output agent used as a
/// gate should not stream.
/// </para>
/// </remarks>
internal sealed class StructuredResponseValidatingAgent : DelegatingAIAgent
{
    private readonly AgentDescriptor _descriptor;
    private readonly IStructuredResponseValidator _validator;
    private readonly AgentPrismStructuredResponseOptions _options;
    private readonly ILogger<StructuredResponseValidatingAgent> _logger;

    /// <summary>Creates a new structured response validation wrapper.</summary>
    /// <param name="innerAgent">The wrapped agent.</param>
    /// <param name="descriptor">The catalog summary of the agent, carrying its requested response format.</param>
    /// <param name="validator">The validation policy.</param>
    /// <param name="options">The structured response settings.</param>
    /// <param name="logger">The logger for a faulting validator.</param>
    public StructuredResponseValidatingAgent(
        AIAgent innerAgent,
        AgentDescriptor descriptor,
        IStructuredResponseValidator validator,
        AgentPrismStructuredResponseOptions options,
        ILogger<StructuredResponseValidatingAgent> logger)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _descriptor = descriptor;
        _validator = validator;
        _options = options;
        _logger = logger;
    }

    private AgentResponseFormat? RequestedFormat => _descriptor.Model?.ResponseFormat;

    private bool ShouldValidate
        => _options.Enabled && RequestedFormat?.Kind is AgentResponseFormatKind.Json or AgentResponseFormatKind.JsonSchema;

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

        if (!ShouldValidate)
        {
            return response;
        }

        await ValidateAsync(response.Text, cancellationToken).ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!ShouldValidate)
        {
            await foreach (var passthrough in base.RunCoreStreamingAsync(messages, session, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return passthrough;
            }

            yield break;
        }

        var updates = new List<AgentResponseUpdate>();

        await foreach (var update in base.RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .ConfigureAwait(false))
        {
            updates.Add(update);
            yield return update;
        }

        // Validation runs AFTER the whole stream has already reached the
        // caller — see the class remarks on why content cannot be un-sent.
        var text = AgentResponseExtensions.ToAgentResponse(updates).Text;

        await ValidateAsync(text, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ValidateAsync(string responseText, CancellationToken cancellationToken)
    {
        var format = RequestedFormat!;
        var reason = WellFormednessReason(responseText);

        if (reason is null)
        {
            reason = await CallValidatorAsync(format, responseText, cancellationToken).ConfigureAwait(false);

            if (reason is null)
            {
                return;
            }
        }

        await RejectAsync(format, reason, cancellationToken).ConfigureAwait(false);

        throw new AgentPrismStructuredResponseException(reason);
    }

    private async ValueTask<string?> CallValidatorAsync(
        AgentResponseFormat format, string responseText, CancellationToken cancellationToken)
    {
        var scope = AgentPrismRunContext.Current;

        var context = new StructuredResponseValidationContext
        {
            AgentName = _descriptor.Name,
            RunId = scope?.RunId ?? Guid.Empty,
            SessionId = scope?.SessionId,
            Provider = _descriptor.Model?.Provider,
            Model = _descriptor.Model?.Model,
            Kind = format.Kind,
            Schema = format.Schema,
            SchemaName = format.SchemaName,
            ResponseText = responseText,
        };

        StructuredResponseValidationResult result;

        try
        {
            result = await _validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "The structured response validator threw while checking agent '{AgentName}'; the response was " +
                "rejected (fail-closed).",
                _descriptor.Name);

            result = StructuredResponseValidationResult.Invalid(
                "The structured response validation check failed. This run was rejected (fail-closed).");
        }

        return result.IsValid ? null : result.Reason ?? "The response was rejected by the structured response validator.";
    }

    /// <summary>Checks that the response is non-empty and parses as JSON.</summary>
    private static string? WellFormednessReason(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return "The response is empty.";
        }

        try
        {
            using var _ = JsonDocument.Parse(responseText);
            return null;
        }
        catch (JsonException)
        {
            return "The response is not valid JSON.";
        }
    }

    private async ValueTask RejectAsync(AgentResponseFormat format, string reason, CancellationToken cancellationToken)
    {
        if (AgentPrismRunContext.Current?.Writer is not { } writer)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new StructuredResponseRejectedEventPayload
            {
                Kind = format.Kind.ToString(),
                SchemaName = format.SchemaName,
                Reason = reason,
                Provider = _descriptor.Model?.Provider,
                Model = _descriptor.Model?.Model,
            },
            AgentPrismCoreJsonContext.Default.StructuredResponseRejectedEventPayload);

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.StructuredResponseRejected) { Text = reason, Payload = payload },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The <see cref="RunEventType.StructuredResponseRejected"/> event payload.</summary>
internal sealed record StructuredResponseRejectedEventPayload
{
    /// <summary>Gets the requested response format kind's wire name.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the requested schema's name, if the agent's definition supplied one.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Gets the safe rejection reason. Never the model's raw response text.</summary>
    public required string Reason { get; init; }

    /// <summary>Gets the model provider that produced the rejected response, if known.</summary>
    public string? Provider { get; init; }

    /// <summary>Gets the model that produced the rejected response, if known.</summary>
    public string? Model { get; init; }
}
