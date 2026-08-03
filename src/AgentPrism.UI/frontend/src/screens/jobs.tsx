import { useState, type FormEvent, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Field,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Select,
  Table,
  Td,
  TextArea,
  TextInput,
  Th,
} from '../components/ui';
import { PlusIcon, TrashIcon } from '../components/icons';
import type { JobKind, JobSchedule, JobStatus, Meta } from '../lib/types';

const EMPTY_FORM = {
  name: '',
  kind: 'AgentBatch' as JobKind,
  targetName: '',
  cron: '',
  timeZone: 'UTC',
  payload: '[]',
  enabled: true,
};

type ScheduleForm = typeof EMPTY_FORM;

export function JobStatusBadge({ status }: { status: JobStatus }): ReactNode {
  switch (status) {
    case 'Completed':
      return <Badge tone="success">completed</Badge>;
    case 'Failed':
      return <Badge tone="danger">failed</Badge>;
    case 'Cancelled':
      return <Badge tone="warn">cancelled</Badge>;
    case 'Leased':
      return <Badge tone="info">leased</Badge>;
    case 'Running':
      return <Badge tone="info">running</Badge>;
    default:
      return <Badge>pending</Badge>;
  }
}

/** `done`/`failed`/`total` as a two-colour bar, the same shape as the waterfall's own bars. */
export function JobProgressBar({
  done,
  failed,
  total,
}: {
  done: number;
  failed: number;
  total: number;
}): ReactNode {
  if (total === 0) {
    return <span className="text-[11px] text-subtle">no items</span>;
  }

  const donePct = (done / total) * 100;
  const failedPct = (failed / total) * 100;

  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-24 overflow-hidden rounded-full bg-raised">
        <div className="flex h-full">
          <div className="h-full bg-success" style={{ width: `${donePct}%` }} />
          <div className="h-full bg-danger" style={{ width: `${failedPct}%` }} />
        </div>
      </div>
      <span className="text-[11px] text-muted">
        {done + failed}/{total}
      </span>
    </div>
  );
}

function toForm(schedule: JobSchedule): ScheduleForm {
  return {
    name: schedule.name,
    kind: schedule.kind,
    targetName: schedule.targetName,
    cron: schedule.cron ?? '',
    timeZone: schedule.timeZone,
    payload: JSON.stringify(schedule.payload ?? [], null, 2),
    enabled: schedule.enabled,
  };
}

/**
 * Schedules (what runs, and when) and the queue they feed (what actually ran).
 *
 * The two are deliberately separate panels: a schedule is a standing
 * definition, a job is one dated execution of it — the same split as
 * Workflows (definition) versus Runs (execution).
 */
