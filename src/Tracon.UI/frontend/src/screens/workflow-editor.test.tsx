import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen, waitFor, within } from '../test/render';
import { WorkflowEditorScreen } from './workflow-editor';

const mixed = {
  name: 'mixed-chain',
  displayName: null,
  description: 'Summarizes, then counts words.',
  kind: 'Sequential',
  agentNames: [],
  nodes: [
    { name: 'summarizer', kind: 'Agent' },
    { name: 'word-count', kind: 'Function' },
  ],
  managerAgentName: null,
  maxIterations: null,
  handoffInstructions: null,
  requirePlanApproval: false,
  tenantId: 'default',
  version: 1,
  updatedAt: '2026-10-10T09:00:00Z',
};

describe('WorkflowEditorScreen', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  // The editor used to open this workflow with an EMPTY participant list and a
  // disabled Save. Adding an agent enabled Save and replaced the chain — the
  // function node was erased without a word (full replace, K-725).
  it('shows a function-node chain read-only and keeps the kind it needs', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/agents', [{ name: 'summarizer', origin: 'Code' }]),
      fixture('GET', 'api/workflows/:name', mixed),
    ]);

    renderScreen(<WorkflowEditorScreen name="mixed-chain" />);

    const chain = await screen.findByTestId('workflow-nodes');
    const steps = within(chain).getAllByRole('listitem');

    expect(steps.map((step) => step.textContent)).toEqual(['1summarizeragent', '2word-countfunction']);

    // The kind is locked: a function node runs in a Sequential chain only.
    expect(screen.getByRole('combobox', { name: /^Kind/ })).toHaveProperty('disabled', true);

    // No agent picker: picking an agent here is what used to erase the chain.
    expect(screen.queryByRole('combobox', { name: /^Participants/ })).toBeNull();

    await waitFor(() => expect(screen.getByTestId('workflow-save')).toHaveProperty('disabled', false));
  });
});
