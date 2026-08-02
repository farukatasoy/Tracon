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

function read(): string | null {
  try {
    return window.sessionStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

export function getToken(): string | null {
  return token;
}

export function setToken(value: string | null): void {
  token = value && value.length > 0 ? value : null;

  try {
    if (token === null) {
      window.sessionStorage.removeItem(STORAGE_KEY);
    } else {
      window.sessionStorage.setItem(STORAGE_KEY, token);
    }
  } catch {
    // Storage may be unavailable; the in-memory value still works for this tab.
  }

  for (const listener of listeners) {
    listener();
  }
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

/** Authorization header for the current token, or an empty object. */
export function authHeaders(): Record<string, string> {
  return token === null ? {} : { Authorization: `Bearer ${token}` };
}
