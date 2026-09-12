import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';
import { installPersistentFetchStub } from './api-fixtures';

/**
 * Node's real `Request` (undici, also what `globalThis.Request` is under
 * jsdom — jsdom itself implements neither `fetch` nor `Request`) rejects a
 * relative URL outright: `new Request('/tracon/api/agents')` throws
 * "Failed to parse URL from …". A browser's `Request`/`fetch` resolve a
 * relative URL against the page instead, which is what `apiBase` (a plain
 * `/tracon/`, no origin — see `lib/base.ts`) relies on at runtime.
 * `openapi-fetch` builds its `Request` internally (`new Request(url, init)`,
 * `openapi-fetch/src/index.js`), so the stub below cannot fix this from
 * outside — every relative request needs a base BEFORE that constructor
 * runs, not just before `fetch` is called.
 */
const RealRequest = globalThis.Request;

class TestRequest extends RealRequest {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    const resolved =
      typeof input === 'string' && !/^[a-z][a-z0-9+.-]*:\/\//i.test(input)
        ? new URL(input, 'http://localhost').toString()
        : input;

    super(resolved as RequestInfo | URL, init);
  }
}

globalThis.Request = TestRequest as unknown as typeof Request;

// Must run before `lib/api.ts` is first imported anywhere in this test file:
// see `installPersistentFetchStub`'s own doc comment for why (same reason as
// the `Request` patch above: both are read once, at client-creation time).
installPersistentFetchStub();

// Vitest has no implicit per-test DOM teardown the way Jest's jsdom
// environment provides one; without this a component left mounted by one
// test is still in the document when the next test's `render()` runs.
afterEach(() => {
  cleanup();
});

// jsdom does not implement `matchMedia` at all. `lib/theme.ts` reads it at
// module load time (`applyTheme` runs on import, the same reason
// `initialiseLocale()` runs before the first render) to pick the starting
// theme — every module that transitively imports it needs this to exist
// before that import happens.
// jsdom does not implement `scrollIntoView` either — used by the playground
// to keep the latest turn in view as it streams in.
if (typeof Element.prototype.scrollIntoView !== 'function') {
  Element.prototype.scrollIntoView = () => {};
}

if (typeof window.matchMedia !== 'function') {
  window.matchMedia = (query: string): MediaQueryList =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList;
}
