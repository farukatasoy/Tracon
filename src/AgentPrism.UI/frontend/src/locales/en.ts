/**
 * English message catalogue — the shape every other language must satisfy.
 *
 * Only aggregates domain fragments from `en/*.ts`; a message literal is never
 * written here directly. Fragment boundaries follow the screen groups in the
 * navigation: common/shell, agents, runs/sessions/dashboard, workflows/jobs/
 * evals, security/operations, settings.
 *
 * The object is deliberately NOT declared `as const`: `Messages` has to describe
 * the set of KEYS with `string` values, not the set of English sentences. With
 * `as const` a translation would only type check when it repeated the English
 * text verbatim.
 *
 * Fragment key sets must be pairwise disjoint — an accidental duplicate would
 * be silently overridden by object spread below. `locales/fragments.test.ts`
 * asserts this at run time; `tsc` cannot catch it because a spread's inferred
 * type simply takes the last writer of a duplicated key.
 */
import { enCommon } from './en/common';
import { enAgents } from './en/agents';
import { enRuns } from './en/runs';
import { enWorkflows } from './en/workflows';
import { enOperations } from './en/operations';
import { enSettings } from './en/settings';

export const en = {
  ...enCommon,
  ...enAgents,
  ...enRuns,
  ...enWorkflows,
  ...enOperations,
  ...enSettings,
};

/**
 * The shape a translation has to satisfy.
 *
 * `en` carries no `as const`, so TypeScript widens each value to `string` while
 * keeping the keys exact — which is precisely the contract wanted: `tr` must
 * supply every key, but is free to supply different sentences.
 */
export type Messages = typeof en;
