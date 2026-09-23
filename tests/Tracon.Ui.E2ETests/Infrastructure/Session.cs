using Microsoft.Playwright;

namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>The browser context for a single test.</summary>
internal sealed class Session : IAsyncDisposable
{
    private readonly IBrowserContext _context;

    private Session(IBrowserContext context, IPage page)
    {
        _context = context;
        Page = page;
    }

    public IPage Page { get; }

    /// <param name="locale">
    /// The browser's language.
    /// <para>
    /// 🚨 Since Phase 30 the UI picks its own language from
    /// <c>navigator.language</c>. A test that asserts on text MUST pin the
    /// language; otherwise the test's outcome depends on the running
    /// machine's system language. The default is therefore <c>en-US</c>,
    /// and only the language tests pass a different value.
    /// </para>
    /// </param>
    /// <param name="colorScheme">
    /// What the operating system claims to prefer. Playwright's own default
    /// is <c>Light</c>, which is what every test that does not care runs
    /// with; <c>NoPreference</c> is how the console's dark-first default is
    /// observed.
    /// </param>
    public static async Task<Session> OpenAsync(
        BrowserFixture browsers,
        UiHost host,
        string locale = "en-US",
        ColorScheme? colorScheme = null)
    {
        var context = await browsers.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = host.BaseAddress,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            Locale = locale,
            ColorScheme = colorScheme,
        });

        return new Session(context, await context.NewPageAsync());
    }

    public async ValueTask DisposeAsync() => await _context.CloseAsync();
}
