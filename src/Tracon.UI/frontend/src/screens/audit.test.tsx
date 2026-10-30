import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, testMeta } from '../test/api-fixtures';
import { renderScreen, screen, waitFor } from '../test/render';
import { AuditScreen } from './audit';

/**
 * The "Verify chain" panel. The walk runs when the operator asks for it, and the panel shows the
 * four answers the endpoint can give, the number of entries that carry no signing key, and where the walk stopped.
 */
describe('AuditScreen chain verification', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  const FAILING = '01a14729-a429-7415-9d8c-0415a1de8dd8';

  async function verify(result: Record<string, unknown>) {
    let walks = 0;

    restoreFetch = installApiMock([
      fixture('GET', 'api/audit', []),
      {
        method: 'GET',
        pattern: 'api/audit/verify',
        handler: () => {
          walks += 1;
          return result;
        },
      },
    ]);

    const user = userEvent.setup();

    renderScreen(<AuditScreen meta={testMeta} />);

    // Not walked on open: the walk reads every entry of the tenant.
    await screen.findByRole('button', { name: 'Verify chain' });
    expect(walks).toBe(0);

    await user.click(screen.getByRole('button', { name: 'Verify chain' }));
    await screen.findByTestId('audit-verify-result');

    return walks;
  }

  it('shows a valid chain with no unsigned entry and no warning', async () => {
    const walks = await verify({ status: 'Valid', entriesChecked: 42, firstFailingEntryId: null, unkeyedEntries: 0 });

    expect(walks).toBe(1);
    expect(within('audit-verify-result')).toContain('Valid');
    expect(within('audit-verify-result')).toContain('42');
    expect(screen.queryByTestId('audit-verify-unkeyed-warning')).toBeNull();
    expect(within('audit-verify-result')).not.toContain('First failing entry');
  });

  it('warns when entries carry no signing key and says how many', async () => {
    await verify({ status: 'Valid', entriesChecked: 10, firstFailingEntryId: null, unkeyedEntries: 7 });

    const warning = screen.getByTestId('audit-verify-unkeyed-warning');

    expect(warning.textContent).toContain('7 of 10 entries carry no signing key');
  });

  it('names the first failing entry for a broken chain', async () => {
    await verify({ status: 'Broken', entriesChecked: 5, firstFailingEntryId: FAILING, unkeyedEntries: 0 });

    expect(within('audit-verify-result')).toContain('Broken');
    expect(within('audit-verify-result')).toContain(FAILING);
    expect(within('audit-verify-result')).toContain('First failing entry');
  });

  it('reports an unknown key as its own answer, not as a break', async () => {
    await verify({ status: 'UnknownKey', entriesChecked: 5, firstFailingEntryId: FAILING, unkeyedEntries: 0 });

    const text = within('audit-verify-result');

    expect(text).toContain('Unknown key');
    expect(text).not.toContain('Broken');
    expect(text).toContain('changed by hand');
  });

  it('treats a missing unkeyedEntries as zero', async () => {
    await verify({ status: 'Valid', entriesChecked: 3 });

    expect(screen.queryByTestId('audit-verify-unkeyed-warning')).toBeNull();
  });

  it('sends no verify request for a reader who cannot administer', async () => {
    let walks = 0;

    restoreFetch = installApiMock([
      fixture('GET', 'api/audit', []),
      {
        method: 'GET',
        pattern: 'api/audit/verify',
        handler: () => {
          walks += 1;
          return {};
        },
      },
    ]);

    renderScreen(
      <AuditScreen meta={{ ...testMeta, roles: { ...testMeta.roles, canAdminister: false } }} />,
    );

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Verify chain' })).toBeNull());
    expect(walks).toBe(0);
  });
});

function within(testId: string): string {
  return screen.getByTestId(testId).textContent;
}
