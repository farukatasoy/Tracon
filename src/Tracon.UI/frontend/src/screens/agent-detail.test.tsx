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

  it('still tells a code agent that it is declared in code', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/agents/:name', detail('Code', 'code')),
    ]);

    renderScreen(<AgentDetailScreen name="greeter" meta={testMeta} />);

    const notice = await screen.findByTestId('agent-readonly-notice');

    expect(notice.textContent).toContain('declared in code');
  });
});
