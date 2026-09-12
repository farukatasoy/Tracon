import type { Messages } from './en';
import { trCommon } from './tr/common';
import { trAgents } from './tr/agents';
import { trRuns } from './tr/runs';
import { trWorkflows } from './tr/workflows';
import { trOperations } from './tr/operations';
import { trSettings } from './tr/settings';

/**
 * Turkish message catalogue.
 *
 * Only aggregates domain fragments from `tr/*.ts`, mirroring `en.ts`. The
 * `Messages` annotation here is the second half of the contract: a fragment can
 * compile on its own (checked against its matching `en/*.ts` fragment) while
 * still being verified against the FULL shape once aggregated — this catches a
 * fragment that was forgotten here entirely.
 *
 * Wording rules, taken from the repository's own documents:
 *  - `agent`, `tool`, `skill`, `workflow`, `token`, `prompt`, `MCP` keep their
 *    English spelling and take Turkish suffixes with an apostrophe.
 *  - `run` is "çalıştırma", `session` is "oturum" — the words the design
 *    documents already use.
 *  - Sentences follow ASD-STE100: one idea, active voice, short.
 */
export const tr: Messages = {
  ...trCommon,
  ...trAgents,
  ...trRuns,
  ...trWorkflows,
  ...trOperations,
  ...trSettings,
};
