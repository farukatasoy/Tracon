import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { count, latencyText, relativeTime } from '../lib/format';
import { useT, type MessageKey } from '../lib/i18n';
import { Link } from '../lib/router';
import type { ModelProviderHealthStatus } from '@tracon/client';
import type { ModelProviderDescriptor, ModelProviderHealth } from '../lib/server-types';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  Th,
} from '../components/ui';
import { Tooltip } from '../components/tooltip';

const STATUS_TONE: Record<ModelProviderHealthStatus, 'neutral' | 'success' | 'warn' | 'danger'> = {
  Unknown: 'neutral',
  Healthy: 'success',
  Degraded: 'warn',
  Unhealthy: 'danger',
};

const STATUS_LABEL: Record<ModelProviderHealthStatus, MessageKey> = {
  Unknown: 'models.status.unknown',
  Healthy: 'models.status.healthy',
  Degraded: 'models.status.degraded',
  Unhealthy: 'models.status.unhealthy',
};

function HealthBadge({ health }: { health: ModelProviderHealth | undefined }): ReactNode {
  const t = useT();
  const status = health?.status ?? 'Unknown';

  return (
    <Badge
      tone={STATUS_TONE[status]}
      description={
        status === 'Unknown'
          ? t('models.noHealthContract')
          : t('models.checkedAt', {
              when: relativeTime(health?.checkedAt),
              latency: health?.latency ? latencyText(health.latency) : '—',
            })
      }
    >
      {t(STATUS_LABEL[status])}
    </Badge>
  );
}

/**
 * Registered providers, their model catalogue, and connectivity status.
 *
 * An empty catalogue is not an error. Tracon ships no built-in model list
 * (decision K-032): a package cannot keep provider model names current, and a
 * stale list produced a 403 model_not_found the day it was written.
 *
 * The catalogue is also not a validation list — a model name that is missing
 * here still works.
 *
 * Health checks never make a paid model call: they hit `GET {endpoint}/models`.
 * A provider that does not implement the check reports "unknown", not an error.
 */
export function ModelsScreen(): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();

  const providers = useQuery({
    queryKey: ['models'],
    queryFn: () => unwrap(client.GET('/api/models')) as Promise<ModelProviderDescriptor[]>,
  });
  const health = useQuery({
    queryKey: ['models-health'],
    queryFn: () => unwrap(client.GET('/api/models/health')) as Promise<ModelProviderHealth[]>,
    // While a provider looks unhealthy (possibly a circuit breaker counting
    // down its break duration), poll gently so the card catches up without a
    // dedicated countdown timer.
    refetchInterval: (query) =>
      query.state.data?.some((entry) => entry.status === 'Unhealthy') === true ? 5_000 : false,
  });

  const checkNow = useMutation({
    mutationFn: (name: string) =>
      unwrap(
        client.GET('/api/models/health/{provider}', {
          params: { path: { provider: name }, query: { refresh: true } },
        }),
      ) as Promise<ModelProviderHealth>,
    onSuccess: (result) => {
      queryClient.setQueryData<ModelProviderHealth[]>(['models-health'], (current) =>
        current === undefined
          ? [result]
          : current.map((entry) => (entry.providerName === result.providerName ? result : entry)),
      );
    },
  });

  const healthByProvider = new Map((health.data ?? []).map((entry) => [entry.providerName, entry]));

  return (
    <>
      <PageHeader
        title={t('nav.models')}
        description={t('models.description')}
      />

      {providers.isPending && <Loading rows={6} />}
      {providers.isError && (
        <ErrorNote error={providers.error} onRetry={() => void providers.refetch()} />
      )}

      {providers.isSuccess && providers.data.length === 0 && (
        <Panel>
          {/* No action: a provider is registered in code, so the only honest
              next step is the call that registers one — printed below. */}
          <Empty title={t('models.empty.title')}>
            {t('models.empty.body')} <Mono>UseOpenAI(apiKey)</Mono> /{' '}
            <Mono>UseOpenAICompatible(name, ...)</Mono>. {t('models.empty.note')}
          </Empty>
        </Panel>
      )}

      <div className="flex flex-col gap-3">
        {(providers.data ?? []).map((provider) => {
          const providerHealth = healthByProvider.get(provider.name);

          return (
            <Panel
              key={provider.name}
              title={
                <span className="flex items-center gap-2">
                  {provider.displayName ?? provider.name}
                  <Mono className="text-subtle">{provider.name}</Mono>
                  <HealthBadge health={providerHealth} />
                </span>
              }
              actions={
                <Tooltip text={t('models.checkNowTitle')}>
                  <Button
                    onClick={() => checkNow.mutate(provider.name)}
                    busy={checkNow.isPending && checkNow.variables === provider.name}
                  >
                    {t('models.checkNow')}
                  </Button>
                </Tooltip>
              }
            >
              {checkNow.isError && checkNow.variables === provider.name && (
                <div className="border-b border-line p-3">
                  <ErrorNote
                    error={checkNow.error}
                    onRetry={() => checkNow.mutate(provider.name)}
                  />
                </div>
              )}

              {providerHealth?.detail != null && providerHealth.detail.length > 0 && (
                <div className="border-b border-line px-4 py-2 text-xs text-muted">
                  {providerHealth.detail}
                </div>
              )}

              {provider.models.length === 0 ? (
                /* No action: the catalogue comes from configuration, and an
                   empty one is not an error — a model missing here still works
                   (K-032). */
                <Empty title={t('models.noModels.title')}>
                  {t('models.noModels.body')} <Mono>Tracon:Providers:OpenAI:Models</Mono>.{' '}
                  {t('models.noModels.note')}
                </Empty>
              ) : (
                <Table label={provider.displayName ?? provider.name}>
                  <thead>
                    <tr>
                      <Th>{t('common.model')}</Th>
                      <Th className="text-right">{t('models.context')}</Th>
                      <Th className="text-right">{t('models.maxOutput')}</Th>
                      <Th>{t('models.capabilities')}</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {provider.models.map((model) => (
                      <tr key={model.name} className="focus-within:bg-raised hover:bg-raised">
                        <Td>
                          <Mono className="font-medium">{model.name}</Mono>
                          {model.displayName != null && (
                            <span className="ml-2 text-muted">{model.displayName}</span>
                          )}
                        </Td>
                        <Td className="text-right font-mono text-id text-muted">
                          {count(model.contextWindowTokens as number | null | undefined)}
                        </Td>
                        <Td className="text-right font-mono text-id text-muted">
                          {count(model.maxOutputTokens as number | null | undefined)}
                        </Td>
                        <Td>
                          <div className="flex flex-wrap gap-1">
                            {model.supportsStreaming && <Badge tone="info">streaming</Badge>}
                            {model.supportsTools && <Badge tone="accent">tools</Badge>}
                            {model.supportsReasoning && <Badge tone="warn">reasoning</Badge>}
                            {model.supportsStructuredOutput && <Badge tone="success">structured output</Badge>}
                          </div>
                        </Td>
                      </tr>
                    ))}
                  </tbody>
                </Table>
              )}
            </Panel>
          );
        })}
      </div>

      <p className="mt-4 text-xs text-subtle">
        {t('models.footerBefore')}{' '}
        <Link to="dashboard">{t('nav.dashboard')}</Link>
        . {t('models.footerAfter')} <Mono>Tracon:Pricing</Mono>.
      </p>
    </>
  );
}
