import { useState, type FormEvent, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { absoluteTime, count, relativeTime, percent } from '../lib/format';
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
  TextInput,
  Th,
} from '../components/ui';
import { StatusBadge } from './experiments';
import type { CanaryDecisionKind, TraconMetaResponse as Meta } from '@tracon/client';
import type {
  CanaryPolicy,
  Experiment,
  ExperimentCanaryResponse,
  ExperimentResultsResponse,
} from '../lib/server-types';

function emptyCanaryForm(variantName: string): CanaryFormState {
  return { canaryVariant: variantName, maxErrorRateDelta: '', minScore: '', minSampleSize: 20, rampSteps: '', rampIntervalHours: 1 };
}

interface CanaryFormState {
  canaryVariant: string;
  maxErrorRateDelta: string;
  minScore: string;
  minSampleSize: number;
  rampSteps: string;
  rampIntervalHours: number;
}

function toCanaryPolicy(form: CanaryFormState): CanaryPolicy {
  return {
    canaryVariant: form.canaryVariant,
    maxErrorRateDelta: form.maxErrorRateDelta.trim() === '' ? null : Number(form.maxErrorRateDelta) / 100,
    minScore: form.minScore.trim() === '' ? null : Number(form.minScore),
    minSampleSize: form.minSampleSize,
    rampSteps:
      form.rampSteps.trim() === ''
        ? []
        : form.rampSteps
            .split(',')
            .map((step) => Number(step.trim()))
            .filter((step) => Number.isFinite(step)),
    rampInterval: `${form.rampIntervalHours}:00:00`,
  };
}

function CanaryDecisionBadge({ decision }: { decision: CanaryDecisionKind }): ReactNode {
  const t = useT();

  switch (decision) {
    case 'RollBack':
      return <Badge tone="danger">{t('experiments.canary.decision.rollBack')}</Badge>;
    case 'Healthy':
      return <Badge tone="success">{t('experiments.canary.decision.healthy')}</Badge>;
    default:
      return <Badge tone="neutral">{t('experiments.canary.decision.insufficientData')}</Badge>;
  }
}