export function JobsScreen({ meta }: { meta: Meta }): ReactNode {
  const client = useQueryClient();
  const [form, setForm] = useState<ScheduleForm>(EMPTY_FORM);
  const [showForm, setShowForm] = useState(false);
  const [payloadError, setPayloadError] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);

  const schedules = useQuery({ queryKey: ['schedules'], queryFn: api.schedules });
  const jobs = useQuery({
    queryKey: ['jobs'],
    queryFn: () => api.jobs({ take: 50 }),
    // A running job's counters move on their own; the list should follow.
    refetchInterval: 5_000,
  });

  const invalidateSchedules = (): void => void client.invalidateQueries({ queryKey: ['schedules'] });
  const invalidateJobs = (): void => void client.invalidateQueries({ queryKey: ['jobs'] });

  const save = useMutation({
    mutationFn: () => {
      const payload = JSON.parse(form.payload) as unknown;

      return api.saveSchedule(form.name, {
        kind: form.kind,
        targetName: form.targetName,
        cron: form.cron.length > 0 ? form.cron : null,
        timeZone: form.timeZone,
        payload,
        enabled: form.enabled,
      });
    },
    onSuccess: () => {
      setForm(EMPTY_FORM);
      setShowForm(false);
      setEditing(null);
      setPayloadError(null);
      invalidateSchedules();
    },
  });

  const remove = useMutation({
    mutationFn: (name: string) => api.deleteSchedule(name),
    onSuccess: invalidateSchedules,
  });

  const trigger = useMutation({
    mutationFn: (name: string) => api.triggerSchedule(name),
    onSuccess: invalidateJobs,
  });

  const cancel = useMutation({
    mutationFn: (id: string) => api.cancelJob(id),
    onSuccess: invalidateJobs,
  });

  const submit = (event: FormEvent): void => {
    event.preventDefault();
    setPayloadError(null);

    try {
      JSON.parse(form.payload);
    } catch {
      setPayloadError('Payload must be valid JSON — typically an array of input strings.');
      return;
    }

    save.mutate();
  };

  return (
    <>
      <PageHeader
        title="Jobs"
        description="Run an agent or workflow over a batch of inputs, on a cron schedule or on demand. A batch item is one ordinary run, linked back here."
        actions={
          meta.roles.canAdminister && (
            <Button
              tone="primary"
              onClick={() => {
                setForm(EMPTY_FORM);
                setEditing(null);
                setShowForm((current) => !current);
              }}
            >
              <PlusIcon className="size-3.5" />
              New schedule
            </Button>
          )
        }
      />

      {!meta.storage.jobWorkerEnabled && (
        <Panel className="mb-4">
          <div className="border-b border-line px-4 py-2.5 text-[12px] text-muted">
            <strong className="text-fg">Worker disabled in this process.</strong> Schedules and jobs
            can still be created and inspected, but nothing is leased or run here —
            <Mono className="ml-1">RunWorker</Mono> is off for this instance.
          </div>
        </Panel>
      )}

      {showForm && (
        <Panel title={editing != null ? `Edit ${editing}` : 'New schedule'} className="mb-4">
          <form className="grid gap-3 p-4 sm:grid-cols-2" onSubmit={submit}>
            <Field label="Name" required>
              <TextInput
                value={form.name}
                required
                disabled={editing != null}
                pattern="[a-zA-Z0-9_-]+"
                placeholder="nightly-report"
                onChange={(event) => setForm({ ...form, name: event.target.value })}
              />
            </Field>

            <Field label="Kind">
              <Select value={form.kind} onChange={(value) => setForm({ ...form, kind: value as JobKind })}>
                <option value="AgentBatch">Agent batch</option>
                <option value="Workflow">Workflow</option>
              </Select>
            </Field>

            <Field
              label="Target name"
              required
              hint="The agent or workflow name this schedule runs."
            >
              <TextInput
                value={form.targetName}
                required
                placeholder="summarizer"
                onChange={(event) => setForm({ ...form, targetName: event.target.value })}
              />
            </Field>

            <Field
              label="Cron"
              hint="Five fields: minute hour day-of-month month day-of-week. Leave blank for manual trigger only."
            >
              <TextInput
                value={form.cron}
                placeholder="0 3 * * *"
                onChange={(event) => setForm({ ...form, cron: event.target.value })}
              />
            </Field>

            <Field label="Time zone">
              <TextInput
                value={form.timeZone}
                placeholder="UTC"
                onChange={(event) => setForm({ ...form, timeZone: event.target.value })}
              />
            </Field>

            <label className="flex items-end gap-2 pb-1.5 text-[13px]">
              <input
                type="checkbox"
                checked={form.enabled}
                onChange={(event) => setForm({ ...form, enabled: event.target.checked })}
              />
              Enabled
            </label>

            <div className="sm:col-span-2">
              <Field
                label="Payload"
                hint="JSON. An array becomes one job item per element; anything else becomes a single item."
              >
                <TextArea
                  value={form.payload}
                  rows={4}
                  onChange={(event) => setForm({ ...form, payload: event.target.value })}
                />
              </Field>
            </div>

            <div className="sm:col-span-2 flex items-center gap-2">
              <Button type="submit" tone="primary" busy={save.isPending}>
                Save
              </Button>
              <Button
                tone="ghost"
                onClick={() => {
                  setShowForm(false);
                  setEditing(null);
                }}
              >
                Cancel
              </Button>
              {payloadError != null && <ErrorNote error={new Error(payloadError)} />}
              {save.isError && <ErrorNote error={save.error} />}
            </div>
          </form>
        </Panel>
      )}

      <Panel title="Schedules" className="mb-4">
        {schedules.isPending && <Loading />}
        {schedules.isError && (
          <div className="p-4">
            <ErrorNote error={schedules.error} />
          </div>
        )}

        {schedules.isSuccess &&
          (schedules.data.length === 0 ? (
            <Empty title="No schedules yet">
              Add one to run an agent or workflow on a timer, or trigger it by hand whenever you
              like.
            </Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Name</Th>
                  <Th>Kind</Th>
                  <Th>Target</Th>
                  <Th>Cron</Th>
                  <Th>Next run</Th>
                  <Th>Last run</Th>
                  <Th />
                  <Th />
                </tr>
              </thead>
              <tbody>
                {schedules.data.map((schedule) => (
                  <tr key={schedule.id} className="hover:bg-raised">
                    <Td>
                      <Mono className="font-semibold">{schedule.name}</Mono>
                    </Td>
                    <Td>
                      <Badge tone="accent">{schedule.kind}</Badge>
                    </Td>
                    <Td className="text-muted">{schedule.targetName}</Td>
                    <Td>
                      {schedule.cron != null && schedule.cron.length > 0 ? (
                        <Mono className="text-[11px]">{schedule.cron}</Mono>
                      ) : (
                        <span className="text-[11px] text-subtle">manual only</span>
                      )}
                    </Td>
                    <Td className="text-muted" title={absoluteTime(schedule.nextRunAt)}>
                      {schedule.nextRunAt == null ? '—' : relativeTime(schedule.nextRunAt)}
                    </Td>
                    <Td className="text-muted" title={absoluteTime(schedule.lastRunAt)}>
                      {schedule.lastRunAt == null ? '—' : relativeTime(schedule.lastRunAt)}
                    </Td>
                    <Td>
                      {schedule.enabled ? (
                        <Badge tone="accent">enabled</Badge>
                      ) : (
                        <Badge>disabled</Badge>
                      )}
                    </Td>
                    <Td className="text-right">
                      {meta.roles.canOperate && (
                        <Button
                          onClick={() => trigger.mutate(schedule.name)}
                          busy={trigger.isPending}
                          title="Run this schedule now, without waiting for its cron."
                        >
                          Trigger
                        </Button>
                      )}
                      {meta.roles.canAdminister && (
                        <>
                          <Button
                            onClick={() => {
                              setForm(toForm(schedule));
                              setEditing(schedule.name);
                              setShowForm(true);
                            }}
                          >
                            Edit
                          </Button>
                          <Button tone="danger" onClick={() => remove.mutate(schedule.name)}>
                            <TrashIcon className="size-3.5" />
                          </Button>
                        </>
                      )}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>

      <Panel title="Recent jobs">
        {jobs.isPending && <Loading />}
        {jobs.isError && (
          <div className="p-4">
            <ErrorNote error={jobs.error} />
          </div>
        )}

        {jobs.isSuccess &&
          (jobs.data.length === 0 ? (
            <Empty title="No jobs yet">
              Trigger a schedule above, or wait for its cron — every run creates a row here.
            </Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Job</Th>
                  <Th>Kind</Th>
                  <Th>Target</Th>
                  <Th>Status</Th>
                  <Th>Progress</Th>
                  <Th>Scheduled for</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {jobs.data.map((job) => (
                  <tr key={job.id} className="hover:bg-raised">
                    <Td>
                      <Link to={`jobs/${encodeURIComponent(job.id)}`}>
                        <Mono title={job.id}>{shortId(job.id, 13, 6)}</Mono>
                      </Link>
                    </Td>
                    <Td>
                      <Badge tone="accent">{job.kind}</Badge>
                    </Td>
                    <Td className="text-muted">{job.targetName}</Td>
                    <Td>
                      <JobStatusBadge status={job.status} />
                    </Td>
                    <Td>
                      <JobProgressBar done={job.doneItems} failed={job.failedItems} total={job.totalItems} />
                    </Td>
                    <Td className="text-muted" title={absoluteTime(job.scheduledFor)}>
                      {relativeTime(job.scheduledFor)}
                    </Td>
                    <Td className="text-right">
                      {meta.roles.canOperate &&
                        (job.status === 'Pending' || job.status === 'Leased' || job.status === 'Running') && (
                          <Button tone="danger" busy={cancel.isPending} onClick={() => cancel.mutate(job.id)}>
                            Cancel
                          </Button>
                        )}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>
    </>
  );
}
