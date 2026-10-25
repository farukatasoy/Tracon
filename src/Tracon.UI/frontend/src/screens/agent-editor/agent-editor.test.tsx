import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, type FixtureRoute } from '../../test/api-fixtures';
import { fireEvent, renderScreen, screen, waitFor } from '../../test/render';
import { AgentEditorScreen } from '../agent-editor';

const providers = [
  { name: 'anthropic', displayName: 'Anthropic', status: 'Unknown', models: [{ name: 'claude', contextWindowTokens: null }] },
  { name: 'openai', displayName: 'OpenAI', status: 'Unknown', models: [] },
];

const fullDefinition = {
  name: 'support',
  displayName: 'Support',
  description: 'Answers support questions.',
  instructions: 'Be brief.',
  instructionsByCulture: {},
  model: { provider: 'anthropic', model: 'claude', temperature: null, maxOutputTokens: null, topP: null, reasoningEffort: null, responseFormat: null, fallbacks: [] },
  toolNames: [],
  skillNames: [],
  extensionNames: [],
  callableAgentNames: [],
  harness: null,
  compaction: null,
  memory: null,
  origin: 'Database',
  version: 1,
};

/** Empty catalogue lists for every section — enough for the screen to settle without a section-specific override. */
function baseOverrides() {
  return [
    fixture('GET', 'api/tools', []),
    fixture('GET', 'api/skills', []),
    fixture('GET', 'api/agent-extensions', []),
    fixture('GET', 'api/agents', []),
    fixture('GET', 'api/models', providers),
  ];
}

