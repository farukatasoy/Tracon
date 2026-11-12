import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen, waitFor } from '../test/render';
import { WebhookPanel } from './webhook-panel';

describe('WebhookPanel creating a subscription', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  // A subscription sends data out of the installation, so a new one is saved switched off unless the person says
  // otherwise: the server's save request defaults to off, and the form no longer sends `true` on its own.
  it('opens switched off and saves what the checkbox says', async () => {
    const bodies: unknown[] = [];

    restoreFetch = installApiMock([
      fixture('GET', 'api/webhooks', []),
      {
        method: 'PUT',
        pattern: 'api/webhooks/:name',
        handler: (_params, _url, body) => {
          bodies.push(body);

          return {};
        },
      },
    ]);

    renderScreen(<WebhookPanel canAdminister />);

    await userEvent.click(await screen.findByRole('button', { name: 'Add webhook' }));

    const enabled = screen.getByTestId('webhook-enabled') as HTMLInputElement;

    expect(enabled.checked).toBe(false);

    await userEvent.type(screen.getByTestId('webhook-name'), 'orders');
    await userEvent.type(screen.getByTestId('webhook-url'), 'https://example.com/hook');
    await userEvent.click(screen.getByTestId('webhook-save'));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ enabled: false });

    await userEvent.click(await screen.findByRole('button', { name: 'Add webhook' }));
    await userEvent.click(screen.getByTestId('webhook-enabled'));
    await userEvent.type(screen.getByTestId('webhook-name'), 'billing');
    await userEvent.type(screen.getByTestId('webhook-url'), 'https://example.com/billing');
    await userEvent.click(screen.getByTestId('webhook-save'));

    await waitFor(() => expect(bodies).toHaveLength(2));
    expect(bodies[1]).toMatchObject({ enabled: true });
  });
});
