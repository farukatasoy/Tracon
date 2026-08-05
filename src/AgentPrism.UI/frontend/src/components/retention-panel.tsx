import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { count, relativeTime } from '../lib/format';
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
import type { RetentionPolicy, RetentionPreview, RetentionTarget } from '../lib/types';

const TARGETS: { value: RetentionTarget; label: string }[] = [
  { value: 'run_events', label: 'Run events' },
  { value: 'tool_invocations', label: 'Tool invocations' },
  { value: 'traces', label: 'Traces & spans' },
  { value: 'jobs', label: 'Completed jobs' },
  { value: 'webhook_deliveries', label: 'Webhook deliveries' },
  { value: 'eval_case_results', label: 'Eval case results' },
  { value: 'workflow_checkpoints', label: 'Workflow checkpoints' },
  { value: 'skill_script_grants', label: 'Skill script grants' },
  { value: 'attachments', label: 'Orphaned attachments' },
  { value: 'sessions', label: 'Sessions (user data)' },
  { value: 'conversations', label: 'Conversations (user data)' },
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
  const client = useQueryClient();
  const [editingTarget, setEditingTarget] = useState<RetentionTarget | null>(null);

  const preview = useQuery({ queryKey: ['retention-preview'], queryFn: () => api.retentionPreview() });
  const policies = useQuery({ queryKey: ['retention-policies'], queryFn: () => api.retentionPolicies() });
  const history = useQuery({
    queryKey: ['retention-history'],
    queryFn: () => api.retentionHistory({ take: 10 }),
  });

  const invalidate = (): void => {
    void client.invalidateQueries({ queryKey: ['retention-preview'] });
    void client.invalidateQueries({ queryKey: ['retention-policies'] });
  };

  const run = useMutation({
    mutationFn: (target?: string) => api.runRetention(target),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['retention-history'] }),
  });

  const remove = useMutation({
    mutationFn: (target: string) => api.deleteRetentionPolicy(target),
    onSuccess: invalidate,
  });

  if (preview.isPending || policies.isPending) {
    return (
      <Panel title="Data retention">
        <Loading />
      </Panel>
    );
  }

  if (preview.isError) {
    return (
      <Panel title="Data retention">
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
      title="Data retention"
      actions={
        <Button tone="default" busy={run.isPending} onClick={() => run.mutate(undefined)}>
          Run all now
        </Button>
      }
    >
      <div className="rounded-md border border-line">
        <Table>
          <thead>
            <tr>
              <Th>Target</Th>
              <Th>Status</Th>
              <Th>Max age</Th>
              <Th>Matching rows</Th>
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
        No policy ships by default — an upgrade never deletes anything on its own.{' '}
        <Mono>audit_log</Mono> is never a valid target and cannot be configured here.
      </p>

      {history.isSuccess && history.data.length > 0 && (
        <div className="border-t border-line">
          <h3 className="px-4 pt-3 text-[12px] font-medium text-muted">Recent runs</h3>
          <Table>
            <thead>
              <tr>
                <Th>Target</Th>
                <Th>Deleted</Th>
                <Th>Archived</Th>
                <Th>Started</Th>
                <Th>Result</Th>
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
                      <Badge tone="danger">failed</Badge>
                    ) : run_.completedAt != null ? (
                      <Badge tone="success">done</Badge>
                    ) : (
                      <Badge tone="accent">running</Badge>
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
  label: string;
  preview: RetentionPreview | undefined;
  policy: RetentionPolicy | undefined;
  editing: boolean;
  onEdit: () => void;
  onSaved: () => void;
  onDelete: () => void;
  onRun: () => void;
  running: boolean;
}): ReactNode {
  return (
    <>
      <tr>
        <Td>{label}</Td>
        <Td>
          {preview?.enabled === true ? (
            <Badge tone="success">configured</Badge>
          ) : (
            <Badge tone="neutral">off</Badge>
          )}
          {policy?.archive === true && (
            <Badge tone="accent" title="Rows are written to the archive sink before deletion.">
              archived
            </Badge>
          )}
        </Td>
        <Td>{preview?.maxAgeDays != null ? `${preview.maxAgeDays} days` : '—'}</Td>
        <Td>{preview != null ? count(preview.matchingRows) : '—'}</Td>
        <Td>
          <div className="flex justify-end gap-1.5">
            <Button tone="ghost" onClick={onEdit}>
              {editing ? 'Close' : 'Edit'}
            </Button>
            {preview?.enabled === true && (
              <Button tone="ghost" busy={running} onClick={onRun}>
                Run now
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
  const [maxAgeDays, setMaxAgeDays] = useState(policy?.maxAgeDays?.toString() ?? '');
  const [archive, setArchive] = useState(policy?.archive ?? false);
  const [enabled, setEnabled] = useState(policy?.enabled ?? true);

  const save = useMutation({
    mutationFn: () =>
      api.saveRetentionPolicy(target, {
        maxAgeDays: maxAgeDays.trim() === '' ? null : Number(maxAgeDays),
        archive,
        enabled,
      }),
    onSuccess: onSaved,
  });

  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-3">
        <Field label="Max age (days)" hint="Rows older than this are deletion candidates.">
          <TextInput
            value={maxAgeDays}
            inputMode="numeric"
            placeholder="e.g. 30"
            onChange={(event) => setMaxAgeDays(event.target.value)}
          />
        </Field>
        <label className="flex items-end gap-2 pb-1.5 text-[13px]">
          <input
            type="checkbox"
            checked={archive}
            onChange={(event) => setArchive(event.target.checked)}
            className="size-4 rounded border-line"
          />
          Archive before deleting
        </label>
        <label className="flex items-end gap-2 pb-1.5 text-[13px]">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(event) => setEnabled(event.target.checked)}
            className="size-4 rounded border-line"
          />
          Enabled
        </label>
      </div>

      {save.isError && <ErrorNote error={save.error} />}

      <div className="flex items-center gap-2">
        <Button
          tone="primary"
          disabled={maxAgeDays.trim() === ''}
          busy={save.isPending}
          onClick={() => save.mutate()}
        >
          Save policy
        </Button>
        {policy != null && (
          <Button tone="danger" onClick={onDelete}>
            Delete policy
          </Button>
        )}
      </div>
    </div>
  );
}
