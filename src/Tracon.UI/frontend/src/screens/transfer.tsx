import { Fragment, useState, type ChangeEvent, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT, type MessageKey } from '../lib/i18n';
import { DiffView } from '../components/diff-view';
import {
  Badge,
  Button,
  ErrorNote,
  Field,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  Th,
  Unauthorized,
} from '../components/ui';
import type { StatusTone } from '../components/status-dot';
import type {
  DefinitionBundle,
  DefinitionImportItem,
  DefinitionImportReport,
  TraconMetaResponse as Meta,
} from '@tracon/client';

/**
 * Moves definitions between installations as one `tracon.definitions` file.
 *
 * Two panels and no state shared between them. The file is a long-lived contract,
 * so the console never edits it: it shows what the server computed (the closure on
 * export, the plan on import) and passes the JSON through untouched.
 */
export function TransferScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();

  if (!meta.roles.canOperate) {
    return (
      <>
        <PageHeader title={t('nav.transfer')} description={t('transfer.description')} />
        <Panel>
          <Unauthorized requires="operator" />
        </Panel>
      </>
    );
  }

  return (
    <>
      <PageHeader title={t('nav.transfer')} description={t('transfer.description')} />
      <div className="grid gap-4 xl:grid-cols-2">
        <ExportPanel />
        <ImportPanel canApply={meta.roles.canAdminister} />
      </div>
    </>
  );
}

/** Names of the definitions in a bundle, by kind, for the preview. */
function contents(bundle: DefinitionBundle): { label: MessageKey; names: string[] }[] {
  return [
    { label: 'transfer.kind.agent', names: (bundle.agents ?? []).map((agent) => agent.definition.name) },
    { label: 'transfer.kind.workflow', names: (bundle.workflows ?? []).map((workflow) => workflow.name) },
    { label: 'transfer.kind.skill', names: (bundle.skills ?? []).map((skill) => skill.definition.name) },
    { label: 'transfer.kind.mcpServer', names: (bundle.mcpServers ?? []).map((server) => server.name) },
  ];
}

function ExportPanel(): ReactNode {
  const t = useT();
  const [agents, setAgents] = useState<string[]>([]);
  const [workflows, setWorkflows] = useState<string[]>([]);

  const agentList = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')),
  });
  const workflowList = useQuery({
    queryKey: ['workflows'],
    queryFn: () => unwrap(client.GET('/api/workflows')),
  });

  const prepare = useMutation({
    mutationFn: () =>
      unwrap(
        client.GET('/api/definitions/export', {
          params: {
            query: {
              ...(agents.length > 0 ? { agent: agents } : {}),
              ...(workflows.length > 0 ? { workflow: workflows } : {}),
            },
          },
        }),
      ),
  });

  const toggle = (list: string[], set: (next: string[]) => void, name: string): void => {
    prepare.reset();
    set(list.includes(name) ? list.filter((entry) => entry !== name) : [...list, name]);
  };

  const download = (bundle: DefinitionBundle): void => {
    const url = URL.createObjectURL(new Blob([JSON.stringify(bundle, null, 2)], { type: 'application/json' }));
    const link = document.createElement('a');

    link.href = url;
    link.download = 'tracon-definitions.json';
    link.click();
    URL.revokeObjectURL(url);
  };

  return (
    <Panel title={t('transfer.export.title')}>
      <div className="flex flex-col gap-3 p-4">
        <p className="text-sm text-muted">{t('transfer.export.hint')}</p>

        <div className="grid gap-3 sm:grid-cols-2">
          <Selection
            label={t('nav.agents')}
            query={agentList}
            selected={agents}
            onToggle={(name) => toggle(agents, setAgents, name)}
          />
          <Selection
            label={t('nav.workflows')}
            query={workflowList}
            selected={workflows}
            onToggle={(name) => toggle(workflows, setWorkflows, name)}
          />
        </div>

        <div>
          <Button tone="primary" busy={prepare.isPending} onClick={() => prepare.mutate()} testId="transfer-export-prepare">
            {t('transfer.export.prepare')}
          </Button>
        </div>

        {prepare.isError && <ErrorNote error={prepare.error} />}

        {prepare.isSuccess && (
          <div data-testid="transfer-export-preview" className="flex flex-col gap-2 border-t border-line pt-3">
            <p className="text-sm font-medium">{t('transfer.export.contents')}</p>
            <ul className="flex flex-col gap-1 text-base">
              {contents(prepare.data).map(({ label, names }) => (
                <li key={label}>
                  <span className="text-muted">{t(label)}</span> <Mono>{names.length > 0 ? names.join(', ') : '—'}</Mono>
                </li>
              ))}
            </ul>
            {(prepare.data.warnings ?? []).map((warning) => (
              <p key={warning} className="rounded border border-line bg-warn-soft px-3 py-1.5 text-xs text-warn">
                {warning}
              </p>
            ))}
            <div>
              <Button tone="primary" onClick={() => download(prepare.data)} testId="transfer-download">
                {t('transfer.export.download')}
              </Button>
            </div>
          </div>
        )}
      </div>
    </Panel>
  );
}

