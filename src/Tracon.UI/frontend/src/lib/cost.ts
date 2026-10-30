import type { RunCost } from './server-types';

/**
 * The cost terms of a run, in the order the server adds them (`RunCost.Total()`).
 *
 * 🚨 Every `*Cost` field of {@link RunCost} is an addend, not a subset of
 * `inputCost`: the server subtracts the cached and the cache-written tokens out of
 * the input charge and bills them in their own terms. A sum that leaves one out
 * under-reports every run that used the prompt cache.
 */
const COST_TERMS = ['inputCost', 'outputCost', 'cachedInputCost', 'cacheWriteCost'] as const satisfies readonly (keyof RunCost)[];

/**
 * Compile-time gate for the class: a `*Cost` field added to `RunCost` and not to
 * {@link COST_TERMS} stops `tsc` here, instead of under-reporting in the console.
 */
type MissingCostTerm = Exclude<Extract<keyof RunCost, `${string}Cost`>, (typeof COST_TERMS)[number]>;
const everyCostTermIsSummed: [MissingCostTerm] extends [never] ? true : { missingCostTerm: MissingCostTerm } = true;
void everyCostTermIsSummed;

/**
 * What a run cost: the sum of every cost term the server priced.
 *
 * `null` when no term was priced — "unknown" must not read as "free".
 *
 * @param cost The run's cost, or `null` when it was never priced.
 * @returns The total in the cost's currency, or `null`.
 */
export function runCostTotal(cost: RunCost | null | undefined): number | null {
  if (cost == null) {
    return null;
  }

  let total: number | null = null;

  for (const term of COST_TERMS) {
    const value = cost[term];

    if (value != null) {
      total = (total ?? 0) + value;
    }
  }

  return total;
}
