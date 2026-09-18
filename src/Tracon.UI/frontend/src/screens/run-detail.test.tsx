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
});
