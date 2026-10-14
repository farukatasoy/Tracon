import type { ReactNode } from 'react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, type Mock } from 'vitest';
import { renderScreen, screen, waitFor, within } from '../test/render';
import { VersionHistory, type VersionHistoryLabels } from './version-history';

interface Row {
  version: number;
  updatedAt: string;
  title: string;
}

const rows: Row[] = [
  { version: 3, updatedAt: '2026-10-13T12:00:00Z', title: 'Third' },
  { version: 2, updatedAt: '2026-10-13T11:00:00Z', title: 'Second' },
  { version: 1, updatedAt: '2026-10-13T10:00:00Z', title: 'First' },
];

const labels: VersionHistoryLabels = {
  empty: 'agentDetail.noVersions',
  emptyBody: 'workflowDetail.noVersionsBody',
  rollbackEffect: 'workflowDetail.rollbackEffect',
};

interface Harness {
  load: Mock<() => Promise<Row[]>>;
  rollback: Mock<(version: number) => Promise<unknown>>;
  onError: Mock<(error: unknown) => void>;
  compared: [number, number][];
}

/** Mounts the component with spies for everything it calls out to. */
function mount(
  overrides: { canRollback?: boolean; data?: Row[]; rollback?: (version: number) => Promise<unknown> } = {},
): Harness {
  const harness: Harness = {
    load: vi.fn(async () => overrides.data ?? rows),
    rollback: vi.fn(overrides.rollback ?? (async () => ({}))),
    onError: vi.fn(),
    compared: [],
  };

  renderScreen(
    <VersionHistory<Row>
      queryKey={['test-versions']}
      load={harness.load}
      rollback={harness.rollback}
      invalidate={[['test-versions']]}
      currentVersion={3}
      canRollback={overrides.canRollback ?? true}
      columns={[{ header: 'Title', cell: (row) => row.title }, { header: 'Size', numeric: true, cell: (row) => row.version * 10 }]}
      labels={labels}
      compare={(a, b): ReactNode => {
        harness.compared.push([a, b]);

        return <p data-testid="compare">{`${a}->${b}`}</p>;
      }}
      onError={harness.onError}
    />,
  );

  return harness;
}

describe('VersionHistory', () => {
  it('lists every version with the caller columns and marks the current one', async () => {
    mount();

    expect(await screen.findByTestId('version-checkbox-3')).toBeTruthy();

    const table = screen.getByRole('table');
    const headers = within(table).getAllByRole('columnheader').map((header) => header.textContent);

    expect(headers).toEqual(['', 'Version', 'Title', 'Size', 'Saved', '']);

    const body = within(table).getAllByRole('row').slice(1);

    expect(body).toHaveLength(3);
    expect(body[0]?.textContent).toContain('v3');
    expect(body[0]?.textContent).toContain('Third');
    expect(body[2]?.textContent).toContain('First');

    // The badge, not the word: the rollback tooltip of the other rows says "current" too.
    within(body[0]!).getByText('current');
    expect(within(body[1]!).queryByText('current')).toBeNull();
    expect(within(body[2]!).queryByText('current')).toBeNull();
  });

  it('compares the two ticked versions in ascending order, and the two most recent picks after a third', async () => {
    const user = userEvent.setup();
    const harness = mount();

    await user.click(await screen.findByTestId('version-checkbox-3'));

    expect(screen.queryByTestId('compare')).toBeNull();
    screen.getByText('Select one more version to compare.');

    await user.click(screen.getByTestId('version-checkbox-1'));

    // Picked 3 then 1; the comparison always reads older -> newer.
    expect((await screen.findByTestId('compare')).textContent).toBe('1->3');
    screen.getByText('Comparing v1 → v3.');

    // A third pick drops the older of the two picks (3) and keeps 1 and 2.
    await user.click(screen.getByTestId('version-checkbox-2'));

    expect(screen.getByTestId('compare').textContent).toBe('1->2');
    expect(harness.compared.at(-1)).toEqual([1, 2]);

    // Unticking one leaves a single pick: no comparison.
    await user.click(screen.getByTestId('version-checkbox-2'));

    expect(screen.queryByTestId('compare')).toBeNull();
  });

  it('rolls back a past version for an administrator, and loads the list again afterwards', async () => {
    const user = userEvent.setup();
    const harness = mount();

    await screen.findByTestId('version-checkbox-3');

    // The current version has nothing to roll back to.
    expect(screen.queryByTestId('version-rollback-3')).toBeNull();

    await user.click(screen.getByTestId('version-rollback-2'));

    await waitFor(() => expect(harness.rollback).toHaveBeenCalledTimes(1));
    expect(harness.rollback.mock.calls[0]?.[0]).toBe(2);

    // The mutation invalidates the list's own key: it is fetched again.
    await waitFor(() => expect(harness.load.mock.calls.length).toBeGreaterThan(1));
    expect(harness.onError).toHaveBeenCalledWith(null);
    expect(harness.onError).not.toHaveBeenCalledWith(expect.any(Error));
  });

  it('offers no rollback to anyone but an administrator', async () => {
    mount({ canRollback: false });

    await screen.findByTestId('version-checkbox-3');

    expect(screen.queryByTestId('version-rollback-1')).toBeNull();
    expect(screen.queryByTestId('version-rollback-2')).toBeNull();
  });

  it('hands a refused rollback to the screen error slot instead of swallowing it', async () => {
    const user = userEvent.setup();
    const refusal = new Error('Version 1 names a function that is no longer registered.');
    const harness = mount({ rollback: async () => Promise.reject(refusal) });

    await user.click(await screen.findByTestId('version-rollback-1'));

    await waitFor(() => expect(harness.onError).toHaveBeenCalledWith(refusal));
  });

  it('says why the list is empty, in the words the caller chose', async () => {
    mount({ data: [] });

    await screen.findByText('No stored versions');
    screen.getByText('A version is written every time the stored definition is saved.');
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('shows the failure of the list with a retry', async () => {
    renderScreen(
      <VersionHistory<Row>
        queryKey={['test-failing']}
        load={() => Promise.reject(new Error('The store is down.'))}
        rollback={async () => ({})}
        invalidate={[]}
        currentVersion={1}
        canRollback
        columns={[]}
        labels={labels}
        compare={() => null}
        onError={() => undefined}
      />,
    );

    await screen.findByText(/The store is down\./);
    screen.getByTestId('error-retry');
  });
});
