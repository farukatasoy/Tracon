using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// <c>/api/api-keys</c> uclarinin CRUD davranisi (Faz 53).
/// </summary>
/// <remarks>
/// 🚨 Bu testlerin korudugu kural: ham anahtar deger yalnizca olusturma
/// yanitinda doner; hicbir listeleme cagrisi onu bir daha vermez (bolum 53.2).
/// </remarks>
public sealed class ApiKeyEndpointTests
{
    [Fact]
    public async Task Olusturma_ham_degeri_bir_kez_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name = "ci", scopes = new[] { "RunsRead" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>();

        created.ShouldNotBeNull();
        created.PlaintextKey.ShouldStartWith("ap_");
        created.Record.Name.ShouldBe("ci");
        created.Record.KeyPrefix.ShouldStartWith("ap_");
    }

    [Fact]
    public async Task Listeleme_ham_deger_veya_ozet_dondurmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await CreateKeyAsync(host, "ci", "RunsRead");

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/api-keys", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(created.PlaintextKey);

        var keys = await response.Content.ReadFromJsonAsync<IReadOnlyList<ApiKeyRecord>>();
        keys.ShouldNotBeNull();
        keys.ShouldHaveSingleItem().Id.ShouldBe(created.Record.Id);
    }

    [Fact]
    public async Task Bos_ad_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name = "", scopes = new[] { "RunsRead" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bos_kapsam_listesi_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name = "ci", scopes = Array.Empty<string>() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bilinmeyen_kapsam_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name = "ci", scopes = new[] { "runs:hepsi" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Iptal_edilen_anahtar_listede_pasif_gorunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var created = await CreateKeyAsync(host, "ci", "RunsRead");

        using var revoke = await host.Client.DeleteAsync(
            new Uri($"/agentprism/api/api-keys/{created.Record.Id}", UriKind.Relative));

        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var keys = await host.Client.GetFromJsonAsync<IReadOnlyList<ApiKeyRecord>>(
            new Uri("/agentprism/api/api-keys", UriKind.Relative));

        keys.ShouldNotBeNull().ShouldHaveSingleItem().IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Olmayan_anahtar_iptali_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync(
            new Uri($"/agentprism/api/api-keys/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    internal static async Task<ApiKeyCreationResult> CreateKeyAsync(
        AgentPrismTestHost host,
        string name,
        params string[] scopes)
    {
        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name, scopes });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>()).ShouldNotBeNull();
    }
}
