import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, type FixtureRoute } from '../test/api-fixtures';
import { fireEvent, renderScreen, screen, waitFor, within } from '../test/render';
import { WorkflowEditorScreen } from './workflow-editor';

const stamp = '2026-10-10T09:00:00Z';

/** The shape the server answers with for a stored workflow; `overrides` is what a case changes. */
function stored(overrides: Record<string, unknown>): Record<string, unknown> {
  return {
    name: 'chain',
    displayName: null,
    description: 'Summarizes, then counts words.',
    kind: 'Sequential',
    agentNames: [],
    nodes: [],
    parameters: [],
    managerAgentName: null,
    maxIterations: null,
    handoffInstructions: null,
    requirePlanApproval: false,
    tenantId: 'default',
    version: 1,
    updatedAt: stamp,
    ...overrides,
  };
}

const mixed = stored({
  nodes: [
    { name: 'summarizer', kind: 'Agent', settings: null },
    { name: 'word-count', kind: 'Function', settings: { language: 'en', limit: 3 } },
  ],
});

const agentsOnly = stored({ agentNames: ['summarizer', 'drafter'] });

const agents = [
  { name: 'summarizer', origin: 'Code' },
  { name: 'drafter', origin: 'Database' },
  { name: 'reviewer', origin: 'Database' },
];

/** What the code registered: one function with settings, one without. */
const functions = [
  {
    name: 'word-count',
    inputType: 'System.String',
    outputType: 'System.String',
    settingsSchema: {
      type: 'object',
      properties: { language: { type: 'string' }, limit: { type: 'integer' } },
      required: ['language'],
      additionalProperties: false,
    },
  },
  { name: 'pass-through', inputType: 'System.String', outputType: 'System.String', settingsSchema: null },
];

const sent: unknown[] = [];

/** Opens the editor on a stored workflow (or a new one) and records every PUT body. */
function open(definition: Record<string, unknown> | null, extra: FixtureRoute[] = []): void {
  sent.length = 0;

  // The first route that matches answers, so a case's own routes go ahead of the defaults.
  restoreFetch = installApiMock([
    ...extra,
    fixture('GET', 'api/agents', agents),
    fixture('GET', 'api/workflows/functions', functions),
    ...(definition === null ? [] : [fixture('GET', 'api/workflows/:name', definition)]),
    {
      method: 'PUT',
      pattern: 'api/workflows/:name',
      handler: (_params, _url, body) => {
        sent.push(body);

        return stored({});
      },
    },
  ]);

  renderScreen(definition === null ? <WorkflowEditorScreen /> : <WorkflowEditorScreen name="chain" />);
}

let restoreFetch: () => void = () => undefined;

afterEach(() => {
  restoreFetch();
});

async function save(user: ReturnType<typeof userEvent.setup>): Promise<void> {
  const button = await screen.findByTestId('workflow-save');

  await waitFor(() => expect(button).toHaveProperty('disabled', false));
  await user.click(button);
  await waitFor(() => expect(sent).toHaveLength(1));
}

/** The chain as the screen shows it: `Kind:name`, in order. */
function steps(): string[] {
  return screen
    .getAllByTestId('workflow-step')
    .map((step) => `${step.getAttribute('data-kind')}:${step.getAttribute('data-name')}`);
}

