import { afterEach, describe, expect, it, vi } from 'vitest';
import { fixture, installApiMock, type FixtureRoute } from '../test/api-fixtures';
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
    // The reader sat on "Waiting for events…" indefinitely and
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
    // Without the badge this row reads as a plain failure while
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

  it('shows the cache write prices a run applied, and only those', async () => {
    // A run that wrote to the prompt cache applied a write price per time-to-live
    // class. The cell appears only for a class that was priced; a run that never wrote shows
    // the three prices it always showed.
    const withWrite = {
      ...runRecord(),
      status: 'Completed',
      cost: {
        inputCost: 0.1,
        inputPricePerMillionTokens: 1000,
        outputCost: 0.02,
        outputPricePerMillionTokens: 2000,
        cachedInputCost: null,
        cachedInputPricePerMillionTokens: null,
        cacheWriteCost: 0.75,
        cacheWritePricePerMillionTokens: 1250,
        cacheWriteLongPricePerMillionTokens: null,
        currency: 'USD',
        source: 'Catalog',
      },
    };

    const common = [
      {
        method: 'GET' as const,
        pattern: 'api/runs/:runId/events',
        handler: () => new Response('', { status: 200, headers: { 'Content-Type': 'text/event-stream' } }),
      },
      fixture('GET', 'api/runs/:runId/trace', { spans: [] }),
      fixture('GET', 'api/runs/:runId/tree', []),
      fixture('GET', 'api/runs/:runId/tools', []),
    ];

    restoreFetch = installApiMock([fixture('GET', 'api/runs/:runId', withWrite), ...common]);

    const first = renderScreen(<RunDetailScreen id={runId} />);

    expect(await screen.findByText('Cache write price (short TTL)')).toBeTruthy();
    expect(screen.getByText('1,250.00 USD / 1M tokens')).toBeTruthy();

    // The long class was not priced: no cell, rather than a permanent "—".
    expect(screen.queryByText('Cache write price (long TTL)')).toBeNull();

    first.unmount();
    restoreFetch();

    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', {
        ...withWrite,
        cost: { ...withWrite.cost, cacheWriteCost: null, cacheWritePricePerMillionTokens: null },
      }),
      ...common,
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    expect(await screen.findByText('Input price')).toBeTruthy();
    expect(screen.queryByText('Cache write price (short TTL)')).toBeNull();
  });

  it('sends a waiting workflow run to the workflow screen opened on that run', async () => {
    // Both links carried only the workflow name, and the workflow
    // screen showed a card only for a run it had started itself. A run started
    // over HTTP, by a job or in another tab could not be answered at all.
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', {
        ...runRecord(),
        kind: 'Workflow',
        agentName: 'approval-flow',
        workflowName: 'approval-flow',
        status: 'AwaitingInput',
      }),
      {
        method: 'GET',
        pattern: 'api/runs/:runId/events',
        handler: () => new Response('', { status: 200, headers: { 'Content-Type': 'text/event-stream' } }),
      },
      fixture('GET', 'api/runs/:runId/trace', { spans: [] }),
      fixture('GET', 'api/runs/:runId/tree', []),
      fixture('GET', 'api/runs/:runId/tools', []),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    const awaitingLink = await screen.findByRole('link', { name: 'workflow screen' });
    const headerLink = screen.getByRole('link', { name: 'approval-flow' });

    for (const link of [awaitingLink, headerLink]) {
      expect(link.getAttribute('href')).toContain(`/workflows/approval-flow?runId=${runId}`);
    }
  });
});

