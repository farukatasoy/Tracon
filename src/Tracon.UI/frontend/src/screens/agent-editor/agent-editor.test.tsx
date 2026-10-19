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
