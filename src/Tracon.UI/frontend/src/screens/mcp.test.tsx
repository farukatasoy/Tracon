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

  /** A stored server the way the list returns it: key names only, never a value. */
  const stored = (name: string, headerConfigurationKeys: Record<string, string>) => ({
    id: '01a14729-a429-7415-9d8c-0415a1de8dd8',
    tenantId: 'default',
    name,
    description: null,
    endpoint: 'https://mcp.example.com/mcp',
    transport: 'StreamableHttp',
    headers: {},
    headerConfigurationKeys,
    enabled: true,
    oauthEnabled: false,
    oauthClientId: null,
    oauthClientSecretConfigurationKey: null,
    oauthScopes: null,
    oauthAuthorizationMode: 'AuthorizationCode',
    requiresApproval: true,
    createdAt: '2026-10-17T00:00:00Z',
    updatedAt: '2026-10-17T00:00:00Z',
  });

  async function save(
    fill: (user: ReturnType<typeof userEvent.setup>) => Promise<void>,
    existing: unknown[] = [],
  ): Promise<Record<string, unknown>> {
    let sent: Record<string, unknown> | undefined;

    restoreFetch = installApiMock([
      fixture('GET', 'api/mcp-servers', existing),
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

  it('keeps the other credential headers of a server saved under the same name', async () => {
    // 🚨 The form saves by name and a filled map REPLACES the stored one: sending
    // only `Authorization` would silently erase `X-Api-Key` and the next call
    // would fail with 401. The list carries the stored key names, so they ride along.
    const body = await save(
      async (user) => {
        await user.type(screen.getByLabelText(/Authorization configuration key/), 'Tracon:McpSecrets:NewToken');
      },
      [
        stored('github', {
          'X-Api-Key': 'Tracon:McpSecrets:SearchKey',
          // Another letter case of the same header: the server refuses both as a duplicate.
          authorization: 'Tracon:McpSecrets:OldToken',
        }),
      ],
    );

    expect(body.headerConfigurationKeys).toStrictEqual({
      'X-Api-Key': 'Tracon:McpSecrets:SearchKey',
      Authorization: 'Tracon:McpSecrets:NewToken',
    });
  });

  it('does not touch another server\'s headers', async () => {
    const body = await save(
      async (user) => {
        await user.type(screen.getByLabelText(/Authorization configuration key/), 'Tracon:McpSecrets:NewToken');
      },
      [stored('another', { 'X-Api-Key': 'Tracon:McpSecrets:SearchKey' })],
    );

    expect(body.headerConfigurationKeys).toStrictEqual({ Authorization: 'Tracon:McpSecrets:NewToken' });
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
