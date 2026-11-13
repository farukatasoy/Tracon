import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen, within } from '../test/render';
import { SessionDetailScreen } from './session-detail';

const sessionId = 'session-1';

function session(messages: unknown[]) {
  return {
    id: sessionId,
    agentName: 'looper',
    tenantId: 'default',
    createdAt: '2026-11-13T10:00:00Z',
    updatedAt: '2026-11-13T10:01:00Z',
    messages,
    state: {},
  };
}

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
});
