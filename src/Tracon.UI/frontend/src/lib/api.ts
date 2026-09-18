import { TraconError, createTraconClient } from '@tracon/client';
import { apiBase, apiUrl } from './base';
import { authHeaders, getToken, rejectToken } from './auth';

/**
 * The single typed client instance the console shares.
 *
 * `apiBase` already carries the application root plus the `MapTracon`
 * prefix (`lib/base.ts`) — the same value the old hand-written `request()`
 * used. `token`/`onUnauthorized` read straight from the existing token store
 * (`lib/auth.ts`); this layer stores nothing itself.
 */
export const client = createTraconClient({
  baseUrl: apiBase,
  token: getToken,
  onUnauthorized: rejectToken,
});

/**
 * Unwraps a successful `@tracon/client` response.
 *
 * Safe: `client`'s own middleware throws `TraconError` before a call can
 * resolve with `error` set, so by the time a call site reaches here `data`
 * is always present — this is the one place that invariant is spent.
 */
export async function unwrap<T>(promise: Promise<{ data?: T }>): Promise<T> {
  return (await promise).data as T;
}

interface ProblemDetailsBody {
  title?: string;
  detail?: string;
}

async function toError(response: Response): Promise<TraconError> {
  let title = `HTTP ${response.status}`;
  let detail: string | null = null;

  try {
    const body = (await response.json()) as ProblemDetailsBody;

    title = body.title ?? title;
    detail = body.detail ?? null;
  } catch {
    // A body that is not problem+json leaves the status line as the message.
  }

  return new TraconError(response.status, title, detail);
}

/**
 * Opens a Server-Sent Events stream, carrying auth and abort support.
 *
 * Not part of `@tracon/client`: six operations answer
 * `text/event-stream`, not JSON, and a `fetch`-based typed client reads a
 * response body as JSON (docs/arsiv/fazlar/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md, section 84.4).
 */
/**
 * How long a run's event stream may send NOTHING AT ALL before the console
 * treats it as dead.
 *
 * 🚨 The server sends a `: waiting` keep-alive every `RunEventPollInterval`
 * (250ms by default) for as long as a run is going, so silence this long means
 * the connection died without saying so — a laptop that slept, a proxy that
 * timed out, a network that went away mid-body. None of those throw: the
 * reader simply waits forever, and the screen sat on "Waiting for events…"
 * with no way for the reader to tell it apart from a run still working
 * (HATA-S3-005).
 *
 * The threshold has to clear the server's keep-alive interval with room for a
 * slow link. It is generous on purpose: cutting a healthy run off is worse
 * than noticing a dead one late.
 */
export const STREAM_IDLE_TIMEOUT_MS = 30_000;

export async function openStream(
  path: string,
  init?: RequestInit & { lastEventId?: string },
): Promise<Response> {
  const headers: Record<string, string> = {
    Accept: 'text/event-stream',
    ...authHeaders(),
    ...(init?.headers as Record<string, string> | undefined),
  };

  if (init?.lastEventId !== undefined) {
    headers['Last-Event-ID'] = init.lastEventId;
  }

  const response = await fetch(apiUrl(path), { ...init, headers });

  if (!response.ok) {
    throw await toError(response);
  }

  return response;
}

export { TraconError };
export type { RunEvent, RunEventType } from './run-event';
