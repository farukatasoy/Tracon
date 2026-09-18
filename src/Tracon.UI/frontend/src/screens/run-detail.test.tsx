import { afterEach, describe, expect, it, vi } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen } from '../test/render';
import { RunDetailScreen } from './run-detail';

const runId = '11111111-1111-1111-1111-111111111111';

function runRecord() {
  return {
    id: runId,
    tenantId: 'default',
    agentName: 'support',
    status: 'Running',
    startedAt: '2026-09-01T10:00:00Z',
    completedAt: null,
    childRunCount: 0,
    parentRunId: null,
  };
}

/**
 * A response whose body never produces another chunk and never closes — the
 * shape a connection takes when it dies without saying so: a laptop sleeping,
 * a proxy timing out, a network that goes away mid-body.
 */
function neverEndingStream(): Response {
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(': waiting\n\n'));
    },
  });

  return new Response(stream, {
    status: 200,
    headers: { 'Content-Type': 'text/event-stream' },
  });
}

describe('RunDetailScreen', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
    vi.useRealTimers();
  });

  it('says so when the event stream goes quiet instead of waiting forever', async () => {
    // 🚨 HATA-S3-005: the reader sat on "Waiting for events…" indefinitely and
    // nothing ever threw, so a dead connection and a run still working looked
    // exactly alike. The only way out was a manual reload.
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', runRecord()),
      {
        method: 'GET',
        pattern: 'api/runs/:runId/events',
        handler: () => neverEndingStream(),
      },
    ]);

    vi.useFakeTimers({ shouldAdvanceTime: true });

    renderScreen(<RunDetailScreen id={runId} />);

    // The screen shows its waiting state first — the defect is that it never
    // left it.
    expect((await screen.findAllByText('Waiting for events…')).length).toBeGreaterThan(0);

    // Just past the console's own STREAM_IDLE_TIMEOUT_MS.
    await vi.advanceTimersByTimeAsync(31_000);

    const alert = await screen.findByRole('alert');

    expect(alert.textContent).toContain('went quiet');
  });

  it('marks a tool call that finished after its timeout was already reported', async () => {
    // 🚨 HATA-S1-025: without the badge this row reads as a plain failure while
    // showing the call's REAL duration — 34s on a tool bounded at 30s — which
    // is the one combination an operator cannot explain.
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', { ...runRecord(), status: 'Completed' }),
      {
        method: 'GET',
        pattern: 'api/runs/:runId/events',
        handler: () => new Response('', { status: 200, headers: { 'Content-Type': 'text/event-stream' } }),
      },
      fixture('GET', 'api/runs/:runId/trace', { spans: [] }),
      fixture('GET', 'api/runs/:runId/tree', []),
      fixture('GET', 'api/runs/:runId/tools', [
        {
          id: '22222222-2222-2222-2222-222222222222',
          runId,
          toolName: 'generate_image',
          toolCallId: 'call-late',
          source: null,
          arguments: null,
          result: 'Images produced. count=1.',
          duration: '00:00:34.0000000',
          error: "Tool 'generate_image' did not complete within 30s.",
          createdAt: '2026-09-01T10:00:30Z',
          usage: { unit: 'images', quantity: 1, cost: 0.04, currency: 'USD', isEstimated: false },
          authorizationDenied: false,
          timedOut: true,
          lateCompletedAt: '2026-09-01T10:00:34Z',
          succeeded: false,
        },
      ]),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    expect(await screen.findByText('finished late')).toBeTruthy();
  });
});
