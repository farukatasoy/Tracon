import { describe, expect, it } from 'vitest';
import { TraconError } from '../src/error.js';
import { createTraconClient } from '../src/client.js';

function client(fetch: (request: Request) => Promise<Response>) {
  return createTraconClient({ baseUrl: 'https://example.test/tracon', fetch });
}

describe('problem+json -> TraconError (section 84.5)', () => {
  it('carries title and detail verbatim (K-232 — not translated)', async () => {
    const c = client(
      async () =>
        new Response(JSON.stringify({ title: 'Name already in use', detail: "'echo' is taken." }), {
          status: 409,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    );

    await expect(c.GET('/api/agents/{name}', { params: { path: { name: 'echo' } } })).rejects.toMatchObject({
      status: 409,
      title: 'Name already in use',
      detail: "'echo' is taken.",
    });
  });

  it('falls back to a status-line title when only title is present', async () => {
    const c = client(
      async () =>
        new Response(JSON.stringify({ title: 'Not found' }), {
          status: 404,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    );

    let caught: unknown;

    try {
      await c.GET('/api/agents/{name}', { params: { path: { name: 'missing' } } });
    } catch (error) {
      caught = error;
    }

    expect(caught).toBeInstanceOf(TraconError);
    expect((caught as TraconError).title).toBe('Not found');
    expect((caught as TraconError).detail).toBeNull();
  });

  it('falls back to "HTTP <status>" when the body is not JSON', async () => {
    const c = client(async () => new Response('<html>Bad Gateway</html>', { status: 502 }));

    await expect(c.GET('/api/agents')).rejects.toMatchObject({
      status: 502,
      title: 'HTTP 502',
      detail: null,
    });
  });

  it('does not throw for a successful response', async () => {
    const c = client(
      async () => new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    );

    const { data, error } = await c.GET('/api/agents');

    expect(error).toBeUndefined();
    expect(data).toEqual([]);
  });
});
