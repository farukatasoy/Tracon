import { useState, type FormEvent, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime } from '../lib/format';
import { useT } from '../lib/i18n';
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
import { Toolbar } from '../components/toolbar';
import { Tooltip } from '../components/tooltip';
import { PlusIcon, TrashIcon } from '../components/icons';
import type { TraconMetaResponse as Meta } from '@tracon/client';
import type { EvalSuite } from '../lib/server-types';

const EMPTY_FORM = {
  agentName: '',
  description: '',
  checks: '[\n  { "kind": "nonEmpty", "minLength": 10 }\n]',
};

type SuiteForm = typeof EMPTY_FORM;

/** A run's pass/fail split as a two-colour bar — the same shape as a job's item progress. */
export function PassRateBar({ passed, failed, total }: { passed: number; failed: number; total: number }): ReactNode {
  const t = useT();

  if (total === 0) {
    return <span className="text-xs text-subtle">{t('evals.noCases')}</span>;
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
      <span className="text-xs text-muted">
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
  const t = useT();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<SuiteForm>(EMPTY_FORM);
  const [showForm, setShowForm] = useState(false);
  const [checksError, setChecksError] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [query, setQuery] = useState('');

  const suites = useQuery({
    queryKey: ['evalSuites'],
    queryFn: () => unwrap(client.GET('/api/evals')) as Promise<EvalSuite[]>,
  });

  const invalidate = (): void => void queryClient.invalidateQueries({ queryKey: ['evalSuites'] });

  const save = useMutation({
    mutationFn: () => {
      const name = editing ?? newName;
      const checks = JSON.parse(form.checks) as unknown;

      return unwrap(
        client.PUT('/api/evals/{name}', {
          params: { path: { name } },
          body: {
            agentName: form.agentName,
            description: form.description.length > 0 ? form.description : null,
            checks,
          },
        }),
      ) as Promise<EvalSuite>;
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
    mutationFn: (name: string) =>
      unwrap(client.DELETE('/api/evals/{name}', { params: { path: { name } } })),
    onSuccess: invalidate,
  });

  const [newName, setNewName] = useState('');

  const needle = query.trim().toLowerCase();
  const filtering = needle.length > 0;
  const filtered = (suites.data ?? []).filter(
    (suite) =>
      !filtering ||
      suite.name.toLowerCase().includes(needle) ||
      suite.agentName.toLowerCase().includes(needle) ||
      (suite.description?.toLowerCase().includes(needle) ?? false),
  );

  const submit = (event: FormEvent): void => {
    event.preventDefault();
    setChecksError(null);

    try {
      JSON.parse(form.checks);
    } catch {
      setChecksError(t('evals.checksError'));
      return;
    }

    save.mutate();
  };

  return (
    <>
      <PageHeader
        title={t('nav.evals')}
        description={t('evals.description')}
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
              {t('evals.newSuite')}
            </Button>
          )
        }
      />

      {showForm && (
        <Panel
          title={editing != null ? t('evals.editSuite', { name: editing }) : t('evals.newSuite')}
          className="mb-4"
        >
          <form className="grid gap-3 p-4 sm:grid-cols-2" onSubmit={submit}>
            <Field label={t('common.name')} required>
              <TextInput
                value={editing ?? newName}
                required
                disabled={editing != null}
                pattern="[a-zA-Z0-9_-]+"
                placeholder="customer-support-suite"
                onChange={(event) => setNewName(event.target.value)}
              />
            </Field>

            <Field label={t('evals.agentName')} required hint={t('evals.agentNameHint')}>
              <TextInput
                value={form.agentName}
                required
                placeholder="customer-support-agent"
                onChange={(event) => setForm({ ...form, agentName: event.target.value })}
              />
            </Field>

            <div className="sm:col-span-2">
              <Field label={t('common.description')}>
                <TextInput
                  value={form.description}
                  onChange={(event) => setForm({ ...form, description: event.target.value })}
                />
              </Field>
            </div>

            <div className="sm:col-span-2">
              {/* The render-prop form: a validation message has to be bound to
                  the control it judges (`aria-describedby` + `aria-invalid`),
                  not printed somewhere beside the submit button. */}
              <Field
                label={t('evals.checks')}
                hint={t('evals.checksHint')}
                error={checksError ?? undefined}
              >
                {(ids) => (
                  <TextArea
                    {...ids}
                    value={form.checks}
                    rows={5}
                    onChange={(event) => setForm({ ...form, checks: event.target.value })}
                  />
                )}
              </Field>
            </div>

            <div className="sm:col-span-2 flex items-center gap-2">
              <Button
                type="submit"
                tone="primary"
                busy={save.isPending}
                disabled={editing == null && newName.trim().length === 0}
              >
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

      <Toolbar
        search={{ value: query, onChange: setQuery, label: t('evals.search') }}
        onReset={query.trim().length > 0 ? () => setQuery('') : undefined}
      />

      {/* A delete that failed is invisible unless it is said out loud: the row
          is still in the list and the operator has no way to know why. */}
      {remove.isError && (
        <div className="mb-4">
          <ErrorNote
            error={remove.error}
            onRetry={
              remove.variables === undefined ? undefined : () => remove.mutate(remove.variables)
            }
          />
        </div>
      )}

      <Panel title={t('evals.suites')}>
        {suites.isPending && <Loading rows={6} />}
        {suites.isError && (
          <div className="p-4">
            <ErrorNote error={suites.error} onRetry={() => void suites.refetch()} />
          </div>
        )}

        {suites.isSuccess &&
          (filtered.length === 0 ? (
            <Empty
              title={filtering ? t('common.noResults') : t('evals.noSuites.title')}
              action={
                filtering ? (
                  <Button onClick={() => setQuery('')}>{t('toolbar.reset')}</Button>
                ) : (
                  meta.roles.canAdminister && (
                    <Button
                      tone="primary"
                      onClick={() => {
                        setForm(EMPTY_FORM);
                        setNewName('');
                        setEditing(null);
                        setShowForm(true);
                      }}
                    >
                      {t('evals.empty.action')}
                    </Button>
                  )
                )
              }
            >
              {filtering ? t('evals.empty.filtered') : t('evals.noSuites.body')}
            </Empty>
          ) : (
            <Table label={t('evals.suites')}>
              <thead>
                <tr>
                  <Th>{t('evals.suite')}</Th>
                  <Th>{t('common.agent')}</Th>
                  <Th>{t('common.updated')}</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {filtered.map((suite) => (
                  <tr key={suite.id} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      <Link to={`evals/${encodeURIComponent(suite.name)}`}>{suite.name}</Link>
                      {suite.description != null && suite.description.length > 0 && (
                        <span className="block text-sm text-subtle">{suite.description}</span>
                      )}
                    </Td>
                    <Td>
                      <Link to={`agents/${encodeURIComponent(suite.agentName)}`}>
                        <Badge tone="accent">{suite.agentName}</Badge>
                      </Link>
                    </Td>
                    <Td className="text-muted" title={absoluteTime(suite.updatedAt)}>
                      {relativeTime(suite.updatedAt)}
                    </Td>
                    <Td className="text-right">
                      {meta.roles.canAdminister && (
                        <div className="flex items-center justify-end gap-1.5">
                          <Button
                            onClick={() => {
                              setForm(toForm(suite));
                              setEditing(suite.name);
                              setShowForm(true);
                            }}
                          >
                            {t('common.edit')}
                          </Button>
                          <Tooltip text={t('evals.deleteSuite')}>
                            <Button
                              tone="danger"
                              ariaLabel={t('evals.deleteSuite')}
                              busy={remove.isPending && remove.variables === suite.name}
                              onClick={() => {
                                if (window.confirm(t('common.confirmDelete', { name: suite.name }))) {
                                  remove.mutate(suite.name);
                                }
                              }}
                            >
                              <TrashIcon className="size-3.5" />
                            </Button>
                          </Tooltip>
                        </div>
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
