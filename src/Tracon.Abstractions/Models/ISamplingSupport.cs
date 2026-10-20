namespace Tracon;

/// <summary>
/// The request fields of <see cref="ModelBinding"/> whose support depends on the
/// provider's API surface.
/// </summary>
/// <remarks>
/// The model adapters of the shipped providers map only some of these fields to
/// the wire and drop the rest without an error. A provider states what its
/// surface sends through <see cref="ISamplingSupport"/>, so a definition that
/// sets an unsupported field stops with an error instead of running without it.
/// </remarks>
[Flags]
public enum SamplingFields
{
    /// <summary>No field.</summary>
    None = 0,

    /// <summary><see cref="ModelBinding.Seed"/>.</summary>
    Seed = 1 << 0,

    /// <summary><see cref="ModelBinding.TopK"/>.</summary>
    TopK = 1 << 1,

    /// <summary><see cref="ModelBinding.StopSequences"/>.</summary>
    StopSequences = 1 << 2,

    /// <summary><see cref="ModelBinding.FrequencyPenalty"/>.</summary>
    FrequencyPenalty = 1 << 3,

    /// <summary><see cref="ModelBinding.PresencePenalty"/>.</summary>
    PresencePenalty = 1 << 4,

    /// <summary><see cref="ModelBinding.ToolMode"/>.</summary>
    ToolMode = 1 << 5,

    /// <summary><see cref="ModelBinding.AllowMultipleToolCalls"/>.</summary>
    AllowMultipleToolCalls = 1 << 6,

    /// <summary><see cref="ModelBinding.ReasoningOutput"/>.</summary>
    ReasoningOutput = 1 << 7,
}

/// <summary>
/// An optional interface of an <see cref="IModelProvider"/> that states which
/// <see cref="SamplingFields"/> its API surface sends to the model.
/// </summary>
/// <remarks>
/// <para>
/// The support belongs to the API surface, not to the model: <c>openai</c> and
/// <c>openai-responses</c> reach the same models, and the Responses API drops
/// <see cref="ModelBinding.Seed"/> where Chat Completions sends it. A provider
/// registered under several names states each name's own support.
/// </para>
/// <para>
/// A provider that does not implement this interface supports none of the
/// fields: a definition that sets one fails to compile and the validation report
/// names the field and the provider. A field is never dropped silently. The
/// primary provider and every <see cref="ModelBinding.Fallbacks"/> link are
/// checked, because a link receives the primary call's request options.
/// </para>
/// <para><strong>Lifetime and threading</strong></para>
/// <para>
/// The registry reads the property from the provider's <strong>singleton</strong> instance whenever
/// it lists providers, which the compile path and the validation path do, so it may be read
/// concurrently. Return a value that does not change after construction.
/// </para>
/// <para><strong>Tenant mode</strong></para>
/// <para>
/// The answer is <strong>tenant-independent</strong>: it describes the API surface, so every tenant
/// gets the same one, including a tenant that brings its own credential.
/// </para>
/// <para><strong>Delivery</strong></para>
/// <para>
/// A read of a value, with no side effect: no delivery guarantee applies.
/// </para>
/// </remarks>
public interface ISamplingSupport
{
    /// <summary>
    /// Gets the fields this provider's API surface sends to the model. Report a field
    /// only when the wire request carries it.
    /// </summary>
    SamplingFields SupportedSamplingFields { get; }
}
