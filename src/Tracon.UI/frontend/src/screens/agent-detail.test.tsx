import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, testMeta } from '../test/api-fixtures';
import { renderScreen, screen } from '../test/render';
import { AgentDetailScreen } from './agent-detail';

function detail(origin: string, sourceName: string): unknown {
  return {
    descriptor: {
      name: 'greeter',
      origin,
      sourceName,
      version: 0,
      model: { provider: 'echo', model: 'echo-1' },
      toolNames: [],
    },
    definition: null,
    factoryInstructions: null,
    isEditable: false,
  };
}

describe('AgentDetailScreen read-only notice', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('tells a custom-source agent which source owns it, not that it is in code', async () => {
    // The notice used to key off isEditable alone, so every non-editable agent
    // read "This agent is declared in code ... edit the application source
    // instead". For an agent that came from an IAgentSource that is false on
    // both counts, and the instruction sends the operator to the wrong file.
    restoreFetch = installApiMock([
      fixture('GET', 'api/agents/:name', detail('Custom', 'json-file')),
    ]);

    renderScreen(<AgentDetailScreen name="greeter" meta={testMeta} />);

    const notice = await screen.findByTestId('agent-readonly-notice');

    expect(notice.textContent).toContain('json-file');
    expect(notice.textContent).not.toContain('declared in code');
  });

  it('compares the loop of two versions and highlights the changed ceiling (Phase 201)', async () => {
    // Before, the version diff drew the harness's flat fields only: a changed
    // loop showed two identical tables.
    const definition = (version: number, maxIterations: number): unknown => ({
      name: 'writer',
      instructions: 'Write.',
      instructionsByCulture: {},
      model: { provider: 'echo', model: 'echo-1' },
      toolNames: [],
      skillNames: [],
      callableAgentNames: [],
      harness: {
        loop: {
          criteria: [{ kind: 'completionMarker', marker: 'DONE', judgeCriteria: [], judgeInstructions: null, modes: [] }],
          maxIterations,
          freshContextPerIteration: false,
          onExhausted: 'FailRun',
          onCriterionError: 'ReturnLastResponse',
        },
      },
      compaction: null,
      memory: null,
      origin: 'Database',
      version,
    });

    restoreFetch = installApiMock([
      fixture('GET', 'api/agents/:name/versions/:a/diff/:b', { left: definition(1, 3), right: definition(2, 4) }),
      fixture('GET', 'api/agents/:name/versions', [
        { name: 'writer', version: 1, model: { provider: 'echo', model: 'echo-1' }, toolNames: [], updatedAt: '2026-10-13T09:00:00Z' },
        { name: 'writer', version: 2, model: { provider: 'echo', model: 'echo-1' }, toolNames: [], updatedAt: '2026-10-13T10:00:00Z' },
      ]),
      fixture('GET', 'api/agents/:name', {
        descriptor: { name: 'writer', origin: 'Database', sourceName: 'database', version: 2, toolNames: [] },
        definition: definition(2, 4),
        factoryInstructions: null,
        isEditable: true,
      }),
    ]);

    const user = userEvent.setup();

    renderScreen(<AgentDetailScreen name="writer" meta={testMeta} />);

    await user.click(await screen.findByTestId('version-checkbox-1'));
    await user.click(screen.getByTestId('version-checkbox-2'));

    const table = await screen.findByTestId('loop-diff');
    const rows = [...table.querySelectorAll('tbody tr')];
    const ceiling = rows.find((row) => row.textContent?.startsWith('Iteration ceiling'));

    expect(ceiling?.textContent).toBe('Iteration ceiling34');
    expect(ceiling?.className).toContain('bg-warn-soft');

    const criteria = rows.find((row) => row.textContent?.startsWith('Criteria'));

    expect(criteria?.textContent).toContain('1. completionMarker (marker=DONE)');
    expect(criteria?.className ?? '').not.toContain('bg-warn-soft');
  });

  it('still tells a code agent that it is declared in code', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/agents/:name', detail('Code', 'code')),
    ]);

    renderScreen(<AgentDetailScreen name="greeter" meta={testMeta} />);

    const notice = await screen.findByTestId('agent-readonly-notice');

    expect(notice.textContent).toContain('declared in code');
  });
});
