import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap, TraconError } from '../lib/api';
import { count } from '../lib/format';
import { translate, usePlural, useT } from '../lib/i18n';
import {
  Badge,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Stat,
  Table,
  Td,
  Th,
  Unauthorized,
} from '../components/ui';
import type {
  ConfigurationDiagnostic,
  ProviderDiagnostic,
  TraconMetaResponse as Meta,
} from '@tracon/client';
import type { TraconDiagnosticsReport } from '../lib/server-types';

/**
 * Self-check of this installation (Phase 33).
 *
 * The endpoint is off by default (`TraconEndpointOptions.EnableDiagnosticsEndpoint`)
 * and requires the Admin role when a role policy is registered — a 404 here almost
 * always means "not turned on", not "broken", so it gets its own explanation instead
 * of the generic error note.
 */
export function DiagnosticsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const plural = usePlural();
  const allowed = meta.roles.canAdminister;
  const diagnostics = useQuery({
    queryKey: ['diagnostics'],
    queryFn: () => unwrap(client.GET('/api/diagnostics')) as Promise<TraconDiagnosticsReport>,
    retry: false,
    // 🚨 Not requested at all without the role. A reader used to get the 403
    // and see it rendered as a generic failure, which reads as "the console is
    // broken" rather than "this screen is not yours".
    enabled: allowed,
  });

  if (!allowed) {
    return (
      <>
        <PageHeader title={t('nav.diagnostics')} description={t('diagnostics.description')} />
        <Panel>
          <Unauthorized requires="administrator" />
        </Panel>
      </>
    );
  }

  return (
    <>
      <PageHeader title={t('nav.diagnostics')} description={t('diagnostics.description')} />

      {diagnostics.isPending && <Loading rows={6} />}

      {diagnostics.isError &&
        (diagnostics.error instanceof TraconError && diagnostics.error.status === 404 ? (
          <Panel>
            {/* A 404 here means the endpoint is switched off, which is the
                default — so it is a state, not a failure, and it carries no
                retry: retrying cannot turn a compile-time option on. */}
            <Empty title={t('diagnostics.disabled.title')}>{t('diagnostics.disabled.body')}</Empty>
          </Panel>
        ) : (
          <div className="p-4">
            <ErrorNote error={diagnostics.error} onRetry={() => void diagnostics.refetch()} />
          </div>
        ))}

      {diagnostics.isSuccess && (
        <div className="grid gap-4 lg:grid-cols-2">
          <Panel title={t('diagnostics.persistence')}>
            <dl className="divide-y divide-line">
              <Row label={t('diagnostics.activeProvider')}>
                <Badge tone={diagnostics.data.persistenceProvider === 'InMemory' ? 'warn' : 'success'}>
                  <Mono>{diagnostics.data.persistenceProvider}</Mono>
                </Badge>
              </Row>
              <Row label={t('diagnostics.registeredProviders')}>
                <Badge tone={diagnostics.data.registeredPersistenceProviders > 1 ? 'warn' : 'neutral'}>
                  {count(diagnostics.data.registeredPersistenceProviders)}
                </Badge>
              </Row>
              <Row label={t('diagnostics.canConnect')}>
                {diagnostics.data.canConnect ? (
                  <Badge tone="success">{t('diagnostics.connected')}</Badge>
                ) : (
                  <Badge tone="danger">{t('diagnostics.notConnected')}</Badge>
                )}
              </Row>
              <Row label={t('diagnostics.migrations')}>
                {diagnostics.data.migrationsUpToDate ? (
                  <Badge tone="success">{t('diagnostics.migrationsUpToDate')}</Badge>
                ) : (
                  <Badge tone="warn">
                    {plural('diagnostics.migrationsPending', diagnostics.data.pendingMigrations.length)}
                  </Badge>
                )}
              </Row>
            </dl>

            {diagnostics.data.registeredPersistenceProviders > 1 && (
              <p className="border-t border-line px-4 py-2.5 text-xs text-warn">
                {t('diagnostics.multipleProvidersWarning')}
              </p>
            )}

            {diagnostics.data.pendingMigrations.length > 0 && (
              <div className="border-t border-line px-4 py-2.5 text-xs text-subtle">
                <div className="mb-1 font-medium text-muted">{t('diagnostics.pendingList')}</div>
                <ul className="list-inside list-disc">
                  {diagnostics.data.pendingMigrations.map((name) => (
                    <li key={name}>
                      <Mono>{name}</Mono>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </Panel>

          <Panel title={t('diagnostics.surface')}>
            <dl className="divide-y divide-line">
              <Row label={t('diagnostics.uiEmbedded')}>
                {diagnostics.data.uiEmbedded ? (
                  <Badge tone="success">{t('common.enabled')}</Badge>
                ) : (
                  <Badge tone="neutral">{t('common.disabled')}</Badge>
                )}
              </Row>
            </dl>
            <div className="grid grid-cols-2 gap-2 p-3">
              <Stat label={t('diagnostics.toolCount')} value={count(diagnostics.data.toolCount)} />
              <Stat label={t('diagnostics.agentCount')} value={count(diagnostics.data.agentCount)} />
            </div>
          </Panel>

          <Panel title={t('diagnostics.modelProviders')} className="lg:col-span-2">
            {diagnostics.data.modelProviders.length === 0 ? (
              <p className="px-4 py-4 text-sm text-subtle">{t('diagnostics.modelProviders.empty')}</p>
            ) : (
              <Table label={t('diagnostics.modelProviders')}>
                <thead>
                  <tr>
                    <Th>{t('common.name')}</Th>
                    <Th>{t('common.status')}</Th>
                    <Th>{t('diagnostics.circuitOpen')}</Th>
                  </tr>
                </thead>
                <tbody>
                  {diagnostics.data.modelProviders.map((provider) => (
                    <ProviderRow key={provider.name} provider={provider} t={t} />
                  ))}
                </tbody>
              </Table>
            )}
          </Panel>

          <Panel title={t('diagnostics.configuration')} className="lg:col-span-2">
            {diagnostics.data.configuration.length === 0 ? (
              <p className="px-4 py-4 text-sm text-subtle">{t('diagnostics.configuration.empty')}</p>
            ) : (
              <Table label={t('diagnostics.configuration')}>
                <thead>
                  <tr>
                    <Th>{t('diagnostics.configurationKey')}</Th>
                    <Th>{t('common.status')}</Th>
                    <Th>{t('diagnostics.hint')}</Th>
                  </tr>
                </thead>
                <tbody>
                  {diagnostics.data.configuration.map((entry) => (
                    <ConfigurationRow key={entry.key} entry={entry} t={t} />
                  ))}
                </tbody>
              </Table>
            )}
          </Panel>
        </div>
      )}
    </>
  );
}

function ProviderRow({
  provider,
  t,
}: {
  provider: ProviderDiagnostic;
  t: typeof translate;
}): ReactNode {
  return (
    <tr className="focus-within:bg-raised hover:bg-raised">
      <Td>
        <Mono>{provider.name}</Mono>
      </Td>
      <Td>
        <Badge tone={statusTone(provider.status)}>{provider.status}</Badge>
      </Td>
      <Td>
        {provider.circuitOpen ? (
          <Badge tone="danger">{t('common.enabled')}</Badge>
        ) : (
          <span className="text-subtle">—</span>
        )}
      </Td>
    </tr>
  );
}

function ConfigurationRow({
  entry,
  t,
}: {
  entry: ConfigurationDiagnostic;
  t: typeof translate;
}): ReactNode {
  return (
    <tr className="focus-within:bg-raised hover:bg-raised">
      <Td>
        <Mono>{entry.key}</Mono>
      </Td>
      <Td>
        {entry.resolved ? (
          <Badge tone="success">{t('diagnostics.resolved')}</Badge>
        ) : (
          <Badge tone="warn">{t('diagnostics.unresolved')}</Badge>
        )}
      </Td>
      {/* Server text, never translated (decision K-232): it names a setup step, not UI copy. */}
      <Td>{entry.hint ? <span className="text-xs text-subtle">{entry.hint}</span> : <span>—</span>}</Td>
    </tr>
  );
}

function statusTone(status: string): 'success' | 'danger' | 'warn' | 'neutral' {
  switch (status) {
    case 'Healthy':
      return 'success';
    case 'Unhealthy':
      return 'danger';
    case 'Degraded':
      return 'warn';
    default:
      return 'neutral';
  }
}

function Row({ label, children }: { label: string; children: ReactNode }): ReactNode {
  return (
    <div className="flex items-center gap-4 px-4 py-2">
      <dt className="w-48 shrink-0 text-sm text-subtle">{label}</dt>
      <dd className="min-w-0 flex-1 text-base">{children}</dd>
    </div>
  );
}
