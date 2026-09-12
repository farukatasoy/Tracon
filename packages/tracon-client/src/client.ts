import createClient, { type Client, type Middleware } from 'openapi-fetch';
import type { paths } from './schema.js';
import { TraconError } from './error.js';

/** Options for {@link createTraconClient}. */
export interface TraconClientOptions {
  /** Application root plus the prefix passed to `MapTracon` (default `/tracon`). */
  baseUrl: string;
  /** Bearer token, or a function that supplies one per request. */
  token?: string | (() => string | null);
  /** Called when the server answers 401 — the caller decides what "reject the token" means. */
  onUnauthorized?: () => void;
  /** Custom fetch, defaults to `globalThis.fetch`. */
  fetch?: typeof globalThis.fetch;
}

/** A client typed against every Tracon management API path. */
export type TraconClient = Client<paths>;

interface ProblemDetailsBody {
  title?: string;
  detail?: string;
}

/**
 * Creates a typed client that throws {@link TraconError} on a failed
 * response.
 *
 * This layer stores no token itself — the caller's own token store (or a
 * closure over one) is passed as `token`, and this layer only reads it. It
 * does not translate `title`/`detail` (K-232): they come from the server and
 * are shown verbatim.
 */
export function createTraconClient(options: TraconClientOptions): TraconClient {
  const client = createClient<paths>({
    baseUrl: options.baseUrl,
    fetch: options.fetch,
  });

  const middleware: Middleware = {
    onRequest({ request }) {
      const token = typeof options.token === 'function' ? options.token() : options.token;

      if (token) {
        request.headers.set('Authorization', `Bearer ${token}`);
      }

      return request;
    },
    async onResponse({ response }) {
      if (response.ok) {
        return response;
      }

      if (response.status === 401) {
        // The token was rejected. The caller decides what that means (drop it,
        // prompt again); this layer still throws below so the failed call does
        // not silently resolve with no data.
        options.onUnauthorized?.();
      }

      let title = `HTTP ${response.status}`;
      let detail: string | null = null;

      try {
        const body = (await response.clone().json()) as ProblemDetailsBody;

        title = body.title ?? title;
        detail = body.detail ?? null;
      } catch {
        // A body that is not problem+json leaves the status line as the message.
      }

      throw new TraconError(response.status, title, detail);
    },
  };

  client.use(middleware);

  return client;
}
