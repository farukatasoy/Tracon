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
  Select,
  Table,
  Td,
  TextInput,
  Th,
} from '../components/ui';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { Tooltip } from '../components/tooltip';
import { ConfirmDialog } from '../components/confirm-dialog';
import { PlusIcon, TrashIcon } from '../components/icons';
import type { TraconMetaResponse as Meta, ExperimentStatus } from '@tracon/client';
import type { AgentDefinition, Experiment, ExperimentVariant } from '../lib/server-types';

export function StatusBadge({ status }: { status: ExperimentStatus }): ReactNode {
  const t = useT();

  switch (status) {
    case 'Running':
      return <Badge tone="success">{t('runs.status.running')}</Badge>;
    case 'Stopped':
      return <Badge tone="neutral">{t('experiments.status.stopped')}</Badge>;
    default:
      return <Badge tone="warn">{t('experiments.status.draft')}</Badge>;
  }
}

interface VariantForm extends ExperimentVariant {
  key: number;
}

let nextKey = 0;

function emptyVariant(): VariantForm {
  return { key: nextKey++, name: '', version: 1, weight: 0 };
}

function toForm(experiment: Experiment): { agentName: string; variants: VariantForm[] } {
  return {
    agentName: experiment.agentName,
    variants: experiment.variants.map((variant) => ({ ...variant, key: nextKey++ })),
  };
}

const EMPTY_FORM = { agentName: '', variants: [emptyVariant(), emptyVariant()] };

/**
 * A/B experiments: which agent's traffic is split, between which stored
 * definition versions, and at what weight. Starting, stopping and the live
 * result table live on the experiment's own detail screen — the same split
 * as Evals (list) versus a suite's own page.
 */
