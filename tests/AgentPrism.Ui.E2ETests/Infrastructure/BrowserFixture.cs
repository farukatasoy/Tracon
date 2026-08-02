using Microsoft.Playwright;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// Tum arayuz testlerinin paylastigi tarayici ornegi.
/// </summary>
/// <remarks>
/// <para>
/// Playwright'in tarayici ikilileri NuGet paketiyle gelmez. Eksikse fixture
/// onlari kendisi indirir, boylece <c>dotnet test</c> her makinede ek bir kurulum
/// adimi olmadan calisir. Ilk calistirma bu yuzden uzun surer; sonrakiler
/// indirilmis ikiliyi kullanir.
/// </para>
/// <para>
/// Yalnizca Chromium indirilir. Uc tarayicinin tamami arayuzun kendisinden cok
/// daha buyuk bir indirme maliyeti getirir ve bu testler tarayici farklarini
/// degil, arayuzun davranisini dogrular.
/// </para>
/// </remarks>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    /// <summary>Test suresince acik kalan tarayici.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Playwright Chromium indirilemedi (cikis kodu {exitCode}). " +
                "Linux'ta sistem bagimliliklari da gerekebilir: playwright install --with-deps chromium");
        }

        _playwright = await Playwright.CreateAsync();

        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}
