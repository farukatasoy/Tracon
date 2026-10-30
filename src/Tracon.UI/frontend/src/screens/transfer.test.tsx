import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock, testMeta } from '../test/api-fixtures';
import { renderScreen, screen, waitFor, within } from '../test/render';
import { TransferScreen } from './transfer';

const BUNDLE = {
  format: 'tracon.definitions',
  formatVersion: 1,
  exportedAt: '2026-10-29T12:00:00Z',
  agents: [],
  workflows: [],
  skills: [],
  mcpServers: [],
  warnings: [],
};

/** What the server answers for a dry run of a bundle with one new agent and one that changes. */
const validDryRun = {
  dryRun: true,
  valid: true,
  applied: false,
  partial: false,
  written: [],
  items: [
    {
      type: 'agent',
      name: 'worker',
      action: 'create',
      sourceVersion: 1,
      targetVersion: 1,
      changedPaths: [],
      current: null,
      incoming: { name: 'worker', instructions: 'Work.' },
      messages: [],
    },
    {
      type: 'agent',
      name: 'triage',
      action: 'newVersion',
      sourceVersion: 4,
      targetVersion: 7,
      changedPaths: ['instructions'],
      current: { name: 'triage', instructions: 'Old.' },
      incoming: { name: 'triage', instructions: 'New.' },
      messages: [],
    },
  ],
};

const invalidDryRun = {
  ...validDryRun,
  valid: false,
  items: [
    {
      type: 'agent',
      name: 'broken',
      action: 'rejected',
      changedPaths: [],
      current: null,
      incoming: null,
      messages: [{ severity: 'Error', code: 'unknown_skill', message: "Agent 'broken' refers to skill 'nope'.", path: 'skillNames[0]' }],
    },
  ],
};

const file = (text: string): File => new File([text], 'bundle.json', { type: 'application/json' });

