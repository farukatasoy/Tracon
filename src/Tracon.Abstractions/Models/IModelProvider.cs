using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// A model provider. Each provider is implemented in its own package (for
/// example, <c>Tracon.OpenAI</c>) and registered with DI.
/// </summary>
/// <remarks>
/// <para>
/// This abstraction ensures that adding a new provider is <em>not a breaking
/// change</em>. This is the main rationale for the modular packaging decision.
/// </para>
/// <para>
/// A provider outside the shipped packages is registered with
/// <c>AddModelProvider()</c>. The runtime contract below is what the registry
/// and the compile path actually rely on; an implementation that breaks any
/// of it compiles and passes its own unit tests, then misbehaves only under a
/// real deployment. The
/// <c>Tracon.Testing.Contracts.Xunit</c> package ships
/// <c>ModelProviderContract</c>, which asserts the parts of this contract that
/// can be checked from outside; deriving it is the cheapest way to prove an
/// implementation honors them.
/// </para>
///
/// <para><strong>Lifetime and threading</strong></para>
/// <para>
/// An implementation is used as a <strong>singleton</strong>.
/// <c>AddModelProvider()</c> registers it with
/// <c>AddSingleton</c>, and <c>ModelProviderRegistry</c> copies the registered
/// providers into a lookup <em>once</em>, when it is constructed. Two
/// consequences follow. A provider must not capture a scoped service — it
/// would be captured by a singleton and outlive its scope. And the set of
/// providers is fixed at startup: a provider cannot be added after the
/// container is built.
/// </para>
/// <para>
/// <see cref="CreateChatClient"/> is called <strong>concurrently</strong> on
/// the same instance and must be thread-safe. Any per-credential client cache
/// an implementation keeps is therefore a concurrent one; note that
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}.GetOrAdd(TKey, System.Func{TKey, TValue})"/>
/// may run its factory <strong>more than once</strong> for the same key when
/// two threads race, and discard the extra results. A factory that builds a
/// client must therefore be side-effect free and idempotent: building it twice
/// must be harmless.
/// </para>
///
/// <para><strong>Naming</strong></para>
/// <para>
/// <see cref="Name"/> is matched against <see cref="ModelBinding.Provider"/>
/// with <see cref="StringComparer.OrdinalIgnoreCase"/>. Registering two
/// providers under the same name is not last-one-wins: the registry throws
/// <see cref="TraconException"/> while it is being constructed, so the
/// host fails at startup rather than silently routing to one of them.
/// </para>
///
/// <para><strong>The model catalog is metadata, not an allow list</strong></para>
/// <para>
/// <see cref="Models"/> drives the console's model picker, capability hints,
/// and cost reporting. It does <strong>not</strong> gate which models may be
/// used: an implementation is not required to reject a
/// <see cref="ModelBinding.Model"/> that is absent from the catalog, and the
/// shipped providers do not — they log at most an informational message, so a
/// newly published model works without a new Tracon release. An empty
/// catalog is a normal, supported state.
/// </para>
/// <para>
/// The flags on <see cref="ModelDescriptor"/> do not carry equal weight.
/// <see cref="ModelDescriptor.SupportsStructuredOutput"/> is a real gate:
/// compilation of an agent that requests JSON output fails when the model is
/// <em>found</em> in the catalog and the flag is explicitly
/// <see langword="false"/> (a model absent from the catalog is not checked).
/// <see cref="ModelDescriptor.SupportsTools"/>,
/// <see cref="ModelDescriptor.SupportsStreaming"/> and
/// <see cref="ModelDescriptor.SupportsReasoning"/> are advisory metadata that
/// nothing enforces at run time.
/// </para>
///
/// <para><strong>Failures</strong></para>
/// <para>
/// There is no required exception type. Tracon classifies a model-call
/// failure by the exception's type <em>name</em> and message <em>text</em>
/// across the whole exception graph, because <c>Tracon.Core</c> holds no
/// compile-time reference to any provider SDK's exception types. What that
/// means for an implementation: a failure whose message carries
/// <c>HTTP 429</c>, <c>too many requests</c>, <c>rate limit</c> or
/// <c>HTTP 5xx</c> lets <see cref="ModelBinding.Fallbacks"/> move to the next
/// link; <c>HTTP 401</c> and <c>HTTP 403</c> deliberately do not (switching
/// providers would hide a configuration mistake); an
/// <see cref="OperationCanceledException"/> anywhere in the graph never
/// retries; and an <strong>unrecognized</strong> failure does not retry
/// either, because the retry set is closed and positive.
/// </para>
/// <para>
/// A response the provider filtered for safety is <strong>not</strong> an
/// exception at this layer. The correct signal is an ordinary
/// <see cref="Microsoft.Extensions.AI.ChatResponse"/> carrying
/// <see cref="Microsoft.Extensions.AI.ChatFinishReason.ContentFilter"/>; the
/// registry's outermost ring turns that into
/// <c>TraconContentFilteredException</c>. Throwing instead makes the
/// circuit breaker count a healthy provider as failing.
/// </para>
/// <para>
/// A <see cref="TraconException"/> thrown from
/// <see cref="CreateChatClient"/> is wrapped as a compilation error naming the
/// agent; every other exception propagates raw. Use it for a configuration or
/// binding problem the host author can act on.
/// </para>
/// </remarks>
public interface IModelProvider
{
    /// <summary>
    /// The provider name. <see cref="ModelBinding.Provider"/> matches this
    /// value. Comparison is case-insensitive.
    /// </summary>
    string Name { get; }

