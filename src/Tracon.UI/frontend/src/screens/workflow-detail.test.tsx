import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, sseFrame, testMeta } from '../test/api-fixtures';
import { renderScreen, screen, waitFor, within } from '../test/render';
import { WorkflowDetailScreen } from './workflow-detail';

const parameterized = {
  name: 'podcast',
  displayName: null,
  description: 'Writes, then edits.',
  origin: 'Database',
  kind: 'Sequential',
  agentNames: ['writer', 'editor'],
  nodes: [],
  parameters: [
    { name: 'tone', kind: 'Text', required: true, defaultValue: null, description: 'Register of the script.' },
    { name: 'voice', kind: 'Text', required: false, defaultValue: 'alloy', description: null },
    { name: 'verbose', kind: 'Boolean', required: false, defaultValue: null, description: null },
  ],
  version: 2,
  updatedAt: '2026-10-12T09:00:00Z',
};

const plain = { ...parameterized, name: 'plain', parameters: [] };

const graph = { name: 'podcast', nodes: [], edges: [], mermaid: 'graph TD', startExecutorId: 'start' };

describe('WorkflowDetailScreen run parameters', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('shows one input per declared parameter and blocks Run until a required one has a value', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [parameterized]),
      fixture('GET', 'api/workflows/:name/graph', graph),
    ]);

    renderScreen(<WorkflowDetailScreen name="podcast" meta={testMeta} />);

    const panel = await screen.findByTestId('workflow-parameters');

    expect(within(panel).getByLabelText(/^tone/)).toBeTruthy();
    expect(within(panel).getByLabelText(/^voice/)).toBeTruthy();
    expect(within(panel).getByRole('checkbox')).toBeTruthy();

    // 'tone' is required and has no default: the run cannot start.
    expect(screen.getByTestId('workflow-run')).toHaveProperty('disabled', true);

    // 'voice' shows its default as the value; the checkbox starts unchecked.
    expect((within(panel).getByLabelText(/^voice/) as HTMLInputElement).value).toBe('alloy');
    expect((within(panel).getByRole('checkbox') as HTMLInputElement).checked).toBe(false);
  });

  it('sends the typed values with the message and nothing for a workflow that declares none', async () => {
    const sent: unknown[] = [];

    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [parameterized, plain]),
      fixture('GET', 'api/workflows/:name/graph', graph),
      {
        method: 'POST',
        pattern: 'api/workflows/:name/run',
        handler: (_params, _url, body) => {
          sent.push(body);

          return new Response(
            sseFrame('run', { runId: '01a12790-0000-7000-8000-000000000001' }) + sseFrame('done', {}),
            { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
          );
        },
      },
    ]);

    const user = userEvent.setup();

    renderScreen(<WorkflowDetailScreen name="podcast" meta={testMeta} />);

    const panel = await screen.findByTestId('workflow-parameters');

    await user.type(within(panel).getByLabelText(/^tone/), 'formal');
    await user.click(within(panel).getByRole('checkbox'));
    await user.type(screen.getByTestId('workflow-message'), 'Episode 12');
    await user.click(screen.getByTestId('workflow-run'));

    await waitFor(() => expect(sent).toHaveLength(1));

    // 'voice' was left at its shown default and is NOT sent: the server applies
    // the workflow's own default, and the run records it. The checkbox sends 'true'.
    expect(sent[0]).toEqual({
      message: 'Episode 12',
      parameters: { tone: 'formal', verbose: 'true' },
    });
  });

  it('shows no parameter section and sends no parameters member for a workflow that declares none', async () => {
    const sent: unknown[] = [];

    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [plain]),
      fixture('GET', 'api/workflows/:name/graph', graph),
      {
        method: 'POST',
        pattern: 'api/workflows/:name/run',
        handler: (_params, _url, body) => {
          sent.push(body);

          return new Response(
            sseFrame('run', { runId: '01a12790-0000-7000-8000-000000000002' }) + sseFrame('done', {}),
            { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
          );
        },
      },
    ]);

    const user = userEvent.setup();

    renderScreen(<WorkflowDetailScreen name="plain" meta={testMeta} />);

    await screen.findByTestId('workflow-run');

    expect(screen.queryByTestId('workflow-parameters')).toBeNull();

    await user.click(screen.getByTestId('workflow-run'));
    await waitFor(() => expect(sent).toHaveLength(1));

    expect(sent[0]).toEqual({ message: null });
  });
});

