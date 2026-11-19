namespace Tracon;

/// <summary>
/// The vector search store this host has, or none. Tracon reads whether vector search is
/// available here, and never by resolving <see cref="IVectorSearchStore"/> itself.
/// </summary>
/// <remarks>
/// <para>
/// Availability can depend on settings that are read only when the container is built: the
/// PostgreSQL provider has a store only while <c>EnableKnowledge</c> is on, and that value comes
/// from <c>IOptions</c>. A registration of <see cref="IVectorSearchStore"/> whose factory returns
/// <see langword="null"/> expresses "none" in Microsoft's container, but Autofac and other
/// containers refuse a null from a factory, so a host built on one of them failed on the first
/// agent the catalog compiled. This holder is never null; only its <see cref="Store"/> is.
/// </para>
/// <para>
/// <c>AddTracon()</c> fills it from the consumer's own <see cref="IVectorSearchStore"/>
/// registration, if there is one. A provider that ships a store replaces it, and still lets a
/// consumer's own registration win.
/// </para>
/// </remarks>
/// <param name="store">The store, or <see langword="null"/> when this host has none.</param>
internal sealed class VectorSearchStoreSlot(IVectorSearchStore? store)
{
    /// <summary>The store, or <see langword="null"/> when this host has none.</summary>
    public IVectorSearchStore? Store { get; } = store;
}
