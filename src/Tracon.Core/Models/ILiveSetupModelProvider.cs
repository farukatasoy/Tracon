namespace Tracon;

/// <summary>
/// Marks a provider whose setup key changes while the host runs: it takes the key from a configuration key it was given
/// the name of, not from a value read once.
/// </summary>
/// <remarks>
/// <para>
/// The registry builds the mask of a pipeline once, but the key a call sends is the one that is current when the call
/// starts. For a provider that carries this marker the mask asks <see cref="IModelProvider.SetupApiKeys"/> again when
/// a failure is written, and that list holds the key now in use and the ones it replaced, so the text of a call that
/// began before a rotation is masked too.
/// </para>
/// <para>
/// The interface only says whether the keys change; the one place that reads them stays the registry
/// (<c>SetupApiKeysReaderTests</c>).
/// </para>
/// </remarks>
internal interface ILiveSetupModelProvider
{
    /// <summary>Gets a value indicating whether the setup keys of this provider change while the host runs.</summary>
    bool SetupKeysChange { get; }
}
