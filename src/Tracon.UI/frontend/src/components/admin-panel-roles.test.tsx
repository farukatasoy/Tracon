import { afterEach, describe, expect, it } from 'vitest';
import type { ReactElement } from 'react';
import { installApiMock, type FixtureRoute } from '../test/api-fixtures';
import { renderScreen, screen, waitFor } from '../test/render';
import { ApiKeyPanel } from './api-key-panel';
import { RetentionPanel } from './retention-panel';
import { TenantProviderPanel } from './tenant-provider-panel';
import { WebhookPanel } from './webhook-panel';

/**
 * A reader opening an administrator-only panel must be told the panel is not
 * theirs — not handed the raw transport failure.
 *
 * 🚨 Measured in a live console (`MT-UIRUN-063`, 2026-09-19), not imagined:
 * with `Tracon:Demo:Roles:Enabled` on and the reader role, the settings screen
 * showed five panels reading `HTTP 403` over a `Try again` button. Retrying can
 * never succeed — the role is the reason — so the button offers the one action
 * that cannot work, and the panel reads as "the console is broken" rather than
 * "this panel is not yours". `diagnostics.tsx` and the MCP prompts tab had
 * already closed this exact gap for themselves, each with a comment saying so;
 * these panels never got the same treatment.
 *
 * The strongest assertion here is NOT the rendered text but that the request is
 * never SENT. Deleting the `enabled:` guard alone brings the whole defect back
 * while any text-only assertion keeps passing, because the 403 then arrives and
 * is rendered by the same `ErrorNote` branch that is still in the file for a
 * genuine transport failure.
 */

/** Records every call, and answers the way the server answers a reader. */
function forbidden(pattern: string, seen: string[]): FixtureRoute {
  return {
    method: 'GET',
    pattern,
    status: 403,
    handler: () => {
      seen.push(pattern);

      return { title: 'Forbidden', status: 403 };
    },
  };
}

interface Case {
  readonly name: string;
  readonly render: () => ReactElement;
  /** Every administrator-only endpoint the panel reads. */
  readonly endpoints: readonly string[];
}

const CASES: readonly Case[] = [
  {
    name: 'WebhookPanel',
    render: () => <WebhookPanel canAdminister={false} />,
    endpoints: ['api/webhooks'],
  },
  {
    name: 'ApiKeyPanel',
    render: () => <ApiKeyPanel canAdminister={false} />,
    endpoints: ['api/api-keys'],
  },
  {
    name: 'RetentionPanel',
    render: () => <RetentionPanel canAdminister={false} />,
    endpoints: ['api/retention', 'api/retention/preview', 'api/retention/history'],
  },
  {
    name: 'TenantProviderPanel',
    render: () => <TenantProviderPanel canAdminister={false} />,
    endpoints: ['api/tenants/current', 'api/tenants/:tenantId/providers', 'api/tenants/:tenantId/egress'],
  },
];

describe('administrator-only panels seen by a reader', () => {
  let restoreFetch: () => void = () => {};

  afterEach(() => {
    restoreFetch();
  });

  for (const testCase of CASES) {
    it(`${testCase.name} explains the role instead of rendering the 403`, async () => {
      const seen: string[] = [];
      restoreFetch = installApiMock(testCase.endpoints.map((pattern) => forbidden(pattern, seen)));

      renderScreen(testCase.render());

      expect(await screen.findByTestId('unauthorized')).toBeTruthy();

      // The retry button is the tell: it is the one action a reader could take
      // that can never change the outcome.
      expect(screen.queryByRole('button', { name: /try again/i })).toBeNull();
      expect(document.body.textContent).not.toContain('403');

      // The request is never sent. A short settle window is enough — a query
      // that was going to fire does so on mount, in the same tick as the paint
      // asserted above.
      await waitFor(() => {
        expect(seen).toEqual([]);
      });
    });
  }
});
