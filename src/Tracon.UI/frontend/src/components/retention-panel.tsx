import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client as apiClient, unwrap } from '../lib/api';
import { count, relativeTime } from '../lib/format';
import { useT, type MessageKey } from '../lib/i18n';
import {
  Badge,
  Button,
  ErrorNote,
  Field,
  Loading,
  Mono,
  Panel,
  Table,
  Td,
  Th,
  TextInput,
} from './ui';
import type { RetentionPolicy, RetentionPreview, RetentionRun } from '../lib/server-types';

// The server models a target only as `target: string` (see RetentionTargets in
// the API doc comment) — this closed list is a client-side convenience, not
// part of the OpenAPI schema.
type RetentionTarget =
  | 'run_events'
  | 'tool_invocations'
  | 'traces'
  | 'jobs'
  | 'webhook_deliveries'
  | 'eval_case_results'
  | 'workflow_checkpoints'
  | 'skill_script_grants'
  | 'attachments'
  | 'sessions'
  | 'conversations';

const TARGETS: { value: RetentionTarget; label: MessageKey }[] = [
  { value: 'run_events', label: 'retention.target.runEvents' },
  { value: 'tool_invocations', label: 'retention.target.toolInvocations' },
  { value: 'traces', label: 'retention.target.traces' },
  { value: 'jobs', label: 'retention.target.jobs' },
  { value: 'webhook_deliveries', label: 'retention.target.webhookDeliveries' },
  { value: 'eval_case_results', label: 'retention.target.evalResults' },
  { value: 'workflow_checkpoints', label: 'retention.target.checkpoints' },
  { value: 'skill_script_grants', label: 'retention.target.scriptGrants' },
  { value: 'attachments', label: 'retention.target.attachments' },
  { value: 'sessions', label: 'retention.target.sessions' },
  { value: 'conversations', label: 'retention.target.conversations' },
];

/**
 * Data retention policies, a delete preview and recent cleanup runs.
 *
 * No default policy ships: an upgrade never deletes anything on its own.
 * `preview` is read-only — every number here comes from a count, never from a
 * delete. "Run now" only enqueues a job; the actual sweep happens in the
 * queue worker.
 */