describe('TransferScreen import', () => {
  let restoreFetch: () => void;
  const imports: { dryRun: string | null; body: unknown }[] = [];

  afterEach(() => {
    restoreFetch();
    imports.length = 0;
  });

  function mount(
    report: unknown,
    roles: { canOperate: boolean; canAdminister: boolean } = { canOperate: true, canAdminister: true },
    applied?: unknown,
  ) {
    restoreFetch = installApiMock([
      fixture('GET', 'api/agents', []),
      fixture('GET', 'api/workflows', []),
      {
        method: 'POST',
        pattern: 'api/definitions/import',
        handler: (_params, url, body) => {
          imports.push({ dryRun: url.searchParams.get('dryRun'), body });

          return url.searchParams.get('dryRun') === 'false' && applied !== undefined ? applied : report;
        },
      },
    ]);

    renderScreen(<TransferScreen meta={{ ...testMeta, roles: { canRead: true, ...roles } }} />);
  }

  it('checks the file as a dry run and sends the JSON as it was read', async () => {
    mount(validDryRun);

    const user = userEvent.setup();

    await user.upload(screen.getByTestId('transfer-file'), file(JSON.stringify(BUNDLE)));
    await user.click(screen.getByTestId('transfer-check'));

    const report = await screen.findByTestId('transfer-report');

    expect(imports).toStrictEqual([{ dryRun: 'true', body: BUNDLE }]);
    within(report).getByText('Create');
    within(report).getByText('New version');
    // The version mapping is the one thing a reviewer reads to see a pin move.
    within(report).getByText('v4 → v7');
  });

  it('opens the two definitions side by side', async () => {
    mount(validDryRun);

    const user = userEvent.setup();

    await user.upload(screen.getByTestId('transfer-file'), file(JSON.stringify(BUNDLE)));
    await user.click(screen.getByTestId('transfer-check'));
    await user.click(await screen.findByRole('button', { name: 'triage' }));

    await screen.findByText(/"instructions": "Old\."/);
    screen.getByText(/"instructions": "New\."/);
  });

  it('keeps Apply off until a valid report is on screen, then writes and shows what was written', async () => {
    mount(validDryRun, undefined, {
      ...validDryRun,
      dryRun: false,
      applied: true,
      written: ['agent:worker', 'agent:triage'],
    });

    const user = userEvent.setup();

    await user.upload(screen.getByTestId('transfer-file'), file(JSON.stringify(BUNDLE)));

    expect((screen.getByTestId('transfer-apply') as HTMLButtonElement).disabled).toBe(true);

    await user.click(screen.getByTestId('transfer-check'));
    await screen.findByTestId('transfer-report');
    await user.click(screen.getByTestId('transfer-apply'));

    await waitFor(() => expect(imports.map((entry) => entry.dryRun)).toStrictEqual(['true', 'false']));
    expect((await screen.findByTestId('transfer-written')).textContent).toBe('agent:worker, agent:triage');
  });

  it('never lights Apply for a file that is not valid, and shows the finding', async () => {
    mount(invalidDryRun);

    const user = userEvent.setup();

    await user.upload(screen.getByTestId('transfer-file'), file(JSON.stringify(BUNDLE)));
    await user.click(screen.getByTestId('transfer-check'));
    await screen.findByTestId('transfer-report');

    expect((screen.getByTestId('transfer-apply') as HTMLButtonElement).disabled).toBe(true);
    screen.getByText("Agent 'broken' refers to skill 'nope'.");
    screen.getByText('Refused');
  });

  it('offers an operator the check and no Apply at all', async () => {
    mount(validDryRun, { canOperate: true, canAdminister: false });

    screen.getByTestId('transfer-check');
    expect(screen.queryByTestId('transfer-apply')).toBeNull();
    screen.getByText('Applying a file needs the administrator role.');
  });

  it('refuses a reader the whole screen', async () => {
    mount(validDryRun, { canOperate: false, canAdminister: false });

    await screen.findByTestId('unauthorized');
    expect(screen.queryByTestId('transfer-check')).toBeNull();
  });

  it('says so when the file is not JSON, and checks nothing', async () => {
    mount(validDryRun);

    const user = userEvent.setup();

    await user.upload(screen.getByTestId('transfer-file'), file('this is not json'));

    await screen.findByText('This file is not JSON.');
    expect((screen.getByTestId('transfer-check') as HTMLButtonElement).disabled).toBe(true);
    expect(imports).toStrictEqual([]);
  });
});

describe('TransferScreen export', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('asks for the selection, previews what the file will hold and lists the warnings', async () => {
    let query: URLSearchParams | undefined;

    restoreFetch = installApiMock([
      fixture('GET', 'api/agents', [{ name: 'triage' }, { name: 'other' }]),
      fixture('GET', 'api/workflows', []),
      {
        method: 'GET',
        pattern: 'api/definitions/export',
        handler: (_params, url) => {
          query = url.searchParams;

          return {
            ...BUNDLE,
            agents: [
              { sourceVersion: 1, definition: { name: 'triage' } },
              { sourceVersion: 1, definition: { name: 'worker' } },
            ],
            skills: [{ sourceVersion: 1, definition: { name: 'policy' } }],
            warnings: ["'triage' refers to agent 'gone' (callableAgents[1].name), which is not a stored agent."],
          };
        },
      },
    ]);

    const user = userEvent.setup();

    renderScreen(<TransferScreen meta={testMeta} />);

    await user.click(await screen.findByLabelText('triage'));
    await user.click(screen.getByTestId('transfer-export-prepare'));

    const preview = await screen.findByTestId('transfer-export-preview');

    expect(query?.getAll('agent')).toStrictEqual(['triage']);
    within(preview).getByText('triage, worker');
    within(preview).getByText('policy');
    within(preview).getByText(/which is not a stored agent/);
    within(preview).getByTestId('transfer-download');
  });
});