describe('RunDetailScreen answered workflow request', () => {
  let restoreFetch: () => void;

  afterEach(() => restoreFetch());

  it('links the run an answer continues back to the run that waited', async () => {
    const source = '33333333-3333-3333-3333-333333333333';
    const event = {
      runId,
      sequence: 1,
      type: 'WorkflowResponseApplied',
      timestamp: '2026-09-01T10:00:01Z',
      text: 'req-1',
      payload: JSON.stringify({ sourceRunId: source, requestId: 'req-1' }),
    };

    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', { ...runRecord(), kind: 'Workflow', workflowName: 'approval', status: 'Completed' }),
      {
        method: 'GET',
        pattern: 'api/runs/:runId/events',
        handler: () =>
          new Response(`id: 1\nevent: workflow.response-applied\ndata: ${JSON.stringify(event)}\n\n`, {
            status: 200,
            headers: { 'Content-Type': 'text/event-stream' },
          }),
      },
      fixture('GET', 'api/runs/:runId/trace', { spans: [] }),
      fixture('GET', 'api/runs/:runId/tree', []),
      fixture('GET', 'api/runs/:runId/tools', []),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    const link = await screen.findByRole('link', { name: /answers a request of run/ });

    expect(link.getAttribute('href')).toContain(source);
  });
});