    /// <summary>The models this provider offers.</summary>
    /// <remarks>
    /// Metadata, not an allow list — see the remarks on
    /// <see cref="IModelProvider"/>. This is read on the compile path and may
    /// be read concurrently; return a stable, immutable collection rather than
    /// one that is mutated after construction.
    /// </remarks>
    IReadOnlyList<ModelDescriptor> Models { get; }

    /// <summary>
    /// Produces a <strong>raw</strong> chat client for the given binding.
    /// </summary>
    /// <param name="binding">The model binding.</param>
    /// <returns>
    /// The provider-specific client. Decorators <em>specific</em> to the
    /// provider (example: Anthropic's settings decorator) may be added here.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Do not build the common pipeline here.</strong>
    /// <c>UseFunctionInvocation()</c>, <c>UseOpenTelemetry()</c>, the content
    /// guard, the circuit breaker, the per-provider concurrency limiter,
    /// attachment resolution, response caching, the fallback chain and
    /// content-filter detection are all added by
    /// <c>ModelProviderRegistry.CreateChatClient</c>. Before that shared pipeline, every
    /// provider package built the tool-call loop inside itself; the result
    /// was that no ring the registry wraps around could see the loop's turns
    /// — a tool result entered the model uninspected.
    /// </para>
    /// <para>
    /// If the loop is also built here, two nested <c>FunctionInvokingChatClient</c>
    /// instances form: the inner one resolves tools, the outer one never sees
    /// any call. This is <strong>not</strong> merely cosmetic. The registry
    /// places the content guard directly above the client this method returns,
    /// so that every turn of the tool-call loop is inspected. With an inner
    /// loop, the turn that carries a tool result back into the model runs
    /// <em>beneath</em> the guard: measured on a single-tool run, the guard
    /// sees the tool result only on its way out
    /// (<c>ContentGuardDirection.Output</c>) and never on its way in
    /// (<c>ContentGuardDirection.Input</c>) — which is the exact path prompt
    /// injection takes. The reply text and the tool-call count are unchanged,
    /// so nothing else reveals the mistake.
    /// </para>
    /// <para>
    /// <strong>Lifetime of the returned client.</strong> Tracon does
    /// <strong>not</strong> dispose it. The client is built once per compiled
    /// agent and stored in it; the compiled agent
    /// (<c>Microsoft.Agents.AI.ChatClientAgent</c>) implements neither
    /// <see cref="IDisposable"/> nor <see cref="IAsyncDisposable"/>, and
    /// evicting an agent from the compile cache drops the reference without
    /// disposing. An implementation therefore owns the lifetime of whatever it
    /// returns, and what it returns must tolerate never being disposed. This
    /// is why the shipped providers return clients backed by a long-lived,
    /// shared SDK client rather than a per-call one: returning a client that
    /// holds a resource needing release would leak it.
    /// </para>
    /// </remarks>
    IChatClient CreateChatClient(ModelBinding binding);
}

/// <summary>
/// A model provider that can build clients from resolved per-tenant credentials.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface only when the provider supports tenant-provided
/// credentials. A provider that implements only <see cref="IModelProvider"/>
/// continues to use its setup-time credential. If a tenant binding exists for
/// such a provider, Tracon fails before invoking the provider and never
/// falls back to the setup-time credential.
/// </para>
/// <para>
/// A compiled agent's chat client is a fixed pipeline object. A client built
/// with a tenant credential is therefore never stored in the shared compile
/// cache. The registry enforces this through
/// <see cref="IModelProviderRegistry.HasTenantProviderOverrideAsync"/>.
/// </para>
/// </remarks>
public interface ITenantCredentialModelProvider : IModelProvider
{
    /// <summary>Produces a raw chat client using a resolved tenant credential.</summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="credential">The non-null tenant credential.</param>
    /// <returns>The provider-specific raw chat client.</returns>
    /// <remarks>
    /// The implementation uses <see cref="ModelProviderCredential.ApiKey"/>
    /// and, when present, <see cref="ModelProviderCredential.Endpoint"/>.
    /// The API key must never fall back to the setup-time key. An endpoint may
    /// fall back to a setup-time endpoint when only the key is overridden.
    /// </remarks>
    IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential);
}
