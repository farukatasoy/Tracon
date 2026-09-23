using System.Net.Http.Json;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The playground: streaming, attachments, parameters, speech and voice.
/// </summary>
public sealed class PlaygroundTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Playground_response_can_be_spoken_and_audio_element_plays()
    {
        // 🚨 Token is ON. This is the whole point of this test: if
        // `<audio src="api/attachments/{id}">` were written, the browser
        // would NOT attach the bearer header to the resource load and the
        // request would get a 401. The UI must fetch the bytes and wrap them
        // in an object URL.
        const string token = "e2e-audio-token";

        await using var host = await UiHost.StartAsync(authToken: token);
        await using var session = await Session.OpenAsync(browsers, host);

        // The token is entered on the shell screen; after that it is a normal session.
        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByLabel("Token").FillAsync(token);
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

        // BUG-S4-011: the `media-src` directive did not exist at all,
        // `<audio src="blob:...">` was silently rejected (`audio.error.code=4`).
        // The presence of the `src` attribute does NOT catch this; listen for
        // the CSP violation event to prove the browser actually allowed the
        // resource to open.
        await session.Page.EvaluateAsync(
            """
            () => {
                window.__cspViolations = [];
                document.addEventListener('securitypolicyviolation', (event) => {
                    window.__cspViolations.push(event.violatedDirective);
                });
            }
            """);

        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        var speak = session.Page.GetByTestId("playground-speak").First;
        await Expect(speak).ToBeVisibleAsync();
        await speak.ClickAsync();

        var audio = session.Page.GetByTestId("playground-audio").First;
        await Expect(audio).ToBeVisibleAsync();

        // The source is an object URL; it must NOT be the endpoint address.
        var source = await audio.GetAttributeAsync("src");
        source.ShouldNotBeNull();
        source.StartsWith("blob:", StringComparison.Ordinal).ShouldBeTrue(source);

        var hadCspViolation = await session.Page.EvaluateAsync<bool>(
            """
            () => new Promise((resolve) => {
                if (window.__cspViolations.length > 0) { resolve(true); return; }
                setTimeout(() => resolve(window.__cspViolations.length > 0), 500);
            })
            """);
        hadCspViolation.ShouldBeFalse();
    }

    [Fact]
    public async Task Playground_voice_mode_opens_microphone_and_shows_transcript()
    {
        // 🚨 Runs with a fake media device (BrowserFixture flags). There is no
        // real microphone; Chromium generates a fixed tone and the client's
        // VAD counts it as speech.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        // 🚨 Phase 184: the commit below used to be clicked the moment its
        // button appeared. A commit that reaches the server before any audio
        // closes nothing: the server answers `idle`, the panel goes back to
        // listening, and the transcript this test waits for never comes - the
        // failure this test had in six phases, each time read as a slow
        // machine. Counting the recorder's binary frames on the voice socket
        // lets the test commit only once audio is really on its way.
        var audioFramesSent = 0;
        session.Page.WebSocket += (_, socket) => socket.FrameSent += (_, frame) =>
        {
            if (frame.Binary is { Length: > 0 })
            {
                Interlocked.Increment(ref audioFramesSent);
            }
        };

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

        await session.Page.GetByTestId("voice-mode").ClickAsync();
        await session.Page.GetByTestId("voice-toggle").ClickAsync();

        // 🚨 The bounds on this flow are deliberately generous and are NOT a
        // performance budget: they exist so a broken voice path reports instead of
        // hanging. This is the slowest chain in the suite — handshake, fake audio
        // device, commit, server round trip — and under a loaded full-solution run
        // the 30 s transcript wait ran out while the browser was still waiting for
        // CPU. A bound that only holds on an idle machine is a machine assumption.
        //
        // Once the handshake completes the server sends `ready` and the panel starts listening.
        await Expect(session.Page.GetByTestId("voice-meter")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // 🚨 Silence detection CANNOT be used here: Chromium's fake device
        // produces a continuous tone and never goes quiet. The manual commit
        // button is already a real need (noisy environment, push-to-talk) and
        // the test uses it.
        var commit = session.Page.GetByTestId("voice-commit");
        await Expect(commit).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await WaitUntil.TrueAsync(
            () => Volatile.Read(ref audioFramesSent) > 0,
            "the recorder to send its first audio chunk",
            TimeSpan.FromSeconds(60));
        await commit.ClickAsync();

        var transcript = session.Page.GetByTestId("voice-transcript");
        await Expect(transcript).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // The resolved text comes from the server; the fake provider returns a fixed response.
        await Expect(transcript.GetByText("where is my order").First).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // Audio is NOT stored by default; the recording notice must not appear.
        await Expect(session.Page.GetByTestId("voice-recording-notice")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Playground_stream_arrives_and_tool_card_fills_in()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        // The tool card is created when a real FunctionCallContent arrives.
        var card = session.Page.GetByTestId("tool-card").First;

        await Expect(card).ToBeVisibleAsync();
        await Expect(card.GetByText("get_order_status")).ToBeVisibleAsync();
        await Expect(card.GetByText("done")).ToBeVisibleAsync();

        // The model response streams in after the tool result.
        await Expect(session.Page.GetByText("Echo: where is ORD-7")).ToBeVisibleAsync();

        // Bridge to the run record: the first SSE frame carries the run id.
        await Expect(session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Playground_file_upload_shows_preview_and_run_continues()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var pngPath = Path.Combine(Path.GetTempPath(), $"tracon-e2e-{Guid.NewGuid():N}.png");

        // 8-byte PNG signature plus a little padding: a valid signature is
        // enough to pass the magic-byte check, a full PNG body is not needed.
        await File.WriteAllBytesAsync(
            pngPath,
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

        try
        {
            await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

            // BUG-S4-011: `img-src` did not carry blob:, so the thumbnail
            // preview (`useAttachmentPreview`) silently disappeared
            // (`img.naturalWidth=0`). The chip being visible does NOT catch
            // this; verify with the CSP violation event that the browser
            // really allowed the object URL to load.
            await session.Page.EvaluateAsync(
                """
                () => {
                    window.__cspViolations = [];
                    document.addEventListener('securitypolicyviolation', (event) => {
                        window.__cspViolations.push(event.violatedDirective);
                    });
                }
                """);

            await session.Page.GetByTestId("attachment-input").SetInputFilesAsync(pngPath);

            var chip = session.Page.GetByTestId("attachment-chip").First;
            await Expect(chip).ToBeVisibleAsync();
            await Expect(chip.GetByText(Path.GetFileName(pngPath))).ToBeVisibleAsync();

            var hadCspViolation = await session.Page.EvaluateAsync<bool>(
                """
                () => new Promise((resolve) => {
                    if (window.__cspViolations.length > 0) { resolve(true); return; }
                    setTimeout(() => resolve(window.__cspViolations.length > 0), 500);
                })
                """);
            hadCspViolation.ShouldBeFalse();

            await session.Page.GetByTestId("playground-input").FillAsync("describe this image");
            await session.Page.GetByTestId("playground-send").ClickAsync();

            // The attachment reference also stays in the turn as a small submission preview.
            await Expect(session.Page.GetByTestId("attachment-chip").First).ToBeVisibleAsync();
            await Expect(session.Page.GetByText("Echo: describe this image")).ToBeVisibleAsync();
        }
        finally
        {
            File.Delete(pngPath);
        }
    }

    [Fact]
    public async Task Playground_renders_a_parameter_form_and_blocks_send_until_required_values_are_filled()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        using (var created = await client.PostAsJsonAsync(
            $"{host.Prefix}/api/agents",
            new
            {
                name = "param-e2e",
                instructions = "Hello {{customer}}!",
                model = new { provider = ScriptedModels.ProviderName, model = ScriptedModels.Default },
                parameters = new[]
                {
                    new { name = "customer", kind = "Text", required = true },
                },
            }))
        {
            created.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/playground/param-e2e");

        await Expect(session.Page.GetByTestId("playground-parameters")).ToBeVisibleAsync();

        var sendButton = session.Page.GetByTestId("playground-send");

        await session.Page.GetByTestId("playground-input").FillAsync("hi there");

        // The required "customer" value is still empty: send stays blocked
        // even though the message itself is non-empty.
        await Expect(sendButton).ToBeDisabledAsync();

        await session.Page.GetByLabel("customer").FillAsync("Acme");

        await Expect(sendButton).ToBeEnabledAsync();

        await sendButton.ClickAsync();

        await Expect(session.Page.GetByText("Echo: hi there")).ToBeVisibleAsync();
    }
}
