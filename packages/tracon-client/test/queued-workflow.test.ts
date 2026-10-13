import { describe, expect, it } from 'vitest';
import { createTraconClient } from '../src/client.js';
import type { components } from '../src/schema.js';

type AcceptedRunResponse = components['schemas']['AcceptedRunResponse'];

/**
 * A workflow run, resume or response sent with `Prefer: respond-async` is queued and
 * answered with `202 Accepted`. The generated schema must carry that body, or a caller
 * of the typed client has no run id to follow.
 */
describe('queued workflow operations', () => {
  const runId = '01a1340e-ad31-75e4-b64b-e3d0a26ab0a1';

  function accepted(): Response {
    const body: AcceptedRunResponse = {
      runId,
      jobId: runId,
      location: `/tracon/api/runs/${runId}`,
      eventsLocation: `/tracon/api/runs/${runId}/events`,
    };

    return new Response(JSON.stringify(body), {
      status: 202,
      headers: { 'Content-Type': 'application/json', Location: body.location, 'Preference-Applied': 'respond-async' },
    });
  }

  it('reads the 202 body of a queued run as the accepted-run shape', async () => {
    let prefer: string | null = null;
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async (request) => {
        prefer = request.headers.get('Prefer');
        return accepted();
      },
    });

    const { data, response } = await client.POST('/api/workflows/{name}/run', {
      params: { path: { name: 'podcast' } },
      headers: { Prefer: 'respond-async', 'Idempotency-Key': 'episode-12' },
      body: { message: 'Episode 12', sessionId: 'episode-12' },
    });

    expect(prefer).toBe('respond-async');
    expect(response.status).toBe(202);
    expect((data as AcceptedRunResponse).runId).toBe(runId);
    expect((data as AcceptedRunResponse).eventsLocation).toBe(`/tracon/api/runs/${runId}/events`);
  });

  it('reads the 202 body of a queued resume and a queued response the same way', async () => {
    const client = createTraconClient({ baseUrl: 'https://example.test/tracon', fetch: async () => accepted() });

    const resumed = await client.POST('/api/workflows/runs/{runId}/resume', {
      params: { path: { runId } },
      headers: { Prefer: 'respond-async' },
      body: {},
    });

    const answered = await client.POST('/api/workflows/runs/{runId}/respond', {
      params: { path: { runId } },
      headers: { Prefer: 'respond-async' },
      body: { requestId: 'gate', approved: true },
    });

    expect((resumed.data as AcceptedRunResponse).jobId).toBe(runId);
    expect((answered.data as AcceptedRunResponse).jobId).toBe(runId);
  });

  it('passes the continuation filter of the run list as a query parameter', async () => {
    let url = '';
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async (request) => {
        url = request.url;
        return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } });
      },
    });

    await client.GET('/api/runs', { params: { query: { continuedFromRunId: runId } } });

    expect(url).toContain(`continuedFromRunId=${runId}`);
  });
});
