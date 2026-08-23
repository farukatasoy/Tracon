// Generated — every path, operation and schema, including a readable
// root-level type alias per schema (e.g. `RunRecord`, `AgentDefinition`).
export * from './schema.js';

import type { components } from './schema.js';

/** Wire contracts, by schema name. Example: `Schemas['RunRecord']`. */
export type Schemas = components['schemas'];

// Hand-written — the thin layer described in docs/arsiv/fazlar/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md, section 84.5.
export { AgentPrismError } from './error.js';
export { createAgentPrismClient, type AgentPrismClient, type AgentPrismClientOptions } from './client.js';
