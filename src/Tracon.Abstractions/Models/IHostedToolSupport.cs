namespace Tracon;

/// <summary>
/// The <see cref="HostedToolKind"/> values a provider's API surface can run, as a set.
/// </summary>
/// <remarks>
/// The set form of <see cref="HostedToolKind"/>: a provider states which kinds it supports,
/// an agent definition selects the kinds it needs.
/// </remarks>
[Flags]
public enum HostedToolKinds
{
    /// <summary>No kind.</summary>
    None = 0,

    /// <summary><see cref="HostedToolKind.WebSearch"/>.</summary>
    WebSearch = 1 << 0,

    /// <summary><see cref="HostedToolKind.CodeInterpreter"/>.</summary>
    CodeInterpreter = 1 << 1,

    /// <summary><see cref="HostedToolKind.FileSearch"/>.</summary>
    FileSearch = 1 << 2,
}

/// <summary>
/// An optional interface of an <see cref="IModelProvider"/> that states which
/// <see cref="HostedToolKind"/> values its API surface runs.
/// </summary>
/// <remarks>
/// <para>
/// The model adapters map only some hosted tools to the wire and drop the rest without an
/// error: on the Chat Completions surface a code interpreter tool is sent nowhere and the model
/// answers as if it had none. A provider states what its surface runs, so a definition that
/// selects an unsupported kind stops with an error instead of running without the tool.
/// </para>
/// <para>
/// The support belongs to the API surface, not to the model: <c>openai</c> and
/// <c>openai-responses</c> reach the same models, and only the Responses API runs a code
/// interpreter. A provider registered under several names states each name's own support.
/// </para>
/// <para>
/// A provider that does not implement this interface supports no kind. The primary provider and
/// every <see cref="ModelBinding.Fallbacks"/> link are checked, because a link receives the
/// primary call's tools. A kind in the set is not a promise: a model can still reject a tool the
/// surface sends, and that rejection is the provider's own error.
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
public interface IHostedToolSupport
{
    /// <summary>
    /// Gets the hosted tool kinds this provider's API surface runs. Report a kind only when the
    /// wire request carries the tool.
    /// </summary>
    HostedToolKinds SupportedHostedTools { get; }
}
