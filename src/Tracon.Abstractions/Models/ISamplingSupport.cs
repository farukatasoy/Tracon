using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// The request fields of <see cref="ModelBinding"/> whose support depends on the
/// provider's API surface. A set is written as member names separated by commas, such as
/// <c>Seed, StopSequences</c>; the empty set is <c>None</c>.
/// </summary>
/// <remarks>
/// <para>
/// The model adapters of the shipped providers map only some of these fields to
/// the wire and drop the rest without an error. A provider states what its
/// surface sends through <see cref="ISamplingSupport"/>, so a definition that
/// sets an unsupported field stops with an error instead of running without it.
/// </para>
/// <para>
/// The model catalog uses the same set for the fields a model rejects
/// (<see cref="ModelDescriptor.RejectedSamplingFields"/>), in JSON and in configuration.
/// </para>
/// <para>
/// <see cref="Temperature"/> and <see cref="TopP"/> joined the set when a shipped adapter
/// (Anthropic SDK 13) stopped sending them. Every other adapter always sent them, so a provider
/// that implements no <see cref="ISamplingSupport"/> is read as sending both; a provider that
/// does implement it names them like any other field.
/// </para>
/// </remarks>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<SamplingFields>))]
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

    /// <summary><see cref="ModelBinding.Temperature"/>.</summary>
    Temperature = 1 << 8,

    /// <summary><see cref="ModelBinding.TopP"/>.</summary>
    TopP = 1 << 9,
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
/// fields except <see cref="SamplingFields.Temperature"/> and <see cref="SamplingFields.TopP"/>,
/// which it is read as sending: a definition that sets another field fails to compile and the
/// validation report names the field and the provider. A field is never dropped silently. The
/// primary provider and every <see cref="ModelBinding.Fallbacks"/> link are
/// checked, because a link receives the primary call's request options.
/// </para>
/// <para>
/// A field the surface sends can still be refused for its value, or by the model. The ranges of the
/// values a surface accepts belong here too (<see cref="SamplingLimits"/>); the fields a single model
/// rejects belong to the model catalog (<see cref="ModelDescriptor.RejectedSamplingFields"/>).
/// </para>
/// <para><strong>Lifetime and threading</strong></para>
/// <para>
/// The registry reads the properties from the provider's <strong>singleton</strong> instance whenever
/// it lists providers, which the compile path and the validation path do, so it may be read
/// concurrently. Return values that do not change after construction.
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
    /// <remarks>
    /// Name <see cref="SamplingFields.Temperature"/> and <see cref="SamplingFields.TopP"/> too when the
    /// surface sends them. A provider written before the two existed and implementing this interface
    /// reports neither, and a definition that sets one then fails to compile with a message that names
    /// the provider.
    /// </remarks>
    SamplingFields SupportedSamplingFields { get; }

    /// <summary>
    /// Gets the value ranges this provider's API surface accepts for the fields it sends. The default
    /// states none, so nothing is checked.
    /// </summary>
    /// <remarks>
    /// The member has a default body: a provider written before it existed still compiles, loads and
    /// runs, and states no limit. State a limit only when you measured it or the provider documents it.
    /// </remarks>
    SamplingLimits SamplingLimits => SamplingLimits.None;
}
