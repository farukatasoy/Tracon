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
import { OriginBadge } from './agents';

export function AgentDetailScreen({ name }: { name: string }): ReactNode {
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
            {isEditable && (
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

      {isEditable && <VersionHistory name={name} currentVersion={descriptor.version} onError={setError} />}
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
  onError,
}: {
  name: string;
  currentVersion: number;
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

  return (
    <div className="mt-4">
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
                      {version.version !== currentVersion && (
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
              Rolling back does not delete anything: the chosen content is written as a new
              version on top of the history.
            </p>
          </>
        )}
      </Panel>
    </div>
  );
}
