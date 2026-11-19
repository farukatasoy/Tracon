using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// The clients of a live setup are kept under the setup scope, replaced when the pair changes, and never handed from one
/// provider name to another; a call that is already cancelled builds nothing.
/// </summary>
public sealed class LiveSetupTests
{
    private const string KeyName = "Platform:Key";

    [Fact]
    public void The_same_snapshot_gives_the_same_factory_and_a_new_key_gives_a_new_one()
    {
        var (live, configuration, built) = Setup("key-one-0001");
        var first = live.Current();

        live.Current().ShouldBeSameAs(first);
        built.Count.ShouldBe(1, "the factory the host started with serves its own snapshot");

        configuration[KeyName] = "key-two-0002";

        var second = live.Current();

        second.ShouldNotBeSameAs(first);
        live.Current().ShouldBeSameAs(second);
        built.Count.ShouldBe(2);
    }

    [Fact]
    public void Two_provider_names_that_share_a_setup_do_not_share_a_chat_client_for_the_same_model()
    {
        var (live, _, _) = Setup("key-one-0001");
        var snapshot = live.Snapshot();

        var chat = live.ChatClient(snapshot, Binding("openai"), static (_, _) => new NullChatClient());
        var responses = live.ChatClient(snapshot, Binding("openai-responses"), static (_, _) => new NullChatClient());

        responses.ShouldNotBeSameAs(chat);
        live.ChatClient(snapshot, Binding("openai"), static (_, _) => new NullChatClient()).ShouldBeSameAs(chat);
    }

    [Fact]
    public async Task A_call_that_is_already_cancelled_reads_no_configuration_and_builds_no_client()
    {
        var calls = 0;
        using var client = new LiveSetupChatClient(() =>
        {
            calls++;

            return new NullChatClient();
        });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: cancelled.Token));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: cancelled.Token))
            {
                _ = update;
            }
        });

        calls.ShouldBe(0);
    }

    [Fact]
    public async Task Callers_that_ask_at_once_for_a_new_snapshot_build_one_factory()
    {
        var (live, configuration, built) = Setup("key-one-0001");

        configuration[KeyName] = "key-two-0002";

        var factories = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(live.Current, TestContext.Current.CancellationToken)));

        factories.Distinct().Count().ShouldBe(1);
        built.Count.ShouldBe(2, "one for the start and one for the new key");
    }

    [Fact]
    public void An_environment_that_changes_replaces_the_entry_of_a_tenant_with_the_same_key()
    {
        var cache = new ProviderCredentialClientCache<object>();
        var credential = TenantCredentialScope.Attach(new ModelProviderCredential { ApiKey = "tenant-key-0001" }, "tenant-a", "openai");

        var first = cache.GetOrAdd(credential, static _ => new object(), "https://one.example/");

        cache.GetOrAdd(credential, static _ => new object(), "https://one.example/").ShouldBeSameAs(first);
        cache.GetOrAdd(credential, static _ => new object(), "https://two.example/").ShouldNotBeSameAs(first);
        cache.Count.ShouldBe(1, "the entry of the binding is replaced, not added to");
    }

    private static ModelBinding Binding(string provider) => new() { Provider = provider, Model = "m" };

    private static (LiveSetup<object> Live, IConfiguration Configuration, List<ModelProviderCredential> Built) Setup(string key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(KeyName, key)])
            .Build();
        var source = new SetupCredentialSource("acme", "AcmeProviderOptions.ApiKey", null, null, KeyName, null, configuration, logger: null);
        var built = new List<ModelProviderCredential>();
        var first = source.Current();

        object Build(ModelProviderCredential snapshot)
        {
            lock (built)
            {
                built.Add(snapshot);
            }

            return new object();
        }

        var initial = Build(first);

        return (new LiveSetup<object>(source, first, initial, Build), configuration, built);
    }

    private sealed class NullChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse());

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