export function RetentionPanel(): ReactNode {
  const t = useT();
  const client = useQueryClient();
  const [editingTarget, setEditingTarget] = useState<RetentionTarget | null>(null);

  const preview = useQuery({
    queryKey: ['retention-preview'],
    queryFn: () => unwrap(apiClient.GET('/api/retention/preview')) as Promise<RetentionPreview[]>,
  });
  const policies = useQuery({
    queryKey: ['retention-policies'],
    queryFn: () => unwrap(apiClient.GET('/api/retention')) as Promise<RetentionPolicy[]>,
  });
  const history = useQuery({
    queryKey: ['retention-history'],
    queryFn: () =>
      unwrap(
        apiClient.GET('/api/retention/history', { params: { query: { take: 10 } } }),
      ) as Promise<RetentionRun[]>,
  });

  const invalidate = (): void => {
    void client.invalidateQueries({ queryKey: ['retention-preview'] });
    void client.invalidateQueries({ queryKey: ['retention-policies'] });
  };

  const run = useMutation({
    mutationFn: (target?: string) =>
      unwrap(apiClient.POST('/api/retention/run', { params: { query: { target } } })),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['retention-history'] }),
  });

  const remove = useMutation({
    mutationFn: (target: string) =>
      unwrap(apiClient.DELETE('/api/retention/{target}', { params: { path: { target } } })),
    onSuccess: invalidate,
  });

  if (preview.isPending || policies.isPending) {
    return (
      <Panel title={t('retention.title')}>
        <Loading />
      </Panel>
    );
  }

  if (preview.isError) {
    return (
      <Panel title={t('retention.title')}>
        <div className="p-4">
          <ErrorNote error={preview.error} />
        </div>
      </Panel>
    );
  }

  const previewByTarget = new Map(preview.data.map((row) => [row.target, row]));
  const policyByTarget = new Map((policies.data ?? []).map((row) => [row.target, row]));

  return (
    <Panel
      title={t('retention.title')}
      actions={
        <Button tone="default" busy={run.isPending} onClick={() => run.mutate(undefined)}>
          {t('retention.runAll')}
        </Button>
      }
    >
      <div className="rounded-md border border-line">
        <Table>
          <thead>
            <tr>
              <Th>{t('retention.target')}</Th>
              <Th>{t('common.status')}</Th>
              <Th>{t('retention.maxAge')}</Th>
              <Th>{t('retention.matchingRows')}</Th>
              <Th />
            </tr>
          </thead>
          <tbody>
            {TARGETS.map(({ value, label }) => (
              <TargetRow
                key={value}
                target={value}
                label={label}
                preview={previewByTarget.get(value)}
                policy={policyByTarget.get(value)}
                editing={editingTarget === value}
                onEdit={() => setEditingTarget((current) => (current === value ? null : value))}
                onSaved={() => {
                  setEditingTarget(null);
                  invalidate();
                }}
                onDelete={() => remove.mutate(value)}
                onRun={() => run.mutate(value)}
                running={run.isPending}
              />
            ))}
          </tbody>
        </Table>
      </div>

      <p className="border-t border-line px-4 py-2.5 text-[11px] text-subtle">
        {t('retention.notice')} <Mono>audit_log</Mono> {t('retention.auditNotice')}
      </p>

      {history.isSuccess && history.data.length > 0 && (
        <div className="border-t border-line">
          <h3 className="px-4 pt-3 text-[12px] font-medium text-muted">{t('retention.recentRuns')}</h3>
          <Table>
            <thead>
              <tr>
                <Th>{t('retention.target')}</Th>
                <Th>{t('retention.deleted')}</Th>
                <Th>{t('retention.archived')}</Th>
                <Th>{t('common.started')}</Th>
                <Th>{t('common.result')}</Th>
              </tr>
            </thead>
            <tbody>
              {history.data.map((run_) => (
                <tr key={run_.id}>
                  <Td>
                    <Mono>{run_.target}</Mono>
                  </Td>
                  <Td>{count(run_.deletedRows)}</Td>
                  <Td>{count(run_.archivedRows)}</Td>
                  <Td>{relativeTime(run_.startedAt)}</Td>
                  <Td title={run_.error ?? undefined}>
                    {run_.error != null ? (
                      <Badge tone="danger">{t('runs.status.failed')}</Badge>
                    ) : run_.completedAt != null ? (
                      <Badge tone="success">{t('transcript.done')}</Badge>
                    ) : (
                      <Badge tone="accent">{t('runs.status.running')}</Badge>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      )}
    </Panel>
  );
}

function TargetRow({
  target,
  label,
  preview,
  policy,
  editing,
  onEdit,
  onSaved,
  onDelete,
  onRun,
  running,
}: {
  target: RetentionTarget;
  label: MessageKey;
  preview: RetentionPreview | undefined;
  policy: RetentionPolicy | undefined;
  editing: boolean;
  onEdit: () => void;
  onSaved: () => void;
  onDelete: () => void;
  onRun: () => void;
  running: boolean;
}): ReactNode {
  const t = useT();

  return (
    <>
      <tr>
        <Td>{t(label)}</Td>
        <Td>
          {preview?.enabled === true ? (
            <Badge tone="success">{t('retention.configured')}</Badge>
          ) : (
            <Badge tone="neutral">{t('common.disabled')}</Badge>
          )}
          {policy?.archive === true && (
            <Badge tone="accent" title={t('retention.archivedTitle')}>
              {t('retention.archived')}
            </Badge>
          )}
        </Td>
        <Td>
          {preview?.maxAgeDays != null ? t('retention.days', { days: preview.maxAgeDays }) : '—'}
        </Td>
        <Td>{preview != null ? count(preview.matchingRows) : '—'}</Td>
        <Td>
          <div className="flex justify-end gap-1.5">
            <Button tone="ghost" onClick={onEdit}>
              {editing ? t('common.close') : t('common.edit')}
            </Button>
            {preview?.enabled === true && (
              <Button tone="ghost" busy={running} onClick={onRun}>
                {t('evals.runNow')}
              </Button>
            )}
          </div>
        </Td>
      </tr>
      {editing && (
        <tr>
          <td colSpan={5} className="border-t border-line bg-raised/40 p-4">
            <PolicyForm target={target} policy={policy} onSaved={onSaved} onDelete={onDelete} />
          </td>
        </tr>
      )}
    </>
  );
}

function PolicyForm({
  target,
  policy,
  onSaved,
  onDelete,
}: {
  target: RetentionTarget;
  policy: RetentionPolicy | undefined;
  onSaved: () => void;
  onDelete: () => void;
}): ReactNode {
  const t = useT();
  const [maxAgeDays, setMaxAgeDays] = useState(policy?.maxAgeDays?.toString() ?? '');
  const [maxRows, setMaxRows] = useState(policy?.maxRows?.toString() ?? '');
  const [archive, setArchive] = useState(policy?.archive ?? false);
  const [enabled, setEnabled] = useState(policy?.enabled ?? true);

  const save = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.PUT('/api/retention/{target}', {
          params: { path: { target } },
          body: {
            maxAgeDays: maxAgeDays.trim() === '' ? null : Number(maxAgeDays),
            maxRows: maxRows.trim() === '' ? null : Number(maxRows),
            archive,
            enabled,
          },
        }),
      ) as Promise<RetentionPolicy>,
    onSuccess: onSaved,
  });

  const hasThreshold = maxAgeDays.trim() !== '' || maxRows.trim() !== '';

  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-4">
        <Field label={t('retention.maxAgeDays')} hint={t('retention.maxAgeHint')}>
          <TextInput
            value={maxAgeDays}
            inputMode="numeric"
            placeholder={t('retention.maxAgePlaceholder')}
            onChange={(event) => setMaxAgeDays(event.target.value)}
          />
        </Field>
        <Field label={t('retention.maxRows')} hint={t('retention.maxRowsHint')}>
          <TextInput
            value={maxRows}
            inputMode="numeric"
            placeholder={t('retention.maxRowsPlaceholder')}
            onChange={(event) => setMaxRows(event.target.value)}
          />
        </Field>
        <label className="flex items-end gap-2 pb-1.5 text-[13px]">
          <input
            type="checkbox"
            checked={archive}
            onChange={(event) => setArchive(event.target.checked)}
            className="size-4 rounded border-line"
          />
          {t('retention.archiveFirst')}
        </label>
        <label className="flex items-end gap-2 pb-1.5 text-[13px]">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(event) => setEnabled(event.target.checked)}
            className="size-4 rounded border-line"
          />
          {t('common.enabled')}
        </label>
      </div>

      {save.isError && <ErrorNote error={save.error} />}

      <div className="flex items-center gap-2">
        <Button
          tone="primary"
          disabled={!hasThreshold}
          busy={save.isPending}
          onClick={() => save.mutate()}
        >
          {t('retention.savePolicy')}
        </Button>
        {policy != null && (
          <Button tone="danger" onClick={onDelete}>
            {t('retention.deletePolicy')}
          </Button>
        )}
      </div>
    </div>
  );
}
