// Generated — every path, operation and schema, including a readable
// root-level type alias per schema (e.g. `RunRecord`, `AgentDefinition`).
export * from './schema.js';

import type { components } from './schema.js';

/** Wire contracts, by schema name. Example: `Schemas['RunRecord']`. */
export type Schemas = components['schemas'];

// Hand-written — the thin layer described in docs/arsiv/fazlar/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md, section 84.5.
export { TraconError } from './error.js';
export { createTraconClient, type TraconClient, type TraconClientOptions } from './client.js';

// Streaming. A `text/event-stream` endpoint needs no special client method —
// pass `parseAs: 'stream'`, or read the `response` every call returns — but it
// does need a decoder, and writing one per consumer is how framing bugs get
// duplicated (Phase 159).
export {
  readSse,
  SseDecoder,
  SseIdleTimeoutError,
  type ReadSseOptions,
  type SseFrame,
} from './sse.js';
