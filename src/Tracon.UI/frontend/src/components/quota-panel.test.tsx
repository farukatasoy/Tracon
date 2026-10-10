import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen } from '../test/render';
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
