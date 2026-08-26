import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, testMeta } from '../test/api-fixtures';
import { renderScreen, screen } from '../test/render';
import { EvalSuiteDetailScreen } from './eval-detail';

function pendingJsonResponse(): Response {
  return new Response(new ReadableStream(), {
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('EvalSuiteDetailScreen', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('does not allow a new case while the initial cases load (F-130)', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/evals/:name', {
        name: 'support-eval',
        description: null,
        agentName: 'support',
        checks: [],
      }),
      {
        method: 'GET',
        pattern: 'api/evals/:name/cases',
        handler: () => pendingJsonResponse(),
      },
      fixture('GET', 'api/evals/:name/runs', []),
    ]);

    renderScreen(<EvalSuiteDetailScreen name="support-eval" meta={testMeta} />);

    const addCase = await screen.findByRole('button', { name: 'Add case' });

    expect(addCase).toHaveProperty('disabled', true);
  });
});