describe('RunDetailScreen non-token usage', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  const closedWorkflow = {
    ...runRecord(),
    kind: 'Workflow',
    agentName: 'podcast',
    workflowName: 'podcast',
    status: 'Completed',
    completedAt: '2026-09-01T10:00:30Z',
  };

  const settled: FixtureRoute[] = [
    {
      method: 'GET',
      pattern: 'api/runs/:runId/events',
      handler: () => new Response('', { status: 200, headers: { 'Content-Type': 'text/event-stream' } }),
    },
    fixture('GET', 'api/runs/:runId/trace', { spans: [] }),
    fixture('GET', 'api/runs/:runId/tree', []),
    fixture('GET', 'api/runs/:runId/tools', []),
  ];

  const speech = {
    id: '33333333-3333-3333-3333-333333333333',
    runId,
    nodeName: 'narrate',
    createdAt: '2026-09-01T10:00:10Z',
    usage: {
      unit: 'characters',
      quantity: 1234.5,
      cost: 0.03,
      currency: 'USD',
      isEstimated: true,
      provider: 'elevenlabs',
      model: 'eleven_multilingual_v2',
    },
  };

  const unpriced = {
    id: '44444444-4444-4444-4444-444444444444',
    runId,
    nodeName: 'render',
    createdAt: '2026-09-01T10:00:20Z',
    usage: { unit: 'gpu-seconds', quantity: 30, cost: null, currency: null, isEstimated: false },
  };

  it('lists a function node\'s items with the amount, the estimate flag and who did the work', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', {
        ...closedWorkflow,
        nonTokenCost: { amount: 0.03, currency: 'USD', isEstimated: true, unsummedItemCount: 1 },
        treeNonTokenCost: { amount: 0.03, currency: 'USD', isEstimated: true, unsummedItemCount: 1 },
      }),
      ...settled,
      fixture('GET', 'api/runs/:runId/usage', [speech, unpriced]),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    expect(await screen.findByText('Non-token usage (2)')).toBeTruthy();

    const lines = screen.getAllByTestId('usage-line');

    expect(lines).toHaveLength(2);
    expect(lines[0]?.textContent).toContain('1,234.5 characters');
    expect(lines[0]?.textContent).toContain('0.03 USD');
    expect(lines[0]?.textContent).toContain('estimated');
    expect(lines[0]?.textContent).toContain('elevenlabs / eleven_multilingual_v2');

    // An item with no amount is NOT shown as a price: not "—", and never "0".
    expect(lines[1]?.textContent).toContain('unpriced');
    expect(lines[1]?.textContent).not.toContain('0.00');
  });

  it('shows the run\'s total, the estimate flag and how many items are not in it', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', {
        ...closedWorkflow,
        nonTokenCost: { amount: 0.03, currency: 'USD', isEstimated: true, unsummedItemCount: 1 },
        treeNonTokenCost: { amount: 0.03, currency: 'USD', isEstimated: true, unsummedItemCount: 1 },
      }),
      ...settled,
      fixture('GET', 'api/runs/:runId/usage', [speech, unpriced]),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    const total = await screen.findByTestId('usage-total');

    expect(total.textContent).toContain('Reported in this run');
    expect(total.textContent).toContain('0.03 USD');
    expect(total.textContent).toContain('estimated');
    expect(total.textContent).toContain('1 not added');

    // No run below this one: the tree line would repeat the line above it.
    expect(screen.queryByTestId('usage-tree-total')).toBeNull();
  });

  it('adds the whole tree\'s total on a root that has runs below it', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', {
        ...closedWorkflow,
        childRunCount: 1,
        nonTokenCost: { amount: 1, currency: 'USD', isEstimated: false, unsummedItemCount: 0 },
        treeNonTokenCost: { amount: 1.25, currency: 'USD', isEstimated: false, unsummedItemCount: 0 },
      }),
      ...settled,
      fixture('GET', 'api/runs/:runId/usage', [speech]),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    const tree = await screen.findByTestId('usage-tree-total');

    expect(tree.textContent).toContain('Reported in the whole tree');
    expect(tree.textContent).toContain('1.25 USD');
  });

  it('says the tree is in mixed currencies instead of showing a sum across them', async () => {
    // The server leaves a tree in two currencies without an amount; "unpriced"
    // would say nothing could be priced, which is not what happened.
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', {
        ...closedWorkflow,
        childRunCount: 1,
        nonTokenCost: { amount: 1, currency: 'USD', currencyCount: 1, isEstimated: false, unsummedItemCount: 0 },
        treeNonTokenCost: { amount: null, currency: null, currencyCount: 2, isEstimated: false, unsummedItemCount: 0 },
      }),
      ...settled,
      fixture('GET', 'api/runs/:runId/usage', [speech]),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    const tree = await screen.findByTestId('usage-tree-total');

    expect(tree.textContent).toContain('mixed currencies');
    expect(tree.textContent).not.toContain('unpriced');
  });

  it('draws no panel for a run that reported nothing', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', {
        ...closedWorkflow,
        nonTokenCost: null,
        treeNonTokenCost: null,
      }),
      ...settled,
      fixture('GET', 'api/runs/:runId/usage', []),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    // The run has settled: its tool panel is the signal that the screen finished loading.
    expect(await screen.findByText('No tool call')).toBeTruthy();
    expect(screen.queryByText(/Non-token usage/)).toBeNull();
  });

  it('shows a tool call\'s own measurement on its row, with the same line as a node item', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId', { ...runRecord(), status: 'Completed', completedAt: '2026-09-01T10:00:30Z' }),
      {
        method: 'GET',
        pattern: 'api/runs/:runId/events',
        handler: () => new Response('', { status: 200, headers: { 'Content-Type': 'text/event-stream' } }),
      },
      fixture('GET', 'api/runs/:runId/trace', { spans: [] }),
      fixture('GET', 'api/runs/:runId/tree', []),
      fixture('GET', 'api/runs/:runId/usage', []),
      fixture('GET', 'api/runs/:runId/tools', [
        {
          id: '55555555-5555-5555-5555-555555555555',
          runId,
          toolName: 'speak',
          toolCallId: 'call-speak',
          source: null,
          arguments: null,
          result: 'Audio produced.',
          duration: null,
          error: null,
          createdAt: '2026-09-01T10:00:10Z',
          usage: {
            unit: 'characters',
            quantity: 120,
            cost: null,
            currency: null,
            isEstimated: true,
            provider: 'elevenlabs',
            model: null,
          },
          authorizationDenied: false,
          timedOut: false,
          lateCompletedAt: null,
          succeeded: true,
        },
      ]),
    ]);

    renderScreen(<RunDetailScreen id={runId} />);

    const line = await screen.findByTestId('usage-line');

    expect(line.textContent).toContain('120 characters');
    expect(line.textContent).toContain('unpriced');
    expect(line.textContent).toContain('estimated');
    expect(line.textContent).toContain('elevenlabs');
  });
});
