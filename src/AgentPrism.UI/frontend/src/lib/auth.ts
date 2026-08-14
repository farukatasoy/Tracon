import { useSyncExternalStore } from 'react';

/**
 * Bearer token store.
 *
 * The token lives in `sessionStorage`, not `localStorage`: it is a secret, and
 * limiting it to the lifetime of the tab keeps it off disk for as short a time
 * as possible. The cost is retyping it in a new tab, which is acceptable for a
 * deployment that has deliberately enabled token auth.
 *
 * The token is never logged, never placed in a URL and never sent anywhere
 * except the `Authorization` header of same-origin requests.
 */
const STORAGE_KEY = 'agentprism.token';

const listeners = new Set<() => void>();

let token: string | null = read();

// Set only by `rejectToken()`, and only when a token was actually attempted:
// distinguishes "the server rejected this token" from "no token was ever set",
// which `token === null` alone cannot do once the rejected value is cleared.
let rejected = false;

function read(): string | null {
  try {
    return window.sessionStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function write(value: string | null): void {
  try {
    if (value === null) {
      window.sessionStorage.removeItem(STORAGE_KEY);
    } else {
      window.sessionStorage.setItem(STORAGE_KEY, value);
    }
  } catch {
    // Storage may be unavailable; the in-memory value still works for this tab.
  }
}

function notify(): void {
  for (const listener of listeners) {
    listener();
  }
}

export function getToken(): string | null {
  return token;
}

export function isTokenRejected(): boolean {
  return rejected;
}

export function setToken(value: string | null): void {
  token = value && value.length > 0 ? value : null;
  rejected = false;
  write(token);
  notify();
}

/**
 * Clears a token the server just answered with 401, marking it as rejected
 * rather than merely absent.
 *
 * A no-op when there was no token to reject: the very first anonymous probe
 * also gets a 401, and that case must not read back as "your token was
 * wrong" ({@link useTokenRejected}).
 */
export function rejectToken(): void {
  if (token === null) {
    return;
  }

  token = null;
  rejected = true;
  write(null);
  notify();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);

  return () => {
    listeners.delete(listener);
  };
}

export function useToken(): string | null {
  return useSyncExternalStore(subscribe, getToken, () => null);
}

/** Whether the last token that was set was subsequently rejected by the server. */
export function useTokenRejected(): boolean {
  return useSyncExternalStore(subscribe, isTokenRejected, () => false);
}

/** Authorization header for the current token, or an empty object. */
export function authHeaders(): Record<string, string> {
  return token === null ? {} : { Authorization: `Bearer ${token}` };
}
