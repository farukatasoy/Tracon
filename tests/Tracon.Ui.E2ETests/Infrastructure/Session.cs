using Microsoft.Playwright;

namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>The browser context for a single test.</summary>
/// <remarks>
/// The session keeps what the page said while the test ran (console errors, page errors, requests that failed or were
/// refused) and records a Playwright trace of the whole test. When the session is disposed it hands both, with the page's
/// address and visible text, to the test's evidence; the evidence keeps them only if the test failed. A test that times out
/// waiting for an element otherwise leaves nothing to say whether the page was on the right screen.
/// </remarks>
internal sealed class Session : IAsyncDisposable
{
    private const int BodyTextLimit = 600;

    private const int EventLimit = 400;

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
        page.Request += (_, request) =>
        {
            // The trace carries no network log (see OpenAsync), so the calls to the product's API are written down here, with
            // whether the request carried the bearer header: a page that shows "the server rejected that token" is otherwise
            // a 401 with nobody able to say what was sent.
            if (request.Url.Contains("/api/", StringComparison.Ordinal))
            {
                var bearer = request.Headers.ContainsKey("authorization") ? "with" : "without";

                Record($"request: {request.Method} {new Uri(request.Url).PathAndQuery} ({bearer} an authorization header)");
            }
        };
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

        // The trace is the one record that shows what the page did between two assertions. It costs a screenshot per action,
        // so it is stopped into a temporary file and kept only for a test that failed. 🚨 No DOM snapshots: the recorder
        // fetches every resource the page shows, a blob: preview included, and that fetch is a connect-src violation the
        // content-security tests rightly report as a failure of the page.
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = false });

        return new Session(context, await context.NewPageAsync());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            var summary = await ReadSummaryAsync().ConfigureAwait(false);

            TestEvidence.Register("page", _ => summary);
            await SaveTraceAsync().ConfigureAwait(false);
        }
        finally
        {
            await _context.CloseAsync().ConfigureAwait(false);
        }
    }

    private void Record(string line)
    {
        lock (_events)
        {
            if (_events.Count < EventLimit)
            {
                _events.Add(line);
            }
        }
    }

    private async Task SaveTraceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tracon-trace-{Guid.NewGuid():N}.zip");

        try
        {
            await _context.Tracing.StopAsync(new TracingStopOptions { Path = path }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PlaywrightException or IOException)
        {
            // A context that is already gone has no trace to save; the page note still says what it can.
            return;
        }

        if (!TestEvidence.RegisterFile("trace", path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A temp file that cannot be deleted is the operating system's to clean up; it must not fail the test.
            }
        }
    }

    private async Task<string> ReadSummaryAsync()
    {
        var summary = new List<string>();

        try
        {
            var body = await Page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 2000 }).ConfigureAwait(false);
            var visible = body.Length <= BodyTextLimit ? body : body[..BodyTextLimit] + "...";

            summary.Add($"address: {Page.Url}");
            summary.Add($"text: {visible.ReplaceLineEndings(" | ")}");
        }
        catch (PlaywrightException exception)
        {
            summary.Add($"the page could not be read: {exception.Message}");
        }

        string[] events;

        lock (_events)
        {
            events = [.. _events];
        }

        foreach (var line in events)
        {
            summary.Add($"event: {line}");
        }

        return string.Join(Environment.NewLine, summary);
    }
}
