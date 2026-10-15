import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fixture, installApiMock } from '../../test/api-fixtures';
import { renderScreen, screen, waitFor, within } from '../../test/render';
import { WorkflowRequestsPanel } from './workflow-requests';

const RUN = '01a12790-0000-7000-8000-000000000001';
const OTHER_RUN = '01a12790-0000-7000-8000-000000000002';
const REPLY_RUN = '01a12790-0000-7000-8000-000000000003';

function request(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    runId: RUN,
    requestId: 'request-1',
    portId: 'approval-port',
    requestType: 'String',
    responseType: 'Boolean',
    prompt: 'Publish the weekly digest?',
    form: 'Boolean',
    requestedAt: '2026-10-14T09:00:00Z',
    workflowName: 'digest',
    sessionId: 'session-1',
    expiresAt: '2026-10-16T09:00:00Z',
    answered: false,
    lastResponseRunId: null,
    ...overrides,
  };
}

const workflows = [
  { name: 'digest', displayName: null, origin: 'Code', version: 1, agentNames: [], nodes: [], parameters: [] },
  { name: 'review', displayName: 'Review', origin: 'Database', version: 2, agentNames: [], nodes: [], parameters: [] },
];

describe('WorkflowRequestsPanel', () => {
  let restoreFetch: () => void;
  const queries: URLSearchParams[] = [];

  // The fixtures carry fixed timestamps and the panel shows them relative to now. With
  // the real clock, a day after the fixtures were written "requested" read "yesterday",
  // and a case that looked for a stray letter in the row failed on every runner.
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date('2026-10-14T12:00:00Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
    restoreFetch();
    queries.length = 0;
  });

  function install(listed: (query: URLSearchParams) => unknown[]): void {
    restoreFetch = installApiMock([
      fixture('GET', 'api/workflows', workflows),
      {
        method: 'GET',
        pattern: 'api/workflows/requests',
        handler: (_params, url) => {
          queries.push(url.searchParams);

          return listed(url.searchParams);
        },
      },
    ]);
  }

  it('lists what the waiting runs wait on, with the time each request runs out and a link to the run card', async () => {
    install(() => [
      request(),
      request({ runId: OTHER_RUN, requestId: 'request-2', workflowName: 'review', expiresAt: null, prompt: 'Approve the plan?' }),
    ]);

    renderScreen(<WorkflowRequestsPanel />);

    const first = await screen.findByTestId('workflow-request-request-1');

    expect(within(first).getByText('Publish the weekly digest?')).toBeTruthy();
    expect(within(first).getByText('Waiting')).toBeTruthy();
    expect(within(first).getByRole('link', { name: 'digest' }).getAttribute('href')).toContain(`workflows/digest?runId=${RUN}`);

    // A request with no limit says so; one with a limit shows when it runs out.
    expect(within(screen.getByTestId('workflow-request-request-2')).getByText('No limit')).toBeTruthy();
    expect(within(first).queryByText('No limit')).toBeNull();
  });

  it('asks for the waiting requests first and follows the state and the workflow filters', async () => {
    install(() => [request()]);

    const user = userEvent.setup();

    renderScreen(<WorkflowRequestsPanel />);

    await screen.findByTestId('workflow-request-request-1');

    expect(queries[0]?.get('state')).toBe('pending');
    expect(queries[0]?.get('take')).toBe('25');
    expect(queries[0]?.get('skip')).toBe('0');
    expect(queries[0]?.has('workflowName')).toBe(false);

    await user.selectOptions(screen.getByTestId('workflow-requests-state'), 'answered');
    await waitFor(() => expect(queries.some((query) => query.get('state') === 'answered')).toBe(true));

    await user.selectOptions(screen.getByTestId('workflow-requests-workflow'), 'review');
    await waitFor(() => expect(queries.some((query) => query.get('workflowName') === 'review')).toBe(true));
  });

  it('marks an answered request and names the run it was answered on', async () => {
    install(() => [request({ answered: true, lastResponseRunId: REPLY_RUN })]);

    renderScreen(<WorkflowRequestsPanel />);

    const row = await screen.findByTestId('workflow-request-request-1');

    expect(within(row).getByText('Answered')).toBeTruthy();
    expect(within(row).queryByText('Waiting')).toBeNull();
    expect(row.textContent).toContain('Answered on run');
  });

  it('shows the start of a long request and leaves the rest to the run card', async () => {
    const long = `${'x'.repeat(140)}${'§'.repeat(60)}`;

    install(() => [request({ prompt: long })]);

    renderScreen(<WorkflowRequestsPanel />);

    const row = await screen.findByTestId('workflow-request-request-1');

    expect(row.textContent).toContain(`${'x'.repeat(140)}…`);
    expect(row.textContent).not.toContain('§');
  });

  it('links a request whose workflow is unknown to its run', async () => {
    install(() => [request({ workflowName: null, prompt: null })]);

    renderScreen(<WorkflowRequestsPanel />);

    const row = await screen.findByTestId('workflow-request-request-1');

    expect(within(row).getByText('No text')).toBeTruthy();
    expect(within(row).getAllByRole('link')[0]?.getAttribute('href')).toContain(`runs/${RUN}`);
  });

  it('says nothing is waiting, and points at the filters when they hide the rest', async () => {
    install(() => []);

    const user = userEvent.setup();

    renderScreen(<WorkflowRequestsPanel />);

    expect(await screen.findByText('No workflow request is waiting')).toBeTruthy();

    await user.selectOptions(screen.getByTestId('workflow-requests-state'), 'all');

    expect(await screen.findByText(/No request matches these filters/)).toBeTruthy();
  });

  it('pages the list with the same size it asks for', async () => {
    const full = Array.from({ length: 25 }, (_, index) => request({ requestId: `request-${index}` }));

    install((query) => (query.get('skip') === '0' ? full : [request({ requestId: 'request-last' })]));

    const user = userEvent.setup();

    renderScreen(<WorkflowRequestsPanel />);

    await screen.findByTestId('workflow-request-request-0');
    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(await screen.findByTestId('workflow-request-request-last')).toBeTruthy();
    expect(queries.some((query) => query.get('skip') === '25')).toBe(true);
  });
});