describe('WorkflowEditorScreen - the chain', () => {
  // The editor used to open this workflow with an EMPTY participant list and a disabled Save.
  // Adding an agent enabled Save and replaced the chain - the function node was erased without
  // a word (full replace, K-725). It then showed the chain read-only; now it edits it.
  it('opens a function-node chain with its settings and keeps the pattern the chain needs', async () => {
    open(mixed);

    await screen.findByTestId('workflow-participants');

    expect(steps()).toEqual(['Agent:summarizer', 'Function:word-count']);

    // The settings come back as JSON text the operator can edit.
    expect(screen.getByTestId('workflow-settings-word-count')).toHaveProperty(
      'value',
      JSON.stringify({ language: 'en', limit: 3 }, null, 2),
    );

    // The pattern is locked: a function node runs in a Sequential chain only, and the screen says why.
    expect(screen.getByRole('combobox', { name: /^Kind/ })).toHaveProperty('disabled', true);
    screen.getByText('Function nodes, step timeouts and pinned agent versions run only in a Sequential chain.');

    await waitFor(() => expect(screen.getByTestId('workflow-save')).toHaveProperty('disabled', false));
  });

  it('builds a chain from agents and functions and sends it as nodes with the settings on their node', async () => {
    const user = userEvent.setup();

    open(null);

    await user.type(await screen.findByTestId('workflow-name'), 'narrated');
    await user.selectOptions(await screen.findByTestId('workflow-add-agent'), 'summarizer');
    await user.selectOptions(await screen.findByTestId('workflow-add-function'), 'word-count');
    fireEvent.change(screen.getByTestId('workflow-settings-word-count'), { target: { value: '{"language":"en"}' } });

    await save(user);

    expect(sent[0]).toStrictEqual({
      displayName: null,
      description: null,
      kind: 'Sequential',
      agentNames: [],
      nodes: [
        { name: 'summarizer', kind: 'Agent', settings: null, timeout: null, agentVersion: null },
        { name: 'word-count', kind: 'Function', settings: { language: 'en' }, timeout: null, agentVersion: null },
      ],
      parameters: [],
      managerAgentName: null,
      maxIterations: null,
      handoffInstructions: null,
      requirePlanApproval: false,
      runTimeout: null,
      // A new workflow keeps the default: every answer opens a new run.
      responseMode: 'Branch',
    });
  });

  it('moves and removes steps and keeps each step with its own settings', async () => {
    const user = userEvent.setup();

    open(
      stored({
        nodes: [
          { name: 'word-count', kind: 'Function', settings: { language: 'tr' } },
          { name: 'summarizer', kind: 'Agent', settings: null },
          { name: 'pass-through', kind: 'Function', settings: null },
        ],
      }),
    );

    await screen.findByTestId('workflow-participants');
    await user.click(screen.getByRole('button', { name: 'Move function word-count down' }));

    expect(steps()).toEqual(['Agent:summarizer', 'Function:word-count', 'Function:pass-through']);

    // The settings text moved with its step.
    expect(screen.getByTestId('workflow-settings-word-count')).toHaveProperty('value', JSON.stringify({ language: 'tr' }, null, 2));

    await user.click(screen.getByRole('button', { name: 'Remove function pass-through' }));
    await save(user);

    expect(sent[0]).toMatchObject({
      nodes: [
        { name: 'summarizer', kind: 'Agent', settings: null },
        { name: 'word-count', kind: 'Function', settings: { language: 'tr' } },
      ],
    });
  });
});

describe('WorkflowEditorScreen - the stored form', () => {
  // 13: one canonical form. The last function leaving the chain returns it to the agent list; an
  // empty node list with agents left in it would be the same chain written the other way.
  it('saves a chain with no function left as agentNames and says so beforehand', async () => {
    const user = userEvent.setup();

    open(mixed);

    await user.click(await screen.findByRole('button', { name: 'Remove function word-count' }));

    expect(screen.getByTestId('workflow-form-notice').textContent).toContain('plain agent list');

    await save(user);

    expect(sent[0]).toMatchObject({ kind: 'Sequential', agentNames: ['summarizer'], nodes: [] });
  });

  it('saves a chain that gets its first function as nodes, with agentNames empty', async () => {
    const user = userEvent.setup();

    open(agentsOnly);

    await user.selectOptions(await screen.findByTestId('workflow-add-function'), 'word-count');

    expect(screen.getByTestId('workflow-form-notice').textContent).toContain('node list');

    fireEvent.change(screen.getByTestId('workflow-settings-word-count'), { target: { value: '{"language":"en"}' } });
    await save(user);

    expect(sent[0]).toMatchObject({
      kind: 'Sequential',
      agentNames: [],
      nodes: [
        { name: 'summarizer', kind: 'Agent', settings: null },
        { name: 'drafter', kind: 'Agent', settings: null },
        { name: 'word-count', kind: 'Function', settings: { language: 'en' } },
      ],
    });
  });

  it('writes an agent-only node list (saved over HTTP) as the plain agent list', async () => {
    const user = userEvent.setup();

    open(
      stored({
        nodes: [
          { name: 'summarizer', kind: 'Agent', settings: null },
          { name: 'drafter', kind: 'Agent', settings: null },
        ],
      }),
    );

    await screen.findByTestId('workflow-participants');

    expect(screen.getByTestId('workflow-form-notice').textContent).toContain('plain agent list');

    await save(user);

    expect(sent[0]).toMatchObject({ agentNames: ['summarizer', 'drafter'], nodes: [] });
  });

  it('says nothing about the stored form when a save leaves it as it is', async () => {
    open(mixed);

    await screen.findByTestId('workflow-participants');

    expect(screen.queryByTestId('workflow-form-notice')).toBeNull();
  });
});