function Selection({
  label,
  query,
  selected,
  onToggle,
}: {
  label: string;
  query: { isPending: boolean; isError: boolean; error: unknown; data?: readonly { name: string }[] };
  selected: string[];
  onToggle: (name: string) => void;
}): ReactNode {
  return (
    <fieldset className="min-w-0">
      <legend className="mb-1 text-sm font-medium">{label}</legend>
      {query.isPending && <Loading rows={2} />}
      {query.isError && <ErrorNote error={query.error} />}
      <div className="flex max-h-40 flex-col gap-1 overflow-y-auto">
        {(query.data ?? []).map((item) => (
          <label key={item.name} className="flex items-center gap-2 text-base">
            <input type="checkbox" checked={selected.includes(item.name)} onChange={() => onToggle(item.name)} />
            <Mono>{item.name}</Mono>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

const ACTION_TONES: Record<string, StatusTone> = {
  create: 'success',
  newVersion: 'info',
  update: 'info',
  unchanged: 'neutral',
  rejected: 'danger',
  blocked: 'warn',
  skipped: 'neutral',
};

function ImportPanel({ canApply }: { canApply: boolean }): ReactNode {
  const t = useT();
  const [bundle, setBundle] = useState<DefinitionBundle | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [report, setReport] = useState<DefinitionImportReport | null>(null);

  const run = useMutation({
    mutationFn: async (dryRun: boolean) =>
      unwrap(
        client.POST('/api/definitions/import', {
          params: { query: { dryRun } },
          body: bundle as DefinitionBundle,
        }),
      ),
    onSuccess: setReport,
  });

  const choose = async (event: ChangeEvent<HTMLInputElement>): Promise<void> => {
    const file = event.target.files?.[0];

    // A person edits the file and chooses the same path again; an input that still holds that path reports no change.
    event.target.value = '';

    run.reset();
    setReport(null);
    setOpen(null);

    if (file === undefined) {
      setBundle(null);

      return;
    }

    try {
      setBundle(JSON.parse(await file.text()) as DefinitionBundle);
      setFileError(null);
    } catch {
      setBundle(null);
      setFileError(t('transfer.import.notJson'));
    }
  };

  // The apply button is only ever lit by a CHECKED file: the report it answers is the one on screen.
  const applicable = canApply && report !== null && report.dryRun && report.valid;

  return (
    <Panel title={t('transfer.import.title')}>
      <div className="flex flex-col gap-3 p-4">
        <p className="text-sm text-muted">{t('transfer.import.hint')}</p>

        <Field label={t('transfer.import.file')} error={fileError ?? undefined}>
          <input
            type="file"
            accept=".json,application/json"
            data-testid="transfer-file"
            className="text-base"
            onChange={(event) => void choose(event)}
          />
        </Field>

        <div className="flex flex-wrap gap-2">
          <Button
            tone="default"
            busy={run.isPending}
            disabled={bundle === null}
            onClick={() => run.mutate(true)}
            testId="transfer-check"
          >
            {t('transfer.import.check')}
          </Button>
          {canApply ? (
            <Button
              tone="primary"
              busy={run.isPending}
              disabled={!applicable}
              onClick={() => run.mutate(false)}
              testId="transfer-apply"
            >
              {t('transfer.import.apply')}
            </Button>
          ) : (
            <p className="self-center text-sm text-muted">{t('transfer.import.notAdmin')}</p>
          )}
        </div>

        {run.isError && <ErrorNote error={run.error} />}

        {report !== null && (
          <div data-testid="transfer-report" className="flex flex-col gap-2">
            <ReportSummary report={report} />
            <Table label={t('transfer.import.title')}>
              <thead>
                <tr>
                  <Th>{t('transfer.report.kind')}</Th>
                  <Th>{t('transfer.report.name')}</Th>
                  <Th>{t('transfer.report.action')}</Th>
                  <Th>{t('transfer.report.version')}</Th>
                  <Th>{t('transfer.report.messages')}</Th>
                </tr>
              </thead>
              <tbody>
                {report.items.map((item) => {
                  const key = `${item.type}:${item.name}`;

                  return (
                    <Fragment key={key}>
                      <ItemRow item={item} expanded={open === key} onToggle={() => setOpen(open === key ? null : key)} />
                      {open === key && (
                        <tr>
                          <td colSpan={5} className="border-b border-line p-3">
                            <ItemDetail item={item} />
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </Table>
          </div>
        )}
      </div>
    </Panel>
  );
}

function ReportSummary({ report }: { report: DefinitionImportReport }): ReactNode {
  const t = useT();

  if (!report.valid) {
    return <p className="text-sm text-danger">{t('transfer.import.invalid')}</p>;
  }

  if (report.dryRun) {
    return <p className="text-sm text-success">{t('transfer.import.valid')}</p>;
  }

  const written = report.written ?? [];

  return (
    <div className="flex flex-col gap-1">
      <p className={report.applied ? 'text-sm text-success' : 'text-sm text-warn'}>
        {report.applied
          ? t('transfer.import.applied', { count: written.length })
          : t('transfer.import.partial', { count: written.length })}
      </p>
      <p data-testid="transfer-written" className="text-sm">
        <Mono>{written.join(', ')}</Mono>
      </p>
    </div>
  );
}

function ItemRow({
  item,
  expanded,
  onToggle,
}: {
  item: DefinitionImportItem;
  expanded: boolean;
  onToggle: () => void;
}): ReactNode {
  const t = useT();
  const messages = item.messages ?? [];
  const first = messages[0];

  return (
    <tr data-testid={`transfer-row-${item.type}-${item.name}`}>
      <Td>{t(`transfer.kind.${item.type}` as MessageKey)}</Td>
      <Td>
        <Button tone="ghost" aria-expanded={expanded} onClick={onToggle}>
          <Mono>{item.name}</Mono>
        </Button>
      </Td>
      <Td>
        <Badge tone={ACTION_TONES[item.action] ?? 'neutral'}>{t(`transfer.action.${item.action}` as MessageKey)}</Badge>
      </Td>
      <Td>
        {item.sourceVersion != null && item.targetVersion != null ? (
          <Mono>{`v${item.sourceVersion} → v${item.targetVersion}`}</Mono>
        ) : (
          '—'
        )}
      </Td>
      <Td>
        {/* The text is the server's own and is not translated (K-232). */}
        {first === undefined ? '—' : first.message}
        {messages.length > 1 && <span className="text-muted">{` (+${messages.length - 1})`}</span>}
      </Td>
    </tr>
  );
}

function ItemDetail({ item }: { item: DefinitionImportItem }): ReactNode {
  const t = useT();
  const pretty = (value: unknown): string => (value == null ? '' : JSON.stringify(value, null, 2));

  return (
    <div className="flex flex-col gap-2">
      {(item.messages ?? []).map((message, index) => (
        <p key={index} className={message.severity === 'Error' ? 'text-sm text-danger' : 'text-sm text-warn'}>
          <Mono>{message.code}</Mono> {message.message}
          {message.path != null && <Mono className="text-muted">{` ${message.path}`}</Mono>}
        </p>
      ))}
      {item.incoming != null ? (
        <DiffView left={pretty(item.current)} right={pretty(item.incoming)} />
      ) : (
        <p className="text-sm text-muted">{t('transfer.report.noChange')}</p>
      )}
    </div>
  );
}
