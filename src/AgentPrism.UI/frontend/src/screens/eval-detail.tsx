import { useEffect, useState, type ReactNode } from 'react';
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
import type { EvalCaseInput, EvalRunStatus, Meta } from '../lib/types';

function emptyCase(): EvalCaseInput {
  return { query: '', expectedOutput: null, expectedTools: [], context: null };
}

function StatusBadge({ status }: { status: EvalRunStatus }): ReactNode {
  switch (status) {
    case 'Completed':
      return <Badge tone="success">completed</Badge>;
    case 'Failed':
      return <Badge tone="danger">failed</Badge>;
    case 'Cancelled':
      return <Badge tone="warn">cancelled</Badge>;
    case 'Running':
      return <Badge tone="info">running</Badge>;
    default:
      return <Badge>pending</Badge>;
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
  return (
    <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-2">
      <div className="sm:col-span-2">
        <Field label="Query" required>
          <TextArea
            rows={2}
            value={evalCase.query}
            placeholder="What is your return policy?"
            onChange={(event) => onChange({ ...evalCase, query: event.target.value })}
          />
        </Field>
      </div>
      <Field label="Expected output" hint="Used by the containsExpected check.">
        <TextInput
          value={evalCase.expectedOutput ?? ''}
          onChange={(event) => onChange({ ...evalCase, expectedOutput: event.target.value || null })}
        />
      </Field>
      <Field label="Expected tools" hint="Comma-separated. Used by the toolCalled check.">
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
        <Field label="Context" hint="Extra context handed to the agent alongside the query.">
          <TextInput
            value={evalCase.context ?? ''}
            onChange={(event) => onChange({ ...evalCase, context: event.target.value || null })}
          />
        </Field>
      </div>
      <div>
        <Button tone="danger" onClick={onRemove}>
          Remove case
        </Button>
      </div>
    </div>
  );
}

export function EvalSuiteDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const client = useQueryClient();
  const [cases, setCases] = useState<EvalCaseInput[]>([]);

  const suite = useQuery({ queryKey: ['evalSuite', name], queryFn: () => api.evalSuite(name) });
  const existingCases = useQuery({ queryKey: ['evalCases', name], queryFn: () => api.evalCases(name) });
  const runs = useQuery({
    queryKey: ['evalRuns', name],
    queryFn: () => api.evalRuns(name, { take: 20 }),
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
    mutationFn: () => api.saveEvalCases(name, cases),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['evalCases', name] }),
  });

  const trigger = useMutation({
    mutationFn: () => api.triggerEvalRun(name),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['evalRuns', name] }),
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
        description={suite.data.description ?? `Measures ${suite.data.agentName}.`}
        actions={
          meta.roles.canOperate && (
            <Button
              tone="primary"
              busy={trigger.isPending}
              disabled={cases.length === 0}
              onClick={() => trigger.mutate()}
            >
              Run now
            </Button>
          )
        }
      />

      {trigger.isError && (
        <div className="mb-4">
          <ErrorNote error={trigger.error} />
        </div>
      )}

      <Panel title="Checks" className="mb-4">
        <div className="p-4">
          <JsonView value={suite.data.checks} maxHeight="10rem" />
        </div>
      </Panel>

      <Panel title="Cases" className="mb-4">
        <div className="flex flex-col gap-3 p-4">
          {existingCases.isPending && <Loading />}
          {cases.length === 0 && !existingCases.isPending && (
            <Empty title="No cases yet">Add a query below, then save.</Empty>
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
              <Button onClick={() => setCases([...cases, emptyCase()])}>Add case</Button>
              <Button
                tone="primary"
                busy={saveCases.isPending}
                disabled={cases.some((item) => item.query.trim().length === 0)}
                onClick={() => saveCases.mutate()}
              >
                Save cases
              </Button>
              {saveCases.isError && <ErrorNote error={saveCases.error} />}
            </div>
          )}
        </div>
      </Panel>

      <Panel title="Runs">
        {runs.isPending && <Loading />}
        {runs.isError && (
          <div className="p-4">
            <ErrorNote error={runs.error} />
          </div>
        )}

        {runs.isSuccess &&
          (runs.data.length === 0 ? (
            <Empty title="No runs yet">Run the suite above to see results here.</Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Run</Th>
                  <Th>Status</Th>
                  <Th>Pass rate</Th>
                  <Th>Version</Th>
                  <Th>Model</Th>
                  <Th>Started</Th>
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