describe('AgentEditorScreen', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('loads an existing definition into the form', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('GET', 'api/agents/:name', { descriptor: { name: 'support', origin: 'Database', sourceName: 'database' }, definition: fullDefinition }),
    ]);

    renderScreen(<AgentEditorScreen name="support" />);

    const nameInput = await screen.findByTestId('agent-name');

    await waitFor(() => {
      expect((nameInput as HTMLInputElement).value).toBe('support');
    });

    expect((screen.getByTestId('agent-instructions') as HTMLTextAreaElement).value).toBe('Be brief.');
    expect((screen.getByTestId('agent-model') as HTMLInputElement).value).toBe('claude');
  });

  it('turning web search on clears the old off switch a stored definition carries', async () => {
    // Web search is opt-in since F-315; a definition saved before it may still
    // carry disableWebSearch: true, and the server refuses both at once.
    let sent: { harness?: { enableWebSearch?: boolean; disableWebSearch?: boolean } } | undefined;

    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('GET', 'api/agents/:name', {
        descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
        definition: { ...fullDefinition, harness: { disableWebSearch: true } },
      }),
      {
        method: 'POST',
        pattern: 'api/agents/validate',
        handler: (_params, _url, body) => {
          sent = body as typeof sent;

          return { valid: true, inconclusive: false, messages: [] };
        },
      },
    ]);

    const user = userEvent.setup();

    renderScreen(<AgentEditorScreen name="support" />);

    await user.click(await screen.findByLabelText('Enable web search'));
    await user.click(screen.getByTestId('agent-validate'));

    await waitFor(() => {
      expect(sent?.harness).toMatchObject({ enableWebSearch: true, disableWebSearch: false });
    });
  });

  // F-627: conversation memory is one box and one choice in the context section.
  describe('conversation memory', () => {
    type Sent = { memory?: Record<string, unknown> | null };

    function validateInto(capture: (body: Sent) => void) {
      return {
        method: 'POST' as const,
        pattern: 'api/agents/validate',
        handler: (_params: unknown, _url: unknown, body: unknown) => {
          capture(body as Sent);

          return { valid: true, inconclusive: false, messages: [] };
        },
      };
    }

    function loadSupport(memory: unknown, capture: (body: Sent) => void) {
      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
          definition: { ...fullDefinition, memory },
        }),
        validateInto(capture),
      ]);
    }

    it('shows the search choice only while the box is checked and sends the choice made', async () => {
      let sent: Sent | undefined;
      loadSupport(null, (body) => (sent = body));
      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByLabelText('Remember the user across sessions');
      expect(screen.queryByTestId('agent-conversation-memory-search')).toBeNull();

      await user.click(screen.getByLabelText('Remember the user across sessions'));
      await user.selectOptions(screen.getByTestId('agent-conversation-memory-search'), 'OnDemand');
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(sent?.memory).toMatchObject({ enableConversationMemory: true, conversationMemorySearch: 'OnDemand' });
      });
    });

    it('drops the search choice when the box is cleared, so no option is saved that changes nothing', async () => {
      let sent: Sent | undefined;
      loadSupport({ enableTodo: true, enableConversationMemory: true, conversationMemorySearch: 'OnDemand', conversationMemoryMaxResults: 4 }, (body) => (sent = body));
      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await user.click(await screen.findByLabelText('Remember the user across sessions'));
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(sent?.memory).toBeDefined();
      });

      expect(sent?.memory).toMatchObject({ enableTodo: true, enableConversationMemory: false });
      expect(sent?.memory).not.toHaveProperty('conversationMemorySearch');
      expect(sent?.memory).not.toHaveProperty('conversationMemoryMaxResults');
    });

    it('keeps the search limit an API client set when the box stays checked', async () => {
      let sent: Sent | undefined;
      loadSupport({ enableConversationMemory: true, conversationMemorySearch: 'BeforeEachTurn', conversationMemoryMaxResults: 4 }, (body) => (sent = body));
      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByLabelText('Remember the user across sessions');
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(sent?.memory).toMatchObject({ enableConversationMemory: true, conversationMemoryMaxResults: 4 });
      });
    });
  });

  // F-598: the request fields and the tool-call loop are edited in the model section.
  describe('request fields and tool-call loop', () => {
    type Sent = { model?: Record<string, unknown> };

    function validateInto(capture: (body: Sent) => void) {
      return {
        method: 'POST' as const,
        pattern: 'api/agents/validate',
        handler: (_params: unknown, _url: unknown, body: unknown) => {
          capture(body as Sent);

          return { valid: true, inconclusive: false, messages: [] };
        },
      };
    }

    it('keeps both groups closed for a definition that sets none and opens them for one that does', async () => {
      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/agents/:name', { descriptor: { name: 'support', origin: 'Database', sourceName: 'database' }, definition: fullDefinition }),
      ]);

      const first = renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByTestId('agent-name');

      expect((screen.getByTestId('sampling-settings') as HTMLDetailsElement).open).toBe(false);
      expect((screen.getByTestId('tool-loop-settings') as HTMLDetailsElement).open).toBe(false);

      first.unmount();
      restoreFetch();

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
          definition: {
            ...fullDefinition,
            model: { ...fullDefinition.model, seed: 42, toolLoop: { allowConcurrentToolCalls: true, maximumConsecutiveErrors: null, terminateOnUnknownCalls: null } },
          },
        }),
      ]);

      renderScreen(<AgentEditorScreen name="support" />);

      await waitFor(() => {
        expect((screen.getByTestId('sampling-settings') as HTMLDetailsElement).open).toBe(true);
        expect((screen.getByTestId('tool-loop-settings') as HTMLDetailsElement).open).toBe(true);
      });
      expect((screen.getByTestId('agent-seed') as HTMLInputElement).value).toBe('42');
      expect((screen.getByTestId('tool-loop-concurrent') as HTMLInputElement).checked).toBe(true);
    });

    it('sends what was typed and leaves the rest null', async () => {
      let sent: Sent | undefined;

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/agents/:name', { descriptor: { name: 'support', origin: 'Database', sourceName: 'database' }, definition: { ...fullDefinition, toolNames: ['lookup'] } }),
        fixture('GET', 'api/tools', [{ name: 'lookup', description: 'Looks it up.', effect: 'ReadOnly', requiresApproval: false, runsOnClient: false }]),
        validateInto((body) => {
          sent = body;
        }),
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByTestId('agent-name');
      await user.click(screen.getByText('Sampling and tool use'));
      await user.type(screen.getByTestId('agent-seed'), '7');
      await user.type(screen.getByTestId('agent-top-k'), '40');
      await user.type(screen.getByTestId('agent-stop-sequences'), 'END{Enter}STOP');
      await user.selectOptions(screen.getByTestId('agent-tool-mode'), 'required:lookup');
      await user.selectOptions(screen.getByTestId('agent-allow-multiple-tool-calls'), 'false');
      await user.selectOptions(screen.getByTestId('agent-reasoning-output'), 'Summary');
      await user.click(screen.getByText('Tool-call loop'));
      await user.click(screen.getByTestId('tool-loop-concurrent'));
      await user.type(screen.getByTestId('tool-loop-max-errors'), '0');
      await user.selectOptions(screen.getByTestId('tool-loop-terminate-unknown'), 'true');
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(sent?.model).toMatchObject({
          seed: 7,
          topK: 40,
          stopSequences: ['END', 'STOP'],
          toolMode: 'required:lookup',
          allowMultipleToolCalls: false,
          reasoningOutput: 'Summary',
          frequencyPenalty: null,
          presencePenalty: null,
          toolLoop: { allowConcurrentToolCalls: true, maximumConsecutiveErrors: 0, terminateOnUnknownCalls: true },
        });
      });
    });

    it('marks a hosted tool and does not offer it as a required tool', async () => {
      // The first matching route wins, so the screen's own fixtures come before the empty defaults.
      restoreFetch = installApiMock([
        fixture('GET', 'api/agents/:name', { descriptor: { name: 'support', origin: 'Database', sourceName: 'database' }, definition: { ...fullDefinition, toolNames: ['lookup', 'search'] } }),
        fixture('GET', 'api/tools', [
          { name: 'lookup', description: 'Looks it up.', effect: 'Read', requiresApproval: false, runsOnClient: false },
          { name: 'search', description: 'Searches the web.', effect: 'External', requiresApproval: false, runsOnClient: false, hostedKind: 'WebSearch' },
        ]),
        ...baseOverrides(),
      ]);

      renderScreen(<AgentEditorScreen name="support" />);

      expect(await screen.findByText('hosted web search')).toBeTruthy();
      expect(screen.getByText(/A hosted tool runs on the model provider/)).toBeTruthy();

      await screen.findByTestId('agent-name');
      const options = Array.from((screen.getByTestId('agent-tool-mode') as HTMLSelectElement).options).map((option) => option.value);

      expect(options).toContain('required:lookup');
      expect(options).not.toContain('required:search');
    });

    it('keeps a tool mode that names a tool the definition no longer lists', async () => {
      let sent: Sent | undefined;

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
          definition: { ...fullDefinition, model: { ...fullDefinition.model, toolMode: 'required:retired' } },
        }),
        validateInto((body) => {
          sent = body;
        }),
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByTestId('agent-name');

      expect((screen.getByTestId('agent-tool-mode') as HTMLSelectElement).value).toBe('required:retired');

      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(sent?.model?.toolMode).toBe('required:retired');
      });
    });
  });

  it('defaults the provider for a new agent once the catalogue loads (HATA-S4-009)', async () => {
    restoreFetch = installApiMock(baseOverrides());

    renderScreen(<AgentEditorScreen />);

    await waitFor(() => {
      const providerSelect = screen.getByLabelText(/^Provider/) as HTMLSelectElement;

      expect(providerSelect.value).toBe('anthropic');
    });
  });

  it('shows the failure instead of an endless skeleton when the definition cannot be loaded', async () => {
    // A definition that does not resolve — another tenant's agent, a deleted
    // one, a store that is down. `ready` only ever turns true on SUCCESS, so
    // without an error branch this screen sat on `aria-busy` for ever.
    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('GET', 'api/agents/support', { title: 'Agent not found', status: 404 }, 404),
    ]);

    renderScreen(<AgentEditorScreen name="support" />);

    const alert = await screen.findByRole('alert');

    expect(alert.textContent).toContain('Agent not found');
    expect(screen.getByTestId('error-retry')).toBeDefined();
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('flags invalid JSON in the response schema and disables Validate/Save', async () => {
    restoreFetch = installApiMock(baseOverrides());

    const user = userEvent.setup();

    renderScreen(<AgentEditorScreen />);

    await user.type(await screen.findByTestId('agent-name'), 'demo');
    await user.type(screen.getByTestId('agent-model'), 'claude');
    await user.selectOptions(screen.getByLabelText(/^Provider/), 'anthropic');
    await user.selectOptions(screen.getByTestId('agent-response-format'), 'JsonSchema');

    const schema = screen.getByTestId('agent-response-schema');

    // `userEvent.type` parses `{`/`}` as special-key syntax; a raw DOM change
    // event is the right tool for pasting literal, syntactically invalid text.
    fireEvent.change(schema, { target: { value: '{ not json' } });

    expect(screen.getByRole('alert')).toHaveProperty('textContent', 'Not valid JSON.');
    expect((screen.getByTestId('agent-validate') as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByTestId('agent-save') as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows the validation report after Validate succeeds', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('POST', 'api/agents/validate', { valid: true, inconclusive: false, messages: [] }),
    ]);

    const user = userEvent.setup();

    renderScreen(<AgentEditorScreen />);

    await user.type(await screen.findByTestId('agent-name'), 'demo');
    await user.type(screen.getByTestId('agent-model'), 'claude');
    await user.selectOptions(screen.getByLabelText(/^Provider/), 'anthropic');

    await user.click(screen.getByTestId('agent-validate'));

    expect(await screen.findByText('Valid')).toBeTruthy();
  });

  describe('callable agents section (Phase 228)', () => {
    type Sent = { subAgents?: Record<string, unknown> | null };

    function validateInto(capture: (body: Sent) => void) {
      return {
        method: 'POST' as const,
        pattern: 'api/agents/validate',
        handler: (_params: unknown, _url: unknown, body: unknown) => {
          capture(body as Sent);

          return { valid: true, inconclusive: false, messages: [] };
        },
      };
    }

    it('shows background tasks for a definition that sets no mode and sends none until it is changed', async () => {
      let sent: Sent | undefined;

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/agents/:name', { descriptor: { name: 'support', origin: 'Database', sourceName: 'database' }, definition: fullDefinition }),
        validateInto((body) => {
          sent = body;
        }),
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByTestId('agent-name');

      expect((screen.getByTestId('agent-call-mode') as HTMLSelectElement).value).toBe('Background');

      await user.click(screen.getByTestId('agent-validate'));
      await waitFor(() => expect(sent).toBeDefined());

      expect(sent?.subAgents ?? null).toBeNull();
    });

    it('sends the mode that was picked and keeps the wait limits the stored definition carries', async () => {
      let sent: Sent | undefined;

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
          definition: { ...fullDefinition, subAgents: { waitTimeout: '00:00:30' } },
        }),
        validateInto((body) => {
          sent = body;
        }),
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByTestId('agent-name');
      await user.selectOptions(screen.getByTestId('agent-call-mode'), 'Tool');
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => expect(sent?.subAgents).toEqual({ waitTimeout: '00:00:30', callMode: 'Tool' }));
    });

    it('loads the stored tool mode into the select', async () => {
      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
          definition: { ...fullDefinition, subAgents: { callMode: 'Tool' } },
        }),
      ]);

      renderScreen(<AgentEditorScreen name="support" />);

      await waitFor(() => {
        expect((screen.getByTestId('agent-call-mode') as HTMLSelectElement).value).toBe('Tool');
      });
    });
  });

  describe('skill read approval (Phase 231)', () => {
    function withSkills(stored: { disableReadApproval?: boolean } | null) {
      return [
        fixture('GET', 'api/tools', []),
        fixture('GET', 'api/skills', [{ name: 'house-style', description: 'How replies are worded.', enabled: true }]),
        fixture('GET', 'api/agent-extensions', []),
        fixture('GET', 'api/agents', []),
        fixture('GET', 'api/models', providers),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
          definition: { ...fullDefinition, skillNames: ['house-style'], skills: stored },
        }),
      ];
    }

    function captureValidate(): { route: FixtureRoute; sent: () => { skills?: { disableReadApproval?: boolean } | null } } {
      let body: { skills?: { disableReadApproval?: boolean } | null } = {};

      return {
        route: {
          method: 'POST',
          pattern: 'api/agents/validate',
          handler: (_params, _url, sent) => {
            body = sent as typeof body;

            return { valid: true, inconclusive: false, messages: [] };
          },
        },
        sent: () => body,
      };
    }

    it('shows the box checked for a definition that turned the approval off', async () => {
      restoreFetch = installApiMock(withSkills({ disableReadApproval: true }));

      renderScreen(<AgentEditorScreen name="support" />);

      const box = await screen.findByLabelText(/Read skills without approval/);

      await waitFor(() => expect((box as HTMLInputElement).checked).toBe(true));
    });

    it('shows the box checked for a code-defined agent, which has no stored definition', async () => {
      restoreFetch = installApiMock([
        fixture('GET', 'api/tools', []),
        fixture('GET', 'api/skills', [{ name: 'house-style', description: 'How replies are worded.', enabled: true }]),
        fixture('GET', 'api/agent-extensions', []),
        fixture('GET', 'api/agents', []),
        fixture('GET', 'api/models', providers),
        fixture('GET', 'api/agents/:name', {
          descriptor: {
            name: 'support',
            origin: 'Code',
            sourceName: 'code',
            model: { provider: 'anthropic', model: 'claude' },
            toolNames: [],
            skillNames: ['house-style'],
            skills: { disableReadApproval: true },
            extensionNames: [],
            callableAgentNames: [],
          },
          definition: null,
        }),
      ]);

      renderScreen(<AgentEditorScreen name="support" />);

      const box = await screen.findByLabelText(/Read skills without approval/);

      await waitFor(() => expect((box as HTMLInputElement).checked).toBe(true));
    });

    it('writes the setting when the box is checked and none when it is not', async () => {
      const capture = captureValidate();

      restoreFetch = installApiMock([...withSkills(null), capture.route]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      const box = await screen.findByLabelText(/Read skills without approval/);

      expect((box as HTMLInputElement).checked).toBe(false);

      await user.click(screen.getByTestId('agent-validate'));
      await waitFor(() => expect(capture.sent().skills).toBeNull());

      await user.click(box);
      await user.click(screen.getByTestId('agent-validate'));
      await waitFor(() => expect(capture.sent().skills).toEqual({ disableReadApproval: true }));
    });
  });

  describe('session workspace (Phase 234)', () => {
    type Sent = { workspace?: { enabled?: boolean; readOnly?: boolean; disableReadApproval?: boolean } | null };

    function withWorkspace(stored: NonNullable<Sent['workspace']> | null) {
      return [
        fixture('GET', 'api/tools', []),
        fixture('GET', 'api/skills', []),
        fixture('GET', 'api/agent-extensions', []),
        fixture('GET', 'api/agents', []),
        fixture('GET', 'api/models', providers),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
          definition: { ...fullDefinition, workspace: stored },
        }),
      ];
    }

    function captureValidate(): { route: FixtureRoute; sent: () => Sent } {
      let body: Sent = {};

      return {
        route: {
          method: 'POST',
          pattern: 'api/agents/validate',
          handler: (_params, _url, sent) => {
            body = sent as Sent;

            return { valid: true, inconclusive: false, messages: [] };
          },
        },
        sent: () => body,
      };
    }

    it('shows the three boxes for a definition that has a read-only workspace with no read approval', async () => {
      restoreFetch = installApiMock(withWorkspace({ enabled: true, readOnly: true, disableReadApproval: true }));

      renderScreen(<AgentEditorScreen name="support" />);

      await waitFor(() => expect((screen.getByTestId('agent-workspace-enabled') as HTMLInputElement).checked).toBe(true));
      expect((screen.getByTestId('agent-workspace-read-only') as HTMLInputElement).checked).toBe(true);
      expect((screen.getByTestId('agent-workspace-disable-read-approval') as HTMLInputElement).checked).toBe(true);
    });

    it('shows the workspace of a code-defined agent, which has no stored definition', async () => {
      restoreFetch = installApiMock([
        fixture('GET', 'api/tools', []),
        fixture('GET', 'api/skills', []),
        fixture('GET', 'api/agent-extensions', []),
        fixture('GET', 'api/agents', []),
        fixture('GET', 'api/models', providers),
        fixture('GET', 'api/agents/:name', {
          descriptor: {
            name: 'support',
            origin: 'Code',
            sourceName: 'code',
            model: { provider: 'anthropic', model: 'claude' },
            toolNames: [],
            skillNames: [],
            workspace: { enabled: true, disableReadApproval: true },
            extensionNames: [],
            callableAgentNames: [],
          },
          definition: null,
        }),
      ]);

      renderScreen(<AgentEditorScreen name="support" />);

      await waitFor(() => expect((screen.getByTestId('agent-workspace-enabled') as HTMLInputElement).checked).toBe(true));
      expect((screen.getByTestId('agent-workspace-disable-read-approval') as HTMLInputElement).checked).toBe(true);
    });

    it('hides the two options while the workspace is off', async () => {
      restoreFetch = installApiMock(withWorkspace(null));

      renderScreen(<AgentEditorScreen name="support" />);

      const enabled = await screen.findByTestId('agent-workspace-enabled');

      expect((enabled as HTMLInputElement).checked).toBe(false);
      expect(screen.queryByTestId('agent-workspace-read-only')).toBeNull();
      expect(screen.queryByTestId('agent-workspace-disable-read-approval')).toBeNull();
    });

    it('writes the settings that are on, and none once the workspace is turned off again', async () => {
      const capture = captureValidate();

      restoreFetch = installApiMock([...withWorkspace(null), capture.route]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await user.click(await screen.findByTestId('agent-workspace-enabled'));
      await user.click(screen.getByTestId('agent-workspace-read-only'));
      await user.click(screen.getByTestId('agent-validate'));
      await waitFor(() => expect(capture.sent().workspace).toEqual({ enabled: true, readOnly: true }));

      await user.click(screen.getByTestId('agent-workspace-disable-read-approval'));
      await user.click(screen.getByTestId('agent-validate'));
      await waitFor(() => expect(capture.sent().workspace).toEqual({ enabled: true, readOnly: true, disableReadApproval: true }));

      // Turning the workspace off drops the two options with it: they would change nothing.
      await user.click(screen.getByTestId('agent-workspace-enabled'));
      await user.click(screen.getByTestId('agent-validate'));
      await waitFor(() => expect(capture.sent().workspace).toBeNull());
    });
  });

  describe('extensions section (Phase 222)', () => {
    const registered = [
      { name: 'audit', description: 'Records every model call.' },
      { name: 'policy', description: null },
    ];

    function withExtensions(stored: string[]) {
      return [
        fixture('GET', 'api/tools', []),
        fixture('GET', 'api/skills', []),
        fixture('GET', 'api/agent-extensions', registered),
        fixture('GET', 'api/agents', []),
        fixture('GET', 'api/models', providers),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database', extensionNames: stored },
          definition: { ...fullDefinition, extensionNames: stored },
        }),
      ];
    }

    function captureValidate(): { route: FixtureRoute; sent: () => { extensionNames?: string[] } } {
      let body: { extensionNames?: string[] } = {};

      return {
        route: {
          method: 'POST',
          pattern: 'api/agents/validate',
          handler: (_params, _url, sent) => {
            body = sent as typeof body;

            return { valid: true, inconclusive: false, messages: [] };
          },
        },
        sent: () => body,
      };
    }

    it('lists what the host registered, with the description when there is one', async () => {
      restoreFetch = installApiMock(withExtensions([]));

      renderScreen(<AgentEditorScreen name="support" />);

      expect(await screen.findByText('audit')).toBeTruthy();
      expect(screen.getByText('Records every model call.')).toBeTruthy();
      expect(screen.getByText('policy')).toBeTruthy();
      expect(screen.queryAllByRole('checkbox', { checked: true })).toHaveLength(0);
    });

    it('sends the checked names in the order they were checked', async () => {
      const capture = captureValidate();

      restoreFetch = installApiMock([...withExtensions([]), capture.route]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await user.click(await screen.findByLabelText('policy'));
      await user.click(screen.getByLabelText(/^audit/));
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => expect(capture.sent().extensionNames).toEqual(['policy', 'audit']));
    });

    it('shows a name the host no longer registers and lets the user remove it', async () => {
      const capture = captureValidate();

      restoreFetch = installApiMock([...withExtensions(['audit', 'retired']), capture.route]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      expect(await screen.findByText('Not registered')).toBeTruthy();

      await user.click(screen.getByLabelText(/^retired/));
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => expect(capture.sent().extensionNames).toEqual(['audit']));
      expect(screen.queryByText('Not registered')).toBeNull();
    });

    it('keeps the names of a code-defined agent, which has no stored definition', async () => {
      const capture = captureValidate();

      restoreFetch = installApiMock([
        fixture('GET', 'api/tools', []),
        fixture('GET', 'api/skills', []),
        fixture('GET', 'api/agent-extensions', registered),
        fixture('GET', 'api/agents', []),
        fixture('GET', 'api/models', providers),
        fixture('GET', 'api/agents/:name', {
          descriptor: {
            name: 'support',
            origin: 'Code',
            sourceName: 'code',
            model: { provider: 'anthropic', model: 'claude' },
            toolNames: [],
            skillNames: [],
            extensionNames: ['audit'],
            callableAgentNames: [],
          },
          definition: null,
        }),
        capture.route,
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await waitFor(() => expect((screen.getByLabelText(/^audit/) as HTMLInputElement).checked).toBe(true));
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => expect(capture.sent().extensionNames).toEqual(['audit']));
    });

    it('says how to add one when nothing is registered', async () => {
      restoreFetch = installApiMock(baseOverrides());

      renderScreen(<AgentEditorScreen />);

      expect(await screen.findByText('AddAgentExtension(...)')).toBeTruthy();
    });
  });

  describe('loop section (Phase 201)', () => {
    const catalog = {
      kinds: [
        { kind: 'completionMarker', builtIn: true, description: 'x' },
        { kind: 'todoCompletion', builtIn: true, description: 'x' },
        { kind: 'aiJudge', builtIn: true, description: 'x' },
        { kind: 'backgroundTaskCompletion', builtIn: true, description: 'x' },
        { kind: 'agentReview', builtIn: true, description: 'x' },
        { kind: 'reviewed', builtIn: false, description: 'The reviewer agent approves the draft.' },
      ],
      judgeModel: null,
    };

    function withLoop(loop: unknown) {
      return fixture('GET', 'api/agents/:name', {
        descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
        definition: { ...fullDefinition, harness: { loop } },
      });
    }

    function captureValidate(): { route: FixtureRoute; sent: () => unknown } {
      let body: unknown;

      return {
        route: {
          method: 'POST',
          pattern: 'api/agents/validate',
          handler: (_params, _url, sent) => {
            body = sent;

            return { valid: true, inconclusive: false, messages: [] };
          },
        },
        sent: () => body,
      };
    }

    it('keeps a kind nobody lists, also when the kind list fails to load', async () => {
      const validate = captureValidate();

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', { title: 'Boom', status: 500 }, 500),
        withLoop({ criteria: [{ kind: 'hasCitations', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] }] }),
        validate.route,
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      const kind = (await screen.findByTestId('loop-criterion-kind-0')) as HTMLSelectElement;

      expect(kind.value).toBe('hasCitations');
      expect(await screen.findByText(/could not be loaded/)).toBeTruthy();

      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(validate.sent()).toMatchObject({ harness: { loop: { criteria: [{ kind: 'hasCitations' }] } } });
      });
    });

    it('marks a kind the host does not list as not registered and describes a code kind', async () => {
      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', catalog),
        withLoop({
          criteria: [
            { kind: 'reviewed', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] },
            { kind: 'gone', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] },
          ],
        }),
      ]);

      renderScreen(<AgentEditorScreen name="support" />);

      expect((await screen.findByTestId('loop-criterion-description-0')).textContent).toContain(
        'The reviewer agent approves the draft.',
      );
      expect(await screen.findByText('Not registered')).toBeTruthy();
      expect((screen.getByTestId('loop-criterion-kind-1') as HTMLSelectElement).value).toBe('gone');
    });

    it('a kind change clears what the new kind does not read; a move carries the fields with the criterion', async () => {
      const validate = captureValidate();

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', catalog),
        withLoop({
          criteria: [
            { kind: 'completionMarker', marker: 'DONE', judgeCriteria: [], judgeInstructions: null, modes: [] },
            { kind: 'aiJudge', marker: null, judgeCriteria: ['Complete?'], judgeInstructions: 'Be strict.', modes: [] },
          ],
          maxIterations: 4,
          onExhausted: 'FailRun',
        }),
        validate.route,
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await user.click(await screen.findByTestId('loop-criterion-down-0'));
      await user.selectOptions(screen.getByTestId('loop-criterion-kind-0'), 'reviewed');
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(validate.sent()).toMatchObject({
          harness: {
            loop: {
              criteria: [
                { kind: 'reviewed', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] },
                { kind: 'completionMarker', marker: 'DONE' },
              ],
              maxIterations: 4,
              onExhausted: 'FailRun',
            },
          },
        });
      });
    });

    it('shows a kind spelled differently from the list as written, and keeps it on save', async () => {
      // The server matches kinds case-insensitively. Built from the listed names
      // alone, the select showed its FIRST option for "aijudge" — with the judge
      // fields under it — and choosing completionMarker fired no change at all.
      const validate = captureValidate();

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', catalog),
        withLoop({
          criteria: [
            { kind: 'aijudge', marker: null, judgeCriteria: ['ok?'], judgeInstructions: null, modes: [] },
            { kind: 'Reviewed', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] },
          ],
        }),
        validate.route,
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      const first = (await screen.findByTestId('loop-criterion-kind-0')) as HTMLSelectElement;

      expect(first.value).toBe('aijudge');
      expect((screen.getByTestId('loop-criterion-kind-1') as HTMLSelectElement).value).toBe('Reviewed');
      expect(screen.getByTestId('loop-criterion-judge-criteria-0')).toBeTruthy();
      expect(screen.getByTestId('loop-criterion-description-1').textContent).toContain('The reviewer agent approves the draft.');
      expect(screen.queryByText('Not registered')).toBeNull();

      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(validate.sent()).toMatchObject({
          harness: { loop: { criteria: [{ kind: 'aijudge', judgeCriteria: ['ok?'] }, { kind: 'Reviewed' }] } },
        });
      });

      await user.selectOptions(first, 'completionMarker');

      expect(first.value).toBe('completionMarker');
      expect(screen.getByTestId('loop-criterion-marker-0')).toBeTruthy();
    });

    it('says nothing about registration while the kind list is unavailable', async () => {
      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', { title: 'Boom', status: 500 }, 500),
        withLoop({ criteria: [{ kind: 'reviewed', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] }] }),
      ]);

      renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByText(/could not be loaded/);

      expect(screen.queryByText(/This host does not list this kind/)).toBeNull();
      expect(screen.queryByText('Not registered')).toBeNull();
    });

    // Phase 242 (F-411): the built-in agentReview kind names a reviewer agent of the catalog.
    describe('agentReview', () => {
      const agents = [
        { name: 'support', description: 'Answers support questions.', origin: 'Database' },
        { name: 'draft-reviewer', description: 'Reviews drafts.', origin: 'Database' },
        { name: 'style-reviewer', description: null, origin: 'Code' },
      ];

      function reviewing(extra: Record<string, unknown> = {}) {
        return withLoop({
          criteria: [
            {
              kind: 'agentReview',
              marker: null,
              judgeCriteria: [],
              judgeInstructions: null,
              modes: [],
              reviewerAgent: null,
              reviewerAgentVersion: null,
              ...extra,
            },
          ],
        });
      }

      it('offers the catalog agents except the agent being edited, and describes the one chosen', async () => {
        const validate = captureValidate();

        restoreFetch = installApiMock([
          ...baseOverrides().filter((route) => route.pattern !== 'api/agents'),
          fixture('GET', 'api/agents', agents),
          fixture('GET', 'api/loop-criteria', catalog),
          reviewing(),
          validate.route,
        ]);

        const user = userEvent.setup();

        renderScreen(<AgentEditorScreen name="support" />);

        const reviewer = (await screen.findByTestId('loop-criterion-reviewer-0')) as HTMLSelectElement;

        await waitFor(() => expect(reviewer.options.length).toBe(3));
        expect(Array.from(reviewer.options).map((option) => option.value)).toEqual(['', 'draft-reviewer', 'style-reviewer']);

        await user.selectOptions(reviewer, 'draft-reviewer');
        await user.type(screen.getByTestId('loop-criterion-reviewer-version-0'), '3');

        expect((await screen.findByTestId('loop-criterion-reviewer-description-0')).textContent).toBe('Reviews drafts.');

        await user.click(screen.getByTestId('agent-validate'));

        await waitFor(() => {
          expect(validate.sent()).toMatchObject({
            harness: { loop: { criteria: [{ kind: 'agentReview', reviewerAgent: 'draft-reviewer', reviewerAgentVersion: 3 }] } },
          });
        });
      });

      it('clearing the version sends null, not zero', async () => {
        const validate = captureValidate();

        restoreFetch = installApiMock([
          ...baseOverrides().filter((route) => route.pattern !== 'api/agents'),
          fixture('GET', 'api/agents', agents),
          fixture('GET', 'api/loop-criteria', catalog),
          reviewing({ reviewerAgent: 'draft-reviewer', reviewerAgentVersion: 2 }),
          validate.route,
        ]);

        const user = userEvent.setup();

        renderScreen(<AgentEditorScreen name="support" />);

        const version = (await screen.findByTestId('loop-criterion-reviewer-version-0')) as HTMLInputElement;

        expect(version.value).toBe('2');

        await user.clear(version);
        await user.click(screen.getByTestId('agent-validate'));

        await waitFor(() => {
          expect(validate.sent()).toMatchObject({
            harness: { loop: { criteria: [{ kind: 'agentReview', reviewerAgent: 'draft-reviewer', reviewerAgentVersion: null }] } },
          });
        });
      });

      it('keeps a reviewer the list does not carry, and says so once the list has loaded', async () => {
        const validate = captureValidate();

        restoreFetch = installApiMock([
          ...baseOverrides().filter((route) => route.pattern !== 'api/agents'),
          fixture('GET', 'api/agents', agents),
          fixture('GET', 'api/loop-criteria', catalog),
          reviewing({ reviewerAgent: 'other-tenant-reviewer' }),
          validate.route,
        ]);

        const user = userEvent.setup();

        renderScreen(<AgentEditorScreen name="support" />);

        const reviewer = (await screen.findByTestId('loop-criterion-reviewer-0')) as HTMLSelectElement;

        expect(reviewer.value).toBe('other-tenant-reviewer');
        expect(await screen.findByTestId('loop-criterion-reviewer-unlisted-0')).toBeTruthy();

        await user.click(screen.getByTestId('agent-validate'));

        await waitFor(() => {
          expect(validate.sent()).toMatchObject({
            harness: { loop: { criteria: [{ kind: 'agentReview', reviewerAgent: 'other-tenant-reviewer' }] } },
          });
        });
      });

      it('keeps the reviewer when the agent list fails to load', async () => {
        restoreFetch = installApiMock([
          ...baseOverrides().filter((route) => route.pattern !== 'api/agents'),
          fixture('GET', 'api/agents', { title: 'Boom', status: 500 }, 500),
          fixture('GET', 'api/loop-criteria', catalog),
          reviewing({ reviewerAgent: 'draft-reviewer' }),
        ]);

        renderScreen(<AgentEditorScreen name="support" />);

        const reviewer = (await screen.findByTestId('loop-criterion-reviewer-0')) as HTMLSelectElement;

        expect(reviewer.value).toBe('draft-reviewer');
        // Nothing is said about the agent not existing while the list is unavailable.
        expect(screen.queryByTestId('loop-criterion-reviewer-unlisted-0')).toBeNull();
      });

      it('a kind change clears the reviewer fields, and choosing agentReview again starts empty', async () => {
        const validate = captureValidate();

        restoreFetch = installApiMock([
          ...baseOverrides().filter((route) => route.pattern !== 'api/agents'),
          fixture('GET', 'api/agents', agents),
          fixture('GET', 'api/loop-criteria', catalog),
          reviewing({ reviewerAgent: 'draft-reviewer', reviewerAgentVersion: 2 }),
          validate.route,
        ]);

        const user = userEvent.setup();

        renderScreen(<AgentEditorScreen name="support" />);

        await user.selectOptions(await screen.findByTestId('loop-criterion-kind-0'), 'aiJudge');
        expect(screen.queryByTestId('loop-criterion-reviewer-0')).toBeNull();

        await user.selectOptions(screen.getByTestId('loop-criterion-kind-0'), 'agentReview');
        expect((screen.getByTestId('loop-criterion-reviewer-0') as HTMLSelectElement).value).toBe('');
        expect((screen.getByTestId('loop-criterion-reviewer-version-0') as HTMLInputElement).value).toBe('');

        await user.selectOptions(screen.getByTestId('loop-criterion-kind-0'), 'completionMarker');
        await user.click(screen.getByTestId('agent-validate'));

        await waitFor(() => {
          expect(validate.sent()).toMatchObject({
            harness: { loop: { criteria: [{ kind: 'completionMarker', reviewerAgent: null, reviewerAgentVersion: null }] } },
          });
        });
      });

      it('loading and saving keep the reviewer fields of a kind that does not read them', async () => {
        // K-918: only a kind CHANGE clears. A definition written over the API with a reviewer on another kind survives a save.
        const validate = captureValidate();

        restoreFetch = installApiMock([
          ...baseOverrides(),
          fixture('GET', 'api/loop-criteria', catalog),
          withLoop({
            criteria: [
              {
                kind: 'completionMarker',
                marker: 'DONE',
                judgeCriteria: [],
                judgeInstructions: null,
                modes: [],
                reviewerAgent: 'draft-reviewer',
                reviewerAgentVersion: 4,
              },
            ],
          }),
          validate.route,
        ]);

        const user = userEvent.setup();

        renderScreen(<AgentEditorScreen name="support" />);

        await user.click(await screen.findByTestId('agent-validate'));

        await waitFor(() => {
          expect(validate.sent()).toMatchObject({
            harness: { loop: { criteria: [{ kind: 'completionMarker', reviewerAgent: 'draft-reviewer', reviewerAgentVersion: 4 }] } },
          });
        });
      });
    });

    it('drops blank judge criteria lines once the field is left', async () => {
      const validate = captureValidate();

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', catalog),
        withLoop({ criteria: [{ kind: 'aiJudge', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] }] }),
        validate.route,
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      const field = await screen.findByTestId('loop-criterion-judge-criteria-0');

      await user.click(field);
      fireEvent.change(field, { target: { value: 'Complete?\n\n  \nCites a source.\n' } });
      // Validate takes the focus, and the field's blur runs first.
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(validate.sent()).toMatchObject({
          harness: { loop: { criteria: [{ kind: 'aiJudge', judgeCriteria: ['Complete?', 'Cites a source.'] }] } },
        });
      });
    });

    it('says the judge model is not configured on an aiJudge criterion', async () => {
      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', catalog),
        withLoop({ criteria: [{ kind: 'aiJudge', marker: null, judgeCriteria: ['Complete?'], judgeInstructions: null, modes: [] }] }),
      ]);

      renderScreen(<AgentEditorScreen name="support" />);

      await waitFor(() => {
        expect(screen.getByTestId('loop-criterion-judge-0').textContent).toContain('No judge model is configured');
      });
    });

    it('turning the harness off says the loop is not saved, and the request carries none', async () => {
      const validate = captureValidate();

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', catalog),
        withLoop({ criteria: [{ kind: 'completionMarker', marker: 'DONE', judgeCriteria: [], judgeInstructions: null, modes: [] }] }),
        validate.route,
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await screen.findByTestId('loop-section');
      await user.click(screen.getByLabelText('Enable harness features'));

      expect(screen.getByTestId('loop-dropped')).toBeTruthy();
      expect(screen.queryByTestId('loop-section')).toBeNull();

      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(validate.sent()).toMatchObject({ harness: null });
      });
    });

    it('a loop turned on from the console sends one criterion and the default outcomes', async () => {
      const validate = captureValidate();

      restoreFetch = installApiMock([
        ...baseOverrides(),
        fixture('GET', 'api/loop-criteria', catalog),
        fixture('GET', 'api/agents/:name', {
          descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
          definition: { ...fullDefinition, harness: {} },
        }),
        validate.route,
      ]);

      const user = userEvent.setup();

      renderScreen(<AgentEditorScreen name="support" />);

      await user.click(await screen.findByTestId('loop-enabled'));
      await user.type(screen.getByTestId('loop-criterion-marker-0'), 'ALL DONE');
      await user.type(screen.getByTestId('loop-max-iterations'), '3');
      await user.click(screen.getByTestId('agent-validate'));

      await waitFor(() => {
        expect(validate.sent()).toMatchObject({
          harness: {
            loop: {
              criteria: [{ kind: 'completionMarker', marker: 'ALL DONE' }],
              maxIterations: 3,
              freshContextPerIteration: false,
              onExhausted: 'ReturnLastResponse',
              onCriterionError: 'ReturnLastResponse',
            },
          },
        });
      });
    });
  });

  it('shows an error note when Save fails', async () => {
    restoreFetch = installApiMock([
      ...baseOverrides(),
      fixture('POST', 'api/agents', { title: 'Name already exists', detail: 'An agent named "demo" already exists.' }, 409),
    ]);

    const user = userEvent.setup();

    renderScreen(<AgentEditorScreen />);

    await user.type(await screen.findByTestId('agent-name'), 'demo');
    await user.type(screen.getByTestId('agent-model'), 'claude');
    await user.selectOptions(screen.getByLabelText(/^Provider/), 'anthropic');

    await user.click(screen.getByTestId('agent-save'));

    const alert = await screen.findByRole('alert');

    expect(alert.textContent).toContain('Name already exists');
  });
});
