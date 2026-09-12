import { describe, expect, it, vi } from 'vitest';
import { TraconError } from '../src/error.js';
import { createTraconClient } from '../src/client.js';

function okResponse(): Response {
  return new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

describe('token', () => {
  it('sends a static token as a bearer header', async () => {
    let authorization: string | null = null;
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      token: 'secret-token',
      fetch: async (request) => {
        authorization = request.headers.get('Authorization');
        return okResponse();
      },
    });

    await client.GET('/api/agents');

    expect(authorization).toBe('Bearer secret-token');
  });

  it('calls a token function per request', async () => {
    let calls = 0;
    const headers: (string | null)[] = [];
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      token: () => `token-${++calls}`,
      fetch: async (request) => {
        headers.push(request.headers.get('Authorization'));
        return okResponse();
      },
    });

    await client.GET('/api/agents');
    await client.GET('/api/agents');

    expect(headers).toEqual(['Bearer token-1', 'Bearer token-2']);
  });

  it('sends no Authorization header when no token is configured', async () => {
    let authorization: string | null = 'unset';
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async (request) => {
        authorization = request.headers.get('Authorization');
        return okResponse();
      },
    });

    await client.GET('/api/agents');

    expect(authorization).toBeNull();
  });

  it('sends no Authorization header when the token function returns null', async () => {
    let authorization: string | null = 'unset';
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      token: () => null,
      fetch: async (request) => {
        authorization = request.headers.get('Authorization');
        return okResponse();
      },
    });

    await client.GET('/api/agents');

    expect(authorization).toBeNull();
  });
});

describe('onUnauthorized', () => {
  it('is called on a 401 response, which still rejects with TraconError', async () => {
    const onUnauthorized = vi.fn();
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      onUnauthorized,
      fetch: async () =>
        new Response(JSON.stringify({ title: 'Unauthorized' }), {
          status: 401,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    });

    await expect(client.GET('/api/agents')).rejects.toBeInstanceOf(TraconError);
    expect(onUnauthorized).toHaveBeenCalledOnce();
  });

  it('is not called on a non-401 error', async () => {
    const onUnauthorized = vi.fn();
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      onUnauthorized,
      fetch: async () => new Response(null, { status: 500 }),
    });

    await expect(client.GET('/api/agents')).rejects.toBeInstanceOf(TraconError);
    expect(onUnauthorized).not.toHaveBeenCalled();
  });
});

describe('cancellation', () => {
  it('propagates an AbortSignal through to the underlying fetch', async () => {
    const controller = new AbortController();
    let observedSignal: AbortSignal | null | undefined;

    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async (request) => {
        observedSignal = request.signal;
        return okResponse();
      },
    });

    await client.GET('/api/agents', { signal: controller.signal });

    expect(observedSignal?.aborted).toBe(false);
    controller.abort();
    expect(observedSignal?.aborted).toBe(true);
  });

  it('rejects when the signal is already aborted', async () => {
    const controller = new AbortController();
    controller.abort();

    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async (request) => {
        if (request.signal.aborted) {
          throw new DOMException('The operation was aborted.', 'AbortError');
        }
        return okResponse();
      },
    });

    await expect(client.GET('/api/agents', { signal: controller.signal })).rejects.toMatchObject({
      name: 'AbortError',
    });
  });
});

describe('subsystem failure', () => {
  it('propagates a network failure as-is, not as an TraconError', async () => {
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async () => {
        throw new TypeError('fetch failed');
      },
    });

    await expect(client.GET('/api/agents')).rejects.toBeInstanceOf(TypeError);
  });
});