export function ExperimentDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const [showCanaryForm, setShowCanaryForm] = useState(false);
  const [canaryForm, setCanaryForm] = useState<CanaryFormState>(emptyCanaryForm(''));

  const experiment = useQuery({
    queryKey: ['experiment', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/experiments/{name}', { params: { path: { name } } }),
      ) as Promise<Experiment>,
  });

  const results = useQuery({
    queryKey: ['experimentResults', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/experiments/{name}/results', { params: { path: { name } } }),
      ) as Promise<ExperimentResultsResponse>,
    refetchInterval: 5_000,
  });

  const twoArmed = experiment.data != null && experiment.data.variants.length === 2;

  const canary = useQuery({
    queryKey: ['experimentCanary', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/experiments/{name}/canary', { params: { path: { name } } }),
      ) as Promise<ExperimentCanaryResponse>,
    enabled: twoArmed,
    refetchInterval: 5_000,
  });

  const invalidate = async (): Promise<void> => {
    await queryClient.invalidateQueries({ queryKey: ['experiment', name] });
    await queryClient.invalidateQueries({ queryKey: ['experiments'] });
    await queryClient.invalidateQueries({ queryKey: ['experimentCanary', name] });
  };

  const setCanary = useMutation({
    // The endpoint accepts a `null` body to remove the canary policy, but the
    // generated request type does not model body-level nullability (only
    // property nullability) — ASP.NET Core's OpenAPI generator does not
    // describe a nullable request body at all, verified against the source
    // document (no `nullable`/`oneOf` wrapping on this operation's requestBody).
    mutationFn: (policy: CanaryPolicy | null) =>
      unwrap(
        client.PUT('/api/experiments/{name}/canary', {
          params: { path: { name } },
          body: policy as NonNullable<typeof policy>,
        }),
      ) as Promise<Experiment>,
    onSuccess: async () => {
      setShowCanaryForm(false);
      await invalidate();
    },
  });

  const start = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/experiments/{name}/start', { params: { path: { name } } }),
      ) as Promise<Experiment>,
    onSuccess: invalidate,
  });

  const stop = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/experiments/{name}/stop', { params: { path: { name } } }),
      ) as Promise<Experiment>,
    onSuccess: invalidate,
  });

  if (experiment.isPending) {
    return <Loading />;
  }

  if (experiment.isError) {
    return <ErrorNote error={experiment.error} />;
  }

  const data = experiment.data;

  return (
    <>
      <PageHeader
        title={data.name}
        description={t('experiments.splits', {
          agent: data.agentName,
          count: data.variants.length,
        })}
        actions={
          meta.roles.canAdminister && (
            <>
              {data.status === 'Draft' && (
                <Button tone="primary" testId="experiment-start" busy={start.isPending} onClick={() => start.mutate()}>
                  {t('experiments.start')}
                </Button>
              )}
              {data.status === 'Running' && (
                <Button tone="danger" testId="experiment-stop" busy={stop.isPending} onClick={() => stop.mutate()}>
                  {t('workflowDetail.stop')}
                </Button>
              )}
            </>
          )
        }
      />

      {(start.isError || stop.isError) && (
        <div className="mb-4">
          <ErrorNote error={start.error ?? stop.error} />
        </div>
      )}

      <Panel title={t('experiments.configuration')} className="mb-4">
        <div className="p-4">
          <dl className="mb-4 flex flex-wrap gap-6 text-base">
            <div>
              <dt className="text-xs text-subtle uppercase">{t('common.status')}</dt>
              <dd className="mt-0.5"><StatusBadge status={data.status} /></dd>
            </div>
            <div>
              <dt className="text-xs text-subtle uppercase">{t('common.started')}</dt>
              <dd className="mt-0.5 text-muted" title={absoluteTime(data.startedAt)}>{relativeTime(data.startedAt)}</dd>
            </div>
            <div>
              <dt className="text-xs text-subtle uppercase">{t('experiments.ended')}</dt>
              <dd className="mt-0.5 text-muted" title={absoluteTime(data.endedAt)}>{relativeTime(data.endedAt)}</dd>
            </div>
          </dl>

          <Table>
            <thead>
              <tr>
                <Th>{t('experiments.variant')}</Th>
                <Th>{t('agentDetail.version')}</Th>
                <Th>{t('experiments.weightColumn')}</Th>
              </tr>
            </thead>
            <tbody>
              {data.variants.map((variant) => (
                <tr key={variant.name}>
                  <Td><Badge tone="accent">{variant.name}</Badge></Td>
                  <Td><Mono>v{variant.version}</Mono></Td>
                  <Td className="text-muted">{variant.weight}%</Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      </Panel>

      <Panel
        title={t('experiments.results')}
        actions={
          <span className="text-xs text-subtle">
            {t('experiments.resultsNote')}
          </span>
        }
      >
        {results.isPending && <Loading />}
        {results.isError && (
          <div className="p-4">
            <ErrorNote error={results.error} />
          </div>
        )}

        {results.isSuccess &&
          (results.data.results.length === 0 ? (
            <Empty title={t('experiments.noTraffic.title')}>{t('experiments.noTraffic.body')}</Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>{t('experiments.variant')}</Th>
                  <Th>{t('agentDetail.version')}</Th>
                  <Th>{t('nav.runs')}</Th>
                  <Th>{t('runs.filter.completed')}</Th>
                  <Th>{t('runs.stat.failed')}</Th>
                  <Th>{t('runs.stat.errorRate')}</Th>
                  <Th>{t('experiments.totalTokens')}</Th>
                  <Th>{t('experiments.avgDuration')}</Th>
                </tr>
              </thead>
              <tbody>
                {results.data.results.map((result) => (
                  <tr key={result.variant} className="hover:bg-raised">
                    <Td><Badge tone="accent">{result.variant}</Badge></Td>
                    <Td><Mono>v{result.version}</Mono></Td>
                    <Td className="text-muted">{result.totalRuns}</Td>
                    <Td className="text-muted">{result.completedRuns}</Td>
                    <Td className="text-muted">{result.failedRuns}</Td>
                    <Td className="text-muted">{percent(result.errorRate)}</Td>
                    <Td className="text-muted">{count(result.totalTokens)}</Td>
                    <Td className="text-muted">
                      {result.averageDurationMs != null ? `${Math.round(result.averageDurationMs)}ms` : '—'}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>

      {twoArmed && (
        <Panel
          title={t('experiments.canary.title')}
          className="mt-4"
          actions={
            meta.roles.canAdminister &&
            data.canary == null &&
            !showCanaryForm && (
              <Button
                testId="canary-enable"
                onClick={() => {
                  setCanaryForm(emptyCanaryForm(data.variants[1]?.name ?? data.variants[0]?.name ?? ''));
                  setShowCanaryForm(true);
                }}
              >
                {t('experiments.canary.enable')}
              </Button>
            )
          }
        >
          {data.rollbackReason != null && (
            <div className="border-b border-line bg-danger/10 p-4 text-base">
              <span className="font-medium text-danger">{t('experiments.canary.rolledBack')}</span>{' '}
              <span className="text-muted">{data.rollbackReason}</span>
            </div>
          )}

          {showCanaryForm && (
            <form
              className="grid gap-3 border-b border-line p-4"
              onSubmit={(event: FormEvent) => {
                event.preventDefault();
                setCanary.mutate(toCanaryPolicy(canaryForm));
              }}
            >
              <div className="grid gap-3 sm:grid-cols-2">
                <Field label={t('experiments.canary.canaryVariant')}>
                  <Select
                    value={canaryForm.canaryVariant}
                    testId="canary-variant"
                    onChange={(value) => setCanaryForm({ ...canaryForm, canaryVariant: value })}
                  >
                    {data.variants.map((variant) => (
                      <option key={variant.name} value={variant.name}>
                        {variant.name}
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label={t('experiments.canary.minSampleSize')} hint={t('experiments.canary.minSampleSizeHint')}>
                  <TextInput
                    type="number"
                    min={1}
                    value={canaryForm.minSampleSize}
                    data-testid="canary-min-sample-size"
                    onChange={(event) => setCanaryForm({ ...canaryForm, minSampleSize: Number(event.target.value) })}
                  />
                </Field>
                <Field label={t('experiments.canary.maxErrorRateDelta')} hint={t('experiments.canary.maxErrorRateDeltaHint')}>
                  <TextInput
                    type="number"
                    min={0}
                    max={100}
                    placeholder="10"
                    value={canaryForm.maxErrorRateDelta}
                    data-testid="canary-max-error-rate-delta"
                    onChange={(event) => setCanaryForm({ ...canaryForm, maxErrorRateDelta: event.target.value })}
                  />
                </Field>
                <Field label={t('experiments.canary.minScore')} hint={t('experiments.canary.minScoreHint')}>
                  <TextInput
                    type="number"
                    min={0}
                    max={100}
                    placeholder="60"
                    value={canaryForm.minScore}
                    data-testid="canary-min-score"
                    onChange={(event) => setCanaryForm({ ...canaryForm, minScore: event.target.value })}
                  />
                </Field>
                <Field label={t('experiments.canary.rampSteps')} hint={t('experiments.canary.rampStepsHint')}>
                  <TextInput
                    placeholder="5, 25, 50, 100"
                    value={canaryForm.rampSteps}
                    data-testid="canary-ramp-steps"
                    onChange={(event) => setCanaryForm({ ...canaryForm, rampSteps: event.target.value })}
                  />
                </Field>
                <Field label={t('experiments.canary.rampIntervalHours')}>
                  <TextInput
                    type="number"
                    min={0}
                    step={0.5}
                    value={canaryForm.rampIntervalHours}
                    data-testid="canary-ramp-interval"
                    onChange={(event) => setCanaryForm({ ...canaryForm, rampIntervalHours: Number(event.target.value) })}
                  />
                </Field>
              </div>

              <div className="flex items-center gap-2">
                <Button type="submit" tone="primary" testId="canary-save" busy={setCanary.isPending}>
                  {t('common.save')}
                </Button>
                <Button tone="ghost" onClick={() => setShowCanaryForm(false)}>
                  {t('common.cancel')}
                </Button>
                {setCanary.isError && <ErrorNote error={setCanary.error} />}
              </div>
            </form>
          )}

          {canary.isPending && <Loading />}
          {canary.isError && (
            <div className="p-4">
              <ErrorNote error={canary.error} />
            </div>
          )}

          {canary.isSuccess &&
            (canary.data.policy == null ? (
              !showCanaryForm && <Empty title={t('experiments.canary.empty.title')}>{t('experiments.canary.empty.body')}</Empty>
            ) : (
              <div className="p-4">
                <dl className="mb-4 flex flex-wrap gap-6 text-base">
                  <div>
                    <dt className="text-xs text-subtle uppercase">{t('experiments.canary.canaryVariant')}</dt>
                    <dd className="mt-0.5"><Badge tone="accent">{canary.data.policy.canaryVariant}</Badge></dd>
                  </div>
                  <div>
                    <dt className="text-xs text-subtle uppercase">{t('experiments.canary.maxErrorRateDelta')}</dt>
                    <dd className="mt-0.5 text-muted">
                      {canary.data.policy.maxErrorRateDelta != null ? percent(canary.data.policy.maxErrorRateDelta) : '—'}
                    </dd>
                  </div>
                  <div>
                    <dt className="text-xs text-subtle uppercase">{t('experiments.canary.minScore')}</dt>
                    <dd className="mt-0.5 text-muted">{canary.data.policy.minScore ?? '—'}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-subtle uppercase">{t('experiments.canary.rampSteps')}</dt>
                    <dd className="mt-0.5 text-muted">
                      {canary.data.policy.rampSteps.length > 0 ? canary.data.policy.rampSteps.join(' → ') : '—'}
                    </dd>
                  </div>
                  {canary.data.evaluation != null && (
                    <div>
                      <dt className="text-xs text-subtle uppercase">{t('experiments.canary.lastEvaluation')}</dt>
                      <dd className="mt-0.5"><CanaryDecisionBadge decision={canary.data.evaluation.decision} /></dd>
                    </div>
                  )}
                </dl>

                {canary.data.evaluation != null && (
                  <p className="mb-4 text-base text-muted" title={absoluteTime(canary.data.evaluation.evaluatedAt)}>
                    {canary.data.evaluation.reason}
                  </p>
                )}

                {meta.roles.canAdminister && (
                  <Button tone="danger" testId="canary-remove" busy={setCanary.isPending} onClick={() => setCanary.mutate(null)}>
                    {t('experiments.canary.remove')}
                  </Button>
                )}
              </div>
            ))}
        </Panel>
      )}
    </>
  );
}
