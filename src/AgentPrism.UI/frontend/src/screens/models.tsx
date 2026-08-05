import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { count, latencyText, relativeTime } from '../lib/format';
import { useT, type MessageKey } from '../lib/i18n';
import { Link } from '../lib/router';
import type { ModelProviderHealth, ModelProviderHealthStatus } from '../lib/types';
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
      title={
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
 * An empty catalogue is not an error. AgentPrism ships no built-in model list
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
  const client = useQueryClient();

  const providers = useQuery({ queryKey: ['models'], queryFn: api.models });
  const health = useQuery({
    queryKey: ['models-health'],
    queryFn: () => api.modelsHealth(),
    // While a provider looks unhealthy (possibly a circuit breaker counting
    // down its break duration), poll gently so the card catches up without a
    // dedicated countdown timer.
    refetchInterval: (query) =>
      query.state.data?.some((entry) => entry.status === 'Unhealthy') === true ? 5_000 : false,
  });

  const checkNow = useMutation({
    mutationFn: (name: string) => api.modelHealth(name, true),
    onSuccess: (result) => {
      client.setQueryData<ModelProviderHealth[]>(['models-health'], (current) =>
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

      {providers.isPending && <Loading />}
      {providers.isError && <ErrorNote error={providers.error} />}

      {providers.isSuccess && providers.data.length === 0 && (
        <Panel>
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
                <Button
                  onClick={() => checkNow.mutate(provider.name)}
                  busy={checkNow.isPending && checkNow.variables === provider.name}
                  title={t('models.checkNowTitle')}
                >
                  {t('models.checkNow')}
                </Button>
              }
            >
              {providerHealth?.detail != null && providerHealth.detail.length > 0 && (
                <div className="border-b border-line px-4 py-2 text-[11px] text-muted">
                  {providerHealth.detail}
                </div>
              )}

              {provider.models.length === 0 ? (
                <Empty title={t('models.noModels.title')}>
                  {t('models.noModels.body')} <Mono>AgentPrism:Providers:OpenAI:Models</Mono>.{' '}
                  {t('models.noModels.note')}
                </Empty>
              ) : (
                <Table>
                  <thead>
                    <tr>
                      <Th>{t('common.model')}</Th>
                      <Th>{t('models.context')}</Th>
                      <Th>{t('models.maxOutput')}</Th>
                      <Th>{t('models.capabilities')}</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {provider.models.map((model) => (
                      <tr key={model.name} className="hover:bg-raised">
                        <Td>
                          <Mono className="font-medium">{model.name}</Mono>
                          {model.displayName != null && (
                            <span className="ml-2 text-muted">{model.displayName}</span>
                          )}
                        </Td>
                        <Td className="text-muted">{count(model.contextWindowTokens)}</Td>
                        <Td className="text-muted">{count(model.maxOutputTokens)}</Td>
                        <Td>
                          <div className="flex flex-wrap gap-1">
                            {model.supportsStreaming && <Badge tone="info">streaming</Badge>}
                            {model.supportsTools && <Badge tone="accent">tools</Badge>}
                            {model.supportsReasoning && <Badge tone="warn">reasoning</Badge>}
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

      <p className="mt-4 text-[11px] text-subtle">
        {t('models.footerBefore')}{' '}
        <Link to="dashboard" className="text-accent hover:underline">
          {t('nav.dashboard')}
        </Link>
        . {t('models.footerAfter')} <Mono>AgentPrism:Pricing</Mono>.
      </p>
    </>
  );
}
