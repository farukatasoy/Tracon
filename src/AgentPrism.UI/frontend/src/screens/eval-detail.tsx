import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Field,
  JsonView,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  TextArea,
  TextInput,
  Th,
} from '../components/ui';
import { PassRateBar } from './evals';
import type {
  EvalCaseInput,
  EvalRunStatus,
  AgentPrismMetaResponse as Meta,
} from '@agentprism/client';
import type { EvalCase, EvalRun, EvalSuite } from '../lib/server-types';

function emptyCase(): EvalCaseInput {
  return { query: '', expectedOutput: null, expectedTools: [], context: null };
}

function StatusBadge({ status }: { status: EvalRunStatus }): ReactNode {
  const t = useT();

  switch (status) {
    case 'Completed':
      return <Badge tone="success">{t('runs.status.completed')}</Badge>;
    case 'Failed':
      return <Badge tone="danger">{t('runs.status.failed')}</Badge>;
    case 'Cancelled':
      return <Badge tone="warn">{t('runs.status.canceled')}</Badge>;
    case 'Running':
      return <Badge tone="info">{t('runs.status.running')}</Badge>;
    default:
      return <Badge>{t('jobs.status.pending')}</Badge>;
  }
}

function CaseEditor({
  evalCase,
  onChange,
  onRemove,
}: {
  evalCase: EvalCaseInput;
  onChange: (value: EvalCaseInput) => void;
  onRemove: () => void;
}): ReactNode {
  const t = useT();

  return (
    <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-2">
      <div className="sm:col-span-2">
        <Field label={t('evals.query')} required>
          <TextArea
            rows={2}
            value={evalCase.query}
            placeholder={t('evals.queryPlaceholder')}
            onChange={(event) => onChange({ ...evalCase, query: event.target.value })}
          />
        </Field>
      </div>
      <Field label={t('evals.expectedOutput')} hint={t('evals.expectedOutputHint')}>
        <TextInput
          value={evalCase.expectedOutput ?? ''}
          onChange={(event) => onChange({ ...evalCase, expectedOutput: event.target.value || null })}
        />
      </Field>
      <Field label={t('evals.expectedTools')} hint={t('evals.expectedToolsHint')}>
        <TextInput
          value={(evalCase.expectedTools ?? []).join(', ')}
          onChange={(event) =>
            onChange({
              ...evalCase,
              expectedTools: event.target.value
                .split(',')
                .map((name) => name.trim())
                .filter((name) => name.length > 0),
            })
          }
        />
      </Field>
      <div className="sm:col-span-2">
        <Field label={t('agentEditor.context')} hint={t('evals.contextHint')}>
          <TextInput
            value={evalCase.context ?? ''}
            onChange={(event) => onChange({ ...evalCase, context: event.target.value || null })}
          />
        </Field>
      </div>
      <div>
        <Button tone="danger" onClick={onRemove}>
          {t('evals.removeCase')}
        </Button>
      </div>
    </div>
  );
}

