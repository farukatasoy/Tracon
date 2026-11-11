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
    // The very first anonymous probe also gets a 401. If this
    // read back as "rejected", TokenPrompt would show the wrong-token error
    // on a screen the user has not typed anything into yet.
    rejectToken(null);

    expect(getToken()).toBeNull();
    expect(isTokenRejected()).toBe(false);
  });

  it('clears a set token and marks it rejected', () => {
    setToken('wrong-token');

    rejectToken('wrong-token');

    expect(getToken()).toBeNull();
    expect(isTokenRejected()).toBe(true);
  });
});

describe('rejectToken with the token that was sent', () => {
  it('ignores an answer to a request that carried no token once a token is set', () => {
    // The anonymous probe and the sign-in overlap: the 401 for the probe can arrive after the
    // user has typed a token. That answer says nothing about the new token.
    setToken('typed-token');

    rejectToken(null);

    expect(getToken()).toBe('typed-token');
    expect(isTokenRejected()).toBe(false);
  });

  it('ignores an answer to a request that carried an earlier token', () => {
    setToken('first-token');
    setToken('second-token');

    rejectToken('first-token');

    expect(getToken()).toBe('second-token');
    expect(isTokenRejected()).toBe(false);
  });

  it('rejects the token the request carried', () => {
    setToken('wrong-token');

    rejectToken('wrong-token');

    expect(getToken()).toBeNull();
    expect(isTokenRejected()).toBe(true);
  });
});

describe('setToken', () => {
  it('clears a previous rejection when a new token is submitted', () => {
    setToken('wrong-token');
    rejectToken('wrong-token');

    setToken('wrong-token-again');

    expect(isTokenRejected()).toBe(false);
  });

  it('clears a previous rejection on an explicit sign-out', () => {
    setToken('wrong-token');
    rejectToken('wrong-token');

    setToken(null);

    expect(isTokenRejected()).toBe(false);
  });
});
