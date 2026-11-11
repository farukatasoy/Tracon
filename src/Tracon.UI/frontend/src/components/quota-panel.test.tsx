import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen, waitFor } from '../test/render';
import { QuotaPanel } from './quota-panel';

const usage = {
  tenantId: 'default',
  timeZone: 'UTC',
  usage: [],
  definitions: [
    {
      id: '01a12790-0000-7000-8000-000000000005',
      tenantId: 'default',
      agentName: 'support',
      period: 'Daily',
      maxRuns: 100,
      maxTokens: null,
      maxCost: null,
      enabled: true,
      createdAt: '2026-10-10T09:00:00Z',
      updatedAt: '2026-10-10T09:00:00Z',
    },
  ],
  dailyResetsAt: '2026-10-11T00:00:00Z',
  monthlyResetsAt: '2026-11-01T00:00:00Z',
};

describe('QuotaPanel', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  // `PUT /api/quotas` upserts by tenant + agent + period. Changing either while
  // editing wrote a second rule — or overwrote another one — and left the rule
  // being edited untouched.
  it('locks the agent and the period of a rule being edited', async () => {
    restoreFetch = installApiMock([fixture('GET', 'api/quotas/usage', usage)]);

    renderScreen(<QuotaPanel />);

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }));

    expect(screen.getByTestId('quota-agent')).toHaveProperty('readOnly', true);
    expect(screen.getByTestId('quota-period')).toHaveProperty('disabled', true);
  });

  it('leaves the agent and the period open for a new rule', async () => {
    restoreFetch = installApiMock([fixture('GET', 'api/quotas/usage', usage)]);

    renderScreen(<QuotaPanel />);

    await userEvent.click(await screen.findByRole('button', { name: 'Add quota' }));

    expect(screen.getByTestId('quota-agent')).toHaveProperty('readOnly', false);
    expect(screen.getByTestId('quota-period')).toHaveProperty('disabled', false);
  });
});

describe('QuotaPanel saving while the first load is still in flight', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  // 🚨 The panel can be used before its first answer comes back. TanStack Query does not start a second request
  // for `invalidateQueries` while the first one has no data yet: it hands back the request already in flight, and
  // that request was answered from the state before the save. The list stayed empty after a save that had
  // succeeded (windows-latest, preview.9 tag run).
  it('shows the rule it just saved when the first answer was computed before the save', async () => {
    let answerFirstLoad!: () => void;
    const firstLoadHeld = new Promise<void>((resolve) => {
      answerFirstLoad = resolve;
    });
    let loads = 0;
    let saved = false;

    restoreFetch = installApiMock([
      {
        method: 'GET',
        pattern: 'api/quotas/usage',
        handler: async () => {
          loads += 1;

          if (loads > 1) {
            return saved ? usage : { ...usage, definitions: [] };
          }

          // The first request is answered from the state before the save, and only after the save is done.
          await firstLoadHeld;

          return { ...usage, definitions: [] };
        },
      },
      {
        method: 'PUT',
        pattern: 'api/quotas',
        handler: () => {
          saved = true;

          return usage.definitions[0];
        },
      },
    ]);

    renderScreen(<QuotaPanel />);

    await userEvent.click(await screen.findByRole('button', { name: 'Add quota' }));
    await userEvent.type(screen.getByTestId('quota-max-runs'), '100');
    await userEvent.click(screen.getByTestId('quota-save'));
    await waitFor(() => expect(screen.queryByTestId('quota-save')).toBeNull());

    answerFirstLoad();

    expect(await screen.findByTestId('quota-row')).toBeTruthy();
  });
});
