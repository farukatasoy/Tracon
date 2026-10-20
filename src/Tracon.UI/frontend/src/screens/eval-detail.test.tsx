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
          expectedToolCalls: [],
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
        expectedToolCalls: [],
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
          expectedToolCalls: [],
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

  describe('expected tool calls', () => {
    const arrange = (expectedToolCalls: unknown[]): { sent: () => unknown } => {
      let sent: unknown;

      restoreFetch = installApiMock([
        fixture('GET', 'api/evals/:name', {
          name: 'support-eval',
          description: null,
          agentName: 'support',
          checks: [{ kind: 'toolCallArgsMatch' }],
        }),
        fixture('GET', 'api/evals/:name/cases', [
          {
            id: '11111111-1111-1111-1111-111111111111',
            suiteId: '22222222-2222-2222-2222-222222222222',
            seq: 0,
            query: 'Where is order ORD-1001?',
            expectedOutput: null,
            expectedToolCalls,
            context: null,
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

      return { sent: () => sent };
    };

    it('sends the stored calls back as they were, with and without arguments', async () => {
      const { sent } = arrange([
        { name: 'get_order_status', arguments: { orderId: 'ORD-1001', verbose: true } },
        { name: 'get_shipping_status', arguments: null },
      ]);

      renderScreen(<EvalSuiteDetailScreen name="support-eval" meta={testMeta} />);

      await userEvent.click(await screen.findByRole('button', { name: 'Save cases' }));
      await expect.poll(sent).toBeDefined();

      expect(sent()).toEqual([
        expect.objectContaining({
          expectedToolCalls: [
            { name: 'get_order_status', arguments: { orderId: 'ORD-1001', verbose: true } },
            { name: 'get_shipping_status' },
          ],
        }),
      ]);
    });

    it('adds a call with a name and a JSON object of arguments', async () => {
      const { sent } = arrange([]);

      renderScreen(<EvalSuiteDetailScreen name="support-eval" meta={testMeta} />);

      await userEvent.click(await screen.findByRole('button', { name: 'Add tool call' }));
      await userEvent.type(screen.getByRole('textbox', { name: /^Tool name/ }), 'get_order_status');
      await userEvent.click(screen.getByRole('textbox', { name: /^Arguments/ }));
      await userEvent.paste('{"orderId": "ORD-1001", "quantity": 2.0, "address": {"city": "Izmir"}}');
      await userEvent.click(screen.getByRole('button', { name: 'Save cases' }));
      await expect.poll(sent).toBeDefined();

      expect(sent()).toEqual([
        expect.objectContaining({
          expectedToolCalls: [
            {
              name: 'get_order_status',
              arguments: { orderId: 'ORD-1001', quantity: 2, address: { city: 'Izmir' } },
            },
          ],
        }),
      ]);
    });

    it('refuses to save arguments that are not a JSON object, and says why', async () => {
      const { sent } = arrange([{ name: 'get_order_status', arguments: null }]);

      renderScreen(<EvalSuiteDetailScreen name="support-eval" meta={testMeta} />);

      const save = await screen.findByRole('button', { name: 'Save cases' });
      const argumentsBox = screen.getByRole('textbox', { name: /^Arguments/ });

      expect(save).toHaveProperty('disabled', false);

      // Half typed: not JSON yet.
      await userEvent.click(argumentsBox);
      await userEvent.paste('{"orderId": ');

      expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'Arguments must be a JSON object.');
      expect(save).toHaveProperty('disabled', true);

      // Valid JSON, but not an object.
      await userEvent.clear(argumentsBox);
      await userEvent.paste('["ORD-1001"]');

      expect(screen.getByRole('alert')).toBeDefined();
      expect(save).toHaveProperty('disabled', true);

      // Back to an object: saves again.
      await userEvent.clear(argumentsBox);
      await userEvent.paste('{"orderId": "ORD-1001"}');

      expect(screen.queryByRole('alert')).toBeNull();
      expect(save).toHaveProperty('disabled', false);

      await userEvent.click(save);
      await expect.poll(sent).toBeDefined();
    });

    it('does not save a call that has no name', async () => {
      arrange([]);

      renderScreen(<EvalSuiteDetailScreen name="support-eval" meta={testMeta} />);

      await userEvent.click(await screen.findByRole('button', { name: 'Add tool call' }));

      expect(screen.getByRole('button', { name: 'Save cases' })).toHaveProperty('disabled', true);

      await userEvent.type(screen.getByRole('textbox', { name: /^Tool name/ }), 'get_order_status');

      expect(screen.getByRole('button', { name: 'Save cases' })).toHaveProperty('disabled', false);
    });

    it('removes a call', async () => {
      const { sent } = arrange([
        { name: 'get_order_status', arguments: null },
        { name: 'refund', arguments: null },
      ]);

      renderScreen(<EvalSuiteDetailScreen name="support-eval" meta={testMeta} />);

      const remove = await screen.findAllByRole('button', { name: 'Remove tool call' });

      await userEvent.click(remove[0]!);
      await userEvent.click(screen.getByRole('button', { name: 'Save cases' }));
      await expect.poll(sent).toBeDefined();

      expect(sent()).toEqual([expect.objectContaining({ expectedToolCalls: [{ name: 'refund' }] })]);
    });
  });
});
