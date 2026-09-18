import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import userEvent from '@testing-library/user-event';
import { renderScreen, screen, waitFor } from '../test/render';
import { en } from '../locales/en';
import { FeedbackControl } from './feedback-control';
import type { RunScore } from '../lib/server-types';

const RUN_ID = '01a07a9c-3e9f-7a14-ae03-732eecbb99bc';

function score(overrides: Partial<RunScore>): RunScore {
  return {
    id: crypto.randomUUID(),
    tenantId: 'default',
    runId: RUN_ID,
    messageId: null,
    name: 'overall',
    kind: 'Binary',
    value: 1,
    textValue: null,
    comment: null,
    source: 'human',
    author: 'operator@example',
    createdAt: '2026-09-07T00:00:00Z',
    ...overrides,
  };
}

describe('FeedbackControl', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('does not mistake a judge score for the reviewer own score', async () => {
    // 🚨 A judge score carries no messageId either, and ListAsync guarantees no
    // order. Matching "mine" on messageId alone let the judge's number fill the
    // thumbs and let Remove delete the judge's row.
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId/feedback', [
        score({
          name: 'quality',
          kind: 'Numeric',
          value: 70,
          comment: 'the judge said this',
          source: 'judge:quality',
          author: 'judge:quality',
        }),
      ]),
    ]);

    renderScreen(<FeedbackControl runId={RUN_ID} />);

    // The judge's row is listed as a judge score...
    expect(await screen.findByText('quality')).toBeTruthy();
    expect(screen.getByText('70/100')).toBeTruthy();

    // ...and the control claims NO score of its own. Before the fix `mine`
    // resolved to the judge row, so the byline named the judge and the comment
    // box loaded the judge's text.
    expect(screen.queryByText(/by judge:quality/)).toBeNull();
    expect(screen.getByTestId('feedback-comment')).toHaveProperty('value', '');
  });

  it('lists a score name it does not own as read-only', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId/feedback', [
        score({ name: 'overall', kind: 'Binary', value: 1 }),
        score({ name: 'severity', kind: 'Categorical', value: null, textValue: 'minor' }),
      ]),
    ]);

    renderScreen(<FeedbackControl runId={RUN_ID} />);

    // The categorical score shows its name and its text value...
    expect(await screen.findByText('severity')).toBeTruthy();
    expect(screen.getByText('minor')).toBeTruthy();

    // ...but the control still owns only `overall`, so no second thumb pair appears.
    expect(screen.getAllByTestId('feedback-up')).toHaveLength(1);
  });

  it('removes its own score when a Stars score shares the overall name', async () => {
    // 🚨 A Stars score may carry the same `name` as the Binary one this control
    // owns; the API does not forbid it and `ListAsync` documents no order.
    // Matching without `kind` bound `mine` to the Stars row, whose value (4)
    // never equals 0 or 1, so every click POSTed a NEW Binary row instead of
    // deleting the existing one - the button looked cleared while duplicate
    // rows piled up in run_scores.
    const sent: string[] = [];

    restoreFetch = installApiMock([
      {
        method: 'GET',
        pattern: 'api/runs/:runId/feedback',
        handler: () => [
          score({ id: 'stars-row', name: 'overall', kind: 'Stars', value: 4 }),
          score({ id: 'binary-row', name: 'overall', kind: 'Binary', value: 1 }),
        ],
      },
      {
        method: 'POST',
        pattern: 'api/runs/:runId/feedback',
        handler: () => {
          sent.push('POST');
          return score({});
        },
      },
      {
        method: 'DELETE',
        pattern: 'api/runs/:runId/feedback/:scoreId',
        handler: (params) => {
          sent.push(`DELETE ${params.scoreId}`);
          return null;
        },
        status: 204,
      },
    ]);

    renderScreen(<FeedbackControl runId={RUN_ID} />);

    await userEvent.click(await screen.findByTestId('feedback-up'));

    await waitFor(() => expect(sent).toHaveLength(1));
    expect(sent[0]).toBe('DELETE binary-row');
  });

  it('says there is no judge when the judge run scored nothing', async () => {
    // 🚨 The endpoint answers `{scores,failures}`, not a bare array. The call
    // site asserted `RunScore[]`, so `data.length` was `undefined` and the
    // "no judge is configured" line could never render: the button reported
    // success by changing nothing on screen.
    restoreFetch = installApiMock([
      fixture('GET', 'api/runs/:runId/feedback', []),
      fixture('POST', 'api/runs/:runId/judge', { scores: [], failures: [] }),
    ]);

    renderScreen(<FeedbackControl runId={RUN_ID} />);

    await userEvent.click(await screen.findByTestId('judge-now'));

    expect(await screen.findByText(en['onlineEval.judgeNoJudges'])).toBeTruthy();
  });
});
