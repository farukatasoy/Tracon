import { describe, expect, it } from 'vitest';
import { runCostTotal } from './cost';
import type { RunCost } from './server-types';

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

describe('runCostTotal', () => {
  it('adds all four terms', () => {
    // 🚨 K-483's class: a three-term sum reads 3.5 here, a tenth of the bill in the write-heavy case.
    expect(runCostTotal(cost({ inputCost: 1, outputCost: 2, cachedInputCost: 0.5, cacheWriteCost: 0.25 }))).toBe(3.75);
  });

  it('counts the cache write when it is the only priced term', () => {
    expect(runCostTotal(cost({ cacheWriteCost: 0.25 }))).toBe(0.25);
  });

  it('ignores the unit prices: they are rates, not amounts', () => {
    expect(
      runCostTotal(
        cost({
          inputCost: 1,
          cacheWriteCost: 0.25,
          inputPricePerMillionTokens: 1000,
          cacheWritePricePerMillionTokens: 1250,
          cacheWriteLongPricePerMillionTokens: 2000,
        }),
      ),
    ).toBe(1.25);
  });

  it('is null, not zero, when nothing was priced', () => {
    expect(runCostTotal(cost({}))).toBeNull();
    expect(runCostTotal(null)).toBeNull();
    expect(runCostTotal(undefined)).toBeNull();
  });

  it('keeps a priced zero as zero', () => {
    expect(runCostTotal(cost({ inputCost: 0 }))).toBe(0);
  });
});
