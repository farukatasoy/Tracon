using System.Net;
using System.Net.WebSockets;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Gercek zamanli konusma ucunu (<c>/api/voice/sessions/{id}/stream</c>) dogrular.
/// </summary>
/// <remarks>
/// Test projesi <c>AgentPrism.Voice</c>'a referans <strong>VERMEZ</strong>:
/// konusma katmani yalnizca <see cref="ISpeechTranscriber"/> ve
/// <see cref="ISpeechSynthesizer"/> soyutlamalarini bilir. ElevenLabs bir
/// uygulamadir (K-215).
/// </remarks>
public sealed class VoiceConversationTests
{
    private const string Agent = "kod-agent";
    private const string StreamPath = "/agentprism/api/voice/sessions/oturum-1/stream";

    [Fact]
    public async Task UseVoiceConversation_cagrilmadiysa_HICBIR_uc_acilmaz()
    {
        // 🚨 Bu fazin en onemli testi: barindirma modelini degistiren bir
        // yetenek sessizce acilmaz. Uc 501 ("var ama kapali") DEGIL, 404
        // ("boyle bir adres yok") doner — cunku gercekten yoktur.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri(StreamPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ses_saglayicisi_yoksa_501_doner()
    {
        // Konusma hem cozum hem sentez ister; yalniz biriyle konusma tek yonlu
        // olurdu ve bu bir konusma degildir.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder => builder.UseVoiceConversation());

        using var response = await host.Client.GetAsync(new Uri(StreamPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task WebSocket_olmayan_istek_400_doner()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(new Uri(StreamPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Uctan_uca_bir_tur_konusma()
    {
        var voice = new StubVoiceProvider { Transcript = "siparisim nerede" };
        await using var host = await StartAsync(voice);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var ready = await client.WaitForAsync("ready");
        ready.Text("agent").ShouldBe(Agent);
        ready.Text("sessionId").ShouldBe("oturum-1");

        // Ses varsayilan olarak SAKLANMAZ ve arayuz bunu kullaniciya gosterir.
        ready.Flag("persistAudio").ShouldBe(false);

        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });

        var frames = new List<VoiceEvent>();

        var transcript = await client.WaitForAsync("transcript", frames);
        transcript.Text("text").ShouldBe("siparisim nerede");
        transcript.Flag("final").ShouldBe(true);

        var runStarted = await client.WaitForAsync("runStarted", frames);
        Guid.TryParse(runStarted.Text("runId"), out _).ShouldBeTrue();

        var done = await client.WaitForAsync("done", frames);
        done.Flag("cancelled").ShouldBe(false);

        // Altyazi metni akti, ses parcalari geldi.
        frames.ShouldContain(static frame => frame.Type == "text");
        frames.ShouldContain(static frame => frame.Type == "audioStart");
        frames.ShouldContain(static frame => frame.IsAudio);
        frames.ShouldContain(static frame => frame.Type == "audioEnd");

        // 🚨 Ham PCM cozume WAV olarak gider: basliksiz ses tek basina bir dosya
        // degildir.
        voice.ReceivedMediaType.ShouldBe("audio/wav");
        voice.ReceivedBytes.ShouldBe(3200 + 44);
        voice.Spoken.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Her_tur_normal_bir_runs_satiri_uretir()
    {
        // Ses calistirma yolunu DEGISTIRMEZ; yalnizca girdi ve cikti bicimini
        // degistirir. Kanit: kayit, span ve maliyet mevcut yoldan gelir.
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        var runStarted = await client.WaitForAsync("runStarted");
        await client.WaitForAsync("done");

        var runId = Guid.Parse(runStarted.Text("runId")!);
        var runs = host.Services.GetRequiredService<IRunStore>();

        var record = await runs.GetRunAsync(runId, TestContext.Current.CancellationToken);

        record.ShouldNotBeNull();
        record.AgentName.ShouldBe(Agent);
        record.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Kesinti_calistirmayi_iptal_eder_ve_yarim_yaniti_gecmise_yazar()
    {
        // 🚨 Kesilen yanit yazilmazsa model bir sonraki turda kendi yarim
        // cumlesini GORMEZ ve konusma kopar.
        var voice = new StubVoiceProvider { SynthesisDelay = TimeSpan.FromSeconds(5) };
        await using var host = await StartAsync(voice);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        // Sentez basladi ama bitmedi: tam kesinti ani.
        await client.WaitForAsync("audioStart");
        await client.SendControlAsync(new { type = "cancel" });

        var done = await client.WaitForAsync("done");
        done.Flag("cancelled").ShouldBe(true);

        using var history = await host.Client.GetAsync(
            new Uri("/agentprism/api/sessions/oturum-1", UriKind.Relative));

        history.EnsureSuccessStatusCode();

        var text = await history.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Contains("kesildi", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    [Fact]
    public async Task Konusma_kaydi_yazilir_ve_ses_ICERMEZ()
    {
        await using var host = await StartAsync();

        await using (var client = await ConnectAsync(host))
        {
            await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
            await client.WaitForAsync("ready");

            await client.SendAudioAsync(new byte[1600]);
            await client.SendControlAsync(new { type = "commit" });
            await client.WaitForAsync("done");

            await client.SendControlAsync(new { type = "stop" });
        }

        // Kapanis kaydin yazilmasini bekler; uc bunu okuyabilmelidir.
        var records = await WaitForRecordsAsync(host);

        records.GetArrayLength().ShouldBe(1);
        records[0].GetProperty("agentName").GetString().ShouldBe(Agent);
        records[0].GetProperty("turns").GetInt32().ShouldBe(1);
        records[0].GetProperty("sessionId").GetString().ShouldBe("oturum-1");
        records[0].GetProperty("endReason").GetString().ShouldBe("Client");

        // Kayit ses ICERMEZ: sutunlarin hicbiri icerik tasimaz.
        records[0].TryGetProperty("audio", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PersistAudio_acikken_YALNIZ_agentin_sesi_ek_olarak_yazilir()
    {
        // 🚨 Kullanicinin sesi hicbir zaman saklanmaz: ses biyometrik veridir ve
        // soylenenin kaydi zaten oturum gecmisindeki transkripttir. Saklanan tek
        // sey agent'in URETTIGI sestir.
        await using var host = await StartAsync(configureConversation: static options =>
            options.PersistAudio = true);

        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        // Arayuz kullaniciya kaydin yapildigini GOSTERIR; kayit sessizce olmaz.
        (await client.WaitForAsync("ready")).Flag("persistAudio").ShouldBe(true);

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        var done = await client.WaitForAsync("done");
        var attachmentId = done.Text("attachmentId").ShouldNotBeNull();

        using var download = await host.Client.GetAsync(
            new Uri($"/agentprism/api/attachments/{attachmentId}", UriKind.Relative));

        download.EnsureSuccessStatusCode();
        download.Content.Headers.ContentType?.MediaType.ShouldBe("audio/mpeg");

        // Ek oturuma BAGLI olmalidir; sahipsiz bir ek saklama politikasi
        // tarafindan silinir (Faz 28/G1).
        using var listing = await host.Client.GetAsync(
            new Uri("/agentprism/api/attachments?sessionId=oturum-1", UriKind.Relative));

        listing.EnsureSuccessStatusCode();

        var body = await AgentPrismTestHost.ReadJsonAsync(listing);
        body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Ses_varsayilan_olarak_SAKLANMAZ()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        (await client.WaitForAsync("ready")).Flag("persistAudio").ShouldBe(false);

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        (await client.WaitForAsync("done")).Text("attachmentId").ShouldBeNull();

        using var listing = await host.Client.GetAsync(
            new Uri("/agentprism/api/attachments?sessionId=oturum-1", UriKind.Relative));

        listing.EnsureSuccessStatusCode();
        (await AgentPrismTestHost.ReadJsonAsync(listing)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Token_alt_protokolde_kabul_edilir()
    {
        await using var host = await StartAsync(authToken: "gizli-token");
        await using var client = await ConnectAsync(host, token: "gizli-token");

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        (await client.WaitForAsync("ready")).Text("agent").ShouldBe(Agent);
    }

    [Fact]
    public async Task Token_SORGU_DIZESINDE_kabul_EDILMEZ()
    {
        // 🚨 Adres sunucu gunluklerine, ters vekil gunluklerine ve tarayici
        // gecmisine yazilir; token oraya konmaz.
        await using var host = await StartAsync(authToken: "gizli-token");

        var socketClient = host.CreateWebSocketClient();
        socketClient.ConfigureRequest = static request => request.Headers.Remove("Authorization");

        var connect = async () => await socketClient.ConnectAsync(
            new Uri("http://localhost/agentprism/api/voice/sessions/oturum-1/stream?token=gizli-token"),
            TestContext.Current.CancellationToken);

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Token_yanlissa_baglanti_reddedilir()
    {
        await using var host = await StartAsync(authToken: "gizli-token");

        var connect = async () => await ConnectAsync(host, token: "yanlis-token");

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Baska_kiracinin_oturumuna_ERISILEMEZ()
    {
        // sessionId guvenilmez girdidir.
        //
        // 🚨 Faz 41'de davranis DEGISTI. Once depo kiraci korlerdi: uc baska bir
        // kiracinin oturumunu okuyabiliyor, goruyor ve "baskasinin" diye
        // REDDEDIYORDU. Bu bir varlik kahiniydi — bir kiraci, hangi oturum
        // kimliklerinin BASKA bir kiracida var oldugunu hata koduyla olcebilirdi.
        // Depo artik kiraciyla sinirlidir (K-277): gorunmeyen bir oturum YOK
        // sayilir, baglanti kendi kiracisinda taze bir oturum acar ve digerinin
        // kaydina HIC dokunulmaz. Yalitim guclendi, sizdirilan bilgi azaldi.
        await using var host = await StartAsync();

        var sessions = host.Services.GetRequiredService<ISessionStore>();

        await sessions.SaveAsync(
            new SessionRecord
            {
                Id = "baskasinin-oturumu",
                AgentName = Agent,
                State = System.Text.Json.JsonDocument.Parse("""{"gizli":true}""").RootElement,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                TenantId = "baska-kiraci",
            },
            TestContext.Current.CancellationToken);

        // Gecerli kiraci o kaydi hic goremez.
        (await sessions.GetAsync("baskasinin-oturumu", TestContext.Current.CancellationToken)).ShouldBeNull();

        var socketClient = host.CreateWebSocketClient();

        using (await socketClient.ConnectAsync(
            new Uri("http://localhost/agentprism/api/voice/sessions/baskasinin-oturumu/stream"),
            TestContext.Current.CancellationToken))
        {
            // Baglanti kurulur; ama karsisindaki oturum digerinin oturumu DEGILDIR.
        }

        // Digerinin kaydi bozulmadan yerinde durur.
        var theirs = (await sessions.QueryAsync(
            new SessionQuery { TenantId = "baska-kiraci" },
            TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        theirs.Id.ShouldBe("baskasinin-oturumu");
        theirs.State.GetProperty("gizli").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Es_zamanli_baglanti_siniri_uygulanir()
    {
        await using var host = await StartAsync(configureConversation: static options =>
            options.MaxConcurrentConnectionsPerTenant = 1);

        await using var first = await ConnectAsync(host);

        var connect = async () => await ConnectAsync(host);

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Bilinmeyen_ses_bicimi_reddedilir()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "mp3" });

        (await client.WaitForAsync("error")).Text("message")
            .ShouldNotBeNull()
            .ShouldContain("mp3");
    }

    [Fact]
    public async Task Olmayan_agent_hata_dondurur()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = "yok-boyle-bir-agent" });

        (await client.WaitForAsync("error")).Text("message")
            .ShouldNotBeNull()
            .ShouldContain("yok-boyle-bir-agent");
    }

    [Fact]
    public async Task Ses_gelmeden_commit_tur_URETMEZ()
    {
        // Kisa bir oksuruk de istemcinin VAD'ini tetikleyebilir; bu bir hata
        // degildir ve konusmayi kesmemelidir.
        var voice = new StubVoiceProvider();
        await using var host = await StartAsync(voice);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendControlAsync(new { type = "commit" });

        // Yeni bir tur yine baslatilabiliyorsa dinlemeye donulmustur.
        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        (await client.WaitForAsync("done")).Json.ShouldNotBeNull()
            .GetProperty("turn").GetInt32().ShouldBe(1);
    }

    private static Task<AgentPrismTestHost> StartAsync(
        StubVoiceProvider? voice = null,
        string? authToken = null,
        Action<VoiceConversationOptions>? configureConversation = null)
    {
        var provider = voice ?? new StubVoiceProvider();

        return AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseVoiceConversation(options =>
                {
                    options.OutputMediaType = "audio/mpeg";
                    configureConversation?.Invoke(options);
                }),
            configureEndpoints: options =>
            {
                if (authToken is { Length: > 0 })
                {
                    options.AuthToken = authToken;
                }
            },
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(provider);
                services.AddSingleton<ISpeechSynthesizer>(provider);
            });
    }

    private static async Task<VoiceConversationClient> ConnectAsync(
        AgentPrismTestHost host,
        string? token = null,
        string sessionId = "oturum-1")
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        if (token is { Length: > 0 })
        {
            socketClient.SubProtocols.Add(VoiceConversationProtocol.TokenSubProtocolPrefix + token);
        }

        var socket = await socketClient.ConnectAsync(
            new Uri($"http://localhost/agentprism/api/voice/sessions/{sessionId}/stream"),
            TestContext.Current.CancellationToken);

        return new VoiceConversationClient(socket);
    }

    /// <summary>Kayit yazilana kadar konusma listesini yoklar.</summary>
    /// <remarks>
    /// Kayit soketin kapanisinda yazilir; istemcinin <c>Dispose</c>'u sunucunun
    /// kapanis isini beklemez.
    /// </remarks>
    private static async Task<System.Text.Json.JsonElement> WaitForRecordsAsync(AgentPrismTestHost host)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await host.Client.GetAsync(
                new Uri("/agentprism/api/voice/sessions", UriKind.Relative));

            response.EnsureSuccessStatusCode();

            var body = await AgentPrismTestHost.ReadJsonAsync(response);

            if (body.GetArrayLength() > 0)
            {
                return body;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException("Konusma kaydi yazilmadi.");
    }
}
