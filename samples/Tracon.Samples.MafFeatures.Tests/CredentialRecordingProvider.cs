using Microsoft.Extensions.AI;
using Tracon.Testing;

namespace Tracon.Samples.MafFeatures.Tests;

/// <summary>
/// A scripted provider that remembers which key every chat client was built with.
/// </summary>
/// <remarks>
/// <see cref="FakeModelProvider"/> accepts a tenant credential and ignores it. This test
/// double records it, so a test can tell the tenant's own key from the setup-time key.
/// </remarks>
internal sealed class CredentialRecordingProvider : ITenantCredentialModelProvider
{
    /// <summary>What <see cref="Keys"/> holds when the setup-time key was used.</summary>
    public const string SetupKey = "setup-key";

    private readonly FakeModelProvider _inner;
    private readonly List<string> _keys = [];

    public CredentialRecordingProvider(string name, ModelDescriptor model)
    {
        Name = name;
        _inner = new FakeModelProvider(name)
            .WithModel(model)
            .RespondsWith("Invoice 7 is paid.", inputTokens: 1_000, outputTokens: 500);
    }

    public string Name { get; }

    public IReadOnlyList<ModelDescriptor> Models => _inner.Models;

    /// <summary>One entry per chat client built: the tenant's key, or <see cref="SetupKey"/>.</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            lock (_keys)
            {
                return [.. _keys];
            }
        }
    }

    public IChatClient CreateChatClient(ModelBinding binding)
    {
        Record(SetupKey);

        return _inner.CreateChatClient(binding);
    }

    public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        Record(credential.ApiKey);

        return _inner.CreateChatClient(binding, credential);
    }

    private void Record(string key)
    {
        lock (_keys)
        {
            _keys.Add(key);
        }
    }
}
