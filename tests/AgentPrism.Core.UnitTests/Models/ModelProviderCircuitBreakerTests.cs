using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// Devre kesici durum makinesini dogrular: <c>Closed</c> &#8594; <c>Open</c>
/// (esik asildiginda) &#8594; <c>HalfOpen</c> (mola suresi dolunca, tek deneme)
/// &#8594; <c>Closed</c> veya yeniden <c>Open</c>.
/// </summary>
public sealed class ModelProviderCircuitBreakerTests
{
    private const string Provider = "test-saglayici";

    [Fact]
    public void Kapaliyken_istek_serbesttir()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor());

        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Esik_asilinca_devre_acilir_ve_istek_reddedilir()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 3));

        breaker.RecordFailure(Provider);
        breaker.RecordFailure(Provider);
        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));

        breaker.RecordFailure(Provider); // 3. ardisik hata: esik asildi
        var exception = Should.Throw<AgentPrismProviderUnavailableException>(
            () => breaker.EnsureRequestAllowed(Provider));

        exception.ProviderName.ShouldBe(Provider);
    }

    [Fact]
    public void Basarili_cagri_ardisik_hata_sayacini_sifirlar()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 2));

        breaker.RecordFailure(Provider);
        breaker.RecordSuccess(Provider);
        breaker.RecordFailure(Provider);

        // Basari sayaci sifirladigi icin bu ikinci hata devreyi ACMAMALI (esik=2).
        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Mola_suresi_dolmadan_yeniden_denenmez()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);
        Should.Throw<AgentPrismProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));

        time.Advance(TimeSpan.FromSeconds(10));
        Should.Throw<AgentPrismProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Mola_suresi_dolunca_tek_deneme_gecer()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);
        time.Advance(TimeSpan.FromSeconds(31));

        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Yari_acik_deneme_basarisiz_olursa_devre_hemen_yeniden_acilir()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);
        time.Advance(TimeSpan.FromSeconds(31));
        breaker.EnsureRequestAllowed(Provider); // yari-acik deneme baslar
        breaker.RecordFailure(Provider); // deneme basarisiz

        Should.Throw<AgentPrismProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Yari_acik_deneme_basarili_olursa_devre_kapanir()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);
        time.Advance(TimeSpan.FromSeconds(31));
        breaker.EnsureRequestAllowed(Provider); // yari-acik deneme baslar
        breaker.RecordSuccess(Provider);

        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Kapatilinca_devre_hicbir_zaman_acilmaz()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o =>
        {
            o.Enabled = false;
            o.FailureThreshold = 1;
        }));

        breaker.RecordFailure(Provider);
        breaker.RecordFailure(Provider);
        breaker.RecordFailure(Provider);

        Should.NotThrow(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Esik_asilmadan_once_esik_asilmamis_sayilir_saglik_ucunda()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 2));

        breaker.RecordFailure(Provider);

        breaker.IsOpen(Provider, out var retryAfter).ShouldBeFalse();
        retryAfter.ShouldBeNull();
    }

    [Fact]
    public void Devre_acikken_IsOpen_kalan_sureyi_bildirir_ve_durumu_degistirmez()
    {
        var time = new ManualTimeProvider();
        var breaker = new ModelProviderCircuitBreaker(
            Monitor(o =>
            {
                o.FailureThreshold = 1;
                o.BreakDuration = TimeSpan.FromSeconds(30);
            }),
            time);

        breaker.RecordFailure(Provider);

        breaker.IsOpen(Provider, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldNotBeNull();
        retryAfter!.Value.ShouldBeInRange(TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(30));

        // IsOpen durumu degistirmemeli: hemen ardindan istek yine reddedilmeli.
        Should.Throw<AgentPrismProviderUnavailableException>(() => breaker.EnsureRequestAllowed(Provider));
    }

    [Fact]
    public void Farkli_saglayicilarin_durumu_birbirinden_bagimsizdir()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 1));

        breaker.RecordFailure("saglayici-a");

        Should.Throw<AgentPrismProviderUnavailableException>(() => breaker.EnsureRequestAllowed("saglayici-a"));
        Should.NotThrow(() => breaker.EnsureRequestAllowed("saglayici-b"));
    }

    [Fact]
    public async Task Wrap_saglayiciya_gitmeden_once_devreyi_denetler()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 1));
        breaker.RecordFailure(Provider);

        var inner = new FakeChatClient();
        using var wrapped = breaker.Wrap(Provider, inner);

        await Should.ThrowAsync<AgentPrismProviderUnavailableException>(
            () => wrapped.GetResponseAsync([]));

        inner.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Wrap_basarisiz_cagriyi_hata_olarak_sayar()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor(o => o.FailureThreshold = 1));
        var inner = new ThrowingChatClient();
        using var wrapped = breaker.Wrap(Provider, inner);

        await Should.ThrowAsync<InvalidOperationException>(() => wrapped.GetResponseAsync([]));

        breaker.IsOpen(Provider, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Wrap_basarili_cagridan_sonra_devre_kapali_kalir()
    {
        var breaker = new ModelProviderCircuitBreaker(Monitor());
        var inner = new FakeChatClient();
        using var wrapped = breaker.Wrap(Provider, inner);

        await wrapped.GetResponseAsync([]);

        breaker.IsOpen(Provider, out _).ShouldBeFalse();
    }

    private static IOptionsMonitor<AgentPrismOptions> Monitor(Action<AgentPrismCircuitBreakerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.Configure<AgentPrismOptions>(options => configure?.Invoke(options.CircuitBreaker));

        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<AgentPrismOptions>>();
    }

    private sealed class ThrowingChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("saglayici hatasi");

        public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("saglayici hatasi");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