// Phase 200 (200.5): the version history of a stored workflow, a comparison of two versions and
// a rollback. A workflow defined in code keeps no history and gets no section.
describe('WorkflowDetailScreen version history', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  const definition = (version: number, overrides: Record<string, unknown> = {}): Record<string, unknown> => ({
    name: 'podcast',
    displayName: null,
    description: 'Writes, then edits.',
    kind: 'Sequential',
    agentNames: ['writer', 'editor'],
    managerAgentName: null,
    nodes: [],
    maxIterations: null,
    handoffInstructions: null,
    requirePlanApproval: false,
    parameters: [],
    runTimeout: null,
    tenantId: 'default',
    version,
    updatedAt: `2026-10-1${version}T09:00:00Z`,
    ...overrides,
  });

  // v1 is a plain agent list. v2 pins writer to version 3 and adds a function with settings and a
  // timeout (a node list). v3 changes one settings value and the description.
  const v1 = definition(1);
  const v2 = definition(2, {
    description: 'Writes, edits, counts.',
    agentNames: [],
    nodes: [
      { name: 'writer', kind: 'Agent', settings: null, timeout: null, agentVersion: 3 },
      { name: 'editor', kind: 'Agent', settings: null, timeout: null, agentVersion: null },
      { name: 'word-count', kind: 'Function', settings: { language: 'tr' }, timeout: '00:00:05', agentVersion: null },
    ],
    runTimeout: '00:10:00',
    parameters: [{ name: 'tone', kind: 'Text', required: true, defaultValue: null, description: 'Register.' }],
  });
  const v3 = definition(3, {
    ...v2,
    version: 3,
    description: 'Writes, edits, counts words.',
    nodes: [
      { name: 'writer', kind: 'Agent', settings: null, timeout: null, agentVersion: 3 },
      { name: 'editor', kind: 'Agent', settings: null, timeout: null, agentVersion: null },
      { name: 'word-count', kind: 'Function', settings: { language: 'en' }, timeout: '00:00:05', agentVersion: null },
    ],
  });

  // What a rollback to v1 appends: the content of v1 under a new version number.
  const v4 = { ...v1, version: 4, updatedAt: '2026-10-14T09:00:00Z' };

  const stored = { ...parameterized, parameters: [], version: 3 };

  /** The lines of one diff block that are marked as added or removed, as `+ text` / `- text`. */
  function changedLines(block: HTMLElement): string[] {
    return [...block.querySelectorAll('pre > div')]
      .filter((line) => line.className.includes('bg-success-soft') || line.className.includes('bg-danger-soft'))
      .map((line) => `${line.className.includes('bg-success-soft') ? '+' : '-'} ${line.querySelectorAll('span')[3]?.textContent?.trim() ?? ''}`);
  }

  it('lists the versions of a stored workflow, newest first, with the current one marked', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [stored]),
      fixture('GET', 'api/workflows/:name/graph', graph),
      fixture('GET', 'api/workflows/:name/versions', [v3, v2, v1]),
    ]);

    renderScreen(<WorkflowDetailScreen name="podcast" meta={testMeta} />);

    await screen.findByTestId('version-checkbox-3');

    const rows = screen.getAllByRole('row').slice(1);

    expect(rows.map((row) => row.textContent?.slice(0, 2))).toEqual(['v3', 'v2', 'v1']);
    within(rows[0]!).getByText('current');

    // Pattern and how many steps each version has: an agent list counts its agents, a node list its nodes.
    expect(rows[0]?.textContent).toContain('Sequential3');
    expect(rows[2]?.textContent).toContain('Sequential2');
  });

  it('compares two versions: the changed fields, the pin, the timeout and the settings value', async () => {
    const user = userEvent.setup();

    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [stored]),
      fixture('GET', 'api/workflows/:name/graph', graph),
      fixture('GET', 'api/workflows/:name/versions', [v3, v2, v1]),
      {
        method: 'GET',
        pattern: 'api/workflows/:name/versions/:a/diff/:b',
        handler: (params) => ({
          left: params['a'] === '1' ? v1 : v2,
          right: params['b'] === '3' ? v3 : v2,
        }),
      },
    ]);

    renderScreen(<WorkflowDetailScreen name="podcast" meta={testMeta} />);

    // v1 -> v2: the form changes (agent list -> node list) and the pin, function and timeout arrive.
    await user.click(await screen.findByTestId('version-checkbox-1'));
    await user.click(screen.getByTestId('version-checkbox-2'));

    const fields = await screen.findByTestId('workflow-fields-diff');
    const changedRows = [...fields.querySelectorAll('tbody tr')].filter((row) => row.className.includes('bg-warn-soft'));

    expect(changedRows.map((row) => row.textContent)).toEqual([
      'DescriptionWrites, then edits.Writes, edits, counts.',
      'Run timeout—00:10:00',
    ]);

    // Kind did not change: its row is present but not marked.
    const kind = [...fields.querySelectorAll('tbody tr')].find((row) => row.textContent?.startsWith('Kind'));

    expect(kind?.className ?? '').not.toContain('bg-warn-soft');

    const steps = screen.getByTestId('workflow-steps-diff');

    // writer: same step, now pinned. editor: untouched, so not a changed line despite the new stored form.
    expect(changedLines(steps)).toEqual([
      '- agent writer',
      '+ agent writer · version 3',
      '+ function word-count · timeout 00:00:05',
      '+ settings:',
      '+ {',
      '+ "language": "tr"',
      '+ }',
    ]);

    // The run parameters appear in v2 only.
    expect(changedLines(screen.getByTestId('workflow-parameters-diff')).some((line) => line.includes('"name": "tone"'))).toBe(true);

    // v2 -> v3: one settings value and the description.
    await user.click(screen.getByTestId('version-checkbox-1'));
    await user.click(screen.getByTestId('version-checkbox-3'));

    await waitFor(() =>
      expect(changedLines(screen.getByTestId('workflow-steps-diff'))).toEqual(['- "language": "tr"', '+ "language": "en"']),
    );
  });

  it('rolls back for an administrator, sends the version, and shows the new current version', async () => {
    const user = userEvent.setup();
    const sent: unknown[] = [];
    let current = 3;

    restoreFetch = installApiMock([
      {
        method: 'POST',
        pattern: 'api/workflows/:name/rollback',
        handler: (_params, _url, body) => {
          sent.push(body);
          current = 4;

          return v4;
        },
      },
      { method: 'GET', pattern: 'api/workflows', handler: () => [{ ...stored, version: current }] },
      fixture('GET', 'api/workflows/:name/graph', graph),
      {
        method: 'GET',
        pattern: 'api/workflows/:name/versions',
        handler: () => (current === 4 ? [v4, v3, v2, v1] : [v3, v2, v1]),
      },
    ]);

    renderScreen(<WorkflowDetailScreen name="podcast" meta={testMeta} />);

    // The current version has no rollback of its own.
    await screen.findByTestId('version-checkbox-3');
    expect(screen.queryByTestId('version-rollback-3')).toBeNull();

    await user.click(screen.getByTestId('version-rollback-1'));

    await waitFor(() => expect(sent).toEqual([{ version: 1 }]));

    // The workflow queries are invalidated: the list gains v4 and the badge moves to it.
    const rows = await waitFor(() => {
      const found = screen.getAllByRole('row').slice(1);

      expect(found).toHaveLength(4);

      return found;
    });

    within(rows[0]!).getByText('current');
    expect(within(rows[1]!).queryByText('current')).toBeNull();
  });

  it('shows the refusal when the old content no longer validates, and keeps the list', async () => {
    const user = userEvent.setup();

    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [stored]),
      fixture('GET', 'api/workflows/:name/graph', graph),
      fixture('GET', 'api/workflows/:name/versions', [v3, v2, v1]),
      {
        method: 'POST',
        pattern: 'api/workflows/:name/rollback',
        handler: () =>
          new Response(
            JSON.stringify({
              title: 'Workflow definition invalid',
              detail: "Version 2 names the function 'word-count', which is no longer registered.",
            }),
            { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
          ),
      },
    ]);

    renderScreen(<WorkflowDetailScreen name="podcast" meta={testMeta} />);

    await user.click(await screen.findByTestId('version-rollback-2'));

    const error = await screen.findByTestId('workflow-rollback-error');

    expect(error.textContent).toContain("Version 2 names the function 'word-count', which is no longer registered.");
    // A rollback has no Retry: pressing the button again is the operator's decision.
    expect(within(error).queryByTestId('error-retry')).toBeNull();
    expect(screen.getAllByRole('row')).toHaveLength(4);
  });

  it('offers no rollback to a reader who is not an administrator, but still lists and compares', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [stored]),
      fixture('GET', 'api/workflows/:name/graph', graph),
      fixture('GET', 'api/workflows/:name/versions', [v3, v2, v1]),
    ]);

    renderScreen(
      <WorkflowDetailScreen name="podcast" meta={{ ...testMeta, roles: { ...testMeta.roles, canAdminister: false } }} />,
    );

    await screen.findByTestId('version-checkbox-1');

    expect(screen.queryByTestId('version-rollback-1')).toBeNull();
    expect(screen.queryByTestId('version-rollback-2')).toBeNull();
  });

  it('shows no history section for a workflow defined in code, and never asks for one', async () => {
    const asked: string[] = [];

    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [{ ...parameterized, name: 'coded', origin: 'Code', kind: null, parameters: [], version: 1 }]),
      fixture('GET', 'api/workflows/:name/graph', graph),
      {
        method: 'GET',
        pattern: 'api/workflows/:name/versions',
        handler: () => {
          asked.push('versions');

          return { title: 'Workflow has no version history' };
        },
      },
    ]);

    renderScreen(<WorkflowDetailScreen name="coded" meta={testMeta} />);

    await screen.findByTestId('workflow-run');

    expect(screen.queryByText('Version history')).toBeNull();
    expect(screen.queryByTestId('version-checkbox-1')).toBeNull();
    expect(asked).toEqual([]);
  });
});