describe('WorkflowEditorScreen - the pattern', () => {
  // 14: a function node runs in a Sequential chain only.
  it('offers no function picker outside the Sequential pattern', async () => {
    const user = userEvent.setup();

    open(agentsOnly);

    await screen.findByTestId('workflow-add-function');

    await user.selectOptions(screen.getByRole('combobox', { name: /^Kind/ }), 'Concurrent');

    expect(screen.queryByTestId('workflow-add-function')).toBeNull();

    // The chain itself is untouched by the pattern change.
    expect(steps()).toEqual(['Agent:summarizer', 'Agent:drafter']);
  });

  it('keeps the pattern fixed while the chain holds a function, and releases it when the function goes', async () => {
    const user = userEvent.setup();

    open(mixed);

    const kind = await screen.findByRole('combobox', { name: /^Kind/ });

    expect(kind).toHaveProperty('disabled', true);

    await user.click(screen.getByRole('button', { name: 'Remove function word-count' }));

    expect(kind).toHaveProperty('disabled', false);
  });

  it('does not offer a function that is already in the chain', async () => {
    open(mixed);

    const picker = await screen.findByTestId('workflow-add-function');

    expect(within(picker).queryByRole('option', { name: 'word-count' })).toBeNull();
    within(picker).getByRole('option', { name: 'pass-through' });
  });
});

