import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link, useNavigate } from '../lib/router';
import { absoluteTime, relativeTime } from '../lib/format';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  JsonView,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  Th,
} from '../components/ui';
import { HistoryIcon, TrashIcon } from '../components/icons';
import { DiffView, FieldDiffTable, SetDiff } from '../components/diff-view';
import { OriginBadge } from './agents';
import type { Meta } from '../lib/types';

export function AgentDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [error, setError] = useState<unknown>(null);

  const agent = useQuery({ queryKey: ['agent', name], queryFn: () => api.agent(name) });

  const remove = useMutation({
    mutationFn: () => api.deleteAgent(name),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['agents'] });
      navigate('agents');
    },
    onError: setError,
  });

  if (agent.isPending) {
    return <Loading />;
  }

  if (agent.isError) {
    return <ErrorNote error={agent.error} />;
  }

  const { descriptor, definition, isEditable } = agent.data;

  return (
    <>
      <PageHeader
        title={descriptor.displayName ?? descriptor.name}
        description={descriptor.description ?? undefined}
        actions={
          <>
            <Link to={`playground/${encodeURIComponent(name)}`}>
              <Button>Open in playground</Button>
            </Link>
            {isEditable && meta.roles.canAdminister && (
              <>
                <Link to={`agents/${encodeURIComponent(name)}/edit`}>
                  <Button tone="primary">Edit</Button>
                </Link>
                <Button
                  tone="danger"
                  busy={remove.isPending}
                  onClick={() => {
                    if (window.confirm(`Delete "${name}" and its version history?`)) {
                      remove.mutate();
                    }
                  }}
                >
                  <TrashIcon className="size-3.5" />
                  Delete
                </Button>
              </>
            )}
          </>
        }
      />

      {error !== null && <div className="mb-4"><ErrorNote error={error} /></div>}

      {!isEditable && (
        <div className="mb-4 rounded-md border border-line bg-info-soft px-3 py-2 text-[12px] text-info">
          This agent is declared in code. Code definitions are validated at compile time and
          cannot be changed from the console — edit the application source instead.
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title="Summary">
          <dl className="divide-y divide-line text-[13px]">
            <Row label="Name"><Mono>{descriptor.name}</Mono></Row>
            <Row label="Source">
              <div className="flex items-center gap-1.5">
                <OriginBadge agent={descriptor} />
                <span className="text-muted">{descriptor.sourceName}</span>
              </div>
            </Row>
            <Row label="Provider">
              <Mono>{descriptor.model?.provider ?? '—'}</Mono>
            </Row>
            <Row label="Model">
              <Mono>{descriptor.model?.model ?? '—'}</Mono>
            </Row>
            <Row label="Harness">
              {descriptor.usesHarness ? <Badge tone="warn">enabled</Badge> : <span className="text-subtle">off</span>}
            </Row>
            <Row label="Tools">
              {descriptor.toolNames.length === 0 ? (
                <span className="text-subtle">none</span>
              ) : (
                <div className="flex flex-wrap gap-1">
                  {descriptor.toolNames.map((tool) => (
                    <Badge key={tool} tone="accent">{tool}</Badge>
                  ))}
                </div>
              )}
            </Row>
            <Row label="Updated">
              <span title={absoluteTime(descriptor.updatedAt)}>{relativeTime(descriptor.updatedAt)}</span>
            </Row>
          </dl>
        </Panel>

        <Panel title="Instructions">
          <div className="p-4">
            {definition?.instructions ? (
              <p className="text-[13px] leading-relaxed whitespace-pre-wrap">{definition.instructions}</p>
            ) : (
              <p className="text-[13px] text-subtle">
                No system instructions.
                {definition === null && ' The persisted definition is not available for code agents.'}
              </p>
            )}
          </div>
        </Panel>
      </div>

      {definition !== null && (
        <div className="mt-4">
          <Panel title="Definition">
            <div className="p-4">
              <JsonView value={definition} />
            </div>
          </Panel>
        </div>
      )}

      {isEditable && (
        <VersionHistory name={name} currentVersion={descriptor.version} meta={meta} onError={setError} />
      )}
    </>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }): ReactNode {
  return (
    <div className="flex items-baseline gap-4 px-4 py-2">
      <dt className="w-24 shrink-0 text-[12px] text-subtle">{label}</dt>
      <dd className="min-w-0 flex-1">{children}</dd>
    </div>
  );
}

function VersionHistory({
  name,
  currentVersion,
  meta,
  onError,
}: {
  name: string;
  currentVersion: number;
  meta: Meta;
  onError: (error: unknown) => void;
}): ReactNode {
  const queryClient = useQueryClient();

  const versions = useQuery({
    queryKey: ['agent-versions', name],
    queryFn: () => api.agentVersions(name),
  });

  const rollback = useMutation({
    mutationFn: (version: number) => api.rollbackAgent(name, version),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['agent', name] });
      await queryClient.invalidateQueries({ queryKey: ['agent-versions', name] });
      await queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
    onError,
  });

  const [selected, setSelected] = useState<number[]>([]);

  const toggleSelected = (version: number): void => {
    setSelected((current) => {
      if (current.includes(version)) {
        return current.filter((candidate) => candidate !== version);
      }

      // Selecting a third version replaces the older of the two picks, so
      // the comparison always tracks the two most recently chosen versions.
      return current.length >= 2 ? [current[1] ?? version, version] : [...current, version];
    });
  };

  const [compareA, compareB] =
    selected.length === 2 ? [Math.min(selected[0]!, selected[1]!), Math.max(selected[0]!, selected[1]!)] : [null, null];

  return (
    <div className="mt-4 space-y-4">
      <Panel
        title={
          <span className="flex items-center gap-1.5">
            <HistoryIcon className="size-3.5" />
            Version history
          </span>
        }
      >
        {versions.isPending && <Loading />}
        {versions.isError && <div className="p-4"><ErrorNote error={versions.error} /></div>}

        {versions.isSuccess && versions.data.length === 0 && (
          <Empty title="No stored versions" />
        )}

        {versions.isSuccess && versions.data.length > 0 && (
          <>
            <Table>
              <thead>
                <tr>
                  <Th />
                  <Th>Version</Th>
                  <Th>Model</Th>
                  <Th>Tools</Th>
                  <Th>Saved</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {versions.data.map((version) => (
                  <tr key={version.version} className="hover:bg-raised">
                    <Td>
                      <input
                        type="checkbox"
                        checked={selected.includes(version.version)}
                        onChange={() => toggleSelected(version.version)}
                        aria-label={`Select version ${version.version} to compare`}
                        data-testid={`version-checkbox-${version.version}`}
                      />
                    </Td>
                    <Td>
                      <Mono>v{version.version}</Mono>
                      {version.version === currentVersion && (
                        <Badge tone="success" title="Currently resolved definition">current</Badge>
                      )}
                    </Td>
                    <Td><Mono>{version.model.model}</Mono></Td>
                    <Td className="text-muted">{version.toolNames.length}</Td>
                    <Td className="text-muted" title={absoluteTime(version.updatedAt)}>
                      {relativeTime(version.updatedAt)}
                    </Td>
                    <Td className="text-right">
                      {version.version !== currentVersion && meta.roles.canAdminister && (
                        <Button
                          tone="ghost"
                          busy={rollback.isPending && rollback.variables === version.version}
                          onClick={() => rollback.mutate(version.version)}
                        >
                          Roll back
                        </Button>
                      )}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <p className="border-t border-line px-4 py-2 text-[11px] text-subtle">
              {selected.length === 1
                ? 'Select one more version to compare.'
                : selected.length === 2
                  ? `Comparing v${compareA} → v${compareB}.`
                  : 'Rolling back does not delete anything: the chosen content is written as a new version on top of the history. Check two versions to compare them.'}
            </p>
          </>
        )}
      </Panel>

      {compareA !== null && compareB !== null && <VersionCompare name={name} a={compareA} b={compareB} />}
    </div>
  );
}

function VersionCompare({ name, a, b }: { name: string; a: number; b: number }): ReactNode {
  const diff = useQuery({
    queryKey: ['agent-version-diff', name, a, b],
    queryFn: () => api.agentVersionDiff(name, a, b),
  });

  return (
    <Panel title={`Comparing v${a} → v${b}`}>
      <div className="p-4">
        {diff.isPending && <Loading />}
        {diff.isError && <ErrorNote error={diff.error} />}

        {diff.isSuccess && (
          <div className="space-y-5">
            <Section title="Instructions">
              <DiffView left={diff.data.left.instructions ?? ''} right={diff.data.right.instructions ?? ''} />
            </Section>

            <Section title="Model">
              <FieldDiffTable
                left={diff.data.left.model}
                right={diff.data.right.model}
                fields={{
                  provider: { label: 'Provider' },
                  model: { label: 'Model' },
                  temperature: { label: 'Temperature' },
                  maxOutputTokens: { label: 'Max output tokens' },
                  topP: { label: 'Top P' },
                  reasoningEffort: { label: 'Reasoning effort' },
                }}
              />
            </Section>

            <SetDiff label="Tools" left={diff.data.left.toolNames} right={diff.data.right.toolNames} />
            <SetDiff label="Skills" left={diff.data.left.skillNames} right={diff.data.right.skillNames} />
            <SetDiff
              label="Callable agents"
              left={diff.data.left.callableAgentNames}
              right={diff.data.right.callableAgentNames}
            />

            {(diff.data.left.harness ?? diff.data.right.harness) !== undefined && (
              <Section title="Harness">
                <FieldDiffTable
                  left={diff.data.left.harness}
                  right={diff.data.right.harness}
                  fields={{
                    maxContextWindowTokens: { label: 'Max context window tokens' },
                    maxOutputTokens: { label: 'Max output tokens' },
                    maximumIterationsPerRequest: { label: 'Max iterations per request' },
                    harnessInstructions: { label: 'Harness instructions' },
                    disableCompaction: { label: 'Disable compaction' },
                    disableTodoProvider: { label: 'Disable todo provider' },
                    disableFileMemory: { label: 'Disable file memory' },
                    disableWebSearch: { label: 'Disable web search' },
                    disableToolAutoApproval: { label: 'Disable tool auto-approval' },
                  }}
                />
              </Section>
            )}

            {(diff.data.left.compaction ?? diff.data.right.compaction) !== undefined && (
              <Section title="Compaction">
                <FieldDiffTable
                  left={diff.data.left.compaction}
                  right={diff.data.right.compaction}
                  fields={{
                    strategy: { label: 'Strategy' },
                    triggerTokens: { label: 'Trigger tokens' },
                    triggerMessages: { label: 'Trigger messages' },
                    triggerTurns: { label: 'Trigger turns' },
                    minimumPreservedTurns: { label: 'Minimum preserved turns' },
                    minimumPreservedGroups: { label: 'Minimum preserved groups' },
                    summarizationPrompt: { label: 'Summarization prompt' },
                  }}
                />
              </Section>
            )}

            {(diff.data.left.memory ?? diff.data.right.memory) !== undefined && (
              <Section title="Memory">
                <FieldDiffTable
                  left={diff.data.left.memory}
                  right={diff.data.right.memory}
                  fields={{
                    enableFileMemory: { label: 'Enable file memory' },
                    enableTodo: { label: 'Enable todo' },
                    enableTextSearch: { label: 'Enable text search' },
                  }}
                />
              </Section>
            )}
          </div>
        )}
      </div>
    </Panel>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }): ReactNode {
  return (
    <section>
      <h3 className="mb-1.5 text-[11px] font-semibold tracking-wide text-subtle uppercase">{title}</h3>
      {children}
    </section>
  );
}
