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
