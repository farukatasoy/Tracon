import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen } from '../test/render';
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
});
