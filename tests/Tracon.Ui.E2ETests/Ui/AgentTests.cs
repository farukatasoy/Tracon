using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The agents screens: the list, the editor, versions and the diff.
/// </summary>
public sealed class AgentTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Code_defined_agent_appears_in_list_and_cannot_be_edited()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents");

        await Expect(session.Page.GetByText("Support assistant")).ToBeVisibleAsync();

        await session.Page.GetByText("Support assistant").ClickAsync();

        // Write endpoints return 409 for a code-defined agent; the UI must
        // never show the edit button.
        await Expect(session.Page.GetByText("This agent is declared in code")).ToBeVisibleAsync();

        await Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Navigating_directly_to_code_agent_edit_URL_fills_form_with_name()
    {
        // BUG-S4-010: the list screen hides "Edit" (per task K1), but
        // navigating directly to the edit URL opened the form empty with a
        // read-only "Name" field — the Validate/Save buttons could never be
        // enabled, so the expected 409 flow for the case was unreachable.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/support/edit");

        var name = session.Page.GetByTestId("agent-name");

        await Expect(name).ToHaveValueAsync("support");
        (await name.IsEditableAsync()).ShouldBeFalse("The code agent's name must still not be editable.");

        await Expect(session.Page.GetByTestId("agent-validate"), "The Validate button stayed disabled even though the form is filled.").ToBeEnabledAsync();
        await Expect(session.Page.GetByTestId("agent-save"), "The Save button stayed disabled even though the form is filled.").ToBeEnabledAsync();
    }

    [Fact]
    public async Task Agent_created_from_UI_can_be_run_immediately()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("ui-agent");
        await session.Page.GetByTestId("agent-display-name").FillAsync("Console agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await session.Page.GetByTestId("agent-save").ClickAsync();

        // After saving, the detail screen opens and the new definition appears.
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Console agent" })).ToBeVisibleAsync();

        await session.Page.GotoAsync($"{host.UiAddress}/playground/ui-agent");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Echo: hello")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Fallback_list_is_saved_and_read_back()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);

        await session.Page.GetByTestId("agent-name").FillAsync("fallback-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await session.Page.GetByTestId("add-fallback").ClickAsync();
        await session.Page.GetByTestId("fallback-provider-0").SelectOptionAsync(ScriptedModels.ProviderName);
        await session.Page.GetByTestId("fallback-model-0").FillAsync(ScriptedModels.Support);

        await session.Page.GetByTestId("agent-save").ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "fallback-agent" })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();

        await Expect(session.Page.GetByTestId("fallback-provider-0")).ToHaveValueAsync(ScriptedModels.ProviderName);
        await Expect(session.Page.GetByTestId("fallback-model-0")).ToHaveValueAsync(ScriptedModels.Support);

        // Removing the only row returns to the empty-list hint, and a save
        // round trip persists the now-empty list (K1: no lingering fallback).
        await session.Page.GetByTestId("remove-fallback-0").ClickAsync();
        await Expect(session.Page.GetByTestId("fallback-provider-0")).ToHaveCountAsync(0);

        await session.Page.GetByTestId("agent-save").ClickAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "fallback-agent" })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();

        // The count below is also 0 before the form has loaded; wait for the
        // loaded form first, or the check proves nothing (Phase 184).
        await Expect(session.Page.GetByTestId("agent-name")).ToHaveValueAsync("fallback-agent");
        await Expect(session.Page.GetByTestId("fallback-provider-0")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Context_panel_reflects_selected_strategy_in_request_preview()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("context-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        // Conditional fields (trigger, etc.) stay hidden until a strategy is selected.
        await Expect(session.Page.GetByLabel("Trigger: message count")).ToHaveCountAsync(0);

        await session.Page.GetByLabel("Compaction strategy").SelectOptionAsync("SlidingWindow");
        await session.Page.GetByLabel("Trigger: message count").FillAsync("40");
        await session.Page.GetByLabel("Enable todo tracking").CheckAsync();

        var preview = await session.Page.Locator("pre").First.TextContentAsync();

        preview.ShouldNotBeNull();
        preview.ShouldContain("\"strategy\": \"SlidingWindow\"");
        preview.ShouldContain("\"triggerMessages\": 40");
        preview.ShouldContain("\"enableTodo\": true");
    }

    [Fact]
    public async Task Version_diff_compares_two_versions()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await CreateAgentWithTwoVersionsAsync(host, session, "diff-agent", "first instructions", "second instructions");

        await session.Page.GotoAsync($"{host.UiAddress}/agents/diff-agent");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "diff-agent" })).ToBeVisibleAsync();

        await session.Page.GetByTestId("version-checkbox-1").CheckAsync();
        await session.Page.GetByTestId("version-checkbox-2").CheckAsync();

        // When two versions are selected, both raw definitions are fetched and
        // a line-based diff is rendered; both instruction texts (one as "-",
        // one as "+") must appear.
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Comparing v1 → v2" })).ToBeVisibleAsync();

        // "first instructions"/"second instructions" also appear in the
        // Instructions panel and the raw Definition JSON; diff lines are
        // distinguished by the span.break-all class.
        await Expect(session.Page.Locator("span.break-all", new() { HasText = "first instructions" }).First).ToBeVisibleAsync();
        await Expect(session.Page.Locator("span.break-all", new() { HasText = "second instructions" }).First).ToBeVisibleAsync();
    }
}
