import { useState, type FormEvent, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { relativeTime } from '../lib/format';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Field,
  Loading,
  PageHeader,
  Panel,
  Table,
  Td,
  TextArea,
  TextInput,
  Th,
} from '../components/ui';
import { PlusIcon, TrashIcon } from '../components/icons';
import type { EvalSuite, Meta } from '../lib/types';

const EMPTY_FORM = {
  agentName: '',
  description: '',
  checks: '[\n  { "kind": "nonEmpty", "minLength": 10 }\n]',
};

type SuiteForm = typeof EMPTY_FORM;

/** A run's pass/fail split as a two-colour bar — the same shape as a job's item progress. */
export function PassRateBar({ passed, failed, total }: { passed: number; failed: number; total: number }): ReactNode {
  if (total === 0) {
    return <span className="text-[11px] text-subtle">no cases</span>;
  }

  const passedPct = (passed / total) * 100;
  const failedPct = (failed / total) * 100;

  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-24 overflow-hidden rounded-full bg-raised">
        <div className="flex h-full">
          <div className="h-full bg-success" style={{ width: `${passedPct}%` }} />
          <div className="h-full bg-danger" style={{ width: `${failedPct}%` }} />
        </div>
      </div>
      <span className="text-[11px] text-muted">
        {passed}/{total}
      </span>
    </div>
  );
}

function toForm(suite: EvalSuite): SuiteForm {
  return {
    agentName: suite.agentName,
    description: suite.description ?? '',
    checks: JSON.stringify(suite.checks ?? [], null, 2),
  };
}

/**
 * Eval suites: which agent a suite measures, and the checks it runs. Cases,
 * triggering and results live on the suite's own detail screen — the same
 * split as Workflows (list) versus a workflow's own page.
 */
export function EvalsScreen({ meta }: { meta: Meta }): ReactNode {
  const client = useQueryClient();
  const [form, setForm] = useState<SuiteForm>(EMPTY_FORM);
  const [showForm, setShowForm] = useState(false);
  const [checksError, setChecksError] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);

  const suites = useQuery({ queryKey: ['evalSuites'], queryFn: api.evalSuites });

  const invalidate = (): void => void client.invalidateQueries({ queryKey: ['evalSuites'] });

  const save = useMutation({
    mutationFn: () => {
      const name = editing ?? newName;
      const checks = JSON.parse(form.checks) as unknown;

      return api.saveEvalSuite(name, {
        agentName: form.agentName,
        description: form.description.length > 0 ? form.description : null,
        checks,
      });
    },
    onSuccess: () => {
      setForm(EMPTY_FORM);
      setNewName('');
      setShowForm(false);
      setEditing(null);
      setChecksError(null);
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: (name: string) => api.deleteEvalSuite(name),
    onSuccess: invalidate,
  });

  const [newName, setNewName] = useState('');

  const submit = (event: FormEvent): void => {
    event.preventDefault();
    setChecksError(null);

    try {
      JSON.parse(form.checks);
    } catch {
      setChecksError('Checks must be valid JSON — an array of check definitions.');
      return;
    }

    save.mutate();
  };

  return (
    <>
      <PageHeader
        title="Evals"
        description="A test suite for one agent: a set of queries with code-written checks, run on demand or after a version change. Each case runs in a fresh session and produces its own row in Runs."
        actions={
          meta.roles.canAdminister && (
            <Button
              tone="primary"
              onClick={() => {
                setForm(EMPTY_FORM);
                setNewName('');
                setEditing(null);
                setShowForm((current) => !current);
              }}
            >
              <PlusIcon className="size-3.5" />
              New suite
            </Button>
          )
        }
      />

      {showForm && (
        <Panel title={editing != null ? `Edit ${editing}` : 'New suite'} className="mb-4">
          <form className="grid gap-3 p-4 sm:grid-cols-2" onSubmit={submit}>
            <Field label="Name" required>
              <TextInput
                value={editing ?? newName}
                required
                disabled={editing != null}
                pattern="[a-zA-Z0-9_-]+"
                placeholder="customer-support-suite"
                onChange={(event) => setNewName(event.target.value)}
              />
            </Field>

            <Field label="Agent name" required hint="The agent this suite measures.">
              <TextInput
                value={form.agentName}
                required
                placeholder="customer-support-agent"
                onChange={(event) => setForm({ ...form, agentName: event.target.value })}
              />
            </Field>

            <div className="sm:col-span-2">
              <Field label="Description">
                <TextInput
                  value={form.description}
                  onChange={(event) => setForm({ ...form, description: event.target.value })}
                />
              </Field>
            </div>

            <div className="sm:col-span-2">
              <Field
                label="Checks"
                hint="JSON array. Built-in kinds: nonEmpty, containsExpected, keywords, toolCalled, toolCallsPresent, hasImageContent."
              >
                <TextArea
                  value={form.checks}
                  rows={5}
                  onChange={(event) => setForm({ ...form, checks: event.target.value })}
                />
              </Field>
            </div>

            <div className="sm:col-span-2 flex items-center gap-2">
              <Button
                type="submit"
                tone="primary"
                busy={save.isPending}
                disabled={editing == null && newName.trim().length === 0}
              >
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
              {checksError != null && <ErrorNote error={new Error(checksError)} />}
              {save.isError && <ErrorNote error={save.error} />}
            </div>
          </form>
        </Panel>
      )}

      <Panel title="Suites">
        {suites.isPending && <Loading />}
        {suites.isError && (
          <div className="p-4">
            <ErrorNote error={suites.error} />
          </div>
        )}

        {suites.isSuccess &&
          (suites.data.length === 0 ? (
            <Empty title="No eval suites yet">
              Add one to start measuring an agent against a set of queries with pass/fail checks.
            </Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Suite</Th>
                  <Th>Agent</Th>
                  <Th>Updated</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {suites.data.map((suite) => (
                  <tr key={suite.id} className="hover:bg-raised">
                    <Td>
                      <Link to={`evals/${encodeURIComponent(suite.name)}`}>{suite.name}</Link>
                      {suite.description != null && suite.description.length > 0 && (
                        <span className="block text-[12px] text-subtle">{suite.description}</span>
                      )}
                    </Td>
                    <Td>
                      <Badge tone="accent">{suite.agentName}</Badge>
                    </Td>
                    <Td className="text-muted">{relativeTime(suite.updatedAt)}</Td>
                    <Td className="text-right">
                      {meta.roles.canAdminister && (
                        <>
                          <Button
                            onClick={() => {
                              setForm(toForm(suite));
                              setEditing(suite.name);
                              setShowForm(true);
                            }}
                          >
                            Edit
                          </Button>
                          <Button tone="danger" onClick={() => remove.mutate(suite.name)}>
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
    </>
  );
}