describe('WorkflowEditorScreen - the settings field', () => {
  it('refuses to send text that is not JSON, says why, and lets go once it is', async () => {
    open(mixed);

    const field = await screen.findByTestId('workflow-settings-word-count');
    const button = screen.getByTestId('workflow-save');

    fireEvent.change(field, { target: { value: '{ "language": ' } });

    expect(button).toHaveProperty('disabled', true);
    screen.getByText('This is not valid JSON.');

    fireEvent.change(field, { target: { value: '{ "language": "en" }' } });

    expect(button).toHaveProperty('disabled', false);
    expect(screen.queryByText('This is not valid JSON.')).toBeNull();
  });

  // Retry is Save again. After a failed save it used to send the draft whatever the settings text
  // held, and text that is not JSON became `null` - which a full-replace save takes for "no
  // settings", so the stored ones were erased.
  it('holds Retry back, like Save, while the settings text is not JSON, and sends nothing then', async () => {
    const user = userEvent.setup();
    let answers = 0;

    open(mixed, [
      {
        method: 'PUT',
        pattern: 'api/workflows/:name',
        handler: (_params, _url, body) => {
          sent.push(body);
          answers += 1;

          return answers === 1
            ? new Response(
                JSON.stringify({ title: 'Workflow definition invalid', detail: "Function node 'word-count' has invalid settings." }),
                { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
              )
            : stored({});
        },
      },
    ]);

    const field = await screen.findByTestId('workflow-settings-word-count');

    fireEvent.change(field, { target: { value: '{"langauge":"en"}' } });
    await user.click(await screen.findByTestId('workflow-save'));

    // The server refused it: its message shows, with Retry.
    await screen.findByText(/Function node 'word-count' has invalid settings\./);
    await screen.findByTestId('error-retry');
    expect(sent).toHaveLength(1);

    // The text stops being JSON: Save is held back, and so is Retry.
    fireEvent.change(field, { target: { value: '{ "language": ' } });

    expect(screen.getByTestId('workflow-save')).toHaveProperty('disabled', true);
    expect(screen.queryByTestId('error-retry')).toBeNull();
    expect(sent).toHaveLength(1);

    // JSON again: Retry is back, and what it sends is the settings as typed - never null.
    fireEvent.change(field, { target: { value: '{"language":"en"}' } });
    await user.click(await screen.findByTestId('error-retry'));
    await waitFor(() => expect(sent).toHaveLength(2));

    expect(sent[1]).toMatchObject({ nodes: [expect.anything(), { name: 'word-count', settings: { language: 'en' } }] });
  });

  it('leaves the verdict on valid JSON to the server: an object the type cannot hold is sent as typed', async () => {
    const user = userEvent.setup();

    open(mixed);

    fireEvent.change(await screen.findByTestId('workflow-settings-word-count'), { target: { value: '{"langauge":"en"}' } });
    await save(user);

    expect(sent[0]).toMatchObject({
      nodes: [expect.anything(), { name: 'word-count', settings: { langauge: 'en' } }],
    });
  });

  it('lists the members the function takes, with their types and which are required', async () => {
    open(mixed);

    const list = await screen.findByTestId('workflow-settings-fields-word-count');

    expect(list.textContent).toContain('language (string, required)');
    expect(list.textContent).toContain('limit (integer)');
  });

  it('shows no field for a function that takes no settings, and says so', async () => {
    const user = userEvent.setup();

    open(agentsOnly);

    await user.selectOptions(await screen.findByTestId('workflow-add-function'), 'pass-through');

    expect(screen.queryByTestId('workflow-settings-pass-through')).toBeNull();
    screen.getByText('This function takes no settings.');
  });

  it('keeps the field of a function that lost its settings while the step still carries some, so they can be cleared', async () => {
    open(
      stored({
        nodes: [
          { name: 'summarizer', kind: 'Agent', settings: null },
          { name: 'pass-through', kind: 'Function', settings: { old: true } },
        ],
      }),
    );

    expect(await screen.findByTestId('workflow-settings-pass-through')).toHaveProperty('value', JSON.stringify({ old: true }, null, 2));
    expect(screen.getAllByText('This function takes no settings.').length).toBeGreaterThan(0);
  });

  it('flags a function the server does not have, at its own step', async () => {
    open(
      stored({
        nodes: [
          { name: 'summarizer', kind: 'Agent', settings: null },
          { name: 'retired', kind: 'Function', settings: null },
        ],
      }),
    );

    await screen.findByTestId('workflow-function-missing');
  });

  it('keeps the chain, and its settings, when the function list cannot be loaded', async () => {
    open(mixed, [fixture('GET', 'api/workflows/functions', { title: 'boom' }, 500)]);

    // The chain is the record; the function list is only what adding a step needs.
    await screen.findByTestId('workflow-participants');

    expect(steps()).toEqual(['Agent:summarizer', 'Function:word-count']);
    expect(await screen.findByText('The list of functions could not be loaded.')).toBeTruthy();
    expect(screen.queryByTestId('workflow-function-missing')).toBeNull();
    expect(screen.getByTestId('workflow-settings-word-count')).toHaveProperty(
      'value',
      JSON.stringify({ language: 'en', limit: 3 }, null, 2),
    );
  });
});

describe('WorkflowEditorScreen - what a save changes', () => {
  // 195.4: a structural edit makes a run that waits to be resumed unresumable (measured), so the
  // screen says so; a settings or description edit does not, so it stays quiet.
  it('stays quiet about waiting runs for an edit of the settings or the description', async () => {
    open(mixed);

    fireEvent.change(await screen.findByTestId('workflow-settings-word-count'), { target: { value: '{"language":"tr"}' } });
    fireEvent.change(screen.getByLabelText('Description', { exact: true }), { target: { value: 'Reworded.' } });

    expect(screen.queryByTestId('workflow-structure-notice')).toBeNull();
  });

  it('warns before a save that changes the steps, the order or the pattern', async () => {
    const user = userEvent.setup();

    open(agentsOnly);

    await screen.findByTestId('workflow-participants');

    expect(screen.queryByTestId('workflow-structure-notice')).toBeNull();

    await user.selectOptions(await screen.findByTestId('workflow-add-agent'), 'reviewer');

    // Phase 200: a waiting run keeps resuming the version it recorded; only resuming it with the
    // new version fails. The notice says that, not that the run is lost.
    const notice = screen.getByTestId('workflow-structure-notice').textContent ?? '';

    expect(notice).toContain('keeps resuming the version it recorded');
    expect(notice).toContain('Resuming it with the new version fails');
    expect(notice).not.toContain('cannot be resumed');
  });

  it('does not warn on a new workflow, which has no run to strand', async () => {
    const user = userEvent.setup();

    open(null);

    await user.selectOptions(await screen.findByTestId('workflow-add-agent'), 'summarizer');

    expect(screen.queryByTestId('workflow-structure-notice')).toBeNull();
    expect(screen.queryByTestId('workflow-form-notice')).toBeNull();
  });
});

describe('WorkflowEditorScreen - timeouts', () => {
  // Phase 196 (F-303, F-405): a step's timeout and the run timeout are typed in seconds and
  // saved as hh:mm:ss; an untouched one goes back as stored (the full-replace round trip is
  // full-replace-editors.test.tsx).
  it('saves a step timeout and the run timeout typed in seconds', async () => {
    const user = userEvent.setup();

    open(mixed);

    fireEvent.change(await screen.findByTestId('workflow-timeout-summarizer'), { target: { value: '300' } });
    fireEvent.change(screen.getByTestId('workflow-timeout-word-count'), { target: { value: '2.5' } });
    fireEvent.change(screen.getByTestId('workflow-run-timeout'), { target: { value: '1800' } });

    // Not a structural change: a run waiting to be resumed stays resumable.
    expect(screen.queryByTestId('workflow-structure-notice')).toBeNull();

    await save(user);

    expect(sent[0]).toMatchObject({
      nodes: [
        { name: 'summarizer', kind: 'Agent', timeout: '00:05:00' },
        { name: 'word-count', kind: 'Function', timeout: '00:00:02.500' },
      ],
      runTimeout: '00:30:00',
    });
  });

  // Phase 196 decision (K-901 unchanged): a step timeout never turns an agent list into a node
  // list. That change of form made a waiting run unresumable and stopped the agents' live
  // token stream; the field is offered only where a node list is saved anyway.
  it('offers no step timeout on an agent-only chain, which stays an agent list', async () => {
    const user = userEvent.setup();

    open(agentsOnly);

    await screen.findByTestId('workflow-participants');

    expect(screen.queryByTestId('workflow-timeout-summarizer')).toBeNull();
    expect(screen.queryByTestId('workflow-timeout-drafter')).toBeNull();

    await save(user);

    expect(sent[0]).toMatchObject({ agentNames: ['summarizer', 'drafter'], nodes: [] });
  });

  it('holds Save back when the last function step leaves a step timeout behind, until it is cleared', async () => {
    const user = userEvent.setup();

    open(mixed);

    fireEvent.change(await screen.findByTestId('workflow-timeout-summarizer'), { target: { value: '300' } });
    await user.click(screen.getByRole('button', { name: 'Remove function word-count' }));

    // Saved as an agent list now, which cannot keep the timeout: held back, not dropped.
    expect(screen.getByTestId('workflow-timeouts-notice').textContent).toContain('Clear the step timeouts');
    expect(screen.getByTestId('workflow-save')).toHaveProperty('disabled', true);

    fireEvent.change(screen.getByTestId('workflow-timeout-summarizer'), { target: { value: '' } });

    expect(screen.queryByTestId('workflow-timeouts-notice')).toBeNull();
    expect(screen.queryByTestId('workflow-timeout-summarizer')).toBeNull();

    await save(user);

    expect(sent[0]).toMatchObject({ agentNames: ['summarizer'], nodes: [] });
  });

  it('holds Save back on an agent-only node list written over HTTP with a step timeout', async () => {
    open(
      stored({
        nodes: [
          { name: 'summarizer', kind: 'Agent', settings: null, timeout: '00:05:00' },
          { name: 'drafter', kind: 'Agent', settings: null, timeout: null },
        ],
      }),
    );

    expect(((await screen.findByTestId('workflow-timeout-summarizer')) as HTMLInputElement).value).toBe('300');
    expect(screen.getByTestId('workflow-timeouts-notice')).toBeTruthy();
    expect(screen.getByTestId('workflow-save')).toHaveProperty('disabled', true);
  });

  it('holds Save back while a timeout cannot be read', async () => {
    open(mixed);

    fireEvent.change(await screen.findByTestId('workflow-timeout-word-count'), { target: { value: 'soon' } });

    expect(screen.getByTestId('workflow-save')).toHaveProperty('disabled', true);
    expect(screen.getByText('Enter seconds (for example 90 or 2.5) or hh:mm:ss.')).toBeTruthy();
  });

  it('names the registered timeout as the placeholder of a function step', async () => {
    open(mixed, [
      fixture('GET', 'api/workflows/functions', [{ ...functions[0], timeout: '00:02:00' }, functions[1]]),
    ]);

    const field = (await screen.findByTestId('workflow-timeout-word-count')) as HTMLInputElement;

    await waitFor(() => expect(field.placeholder).toBe('120 (registered)'));
    expect((screen.getByTestId('workflow-timeout-summarizer') as HTMLInputElement).placeholder).toBe('Sub-agent deadline');
  });

  it('offers no step timeout outside a Sequential chain, where steps are not nodes', async () => {
    open(stored({ kind: 'Concurrent', agentNames: ['summarizer', 'drafter'] }));

    await screen.findByTestId('workflow-participants');

    expect(screen.queryByTestId('workflow-timeout-summarizer')).toBeNull();
    expect(screen.getByTestId('workflow-run-timeout')).toBeTruthy();
  });
});

describe('WorkflowEditorScreen - agent versions', () => {
  // Phase 200 (200.3): an agent step of a Sequential chain can run one version of a stored agent.
  // The versions are the agent's own, newest first; an agent defined in code keeps none.
  const askedFor: string[] = [];
  const versions: FixtureRoute = {
    method: 'GET',
    pattern: 'api/agents/:name/versions',
    handler: (params) => {
      askedFor.push(params['name'] ?? '');

      return [
        { name: params['name'], version: 3 },
        { name: params['name'], version: 2 },
        { name: params['name'], version: 1 },
      ];
    },
  };

  beforeEach(() => {
    askedFor.length = 0;
  });

  /** A chain of two stored agents, the first one pinned to version 2. */
  const pinnedChain = stored({
    nodes: [
      { name: 'drafter', kind: 'Agent', settings: null, timeout: null, agentVersion: 2 },
      { name: 'reviewer', kind: 'Agent', settings: null, timeout: null, agentVersion: null },
    ],
  });

  it('offers the selector for a stored agent only, listing its versions and "Current version"', async () => {
    open(agentsOnly, [versions]);

    const selector = await screen.findByTestId('workflow-version-drafter');

    // summarizer is defined in code: no versions to pin to, so no selector - and no request for them.
    expect(screen.queryByTestId('workflow-version-summarizer')).toBeNull();

    await waitFor(() =>
      expect(within(selector).getAllByRole('option').map((option) => option.textContent)).toEqual([
        'Current version',
        'v3',
        'v2',
        'v1',
      ]),
    );
    expect(selector).toHaveProperty('value', '');
    expect(askedFor).not.toContain('summarizer');
  });

  it('offers no selector for a function step', async () => {
    open(
      stored({
        nodes: [
          { name: 'drafter', kind: 'Agent', settings: null },
          { name: 'word-count', kind: 'Function', settings: { language: 'en' } },
        ],
      }),
      [versions],
    );

    await screen.findByTestId('workflow-version-drafter');

    expect(screen.queryByTestId('workflow-version-word-count')).toBeNull();
    expect(askedFor).not.toContain('word-count');
  });

  it('offers no selector outside the Sequential pattern: only a node can carry a pin', async () => {
    const user = userEvent.setup();

    open(agentsOnly, [versions]);

    await screen.findByTestId('workflow-version-drafter');
    await user.selectOptions(screen.getByRole('combobox', { name: /^Kind/ }), 'Concurrent');

    expect(screen.queryByTestId('workflow-version-drafter')).toBeNull();
  });

  it('saves a pin as a node list, and says before the save that the stored form and the graph change', async () => {
    const user = userEvent.setup();

    open(agentsOnly, [versions]);

    await screen.findByTestId('workflow-participants');
    expect(screen.queryByTestId('workflow-structure-notice')).toBeNull();

    const selector = await screen.findByTestId('workflow-version-drafter');

    await waitFor(() => expect(within(selector).getByRole('option', { name: 'v2' })).toBeTruthy());
    await user.selectOptions(selector, 'v2');

    // The first pin turns the agent list into a node list: different executors, so a different
    // graph - the same warning a first function step gets.
    expect(screen.getByTestId('workflow-form-notice').textContent).toContain('node list');
    expect(screen.getByTestId('workflow-structure-notice')).toBeTruthy();

    // The pattern cannot leave Sequential now, and a step timeout has a node to live on.
    expect(screen.getByRole('combobox', { name: /^Kind/ })).toHaveProperty('disabled', true);
    expect(screen.getByTestId('workflow-timeout-drafter')).toBeTruthy();

    await save(user);

    expect(sent[0]).toMatchObject({
      kind: 'Sequential',
      agentNames: [],
      nodes: [
        { name: 'summarizer', kind: 'Agent', settings: null, timeout: null, agentVersion: null },
        { name: 'drafter', kind: 'Agent', settings: null, timeout: null, agentVersion: 2 },
      ],
    });
  });

  it('says nothing about the graph when only the pinned version changes, and saves the new one', async () => {
    const user = userEvent.setup();

    open(pinnedChain, [versions]);

    const selector = await screen.findByTestId('workflow-version-drafter');

    // The stored pin shows even before the agent's list is in.
    expect(selector).toHaveProperty('value', '2');

    await waitFor(() => expect(within(selector).getByRole('option', { name: 'v1' })).toBeTruthy());
    await user.selectOptions(selector, 'v1');

    // Same steps, same order, same stored form: a run that is waiting keeps its graph.
    expect(screen.queryByTestId('workflow-structure-notice')).toBeNull();
    expect(screen.queryByTestId('workflow-form-notice')).toBeNull();

    await save(user);

    expect(sent[0]).toMatchObject({
      agentNames: [],
      nodes: [
        { name: 'drafter', agentVersion: 1 },
        { name: 'reviewer', agentVersion: null },
      ],
    });
  });

  it('sends an untouched pin back exactly as stored', async () => {
    const user = userEvent.setup();

    open(pinnedChain, [versions]);

    await screen.findByTestId('workflow-version-drafter');
    await save(user);

    expect(sent[0]).toMatchObject({
      nodes: [
        { name: 'drafter', agentVersion: 2 },
        { name: 'reviewer', agentVersion: null },
      ],
    });
  });

  it('returns a chain to the agent list when its last pin is cleared, and says so', async () => {
    const user = userEvent.setup();

    open(pinnedChain, [versions]);

    await user.selectOptions(await screen.findByTestId('workflow-version-drafter'), 'Current version');

    expect(screen.getByTestId('workflow-form-notice').textContent).toContain('plain agent list');
    expect(screen.getByRole('combobox', { name: /^Kind/ })).toHaveProperty('disabled', false);

    await save(user);

    expect(sent[0]).toMatchObject({ agentNames: ['drafter', 'reviewer'], nodes: [] });
  });

  // A pin the server sent must survive even when the agent's list no longer names it: dropping it
  // on a full-replace save would move the step to the current version without a word.
  it('keeps a stored pin that the agent no longer lists', async () => {
    const user = userEvent.setup();

    open(stored({ nodes: [{ name: 'drafter', kind: 'Agent', settings: null, agentVersion: 9 }] }), [versions]);

    const selector = await screen.findByTestId('workflow-version-drafter');

    await waitFor(() => expect(within(selector).getByRole('option', { name: 'Version 9 (not found)' })).toBeTruthy());
    expect(selector).toHaveProperty('value', '9');

    await save(user);

    expect(sent[0]).toMatchObject({ nodes: [{ name: 'drafter', agentVersion: 9 }] });
  });

  it('keeps the chain and its pin when the versions cannot be loaded, and offers a retry', async () => {
    open(pinnedChain, [fixture('GET', 'api/agents/:name/versions', { title: 'boom' }, 500)]);

    const selector = await screen.findByTestId('workflow-version-drafter');

    expect((await screen.findAllByText('The list of versions could not be loaded.')).length).toBeGreaterThan(0);
    expect(selector).toHaveProperty('value', '2');
    expect(steps()).toEqual(['Agent:drafter', 'Agent:reviewer']);
  });
});
