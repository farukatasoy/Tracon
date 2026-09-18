import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, testMeta } from '../test/api-fixtures';
import { renderScreen, screen } from '../test/render';
import { AgentsScreen } from './agents';

describe('AgentsScreen tool column', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('names the tools behind the count', async () => {
    // 🚨 The cell rendered `toolNames.length` and nothing else: no tooltip, no
    // markup, no accessible name. An operator could see that an agent had six
    // tools and had to open the agent detail to learn which six.
    restoreFetch = installApiMock([
      fixture('GET', 'api/agents', [
        {
          name: 'support',
          origin: 'Code',
          version: 1,
          model: { provider: 'echo', model: 'echo-1' },
          toolNames: ['get_order_status', 'cancel_order', 'refund_order'],
          updatedAt: '2026-09-07T00:00:00Z',
        },
      ]),
    ]);

    renderScreen(<AgentsScreen meta={testMeta} />);

    const cell = await screen.findByTestId('agent-tool-count');

    expect(cell.textContent).toBe('3');

    // The names are reachable, and reachable the way this console reaches
    // them: a described element, not a `title` a touch or keyboard user
    // never sees.
    const describedBy = cell.getAttribute('aria-describedby');

    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)?.textContent)
      .toBe('get_order_status, cancel_order, refund_order');
  });
});
