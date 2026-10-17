import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, testMeta } from '../test/api-fixtures';
import { renderScreen, screen, waitFor } from '../test/render';
import { McpScreen } from './mcp';

/**
 * What the create form SENDS (phase 218). `authorizationConfigurationKey` is
 * gone from the API: the form's key input is written into
 * `headerConfigurationKeys.Authorization`, and an empty input sends no map at
 * all, because an absent map keeps the stored one while `{}` erases it (K-870).
 */
describe('McpScreen create form', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  async function save(fill: (user: ReturnType<typeof userEvent.setup>) => Promise<void>): Promise<Record<string, unknown>> {
    let sent: Record<string, unknown> | undefined;

    restoreFetch = installApiMock([
      fixture('GET', 'api/mcp-servers', []),
      fixture('GET', 'api/approvals/rules', []),
      {
        method: 'PUT',
        pattern: 'api/mcp-servers/:name',
        handler: (_params, _url, body) => {
          sent = body as Record<string, unknown>;
          return {};
        },
      },
    ]);

    const user = userEvent.setup();

    renderScreen(<McpScreen meta={testMeta} />);

    await user.click(await screen.findByRole('button', { name: 'Add server' }));
    await user.type(screen.getByLabelText(/^Name/), 'github');
    await user.type(screen.getByLabelText(/^Endpoint/), 'https://mcp.example.com/mcp');
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(sent).toBeDefined());

    return sent!;
  }

  it('sends the key name as headerConfigurationKeys.Authorization', async () => {
    const body = await save(async (user) => {
      await user.type(screen.getByLabelText(/Authorization configuration key/), 'Tracon:McpSecrets:GithubToken');
    });

    expect(body.headerConfigurationKeys).toStrictEqual({ Authorization: 'Tracon:McpSecrets:GithubToken' });
    expect(body).not.toHaveProperty('authorizationConfigurationKey');
    expect(body).not.toHaveProperty('authorizationKey');
  });

  it('sends no map at all when the key input is empty', async () => {
    const body = await save(async () => {});

    expect(body).not.toHaveProperty('headerConfigurationKeys');
    expect(body).not.toHaveProperty('authorizationConfigurationKey');
    expect(body).not.toHaveProperty('authorizationKey');
  });

  it('clears the key input when OAuth is switched on', async () => {
    const body = await save(async (user) => {
      await user.type(screen.getByLabelText(/Authorization configuration key/), 'Tracon:McpSecrets:GithubToken');
      await user.click(screen.getByLabelText(/OAuth \(Authorization Code\)/));
      await user.type(screen.getByLabelText(/OAuth client ID/), 'tracon-client');
    });

    expect(body.oauthEnabled).toBe(true);
    expect(body).not.toHaveProperty('headerConfigurationKeys');
  });
});
