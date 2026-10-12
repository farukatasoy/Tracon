import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, testMeta } from '../test/api-fixtures';
import { renderScreen, screen, within } from '../test/render';
import { WorkflowsScreen } from './workflows';

const stamp = '2026-10-12T09:00:00Z';

/** What the server answers for a stored workflow; the case supplies the name and the steps. */
function stored(name: string, steps: Record<string, unknown>): Record<string, unknown> {
  return {
    name,
    displayName: null,
    description: null,
    origin: 'Database',
    kind: 'Sequential',
    agentNames: [],
    nodes: [],
    parameters: [],
    version: 1,
    updatedAt: stamp,
    ...steps,
  };
}

let restoreFetch: () => void = () => undefined;

afterEach(() => {
  restoreFetch();
});

/** The row of the workflow with this name. */
async function rowOf(name: string): Promise<HTMLElement> {
  return (await screen.findByRole('link', { name })).closest('tr') as HTMLElement;
}

describe('WorkflowsScreen - the participants column', () => {
  // The console writes a chain that holds a function as `nodes` and leaves `agentNames` empty, so a
  // column that read only `agentNames` showed the chain the operator had just built as empty.
  it('lists the steps of a chain saved as nodes, of one saved as agent names, and nothing for a code graph', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', [
        stored('narrated', {
          nodes: [
            { name: 'support', kind: 'Agent', settings: null },
            { name: 'speak', kind: 'Function', settings: { voice: 'alloy' } },
          ],
        }),
        stored('review', { agentNames: ['drafter', 'reviewer'] }),
        stored('approval-flow', { origin: 'Code', kind: null }),
      ]),
    ]);

    renderScreen(<WorkflowsScreen meta={testMeta} />);

    expect(within(await rowOf('narrated')).getByText('support → speak')).toBeTruthy();
    expect(within(await rowOf('review')).getByText('drafter → reviewer')).toBeTruthy();
    expect(within(await rowOf('approval-flow')).getByText('—', { selector: 'td' })).toBeTruthy();
  });

  it('names the column for what it holds: the steps, which are not always agents', async () => {
    restoreFetch = installApiMock([fixture('GET', 'api/workflows', [stored('review', { agentNames: ['drafter'] })])]);

    renderScreen(<WorkflowsScreen meta={testMeta} />);

    await rowOf('review');

    expect(screen.getByRole('columnheader', { name: 'Participants' })).toBeTruthy();
    expect(screen.queryByRole('columnheader', { name: 'Agents' })).toBeNull();
  });
});
