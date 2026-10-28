import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, type FixtureRoute } from '../test/api-fixtures';
import { renderScreen, screen, waitFor } from '../test/render';
import { TenantToolPolicySection } from './tenant-tool-policy-panel';

const stamp = '2026-10-29T09:00:00Z';

const registeredTools = [
  { name: 'get_order', description: 'Reads an order.', effect: 'Read', requiresApproval: false, runsOnClient: false, requiredPermission: null },
  { name: 'refund_order', description: 'Refunds an order.', effect: 'Write', requiresApproval: false, runsOnClient: false, requiredPermission: 'orders.refund' },
];

function routes(
  policy: { allowedTools: string[]; grantedPermissions: string[] } | null,
  requirePolicy: boolean,
  extra: FixtureRoute[] = [],
): FixtureRoute[] {
  return [
    ...extra,
    fixture('GET', 'api/tools', registeredTools),
    fixture(
      'GET',
      'api/tenants/:tenantId/tools',
      policy === null
        ? { tenantId: 'acme', allowedTools: null, grantedPermissions: null, updatedAt: null, requirePolicy }
        : { tenantId: 'acme', ...policy, updatedAt: stamp, requirePolicy },
    ),
  ];
}

describe('TenantToolPolicySection', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  // "No policy" and "an empty policy" are opposite states; the text says which one the tenant is in, and
  // when the host requires a policy the absence reads as "no tool runs", not as "every tool is allowed".
  it('says what no policy means, and that it means the opposite when the host requires one', async () => {
    restoreFetch = installApiMock(routes(null, false));

    const view = renderScreen(<TenantToolPolicySection tenantId="acme" />);

    expect((await screen.findByTestId('tenant-tool-policy-state')).textContent).toBe('No policy: every tool is allowed');

    view.unmount();
    restoreFetch();
    restoreFetch = installApiMock(routes(null, true));

    renderScreen(<TenantToolPolicySection tenantId="acme" />);

    await waitFor(() =>
      expect(screen.getByTestId('tenant-tool-policy-state').textContent).toBe('No policy: no tool runs'),
    );
  });

  it('offers the registered tools and their permissions, and saves the two lists', async () => {
    let sent: unknown;
    const user = userEvent.setup();

    restoreFetch = installApiMock(
      routes(null, false, [
        {
          method: 'PUT',
          pattern: 'api/tenants/:tenantId/tools',
          handler: (_params, _url, body) => {
            sent = body;

            return { tenantId: 'acme', allowedTools: ['get_order'], grantedPermissions: ['orders.refund'], updatedAt: stamp, requirePolicy: false };
          },
        },
      ]),
    );

    renderScreen(<TenantToolPolicySection tenantId="acme" />);

    await user.click(await screen.findByTestId('tenant-tool-restrict'));
    await user.click(await screen.findByTestId('tenant-tool-option-get_order'));
    await user.click(screen.getByTestId('tenant-permission-option-orders.refund'));
    await user.click(screen.getByTestId('tenant-tool-save'));

    await waitFor(() => expect(sent).toEqual({ allowedTools: ['get_order'], grantedPermissions: ['orders.refund'] }));
  });

  // A tool of an MCP server and the workspace and knowledge tools are not in the registry listing; they are
  // added by name. The name only selects a tool that exists.
  it('adds a tool the registry does not list by its name', async () => {
    let sent: unknown;
    const user = userEvent.setup();

    restoreFetch = installApiMock(
      routes({ allowedTools: ['get_order'], grantedPermissions: [] }, false, [
        {
          method: 'PUT',
          pattern: 'api/tenants/:tenantId/tools',
          handler: (_params, _url, body) => {
            sent = body;

            return { tenantId: 'acme', allowedTools: ['get_order', 'srv_lookup'], grantedPermissions: [], updatedAt: stamp, requirePolicy: false };
          },
        },
      ]),
    );

    renderScreen(<TenantToolPolicySection tenantId="acme" />);

    await user.type(await screen.findByTestId('tenant-tool-add-name'), '  srv_lookup  ');
    await user.click(screen.getByTestId('tenant-tool-add-name-button'));

    expect(screen.getByTestId('tenant-tool-option-srv_lookup').getAttribute('aria-pressed')).toBe('true');

    await user.click(screen.getByTestId('tenant-tool-save'));

    await waitFor(() => expect(sent).toEqual({ allowedTools: ['get_order', 'srv_lookup'], grantedPermissions: [] }));
  });

  // A saved name no registered tool carries any more was invisible but still sent back on every save: it could
  // never be removed from the console.
  it('keeps a saved name that no tool carries, and lets it be removed', async () => {
    let sent: unknown;
    const user = userEvent.setup();

    restoreFetch = installApiMock(
      routes({ allowedTools: ['get_order', 'retired_tool'], grantedPermissions: ['retired.permission'] }, false, [
        {
          method: 'PUT',
          pattern: 'api/tenants/:tenantId/tools',
          handler: (_params, _url, body) => {
            sent = body;

            return { tenantId: 'acme', allowedTools: ['get_order'], grantedPermissions: [], updatedAt: stamp, requirePolicy: false };
          },
        },
      ]),
    );

    renderScreen(<TenantToolPolicySection tenantId="acme" />);

    const retiredTool = await screen.findByTestId('tenant-tool-option-retired_tool');
    const retiredPermission = screen.getByTestId('tenant-permission-option-retired.permission');

    expect(retiredTool.getAttribute('aria-pressed')).toBe('true');
    expect(retiredPermission.getAttribute('aria-pressed')).toBe('true');

    await user.click(retiredTool);
    await user.click(retiredPermission);
    await user.click(screen.getByTestId('tenant-tool-save'));

    await waitFor(() => expect(sent).toEqual({ allowedTools: ['get_order'], grantedPermissions: [] }));
  });

  it('warns that two empty lists allow no tool and still saves them as a policy', async () => {
    let sent: unknown;
    const user = userEvent.setup();

    restoreFetch = installApiMock(
      routes(null, false, [
        {
          method: 'PUT',
          pattern: 'api/tenants/:tenantId/tools',
          handler: (_params, _url, body) => {
            sent = body;

            return { tenantId: 'acme', allowedTools: [], grantedPermissions: [], updatedAt: stamp, requirePolicy: false };
          },
        },
      ]),
    );

    renderScreen(<TenantToolPolicySection tenantId="acme" />);

    await user.click(await screen.findByTestId('tenant-tool-restrict'));

    expect(screen.getByTestId('tenant-tool-policy-empty').textContent).toContain('allows no tool');

    await user.click(screen.getByTestId('tenant-tool-save'));

    await waitFor(() => expect(sent).toEqual({ allowedTools: [], grantedPermissions: [] }));
  });

  it('clears the saved policy with a DELETE', async () => {
    let deleted = 0;
    const user = userEvent.setup();

    restoreFetch = installApiMock(
      routes({ allowedTools: ['get_order'], grantedPermissions: [] }, false, [
        {
          method: 'DELETE',
          pattern: 'api/tenants/:tenantId/tools',
          handler: () => {
            deleted += 1;

            return {};
          },
        },
      ]),
    );

    renderScreen(<TenantToolPolicySection tenantId="acme" />);

    await user.click(await screen.findByTestId('tenant-tool-clear'));

    await waitFor(() => expect(deleted).toBe(1));
  });
});
