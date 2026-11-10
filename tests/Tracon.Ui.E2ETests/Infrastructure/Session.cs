using Microsoft.Playwright;

namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>The browser context for a single test.</summary>
/// <remarks>
/// The session keeps what the page said while the test ran (console errors, page errors, requests that failed or were
/// refused) and writes it, with the page's address and visible text, to the test's output when it is disposed. A test
/// that times out waiting for an element otherwise leaves nothing to say whether the page was on the right screen.
/// </remarks>
internal sealed class Session : IAsyncDisposable
{
    private const int BodyTextLimit = 600;

    private readonly IBrowserContext _context;
    private readonly List<string> _events = [];

    private Session(IBrowserContext context, IPage page)
    {
        _context = context;
        Page = page;

        page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning")
            {
                Record($"console {message.Type}: {message.Text}");
            }
        };
        page.PageError += (_, error) => Record($"page error: {error}");
        page.RequestFailed += (_, request) => Record($"request failed: {request.Method} {request.Url} ({request.Failure})");
        page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                Record($"response {response.Status}: {response.Request.Method} {response.Url}");
            }
        };
    }

    public IPage Page { get; }

    /// <param name="locale">
    /// The browser's language.
    /// <para>
    /// 🚨 Since then the UI picks its own language from
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

    public async ValueTask DisposeAsync()
    {
        await WriteDiagnosticsAsync().ConfigureAwait(false);
        await _context.CloseAsync().ConfigureAwait(false);
    }

    private void Record(string line)
    {
        lock (_events)
        {
            _events.Add(line);
        }
    }

    private async Task WriteDiagnosticsAsync()
    {
        var output = TestContext.Current.TestOutputHelper;

        if (output is null)
        {
            return;
        }

        try
        {
            var body = await Page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 2000 }).ConfigureAwait(false);
            var visible = body.Length <= BodyTextLimit ? body : body[..BodyTextLimit] + "...";

            output.WriteLine($"[page] {Page.Url}");
            output.WriteLine($"[page text] {visible.ReplaceLineEndings(" | ")}");
        }
        catch (PlaywrightException exception)
        {
            output.WriteLine($"[page] could not be read: {exception.Message}");
        }

        string[] events;

        lock (_events)
        {
            events = [.. _events];
        }

        foreach (var line in events)
        {
            output.WriteLine($"[page event] {line}");
        }
    }
}