export function ExperimentsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<string | null>(null);
  const [newName, setNewName] = useState('');
  const [form, setForm] = useState<{ agentName: string; variants: VariantForm[] }>(EMPTY_FORM);
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState('');

  const experiments = useQuery({
    queryKey: ['experiments'],
    queryFn: () => unwrap(client.GET('/api/experiments')) as Promise<Experiment[]>,
  });

  const needle = query.trim().toLowerCase();
  const filtering = needle.length > 0 || status.length > 0;
  const filtered = (experiments.data ?? []).filter((experiment) => {
    if (status.length > 0 && experiment.status !== status) {
      return false;
    }

    return (
      needle.length === 0 ||
      experiment.name.toLowerCase().includes(needle) ||
      experiment.agentName.toLowerCase().includes(needle)
    );
  });

  const reset = (): void => {
    setQuery('');
    setStatus('');
  };

  const versions = useQuery({
    queryKey: ['agentVersions', form.agentName],
    queryFn: () =>
      unwrap(
        client.GET('/api/agents/{name}/versions', { params: { path: { name: form.agentName } } }),
      ) as Promise<AgentDefinition[]>,
    enabled: form.agentName.trim().length > 0,
  });

  const invalidate = (): void => void queryClient.invalidateQueries({ queryKey: ['experiments'] });

  const save = useMutation({
    mutationFn: () => {
      const name = editing ?? newName;

      return unwrap(
        client.PUT('/api/experiments/{name}', {
          params: { path: { name } },
          body: {
            agentName: form.agentName,
            variants: form.variants.map(({ key, ...variant }) => variant),
          },
        }),
      ) as Promise<Experiment>;
    },
    onSuccess: () => {
      setForm(EMPTY_FORM);
      setNewName('');
      setShowForm(false);
      setEditing(null);
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: (name: string) =>
      unwrap(client.DELETE('/api/experiments/{name}', { params: { path: { name } } })),
    onSuccess: () => {
      setConfirming(null);
      invalidate();
    },
  });

  /*
    🚨 §175.3 criterion (a). `runs.experiment_id` carries no foreign key, so
    the run rows survive — and that is exactly the problem: the experiment row
    is the only thing that says which variant name each recorded id meant.
    Creating the experiment again gives it a NEW id, so the old runs never
    rejoin it. The results are still in the database and no longer readable.
  */
  const [confirming, setConfirming] = useState<string | null>(null);

  const totalWeight = form.variants.reduce((sum, variant) => sum + (Number.isFinite(variant.weight) ? variant.weight : 0), 0);

  const submit = (event: FormEvent): void => {
    event.preventDefault();
    save.mutate();
  };

  const updateVariant = (key: number, patch: Partial<ExperimentVariant>): void =>
    setForm((current) => ({
      ...current,
      variants: current.variants.map((variant) => (variant.key === key ? { ...variant, ...patch } : variant)),
    }));

  return (
    <>
      <PageHeader
        title={t('nav.experiments')}
        description={t('experiments.description')}
        actions={
          meta.roles.canAdminister && (
            <Button
              tone="primary"
              testId="new-experiment"
              onClick={() => {
                setForm(EMPTY_FORM);
                setNewName('');
                setEditing(null);
                setShowForm((current) => !current);
              }}
            >
              <PlusIcon className="size-3.5" />
              {t('experiments.new')}
            </Button>
          )
        }
      />

      {showForm && (
        <Panel
          title={editing != null ? t('experiments.editTitle', { name: editing }) : t('experiments.new')}
          className="mb-4"
        >
          <form className="grid gap-3 p-4" onSubmit={submit}>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label={t('common.name')} required>
                <TextInput
                  value={editing ?? newName}
                  required
                  disabled={editing != null}
                  pattern="[a-zA-Z0-9_-]+"
                  placeholder="instructions-v2-rollout"
                  data-testid="experiment-name"
                  onChange={(event) => setNewName(event.target.value)}
                />
              </Field>

              <Field label={t('evals.agentName')} required hint={t('experiments.agentHint')}>
                <TextInput
                  value={form.agentName}
                  required
                  placeholder="customer-support-agent"
                  data-testid="experiment-agent-name"
                  onChange={(event) => setForm({ ...form, agentName: event.target.value })}
                />
              </Field>
            </div>

            <div>
              <p className="mb-2 text-sm font-medium text-muted">{t('experiments.variants')}</p>
              <div className="flex flex-col gap-2">
                {form.variants.map((variant, index) => (
                  <div key={variant.key} className="flex items-end gap-2">
                    <Field label={t('common.name')}>
                      <TextInput
                        value={variant.name}
                        placeholder="control"
                        data-testid={`variant-name-${index}`}
                        onChange={(event) => updateVariant(variant.key, { name: event.target.value })}
                      />
                    </Field>
                    <Field label={t('agentDetail.version')}>
                      {versions.isSuccess && versions.data.length > 0 ? (
                        <Select
                          value={String(variant.version)}
                          testId={`variant-version-${index}`}
                          onChange={(value) => updateVariant(variant.key, { version: Number(value) })}
                        >
                          {versions.data.map((definitionVersion) => (
                            <option key={definitionVersion.version} value={definitionVersion.version}>
                              v{definitionVersion.version}
                            </option>
                          ))}
                        </Select>
                      ) : (
                        <TextInput
                          type="number"
                          min={1}
                          value={variant.version}
                          data-testid={`variant-version-${index}`}
                          onChange={(event) => updateVariant(variant.key, { version: Number(event.target.value) })}
                        />
                      )}
                    </Field>
                    <Field label={t('experiments.weight')}>
                      <TextInput
                        type="number"
                        min={0}
                        max={100}
                        value={variant.weight}
                        data-testid={`variant-weight-${index}`}
                        onChange={(event) => updateVariant(variant.key, { weight: Number(event.target.value) })}
                      />
                    </Field>
                    <Button
                      type="button"
                      tone="ghost"
                      onClick={() =>
                        setForm((current) => ({
                          ...current,
                          variants: current.variants.filter((candidate) => candidate.key !== variant.key),
                        }))
                      }
                    >
                      <TrashIcon className="size-3.5" />
                    </Button>
                  </div>
                ))}
              </div>
              <div className="mt-2 flex items-center gap-3">
                <Button
                  type="button"
                  onClick={() => setForm((current) => ({ ...current, variants: [...current.variants, emptyVariant()] }))}
                >
                  {t('experiments.addVariant')}
                </Button>
                <span className={totalWeight === 100 ? 'text-sm text-muted' : 'text-sm text-danger'}>
                  {t('experiments.weightTotal', { total: totalWeight })}
                </span>
              </div>
            </div>

            <div className="flex items-center gap-2 border-t border-line pt-3">
              <Button
                type="submit"
                tone="primary"
                testId="experiment-save"
                busy={save.isPending}
                disabled={
                  (editing == null && newName.trim().length === 0) ||
                  form.agentName.trim().length === 0 ||
                  totalWeight !== 100
                }
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
        search={{ value: query, onChange: setQuery, label: t('experiments.search') }}
        onReset={filtering ? reset : undefined}
      >
        <ToolbarField label={t('common.status')}>
          {(id) => (
            <Select id={id} value={status} onChange={setStatus}>
              <option value="">{t('experiments.anyStatus')}</option>
              <option value="Draft">{t('experiments.status.draft')}</option>
              <option value="Running">{t('runs.status.running')}</option>
              <option value="Stopped">{t('experiments.status.stopped')}</option>
            </Select>
          )}
        </ToolbarField>
      </Toolbar>

      <ConfirmDialog
        open={confirming !== null}
        onClose={() => setConfirming(null)}
        onConfirm={() => {
          if (confirming !== null) {
            remove.mutate(confirming);
          }
        }}
        title={t('experiments.deleteTitle', { name: confirming ?? '' })}
        consequence={t('experiments.deleteEffect')}
        confirmLabel={t('common.delete')}
        busy={remove.isPending}
        error={remove.error}
        testId="confirm-delete-experiment"
      />

      {remove.isError && (
        <div className="mb-4">
          <ErrorNote
            error={remove.error}
            /* Reopens the confirmation rather than firing the delete — see sessions.tsx. */
            onRetry={
              remove.variables === undefined ? undefined : () => setConfirming(remove.variables)
            }
          />
        </div>
      )}

      <Panel title={t('nav.experiments')}>
        {experiments.isPending && <Loading rows={6} />}
        {experiments.isError && (
          <div className="p-4">
            <ErrorNote error={experiments.error} onRetry={() => void experiments.refetch()} />
          </div>
        )}

        {experiments.isSuccess &&
          (filtered.length === 0 ? (
            <Empty
              title={filtering ? t('common.noResults') : t('experiments.empty.title')}
              action={
                filtering ? (
                  <Button onClick={reset}>{t('toolbar.reset')}</Button>
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
                      {t('experiments.empty.action')}
                    </Button>
                  )
                )
              }
            >
              {filtering ? t('experiments.empty.filtered') : t('experiments.empty.body')}
            </Empty>
          ) : (
            <Table label={t('nav.experiments')}>
              <thead>
                <tr>
                  <Th>{t('experiments.experiment')}</Th>
                  <Th>{t('common.agent')}</Th>
                  <Th>{t('common.status')}</Th>
                  <Th className="text-right">{t('experiments.variants')}</Th>
                  <Th>{t('common.updated')}</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {filtered.map((experiment) => (
                  <tr key={experiment.id} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      <Link to={`experiments/${encodeURIComponent(experiment.name)}`}>
                        {experiment.name}
                      </Link>
                    </Td>
                    <Td>
                      <Link to={`agents/${encodeURIComponent(experiment.agentName)}`}>
                        <Badge tone="accent">{experiment.agentName}</Badge>
                      </Link>
                    </Td>
                    <Td>
                      <StatusBadge status={experiment.status} />
                    </Td>
                    <Td className="text-right font-mono text-id text-muted">
                      {experiment.variants.length}
                    </Td>
                    <Td className="text-muted" title={absoluteTime(experiment.updatedAt)}>
                      {relativeTime(experiment.updatedAt)}
                    </Td>
                    <Td className="text-right">
                      {meta.roles.canAdminister && experiment.status === 'Draft' && (
                        <div className="flex items-center justify-end gap-1.5">
                          <Button
                            onClick={() => {
                              setForm(toForm(experiment));
                              setEditing(experiment.name);
                              setShowForm(true);
                            }}
                          >
                            {t('common.edit')}
                          </Button>
                          <Tooltip text={t('experiments.deleteEffect')}>
                            <Button
                              tone="danger"
                              ariaLabel={t('experiments.deleteExperiment')}
                              busy={remove.isPending && remove.variables === experiment.name}
                              onClick={() => setConfirming(experiment.name)}
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