export function EvalSuiteDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const [cases, setCases] = useState<EvalCaseInput[]>([]);

  const suite = useQuery({
    queryKey: ['evalSuite', name],
    queryFn: () =>
      unwrap(client.GET('/api/evals/{name}', { params: { path: { name } } })) as Promise<EvalSuite>,
  });
  const existingCases = useQuery({
    queryKey: ['evalCases', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/evals/{name}/cases', { params: { path: { name } } }),
      ) as Promise<EvalCase[]>,
  });
  const runs = useQuery({
    queryKey: ['evalRuns', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/evals/{name}/runs', { params: { path: { name }, query: { take: 20 } } }),
      ) as Promise<EvalRun[]>,
    refetchInterval: 5_000,
  });

  useEffect(() => {
    if (!existingCases.isSuccess) return;

    setCases(
      existingCases.data.map((item) => ({
        query: item.query,
        expectedOutput: item.expectedOutput ?? null,
        expectedTools: item.expectedTools,
        context: item.context ?? null,
      })),
    );
  }, [existingCases.isSuccess, existingCases.data]);

  const saveCases = useMutation({
    mutationFn: () =>
      unwrap(client.PUT('/api/evals/{name}/cases', { params: { path: { name } }, body: cases })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['evalCases', name] }),
  });

  const trigger = useMutation({
    mutationFn: () =>
      unwrap(client.POST('/api/evals/{name}/run', { params: { path: { name } }, body: {} })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['evalRuns', name] }),
  });

  if (suite.isPending) {
    return <Loading />;
  }

  if (suite.isError) {
    return <ErrorNote error={suite.error} />;
  }

  return (
    <>
      <PageHeader
        title={suite.data.name}
        description={suite.data.description ?? t('evals.measures', { agent: suite.data.agentName })}
        actions={
          meta.roles.canOperate && (
            <Button
              tone="primary"
              busy={trigger.isPending}
              disabled={cases.length === 0}
              onClick={() => trigger.mutate()}
            >
              {t('evals.runNow')}
            </Button>
          )
        }
      />

      {trigger.isError && (
        <div className="mb-4">
          <ErrorNote error={trigger.error} />
        </div>
      )}

      <Panel title={t('evals.checks')} className="mb-4">
        <div className="p-4">
          <JsonView value={suite.data.checks} maxHeight="10rem" />
        </div>
      </Panel>

      <Panel title={t('evals.cases')} className="mb-4">
        <div className="flex flex-col gap-3 p-4">
          {existingCases.isPending && <Loading />}
          {cases.length === 0 && !existingCases.isPending && (
            <Empty title={t('evals.noCasesYet')}>{t('evals.noCasesBody')}</Empty>
          )}
          {cases.map((item, index) => (
            <CaseEditor
              key={index}
              evalCase={item}
              onChange={(value) => setCases(cases.map((current, i) => (i === index ? value : current)))}
              onRemove={() => setCases(cases.filter((_, i) => i !== index))}
            />
          ))}

          {meta.roles.canAdminister && (
            <div className="flex items-center gap-2 border-t border-line pt-3">
              <Button onClick={() => setCases([...cases, emptyCase()])}>{t('evals.addCase')}</Button>
              <Button
                tone="primary"
                busy={saveCases.isPending}
                disabled={cases.some((item) => item.query.trim().length === 0)}
                onClick={() => saveCases.mutate()}
              >
                {t('evals.saveCases')}
              </Button>
              {saveCases.isError && <ErrorNote error={saveCases.error} />}
            </div>
          )}
        </div>
      </Panel>

      <Panel title={t('nav.runs')}>
        {runs.isPending && <Loading />}
        {runs.isError && (
          <div className="p-4">
            <ErrorNote error={runs.error} />
          </div>
        )}

        {runs.isSuccess &&
          (runs.data.length === 0 ? (
            <Empty title={t('evals.noRuns.title')}>{t('evals.noRuns.body')}</Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>{t('runs.column.run')}</Th>
                  <Th>{t('common.status')}</Th>
                  <Th>{t('evals.passRate')}</Th>
                  <Th>{t('agentDetail.version')}</Th>
                  <Th>{t('common.model')}</Th>
                  <Th>{t('common.started')}</Th>
                </tr>
              </thead>
              <tbody>
                {runs.data.map((run) => (
                  <tr key={run.id} className="hover:bg-raised">
                    <Td>
                      <Link to={`evals/runs/${encodeURIComponent(run.id)}`}>
                        <Mono title={run.id}>{shortId(run.id, 13, 6)}</Mono>
                      </Link>
                    </Td>
                    <Td>
                      <StatusBadge status={run.status} />
                    </Td>
                    <Td>
                      <PassRateBar passed={run.passed} failed={run.failed} total={run.total} />
                    </Td>
                    <Td className="text-muted">{run.agentVersion ?? '—'}</Td>
                    <Td className="text-muted">{run.modelId ?? '—'}</Td>
                    <Td className="text-muted" title={absoluteTime(run.startedAt)}>
                      {relativeTime(run.startedAt)}
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
