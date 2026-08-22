import { describe, expect, it } from 'vitest';
import { DEFAULT_PREFIX, stripPrefix } from '../scripts/strip-prefix.mjs';
import { createAgentPrismClient } from '../src/client.js';

function jsonResponse(body: unknown, init?: ResponseInit): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });
}

describe('strip-prefix (build-time)', () => {
  it('strips the default /agentprism prefix from every path', () => {
    const document = stripPrefix({ paths: { '/agentprism/api/agents': {} } });

    expect(document.paths).toEqual({ '/api/agents': {} });
  });

  it('strips a custom prefix', () => {
    const document = stripPrefix({ paths: { '/control/api/agents': {} } }, '/control');

    expect(document.paths).toEqual({ '/api/agents': {} });
  });

  it('maps a path equal to the prefix itself to "/"', () => {
    const document = stripPrefix({ paths: { [DEFAULT_PREFIX]: {} } });

    expect(document.paths).toEqual({ '/': {} });
  });

  it('throws when a path does not start with the expected prefix', () => {
    expect(() => stripPrefix({ paths: { '/other/api/agents': {} } })).toThrow(/does not start with/u);
  });
});

describe('AgentPrismClientOptions.baseUrl (section 84.2 — MapAgentPrism prefix)', () => {
  it('reaches the default /agentprism prefix', async () => {
    let requestUrl: string | undefined;
    const client = createAgentPrismClient({
      baseUrl: 'https://example.test/agentprism',
      fetch: async (request) => {
        requestUrl = request.url;
        return jsonResponse([]);
      },
    });

    await client.GET('/api/agents');

    expect(requestUrl).toBe('https://example.test/agentprism/api/agents');
  });

  it('reaches a custom prefix set via MapAgentPrism("/control")', async () => {
    let requestUrl: string | undefined;
    const client = createAgentPrismClient({
      baseUrl: 'https://example.test/control',
      fetch: async (request) => {
        requestUrl = request.url;
        return jsonResponse([]);
      },
    });

    await client.GET('/api/agents');

    expect(requestUrl).toBe('https://example.test/control/api/agents');
  });

  it('behaves the same whether baseUrl carries a trailing slash or not', async () => {
    const urls: string[] = [];
    const fetch = async (request: Request): Promise<Response> => {
      urls.push(request.url);
      return jsonResponse([]);
    };

    await createAgentPrismClient({ baseUrl: 'https://example.test/agentprism', fetch }).GET('/api/agents');
    await createAgentPrismClient({ baseUrl: 'https://example.test/agentprism/', fetch }).GET('/api/agents');

    expect(urls[0]).toBe(urls[1]);
  });
});
