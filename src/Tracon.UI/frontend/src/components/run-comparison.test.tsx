import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen, within } from '../test/render';
import { RunComparison } from './run-comparison';
import type { RunComparisonSide, RunCost } from '../lib/server-types';

function cost(terms: Partial<RunCost>): RunCost {
  return {
    cacheWriteCost: null,
    cacheWriteLongPricePerMillionTokens: null,
    cacheWritePricePerMillionTokens: null,
    cachedInputCost: null,
    cachedInputPricePerMillionTokens: null,
    inputCost: null,
    inputPricePerMillionTokens: null,
    outputCost: null,
    outputPricePerMillionTokens: null,
    currency: 'USD',
    source: 'Catalog',
    ...terms,
  } as RunCost;
}

function side(runId: string, runCost: RunCost | null): RunComparisonSide {
  return {
    runId,
    agentName: 'support',
    agentVersion: 1,
    modelId: 'claude-test',
    status: 'Completed',
    durationMs: 1200,
    usage: null,
    cost: runCost,
    toolCallCount: 0,
    errorClass: null,
    errorMessage: null,
    replayOfRunId: null,
    output: 'ok',
    scores: [],
  } as RunComparisonSide;
}

describe('RunComparison', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('compares the whole cost of each run: input, output, cache read and cache write', async () => {
    // 🚨 K-483's class, client copy: the left run wrote 1.35 to the prompt cache. A three-term sum
    // shows 0.14 USD for a run that cost 1.49 USD.
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:a/compare/:b', {
        left: side('a', cost({ inputCost: 0.1, outputCost: 0.02, cachedInputCost: 0.02, cacheWriteCost: 1.35 })),
        right: side('b', cost({ inputCost: 1, outputCost: 0.5 })),
      }),
    ]);

    renderScreen(<RunComparison left="a" right="b" />);

    const table = within(await screen.findByTestId('run-comparison'));
    const row = table.getByText('Cost').closest('tr')!;

    expect(within(row).getByText('1.49 USD')).toBeTruthy();
    expect(within(row).getByText('1.50 USD')).toBeTruthy();
  });

  it('shows a run that was never priced as unknown and not as free', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:a/compare/:b', {
        left: side('a', null),
        right: side('b', cost({ cacheWriteCost: 0.25 })),
      }),
    ]);

    renderScreen(<RunComparison left="a" right="b" />);

    const row = (await screen.findByText('Cost')).closest('tr')!;

    // Unknown on the left; a priced run whose only term is the write on the right.
    expect(within(row).getByText('—')).toBeTruthy();
    expect(within(row).getByText('0.25 USD')).toBeTruthy();
  });
});
