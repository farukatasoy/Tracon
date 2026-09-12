import { beforeEach, describe, expect, it } from 'vitest';
import { getToken, isTokenRejected, rejectToken, setToken } from './auth';

// `Tracon` is a Node environment (see vitest.config.ts); `sessionStorage`
// writes inside `auth.ts` are wrapped in try/catch and silently no-op here —
// only the in-memory state under test is exercised.

beforeEach(() => {
  // `setToken` always clears `rejected` too, so this is a full reset.
  setToken(null);
});

describe('rejectToken', () => {
  it('is a no-op when there was no token to reject', () => {
    // HATA-S4-001: the very first anonymous probe also gets a 401. If this
    // read back as "rejected", TokenPrompt would show the wrong-token error
    // on a screen the user has not typed anything into yet.
    rejectToken();

    expect(getToken()).toBeNull();
    expect(isTokenRejected()).toBe(false);
  });

  it('clears a set token and marks it rejected', () => {
    setToken('wrong-token');

    rejectToken();

    expect(getToken()).toBeNull();
    expect(isTokenRejected()).toBe(true);
  });
});

describe('setToken', () => {
  it('clears a previous rejection when a new token is submitted', () => {
    setToken('wrong-token');
    rejectToken();

    setToken('wrong-token-again');

    expect(isTokenRejected()).toBe(false);
  });

  it('clears a previous rejection on an explicit sign-out', () => {
    setToken('wrong-token');
    rejectToken();

    setToken(null);

    expect(isTokenRejected()).toBe(false);
  });
});
