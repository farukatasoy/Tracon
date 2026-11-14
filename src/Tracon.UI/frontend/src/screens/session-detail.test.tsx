import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen, within } from '../test/render';
import { SessionDetailScreen } from './session-detail';

const sessionId = 'session-1';

function session(messages: unknown[], pendingRequestIds?: string[]) {
  return {
    id: sessionId,
    agentName: 'looper',
    tenantId: 'default',
    createdAt: '2026-11-13T10:00:00Z',
    updatedAt: '2026-11-13T10:01:00Z',
    messages,
    pendingRequestIds,
    state: {},
  };
}

const approvalRequest = {
  $type: 'toolApprovalRequest',
  requestId: 'req-1',
  toolCall: { $type: 'functionCall', callId: 'c1', name: 'cancel_order', arguments: { orderId: 'ORD-7' } },
};

describe('SessionDetailScreen', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('keeps the feedback a loop gave the model in a message of its own, apart from the answers around it', async () => {
    // The history of a looping agent holds the feedback as a `user` message between two answers, so the screen
    // already shows it apart (measured on a real run). This holds that: it must not fold into the next answer.
    restoreFetch = installApiMock([
      fixture(
        'GET',
        'api/sessions/:sessionId',
        session([
          { role: 'user', contents: [{ $type: 'text', text: 'write the episode' }] },
          { role: 'assistant', contents: [{ $type: 'text', text: 'first draft' }] },
          { role: 'user', contents: [{ $type: 'text', text: 'Reviewer findings (round 1): add a heading' }] },
          { role: 'assistant', contents: [{ $type: 'text', text: 'final answer' }] },
        ]),
      ),
      fixture('GET', 'api/runs', []),
    ]);

    renderScreen(<SessionDetailScreen id={sessionId} />);

    // Exact: the answer is a paragraph of its own.
    const answer = await screen.findByText('final answer');
    const feedback = screen.getByText('Reviewer findings (round 1): add a heading');

    expect(feedback).not.toBe(answer);

    // Each text sits in the block of its own message, and the feedback's block says `user`.
    const answerBlock = answer.closest('div.px-4.py-3') as HTMLElement;
    const feedbackBlock = feedback.closest('div.px-4.py-3') as HTMLElement;

    expect(answerBlock).not.toBe(feedbackBlock);
    expect(within(feedbackBlock).getByText('user')).toBeTruthy();
    expect(within(answerBlock).getByText('assistant')).toBeTruthy();
  });
  it('shows an approval the session no longer waits for as answered, and one it still waits for as waiting', async () => {
    // A run applied the answer and its caller left before the run saved the session: the history holds the request and no
    // answer, and the session reports the request is no longer open. The card must not ask again for a decision that was made.
    restoreFetch = installApiMock([
      fixture('GET', 'api/sessions/:sessionId', session([{ role: 'assistant', contents: [approvalRequest] }], [])),
      fixture('GET', 'api/runs', []),
    ]);

    renderScreen(<SessionDetailScreen id={sessionId} />);

    const card = await screen.findByTestId('approval-card');

    expect(within(card).getByText('answered')).toBeTruthy();
    expect(within(card).queryByText('approval required')).toBeNull();
  });

  it('keeps an approval the session still waits for as waiting', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/sessions/:sessionId', session([{ role: 'assistant', contents: [approvalRequest] }], ['req-1'])),
      fixture('GET', 'api/runs', []),
    ]);

    renderScreen(<SessionDetailScreen id={sessionId} />);

    const card = await screen.findByTestId('approval-card');

    expect(within(card).getByText('approval required')).toBeTruthy();
  });
});
