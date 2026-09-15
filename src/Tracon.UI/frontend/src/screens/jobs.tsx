import { useState, type FormEvent, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client as apiClient, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT } from '../lib/i18n';
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
import { Toolbar, ToolbarField } from '../components/toolbar';
import { Tooltip } from '../components/tooltip';
import { PlusIcon, TrashIcon } from '../components/icons';
import type { TraconMetaResponse as Meta, JobStatus } from '@tracon/client';
import type { JobRecord, JobSchedule } from '../lib/server-types';

const EMPTY_FORM = {
  name: '',
  handlerKey: '',
  targetName: '',
  lane: '',
  cron: '',
  timeZone: 'UTC',
  payload: '[]',
  enabled: true,
};

type ScheduleForm = typeof EMPTY_FORM;

export function JobStatusBadge({ status }: { status: JobStatus }): ReactNode {
  const t = useT();

  switch (status) {
    case 'Completed':
      return <Badge tone="success">{t('runs.status.completed')}</Badge>;
    case 'Failed':
      return <Badge tone="danger">{t('runs.status.failed')}</Badge>;
    case 'Cancelled':
      return <Badge tone="warn">{t('runs.status.canceled')}</Badge>;
    case 'Leased':
      return <Badge tone="info">{t('jobs.status.leased')}</Badge>;
    case 'Running':
      return <Badge tone="info">{t('runs.status.running')}</Badge>;
    default:
      return <Badge>{t('jobs.status.pending')}</Badge>;
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
  const t = useT();

  if (total === 0) {
    return <span className="text-xs text-subtle">{t('jobs.noItems')}</span>;
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
      <span className="text-xs text-muted">
        {done + failed}/{total}
      </span>
    </div>
  );
}

function toForm(schedule: JobSchedule): ScheduleForm {
  return {
    name: schedule.name,
    handlerKey: schedule.handlerKey,
    targetName: schedule.targetName,
    lane: schedule.lane === 'default' ? '' : schedule.lane,
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
  const t = useT();
  const client = useQueryClient();
  const [form, setForm] = useState<ScheduleForm>(EMPTY_FORM);
  const [showForm, setShowForm] = useState(false);
  const [payloadError, setPayloadError] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [laneFilter, setLaneFilter] = useState('');

  const schedules = useQuery({
    queryKey: ['schedules'],
    queryFn: () => unwrap(apiClient.GET('/api/schedules')) as Promise<JobSchedule[]>,
  });
  // The keys the server will actually accept. Hard-coding them here is what
  // made the old screen offer exactly two of the nine built-in kinds and none
  // of a consumer's own; the allow-list lives in the server's settings, so the
  // dropdown reads it from there.
  const handlerKeys = useQuery({
    queryKey: ['schedule-handler-keys'],
    queryFn: () => unwrap(apiClient.GET('/api/schedules/handler-keys')) as Promise<string[]>,
    enabled: meta.roles.canAdminister,
  });
  const jobs = useQuery({
    queryKey: ['jobs', laneFilter],
    queryFn: () =>
      unwrap(
        apiClient.GET('/api/jobs', {
          params: { query: { take: 50, lane: laneFilter.length > 0 ? laneFilter : undefined } },
        }),
      ) as Promise<JobRecord[]>,
    // A running job's counters move on their own; the list should follow.
    refetchInterval: 5_000,
  });

  const invalidateSchedules = (): void => void client.invalidateQueries({ queryKey: ['schedules'] });
  const invalidateJobs = (): void => void client.invalidateQueries({ queryKey: ['jobs'] });

  const save = useMutation({
    mutationFn: () => {
      const payload = JSON.parse(form.payload) as unknown;

      return unwrap(
        apiClient.PUT('/api/schedules/{name}', {
          params: { path: { name: form.name } },
          body: {
            handlerKey: form.handlerKey,
            targetName: form.targetName,
            lane: form.lane.length > 0 ? form.lane : null,
            cron: form.cron.length > 0 ? form.cron : null,
            timeZone: form.timeZone,
            payload,
            enabled: form.enabled,
          },
        }),
      ) as Promise<JobSchedule>;
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
    mutationFn: (name: string) =>
      unwrap(apiClient.DELETE('/api/schedules/{name}', { params: { path: { name } } })),
    onSuccess: invalidateSchedules,
  });

  const trigger = useMutation({
    mutationFn: (name: string) =>
      unwrap(
        apiClient.POST('/api/schedules/{name}/trigger', { params: { path: { name } } }),
      ) as Promise<JobRecord>,
    onSuccess: invalidateJobs,
  });

  const cancel = useMutation({
    mutationFn: (id: string) =>
      unwrap(apiClient.POST('/api/jobs/{id}/cancel', { params: { path: { id } } })),
    onSuccess: invalidateJobs,
  });

  const submit = (event: FormEvent): void => {
    event.preventDefault();
    setPayloadError(null);

    try {
      JSON.parse(form.payload);
    } catch {
      setPayloadError(t('jobs.payloadError'));
      return;
    }

    save.mutate();
  };

  return (
    <>
      <PageHeader
        title={t('nav.jobs')}
        description={t('jobs.description')}
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
              {t('jobs.newSchedule')}
            </Button>
          )
        }
      />

      {!meta.storage.jobWorkerEnabled && (
        <Panel className="mb-4">
          <div className="border-b border-line px-4 py-2.5 text-sm text-muted">
            <strong className="text-fg">{t('jobs.workerOff.title')}</strong> {t('jobs.workerOff.body')}{' '}
            <Mono className="ml-1">RunWorker</Mono>.
          </div>
        </Panel>
      )}

      {showForm && (
        <Panel
          title={editing != null ? t('jobs.editSchedule', { name: editing }) : t('jobs.newSchedule')}
          className="mb-4"
        >
          <form className="grid gap-3 p-4 sm:grid-cols-2" onSubmit={submit}>
            <Field label={t('common.name')} required>
              <TextInput
                value={form.name}
                required
                disabled={editing != null}
                pattern="[a-zA-Z0-9_-]+"
                placeholder="nightly-report"
                onChange={(event) => setForm({ ...form, name: event.target.value })}
              />
            </Field>

            <Field label={t('jobs.handlerKey')} required hint={t('jobs.handlerKeyHint')}>
              <Select
                value={form.handlerKey}
                testId="schedule-handler-key"
                onChange={(value) => setForm({ ...form, handlerKey: value })}
              >
                <option value="">{t('jobs.handlerKeyPlaceholder')}</option>
                {(handlerKeys.data ?? []).map((key) => (
                  <option key={key} value={key}>
                    {key}
                  </option>
                ))}
              </Select>
            </Field>

            <Field
              label={t('jobs.targetName')}
              required
              hint={t('jobs.targetHint')}
            >
              <TextInput
                value={form.targetName}
                required
                placeholder="summarizer"
                onChange={(event) => setForm({ ...form, targetName: event.target.value })}
              />
            </Field>

            <Field
              label={t('jobs.cron')}
              hint={t('jobs.cronHint')}
            >
              <TextInput
                value={form.cron}
                placeholder="0 3 * * *"
                onChange={(event) => setForm({ ...form, cron: event.target.value })}
              />
            </Field>

            <Field label={t('jobs.timeZone')}>
              <TextInput
                value={form.timeZone}
                placeholder="UTC"
                onChange={(event) => setForm({ ...form, timeZone: event.target.value })}
              />
            </Field>

            <Field label={t('jobs.lane')} hint={t('jobs.laneHint')}>
              <TextInput
                value={form.lane}
                pattern="[a-z0-9][a-z0-9._-]{0,63}"
                placeholder="default"
                onChange={(event) => setForm({ ...form, lane: event.target.value })}
              />
            </Field>

            <label className="flex items-end gap-2 pb-1.5 text-base">
              <input
                type="checkbox"
                checked={form.enabled}
                onChange={(event) => setForm({ ...form, enabled: event.target.checked })}
              />
              {t('common.enabled')}
            </label>

            <div className="sm:col-span-2">
              <Field
                label={t('jobs.payload')}
                hint={t('jobs.payloadHint')}
                error={payloadError ?? undefined}
              >
                {(ids) => (
                  <TextArea
                    {...ids}
                    value={form.payload}
                    rows={4}
                    onChange={(event) => setForm({ ...form, payload: event.target.value })}
                  />
                )}
              </Field>
            </div>

            <div className="sm:col-span-2 flex items-center gap-2">
              <Button type="submit" tone="primary" busy={save.isPending}>
                {t('common.save')}
              </Button>
              <Button
                tone="ghost"
                onClick={() => {
                  setShowForm(false);
                  setEditing(null);
                }}
              >
                {t('common.cancel')}
              </Button>
              {save.isError && <ErrorNote error={save.error} onRetry={() => save.mutate()} />}
            </div>
          </form>
        </Panel>
      )}

      {/* Two mutations that used to fail silently: a trigger that the server
          refused, and a delete that did not take. Both leave the row exactly as
          it was, so without saying so the operator concludes it worked. */}
      {(trigger.isError || remove.isError) && (
        <div className="mb-4 flex flex-col gap-2">
          {trigger.isError && (
            <ErrorNote
              error={trigger.error}
              onRetry={
                trigger.variables === undefined ? undefined : () => trigger.mutate(trigger.variables)
              }
            />
          )}
          {remove.isError && (
            <ErrorNote
              error={remove.error}
              onRetry={
                remove.variables === undefined ? undefined : () => remove.mutate(remove.variables)
              }
            />
          )}
        </div>
      )}

      <Panel title={t('jobs.schedules')} className="mb-4">
        {schedules.isPending && <Loading rows={4} />}
        {schedules.isError && (
          <div className="p-4">
            <ErrorNote error={schedules.error} onRetry={() => void schedules.refetch()} />
          </div>
        )}

        {schedules.isSuccess &&
          (schedules.data.length === 0 ? (
            <Empty
              title={t('jobs.noSchedules.title')}
              action={
                meta.roles.canAdminister && (
                  <Button
                    tone="primary"
                    onClick={() => {
                      setForm(EMPTY_FORM);
                      setEditing(null);
                      setShowForm(true);
                    }}
                  >
                    {t('jobs.empty.action')}
                  </Button>
                )
              }
            >
              {t('jobs.noSchedules.body')}
            </Empty>
          ) : (
            <Table label={t('jobs.schedules')}>
              <thead>
                <tr>
                  <Th>{t('common.name')}</Th>
                  <Th>{t('jobs.handlerKey')}</Th>
                  <Th>{t('jobs.target')}</Th>
                  <Th>{t('jobs.lane')}</Th>
                  <Th>{t('jobs.cron')}</Th>
                  <Th>{t('jobs.nextRun')}</Th>
                  <Th>{t('jobs.lastRun')}</Th>
                  <Th />
                  <Th />
                </tr>
              </thead>
              <tbody>
                {schedules.data.map((schedule) => (
                  <tr key={schedule.id} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      <Mono className="font-semibold">{schedule.name}</Mono>
                    </Td>
                    <Td>
                      <Badge tone="accent">{schedule.handlerKey}</Badge>
                    </Td>
                    <Td className="text-muted">{schedule.targetName}</Td>
                    <Td>
                      <Mono className="text-xs text-muted">{schedule.lane}</Mono>
                    </Td>
                    <Td>
                      {schedule.cron != null && schedule.cron.length > 0 ? (
                        <Mono className="text-xs">{schedule.cron}</Mono>
                      ) : (
                        <span className="text-xs text-subtle">{t('jobs.manualOnly')}</span>
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
                        <Badge tone="accent">{t('common.enabled')}</Badge>
                      ) : (
                        <Badge>{t('common.disabled')}</Badge>
                      )}
                    </Td>
                    <Td className="text-right">
                      <div className="flex items-center justify-end gap-1.5">
                        {meta.roles.canOperate && (
                          <Tooltip text={t('jobs.triggerTitle')}>
                            <Button
                              onClick={() => trigger.mutate(schedule.name)}
                              busy={trigger.isPending && trigger.variables === schedule.name}
                            >
                              {t('jobs.trigger')}
                            </Button>
                          </Tooltip>
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
                              {t('common.edit')}
                            </Button>
                            {/* No confirmation step: §175.3 fails on both
                                counts. `jobs.schedule_id` is ON DELETE SET
                                NULL, so the jobs it already started stay in
                                the list, and the schedule itself is typed back
                                from the form beside it. Layer 1 carries the
                                consequence; the click stays one click. */}
                            <Tooltip text={t('jobs.deleteScheduleEffect')}>
                              <Button
                                tone="danger"
                                ariaLabel={t('jobs.deleteSchedule')}
                                busy={remove.isPending && remove.variables === schedule.name}
                                onClick={() => remove.mutate(schedule.name)}
                              >
                                <TrashIcon className="size-3.5" />
                              </Button>
                            </Tooltip>
                          </>
                        )}
                      </div>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>

      <Toolbar onReset={laneFilter.length > 0 ? () => setLaneFilter('') : undefined}>
        <ToolbarField label={t('jobs.laneFilter')}>
          {(id) => (
            <TextInput
              id={id}
              className="h-8 w-40 py-0"
              value={laneFilter}
              placeholder={t('jobs.laneFilterPlaceholder')}
              onChange={(event) => setLaneFilter(event.target.value)}
            />
          )}
        </ToolbarField>
      </Toolbar>

      {cancel.isError && (
        <div className="mb-4">
          <ErrorNote
            error={cancel.error}
            onRetry={
              cancel.variables === undefined ? undefined : () => cancel.mutate(cancel.variables)
            }
          />
        </div>
      )}

      <Panel title={t('jobs.recent')}>
        {jobs.isPending && <Loading rows={8} />}
        {jobs.isError && (
          <div className="p-4">
            <ErrorNote error={jobs.error} onRetry={() => void jobs.refetch()} />
          </div>
        )}

        {jobs.isSuccess &&
          (jobs.data.length === 0 ? (
            /*
              🚨 No fabricated action. A job appears here because a schedule
              fired or an API caller queued one — a console cannot make one
              directly, so the honest next step is the schedule above.
            */
            <Empty
              title={laneFilter.length > 0 ? t('common.noResults') : t('jobs.noJobs.title')}
              action={
                laneFilter.length > 0 ? (
                  <Button onClick={() => setLaneFilter('')}>{t('toolbar.reset')}</Button>
                ) : undefined
              }
            >
              {laneFilter.length > 0 ? t('jobs.noJobs.filtered') : t('jobs.noJobs.body')}
            </Empty>
          ) : (
            <Table label={t('jobs.recent')}>
              <thead>
                <tr>
                  <Th>{t('jobs.job')}</Th>
                  <Th>{t('jobs.handlerKey')}</Th>
                  <Th>{t('jobs.target')}</Th>
                  <Th>{t('jobs.lane')}</Th>
                  <Th>{t('common.status')}</Th>
                  <Th>{t('jobs.progress')}</Th>
                  <Th>{t('jobs.scheduledFor')}</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {jobs.data.map((job) => (
                  <tr key={job.id} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      <Link to={`jobs/${encodeURIComponent(job.id)}`}>
                        <Mono title={job.id}>{shortId(job.id, 13, 6)}</Mono>
                      </Link>
                    </Td>
                    <Td>
                      <Badge tone="accent">{job.handlerKey}</Badge>
                    </Td>
                    <Td className="text-muted">{job.targetName}</Td>
                    <Td>
                      <Mono className="text-xs text-muted">{job.lane}</Mono>
                    </Td>
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
                          <Button
                            tone="danger"
                            busy={cancel.isPending && cancel.variables === job.id}
                            onClick={() => cancel.mutate(job.id)}
                          >
                            {t('common.cancel')}
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
