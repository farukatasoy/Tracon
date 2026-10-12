import { afterEach, describe, expect, it } from 'vitest';
import userEvent from '@testing-library/user-event';
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

  it('sends each kept case back with its id and its parameter values', async () => {
    // A case holds its id for life and the run-to-run diff behind --baseline
    // is matched on it. This screen loaded the cases and sent them back
    // WITHOUT the id and without their parameter values, so one save re-created
    // every case: an unchanged case then read as one removed and another
    // added, and a parameterized agent's cases lost the values they need to
    // run at all.
    let sent: unknown;

    restoreFetch = installApiMock([
      fixture('GET', 'api/evals/:name', {
        name: 'support-eval',
        description: null,
        agentName: 'support',
        checks: [],
      }),
      fixture('GET', 'api/evals/:name/cases', [
        {
          id: '11111111-1111-1111-1111-111111111111',
          suiteId: '22222222-2222-2222-2222-222222222222',
          seq: 0,
          query: 'Where is my order?',
          expectedOutput: 'shipped',
          expectedTools: [],
          context: null,
          parameters: { customer: 'Acme' },
        },
      ]),
      fixture('GET', 'api/evals/:name/runs', []),
      {
        method: 'PUT',
        pattern: 'api/evals/:name/cases',
        handler: (_params, _url, body) => {
          sent = body;
          return [];
        },
      },
    ]);

    renderScreen(<EvalSuiteDetailScreen name="support-eval" meta={testMeta} />);

    await userEvent.click(await screen.findByRole('button', { name: 'Save cases' }));

    await expect.poll(() => sent).toBeDefined();

    expect(sent).toEqual([
      {
        id: '11111111-1111-1111-1111-111111111111',
        query: 'Where is my order?',
        expectedOutput: 'shipped',
        expectedTools: [],
        context: null,
        parameters: { customer: 'Acme' },
      },
    ]);
  });

  it('keeps the line breaks of a promoted context the operator edits (F-343)', async () => {
    // A promoted case stores the run's documents in `context` as document
    // blocks, one per document, and each block goes back to the agent as its
    // own document. A single-line input drops line breaks the moment the
    // operator edits it, which flattened the blocks into one document.
    const promoted =
      '-----BEGIN TRACON DOCUMENT-----\nName: policy.md\n\nRefunds within 30 days.\n-----END TRACON DOCUMENT-----';
    let sent: unknown;

    restoreFetch = installApiMock([
      fixture('GET', 'api/evals/:name', {
        name: 'support-eval',
        description: null,
        agentName: 'support',
        checks: [],
      }),
      fixture('GET', 'api/evals/:name/cases', [
        {
          id: '11111111-1111-1111-1111-111111111111',
          suiteId: '22222222-2222-2222-2222-222222222222',
          seq: 0,
          query: 'Summarize the attached policy.',
          expectedOutput: null,
          expectedTools: [],
          context: promoted,
          parameters: null,
        },
      ]),
      fixture('GET', 'api/evals/:name/runs', []),
      {
        method: 'PUT',
        pattern: 'api/evals/:name/cases',
        handler: (_params, _url, body) => {
          sent = body;
          return [];
        },
      },
    ]);

    renderScreen(<EvalSuiteDetailScreen name="support-eval" meta={testMeta} />);

    const context = await screen.findByRole('textbox', { name: /^Context/ });
    await userEvent.type(context, ' ');
    await userEvent.click(screen.getByRole('button', { name: 'Save cases' }));

    await expect.poll(() => sent).toBeDefined();

    expect(sent).toEqual([expect.objectContaining({ context: `${promoted} ` })]);
  });
});
